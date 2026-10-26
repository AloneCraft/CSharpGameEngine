using Camera=CSharpGameEngine.Rendering.View.RenderCamera;
using CSharpGameEngine.Rendering;
using System.Numerics;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Platform.Windows;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>ディレクショナルライトの影が光源と反対側へ落ちることを検証するWARPオラクル。</summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class DirectionalShadowGpuTests {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Stage_Warp_OverheadSun_ShadowsGroundBelowOccluderOnly(){
        var Ground=ProceduralMeshFactory.CreatePlane(Width:20f,Depth:20f);
        var Box=ProceduralMeshFactory.CreateBox(new Vector3(2f,0.2f,2f));
        using var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Directional Shadow Test",Width=128,Height=128,Visible=false });
        StageMeshCreateInfo ToMesh(ProceduralMeshData Mesh){
            var Tangents=new Vector4[Mesh.Positions.Length];
            Array.Fill(Tangents,new Vector4(1f,0f,0f,1f));
            return new StageMeshCreateInfo { Positions=Mesh.Positions,Normals=Mesh.Normals,Tangents=Tangents,Uvs=new Vector2[Mesh.Positions.Length],Indices=Mesh.Indices };
        }
        StageMaterialCreateInfo Material=new(){ BaseColorFactor=Vector4.One,RoughnessFactor=1f,MetallicFactor=0f };
        using var Renderer=StageRenderer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },ShaderArtifactPath,
            [ToMesh(Ground),ToMesh(Box)],[Material],
            [new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 },new StageObjectCreateInfo { MeshIndex=1,MaterialIndex=0 }]);
        Assert.True(Renderer.ShaderArtifactLoaded,string.Join(Environment.NewLine,Renderer.ShaderArtifactDiagnostics));
        var View=Camera.CreateDefault(Vector3.Zero);
        View.Pitch=-89f*MathF.PI/180f;
        View.Distance=12f;
        View.FarZ=100f;
        for(var Frame=0;Frame<3;Frame++){
            Renderer.SetCamera(View);
            Renderer.SetAmbientLight(new Vector3(0.05f));
            // 光は上方かつ片側から来る（光源方向は上向きで+X方向も指す）。そのため、
            // 遮蔽物の影は横へずれ、上方のカメラに見える地面に落ちる。
            Renderer.SetDirectionalLight(Vector3.Normalize(new Vector3(0.6f,1f,0f)),Vector3.One,3f,CastsShadows:true,ShadowBiasMeters:0.01f);
            Renderer.SetObjectTransform(0,Matrix4x4.Identity);
            Renderer.SetObjectTransform(1,Matrix4x4.CreateTranslation(0f,2f,0f));
            Renderer.RenderFrame(CaptureForVerification:Frame==2);
        }
        var Capture=Renderer.LastCapturedFrame!;
        int Luma(int X,int Y){
            var Offset=(int)(Y*Capture.RowPitch)+X*4;
            return Capture.Pixels[Offset]+Capture.Pixels[Offset+1]+Capture.Pixels[Offset+2];
        }
        // 遮蔽物の上面と奥の地面は、いずれも上を向くため日光が当たる。
        var Far=Luma(10,64);
        var OccluderTop=Luma(64,64);
        // 影は遮蔽物の横、光源と反対側（画面上のどちらか一方）に落ちる。
        var Beside=Math.Min(Luma(40,64),Luma(88,64));
        var Diagnostic=$"far={Far} top={OccluderTop} beside={Beside} left={Luma(40,64)} right={Luma(88,64)}";
        Assert.True(Far>60,"Ground far from the occluder must be sunlit. "+Diagnostic);
        Assert.True(OccluderTop>Far*0.8,"Nothing is above the occluder, so its top must be sunlit. "+Diagnostic);
        Assert.True(Beside<Far*0.6,"The occluder must cast a shadow onto the ground away from the light. "+Diagnostic);
    }
}
