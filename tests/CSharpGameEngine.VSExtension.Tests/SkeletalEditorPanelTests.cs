using System.Reflection;
using System.Windows.Controls;
using CSharpGameEngine.Contracts;
using Microsoft.VisualStudio.Shell;
namespace CSharpGameEngine.VSExtension.Tests;
[Collection(MockedVsCollection.Name)]
public sealed class SkeletalEditorPanelTests {
    public SkeletalEditorPanelTests(MockedVsFixture Fixture){ }
    [Fact]
    public void ChangingPreviewMeshClearsOldSummaryUntilTheNewMeshLoads(){
        ThreadHelper.JoinableTaskFactory.Run(async()=>{
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var FilePath=Path.Combine(Path.GetTempPath(),"animation.glb");
            using var Viewport=new AssetViewportHost(FilePath,PreviewLevelBuiltIns.Default());
            using var Panel=new SkeletalEditorPanel(Viewport,FilePath,new GltfSkeletalInfo(1,1,["clip"]));
            var Loaded=typeof(SkeletalEditorPanel).GetMethod("OnSummaryLoaded",BindingFlags.Instance|BindingFlags.NonPublic)!;
            var Summary=new RenderPreviewAssetSummary {
                JointNames=["root"],JointParents=[-1],
                Clips=[new RenderPreviewClipSummary { Name="walk",DurationSeconds=2f,BoundTrackCount=1 }],
            };
            Loaded.Invoke(Panel,[Summary]);
            Assert.True(Panel.TimelineStateForTesting.Playing);
            var Tree=(TreeView)typeof(SkeletalEditorPanel).GetField("JointTree",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(Panel)!;
            Assert.Single(Tree.Items);
            typeof(SkeletalEditorPanel).GetMethod("UseMesh",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(Panel,[Path.Combine(Path.GetTempPath(),"new.glb")]);
            Assert.False(Panel.TimelineStateForTesting.Playing);
            Assert.False(Panel.TimelineStateForTesting.HasClip);
            Assert.Empty(Tree.Items);
            Assert.Equal(-1,Viewport.SelectedJoint);
            Loaded.Invoke(Panel,[Summary]);
            Assert.True(Panel.TimelineStateForTesting.Playing);
            Assert.Single(Tree.Items);
            Panel.Retarget(FilePath);
            Assert.False(Panel.TimelineStateForTesting.Playing);
            Assert.Empty(Tree.Items);
        });
    }
}
