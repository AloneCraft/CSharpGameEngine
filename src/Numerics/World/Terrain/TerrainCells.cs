using System.Numerics;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Rendering.Streaming;
namespace CSharpGameEngine.World.Terrain;
/// <summary>地形の形。</summary>
public enum TerrainShape {
    /// <summary>平らな正方形（面 1 つ）。</summary>
    Planar,
    /// <summary>球（立方体の 6 面を球へ写す）。</summary>
    Sphere,
}
/// <summary>
/// 地形の設定（ADR-0100 決定 7、M0134）。位置は世界の Q64.64。平面なら <see cref="Origin"/> は正方形の中心で、<see cref="SizeMeters"/> は一辺。
/// 球なら <see cref="Origin"/> は中心で、<see cref="SizeMeters"/> は基準の半径。
/// </summary>
/// <param name="GridQuads">マス 1 つの一辺の四角の数（2 以上の偶数）。頂点は (GridQuads + 1)²。</param>
/// <param name="MaximumDepth">四分木の深さの上限（0〜20）。</param>
/// <param name="HeightRangeMeters">高さの最大の振れ（包む球の余裕）。</param>
/// <param name="PixelThreshold">頂点の間隔がこの画素より大きいマスは分ける。</param>
public sealed record TerrainSettings(TerrainShape Shape,LogicalVector3 Origin,LogicalScalar SizeMeters,int GridQuads=16,int MaximumDepth=12,float HeightRangeMeters=1000f,float PixelThreshold=8f) {
    public int FaceCount=>this.Shape==TerrainShape.Sphere?6:1;
    /// <summary>格子の一辺の頂点の間隔の数（いちばん深い段で）。</summary>
    public long LatticeSize=>(long)this.GridQuads<<this.MaximumDepth;
    public string?Validate(){
        if(this.GridQuads<2||this.GridQuads>256||this.GridQuads%2!=0){ return "The grid quads must be an even number between 2 and 256."; }
        if(this.MaximumDepth<0||this.MaximumDepth>20){ return "The maximum depth must lie between 0 and 20."; }
        if(this.SizeMeters<=LogicalScalar.Zero){ return "The size must be positive."; }
        if(!(this.HeightRangeMeters>=0f)||!float.IsFinite(this.HeightRangeMeters)){ return "The height range must be finite and not negative."; }
        return this.PixelThreshold>0f&&float.IsFinite(this.PixelThreshold)?null:"The pixel threshold must be finite and positive.";
    }
}
/// <summary>マス 1 つ（面、深さ、面の中の x と y）。</summary>
public readonly record struct TerrainCellKey(int Face,int Depth,int X,int Y);
/// <summary>選ばれたマス。<see cref="CoarserEdges"/> のビット 0〜3（-x、+x、-y、+y の辺）が立っていれば、その辺の隣は 1 段粗い。</summary>
public readonly record struct TerrainCell(TerrainCellKey Key,int CoarserEdges);
/// <summary>
/// 地形の高さ（メートル）。<paramref name="Point"/> は球なら中心からの向き（長さ 1）、平面なら中心からの (x, 0, z)（メートル）。
/// 高さは点だけで決まり、マスの深さに依らないこと（隣のマスと共有する頂点が、深さの違うマスからも同じ位置になるため）。同じ入力に同じ値を返すこと。
/// </summary>
public interface ITerrainHeightField {
    float Height(Vector3 Point);
}
/// <summary>どこも高さ 0 の地形。</summary>
public sealed class FlatTerrainHeightField:ITerrainHeightField {
    public static FlatTerrainHeightField Instance { get; }=new();
    public float Height(Vector3 Point)=>0f;
}
/// <summary>
/// 地形のマスの選び方と形（ADR-0100 決定 7、M0134）。
/// <list type="bullet">
/// <item>四分木: 平面は 1 本、球は立方体の 6 面で 6 本。頂点の間隔が画面で <see cref="TerrainSettings.PixelThreshold"/> 画素より大きいマスを分ける。画素は共通の式
/// <see cref="ScreenSizeMetric"/> で、マスを包む球の表面までの距離で測る（位置の差は Q64.64 で引いてから float にする）。</item>
/// <item>隣のマスとの深さの差は 1 まで（面をまたいでも）。粗い隣に接する辺は、細かい側の途中の頂点を粗い辺の上（両隣の頂点の真ん中）へ置くので、割れ目が出ない。</item>
/// <item>頂点は、いちばん深い段の整数の格子の点で決まる。球の頂点は立方体の表面の整数の点を Q64.64 で正規化した向きなので、面をまたいで共有する頂点も同じ位置になる。</item>
/// <item>同じ入力から同じマスと同じ形。</item>
/// </list>
/// </summary>
public static class TerrainCells {
    /// <summary>立方体の面ごとの (法線, u, v)。u × v = 法線。</summary>
    private static readonly (int Nx,int Ny,int Nz,int Ux,int Uy,int Uz,int Vx,int Vy,int Vz)[]Faces=[
        (1,0,0,0,0,-1,0,1,0),
        (-1,0,0,0,0,1,0,1,0),
        (0,1,0,1,0,0,0,0,-1),
        (0,-1,0,1,0,0,0,0,1),
        (0,0,1,1,0,0,0,1,0),
        (0,0,-1,-1,0,0,0,1,0),
    ];
    /// <summary>マスを選ぶ。<paramref name="Camera"/> は世界の位置。結果はキーの順（面、深さ、y、x）。</summary>
    public static List<TerrainCell>Select(TerrainSettings Settings,LogicalVector3 Camera,ScreenSizeMetric Metric){
        if(Settings.Validate() is{ }Problem){ throw new ArgumentException(Problem,nameof(Settings)); }
        var Leaves=new HashSet<TerrainCellKey>();
        var Stack=new Stack<TerrainCellKey>();
        for(var Face=0;Face<Settings.FaceCount;Face++){ Stack.Push(new(Face,0,0,0)); }
        while(Stack.Count>0){
            var Key=Stack.Pop();
            if(Key.Depth<Settings.MaximumDepth&&NeedsSplit(Settings,Key,Camera,Metric)){
                foreach(var Child in Children(Key)){ Stack.Push(Child); }
            } else {
                Leaves.Add(Key);
            }
        }
        Balance(Settings,Leaves);
        var Result=new List<TerrainCell>(Leaves.Count);
        foreach(var Key in Leaves){
            var Coarser=0;
            for(var Edge=0;Edge<4;Edge++){
                if(NeighbourDepth(Settings,Leaves,Key,Edge) is{ }Depth&&Depth<Key.Depth){ Coarser|=1<<Edge; }
            }
            Result.Add(new(Key,Coarser));
        }
        Result.Sort(static (A,B)=>A.Key.Face!=B.Key.Face?A.Key.Face.CompareTo(B.Key.Face):A.Key.Depth!=B.Key.Depth?A.Key.Depth.CompareTo(B.Key.Depth):A.Key.Y!=B.Key.Y?A.Key.Y.CompareTo(B.Key.Y):A.Key.X.CompareTo(B.Key.X));
        return Result;
    }
    private static IEnumerable<TerrainCellKey>Children(TerrainCellKey Key){
        for(var Index=0;Index<4;Index++){ yield return new(Key.Face,Key.Depth+1,Key.X*2+(Index&1),Key.Y*2+(Index>>1)); }
    }
    /// <summary>マスの一辺の格子の間隔の数（いちばん深い段で）。</summary>
    private static long Span(TerrainSettings Settings,int Depth)=>(long)Settings.GridQuads<<(Settings.MaximumDepth-Depth);
    private static bool NeedsSplit(TerrainSettings Settings,TerrainCellKey Key,LogicalVector3 Camera,ScreenSizeMetric Metric){
        var Span=TerrainCells.Span(Settings,Key.Depth);
        var I0=Key.X*Span;
        var J0=Key.Y*Span;
        var Centre=SurfacePoint(Settings,Key.Face,I0+Span/2,J0+Span/2,0f);
        var Radius=0f;
        foreach(var (I,J) in new[]{ (I0,J0),(I0+Span,J0),(I0,J0+Span),(I0+Span,J0+Span) }){
            if((SurfacePoint(Settings,Key.Face,I,J,0f)-Centre).TryToSingle(out var Corner)){ Radius=MathF.Max(Radius,Corner.Length()); }
        }
        Radius+=Settings.HeightRangeMeters;
        if(!(Centre-Camera).TryToSingle(out var Relative)){ return false; }
        var Spacing=EdgeLengthMeters(Settings,Key.Depth)/Settings.GridQuads;
        var Pixels=Metric.ProjectedPixels(Spacing,MathF.Max(Relative.Length()-Radius,0f));
        return Pixels>Settings.PixelThreshold;
    }
    /// <summary>深さ <paramref name="Depth"/> のマスの一辺のおよその長さ（球は面の弧 π/2 × 半径を分けた長さ）。</summary>
    public static float EdgeLengthMeters(TerrainSettings Settings,int Depth){
        Settings.SizeMeters.TryToSingle(out var Size);
        var Face=Settings.Shape==TerrainShape.Sphere?Size*MathF.PI/2f:Size;
        return MathF.ScaleB(Face,-Depth);
    }
    /// <summary>隣のマスとの深さの差を 1 までにする（粗すぎる隣を分ける）。</summary>
    private static void Balance(TerrainSettings Settings,HashSet<TerrainCellKey>Leaves){
        var Changed=true;
        while(Changed){
            Changed=false;
            foreach(var Key in Leaves.OrderByDescending(static Key=>Key.Depth).ToArray()){
                if(!Leaves.Contains(Key)){ continue; }
                for(var Edge=0;Edge<4;Edge++){
                    var Neighbour=NeighbourLeaf(Settings,Leaves,Key,Edge);
                    if(Neighbour is{ }Coarse&&Coarse.Depth<Key.Depth-1){
                        Leaves.Remove(Coarse);
                        foreach(var Child in Children(Coarse)){ Leaves.Add(Child); }
                        Changed=true;
                    }
                }
            }
        }
    }
    private static int?NeighbourDepth(TerrainSettings Settings,HashSet<TerrainCellKey>Leaves,TerrainCellKey Key,int Edge)=>NeighbourLeaf(Settings,Leaves,Key,Edge)?.Depth;
    /// <summary>辺 <paramref name="Edge"/> の真ん中のすぐ外にある葉（面の外なら隣の面。平面の外なら無い）。</summary>
    private static TerrainCellKey?NeighbourLeaf(TerrainSettings Settings,HashSet<TerrainCellKey>Leaves,TerrainCellKey Key,int Edge){
        var Span=TerrainCells.Span(Settings,Key.Depth);
        var I=Key.X*Span+Span/2;
        var J=Key.Y*Span+Span/2;
        switch(Edge){
            case 0:I=Key.X*Span-1;break;
            case 1:I=(Key.X+1)*Span+1;break;
            case 2:J=Key.Y*Span-1;break;
            default:J=(Key.Y+1)*Span+1;break;
        }
        var Face=Key.Face;
        var Size=Settings.LatticeSize;
        if(I<0||J<0||I>Size||J>Size){
            if(Settings.Shape!=TerrainShape.Sphere){ return null; }
            (Face,I,J)=OntoFace(Settings,Face,I,J);
        }
        for(var Depth=Settings.MaximumDepth;Depth>=0;Depth--){
            var CellSpan=TerrainCells.Span(Settings,Depth);
            var Candidate=new TerrainCellKey(Face,Depth,(int)Math.Min(I/CellSpan,(1L<<Depth)-1),(int)Math.Min(J/CellSpan,(1L<<Depth)-1));
            if(Leaves.Contains(Candidate)){ return Candidate; }
        }
        return null;
    }
    /// <summary>面の格子の外の点を、立方体の向きで隣の面の格子へ写す。</summary>
    private static (int Face,long I,long J)OntoFace(TerrainSettings Settings,int Face,long I,long J){
        var Size=Settings.LatticeSize;
        var (X,Y,Z)=CubePoint(Face,I,J,Size);
        var Ax=Math.Abs(X);
        var Ay=Math.Abs(Y);
        var Az=Math.Abs(Z);
        var Target=Ax>=Ay&&Ax>=Az?(X>0?0:1):Ay>=Az?(Y>0?2:3):(Z>0?4:5);
        var F=Faces[Target];
        var Normal=X*F.Nx+Y*F.Ny+Z*F.Nz;
        var U=X*F.Ux+Y*F.Uy+Z*F.Uz;
        var V=X*F.Vx+Y*F.Vy+Z*F.Vz;
        // 立方体の表面へ縮めてから、格子の位置（0〜Size）に直す。
        var NewI=Math.Clamp((U*Size/Normal+Size)/2,0,Size);
        var NewJ=Math.Clamp((V*Size/Normal+Size)/2,0,Size);
        return (Target,NewI,NewJ);
    }
    /// <summary>
    /// 格子の点の名前（同じ点なら面をまたいでも同じ）。球は立方体の表面の整数の点、平面は (I, J, 0)。試験で、隣のマスと共有する頂点を見つけるのに使う。
    /// </summary>
    public static (long X,long Y,long Z)LatticePoint(TerrainSettings Settings,int Face,long I,long J)=>Settings.Shape==TerrainShape.Sphere?CubePoint(Face,I,J,Settings.LatticeSize):(I,J,0);
    /// <summary>面 <paramref name="Face"/> の格子の分数の位置 (I, J) の、立方体の表面の点（float、正規化する前）。仮想テクスチャのテクセルの向きに使う。</summary>
    public static Vector3 CubeDirection(int Face,float I,float J,long Size){
        var F=Faces[Face];
        var U=2f*I-Size;
        var V=2f*J-Size;
        return new Vector3(F.Nx*Size+F.Ux*U+F.Vx*V,F.Ny*Size+F.Uy*U+F.Vy*V,F.Nz*Size+F.Uz*U+F.Vz*V);
    }
    /// <summary>面 <paramref name="Face"/> の格子の点 (I, J) の、立方体の表面の整数の点（各成分 −Size〜Size、2 倍の単位）。</summary>
    private static (long X,long Y,long Z)CubePoint(int Face,long I,long J,long Size){
        var F=Faces[Face];
        var U=2*I-Size;
        var V=2*J-Size;
        return (F.Nx*Size+F.Ux*U+F.Vx*V,F.Ny*Size+F.Uy*U+F.Vy*V,F.Nz*Size+F.Uz*U+F.Vz*V);
    }
    /// <summary>
    /// 格子の点 (I, J) の、高さ <paramref name="Height"/> の世界の位置（Q64.64）。球は立方体の点を正規化した向き × (半径 + 高さ)、平面は中心 + (x, 高さ, z)。
    /// </summary>
    public static LogicalVector3 SurfacePoint(TerrainSettings Settings,int Face,long I,long J,float Height){
        var Size=Settings.LatticeSize;
        if(Settings.Shape==TerrainShape.Planar){
            var Step=Settings.SizeMeters/LogicalScalar.FromInt64(Size);
            var Half=Settings.SizeMeters/LogicalScalar.FromInt64(2);
            return Settings.Origin+new LogicalVector3(LogicalScalar.FromInt64(I)*Step-Half,LogicalScalar.FromSingle(Height),LogicalScalar.FromInt64(J)*Step-Half);
        }
        return Settings.Origin+Direction(Settings,Face,I,J)*(Settings.SizeMeters+LogicalScalar.FromSingle(Height));
    }
    /// <summary>球の格子の点の向き（長さ 1、Q64.64）。平面なら上。</summary>
    public static LogicalVector3 Direction(TerrainSettings Settings,int Face,long I,long J){
        if(Settings.Shape==TerrainShape.Planar){ return LogicalVector3.UnitU; }
        var (X,Y,Z)=CubePoint(Face,I,J,Settings.LatticeSize);
        new LogicalVector3(LogicalScalar.FromInt64(X),LogicalScalar.FromInt64(Y),LogicalScalar.FromInt64(Z)).TryNormalize(out var Result);
        return Result;
    }
    /// <summary>高さを求める位置（球は向き、平面は中心からの (x, 0, z)）。</summary>
    public static Vector3 HeightSamplePoint(TerrainSettings Settings,int Face,long I,long J){
        if(Settings.Shape==TerrainShape.Sphere){
            Direction(Settings,Face,I,J).TryToSingle(out var Unit);
            return Unit;
        }
        (SurfacePoint(Settings,Face,I,J,0f)-Settings.Origin).TryToSingle(out var Local);
        return new Vector3(Local.X,0f,Local.Z);
    }
}
