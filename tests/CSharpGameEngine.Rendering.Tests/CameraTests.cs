using Camera=CSharpGameEngine.Rendering.View.RenderCamera;
using System.Numerics;

namespace CSharpGameEngine.Rendering.Tests;

public class CameraTests {
    [Fact]
    public void CreateDefault_SetsExpectedDefaults(){
        Camera Camera=Camera.CreateDefault(new Vector3(1,2,3));

        Assert.Equal(new Vector3(1,2,3),Camera.Target);
        Assert.Equal(0f,Camera.Yaw);
        Assert.True(Camera.Distance>0f);
        Assert.True(Camera.FovYRadians>0f);
        Assert.True(Camera.NearZ>0f);
        Assert.True(Camera.FarZ>Camera.NearZ);
    }

    [Fact]
    public void ObjectInitializer_DefaultsToTheLogarithmicCurveWithoutAFactory(){
        var View=new Camera { FovYRadians=MathF.PI/4f,NearZ=0.1f,FarZ=1e18f };
        Assert.Equal(CSharpGameEngine.Rendering.RadialDepthCurve.DefaultTransitionMeters,View.RadialTransitionMeters);
    }
    [Fact]
    public void Factories_ShareTheNativeCurveDefaults(){
        foreach(var View in new[]{Camera.CreateDefault(Vector3.Zero),Camera.CreateFirstPerson(Vector3.Zero),Camera.CreateBoundsFit(Vector3.Zero,Vector3.One)}){
            Assert.Equal(CSharpGameEngine.Rendering.RadialDepthCurve.DefaultTransitionMeters,View.RadialTransitionMeters);
        }
    }

    [Fact]
    public void GetForward_AtZeroYawZeroPitch_PointsAlongPositiveZ(){
        Camera Camera=Camera.CreateDefault(Vector3.Zero);
        Camera.Pitch=0f;

        Vector3 Forward=Camera.GetForward();

        AssertApprox(0f,Forward.X);
        AssertApprox(0f,Forward.Y);
        AssertApprox(1f,Forward.Z);
    }

    [Fact]
    public void GetEyePosition_IsDistanceAwayFromTargetOppositeForward(){
        Camera Camera=Camera.CreateDefault(new Vector3(5,0,0));
        Camera.Pitch=0f;
        Camera.Distance=3f;

        Vector3 Eye=Camera.GetEyePosition();
        Vector3 Forward=Camera.GetForward();

        AssertApprox((Camera.Target-(Forward*Camera.Distance)).X,Eye.X);
        AssertApprox((Camera.Target-(Forward*Camera.Distance)).Y,Eye.Y);
        AssertApprox((Camera.Target-(Forward*Camera.Distance)).Z,Eye.Z);
        AssertApprox(Camera.Distance,Vector3.Distance(Eye,Camera.Target));
    }

    [Fact]
    public void GetFlatForwardAndFlatRight_AreUnitLengthOrthogonalAndFlat(){
        Camera Camera=Camera.CreateDefault(Vector3.Zero);
        Camera.Yaw=0.7f;
        Camera.Pitch=0.3f;

        Vector3 FlatForward=Camera.GetFlatForward();
        Vector3 FlatRight=Camera.GetFlatRight();

        AssertApprox(0f,FlatForward.Y);
        AssertApprox(0f,FlatRight.Y);
        AssertApprox(1f,FlatForward.Length());
        AssertApprox(1f,FlatRight.Length());
        AssertApprox(0f,Vector3.Dot(FlatForward,FlatRight));
    }

    [Fact]
    public void ApplyMouseDelta_ClampsPitchToConfiguredRange(){
        Camera Camera=Camera.CreateDefault(Vector3.Zero);

        Camera.ApplyMouseDelta(DeltaX:0f,DeltaY:-100000f,YawSensitivity:1f,PitchSensitivity:1f);
        Assert.Equal(Camera.MaxPitchRadians,Camera.Pitch);

        Camera.ApplyMouseDelta(DeltaX:0f,DeltaY:100000f,YawSensitivity:1f,PitchSensitivity:1f);
        Assert.Equal(Camera.MinPitchRadians,Camera.Pitch);
    }

    [Fact]
    public void ApplyMouseDelta_AccumulatesYawWithoutClamping(){
        Camera Camera=Camera.CreateDefault(Vector3.Zero);

        Camera.ApplyMouseDelta(DeltaX:1f,DeltaY:0f,YawSensitivity:0.5f,PitchSensitivity:1f);
        Camera.ApplyMouseDelta(DeltaX:1f,DeltaY:0f,YawSensitivity:0.5f,PitchSensitivity:1f);

        AssertApprox(1f,Camera.Yaw);
    }

