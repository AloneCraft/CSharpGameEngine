using System.Diagnostics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Generated;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Platform.Windows;
using UserGameProject;
using UserGameProject.Systems;

// UE5.8 Lvl_FirstPerson stage port (M0004, ADR-0017/ADR-0018): SceneLoader turns the exported GLB
// + scene_meta.json into StageRenderer inputs and a set of ECS spawns.
//
// Everything ECS-shaped now lives in Components.cs + Systems/Systems.cs (ADR-0033): even the
// startup spawning of props/lights/player is a System (SceneSpawn), reached by creating a
// single SceneSpawnRequest entity below. No generated Archetype identifier appears anywhere in
// hand-written code - World.Create's overloads are resolved by the component types alone.
//
// What stays here is only what the generator cannot know: window/renderer/scene-file setup, and
// the per-tick calls of this particular engine (PumpEvents / FrameContext / RenderFrame). That is
// also why this loop calls the generated RunFrame per frame rather than the generated Run, whose
// own while-loop would freeze the Context arguments for its entire duration.

const string ProductDisplayName="C# Game Engine";

var ShaderPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.hlsl");
var GlbPath=Path.Combine(AppContext.BaseDirectory,"Assets","Stage","Lvl_FirstPerson","Lvl_FirstPerson.glb");
var SceneMetaPath=Path.Combine(AppContext.BaseDirectory,"Assets","Stage","Lvl_FirstPerson","scene_meta.json");

LoadedScene Scene=SceneLoader.Load(GlbPath,SceneMetaPath);

using Win32Window Window=new(new Win32WindowOptions {
    Title= $"{ProductDisplayName} - FirstPerson Stage",
    Width=1280,
    Height=720,
    Visible=true,
});

GraphicsDeviceOptions DeviceOptions=new();

using StageRenderer Renderer=StageRenderer.Create(
    Window.Handle,Window.ClientWidth,Window.ClientHeight,DeviceOptions,ShaderPath,
    Scene.Meshes,Scene.Materials,Scene.Objects);

Window.Resized+=(W,H)=>Renderer.Resize(W,H);

World world=new();

// The only entity created outside the ECS: SceneSpawn consumes it on the first frame and
// creates everything else (props, lights, player).
world.Create(new SceneSpawnRequest { Scene=Scene });

Window.SetMouseCaptured(true);

State state=new(world){ Enabled=true };

SceneSpawn SceneSpawnSystem=new();
Input InputSystem=new();
PlayerControl PlayerControlSystem=new();
UserGameProject.Systems.Camera CameraSystem=new();
MovingProp MovingPropSystem=new();
RenderSync RenderSyncSystem=new();

Stopwatch stopwatch=Stopwatch.StartNew();
var PreviousTicks=stopwatch.ElapsedTicks;
var TotalTime=0f;

while(!Window.IsClosed){
    Window.PumpEvents();
    if(Window.IsClosed||Window.IsMinimized){
        continue;
    }

    var CurrentTicks=stopwatch.ElapsedTicks;
    var DeltaTime=MathF.Min((float)((CurrentTicks-PreviousTicks)/(double)Stopwatch.Frequency),0.1f);
    PreviousTicks=CurrentTicks;
    TotalTime+=DeltaTime;

    FrameContext Frame=new(DeltaTime,TotalTime);

    GeneratedGameLoop.RunFrame(
        ref state,
        ref SceneSpawnSystem,
        ref InputSystem,
        ref PlayerControlSystem,
        ref CameraSystem,
        ref MovingPropSystem,
        ref RenderSyncSystem,
        Renderer,
        Window,
        in Frame);

    Renderer.RenderFrame();
}
