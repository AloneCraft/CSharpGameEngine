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
/// シーン JSON（プロジェクトの <c>Scenes</c> フォルダー内の <c>*.json</c>）をダブルクリックして開く Scene Editor（ADR-0071）。XAML editor のように、Design view（Scene Designer）と JSON view（Visual Studio JSON editor）を1つの tab に表示します。
///
/// <para>
/// シーンはフォルダーによって識別される通常の <c>.json</c> ファイルなので、JSON editor より優先して <c>.json</c> を登録します。ほかの JSON ファイルには <c>VS_E_UNSUPPORTEDFORMAT</c> を返して処理を辞退し、Visual Studio が次の editor で開きます。
/// </para>
///
/// <para>
/// ドキュメントデータは常に JSON テキストバッファーです。ファイルをすでに開いている JSON editor と共有します。editor 自身は model、save、Undo を持ちません（ADR-0070 decision 5）。
/// </para>
/// </summary>
[Guid(FactoryGuidString)]
internal sealed class SceneJsonEditorFactory:IVsEditorFactory {
    public const string FactoryGuidString="5d8f0c52-7a3e-4b0f-9c11-2f6e3a9b4d71";

    /// <summary>JSON editor は 0x32 に <c>.json</c> を登録するため、Scene Editor を先に選択させる必要があります。</summary>
    public const int ExtensionPriority=0x40;

    /// <summary>Visual Studio JSON language service（Microsoft.WebTools.Languages.Json）。</summary>
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
            // 物理的な view は1つです。JSON（code）view が要求された場合、Visual Studio は別ウィンドウを開かず、
            // ペインに tab の切り替えを要求します（IVsMultiViewDocumentView）。
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

    /// <summary>シーン JSON だけを扱い、それ以外の JSON ファイルは次の editor に渡します。</summary>
    internal static bool Accepts(string?DocumentPath)=>SceneJsonCommitPlanner.IsJsonScene(DocumentPath);

    /// <summary>
    /// 別の editor がすでに開いた document data のテキストバッファー（JSON editor のバッファー、またはそれを提供するもの）。document data がテキストでなく、この editor と共有できない場合は null です。
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
        // editor の作成後、Visual Studio はこのバッファーにファイルをロードします（IVsPersistDocData）。
        var Buffer=(IVsTextLines)Adapters.CreateVsTextBufferAdapter(this.Site);
        Guid Language=JsonLanguageService;
        ErrorHandler.ThrowOnFailure(Buffer.SetLanguageServiceID(ref Language));
        return Buffer;
    }
}
