using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.TextManager.Interop;
namespace CSharpGameEngine.VSExtension;
internal static class AssetDocumentWorkspace {
    internal static VisualStudioWorkspace Workspace=>Components.GetService<VisualStudioWorkspace>();
    private static IComponentModel Components=>(IComponentModel)(Package.GetGlobalService(typeof(SComponentModel))??throw new InvalidOperationException("Workspace unavailable."));
    internal static ITextUndoHistoryRegistry Histories=>Components.GetService<ITextUndoHistoryRegistry>();
    internal static string Read(string PathValue){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(TryBuffer(PathValue,out var Buffer)){ return Buffer!.CurrentSnapshot.GetText(); }
        return File.ReadAllText(PathValue);
    }
    internal static ITextBuffer OpenBuffer(string PathValue){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(TryBuffer(PathValue,out var Existing)){ return WithUndo(Existing!); }
        var Document=Workspace.CurrentSolution.Projects.SelectMany(static Project=>Project.AdditionalDocuments.Cast<TextDocument>().Concat(Project.Documents)).FirstOrDefault(Document=>string.Equals(Document.FilePath,PathValue,StringComparison.OrdinalIgnoreCase));
        if(Document is Document Code){ Workspace.OpenDocument(Code.Id,false); }
        else if(Document is not null){ Workspace.OpenAdditionalDocument(Document.Id,false); }
        else { VsShellUtilities.OpenDocument(ServiceProvider.GlobalProvider,PathValue,VSConstants.LOGVIEWID.TextView_guid,out _,out _,out _); }
        if(!TryBuffer(PathValue,out var Buffer)){ throw new InvalidOperationException("The document does not expose an editable text buffer: "+PathValue); }
        return WithUndo(Buffer!);
    }
    private static ITextBuffer WithUndo(ITextBuffer Buffer){
        Components.GetService<ITextBufferUndoManagerProvider>().GetTextBufferUndoManager(Buffer);
        return Buffer;
    }
    internal static bool TryBuffer(string PathValue,out ITextBuffer?Buffer){
        ThreadHelper.ThrowIfNotOnUIThread();
        Buffer=null;
        if(Package.GetGlobalService(typeof(SVsRunningDocumentTable)) is not IVsRunningDocumentTable Table){ return false; }
        var Pointer=IntPtr.Zero;
        try {
            if(ErrorHandler.Failed(Table.FindAndLockDocument((uint)_VSRDTFLAGS.RDT_NoLock,PathValue,out _,out _,out Pointer,out _))||Pointer==IntPtr.Zero){ return false; }
            var Data=Marshal.GetObjectForIUnknown(Pointer);
            var Native=Data as IVsTextBuffer;
            if(Native is null&&Data is IVsTextBufferProvider Provider&&ErrorHandler.Succeeded(Provider.GetTextBuffer(out var Lines))){ Native=Lines as IVsTextBuffer; }
            if(Native is null){ throw new InvalidOperationException("An open custom editor must be saved and closed before this source operation: "+PathValue); }
            Buffer=Components.GetService<IVsEditorAdaptersFactoryService>().GetDocumentBuffer(Native);
            return Buffer is not null;
        } finally { if(Pointer!=IntPtr.Zero){ Marshal.Release(Pointer); } }
    }
    internal static bool CloseCleanDocument(string PathValue){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(!VsShellUtilities.IsDocumentOpen(ServiceProvider.GlobalProvider,PathValue,Guid.Empty,out _,out _,out var Frame)){ return false; }
        var Rdt=(IVsRunningDocumentTable)Package.GetGlobalService(typeof(SVsRunningDocumentTable));
        var Pointer=IntPtr.Zero;
        try {
            ErrorHandler.ThrowOnFailure(Rdt.FindAndLockDocument((uint)_VSRDTFLAGS.RDT_NoLock,PathValue,out _,out _,out Pointer,out _));
            if(Pointer==IntPtr.Zero||Marshal.GetObjectForIUnknown(Pointer) is not IVsPersistDocData Persist){ throw new InvalidOperationException("Cannot inspect the open asset editor."); }
            ErrorHandler.ThrowOnFailure(Persist.IsDocDataDirty(out var Dirty));
            if(Dirty!=0){ throw new InvalidOperationException(VsixText.Get("Assets_Conflict")); }
            ErrorHandler.ThrowOnFailure(Frame.CloseFrame((uint)__FRAMECLOSE.FRAMECLOSE_NoSave));
            return true;
        }finally { if(Pointer!=IntPtr.Zero){ Marshal.Release(Pointer); } }
    }
    internal static async Task<Compilation>CompilationAsync(string ProjectPath,CancellationToken CancellationToken){
        var Project=Workspace.CurrentSolution.Projects.FirstOrDefault(Project=>string.Equals(Project.FilePath,ProjectPath,StringComparison.OrdinalIgnoreCase));
        if(Project is null){ throw new InvalidOperationException("Game project is not loaded in the workspace."); }
        return await Project.GetCompilationAsync(CancellationToken)??throw new InvalidOperationException("Game compilation is unavailable.");
    }
    internal static bool Write(string PathValue,string Before,string After,string Description){
        ThreadHelper.ThrowIfNotOnUIThread();
        return new SceneTextBufferWriter(Histories).TryApply(OpenBuffer(PathValue),SourceText.From(Before),SourceText.From(After),Description);
    }
    internal static void Save(string PathValue){
        ThreadHelper.ThrowIfNotOnUIThread();
        var Rdt=Package.GetGlobalService(typeof(SVsRunningDocumentTable)) as IVsRunningDocumentTable
            ??throw new InvalidOperationException(VsixText.Format("AssetEditor_SaveUnavailable",PathValue));
        Save(PathValue,Rdt);
    }
    internal static void Save(string PathValue,IVsRunningDocumentTable Rdt){
        ThreadHelper.ThrowIfNotOnUIThread();
        var Pointer=IntPtr.Zero;
        try {
            ErrorHandler.ThrowOnFailure(Rdt.FindAndLockDocument((uint)_VSRDTFLAGS.RDT_NoLock,PathValue,out _,out _,out Pointer,out _));
            if(Pointer==IntPtr.Zero||Marshal.GetObjectForIUnknown(Pointer) is not IVsPersistDocData Persist){
                throw new InvalidOperationException(VsixText.Format("AssetEditor_SaveUnavailable",PathValue));
            }
            ErrorHandler.ThrowOnFailure(Persist.SaveDocData(VSSAVEFLAGS.VSSAVE_Save,out _,out var Cancelled));
            if(Cancelled!=0){ throw new OperationCanceledException(); }
        }finally { if(Pointer!=IntPtr.Zero){ Marshal.Release(Pointer); } }
    }
}
