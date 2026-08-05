using System.Numerics;

namespace CSharpGameEngine.Graphics.D3D12.Tests;

public class CameraTests
{
    [Fact]
    public void CreateDefault_SetsExpectedDefaults()
    {
        Camera camera = Camera.CreateDefault(new Vector3(1, 2, 3));

        Assert.Equal(new Vector3(1, 2, 3), camera.Target);
        Assert.Equal(0f, camera.Yaw);
        Assert.True(camera.Distance > 0f);
        Assert.True(camera.FovYRadians > 0f);
        Assert.True(camera.NearZ > 0f);
        Assert.True(camera.FarZ > camera.NearZ);
    }

    [Fact]
    public void GetForward_AtZeroYawZeroPitch_PointsAlongPositiveZ()
    {
        Camera camera = Camera.CreateDefault(Vector3.Zero);
        camera.Pitch = 0f;

        Vector3 forward = camera.GetForward();

        AssertApprox(0f, forward.X);
        AssertApprox(0f, forward.Y);
        AssertApprox(1f, forward.Z);
    }

    [Fact]
    public void GetEyePosition_IsDistanceAwayFromTargetOppositeForward()
    {
        Camera camera = Camera.CreateDefault(new Vector3(5, 0, 0));
        camera.Pitch = 0f;
        camera.Distance = 3f;

        Vector3 eye = camera.GetEyePosition();
        Vector3 forward = camera.GetForward();

        AssertApprox((camera.Target - (forward * camera.Distance)).X, eye.X);
        AssertApprox((camera.Target - (forward * camera.Distance)).Y, eye.Y);
        AssertApprox((camera.Target - (forward * camera.Distance)).Z, eye.Z);
        AssertApprox(camera.Distance, Vector3.Distance(eye, camera.Target));
    }

    [Fact]
    public void GetFlatForwardAndFlatRight_AreUnitLengthOrthogonalAndFlat()
    {
        Camera camera = Camera.CreateDefault(Vector3.Zero);
        camera.Yaw = 0.7f;
        camera.Pitch = 0.3f;

        Vector3 flatForward = camera.GetFlatForward();
        Vector3 flatRight = camera.GetFlatRight();

        AssertApprox(0f, flatForward.Y);
        AssertApprox(0f, flatRight.Y);
        AssertApprox(1f, flatForward.Length());
        AssertApprox(1f, flatRight.Length());
        AssertApprox(0f, Vector3.Dot(flatForward, flatRight));
    }

    [Fact]
    public void ApplyMouseDelta_ClampsPitchToConfiguredRange()
    {
        Camera camera = Camera.CreateDefault(Vector3.Zero);

        camera.ApplyMouseDelta(deltaX: 0f, deltaY: -100000f, yawSensitivity: 1f, pitchSensitivity: 1f);
        Assert.Equal(Camera.MaxPitchRadians, camera.Pitch);

        camera.ApplyMouseDelta(deltaX: 0f, deltaY: 100000f, yawSensitivity: 1f, pitchSensitivity: 1f);
        Assert.Equal(Camera.MinPitchRadians, camera.Pitch);
    }

    [Fact]
    public void ApplyMouseDelta_AccumulatesYawWithoutClamping()
    {
        Camera camera = Camera.CreateDefault(Vector3.Zero);

        camera.ApplyMouseDelta(deltaX: 1f, deltaY: 0f, yawSensitivity: 0.5f, pitchSensitivity: 1f);
        camera.ApplyMouseDelta(deltaX: 1f, deltaY: 0f, yawSensitivity: 0.5f, pitchSensitivity: 1f);

        AssertApprox(1f, camera.Yaw);
    }

    [Fact]
    public void GetView_ProducesAnInvertibleMatrix()
    {
        Camera camera = Camera.CreateDefault(new Vector3(0, 1, 0));

        Matrix4x4 view = camera.GetView();

        Assert.True(Matrix4x4.Invert(view, out _));
    }

    [Fact]
    public void GetProjection_ProducesAnInvertibleMatrix()
    {
        Camera camera = Camera.CreateDefault(Vector3.Zero);

        Matrix4x4 projection = camera.GetProjection(aspectRatio: 16f / 9f);

        Assert.True(Matrix4x4.Invert(projection, out _));
    }

