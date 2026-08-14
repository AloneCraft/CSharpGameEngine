using System;
using System.Numerics;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// <see cref="SceneViewCamera"/>の閉形式が legacy
/// <c>CSharpGameEngine.Graphics.D3D12.Camera</c>(net10.0)と一致することを固定する。
/// GetForward/GetFlatRight/ApplyLookDeltaの2件は net10.0 側の
/// CameraTests.GetForward_MatchesLegacyTrigFormula_AcrossYawPitchSweep /
/// GetFlatRight_MatchesLegacyCrossProductFormula_AcrossYawSweep の複製オラクル。
/// </summary>
public sealed class SceneViewCameraTests {
    private const int Precision=4;

    private static readonly float[]Yaws=[-3.0f,-1.5f,-0.7f,0f,0.3f,0.7f,1.5f,3.0f];
    private static readonly float[]Pitches=[-1.2f,-0.5f,0f,0.3f,1.2f];

    [Fact]
    public void GetForward_MatchesLegacyTrigFormula_AcrossYawPitchSweep(){
        foreach(float Yaw in Yaws){
            foreach(float Pitch in Pitches){
                Vector3 Actual=SceneViewCamera.GetForward(Yaw,Pitch);

                var CosPitch=(float)Math.Cos(Pitch);
                Vector3 Expected=Vector3.Normalize(new Vector3(
                    CosPitch*(float)Math.Sin(Yaw),
                    (float)Math.Sin(Pitch),
                    CosPitch*(float)Math.Cos(Yaw)));

                AssertApprox(Expected,Actual);
            }
        }
    }

    [Fact]
    public void GetFlatRight_MatchesLegacyCrossProductFormula_AcrossYawSweep(){
        foreach(float Yaw in Yaws){
            Vector3 Actual=SceneViewCamera.GetFlatRight(Yaw);
            Vector3 Expected=Vector3.Normalize(Vector3.Cross(Vector3.UnitY,SceneViewCamera.GetFlatForward(Yaw)));

            AssertApprox(Expected,Actual);
        }
    }

    [Fact]
    public void GetUp_IsOrthogonalToForwardAndFlatRight_AcrossYawPitchSweep(){
        foreach(float Yaw in Yaws){
            foreach(float Pitch in Pitches){
                Vector3 Up=SceneViewCamera.GetUp(Yaw,Pitch);
                Vector3 Forward=SceneViewCamera.GetForward(Yaw,Pitch);
                Vector3 Right=SceneViewCamera.GetFlatRight(Yaw);

                Assert.Equal(0f,Vector3.Dot(Up,Forward),3);
                Assert.Equal(0f,Vector3.Dot(Up,Right),3);
                Assert.Equal(1f,Up.Length(),3);
            }
        }
    }

    [Fact]
    public void GetUp_AtZeroPitch_EqualsWorldUp(){
        Vector3 Up=SceneViewCamera.GetUp(Yaw:0.7f,Pitch:0f);

        AssertApprox(Vector3.UnitY,Up);
    }

    [Fact]
    public void GetEyePosition_EqualsFocusMinusForwardTimesDistance(){
        Vector3 Focus=new(3f,4f,5f);
        const float Yaw=0.6f;
        const float Pitch=0.2f;
        const float Distance=7f;

        Vector3 Eye=SceneViewCamera.GetEyePosition(Focus,Yaw,Pitch,Distance);

        AssertApprox(Focus-SceneViewCamera.GetForward(Yaw,Pitch)*Distance,Eye);
    }

    [Fact]
    public void GetView_AtPositiveDistance_LooksFromEyeTowardFocus(){
        Vector3 Focus=new(1f,2f,3f);
        Matrix4x4 View=SceneViewCamera.GetView(Focus,Yaw:0.4f,Pitch:0.1f,FocusDistance:5f);

        Assert.True(Matrix4x4.Invert(View,out Matrix4x4 InverseView));
        Vector3 Eye=SceneViewCamera.GetEyePosition(Focus,0.4f,0.1f,5f);
        AssertApprox(Eye,InverseView.Translation);
    }

