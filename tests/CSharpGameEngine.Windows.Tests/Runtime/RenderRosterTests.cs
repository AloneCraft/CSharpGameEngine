using System.Reflection;
using System.Security.Cryptography;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Generated;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Graphics.D3D12.Tests;
using CSharpGameEngine.Platform.Windows;
using UserEcsProject.Scenes;
using EcsWorld=CSharpGameEngine.Entities.World;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// ADR-0078/M0105: the renderer's objects follow the Entities after the level loaded. A game that spawns, destroys or gives an Entity a
/// mesh while it runs must see exactly those objects drawn. Runs the generated Presentation phase on WARP, so it covers the binder,
/// the renderable wrappers and the Render Server together, and compares what the frame shows.
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class RenderRosterTests {
    private sealed class Game:IDisposable {
        private readonly Win32Window Surface;
        internal readonly EngineHost Host=new();
        internal State State;
        private GeneratedSystemSet Systems;
        internal Game(){
            this.Surface=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Render Roster Test",Width=128,Height=128,Visible=false });
            this.Host.UseExternalSurface(this.Surface.Handle,this.Surface.ClientWidth,this.Surface.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true });
            ref var World=ref this.Host.World;
            World.Create(new AppHost(),new AppHostRuntime(),new Level { ShaderArtifactPath=Path.Combine("Shaders","Stage.cgeshaderartifact") },new LevelRuntime());
            World.Create(new Transform { Position=new LogicalVector3(0f,0.5f,-4f) },new Camera());
            this.State=this.Host.CreateState();
            this.State.Enabled=true;
        }
        internal ref EcsWorld World=>ref this.State.World;
        /// <summary>The Spawner the generated loop gives every system (it makes one on first use); made here so a test can ask it for a Scene.</summary>
        internal Spawner Spawner(){
            var Field=typeof(GeneratedSystemSet).GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Single(Candidate=>Candidate.FieldType==typeof(Spawner));
            object Boxed=this.Systems;
            if(Field.GetValue(Boxed) is not Spawner Existing){
                Existing=new Spawner();
                Field.SetValue(Boxed,Existing);
                this.Systems=(GeneratedSystemSet)Boxed;
            }
            return Existing;
        }
        internal void Frame()=>GeneratedGameLoop.RunPresentationFrame(ref this.State,ref this.Systems,this.Host);
        /// <summary>Runs one frame and returns what it showed: the pixel hash and how many pixels differ from the corner (the clear color).</summary>
        internal (string Hash,int Covered)Capture(){
            this.Frame();
            var Renderer=this.Host.Renderer!;
            Renderer.RenderFrame(CaptureForVerification:true);
            Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
            var Frame=Renderer.LastCapturedFrame!;
            var Covered=0;
            for(var Y=0;Y<Frame.Height;Y++){
                for(var X=0;X<Frame.Width;X++){
                    var Pixel=Y*(int)Frame.RowPitch+X*4;
                    if(Frame.Pixels.AsSpan(Pixel,4).SequenceEqual(Frame.Pixels.AsSpan(0,4))==false){ Covered++; }
                }
            }
            return(Convert.ToHexString(SHA256.HashData(Frame.Pixels)),Covered);
        }
        public void Dispose(){
            this.Host.Dispose();
            this.Surface.Dispose();
        }
    }
    private static Entity SpawnPrimitive(Game Game,PrimitiveType Type,float Right){
        return Game.World.Create(new Transform { Position=new LogicalVector3(Right,0f,0f) },new PrimitiveMesh { Type=Type });
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void ASpawnedEntityIsDrawnAndADestroyedOneStopsBeingDrawn(){
        using var Game=new Game();
        var Box=SpawnPrimitive(Game,PrimitiveType.Box,0f);
        var Alone=Game.Capture();
        Assert.True(Alone.Covered>0,"the box in the level is not drawn");
        Assert.Equal(1,Game.Host.Renderer!.ActiveObjectCount);
        // A type the level never used: its mesh is added to the renderer on the spot.
        var Sphere=SpawnPrimitive(Game,PrimitiveType.Sphere,1.2f);
        var Both=Game.Capture();
        Assert.NotEqual(Alone.Hash,Both.Hash);
        Assert.True(Both.Covered>Alone.Covered);
        Assert.Equal(2,Game.Host.Renderer.ActiveObjectCount);
        Game.World.Destroy(Sphere);
        var AfterDestroy=Game.Capture();
        Assert.Equal(Alone.Hash,AfterDestroy.Hash);
        Assert.Equal(1,Game.Host.Renderer.ActiveObjectCount);
        Assert.False(Game.Host.Roster!.IsBound(Sphere));
        // The destroyed Entities' slots are reused, so the new Entity has Generation 2: it must still be placed where it was put.
        var Again=SpawnPrimitive(Game,PrimitiveType.Sphere,1.2f);
        Assert.True(Again.Generation>1,"the Entity did not get a recycled slot");
        var Respawned=Game.Capture();
        Assert.Equal(Both.Hash,Respawned.Hash);
        Assert.Equal(2,Game.Host.Renderer.ActiveObjectCount);
        Assert.Empty(Game.Host.Roster.Diagnostics);
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void AnEntityThatIsDrawnFromTheStartStopsBeingDrawnWhenItIsDestroyed(){
        using var Game=new Game();
        var Box=SpawnPrimitive(Game,PrimitiveType.Box,0f);
        var Other=SpawnPrimitive(Game,PrimitiveType.Box,1.2f);
        var Two=Game.Capture();
        Assert.Equal(2,Game.Host.Renderer!.ActiveObjectCount);
        Game.World.Destroy(Other);
        var One=Game.Capture();
        Assert.True(One.Covered<Two.Covered);
        Assert.Equal(1,Game.Host.Renderer.ActiveObjectCount);
        Game.World.Destroy(Box);
        var None=Game.Capture();
        Assert.Equal(0,None.Covered);
        Assert.Equal(0,Game.Host.Renderer.ActiveObjectCount);
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void SpawningAndDestroyingManyTimesKeepsTheObjectCountAndTheLastPicture(){
        using var Game=new Game();
        SpawnPrimitive(Game,PrimitiveType.Box,0f);
        var Baseline=Game.Capture();
        var Churned=Baseline;
        for(var Round=0;Round<40;Round++){
            var Spawned=new List<Entity>();
            for(var Index=0;Index<5;Index++){
                Spawned.Add(SpawnPrimitive(Game,Index%2==0?PrimitiveType.Sphere:PrimitiveType.Box,1.0f+Index*0.1f));
            }
            Game.Frame();
            Assert.Equal(6,Game.Host.Renderer!.ActiveObjectCount);
            foreach(var Entity in Spawned){ Game.World.Destroy(Entity); }
            Churned=Game.Capture();
            Assert.Equal(1,Game.Host.Renderer.ActiveObjectCount);
        }
        Assert.Equal(Baseline.Hash,Churned.Hash);
        var Alive=Game.World.GetStatistics().AliveEntityCount;
        Game.Frame();
        Assert.Equal(Alive,Game.World.GetStatistics().AliveEntityCount);
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void AMeshAssetNoEntityHasUsedIsLoadedWhenTheFirstEntityWithItAppears(){
        using var Game=new Game();
        SpawnPrimitive(Game,PrimitiveType.Box,0f);
        var Before=Game.Capture();
        // Box.glb has one untextured material, BoxTextured.glb a textured one: the second uploads a texture while the game runs.
        foreach(var Asset in new[]{ "Fixtures/Box.glb","Fixtures/BoxTextured.glb" }){
            var Spawned=Game.World.Create(new Transform { Position=new LogicalVector3(1.5f,0f,0f) },new StaticMesh { AssetPath=Asset });
            var After=Game.Capture();
            Assert.NotEqual(Before.Hash,After.Hash);
            Assert.True(After.Covered>Before.Covered,Asset);
            Assert.True(Game.Host.Roster!.IsBound(Spawned),Asset);
            Game.World.Destroy(Spawned);
            Assert.Equal(Before.Hash,Game.Capture().Hash);
        }
        Assert.Empty(Game.Host.Roster!.Diagnostics);
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void ASceneSpawnedThroughTheSpawnerIsDrawnWhereItWasPutAndGoneWithItsScene(){
        using var Game=new Game();
        SpawnPrimitive(Game,PrimitiveType.Box,0f);
        var Alone=Game.Capture();
        var Spawner=Game.Spawner();
        // Box scene: a unit box. Put it to the side of the one in the level.
        Spawner.Spawn<PrimitiveShowcaseBoxScene>(new LogicalVector3(1.3f,0f,0f));
        var Created=new List<SceneInstance>();
        Game.Frame();
        Assert.Equal(0,Spawner.PendingCount);
        var Drawn=Game.Capture();
        Assert.Equal(2,Game.Host.Renderer!.ActiveObjectCount);
        Assert.True(Drawn.Covered>Alone.Covered);
        // The same Scene a second time, elsewhere: a third object.
        Spawner.Spawn<PrimitiveShowcaseBoxScene>(new LogicalVector3(-1.3f,0f,0f));
        Game.Frame();
        var Third=Game.Capture();
        Assert.Equal(3,Game.Host.Renderer.ActiveObjectCount);
        Assert.True(Third.Covered>Drawn.Covered);
        Assert.Empty(Game.Host.Roster!.Diagnostics);
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void AnAssetThatCannotBeLoadedIsReportedOnceAndDrawsNothing(){
        using var Game=new Game();
        SpawnPrimitive(Game,PrimitiveType.Box,0f);
        var Before=Game.Capture();
        Game.World.Create(new Transform { Position=new LogicalVector3(1.5f,0f,0f) },new StaticMesh { AssetPath="Fixtures/Missing.glb" });
        for(var Index=0;Index<3;Index++){ Assert.Equal(Before.Hash,Game.Capture().Hash); }
        Assert.Single(Game.Host.Roster!.Diagnostics);
    }
}
