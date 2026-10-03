using System.Numerics;
using System.Security.Cryptography;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Lighting;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Runtime.Windows;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>
/// M0099 Phase 1: the standard depth mode's frames are pinned as SHA-256 hashes of the WARP back buffer before the
/// radial depth mode is added, so no later change to the shared Stage program, FrameCB or pipeline states can move
/// a standard pixel unnoticed. The cases cover every pass a standard camera uses: typed lights with shadow atlas
/// tiles, legacy fog, the physical environment (sky, sky light, physical fog, sun cascades, GI, AO, filmic
/// exposure), the world-space grid, the presentation modes, camera thumbnails and the overlay.
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class StandardModeGoldenFrameTests {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    private static readonly LinearLightColor White=new(1f,1f,1f);
    private static readonly LightShadowSettings Shadow=new(0,new ShadowBias(0.005f));
    private static readonly Dictionary<string,string>ExpectedHashes=new(){
        ["legacy-lit"]="3F7940D2345F87022E7050E7CE31C31E88AF18E6AB0984189692CE9313905A64",
        ["environment"]="E16BAAAA34D474B4A683B2187C1988F2D57947F53BCC259482BF0775E7E62415",
        ["selection"]="2A65C34872C6E0953D0FD8303FADA29936B02A8F85C35CFE9A68033A702C7273",
        ["base-color"]="C1321971CCBB47E60800E36084AE550109FF63243897D5E62C8C56B448FAB7A9",
        ["normal"]="A0C9623A15D73265623CDE7E30591B9B6A20ECEE459931067C3F381668B25880",
        ["wireframe"]="25DB6AB16B2F38481923D1F40989AF0E24C128E201731C12764A82FD2BAB82E7",
        ["previews-and-overlay"]="A823A16303399452564DC404B26FD046F66D3361662F3E9709BA19A8D1932102",
    };
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void StandardFrames_MatchTheirPinnedHashes(){
        List<string>Mismatches=[];
        foreach(var (Case,Expected) in ExpectedHashes){
            var Hash=Render(Case);
            if(Hash!=Expected){
                Mismatches.Add($"[\"{Case}\"]=\"{Hash}\",");
            }
        }
        Assert.True(Mismatches.Count==0,string.Join(System.Environment.NewLine,Mismatches));
    }
    private static string Render(string Case){
        using var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Standard Golden Frame",Width=192,Height=108,Visible=false });
        StageMeshCreateInfo ToMesh(ProceduralMeshData Data){
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
        using var Renderer=RenderServer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,
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
                    var ThumbnailViewProjection=Thumbnail.GetView()*Thumbnail.GetProjection(16f/9f);
                    Renderer.SetCameraPreviews([
                        new RenderCameraPreview(4,4,64,36,ThumbnailViewProjection,new Vector3(-3f,2.5f,-2f)),
                        new RenderCameraPreview(124,68,64,36,View.GetView()*View.GetProjection(16f/9f),new Vector3(0.3f,1.6f,-4f),ExcludedObjectIndex:1),
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
        return Convert.ToHexString(SHA256.HashData(Renderer.LastCapturedFrame!.Pixels));
    }
}
