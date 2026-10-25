using System.Numerics;
using System.Security.Cryptography;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Rendering.View;
using CSharpGameEngine.Runtime.Windows;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
[Collection(D3D12GpuCollection.Name)]
public sealed class RenderViewGpuIntegrationTests {
    [WindowsOnlyTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [Trait("Category","GPU")]
    public void TypedViewMatchesLegacySubmissionAndSurvivesDeviceRecovery(int Mode){
        using var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine typed view",Width=80,Height=48,Visible=false });
        var (Meshes,Materials,Objects)=RenderServer.BuildPrimitiveRoster([PrimitiveType.Box],0,0);
        using var Server=RenderServer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,
            new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact"),Meshes,Materials,Objects);
        Assert.True(Server.DebugLayerEnabled);
        var Camera=RenderCamera.CreateDefault(new Vector3(0.1f,0.2f,0f));
        Camera.FarZ=1e18f;
        var Aspect=(float)Window.ClientWidth/Window.ClientHeight;
        var Vp=Camera.GetView()*(Mode==2?Matrix4x4.CreateOrthographic(4f*Aspect,4f,0.1f,100f):Camera.GetProjection(Aspect));
        var Parameters=new RadialViewParameters(Camera.FovYRadians,Camera.NearZ,Camera.FarZ,Camera.RadialTransitionMeters);
        var View=Mode switch {
            0=>RenderViewPolicy.FromCamera(Camera,Aspect),
            1=>RenderViewPolicy.FromPerspective(Vp,Camera.GetEyePosition(),Parameters,Aspect),
            _=>RenderViewPolicy.FromOrthographic(Vp,Camera.GetEyePosition()),
        };
        for(var Frame=0;Frame<2;Frame++){
            if(Mode==0){ Server.SetCamera(Camera,Aspect); }
            else if(Mode==1){ Server.SetViewProjection(Vp,Camera.GetEyePosition(),Parameters); }
            else { Server.SetViewProjection(Vp,Camera.GetEyePosition()); }
            Server.RenderFrame(CaptureForVerification:true);
        }
        var Expected=Hash(Server);
        Server.SetView(View);
        for(var Frame=0;Frame<3;Frame++){
            Server.RenderFrame(CaptureForVerification:true);
            Assert.Equal(Expected,Hash(Server));
        }
        Assert.Throws<ArgumentException>(()=>Server.SetView(default));
        Server.RemoveDeviceForTesting();
        Server.RenderFrame(CaptureForVerification:true);
        Assert.Equal(Expected,Hash(Server));
        Assert.Equal(2u,Server.DeviceSnapshot.Generation);
        Assert.Equal(0,Server.ErrorOrWorseMessageCount);
    }
    private static string Hash(RenderServer Server)=>Convert.ToHexString(SHA256.HashData(Assert.IsType<PixelReadbackResult>(Server.LastCapturedFrame).Pixels));
}
