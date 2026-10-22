using System.Buffers.Binary;
using CSharpGameEngine.Numerics;
namespace CSharpGameEngine.Navigation;
/// <summary>
/// 歩ける長方形。格子のセルの集まりで、<c>Max</c>は含まない。<see cref="Y"/>は床の高さ(原点からの段数。段の高さは<see cref="NavMesh.CellHeight"/>)。
/// 長方形は平らで、中のどの2点も直線で結べる(凸)。
/// </summary>
public readonly record struct NavPolygon(int MinX,int MinZ,int MaxX,int MaxZ,int Y) {
    public int Width=>this.MaxX-this.MinX;
    public int Depth=>this.MaxZ-this.MinZ;
}
/// <summary>
/// 隣り合う2つの多角形の境のうち、実際に通れる区間。格子の線の上にある軸に平行な線分(<c>X0==X1</c> か <c>Z0==Z1</c>)で、座標はセル単位。
/// 同じ2つの多角形の間に、通れない区間をはさんで複数あることがある。
/// </summary>
public readonly record struct NavPortal(int PolygonA,int PolygonB,int X0,int Z0,int X1,int Z1);
/// <summary>ナビメッシュを作ったときの歩く者の大きさと、格子の目の大きさ(m、度)。</summary>
public readonly record struct NavMeshSettings(LogicalScalar AgentRadius,LogicalScalar AgentHeight,LogicalScalar AgentMaxClimb,LogicalScalar AgentMaxSlopeDegrees,LogicalScalar CellSize,LogicalScalar CellHeight);
/// <summary>
/// ナビメッシュ(ADR-0079/M0106)。平らな長方形の多角形と、その隣接(通れる境の区間)の集まり。歩く者の半径だけ壁から離れた、足の位置(カプセルの中心を
/// 床へ下ろした点)が居てよい場所を表す。座標は整数のセルで持ち(原点と目の大きさで世界へ戻す)、経路探索は整数だけで解くので、どのマシンでも同じ経路になる。
/// ファイルは<c>.cgenav</c>(<see cref="ToBytes"/>、<see cref="TryRead"/>)。
/// </summary>
public sealed partial class NavMesh {
    public const string Magic="CGENAV01";
    /// <summary>格子の幅と奥行きの上限(セル)。</summary>
    public const int MaximumGridSize=16384;
    public const int MaximumPolygons=1_000_000;
    public const int MaximumPortals=8_000_000;
    private const int PolygonBytes=20;
    private const int PortalBytes=24;
    private readonly NavPolygon[]PolygonArray;
    private readonly NavPortal[]PortalArray;
    private NavMesh(NavMeshSettings Settings,LogicalVector3 Origin,int GridWidth,int GridDepth,NavPolygon[]Polygons,NavPortal[]Portals){
        this.Settings=Settings;
        this.Origin=Origin;
        this.GridWidth=GridWidth;
        this.GridDepth=GridDepth;
        this.PolygonArray=Polygons;
        this.PortalArray=Portals;
        this.BuildQueryTables();
    }
    public NavMeshSettings Settings { get; }
    /// <summary>格子の原点(セル(0,0)の、R・F が最小の角と、段数 0 の高さ)。</summary>
    public LogicalVector3 Origin { get; }
    public int GridWidth { get; }
    public int GridDepth { get; }
    public LogicalScalar CellSize=>this.Settings.CellSize;
    public LogicalScalar CellHeight=>this.Settings.CellHeight;
    public IReadOnlyList<NavPolygon>Polygons=>this.PolygonArray;
    public IReadOnlyList<NavPortal>Portals=>this.PortalArray;
    /// <summary>
    /// 検査を通ったナビメッシュを作る。設定が正しく、全部の多角形が格子の中にあり、全部の接続が実在する多角形の境の上にあるときだけ成功する。
    /// 多角形どうしが重ならないことは調べない(同じ高さで重なっても経路は壊れず、探索が片方を選ぶだけ)。
    /// </summary>
    public static bool TryCreate(NavMeshSettings Settings,LogicalVector3 Origin,int GridWidth,int GridDepth,IReadOnlyList<NavPolygon>Polygons,IReadOnlyList<NavPortal>Portals,out NavMesh?Mesh,out string Error){
        Mesh=null;
        Error=string.Empty;
        if(Settings.CellSize<=LogicalScalar.Zero||Settings.CellHeight<=LogicalScalar.Zero){
            Error="The cell size and the cell height must be positive.";
            return false;
        }
        if(Settings.AgentRadius<LogicalScalar.Zero||Settings.AgentHeight<=LogicalScalar.Zero||Settings.AgentMaxClimb<LogicalScalar.Zero){
            Error="The agent radius and climb must not be negative and the agent height must be positive.";
            return false;
        }
        if(Settings.AgentMaxSlopeDegrees<LogicalScalar.Zero||Settings.AgentMaxSlopeDegrees>=LogicalScalar.FromInt64(90)){
            Error="The agent's maximum slope must be at least 0 and less than 90 degrees.";
            return false;
        }
        if(GridWidth is <1 or >MaximumGridSize||GridDepth is <1 or >MaximumGridSize){
            Error=$"The grid must be 1 to {MaximumGridSize} cells wide and deep.";
            return false;
        }
        if(Polygons.Count>MaximumPolygons||Portals.Count>MaximumPortals){
            Error="The navigation mesh has too many polygons or portals.";
            return false;
        }
        for(var Index=0;Index<Polygons.Count;Index++){
            var Polygon=Polygons[Index];
            if(Polygon.MinX<0||Polygon.MinZ<0||Polygon.MaxX<=Polygon.MinX||Polygon.MaxZ<=Polygon.MinZ||Polygon.MaxX>GridWidth||Polygon.MaxZ>GridDepth){
                Error=$"Polygon {Index} is empty or outside the grid.";
                return false;
            }
            if(Polygon.Y is <-1_000_000 or >1_000_000){
                Error=$"Polygon {Index} has a floor height outside the supported range.";
                return false;
            }
        }
        for(var Index=0;Index<Portals.Count;Index++){
            var Portal=Portals[Index];
            if(Portal.PolygonA<0||Portal.PolygonB<0||Portal.PolygonA>=Polygons.Count||Portal.PolygonB>=Polygons.Count||Portal.PolygonA==Portal.PolygonB){
                Error=$"Portal {Index} joins polygons that do not exist.";
                return false;
            }
            if(!PortalLiesOnBorder(Portal,Polygons[Portal.PolygonA],Polygons[Portal.PolygonB])){
                Error=$"Portal {Index} is not on the border shared by its two polygons.";
                return false;
            }
            var Step=Math.Abs((long)Polygons[Portal.PolygonA].Y-Polygons[Portal.PolygonB].Y);
            if(Step>1_000_000){
                Error=$"Portal {Index} joins floors that are too far apart.";
                return false;
            }
        }
        Mesh=new NavMesh(Settings,Origin,GridWidth,GridDepth,[.. Polygons],[.. Portals]);
        return true;
    }
    /// <summary>線分が、2つの長方形が接する格子の線の上にあり、両方の辺の範囲に収まっていて、長さがある。</summary>
    private static bool PortalLiesOnBorder(NavPortal Portal,NavPolygon A,NavPolygon B){
        if(Portal.X0==Portal.X1){
            if(Portal.Z1<=Portal.Z0){ return false; }
            var Touching=(A.MaxX==Portal.X0&&B.MinX==Portal.X0)||(B.MaxX==Portal.X0&&A.MinX==Portal.X0);
            return Touching&&Portal.Z0>=Math.Max(A.MinZ,B.MinZ)&&Portal.Z1<=Math.Min(A.MaxZ,B.MaxZ);
        }
        if(Portal.Z0==Portal.Z1){
            if(Portal.X1<=Portal.X0){ return false; }
            var Touching=(A.MaxZ==Portal.Z0&&B.MinZ==Portal.Z0)||(B.MaxZ==Portal.Z0&&A.MinZ==Portal.Z0);
            return Touching&&Portal.X0>=Math.Max(A.MinX,B.MinX)&&Portal.X1<=Math.Min(A.MaxX,B.MaxX);
        }
        return false;
    }
    /// <summary>同じ入力から、同じバイト列になる(決定的)。</summary>
    public byte[] ToBytes(){
        using var Stream=new MemoryStream();
        using(var Writer=new BinaryWriter(Stream,System.Text.Encoding.ASCII,leaveOpen:true)){
            foreach(var Character in Magic){ Writer.Write((byte)Character); }
            var Scalar=new byte[LogicalScalar.SerializedSize];
            foreach(var Value in new[]{ this.Settings.AgentRadius,this.Settings.AgentHeight,this.Settings.AgentMaxClimb,this.Settings.AgentMaxSlopeDegrees,this.Settings.CellSize,this.Settings.CellHeight,this.Origin.R,this.Origin.U,this.Origin.F }){
                Value.WriteLittleEndian(Scalar);
                Writer.Write(Scalar);
            }
            Writer.Write(this.GridWidth);
            Writer.Write(this.GridDepth);
            Writer.Write(this.PolygonArray.Length);
            foreach(var Polygon in this.PolygonArray){
                Writer.Write(Polygon.MinX);
                Writer.Write(Polygon.MinZ);
                Writer.Write(Polygon.MaxX);
                Writer.Write(Polygon.MaxZ);
                Writer.Write(Polygon.Y);
            }
            Writer.Write(this.PortalArray.Length);
            foreach(var Portal in this.PortalArray){
                Writer.Write(Portal.PolygonA);
                Writer.Write(Portal.PolygonB);
                Writer.Write(Portal.X0);
                Writer.Write(Portal.Z0);
                Writer.Write(Portal.X1);
                Writer.Write(Portal.Z1);
            }
        }
        return Stream.ToArray();
    }
    /// <summary>
    /// <c>.cgenav</c>を読む。壊れたファイル(印の違い、切り詰め、後ろの余り、範囲外の値、存在しない多角形への参照)は、例外ではなく false と理由で返す。
    /// 件数を確かめてから領域を確保するので、壊れた件数が大きな確保を起こさない。
    /// </summary>
    public static bool TryRead(byte[] Bytes,out NavMesh?Mesh,out string Error){
        Mesh=null;
        var HeaderBytes=Magic.Length+(6+3)*LogicalScalar.SerializedSize+4*3;
        if(Bytes.Length<HeaderBytes){
            Error="The navigation mesh file is truncated (header).";
            return false;
        }
        for(var Index=0;Index<Magic.Length;Index++){
            if(Bytes[Index]!=(byte)Magic[Index]){
                Error="The file is not a navigation mesh (wrong signature or version).";
                return false;
            }
        }
        var Offset=Magic.Length;
        var Scalars=new LogicalScalar[9];
        for(var Index=0;Index<Scalars.Length;Index++){
            if(!LogicalScalar.TryReadLittleEndian(Bytes.AsSpan(Offset,LogicalScalar.SerializedSize),out Scalars[Index])){
                Error="The navigation mesh header holds an unreadable number.";
                return false;
            }
            Offset+=LogicalScalar.SerializedSize;
        }
        var GridWidth=ReadInt(Bytes,ref Offset);
        var GridDepth=ReadInt(Bytes,ref Offset);
        var PolygonCount=ReadInt(Bytes,ref Offset);
        if(PolygonCount<0||PolygonCount>MaximumPolygons||(long)PolygonCount*PolygonBytes+4>Bytes.Length-Offset){
            Error="The navigation mesh file is truncated or its polygon count is invalid.";
            return false;
        }
        var Polygons=new NavPolygon[PolygonCount];
        for(var Index=0;Index<PolygonCount;Index++){
            Polygons[Index]=new NavPolygon(ReadInt(Bytes,ref Offset),ReadInt(Bytes,ref Offset),ReadInt(Bytes,ref Offset),ReadInt(Bytes,ref Offset),ReadInt(Bytes,ref Offset));
        }
        var PortalCount=ReadInt(Bytes,ref Offset);
        if(PortalCount<0||PortalCount>MaximumPortals||(long)PortalCount*PortalBytes>Bytes.Length-Offset){
            Error="The navigation mesh file is truncated or its portal count is invalid.";
            return false;
        }
        if((long)PortalCount*PortalBytes!=Bytes.Length-Offset){
            Error="The navigation mesh file has bytes after its last portal.";
            return false;
        }
        var Portals=new NavPortal[PortalCount];
        for(var Index=0;Index<PortalCount;Index++){
            Portals[Index]=new NavPortal(ReadInt(Bytes,ref Offset),ReadInt(Bytes,ref Offset),ReadInt(Bytes,ref Offset),ReadInt(Bytes,ref Offset),ReadInt(Bytes,ref Offset),ReadInt(Bytes,ref Offset));
        }
        var Settings=new NavMeshSettings(Scalars[0],Scalars[1],Scalars[2],Scalars[3],Scalars[4],Scalars[5]);
        return TryCreate(Settings,new LogicalVector3(Scalars[6],Scalars[7],Scalars[8]),GridWidth,GridDepth,Polygons,Portals,out Mesh,out Error);
    }
    private static int ReadInt(byte[]Bytes,ref int Offset){
        var Value=BinaryPrimitives.ReadInt32LittleEndian(Bytes.AsSpan(Offset,4));
        Offset+=4;
        return Value;
    }
}
