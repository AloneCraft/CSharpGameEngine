using System.Numerics;
using CSharpGameEngine.Graphics.D3D12;
using UserGameProject.Systems;

namespace UserGameProject.Tests;

/// <summary>
/// PlayerControlSystem.Control is a plain static method that GameSystemGenerator (ADR-0029) wraps
/// in a Query/SoA loop purely from its ref/in parameters - the method body itself has no ECS
/// dependency, so these tests call it directly with local ref/in arguments and never need a World
/// or Entity. InputState is scripted exactly as Systems/InputSystem.cs would have written it, so
/// these tests never need a Win32Window either.
/// </summary>
public class PlayerControlSystemTests
{
    private const float DeltaTime = 1f / 60f;

    private static void RunControl(ref Position position, ref Rotation rotation, ref VerticalMotion verticalMotion, ref CameraRig cameraRig, in InputState inputState)
    {
        PlayerTag playerTag = default;
        FrameContext frame = new(DeltaTime, 0f);
        PlayerControlSystem.Control(ref position, ref rotation, ref verticalMotion, ref cameraRig, in inputState, in playerTag, in frame);
    }

    [Fact]
    public void Update_MoveForwardAtZeroYaw_MovesAlongPositiveZ()
    {
        Position position = new() { X = 0, Y = 0, Z = 0 };
        Rotation rotation = new() { X = 0, Y = 0, Z = 0, W = 1 };
        VerticalMotion verticalMotion = default;
        CameraRig cameraRig = default;
        InputState inputState = new() { MoveForward = true };

        RunControl(ref position, ref rotation, ref verticalMotion, ref cameraRig, in inputState);

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

        RunControl(ref position, ref rotation, ref verticalMotion, ref cameraRig, in inputState);

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

        RunControl(ref position, ref rotation, ref verticalMotion, ref cameraRig, in inputState);

        Assert.Equal(Camera.MaxPitchRadiansFirstPerson, cameraRig.Pitch, precision: 4);
    }

    [Fact]
    public void Update_RotationAlwaysMatchesCameraRigYaw_FirstPersonBodyFacesView()
    {
        Position position = new() { X = 0, Y = 0, Z = 0 };
        Rotation rotation = new() { X = 0, Y = 0, Z = 0, W = 1 };
        VerticalMotion verticalMotion = default;
        CameraRig cameraRig = default;
        InputState inputState = new() { LookDeltaX = 250f };

        RunControl(ref position, ref rotation, ref verticalMotion, ref cameraRig, in inputState);

        Quaternion expected = Quaternion.CreateFromAxisAngle(Vector3.UnitY, cameraRig.Yaw);
        Quaternion actual = new(rotation.X, rotation.Y, rotation.Z, rotation.W);
        Assert.True(Quaternion.Dot(expected, actual) > 0.9999f, $"Expected Rotation to match CameraRig.Yaw exactly, expected={expected}, actual={actual}.");
    }

    [Fact]
    public void Update_JumpWhileGrounded_LaunchesUpwardMatchingCharacterMotion()
    {
        Position position = new() { X = 0, Y = 0, Z = 0 };
        Rotation rotation = new() { X = 0, Y = 0, Z = 0, W = 1 };
        VerticalMotion verticalMotion = default;
        CameraRig cameraRig = default;
        InputState inputState = new() { JumpPressed = true };

        RunControl(ref position, ref rotation, ref verticalMotion, ref cameraRig, in inputState);

        float expectedVelocity = CharacterMotion.JumpSpeed - (CharacterMotion.Gravity / 60f);
        Assert.Equal(expectedVelocity, verticalMotion.Velocity, precision: 3);
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

        RunControl(ref position, ref rotation, ref verticalMotion, ref cameraRig, in inputState);

        Assert.Equal(1f, position.X, precision: 4);
        Assert.Equal(0f, position.Y, precision: 4);
        Assert.Equal(2f, position.Z, precision: 4);
        Assert.True(verticalMotion.IsGrounded);
    }
}
