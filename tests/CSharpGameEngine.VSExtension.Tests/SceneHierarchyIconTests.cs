using System.Collections.Generic;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;
using Microsoft.VisualStudio.Imaging;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>Presentation-only icon contracts for Hierarchy rows.</summary>
public sealed class SceneHierarchyIconTests {
    private const string Source="""
        using CSharpGameEngine.Entities;
        using Sample;
        namespace Game;
        public partial class MainScene:IScene {
            public Node Root=>new() {
                Name="Root",
                Components=[new Transform(),new DirectionalLight { Intensity=1f }],
                Children=[new Node { Name="Child" }],
            };
        }
        """;

    [Fact]
    public void HierarchyUsesSceneAndNodeMonikersAndRetainsEveryComponentIcon(){
        SceneModel Model=SceneAnalysisTestHelper.Analyze(Source);
        IReadOnlyList<SceneNodeViewModel>Roots=SceneNodeViewModel.Build(Model,new HashSet<NodePath>(),null);

        SceneNodeViewModel Root=Assert.Single(Roots);
        SceneNodeViewModel Child=Assert.Single(Root.Children);
        Assert.Equal(KnownMonikers.ThreeDScene,Root.NodeMoniker);
        Assert.Equal(KnownMonikers.AbstractCube,Child.NodeMoniker);
        Assert.Equal(2,Root.ComponentIcons.Count);
        Assert.All(Root.ComponentIcons,static Icon=>Assert.Equal(KnownMonikers.Component,Icon.Moniker));
        Assert.Empty(Child.ComponentIcons);
    }

    [Fact]
    public void IconPropertiesBoundByHierarchyXamlArePublic(){
        Assert.NotNull(typeof(SceneNodeViewModel).GetProperty(nameof(SceneNodeViewModel.NodeMoniker)));
        Assert.NotNull(typeof(SceneNodeViewModel).GetProperty(nameof(SceneNodeViewModel.ComponentIcons)));
        Assert.NotNull(typeof(SceneVisualIcon).GetProperty(nameof(SceneVisualIcon.Moniker)));
        Assert.NotNull(typeof(SceneVisualIcon).GetProperty(nameof(SceneVisualIcon.IsProjectAsset)));
    }
}