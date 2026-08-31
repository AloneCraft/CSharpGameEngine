using System.Globalization;
using CSharpGameEngine.Core;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Input;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Runtime.Windows;
namespace UserEcsProject.Showcase;
[SystemOrder(2)]
file sealed class Order;
[SystemPhase(SystemPhase.FrameInput)]
internal struct ShowcaseControls:ISystem {
    public bool Update(ref AppHostRuntime AppRuntime,ref EngineShowcaseRuntime Runtime,ref State State){
        var Session=Runtime.Session??=new ShowcaseSession();
        if(!Session.InputConfigured){
            AppRuntime.Input=ShowcaseInput.CreateServer();
            Session.InputConfigured=true;
            return true;
        }
        if(AppRuntime.Input is not { }Input){ return true; }
        foreach(var Action in Input.CurrentFrame.Where(static Action=>Action.Phase==InputActionPhase.Started)){
            if(Action.ActionId==ShowcaseInput.EcsPage){ this.SelectPage(Session,1,State.FixedTick); }
            else if(Action.ActionId==ShowcaseInput.RenderingPage){ this.SelectPage(Session,2,State.FixedTick); }
            else if(Action.ActionId==ShowcaseInput.PhysicsPage){ this.SelectPage(Session,3,State.FixedTick); }
            else if(Action.ActionId==ShowcaseInput.ProgressPage){ this.SelectPage(Session,4,State.FixedTick); }
            else if(Action.ActionId==ShowcaseInput.Pause){
                if(State.PlaybackState==SimulationPlaybackState.Running){ State.PauseSimulation(); }
                else{ State.ResumeSimulation(); }
                Session.NextAutomaticPageTick=State.FixedTick+600;
            } else if(Action.ActionId==ShowcaseInput.Step){
                State.StepSimulation();
                Session.NextAutomaticPageTick=State.FixedTick+600;
            } else if(Action.ActionId==ShowcaseInput.TogglePanel){
                Session.PanelVisible=!Session.PanelVisible;
                Session.Panel?.SetVisible(Session.PanelVisible);
                Session.NextAutomaticPageTick=State.FixedTick+600;
            } else if(Action.ActionId==ShowcaseInput.LayerUp){
                Session.Page=4;
                Session.ProgressLayer=Math.Max(0,Session.ProgressLayer-1);
                Session.ProgressOffset=0;
                Session.NextAutomaticPageTick=State.FixedTick+600;
            } else if(Action.ActionId==ShowcaseInput.LayerDown){
                Session.Page=4;
                Session.ProgressLayer=Math.Min(9,Session.ProgressLayer+1);
                Session.ProgressOffset=0;
                Session.NextAutomaticPageTick=State.FixedTick+600;
            } else if(Action.ActionId==ShowcaseInput.Previous){
                Session.Page=4;
                Session.ProgressOffset=Math.Max(0,Session.ProgressOffset-4);
                Session.NextAutomaticPageTick=State.FixedTick+600;
            } else if(Action.ActionId==ShowcaseInput.Next){
                Session.Page=4;
                Session.ProgressOffset+=4;
                Session.NextAutomaticPageTick=State.FixedTick+600;
            }
        }
        if(State.PlaybackState==SimulationPlaybackState.Running&&State.FixedTick>=Session.NextAutomaticPageTick){
            Session.Page=Session.Page%4+1;
            Session.NextAutomaticPageTick=State.FixedTick+600;
        }
        return true;
    }
    private void SelectPage(ShowcaseSession Session,int Page,ulong Tick){
        Session.Page=Page;
        Session.NextAutomaticPageTick=Tick+600;
    }
}
[SystemPhase(SystemPhase.FixedSimulation)]
internal struct ShowcaseFixedMotion:ISystem {
    public bool Update(ref Transform Transform,ref ShowcaseMotion Motion,ref State State){
        var Period=Math.Max(4,Motion.PeriodTicks);
        var Half=Period/2;
        var Phase=(int)((State.FixedTick+(ulong)Math.Max(0,Motion.PhaseTicks))%(ulong)Period);
        var Triangle=Phase<=Half?Phase:Period-Phase;
        var CenteredMillimeters=((Triangle*2-Half)*Motion.AmplitudeMillimeters)/Math.Max(1,Half);
        Transform.Position=new Logical.Vector3(
            Motion.BaseX+CenteredMillimeters/1000.0,Motion.BaseY,Motion.BaseZ);
        return true;
    }
}
[SystemPhase(SystemPhase.Presentation)]
internal struct ShowcaseDashboard:ISystem {
    public bool Update(
        ref AppHostRuntime AppRuntime,ref LevelRuntime LevelRuntime,
        ref EngineShowcaseRuntime Runtime,ref State State){
        if(AppRuntime.Window is not { }Window){ return true; }
        var Session=Runtime.Session??=new ShowcaseSession();
        Session.Locale=ResolveLocale();
        Session.Manifest??=ShowcaseManifest.Load(Path.Combine(AppContext.BaseDirectory,"Showcase","feature-progress.json"));
        Session.Panel??=new Win32DashboardPanel(Window.Handle);
        Session.Panel.Resize();
        if(Session.Panel.Visible!=Session.PanelVisible){ Session.Panel.SetVisible(Session.PanelVisible); }
        Session.Panel.Update(BuildDocument(Session,LevelRuntime,State));
        var EvidenceDirectory=Environment.GetEnvironmentVariable("CGE_SHOWCASE_EVIDENCE_DIR");
        if(!string.IsNullOrWhiteSpace(EvidenceDirectory)){
            var Request=Path.Combine(EvidenceDirectory,"dashboard-capture.request");
            if(File.Exists(Request)){
                Session.Panel.SaveSnapshot(Path.Combine(EvidenceDirectory,"dashboard-panel.bmp"));
                File.Move(Request,Request+".done",true);
            }
        }
        return true;
    }
    public void Shutdown(ref EngineShowcaseRuntime Runtime,ref State State){
        Runtime.Session?.Dispose();
        Runtime.Session=null;
    }
    private static DashboardDocument BuildDocument(ShowcaseSession Session,LevelRuntime LevelRuntime,State State){
        var Japanese=Session.Locale=="ja-JP";
        var Statistics=State.World.GetStatistics();
        var Playback=State.PlaybackState==SimulationPlaybackState.Running?"running":"paused";
        var PageName=Session.Page switch {1=>"ecs",2=>"rendering",3=>"physics",_=>"progress"};
        var Manifest=Session.Manifest!;
        List<DashboardLine>Lines=[
            new(Japanese?"固定Tick":"Fixed tick",State.FixedTick.ToString(CultureInfo.InvariantCulture)),
            new(Japanese?"再生状態":"Playback",Playback),
            new(Japanese?"補間率":"Interpolation",State.InterpolationAlpha.ToString("0.000",CultureInfo.InvariantCulture)),
            new(Japanese?"生存Entity":"Alive entities",Statistics.AliveEntityCount.ToString(CultureInfo.InvariantCulture)),
        ];
        List<DashboardBar>Bars=[];
        var Subtitle=Session.Page switch {
            1=>Japanese?"1  ECS・決定的固定tick":"1  ECS deterministic fixed tick",
            2=>Japanese?"2  Rendering・Shader Artifact":"2  Rendering and shader artifact",
            3=>Japanese?"3  Physics・剛体runtime":"3  Physics rigid runtime",
            _=>Japanese?$"4  実装進捗・L{Session.ProgressLayer}":$"4  Feature progress · L{Session.ProgressLayer}",
        };
        if(Session.Page==1){
            Lines.Add(new DashboardLine(Japanese?"Entity slot":"Entity slots",Statistics.EntitySlotCount.ToString(CultureInfo.InvariantCulture)));
            Lines.Add(new DashboardLine(Japanese?"保留構造変更":"Pending changes",(Statistics.PendingDestroyCount+Statistics.PendingSceneUnloadCount).ToString(CultureInfo.InvariantCulture)));
            Lines.Add(new DashboardLine(Japanese?"動作波形":"Motion wave","integer triangle / 60 Hz"));
        } else if(Session.Page==2){
            var Renderer=LevelRuntime.Renderer;
            var Hash=Renderer?.ShaderSemanticHash??string.Empty;
            Lines.Add(new DashboardLine("Shader artifact",Renderer?.ShaderArtifactLoaded==true?"verified":"loading"));
            Lines.Add(new DashboardLine("Semantic hash",Hash.Length>12?Hash[..12]:Hash));
            Lines.Add(new DashboardLine("Hot reload",CSharpGameEngine.Graphics.D3D12.StageRenderer.ShaderHotReloadEnabled?"enabled":"release/off"));
            Lines.Add(new DashboardLine(Japanese?"診断":"Diagnostics",(Renderer?.ShaderArtifactDiagnostics.Count??0).ToString(CultureInfo.InvariantCulture)));
            Lines.Add(new DashboardLine("HDR / Bloom / ACES","active pipeline"));
        } else if(Session.Page==3){
            Lines.Add(new DashboardLine(Japanese?"剛体stack":"Rigid stack","3 dynamic bodies"));
            Lines.Add(new DashboardLine("CCD projectile","ContinuousStatic"));
            Lines.Add(new DashboardLine(Japanese?"地面collider":"Ground collider","static box"));
            Lines.Add(new DashboardLine(Japanese?"未完成展示":"Deferred demos","prefracture / cache hidden"));
        } else {
            Bars.Add(new DashboardBar(Japanese?"実装済Feature":"Implemented features",Manifest.Summary.Implemented,Manifest.Summary.FeatureTotal));
            Bars.Add(new DashboardBar(Japanese?"実行可能Stage":"Executable stages",Manifest.Summary.Executable,Manifest.Summary.StageTotal));
            Lines.Add(new DashboardLine("Partial / Specified",$"{Manifest.Summary.PartiallyImplemented} / {Manifest.Summary.Specified}"));
            Lines.Add(new DashboardLine("Deferred",Manifest.Summary.Deferred.ToString(CultureInfo.InvariantCulture)));
            var Layer=$"L{Session.ProgressLayer}";
            var Features=Manifest.Features.Where(Feature=>Feature.Layer==Layer).ToArray();
            var MaximumOffset=Math.Max(0,((Features.Length-1)/4)*4);
            Session.ProgressOffset=Math.Min(Session.ProgressOffset,MaximumOffset);
            foreach(var Feature in Features.Skip(Session.ProgressOffset).Take(4)){
                Lines.Add(new DashboardLine(Feature.Id,$"{Feature.Status}: {Feature.Name}"));
            }
        }
        var HashPrefix=Manifest.Hash.Length>12?Manifest.Hash[..12]:Manifest.Hash;
        var Machine=$"CGE|page={PageName}|tick={State.FixedTick}|playback={Playback}|manifest={HashPrefix}|layer=L{Session.ProgressLayer}|index={Session.ProgressOffset}";
        var Footer=Japanese?"1-4 ページ  P 一時停止  O 1tick  F1 表示":"1-4 pages  P pause  O step  F1 panel";
        return new DashboardDocument("C# Game Engine",Subtitle,Lines,Bars,Footer,Machine);
    }
    private static string ResolveLocale(){
        var Requested=Environment.GetEnvironmentVariable("CGE_SHOWCASE_LOCALE");
        if(string.Equals(Requested,"ja-JP",StringComparison.OrdinalIgnoreCase)){ return "ja-JP"; }
        if(string.Equals(Requested,"en-US",StringComparison.OrdinalIgnoreCase)){ return "en-US"; }
        return CultureInfo.CurrentUICulture.Name.StartsWith("ja",StringComparison.OrdinalIgnoreCase)?"ja-JP":"en-US";
    }
}
