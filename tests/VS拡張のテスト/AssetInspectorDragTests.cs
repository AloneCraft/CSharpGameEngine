using System.Windows;
using System.Windows.Controls;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.VisualStudio.Shell;
namespace CSharpGameEngine.VSExtension.Tests;
/// <summary>
/// Assets で素材を選ぶと Inspector は素材の表示になる。そこから物の欄へ素材を落とすには、物の Inspector が見えている必要がある。
/// </summary>
[Collection(MockedVsCollection.Name)]
public sealed class AssetInspectorDragTests {
    public AssetInspectorDragTests(MockedVsFixture Fixture){ }
    [Fact]
    public void AssetClickShowsAssetInspectorUntilAnActualDragRuns()=>RunOnUiThread(()=>{
        WithPane(Pane=>{
            SelectAsset();AssertAsset(Pane,true);
            var Selected=AssetWorkspaceSelection.Current;
            AssetWorkspaceDrag.Run(()=>{
                AssertAsset(Pane,false);
                AssetWorkspaceSelection.Select(Selected);AssertAsset(Pane,false);
            });
            Assert.Same(Selected,AssetWorkspaceSelection.Current);AssertAsset(Pane,true);
        });
    });
    [Fact]
    public void NestedDragKeepsTargetsUntilTheOuterDragEnds()=>RunOnUiThread(()=>{
        WithPane(Pane=>{
            SelectAsset();
            AssetWorkspaceDrag.Run(()=>{
                AssetWorkspaceDrag.Run(()=>AssertAsset(Pane,false));
                AssertAsset(Pane,false);
            });
            AssertAsset(Pane,true);
        });
    });
    [Fact]
    public void FailedDragRestoresCurrentSelectionAndPropagatesItsException()=>RunOnUiThread(()=>{
        WithPane(Pane=>{
            SelectAsset();var Failure=new InvalidOperationException("owned drag failure");
            Assert.Same(Failure,Assert.Throws<InvalidOperationException>(()=>AssetWorkspaceDrag.Run(()=>{
                AssertAsset(Pane,false);throw Failure;
            })));
            AssertAsset(Pane,true);
            AssetWorkspaceSelection.Clear();AssertAsset(Pane,false);
        });
    });
    [Fact]
    public void DragEndUsesLatestSelectionRatherThanStaleVisibility()=>RunOnUiThread(()=>{
        WithPane(Pane=>{
            SelectAsset();
            AssetWorkspaceDrag.Run(()=>{
                AssertAsset(Pane,false);AssetWorkspaceSelection.Clear();AssertAsset(Pane,false);
            });
            AssertAsset(Pane,false);SelectAsset();AssertAsset(Pane,true);
        });
    });
    [Fact]
    public void RestoredAndDisposedPanesDoNotOwnTheDragLifetime()=>RunOnUiThread(()=>{
        WithPane(First=>{
            SelectAsset();
            AssetWorkspaceDrag.Run(()=>{
                WithPane(Second=>{
                    AssertAsset(First,false);AssertAsset(Second,false);
                    ((IDisposable)First).Dispose();
                    AssetWorkspaceSelection.Select(AssetWorkspaceSelection.Current);AssertAsset(Second,false);
                });
            });
            WithPane(After=>AssertAsset(After,true));
        });
    });
    [Fact]
    public void ExplicitHierarchyReselectionRevealsNodeWithoutRepeatingSemanticSelection()=>RunOnUiThread(()=>{
        WithPane(Pane=>{
            var Previous=SceneEditorHub.HierarchySelection;
            var Service=new SceneSelectionService();var Key=new SceneKey("Game","Probe");var Node=new EditorNodeId(1);var Notifications=0;
            Service.SelectionChanged+=_=>Notifications++;
            void Publish(HierarchyNodeSelection Selection)=>Service.Select(Key,[Node],Node,null,null,SceneSelectionOrigin.Hierarchy,Guid.NewGuid());
            SceneEditorHub.HierarchySelectionChanged+=Publish;
            try {
                var Selection=new HierarchyNodeSelection([NodePath.Root],NodePath.Root,NodePath.Root);
                SceneEditorHub.PublishHierarchySelection(Selection);SelectAsset();AssertAsset(Pane,true);
                SceneEditorHub.PublishHierarchySelection(Selection);
                Assert.Empty(AssetWorkspaceSelection.Current);AssertAsset(Pane,false);
                Assert.Equal(1,Notifications);Assert.Equal(1,Service.CurrentSelection!.Revision);
            }finally{ SceneEditorHub.HierarchySelectionChanged-=Publish;SceneEditorHub.PublishHierarchySelection(Previous); }
        });
    });
    [Fact]
    public void EmptyHierarchySelectionDoesNotDismissAnAssetInspector()=>RunOnUiThread(()=>{
        WithPane(Pane=>{
            var Previous=SceneEditorHub.HierarchySelection;
            try {
                SelectAsset();SceneEditorHub.PublishHierarchySelection(HierarchyNodeSelection.Empty);AssertAsset(Pane,true);
            }finally{ SceneEditorHub.PublishHierarchySelection(Previous); }
        });
    });
    private static void SelectAsset()=>AssetWorkspaceSelection.Select([new AssetWorkspaceItem{Kind=AssetWorkspaceKind.Model,Source=new(){ProjectPath="Game",SourcePath="Assets/Probe.glb",RelativePath="Assets/Probe.glb"}}]);
    private static void AssertAsset(InspectorToolWindowPane Pane,bool Showing){
        var Root=Assert.IsType<Grid>(Pane.Content);
        Assert.Equal(Showing?Visibility.Collapsed:Visibility.Visible,Assert.IsType<InspectorToolWindowControl>(Root.Children[0]).Visibility);
        Assert.Equal(Showing?Visibility.Visible:Visibility.Collapsed,Assert.IsType<AssetInspectorControl>(Root.Children[1]).Visibility);
    }
    private static void WithPane(Action<InspectorToolWindowPane> Action){
        var Previous=AssetWorkspaceSelection.Current;
        var Pane=new InspectorToolWindowPane();
        try{ Action(Pane); }finally{ ((IDisposable)Pane).Dispose();AssetWorkspaceSelection.Select(Previous); }
    }
    private static void RunOnUiThread(Action Action)=>ThreadHelper.JoinableTaskFactory.Run(async()=>{
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();Action();
    });
}
