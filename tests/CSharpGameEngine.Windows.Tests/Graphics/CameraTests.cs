using System.Numerics;

namespace CSharpGameEngine.Graphics.D3D12.Tests;

public class CameraTests {
    [Fact]
    public void CreateDefault_SetsExpectedDefaults(){
        Camera camera=Camera.CreateDefault(new Vector3(1,2,3));

        Assert.Equal(new Vector3(1,2,3),camera.Target);
        Assert.Equal(0f,camera.Yaw);
        Assert.True(camera.Distance>0f);
        Assert.True(camera.FovYRadians>0f);
        Assert.True(camera.NearZ>0f);
        Assert.True(camera.FarZ>camera.NearZ);
    }

    [Fact]
    public void GetForward_AtZeroYawZeroPitch_PointsAlongPositiveZ(){
        Camera camera=Camera.CreateDefault(Vector3.Zero);
        camera.Pitch=0f;

        Vector3 Forward=camera.GetForward();

        AssertApprox(0f,Forward.X);
        AssertApprox(0f,Forward.Y);
        AssertApprox(1f,Forward.Z);
    }

    [Fact]
    public void GetEyePosition_IsDistanceAwayFromTargetOppositeForward(){
        Camera camera=Camera.CreateDefault(new Vector3(5,0,0));
        camera.Pitch=0f;
        camera.Distance=3f;

        Vector3 Eye=camera.GetEyePosition();
        Vector3 Forward=camera.GetForward();

        AssertApprox((camera.Target-(Forward*camera.Distance)).X,Eye.X);
        AssertApprox((camera.Target-(Forward*camera.Distance)).Y,Eye.Y);
        AssertApprox((camera.Target-(Forward*camera.Distance)).Z,Eye.Z);
        AssertApprox(camera.Distance,Vector3.Distance(Eye,camera.Target));
    }

    [Fact]
    public void GetFlatForwardAndFlatRight_AreUnitLengthOrthogonalAndFlat(){
        Camera camera=Camera.CreateDefault(Vector3.Zero);
        camera.Yaw=0.7f;
        camera.Pitch=0.3f;

        Vector3 FlatForward=camera.GetFlatForward();
        Vector3 FlatRight=camera.GetFlatRight();

        AssertApprox(0f,FlatForward.Y);
        AssertApprox(0f,FlatRight.Y);
        AssertApprox(1f,FlatForward.Length());
        AssertApprox(1f,FlatRight.Length());
        AssertApprox(0f,Vector3.Dot(FlatForward,FlatRight));
    }

    [Fact]
    public void ApplyMouseDelta_ClampsPitchToConfiguredRange(){
        Camera camera=Camera.CreateDefault(Vector3.Zero);

        camera.ApplyMouseDelta(DeltaX:0f,DeltaY:-100000f,YawSensitivity:1f,PitchSensitivity:1f);
        Assert.Equal(Camera.MaxPitchRadians,camera.Pitch);

        camera.ApplyMouseDelta(DeltaX:0f,DeltaY:100000f,YawSensitivity:1f,PitchSensitivity:1f);
        Assert.Equal(Camera.MinPitchRadians,camera.Pitch);
    }

    [Fact]
    public void ApplyMouseDelta_AccumulatesYawWithoutClamping(){
        Camera camera=Camera.CreateDefault(Vector3.Zero);

        camera.ApplyMouseDelta(DeltaX:1f,DeltaY:0f,YawSensitivity:0.5f,PitchSensitivity:1f);
        camera.ApplyMouseDelta(DeltaX:1f,DeltaY:0f,YawSensitivity:0.5f,PitchSensitivity:1f);

        AssertApprox(1f,camera.Yaw);
    }

    [Fact]
    public void GetView_ProducesAnInvertibleMatrix(){
        Camera camera=Camera.CreateDefault(new Vector3(0,1,0));

        Matrix4x4 View=camera.GetView();

        Assert.True(Matrix4x4.Invert(View,out _));
    }

    [Fact]
    public void GetProjection_ProducesAnInvertibleMatrix(){
        Camera camera=Camera.CreateDefault(Vector3.Zero);

        Matrix4x4 Projection=camera.GetProjection(AspectRatio:16f/9f);

        Assert.True(Matrix4x4.Invert(Projection,out _));
    }

