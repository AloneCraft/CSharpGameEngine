using System.Security.Cryptography;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Graphics.D3D12.Tests;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// ADR-0070 B1 step 1: the runtime renders into the Scene Designer's surface and from its camera.
/// The editor camera goes through the scene camera's own path, so from the same pose it renders the
/// same pixels; that is what makes the design-time image the runtime image.
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class EditorRuntimeViewTests {
    private static readonly Transform Near=new() { Position=new LogicalVector3(0f,0.5f,-4f) };
    private static readonly Transform Far=new() { Position=new LogicalVector3(3f,2f,-9f) };

    [WindowsOnlyFact]
    public void ExternalSurface_LoadsTheLevelIntoTheGivenWindowWithoutCreatingOne(){
        using var Surface=CreateSurface();
        using var Host=new EngineHost();
        Host.UseExternalSurface(Surface.Handle,Surface.ClientWidth,Surface.ClientHeight,Warp);

        var Hash=Render(Host,SceneCamera:Near);

        Assert.Null(Host.Window);
        Assert.NotNull(Host.Renderer);
        Assert.Equal(0,Host.Renderer!.ErrorOrWorseMessageCount);
        var Verification=ModelPixelVerifier.Verify(Host.Renderer.LastCapturedFrame!,RenderServer.ClearColor);
        Assert.True(Verification.Success,Verification.Details);
        Assert.NotEmpty(Hash);
        Assert.Throws<InvalidOperationException>(()=>Host.UseExternalSurface(Surface.Handle,64,64,Warp));
    }

    [WindowsOnlyFact]
    public void EditorCamera_RendersWhatTheSceneCameraRendersFromTheSamePose(){
        using var Surface=CreateSurface();
        string SceneCameraNear,SceneCameraFar,EditorNear;
        using(var Host=new EngineHost()){
            Host.UseExternalSurface(Surface.Handle,Surface.ClientWidth,Surface.ClientHeight,Warp);
            SceneCameraNear=Render(Host,SceneCamera:Near);
        }
        using(var Host=new EngineHost()){
            Host.UseExternalSurface(Surface.Handle,Surface.ClientWidth,Surface.ClientHeight,Warp);
            SceneCameraFar=Render(Host,SceneCamera:Far);
        }
        using(var Host=new EngineHost()){
            Host.UseExternalSurface(Surface.Handle,Surface.ClientWidth,Surface.ClientHeight,Warp);
            var Scene=new Camera();
            Host.SetEditorCamera(new EditorCameraPose(Near,Scene.FovYRadians,Scene.NearZ,Scene.FarZ));
            EditorNear=Render(Host,SceneCamera:Far);
        }

        Assert.NotEqual(SceneCameraNear,SceneCameraFar);
        Assert.Equal(SceneCameraNear,EditorNear);
    }

    [WindowsOnlyFact]
    public void EditorCamera_NeedsNoSceneCamera(){
        using var Surface=CreateSurface();
        using var Host=new EngineHost();
        Host.UseExternalSurface(Surface.Handle,Surface.ClientWidth,Surface.ClientHeight,Warp);
        var Scene=new Camera();
        Host.SetEditorCamera(new EditorCameraPose(Near,Scene.FovYRadians,Scene.NearZ,Scene.FarZ));

        Assert.NotEmpty(Render(Host,SceneCamera:null));
        Assert.Equal(0,Host.Renderer!.ErrorOrWorseMessageCount);
    }

    [WindowsOnlyFact]
    public void RadialSceneCamera_RendersTheGameWithRadialDepthAndItsOwnClipRange(){
        using var Surface=CreateSurface();
        using var Host=new EngineHost();
        Host.UseExternalSurface(Surface.Handle,Surface.ClientWidth,Surface.ClientHeight,Warp);
        Render(Host,SceneCamera:Near,Component:RadialCamera);
        Assert.Equal(RenderDepthMapping.Radial,Host.RenderCamera.DepthMapping);
        Assert.Equal(RadialCamera.NearZ,Host.RenderCamera.NearZ);
        Assert.Equal(RadialCamera.FarZ,Host.RenderCamera.FarZ);
        Assert.Equal(2f,Host.RenderCamera.RadialTransitionMeters);
        Assert.Equal(0,Host.Renderer!.ErrorOrWorseMessageCount);
    }

    [WindowsOnlyFact]
    public void EditorCamera_FollowsARadialSceneCameraAndRendersWhatItRendersFromTheSamePose(){
        // M0099: the editor view takes the scene camera's depth mapping and, for radial depth, its clip range (the
        // editor's own focus-based near/far would undo radial depth), so from the same pose it renders the game's pixels.
        using var Surface=CreateSurface();
        string Game,Editor;
        using(var Host=new EngineHost()){
            Host.UseExternalSurface(Surface.Handle,Surface.ClientWidth,Surface.ClientHeight,Warp);
            Game=Render(Host,SceneCamera:Near,Component:RadialCamera);
        }
        using(var Host=new EngineHost()){
            Host.UseExternalSurface(Surface.Handle,Surface.ClientWidth,Surface.ClientHeight,Warp);
            Host.SetEditorCamera(new EditorCameraPose(Near,RadialCamera.FovYRadians,0.5f,20f));
            Editor=Render(Host,SceneCamera:Far,Component:RadialCamera,Frames:2);
            Assert.Equal(RenderDepthMapping.Radial,Host.RenderCamera.DepthMapping);
            Assert.Equal(RadialCamera.NearZ,Host.RenderCamera.NearZ);
            Assert.Equal(RadialCamera.FarZ,Host.RenderCamera.FarZ);
            Assert.Equal(0,Host.Renderer!.ErrorOrWorseMessageCount);
        }
        Assert.Equal(Game,Editor);
    }

    private static Camera RadialCamera=>new() { DepthMapping=CameraDepthMapping.Radial,RadialTransitionMeters=2f,NearZ=0.01f,FarZ=1e12f };

    private static GraphicsDeviceOptions Warp=>new() { UseWarp=true,EnableDebugLayer=true };

    private static Win32Window CreateSurface()=>new(new Win32WindowOptions {
        Title="CSharpGameEngine Editor Runtime View Test",Width=128,Height=128,Visible=false,
    });

    /// <summary>Loads a level with one box (and a scene camera when given) and captures one frame.</summary>
    private static string Render(EngineHost Host,Transform?SceneCamera,Camera?Component=null,int Frames=1){
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
        if(SceneCamera is { }CameraPose){
            var CameraTransform=CameraPose;
            var Camera=Component??new Camera();
            var CameraEntity=World.Create(CameraTransform,Camera);
            Load.Update(CameraEntity,ref CameraTransform,ref Camera,ref State);
        }
        Load.Update(LevelEntity,ref App,ref AppRuntime,ref Level,ref LevelRuntime,Host,ref State);
        Assert.True(LevelRuntime.Loaded);
        Host.Present();
        for(var Frame=1;Frame<Frames;Frame++){
            // Later frames take LevelLoad's per-frame path, where the editor camera is applied again.
            Load.Update(LevelEntity,ref App,ref AppRuntime,ref Level,ref LevelRuntime,Host,ref State);
            Host.Present();
        }
        Host.Renderer!.RenderFrame(CaptureForVerification:true);
        return Convert.ToHexString(SHA256.HashData(Host.Renderer.LastCapturedFrame!.Pixels));
    }
}
