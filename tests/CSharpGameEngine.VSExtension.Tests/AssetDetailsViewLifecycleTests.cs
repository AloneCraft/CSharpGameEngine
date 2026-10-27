using System.Reflection;
using System.Windows.Controls;
using CSharpGameEngine.Contracts;
using Microsoft.VisualStudio.Shell;
namespace CSharpGameEngine.VSExtension.Tests;
[Collection(MockedVsCollection.Name)]
public sealed class AssetDetailsViewLifecycleTests {
    public AssetDetailsViewLifecycleTests(MockedVsFixture Fixture){ }
    [Fact]
    public void DetailsReconnectWhenTheWorkspaceControllerAppearsOrChanges(){
        ThreadHelper.JoinableTaskFactory.Run(async()=>{
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var Prior=AssetsToolWindowPane.Controller;
            AssetsToolWindowPane.Controller=null;
            try {
                using var Details=new AssetDetailsView(Path.Combine(Path.GetTempPath(),"asset.cgeasset"));
                var Message=(TextBlock)typeof(AssetDetailsView).GetField("Message",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(Details)!;
                Assert.Equal(VsixText.Get("AssetEditor_DetailsUnavailable"),Message.Text);
                var Constructor=typeof(AssetWorkspaceController).GetConstructors(BindingFlags.NonPublic|BindingFlags.Instance).Single();
                var First=(AssetWorkspaceController)Constructor.Invoke([null,null,null]);
                var Second=(AssetWorkspaceController)Constructor.Invoke([null,null,null]);
                var Field=typeof(AssetDetailsView).GetField("Controller",BindingFlags.NonPublic|BindingFlags.Instance)!;
                AssetsToolWindowPane.Controller=First;
                Assert.Same(First,Field.GetValue(Details));
                Assert.Equal(VsixText.Get("AssetEditor_NotInInventory"),Message.Text);
                AssetsToolWindowPane.Controller=Second;
                Assert.Same(Second,Field.GetValue(Details));
                AssetsToolWindowPane.Controller=null;
                Assert.Null(Field.GetValue(Details));
                Assert.Equal(VsixText.Get("AssetEditor_DetailsUnavailable"),Message.Text);
            } finally {
                AssetsToolWindowPane.Controller=Prior;
            }
        });
    }
    [Fact]
    public void RetargetingToAnUnavailableAssetClearsTheOldInspectorSelection(){
        ThreadHelper.JoinableTaskFactory.Run(async()=>{
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var Prior=AssetsToolWindowPane.Controller;
            AssetsToolWindowPane.Controller=null;
            try {
                using var Details=new AssetDetailsView(Path.Combine(Path.GetTempPath(),"old.cgeasset"));
                var Inspector=(AssetInspectorControl)typeof(AssetDetailsView).GetField("Inspector",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(Details)!;
                var Old=new AssetWorkspaceItem { State=AssetWorkspaceState.MissingSource,Source=new AssetWorkspaceSource { SourcePath=Path.Combine(Path.GetTempPath(),"old.cgeasset"),RelativePath="old.cgeasset" } };
                Inspector.Show([Old]);
                var Current=typeof(AssetInspectorControl).GetField("CurrentItems",BindingFlags.NonPublic|BindingFlags.Instance)!;
                var Generation=typeof(AssetInspectorControl).GetField("Generation",BindingFlags.NonPublic|BindingFlags.Instance)!;
                Assert.Single((IReadOnlyList<AssetWorkspaceItem>)Current.GetValue(Inspector)!);
                var Before=(int)Generation.GetValue(Inspector)!;
                Details.Retarget(Path.Combine(Path.GetTempPath(),"missing.cgeasset"));
                Assert.Empty((IReadOnlyList<AssetWorkspaceItem>)Current.GetValue(Inspector)!);
                Assert.True((int)Generation.GetValue(Inspector)!>Before);
            } finally {
                AssetsToolWindowPane.Controller=Prior;
            }
        });
    }
    [Fact]
    public void AnOldAudioFailureDoesNotAppearAfterSelectionChanges(){
        ThreadHelper.JoinableTaskFactory.Run(async()=>{
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            using var Inspector=new AssetInspectorControl(FollowSelection:false);
            var Audio=new AssetWorkspaceItem { Kind=AssetWorkspaceKind.Audio,State=AssetWorkspaceState.Source,Source=new AssetWorkspaceSource { SourcePath=Path.Combine(Path.GetTempPath(),"old-sound.wav"),RelativePath="old-sound.wav" } };
            Inspector.Show([Audio]);
            var Player=typeof(AssetInspectorControl).GetField("Audio",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(Inspector)!;
            var State=Player.GetType().GetField("_mediaPlayerState",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(Player)!;
            var Events=State.GetType().GetField("_mediaEventsHelper",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(State)!;
            var Raise=Events.GetType().GetMethod("RaiseMediaFailed",BindingFlags.NonPublic|BindingFlags.Instance)!;
            var Panel=(StackPanel)typeof(AssetInspectorControl).GetField("Panel",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(Inspector)!;
            Raise.Invoke(Events,[new InvalidOperationException("current sound failed")]);
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            Assert.Contains(Panel.Children.OfType<TextBlock>(),Text=>Text.Text.Contains("current sound failed"));
            Inspector.Show([]);
            Raise.Invoke(Events,[new InvalidOperationException("old sound failed")]);
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            Assert.DoesNotContain(Panel.Children.OfType<TextBlock>(),Text=>Text.Text.Contains("old sound failed"));
        });
    }
}
