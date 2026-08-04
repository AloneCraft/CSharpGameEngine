using System.Diagnostics;
using System.Numerics;
using CSharpGameEngine.Ecs;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Platform.Windows;
using UserGameProject;
using UserGameProject.Systems;
using World = CSharpGameEngine.Ecs.World;

// UE5.8 Lvl_FirstPerson stage port (M0004, ADR-0017/ADR-0018): SceneLoader turns the exported GLB
// + scene_meta.json into StageRenderer inputs and a set of ECS spawns, then every frame's work -
// input, player control, camera, movable-prop animation, and pushing transforms/lighting into the
// renderer - runs as an explicit sequence of plain systems (Systems/*.cs). There is no scheduler;
// call order here *is* the update order, following the same Window+Renderer+loop composition
// already used by tools/CSharpGameEngine.Extension/Program.cs and the earlier ThirdPerson slice.

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
    window.Handle, window.ClientWidth, window.ClientHeight, deviceOptions, shaderPath,
    scene.Meshes, scene.Materials, scene.Objects);

window.Resized += (w, h) => renderer.Resize(w, h);

World world = new();

List<StageProp> stageProps = [];
List<MovableStageProp> movableProps = [];

foreach (LoadedSceneSpawn spawn in scene.Spawns)
{
    Rotation rotation = new() { X = spawn.Rotation.X, Y = spawn.Rotation.Y, Z = spawn.Rotation.Z, W = spawn.Rotation.W };
    Position position = new() { X = spawn.Position.X, Y = spawn.Position.Y, Z = spawn.Position.Z };
    Scale scaleComponent = new() { X = spawn.Scale.X, Y = spawn.Scale.Y, Z = spawn.Scale.Z };
    RenderObjectRef renderObjectRef = new() { Index = spawn.ObjectIndex };

    if (spawn.Mobility == PropMobility.Movable)
    {
        MovableStageProp prop = world.Create<MovableStageProp>();
        prop.Position = position;
        prop.Rotation = rotation;
        prop.Scale = scaleComponent;
        prop.RenderObjectRef = renderObjectRef;
    }
    else
    {
        StageProp prop = world.Create<StageProp>();
        prop.Position = position;
        prop.Rotation = rotation;
        prop.Scale = scaleComponent;
        prop.RenderObjectRef = renderObjectRef;
    }
}

world.CollectViews(stageProps);
world.CollectViews(movableProps);

if (scene.Sun is { } sun)
{
    world.Create<SunLight>().DirectionalLightData = sun;
}
if (scene.Ambient is { } ambient)
{
    world.Create<AmbientLight>().AmbientLightData = ambient;
}
if (scene.Fog is { } fog)
{
    world.Create<FogVolume>().FogData = fog;
}

List<SunLight> sunLights = [];
List<AmbientLight> ambientLights = [];
List<FogVolume> fogVolumes = [];
world.CollectViews(sunLights);
world.CollectViews(ambientLights);
world.CollectViews(fogVolumes);

Player player = world.Create<Player>();
player.Position = new Position { X = scene.PlayerStartPosition.X, Y = scene.PlayerStartPosition.Y, Z = scene.PlayerStartPosition.Z };
player.Rotation = new Rotation { X = 0, Y = 0, Z = 0, W = 1 };
player.Health.Value = 100;
player.CameraRig = new CameraRig { Yaw = scene.PlayerStartYawRadians, Pitch = 0f };

Camera camera = Camera.CreateFirstPerson(new Vector3(player.Position.X, player.Position.Y + CameraSystem.EyeHeight, player.Position.Z));

window.SetMouseCaptured(true);

Stopwatch stopwatch = Stopwatch.StartNew();
long previousTicks = stopwatch.ElapsedTicks;
bool wasJumpKeyDown = false;
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

    InputSystem.Update(window, ref player.InputState, ref wasJumpKeyDown);
    PlayerControlSystem.Update(player, deltaTime);
    CameraSystem.Update(player, ref camera);
    MovingPropSystem.Update(movableProps, totalTime);

    renderer.SetCamera(camera);
    RenderSyncSystem.SyncProps(stageProps, renderer);
    RenderSyncSystem.SyncMovableProps(movableProps, renderer);
    RenderSyncSystem.SyncLighting(sunLights, ambientLights, fogVolumes, renderer);

    renderer.RenderFrame();
}
