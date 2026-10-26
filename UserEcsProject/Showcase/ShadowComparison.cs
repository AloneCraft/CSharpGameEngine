using CSharpGameEngine.Core;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Input;
using CSharpGameEngine.Runtime.Lighting;
using CSharpGameEngine.Runtime.Windows;
using CSharpGameEngine.Rendering;
namespace UserEcsProject.Showcase;
public struct ShadowComparisonControls { public bool Configured; }
public struct ShadowComparisonCamera { }
internal static class ShadowComparisonInput {
    internal static readonly InputContextId Context=new(StableId.Parse("0991ddf4-048b-4c99-a7ae-cd53b2046a28"));
    internal static readonly InputActionId Pause=new(StableId.Parse("78d67990-553e-42ed-9d71-01572bdc387d"));
    internal static readonly InputActionId Reset=new(StableId.Parse("881944cd-9ebc-4f21-bc1a-7b671e13fc4e"));
    internal static readonly InputActionId Left=new(StableId.Parse("2e9be669-706d-4b28-a18e-17fa0da19c70"));
    internal static readonly InputActionId Right=new(StableId.Parse("e3ecbdf9-4388-41f6-8fac-2ae77817612a"));
    internal static readonly InputActionId Forward=new(StableId.Parse("2bb430fa-6032-4547-9b57-5cae82f553a9"));
    internal static readonly InputActionId Back=new(StableId.Parse("29e83b4e-43ba-49a1-914d-ad4cc1b11ca4"));
    internal static readonly InputActionId Csm=new(StableId.Parse("bb7fd592-624d-4bbc-8278-a71b026c622e"));
    internal static readonly InputActionId Virtual=new(StableId.Parse("1e8407fd-4123-41fa-8b23-b45ef44aff4a"));
    internal static readonly InputActionId Both=new(StableId.Parse("57c0d2be-bd58-45fe-a19b-fe95c622691a"));
    internal static readonly InputActionId Soft=new(StableId.Parse("5813b3c1-60d0-4d7c-ab5c-6ab136c60b41"));
    internal static readonly InputActionId SmallerSun=new(StableId.Parse("743bcaae-7330-40f4-93e5-d54ec35bd8cb"));
    internal static readonly InputActionId LargerSun=new(StableId.Parse("97eea871-389f-42b2-a66d-dd9d49f8a5cf"));
    internal static InputServer Create(){
        InputBinding Bind(InputActionId Action,string Key)=>new(Action,new InputControl(InputDeviceKind.Keyboard,Key));
        var Server=new InputServer(new InputActionMap([new InputContext(Context,"Shadow comparison",100,[Bind(Pause,"P"),Bind(Reset,"R"),Bind(Left,"Left"),Bind(Right,"Right"),Bind(Forward,"Up"),Bind(Back,"Down"),Bind(Csm,"1"),Bind(Virtual,"2"),Bind(Both,"3"),Bind(Soft,"S"),Bind(SmallerSun,"4"),Bind(LargerSun,"5")])]));
        Server.SetContextActive(Context,true,default);return Server;
    }
}
[SystemPhase(SystemPhase.FrameInput)]
[RunsAfter(typeof(Window))]
internal struct ShadowComparisonControlSystem:ISystem {
    public void Update(ref ShadowComparisonControls Controls,ref DirectionalShadowRendering Settings,EngineHost Host,ref State State){
        if(!Controls.Configured){ Host.SetInput(ShadowComparisonInput.Create());Controls.Configured=true; }
        foreach(var Action in Host.Input.CurrentFrame){
            if(Action.Phase!=InputActionPhase.Started){ continue; }
            if(Action.ActionId==ShadowComparisonInput.Pause){ if(State.PlaybackState==SimulationPlaybackState.Running){ State.PauseSimulation(); }else{ State.ResumeSimulation(); } }
            if(Action.ActionId==ShadowComparisonInput.Csm){ Settings.Mode=DirectionalShadowMode.Cascaded;Settings.Compare=false; }
            if(Action.ActionId==ShadowComparisonInput.Virtual){ Settings.Mode=DirectionalShadowMode.Virtual;Settings.Compare=false; }
            if(Action.ActionId==ShadowComparisonInput.Both){ Settings.Mode=DirectionalShadowMode.Virtual;Settings.Compare=true; }
            if(Action.ActionId==ShadowComparisonInput.Soft){ Settings.SoftShadows=true;Settings.SourceAngularDiameterDegrees=Settings.SourceAngularDiameterDegrees>0f?0f:2f; }
            if(Action.ActionId==ShadowComparisonInput.SmallerSun){ Settings.SoftShadows=true;Settings.SourceAngularDiameterDegrees=MathF.Max(0f,Settings.SourceAngularDiameterDegrees-.5f); }
            if(Action.ActionId==ShadowComparisonInput.LargerSun){ Settings.SoftShadows=true;Settings.SourceAngularDiameterDegrees=MathF.Min(10f,Settings.SourceAngularDiameterDegrees+.5f); }
        }
    }
    public void Update(ref Transform Transform,ref ShadowComparisonCamera Camera,EngineHost Host,ref State State){
        foreach(var Action in Host.Input.CurrentFrame){
            if(Action.Phase!=InputActionPhase.Started){ continue; }
            if(Action.ActionId==ShadowComparisonInput.Reset){ Transform.Position=new(0,9,-18); }
            var X=Action.ActionId==ShadowComparisonInput.Left?-1:Action.ActionId==ShadowComparisonInput.Right?1:0;
            var Z=Action.ActionId==ShadowComparisonInput.Forward?1:Action.ActionId==ShadowComparisonInput.Back?-1:0;
            Transform.Position+=new LogicalVector3(X,0,Z);
        }
    }
}
[SystemPhase(SystemPhase.Presentation)]
internal struct ShadowComparisonHud:ISystem {
    public void Update(ref ShadowComparisonControls Controls,ref DirectionalShadowRendering Settings,EngineHost Host,ScreenUi Ui,ref State State){
        var Stats=Host.Renderer?.VirtualShadowStatistics??default;
        var Title=Settings.Compare?"CSM (left) | Virtual (right)":Settings.Mode+" (single view)";
        Ui.Add(new ScreenText { Text=$"{Title}   P pause/play   Arrows camera   R reset   1 CSM / 2 Virtual / 3 Both\nMatched PCSS: sun diameter {Settings.SourceAngularDiameterDegrees:F2} deg   radius cap {Settings.MaximumSoftShadowRadiusMeters:F1} m   S hard/soft   4/5 sun size\nPages requested {Stats.Requested}   resident {Stats.Resident}   rendered {Stats.Rendered}   reused {Stats.Reused}   missing requests {Stats.Missing}\nGPU shadow passes: CSM {Host.Renderer?.CascadedShadowGpuMilliseconds:F3} ms   virtual pages {Host.Renderer?.VirtualShadowGpuMilliseconds:F3} ms (CSM fallback cost is additional)",Size=20,OffsetX=16,OffsetY=12,Layer=10 });
    }
}