    [Fact]
    public void CreateFirstPerson_HasZeroDistanceAndTargetIsEyePosition(){
        Camera camera=Camera.CreateFirstPerson(new Vector3(1,2,3));

        Assert.Equal(0f,camera.Distance);
        Assert.Equal(new Vector3(1,2,3),camera.Target);
        AssertApprox(0f,Vector3.Distance(camera.GetEyePosition(),new Vector3(1,2,3)));
    }

    [Fact]
    public void CreateFirstPerson_GetView_IsInvertibleNotDegenerate(){
        Camera camera=Camera.CreateFirstPerson(new Vector3(0,1.6f,0));

        Matrix4x4 View=camera.GetView();

        Assert.True(Matrix4x4.Invert(View,out _));
    }

    [Fact]
    public void CreateFirstPerson_AllowsSteeperPitchThanOrbitCamera(){
        Camera camera=Camera.CreateFirstPerson(Vector3.Zero);

        camera.ApplyMouseDelta(DeltaX:0f,DeltaY:-100000f,YawSensitivity:1f,PitchSensitivity:1f);

        Assert.Equal(Camera.MaxPitchRadiansFirstPerson,camera.Pitch);
        Assert.True(Camera.MaxPitchRadiansFirstPerson>Camera.MaxPitchRadians);
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
                Camera camera=Camera.CreateDefault(Vector3.Zero);
                camera.Yaw=Yaw;
                camera.Pitch=Pitch;

                Vector3 Actual=camera.GetForward();

                var CosPitch=MathF.Cos(Pitch);
                Vector3 Expected=Vector3.Normalize(new Vector3(
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
            Camera camera=Camera.CreateDefault(Vector3.Zero);
            camera.Yaw=Yaw;

            Vector3 Actual=camera.GetFlatRight();
            Vector3 Expected=Vector3.Normalize(Vector3.Cross(Vector3.UnitY,camera.GetFlatForward()));

            AssertApprox(Expected.X,Actual.X);
            AssertApprox(Expected.Y,Actual.Y);
            AssertApprox(Expected.Z,Actual.Z);
        }
    }

    [Fact]
    public void CreateBoundsFit_TargetsBoundsCenter(){
        Camera camera=Camera.CreateBoundsFit(new Vector3(-20,-1,-20),new Vector3(20,4,20));

        AssertApprox(0f,camera.Target.X);
        AssertApprox(1.5f,camera.Target.Y);
        AssertApprox(0f,camera.Target.Z);
    }

    [Fact]
    public void CreateBoundsFit_EyeOffsetFromTarget_MatchesTheSharedDiagonalDirection(){
        // Same (1,0.75,1) eye direction ModelRenderer's thumbnail camera and the VSIX
        // SceneRenderer/VorticeRenderer preview cameras already use.
        Camera camera=Camera.CreateBoundsFit(new Vector3(-1,-1,-1),new Vector3(1,1,1));

        Vector3 EyeOffset=Vector3.Normalize(camera.GetEyePosition()-camera.Target);
        Vector3 Expected=Vector3.Normalize(new Vector3(1f,0.75f,1f));

        AssertApprox(Expected.X,EyeOffset.X);
        AssertApprox(Expected.Y,EyeOffset.Y);
        AssertApprox(Expected.Z,EyeOffset.Z);
    }

    [Fact]
    public void CreateBoundsFit_DistanceIsFarEnoughThatTheWholeBoundingSphereFitsWithinTheFieldOfView(){
        Vector3 Min=new(-20,-1,-20);
        Vector3 Max=new(20,4,20);
        Camera camera=Camera.CreateBoundsFit(Min,Max);

        float Radius=Vector3.Distance(Min,Max)/2f;
        float HalfAngleToBoundingSphere=MathF.Asin(Radius/camera.Distance);

        Assert.True(HalfAngleToBoundingSphere<camera.FovYRadians/2f,
            $"Bounding sphere half-angle {HalfAngleToBoundingSphere} must be smaller than the camera's half FOV {camera.FovYRadians/2f}.");
    }

    private static void AssertApprox(float Expected,float Actual,float Tolerance=1e-4f){
        Assert.True(MathF.Abs(Expected-Actual)<=Tolerance, $"Expected {Expected} but got {Actual} (tolerance {Tolerance}).");
    }
}
