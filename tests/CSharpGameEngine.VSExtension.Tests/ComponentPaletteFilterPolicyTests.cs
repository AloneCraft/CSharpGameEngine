using System.Linq;
using System.Reflection;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// M0052 Step 2-2: パレットの絞り込み。generic型は1定義あたり最大50件のinstantiationへ展開されるので、
/// 検索が効かないと一覧が実用にならない。
/// </summary>
public sealed class ComponentPaletteFilterPolicyTests {
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Matches_EmptyQuery_KeepsEverything(string?Query)=>
        Assert.True(ComponentPaletteFilterPolicy.Matches("Transform","Sample",Query));

    [Fact]
    public void Matches_IgnoresCase(){
        Assert.True(ComponentPaletteFilterPolicy.Matches("Transform","Sample","TRANS"));
        Assert.True(ComponentPaletteFilterPolicy.Matches("Transform","Sample","trans"));
    }

    [Fact]
    public void Matches_HitsOnTheNamespaceToo(){
        // 同名型を名前空間で選り分けられる必要がある。
        Assert.True(ComponentPaletteFilterPolicy.Matches("Transform","Game.Runtime","runtime"));
    }

    [Fact]
    public void Matches_MultipleTermsAreAnded(){
        Assert.True(ComponentPaletteFilterPolicy.Matches("Transform","Game.Runtime","trans runtime"));
        Assert.False(ComponentPaletteFilterPolicy.Matches("Transform","Game.Runtime","trans physics"));
    }

    [Fact]
    public void Matches_SearchesInsideClosedGenericNames(){
        Assert.True(ComponentPaletteFilterPolicy.Matches("StatusEffect<PoisonKind>","Sample","effect<poison"));
        Assert.True(ComponentPaletteFilterPolicy.Matches("StatusEffect<PoisonKind>","Sample","poisonkind>"));
    }

    [Fact]
    public void Matches_NonHit_IsRejected()=>
        Assert.False(ComponentPaletteFilterPolicy.Matches("Transform","Sample","camera"));

    [Fact]
    public void Filter_EmptyQuery_ReturnsTheSameEntriesInOrder(){
        var Entries=SampleEntries();

        Assert.Equal(
            Entries.Select(static Entry=>Entry.FullyQualifiedName),
            ComponentPaletteFilterPolicy.Filter(Entries,"").Select(static Entry=>Entry.FullyQualifiedName));
    }

    [Fact]
    public void Filter_NarrowsToMatchingEntries(){
        var Filtered=ComponentPaletteFilterPolicy.Filter(SampleEntries(),"light");

        Assert.Equal(["DirectionalLight"],Filtered.Select(static Entry=>Entry.DisplayName));
    }

    /// <summary>
    /// 式はカタログ構築時(シンボルが生きている場所)で組み立て済みのものを持ち回るだけ。
    /// 中身の妥当性は<see cref="SceneComponentInitializerPolicyTests"/>が固定する。
    /// </summary>
    [Fact]
    public void ComponentExpression_IsCarriedThroughVerbatimFromTheCatalog(){
        var Entry=SampleEntries().First();

        Assert.Equal("new global::Sample.Transform { PositionX=default }",Entry.ComponentExpression);
    }

    /// <summary>
    /// <c>ComponentPaletteToolWindowControl.xaml</c>がバインドする3プロパティ。internalのままだと
    /// WPFのバインディングは例外も警告も出さず黙って失敗し、アイコンだけが既定値で出て文字が
    /// 全部空になる(実機で報告された不具合そのもの)。<see cref="InspectorMemberViewModelTests"/>と
    /// 同じ理由で、アクセシビリティ自体をここで固定する。
    /// </summary>
    [Theory]
    [InlineData(nameof(ComponentPaletteEntry.DisplayName))]
    [InlineData(nameof(ComponentPaletteEntry.Namespace))]
    [InlineData(nameof(ComponentPaletteEntry.Icon))]
    public void PropertiesBoundFromXamlArePublicSoWpfCanResolveThem(string PropertyName){
        Assert.NotNull(typeof(ComponentPaletteEntry)
            .GetProperty(PropertyName,BindingFlags.Public|BindingFlags.Instance));
    }

    private static ComponentPaletteEntry[]SampleEntries()=>[
        new("global::Sample.Transform","Transform","Sample",
            "new global::Sample.Transform { PositionX=default }",SceneVisualIcon.ComponentDefault()),
        new("global::Sample.DirectionalLight","DirectionalLight","Sample",
            "new global::Sample.DirectionalLight()",SceneVisualIcon.ComponentDefault()),
    ];
}
