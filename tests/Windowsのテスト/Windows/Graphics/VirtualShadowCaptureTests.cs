using Xunit;
using System.Numerics;
using Camera=CSharpGameEngine.Rendering.View.RenderCamera;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Runtime.Windows;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>
/// 仮想シャドウの前後比較の撮影（M0132）。環境変数 CGE_VIRTUAL_SHADOW_CAPTURE にフォルダーがあるときだけ、広い床に近い物と遠い物（60 m・250 m）を置いた場面を、
/// 仮想シャドウとカスケード影で同じカメラ・同じ光で撮って PNG で書く。版をまたいで同じ条件で撮れるよう、公開されている描画器の入口だけを使う。
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class VirtualShadowCaptureTests {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Warp_CapturesTheComparisonScene(){
        if(System.Environment.GetEnvironmentVariable("CGE_VIRTUAL_SHADOW_CAPTURE") is not{ Length:>0 }Folder){ return; }
        Directory.CreateDirectory(Folder);
        const int Width=640;
        const int Height=360;
        using var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Virtual Shadow Capture",Width=Width,Height=Height,Visible=false });
        static StageMeshCreateInfo Mesh(ProceduralMeshData Data)=>new(){ Positions=Data.Positions,Normals=Data.Normals,
            Tangents=Enumerable.Repeat(new Vector4(1f,0f,0f,1f),Data.Positions.Length).ToArray(),Uvs=new Vector2[Data.Positions.Length],Indices=Data.Indices };
        using var Renderer=StageRenderer.Create(Window.Handle,Width,Height,new GraphicsDeviceOptions { UseWarp=true },ShaderArtifactPath,
            [Mesh(ProceduralMeshFactory.CreatePlane(Width:1200f,Depth:1200f)),Mesh(ProceduralMeshFactory.CreateBox(Vector3.One)),Mesh(ProceduralMeshFactory.CreateBox(new Vector3(0.05f,2f,0.05f)))],
            [new StageMaterialCreateInfo { BaseColorFactor=new Vector4(0.7f,0.7f,0.7f,1f),RoughnessFactor=1f,MetallicFactor=0f }],
            [new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 },new StageObjectCreateInfo { MeshIndex=1,MaterialIndex=0 },new StageObjectCreateInfo { MeshIndex=2,MaterialIndex=0 },
                new StageObjectCreateInfo { MeshIndex=1,MaterialIndex=0 },new StageObjectCreateInfo { MeshIndex=1,MaterialIndex=0 }]);
        var View=Camera.CreateFirstPerson(new Vector3(0.3f,1.7f,-6f));
        View.Pitch=-0.06f;
        foreach(var Mode in new[]{ DirectionalShadowMode.Virtual,DirectionalShadowMode.Cascaded }){
            Renderer.SetEnvironmentLighting(EnvironmentLightingSettings.Legacy with { DirectionalShadows=new(Mode) });
            for(var Frame=0;Frame<6;Frame++){
                Renderer.SetAmbientLight(new Vector3(0.25f));
                Renderer.SetCamera(View);
                Renderer.SetDirectionalLight(Vector3.Normalize(new Vector3(0.5f,0.45f,-0.35f)),Vector3.One,3f,CastsShadows:true,ShadowBiasMeters:0.005f);
                Renderer.SetObjectTransform(0,Matrix4x4.Identity);
                Renderer.SetObjectTransform(1,Matrix4x4.CreateTranslation(0.5f,0.5f,0f));
                Renderer.SetObjectTransform(2,Matrix4x4.CreateTranslation(-1.2f,1f,-2f));
                Renderer.SetObjectTransform(3,Matrix4x4.CreateScale(6f,12f,6f)*Matrix4x4.CreateTranslation(-12f,6f,60f));
                Renderer.SetObjectTransform(4,Matrix4x4.CreateScale(20f,40f,20f)*Matrix4x4.CreateTranslation(30f,20f,250f));
                Renderer.RenderFrame(Frame==5);
            }
            RuntimeAutomation.WritePng(Renderer.LastCapturedFrame!,Path.Combine(Folder,Mode==DirectionalShadowMode.Virtual?"virtual.png":"cascaded.png"));
        }
    }
}
