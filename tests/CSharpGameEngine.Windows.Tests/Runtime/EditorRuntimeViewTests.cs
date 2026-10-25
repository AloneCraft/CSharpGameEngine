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
            Assert.Equal(RadialCamera.NearZ,Host.RenderCamera.NearZ);
            Assert.Equal(RadialCamera.FarZ,Host.RenderCamera.FarZ);
            Assert.Equal(0,Host.Renderer!.ErrorOrWorseMessageCount);
        }
        Assert.Equal(Game,Editor);
    }

    [WindowsOnlyFact]
    public void EditorCamera_TakesAValueEditOfTheSceneCameraOnTheNextFrame(){
        // The Scene Designer applies a value edit by replacing the component in the running world
        // (RenderPreviewRuntimeSession.ReplaceScene); the view must show it like any other value edit.
        using var Surface=CreateSurface();
        string Edited,Loaded;
        using(var Host=new EngineHost()){
            Host.UseExternalSurface(Surface.Handle,Surface.ClientWidth,Surface.ClientHeight,Warp);
            Host.SetEditorCamera(new EditorCameraPose(Near,RadialCamera.FovYRadians,0.5f,20f));
            Edited=Render(Host,SceneCamera:Far,Component:new Camera(),Frames:2,EditedComponent:BrightRadialCamera);
            Assert.Equal(BrightRadialCamera.NearZ,Host.RenderCamera.NearZ);
            Assert.Equal(BrightRadialCamera.FarZ,Host.RenderCamera.FarZ);
            Assert.Equal(BrightRadialCamera.ExposureEv100,Host.RenderCamera.ExposureEv100);
        }
        using(var Host=new EngineHost()){
            Host.UseExternalSurface(Surface.Handle,Surface.ClientWidth,Surface.ClientHeight,Warp);
            Host.SetEditorCamera(new EditorCameraPose(Near,RadialCamera.FovYRadians,0.5f,20f));
            Loaded=Render(Host,SceneCamera:Far,Component:BrightRadialCamera,Frames:2);
        }
        Assert.Equal(Loaded,Edited);
    }

    [WindowsOnlyFact]
    public void SceneCamera_TakesAValueEditOnTheNextFrameInTheGame(){
        // A game changes its camera through the component too (a zoom, an exposure change, switching to radial).
        using var Surface=CreateSurface();
        string Edited,Loaded;
        var Zoomed=BrightRadialCamera with { FovYRadians=0.5f };
        using(var Host=new EngineHost()){
            Host.UseExternalSurface(Surface.Handle,Surface.ClientWidth,Surface.ClientHeight,Warp);
            Edited=Render(Host,SceneCamera:Near,Component:new Camera(),Frames:2,EditedComponent:Zoomed);
            Assert.Equal(0.5f,Host.RenderCamera.FovYRadians);
            Assert.Equal(Zoomed.ExposureEv100,Host.RenderCamera.ExposureEv100);
        }
        using(var Host=new EngineHost()){
            Host.UseExternalSurface(Surface.Handle,Surface.ClientWidth,Surface.ClientHeight,Warp);
            Loaded=Render(Host,SceneCamera:Near,Component:Zoomed,Frames:2);
        }
        Assert.Equal(Loaded,Edited);
    }

    [WindowsOnlyTheory]
    [InlineData(nameof(Camera.FovYRadians))]
    [InlineData(nameof(Camera.NearZ))]
    [InlineData(nameof(Camera.FarZ))]
    [InlineData(nameof(Camera.ExposureEv100))]
    [InlineData(nameof(Camera.RadialTransitionMeters))]
    public void SceneCamera_KeepsTheLastUsableProjectionWhileAnEditIsInvalid(string Member){
        // Typing into the Inspector passes through values such as a zero near plane. Like the lights, the view keeps
        // the last usable projection and names the member instead of failing the frame.
        var Invalid=Member switch {
            nameof(Camera.FovYRadians)=>RadialCamera with { FovYRadians=0f },
            nameof(Camera.NearZ)=>RadialCamera with { NearZ=0f },
            nameof(Camera.FarZ)=>RadialCamera with { FarZ=RadialCamera.NearZ },
            nameof(Camera.ExposureEv100)=>RadialCamera with { ExposureEv100=float.NaN },
            _=>RadialCamera with { RadialTransitionMeters=-1f },
        };
        using var Surface=CreateSurface();
        string Edited,Loaded;
        string?Reported=null;
        using(var Host=new EngineHost()){
            Host.UseExternalSurface(Surface.Handle,Surface.ClientWidth,Surface.ClientHeight,Warp);
            Edited=Render(Host,SceneCamera:Near,Component:RadialCamera,Frames:2,EditedComponent:Invalid,
                AfterFrames:Runtime=>Reported=Runtime.InvalidCameraProperty);
            Assert.Equal(0,Host.Renderer!.ErrorOrWorseMessageCount);
        }
        using(var Host=new EngineHost()){
            Host.UseExternalSurface(Surface.Handle,Surface.ClientWidth,Surface.ClientHeight,Warp);
            Loaded=Render(Host,SceneCamera:Near,Component:RadialCamera,Frames:2,AfterFrames:Runtime=>Assert.Null(Runtime.InvalidCameraProperty));
        }
        Assert.Equal(Member,Reported);
        Assert.Equal(Loaded,Edited);
    }

    [WindowsOnlyTheory]
    [InlineData(nameof(Camera.NearZ),false)]
    [InlineData(nameof(Camera.RadialTransitionMeters),false)]
    [InlineData(nameof(Camera.NearZ),true)]
    [InlineData(nameof(Camera.RadialTransitionMeters),true)]
    public void SceneCamera_LoadedWithAnUnusableValue_NamesItAndRendersOnceItIsFixed(string Member,bool WithEditorCamera){
        // A saved scene can hold a value the renderer cannot use (a zero near plane typed into the Inspector). The load
        // names the member and keeps the default projection, as an edit on a later frame does, instead of failing the
        // load of the game or of the Scene Designer's view (whose radial range includes the scene camera's near plane).
        var Invalid=Member==nameof(Camera.NearZ)?RadialCamera with { NearZ=0f }:RadialCamera with { RadialTransitionMeters=-1f };
        using var Surface=CreateSurface();
        EngineHost NewHost(){
            var Host=new EngineHost();
            Host.UseExternalSurface(Surface.Handle,Surface.ClientWidth,Surface.ClientHeight,Warp);
            if(WithEditorCamera){
                Host.SetEditorCamera(new EditorCameraPose(Near,RadialCamera.FovYRadians,0.5f,5000f));
            }
            return Host;
        }
        string?ReportedAtLoad=null;
        string Fixed,Direct;
        using(var Host=NewHost()){
            Render(Host,SceneCamera:Near,Component:Invalid,AfterFrames:Runtime=>ReportedAtLoad=Runtime.InvalidCameraProperty);
            Assert.Equal(0,Host.Renderer!.ErrorOrWorseMessageCount);
        }
        using(var Host=NewHost()){
            Fixed=Render(Host,SceneCamera:Near,Component:Invalid,Frames:2,EditedComponent:RadialCamera,
                AfterFrames:Runtime=>Assert.Null(Runtime.InvalidCameraProperty));
        }
        using(var Host=NewHost()){
            Direct=Render(Host,SceneCamera:Near,Component:RadialCamera,Frames:2);
        }
        Assert.Equal(Member,ReportedAtLoad);
        Assert.Equal(Direct,Fixed);
    }

    [WindowsOnlyFact]
    public void EditorCamera_OnARadialSceneCamera_SeesBothItsOwnRangeAndTheScenes(){
        // M0100: radial depth covers any range, so the editor view keeps what the designer frames beyond the scene
        // camera's far plane (a zoomed-out view of a large level) and what the game sees.
        using var Surface=CreateSurface();
        using var Host=new EngineHost();
        Host.UseExternalSurface(Surface.Handle,Surface.ClientWidth,Surface.ClientHeight,Warp);
        var Scene=new Camera();
        Host.SetEditorCamera(new EditorCameraPose(Near,Scene.FovYRadians,0.5f,5000f));
        Render(Host,SceneCamera:Far,Component:Scene,Frames:2);
        Assert.Equal(Scene.NearZ,Host.RenderCamera.NearZ);
        Assert.Equal(5000f,Host.RenderCamera.FarZ);
        Assert.Equal(0,Host.Renderer!.ErrorOrWorseMessageCount);
    }

    private static Camera RadialCamera=>new() { RadialTransitionMeters=2f,NearZ=0.01f,FarZ=1e12f };
    private static Camera BrightRadialCamera=>RadialCamera with { ExposureEv100=-1.5f };

    private static GraphicsDeviceOptions Warp=>new() { UseWarp=true,EnableDebugLayer=true };

    private static Win32Window CreateSurface()=>new(new Win32WindowOptions {
        Title="CSharpGameEngine Editor Runtime View Test",Width=128,Height=128,Visible=false,
    });

    /// <summary>
    /// Loads a level with one box (and a scene camera when given) and captures one frame. <paramref name="EditedComponent"/>
    /// replaces the scene camera's component after the load, as a value edit does.
    /// </summary>
    private static string Render(EngineHost Host,Transform?SceneCamera,Camera?Component=null,int Frames=1,Camera?EditedComponent=null,
        Action<LevelRuntime>?AfterFrames=null){
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
        Entity CameraEntity=default;
        if(SceneCamera is { }CameraPose){
            var CameraTransform=CameraPose;
            var Camera=Component??new Camera();
            CameraEntity=World.Create(CameraTransform,Camera);
            Load.Update(CameraEntity,ref CameraTransform,ref Camera,ref State);
        }
        Load.Update(LevelEntity,ref App,ref AppRuntime,ref Level,ref LevelRuntime,Host,ref State);
        Assert.True(LevelRuntime.Loaded);
        Host.Present();
        if(EditedComponent is { }Edit){
            Assert.True(Host.World.TrySetComponent(CameraEntity,Edit));
        }
        for(var Frame=1;Frame<Frames;Frame++){
            // Later frames take LevelLoad's per-frame path, where the editor camera is applied again.
            Load.Update(LevelEntity,ref App,ref AppRuntime,ref Level,ref LevelRuntime,Host,ref State);
            Host.Present();
        }
        AfterFrames?.Invoke(LevelRuntime);
        Host.Renderer!.RenderFrame(CaptureForVerification:true);
        return Convert.ToHexString(SHA256.HashData(Host.Renderer.LastCapturedFrame!.Pixels));
    }
}
