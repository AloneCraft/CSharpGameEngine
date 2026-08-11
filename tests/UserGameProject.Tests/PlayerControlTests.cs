using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;
using UserGameProject.Systems;

namespace UserGameProject.Tests;

/// <summary>
/// PlayerControl.Update's body has no ECS dependency of its own - GameSystemGenerator
/// (ADR-0033) derives the Archetype and dispatch purely from its parameters - so these tests call it
/// directly with local ref arguments and never need real entities. A throwaway World/State is still
/// passed because every Update ends with a trailing `ref State`, but this method never touches it.
/// InputState is scripted exactly as Input would have written it, so no Win32Window is needed
/// either.
/// </summary>
public class PlayerControlTests
{
    private const float DeltaTime = 1f / 60f;

    private static void RunControl(ref Position position, ref Rotation rotation, ref VerticalMotion verticalMotion, ref CameraRig cameraRig, ref InputState inputState)
    {
        PlayerTag playerTag = default;
        FrameContext Frame = new(DeltaTime, 0f);
        World world = new();
        State state = new(world);
        PlayerControl system = new();

        system.Update(ref position, ref rotation, ref verticalMotion, ref cameraRig, ref inputState, ref playerTag, in Frame, ref state);
    }

    [Fact]
    public void Update_MoveForwardAtZeroYaw_MovesAlongPositiveZ()
    {
        Position position = new() { X = 0, Y = 0, Z = 0 };
        Rotation rotation = new() { X = 0, Y = 0, Z = 0, W = 1 };
        VerticalMotion verticalMotion = default;
        CameraRig cameraRig = default;
        InputState inputState = new() { MoveForward = true };

        RunControl(ref position, ref rotation, ref verticalMotion, ref cameraRig, ref inputState);

        Assert.Equal(0f, position.X, precision: 4);
        Assert.True(position.Z > 0f, $"Expected forward movement along +Z, got Z={position.Z}.");
    }

    [Fact]
    public void Update_LookDeltaX_AccumulatesCameraRigYaw()
    {
        Position position = new() { X = 0, Y = 0, Z = 0 };
        Rotation rotation = new() { X = 0, Y = 0, Z = 0, W = 1 };
        VerticalMotion verticalMotion = default;
        CameraRig cameraRig = default;
        InputState inputState = new() { LookDeltaX = 100f };

        RunControl(ref position, ref rotation, ref verticalMotion, ref cameraRig, ref inputState);

        Assert.True(cameraRig.Yaw > 0f, "Expected a positive mouse-X delta to increase Yaw.");
    }

    [Fact]
    public void Update_LookDeltaY_ClampsPitchToFirstPersonRange()
    {
        Position position = new() { X = 0, Y = 0, Z = 0 };
        Rotation rotation = new() { X = 0, Y = 0, Z = 0, W = 1 };
        VerticalMotion verticalMotion = default;
        CameraRig cameraRig = default;
        // A huge upward look (negative Y delta pitches up, matching Camera.ApplyMouseDelta's convention).
        InputState inputState = new() { LookDeltaY = -1_000_000f };

        RunControl(ref position, ref rotation, ref verticalMotion, ref cameraRig, ref inputState);

        Assert.Equal(CSharpGameEngine.Graphics.D3D12.Camera.MaxPitchRadiansFirstPerson, cameraRig.Pitch, precision: 4);
    }

    [Fact]
    public void Update_RotationAlwaysMatchesCameraRigYaw_FirstPersonBodyFacesView()
    {
        Position position = new() { X = 0, Y = 0, Z = 0 };
        Rotation rotation = new() { X = 0, Y = 0, Z = 0, W = 1 };
        VerticalMotion verticalMotion = default;
        CameraRig cameraRig = default;
        InputState inputState = new() { LookDeltaX = 250f };

        RunControl(ref position, ref rotation, ref verticalMotion, ref cameraRig, ref inputState);

        Quaternion Expected = Quaternion.CreateFromAxisAngle(Vector3.UnitY, cameraRig.Yaw);
        Quaternion Actual = new(rotation.X, rotation.Y, rotation.Z, rotation.W);
        Assert.True(Quaternion.Dot(Expected, Actual) > 0.9999f, $"Expected Rotation to match CameraRig.Yaw exactly, expected={Expected}, actual={Actual}.");
    }

    [Fact]
    public void Update_JumpWhileGrounded_LaunchesUpwardMatchingCharacterMotion()
    {
        Position position = new() { X = 0, Y = 0, Z = 0 };
        Rotation rotation = new() { X = 0, Y = 0, Z = 0, W = 1 };
        VerticalMotion verticalMotion = default;
        CameraRig cameraRig = default;
        InputState inputState = new() { JumpPressed = true };

        RunControl(ref position, ref rotation, ref verticalMotion, ref cameraRig, ref inputState);

        float ExpectedVelocity = CharacterMotion.JumpSpeed - (CharacterMotion.Gravity / 60f);
        Assert.Equal(ExpectedVelocity, verticalMotion.Velocity, precision: 3);
        Assert.False(verticalMotion.IsGrounded);
    }

    [Fact]
    public void Update_NoInput_PlayerStaysPutAndGrounded()
    {
        Position position = new() { X = 1, Y = 0, Z = 2 };
        Rotation rotation = new() { X = 0, Y = 0, Z = 0, W = 1 };
        VerticalMotion verticalMotion = default;
        CameraRig cameraRig = default;
        InputState inputState = new();

        RunControl(ref position, ref rotation, ref verticalMotion, ref cameraRig, ref inputState);

        Assert.Equal(1f, position.X, precision: 4);
        Assert.Equal(0f, position.Y, precision: 4);
        Assert.Equal(2f, position.Z, precision: 4);
        Assert.True(verticalMotion.IsGrounded);
    }
}
