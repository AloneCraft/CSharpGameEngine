using System;
using System.IO;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension;

internal static class SceneDesignerDocumentWindowPolicy {
    /// <summary>Ad-hoc window panes have no registered editor factory.</summary>
    internal static Guid EditorTypeGuid=>Guid.Empty;
    /// <summary>
    /// A Scene Designer tab is a programmatically-created view, not a persisted source document.
    /// Keep it out of save/MRU/session restoration while still giving the shell a stable RDT key.
    /// </summary>
    internal const uint CreateFlags=(uint)__VSCREATEDOCWIN.CDW_fCreateNewWindow
        |(uint)_VSRDTFLAGS.RDT_NonCreatable
        |(uint)_VSRDTFLAGS.RDT_DontSave
        |(uint)_VSRDTFLAGS.RDT_DontSaveAs
        |(uint)_VSRDTFLAGS.RDT_DontAutoOpen
        |(uint)_VSRDTFLAGS.RDT_VirtualDocument
        |(uint)_VSRDTFLAGS.RDT_DontAddToMRU;

    /// <summary>
    /// Direct <c>IVsUIShell.CreateDocumentWindow</c> requires the caller to supply a valid
    /// hierarchy/item pair. Scene Designer is not a project item, so use the environment's
    /// Miscellaneous Files owner, which supplies that pair for external/virtual documents.
    /// </summary>
    internal static Type HostServiceType=>typeof(SVsExternalFilesManager);

    /// <summary>
    /// The primary physical view is represented by a null BSTR. An empty BSTR is a named
    /// physical view and is invalid when no editor factory is registered.
    /// </summary>
    internal static string?PhysicalView=>null;

    internal static string EditorCaption=>string.Empty;

    /// <summary>
    /// This fileless pane has no separate persisted model. Registering the pane itself as both
    /// DocView and DocData prevents the Miscellaneous Files owner from trying to manufacture a
    /// default DocData through a nonexistent editor factory.
    /// </summary>
    internal static IntPtr SelectDocData(IntPtr DocView)=>DocView!=IntPtr.Zero
        ?DocView:throw new ArgumentException("The Scene Designer DocView is required.",nameof(DocView));

    internal static SceneDesignerDocumentWindowRequest CreateRequest(SceneKey Key)=>
        new(CreateMoniker(Key),CreateFlags,EditorTypeGuid,PhysicalView,Guid.Empty,
            SceneDesignerDocumentPane.CreateCaption(Key),EditorCaption);

    internal static void EnsureAddDocumentSucceeded(
        SceneDesignerDocumentWindowRequest Request,
        int HResult,
        object? Frame){
        if(HResult<0){
            throw CreateFailure("IVsExternalFilesManager.AddDocument",Request.Moniker,HResult);
        }
        if(Frame is null){
            throw CreateFailure("IVsExternalFilesManager.AddDocument returned no frame",Request.Moniker,
                unchecked((int)0x8000FFFF));
        }
    }

    internal static string CreateMoniker(SceneKey Key)=>
        Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "CSharpGameEngine.SceneDesigner",
            Uri.EscapeDataString(Key.ProjectId),
            Uri.EscapeDataString(Key.ClassFullyQualifiedName)+".cgescene"));

    internal static SceneDesignerDocumentWindowException CreateFailure(
        string Operation,
        string Moniker,
        int HResult,
        Exception?InnerException=null)=>
        new(Operation,Moniker,HResult,InnerException);

    internal static string FormatHResult(int HResult)=>$"0x{HResult:X8}";
}

internal sealed class SceneDesignerDocumentWindowRequest {
    internal SceneDesignerDocumentWindowRequest(
        string Moniker,
        uint Flags,
        Guid EditorTypeGuid,
        string?PhysicalView,
        Guid CommandUiGuid,
        string OwnerCaption,
        string EditorCaption){
        this.Moniker=Moniker;
        this.Flags=Flags;
        this.EditorTypeGuid=EditorTypeGuid;
        this.PhysicalView=PhysicalView;
        this.CommandUiGuid=CommandUiGuid;
        this.OwnerCaption=OwnerCaption;
        this.EditorCaption=EditorCaption;
    }

    internal string Moniker { get; }

    internal uint Flags { get; }

    internal Guid EditorTypeGuid { get; }

    internal string?PhysicalView { get; }

    internal Guid CommandUiGuid { get; }

    internal string OwnerCaption { get; }

    internal string EditorCaption { get; }
}

/// <summary>
/// Carries the exact shell call contract into ActivityLog, the persistent diagnostics file, and
/// the fallback status. This avoids hiding an HRESULT behind a generic ArgumentException.
/// </summary>
internal sealed class SceneDesignerDocumentWindowException:InvalidOperationException {
    internal SceneDesignerDocumentWindowException(
        string Operation,
        string Moniker,
        int HResult,
        Exception?InnerException=null)
        :base(CreateMessage(Operation,Moniker,HResult),InnerException){
        this.Operation=Operation;
        this.Moniker=Moniker;
        this.ErrorCode=HResult;
        this.HResult=HResult;
    }

    internal string Operation { get; }

    internal string Moniker { get; }

    internal int ErrorCode { get; }

    internal string DiagnosticText=>
        $"operation={this.Operation}; HRESULT={SceneDesignerDocumentWindowPolicy.FormatHResult(this.ErrorCode)}; "
        +$"flags={SceneDesignerDocumentWindowPolicy.FormatHResult(unchecked((int)SceneDesignerDocumentWindowPolicy.CreateFlags))}; "
        +$"editor={SceneDesignerDocumentWindowPolicy.EditorTypeGuid:B}; "
        +$"commandUi={Guid.Empty:B}; physicalView='{SceneDesignerDocumentWindowPolicy.PhysicalView}'; "
        +$"moniker='{this.Moniker}'; hostService={SceneDesignerDocumentWindowPolicy.HostServiceType.FullName}.";

    private static string CreateMessage(string Operation,string Moniker,int HResult)=>
        $"Scene Designer Document Well creation failed in {Operation} "
        +$"({SceneDesignerDocumentWindowPolicy.FormatHResult(HResult)}); moniker='{Moniker}'.";
}
