using Camera=CSharpGameEngine.Rendering.View.RenderCamera;
using CSharpGameEngine.Rendering;
using System.Numerics;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Platform.Windows;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>
/// フレーム定数は処理中のフレームスロットごとに二重化される。1度だけ設定した値（Sceneビューではプレビューレンダラーの作成時にアンビエントライトを設定する）も、
/// すべてのスロットへ届かなければならない。そうでないと連続するpresent間で設定値とスロットの既定値が交互に使われ、ビューがちらつく。
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class StageFrameSlotPersistenceTests {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void AmbientAndFogSetOnce_Warp_RenderIdenticallyInEveryFrameSlot(){
        using var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Frame Slot GPU Test",Width=96,Height=54,Visible=false });
        var Plane=ProceduralMeshFactory.CreatePlane(Width:40f,Depth:40f);
        var Tangents=new Vector4[Plane.Positions.Length];
        Array.Fill(Tangents,new Vector4(1f,0f,0f,1f));
        StageMeshCreateInfo Mesh=new(){ Positions=Plane.Positions,Normals=Plane.Normals,Tangents=Tangents,Uvs=new Vector2[Plane.Positions.Length],Indices=Plane.Indices };
        var Material=new StageMaterialCreateInfo { BaseColorFactor=new Vector4(0.6f,0.6f,0.6f,1f),RoughnessFactor=1f,MetallicFactor=0f };
        using var Renderer=StageRenderer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },ShaderArtifactPath,
            [Mesh],[Material],[new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }]);
        Assert.True(Renderer.ShaderArtifactLoaded,string.Join(Environment.NewLine,Renderer.ShaderArtifactDiagnostics));
        Renderer.SetAmbientLight(new Vector3(0.4f));
        Renderer.SetFog(new FogParams(0.05f,0.2f,new Vector3(0.5f,0.6f,0.7f)));
        List<byte[]>Frames=[];
        for(var Frame=0;Frame<4;Frame++){
            Renderer.SetCamera(Camera.CreateFirstPerson(new Vector3(0f,1.5f,-4f)));
            Renderer.SetObjectTransform(0,Matrix4x4.Identity);
            Renderer.RenderFrame(CaptureForVerification:true);
            Frames.Add(Renderer.LastCapturedFrame!.Pixels.ToArray());
        }
        string Describe(int Left,int Right){
            var Differing=0;
            var Largest=0;
            for(var Index=0;Index<Frames[Left].Length;Index++){
                var Delta=Math.Abs(Frames[Left][Index]-Frames[Right][Index]);
                if(Delta>0){ Differing++; }
                Largest=Math.Max(Largest,Delta);
            }
            return $"{Left}-{Right}: {Differing} bytes differ, max {Largest}";
        }
        var Report=string.Join("; ",Describe(0,1),Describe(0,2),Describe(1,3),Describe(2,3));
        for(var Frame=1;Frame<Frames.Count;Frame++){
            Assert.True(Frames[0].AsSpan().SequenceEqual(Frames[Frame]),$"frame {Frame} differs from frame 0 ({Report})");
        }
        Assert.DoesNotContain(Renderer.DebugMessages,static Message=>Message.IsErrorOrWorse);
    }
}
