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

    public static CharacterMotionResult Compute(in CharacterMotionInput input)
    {
        Vector3 moveDirection = Vector3.Zero;
        if (input.MoveForward)
        {
            moveDirection += input.FlatForward;
        }
        if (input.MoveBack)
        {
            moveDirection -= input.FlatForward;
        }
        if (input.MoveRight)
        {
            moveDirection += input.FlatRight;
        }
        if (input.MoveLeft)
        {
            moveDirection -= input.FlatRight;
        }

        if (moveDirection != Vector3.Zero)
        {
            moveDirection = Vector3.Normalize(moveDirection);
        }

        bool wasGrounded = input.CurrentY <= 0f;
        float verticalVelocity = input.VerticalVelocity;

        if (wasGrounded && input.JumpPressed)
        {
            verticalVelocity = JumpSpeed;
        }

        verticalVelocity -= Gravity * input.DeltaTime;

        float newY = input.CurrentY + (verticalVelocity * input.DeltaTime);
        bool isGrounded = newY <= 0f;
        if (isGrounded)
        {
            newY = 0f;
            verticalVelocity = 0f;
        }

        return new CharacterMotionResult
        {
            HorizontalVelocity = moveDirection * MoveSpeed,
            NewY = newY,
            NewVerticalVelocity = verticalVelocity,
            IsGrounded = isGrounded,
        };
    }

    /// <summary>Steps <paramref name="current"/> toward <paramref name="target"/> (radians) by the shortest path, at most <paramref name="rotationSpeedRadiansPerSecond"/>*<paramref name="deltaTime"/>.</summary>
    public static float LerpAngle(float current, float target, float rotationSpeedRadiansPerSecond, float deltaTime)
    {
        float difference = WrapAngle(target - current);
        float maxStep = rotationSpeedRadiansPerSecond * deltaTime;
        float step = Math.Clamp(difference, -maxStep, maxStep);
        return WrapAngle(current + step);
    }

    private static float WrapAngle(float angleRadians)
    {
        angleRadians %= MathF.Tau;
        if (angleRadians > MathF.PI)
        {
            angleRadians -= MathF.Tau;
        }
        if (angleRadians < -MathF.PI)
        {
            angleRadians += MathF.Tau;
        }
        return angleRadians;
    }
}