    [Fact]
    public void GetView_ProducesAnInvertibleMatrix(){
        Camera Camera=Camera.CreateDefault(new Vector3(0,1,0));

        Matrix4x4 View=Camera.GetView();

        Assert.True(Matrix4x4.Invert(View,out _));
    }

    [Fact]
    public void GetProjection_ProducesAnInvertibleMatrix(){
        Camera Camera=Camera.CreateDefault(Vector3.Zero);

        Matrix4x4 Projection=Camera.GetProjection(AspectRatio:16f/9f);

        Assert.True(Matrix4x4.Invert(Projection,out _));
    }

    [Fact]
    public void CreateFirstPerson_HasZeroDistanceAndTargetIsEyePosition(){
        Camera Camera=Camera.CreateFirstPerson(new Vector3(1,2,3));

        Assert.Equal(0f,Camera.Distance);
        Assert.Equal(new Vector3(1,2,3),Camera.Target);
        AssertApprox(0f,Vector3.Distance(Camera.GetEyePosition(),new Vector3(1,2,3)));
    }

    [Fact]
    public void CreateFirstPerson_GetView_IsInvertibleNotDegenerate(){
        Camera Camera=Camera.CreateFirstPerson(new Vector3(0,1.6f,0));

        Matrix4x4 View=Camera.GetView();

        Assert.True(Matrix4x4.Invert(View,out _));
    }

    [Fact]
    public void CreateFirstPerson_AllowsSteeperPitchThanOrbitCamera(){
        Camera Camera=Camera.CreateFirstPerson(Vector3.Zero);

        Camera.ApplyMouseDelta(DeltaX:0f,DeltaY:-100000f,YawSensitivity:1f,PitchSensitivity:1f);

        Assert.Equal(Camera.MaxPitchRadiansFirstPerson,Camera.Pitch);
        Assert.True(Camera.MaxPitchRadiansFirstPerson>Camera.MaxPitchRadians);
    }

    [Fact]
    public void TrySetWorldOrientation_UsesTransformLocalPositiveZAndPositiveYForForwardAndView(){
        Camera Camera=Camera.CreateFirstPerson(Vector3.Zero);

        Assert.True(Camera.TrySetWorldOrientation(Vector3.UnitZ,Vector3.UnitX));

        AssertApprox(0f,Camera.GetForward().X);
        AssertApprox(0f,Camera.GetForward().Y);
        AssertApprox(1f,Camera.GetForward().Z);
        AssertApprox(1f,Camera.GetUp().X);
        AssertApprox(0f,Camera.GetUp().Y);
        AssertApprox(0f,Camera.GetUp().Z);
        Assert.Equal(
            global::CSharpGameEngine.Rendering.View.RenderCamera.BuildViewMatrix(Vector3.Zero,Vector3.UnitZ,Vector3.UnitX),
            Camera.GetView());
    }

    [Fact]
    public void TrySetWorldOrientation_RejectsCollapsedBasisWithoutOverwritingLastValidPose(){
        Camera Camera=Camera.CreateFirstPerson(Vector3.Zero);
        Assert.True(Camera.TrySetWorldOrientation(
            Vector3.Normalize(new Vector3(1f,0.25f,1f)),Vector3.UnitY));
        float PreviousYaw=Camera.Yaw;
        float PreviousPitch=Camera.Pitch;
        Vector3 PreviousUp=Camera.Up;

        Assert.False(Camera.TrySetWorldOrientation(Vector3.Zero,Vector3.UnitY));

        Assert.Equal(PreviousYaw,Camera.Yaw);
        Assert.Equal(PreviousPitch,Camera.Pitch);
        Assert.Equal(PreviousUp,Camera.Up);
    }

    [Fact]
    public void IsIdentityWorldOrientation_ChecksBothForwardAndUpAxes(){
        Assert.True(Camera.IsIdentityWorldOrientation(Vector3.UnitZ,Vector3.UnitY));
        Assert.False(Camera.IsIdentityWorldOrientation(Vector3.UnitX,Vector3.UnitY));
        Assert.False(Camera.IsIdentityWorldOrientation(Vector3.UnitZ,Vector3.UnitX));
    }

