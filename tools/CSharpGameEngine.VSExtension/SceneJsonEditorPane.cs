using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Threading;
using IOleServiceProvider=Microsoft.VisualStudio.OLE.Interop.IServiceProvider;

namespace CSharpGameEngine.VSExtension;

internal enum SceneJsonEditorView {
    Design,
    Json,
}

/// <summary>
/// The Scene Editor's document pane (ADR-0071): the Scene Designer and the JSON text of one
/// scene JSON, switched by tabs at the bottom like the XAML editor. Both views show the one
/// JSON text buffer: the designer writes its gestures into it (SceneTextBufferWriter), and the JSON
/// view is a Visual Studio code window over it, so Save, the dirty mark and Undo are Visual Studio's.
///
/// <para>
/// The pane registers no standard commands of its own (M0045). While the JSON view is shown, command
/// routing reaches the code window's text view; the designer handles its keys itself.
/// </para>
/// </summary>
internal sealed class SceneJsonEditorPane:WindowPane,IOleCommandTarget,IVsWindowFrameNotify3,IVsMultiViewDocumentView {
    private static readonly List<WeakReference<SceneJsonEditorPane>>OpenPanes=[];
    private static Func<SceneEditorSessionService?>?SessionProvider;

    private readonly string FilePath;
    private readonly IVsTextLines Buffer;
    private readonly Grid Root=new();
    private readonly ContentControl DesignHost=new();
    private readonly ContentControl JsonHost=new();
    private readonly TextBlock DesignMessage=new(){ Margin=new Thickness(12),TextWrapping=TextWrapping.Wrap };
    private readonly ToggleButton DesignTab;
    private readonly ToggleButton JsonTab;
    private SceneJsonEditorView Current;
    private SceneDesignerToolWindowControl?Designer;
    private IVsTextView?TextView;
    private IOleCommandTarget?TextViewCommands;
    private IWpfTextViewHost?TextViewHost;
    private JoinableTask?LoadingTask;
    private bool Closed;
    private bool Switching;

    internal SceneJsonEditorPane(System.IServiceProvider Package,string FilePath,IVsTextLines Buffer,SceneJsonEditorView InitialView):base(Package){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.FilePath=FilePath??throw new ArgumentNullException(nameof(FilePath));
        this.Buffer=Buffer??throw new ArgumentNullException(nameof(Buffer));
        this.Current=InitialView;
        this.DesignTab=CreateTab(VsixText.Get("SceneEditorDesignTab"));
        this.JsonTab=CreateTab(VsixText.Get("SceneEditorJsonTab"));
        // Checked, not Click: a click and UI Automation's Toggle both select a tab. The selected tab
        // cannot be unchecked; the other tab is the way to switch.
        this.DesignTab.Checked+=(_,_)=>this.Show(SceneJsonEditorView.Design);
        this.JsonTab.Checked+=(_,_)=>this.Show(SceneJsonEditorView.Json);
        this.DesignTab.Unchecked+=(_,_)=>this.KeepSelectedTabChecked();
        this.JsonTab.Unchecked+=(_,_)=>this.KeepSelectedTabChecked();
        this.Root.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
        this.Root.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        this.DesignHost.Content=this.DesignMessage;
        this.DesignMessage.SetResourceReference(TextBlock.ForegroundProperty,EnvironmentColors.ToolWindowTextBrushKey);
        this.Root.Children.Add(this.DesignHost);
        this.Root.Children.Add(this.JsonHost);
        StackPanel Tabs=new(){ Orientation=Orientation.Horizontal };
        Tabs.Children.Add(this.DesignTab);
        Tabs.Children.Add(this.JsonTab);
        Border TabBar=new(){ Child=Tabs,BorderThickness=new Thickness(0,1,0,0) };
        TabBar.SetResourceReference(Border.BackgroundProperty,EnvironmentColors.ToolWindowBackgroundBrushKey);
        TabBar.SetResourceReference(Border.BorderBrushProperty,EnvironmentColors.ToolWindowBorderBrushKey);
        Grid.SetRow(TabBar,1);
        this.Root.Children.Add(TabBar);
        this.Root.SetResourceReference(Panel.BackgroundProperty,EnvironmentColors.ToolWindowBackgroundBrushKey);
        this.Content=this.Root;
        OpenPanes.Add(new WeakReference<SceneJsonEditorPane>(this));
    }