    [Fact]
    public void ApplyLookDelta_ClampsPitchToOrbitRange(){
        (_,float Pitch)=SceneViewCamera.ApplyLookDelta(
            Yaw:0f,Pitch:0f,DeltaX:0f,DeltaY:-100000f,
            YawSensitivity:0.01f,PitchSensitivity:0.01f,InvertX:false,InvertY:false);

        Assert.Equal(SceneViewCamera.MaxPitchRadians,Pitch,4);
    }

    [Fact]
    public void ApplyLookDelta_InvertX_NegatesYawDelta(){
        (float YawNormal,_)=SceneViewCamera.ApplyLookDelta(0f,0f,10f,0f,0.01f,0.01f,InvertX:false,InvertY:false);
        (float YawInverted,_)=SceneViewCamera.ApplyLookDelta(0f,0f,10f,0f,0.01f,0.01f,InvertX:true,InvertY:false);

        Assert.Equal(-YawNormal,YawInverted,4);
    }

    [Fact]
    public void ApplyLookDelta_InvertY_NegatesPitchDelta(){
        (_,float PitchNormal)=SceneViewCamera.ApplyLookDelta(0f,0f,0f,10f,0.01f,0.01f,InvertX:false,InvertY:false);
        (_,float PitchInverted)=SceneViewCamera.ApplyLookDelta(0f,0f,0f,10f,0.01f,0.01f,InvertX:false,InvertY:true);

        Assert.Equal(-PitchNormal,PitchInverted,4);
    }

    [Fact]
    public void ScreenPointToRay_ViewportCenter_DirectionMatchesForward(){
        Vector3 Focus=new(1f,2f,3f);
        const float Yaw=0.4f;
        const float Pitch=0.15f;
        Matrix4x4 View=SceneViewCamera.GetView(Focus,Yaw,Pitch,FocusDistance:5f);
        Matrix4x4 Projection=SceneViewCamera.GetProjection((float)(Math.PI/4.0),1f,0.1f,100f);

        (_,Vector3 Direction)=SceneViewCamera.ScreenPointToRay(View,Projection,400f,300f,800f,600f);

        AssertApprox(SceneViewCamera.GetForward(Yaw,Pitch),Direction);
    }

    [Fact]
    public void ScreenPointToRay_KnownWorldPoint_RayPassesThroughIt(){
        Vector3 Focus=Vector3.Zero;
        Matrix4x4 View=SceneViewCamera.GetView(Focus,Yaw:0.3f,Pitch:0.2f,FocusDistance:10f);
        Matrix4x4 Projection=SceneViewCamera.GetProjection((float)(Math.PI/4.0),800f/600f,0.1f,100f);
        Vector3 WorldPoint=new(2f,1f,-3f);

        Vector4 Clip=Vector4.Transform(new Vector4(WorldPoint,1f),View*Projection);
        Vector3 Ndc=new Vector3(Clip.X,Clip.Y,Clip.Z)/Clip.W;
        float ScreenX=(Ndc.X+1f)/2f*800f;
        float ScreenY=(1f-Ndc.Y)/2f*600f;

        (Vector3 Origin,Vector3 Direction)=SceneViewCamera.ScreenPointToRay(View,Projection,ScreenX,ScreenY,800f,600f);

        float T=Vector3.Dot(WorldPoint-Origin,Direction);
        Vector3 ClosestPointOnRay=Origin+(Direction*T);
        Assert.Equal(0f,Vector3.Distance(ClosestPointOnRay,WorldPoint),2);
    }

    [Fact]
    public void ScreenPointToRay_Orthographic_ProducesParallelRaysAcrossScreen(){
        Matrix4x4 View=SceneViewCamera.GetView(Vector3.Zero,Yaw:0.1f,Pitch:0.1f,FocusDistance:10f);
        Matrix4x4 Projection=Matrix4x4.CreateOrthographic(10f,10f,0.1f,100f);

        (_,Vector3 DirectionA)=SceneViewCamera.ScreenPointToRay(View,Projection,100f,100f,800f,600f);
        (_,Vector3 DirectionB)=SceneViewCamera.ScreenPointToRay(View,Projection,700f,500f,800f,600f);

        AssertApprox(DirectionA,DirectionB);
    }

    private static void AssertApprox(Vector3 Expected,Vector3 Actual){
        Assert.Equal(Expected.X,Actual.X,Precision);
        Assert.Equal(Expected.Y,Actual.Y,Precision);
        Assert.Equal(Expected.Z,Actual.Z,Precision);
    }
}
