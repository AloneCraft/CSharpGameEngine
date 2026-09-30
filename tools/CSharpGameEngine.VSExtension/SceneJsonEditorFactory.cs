using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using IOleServiceProvider=Microsoft.VisualStudio.OLE.Interop.IServiceProvider;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// ADR-0071: the Scene Editor, opened by double-clicking a <c>*.scene.json</c>. One tab with a Design
/// view (the Scene Designer) and a JSON view (the Visual Studio JSON editor), like the XAML editor.
///
/// <para>
/// Registered for <c>.json</c> above the JSON editor, because Visual Studio does not match the
/// two-dot extension <c>.scene.json</c> (ADR-0021). Any other JSON file is declined with
/// <c>VS_E_UNSUPPORTEDFORMAT</c>, and Visual Studio opens it with the next editor.
/// </para>
///
/// <para>
/// The document data is always the JSON text buffer, shared with the JSON editor when that already
/// has the file open. The editor has no model, save or Undo of its own (ADR-0070 decision 5).
/// </para>
/// </summary>
[Guid(FactoryGuidString)]
internal sealed class SceneJsonEditorFactory:IVsEditorFactory {
    public const string FactoryGuidString="5d8f0c52-7a3e-4b0f-9c11-2f6e3a9b4d71";

    /// <summary>The JSON editor registers <c>.json</c> at 0x32; the Scene Editor must be asked first.</summary>
    public const int ExtensionPriority=0x40;

    /// <summary>The Visual Studio JSON language service (Microsoft.WebTools.Languages.Json).</summary>
    internal static readonly Guid JsonLanguageService=new("18588c2a-9945-44ad-9894-b271babc0582");


    private readonly IServiceProvider Package;
    private IOleServiceProvider?Site;

    internal SceneJsonEditorFactory(IServiceProvider Package)=>
        this.Package=Package??throw new ArgumentNullException(nameof(Package));

    public int SetSite(IOleServiceProvider ServiceProvider){
        this.Site=ServiceProvider;
        return VSConstants.S_OK;
    }

    public int Close()=>VSConstants.S_OK;

    public int CreateEditorInstance(
        uint CreateFlags,string DocumentPath,string PhysicalView,IVsHierarchy Hierarchy,uint ItemId,
        IntPtr ExistingDocData,out IntPtr DocView,out IntPtr DocData,out string Caption,out Guid CommandUi,
        out int CreateDocumentWindowFlags){
        ThreadHelper.ThrowIfNotOnUIThread();
        DocView=IntPtr.Zero;
        DocData=IntPtr.Zero;
        Caption=string.Empty;
        CommandUi=VSConstants.GUID_TextEditorFactory;
        CreateDocumentWindowFlags=0;
        if(!Accepts(DocumentPath)){
            return VSConstants.VS_E_UNSUPPORTEDFORMAT;
        }
        IVsTextLines?Existing=null;
        if(ExistingDocData!=IntPtr.Zero
            &&(Existing=SharedBuffer(Marshal.GetObjectForIUnknown(ExistingDocData)))is null){
            return VSConstants.VS_E_INCOMPATIBLEDOCDATA;
        }
        try {
            IVsTextLines Buffer=Existing??this.CreateBuffer();
            // One physical view: Visual Studio asks the pane to switch tabs (IVsMultiViewDocumentView)
            // when the JSON (code) view is requested, instead of opening a second window.
            SceneJsonEditorPane Pane=new(this.Package,DocumentPath,Buffer,SceneJsonEditorView.Design);
            ActivityLog.LogInformation(nameof(SceneJsonEditorFactory),$"Scene Editor opened '{DocumentPath}'; shared buffer={Existing is not null}.");
            DocView=Marshal.GetIUnknownForObject(Pane);
            DocData=Marshal.GetIUnknownForObject(Buffer);
            return VSConstants.S_OK;
        } catch(Exception Error){
            ActivityLog.LogError(nameof(SceneJsonEditorFactory),$"Scene Editor creation failed for '{DocumentPath}': {Error}");
            if(DocView!=IntPtr.Zero){ Marshal.Release(DocView);DocView=IntPtr.Zero; }
            return VSConstants.E_FAIL;
        }
    }

    /// <summary>Only a <c>*.scene.json</c>; every other JSON file goes to the next editor.</summary>
    internal static bool Accepts(string?DocumentPath)=>SceneJsonCommitPlanner.IsJsonScene(DocumentPath);

    /// <summary>
    /// The text buffer of document data another editor already opened (the JSON editor's buffer, or
    /// a provider of one); null when the document data is not text, which this editor cannot share.
    /// </summary>
    private static IVsTextLines?SharedBuffer(object DocData){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(DocData is IVsTextLines Lines){
            return Lines;
        }
        return DocData is IVsTextBufferProvider Provider
            &&Provider.GetTextBuffer(out IVsTextLines Provided)==VSConstants.S_OK
            ?Provided
            :null;
    }

    public int MapLogicalView(ref Guid LogicalView,out string PhysicalView){
        PhysicalView=null!;
        return SceneJsonEditorPane.ViewFor(LogicalView) is null?VSConstants.E_NOTIMPL:VSConstants.S_OK;
    }

    private IVsTextLines CreateBuffer(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Package.GetService(typeof(SComponentModel))is not IComponentModel ComponentModel
            ||this.Site is null){
            throw new InvalidOperationException("The editor services are unavailable.");
        }
        var Adapters=ComponentModel.GetService<IVsEditorAdaptersFactoryService>();
        // Visual Studio loads the file into this buffer (IVsPersistDocData) after the editor is created.
        var Buffer=(IVsTextLines)Adapters.CreateVsTextBufferAdapter(this.Site);
        Guid Language=JsonLanguageService;
        ErrorHandler.ThrowOnFailure(Buffer.SetLanguageServiceID(ref Language));
        return Buffer;
    }
}
