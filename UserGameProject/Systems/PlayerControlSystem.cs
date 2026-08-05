using System.Numerics;
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
public static class PlayerControlSystem
{
    private const float MouseYawSensitivity = 0.0025f;
    private const float MousePitchSensitivity = 0.0025f;

    public static void Update(Player player, float deltaTime)
    {
        ref CameraRig cameraRig = ref player.CameraRig;
        cameraRig.Yaw += player.InputState.LookDeltaX * MouseYawSensitivity;
        cameraRig.Pitch = Math.Clamp(
            cameraRig.Pitch - (player.InputState.LookDeltaY * MousePitchSensitivity),
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
            MoveForward = player.InputState.MoveForward,
            MoveBack = player.InputState.MoveBack,
            MoveLeft = player.InputState.MoveLeft,
            MoveRight = player.InputState.MoveRight,
            JumpPressed = player.InputState.JumpPressed,
            CurrentY = player.Position.Y,
            VerticalVelocity = player.VerticalMotion.Velocity,
            DeltaTime = deltaTime,
        });

        player.VerticalMotion = new VerticalMotion { Velocity = motion.NewVerticalVelocity, IsGrounded = motion.IsGrounded };
        player.Position = new Position
        {
            X = player.Position.X + (motion.HorizontalVelocity.X * deltaTime),
            Y = motion.NewY,
            Z = player.Position.Z + (motion.HorizontalVelocity.Z * deltaTime),
        };

        player.Rotation = new Rotation { X = facingRuf.R, Y = facingRuf.U, Z = facingRuf.F, W = facingRuf.W };
    }
}
