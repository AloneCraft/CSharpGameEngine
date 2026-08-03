using System.Numerics;

namespace UserGameProject.Tests;

public class CharacterMotionTests
{
    private static readonly Vector3 Forward = new(0f, 0f, 1f);
    private static readonly Vector3 Right = new(1f, 0f, 0f);

    private static CharacterMotionInput NoInputAt(float currentY = 0f, float verticalVelocity = 0f, float deltaTime = 1f / 60f)
    {
        return new CharacterMotionInput
        {
            FlatForward = Forward,
            FlatRight = Right,
            MoveForward = false,
            MoveBack = false,
            MoveLeft = false,
            MoveRight = false,
            JumpPressed = false,
            CurrentY = currentY,
            VerticalVelocity = verticalVelocity,
            DeltaTime = deltaTime,
        };
    }

    [Fact]
    public void NoInput_OnGround_StaysGroundedWithNoMovement()
    {
        CharacterMotionResult result = CharacterMotion.Compute(NoInputAt(currentY: 0f, verticalVelocity: 0f));

        Assert.Equal(Vector3.Zero, result.HorizontalVelocity);
        Assert.Equal(0f, result.NewY);
        Assert.Equal(0f, result.NewVerticalVelocity);
        Assert.True(result.IsGrounded);
    }

    [Fact]
    public void MoveForward_ProducesVelocityAlongFlatForward()
    {
        CharacterMotionInput input = NoInputAt() with { MoveForward = true };

        CharacterMotionResult result = CharacterMotion.Compute(input);

        Vector3 expected = Forward * CharacterMotion.MoveSpeed;
        Assert.Equal(expected.X, result.HorizontalVelocity.X, 4);
        Assert.Equal(expected.Y, result.HorizontalVelocity.Y, 4);
        Assert.Equal(expected.Z, result.HorizontalVelocity.Z, 4);
    }

    [Fact]
    public void MoveForwardAndRight_NormalizesToMoveSpeedNotSqrtTwoTimesMoveSpeed()
    {
        CharacterMotionInput input = NoInputAt() with { MoveForward = true, MoveRight = true };

        CharacterMotionResult result = CharacterMotion.Compute(input);

        Assert.Equal(CharacterMotion.MoveSpeed, result.HorizontalVelocity.Length(), 4);
    }

    [Fact]
    public void MoveForwardAndBack_CancelOutToZero()
    {
        CharacterMotionInput input = NoInputAt() with { MoveForward = true, MoveBack = true };

        CharacterMotionResult result = CharacterMotion.Compute(input);

        Assert.Equal(Vector3.Zero, result.HorizontalVelocity);
    }

    [Fact]
    public void Jump_WhenGrounded_LaunchesUpwardMinusOneFrameOfGravity()
    {
        CharacterMotionInput input = NoInputAt(currentY: 0f, verticalVelocity: 0f) with { JumpPressed = true };

        CharacterMotionResult result = CharacterMotion.Compute(input);

        float expectedVelocity = CharacterMotion.JumpSpeed - (CharacterMotion.Gravity * input.DeltaTime);
        Assert.Equal(expectedVelocity, result.NewVerticalVelocity, 4);
        Assert.False(result.IsGrounded);
        Assert.True(result.NewY > 0f);
    }

    [Fact]
    public void Jump_WhenAirborne_IsIgnored()
    {
        CharacterMotionInput input = NoInputAt(currentY: 5f, verticalVelocity: 1f) with { JumpPressed = true };

        CharacterMotionResult result = CharacterMotion.Compute(input);

        // Airborne jump presses must not add JumpSpeed - only gravity should have been applied.
        float expectedVelocity = 1f - (CharacterMotion.Gravity * input.DeltaTime);
        Assert.Equal(expectedVelocity, result.NewVerticalVelocity, 4);
    }

    [Fact]
    public void Airborne_FallsUnderGravityOverTime()
    {
        CharacterMotionInput input = NoInputAt(currentY: 10f, verticalVelocity: 0f);

        CharacterMotionResult result = CharacterMotion.Compute(input);

        Assert.True(result.NewY < 10f);
        Assert.True(result.NewVerticalVelocity < 0f);
        Assert.False(result.IsGrounded);
    }

    [Fact]
    public void FallingBelowGround_ClampsToZeroAndZeroesVerticalVelocity()
    {
        CharacterMotionInput input = NoInputAt(currentY: 0.05f, verticalVelocity: -20f, deltaTime: 1f);

        CharacterMotionResult result = CharacterMotion.Compute(input);

        Assert.Equal(0f, result.NewY);
        Assert.Equal(0f, result.NewVerticalVelocity);
        Assert.True(result.IsGrounded);
    }

    [Fact]
    public void LerpAngle_StepsTowardTargetByAtMostMaxStep()
    {
        float result = CharacterMotion.LerpAngle(current: 0f, target: MathF.PI / 2f, rotationSpeedRadiansPerSecond: 1f, deltaTime: 0.1f);

        Assert.Equal(0.1f, result, 4);
    }

    [Fact]
    public void LerpAngle_ReachesTargetExactlyWhenWithinOneStep()
    {
        float result = CharacterMotion.LerpAngle(current: 0f, target: 0.05f, rotationSpeedRadiansPerSecond: 10f, deltaTime: 1f);

        Assert.Equal(0.05f, result, 4);
    }

    [Fact]
    public void LerpAngle_TakesShortestPathAcrossThePiWrapBoundary()
    {
        // current is just below +PI, target is just above -PI - the short way is a small step
        // "forward" across the wrap boundary, not the long way back through 0.
        float current = MathF.PI - 0.05f;
        float target = -MathF.PI + 0.05f;

        float result = CharacterMotion.LerpAngle(current, target, rotationSpeedRadiansPerSecond: 100f, deltaTime: 1f);

        Assert.Equal(target, result, 3);
    }
}
