using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
namespace CSharpGameEngine.VSExtension;
[Guid(PaneGuidString)]
public sealed class AssetsToolWindowPane:ToolWindowPane {
    public const string PaneGuidString="ac836ce6-8b5d-4eda-a8f9-2f9145d85943";
    internal static AssetWorkspaceController?Controller{get;set;}
    internal AssetsToolWindowControl Control{get;}
    public AssetsToolWindowPane():base(null){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.Caption=VsixText.Get("Assets_Title");
        this.BitmapImageMoniker=AssetWorkspaceIcons.Window;
        this.Control=new AssetsToolWindowControl();
        this.Content=this.Control;
        this.Control.Loaded+=(_,_)=>{ ThreadHelper.ThrowIfNotOnUIThread();this.Control.Attach(Controller); };
    }
    protected override void Dispose(bool Disposing){
        if(Disposing){ this.Control.Dispose(); }
        base.Dispose(Disposing);
    }
    protected override bool PreProcessMessage(ref System.Windows.Forms.Message Message){
        ThreadHelper.ThrowIfNotOnUIThread();
        return this.Control.HandleNativeShortcut(AssetToolWindowInput.Shortcut(ref Message))||base.PreProcessMessage(ref Message);
    }
}
