using System.Numerics;
using CSharpGameEngine.Content.Tiles;
using CSharpGameEngine.Numerics;
namespace CSharpGameEngine.World.Terrain;
/// <summary>
/// マス 1 つの形（ADR-0100 決定 7、M0134）。<see cref="Anchor"/> は世界の位置（Q64.64、マスの中心の高さ 0）で、頂点の位置は Anchor からの差（メートル、float）。
/// 描くときは Anchor − カメラ を Q64.64 で引いてから物の位置にする。
/// </summary>
public sealed record TerrainCellMesh(TerrainCellKey Key,LogicalVector3 Anchor,Vector3[]Positions,Vector3[]Normals,Vector2[]Uvs,uint[]Indices,LogicalVector3[]WorldPositions) {
    /// <summary>
    /// 形を作る。頂点 (a, b)（0〜GridQuads）は格子の点 (I0 + a × 間隔, J0 + b × 間隔) の、高さ <paramref name="Heights"/> の位置。
    /// 粗い隣に接する辺の奇数番目の頂点は、両隣の頂点の真ん中（Q64.64）に置く。法線は格子の 1 つ外まで含めた位置の差から求める。
    /// </summary>
    public static TerrainCellMesh Build(TerrainSettings Settings,TerrainCell Cell,ITerrainHeightField Heights){
        var Key=Cell.Key;
        var N=Settings.GridQuads;
        var Step=(long)1<<(Settings.MaximumDepth-Key.Depth);
        var I0=Key.X*N*Step;
        var J0=Key.Y*N*Step;
        // 1 つ外の輪まで含めた世界の位置（法線のため）。
        var Ring=N+3;
        var World=new LogicalVector3[Ring*Ring];
        for(var B=-1;B<=N+1;B++){
            for(var A=-1;A<=N+1;A++){
                var I=I0+A*Step;
                var J=J0+B*Step;
                World[(B+1)*Ring+A+1]=TerrainCells.SurfacePoint(Settings,Key.Face,I,J,Heights.Height(TerrainCells.HeightSamplePoint(Settings,Key.Face,I,J)));
            }
        }
        var Two=LogicalScalar.FromInt64(2);
        LogicalVector3 Midpoint(LogicalVector3 First,LogicalVector3 Second)=>(First+Second)/Two;
        ref LogicalVector3 At(int A,int B)=>ref World[(B+1)*Ring+A+1];
        // 粗い隣に接する辺: 奇数番目を真ん中へ（-x、+x、-y、+y）。
        for(var Index=1;Index<N;Index+=2){
            if((Cell.CoarserEdges&1)!=0){ At(0,Index)=Midpoint(At(0,Index-1),At(0,Index+1)); }
            if((Cell.CoarserEdges&2)!=0){ At(N,Index)=Midpoint(At(N,Index-1),At(N,Index+1)); }
            if((Cell.CoarserEdges&4)!=0){ At(Index,0)=Midpoint(At(Index-1,0),At(Index+1,0)); }
            if((Cell.CoarserEdges&8)!=0){ At(Index,N)=Midpoint(At(Index-1,N),At(Index+1,N)); }
        }
        var Anchor=TerrainCells.SurfacePoint(Settings,Key.Face,I0+N*Step/2,J0+N*Step/2,0f);
        var Count=(N+1)*(N+1);
        var WorldPositions=new LogicalVector3[Count];
        var Positions=new Vector3[Count];
        var Normals=new Vector3[Count];
        var Uvs=new Vector2[Count];
        Vector3 Local(int A,int B){
            (At(A,B)-Anchor).TryToSingle(out var Value);
            return Value;
        }
        for(var B=0;B<=N;B++){
            for(var A=0;A<=N;A++){
                var Index=B*(N+1)+A;
                Positions[Index]=Local(A,B);
                WorldPositions[Index]=At(A,B);
                var Along=Local(A+1,B)-Local(A-1,B);
                var Across=Local(A,B+1)-Local(A,B-1);
                var Normal=Vector3.Cross(Across,Along);
                // 外向き（球は中心から、平面は上）へそろえる。
                var Outward=Settings.Shape==TerrainShape.Sphere?Positions[Index]+LocalOf(Anchor-Settings.Origin):Vector3.UnitY;
                if(Vector3.Dot(Normal,Outward)<0f){ Normal=-Normal; }
                Normals[Index]=Normal.LengthSquared()>0f?Vector3.Normalize(Normal):Vector3.UnitY;
                Uvs[Index]=new Vector2((float)A/N,(float)B/N);
            }
        }
        var Indices=new uint[N*N*6];
        var Write=0;
        for(var B=0;B<N;B++){
            for(var A=0;A<N;A++){
                var V00=(uint)(B*(N+1)+A);
                var V10=V00+1;
                var V01=V00+(uint)(N+1);
                var V11=V01+1;
                Indices[Write++]=V00;
                Indices[Write++]=V01;
                Indices[Write++]=V11;
                Indices[Write++]=V00;
                Indices[Write++]=V11;
                Indices[Write++]=V10;
            }
        }
        // 三角形の向きを外向きの法線にそろえる（面ごとに u × v の向きが違うため）。
        var First=Vector3.Cross(Positions[Indices[1]]-Positions[Indices[0]],Positions[Indices[2]]-Positions[Indices[0]]);
        if(Vector3.Dot(First,Normals[Indices[0]])<0f){
            for(var Index=0;Index<Indices.Length;Index+=3){ (Indices[Index+1],Indices[Index+2])=(Indices[Index+2],Indices[Index+1]); }
        }
        return new TerrainCellMesh(Key,Anchor,Positions,Normals,Uvs,Indices,WorldPositions);
    }
    private static Vector3 LocalOf(LogicalVector3 Value){
        Value.TryToSingle(out var Result);
        return Result;
    }
}
/// <summary>
/// タイルの階層から高さを読む地形（ADR-0100 決定 7、M0134）。球は向きを経緯度（+Y が北極、+X が経度 0、+Z が経度 +90°）にして正距円筒のタイルを読み、
/// 平面は中心からの (x, z) を範囲（West〜East、North〜South、メートル）でタイルの画素にする。読む段は <see cref="Level"/>（既定は段 0、元の解像度）。
/// 値の無い所は 0。
/// </summary>
public sealed class TilePyramidHeightField(TilePyramid Pyramid,int Level=0,float Scale=1f):ITerrainHeightField {
    public int Level { get; }=Math.Clamp(Level,0,Pyramid.Header.LevelCount-1);
    public float Height(Vector3 Point){
        var Header=Pyramid.Header;
        float U;
        float V;
        if(Header.Projection==TileProjection.Equirectangular){
            var Longitude=MathF.Atan2(Point.Z,Point.X)*180f/MathF.PI;
            var Latitude=MathF.Asin(Math.Clamp(Point.Y,-1f,1f))*180f/MathF.PI;
            U=(Longitude-Header.West)/(Header.East-Header.West);
            U-=MathF.Floor(U);
            V=(Header.North-Latitude)/(Header.North-Header.South);
        } else {
            U=(Point.X-Header.West)/(Header.East-Header.West);
            V=(Header.North-Point.Z)/(Header.North-Header.South);
        }
        var Width=Header.LevelWidth(this.Level);
        var Height=Header.LevelHeight(this.Level);
        var Value=Pyramid.SampleBilinear(this.Level,U*Width,V*Height);
        return float.IsNaN(Value)?0f:Value*Scale;
    }
}
