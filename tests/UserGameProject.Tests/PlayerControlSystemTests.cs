using System.Numerics;
using CSharpGameEngine.Ecs;
using CSharpGameEngine.Graphics.D3D12;
using UserGameProject.Systems;

namespace UserGameProject.Tests;

/// <summary>
/// PlayerControlSystem is the bridge from the ECS input abstraction (InputState, written only by
/// Systems/InputSystem.cs - see its remarks) to the existing pure CharacterMotion math. These
/// tests script InputState directly, exactly as InputSystem would have written it, so they never
/// need a Win32Window.
/// </summary>
public class PlayerControlSystemTests
{
    private static Player CreatePlayerAt(CSharpGameEngine.Ecs.World world, float x, float y, float z)
    {
        Player player = world.Create<Player>();
        player.Position = new Position { X = x, Y = y, Z = z };
        player.Rotation = new Rotation { X = 0, Y = 0, Z = 0, W = 1 };
        return player;
    }

    [Fact]
    public void Update_MoveForwardAtZeroYaw_MovesAlongPositiveZ()
    {
        CSharpGameEngine.Ecs.World world = new();
        Player player = CreatePlayerAt(world, 0, 0, 0);
        player.InputState = new InputState { MoveForward = true };

        PlayerControlSystem.Update(player, deltaTime: 1f / 60f);

        Assert.Equal(0f, player.Position.X, precision: 4);
        Assert.True(player.Position.Z > 0f, $"Expected forward movement along +Z, got Z={player.Position.Z}.");
    }

    [Fact]
    public void Update_LookDeltaX_AccumulatesCameraRigYaw()
    {
        CSharpGameEngine.Ecs.World world = new();
        Player player = CreatePlayerAt(world, 0, 0, 0);
        player.InputState = new InputState { LookDeltaX = 100f };

        PlayerControlSystem.Update(player, deltaTime: 1f / 60f);

        Assert.True(player.CameraRig.Yaw > 0f, "Expected a positive mouse-X delta to increase Yaw.");
    }

    [Fact]
    public void Update_LookDeltaY_ClampsPitchToFirstPersonRange()
    {
        CSharpGameEngine.Ecs.World world = new();
        Player player = CreatePlayerAt(world, 0, 0, 0);
        // A huge upward look (negative Y delta pitches up, matching Camera.ApplyMouseDelta's convention).
        player.InputState = new InputState { LookDeltaY = -1_000_000f };

        PlayerControlSystem.Update(player, deltaTime: 1f / 60f);

        Assert.Equal(Camera.MaxPitchRadiansFirstPerson, player.CameraRig.Pitch, precision: 4);
    }

    [Fact]
    public void Update_RotationAlwaysMatchesCameraRigYaw_FirstPersonBodyFacesView()
    {
        CSharpGameEngine.Ecs.World world = new();
        Player player = CreatePlayerAt(world, 0, 0, 0);
        player.InputState = new InputState { LookDeltaX = 250f };

        PlayerControlSystem.Update(player, deltaTime: 1f / 60f);

        Quaternion expected = Quaternion.CreateFromAxisAngle(Vector3.UnitY, player.CameraRig.Yaw);
        Quaternion actual = new(player.Rotation.X, player.Rotation.Y, player.Rotation.Z, player.Rotation.W);
        Assert.True(Quaternion.Dot(expected, actual) > 0.9999f, $"Expected Rotation to match CameraRig.Yaw exactly, expected={expected}, actual={actual}.");
    }

    [Fact]
    public void Update_JumpWhileGrounded_LaunchesUpwardMatchingCharacterMotion()
    {
        CSharpGameEngine.Ecs.World world = new();
        Player player = CreatePlayerAt(world, 0, 0, 0);
        player.InputState = new InputState { JumpPressed = true };

        PlayerControlSystem.Update(player, deltaTime: 1f / 60f);

        float expectedVelocity = CharacterMotion.JumpSpeed - (CharacterMotion.Gravity / 60f);
        Assert.Equal(expectedVelocity, player.VerticalMotion.Velocity, precision: 3);
        Assert.False(player.VerticalMotion.IsGrounded);
    }

    [Fact]
    public void Update_NoInput_PlayerStaysPutAndGrounded()
    {
        CSharpGameEngine.Ecs.World world = new();
        Player player = CreatePlayerAt(world, 1, 0, 2);
        player.InputState = new InputState();

        PlayerControlSystem.Update(player, deltaTime: 1f / 60f);

        Assert.Equal(1f, player.Position.X, precision: 4);
        Assert.Equal(0f, player.Position.Y, precision: 4);
        Assert.Equal(2f, player.Position.Z, precision: 4);
        Assert.True(player.VerticalMotion.IsGrounded);
    }
}
