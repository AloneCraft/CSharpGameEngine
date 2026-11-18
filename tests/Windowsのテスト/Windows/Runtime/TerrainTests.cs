using Xunit;
using System.Security.Cryptography;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Content.Tiles;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Graphics.D3D12.Tests;
using CSharpGameEngine.Import.Images;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.World.Terrain;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// 地形の部品（M0134、ADR-0100 決定 7）。WARP の描画器を持つエンジンで、平らな地形と、試験の中で作った起伏のタイルの地形を描く。
/// マスが選ばれて描かれ、カメラが動くと枠（メッシュと物の組）を使い回して形を差し替え、同じ入力から同じ絵になる。
/// 環境変数 <c>CGE_TERRAIN_MOON_IMG</c> に月の標高（LOLA の LDEM_64.IMG）、<c>CGE_TERRAIN_CAPTURE</c> に場所があれば、月の地形を球で描いて PNG を書く。
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class TerrainTests {
    private sealed class Scene:IDisposable {
        internal readonly Win32Window Surface;
        internal readonly EngineHost Host;
        internal readonly Entity TerrainEntity;
        internal readonly Entity CameraEntity;
        internal Terrain Settings;
        internal TerrainRuntime Runtime;
        internal Transform CameraTransform;
        internal Camera Camera;
        internal CSharpGameEngine.Entities.State State;
        internal readonly TerrainStreaming Streaming=new();
        internal Scene(Terrain Settings,LogicalVector3 TerrainCentre,LogicalVector3 CameraPosition,LogicalQuaternion CameraRotation,int Width=320,int Height=180){
            this.Surface=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Terrain Test",Width=Width,Height=Height,Visible=false });
            this.Host=new EngineHost();
            this.Host.UseExternalSurface(this.Surface.Handle,this.Surface.ClientWidth,this.Surface.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true });
            ref var World=ref this.Host.World;
            var App=new AppHost();
            var AppRuntime=new AppHostRuntime();
            var Level=new Level { ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact") };
            var LevelRuntime=new LevelRuntime();
            var LevelEntity=World.Create(App,AppRuntime,Level,LevelRuntime);
            this.Settings=Settings;
            // 組み合わせは見本のシーン（UserEcsProject/Scenes/TerrainSampleScene.json）と同じ。
            this.TerrainEntity=World.Create(new Transform { Position=TerrainCentre },Settings,new TerrainRuntime());
            this.CameraTransform=new Transform { Position=CameraPosition,Rotation=CameraRotation };
            this.Camera=new Camera { FarZ=1e7f };
            this.CameraEntity=World.Create(this.CameraTransform,this.Camera);
            this.State=this.Host.CreateState();
            var Load=new LevelLoad();
            // 描画器は物が 1 つ以上要る（地形のマスは後から足す）。見えない所に小さな箱を置く。
            var MarkerTransform=new Transform { Position=TerrainCentre-new LogicalVector3(LogicalScalar.Zero,LogicalScalar.FromInt64(1000),LogicalScalar.Zero),Scale=new LogicalVector3(LogicalScalar.FromRatio(1,100),LogicalScalar.FromRatio(1,100),LogicalScalar.FromRatio(1,100)) };
            var Marker=new PrimitiveMesh { Type=PrimitiveType.Box };
            var MarkerEntity=World.Create(MarkerTransform,Marker);
            Load.Update(MarkerEntity,ref MarkerTransform,ref Marker,ref this.State);
            Load.Update(this.CameraEntity,ref this.CameraTransform,ref this.Camera,ref this.State);
            Load.Update(LevelEntity,ref App,ref AppRuntime,ref Level,ref LevelRuntime,this.Host,ref this.State);
            Assert.True(LevelRuntime.Loaded);
        }
        internal void MoveCamera(LogicalVector3 Position){
            this.CameraTransform=this.CameraTransform with { Position=Position };
            Assert.True(this.Host.World.TrySetComponent(this.CameraEntity,in this.CameraTransform));
            new CameraOriginSync().Update(this.CameraEntity,ref this.CameraTransform,ref this.Camera,this.Host,ref this.State);
        }
        internal string Frame(){
            new TerrainUpdate().Update(this.TerrainEntity,ref this.Settings,ref this.Runtime,this.Streaming,this.Host,ref this.State);
            Assert.Equal(TerrainFailure.None,this.Runtime.Failure);
            // 太陽（カメラの後ろ上から）。光はそのフレームだけ有効なので、毎フレーム渡す。
            this.Host.Renderer!.SetAmbientLight(new System.Numerics.Vector3(0.04f));
            this.Host.Renderer.SetDirectionalLight(new CSharpGameEngine.Rendering.DirectionalLightSubmission(System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(-0.8f,-0.45f,0.4f)),
                new CSharpGameEngine.Lighting.LinearLightColor(1f,1f,1f),new CSharpGameEngine.Lighting.Illuminance(4f),new CSharpGameEngine.Lighting.LightShadowSettings(0,new CSharpGameEngine.Lighting.ShadowBias(0.01f))));
            this.Host.Present();
            this.Host.Renderer!.RenderFrame(CaptureForVerification:true);
            return Convert.ToHexString(SHA256.HashData(this.Host.Renderer.LastCapturedFrame!.Pixels));
        }
        public void Dispose(){
            this.Streaming.Dispose();
            this.Host.Dispose();
            this.Surface.Dispose();
        }
    }
    private static LogicalQuaternion LookDown(float Pitch)=>LogicalQuaternion.FromEulerRuf(new LogicalVector3(LogicalScalar.FromSingle(Pitch),LogicalScalar.Zero,LogicalScalar.Zero));
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void AFlatTerrainIsDrawnAndReusesItsSlotsWhenTheCameraMoves(){
        var Settings=new Terrain { Shape=TerrainShape.Planar,SizeMeters=2048f,GridQuads=8,MaximumDepth=8,HeightRangeMeters=10f,PixelThreshold=8f,CellsPerFrame=1000 };
        using var Scene=new Scene(Settings,LogicalVector3.Zero,new LogicalVector3(LogicalScalar.FromInt64(10),LogicalScalar.FromInt64(15),LogicalScalar.FromInt64(-200)),LookDown(0.35f));
        var First=Scene.Frame();
        Assert.True(Scene.Runtime.Cells>10,$"{Scene.Runtime.Cells} cells");
        Assert.True(Scene.Runtime.DeepestLevel>=4,$"the deepest level is {Scene.Runtime.DeepestLevel}");
        Assert.Equal(0,Scene.Runtime.Pending);
        var Slots=Scene.Streaming.SlotCountForTesting(Scene.TerrainEntity);
        Assert.Equal(Scene.Runtime.Cells,Slots);
        Assert.Equal(First,Scene.Frame());
        Assert.Equal(0,Scene.Runtime.Built);
        // 横へ 40 m 動くと、近くのマスが入れ替わる。空いた枠を使い回すので、枠はほとんど増えない。
        Scene.MoveCamera(new LogicalVector3(LogicalScalar.FromInt64(50),LogicalScalar.FromInt64(15),LogicalScalar.FromInt64(-200)));
        // 平らで一様な面なので、絵は同じでもよい。マスは入れ替わる。
        _=Scene.Frame();
        Assert.True(Scene.Runtime.Built>0);
        Assert.True(Scene.Streaming.SlotCountForTesting(Scene.TerrainEntity)<=Slots+Scene.Runtime.Built,"slots were not reused");
        Assert.Equal(0,Scene.Host.Renderer!.ErrorOrWorseMessageCount);
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void HeightTilesRaiseTheTerrainAndALimitedBudgetKeepsTheCoarserCells(){
        var Folder=Path.Combine(Path.GetTempPath(),"cge-terrain-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);
        try {
            var Index=Path.Combine(Folder,"Hills.cgetiles");
            using(var Source=new HillSource(512,512)){
                _=TilePyramidCompiler.Compile(Source,Index,new TilePyramidCompileOptions { TileSize=64,Projection=TileProjection.Planar,West=-1024f,East=1024f,North=1024f,South=-1024f });
            }
            var Flat=new Terrain { Shape=TerrainShape.Planar,SizeMeters=2048f,GridQuads=8,MaximumDepth=7,HeightRangeMeters=80f,PixelThreshold=8f,CellsPerFrame=1000 };
            string FlatImage;
            using(var Scene=new Scene(Flat,LogicalVector3.Zero,new LogicalVector3(LogicalScalar.Zero,LogicalScalar.FromInt64(120),LogicalScalar.FromInt64(-600)),LookDown(0.3f))){ FlatImage=Scene.Frame(); }
            using(var Scene=new Scene(Flat with { HeightTiles=Index },LogicalVector3.Zero,new LogicalVector3(LogicalScalar.Zero,LogicalScalar.FromInt64(120),LogicalScalar.FromInt64(-600)),LookDown(0.3f))){
                var Hills=Scene.Frame();
                Assert.NotEqual(FlatImage,Hills);
            }
            // 1 フレームに 50 マスしか作れないと、残りはまだ作れていない（その間は作ってある粗いマスを描く）。
            using(var Scene=new Scene(Flat with { HeightTiles=Index,CellsPerFrame=50 },LogicalVector3.Zero,new LogicalVector3(LogicalScalar.Zero,LogicalScalar.FromInt64(120),LogicalScalar.FromInt64(-600)),LookDown(0.3f))){
                _=Scene.Frame();
                Assert.Equal(50,Scene.Runtime.Built);
                Assert.True(Scene.Runtime.Pending>0);
                // 作れていないマスは毎フレーム上限の数ずつ減り、いつか 0 になる（同じマスを作り直さない）。
                var Previous=Scene.Runtime.Pending;
                for(var Frame=0;Frame<60&&Scene.Runtime.Pending>0;Frame++){
                    _=Scene.Frame();
                    Assert.True(Scene.Runtime.Pending==0||Scene.Runtime.Pending<Previous,$"frame {Frame}: {Scene.Runtime.Pending} pending after {Previous}");
                    Previous=Scene.Runtime.Pending;
                }
                Assert.Equal(0,Scene.Runtime.Pending);
                Assert.Equal(0,Scene.Host.Renderer!.ErrorOrWorseMessageCount);
            }
        } finally {
            Directory.Delete(Folder,true);
        }
    }
    /// <summary>
    /// 月の実データ（LOLA の LDEM_64.IMG、23040×11520、int16、標高 = 値 × 0.5 m、経度 0〜360°）をタイルの階層にし、月を球の地形で描く。
    /// 環境変数が無ければ何もしない。タイルの階層は撮影の場所に作り、次からは使い回す。
    /// </summary>
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void TheMoonFromLolaHeightsIsDrawnAsASphere(){
        if(System.Environment.GetEnvironmentVariable("CGE_TERRAIN_MOON_IMG") is not{ Length:>0 }Image||System.Environment.GetEnvironmentVariable("CGE_TERRAIN_CAPTURE") is not{ Length:>0 }Folder){ return; }
        Directory.CreateDirectory(Folder);
        var Index=Path.Combine(Folder,"ldem64.cgetiles");
        if(!File.Exists(Index)){
            using var Source=new RawRasterSource(Image,new RawRasterDescription(23040,11520,1,RasterSampleType.Int16,LittleEndian:true,Scale:0.5f,Unit:"meters above 1737.4 km"));
            var Watch=System.Diagnostics.Stopwatch.StartNew();
            var Report=TilePyramidCompiler.Compile(Source,Index,new TilePyramidCompileOptions { TileSize=256,West=0f,East=360f,North=90f,South=-90f,RowsPerRead=256 });
            File.WriteAllText(Path.Combine(Folder,"compile.txt"),$"{Report.Levels} levels, {Report.Tiles} tiles, {Report.PackBytes} pack bytes from {Report.SourceSamples} samples in {Watch.ElapsedMilliseconds} ms");
        }
        // 元の値と、タイルの階層の段 0 の値が一致する（ところどころ）。
        using(var Pyramid=TilePyramid.Open(Index))
        using(var Source=new RawRasterSource(Image,new RawRasterDescription(23040,11520,1,RasterSampleType.Int16,LittleEndian:true,Scale:0.5f))){
            var Row=new float[23040];
            foreach(var Y in new[]{ 0,1777,5760,11519 }){
                Source.ReadRows(Y,1,Row);
                foreach(var X in new[]{ 0,4321,11520,23039 }){ Assert.Equal(Row[X],Pyramid.Texel(0,X,Y,23040,11520)); }
            }
        }
        const float Radius=1_737_400f;
        var Settings=new Terrain { Shape=TerrainShape.Sphere,SizeMeters=Radius,GridQuads=16,MaximumDepth=14,HeightRangeMeters=10_000f,PixelThreshold=6f,HeightTiles=Index,HeightLevel=0,CellsPerFrame=100000 };
        var Lines=new List<string>();
        // 色のタイルの階層（LROC のカラーの 16k、経度 −180〜180°）が撮影の場所にあれば、仮想テクスチャで色を貼った絵も撮る（M0135）。
        var Color=Path.Combine(Folder,"lroc-color.cgetiles");
        var Shots=new List<(string Name,float Altitude,float Pitch,bool Textured)>{ ("moon-orbit",4_000_000f,0f,false),("moon-30km",30_000f,0.45f,false) };
        if(File.Exists(Color)){ Shots.AddRange([("moon-orbit-rvt",4_000_000f,0f,true),("moon-30km-rvt",30_000f,0.45f,true)]); }
        foreach(var (Name,Altitude,Pitch,Textured) in Shots){
            // 赤道の経度 0 の上（+X）から、中心（-X）の方へ見る。高い所では月全体、低い所では地平線。
            var Position=new LogicalVector3(LogicalScalar.FromSingle(Radius+Altitude),LogicalScalar.Zero,LogicalScalar.Zero);
            var Facing=LogicalQuaternion.FromEulerRuf(new LogicalVector3(LogicalScalar.FromSingle(Pitch),LogicalScalar.FromSingle(-MathF.PI/2f),LogicalScalar.Zero));
            using var Scene=new Scene(Textured?Settings with { VirtualTexture=true,ColorTiles=Color,PageTexels=64 }:Settings,LogicalVector3.Zero,Position,Facing,640,360);
            _=Scene.Frame();
            Lines.Add($"{Name}: {Scene.Runtime.Cells} cells, deepest level {Scene.Runtime.DeepestLevel}");
            CSharpGameEngine.Runtime.Windows.RuntimeAutomation.WritePng(Scene.Host.Renderer!.LastCapturedFrame!,Path.Combine(Folder,Name+".png"));
            Assert.Equal(0,Scene.Host.Renderer!.ErrorOrWorseMessageCount);
        }
        File.WriteAllLines(Path.Combine(Folder,"moon.txt"),Lines);
    }
    /// <summary>
    /// 仮想テクスチャ（M0135）: マスごとに色のページを作って置き場に並べて貼る。色の付いた絵になり（無しと違う）、カメラが動くとページを作り直し、
    /// 同じ入力から同じ絵になる。
    /// </summary>
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void TheVirtualTextureColorsTheTerrainFromItsLayers(){
        var Folder=Path.Combine(Path.GetTempPath(),"cge-rvt-gpu-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);
        try {
            var Heights=Path.Combine(Folder,"Hills.cgetiles");
            var Colors=Path.Combine(Folder,"Colors.cgetiles");
            using(var Source=new HillSource(512,512)){
                _=TilePyramidCompiler.Compile(Source,Heights,new TilePyramidCompileOptions { TileSize=64,Projection=TileProjection.Planar,West=-1024f,East=1024f,North=1024f,South=-1024f });
            }
            using(var Source=new StripeSource(512)){
                _=TilePyramidCompiler.Compile(Source,Colors,new TilePyramidCompileOptions { TileSize=64,Encoding=TileSampleEncoding.UNorm8,Projection=TileProjection.Planar,West=-1024f,East=1024f,North=1024f,South=-1024f });
            }
            var Settings=new Terrain { Shape=TerrainShape.Planar,SizeMeters=2048f,GridQuads=8,MaximumDepth=7,HeightRangeMeters=80f,PixelThreshold=8f,CellsPerFrame=1000,HeightTiles=Heights };
            var Camera=new LogicalVector3(LogicalScalar.Zero,LogicalScalar.FromInt64(120),LogicalScalar.FromInt64(-600));
            string Plain;
            using(var Scene=new Scene(Settings,LogicalVector3.Zero,Camera,LookDown(0.3f))){ Plain=Scene.Frame(); }
            string Textured;
            using(var Scene=new Scene(Settings with { VirtualTexture=true,ColorTiles=Colors,PageTexels=32 },LogicalVector3.Zero,Camera,LookDown(0.3f))){
                Textured=Scene.Frame();
                Assert.NotEqual(Plain,Textured);
                Assert.Equal(Textured,Scene.Frame());
                if(System.Environment.GetEnvironmentVariable("CGE_TERRAIN_CAPTURE") is{ Length:>0 }Capture){
                    Directory.CreateDirectory(Capture);
                    CSharpGameEngine.Runtime.Windows.RuntimeAutomation.WritePng(Scene.Host.Renderer!.LastCapturedFrame!,Path.Combine(Capture,"planar-rvt.png"));
                }
                Scene.MoveCamera(new LogicalVector3(LogicalScalar.FromInt64(80),LogicalScalar.FromInt64(120),LogicalScalar.FromInt64(-600)));
                _=Scene.Frame();
                Assert.True(Scene.Runtime.Built>0);
                Assert.Equal(0,Scene.Host.Renderer!.ErrorOrWorseMessageCount);
            }
            using(var Scene=new Scene(Settings with { VirtualTexture=true,ColorTiles=Colors,PageTexels=32 },LogicalVector3.Zero,Camera,LookDown(0.3f))){
                Assert.Equal(Textured,Scene.Frame());
            }
        } finally {
            Directory.Delete(Folder,true);
        }
    }
    /// <summary>しま模様の色（RGB、0〜1）。</summary>
    private sealed class StripeSource(int Size):IRasterSource {
        public int Width=>Size;
        public int Height=>Size;
        public int Channels=>3;
        public string Unit=>"srgb";
        public float?NoData=>null;
        public void ReadRows(int Row,int Rows,Span<float>Destination){
            for(var Y=0;Y<Rows;Y++){
                for(var X=0;X<Size;X++){
                    var At=(Y*Size+X)*3;
                    var Band=(X/32+(Row+Y)/32)%2==0;
                    Destination[At]=Band?0.85f:0.25f;
                    Destination[At+1]=Band?0.55f:0.35f;
                    Destination[At+2]=Band?0.2f:0.6f;
                }
            }
        }
        public void Dispose(){ }
    }
    /// <summary>なだらかな丘（試験用の標高、メートル）。</summary>
    private sealed class HillSource(int Width,int Height):IRasterSource {
        public int Width { get; }=Width;
        public int Height { get; }=Height;
        public int Channels=>1;
        public string Unit=>"meters";
        public float?NoData=>null;
        public void ReadRows(int Row,int Rows,Span<float>Destination){
            for(var Y=0;Y<Rows;Y++){
                for(var X=0;X<this.Width;X++){ Destination[Y*this.Width+X]=40f*MathF.Sin(X*0.05f)*MathF.Cos((Row+Y)*0.04f)+30f; }
            }
        }
        public void Dispose(){ }
    }
}
