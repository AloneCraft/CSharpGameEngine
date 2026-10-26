using Camera=CSharpGameEngine.Rendering.View.RenderCamera;
using System.Numerics;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary><see cref="DepthOracle.Measure"/>が求めた結果。期待深度からの最大ULP誤差、書き込まれた最大深度、使用アダプター。</summary>
internal readonly record struct DepthMeasurement(double Centre,double Edge,float MaximumDepth,string Adapter,double DistanceRelativeError);
/// <summary>
/// 対数深度の倍精度オラクル（ADR-0084 §6、M0099／M0100）。カメラへ向いたクアッドを指定距離に描画し、
/// 各ピクセルの深度を、<see cref="double"/>で評価した<c>log2(1 + r/T) / log2(1 + rMax/T)</c>と比較する。
/// ここで<c>r</c>はレイに沿ったピクセルの距離である。ビュー中央ではピクセル間の距離差が小さいため、誤差は主にシェーダー演算に由来する。
/// 端付近では、ラスターライザーが頂点をサブピクセル格子へスナップすることで、距離勾配に比例した誤差が加わる。
/// </summary>
internal static class DepthOracle {
    public const int Width=160;
    public const int Height=90;
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    /// <summary>中央17 × 11ピクセルと、幅6～10ピクセルの境界内で、期待深度からの最大ULP誤差。</summary>
    public static DepthMeasurement Measure(bool Hardware,float Distance,float Near,float Far,bool UseFactory=true){
        using var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Depth Oracle",Width=Width,Height=Height,Visible=false });
        var Plane=ProceduralMeshFactory.CreatePlane(Width:1f,Depth:1f);
        var Tangents=new Vector4[Plane.Positions.Length];
        Array.Fill(Tangents,new Vector4(1f,0f,0f,1f));
        using var Renderer=StageRenderer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,
            new GraphicsDeviceOptions { UseWarp=!Hardware,EnableDebugLayer=false },ShaderArtifactPath,
            [new StageMeshCreateInfo { Positions=Plane.Positions,Normals=Plane.Normals,Tangents=Tangents,Uvs=new Vector2[Plane.Positions.Length],Indices=Plane.Indices }],
            [new StageMaterialCreateInfo { BaseColorFactor=new Vector4(1f,0f,0f,1f),RoughnessFactor=1f,MetallicFactor=0f }],
            [new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }]);
        var View=UseFactory?Camera.CreateFirstPerson(Vector3.Zero):new Camera { Target=Vector3.Zero,Up=Vector3.UnitY,FovYRadians=MathF.PI/4f };
        View.NearZ=Near;
        View.FarZ=Far;
        for(var Frame=0;Frame<2;Frame++){
            Renderer.SetCamera(View);
            Renderer.SetAmbientLight(new Vector3(1f));
            Renderer.SetObjectTransform(0,Matrix4x4.CreateScale(Distance*2f,1f,Distance*2f)*Matrix4x4.CreateRotationX(-MathF.PI/2f)*Matrix4x4.CreateTranslation(0f,0f,Distance));
            Renderer.RenderFrame();
        }
        var Depth=Renderer.ReadDepthForTesting();
        var Distances=Renderer.ReadSceneDistancesForTesting();
        Assert.True(RadialDepthProjection.TryCreate(RadialDepthCurve.Default,Near,Far,View.FovYRadians,(float)Width/Height,out var Projection));
        var MaximumG=Math.Log2(1d+Projection.FrustumCornerDistance);
        var TanHalfFov=Math.Tan(View.FovYRadians*0.5);
        double Centre=0d,Edge=0d,DistanceRelativeError=0d;
        var MaximumDepth=0f;
        for(var Y=0;Y<Height;Y++){
            for(var X=0;X<Width;X++){
                var NdcX=(X+0.5)/Width*2d-1d;
                var NdcY=1d-(Y+0.5)/Height*2d;
                var Ray=Math.Sqrt(1d+NdcX*NdcX*TanHalfFov*TanHalfFov*Width*Width/(Height*(double)Height)+NdcY*NdcY*TanHalfFov*TanHalfFov);
                var PhysicalDistance=Distance*Ray;
                DistanceRelativeError=Math.Max(DistanceRelativeError,Math.Abs(Distances[Y*Width+X]-PhysicalDistance)/PhysicalDistance);
                var Expected=Math.Log2(1d+PhysicalDistance)/MaximumG;
                var Single=(float)Expected;
                var Error=Math.Abs(Depth[Y*Width+X]-Expected)/(MathF.BitIncrement(Single)-Single);
                MaximumDepth=MathF.Max(MaximumDepth,Depth[Y*Width+X]);
                if(Math.Abs(X-Width/2)<=8&&Math.Abs(Y-Height/2)<=5){ Centre=Math.Max(Centre,Error); }
                if(X<10||X>=Width-10||Y<6||Y>=Height-6){ Edge=Math.Max(Edge,Error); }
            }
        }
        return new DepthMeasurement(Centre,Edge,MaximumDepth,Hardware?Renderer.AdapterDescription:"WARP",DistanceRelativeError);
    }
}
