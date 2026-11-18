using Xunit;
using System.Numerics;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Rendering.Streaming;
using CSharpGameEngine.World.Terrain;
namespace CSharpGameEngine.World.Tests;
/// <summary>
/// 地形のマスの選び方と形（M0134、ADR-0100 決定 7）。平面と球で、マスが面をすき間なく覆い、隣との深さの差が 1 までで、
/// 隣のマスと共有する頂点が（面をまたいでも）同じ位置になり（割れ目が出ない）、同じ入力から同じ結果になることを確かめる。
/// </summary>
public sealed class TerrainCellsTests {
    private static readonly ScreenSizeMetric Metric=ScreenSizeMetric.Perspective(MathF.PI/3f,1080,0.1f);
    /// <summary>なだらかな起伏（点だけで決まる）。</summary>
    private sealed class Hills(float Amplitude,float Wavelength):ITerrainHeightField {
        public float Height(Vector3 Point)=>Amplitude*MathF.Sin(Point.X*MathF.Tau/Wavelength)*MathF.Cos(Point.Z*MathF.Tau/Wavelength)+Amplitude*0.5f*MathF.Sin(Point.Y*7f);
    }
    private static TerrainSettings Planar=>new(TerrainShape.Planar,LogicalVector3.Zero,LogicalScalar.FromInt64(4096),GridQuads:8,MaximumDepth:8,HeightRangeMeters:50f,PixelThreshold:6f);
    private static TerrainSettings Moon=>new(TerrainShape.Sphere,new LogicalVector3(LogicalScalar.FromInt64(1_000_000_000),LogicalScalar.Zero,LogicalScalar.FromInt64(-3_000_000)),LogicalScalar.FromInt64(1_737_400),GridQuads:8,MaximumDepth:14,HeightRangeMeters:10_000f,PixelThreshold:6f);
    [Fact]
    public void PlanarCellsCoverTheSquareAreBalancedAndRefineNearTheCamera(){
        var Settings=Planar;
        var Camera=new LogicalVector3(LogicalScalar.FromInt64(300),LogicalScalar.FromInt64(20),LogicalScalar.FromInt64(-700));
        var Cells=TerrainCells.Select(Settings,Camera,Metric);
        AssertCoversEachFace(Settings,Cells);
        AssertBalanced(Settings,Cells);
        var Near=Cells.Where(Cell=>Distance(Settings,Cell,Camera)<200f).Max(static Cell=>Cell.Key.Depth);
        var Far=Cells.Where(Cell=>Distance(Settings,Cell,Camera)>1500f).Max(static Cell=>Cell.Key.Depth);
        Assert.True(Near>Far,$"near cells reach depth {Near}, far ones {Far}");
        Assert.Equal(Cells,TerrainCells.Select(Settings,Camera,Metric));
        var Higher=TerrainCells.Select(Settings,new LogicalVector3(LogicalScalar.FromInt64(300),LogicalScalar.FromInt64(2000),LogicalScalar.FromInt64(-700)),Metric);
        Assert.True(Higher.Count<Cells.Count,$"{Higher.Count} cells from 2 km up, {Cells.Count} from 20 m");
    }
    [Fact]
    public void PlanarNeighboursShareEveryVertexPosition(){
        var Settings=Planar;
        var Cells=TerrainCells.Select(Settings,new LogicalVector3(LogicalScalar.FromInt64(300),LogicalScalar.FromInt64(20),LogicalScalar.FromInt64(-700)),Metric);
        Assert.Contains(Cells,static Cell=>Cell.CoarserEdges!=0);
        AssertNoCracks(Settings,Cells,new Hills(30f,700f));
    }
    [Fact]
    public void SphereCellsCoverAllSixFacesAndShareVerticesAcrossFaces(){
        var Settings=Moon;
        // 面の角の近く（3 つの面が会う所）の上 30 km から見る。
        var Corner=Vector3.Normalize(new Vector3(1f,1f,1f));
        var Camera=Settings.Origin+new LogicalVector3(LogicalScalar.FromSingle(Corner.X*1_767_400f),LogicalScalar.FromSingle(Corner.Y*1_767_400f),LogicalScalar.FromSingle(Corner.Z*1_767_400f));
        var Cells=TerrainCells.Select(Settings,Camera,Metric);
        AssertCoversEachFace(Settings,Cells);
        AssertBalanced(Settings,Cells);
        Assert.True(Cells.Max(static Cell=>Cell.Key.Depth)>=6,$"the deepest cell is {Cells.Max(static Cell=>Cell.Key.Depth)}");
        Assert.Contains(Cells,static Cell=>Cell.CoarserEdges!=0);
        AssertNoCracks(Settings,Cells,new Hills(3000f,0.3f));
    }
    [Fact]
    public void InvalidSettingsAreRejected(){
        Assert.Throws<ArgumentException>(()=>TerrainCells.Select(Planar with { GridQuads=7 },LogicalVector3.Zero,Metric));
        Assert.Throws<ArgumentException>(()=>TerrainCells.Select(Planar with { MaximumDepth=21 },LogicalVector3.Zero,Metric));
        Assert.Throws<ArgumentException>(()=>TerrainCells.Select(Planar with { SizeMeters=LogicalScalar.Zero },LogicalVector3.Zero,Metric));
    }
    private static float Distance(TerrainSettings Settings,TerrainCell Cell,LogicalVector3 Camera){
        var Span=(long)Settings.GridQuads<<(Settings.MaximumDepth-Cell.Key.Depth);
        (TerrainCells.SurfacePoint(Settings,Cell.Key.Face,Cell.Key.X*Span+Span/2,Cell.Key.Y*Span+Span/2,0f)-Camera).TryToSingle(out var Relative);
        return Relative.Length();
    }
    /// <summary>各面で、葉の面積の和が面全体と同じで、重なりが無い。</summary>
    private static void AssertCoversEachFace(TerrainSettings Settings,List<TerrainCell>Cells){
        Assert.Equal(Cells.Count,Cells.Select(static Cell=>Cell.Key).Distinct().Count());
        for(var Face=0;Face<Settings.FaceCount;Face++){
            var Area=0L;
            foreach(var Cell in Cells.Where(Cell=>Cell.Key.Face==Face)){ Area+=1L<<(2*(Settings.MaximumDepth-Cell.Key.Depth)); }
            Assert.Equal(1L<<(2*Settings.MaximumDepth),Area);
        }
        Assert.All(Cells,static Cell=>Assert.InRange(Cell.Key.Face,0,5));
    }
    /// <summary>辺を共有する（同じ格子の点を辺の上に持つ）マスの深さの差は 1 まで。</summary>
    private static void AssertBalanced(TerrainSettings Settings,List<TerrainCell>Cells){
        var Depths=new Dictionary<(long,long,long),List<int>>();
        foreach(var Cell in Cells){
            foreach(var Point in EdgeMidpoints(Settings,Cell.Key)){
                if(!Depths.TryGetValue(Point,out var List)){ Depths[Point]=List=[]; }
                List.Add(Cell.Key.Depth);
            }
        }
        foreach(var Cell in Cells){
            foreach(var Point in BoundaryPoints(Settings,Cell.Key)){
                if(Depths.TryGetValue(Point,out var Others)){
                    Assert.All(Others,Depth=>Assert.InRange(Depth,Cell.Key.Depth-1,Cell.Key.Depth+1));
                }
            }
        }
    }
    /// <summary>隣のマスと共有する格子の点は、どのマスで作っても同じ世界の位置（Q64.64 で完全に同じ）。</summary>
    private static void AssertNoCracks(TerrainSettings Settings,List<TerrainCell>Cells,ITerrainHeightField Heights){
        var Seen=new Dictionary<(long,long,long),(LogicalVector3 Position,TerrainCellKey Cell)>();
        var Shared=0;
        foreach(var Cell in Cells){
            var Mesh=TerrainCellMesh.Build(Settings,Cell,Heights);
            var N=Settings.GridQuads;
            var Step=(long)1<<(Settings.MaximumDepth-Cell.Key.Depth);
            for(var B=0;B<=N;B++){
                for(var A=0;A<=N;A++){
                    if(A!=0&&A!=N&&B!=0&&B!=N){ continue; }
                    var Point=TerrainCells.LatticePoint(Settings,Cell.Key.Face,Cell.Key.X*N*Step+A*Step,Cell.Key.Y*N*Step+B*Step);
                    var Position=Mesh.WorldPositions[B*(N+1)+A];
                    if(Seen.TryGetValue(Point,out var Other)){
                        Shared++;
                        Assert.True(Other.Position==Position,$"the lattice point {Point} is {Position} in {Cell.Key} but {Other.Position} in {Other.Cell}");
                    } else {
                        Seen[Point]=(Position,Cell.Key);
                    }
                }
            }
            Assert.All(Mesh.Normals,static Normal=>Assert.InRange(Normal.Length(),0.999f,1.001f));
            Assert.All(Mesh.Positions,static Position=>Assert.True(float.IsFinite(Position.X)&&float.IsFinite(Position.Y)&&float.IsFinite(Position.Z)));
        }
        Assert.True(Shared>Cells.Count,$"only {Shared} shared vertices were compared");
    }
    private static IEnumerable<(long,long,long)>EdgeMidpoints(TerrainSettings Settings,TerrainCellKey Key){
        var Span=(long)Settings.GridQuads<<(Settings.MaximumDepth-Key.Depth);
        var I0=Key.X*Span;
        var J0=Key.Y*Span;
        yield return TerrainCells.LatticePoint(Settings,Key.Face,I0,J0+Span/2);
        yield return TerrainCells.LatticePoint(Settings,Key.Face,I0+Span,J0+Span/2);
        yield return TerrainCells.LatticePoint(Settings,Key.Face,I0+Span/2,J0);
        yield return TerrainCells.LatticePoint(Settings,Key.Face,I0+Span/2,J0+Span);
    }
    /// <summary>マスの辺の上の、いちばん深い段の格子の点（深さの差が 2 以上なら、細かい側の辺の真ん中が粗い側の辺の上に来る）。</summary>
    private static IEnumerable<(long,long,long)>BoundaryPoints(TerrainSettings Settings,TerrainCellKey Key){
        var Span=(long)Settings.GridQuads<<(Settings.MaximumDepth-Key.Depth);
        var I0=Key.X*Span;
        var J0=Key.Y*Span;
        var Step=Math.Max(1L,Span/64);
        for(var Offset=0L;Offset<=Span;Offset+=Step){
            yield return TerrainCells.LatticePoint(Settings,Key.Face,I0,J0+Offset);
            yield return TerrainCells.LatticePoint(Settings,Key.Face,I0+Span,J0+Offset);
            yield return TerrainCells.LatticePoint(Settings,Key.Face,I0+Offset,J0);
            yield return TerrainCells.LatticePoint(Settings,Key.Face,I0+Offset,J0+Span);
        }
    }
}