    internal SceneJsonEditorView CurrentView=>this.Current;

    /// <summary>
    /// The tab a logical view selects: Primary and Designer show the Design view, TextView and Code
    /// the JSON view (View Code / F7). Null for a view this editor does not have.
    /// </summary>
    internal static SceneJsonEditorView?ViewFor(Guid LogicalView)=>
        LogicalView==VSConstants.LOGVIEWID_Primary||LogicalView==VSConstants.LOGVIEWID_Designer?SceneJsonEditorView.Design
        :LogicalView==VSConstants.LOGVIEWID_TextView||LogicalView==VSConstants.LOGVIEWID_Code?SceneJsonEditorView.Json
        :null;

    public int ActivateLogicalView(ref Guid LogicalView){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(ViewFor(LogicalView) is not { }View){
            return VSConstants.E_NOTIMPL;
        }
        this.Show(View);
        return VSConstants.S_OK;
    }

    public int IsLogicalViewActive(ref Guid LogicalView,out int IsActive){
        IsActive=ViewFor(LogicalView)==this.Current?1:0;
        return VSConstants.S_OK;
    }

    public int GetActiveLogicalView(out Guid LogicalView){
        LogicalView=this.Current==SceneJsonEditorView.Json?VSConstants.LOGVIEWID_TextView:VSConstants.LOGVIEWID_Designer;
        return VSConstants.S_OK;
    }

    /// <summary>Lets panes opened before the scene subsystem started attach once it has.</summary>
    internal static void ConfigureSessionProvider(Func<SceneEditorSessionService?>Provider){
        ThreadHelper.ThrowIfNotOnUIThread();
        SessionProvider=Provider??throw new ArgumentNullException(nameof(Provider));
        foreach(var Pane in LivePanes()){ Pane.LoadDesign(); }
    }

    internal static void ClearSessionProvider()=>SessionProvider=null;

    /// <summary>
    /// Solution close: the shell is already closing the frames, and re-entering CloseFrame from that
    /// callback deadlocks. Release only the engine-owned designers and preview hosts.
    /// </summary>
    internal static void ReleaseRuntimeResourcesForSolutionClose(){
        ThreadHelper.ThrowIfNotOnUIThread();
        foreach(var Pane in LivePanes()){ Pane.ReleaseDesigner(); }
    }

    private static List<SceneJsonEditorPane>LivePanes(){
        List<SceneJsonEditorPane>Live=[];
        OpenPanes.RemoveAll(Reference=>!Reference.TryGetTarget(out var Pane)||Pane.Closed);
        foreach(var Reference in OpenPanes){
            if(Reference.TryGetTarget(out var Pane)){ Live.Add(Pane); }
        }
        return Live;
    }

    protected override void OnCreate(){
        ThreadHelper.ThrowIfNotOnUIThread();
        base.OnCreate();
        if(this.GetService(typeof(SVsWindowFrame))is IVsWindowFrame Frame){
            ErrorHandler.ThrowOnFailure(Frame.SetProperty((int)__VSFPROPID.VSFPROPID_ViewHelper,this));
        }
        this.Show(this.Current);
    }

    private static ToggleButton CreateTab(string Text){
        ToggleButton Tab=new(){ Content=Text,Padding=new Thickness(12,3,12,3),BorderThickness=new Thickness(0),Focusable=false };
        Tab.SetResourceReference(Control.ForegroundProperty,EnvironmentColors.ToolWindowTextBrushKey);
        Tab.SetResourceReference(Control.BackgroundProperty,EnvironmentColors.ToolWindowBackgroundBrushKey);
        return Tab;
    }

