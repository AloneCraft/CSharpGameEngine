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
// startup spawning of props/lights/player is a System (SceneSpawnSystem), reached by creating a
// single SceneSpawnRequest entity below. No generated Archetype identifier appears anywhere in
// hand-written code - World.Create's overloads are resolved by the component types alone.
//
// What stays here is only what the generator cannot know: window/renderer/scene-file setup, and
// the per-tick calls of this particular engine (PumpEvents / FrameContext / RenderFrame). That is
// also why this loop calls the generated RunFrame per frame rather than the generated Run, whose
// own while-loop would freeze the Context arguments for its entire duration.

const string ProductDisplayName = "C# Game Engine";

string shaderPath = Path.Combine(AppContext.BaseDirectory, "Shaders", "Stage.hlsl");
string glbPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Stage", "Lvl_FirstPerson", "Lvl_FirstPerson.glb");
string sceneMetaPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Stage", "Lvl_FirstPerson", "scene_meta.json");

LoadedScene scene = SceneLoader.Load(glbPath, sceneMetaPath);

using Win32Window window = new(new Win32WindowOptions
{
    Title = $"{ProductDisplayName} - FirstPerson Stage",
    Width = 1280,
    Height = 720,
    Visible = true,
});

GraphicsDeviceOptions deviceOptions = new();

using StageRenderer renderer = StageRenderer.Create(
    window.Handle,window.ClientWidth,window.ClientHeight,deviceOptions,shaderPath,
    scene.Meshes,scene.Materials,scene.Objects);

window.Resized+=(w,h) => renderer.Resize(w,h);

World world = new();

// The only entity created outside the ECS: SceneSpawnSystem consumes it on the first frame and
// creates everything else (props, lights, player).
world.Create(new SceneSpawnRequest { Scene = scene });

window.SetMouseCaptured(true);

State state = new(world) { Enabled = true };

SceneSpawnSystem sceneSpawnSystem = new();
InputSystem inputSystem = new();
PlayerControlSystem playerControlSystem = new();
CameraSystem cameraSystem = new();
MovingPropSystem movingPropSystem = new();
RenderSyncSystem renderSyncSystem = new();

Stopwatch stopwatch = Stopwatch.StartNew();
long previousTicks = stopwatch.ElapsedTicks;
float totalTime = 0f;

while (!window.IsClosed)
{
    window.PumpEvents();
    if (window.IsClosed || window.IsMinimized)
    {
        continue;
    }

    long currentTicks = stopwatch.ElapsedTicks;
    float deltaTime = MathF.Min((float)((currentTicks - previousTicks) / (double)Stopwatch.Frequency), 0.1f);
    previousTicks = currentTicks;
    totalTime += deltaTime;

    FrameContext frame = new(deltaTime, totalTime);

    GeneratedGameLoop.RunFrame(
        ref state,
        ref sceneSpawnSystem,
        ref inputSystem,
        ref playerControlSystem,
        ref cameraSystem,
        ref movingPropSystem,
        ref renderSyncSystem,
        renderer,
        window,
        in frame);

    renderer.RenderFrame();
}
