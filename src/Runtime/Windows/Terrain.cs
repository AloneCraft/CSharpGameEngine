using System.Numerics;
using CSharpGameEngine.Authoring;
using CSharpGameEngine.Content.Tiles;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Rendering.Server;
using CSharpGameEngine.Rendering.Streaming;
using CSharpGameEngine.World.Terrain;
namespace CSharpGameEngine.Runtime.Windows;
/// <summary>
/// 地形（ADR-0100 決定 7、M0134）。Entity の位置が地形の中心（平面は正方形の中心、球は中心）。毎フレーム、カメラの世界の位置からマスの四分木を選び、
/// 新しく要るマスの形を作って描く（1 フレームに作るマスは <see cref="CellsPerFrame"/> まで。まだ無いマスは親のマスを描き続ける）。
/// 高さは <see cref="HeightTiles"/> のタイルの階層（<c>*.cgetiles</c>、球は正距円筒、平面は範囲がメートル）から読む。空なら平ら。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Rendering,15)]
public struct Terrain() {
    public TerrainShape Shape=TerrainShape.Planar;
    /// <summary>平面なら一辺、球なら基準の半径（メートル）。</summary>
    public float SizeMeters=1000f;
    public int GridQuads=16;
    public int MaximumDepth=10;
    /// <summary>高さの最大の振れ（マスを分けるかの判定の余裕、メートル）。</summary>
    public float HeightRangeMeters=100f;
    /// <summary>頂点の間隔がこの画素より大きいマスを分ける。</summary>
    public float PixelThreshold=8f;
    /// <summary>高さのタイルの階層の索引（空なら平ら）。相対なら実行ファイルのフォルダーから。</summary>
    public string HeightTiles="";
    /// <summary>高さのタイルの段（0 が元の解像度）。</summary>
    public int HeightLevel=0;
    /// <summary>1 フレームに作るマスの数の上限。</summary>
    public int CellsPerFrame=64;
    /// <summary>
    /// 仮想テクスチャ（M0135）: マスごとに色のページを層から作り、1 枚の置き場の画像に並べて貼る。層は下から、<see cref="ColorTiles"/>（色のタイルの階層、空なら灰色）、
    /// 急な所の岩の色。
    /// </summary>
    public bool VirtualTexture=false;
    /// <summary>色のタイルの階層の索引（RGB、0〜1、sRGB で詰めたもの）。空なら灰色。</summary>
    public string ColorTiles="";
    public int ColorLevel=0;
    /// <summary>ページの一辺（テクセル、縁を除く）。</summary>
    public int PageTexels=64;
}
/// <summary>地形がうまく描けない理由。</summary>
public enum TerrainFailure { None,InvalidSettings,NoCamera,NoTransform,HeightTilesMissing,NoRenderer }
/// <summary><see cref="Terrain"/> の実行時の状態。</summary>
public struct TerrainRuntime:IRuntimeComponent<Terrain> {
    /// <summary>このフレームに描いたマスの数。</summary>
    public int Cells;
    /// <summary>このフレームに作ったマスの数。</summary>
    public int Built;
    /// <summary>まだ作れていないマスの数（親のマスで代わりに描いた）。</summary>
    public int Pending;
    public int DeepestLevel;
    public TerrainFailure Failure;
}
/// <summary>
/// 地形のマスの置き場（Entity ごと）。描画サーバーのメッシュと物の組（枠）を使い回し、マスが変わったらメッシュの中身を差し替える（GPU を待たない）。
/// </summary>
[EngineService]
public sealed class TerrainStreaming:IDisposable {
    private sealed class State {
        internal TerrainSettings?Settings;
        internal string HeightTiles="";
        internal int HeightLevel;
        internal TilePyramid?Pyramid;
        internal ITerrainHeightField Heights=FlatTerrainHeightField.Instance;
        internal int Material=-1;
        internal RenderServer?Renderer;
        internal readonly List<(int Mesh,int Object)>Slots=[];
        internal readonly Stack<int>FreeSlots=new();
        internal readonly Dictionary<TerrainCell,(int Slot,LogicalVector3 Anchor)>Resident=[];
        internal bool VirtualTexture;
        internal string ColorTiles="";
        internal int ColorLevel;
        internal int PageTexels;
        internal TilePyramid?ColorPyramid;
        internal TerrainVirtualTexture?Pages;
        internal RenderTextureResource?Atlas;
        internal int PagesPerRow;
    }
    /// <summary>仮想テクスチャの置き場の画像の一辺（テクセル）。</summary>
    internal const int AtlasSize=4096;
    private readonly Dictionary<Entity,State>States=[];
    /// <summary>試験用: 地形の Entity の、いま描いているマス。</summary>
    internal IReadOnlyCollection<TerrainCell>ResidentCellsForTesting(Entity Entity)=>this.States.TryGetValue(Entity,out var Found)?Found.Resident.Keys:[];
    /// <summary>試験用: 地形の Entity が使っている枠の数（メッシュと物の組）。</summary>
    internal int SlotCountForTesting(Entity Entity)=>this.States.TryGetValue(Entity,out var Found)?Found.Slots.Count:0;
    internal void Update(Entity Entity,in Terrain Settings,ref TerrainRuntime Runtime,LogicalVector3 Centre,LogicalVector3 Camera,ScreenSizeMetric Metric,RenderServer Renderer){
        if(!this.States.TryGetValue(Entity,out var State)){ this.States[Entity]=State=new State(); }
        if(!ReferenceEquals(State.Renderer,Renderer)){
            // 描画サーバーが替わった（作り直しではなく別の物）。枠は前のサーバーの物なので捨てる。
            State.Slots.Clear();
            State.FreeSlots.Clear();
            State.Resident.Clear();
            State.Material=-1;
            State.Renderer=Renderer;
        }
        var Wanted=new TerrainSettings(Settings.Shape,Centre,LogicalScalar.FromSingle(Settings.SizeMeters),Settings.GridQuads,Settings.MaximumDepth,Settings.HeightRangeMeters,Settings.PixelThreshold);
        if(Wanted.Validate() is not null||!(Settings.SizeMeters>0f)||Settings.CellsPerFrame<=0){
            Runtime.Failure=TerrainFailure.InvalidSettings;
            return;
        }
        if(State.HeightTiles!=Settings.HeightTiles||State.HeightLevel!=Settings.HeightLevel){
            State.Pyramid?.Dispose();
            State.Pyramid=null;
            State.Heights=FlatTerrainHeightField.Instance;
            State.HeightTiles=Settings.HeightTiles;
            State.HeightLevel=Settings.HeightLevel;
            this.Release(State);
            if(Settings.HeightTiles.Length>0){
                var Path=System.IO.Path.IsPathRooted(Settings.HeightTiles)?Settings.HeightTiles:System.IO.Path.Combine(AppContext.BaseDirectory,Settings.HeightTiles);
                if(!File.Exists(Path)){
                    Runtime.Failure=TerrainFailure.HeightTilesMissing;
                    return;
                }
                State.Pyramid=TilePyramid.Open(Path);
                State.Heights=new TilePyramidHeightField(State.Pyramid,Settings.HeightLevel);
            }
        }
        // 形の設定が変わったら（中心だけの移動は除く）、作ったマスを全部捨てる。
        if(State.Settings is{ }Previous&&Previous with { Origin=Centre }!=Wanted){ this.Release(State); }
        State.Settings=Wanted;
        if(State.VirtualTexture!=Settings.VirtualTexture||State.ColorTiles!=Settings.ColorTiles||State.ColorLevel!=Settings.ColorLevel||State.PageTexels!=Settings.PageTexels){
            // 貼り方が変わった: 材質を作り直し、作ったマスを全部捨てる（枠は残す）。
            State.ColorPyramid?.Dispose();
            State.ColorPyramid=null;
            State.VirtualTexture=Settings.VirtualTexture;
            State.ColorTiles=Settings.ColorTiles;
            State.ColorLevel=Settings.ColorLevel;
            State.PageTexels=Settings.PageTexels;
            State.Material=-1;
            State.Atlas=null;
            this.Release(State);
            if(Settings.VirtualTexture&&Settings.ColorTiles.Length>0){
                var ColorPath=System.IO.Path.IsPathRooted(Settings.ColorTiles)?Settings.ColorTiles:System.IO.Path.Combine(AppContext.BaseDirectory,Settings.ColorTiles);
                if(!File.Exists(ColorPath)){
                    Runtime.Failure=TerrainFailure.HeightTilesMissing;
                    return;
                }
                State.ColorPyramid=TilePyramid.Open(ColorPath);
            }
        }
        if(State.Settings is{ }Current&&State.VirtualTexture){
            List<ITerrainColorLayer>Layers=[];
            if(State.ColorPyramid is{ }Colors){ Layers.Add(new TileColorLayer(Colors,State.ColorLevel)); }
            Layers.Add(new SlopeColorLayer(new Vector3(0.18f,0.17f,0.16f)));
            State.Pages=new TerrainVirtualTexture(Current,State.Heights,Layers,Math.Clamp(State.PageTexels,4,1024));
            State.PagesPerRow=AtlasSize/State.Pages.PageStride;
        }
        if(State.Material<0){
            if(State.VirtualTexture){
                State.Atlas=RenderTextureResource.CreateDynamic(AtlasSize,AtlasSize,RenderTextureColorSpace.Srgb);
                State.Material=Renderer.AddMaterial(new StageMaterialCreateInfo { BaseColorFactor=Vector4.One,RoughnessFactor=0.95f,MetallicFactor=0f,BaseColorTexture=State.Atlas });
            } else {
                State.Material=Renderer.AddMaterial(new StageMaterialCreateInfo { BaseColorFactor=new Vector4(0.62f,0.6f,0.57f,1f),RoughnessFactor=0.95f,MetallicFactor=0f });
            }
            // 材質が替わったので、枠を作り直す（物の材質は後から変えられない）。
            foreach(var (_,Object) in State.Slots){ Renderer.RemoveObject(Object); }
            State.Slots.Clear();
            State.FreeSlots.Clear();
            State.Resident.Clear();
        }
        var Cells=TerrainCells.Select(Wanted,Camera,Metric);
        var Selected=new HashSet<TerrainCell>(Cells);
        // 選ばれなくなったマスの枠を空ける（新しいマスを作れなかった所は、下でそのまま描き続ける）。
        var Built=0;
        var Pending=0;
        var Keep=new HashSet<TerrainCell>();
        foreach(var Cell in Cells){
            if(State.Resident.ContainsKey(Cell)){ Keep.Add(Cell);continue; }
            if(Built>=Settings.CellsPerFrame){
                Pending++;
                // 代わりに、このマスを覆う作ってあるマス（親か子）を残す。
                foreach(var Old in State.Resident.Keys){
                    if(Old.Key.Face==Cell.Key.Face&&Covers(Old.Key,Cell.Key)||Covers(Cell.Key,Old.Key)&&Old.Key.Face==Cell.Key.Face){ Keep.Add(Old); }
                }
                continue;
            }
            var Mesh=TerrainCellMesh.Build(Wanted,Cell,State.Heights);
            int Slot;
            var Reused=State.FreeSlots.Count>0;
            if(Reused){
                Slot=State.FreeSlots.Pop();
            } else {
                Slot=State.Slots.Count;
                if(State.VirtualTexture&&Slot>=State.PagesPerRow*State.PagesPerRow){ Pending++;continue; }
            }
            var Uvs=Mesh.Uvs;
            if(State.VirtualTexture&&State.Pages is{ }Pages&&State.Atlas is{ }Atlas){
                // ページは枠の番号の場所に置く。UV は縁の内側を指す。
                var PageX=Slot%State.PagesPerRow*Pages.PageStride;
                var PageY=Slot/State.PagesPerRow*Pages.PageStride;
                Renderer.UpdateTextureRegion(Atlas,PageX,PageY,Pages.PageStride,Pages.PageStride,Pages.ComposePage(Cell.Key));
                Uvs=new Vector2[Mesh.Uvs.Length];
                for(var Index=0;Index<Uvs.Length;Index++){
                    Uvs[Index]=new Vector2(PageX+TerrainVirtualTexture.Border+Mesh.Uvs[Index].X*Pages.PageTexels,PageY+TerrainVirtualTexture.Border+Mesh.Uvs[Index].Y*Pages.PageTexels)/AtlasSize;
                }
            }
            var Info=new StageMeshCreateInfo { Positions=Mesh.Positions,Normals=Mesh.Normals,Tangents=Tangents(Mesh.Normals),Uvs=Uvs,Indices=Mesh.Indices };
            if(Reused){
                Renderer.ReplaceMesh(State.Slots[Slot].Mesh,Info);
            } else {
                var MeshIndex=Renderer.AddMesh(Info);
                var ObjectIndex=Renderer.AddObject(MeshIndex,State.Material);
                if(ObjectIndex<0){ Runtime.Failure=TerrainFailure.NoRenderer;break; }
                State.Slots.Add((MeshIndex,ObjectIndex));
            }
            State.Resident[Cell]=(Slot,Mesh.Anchor);
            Keep.Add(Cell);
            Built++;
        }
        foreach(var Cell in State.Resident.Keys.Where(Cell=>!Keep.Contains(Cell)).ToArray()){
            var Slot=State.Resident[Cell].Slot;
            State.Resident.Remove(Cell);
            State.FreeSlots.Push(Slot);
        }
        // 置いたマスはカメラから見た位置へ、空いた枠は隠す。
        var Used=new bool[State.Slots.Count];
        foreach(var (Cell,(Slot,Anchor)) in State.Resident){
            Used[Slot]=true;
            (Anchor-Camera).TryToSingle(out var Relative);
            Renderer.SetObjectTransform(State.Slots[Slot].Object,Matrix4x4.CreateTranslation(Relative));
            Renderer.SetObjectVisibility(State.Slots[Slot].Object,true,true);
        }
        for(var Slot=0;Slot<Used.Length;Slot++){
            if(!Used[Slot]){ Renderer.SetObjectVisibility(State.Slots[Slot].Object,false,false); }
        }
        Runtime.Cells=State.Resident.Count;
        Runtime.Built=Built;
        Runtime.Pending=Pending;
        Runtime.DeepestLevel=Cells.Count>0?Cells.Max(static Cell=>Cell.Key.Depth):0;
        if(Runtime.Failure!=TerrainFailure.NoRenderer){ Runtime.Failure=TerrainFailure.None; }
    }
    /// <summary><paramref name="Coarse"/> が <paramref name="Fine"/> を覆うか（同じ面で、深さが浅いか同じ）。</summary>
    private static bool Covers(TerrainCellKey Coarse,TerrainCellKey Fine){
        if(Coarse.Depth>Fine.Depth){ return false; }
        var Shift=Fine.Depth-Coarse.Depth;
        return Fine.X>>Shift==Coarse.X&&Fine.Y>>Shift==Coarse.Y;
    }
    private static Vector4[]Tangents(Vector3[]Normals){
        var Result=new Vector4[Normals.Length];
        for(var Index=0;Index<Normals.Length;Index++){
            var Side=MathF.Abs(Normals[Index].Y)<0.99f?Vector3.UnitY:Vector3.UnitX;
            Result[Index]=new Vector4(Vector3.Normalize(Vector3.Cross(Side,Normals[Index])),1f);
        }
        return Result;
    }
    private void Release(State State){
        foreach(var (Slot,_) in State.Resident.Values){ State.FreeSlots.Push(Slot); }
        State.Resident.Clear();
    }
    public void Dispose(){
        foreach(var State in this.States.Values){
            State.Pyramid?.Dispose();
            State.ColorPyramid?.Dispose();
        }
        this.States.Clear();
    }
}
/// <summary>
/// 地形を毎フレーム更新する（カメラの姿勢が決まった後）。カメラと地形の中心は世界の Q64.64 の位置で、描く位置はその差を float にした、カメラから見た位置。
/// </summary>
[SystemPhase(SystemPhase.Presentation)]
[RunsAfter(typeof(CameraOriginSync))]
internal struct TerrainUpdate:ISystem {
    public void Update(Entity Entity,ref Terrain Settings,ref TerrainRuntime Runtime,TerrainStreaming Streaming,EngineHost Host,ref State State){
        if(Host.Renderer is not{ }Renderer){
            Runtime.Failure=TerrainFailure.NoRenderer;
            return;
        }
        var Camera=Host.RenderCamera;
        var Height=Renderer.SurfaceHeight;
        if(!Host.TryGetCameraPose(out var Pose,out _)||Height<=0||!(Camera.FovYRadians>0f&&Camera.FovYRadians<MathF.PI)||!(Camera.NearZ>0f)){
            Runtime.Failure=TerrainFailure.NoCamera;
            return;
        }
        if(RuntimeWorldTransform.TryResolveWorld(Entity,State.World,out FixedAffineTransform World,out _)!=FixedTransformResolveStatus.Success){
            Runtime.Failure=TerrainFailure.NoTransform;
            return;
        }
        var Centre=new LogicalVector3(World.M41,World.M42,World.M43);
        var CameraPosition=new LogicalVector3(Pose.M41,Pose.M42,Pose.M43);
        Streaming.Update(Entity,in Settings,ref Runtime,Centre,CameraPosition,ScreenSizeMetric.Perspective(Camera.FovYRadians,Height,Camera.NearZ),Renderer);
    }
}
