using System.Numerics;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
[Collection(D3D12GpuCollection.Name)]
public sealed class SceneDistanceGpuIntegrationTests {
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void PreviewDoesNotOverwriteMainDistancesAndResizeRecreatesTheTarget(){
        using var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Scene Distance",Width=32,Height=18,Visible=false });
        var Plane=ProceduralMeshFactory.CreatePlane(Width:1f,Depth:1f);
        using var Renderer=StageRenderer.Create(Window.Handle,32,18,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },
            Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact"),
            [new StageMeshCreateInfo { Positions=Plane.Positions,Normals=Plane.Normals,Tangents=Enumerable.Repeat(new Vector4(1f,0f,0f,1f),Plane.Positions.Length).ToArray(),Uvs=new Vector2[Plane.Positions.Length],Indices=Plane.Indices }],
            [new StageMaterialCreateInfo { BaseColorFactor=Vector4.One,RoughnessFactor=1f,MetallicFactor=0f }],
            [new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }]);
        var View=Camera.CreateFirstPerson(Vector3.Zero);
        var Transform=Matrix4x4.CreateScale(4f,1f,4f)*Matrix4x4.CreateRotationX(-MathF.PI/2f)*Matrix4x4.CreateTranslation(0f,0f,1f);
        for(var Frame=0;Frame<2;Frame++){
            Renderer.SetCamera(View);
            Renderer.SetObjectTransform(0,Transform);
            Renderer.RenderFrame();
        }
        var Before=Renderer.ReadSceneDistancesForTesting();
        Assert.All(Before,Distance=>Assert.True(Distance>0f));
        var Preview=Camera.CreateFirstPerson(new Vector3(0f,0f,-1f));
        Assert.True(RadialDepthProjection.TryCreate(RadialDepthCurve.Default,Preview.NearZ,Preview.FarZ,Preview.FovYRadians,1f,out var Projection));
        Renderer.SetCameraPreviews([new RenderCameraPreview(0,0,8,8,Preview.GetView()*Preview.GetProjection(1f),Preview.GetEyePosition(),DepthMapping:RenderDepthMapping.Radial,Radial:Projection)]);
        Renderer.SetCamera(View);
        Renderer.RenderFrame();
        Assert.True(Assert.Single(Renderer.GetCameraPreviewDiagnostics()).DrawCallCount>0);
        Assert.Equal(Before,Renderer.ReadSceneDistancesForTesting());
        Renderer.SetCameraPreviews([]);
        Renderer.Resize(24,16);
        Renderer.SetCamera(View);
        Renderer.RenderFrame();
        var Resized=Renderer.ReadSceneDistancesForTesting();
        Assert.Equal(24*16,Resized.Length);
        Assert.All(Resized,Distance=>Assert.True(Distance>0f));
        Renderer.SetEnvironmentLighting(EnvironmentLightingSettings.Legacy with { AmbientOcclusion=new AmbientOcclusionSettings(1f,0.5f) });
        Renderer.SetCamera(View);
        Renderer.RenderFrame();
        var (BeforeResize,AoWidth,AoHeight)=Renderer.ReadAmbientOcclusionForTesting();
        Renderer.Resize(25,17);
        Renderer.SetCamera(View);
        Renderer.RenderFrame();
        var (AfterResize,NewWidth,NewHeight)=Renderer.ReadAmbientOcclusionForTesting();
        Assert.Equal(AoWidth,NewWidth);
        Assert.Equal(AoHeight,NewHeight);
        Assert.Equal(BeforeResize.Length,AfterResize.Length);
        Assert.All(AfterResize,Value=>Assert.InRange(Value,0f,1f));
        Assert.All(Renderer.ReadSceneDistancesForTesting(),Distance=>Assert.True(Distance>0f));
        Assert.DoesNotContain(Renderer.DebugMessages,Message=>Message.IsErrorOrWorse);
    }
}
