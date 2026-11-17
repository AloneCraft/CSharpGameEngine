using System.Numerics;
using CSharpGameEngine.Content.VirtualGeometry;
namespace CSharpGameEngine.Cooking.VirtualGeometry;
/// <summary>
/// 物体の向きの法線マップを焼く（ADR-0100 決定 3、M0130 単位 6）。元の細かいメッシュの三角形を UV の上に塗り、頂点の法線をなめらかにつないだ向きを
/// 物体の座標のまま書く（RGBA8、向きの各成分を 0〜1 に写す）。粗い塊も同じ UV を持つので、どの細かさの塊から引いても同じ陰影になる。
/// 条件: UV が重ならないこと。塗った画素の 1% より多くが 2 枚以上の三角形の内側にあれば、重なっているとして焼かない（null）。
/// 塗られない画素は、隣の塗られた画素の値で数画素ぶん埋める（ミップと縁のにじみ止め）。
/// </summary>
public static class ObjectNormalMapBaker {
    /// <summary>縁を外へ広げる画素数。</summary>
    public const int DilationPixels=4;
    /// <summary>重なりとみなす、2 枚以上の三角形の内側にある画素の割合。</summary>
    public const float OverlapLimit=0.01f;
    /// <summary>三角形の数から決める大きさ: 4×√三角形数 を 2 のべき乗に切り上げ、256〜2048 に収める。</summary>
    public static int SizeFor(int TriangleCount){
        var Wanted=Math.Clamp((int)MathF.Ceiling(4f*MathF.Sqrt(Math.Max(TriangleCount,1))),256,2048);
        var Size=256;
        while(Size<Wanted){ Size*=2; }
        return Size;
    }
    /// <summary>焼いた RGBA8。UV が重なっているか、塗れる三角形が無ければ null。</summary>
    public static byte[]?Bake(ReadOnlySpan<VirtualGeometryVertex>Vertices,ReadOnlySpan<uint>Indices,int Size){
        ArgumentOutOfRangeException.ThrowIfLessThan(Size,1);
        if(Indices.Length%3!=0){ throw new ArgumentException("The index count must be a multiple of three.",nameof(Indices)); }
        var Normals=new Vector3[Size*Size];
        var Filled=new bool[Size*Size];
        var FilledCount=0;
        var Overlaps=0;
        for(var Corner=0;Corner<Indices.Length;Corner+=3){
            var A=Vertices[(int)Indices[Corner]];
            var B=Vertices[(int)Indices[Corner+1]];
            var C=Vertices[(int)Indices[Corner+2]];
            var Pa=A.Uv*Size;
            var Pb=B.Uv*Size;
            var Pc=C.Uv*Size;
            var Area=Cross(Pb-Pa,Pc-Pa);
            if(MathF.Abs(Area)<1e-12f){ continue; }
            var MinimumX=Math.Max(0,(int)MathF.Floor(MathF.Min(Pa.X,MathF.Min(Pb.X,Pc.X))));
            var MaximumX=Math.Min(Size-1,(int)MathF.Ceiling(MathF.Max(Pa.X,MathF.Max(Pb.X,Pc.X))));
            var MinimumY=Math.Max(0,(int)MathF.Floor(MathF.Min(Pa.Y,MathF.Min(Pb.Y,Pc.Y))));
            var MaximumY=Math.Min(Size-1,(int)MathF.Ceiling(MathF.Max(Pa.Y,MathF.Max(Pb.Y,Pc.Y))));
            for(var Y=MinimumY;Y<=MaximumY;Y++){
                for(var X=MinimumX;X<=MaximumX;X++){
                    var P=new Vector2(X+0.5f,Y+0.5f);
                    var Wa=Cross(Pb-P,Pc-P)/Area;
                    var Wb=Cross(Pc-P,Pa-P)/Area;
                    var Wc=1f-Wa-Wb;
                    const float Edge=-1e-5f;
                    if(Wa<Edge||Wb<Edge||Wc<Edge){ continue; }
                    var Normal=A.Normal*Wa+B.Normal*Wb+C.Normal*Wc;
                    if(Normal.LengthSquared()<1e-20f){ continue; }
                    // 辺の上の画素は隣の三角形とも共有するので、はっきり内側（重みがどれも 1% 以上）のときだけ重なりに数える。
                    if(Filled[Y*Size+X]){
                        if(Wa>0.01f&&Wb>0.01f&&Wc>0.01f){ Overlaps++; }
                    } else {
                        FilledCount++;
                    }
                    Normals[Y*Size+X]=Vector3.Normalize(Normal);
                    Filled[Y*Size+X]=true;
                }
            }
        }
        if(FilledCount==0||Overlaps>FilledCount*OverlapLimit){ return null; }
        Dilate(Normals,Filled,Size);
        var Pixels=new byte[Size*Size*4];
        for(var Index=0;Index<Normals.Length;Index++){
            var Normal=Filled[Index]?Normals[Index]:Vector3.UnitZ;
            Pixels[Index*4]=Encode(Normal.X);
            Pixels[Index*4+1]=Encode(Normal.Y);
            Pixels[Index*4+2]=Encode(Normal.Z);
            Pixels[Index*4+3]=255;
        }
        return Pixels;
    }
    private static float Cross(Vector2 A,Vector2 B)=>A.X*B.Y-A.Y*B.X;
    private static byte Encode(float Value)=>(byte)Math.Clamp((int)MathF.Round((Value*0.5f+0.5f)*255f),0,255);
    /// <summary>塗られていない画素を、塗られた 8 近傍の平均で埋める。1 回で 1 画素ぶん広がる。</summary>
    private static void Dilate(Vector3[]Normals,bool[]Filled,int Size){
        for(var Pass=0;Pass<DilationPixels;Pass++){
            List<(int Index,Vector3 Normal)>Added=[];
            for(var Y=0;Y<Size;Y++){
                for(var X=0;X<Size;X++){
                    if(Filled[Y*Size+X]){ continue; }
                    var Sum=Vector3.Zero;
                    for(var Dy=-1;Dy<=1;Dy++){
                        for(var Dx=-1;Dx<=1;Dx++){
                            int Nx=X+Dx,Ny=Y+Dy;
                            if(Nx<0||Ny<0||Nx>=Size||Ny>=Size||!Filled[Ny*Size+Nx]){ continue; }
                            Sum+=Normals[Ny*Size+Nx];
                        }
                    }
                    if(Sum.LengthSquared()>1e-20f){ Added.Add((Y*Size+X,Vector3.Normalize(Sum))); }
                }
            }
            foreach(var (Index,Normal) in Added){
                Normals[Index]=Normal;
                Filled[Index]=true;
            }
        }
    }
}
