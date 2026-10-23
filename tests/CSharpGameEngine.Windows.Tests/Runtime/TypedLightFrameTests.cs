using System.Collections;
using System.Numerics;
using System.Reflection;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Lighting;
using CSharpGameEngine.Runtime.Lighting;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Graphics.D3D12.Tests;
using CSharpGameEngine.Platform.Windows;
namespace CSharpGameEngine.Runtime.Windows.Tests;
[Collection(D3D12GpuCollection.Name)]
public sealed class TypedLightFrameTests {
    private static readonly LinearLightColor White=new(1f,1f,1f);
    private static readonly LightShadowSettings Shadow=new(0,new ShadowBias(0.005f));
    [Fact]
    public void SubmissionDefaults_AreRejectedBeforeRendering(){
        Assert.False(default(DirectionalLightSubmission).IsValid);
        Assert.False(default(PointLightSubmission).IsValid);
        Assert.False(default(SpotLightSubmission).IsValid);
        Assert.False(default(RectAreaLightSubmission).IsValid);
        Assert.Throws<ArgumentException>(()=>default(PointLightSubmission).Validate());
        Assert.True(new PointLightSubmission(Vector3.Zero,White,new(0f),new(10f)).IsValid);
    }
    [Fact]
    public void GeneratedRuntime_ReportsEntityScopedInvalidValuesAndOrphanShadow(){
        var World=new CSharpGameEngine.Entities.World();
        var Missing=World.Create(new PointLight(),new PointLightRuntime());
        Assert.Equal("Transform",LightValidation.SceneFailure(Missing,World));
        var Orphan=World.Create(new LightShadow(),new LightShadowRuntime());
        Assert.Equal("LightMultiplicity",LightValidation.SceneFailure(Orphan,World));
        var Light=default(PointLight);
        var Runtime=new PointLightRuntime();
        var Entity=World.Create(new Transform(),Light,Runtime,new LightShadow(),new LightShadowRuntime(),new Parent());
        var State=new State(World);
        using var Host=new EngineHost();
        var Sync=new PointLightSync();
        Sync.Update(Entity,ref Light,ref Runtime,Host,ref State);
        Assert.Equal("Range",Runtime.InvalidProperty);
        Light=new PointLight();
        Sync.Update(Entity,ref Light,ref Runtime,Host,ref State);
        Assert.Null(Runtime.InvalidProperty);
    }
    [Fact]
    public void LightVisibility_HidesOnlyItsOwnLightAndKeepsItsValidation(){
        // ADR-0078/M0109: UE の SetVisibility。無いか Visible なら表示、偽ならその光だけを出さない。値の検査は表示に関わらず行う。
        // このコンパイルの World には、光と LightVisibility を両方持つアーキタイプが無いので、LightVisibility だけの Entity で確かめる。
        var World=new CSharpGameEngine.Entities.World();
        var Plain=World.Create(new Transform(),new PointLight(),new PointLightRuntime(),new LightShadow(),new LightShadowRuntime(),new Parent());
        var Hidden=World.Create(new LightVisibility { Visible=false });
        var Shown=World.Create(new LightVisibility());
        Assert.False(LightValidation.Hidden(Plain,World));
        Assert.True(LightValidation.Hidden(Hidden,World));
        Assert.False(LightValidation.Hidden(Shown,World));
        var Light=new SpotLight();
        var Runtime=new SpotLightRuntime();
        var State=new State(World);
        using var Host=new EngineHost();
        new SpotLightSync().Update(Hidden,ref Light,ref Runtime,Host,ref State);
        Assert.Equal("LightMultiplicity",Runtime.InvalidProperty);
    }
    [Fact]
    public void FrameRecording_PreservesCrossTypeOrderAndAllocatesNothingAfterWarmup(){
        var Frame=new RenderFrameState(0);
        var Directional=new DirectionalLightSubmission(Vector3.UnitZ,White,new(100f),Shadow);
        var Point=new PointLightSubmission(Vector3.Zero,White,new(800f),new(10f));
        var Spot=new SpotLightSubmission(Vector3.Zero,Vector3.UnitZ,White,new(800f),new(10f),new(30f,45f),Shadow);
        var Rect=new RectAreaLightSubmission(Vector3.Zero,Vector3.UnitZ,Vector3.UnitY,White,new(100f),new(1f,1f),new(10f));
        void Record(){
            Frame.RecordAddDirectional(Directional);
            Frame.RecordPoint(Point);
            Frame.RecordSetDirectional(Directional);
            Frame.RecordSpot(Spot);
            Frame.RecordArea(Rect);
        }
        Record();
        var Order=(IEnumerable)typeof(RenderFrameState).GetField("LightOrder",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(Frame)!;
        Assert.Equal(new[]{"Directional","Point","SetDirectional","Spot","Rect"},Order.Cast<object>().Select(Item=>Item.GetType().GetProperty("Kind")!.GetValue(Item)!.ToString()));
        for(var Index=0;Index<100;Index++){ Frame.Reset();Record(); }
        var Before=GC.GetAllocatedBytesForCurrentThread();
        for(var Index=0;Index<10000;Index++){ Frame.Reset();Record(); }
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-Before);
    }
    [Fact]
    public void RectanglePose_PreservesRollAndNormalizesScale(){
        var World=new CSharpGameEngine.Entities.World();
        var Transform=new Transform { Rotation=new LogicalQuaternion(new LogicalVector3(0f,0f,MathF.PI/2f)),Scale=new LogicalVector3(5f,2f,3f) };
        var Entity=World.Create(Transform,new PrimitiveMesh());
        using var Host=new EngineHost();
        Assert.Equal(FixedTransformStatus.Success,FixedAffineTransform.TryFromTransform(new Transform(),out var Camera));
        Host.SetCameraPose(Camera,CSharpGameEngine.Contracts.ReferenceFrameId.Root);
        Assert.Equal(FixedTransformStatus.Success,LightTransform.TryGetRectanglePose(Entity,World,Host,out _,out var Forward,out var Up,out _));
        Assert.InRange(Vector3.Distance(Forward,Vector3.UnitZ),0f,0.0001f);
        Assert.InRange(Vector3.Distance(Up,-Vector3.UnitX),0f,0.0001f);
        Assert.True(new RectAreaLightSubmission(Vector3.Zero,Forward,Up,White,new(100f),new(2f,3f),new(10f)).IsValid);
        Assert.False(new RectAreaLightSubmission(Vector3.Zero,Forward,Forward,White,new(100f),new(2f,3f),new(10f)).IsValid);
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void TypedLights_RenderIdenticalPixelsAfterDeviceRecovery(){
        var Task=System.Threading.Tasks.Task.Run(RunRecovery);
        Assert.True(Task.Wait(TimeSpan.FromSeconds(60)));
    }
    private static void RunRecovery(){
        var Roster=RenderServer.BuildPrimitiveRoster([PrimitiveType.Box],0,0);
        using var Window=new Win32Window(new Win32WindowOptions { Title="Typed light recovery",Width=128,Height=128,Visible=false });
        using var Renderer=RenderServer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,
            new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },
            Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact"),Roster.Meshes,Roster.Materials,Roster.Objects);
        Renderer.SetCamera(CSharpGameEngine.Graphics.D3D12.Camera.CreateDefault(Vector3.Zero));
        Renderer.SetAmbientLight(new Vector3(0.1f));
        Renderer.AddDirectionalLight(new DirectionalLightSubmission(Vector3.Normalize(new Vector3(1f,-1f,1f)),White,new(100f),Shadow));
        Renderer.AddPointLight(new PointLightSubmission(new Vector3(0f,2f,-2f),new(1f,0f,0f),new(400f),new(10f),Shadow));
        Renderer.AddSpotLight(new SpotLightSubmission(new Vector3(0f,2f,-2f),Vector3.Normalize(new Vector3(0f,-2f,2f)),new(0f,1f,0f),new(400f),new(10f),new(30f,60f),Shadow));
        Renderer.AddRectAreaLight(new RectAreaLightSubmission(new Vector3(0f,1f,-2f),Vector3.UnitZ,Vector3.UnitY,new(0f,0f,1f),new(100f),new(1f,2f),new(10f),Shadow));
        Renderer.SetObjectTransform(0,Matrix4x4.Identity);
        Renderer.RenderFrame(CaptureForVerification:true);
        var Before=Renderer.LastCapturedFrame!.Pixels.ToArray();
        Renderer.RemoveDeviceForTesting();
        Renderer.RenderFrame(CaptureForVerification:true);
        Assert.Equal(2u,Renderer.DeviceSnapshot.Generation);
        Assert.Equal(Before,Renderer.LastCapturedFrame!.Pixels);
        Renderer.RemoveDeviceForTesting();
        Renderer.RenderFrame(CaptureForVerification:true);
        Assert.Equal(Before,Renderer.LastCapturedFrame!.Pixels);
        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
        var Directional=new DirectionalLightSubmission(Vector3.UnitZ,White,new(0f));
        var Point=new PointLightSubmission(Vector3.Zero,White,new(0f),new(10f));
        var Spot=new SpotLightSubmission(Vector3.Zero,Vector3.UnitZ,White,new(0f),new(10f),new(30f,45f));
        var Rect=new RectAreaLightSubmission(Vector3.Zero,Vector3.UnitZ,Vector3.UnitY,White,new(0f),new(1f,1f),new(10f));
        void Submit(){
            for(var Index=0;Index<32;Index++){
                Renderer.AddDirectionalLight(Directional);
                Renderer.AddPointLight(Point);
                Renderer.AddSpotLight(Spot);
                Renderer.AddRectAreaLight(Rect);
            }
        }
        for(var Warmup=0;Warmup<4;Warmup++){ Submit();Renderer.RenderFrame(); }
        var AllocatedBefore=GC.GetAllocatedBytesForCurrentThread();
        Submit();
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-AllocatedBefore);
    }
}
