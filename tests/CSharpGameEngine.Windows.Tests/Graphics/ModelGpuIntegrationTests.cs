using CSharpGameEngine.Lighting;
using System.Numerics;
using CSharpGameEngine.Import.Gltf;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Runtime.Windows;

namespace CSharpGameEngine.Graphics.D3D12.Tests;

/// <summary>
/// Exercises the real glTF import + Windows + D3D12 model-viewer vertical slice end to end. Runs
/// against the WARP software adapter so it is deterministic in CI without requiring a physical
/// GPU. Guarded by an explicit timeout so a driver/GPU hang fails the test instead of hanging the
/// run (same rationale as <see cref="TriangleGpuIntegrationTests"/>: xunit's own Timeout cannot
/// abort a hung CoreCLR thread).
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public class ModelGpuIntegrationTests {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    private static readonly string BoxPath=Path.Combine(AppContext.BaseDirectory,"Fixtures","Box.glb");
    private static readonly string BoxTexturedPath=Path.Combine(AppContext.BaseDirectory,"Fixtures","BoxTextured.glb");

    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Model_Warp_DebugLayer_Verify_RendersAndPasses(){
        // Box.glb has no texture, only a red baseColorFactor (0.8,0,0,1) - a regression check that
        // the white-fallback-texture x factor path still renders sensibly after the texture-support changes.
        RunWithTimeout(()=>RunModelGpuScenario(BoxPath,UseWarp:true),TimeSpan.FromSeconds(60));
    }

    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Model_BoxTextured_Warp_DebugLayer_Verify_RendersAndPasses(){
        RunWithTimeout(()=>RunModelGpuScenario(BoxTexturedPath,UseWarp:true),TimeSpan.FromSeconds(60));
    }

    private static void RunWithTimeout(Action Action,TimeSpan Timeout){
        Task Task=Task.Run(Action);
        if(!Task.Wait(Timeout)){
            throw new TimeoutException($"GPU test did not complete within {Timeout}.");
        }
    }

    private static void RunModelGpuScenario(string ModelPath,bool UseWarp){
        var ImportResult=GltfImporter.Import(ModelPath);
        Assert.True(ImportResult.Success,string.Join(Environment.NewLine,ImportResult.Diagnostics));
        var Mesh=ImportResult.Mesh!;
        RenderTextureResource?BaseColorTexture=
            RenderTextureArtifactAdapter.DecodeOptional(Mesh.BaseColorTextureBytes??[]);

        using Win32Window Window=new(new Win32WindowOptions {
            Title="CSharpGameEngine Model GPU Test",
            Width=256,
            Height=256,
            Visible=false,
        });

        GraphicsDeviceOptions DeviceOptions=new(){
            UseWarp=UseWarp,
            EnableDebugLayer=true,
        };

        StageMaterialCreateInfo Material=new(){
            BaseColorFactor=Mesh.BaseColorFactor,
            RoughnessFactor=1f,
            MetallicFactor=0f,
            BaseColorTexture=BaseColorTexture,
        };
        (StageMeshCreateInfo[]Meshes,StageMaterialCreateInfo[]Materials,
            StageObjectCreateInfo[]Objects)=RenderServer.BuildSingleMeshRoster(
            Mesh.Positions,Mesh.Normals,Mesh.Uvs,Mesh.Indices,Material);
        using var Renderer=RenderServer.Create(
            Window.Handle,Window.ClientWidth,Window.ClientHeight,DeviceOptions,
            ShaderArtifactPath,Meshes,Materials,Objects);
        Vector3 Extent=new(Mesh.Bounds.BoundingSphereRadius);
        Renderer.SetCamera(Camera.CreateBoundsFit(
            Mesh.Bounds.Center-Extent,Mesh.Bounds.Center+Extent));
        Renderer.SetAmbientLight(new Vector3(0.35f));
        Renderer.SetDirectionalLight(new DirectionalLightSubmission(
            Vector3.Normalize(new Vector3(1f,-1f,1f)),new LinearLightColor(1f,1f,1f),new Illuminance(50_000f),new LightShadowSettings(0,new ShadowBias(0.0015f))));

        Assert.True(Renderer.ShaderArtifactLoaded,string.Join(Environment.NewLine,Renderer.ShaderArtifactDiagnostics));
        Assert.Equal(UseWarp,Renderer.IsWarp);
        Assert.True(Renderer.DebugLayerEnabled);

        const int TotalFrames=3;
        for(var Frame=0;Frame<TotalFrames;Frame++){
            Window.PumpEvents();
            Renderer.RenderFrame(CaptureForVerification:Frame==TotalFrames-1);
        }

        Assert.Equal((uint)TotalFrames,Renderer.FramesRendered);
        Assert.NotNull(Renderer.LastCapturedFrame);

        var Verification=ModelPixelVerifier.Verify(Renderer.LastCapturedFrame!,RenderServer.ClearColor);
        Assert.True(Verification.Success,Verification.Details);

        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);

        // Exercises the depth-buffer-recreate-on-resize path, which has no TriangleRenderer
        // precedent (see ADR-0004).
        Renderer.Resize(128,128);
        Renderer.RenderFrame(CaptureForVerification:true);

        Assert.NotNull(Renderer.LastCapturedFrame);
        Assert.Equal(128,Renderer.LastCapturedFrame!.Width);
        var ResizedVerification=ModelPixelVerifier.Verify(Renderer.LastCapturedFrame,RenderServer.ClearColor);
        Assert.True(ResizedVerification.Success,ResizedVerification.Details);

        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
    }
}
