using System.Numerics;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>What <see cref="DepthOracle.Measure"/> found: the largest error in ulps of the expected depth, the greatest depth written, and the adapter.</summary>
internal readonly record struct DepthMeasurement(double Centre,double Edge,float MaximumDepth,string Adapter);
/// <summary>
/// The double-precision oracle of the logarithmic depth (ADR-0080 §6, M0099/M0100): a quad facing the camera at a given
/// distance is rendered and every pixel's depth is compared with <c>log2(1 + r/T) / log2(1 + rMax/T)</c> evaluated in
/// <see cref="double"/>, where <c>r</c> is the pixel's distance along its ray. In the middle of the view the distance
/// hardly changes from pixel to pixel, so the error there is the shader arithmetic's; near the edges the rasterizer's
/// snapping of vertices to its sub-pixel grid adds error in proportion to the distance gradient.
/// </summary>
internal static class DepthOracle {
    public const int Width=160;
    public const int Height=90;
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    /// <summary>The largest error, in ulps of the expected depth, in the middle 17 × 11 pixels and in a 6–10 pixel border.</summary>
    public static DepthMeasurement Measure(bool Hardware,float Distance,float Near,float Far){
        using var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Depth Oracle",Width=Width,Height=Height,Visible=false });
        var Plane=ProceduralMeshFactory.CreatePlane(Width:1f,Depth:1f);
        var Tangents=new Vector4[Plane.Positions.Length];
        Array.Fill(Tangents,new Vector4(1f,0f,0f,1f));
        using var Renderer=StageRenderer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,
            new GraphicsDeviceOptions { UseWarp=!Hardware,EnableDebugLayer=false },ShaderArtifactPath,
            [new StageMeshCreateInfo { Positions=Plane.Positions,Normals=Plane.Normals,Tangents=Tangents,Uvs=new Vector2[Plane.Positions.Length],Indices=Plane.Indices }],
            [new StageMaterialCreateInfo { BaseColorFactor=new Vector4(1f,0f,0f,1f),RoughnessFactor=1f,MetallicFactor=0f }],
            [new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }]);
        var View=Camera.CreateFirstPerson(Vector3.Zero);
        View.NearZ=Near;
        View.FarZ=Far;
        for(var Frame=0;Frame<2;Frame++){
            Renderer.SetCamera(View);
            Renderer.SetAmbientLight(new Vector3(1f));
            Renderer.SetObjectTransform(0,Matrix4x4.CreateScale(Distance*2f,1f,Distance*2f)*Matrix4x4.CreateRotationX(-MathF.PI/2f)*Matrix4x4.CreateTranslation(0f,0f,Distance));
            Renderer.RenderFrame();
        }
        var Depth=Renderer.ReadDepthForTesting();
        Assert.True(RadialDepthProjection.TryCreate(RadialDepthCurve.Default,Near,Far,View.FovYRadians,(float)Width/Height,out var Projection));
        var MaximumG=Math.Log2(1d+Projection.FrustumCornerDistance);
        var TanHalfFov=Math.Tan(View.FovYRadians*0.5);
        double Centre=0d,Edge=0d;
        var MaximumDepth=0f;
        for(var Y=0;Y<Height;Y++){
            for(var X=0;X<Width;X++){
                var NdcX=(X+0.5)/Width*2d-1d;
                var NdcY=1d-(Y+0.5)/Height*2d;
                var Ray=Math.Sqrt(1d+NdcX*NdcX*TanHalfFov*TanHalfFov*Width*Width/(Height*(double)Height)+NdcY*NdcY*TanHalfFov*TanHalfFov);
                var Expected=Math.Log2(1d+Distance*Ray)/MaximumG;
                var Single=(float)Expected;
                var Error=Math.Abs(Depth[Y*Width+X]-Expected)/(MathF.BitIncrement(Single)-Single);
                MaximumDepth=MathF.Max(MaximumDepth,Depth[Y*Width+X]);
                if(Math.Abs(X-Width/2)<=8&&Math.Abs(Y-Height/2)<=5){ Centre=Math.Max(Centre,Error); }
                if(X<10||X>=Width-10||Y<6||Y>=Height-6){ Edge=Math.Max(Edge,Error); }
            }
        }
        return new DepthMeasurement(Centre,Edge,MaximumDepth,Hardware?Renderer.AdapterDescription:"WARP");
    }
}
