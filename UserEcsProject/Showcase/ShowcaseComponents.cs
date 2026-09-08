using CSharpGameEngine.Entities;
using CSharpGameEngine.Platform.Windows;
namespace UserEcsProject.Showcase;
public struct EngineShowcase {
}
public struct ShowcaseMotion {
    public LogicalScalar BaseX;
    public LogicalScalar BaseY;
    public LogicalScalar BaseZ;
    public int AmplitudeMillimeters;
    public int PeriodTicks;
    public int PhaseTicks;
}
public struct EngineShowcaseRuntime:IRuntimeComponent<EngineShowcase> {
    public ShowcaseSession?Session;
}
public sealed class ShowcaseSession:IDisposable {
    public Win32DashboardPanel?Panel;
    public ShowcaseManifest?Manifest;
    public bool InputConfigured;
    public bool PanelVisible=true;
    public int Page=1;
    public int ProgressLayer;
    public int ProgressOffset;
    public ulong NextAutomaticPageTick=600;
    public string Locale="en-US";
    public void Dispose(){
        this.Panel?.Dispose();
        this.Panel=null;
    }
}
