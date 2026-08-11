using CSharpGameEngine.Import.Gltf;
using CSharpGameEngine.Platform.Windows;

namespace CSharpGameEngine.Graphics.D3D12.Tests;

/// <summary>
/// Exercises the real glTF import + Windows + D3D12 model-viewer vertical slice end to end. Runs
/// against the WARP software adapter so it is deterministic in CI without requiring a physical
/// GPU. Guarded by an explicit timeout so a driver/GPU hang fails the test instead of hanging the
/// run (same rationale as <see cref="TriangleGpuIntegrationTests"/>: xunit's own Timeout cannot
/// abort a hung CoreCLR thread).
/// </summary>
public class ModelGpuIntegrationTests {
    private static readonly string ShaderPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Model.hlsl");
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

    private static void RunWithTimeout(Action action,TimeSpan timeout){
        Task task=Task.Run(action);
        if(!task.Wait(timeout)){
            throw new TimeoutException($"GPU test did not complete within {timeout}.");
        }
    }

    private static void RunModelGpuScenario(string ModelPath,bool UseWarp){
        GltfImportResult ImportResult=GltfImporter.Import(ModelPath);
        Assert.True(ImportResult.Success,string.Join(Environment.NewLine,ImportResult.Diagnostics));
        ImportedMesh Mesh=ImportResult.Mesh!;

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

        using ModelRenderer Renderer=ModelRenderer.Create(
            Window.Handle,Window.ClientWidth,Window.ClientHeight,DeviceOptions,ShaderPath,
            Mesh.Positions,Mesh.Normals,Mesh.Uvs,Mesh.Indices,Mesh.BaseColorTextureBytes,Mesh.BaseColorFactor,
            Mesh.Bounds.Center,Mesh.Bounds.BoundingSphereRadius);

        Assert.True(Renderer.ShaderCompilationSucceeded,string.Join(Environment.NewLine,Renderer.ShaderDiagnostics));
        Assert.Equal(UseWarp,Renderer.IsWarp);
        Assert.True(Renderer.DebugLayerEnabled);

        const int TotalFrames=3;
        for(var frame=0;frame<TotalFrames;frame++){
            Window.PumpEvents();
            Renderer.RenderFrame(CaptureForVerification:frame==TotalFrames-1);
        }

        Assert.Equal((uint)TotalFrames,Renderer.FramesRendered);
        Assert.NotNull(Renderer.LastCapturedFrame);

        ModelVerificationResult Verification=ModelPixelVerifier.Verify(Renderer.LastCapturedFrame!,ModelRenderer.ClearColor);
        Assert.True(Verification.Success,Verification.Details);

        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);

        // Exercises the depth-buffer-recreate-on-resize path, which has no TriangleRenderer
        // precedent (see ADR-0004).
        Renderer.Resize(128,128);
        Renderer.RenderFrame(CaptureForVerification:true);

        Assert.NotNull(Renderer.LastCapturedFrame);
        Assert.Equal(128,Renderer.LastCapturedFrame!.Width);
        ModelVerificationResult ResizedVerification=ModelPixelVerifier.Verify(Renderer.LastCapturedFrame,ModelRenderer.ClearColor);
        Assert.True(ResizedVerification.Success,ResizedVerification.Details);

        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
    }
}
