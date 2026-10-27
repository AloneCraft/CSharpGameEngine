using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System.Runtime.InteropServices;
namespace CSharpGameEngine.VSExtension.Tests;
[Collection(MockedVsCollection.Name)]
public sealed class AssetDocumentSaveTests {
    public AssetDocumentSaveTests(MockedVsFixture Fixture){ }
    [Fact]
    public void MissingDocumentIsNotReportedAsSaved(){
        ThreadHelper.JoinableTaskFactory.Run(async()=>{
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var Table=new MissingDocumentTable();
            Assert.Throws<InvalidOperationException>(()=>AssetDocumentWorkspace.Save(@"C:\missing.cgeasset",Table));
        });
    }
    [Fact]
    public void DocumentWithoutPersistenceIsNotReportedAsSaved(){
        ThreadHelper.JoinableTaskFactory.Run(async()=>{
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var Table=new MissingDocumentTable { Pointer=Marshal.GetIUnknownForObject(new object()) };
            Assert.Throws<InvalidOperationException>(()=>AssetDocumentWorkspace.Save(@"C:\unsupported.cgeasset",Table));
        });
    }
    [Fact]
    public void DataEditorSubscribesOnceAndReleasesDocumentEventsOnDispose(){
        ThreadHelper.JoinableTaskFactory.Run(async()=>{
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var Path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"CgeData-Tracking.cgeasset");
            Assert.True(AssetEditorRegistry.TryGet(Path,out var Descriptor));
            var Table=new MissingDocumentTable();
            var Pane=new AssetEditorTextPane(ServiceProvider.GlobalProvider,Path,Descriptor);
            try {
                Pane.TrackRunningDocuments(Table);
                Pane.TrackRunningDocuments(Table);
                Assert.Equal(1,Table.AdviseCount);
                Assert.Same(Pane,Table.Sink);
            } finally {
                Pane.Dispose();
            }
            Assert.Equal(1,Table.UnadviseCount);
            Assert.Equal(42u,Table.LastUnadvisedCookie);
        });
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpenDocumentIsSavedOrReportsCancellation(bool Cancel){
        ThreadHelper.JoinableTaskFactory.Run(async()=>{
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var Persist=new PersistedDocument(Cancel);
            var Table=new MissingDocumentTable { Pointer=Marshal.GetIUnknownForObject(Persist) };
            if(Cancel){ Assert.Throws<OperationCanceledException>(()=>AssetDocumentWorkspace.Save(@"C:\open.cgeasset",Table)); }
            else { AssetDocumentWorkspace.Save(@"C:\open.cgeasset",Table); }
            Assert.Equal(1,Persist.SaveCount);
        });
    }
    private sealed class MissingDocumentTable:IVsRunningDocumentTable {
        internal IntPtr Pointer;
        internal int AdviseCount;
        internal int UnadviseCount;
        internal uint LastUnadvisedCookie;
        internal IVsRunningDocTableEvents?Sink;
        public int FindAndLockDocument(uint Flags,string Moniker,out IVsHierarchy Hierarchy,out uint ItemId,out IntPtr DocData,out uint Cookie){
            Hierarchy=null!;ItemId=0;DocData=this.Pointer;Cookie=0;
            return VSConstants.S_OK;
        }
        public int RegisterAndLockDocument(uint Flags,string Moniker,IVsHierarchy Hierarchy,uint ItemId,IntPtr DocData,out uint Cookie)=>throw new NotSupportedException();
        public int LockDocument(uint Flags,uint Cookie)=>throw new NotSupportedException();
        public int UnlockDocument(uint Flags,uint Cookie)=>throw new NotSupportedException();
        public int RenameDocument(string OldMoniker,string NewMoniker,IntPtr Hierarchy,uint ItemId)=>throw new NotSupportedException();
        public int AdviseRunningDocTableEvents(IVsRunningDocTableEvents Sink,out uint Cookie){ this.AdviseCount++;this.Sink=Sink;Cookie=42;return VSConstants.S_OK; }
        public int UnadviseRunningDocTableEvents(uint Cookie){ this.UnadviseCount++;this.LastUnadvisedCookie=Cookie;this.Sink=null;return VSConstants.S_OK; }
        public int GetDocumentInfo(uint Cookie,out uint Flags,out uint ReadLocks,out uint EditLocks,out string Moniker,out IVsHierarchy Hierarchy,out uint ItemId,out IntPtr DocData)=>throw new NotSupportedException();
        public int NotifyDocumentChanged(uint Cookie,uint Attributes)=>throw new NotSupportedException();
        public int NotifyOnAfterSave(uint Cookie)=>throw new NotSupportedException();
        public int GetRunningDocumentsEnum(out IEnumRunningDocuments Enumerator)=>throw new NotSupportedException();
        public int SaveDocuments(uint Flags,IVsHierarchy Hierarchy,uint ItemId,uint Cookie)=>throw new NotSupportedException();
        public int NotifyOnBeforeSave(uint Cookie)=>throw new NotSupportedException();
        public int RegisterDocumentLockHolder(uint Flags,uint Cookie,IVsDocumentLockHolder Holder,out uint LockHolderCookie)=>throw new NotSupportedException();
        public int UnregisterDocumentLockHolder(uint LockHolderCookie)=>throw new NotSupportedException();
        public int ModifyDocumentFlags(uint Cookie,uint Flags,int Set)=>throw new NotSupportedException();
    }
    private sealed class PersistedDocument(bool Cancel):IVsPersistDocData {
        internal int SaveCount;
        public int SaveDocData(VSSAVEFLAGS Flags,out string NewPath,out int Canceled){ this.SaveCount++;NewPath=null!;Canceled=Cancel?1:0;return VSConstants.S_OK; }
        public int GetGuidEditorType(out Guid Guid)=>throw new NotSupportedException();
        public int IsDocDataDirty(out int Dirty)=>throw new NotSupportedException();
        public int SetUntitledDocPath(string Path)=>throw new NotSupportedException();
        public int LoadDocData(string Path)=>throw new NotSupportedException();
        public int Close()=>throw new NotSupportedException();
        public int OnRegisterDocData(uint Cookie,IVsHierarchy Hierarchy,uint ItemId)=>throw new NotSupportedException();
        public int RenameDocData(uint Attributes,IVsHierarchy Hierarchy,uint ItemId,string Path)=>throw new NotSupportedException();
        public int IsDocDataReloadable(out int Reloadable)=>throw new NotSupportedException();
        public int ReloadDocData(uint Flags)=>throw new NotSupportedException();
    }
}
