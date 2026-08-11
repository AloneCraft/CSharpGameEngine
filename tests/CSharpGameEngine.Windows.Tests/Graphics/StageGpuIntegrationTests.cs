using System.Numerics;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Platform.Windows;

namespace CSharpGameEngine.Graphics.D3D12.Tests;

/// <summary>
/// Exercises the real Windows + D3D12 multi-object stage renderer end to end (ground plane +
/// box "character" placeholder, movable camera, double-buffered per-frame/per-object constant
/// buffers). Runs against the WARP software adapter so it is deterministic in CI without
/// requiring a physical GPU (same rationale as <see cref="ModelGpuIntegrationTests"/>).
/// </summary>
public class StageGpuIntegrationTests {
    private static readonly string ShaderPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.hlsl");

    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Stage_Warp_DebugLayer_Verify_RendersGroundAndCharacter(){
        RunWithTimeout(()=>RunStageGpuScenario(),TimeSpan.FromSeconds(60));
    }

    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Stage_Warp_ResizeMidRun_StillRendersAfterResize(){
        RunWithTimeout(RunStageResizeScenario,TimeSpan.FromSeconds(60));
    }

    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Stage_Warp_TwoObjectsWithDifferentMaterials_EachSamplesItsOwnDescriptorTableSlot(){
        RunWithTimeout(RunTwoMaterialsScenario,TimeSpan.FromSeconds(60));
    }

    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Stage_Warp_DirectionalLightAndAmbientChangeRenderedColor(){
        RunWithTimeout(RunLightingApiScenario,TimeSpan.FromSeconds(60));
    }

    private static void RunWithTimeout(Action action,TimeSpan timeout){
        Task task=Task.Run(action);
        if(!task.Wait(timeout)){
            throw new TimeoutException($"GPU test did not complete within {timeout}.");
        }
    }

    private static void RunStageGpuScenario(){
        ProceduralMeshData Ground=ProceduralMeshFactory.CreatePlane(Width:20f,Depth:20f);
        ProceduralMeshData Character=ProceduralMeshFactory.CreateBox(new Vector3(1f,2f,1f));

        using Win32Window Window=new(new Win32WindowOptions {
            Title="CSharpGameEngine Stage GPU Test",
            Width=256,
            Height=256,
            Visible=false,
        });

        GraphicsDeviceOptions DeviceOptions=new(){
            UseWarp=true,
            EnableDebugLayer=true,
        };

        using StageRenderer Renderer=StageRenderer.Create(
            Window.Handle,Window.ClientWidth,Window.ClientHeight,DeviceOptions,ShaderPath,
            [
                new MeshCreateInfo { Positions=Ground.Positions,Normals=Ground.Normals,Indices=Ground.Indices,BaseColorFactor=new Vector4(0.3f,0.6f,0.3f,1f)},
                new MeshCreateInfo { Positions=Character.Positions,Normals=Character.Normals,Indices=Character.Indices,BaseColorFactor=new Vector4(0.8f,0.2f,0.2f,1f)},
            ]);

        Assert.True(Renderer.ShaderCompilationSucceeded,string.Join(Environment.NewLine,Renderer.ShaderDiagnostics));
        Assert.True(Renderer.IsWarp);

        Camera camera=Camera.CreateDefault(new Vector3(0f,1f,0f));
        Renderer.SetCamera(camera);
        Renderer.SetObjectTransform(0,Matrix4x4.Identity);
        Renderer.SetObjectTransform(1,Matrix4x4.CreateTranslation(0f,1f,0f));

        for(var i=0;i<3;i++){
            Renderer.RenderFrame(CaptureForVerification:i==2);
        }

        Assert.Equal(3u,Renderer.FramesRendered);
        Assert.NotNull(Renderer.LastCapturedFrame);

        ModelVerificationResult Verification=ModelPixelVerifier.Verify(Renderer.LastCapturedFrame!,StageRenderer.ClearColor);
        Assert.True(Verification.Success,Verification.Details);

        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
    }

