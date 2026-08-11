using CSharpGameEngine.Platform.Windows;

namespace CSharpGameEngine.Graphics.D3D12.Tests;

/// <summary>
/// Exercises the real Windows + D3D12 vertical slice end to end. Runs against the WARP software
/// adapter so it is deterministic in CI without requiring a physical GPU. Guarded by an explicit
/// timeout so a driver/GPU hang fails the test instead of hanging the run.
/// </summary>
public class TriangleGpuIntegrationTests {
    private static readonly string ShaderPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Triangle.hlsl");

    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Triangle_Warp_DebugLayer_Verify_RendersAndPasses(){
        RunWithTimeout(()=>RunTriangleGpuScenario(UseWarp:true),TimeSpan.FromSeconds(60));
    }

    private static void RunWithTimeout(Action action,TimeSpan timeout){
        Task task=Task.Run(action);
        if(!task.Wait(timeout)){
            throw new TimeoutException($"GPU test did not complete within {timeout}.");
        }
    }

    private static void RunTriangleGpuScenario(bool UseWarp){
        using Win32Window Window=new(new Win32WindowOptions {
            Title="CSharpGameEngine GPU Test",
            Width=256,
            Height=256,
            Visible=false,
        });

        GraphicsDeviceOptions DeviceOptions=new(){
            UseWarp=UseWarp,
            EnableDebugLayer=true,
        };

        using TriangleRenderer Renderer=TriangleRenderer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,DeviceOptions,ShaderPath);

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

        TriangleVerificationResult Verification=TrianglePixelVerifier.Verify(Renderer.LastCapturedFrame!,TriangleRenderer.ClearColor);
        Assert.True(Verification.Success,Verification.Details);

        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);

        // Resize must not require recreating the renderer, and rendering must continue correctly afterward.
        Renderer.Resize(128,128);
        Renderer.RenderFrame(CaptureForVerification:true);

        Assert.NotNull(Renderer.LastCapturedFrame);
        Assert.Equal(128,Renderer.LastCapturedFrame!.Width);
        TriangleVerificationResult ResizedVerification=TrianglePixelVerifier.Verify(Renderer.LastCapturedFrame,TriangleRenderer.ClearColor);
        Assert.True(ResizedVerification.Success,ResizedVerification.Details);

        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
    }
}
