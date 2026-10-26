using Camera=CSharpGameEngine.Rendering.View.RenderCamera;
using System.Numerics;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>
/// Native logarithmic depth edges: presentation frame slots, microscopic scales, curve updates, resizing and an observer inset.
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class RadialDepthEdgeCaseTests {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    private static StageMeshCreateInfo ToMesh(ProceduralMeshData Data){
        var Tangents=new Vector4[Data.Positions.Length];
        for(var Index=0;Index<Tangents.Length;Index++){
            var Normal=Data.Normals[Index];
            var Reference=MathF.Abs(Normal.Y)<0.99f?Vector3.UnitY:Vector3.UnitX;
            Tangents[Index]=new Vector4(Vector3.Normalize(Vector3.Cross(Reference,Normal)),1f);
        }
        var Uvs=new Vector2[Data.Positions.Length];
        for(var Index=0;Index<Uvs.Length;Index++){
            Uvs[Index]=new Vector2(Data.Positions[Index].X,Data.Positions[Index].Y)+new Vector2(0.5f);
        }
        return new StageMeshCreateInfo { Positions=Data.Positions,Normals=Data.Normals,Tangents=Tangents,Uvs=Uvs,Indices=Data.Indices };
    }
    /// <summary>A box and a sphere wholly inside the view, in front of a wall: nothing is clipped.</summary>
    private static StageRenderer CreateScene(Win32Window Window)=>StageRenderer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,
        new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },ShaderArtifactPath,
        [ToMesh(ProceduralMeshFactory.CreateBox(new Vector3(1f,1f,1f))),ToMesh(ProceduralMeshFactory.CreateSphere(0.5f)),ToMesh(ProceduralMeshFactory.CreateBox(new Vector3(6f,3f,0.2f)))],
        [new StageMaterialCreateInfo { BaseColorFactor=new Vector4(0.8f,0.2f,0.1f,1f),RoughnessFactor=0.6f,MetallicFactor=0f },
         new StageMaterialCreateInfo { BaseColorFactor=new Vector4(0.9f,0.8f,0.5f,1f),RoughnessFactor=0.3f,MetallicFactor=1f },
         new StageMaterialCreateInfo { BaseColorFactor=new Vector4(0.5f,0.55f,0.6f,1f),RoughnessFactor=0.9f,MetallicFactor=0f }],
        [new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 },new StageObjectCreateInfo { MeshIndex=1,MaterialIndex=1 },new StageObjectCreateInfo { MeshIndex=2,MaterialIndex=2 }]);
    private static Camera View(){
        var Result=Camera.CreateFirstPerson(new Vector3(0f,0.8f,-4f));
        Result.FarZ=50f;
        return Result;
    }
    private static byte[]Render(StageRenderer Renderer,Camera Eye,Action<StageRenderer>?Setup=null,int Frames=2,float Scale=1f){
        for(var Frame=0;Frame<Frames;Frame++){
            Renderer.SetCamera(Eye);
            Renderer.SetAmbientLight(new Vector3(0.1f));
            Renderer.SetDirectionalLight(Vector3.Normalize(new Vector3(0.3f,0.8f,-0.4f)),Vector3.One,2f);
            Renderer.SetObjectTransform(0,Matrix4x4.CreateScale(Scale)*Matrix4x4.CreateRotationY(0.4f)*Matrix4x4.CreateTranslation(new Vector3(-0.8f,0.6f,0f)*Scale));
            Renderer.SetObjectTransform(1,Matrix4x4.CreateScale(Scale)*Matrix4x4.CreateTranslation(new Vector3(0.9f,0.7f,-0.5f)*Scale));
            Renderer.SetObjectTransform(2,Matrix4x4.CreateScale(Scale)*Matrix4x4.CreateTranslation(new Vector3(0f,1f,2.5f)*Scale));
            Setup?.Invoke(Renderer);
            Renderer.RenderFrame(CaptureForVerification:Frame==Frames-1);
        }
        return Renderer.LastCapturedFrame!.Pixels.ToArray();
    }
    [WindowsOnlyTheory]
    [Trait("Category","GPU")]
    [InlineData(RenderPresentationMode.BaseColor)]
    [InlineData(RenderPresentationMode.Normal)]
    [InlineData(RenderPresentationMode.Wireframe)]
    [InlineData(RenderPresentationMode.Lit)]
    public void PresentationModes_PersistAcrossFrameSlots(RenderPresentationMode Mode){
        using var Window=new Win32Window(new Win32WindowOptions { Title="Native presentation",Width=160,Height=90,Visible=false });
        using var Renderer=CreateScene(Window);
        void Setup(StageRenderer Target){
            Target.SetPresentationMode(Mode);
            Target.SetObjectSelectionWeight(0,Mode==RenderPresentationMode.Lit?1f:0f);
        }
        var First=Render(Renderer,View(),Setup);
        var Next=Render(Renderer,View(),Setup,Frames:1);
        Assert.Equal(First,Next);
        Assert.True(First.Distinct().Count()>8,"The presentation must draw visible geometry.");
        Assert.DoesNotContain(Renderer.DebugMessages,static Message=>Message.IsErrorOrWorse);
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void MicroscopicScene_RemainsVisibleAndStable(){
        using var Window=new Win32Window(new Win32WindowOptions { Title="Native micro",Width=160,Height=90,Visible=false });
        using var Renderer=CreateScene(Window);
        var Micro=View();
        Micro.Target*=1e-6f;
        Micro.NearZ=1e-7f;
        Micro.FarZ=50e-6f;
        var Pixels=Render(Renderer,Micro,Scale:1e-6f);
        Assert.Equal(Pixels,Render(Renderer,Micro,Frames:1,Scale:1e-6f));
        Assert.True(Pixels.Distinct().Count()>8,"The micro scene must be visible.");
        Assert.InRange(Renderer.ReadDepthForTesting()[45*160+80],0f,RadialDepthProjection.MaximumDepth);
        Assert.DoesNotContain(Renderer.DebugMessages,static Message=>Message.IsErrorOrWorse);
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void ChangingTheCurve_UpdatesDepthAndPreservesThePhysicalImage(){
        using var Window=new Win32Window(new Win32WindowOptions { Title="Native curve changes",Width=160,Height=90,Visible=false });
        using var Renderer=CreateScene(Window);
        var Camera=View();
        var Expected=Render(Renderer,Camera);
        var InitialDepth=Renderer.ReadDepthForTesting();
        foreach(var Transition in new[]{0.01f,100f,0.01f,100f}){
            Camera.RadialTransitionMeters=Transition;
            Assert.Equal(Expected,Render(Renderer,Camera,Frames:1));
            Assert.NotEqual(InitialDepth[45*160+80],Renderer.ReadDepthForTesting()[45*160+80]);
        }
        Assert.DoesNotContain(Renderer.DebugMessages,static Message=>Message.IsErrorOrWorse);
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Resizing_MatchesAFreshNativeRenderer(){
        using var Window=new Win32Window(new Win32WindowOptions { Title="Native resize",Width=160,Height=90,Visible=false });
        using var Renderer=CreateScene(Window);
        Render(Renderer,View());
        Renderer.Resize(200,80);
        var Resized=Render(Renderer,View());
        using var FreshWindow=new Win32Window(new Win32WindowOptions { Title="Native resize control",Width=200,Height=80,Visible=false });
        using var Fresh=CreateScene(FreshWindow);
        Assert.Equal(200,Renderer.LastCapturedFrame!.Width);
        var Expected=Render(Fresh,View());
        for(var Row=0;Row<80;Row++){
            var ActualRow=Resized.AsSpan(Row*(int)Renderer.LastCapturedFrame.RowPitch,200*4);
            var ExpectedRow=Expected.AsSpan(Row*(int)Fresh.LastCapturedFrame!.RowPitch,200*4);
            Assert.True(ExpectedRow.SequenceEqual(ActualRow),$"Resized image differs from a fresh renderer on row {Row}.");
        }
        Assert.Equal(Fresh.ReadDepthForTesting(),Renderer.ReadDepthForTesting());
        Assert.DoesNotContain(Renderer.DebugMessages,static Message=>Message.IsErrorOrWorse);
        Assert.DoesNotContain(Fresh.DebugMessages,static Message=>Message.IsErrorOrWorse);
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void ObserverInset_DrawsInWireframeMode(){
        using var Window=new Win32Window(new Win32WindowOptions { Title="Radial observer wireframe",Width=160,Height=90,Visible=false });
        using var Renderer=CreateScene(Window);
        var Center=View();
        Assert.True(RadialDepthProjection.TryCreate(RadialDepthCurve.Default,Center.NearZ,Center.FarZ,Center.FovYRadians,160f/90f,out var Projection));
        var Eye=new Vector3(0f,0f,-30f);
        var ObserverViewProjection=Camera.CreateFirstPerson(Vector3.Zero).GetView()*Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI/3f,1f,0.1f,30f+1.05f*Projection.ObservedWorldRadius);
        var Inset=new RenderCameraPreview(96,26,64,64,ObserverViewProjection,Eye,Depth:RenderViewDepth.ForObserver(Projection,Vector3.Zero),MarkRadialCenter:true);
        var WithoutInset=Render(Renderer,Center,Target=>Target.SetPresentationMode(RenderPresentationMode.Wireframe));
        var WithInset=Render(Renderer,Center,Target=>{
            Target.SetPresentationMode(RenderPresentationMode.Wireframe);
            Target.SetCameraPreviews([Inset]);
        });
        var RowPitch=(int)Renderer.LastCapturedFrame!.RowPitch;
        var InsetChanged=0;
        for(var Y=26;Y<90;Y++){
            for(var X=96;X<160;X++){
                var Index=Y*RowPitch+X*4;
                if(WithInset[Index]!=WithoutInset[Index]||WithInset[Index+1]!=WithoutInset[Index+1]){ InsetChanged++; }
            }
        }
        Assert.True(InsetChanged>0,"The observer inset must draw in wireframe mode.");
        Assert.DoesNotContain(Renderer.DebugMessages,static Message=>Message.IsErrorOrWorse);
    }
}