    [Fact]
    public void CreateFirstPerson_HasZeroDistanceAndTargetIsEyePosition()
    {
        Camera camera = Camera.CreateFirstPerson(new Vector3(1, 2, 3));

        Assert.Equal(0f, camera.Distance);
        Assert.Equal(new Vector3(1, 2, 3), camera.Target);
        AssertApprox(0f, Vector3.Distance(camera.GetEyePosition(), new Vector3(1, 2, 3)));
    }

    [Fact]
    public void CreateFirstPerson_GetView_IsInvertibleNotDegenerate()
    {
        Camera camera = Camera.CreateFirstPerson(new Vector3(0, 1.6f, 0));

        Matrix4x4 view = camera.GetView();

        Assert.True(Matrix4x4.Invert(view, out _));
    }

    [Fact]
    public void CreateFirstPerson_AllowsSteeperPitchThanOrbitCamera()
    {
        Camera camera = Camera.CreateFirstPerson(Vector3.Zero);

        camera.ApplyMouseDelta(deltaX: 0f, deltaY: -100000f, yawSensitivity: 1f, pitchSensitivity: 1f);

        Assert.Equal(Camera.MaxPitchRadiansFirstPerson, camera.Pitch);
        Assert.True(Camera.MaxPitchRadiansFirstPerson > Camera.MaxPitchRadians);
    }

    /// <summary>
    /// Permanent regression guard for ADR-0026's RUF rewrite: independently re-derives the original
    /// hand-written trig formula (the pre-RUF implementation) and checks GetForward against it across
    /// a Yaw/Pitch sweep, so a future change to the RufQuaternion-based implementation cannot silently
    /// change what "forward" means without failing a test.
    /// </summary>
    [Fact]
    public void GetForward_MatchesLegacyTrigFormula_AcrossYawPitchSweep()
    {
        float[] yaws = [-3.0f, -1.5f, -0.7f, 0f, 0.3f, 0.7f, 1.5f, 3.0f];
        float[] pitches = [-1.2f, -0.5f, 0f, 0.3f, 1.2f];

        foreach (float yaw in yaws)
        {
            foreach (float pitch in pitches)
            {
                Camera camera = Camera.CreateDefault(Vector3.Zero);
                camera.Yaw = yaw;
                camera.Pitch = pitch;

                Vector3 actual = camera.GetForward();

                float cosPitch = MathF.Cos(pitch);
                Vector3 expected = Vector3.Normalize(new Vector3(
                    cosPitch * MathF.Sin(yaw),
                    MathF.Sin(pitch),
                    cosPitch * MathF.Cos(yaw)));

                AssertApprox(expected.X, actual.X);
                AssertApprox(expected.Y, actual.Y);
                AssertApprox(expected.Z, actual.Z);
            }
        }
    }

    /// <summary>
    /// Permanent regression guard for ADR-0026: proves Spatial&lt;UnityCoordinateConvention&gt;.
    /// RightFromUpForward produces the same Right direction as the original ad-hoc
    /// Cross(Vector3.UnitY, flatForward) formula, across a Yaw sweep.
    /// </summary>
    [Fact]
    public void GetFlatRight_MatchesLegacyCrossProductFormula_AcrossYawSweep()
    {
        float[] yaws = [-3.0f, -1.5f, -0.7f, 0f, 0.3f, 0.7f, 1.5f, 3.0f];

        foreach (float yaw in yaws)
        {
            Camera camera = Camera.CreateDefault(Vector3.Zero);
            camera.Yaw = yaw;

            Vector3 actual = camera.GetFlatRight();
            Vector3 expected = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, camera.GetFlatForward()));

            AssertApprox(expected.X, actual.X);
            AssertApprox(expected.Y, actual.Y);
            AssertApprox(expected.Z, actual.Z);
        }
    }

    private static void AssertApprox(float expected, float actual, float tolerance = 1e-4f)
    {
        Assert.True(MathF.Abs(expected - actual) <= tolerance, $"Expected {expected} but got {actual} (tolerance {tolerance}).");
    }
}
