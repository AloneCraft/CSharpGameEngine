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
public class ModelGpuIntegrationTests
{
    private static readonly string ShaderPath = Path.Combine(AppContext.BaseDirectory, "Shaders", "Model.hlsl");
    private static readonly string BoxPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Box.glb");
    private static readonly string BoxTexturedPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "BoxTextured.glb");

    [WindowsOnlyFact]
    [Trait("Category", "GPU")]
    public void Model_Warp_DebugLayer_Verify_RendersAndPasses()
    {
        // Box.glb has no texture, only a red baseColorFactor (0.8,0,0,1) - a regression check that
        // the white-fallback-texture x factor path still renders sensibly after the texture-support changes.
        RunWithTimeout(() => RunModelGpuScenario(BoxPath, useWarp: true), TimeSpan.FromSeconds(60));
    }

    [WindowsOnlyFact]
    [Trait("Category", "GPU")]
    public void Model_BoxTextured_Warp_DebugLayer_Verify_RendersAndPasses()
    {
        RunWithTimeout(() => RunModelGpuScenario(BoxTexturedPath, useWarp: true), TimeSpan.FromSeconds(60));
    }

    private static void RunWithTimeout(Action action, TimeSpan timeout)
    {
        Task task = Task.Run(action);
        if (!task.Wait(timeout))
        {
            throw new TimeoutException($"GPU test did not complete within {timeout}.");
        }
    }

    private static void RunModelGpuScenario(string modelPath, bool useWarp)
    {
        GltfImportResult importResult = GltfImporter.Import(modelPath);
        Assert.True(importResult.Success, string.Join(Environment.NewLine, importResult.Diagnostics));
        ImportedMesh mesh = importResult.Mesh!;

        using Win32Window window = new(new Win32WindowOptions
        {
            Title = "CSharpGameEngine Model GPU Test",
            Width = 256,
            Height = 256,
            Visible = false,
        });

        GraphicsDeviceOptions deviceOptions = new()
        {
            UseWarp = useWarp,
            EnableDebugLayer = true,
        };

        using ModelRenderer renderer = ModelRenderer.Create(
            window.Handle, window.ClientWidth, window.ClientHeight, deviceOptions, ShaderPath,
            mesh.Positions, mesh.Normals, mesh.Uvs, mesh.Indices, mesh.BaseColorTextureBytes, mesh.BaseColorFactor,
            mesh.Bounds.Center, mesh.Bounds.BoundingSphereRadius);

        Assert.True(renderer.ShaderCompilationSucceeded, string.Join(Environment.NewLine, renderer.ShaderDiagnostics));
        Assert.Equal(useWarp, renderer.IsWarp);
        Assert.True(renderer.DebugLayerEnabled);

        const int totalFrames = 3;
        for (int frame = 0; frame < totalFrames; frame++)
        {
            window.PumpEvents();
            renderer.RenderFrame(captureForVerification: frame == totalFrames - 1);
        }

        Assert.Equal((uint)totalFrames, renderer.FramesRendered);
        Assert.NotNull(renderer.LastCapturedFrame);

        ModelVerificationResult verification = ModelPixelVerifier.Verify(renderer.LastCapturedFrame!, ModelRenderer.ClearColor);
        Assert.True(verification.Success, verification.Details);

        Assert.Equal(0, renderer.ErrorOrWorseMessageCount);

        // Exercises the depth-buffer-recreate-on-resize path, which has no TriangleRenderer
        // precedent (see ADR-0004).
        renderer.Resize(128, 128);
        renderer.RenderFrame(captureForVerification: true);

        Assert.NotNull(renderer.LastCapturedFrame);
        Assert.Equal(128, renderer.LastCapturedFrame!.Width);
        ModelVerificationResult resizedVerification = ModelPixelVerifier.Verify(renderer.LastCapturedFrame, ModelRenderer.ClearColor);
        Assert.True(resizedVerification.Success, resizedVerification.Details);

        Assert.Equal(0, renderer.ErrorOrWorseMessageCount);
    }
}
