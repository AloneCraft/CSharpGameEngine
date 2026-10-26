using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// Hierarchy ViewModel の構築 (ADR-0040)。Visual Studio に依存せず実行する。モデルは Source Generator と同じ共有アナライザーから得られ、ViewModel は通常のオブジェクトである。
///
/// <para>
/// ここで重要なのは展開状態と選択状態の維持である。ライブシーンはキー入力のたびに再解析されるため、ツリーは頻繁に再構築される。状態が再構築をまたいで維持されなければ、ユーザーの入力中に Hierarchy が折りたたまれてしまう。この種のロジックはライブ UI 検証では検出しにくいため、ここで固定して検証する。
/// </para>
/// </summary>
public sealed class SceneNodeViewModelTests {
    private const string NestedScene="""
        using CSharpGameEngine.Entities;
        using Sample;
        namespace Game;
        public partial class MainScene:IScene {
            public Node Root=>new() {
                Name="Root",
                Components=[new Transform()],
                Children=[
                    new Node { Name="A",Children=[new Node { Name="A0" }] },
                    new Node { Name="B",Components=[new DirectionalLight { Intensity=3f }] },
                ],
            };
        }
        """;

    private static IReadOnlyList<SceneNodeViewModel>Build(string Source,ISet<NodePath>?Collapsed=null,NodePath?Selected=null){
        var Model=SceneAnalysisTestHelper.Analyze(Source);
        return SceneNodeViewModel.Build(Model,Collapsed??new HashSet<NodePath>(),Selected);
    }

    [Fact]
    public void BuildsOneRootMatchingTheDeclaration(){
        var Roots=Build(NestedScene);

        var Root=Assert.Single(Roots);
        Assert.Equal("Root",Root.DisplayName);
        Assert.Equal(2,Root.Children.Count);
        Assert.Equal("A",Root.Children[0].DisplayName);
        Assert.Equal("A0",Root.Children[0].Children[0].DisplayName);
    }

    [Fact]
    public void ComponentSummaryShowsSimpleTypeNames(){
        var Roots=Build(NestedScene);

        Assert.Equal("Transform",Roots[0].ComponentSummary);
        Assert.Equal("DirectionalLight",Roots[0].Children[1].ComponentSummary);
        Assert.Equal(string.Empty,Roots[0].Children[0].ComponentSummary);
    }

    [Fact]
    public void UnnamedNodeFallsBackToItsPathSoItIsStillAddressable(){
        var Roots=Build("""
                       using CSharpGameEngine.Entities;
                       namespace Game;
                       public partial class MainScene:IScene {
                           public Node Root=>new() { Children=[new Node()] };
                       }
                       """);

        Assert.Equal("(root)",Roots[0].DisplayName);
        Assert.Equal("0",Roots[0].Children[0].DisplayName);
    }

    [Fact]
    public void NodesAreExpandedByDefault(){
        // 既定で折りたたむと、
        // ユーザーの入力中に再構築されるたび、シーン全体が 1 つの開閉三角形の背後に隠れてしまう。
        var Roots=Build(NestedScene);

        Assert.All(Roots[0].SelfAndDescendants(),static Node=>Assert.True(Node.IsExpanded));
    }

    [Fact]
    public void CollapsedPathsSurviveARebuild(){
        var CollapsedChild=NodePath.Root.Append(0);
        HashSet<NodePath>Collapsed=[CollapsedChild];

        var Roots=Build(NestedScene,Collapsed);

        Assert.True(Roots[0].IsExpanded);
        Assert.False(Roots[0].Children[0].IsExpanded);
        Assert.True(Roots[0].Children[1].IsExpanded);
    }

    [Fact]
    public void SelectionSurvivesARebuild(){
        var Selected=NodePath.Root.Append(0).Append(0);

        var Roots=Build(NestedScene,Collapsed:null,Selected);

        Assert.True(Roots[0].Children[0].Children[0].IsSelected);
        Assert.False(Roots[0].IsSelected);
        Assert.False(Roots[0].Children[1].IsSelected);
    }

    [Fact]
    public void SelectionOfARemovedNodeSimplySelectsNothing(){
        // ユーザーはソースから選択中のノードを削除できる。その場合も例外を投げてはならない。
        var Vanished=NodePath.Root.Append(9).Append(9);

        var Roots=Build(NestedScene,Collapsed:null,Vanished);

        Assert.All(Roots[0].SelfAndDescendants(),static Node=>Assert.False(Node.IsSelected));
    }

    [Fact]
    public void DocumentWithoutASceneProducesAnEmptyTree(){
        Assert.Empty(SceneNodeViewModel.Build(null,new HashSet<NodePath>(),null));
    }

    [Fact]
    public void SceneWithDiagnosticsStillRendersWhatCouldBeParsed(){
        // 入力途中のシーンでも何かを表示する。入力中の一時的なエラーのたびにツリーを空にすると、
        // 編集中に Hierarchy が絶えずちらつく。
        var Roots=Build("""
                       using CSharpGameEngine.Entities;
                       using Sample;
                       namespace Game;
                       public partial class MainScene:IScene {
                           public Node Root=>new() {
                               Name="Root",
                               Components=[new Transform(),new NotYetTyped()],
                           };
                       }
                       """);

        Assert.Equal("Root",Assert.Single(Roots).DisplayName);
    }

    [Fact]
    public void SelfAndDescendantsWalksDepthFirstInDeclarationOrder(){
        // 生成された Instantiate がエンティティを作成する順序と同じにし、インデックスによるマッピングで
        // ツリー行とエンティティが対応するようにする。
        var Roots=Build(NestedScene);

        Assert.Equal(
            ["Root","A","A0","B"],
            Roots[0].SelfAndDescendants().Select(static Node=>Node.DisplayName));
    }

    [Fact]
    public void ExpansionIsObservableSoTheTreeViewCanBindTwoWay(){
        var Root=Build(NestedScene)[0];
        List<string?>Changed=[];
        Root.PropertyChanged+=(_,Args)=>Changed.Add(Args.PropertyName);

        Root.IsExpanded=false;
        Root.IsSelected=true;
        Root.IsSelected=true;

        Assert.Equal(["IsExpanded","IsSelected"],Changed);
    }

    /// <summary>
    /// HierarchyToolWindowControl.xaml がバインドするプロパティはすべて public でなければならない。WPF のバインディングエンジンが解決するのは public プロパティだけであり、internal へのバインドは<b>黙って</b>失敗する。例外もビルド警告もなく、行が空になるだけである。以前 DisplayName/ComponentSummary/Children が internal だったため、Hierarchy が空のパネルとして表示された。
    ///
    /// <para>
    /// このファイルの他の処理では、その回帰を検出できない。このテストプロジェクトには InternalsVisibleTo があり、他のすべてのテストは同じプロパティを問題なく読み取るため、public でも internal でも成功する。アクセス修飾子そのものを明示的に検証する必要がある。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(nameof(SceneNodeViewModel.DisplayName))]
    [InlineData(nameof(SceneNodeViewModel.ComponentSummary))]
    [InlineData(nameof(SceneNodeViewModel.Children))]
    [InlineData(nameof(SceneNodeViewModel.IsExpanded))]
    [InlineData(nameof(SceneNodeViewModel.IsSelected))]
    [InlineData(nameof(SceneNodeViewModel.IsMultiSelected))]
    public void PropertiesBoundFromXamlArePublicSoWpfCanResolveThem(string PropertyName){
        Assert.NotNull(typeof(SceneNodeViewModel)
            .GetProperty(PropertyName,BindingFlags.Public|BindingFlags.Instance));
    }
}
