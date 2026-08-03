using System.Numerics;
using CSharpGameEngine.Platform.Windows;

namespace CSharpGameEngine.Graphics.D3D12.Tests;

/// <summary>
/// Exercises the real Windows + D3D12 multi-object stage renderer end to end (ground plane +
/// box "character" placeholder, movable camera, double-buffered per-frame/per-object constant
/// buffers). Runs against the WARP software adapter so it is deterministic in CI without
/// requiring a physical GPU (same rationale as <see cref="ModelGpuIntegrationTests"/>).
/// </summary>
public class StageGpuIntegrationTests
{
    private static readonly string ShaderPath = Path.Combine(AppContext.BaseDirectory, "Shaders", "Stage.hlsl");

    [WindowsOnlyFact]
    [Trait("Category", "GPU")]
    public void Stage_Warp_DebugLayer_Verify_RendersGroundAndCharacter()
    {
        RunWithTimeout(() => RunStageGpuScenario(), TimeSpan.FromSeconds(60));
    }

    [WindowsOnlyFact]
    [Trait("Category", "GPU")]
    public void Stage_Warp_ResizeMidRun_StillRendersAfterResize()
    {
        RunWithTimeout(RunStageResizeScenario, TimeSpan.FromSeconds(60));
    }

    private static void RunWithTimeout(Action action, TimeSpan timeout)
    {
        Task task = Task.Run(action);
        if (!task.Wait(timeout))
        {
            throw new TimeoutException($"GPU test did not complete within {timeout}.");
        }
    }

    private static void RunStageGpuScenario()
    {
        ProceduralMeshData ground = ProceduralMeshFactory.CreatePlane(width: 20f, depth: 20f);
        ProceduralMeshData character = ProceduralMeshFactory.CreateBox(new Vector3(1f, 2f, 1f));

        using Win32Window window = new(new Win32WindowOptions
        {
            Title = "CSharpGameEngine Stage GPU Test",
            Width = 256,
            Height = 256,
            Visible = false,
        });

        GraphicsDeviceOptions deviceOptions = new()
        {
            UseWarp = true,
            EnableDebugLayer = true,
        };

        using StageRenderer renderer = StageRenderer.Create(
            window.Handle, window.ClientWidth, window.ClientHeight, deviceOptions, ShaderPath,
            [
                new MeshCreateInfo { Positions = ground.Positions, Normals = ground.Normals, Indices = ground.Indices, BaseColorFactor = new Vector4(0.3f, 0.6f, 0.3f, 1f) },
                new MeshCreateInfo { Positions = character.Positions, Normals = character.Normals, Indices = character.Indices, BaseColorFactor = new Vector4(0.8f, 0.2f, 0.2f, 1f) },
            ]);

        Assert.True(renderer.ShaderCompilationSucceeded, string.Join(Environment.NewLine, renderer.ShaderDiagnostics));
        Assert.True(renderer.IsWarp);

        Camera camera = Camera.CreateDefault(new Vector3(0f, 1f, 0f));
        renderer.SetCamera(camera);
        renderer.SetObjectTransform(0, Matrix4x4.Identity);
        renderer.SetObjectTransform(1, Matrix4x4.CreateTranslation(0f, 1f, 0f));

        for (int i = 0; i < 3; i++)
        {
            renderer.RenderFrame(captureForVerification: i == 2);
        }

        Assert.Equal(3u, renderer.FramesRendered);
        Assert.NotNull(renderer.LastCapturedFrame);

        ModelVerificationResult verification = ModelPixelVerifier.Verify(renderer.LastCapturedFrame!, StageRenderer.ClearColor);
        Assert.True(verification.Success, verification.Details);

        Assert.Equal(0, renderer.ErrorOrWorseMessageCount);
    }

    private static void RunStageResizeScenario()
    {
        ProceduralMeshData ground = ProceduralMeshFactory.CreatePlane(width: 10f, depth: 10f);

        using Win32Window window = new(new Win32WindowOptions
        {
            Title = "CSharpGameEngine Stage Resize GPU Test",
            Width = 128,
            Height = 128,
            Visible = false,
        });

        GraphicsDeviceOptions deviceOptions = new()
        {
            UseWarp = true,
            EnableDebugLayer = true,
        };

        using StageRenderer renderer = StageRenderer.Create(
            window.Handle, window.ClientWidth, window.ClientHeight, deviceOptions, ShaderPath,
            [new MeshCreateInfo { Positions = ground.Positions, Normals = ground.Normals, Indices = ground.Indices, BaseColorFactor = Vector4.One }]);

        Camera camera = Camera.CreateDefault(Vector3.Zero);
        renderer.SetCamera(camera);
        renderer.SetObjectTransform(0, Matrix4x4.Identity);
        renderer.RenderFrame();

        renderer.Resize(256, 256);
        renderer.SetCamera(camera);
        renderer.SetObjectTransform(0, Matrix4x4.Identity);
        renderer.RenderFrame(captureForVerification: true);

        Assert.NotNull(renderer.LastCapturedFrame);
        Assert.Equal(256, renderer.LastCapturedFrame!.Width);
        Assert.Equal(256, renderer.LastCapturedFrame.Height);
        Assert.Equal(0, renderer.ErrorOrWorseMessageCount);
    }
}
