using CSharpGameEngine.Rendering.View;
using CSharpGameEngine.Rendering.Server;
using Camera=CSharpGameEngine.Rendering.View.RenderCamera;
using System.Numerics;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Lighting;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Runtime.Windows;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>
/// The scene behind the pinned golden frames (M0099 Phase 1, M0119). The cases cover every pass a perspective camera
/// uses: typed lights with shadow atlas tiles, legacy fog, the physical environment (sky, sky light, physical fog, sun
/// cascades, GI, AO, filmic exposure), the world-space grid, the presentation modes, camera thumbnails and the overlay.
/// </summary>
internal static class GoldenFrameScene {
    public static readonly string[]Cases=["legacy-lit","environment","selection","base-color","normal","wireframe","previews-and-overlay"];
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    private static readonly LinearLightColor White=new(1f,1f,1f);
    private static readonly LightShadowSettings Shadow=new(0,new ShadowBias(0.005f));
    private static StageMeshCreateInfo ToMesh(ProceduralMeshData Data){
        // A tangent perpendicular to each normal keeps the TBN valid on every face.
        var Tangents=new Vector4[Data.Positions.Length];
        for(var Index=0;Index<Tangents.Length;Index++){
            var Normal=Data.Normals[Index];
            var Reference=MathF.Abs(Normal.Y)<0.99f?Vector3.UnitY:Vector3.UnitX;
            Tangents[Index]=new Vector4(Vector3.Normalize(Vector3.Cross(Reference,Normal)),1f);
        }
        var Uvs=new Vector2[Data.Positions.Length];
        for(var Index=0;Index<Uvs.Length;Index++){
            Uvs[Index]=new Vector2(Data.Positions[Index].X,Data.Positions[Index].Z)*0.5f;
        }
        return new StageMeshCreateInfo { Positions=Data.Positions,Normals=Data.Normals,Tangents=Tangents,Uvs=Uvs,Indices=Data.Indices };
    }
    /// <summary>A thumbnail pass of <paramref name="View"/> in the rectangle; a radial one gets the projection its rectangle's aspect ratio gives.</summary>
    private static RenderCameraPreview Preview(int X,int Y,Camera View,Vector3 Eye,int ExcludedObjectIndex=-1){
        const float Aspect=16f/9f;
        var ViewProjection=View.GetView()*View.GetProjection(Aspect);
        Assert.True(RadialDepthProjection.TryCreate(RadialDepthCurve.Default,View.NearZ,View.FarZ,View.FovYRadians,Aspect,out var Projection));
        return new RenderCameraPreview(X,Y,64,36,ViewProjection,Eye,RenderViewDepth.ForPerspective(Projection,ConservativeRasterDepth:false),ExcludedObjectIndex);
    }
    /// <summary>Renders one native logarithmic case with its camera thumbnails.</summary>
    public static PixelReadbackResult Render(string Case){
        using var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Golden Frame",Width=192,Height=108,Visible=false });
        StageMeshCreateInfo[]Meshes=[
            ToMesh(ProceduralMeshFactory.CreatePlane(Width:16f,Depth:16f)),
            ToMesh(ProceduralMeshFactory.CreateBox(new Vector3(1f,1f,1f))),
            ToMesh(ProceduralMeshFactory.CreateSphere(0.5f)),
            ToMesh(ProceduralMeshFactory.CreateBox(new Vector3(8f,4f,0.2f))),
        ];
        StageMaterialCreateInfo[]Materials=[
            new StageMaterialCreateInfo { BaseColorFactor=new Vector4(0.18f,0.18f,0.18f,1f),RoughnessFactor=1f,MetallicFactor=0f,
                WorldGrid=new StageWorldGrid(1f,5,5f/512f,new Vector3(0.026f),new Vector3(0.0885f)) },
            new StageMaterialCreateInfo { BaseColorFactor=new Vector4(0.8f,0.1f,0.1f,1f),RoughnessFactor=0.6f,MetallicFactor=0f },
            new StageMaterialCreateInfo { BaseColorFactor=new Vector4(0.9f,0.8f,0.5f,1f),RoughnessFactor=0.3f,MetallicFactor=1f },
            new StageMaterialCreateInfo { BaseColorFactor=new Vector4(0.6f,0.6f,0.65f,1f),RoughnessFactor=0.9f,MetallicFactor=0f },
        ];
        StageObjectCreateInfo[]Objects=[
            new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 },
            new StageObjectCreateInfo { MeshIndex=1,MaterialIndex=1 },
            new StageObjectCreateInfo { MeshIndex=2,MaterialIndex=2 },
            new StageObjectCreateInfo { MeshIndex=3,MaterialIndex=3 },
        ];
        using var Renderer=D3D12RenderServer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,
            new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },ShaderArtifactPath,Meshes,Materials,Objects);
        var View=Camera.CreateFirstPerson(new Vector3(0.3f,1.6f,-4f));
        View.Pitch=-12f*MathF.PI/180f;
        var Environment=Case=="environment";
        var Frames=Environment?4:2;
        for(var Frame=0;Frame<Frames;Frame++){
            Renderer.SetCamera(View);
            if(Environment){
                Renderer.SetEnvironmentLighting(new EnvironmentLightingSettings(
                    SkyAtmosphereSettings.Earth,new SkyLightSettings(1f,Vector3.Zero),
                    new PhysicalHeightFogSettings(0.02f,0.2f,0f,new Vector3(0.4f,0.5f,0.6f),1f,1f),
                    new ExposureSettings(ExposureMode.PhysicalCamera,14f,0f),ToneMapper.UnrealFilmicSrgb,0.675f,0.0064f,
                    new GlobalIlluminationSettings(new Vector3(0f,1.6f,-0.5f),new Vector3(4f,1.4f,3.5f),2f,64,0.5f,1f),
                    new AmbientOcclusionSettings(1f,0.5f)));
                Renderer.SetCameraAltitude(1.6f);
                Renderer.SetDirectionalLight(new DirectionalLightSubmission(Vector3.Normalize(new Vector3(0.4f,-0.8f,0.5f)),White,new(100000f),Shadow));
            } else {
                Renderer.SetAmbientLight(new Vector3(0.08f));
                Renderer.SetFog(new FogParams(0.03f,0.3f,new Vector3(0.5f,0.55f,0.6f)));
                Renderer.AddDirectionalLight(new DirectionalLightSubmission(Vector3.Normalize(new Vector3(0.4f,-0.8f,0.5f)),White,new(3f),Shadow));
                Renderer.AddPointLight(new PointLightSubmission(new Vector3(-1.5f,1.2f,-1f),new(1f,0.5f,0.2f),new(600f),new(8f),Shadow));
                Renderer.AddSpotLight(new SpotLightSubmission(new Vector3(1.5f,2.5f,-1.5f),Vector3.Normalize(new Vector3(-0.5f,-1f,1f)),new(0.3f,1f,0.4f),new(900f),new(10f),new(20f,35f),Shadow));
                Renderer.AddRectAreaLight(new RectAreaLightSubmission(new Vector3(0f,2f,1.8f),-Vector3.UnitZ,Vector3.UnitY,new(0.4f,0.5f,1f),new(200f),new(1.5f,0.8f),new(8f),Shadow));
            }
            Renderer.SetObjectTransform(0,Matrix4x4.Identity);
            Renderer.SetObjectTransform(1,Matrix4x4.CreateRotationY(0.5f)*Matrix4x4.CreateTranslation(-0.8f,0.5f,0f));
            Renderer.SetObjectTransform(2,Matrix4x4.CreateTranslation(0.9f,0.5f,-0.4f));
            Renderer.SetObjectTransform(3,Matrix4x4.CreateTranslation(0f,2f,2f));
            switch(Case){
                case "selection":
                    Renderer.SetObjectSelectionWeight(1,1f);
                    break;
                case "base-color":
                    Renderer.SetPresentationMode(RenderPresentationMode.BaseColor);
                    break;
                case "normal":
                    Renderer.SetPresentationMode(RenderPresentationMode.Normal);
                    break;
                case "wireframe":
                    Renderer.SetPresentationMode(RenderPresentationMode.Wireframe);
                    break;
                case "previews-and-overlay":
                    var Thumbnail=Camera.CreateFirstPerson(new Vector3(-3f,2.5f,-2f));
                    Thumbnail.Yaw=0.9f;
                    Thumbnail.Pitch=-0.4f;
                    Renderer.SetCameraPreviews([
                        Preview(4,4,Thumbnail,new Vector3(-3f,2.5f,-2f)),
                        Preview(124,68,View,new Vector3(0.3f,1.6f,-4f),ExcludedObjectIndex:1),
                    ]);
                    RenderOverlayVertex[]Triangles=[
                        new(new Vector3(-0.8f,1.2f,0f),new Vector4(1f,0.8f,0f,0.6f)),
                        new(new Vector3(-0.3f,1.2f,0f),new Vector4(1f,0.8f,0f,0.6f)),
                        new(new Vector3(-0.55f,1.6f,0f),new Vector4(1f,0.8f,0f,0.6f)),
                    ];
                    RenderOverlayVertex[]Lines=[
                        new(new Vector3(0.9f,0.5f,-0.4f),new Vector4(1f,0f,0f,1f)),new(new Vector3(1.9f,0.5f,-0.4f),new Vector4(1f,0f,0f,1f)),
                        new(new Vector3(0.9f,0.5f,-0.4f),new Vector4(0f,1f,0f,1f)),new(new Vector3(0.9f,1.5f,-0.4f),new Vector4(0f,1f,0f,1f)),
                        new(new Vector3(0.9f,0.5f,-0.4f),new Vector4(0f,0f,1f,1f)),new(new Vector3(0.9f,0.5f,0.6f),new Vector4(0f,0f,1f,1f)),
                    ];
                    Renderer.SetOverlay(Triangles,Lines);
                    break;
            }
            Renderer.RenderFrame(CaptureForVerification:Frame==Frames-1);
        }
        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
        return Renderer.LastCapturedFrame!;
    }
}
