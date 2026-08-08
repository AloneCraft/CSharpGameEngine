using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Spatial.Abstractions;
using CSharpGameEngine.Spatial.Unity;

namespace UserGameProject.Systems;

/// <summary>
/// Bridges <see cref="InputState"/> + <see cref="CameraRig"/> into the existing pure character
/// motion math (<see cref="CharacterMotion"/>, unchanged from the third-person slice) and writes
/// the result back onto the player's Position/VerticalMotion/Rotation. First-person: the player
/// always faces the camera's yaw directly (there is no third-person "turn body toward movement"
/// animation concern), so the movement basis comes from CameraRig.Yaw rather than from the
/// previous frame's velocity direction.
/// </summary>
[GameSystem]
public static class PlayerControlSystem
{
    private const float MouseYawSensitivity = 0.0025f;
    private const float MousePitchSensitivity = 0.0025f;

    [Update]
    public static void Control(
        ref Position position,
        ref Rotation rotation,
        ref VerticalMotion verticalMotion,
        ref CameraRig cameraRig,
        in InputState inputState,
        in PlayerTag _,
        [Context] in FrameContext frame)
    {
        cameraRig.Yaw += inputState.LookDeltaX * MouseYawSensitivity;
        cameraRig.Pitch = Math.Clamp(
            cameraRig.Pitch - (inputState.LookDeltaY * MousePitchSensitivity),
            Camera.MinPitchRadiansFirstPerson,
            Camera.MaxPitchRadiansFirstPerson);

        // RUF boundary (ADR-0028): yaw-only facing, flat forward/right, and the resulting body
        // rotation are derived in RUF space (mirrors Camera.GetFlatForward/GetFlatRight, ADR-0026)
        // before converting back to the Vector3/Quaternion types CharacterMotion and the ECS
        // Rotation component still use.
        RufQuaternion facingRuf = RufQuaternion.FromAxisAngle(RufVector.Up, cameraRig.Yaw);
        RufVector flatForwardRuf = RufQuaternion.RotateVector(facingRuf, RufVector.Forward).Normalize();
        RufVector flatRightRuf = Spatial<UnityCoordinateConvention>.RightFromUpForward(RufVector.Up, flatForwardRuf);

        Vector3 flatForward = (Vector3)Spatial<UnityCoordinateConvention>.ToXyz(flatForwardRuf);
        Vector3 flatRight = (Vector3)Spatial<UnityCoordinateConvention>.ToXyz(flatRightRuf);

        CharacterMotionResult motion = CharacterMotion.Compute(new CharacterMotionInput
        {
            FlatForward = flatForward,
            FlatRight = flatRight,
            MoveForward = inputState.MoveForward,
            MoveBack = inputState.MoveBack,
            MoveLeft = inputState.MoveLeft,
            MoveRight = inputState.MoveRight,
            JumpPressed = inputState.JumpPressed,
            CurrentY = position.Y,
            VerticalVelocity = verticalMotion.Velocity,
            DeltaTime = frame.DeltaTime,
        });

        verticalMotion = new VerticalMotion { Velocity = motion.NewVerticalVelocity, IsGrounded = motion.IsGrounded };
        position = new Position
        {
            X = position.X + (motion.HorizontalVelocity.X * frame.DeltaTime),
            Y = motion.NewY,
            Z = position.Z + (motion.HorizontalVelocity.Z * frame.DeltaTime),
        };

        rotation = new Rotation { X = facingRuf.R, Y = facingRuf.U, Z = facingRuf.F, W = facingRuf.W };
    }
}
