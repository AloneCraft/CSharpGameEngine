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

    /// <summary>
    /// <b><see cref="SceneViewCamera.GetFlatRight"/>は名前に反して画面の「左」(NDC X が負)へ写る。
    /// これは意図的であり、直してはいけない。</b>
    ///
    /// <para>
    /// 基底は左手系(<c>GetFlatForward=(sinY,0,cosY)</c>、ADR-0025のUnity規約)なのに行列は右手系
    /// (<c>Matrix4x4.CreateLookAt</c>は<c>xaxis=Cross(Up,normalize(Eye-Target))=-GetFlatRight</c>を作る)
    /// という食い違いがあるため。2026-08にこれを「バグ」と判断して左手系へ組み替えたが、
    /// <b>ゲーム本体も同じ右手系</b>(<c>src/CSharpGameEngine.Windows/Graphics/Camera.cs</c>の
    /// <c>BuildViewMatrix</c>/<c>BuildProjection</c>、<c>StageRenderer</c>、<c>ModelRenderer</c>)なので、
    /// シーンデザイナーだけがゲームと左右反転してしまい差し戻した。シーンデザイナーはゲームの見た目を
    /// 再現するものなので、ゲームとの一致が判断基準になる。
    /// </para>
    ///
    /// <para>
    /// このテストは「左右が反転しているように見える」ことを不変条件として固定し、同じ誤診を
    /// 繰り返さないためにある。上下(<see cref="SceneViewCamera.GetUp"/>)は反転しておらず素直に
    /// 画面の上へ写る(右手系の<c>yaxis=Cross(zaxis,xaxis)</c>が<c>GetUp</c>と一致するため)。
    /// Fly の A/D と <c>SceneViewState.ApplyPan</c> の横方向が符号を反転させているのはこの帰結。
    /// </para>
    /// </summary>
    [Fact]
    public void GetFlatRight_ProjectsToScreenLeft_BecauseTheEngineUsesRightHandedMatrices(){
        foreach(float Yaw in Yaws){
            foreach(float Pitch in Pitches){
                Vector3 Focus=new(3f,-2f,5f);
                Matrix4x4 ViewProjection=
                    SceneViewCamera.GetView(Focus,Yaw,Pitch,FocusDistance:10f)
                    *SceneViewCamera.GetProjection(FovYRadians:1f,AspectRatio:1.5f,NearZ:0.1f,FarZ:100f);

                Vector3 RightNdc=ProjectToNdc(Focus+(SceneViewCamera.GetFlatRight(Yaw)*2f),ViewProjection);
                Vector3 UpNdc=ProjectToNdc(Focus+(SceneViewCamera.GetUp(Yaw,Pitch)*2f),ViewProjection);

                Assert.True(
                    RightNdc.X<0f,
                    $"Yaw={Yaw} Pitch={Pitch}: GetFlatRightがNDC X={RightNdc.X}。"
                    +"正になったなら行列を左手系へ変えた可能性がある — ゲーム本体と一致しなくなる(要約はGetViewのコメント)。");
                Assert.True(UpNdc.Y>0f,$"Yaw={Yaw} Pitch={Pitch}: 上方向がNDC Y={UpNdc.Y}(上下は反転しないはず)");
            }
        }
    }

    /// <summary>
    /// ゲーム本体(<c>Camera.BuildViewMatrix</c>/<c>BuildProjection</c>)と同じ行列構成であること。
    /// シーンデザイナーの見た目がゲームと一致することの根拠なので、どちらかを変えるときは
    /// 両方を一緒に変えなければならない。
    /// </summary>
    [Fact]
    public void ViewAndProjection_MatchTheRuntimeGameConstruction(){
        Vector3 Focus=new(1f,2f,3f);
        const float Yaw=0.6f;
        const float Pitch=0.3f;
        const float Distance=12f;

        Vector3 Eye=SceneViewCamera.GetEyePosition(Focus,Yaw,Pitch,Distance);

        Assert.Equal(
            Matrix4x4.CreateLookAt(Eye,Focus,Vector3.UnitY),
            SceneViewCamera.GetView(Focus,Yaw,Pitch,Distance));
        Assert.Equal(
            Matrix4x4.CreatePerspectiveFieldOfView(1f,1.5f,0.1f,100f),
            SceneViewCamera.GetProjection(1f,1.5f,0.1f,100f));
    }

    /// <summary>
    /// 深度の向き: 近い点のNDC Zが遠い点より小さいこと。左手系へ組み替えるときに
    /// 深度を壊していないことを固定する(D3D12は0..1のZを前提)。
    /// </summary>
    [Fact]
    public void NearerPointsGetSmallerNdcDepth(){
        Vector3 Focus=Vector3.Zero;
        const float Yaw=0.4f;
        const float Pitch=0.2f;
        Matrix4x4 ViewProjection=
            SceneViewCamera.GetView(Focus,Yaw,Pitch,FocusDistance:10f)
            *SceneViewCamera.GetProjection(FovYRadians:1f,AspectRatio:1.5f,NearZ:0.1f,FarZ:100f);

        Vector3 Forward=SceneViewCamera.GetForward(Yaw,Pitch);
        Vector3 Eye=SceneViewCamera.GetEyePosition(Focus,Yaw,Pitch,FocusDistance:10f);

        float NearDepth=ProjectToNdc(Eye+(Forward*1f),ViewProjection).Z;
        float FarDepth=ProjectToNdc(Eye+(Forward*50f),ViewProjection).Z;

        Assert.True(NearDepth<FarDepth,$"近{NearDepth} 遠{FarDepth}: 深度の向きが逆");
        Assert.True(NearDepth is >=0f and <=1f,$"近点のNDC Z={NearDepth}が0..1の外");
        Assert.True(FarDepth is >=0f and <=1f,$"遠点のNDC Z={FarDepth}が0..1の外");
    }

    private static Vector3 ProjectToNdc(Vector3 World,Matrix4x4 ViewProjection){
        Vector4 Clip=Vector4.Transform(new Vector4(World,1f),ViewProjection);
        return new Vector3(Clip.X,Clip.Y,Clip.Z)/Clip.W;
    }

    private static void AssertApprox(Vector3 Expected,Vector3 Actual){
        Assert.Equal(Expected.X,Actual.X,Precision);
        Assert.Equal(Expected.Y,Actual.Y,Precision);
        Assert.Equal(Expected.Z,Actual.Z,Precision);
    }
}
