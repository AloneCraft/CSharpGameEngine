using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System.Reflection;
using System.Windows.Controls;
namespace CSharpGameEngine.VSExtension.Tests;
[Collection(MockedVsCollection.Name)]
public sealed class AssetEditorReloadTests {
    public AssetEditorReloadTests(MockedVsFixture Fixture){ }
    [Fact]
    public void AnExplicitReloadRestartsLoadingEvenWhenTheDocumentPathHasNotChanged(){
        ThreadHelper.JoinableTaskFactory.Run(async()=>{
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var Path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"CgeReload-"+Guid.NewGuid().ToString("N")+".png");
            Assert.True(AssetEditorRegistry.TryGet(Path,out var Descriptor));
            using var Pane=new AssetEditorDocumentPane(ServiceProvider.GlobalProvider,Path,Descriptor);
            var Content=Assert.IsAssignableFrom<AssetEditorContent>(Pane.EditorContent);
            var States=new List<string>();
            Content.StatusChanged+=States.Add;
            var Document=(IVsPersistDocData)Pane;
            Assert.Equal(VSConstants.S_OK,Document.LoadDocData(Path));
            Assert.DoesNotContain(VsixText.Get("AssetEditor_TextureLoading"),States);
            Assert.Equal(VSConstants.S_OK,Document.ReloadDocData(0));
            Assert.Contains(VsixText.Get("AssetEditor_TextureLoading"),States);
            Assert.Equal(Path,Content.FilePath);
        });
    }
    [Fact]
    public void ReloadingSoundClearsTheDisplayedTimeAndPlayheadImmediately(){
        ThreadHelper.JoinableTaskFactory.Run(async()=>{
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var Directory=System.IO.Path.GetTempPath();
            using var Content=new SoundEditorContent(System.IO.Path.Combine(Directory,"CgeSound-A.mp3"));
            var Text=(TextBlock)typeof(SoundEditorContent).GetField("TimeText",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(Content)!;
            var Wave=(AssetWaveformView)typeof(SoundEditorContent).GetField("Waveform",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(Content)!;
            Text.Text="old position";
            Wave.SetPlayhead(0.75f);
            Content.Reload(System.IO.Path.Combine(Directory,"CgeSound-B.mp3"));
            Assert.Equal(AssetWaveform.FormatTime(0f)+" / "+AssetWaveform.FormatTime(0f),Text.Text);
            Assert.Equal(0f,(float)typeof(AssetWaveformView).GetField("Playhead",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(Wave)!);
        });
    }
    [Fact]
    public void DataEditorTracksItsOwnRunningDocumentRename(){
        ThreadHelper.JoinableTaskFactory.Run(async()=>{
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var Before=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"CgeData-Before.cgeasset");
            var After=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"CgeData-After.cgeasset");
            Assert.True(AssetEditorRegistry.TryGet(Before,out var Descriptor));
            using var Pane=new AssetEditorTextPane(ServiceProvider.GlobalProvider,Before,Descriptor);
            var Events=Assert.IsAssignableFrom<IVsRunningDocTableEvents2>(Pane);
            Assert.Equal(VSConstants.S_OK,Events.OnAfterAttributeChangeEx(1,(uint)__VSRDTATTRIB.RDTA_MkDocument,null!,0,Before,null!,0,After));
            Assert.Equal(After,Pane.EditorContent!.FilePath);
            Events.OnAfterAttributeChangeEx(2,(uint)__VSRDTATTRIB.RDTA_MkDocument,null!,0,Before,null!,0,"other.cgeasset");
            Assert.Equal(After,Pane.EditorContent.FilePath);
        });
    }
}
