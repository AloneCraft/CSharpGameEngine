using CSharpGameEngine.Rendering.View;
using CSharpGameEngine.Rendering.Server;
using Camera=CSharpGameEngine.Rendering.View.RenderCamera;
using System.Numerics;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Runtime.Windows;

namespace CSharpGameEngine.Graphics.D3D12.Tests;

/// <summary>
/// WindowsとD3D12の垂直スライスを端から端まで実行する。物理GPUなしでもCIで決定的に実行できるよう、
/// WARPソフトウェアアダプターを使う。ドライバー／GPUが停止した場合に実行全体が固まらないよう、明示的なタイムアウトを設定する。
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public class TriangleGpuIntegrationTests {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");

    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Triangle_Warp_DebugLayer_Verify_RendersAndPasses(){
        RunWithTimeout(()=>RunTriangleGpuScenario(UseWarp:true),TimeSpan.FromSeconds(60));
    }

    private static void RunWithTimeout(Action Action,TimeSpan Timeout){
        Task Task=Task.Run(Action);
        if(!Task.Wait(Timeout)){
            throw new TimeoutException($"GPU test did not complete within {Timeout}.");
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

        StageMaterialCreateInfo Material=new(){
            BaseColorFactor=new Vector4(0.9f,0.25f,0.15f,1f),
            RoughnessFactor=1f,
            MetallicFactor=0f,
        };
        (StageMeshCreateInfo[]Meshes,StageMaterialCreateInfo[]Materials,
            StageObjectCreateInfo[]Objects)=RenderServer.BuildSingleMeshRoster(
            [new Vector3(0f,0.6f,0f),new Vector3(-0.6f,-0.6f,0f),new Vector3(0.6f,-0.6f,0f)],
            [Vector3.UnitZ,Vector3.UnitZ,Vector3.UnitZ],
            [new Vector2(0.5f,0f),new Vector2(0f,1f),new Vector2(1f,1f)],
            [0u,1u,2u],Material);
        using var Renderer=D3D12RenderServer.Create(
            Window.Handle,Window.ClientWidth,Window.ClientHeight,DeviceOptions,
            ShaderArtifactPath,Meshes,Materials,Objects);
        Renderer.SetCamera(Camera.CreateDefault(Vector3.Zero));
        Renderer.SetAmbientLight(Vector3.One);

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

        var Verification=TrianglePixelVerifier.Verify(Renderer.LastCapturedFrame!,RenderServer.ClearColor);
        Assert.True(Verification.Success,Verification.Details);

        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);

        // レンダラーを再作成せずにリサイズでき、その後も正しく描画を続けること。
        Renderer.Resize(128,128);
        Renderer.RenderFrame(CaptureForVerification:true);

        Assert.NotNull(Renderer.LastCapturedFrame);
        Assert.Equal(128,Renderer.LastCapturedFrame!.Width);
        var ResizedVerification=TrianglePixelVerifier.Verify(Renderer.LastCapturedFrame,RenderServer.ClearColor);
        Assert.True(ResizedVerification.Success,ResizedVerification.Details);

        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
    }
}
