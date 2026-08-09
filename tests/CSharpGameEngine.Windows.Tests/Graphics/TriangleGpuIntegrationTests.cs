using CSharpGameEngine.Platform.Windows;

namespace CSharpGameEngine.Graphics.D3D12.Tests;

/// <summary>
/// Exercises the real Windows + D3D12 vertical slice end to end. Runs against the WARP software
/// adapter so it is deterministic in CI without requiring a physical GPU. Guarded by an explicit
/// timeout so a driver/GPU hang fails the test instead of hanging the run.
/// </summary>
public class TriangleGpuIntegrationTests
{
    private static readonly string ShaderPath = Path.Combine(AppContext.BaseDirectory, "Shaders", "Triangle.hlsl");

    [WindowsOnlyFact]
    [Trait("Category", "GPU")]
    public void Triangle_Warp_DebugLayer_Verify_RendersAndPasses()
    {
        RunWithTimeout(() => RunTriangleGpuScenario(useWarp: true), TimeSpan.FromSeconds(60));
    }

    private static void RunWithTimeout(Action action, TimeSpan timeout)
    {
        Task task = Task.Run(action);
        if (!task.Wait(timeout))
        {
            throw new TimeoutException($"GPU test did not complete within {timeout}.");
        }
    }

    private static void RunTriangleGpuScenario(bool useWarp)
    {
        using Win32Window window = new(new Win32WindowOptions
        {
            Title = "CSharpGameEngine GPU Test",
            Width = 256,
            Height = 256,
            Visible = false,
        });

        GraphicsDeviceOptions deviceOptions = new()
        {
            UseWarp = useWarp,
            EnableDebugLayer = true,
        };

        using TriangleRenderer renderer = TriangleRenderer.Create(window.Handle, window.ClientWidth, window.ClientHeight, deviceOptions, ShaderPath);

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

        TriangleVerificationResult verification = TrianglePixelVerifier.Verify(renderer.LastCapturedFrame!, TriangleRenderer.ClearColor);
        Assert.True(verification.Success, verification.Details);

        Assert.Equal(0, renderer.ErrorOrWorseMessageCount);

        // Resize must not require recreating the renderer, and rendering must continue correctly afterward.
        renderer.Resize(128, 128);
        renderer.RenderFrame(captureForVerification: true);

        Assert.NotNull(renderer.LastCapturedFrame);
        Assert.Equal(128, renderer.LastCapturedFrame!.Width);
        TriangleVerificationResult resizedVerification = TrianglePixelVerifier.Verify(renderer.LastCapturedFrame, TriangleRenderer.ClearColor);
        Assert.True(resizedVerification.Success, resizedVerification.Details);

        Assert.Equal(0, renderer.ErrorOrWorseMessageCount);
    }
}
