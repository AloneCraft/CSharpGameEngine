using System.Collections.Generic;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// 選択ハイライトの強さ。M0020 Phase 2まではハイライトが primary 1件のみで、Ctrl+LMBで複数選択して
/// も見た目が変わらず「複数選択ができない」と観測されていた(実機検証#5)。GPU側の定数バッファ更新は
/// テスト不能なので、強さの判断だけを純粋関数として固定する。
/// </summary>
public sealed class SceneSelectionTintTests {
    private static readonly NodePath Root=NodePath.Root;
    private static readonly NodePath First=NodePath.Root.Append(0);
    private static readonly NodePath Second=NodePath.Root.Append(1);
    private static readonly NodePath Third=NodePath.Root.Append(2);

    [Fact]
    public void PrimaryIsBrighterThanSecondary_SoTheUserCanTellWhichIsTheMainTarget(){
        List<NodePath>Selected=[First,Second];

        Assert.Equal(SceneSelectionTint.PrimaryWeight,SceneSelectionTint.GetWeight(First,First,Selected));
        Assert.Equal(SceneSelectionTint.SecondaryWeight,SceneSelectionTint.GetWeight(Second,First,Selected));
        Assert.True(SceneSelectionTint.SecondaryWeight<SceneSelectionTint.PrimaryWeight);
        Assert.True(SceneSelectionTint.SecondaryWeight>SceneSelectionTint.UnselectedWeight);
    }

    [Fact]
    public void NodesOutsideTheSelectionAreNotTinted(){
        List<NodePath>Selected=[First,Second];

        Assert.Equal(SceneSelectionTint.UnselectedWeight,SceneSelectionTint.GetWeight(Third,First,Selected));
        Assert.Equal(SceneSelectionTint.UnselectedWeight,SceneSelectionTint.GetWeight(Root,First,Selected));
    }

    [Fact]
    public void NoSelectionAtAllTintsNothing(){
        Assert.Equal(SceneSelectionTint.UnselectedWeight,SceneSelectionTint.GetWeight(First,null,null));
        Assert.Equal(SceneSelectionTint.UnselectedWeight,SceneSelectionTint.GetWeight(First,null,[]));
    }

    /// <summary>
    /// primaryだけが渡され集合が無い経路(単一選択)でも従来どおり光ること。
    /// SetSelectedNodeの第2引数は省略可能なので、この形の呼び出しが残っていても壊れない。
    /// </summary>
    [Fact]
    public void PrimaryAloneStillTintsWithoutASelectionSet(){
        Assert.Equal(SceneSelectionTint.PrimaryWeight,SceneSelectionTint.GetWeight(First,First,null));
        Assert.Equal(SceneSelectionTint.UnselectedWeight,SceneSelectionTint.GetWeight(Second,First,null));
    }

    /// <summary>
    /// 集合にprimaryが含まれていても、primaryの判定が先に立って弱い方に落ちないこと
    /// (SceneHwndHostはNodeIdsにprimaryを含めて渡す)。
    /// </summary>
    [Fact]
    public void PrimaryContainedInTheSetIsStillFullBrightness(){
        List<NodePath>Selected=[First,Second,Third];

        Assert.Equal(SceneSelectionTint.PrimaryWeight,SceneSelectionTint.GetWeight(Second,Second,Selected));
    }
}
