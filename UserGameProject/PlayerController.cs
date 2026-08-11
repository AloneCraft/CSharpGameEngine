using System.Numerics;

namespace UserGameProject;

/// <summary>Everything <see cref="CharacterMotion.Compute"/> needs for one frame's worth of movement.</summary>
public readonly record struct CharacterMotionInput
{
    public required Vector3 FlatForward { get; init; }
    public required Vector3 FlatRight { get; init; }
    public required bool MoveForward { get; init; }
    public required bool MoveBack { get; init; }
    public required bool MoveLeft { get; init; }
    public required bool MoveRight { get; init; }

    /// <summary>Edge-detected (true only on the frame the jump key was first pressed) - the caller does the edge detection, not <see cref="CharacterMotion"/>.</summary>
    public required bool JumpPressed { get; init; }
    public required float CurrentY { get; init; }
    public required float VerticalVelocity { get; init; }
    public required float DeltaTime { get; init; }
}

public readonly record struct CharacterMotionResult
{
    public required Vector3 HorizontalVelocity { get; init; }
    public required float NewY { get; init; }
    public required float NewVerticalVelocity { get; init; }
    public required bool IsGrounded { get; init; }
}

/// <summary>
/// Pure, GPU/World/Window-free movement math for the ThirdPerson-style character controller
/// (ADR-0016): camera-relative WASD, simple hand-rolled gravity/jump/ground-clamp (no physics or
/// collision engine - the ground is always Y=0), and shortest-path yaw rotation toward the
/// movement direction. Unit-testable without a window, GPU, or ECS World.
/// </summary>
public static class CharacterMotion
{
    public const float MoveSpeed = 5.0f;
    public const float Gravity = 18.0f;
    public const float JumpSpeed = 6.0f;

    public static CharacterMotionResult Compute(in CharacterMotionInput Input)
    {
        Vector3 MoveDirection = Vector3.Zero;
        if (Input.MoveForward)
        {
            MoveDirection += Input.FlatForward;
        }
        if (Input.MoveBack)
        {
            MoveDirection -= Input.FlatForward;
        }
        if (Input.MoveRight)
        {
            MoveDirection += Input.FlatRight;
        }
        if (Input.MoveLeft)
        {
            MoveDirection -= Input.FlatRight;
        }

        if (MoveDirection != Vector3.Zero)
        {
            MoveDirection = Vector3.Normalize(MoveDirection);
        }

        bool WasGrounded = Input.CurrentY <= 0f;
        float VerticalVelocity = Input.VerticalVelocity;

        if (WasGrounded && Input.JumpPressed)
        {
            VerticalVelocity = JumpSpeed;
        }

        VerticalVelocity -= Gravity * Input.DeltaTime;

        float NewY = Input.CurrentY + (VerticalVelocity * Input.DeltaTime);
        bool IsGrounded = NewY <= 0f;
        if (IsGrounded)
        {
            NewY = 0f;
            VerticalVelocity = 0f;
        }

        return new CharacterMotionResult
        {
            HorizontalVelocity = MoveDirection * MoveSpeed,
            NewY = NewY,
            NewVerticalVelocity = VerticalVelocity,
            IsGrounded = IsGrounded,
        };
    }

    /// <summary>Steps <paramref name="Current"/> toward <paramref name="Target"/> (radians) by the shortest path, at most <paramref name="RotationSpeedRadiansPerSecond"/>*<paramref name="DeltaTime"/>.</summary>
    public static float LerpAngle(float Current, float Target, float RotationSpeedRadiansPerSecond, float DeltaTime)
    {
        float Difference = WrapAngle(Target - Current);
        float MaxStep = RotationSpeedRadiansPerSecond * DeltaTime;
        float Step = Math.Clamp(Difference, -MaxStep, MaxStep);
        return WrapAngle(Current + Step);
    }

    private static float WrapAngle(float AngleRadians)
    {
        AngleRadians %= MathF.Tau;
        if (AngleRadians > MathF.PI)
        {
            AngleRadians -= MathF.Tau;
        }
        if (AngleRadians < -MathF.PI)
        {
            AngleRadians += MathF.Tau;
        }
        return AngleRadians;
    }
}