    private static void RunStageResizeScenario(){
        ProceduralMeshData Ground=ProceduralMeshFactory.CreatePlane(Width:10f,Depth:10f);

        using Win32Window Window=new(new Win32WindowOptions {
            Title="CSharpGameEngine Stage Resize GPU Test",
            Width=128,
            Height=128,
            Visible=false,
        });

        GraphicsDeviceOptions DeviceOptions=new(){
            UseWarp=true,
            EnableDebugLayer=true,
        };

        using StageRenderer Renderer=StageRenderer.Create(
            Window.Handle,Window.ClientWidth,Window.ClientHeight,DeviceOptions,ShaderPath,
            [new MeshCreateInfo { Positions=Ground.Positions,Normals=Ground.Normals,Indices=Ground.Indices,BaseColorFactor=Vector4.One }]);

        Camera camera=Camera.CreateDefault(Vector3.Zero);
        Renderer.SetCamera(camera);
        Renderer.SetObjectTransform(0,Matrix4x4.Identity);
        Renderer.RenderFrame();

        Renderer.Resize(256,256);
        Renderer.SetCamera(camera);
        Renderer.SetObjectTransform(0,Matrix4x4.Identity);
        Renderer.RenderFrame(CaptureForVerification:true);

        Assert.NotNull(Renderer.LastCapturedFrame);
        Assert.Equal(256,Renderer.LastCapturedFrame!.Width);
        Assert.Equal(256,Renderer.LastCapturedFrame.Height);
        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
    }

