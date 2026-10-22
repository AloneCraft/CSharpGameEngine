using System.Numerics;
using CSharpGameEngine.Platform.Windows;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>ADR-0066: Skinning.hlsl rewrites a skinned mesh's vertices from the palette before the frame draws.</summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class SkinningGpuIntegrationTests {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    private static readonly Vector3 Pivot=new(0f,1f,0f);
    private static (Vector3 Position,Vector3 Normal)[]Vertices(byte[]Data){
        var Floats=new float[Data.Length/sizeof(float)];
        Buffer.BlockCopy(Data,0,Floats,0,Data.Length);
        return [.. Enumerable.Range(0,Data.Length/48).Select(Index=>(new Vector3(Floats[Index*12],Floats[(Index*12)+1],Floats[(Index*12)+2]),new Vector3(Floats[(Index*12)+3],Floats[(Index*12)+4],Floats[(Index*12)+5])))];
    }
    private static void AssertNear(Vector3 Expected,Vector3 Actual){
        Assert.True(Vector3.Distance(Expected,Actual)<1e-3f,$"expected {Expected}, got {Actual}");
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Warp_PaletteBendsTheSecondJointAndBlendsTheSharedVertex(){
        using var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Skinning GPU Test",Width=64,Height=64,Visible=false });
        // Vertex 0 follows joint 0, vertex 1 joint 1, vertex 2 both equally.
        ushort[]Joints=new ushort[3*8];
        float[]Weights=new float[3*8];
        Weights[0]=1f;
        Joints[8]=1;
        Weights[8]=1f;
        Joints[17]=1;
        Weights[16]=0.5f;
        Weights[17]=0.5f;
        StageMeshCreateInfo Mesh=new(){
            Positions=[new(0f,0f,0f),new(0f,2f,0f),new(1f,2f,0f)],
            Normals=[-Vector3.UnitZ,-Vector3.UnitZ,-Vector3.UnitZ],
            Tangents=[Vector4.UnitX,Vector4.UnitX,Vector4.UnitX],
            Uvs=[Vector2.Zero,Vector2.Zero,Vector2.Zero],
            Indices=[0,1,2],
            Skin=new StageSkinCreateInfo { JointCount=2,JointIndices=Joints,Weights=Weights },
        };
        using var Renderer=StageRenderer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },ShaderArtifactPath,
            [Mesh],[new StageMaterialCreateInfo { BaseColorFactor=Vector4.One,RoughnessFactor=1f,MetallicFactor=0f }],[new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }]);
        Assert.True(Renderer.ShaderArtifactLoaded,string.Join(Environment.NewLine,Renderer.ShaderArtifactDiagnostics));
        Renderer.SetCamera(Camera.CreateFirstPerson(new Vector3(0f,1f,-4f)));
        Renderer.SetObjectTransform(0,Matrix4x4.Identity);
        Renderer.RenderFrame();
        // Before any palette the mesh is in its bind pose.
        var Bind=Vertices(Renderer.ReadSkinnedVerticesForTesting(0));
        AssertNear(new Vector3(0f,2f,0f),Bind[1].Position);
        var Bend=Matrix4x4.CreateTranslation(-Pivot)*Matrix4x4.CreateRotationZ(MathF.PI/2f)*Matrix4x4.CreateTranslation(Pivot);
        Renderer.SetSkinPalette(0,[Matrix4x4.Identity,Bend]);
        Renderer.RenderFrame();
        var Bent=Vertices(Renderer.ReadSkinnedVerticesForTesting(0));
        AssertNear(Vector3.Zero,Bent[0].Position);
        AssertNear(Vector3.Transform(new Vector3(0f,2f,0f),Bend),Bent[1].Position);
        AssertNear((new Vector3(1f,2f,0f)+Vector3.Transform(new Vector3(1f,2f,0f),Bend))/2f,Bent[2].Position);
        AssertNear(-Vector3.UnitZ,Bent[1].Normal);
        Assert.DoesNotContain(Renderer.DebugMessages,static Message=>Message.IsErrorOrWorse);
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Warp_RenderServerKeepsTheSkinAndRecordsThePalette(){
        using var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Skinning Server Test",Width=64,Height=64,Visible=false });
        ushort[]Joints=new ushort[3*8];
        float[]Weights=new float[3*8];
        for(var Vertex=0;Vertex<3;Vertex++){ Weights[Vertex*8]=1f; }
        StageMeshCreateInfo Mesh=new(){
            Positions=[Vector3.Zero,Vector3.UnitY,Vector3.UnitX],Normals=[-Vector3.UnitZ,-Vector3.UnitZ,-Vector3.UnitZ],Tangents=[Vector4.UnitX,Vector4.UnitX,Vector4.UnitX],Uvs=[Vector2.Zero,Vector2.Zero,Vector2.Zero],Indices=[0,1,2],
            Skin=new StageSkinCreateInfo { JointCount=1,JointIndices=Joints,Weights=Weights },
        };
        using var Server=CSharpGameEngine.Runtime.Windows.RenderServer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,new GraphicsDeviceOptions { UseWarp=true },ShaderArtifactPath,
            [Mesh],[new StageMaterialCreateInfo { BaseColorFactor=Vector4.One,RoughnessFactor=1f,MetallicFactor=0f }],[new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }]);
        Server.SetSkinPalette(0,[Matrix4x4.CreateTranslation(0f,5f,0f)]);
    }
    private static StageMeshCreateInfo BendableMesh(){
        // Vertex 0 follows joint 0, vertex 1 joint 1, vertex 2 both equally.
        ushort[]Joints=new ushort[3*8];
        float[]Weights=new float[3*8];
        Weights[0]=1f;
        Joints[8]=1;
        Weights[8]=1f;
        Joints[17]=1;
        Weights[16]=0.5f;
        Weights[17]=0.5f;
        return new StageMeshCreateInfo {
            Positions=[new(0f,0f,0f),new(0f,2f,0f),new(1f,2f,0f)],
            Normals=[-Vector3.UnitZ,-Vector3.UnitZ,-Vector3.UnitZ],
            Tangents=[Vector4.UnitX,Vector4.UnitX,Vector4.UnitX],
            Uvs=[Vector2.Zero,Vector2.Zero,Vector2.Zero],
            Indices=[0,1,2],
            Skin=new StageSkinCreateInfo { JointCount=2,JointIndices=Joints,Weights=Weights },
        };
    }
    /// <summary>ADR-0078/M0107: a character spawned after the level loaded adds skinned meshes to a renderer that may have had none, or some.</summary>
    [WindowsOnlyTheory]
    [Trait("Category","GPU")]
    [InlineData(false)]
    [InlineData(true)]
    public void Warp_ASkinnedMeshAddedAfterCreationIsSkinnedLikeTheOnesFromTheStart(bool StartWithASkinnedMesh){
        using var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Late Skin Test",Width=64,Height=64,Visible=false });
        var Plain=new StageMeshCreateInfo { Positions=[Vector3.Zero,Vector3.UnitY,Vector3.UnitX],Normals=[-Vector3.UnitZ,-Vector3.UnitZ,-Vector3.UnitZ],Tangents=[Vector4.UnitX,Vector4.UnitX,Vector4.UnitX],Uvs=[Vector2.Zero,Vector2.Zero,Vector2.Zero],Indices=[0,1,2] };
        using var Renderer=StageRenderer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },ShaderArtifactPath,
            [StartWithASkinnedMesh?BendableMesh():Plain],[new StageMaterialCreateInfo { BaseColorFactor=Vector4.One,RoughnessFactor=1f,MetallicFactor=0f }],[new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }]);
        Assert.True(Renderer.ShaderArtifactLoaded,string.Join(Environment.NewLine,Renderer.ShaderArtifactDiagnostics));
        Renderer.SetCamera(Camera.CreateFirstPerson(new Vector3(0f,1f,-4f)));
        Renderer.SetObjectTransform(0,Matrix4x4.Identity);
        Renderer.RenderFrame();
        var Added=Renderer.AddMesh(BendableMesh());
        Assert.Equal(1,Added);
        var Second=Renderer.AddObject(Added,0);
        Renderer.SetObjectTransform(Second,Matrix4x4.CreateTranslation(2f,0f,0f));
        var Bend=Matrix4x4.CreateTranslation(-Pivot)*Matrix4x4.CreateRotationZ(MathF.PI/2f)*Matrix4x4.CreateTranslation(Pivot);
        if(StartWithASkinnedMesh){
            Renderer.SetSkinPalette(0,[Matrix4x4.Identity,Matrix4x4.Identity]);
        }
        Renderer.SetSkinPalette(Added,[Matrix4x4.Identity,Bend]);
        Renderer.RenderFrame();
        // Before the first frame after the add the new mesh is in its bind pose; after it, bent by its own palette. The mesh from the start is untouched.
        var Bent=Vertices(Renderer.ReadSkinnedVerticesForTesting(Added));
        AssertNear(Vector3.Zero,Bent[0].Position);
        AssertNear(Vector3.Transform(new Vector3(0f,2f,0f),Bend),Bent[1].Position);
        AssertNear((new Vector3(1f,2f,0f)+Vector3.Transform(new Vector3(1f,2f,0f),Bend))/2f,Bent[2].Position);
        if(StartWithASkinnedMesh){
            var First=Vertices(Renderer.ReadSkinnedVerticesForTesting(0));
            AssertNear(new Vector3(0f,2f,0f),First[1].Position);
        }
        // A third one and another frame: the palette ring keeps growing safely, and every frame is free of debug-layer errors.
        var Third=Renderer.AddMesh(BendableMesh());
        Renderer.SetSkinPalette(Third,[Matrix4x4.Identity,Matrix4x4.Identity]);
        Renderer.RenderFrame();
        Renderer.RenderFrame();
        AssertNear(Vector3.Transform(new Vector3(0f,2f,0f),Bend),Vertices(Renderer.ReadSkinnedVerticesForTesting(Added))[1].Position);
        AssertNear(new Vector3(0f,2f,0f),Vertices(Renderer.ReadSkinnedVerticesForTesting(Third))[1].Position);
        Assert.DoesNotContain(Renderer.DebugMessages,static Message=>Message.IsErrorOrWorse);
    }
    [Fact]
    public void PackInfluences_QuantizedWeightsSumToOne(){
        ushort[]Joints=[0,1,2,0,0,0,0,0];
        float[]Weights=[0.3333f,0.3333f,0.3334f,0f,0f,0f,0f,0f];
        var Packed=StageSkinning.PackInfluences(Joints,Weights,1,3);
        var Sum=0u;
        for(var Pair=0;Pair<4;Pair++){
            Sum+=(Packed[4+Pair]&0xFFFFu)+(Packed[4+Pair]>>16);
        }
        Assert.Equal(65535u,Sum);
        Assert.Equal(1u<<16,Packed[0]);
        Assert.Throws<ArgumentException>(()=>StageSkinning.PackInfluences([5,0,0,0,0,0,0,0],[1f,0f,0f,0f,0f,0f,0f,0f],1,3));
    }
}
