using System.Numerics;
using CSharpGameEngine.Graphics.D3D12;

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

        Vector3 flatForward = new(MathF.Sin(cameraRig.Yaw), 0f, MathF.Cos(cameraRig.Yaw));
        Vector3 flatRight = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, flatForward));

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

        Quaternion facing = Quaternion.CreateFromAxisAngle(Vector3.UnitY, cameraRig.Yaw);
        player.Rotation = new Rotation { X = facing.X, Y = facing.Y, Z = facing.Z, W = facing.W };
    }
}
