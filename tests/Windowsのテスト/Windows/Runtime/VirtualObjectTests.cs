using Xunit;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Graphics.D3D12.Tests;
using CSharpGameEngine.Platform.Windows;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// VirtualObject の部品（M0133、ADR-0100 決定 6）。WARP の描画器を持つエンジンに、半径 0.5 m の親と 3 つの段の子（箱・球・カプセル）を置き、
/// 親をカメラから遠ざけたり近づけたりして、画素の大きさで段が選ばれ、選ばれた段の子だけが描かれる印を持つことを確かめる。
/// 段の子の準備はサービス経由で親へ届き、準備できていない段へは上がらない。
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class VirtualObjectTests {
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void TheParentChoosesTheLevelByPixelsAndOnlyThatChildIsShown(){
        using var Surface=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Virtual Object Test",Width=256,Height=256,Visible=false });
        using var Host=new EngineHost();
        Host.UseExternalSurface(Surface.Handle,Surface.ClientWidth,Surface.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true });
        ref var World=ref Host.World;
        var App=new AppHost();
        var AppRuntime=new AppHostRuntime();
        var Level=new Level { ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact") };
        var LevelRuntime=new LevelRuntime();
        var LevelEntity=World.Create(App,AppRuntime,Level,LevelRuntime);
        var Settings=new VirtualObject { RadiusMeters=0.5f,Enter1=20f,Enter2=80f };
        var Runtime=new VirtualObjectRuntime();
        var OwnerTransform=new Transform { Position=new LogicalVector3(0f,0.5f,40f) };
        var Owner=World.Create(OwnerTransform,Settings,Runtime);
        var State=Host.CreateState();
        var Load=new LevelLoad();
        var Shapes=new[]{ PrimitiveType.Box,PrimitiveType.Sphere,PrimitiveType.Capsule };
        var Children=new Entity[Shapes.Length];
        var Levels=new VirtualObjectLevel[Shapes.Length];
        var LevelStates=new VirtualObjectLevelRuntime[Shapes.Length];
        for(var Index=0;Index<Shapes.Length;Index++){
            var ChildTransform=new Transform();
            var Mesh=new PrimitiveMesh { Type=Shapes[Index] };
            Levels[Index]=new VirtualObjectLevel { Level=Index };
            // 組み合わせは見本のシーン（UserEcsProject/Scenes/VirtualObjectSampleScene.json）の段の子と同じ。
            Children[Index]=World.Create(ChildTransform,Mesh,Levels[Index],LevelStates[Index],new SpatialBounds(),new Parent { Value=Owner });
            Load.Update(Children[Index],ref ChildTransform,ref Mesh,ref State);
        }
        var CameraTransform=new Transform { Position=new LogicalVector3(0f,0.5f,0f) };
        var Camera=new Camera();
        var CameraEntity=World.Create(CameraTransform,Camera);
        Load.Update(CameraEntity,ref CameraTransform,ref Camera,ref State);
        Load.Update(LevelEntity,ref App,ref AppRuntime,ref Level,ref LevelRuntime,Host,ref State);
        Assert.True(LevelRuntime.Loaded);
        var Readiness=new VirtualObjectReadiness();
        int Tick(float Distance){
            OwnerTransform.Position=new LogicalVector3(0f,0.5f,Distance);
            Assert.True(Host.World.TrySetComponent(Owner,in OwnerTransform));
            for(var Index=0;Index<Children.Length;Index++){
                new VirtualObjectLevelReport().Update(in Levels[Index],new Parent<VirtualObject>(Owner,Settings),Readiness);
            }
            new VirtualObjectSelect().Update(Owner,ref Settings,ref Runtime,Readiness,Host,ref State);
            for(var Index=0;Index<Children.Length;Index++){
                new VirtualObjectLevelShow().Update(ref LevelStates[Index],in Levels[Index],new Parent<VirtualObjectRuntime>(Owner,Runtime),Readiness);
                Assert.True(Host.World.TrySetComponent(Children[Index],in LevelStates[Index]));
            }
            Assert.Equal(VirtualObjectFailure.None,Runtime.Failure);
            return Runtime.Level;
        }
        // 縦の画角 45°、高さ 256 画素: 1 m 先の 1 m は約 309 画素。直径 1 m の球は、表面までの距離 d − 0.5 で 309 ÷ (d − 0.5) 画素。
        Assert.Equal(0,Tick(40f));
        Assert.InRange(Runtime.Pixels,7.5f,8.2f);
        Assert.Equal(1,Tick(10f));
        Assert.Equal(2,Tick(2f));
        Assert.Equal(2,Runtime.Transitions);
        for(var Index=0;Index<Children.Length;Index++){
            Assert.Equal(Index==2,LevelStates[Index].Shown);
            Assert.Equal(Index!=2,VirtualObjectVisibility.IsHidden(Children[Index],Host.World));
        }
        // 境の近く（80 画素 ÷ 1.25 = 64 画素の少し上）では段 2 のまま。
        Assert.Equal(2,Tick(5f));
        // 段 2 の子が準備できていないと、近づいても段 1 のまま（望む段は 2）。準備できたら上がる。
        Assert.Equal(0,Tick(40f));
        Levels[2]=Levels[2] with { Ready=false };
        Assert.Equal(1,Tick(2f));
        Assert.Equal(2,Runtime.Desired);
        Assert.True(LevelStates[1].Shown);
        Levels[2]=Levels[2] with { Ready=true };
        Assert.Equal(2,Tick(2f));
        // 測れない設定（昇順でない閾値）は理由を残し、今の段を描き続ける。
        Settings=Settings with { Enter2=10f };
        new VirtualObjectSelect().Update(Owner,ref Settings,ref Runtime,Readiness,Host,ref State);
        Assert.Equal(VirtualObjectFailure.InvalidSettings,Runtime.Failure);
        Assert.Equal(2,Runtime.Level);
        Assert.Equal(0,Host.Renderer!.ErrorOrWorseMessageCount);
    }
}
