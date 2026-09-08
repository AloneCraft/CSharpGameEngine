using System.Numerics;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Platform.Windows;

namespace CSharpGameEngine.Graphics.D3D12.Tests;

/// <summary>WARP regressions for the native one-record rectangle Area local-light path.</summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class AreaLightGpuIntegrationTests {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");

    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Stage_Warp_PhysicalAreaLight_IsOneSidedAndScalesWithRectangleArea(){
        RunWithTimeout(RunOneSidedAreaScenario,TimeSpan.FromSeconds(60));
    }

    private static void RunOneSidedAreaScenario(){
        var Ground=ProceduralMeshFactory.CreatePlane(Width:16f,Depth:16f);
        using Win32Window Window=new(new Win32WindowOptions {
            Title="CSharpGameEngine Area Light GPU Test",
            Width=128,
            Height=128,
            Visible=false,
        });

        GraphicsDeviceOptions DeviceOptions=new(){ UseWarp=true,EnableDebugLayer=true };
        var GroundUvs=new Vector2[Ground.Positions.Length];
        var GroundTangents=new Vector4[Ground.Positions.Length];
        for(var I=0;I<Ground.Positions.Length;I++){
            GroundTangents[I]=new Vector4(1f,0f,0f,1f);
        }
        StageMeshCreateInfo GroundMesh=new(){
            Positions=Ground.Positions,Normals=Ground.Normals,Tangents=GroundTangents,
            Uvs=GroundUvs,Indices=Ground.Indices,
        };
        StageMaterialCreateInfo Material=new(){
            BaseColorFactor=Vector4.One,RoughnessFactor=1f,MetallicFactor=0f,
        };

        using var Renderer=StageRenderer.Create(
            Window.Handle,Window.ClientWidth,Window.ClientHeight,DeviceOptions,ShaderArtifactPath,
            [GroundMesh],[Material],[new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }]);
        Assert.True(Renderer.ShaderArtifactLoaded,string.Join(Environment.NewLine,Renderer.ShaderArtifactDiagnostics));

        Camera Camera=Camera.CreateDefault(Vector3.Zero);
        Camera.Pitch=-89f*MathF.PI/180f;
        Camera.Distance=20f;
        Camera.FarZ=200f;

        PixelColor RenderCenter(Vector3 Forward,float WidthMeters,float HeightMeters){
            for(var I=0;I<3;I++){
                Renderer.SetCamera(Camera);
                Renderer.SetAmbientLight(Vector3.Zero);
                Renderer.SetDirectionalLight(Vector3.UnitY,Vector3.Zero,0f);
                Renderer.SetObjectTransform(0,Matrix4x4.Identity);
                Renderer.AddAreaLight(
                    Position:new Vector3(0f,6f,0f),LightForward:Forward,LinearRgb:Vector3.One,
                    LuminanceNits:20f,WidthMeters:WidthMeters,HeightMeters:HeightMeters,RangeMeters:20f,
                    CastsShadows:false,ShadowPriority:0,ShadowBiasMeters:0f);
                Renderer.RenderFrame(CaptureForVerification:I==2);
            }
            PixelReadbackResult Frame=Assert.IsType<PixelReadbackResult>(Renderer.LastCapturedFrame);
            return ReadPixel(Frame,Frame.Width/2,Frame.Height/2);
        }

        PixelColor Front=RenderCenter(-Vector3.UnitY,1f,1f);
        PixelColor Back=RenderCenter(Vector3.UnitY,1f,1f);
        PixelColor Larger=RenderCenter(-Vector3.UnitY,4f,4f);
        int FrontSum=Front.R+Front.G+Front.B;
        int BackSum=Back.R+Back.G+Back.B;
        int LargerSum=Larger.R+Larger.G+Larger.B;
        Assert.True(FrontSum>BackSum+15,
            $"Expected a one-sided Area light to illuminate only its front: front={Front} (sum={FrontSum}), back={Back} (sum={BackSum}).");
        Assert.True(LargerSum>FrontSum+15,
            $"Expected a larger rectangle to produce more irradiance at the same receiver: 1m²={Front} (sum={FrontSum}), 16m²={Larger} (sum={LargerSum}).");
        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
    }

    private static void RunWithTimeout(Action Action,TimeSpan Timeout){
        Task Task=Task.Run(Action);
        if(!Task.Wait(Timeout)){
            throw new TimeoutException($"GPU test did not complete within {Timeout}.");
        }
    }

    private static PixelColor ReadPixel(PixelReadbackResult Readback,int X,int Y){
        const int BytesPerPixel=4;
        long Offset=((long)Y*Readback.RowPitch)+(X*BytesPerPixel);
        return new PixelColor(
            Readback.Pixels[Offset],Readback.Pixels[Offset+1],Readback.Pixels[Offset+2],Readback.Pixels[Offset+3]);
    }
}
