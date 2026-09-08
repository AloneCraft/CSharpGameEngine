using System;
using System.Collections.Generic;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using CSharpGameEngine.ShaderGraph;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
namespace CSharpGameEngine.VSExtension;
public static class ShaderGraphAutomationIds {
    public const string Root="Cge.ShaderGraph.Root";
    public const string Toolbar="Cge.ShaderGraph.Toolbar";
    public const string Undo="Cge.ShaderGraph.Undo";
    public const string Redo="Cge.ShaderGraph.Redo";
    public const string Save="Cge.ShaderGraph.Save";
    public const string EntryTabs="Cge.ShaderGraph.EntryTabs";
    public const string PaletteSearch="Cge.ShaderGraph.PaletteSearch";
    public const string PaletteList="Cge.ShaderGraph.PaletteList";
    public const string DependencyList="Cge.ShaderGraph.DependencyList";
    public const string Canvas="Cge.ShaderGraph.Canvas";
    public const string PropertyPanel="Cge.ShaderGraph.PropertyPanel";
    public const string Diagnostics="Cge.ShaderGraph.Diagnostics";
    public const string Status="Cge.ShaderGraph.Status";
    public static IReadOnlyList<string>StaticIds { get; }=[Root,Toolbar,Undo,Redo,Save,EntryTabs,PaletteSearch,PaletteList,DependencyList,Canvas,PropertyPanel,Diagnostics,Status];
    public static string Node(StableId NodeId)=>$"Cge.ShaderGraph.Node.{NodeId}";
    public static string Argument(StableId NodeId,string Name)=>$"Cge.ShaderGraph.Argument.{NodeId}.{Escape(Name)}";
    private static string Escape(string Value)=>Value.Replace(" ","_").Replace(".","_");
}
internal sealed class ShaderGraphAutomationBorder:Border {
    internal Action<float,float>?AutomationMove { get; set; }
    protected override AutomationPeer OnCreateAutomationPeer()=>new ShaderGraphAutomationBorderPeer(this);
}
internal sealed class ShaderGraphAutomationBorderPeer:FrameworkElementAutomationPeer,ITransformProvider {
    private readonly ShaderGraphAutomationBorder OwnerBorder;
    private JoinableTask?RunningMove;
    internal ShaderGraphAutomationBorderPeer(ShaderGraphAutomationBorder Owner):base(Owner) { this.OwnerBorder=Owner; }
    public override object?GetPattern(PatternInterface PatternInterface)=>PatternInterface==PatternInterface.Transform?this:base.GetPattern(PatternInterface);
    bool ITransformProvider.CanMove=>true;
    bool ITransformProvider.CanResize=>false;
    bool ITransformProvider.CanRotate=>false;
    void ITransformProvider.Move(double X,double Y) {
        if(double.IsNaN(X)||double.IsInfinity(X)||double.IsNaN(Y)||double.IsInfinity(Y)||X<float.MinValue||X>float.MaxValue||Y<float.MinValue||Y>float.MaxValue){ throw new ArgumentOutOfRangeException(); }
        var ProjectedX=(float)X;
        var ProjectedY=(float)Y;
        if(this.OwnerBorder.Dispatcher.CheckAccess()) { this.OwnerBorder.AutomationMove?.Invoke(ProjectedX,ProjectedY);return; }
        this.RunningMove=ThreadHelper.JoinableTaskFactory.RunAsync(async delegate {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            this.OwnerBorder.AutomationMove?.Invoke(ProjectedX,ProjectedY);
        });
    }
    void ITransformProvider.Resize(double Width,double Height)=>throw new InvalidOperationException("Shader Graph nodes cannot be resized through UI Automation.");
    void ITransformProvider.Rotate(double Degrees)=>throw new InvalidOperationException("Shader Graph nodes cannot be rotated through UI Automation.");
}
