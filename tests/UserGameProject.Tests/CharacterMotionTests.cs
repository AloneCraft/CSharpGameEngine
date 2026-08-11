using System.Numerics;

namespace UserGameProject.Tests;

public class CharacterMotionTests
{
    private static readonly Vector3 Forward = new(0f, 0f, 1f);
    private static readonly Vector3 Right = new(1f, 0f, 0f);

    private static CharacterMotionInput NoInputAt(float CurrentY = 0f, float VerticalVelocity = 0f, float DeltaTime = 1f / 60f)
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
            CurrentY = CurrentY,
            VerticalVelocity = VerticalVelocity,
            DeltaTime = DeltaTime,
        };
    }

    [Fact]
    public void NoInput_OnGround_StaysGroundedWithNoMovement()
    {
        CharacterMotionResult Result = CharacterMotion.Compute(NoInputAt(CurrentY: 0f, VerticalVelocity: 0f));

        Assert.Equal(Vector3.Zero, Result.HorizontalVelocity);
        Assert.Equal(0f, Result.NewY);
        Assert.Equal(0f, Result.NewVerticalVelocity);
        Assert.True(Result.IsGrounded);
    }

    [Fact]
    public void MoveForward_ProducesVelocityAlongFlatForward()
    {
        CharacterMotionInput Input = NoInputAt() with { MoveForward = true };

        CharacterMotionResult Result = CharacterMotion.Compute(Input);

        Vector3 Expected = Forward * CharacterMotion.MoveSpeed;
        Assert.Equal(Expected.X, Result.HorizontalVelocity.X, 4);
        Assert.Equal(Expected.Y, Result.HorizontalVelocity.Y, 4);
        Assert.Equal(Expected.Z, Result.HorizontalVelocity.Z, 4);
    }

    [Fact]
    public void MoveForwardAndRight_NormalizesToMoveSpeedNotSqrtTwoTimesMoveSpeed()
    {
        CharacterMotionInput Input = NoInputAt() with { MoveForward = true, MoveRight = true };

        CharacterMotionResult Result = CharacterMotion.Compute(Input);

        Assert.Equal(CharacterMotion.MoveSpeed, Result.HorizontalVelocity.Length(), 4);
    }

    [Fact]
    public void MoveForwardAndBack_CancelOutToZero()
    {
        CharacterMotionInput Input = NoInputAt() with { MoveForward = true, MoveBack = true };

        CharacterMotionResult Result = CharacterMotion.Compute(Input);

        Assert.Equal(Vector3.Zero, Result.HorizontalVelocity);
    }

    [Fact]
    public void Jump_WhenGrounded_LaunchesUpwardMinusOneFrameOfGravity()
    {
        CharacterMotionInput Input = NoInputAt(CurrentY: 0f, VerticalVelocity: 0f) with { JumpPressed = true };

        CharacterMotionResult Result = CharacterMotion.Compute(Input);

        float ExpectedVelocity = CharacterMotion.JumpSpeed - (CharacterMotion.Gravity * Input.DeltaTime);
        Assert.Equal(ExpectedVelocity, Result.NewVerticalVelocity, 4);
        Assert.False(Result.IsGrounded);
        Assert.True(Result.NewY > 0f);
    }

    [Fact]
    public void Jump_WhenAirborne_IsIgnored()
    {
        CharacterMotionInput Input = NoInputAt(CurrentY: 5f, VerticalVelocity: 1f) with { JumpPressed = true };

        CharacterMotionResult Result = CharacterMotion.Compute(Input);

        // Airborne jump presses must not add JumpSpeed - only gravity should have been applied.
        float ExpectedVelocity = 1f - (CharacterMotion.Gravity * Input.DeltaTime);
        Assert.Equal(ExpectedVelocity, Result.NewVerticalVelocity, 4);
    }

    [Fact]
    public void Airborne_FallsUnderGravityOverTime()
    {
        CharacterMotionInput Input = NoInputAt(CurrentY: 10f, VerticalVelocity: 0f);

        CharacterMotionResult Result = CharacterMotion.Compute(Input);

        Assert.True(Result.NewY < 10f);
        Assert.True(Result.NewVerticalVelocity < 0f);
        Assert.False(Result.IsGrounded);
    }

    [Fact]
    public void FallingBelowGround_ClampsToZeroAndZeroesVerticalVelocity()
    {
        CharacterMotionInput Input = NoInputAt(CurrentY: 0.05f, VerticalVelocity: -20f, DeltaTime: 1f);

        CharacterMotionResult Result = CharacterMotion.Compute(Input);

        Assert.Equal(0f, Result.NewY);
        Assert.Equal(0f, Result.NewVerticalVelocity);
        Assert.True(Result.IsGrounded);
    }

    [Fact]
    public void LerpAngle_StepsTowardTargetByAtMostMaxStep()
    {
        float Result = CharacterMotion.LerpAngle(Current: 0f, Target: MathF.PI / 2f, RotationSpeedRadiansPerSecond: 1f, DeltaTime: 0.1f);

        Assert.Equal(0.1f, Result, 4);
    }

    [Fact]
    public void LerpAngle_ReachesTargetExactlyWhenWithinOneStep()
    {
        float Result = CharacterMotion.LerpAngle(Current: 0f, Target: 0.05f, RotationSpeedRadiansPerSecond: 10f, DeltaTime: 1f);

        Assert.Equal(0.05f, Result, 4);
    }

    [Fact]
    public void LerpAngle_TakesShortestPathAcrossThePiWrapBoundary()
    {
        // current is just below +PI, target is just above -PI - the short way is a small step
        // "forward" across the wrap boundary, not the long way back through 0.
        float Current = MathF.PI - 0.05f;
        float Target = -MathF.PI + 0.05f;

        float Result = CharacterMotion.LerpAngle(Current, Target, RotationSpeedRadiansPerSecond: 100f, DeltaTime: 1f);

        Assert.Equal(Target, Result, 3);
    }
}
