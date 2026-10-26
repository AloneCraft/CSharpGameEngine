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
/// ADR-0078/M0105：レベルのロード後、レンダラーのオブジェクトは Entities に追従します。実行中に Entity を生成、破棄したり、メッシュを追加したりするゲームでは、それらのオブジェクトが正確に描画される必要があります。WARP 上で生成済み Presentation フェーズを実行するため、binder、renderable wrappers、Render Server をまとめて対象とし、フレームの描画結果を比較します。
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
        /// <summary>生成されたループが各システムに渡す Spawner（初回使用時に生成されるもの）。テストから Scene を要求できるよう、ここで作成します。</summary>
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
        /// <summary>1フレームを実行し、その表示結果としてピクセルハッシュと、隅のピクセル（clear color）と異なるピクセル数を返します。</summary>
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
        // レベルでは使われていない型で、そのメッシュはその場でレンダラーに追加されます。
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
        // 破棄された Entities のスロットが再利用されるため、新しい Entity の Generation は2です。指定した位置に配置されなければなりません。
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
        // Box.glb にはテクスチャなしのマテリアルが1つ、BoxTextured.glb にはテクスチャ付きのマテリアルがあります。後者はゲーム実行中にテクスチャをアップロードします。
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
        // Box シーンは単位ボックスです。レベル内のボックスの横に配置します。
        Spawner.Spawn<PrimitiveShowcaseBoxScene>(new LogicalVector3(1.3f,0f,0f));
        var Created=new List<SceneInstance>();
        Game.Frame();
        Assert.Equal(0,Spawner.PendingCount);
        var Drawn=Game.Capture();
        Assert.Equal(2,Game.Host.Renderer!.ActiveObjectCount);
        Assert.True(Drawn.Covered>Alone.Covered);
        // 同じ Scene を別の位置にもう一度配置し、3つ目のオブジェクトにします。
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
    private static string Mannequin(){
        var Directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(Directory is not null&&!File.Exists(System.IO.Path.Combine(Directory.FullName,"CSharpGameEngine.slnx"))){ Directory=Directory.Parent; }
        return System.IO.Path.Combine(Directory?.FullName??throw new InvalidOperationException("The repository root was not found."),"Templates","CSharpGameEngine.Templates.ThirdPerson","Assets","Characters","Mannequins","SKM_Quinn_Simple.glb");
    }
    /// <summary>
    /// ADR-0078/M0107：レベルのロード後に生成されたキャラクター（再スポーンした pawn）には、スキンメッシュとマテリアルの専用コピーが割り当てられます。キャラクターが消えるとコピーは pool に戻り、pawn が何度再スポーンしてもレンダラーの使用量は増えません。同時に2体いる場合は2つ目のコピーが使われます。
    /// </summary>
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void ASkeletalMeshSpawnedAfterTheLevelLoadedIsDrawnAndItsMeshesAreReusedByTheNextOne(){
        using var Game=new Game();
        Game.World.Create(new Transform { Position=new LogicalVector3(0f,0f,8f) },new PrimitiveMesh { Type=PrimitiveType.Box });
        var Empty=Game.Capture();
        var Placement=new Transform { Position=new LogicalVector3(0f,0f,0f) };
        var First=Game.World.Create(Placement,new CSharpGameEngine.Runtime.Animation.SkeletalMesh { AssetPath=Mannequin() });
        var Drawn=Game.Capture();
        Assert.True(Game.Host.Roster!.IsBound(First));
        Assert.NotEqual(Empty.Hash,Drawn.Hash);
        Assert.True(Drawn.Covered>Empty.Covered+200,$"{Drawn.Covered} pixels against {Empty.Covered}");
        Assert.True(Game.Host.TryGetSkinnedMeshes(First,out var FirstMeshes));
        var Renderer=Game.Host.Renderer!;
        var Meshes=Renderer.MeshCount;
        var Materials=Renderer.MaterialCount;
        var Objects=Renderer.ActiveObjectCount;
        // 消えるとレベルだけの画像に戻り、Entity はメッシュを所有しなくなります。
        Game.World.Destroy(First);
        Assert.Equal(Empty.Hash,Game.Capture().Hash);
        Assert.False(Game.Host.TryGetSkinnedMeshes(First,out _));
        Assert.Equal(Objects-FirstMeshes.Length,Renderer.ActiveObjectCount);
        // 次のキャラクターは pool 内のコピーを使います。同じ画像になり、メッシュもマテリアルも増えません。
        var Again=Game.World.Create(Placement,new CSharpGameEngine.Runtime.Animation.SkeletalMesh { AssetPath=Mannequin() });
        Assert.Equal(Drawn.Hash,Game.Capture().Hash);
        Assert.Equal(Meshes,Renderer.MeshCount);
        Assert.Equal(Materials,Renderer.MaterialCount);
        Assert.True(Game.Host.TryGetSkinnedMeshes(Again,out var AgainMeshes));
        Assert.Equal(FirstMeshes,AgainMeshes);
        // 何度連続で再スポーンしても、使用量は一定です。
        for(var Round=0;Round<12;Round++){
            Game.World.Destroy(Again);
            Game.Frame();
            Again=Game.World.Create(Placement,new CSharpGameEngine.Runtime.Animation.SkeletalMesh { AssetPath=Mannequin() });
            Game.Frame();
        }
        Assert.Equal(Meshes,Renderer.MeshCount);
        Assert.Equal(Materials,Renderer.MaterialCount);
        Assert.Equal(Drawn.Hash,Game.Capture().Hash);
        // 同時に2体いる場合は、2つ目のコピーが必要です。
        var Beside=Game.World.Create(new Transform { Position=new LogicalVector3(1.2f,0f,0f) },new CSharpGameEngine.Runtime.Animation.SkeletalMesh { AssetPath=Mannequin() });
        var Two=Game.Capture();
        Assert.True(Game.Host.Roster.IsBound(Beside));
        Assert.True(Renderer.MeshCount>Meshes);
        Assert.True(Two.Covered>Drawn.Covered);
        Assert.Empty(Game.Host.Roster.Diagnostics);
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void ASkeletalMeshThatCannotBeLoadedIsReportedOnceAndDrawsNothing(){
        using var Game=new Game();
        SpawnPrimitive(Game,PrimitiveType.Box,0f);
        var Before=Game.Capture();
        Game.World.Create(new Transform { Position=new LogicalVector3(1.5f,0f,0f) },new CSharpGameEngine.Runtime.Animation.SkeletalMesh { AssetPath="Fixtures/Missing.glb" });
        Game.World.Create(new Transform { Position=new LogicalVector3(-1.5f,0f,0f) },new CSharpGameEngine.Runtime.Animation.SkeletalMesh { AssetPath=string.Empty });
        for(var Index=0;Index<3;Index++){ Assert.Equal(Before.Hash,Game.Capture().Hash); }
        Assert.Equal(2,Game.Host.Roster!.Diagnostics.Count);
    }
}