    /// <summary>Switches the view. Only a click on a tab calls this with the other view, so it never steals focus (ADR-0041).</summary>
    internal void Show(SceneJsonEditorView View){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Closed||this.Switching){ return; }
        this.Current=View;
        this.Switching=true;
        try {
            this.DesignTab.IsChecked=View==SceneJsonEditorView.Design;
            this.JsonTab.IsChecked=View==SceneJsonEditorView.Json;
        } finally {
            this.Switching=false;
        }
        this.DesignHost.Visibility=View==SceneJsonEditorView.Design?Visibility.Visible:Visibility.Collapsed;
        this.JsonHost.Visibility=View==SceneJsonEditorView.Json?Visibility.Visible:Visibility.Collapsed;
        if(View==SceneJsonEditorView.Json){
            this.EnsureTextView();
        } else {
            this.LoadDesign();
        }
    }

    private void KeepSelectedTabChecked(){
        if(this.Switching){ return; }
        this.Switching=true;
        try {
            this.DesignTab.IsChecked=this.Current==SceneJsonEditorView.Design;
            this.JsonTab.IsChecked=this.Current==SceneJsonEditorView.Json;
        } finally {
            this.Switching=false;
        }
    }

    /// <summary>
    /// Analyzes the file and pins its scene, so the Designer, Hierarchy and Inspector show this tab's
    /// scene. Runs when the Design view is shown and whenever the tab comes to the front.
    /// </summary>
    private void LoadDesign(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Closed||this.LoadingTask is { IsCompleted:false }||this.Current!=SceneJsonEditorView.Design){ return; }
        if(SessionProvider?.Invoke() is not { }Session){
            this.DesignMessage.Text=VsixText.Get("SceneEditorStarting");
            this.DesignHost.Content=this.DesignMessage;
            return;
        }
        if(this.Designer?.FixedSceneKey is { }Key&&Key.Equals(Session.PinnedSceneKey)){ return; }
        this.LoadingTask=ThreadHelper.JoinableTaskFactory.RunAsync(async ()=>{
            try {
                var Models=await Session.AnalyzeFileNowAsync(this.FilePath,System.Threading.CancellationToken.None).ConfigureAwait(false);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if(this.Closed){ return; }
                if(Models.Count!=1){
                    this.DesignMessage.Text=VsixText.Get("SceneDesigner_SelectedSceneMissing");
                    this.DesignHost.Content=this.DesignMessage;
                    return;
                }
                var Snapshot=Session.Pin(Models[0]);
                this.Designer??=new SceneDesignerToolWindowControl { ShowsSceneSelector=false };
                this.Designer.FixedSceneKey=Snapshot.Key;
                this.DesignHost.Content=this.Designer;
                this.Designer.SetTarget(Session,Snapshot);
            } catch(Exception Error){
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                ActivityLog.LogError(nameof(SceneJsonEditorPane),$"Scene Editor design view failed for '{this.FilePath}': {Error}");
                this.DesignMessage.Text=VsixText.Format("SceneDesigner_OpenFailed",this.FilePath,Error.GetType().Name,Error.Message);
                this.DesignHost.Content=this.DesignMessage;
            }
        });
    }

    /// <summary>
    /// The JSON view: a Visual Studio text view over the document's text buffer, created for WPF
    /// hosting (no HWND of its own) so its host control can sit in this pane. A code window was tried
    /// first: CreatePaneWindow parents the host control in a window of its own, and placing it here as
    /// well crashed Visual Studio in WPF layout (AddVisualChild).
    /// </summary>
    private void EnsureTextView(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.TextView is not null){ return; }
        if(this.GetService(typeof(SComponentModel))is not IComponentModel ComponentModel
            ||this.GetService(typeof(IOleServiceProvider))is not IOleServiceProvider OleServices){
            throw new InvalidOperationException("The editor services are unavailable.");
        }
        var Adapters=ComponentModel.GetService<IVsEditorAdaptersFactoryService>();
        var Factory=ComponentModel.GetService<ITextEditorFactoryService>();
        IVsTextView View=Adapters.CreateVsTextViewAdapter(OleServices,Factory.CreateTextViewRoleSet(
            PredefinedTextViewRoles.Analyzable,PredefinedTextViewRoles.Document,PredefinedTextViewRoles.Editable,
            PredefinedTextViewRoles.Interactive,PredefinedTextViewRoles.Structured,PredefinedTextViewRoles.Zoomable,
            PredefinedTextViewRoles.PrimaryDocument));
        ErrorHandler.ThrowOnFailure(View.Initialize(
            this.Buffer,IntPtr.Zero,
            (uint)TextViewInitFlags.VIF_HSCROLL|(uint)TextViewInitFlags.VIF_VSCROLL|(uint)TextViewInitFlags3.VIF_NO_HWND_SUPPORT,
            [new INITVIEW { fSelectionMargin=0,fWidgetMargin=0,fVirtualSpace=0,fDragDropMove=1 }]));
        IWpfTextViewHost Host=Adapters.GetWpfTextViewHost(View)
            ??throw new InvalidOperationException("The JSON view has no WPF host.");
        this.TextViewHost=Host;
        this.TextViewCommands=View as IOleCommandTarget;
        this.JsonHost.Content=Host.HostControl;
        this.TextView=View;
    }

    int IOleCommandTarget.QueryStatus(ref Guid CommandGroup,uint CommandCount,OLECMD[]Commands,IntPtr Text){
        ThreadHelper.ThrowIfNotOnUIThread();
        return this.Current==SceneJsonEditorView.Json&&this.TextViewCommands is { }Target
            ?Target.QueryStatus(ref CommandGroup,CommandCount,Commands,Text)
            :(int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
    }

    int IOleCommandTarget.Exec(ref Guid CommandGroup,uint CommandId,uint Options,IntPtr In,IntPtr Out){
        ThreadHelper.ThrowIfNotOnUIThread();
        return this.Current==SceneJsonEditorView.Json&&this.TextViewCommands is { }Target
            ?Target.Exec(ref CommandGroup,CommandId,Options,In,Out)
            :(int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
    }

    public int OnShow(int Show){
        ThreadHelper.ThrowIfNotOnUIThread();
        // The tab came to the front: its scene becomes the one Hierarchy and Inspector edit.
        if(Show is (int)__FRAMESHOW.FRAMESHOW_WinShown or (int)__FRAMESHOW.FRAMESHOW_TabActivated){
            this.LoadDesign();
        }
        return VSConstants.S_OK;
    }

    public int OnMove(int X,int Y,int Width,int Height)=>VSConstants.S_OK;
    public int OnSize(int X,int Y,int Width,int Height)=>VSConstants.S_OK;
    public int OnDockableChange(int Docked,int X,int Y,int Width,int Height)=>VSConstants.S_OK;
    public int OnClose(ref uint Options)=>VSConstants.S_OK;

    private void ReleaseDesigner(){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.Designer?.Dispose();
        this.Designer=null;
        this.DesignHost.Content=this.DesignMessage;
    }

    protected override void OnClose(){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.Closed=true;
        if(SessionProvider?.Invoke() is { }Session
            &&SceneDesignerTargetFilter.ReleasesPinOnClose(this.Designer?.FixedSceneKey,Session.PinnedSceneKey)){
            Session.ClearPin();
        }
        this.ReleaseDesigner();
        if(this.TextView is { }View){
            this.JsonHost.Content=null;
            // Closing the adapter closes its WPF text view and host as well.
            View.CloseView();
            this.TextView=null;
            this.TextViewCommands=null;
            this.TextViewHost=null;
        }
        base.OnClose();
    }
}