    /// <summary>
    /// Permanent regression guard for ADR-0026's RUF rewrite: independently re-derives the original
    /// hand-written trig formula (the pre-RUF implementation) and checks GetForward against it across
    /// a Yaw/Pitch sweep, so a future change to the RufQuaternion-based implementation cannot silently
    /// change what "forward" means without failing a test.
    /// </summary>
    [Fact]
    public void GetForward_MatchesLegacyTrigFormula_AcrossYawPitchSweep(){
        float[]Yaws=[-3.0f,-1.5f,-0.7f,0f,0.3f,0.7f,1.5f,3.0f];
        float[]Pitches=[-1.2f,-0.5f,0f,0.3f,1.2f];

        foreach(var Yaw in Yaws){
            foreach(var Pitch in Pitches){
                Camera Camera=Camera.CreateDefault(Vector3.Zero);
                Camera.Yaw=Yaw;
                Camera.Pitch=Pitch;

                Vector3 Actual=Camera.GetForward();

                var CosPitch=MathF.Cos(Pitch);
                var Expected=Vector3.Normalize(new Vector3(
                                                           CosPitch*MathF.Sin(Yaw),
                                                           MathF.Sin(Pitch),
                                                           CosPitch*MathF.Cos(Yaw)));

                AssertApprox(Expected.X,Actual.X);
                AssertApprox(Expected.Y,Actual.Y);
                AssertApprox(Expected.Z,Actual.Z);
            }
        }
    }

    /// <summary>
    /// Permanent regression guard for ADR-0026: proves Spatial&lt;UnityCoordinateConvention&gt;.
    /// RightFromUpForward produces the same Right direction as the original ad-hoc
    /// Cross(Vector3.UnitY, flatForward) formula, across a Yaw sweep.
    /// </summary>
    [Fact]
    public void GetFlatRight_MatchesLegacyCrossProductFormula_AcrossYawSweep(){
        float[]Yaws=[-3.0f,-1.5f,-0.7f,0f,0.3f,0.7f,1.5f,3.0f];

        foreach(var Yaw in Yaws){
            Camera Camera=Camera.CreateDefault(Vector3.Zero);
            Camera.Yaw=Yaw;

            Vector3 Actual=Camera.GetFlatRight();
            var Expected=Vector3.Normalize(Vector3.Cross(Vector3.UnitY,Camera.GetFlatForward()));

            AssertApprox(Expected.X,Actual.X);
            AssertApprox(Expected.Y,Actual.Y);
            AssertApprox(Expected.Z,Actual.Z);
        }
    }

    [Fact]
    public void CreateBoundsFit_TargetsBoundsCenter(){
        Camera Camera=Camera.CreateBoundsFit(new Vector3(-20,-1,-20),new Vector3(20,4,20));

        AssertApprox(0f,Camera.Target.X);
        AssertApprox(1.5f,Camera.Target.Y);
        AssertApprox(0f,Camera.Target.Z);
    }

    [Fact]
    public void CreateBoundsFit_EyeOffsetFromTarget_MatchesTheSharedDiagonalDirection(){
        // Same (1,0.75,1) eye direction the asset thumbnail and the VSIX
        // SceneRenderer/VorticeRenderer preview cameras already use.
        Camera Camera=Camera.CreateBoundsFit(new Vector3(-1,-1,-1),new Vector3(1,1,1));

        var EyeOffset=Vector3.Normalize(Camera.GetEyePosition()-Camera.Target);
        var Expected=Vector3.Normalize(new Vector3(1f,0.75f,1f));

        AssertApprox(Expected.X,EyeOffset.X);
        AssertApprox(Expected.Y,EyeOffset.Y);
        AssertApprox(Expected.Z,EyeOffset.Z);
    }

    [Fact]
    public void CreateBoundsFit_DistanceIsFarEnoughThatTheWholeBoundingSphereFitsWithinTheFieldOfView(){
        Vector3 Min=new(-20,-1,-20);
        Vector3 Max=new(20,4,20);
        Camera Camera=Camera.CreateBoundsFit(Min,Max);

        var Radius=Vector3.Distance(Min,Max)/2f;
        var HalfAngleToBoundingSphere=MathF.Asin(Radius/Camera.Distance);

        Assert.True(HalfAngleToBoundingSphere<Camera.FovYRadians/2f,
            $"Bounding sphere half-angle {HalfAngleToBoundingSphere} must be smaller than the camera's half FOV {Camera.FovYRadians/2f}.");
    }

    private static void AssertApprox(float Expected,float Actual,float Tolerance=1e-4f){
        Assert.True(MathF.Abs(Expected-Actual)<=Tolerance, $"Expected {Expected} but got {Actual} (tolerance {Tolerance}).");
    }
}
