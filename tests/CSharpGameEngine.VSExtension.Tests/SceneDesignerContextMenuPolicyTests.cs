using System.Linq;
using CSharpGameEngine.VSExtension;
using Xunit;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class SceneDesignerContextMenuPolicyTests {
    private static readonly SceneDesignerMenuItem Box=new("PrimitiveShowcaseBoxScene","Project.PrimitiveShowcaseBoxScene");
    private static readonly SceneDesignerMenuItem Sphere=new("PrimitiveShowcaseSphereScene","Project.PrimitiveShowcaseSphereScene");

    /// <summary>
    /// ✓は「デザイナーで開いているシーン」だけに付く。キャレット位置を基準にしていた頃は、別ファイルの
    /// シーンを表示している状態でも右クリックしたシーンに✓が付き「これが開いている」と誤読させていた。
    /// </summary>
    [Fact]
    public void OpenScene_IsTheOnlyLatchedItem(){
        Assert.True(SceneDesignerMenuSelectionPolicy.IsLatched("Project.PrimitiveShowcaseBoxScene",Box));
        Assert.False(SceneDesignerMenuSelectionPolicy.IsLatched("Project.PrimitiveShowcaseBoxScene",Sphere));

        // 別ファイルのシーンを開いている(=この一覧のどれでもない)場合はどれにも付かない。
        Assert.False(SceneDesignerMenuSelectionPolicy.IsLatched("Other.FirstPersonStageScene",Box));
        Assert.False(SceneDesignerMenuSelectionPolicy.IsLatched("Other.FirstPersonStageScene",Sphere));

        // 何も開いていない場合も付かない。
        Assert.False(SceneDesignerMenuSelectionPolicy.IsLatched(null,Box));
    }

    /// <summary>
    /// 今回の不具合そのもの: 開いているシーンとキャレット位置のシーンが別なら、✓と「（カーソル位置）」は
    /// 別々の項目に付かなければならない。
    /// </summary>
    [Fact]
    public void OpenSceneAndCaretScene_AreTrackedIndependently(){
        const string OpenScene="Project.PrimitiveShowcaseSphereScene";
        const string CaretScene="Project.PrimitiveShowcaseBoxScene";

        Assert.False(SceneDesignerMenuSelectionPolicy.IsLatched(OpenScene,Box));
        Assert.True(SceneDesignerMenuSelectionPolicy.IsAtCaret(CaretScene,Box));

        Assert.True(SceneDesignerMenuSelectionPolicy.IsLatched(OpenScene,Sphere));
        Assert.False(SceneDesignerMenuSelectionPolicy.IsAtCaret(CaretScene,Sphere));
    }

    [Fact]
    public void CaretOutsideAnyScene_MarksNothing(){
        Assert.False(SceneDesignerMenuSelectionPolicy.IsAtCaret(null,Box));
        Assert.False(SceneDesignerMenuSelectionPolicy.IsAtCaret(null,Sphere));
    }

    [Fact]
    public void NonSceneType_IsDisabledWithAnInlineReason(){
        SceneDesignerCommandPresentation Presentation=SceneDesignerContextMenuPolicy.Create(
            @"G:\Project\Components.cs",
            SceneDesignerTargetState.NonSceneType,
            []);

        Assert.True(Presentation.Visible);
        Assert.False(Presentation.Enabled);
        Assert.Equal(SceneDesignerMenuMode.Disabled,Presentation.Mode);
        Assert.Contains("IScene",Presentation.Text);
    }

    [Fact]
    public void PositionOutsideAType_IsDisabledWithAnInlineReason(){
        SceneDesignerCommandPresentation Presentation=SceneDesignerContextMenuPolicy.Create(
            @"G:\Project\Scenes.cs",
            SceneDesignerTargetState.OutsideType,
            []);

        Assert.True(Presentation.Visible);
        Assert.False(Presentation.Enabled);
        Assert.Contains("ありません",Presentation.Text);
    }

    [Fact]
    public void PositionOutsideAType_InASceneFileUsesTheSceneSelector(){
        SceneDesignerCommandPresentation Presentation=SceneDesignerContextMenuPolicy.Create(
            @"G:\Project\PrimitiveShowcaseScene.cs",
            SceneDesignerTargetState.OutsideType,
            [
                new SceneDesignerMenuItem("BoxScene","Project.BoxScene"),
                new SceneDesignerMenuItem("SphereScene","Project.SphereScene"),
            ]);

        Assert.True(Presentation.Visible);
        Assert.True(Presentation.Enabled);
        Assert.Equal(SceneDesignerMenuMode.SceneList,Presentation.Mode);
    }

    [Fact]
    public void NonCSharpDocument_IsHidden(){
        SceneDesignerCommandPresentation Presentation=SceneDesignerContextMenuPolicy.Create(
            @"G:\Project\Readme.md",
            SceneDesignerTargetState.SceneType,
            [new SceneDesignerMenuItem("MainScene","Project.MainScene")]);

        Assert.False(Presentation.Visible);
        Assert.Equal(SceneDesignerMenuMode.Hidden,Presentation.Mode);
    }

    [Fact]
    public void OneScene_UsesTheDirectCommand(){
        SceneDesignerCommandPresentation Presentation=SceneDesignerContextMenuPolicy.Create(
            @"G:\Project\MainScene.cs",
            SceneDesignerTargetState.SceneType,
            [new SceneDesignerMenuItem("MainScene","Project.MainScene")]);

        Assert.True(Presentation.Visible);
        Assert.True(Presentation.Enabled);
        Assert.Equal(SceneDesignerMenuMode.Direct,Presentation.Mode);
        Assert.Equal("シーン デザイナーで開く",Presentation.Text);
    }

    [Fact]
    public void MultipleScenes_UseAFlatSceneListInDeclarationOrder(){
        SceneDesignerCommandPresentation Presentation=SceneDesignerContextMenuPolicy.Create(
            @"G:\Project\PrimitiveShowcaseScene.cs",
            SceneDesignerTargetState.SceneType,
            [
                new SceneDesignerMenuItem("PrimitiveShowcaseBoxScene","Project.PrimitiveShowcaseBoxScene"),
                new SceneDesignerMenuItem("PrimitiveShowcaseSphereScene","Project.PrimitiveShowcaseSphereScene"),
            ]);

        Assert.True(Presentation.Visible);
        Assert.True(Presentation.Enabled);
        Assert.Equal(SceneDesignerMenuMode.SceneList,Presentation.Mode);
        Assert.Equal(
            ["PrimitiveShowcaseBoxScene","PrimitiveShowcaseSphereScene"],
            Presentation.Items.Select(Item=>Item.Text).ToArray());
    }

    /// <summary>
    /// 一覧はサブメニューではなくコンテキストメニュー直下に並ぶため、項目単体で操作内容が読めないと
    /// 素のクラス名がコードウィンドウのメニューに突然現れることになる。
    /// </summary>
    [Fact]
    public void SceneListItemText_SaysWhatItDoesNotJustTheClassName(){
        string Text=SceneDesignerContextMenuPolicy.ItemText(
            new SceneDesignerMenuItem("BoxScene","Project.BoxScene"),
            IsAtCaret:false);

        Assert.Equal("BoxScene をシーン デザイナーで開く",Text);
    }

    /// <summary>
    /// シーン一覧は .vsct に固定 ID で並べたスロットで表現するため、件数分だけを表示して残りは
    /// 隠す。スロット index と実件数の対応がずれると「1番目を押したのに2番目が開く」類の誘導ミスに
    /// 直結するので、可視判定そのものをここで固定する。
    /// </summary>
    [Fact]
    public void ItemSlots_AreVisibleOnlyUpToTheSceneCount(){
        Assert.True(SceneDesignerContextMenuPolicy.IsItemSlotVisible(SceneDesignerMenuMode.SceneList,0,ItemCount:2));
        Assert.True(SceneDesignerContextMenuPolicy.IsItemSlotVisible(SceneDesignerMenuMode.SceneList,1,ItemCount:2));
        Assert.False(SceneDesignerContextMenuPolicy.IsItemSlotVisible(SceneDesignerMenuMode.SceneList,2,ItemCount:2));
        Assert.False(SceneDesignerContextMenuPolicy.IsItemSlotVisible(SceneDesignerMenuMode.SceneList,-1,ItemCount:2));
    }

    /// <summary>
    /// シーン1件は直接コマンドが出る経路なので、スロットは1つも出さない。
    /// (SceneDesignerMenuMode は internal なので public な Theory の引数にできず、Fact でまとめる。)
    /// </summary>
    [Fact]
    public void ItemSlots_AreHiddenOutsideTheSceneListMode(){
        foreach(SceneDesignerMenuMode Mode in new[]{
            SceneDesignerMenuMode.Direct,
            SceneDesignerMenuMode.Disabled,
            SceneDesignerMenuMode.Hidden,
        }){
            Assert.False(SceneDesignerContextMenuPolicy.IsItemSlotVisible(Mode,0,ItemCount:1));
            Assert.False(SceneDesignerContextMenuPolicy.IsItemSlotVisible(Mode,0,ItemCount:5));
        }
    }

    /// <summary>
    /// スロット数は有限なので、超えた分は「存在するのに表示されない」状態になる。黙って消さずに
    /// 件数を伝えることが要件(上限自体はサブメニューでは回避できない —
    /// リーフ1項目につき CommandID が1つ必要なため)。
    /// </summary>
    [Fact]
    public void HiddenItemCount_ReportsOnlyWhatDoesNotFit(){
        Assert.Equal(0,SceneDesignerContextMenuPolicy.HiddenItemCount(ItemCount:2,SlotCount:32));
        Assert.Equal(0,SceneDesignerContextMenuPolicy.HiddenItemCount(ItemCount:32,SlotCount:32));
        Assert.Equal(1,SceneDesignerContextMenuPolicy.HiddenItemCount(ItemCount:33,SlotCount:32));
        Assert.Equal(8,SceneDesignerContextMenuPolicy.HiddenItemCount(ItemCount:40,SlotCount:32));
    }

    [Fact]
    public void OverflowText_PointsAtTheCompleteListingPath(){
        string Text=SceneDesignerContextMenuPolicy.OverflowText(HiddenCount:5);

        Assert.Contains("5 件",Text);
        Assert.Contains("コンボボックス",Text);
    }

    /// <summary>
    /// ✓は「開いているシーン」専用なので、キャレット位置はテキストで示す(VSのメニュー項目は
    /// 太字にできない — SceneDesignerMenuSelectionPolicy 参照)。
    /// </summary>
    [Fact]
    public void SceneListItemText_MarksTheCaretPosition(){
        string Text=SceneDesignerContextMenuPolicy.ItemText(
            new SceneDesignerMenuItem("BoxScene","Project.BoxScene"),
            IsAtCaret:true);

        Assert.Equal("BoxScene をシーン デザイナーで開く（カーソル位置）",Text);
    }

    [Theory]
    [InlineData("Analyzing","解析中")]
    [InlineData("Failed","失敗")]
    public void IncompleteAnalysis_IsDisabledWithTheCurrentReason(
        string StateName,string ExpectedReason){
        SceneDesignerTargetState State=(SceneDesignerTargetState)System.Enum.Parse(typeof(SceneDesignerTargetState),StateName);
        SceneDesignerCommandPresentation Presentation=SceneDesignerContextMenuPolicy.Create(
            @"G:\Project\MainScene.cs",
            State,
            []);

        Assert.True(Presentation.Visible);
        Assert.False(Presentation.Enabled);
        Assert.Contains(ExpectedReason,Presentation.Text);
    }
}