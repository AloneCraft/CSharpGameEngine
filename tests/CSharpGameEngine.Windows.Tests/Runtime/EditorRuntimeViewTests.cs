using CSharpGameEngine.Rendering.Server;
using System.Security.Cryptography;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Graphics.D3D12.Tests;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// ADR-0070 B1 step 1：ランタイムはScene Designerのサーフェスとカメラを使って描画する。
/// エディターカメラもシーンカメラと同じ経路を通るため、同じ姿勢なら同じピクセルを描画する。
/// これによりデザイン時の画像がランタイムの画像と一致する。
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
        // M0099：エディタービューではシーンカメラの深度マッピングを使い、放射状深度の場合はクリップ範囲も引き継ぐ
        //（エディター独自のフォーカス基準near／farでは放射状深度の意味がなくなる）。同じ姿勢ならゲームと同じピクセルを描画する。
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
        // Scene Designerは実行中のワールドのコンポーネントを置き換えて、値の編集を反映する。
        //（RenderPreviewRuntimeSession.ReplaceScene）。ビューは他の値編集と同様に反映後の状態を表示する必要がある。
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
        // ゲームではコンポーネント経由でカメラを変更することもある（ズーム、露出変更、放射状モードへの切り替えなど）。
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
        // Inspector への入力では near plane の 0 なども渡されます。ライトの場合と同じく、ビューは最後に使用可能だった投影を保持し、
        // フレームを失敗させず、該当メンバー名を示します。
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
        // 保存済みシーンにはレンダラーが使用できない値（Inspector に入力された near plane の 0 など）が含まれることがあります。ロード時には該当メンバーを示し、既定の投影を維持します。後続フレームでの編集と同様に、ゲームや
        // Scene Designer のビュー（シーンカメラの near plane を含む radial range を持つ）のロードを失敗させません。
        // ロード処理も失敗させず、既定の投影を使用します。
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
        // M0100：radial depth は任意の範囲を扱えるため、エディターのビューはシーンカメラの far plane を超えてデザイナーがフレーミングした内容（大きなレベルをズームアウトしたビュー）も、
        // ゲームが見る内容も維持します。
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
    /// ボックスを1つ含むレベル（指定された場合はシーンカメラも含む）をロードし、1フレームをキャプチャします。<paramref name="EditedComponent"/> は、ロード後にシーンカメラのコンポーネントを編集値として置き換えます。
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
            // 後続フレームでは LevelLoad のフレームごとの処理経路に入り、エディターカメラが再度適用されます。
            Load.Update(LevelEntity,ref App,ref AppRuntime,ref Level,ref LevelRuntime,Host,ref State);
            Host.Present();
        }
        AfterFrames?.Invoke(LevelRuntime);
        Host.Renderer!.RenderFrame(CaptureForVerification:true);
        return Convert.ToHexString(SHA256.HashData(Host.Renderer.LastCapturedFrame!.Pixels));
    }
}
