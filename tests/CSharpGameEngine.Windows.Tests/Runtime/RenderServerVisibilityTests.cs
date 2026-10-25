using System.Numerics;
using System.Security.Cryptography;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Graphics.D3D12.Tests;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Lighting;
using CSharpGameEngine.Rendering;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// ADR-0078/M0107: where an object is drawn. An object can be kept out of the camera's picture (UE's OwnerNoSee, the first-person body) while it still casts its
/// shadow, or be seen without casting one (the first-person arms). The choice is recorded with the frame, so it survives a device loss.
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class RenderServerVisibilityTests {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    private const int Floor=0;
    private const int Caster=1;
    private static RenderServer CreateServer(Win32Window Window){
        // One mesh per primitive type, in the order the types first appear: the plane is mesh 0 and the box mesh 1.
        (var Meshes,var Materials,var Objects)=RenderServer.BuildPrimitiveRoster([PrimitiveType.Plane,PrimitiveType.Box],0,0);
        return RenderServer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },ShaderArtifactPath,Meshes,Materials,Objects);
    }
    private static (string Hash,int Covered)Frame(RenderServer Renderer,bool CasterVisible=true,bool CasterCastsShadow=true,bool Recovery=false){
        Renderer.SetCamera(CSharpGameEngine.Rendering.View.RenderCamera.CreateDefault(Vector3.Zero));
        Renderer.SetAmbientLight(new Vector3(0.2f));
        Renderer.SetDirectionalLight(new DirectionalLightSubmission(Vector3.Normalize(new Vector3(0.3f,-1f,0.5f)),new LinearLightColor(1f,1f,1f),new Illuminance(3f),new LightShadowSettings(0,new ShadowBias(0.0015f))));
        Renderer.SetObjectTransform(Floor,Matrix4x4.CreateScale(8f)*Matrix4x4.CreateTranslation(0f,0f,0f));
        Renderer.SetObjectTransform(Caster,Matrix4x4.CreateScale(1f)*Matrix4x4.CreateTranslation(0f,1.6f,0f));
        Renderer.SetObjectVisibility(Caster,CasterVisible,CasterCastsShadow);
        if(Recovery){
            Renderer.RemoveDeviceForTesting();
        }
        Renderer.RenderFrame(CaptureForVerification:true);
        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
        var Captured=Renderer.LastCapturedFrame!;
        var Covered=0;
        for(var Y=0;Y<Captured.Height;Y++){
            for(var X=0;X<Captured.Width;X++){
                if(!Captured.Pixels.AsSpan(Y*(int)Captured.RowPitch+X*4,4).SequenceEqual(Captured.Pixels.AsSpan(0,4))){ Covered++; }
            }
        }
        return(Convert.ToHexString(SHA256.HashData(Captured.Pixels)),Covered);
    }
    private static void RunWithTimeout(Action Action){
        var Task=System.Threading.Tasks.Task.Run(Action);
        if(!Task.Wait(TimeSpan.FromSeconds(120))){ throw new TimeoutException("GPU test did not complete in time."); }
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void AnObjectKeptOutOfTheViewIsNotDrawnButStillCastsItsShadow()=>RunWithTimeout(()=>{
        using var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Visibility Test",Width=128,Height=128,Visible=false });
        using var Renderer=CreateServer(Window);
        var Everything=Frame(Renderer);
        var BodyHidden=Frame(Renderer,CasterVisible:false);
        var NoCaster=Frame(Renderer,CasterVisible:false,CasterCastsShadow:false);
        var BodyWithoutShadow=Frame(Renderer,CasterCastsShadow:false);
        // Hidden but casting: the box itself is gone from the picture, its shadow on the floor is not.
        Assert.NotEqual(Everything.Hash,BodyHidden.Hash);
        Assert.NotEqual(BodyHidden.Hash,NoCaster.Hash);
        // Seen but not casting: the box is drawn and the floor has no shadow of it.
        Assert.NotEqual(Everything.Hash,BodyWithoutShadow.Hash);
        Assert.NotEqual(BodyWithoutShadow.Hash,NoCaster.Hash);
        // Both off is the floor alone, whichever way it is reached; and turning them back on restores the first picture.
        Assert.True(NoCaster.Covered>0);
        Assert.Equal(Everything.Hash,Frame(Renderer).Hash);
    });
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void ThePictureOfAHiddenObjectIsTheFloorWithItsShadowAndSurvivesADeviceLoss()=>RunWithTimeout(()=>{
        using var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Visibility Recovery Test",Width=128,Height=128,Visible=false });
        using var Renderer=CreateServer(Window);
        var Hidden=Frame(Renderer,CasterVisible:false);
        var Recovered=Frame(Renderer,CasterVisible:false,Recovery:true);
        Assert.Equal(2u,Renderer.DeviceSnapshot.Generation);
        Assert.Equal(Hidden.Hash,Recovered.Hash);
        // A new object starts visible and casting, even in the slot a hidden one gave up.
        Renderer.RemoveObject(Caster);
        var Reused=Renderer.AddObject(1,0);
        Assert.Equal(Caster,Reused);
        Renderer.SetObjectTransform(Reused,Matrix4x4.CreateTranslation(0f,1.6f,0f));
        Renderer.RenderFrame(CaptureForVerification:true);
        Assert.Equal(Frame(Renderer).Hash,Convert.ToHexString(SHA256.HashData(Renderer.LastCapturedFrame!.Pixels)));
    });
}
