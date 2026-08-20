using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// Hierarchy ViewModel construction (ADR-0040). Runs entirely without Visual Studio: the model comes
/// from the same shared analyser the Source Generator uses, and the ViewModel is a plain object.
///
/// <para>
/// The behaviour that matters here is expansion/selection survival. A live scene is re-analysed on
/// every keystroke, so the tree is rebuilt constantly; if state did not carry across rebuilds the
/// Hierarchy would collapse itself while the user typed. This is exactly the kind of logic that live
/// UI verification is bad at catching, so it is pinned here instead.
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
        // Defaulting to collapsed would hide the whole scene behind one disclosure triangle on
        // every rebuild, and rebuilds happen while the user types.
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
        // The user may delete the selected node from the source; that must not throw.
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
        // A half-typed scene must still show something; blanking the tree on every transient error
        // would make the Hierarchy flicker constantly while editing.
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
        // Same order the generated Instantiate creates entities in, so a future index-based mapping
        // between tree rows and entities lines up.
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
    /// Every property HierarchyToolWindowControl.xaml binds to must be public: WPF's binding engine
    /// resolves public properties only, and a binding to an internal one fails <b>silently</b> - no
    /// exception, no build warning, just an empty row. That is exactly what made the Hierarchy render
    /// as a blank panel while DisplayName/ComponentSummary/Children were internal.
    ///
    /// <para>
    /// Nothing else in this file can catch that regression. This test project has InternalsVisibleTo,
    /// so every other test here reads those same properties happily and passes whether they are public
    /// or internal - the accessibility itself has to be asserted explicitly.
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
