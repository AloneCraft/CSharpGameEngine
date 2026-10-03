using System.Security.Cryptography;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Graphics.D3D12.Tests;
using CSharpGameEngine.Platform.Windows;
namespace CSharpGameEngine.Runtime.Windows.Tests;
[Collection(D3D12GpuCollection.Name)]
public sealed class AmbientLightSyncTests {
    [WindowsOnlyTheory]
    [InlineData(nameof(AmbientLight.Intensity))]
    [InlineData(nameof(AmbientLight.ColorG))]
    [InlineData("Overflow")]
    public void UnusableValue_KeepsTheLastUsableAmbient(string Case){
        // Like the typed lights and the camera, a value the renderer cannot use keeps the last usable one. A negative
        // ambient used to reach the shader, where the tone mapper turns negative radiance white.
        var Valid=new AmbientLight { ColorR=0.4f,ColorG=0.5f,ColorB=0.6f,Intensity=0.8f };
        var Invalid=Case switch {
            nameof(AmbientLight.Intensity)=>Valid with { Intensity=-1f },
            nameof(AmbientLight.ColorG)=>Valid with { ColorG=-0.5f },
            _=>Valid with { ColorR=10f,Intensity=3e38f },
        };
        var Edited=Render(Valid,Invalid);
        var Kept=Render(Valid,Valid);
        Assert.Equal(Kept,Edited);
    }

    private static string Render(AmbientLight First,AmbientLight Then){
        using var Surface=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Ambient Light Test",Width=128,Height=128,Visible=false });
        using var Host=new EngineHost();
        Host.UseExternalSurface(Surface.Handle,Surface.ClientWidth,Surface.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true });
        ref var World=ref Host.World;
        var App=new AppHost();
        var AppRuntime=new AppHostRuntime();
        var Level=new Level { ShaderArtifactPath=Path.Combine("Shaders","Stage.cgeshaderartifact") };
        var LevelRuntime=new LevelRuntime();
        var LevelEntity=World.Create(App,AppRuntime,Level,LevelRuntime);
        var BoxTransform=new Transform();
        var Box=new PrimitiveMesh { Type=PrimitiveType.Box };
        var BoxEntity=World.Create(BoxTransform,Box);
        var State=Host.CreateState();
        var Load=new LevelLoad();
        Load.Update(BoxEntity,ref BoxTransform,ref Box,ref State);
        var CameraTransform=new Transform { Position=new LogicalVector3(0f,0.5f,-4f) };
        var Camera=new Camera();
        var CameraEntity=World.Create(CameraTransform,Camera);
        Load.Update(CameraEntity,ref CameraTransform,ref Camera,ref State);
        Load.Update(LevelEntity,ref App,ref AppRuntime,ref Level,ref LevelRuntime,Host,ref State);
        var Ambient=First;
        new AmbientLightSync().Update(ref Ambient,Host,ref State);
        Host.Present();
        Ambient=Then;
        new AmbientLightSync().Update(ref Ambient,Host,ref State);
        Host.Present();
        Host.Renderer!.RenderFrame(CaptureForVerification:true);
        Assert.Equal(0,Host.Renderer.ErrorOrWorseMessageCount);
        return Convert.ToHexString(SHA256.HashData(Host.Renderer.LastCapturedFrame!.Pixels));
    }
}
