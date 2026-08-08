using System.Diagnostics;
using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Generated;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Platform.Windows;
using UserGameProject;
using UserGameProject.Systems;

// UE5.8 Lvl_FirstPerson stage port (M0004, ADR-0017/ADR-0018): SceneLoader turns the exported GLB
// + scene_meta.json into StageRenderer inputs and a set of ECS spawns. Every frame's ECS work -
// input, player control, camera, movable-prop animation, and pushing transforms/lighting into the
// renderer - now runs as the single GeneratedGameLoop.Update call (ADR-0029): GameSystemGenerator
// infers each [GameSystem] method's Query and Read/Write access from its ref/in parameters and
// derives the call order from Write->Read component dependencies, so there is no hand-written
// system call sequence left here (unlike the pre-migration loop this file used to have). Only
// window pump / camera hookup / RenderFrame stay outside the ECS, since they are not per-entity
// data.

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

EntityArchetype stagePropArchetype = world.CreateArchetype<Position,Rotation,Scale,RenderObjectRef>();
EntityArchetype movablePropArchetype = world.CreateArchetype<Position,Rotation,Scale,RenderObjectRef,Velocity,MovableTag>();

foreach(LoadedSceneSpawn spawn in scene.Spawns) {
    Rotation rotation = new() { X=spawn.Rotation.X,Y=spawn.Rotation.Y,Z=spawn.Rotation.Z,W=spawn.Rotation.W };
    Position position = new() { X=spawn.Position.X,Y=spawn.Position.Y,Z=spawn.Position.Z };
    Scale scaleComponent = new() { X=spawn.Scale.X,Y=spawn.Scale.Y,Z=spawn.Scale.Z };
    RenderObjectRef renderObjectRef = new() { Index=spawn.ObjectIndex };

    Entity prop = world.CreateEntity(spawn.Mobility==PropMobility.Movable ? movablePropArchetype : stagePropArchetype);
    world.GetRef<Position>(prop)=position;
    world.GetRef<Rotation>(prop)=rotation;
    world.GetRef<Scale>(prop)=scaleComponent;
    world.GetRef<RenderObjectRef>(prop)=renderObjectRef;
}

if(scene.Sun is { } sun) {
    Entity sunEntity = world.CreateEntity(world.CreateArchetype<DirectionalLightData>());
    world.GetRef<DirectionalLightData>(sunEntity)=sun;
}
if(scene.Ambient is { } ambient) {
    Entity ambientEntity = world.CreateEntity(world.CreateArchetype<AmbientLightData>());
    world.GetRef<AmbientLightData>(ambientEntity)=ambient;
}
if(scene.Fog is { } fog) {
    Entity fogEntity = world.CreateEntity(world.CreateArchetype<FogData>());
    world.GetRef<FogData>(fogEntity)=fog;
}

// Camera lives on the player entity itself (Systems/CameraSystem.cs) rather than a Velocity
// component, which nothing ever read even before this migration - dropped so Player's spawned
// component set stays within CreateArchetype's 8-type-parameter arity.
EntityArchetype playerArchetype = world.CreateArchetype<Position, Rotation, Health, VerticalMotion, CameraRig, InputState, PlayerTag, Camera>();
Entity player = world.CreateEntity(playerArchetype);
world.GetRef<Position>(player) = new Position { X = scene.PlayerStartPosition.X, Y = scene.PlayerStartPosition.Y, Z = scene.PlayerStartPosition.Z };
world.GetRef<Rotation>(player) = new Rotation { X = 0, Y = 0, Z = 0, W = 1 };
world.GetRef<Health>(player).Value = 100;
world.GetRef<CameraRig>(player) = new CameraRig { Yaw = scene.PlayerStartYawRadians, Pitch = 0f };

Position playerStart = world.GetRef<Position>(player);
world.GetRef<Camera>(player) = Camera.CreateFirstPerson(new Vector3(playerStart.X, playerStart.Y + CameraSystem.EyeHeight, playerStart.Z));

window.SetMouseCaptured(true);

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
    GeneratedGameLoop.Update(world, stageRenderer: renderer, win32Window: window, frameContext: frame);

    renderer.RenderFrame();
}