    private static void RunTwoMaterialsScenario(){
        ProceduralMeshData Box=ProceduralMeshFactory.CreateBox(new Vector3(1.2f,1.2f,1.2f));

        using Win32Window Window=new(new Win32WindowOptions {
            Title="CSharpGameEngine Stage Multi-Material GPU Test",
            Width=256,
            Height=256,
            Visible=false,
        });

        GraphicsDeviceOptions DeviceOptions=new(){ UseWarp=true,EnableDebugLayer=true };

        var Uvs=new Vector2[Box.Positions.Length];
        var Tangents=new Vector4[Box.Positions.Length];
        for(var i=0;i<Box.Positions.Length;i++){
            Tangents[i]=new Vector4(1,0,0,1);
        }

        StageMeshCreateInfo Mesh=new(){ Positions=Box.Positions,Normals=Box.Normals,Tangents=Tangents,Uvs=Uvs,Indices=Box.Indices };
        StageMaterialCreateInfo RedMaterial=new(){ BaseColorFactor=new Vector4(1,0,0,1),RoughnessFactor=1f,MetallicFactor=0f };
        StageMaterialCreateInfo BlueMaterial=new(){ BaseColorFactor=new Vector4(0,0,1,1),RoughnessFactor=1f,MetallicFactor=0f };

        using StageRenderer Renderer=StageRenderer.Create(
            Window.Handle,Window.ClientWidth,Window.ClientHeight,DeviceOptions,ShaderPath,
            [Mesh],
            [RedMaterial,BlueMaterial],
            [
                new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 },
                new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=1 },
            ]);

        Assert.True(Renderer.ShaderCompilationSucceeded,string.Join(Environment.NewLine,Renderer.ShaderDiagnostics));

        Camera camera=Camera.CreateDefault(Vector3.Zero);
        Renderer.SetCamera(camera);
        Renderer.SetAmbientLight(new Vector3(0.4f,0.4f,0.4f));
        Renderer.SetDirectionalLight(new Vector3(0.3f,0.8f,0.4f),Vector3.One,2f);
        Renderer.SetObjectTransform(0,Matrix4x4.CreateTranslation(-1.0f,0f,0f));
        Renderer.SetObjectTransform(1,Matrix4x4.CreateTranslation(1.0f,0f,0f));

        for(var i=0;i<3;i++){
            Renderer.RenderFrame(CaptureForVerification:i==2);
        }

        Assert.NotNull(Renderer.LastCapturedFrame);
        PixelReadbackResult Frame=Renderer.LastCapturedFrame!;

        var FoundReddish=false;
        var FoundBluish=false;
        for(var y=0;y<Frame.Height&&!(FoundReddish&&FoundBluish);y++){
            for(var x=0;x<Frame.Width;x++){
                PixelColor Pixel=ReadPixel(Frame,x,y);
                if(Pixel.R>Pixel.G+15&&Pixel.R>Pixel.B+15){
                    FoundReddish=true;
                }
                if(Pixel.B>Pixel.R+15&&Pixel.B>Pixel.G+15){
                    FoundBluish=true;
                }
            }
        }

        Assert.True(FoundReddish,"Expected at least one red-dominant pixel from the object using materialIndex 0.");
        Assert.True(FoundBluish,"Expected at least one blue-dominant pixel from the object using materialIndex 1.");
        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
    }

    private static void RunLightingApiScenario(){
        ProceduralMeshData Ground=ProceduralMeshFactory.CreatePlane(Width:20f,Depth:20f);

        using Win32Window Window=new(new Win32WindowOptions {
            Title="CSharpGameEngine Stage Lighting API GPU Test",
            Width=128,
            Height=128,
            Visible=false,
        });

        GraphicsDeviceOptions DeviceOptions=new(){ UseWarp=true,EnableDebugLayer=true };

        var Uvs=new Vector2[Ground.Positions.Length];
        var Tangents=new Vector4[Ground.Positions.Length];
        for(var i=0;i<Ground.Positions.Length;i++){
            Tangents[i]=new Vector4(1,0,0,1);
        }

        StageMeshCreateInfo Mesh=new(){ Positions=Ground.Positions,Normals=Ground.Normals,Tangents=Tangents,Uvs=Uvs,Indices=Ground.Indices };
        StageMaterialCreateInfo Material=new(){ BaseColorFactor=Vector4.One,RoughnessFactor=1f,MetallicFactor=0f };

        using StageRenderer Renderer=StageRenderer.Create(
            Window.Handle,Window.ClientWidth,Window.ClientHeight,DeviceOptions,ShaderPath,
            [Mesh],[Material],[new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }]);

        Camera camera=Camera.CreateDefault(Vector3.Zero);
        Renderer.SetCamera(camera);
        Renderer.SetObjectTransform(0,Matrix4x4.Identity);

        Renderer.SetAmbientLight(Vector3.Zero);
        Renderer.SetDirectionalLight(Vector3.UnitY,Vector3.Zero,0f);
        Renderer.RenderFrame();
        Renderer.RenderFrame();
        Renderer.RenderFrame(CaptureForVerification:true);
        PixelColor DarkCenter=ReadPixel(Renderer.LastCapturedFrame!,Renderer.LastCapturedFrame!.Width/2,Renderer.LastCapturedFrame!.Height/2);

        Renderer.SetCamera(camera);
        Renderer.SetAmbientLight(new Vector3(0.3f,0.3f,0.3f));
        Renderer.SetDirectionalLight(Vector3.UnitY,Vector3.One,3f);
        Renderer.SetObjectTransform(0,Matrix4x4.Identity);
        Renderer.RenderFrame();
        Renderer.RenderFrame();
        Renderer.RenderFrame(CaptureForVerification:true);
        PixelColor BrightCenter=ReadPixel(Renderer.LastCapturedFrame!,Renderer.LastCapturedFrame!.Width/2,Renderer.LastCapturedFrame!.Height/2);

        var DarkSum=DarkCenter.R+DarkCenter.G+DarkCenter.B;
        var BrightSum=BrightCenter.R+BrightCenter.G+BrightCenter.B;
        Assert.True(DarkSum<15, $"Expected a near-black center pixel with zero light/ambient, got {DarkCenter} (sum={DarkSum}).");
        Assert.True(BrightSum>DarkSum+200, $"Expected SetDirectionalLight/SetAmbientLight to meaningfully brighten the center pixel: dark={DarkCenter}, bright={BrightCenter}.");
        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
    }

    private static PixelColor ReadPixel(PixelReadbackResult Readback,int X,int Y){
        const int BytesPerPixel=4;
        var Offset=((long)Y*Readback.RowPitch)+(X*BytesPerPixel);
        return new PixelColor(Readback.Pixels[Offset],Readback.Pixels[Offset+1],Readback.Pixels[Offset+2],Readback.Pixels[Offset+3]);
    }
}
