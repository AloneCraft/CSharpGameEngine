using System.Reflection;
using System.Windows.Controls;
using CSharpGameEngine.Contracts;
using Microsoft.VisualStudio.Shell;
namespace CSharpGameEngine.VSExtension.Tests;
[Collection(MockedVsCollection.Name)]
public sealed class AssetDetailsViewLifecycleTests {
    public AssetDetailsViewLifecycleTests(MockedVsFixture Fixture){ }
    [Fact]
    public void NativeRenameEscapeDiscardsTypedTextBeforeTheShellCanMoveFocus(){
        ThreadHelper.JoinableTaskFactory.Run(async()=>{
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            using var Control=new AssetsToolWindowControl();
            var Row=new AssetWorkspaceRow(new(){Source=new(){SourcePath="Ship.glb"}});Assert.True(Row.BeginRename());Row.RenameText="Discarded.glb";
            // The IDE normally establishes this active edit through selection and F2.
            typeof(AssetsToolWindowControl).GetField("EditingRow",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(Control,Row);
            Assert.True(Control.HandleRenameEscape());Assert.False(Row.IsRenaming);Assert.Equal("Ship.glb",Row.RenameText);
            Assert.False(Control.HandleRenameEscape());
        });
    }
    [Fact]
    public void LoadingAssetTilesCanBeRealizedAndTheirAnimationStopsOnCancellation(){
        ThreadHelper.JoinableTaskFactory.Run(async()=>{
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            using var Control=new AssetsToolWindowControl();
            var List=(ListBox)Control.FindName("Tiles");
            foreach(var InitiallyLoading in new[]{true,false}){
                var Row=new AssetWorkspaceRow(new(){Kind=AssetWorkspaceKind.Model,Source=new(){SourcePath="Ship.glb"}},ThumbnailLoader:async Cancellation=>{ await Task.Delay(Timeout.Infinite,Cancellation);return null; });
                var Request=InitiallyLoading?Row.LoadAsync():null;
                var Presenter=new ContentPresenter{Content=Row,ContentTemplate=List.ItemTemplate};
                Presenter.Measure(new(130,160));Presenter.Arrange(new(0,0,130,160));
                var Tile=(StackPanel)System.Windows.Media.VisualTreeHelper.GetChild(Presenter,0);
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var Spinner=((Grid)Tile.Children[0]).Children.OfType<System.Windows.Shapes.Path>().Single();
                Assert.Equal(System.Windows.Visibility.Visible,Spinner.Visibility);Assert.True(Spinner.RenderTransform.HasAnimatedProperties);
                Row.CancelThumbnail();
#pragma warning disable VSTHRD003 // This test owns and cancels the pending loader; it has no VS work to join.
                if(Request is not null){ await Request; }
#pragma warning restore VSTHRD003
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                Assert.Equal(System.Windows.Visibility.Collapsed,Spinner.Visibility);Assert.False(Spinner.RenderTransform.HasAnimatedProperties);
            }
        });
    }
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
