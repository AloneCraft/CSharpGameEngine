using System.Numerics;
using System.Security.Cryptography;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Graphics.D3D12.Tests;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Lighting;
using CSharpGameEngine.Rendering;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// ADR-0078/M0105: the Render Server's roster grows and shrinks while it runs. An object added later is drawn, a removed one is not,
/// its slot is handed out again, a mesh and a material can be added, and a device loss rebuilds exactly what was there.
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class RenderServerDynamicObjectTests {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    private static RenderServer CreateServer(Win32Window Window){
        (var Meshes,var Materials,var Objects)=RenderServer.BuildPrimitiveRoster([PrimitiveType.Box],0,0);
        return RenderServer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },ShaderArtifactPath,Meshes,Materials,Objects);
    }
    private static (string Hash,int Covered)Capture(RenderServer Renderer,params (int Index,Matrix4x4 World)[]Placed){
        Renderer.SetCamera(CSharpGameEngine.Graphics.D3D12.Camera.CreateDefault(Vector3.Zero));
        Renderer.SetAmbientLight(new Vector3(0.4f));
        Renderer.SetDirectionalLight(new DirectionalLightSubmission(Vector3.UnitY,new LinearLightColor(1f,1f,1f),new Illuminance(2f),new LightShadowSettings(0,new ShadowBias(0.0015f))));
        foreach(var (Index,World) in Placed){ Renderer.SetObjectTransform(Index,World); }
        Renderer.RenderFrame(CaptureForVerification:true);
        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
        var Frame=Renderer.LastCapturedFrame!;
        var Covered=0;
        for(var Y=0;Y<Frame.Height;Y++){
            for(var X=0;X<Frame.Width;X++){
                if(!Frame.Pixels.AsSpan(Y*(int)Frame.RowPitch+X*4,4).SequenceEqual(Frame.Pixels.AsSpan(0,4))){ Covered++; }
            }
        }
        return(Convert.ToHexString(SHA256.HashData(Frame.Pixels)),Covered);
    }
    private static void RunWithTimeout(Action Action){
        var Task=System.Threading.Tasks.Task.Run(Action);
        if(!Task.Wait(TimeSpan.FromSeconds(120))){ throw new TimeoutException("GPU test did not complete in time."); }
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void AnAddedObjectIsDrawnARemovedOneIsNotAndItsSlotIsReused()=>RunWithTimeout(()=>{
        using var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Dynamic Object Test",Width=128,Height=128,Visible=false });
        using var Renderer=CreateServer(Window);
        var Center=(Index:0,World:Matrix4x4.Identity);
        var Side=Matrix4x4.CreateTranslation(1.5f,0f,0f);
        var Alone=Capture(Renderer,Center);
        var Added=Renderer.AddObject(0,0);
        Assert.Equal(1,Added);
        Assert.Equal(2,Renderer.ActiveObjectCount);
        var Both=Capture(Renderer,Center,(Added,Side));
        Assert.NotEqual(Alone.Hash,Both.Hash);
        Assert.True(Both.Covered>Alone.Covered);
        Renderer.RemoveObject(Added);
        Assert.Equal(1,Renderer.ActiveObjectCount);
        Assert.Equal(Alone.Hash,Capture(Renderer,Center).Hash);
        Renderer.RemoveObject(Added);
        Assert.Equal(1,Renderer.ActiveObjectCount);
        var Reused=Renderer.AddObject(0,0);
        Assert.Equal(Added,Reused);
        Assert.Equal(Both.Hash,Capture(Renderer,Center,(Reused,Side)).Hash);
    });
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void AMeshAndAMaterialAddedLaterAreDrawnAndSurviveADeviceLoss()=>RunWithTimeout(()=>{
        using var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Dynamic Mesh Test",Width=128,Height=128,Visible=false });
        using var Renderer=CreateServer(Window);
        var Center=(Index:0,World:Matrix4x4.Identity);
        var Alone=Capture(Renderer,Center);
        (var Meshes,_,_)=RenderServer.BuildPrimitiveRoster([PrimitiveType.Sphere],0,0);
        var Mesh=Renderer.AddMesh(Meshes[0]);
        var Material=Renderer.AddMaterial(new StageMaterialCreateInfo { BaseColorFactor=new Vector4(1f,0f,0f,1f),RoughnessFactor=1f,MetallicFactor=0f });
        Assert.Equal(1,Mesh);
        Assert.Equal(1,Material);
        Assert.Equal(2,Renderer.MeshCount);
        Assert.Equal(2,Renderer.MaterialCount);
        var Sphere=Renderer.AddObject(Mesh,Material);
        var SideMatrix=Matrix4x4.CreateTranslation(-1.5f,0f,0f);
        var With=Capture(Renderer,Center,(Sphere,SideMatrix));
        Assert.NotEqual(Alone.Hash,With.Hash);
        Assert.True(With.Covered>Alone.Covered);
        // A device loss rebuilds the renderer from the Render Server's roster, which now holds the added mesh, material and object.
        Renderer.RemoveDeviceForTesting();
        Renderer.RenderFrame(CaptureForVerification:true);
        Assert.Equal(2u,Renderer.DeviceSnapshot.Generation);
        Assert.Equal(With.Hash,Convert.ToHexString(SHA256.HashData(Renderer.LastCapturedFrame!.Pixels)));
        Assert.Equal(2,Renderer.ActiveObjectCount);
        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
        // A removed object stays removed after a device loss, and its slot is still the next one handed out.
        Renderer.RemoveObject(Sphere);
        Assert.Equal(Alone.Hash,Capture(Renderer,Center).Hash);
        Renderer.RemoveDeviceForTesting();
        Renderer.RenderFrame(CaptureForVerification:true);
        Assert.Equal(3u,Renderer.DeviceSnapshot.Generation);
        Assert.Equal(Alone.Hash,Convert.ToHexString(SHA256.HashData(Renderer.LastCapturedFrame!.Pixels)));
        Assert.Equal(1,Renderer.ActiveObjectCount);
        Assert.Equal(Sphere,Renderer.AddObject(Mesh,Material));
    });
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void TheHeadroomIsFiniteAndAddingPastItIsRefusedWithoutHarm()=>RunWithTimeout(()=>{
        using var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Object Headroom Test",Width=64,Height=64,Visible=false });
        using var Renderer=CreateServer(Window);
        var Last=0;
        for(var Index=0;Index<StageRenderer.DynamicObjectHeadroom;Index++){
            Last=Renderer.AddObject(0,0);
            Assert.True(Last>0);
        }
        Assert.Equal(-1,Renderer.AddObject(0,0));
        Assert.Equal(StageRenderer.DynamicObjectHeadroom+1,Renderer.ActiveObjectCount);
        Renderer.RemoveObject(Last);
        Assert.Equal(Last,Renderer.AddObject(0,0));
        var Frame=Capture(Renderer,(0,Matrix4x4.Identity));
        Assert.True(Frame.Covered>0);
        Assert.Throws<ArgumentOutOfRangeException>(()=>Renderer.AddObject(5,0));
        Assert.Throws<ArgumentOutOfRangeException>(()=>Renderer.AddObject(0,5));
        Assert.Throws<ArgumentOutOfRangeException>(()=>Renderer.RemoveObject(StageRenderer.DynamicObjectHeadroom+10));
    });
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void ASkinnedMeshCannotBeAddedAfterCreation()=>RunWithTimeout(()=>{
        using var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Skinned Add Test",Width=64,Height=64,Visible=false });
        using var Renderer=CreateServer(Window);
        (var Meshes,_,_)=RenderServer.BuildPrimitiveRoster([PrimitiveType.Box],0,0);
        var Skinned=new StageMeshCreateInfo {
            Positions=Meshes[0].Positions,Normals=Meshes[0].Normals,Tangents=Meshes[0].Tangents,Uvs=Meshes[0].Uvs,Indices=Meshes[0].Indices,
            Skin=new StageSkinCreateInfo { JointCount=1,JointIndices=new ushort[Meshes[0].Positions.Count*4],Weights=new float[Meshes[0].Positions.Count*4] },
        };
        Assert.Throws<ArgumentException>(()=>Renderer.AddMesh(Skinned));
        Assert.Equal(1,Renderer.MeshCount);
    });
}
