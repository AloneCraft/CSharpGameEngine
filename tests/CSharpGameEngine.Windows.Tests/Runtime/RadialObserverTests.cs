using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Graphics.D3D12.Tests;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
using RenderCamera=CSharpGameEngine.Graphics.D3D12.Camera;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// M0099 Phase 6 (ADR-0076, Omni specification "Observer view"): a RadialObserver shows the world compressed around
/// the active radial camera from outside, as a camera preview inset in the game.
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class RadialObserverTests {
    private static RenderCamera RadialCenter(float FarZ=1e7f){
        var Center=RenderCamera.CreateFirstPerson(Vector3.Zero);
        Center.DepthMapping=RenderDepthMapping.Radial;
        Center.FarZ=FarZ;
        return Center;
    }
    [Fact]
    public void Preview_IsOnlyMadeForARadialCamera(){
        var Standard=RenderCamera.CreateFirstPerson(Vector3.Zero);
        Standard.DepthMapping=RenderDepthMapping.Standard;
        Assert.False(RadialObserverView.TryCreatePreview(Standard,1280,720,new RadialObserver(),new Vector3(0f,0f,-50f),Vector3.UnitZ,Vector3.UnitY,out _));
        Assert.True(RadialObserverView.TryCreatePreview(RadialCenter(),1280,720,new RadialObserver(),new Vector3(0f,0f,-50f),Vector3.UnitZ,Vector3.UnitY,out var Preview));
        Assert.Equal(RenderDepthMapping.RadialObserver,Preview.DepthMapping);
        Assert.True(Preview.MarkRadialCenter);
        Assert.Equal(Vector3.Zero,Preview.RadialCenter);
        Assert.Equal(new Vector3(0f,0f,-50f),Preview.CameraPosition);
    }
    [Fact]
    public void Preview_PlacesTheInsetAndSeesTheCompressedWorld(){
        var Observer=new RadialObserver();
        var Eye=new Vector3(0f,0f,-80f);
        Assert.True(RadialObserverView.TryCreatePreview(RadialCenter(),1000,500,Observer,Eye,Vector3.UnitZ,Vector3.UnitY,out var Preview));
        Assert.Equal((680,320,300,165),(Preview.X,Preview.Y,Preview.Width,Preview.Height));
        // The centre camera sits at the middle of the inset, and the far plane reaches past the compressed world.
        var Centre=Vector4.Transform(new Vector4(-Eye,1f),Preview.ViewProjection);
        Assert.Equal(0f,Centre.X/Centre.W,5);
        Assert.Equal(0f,Centre.Y/Centre.W,5);
        var FarSide=Vector4.Transform(new Vector4(new Vector3(0f,0f,Preview.Radial.ObservedWorldRadius)-Eye,1f),Preview.ViewProjection);
        Assert.InRange(FarSide.Z/FarSide.W,0f,1f);
    }
    [Fact]
    public void Preview_RejectsUnusableObservers(){
        Assert.False(RadialObserverView.TryCreatePreview(RadialCenter(),1280,720,new RadialObserver { NearZ=0f },Vector3.Zero,Vector3.UnitZ,Vector3.UnitY,out _));
        Assert.False(RadialObserverView.TryCreatePreview(RadialCenter(),1280,720,new RadialObserver { ViewportWidth=0f },Vector3.Zero,Vector3.UnitZ,Vector3.UnitY,out _));
        Assert.False(RadialObserverView.TryCreatePreview(RadialCenter(),1280,720,new RadialObserver { FovYRadians=MathF.PI },Vector3.Zero,Vector3.UnitZ,Vector3.UnitY,out _));
        Assert.False(RadialObserverView.TryCreatePreview(RadialCenter(),0,720,new RadialObserver(),Vector3.Zero,Vector3.UnitZ,Vector3.UnitY,out _));
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Game_ShowsOneInsetAndTheEditorKeepsItsOwnPreviews(){
        Assert.Single(RunGame(Editor:false));
        Assert.Empty(RunGame(Editor:true));
    }
    /// <summary>Loads a level with a box, a scene camera and a RadialObserver, runs two frames and returns the previews drawn.</summary>
    private static RenderCameraPreviewDiagnostics[]RunGame(bool Editor){
        using var Surface=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Radial Observer Test",Width=160,Height=90,Visible=false });
        using var Host=new EngineHost();
        Host.UseExternalSurface(Surface.Handle,Surface.ClientWidth,Surface.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true });
        var CameraPose=new Transform { Position=new LogicalVector3(0f,0.5f,-4f) };
        if(Editor){
            Host.SetEditorCamera(new EditorCameraPose(CameraPose,MathF.PI/4f,0.1f,300f));
        }
        ref var World=ref Host.World;
        var App=new AppHost();
        var AppRuntime=new AppHostRuntime();
        var Level=new Level { ShaderArtifactPath=Path.Combine("Shaders","Stage.cgeshaderartifact") };
        var LevelRuntime=new LevelRuntime();
        var LevelEntity=World.Create(App,AppRuntime,Level,LevelRuntime);
        var BoxTransform=new Transform();
        var Box=new PrimitiveMesh { Type=PrimitiveType.Box };
        var BoxEntity=World.Create(BoxTransform,Box);
        var CameraTransform=CameraPose;
        var Camera=new Camera { FarZ=1e9f };
        var CameraEntity=World.Create(CameraTransform,Camera);
        var ObserverTransform=new Transform { Position=new LogicalVector3(0f,30f,-60f) };
        var Observer=new RadialObserver();
        var ObserverEntity=World.Create(ObserverTransform,Observer);
        var State=Host.CreateState();
        var Load=new LevelLoad();
        Load.Update(BoxEntity,ref BoxTransform,ref Box,ref State);
        Load.Update(CameraEntity,ref CameraTransform,ref Camera,ref State);
        Load.Update(LevelEntity,ref App,ref AppRuntime,ref Level,ref LevelRuntime,Host,ref State);
        Assert.True(LevelRuntime.Loaded);
        for(var Frame=0;Frame<2;Frame++){
            new RadialObserverSync().Update(ObserverEntity,ref ObserverTransform,ref Observer,Host,ref State);
            Host.Present();
        }
        Assert.Equal(0,Host.Renderer!.ErrorOrWorseMessageCount);
        return Host.Renderer.GetCameraPreviewDiagnostics();
    }
}
