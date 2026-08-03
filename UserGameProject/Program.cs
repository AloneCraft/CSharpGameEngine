using System.Diagnostics;
using System.Numerics;
using CSharpGameEngine.Ecs;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Platform.Windows;
using UserGameProject;

// ThirdPerson-style ECS-driven stage vertical slice (ADR-0016): a ground plane and a player
// character are created as ECS entities via CSharpGameEngine.Ecs, then rendered every frame with
// StageRenderer while a mouse-orbit camera and WASD/gravity/jump character controller (see
// PlayerController.cs) update the player's Position/Rotation components. Follows the same
// Window+Renderer+loop composition pattern as tools/CSharpGameEngine.Extension/Program.cs.

const string ProductDisplayName = "C# Game Engine";
const int GroundObjectIndex = 0;
const int PlayerObjectIndex = 1;
const int VkSpace = 0x20;

string shaderPath = Path.Combine(AppContext.BaseDirectory, "Shaders", "Stage.hlsl");

using Win32Window window = new(new Win32WindowOptions
{
    Title = $"{ProductDisplayName} - Stage",
    Width = 1280,
    Height = 720,
    Visible = true,
});

GraphicsDeviceOptions deviceOptions = new();

ProceduralMeshData groundMesh = ProceduralMeshFactory.CreatePlane(width: 40f, depth: 40f);
ProceduralMeshData characterMesh = ProceduralMeshFactory.CreateBox(new Vector3(1f, 2f, 1f));

using StageRenderer renderer = StageRenderer.Create(
    window.Handle, window.ClientWidth, window.ClientHeight, deviceOptions, shaderPath,
    [
        new MeshCreateInfo { Positions = groundMesh.Positions, Normals = groundMesh.Normals, Indices = groundMesh.Indices, BaseColorFactor = new Vector4(0.3f, 0.55f, 0.3f, 1f) },
        new MeshCreateInfo { Positions = characterMesh.Positions, Normals = characterMesh.Normals, Indices = characterMesh.Indices, BaseColorFactor = new Vector4(0.75f, 0.25f, 0.2f, 1f) },
    ]);

window.Resized += (w, h) => renderer.Resize(w, h);

World world = new();

StaticProp ground = world.Create<StaticProp>();
ground.Position = new Position { X = 0, Y = 0, Z = 0 };
ground.Rotation = new Rotation { X = 0, Y = 0, Z = 0, W = 1 };

Player player = world.Create<Player>();
player.Position = new Position { X = 0, Y = 0, Z = 0 };
player.Rotation = new Rotation { X = 0, Y = 0, Z = 0, W = 1 };
player.Health.Value = 100;

Camera camera = Camera.CreateDefault(new Vector3(player.Position.X, player.Position.Y + 1.6f, player.Position.Z));
float currentYaw = 0f;
float verticalVelocity = 0f;
bool wasJumpKeyDown = false;

window.SetMouseCaptured(true);

Stopwatch stopwatch = Stopwatch.StartNew();
long previousTicks = stopwatch.ElapsedTicks;

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

    (float mouseDeltaX, float mouseDeltaY) = window.ConsumeMouseDelta();
    camera.ApplyMouseDelta(mouseDeltaX, mouseDeltaY, yawSensitivity: 0.0025f, pitchSensitivity: 0.0025f);

    bool jumpKeyDown = window.IsKeyDown(VkSpace);
    bool jumpPressed = jumpKeyDown && !wasJumpKeyDown;
    wasJumpKeyDown = jumpKeyDown;

    CharacterMotionResult motion = CharacterMotion.Compute(new CharacterMotionInput
    {
        FlatForward = camera.GetFlatForward(),
        FlatRight = camera.GetFlatRight(),
        MoveForward = window.IsKeyDown('W'),
        MoveBack = window.IsKeyDown('S'),
        MoveLeft = window.IsKeyDown('A'),
        MoveRight = window.IsKeyDown('D'),
        JumpPressed = jumpPressed,
        CurrentY = player.Position.Y,
        VerticalVelocity = verticalVelocity,
        DeltaTime = deltaTime,
    });

    verticalVelocity = motion.NewVerticalVelocity;

    player.Position = new Position
    {
        X = player.Position.X + (motion.HorizontalVelocity.X * deltaTime),
        Y = motion.NewY,
        Z = player.Position.Z + (motion.HorizontalVelocity.Z * deltaTime),
    };

    if (motion.HorizontalVelocity != Vector3.Zero)
    {
        float targetYaw = MathF.Atan2(motion.HorizontalVelocity.X, motion.HorizontalVelocity.Z);
        currentYaw = CharacterMotion.LerpAngle(currentYaw, targetYaw, rotationSpeedRadiansPerSecond: 12f, deltaTime);

        Quaternion rotationQuaternion = Quaternion.CreateFromAxisAngle(Vector3.UnitY, currentYaw);
        player.Rotation = new Rotation { X = rotationQuaternion.X, Y = rotationQuaternion.Y, Z = rotationQuaternion.Z, W = rotationQuaternion.W };
    }

    camera.Target = new Vector3(player.Position.X, player.Position.Y + 1.6f, player.Position.Z);
    renderer.SetCamera(camera);

    renderer.SetObjectTransform(GroundObjectIndex, Matrix4x4.CreateTranslation(ground.Position.X, ground.Position.Y, ground.Position.Z));

    Quaternion playerRotation = new(player.Rotation.X, player.Rotation.Y, player.Rotation.Z, player.Rotation.W);
    Matrix4x4 playerWorld = Matrix4x4.CreateFromQuaternion(playerRotation) * Matrix4x4.CreateTranslation(player.Position.X, player.Position.Y + 1f, player.Position.Z);
    renderer.SetObjectTransform(PlayerObjectIndex, playerWorld);

    renderer.RenderFrame();
}
