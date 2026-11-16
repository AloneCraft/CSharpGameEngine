using System.Buffers.Binary;
using System.Numerics;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.Rendering;
/// <summary>
/// メッシュ1つ分の符号付き距離場（ADR-0078/M0110、メッシュのローカル空間）。<see cref="BoundsMin"/> から始まる <see cref="ResolutionX"/>
/// × <see cref="ResolutionY"/> × <see cref="ResolutionZ"/> 個の立方ボクセルで構成し、各ボクセルの寸法は
/// <see cref="VoxelSize"/> メートル。各ボクセルには中心から最寄りの三角形までの距離を格納し、メッシュ内部では負の値にする。距離はボクセル単位の半精度浮動小数点数で保存するため、
/// メッシュの大きさによらず精度は一定。メッシュの三角形から事前に生成する形状データであり（ビルド時に glTF の隣へ出力）、ライティングデータではない。
/// </summary>
public sealed class MeshDistanceField {
    private readonly Half[]DistancesStorage;
    internal MeshDistanceField(Hash256 MeshHash,int ResolutionX,int ResolutionY,int ResolutionZ,Vector3 BoundsMin,float VoxelSize,Half[]Distances){
        this.MeshHash=MeshHash;
        this.ResolutionX=ResolutionX;
        this.ResolutionY=ResolutionY;
        this.ResolutionZ=ResolutionZ;
        this.BoundsMin=BoundsMin;
        this.VoxelSize=VoxelSize;
        this.DistancesStorage=Distances;
    }
    /// <summary>距離場の生成元となった頂点位置とインデックスのコンテンツハッシュ（<see cref="ComputeMeshHash"/>）。</summary>
    public Hash256 MeshHash { get; }
    public int ResolutionX { get; }
    public int ResolutionY { get; }
    public int ResolutionZ { get; }
    /// <summary>ボリュームの角。ボクセル (0, 0, 0) の中心は、その内側へボクセル半個分進んだ位置。</summary>
    public Vector3 BoundsMin { get; }
    public float VoxelSize { get; }
    public Vector3 BoundsMax=>this.BoundsMin+new Vector3(this.ResolutionX,this.ResolutionY,this.ResolutionZ)*this.VoxelSize;
    /// <summary>X、次に Y、最後に Z の順に並ぶ、ボクセル単位の距離。</summary>
    public ReadOnlySpan<Half>Distances=>this.DistancesStorage;
    /// <summary>1つのボクセルに保存された距離（メートル）。</summary>
    public float VoxelDistance(int X,int Y,int Z)=>(float)this.DistancesStorage[(Z*this.ResolutionY+Y)*this.ResolutionX+X]*this.VoxelSize;
    /// <summary>メッシュ空間上の点での距離を、レンダラーと同じ方法で求める。ボリューム内ではボクセル中心間を三線形補間する。外側ではボリュームのボックスまでの距離を加える。</summary>
    public float Sample(Vector3 Position){
        var Inside=Vector3.Clamp(Position,this.BoundsMin,this.BoundsMax);
        var Outside=Vector3.Distance(Position,Inside);
        var Grid=(Inside-this.BoundsMin)/this.VoxelSize-new Vector3(0.5f);
        Grid=Vector3.Clamp(Grid,Vector3.Zero,new Vector3(this.ResolutionX-1,this.ResolutionY-1,this.ResolutionZ-1));
        var X0=(int)MathF.Floor(Grid.X);
        var Y0=(int)MathF.Floor(Grid.Y);
        var Z0=(int)MathF.Floor(Grid.Z);
        var X1=Math.Min(X0+1,this.ResolutionX-1);
        var Y1=Math.Min(Y0+1,this.ResolutionY-1);
        var Z1=Math.Min(Z0+1,this.ResolutionZ-1);
        var F=Grid-new Vector3(X0,Y0,Z0);
        float Lerp(float A,float B,float T)=>A+(B-A)*T;
        var D00=Lerp(this.VoxelDistance(X0,Y0,Z0),this.VoxelDistance(X1,Y0,Z0),F.X);
        var D10=Lerp(this.VoxelDistance(X0,Y1,Z0),this.VoxelDistance(X1,Y1,Z0),F.X);
        var D01=Lerp(this.VoxelDistance(X0,Y0,Z1),this.VoxelDistance(X1,Y0,Z1),F.X);
        var D11=Lerp(this.VoxelDistance(X0,Y1,Z1),this.VoxelDistance(X1,Y1,Z1),F.X);
        return Lerp(Lerp(D00,D10,F.Y),Lerp(D01,D11,F.Y),F.Z)+Outside;
    }
    /// <summary>頂点位置（リトルエンディアンの float）に続けてインデックス（リトルエンディアンの32ビット整数）を並べた SHA-256。距離場の対応元を識別する。</summary>
    public static Hash256 ComputeMeshHash(IReadOnlyList<Vector3>Positions,IReadOnlyList<uint>Indices){
        ArgumentNullException.ThrowIfNull(Positions);
        ArgumentNullException.ThrowIfNull(Indices);
        var Bytes=new byte[checked(Positions.Count*12+Indices.Count*4)];
        for(var Index=0;Index<Positions.Count;Index++){
            BinaryPrimitives.WriteSingleLittleEndian(Bytes.AsSpan(Index*12,4),Positions[Index].X);
            BinaryPrimitives.WriteSingleLittleEndian(Bytes.AsSpan(Index*12+4,4),Positions[Index].Y);
            BinaryPrimitives.WriteSingleLittleEndian(Bytes.AsSpan(Index*12+8,4),Positions[Index].Z);
        }
        var IndexStart=Positions.Count*12;
        for(var Index=0;Index<Indices.Count;Index++){
            BinaryPrimitives.WriteUInt32LittleEndian(Bytes.AsSpan(IndexStart+Index*4,4),Indices[Index]);
        }
        return Hash256.Compute(Bytes);
    }
}
/// <summary>
/// 三角形から <see cref="MeshDistanceField"/> を作る（M0110）。決定論的になるよう、浮動小数点演算の順序を固定し、同値なら三角形インデックスで決まる中央値分割 BVH
///  を使うため、同じメッシュからは同じバイト列が得られる。体積は立方ボクセルで構成し、メッシュの最長辺を <c>MaximumResolution - 4</c> 個のボクセルに収め、各辺に2ボクセルの
/// 余白を設ける。距離の絶対値は、ボクセル中心から最も近い三角形までの正確な距離。符号は X、Y、Z 方向の3回のレイ交差判定による多数決で決め、交差回数が奇数の軸が2つ以上なら内部とする。閉じたメッシュ
/// では正確に判定でき、開いた平面は両側とも符号なしになる。
/// </summary>
public static class MeshDistanceFieldBuilder {
    /// <summary>メッシュの最長辺に沿うボクセル数。余白を含む（Unreal はサイズに応じて解像度を最大128まで変更する。48なら WARP とメモリの使用量を抑えられる）。</summary>
    public const int DefaultMaximumResolution=48;
    private const int Margin=2;
    /// <summary>メッシュの距離場。面積のある三角形がない（空のメッシュ、または点と線のみのメッシュ）場合は null。有限でない頂点位置や、頂点数を超えるインデックスがある場合は例外をスローする。</summary>
    public static MeshDistanceField?Build(IReadOnlyList<Vector3>Positions,IReadOnlyList<uint>Indices,int MaximumResolution=DefaultMaximumResolution){
        ArgumentNullException.ThrowIfNull(Positions);
        ArgumentNullException.ThrowIfNull(Indices);
        if(MaximumResolution<8||MaximumResolution>MeshDistanceFieldCodec.MaximumResolution){
            throw new ArgumentOutOfRangeException(nameof(MaximumResolution));
        }
        if(Indices.Count%3!=0){
            throw new ArgumentException("Indices must describe whole triangles.",nameof(Indices));
        }
        foreach(var Position in Positions){
            if(!float.IsFinite(Position.X)||!float.IsFinite(Position.Y)||!float.IsFinite(Position.Z)){
                throw new ArgumentException("Mesh positions must be finite.",nameof(Positions));
            }
        }
        List<Triangle>Triangles=[];
        for(var Index=0;Index<Indices.Count;Index+=3){
            if(Indices[Index]>=Positions.Count||Indices[Index+1]>=Positions.Count||Indices[Index+2]>=Positions.Count){
                throw new ArgumentException("An index is outside the positions.",nameof(Indices));
            }
            var Candidate=new Triangle(Positions[(int)Indices[Index]],Positions[(int)Indices[Index+1]],Positions[(int)Indices[Index+2]]);
            if(Vector3.Cross(Candidate.B-Candidate.A,Candidate.C-Candidate.A).LengthSquared()>0f){
                Triangles.Add(Candidate);
            }
        }
        if(Triangles.Count==0){
            return null;
        }
        var Minimum=Triangles[0].A;
        var Maximum=Triangles[0].A;
        foreach(var Item in Triangles){
            Minimum=Vector3.Min(Minimum,Vector3.Min(Item.A,Vector3.Min(Item.B,Item.C)));
            Maximum=Vector3.Max(Maximum,Vector3.Max(Item.A,Vector3.Max(Item.B,Item.C)));
        }
        var Extent=Maximum-Minimum;
        var Longest=MathF.Max(Extent.X,MathF.Max(Extent.Y,Extent.Z));
        var VoxelSize=Longest/(MaximumResolution-2*Margin);
        if(!(VoxelSize>0f)||!float.IsFinite(VoxelSize)){
            return null;
        }
        int Axis(float Size)=>Math.Clamp((int)MathF.Ceiling(Size/VoxelSize)+2*Margin,2*Margin+1,MaximumResolution);
        var ResolutionX=Axis(Extent.X);
        var ResolutionY=Axis(Extent.Y);
        var ResolutionZ=Axis(Extent.Z);
        // メッシュをボリュームの中央に置く。
        var Size=new Vector3(ResolutionX,ResolutionY,ResolutionZ)*VoxelSize;
        var BoundsMin=(Minimum+Maximum)*0.5f-Size*0.5f;
        var Bvh=new TriangleBvh(Triangles);
        var Parity=new byte[ResolutionX*ResolutionY*ResolutionZ];
        for(var RayAxis=0;RayAxis<3;RayAxis++){
            CountCrossings(Triangles,RayAxis,ResolutionX,ResolutionY,ResolutionZ,BoundsMin,VoxelSize,Parity);
        }
        var Distances=new Half[Parity.Length];
        for(var Z=0;Z<ResolutionZ;Z++){
            for(var Y=0;Y<ResolutionY;Y++){
                for(var X=0;X<ResolutionX;X++){
                    var Index=(Z*ResolutionY+Y)*ResolutionX+X;
                    var Centre=BoundsMin+(new Vector3(X,Y,Z)+new Vector3(0.5f))*VoxelSize;
                    var Distance=Bvh.Nearest(Centre)/VoxelSize;
                    Distances[Index]=(Half)(Parity[Index]>=2?-Distance:Distance);
                }
            }
        }
        var Hash=MeshDistanceField.ComputeMeshHash(Positions,Indices);
        return new MeshDistanceField(Hash,ResolutionX,ResolutionY,ResolutionZ,BoundsMin,VoxelSize,Distances);
    }
    /// <summary>
    /// <paramref name="RayAxis"/> に沿ったボクセル列ごとに、列上の直線が三角形を横切る位置を求める。中心までの交差回数が奇数である各ボクセルの票を1つ増やす。直線が辺にぴったり重な
    /// らないよう、ボクセル中心から一定の微小量だけずらす。
    /// </summary>
    private static void CountCrossings(List<Triangle>Triangles,int RayAxis,int ResolutionX,int ResolutionY,int ResolutionZ,Vector3 BoundsMin,float VoxelSize,byte[]Parity){
        Span<int>Resolution=[ResolutionX,ResolutionY,ResolutionZ];
        var U=(RayAxis+1)%3;
        var V=(RayAxis+2)%3;
        var RowsU=Resolution[U];
        var RowsV=Resolution[V];
        var Hits=new List<float>?[RowsU*RowsV];
        float Component(Vector3 Value,int Index)=>Index==0?Value.X:Index==1?Value.Y:Value.Z;
        var OriginU=Component(BoundsMin,U);
        var OriginV=Component(BoundsMin,V);
        var NudgeU=VoxelSize*0.000713f;
        var NudgeV=VoxelSize*0.000317f;
        foreach(var Item in Triangles){
            var Au=Component(Item.A,U);
            var Av=Component(Item.A,V);
            var Bu=Component(Item.B,U);
            var Bv=Component(Item.B,V);
            var Cu=Component(Item.C,U);
            var Cv=Component(Item.C,V);
            var Area=(Bu-Au)*(Cv-Av)-(Bv-Av)*(Cu-Au);
            if(Area==0f){
                continue;
            }
            var FirstU=Math.Max(0,(int)MathF.Floor((MathF.Min(Au,MathF.Min(Bu,Cu))-OriginU)/VoxelSize-0.5f));
            var LastU=Math.Min(RowsU-1,(int)MathF.Ceiling((MathF.Max(Au,MathF.Max(Bu,Cu))-OriginU)/VoxelSize-0.5f));
            var FirstV=Math.Max(0,(int)MathF.Floor((MathF.Min(Av,MathF.Min(Bv,Cv))-OriginV)/VoxelSize-0.5f));
            var LastV=Math.Min(RowsV-1,(int)MathF.Ceiling((MathF.Max(Av,MathF.Max(Bv,Cv))-OriginV)/VoxelSize-0.5f));
            for(var RowV=FirstV;RowV<=LastV;RowV++){
                var Pv=OriginV+(RowV+0.5f)*VoxelSize+NudgeV;
                for(var RowU=FirstU;RowU<=LastU;RowU++){
                    var Pu=OriginU+(RowU+0.5f)*VoxelSize+NudgeU;
                    // 三角形への投影上での直線の点の重心座標。3つの値が同符号なら内部。
                    var W0=(Bu-Pu)*(Cv-Pv)-(Bv-Pv)*(Cu-Pu);
                    var W1=(Cu-Pu)*(Av-Pv)-(Cv-Pv)*(Au-Pu);
                    var W2=(Au-Pu)*(Bv-Pv)-(Av-Pv)*(Bu-Pu);
                    var Inside=Area>0f?W0>=0f&&W1>=0f&&W2>=0f:W0<=0f&&W1<=0f&&W2<=0f;
                    if(!Inside){
                        continue;
                    }
                    var Along=(W0*Component(Item.A,RayAxis)+W1*Component(Item.B,RayAxis)+W2*Component(Item.C,RayAxis))/Area;
                    (Hits[RowV*RowsU+RowU]??=[]).Add(Along);
                }
            }
        }
        Span<int>Voxel=stackalloc int[3];
        for(var RowV=0;RowV<RowsV;RowV++){
            for(var RowU=0;RowU<RowsU;RowU++){
                var Row=Hits[RowV*RowsU+RowU];
                if(Row is null){
                    continue;
                }
                Row.Sort();
                var Origin=Component(BoundsMin,RayAxis);
                var Next=0;
                for(var Step=0;Step<Resolution[RayAxis];Step++){
                    var Centre=Origin+(Step+0.5f)*VoxelSize;
                    while(Next<Row.Count&&Row[Next]<Centre){
                        Next++;
                    }
                    if((Next&1)==0){
                        continue;
                    }
                    Voxel[RayAxis]=Step;
                    Voxel[U]=RowU;
                    Voxel[V]=RowV;
                    Parity[(Voxel[2]*ResolutionY+Voxel[1])*ResolutionX+Voxel[0]]++;
                }
            }
        }
    }
    private readonly record struct Triangle(Vector3 A,Vector3 B,Vector3 C) {
        public Vector3 Centroid=>(this.A+this.B+this.C)/3f;
    }
    /// <summary>最近接点の問い合わせ用に三角形を格納する境界ボリューム階層。最長辺に沿って中央値で分割する。</summary>
    private sealed class TriangleBvh {
        private const int LeafSize=4;
        private readonly Triangle[]Triangles;
        private readonly List<Node>Nodes=[];
        internal TriangleBvh(List<Triangle>Source){
            var Order=Enumerable.Range(0,Source.Count).ToArray();
            this.Triangles=new Triangle[Source.Count];
            this.Build(Source,Order,0,Order.Length);
            for(var Index=0;Index<Order.Length;Index++){
                this.Triangles[Index]=Source[Order[Index]];
            }
        }
        private int Build(List<Triangle>Source,int[]Order,int Start,int End){
            var Minimum=new Vector3(float.MaxValue);
            var Maximum=new Vector3(float.MinValue);
            for(var Index=Start;Index<End;Index++){
                var Item=Source[Order[Index]];
                Minimum=Vector3.Min(Minimum,Vector3.Min(Item.A,Vector3.Min(Item.B,Item.C)));
                Maximum=Vector3.Max(Maximum,Vector3.Max(Item.A,Vector3.Max(Item.B,Item.C)));
            }
            var NodeIndex=this.Nodes.Count;
            this.Nodes.Add(new Node(Minimum,Maximum,Start,End,-1,-1));
            if(End-Start<=LeafSize){
                return NodeIndex;
            }
            var Extent=Maximum-Minimum;
            var Axis=Extent.X>=Extent.Y&&Extent.X>=Extent.Z?0:Extent.Y>=Extent.Z?1:2;
            float Key(int Triangle){
                var Centroid=Source[Triangle].Centroid;
                return Axis==0?Centroid.X:Axis==1?Centroid.Y:Centroid.Z;
            }
            Array.Sort(Order,Start,End-Start,Comparer<int>.Create((Left,Right)=>{
                var Compared=Key(Left).CompareTo(Key(Right));
                return Compared!=0?Compared:Left.CompareTo(Right);
            }));
            var Middle=(Start+End)/2;
            var LeftChild=this.Build(Source,Order,Start,Middle);
            var RightChild=this.Build(Source,Order,Middle,End);
            this.Nodes[NodeIndex]=new Node(Minimum,Maximum,Start,End,LeftChild,RightChild);
            return NodeIndex;
        }
        internal float Nearest(Vector3 Point){
            var Best=float.MaxValue;
            Span<int>Stack=stackalloc int[128];
            var Depth=0;
            Stack[Depth++]=0;
            while(Depth>0){
                var Current=this.Nodes[Stack[--Depth]];
                if(BoxDistanceSquared(Point,Current.Minimum,Current.Maximum)>=Best){
                    continue;
                }
                if(Current.Left<0){
                    for(var Index=Current.Start;Index<Current.End;Index++){
                        var Item=this.Triangles[Index];
                        var Closest=ClosestPoint(Point,Item.A,Item.B,Item.C);
                        Best=MathF.Min(Best,Vector3.DistanceSquared(Point,Closest));
                    }
                    continue;
                }
                var Left=this.Nodes[Current.Left];
                var Right=this.Nodes[Current.Right];
                var LeftDistance=BoxDistanceSquared(Point,Left.Minimum,Left.Maximum);
                var RightDistance=BoxDistanceSquared(Point,Right.Minimum,Right.Maximum);
                // 近い方の子を先に調べるため、先にスタックへ積む。
                if(LeftDistance<=RightDistance){
                    Stack[Depth++]=Current.Right;
                    Stack[Depth++]=Current.Left;
                } else {
                    Stack[Depth++]=Current.Left;
                    Stack[Depth++]=Current.Right;
                }
            }
            return MathF.Sqrt(Best);
        }
        private static float BoxDistanceSquared(Vector3 Point,Vector3 Minimum,Vector3 Maximum)=>
            Vector3.DistanceSquared(Point,Vector3.Clamp(Point,Minimum,Maximum));
        private readonly record struct Node(Vector3 Minimum,Vector3 Maximum,int Start,int End,int Left,int Right);
    }
    /// <summary>点 P に最も近い三角形 ABC 上の点（Ericson, Real-Time Collision Detection 5.1.5）。</summary>
    internal static Vector3 ClosestPoint(Vector3 P,Vector3 A,Vector3 B,Vector3 C){
        var Ab=B-A;
        var Ac=C-A;
        var Ap=P-A;
        var D1=Vector3.Dot(Ab,Ap);
        var D2=Vector3.Dot(Ac,Ap);
        if(D1<=0f&&D2<=0f){ return A; }
        var Bp=P-B;
        var D3=Vector3.Dot(Ab,Bp);
        var D4=Vector3.Dot(Ac,Bp);
        if(D3>=0f&&D4<=D3){ return B; }
        var Vc=D1*D4-D3*D2;
        if(Vc<=0f&&D1>=0f&&D3<=0f){ return A+Ab*(D1/(D1-D3)); }
        var Cp=P-C;
        var D5=Vector3.Dot(Ab,Cp);
        var D6=Vector3.Dot(Ac,Cp);
        if(D6>=0f&&D5<=D6){ return C; }
        var Vb=D5*D2-D1*D6;
        if(Vb<=0f&&D2>=0f&&D6<=0f){ return A+Ac*(D2/(D2-D6)); }
        var Va=D3*D6-D5*D4;
        if(Va<=0f&&D4-D3>=0f&&D5-D6>=0f){ return B+(C-B)*((D4-D3)/((D4-D3)+(D5-D6))); }
        var Denominator=1f/(Va+Vb+Vc);
        return A+Ab*(Vb*Denominator)+Ac*(Vc*Denominator);
    }
}
/// <summary>
/// glTF メッシュの距離場ファイル（M0110）、<c>&lt;asset&gt;.glb.cgesdf</c>。glTF の各メッシュについてインポーター順に1エントリーを持ち、元メッシュの
/// コンテンツハッシュを記録する。読み込んだメッシュとハッシュが一致しないエントリーは無視する。距離場のないメッシュ（スキニング対象または空のメッシュ）は記録しない。リトルエンディアン形式で、"
/// CGEMSDF1"、バージョン、エントリー数に続けて、各エントリーのハッシュ、3方向の解像度（0なら距離場なし）、ボリューム角、ボクセルサイズ、ボクセル単位の半精度距離を格納する。
/// </summary>
public static class MeshDistanceFieldCodec {
    public const int MaximumResolution=128;
    private static ReadOnlySpan<byte>MagicBytes=>"CGEMSDF1"u8;
    private const uint Version=1;
    public static byte[]Write(IReadOnlyList<(Hash256 MeshHash,MeshDistanceField?Field)>Entries){
        ArgumentNullException.ThrowIfNull(Entries);
        using var Stream=new MemoryStream();
        // BinaryWriter は、どのプラットフォームでもリトルエンディアンで書き込む。
        using(var Writer=new BinaryWriter(Stream,System.Text.Encoding.UTF8,leaveOpen:true)){
            Writer.Write(MagicBytes);
            Writer.Write(Version);
            Writer.Write(Entries.Count);
            foreach(var (MeshHash,Field) in Entries){
                if(Field is not null&&Field.MeshHash!=MeshHash){
                    throw new ArgumentException("A field must belong to the mesh hash it is written with.",nameof(Entries));
                }
                Writer.Write(MeshHash.ToByteArray());
                Writer.Write(Field?.ResolutionX??0);
                Writer.Write(Field?.ResolutionY??0);
                Writer.Write(Field?.ResolutionZ??0);
                Writer.Write(Field?.BoundsMin.X??0f);
                Writer.Write(Field?.BoundsMin.Y??0f);
                Writer.Write(Field?.BoundsMin.Z??0f);
                Writer.Write(Field?.VoxelSize??0f);
                if(Field is null){
                    continue;
                }
                foreach(var Value in Field.Distances){
                    Writer.Write(Value);
                }
            }
        }
        return Stream.ToArray();
    }
    public static bool TryRead(ReadOnlySpan<byte>Bytes,out IReadOnlyList<(Hash256 MeshHash,MeshDistanceField?Field)>Entries,out string Error){
        List<(Hash256,MeshDistanceField?)>Result=[];
        Entries=Result;
        Error=string.Empty;
        if(Bytes.Length<16||!Bytes[..8].SequenceEqual(MagicBytes)){
            Error="Not a mesh distance field file (CGEMSDF1).";
            return false;
        }
        if(BinaryPrimitives.ReadUInt32LittleEndian(Bytes.Slice(8,4))!=Version){
            Error="Unsupported mesh distance field version.";
            return false;
        }
        var Count=BinaryPrimitives.ReadInt32LittleEndian(Bytes.Slice(12,4));
        if(Count<0||Count>65536){
            Error="The entry count is out of range.";
            return false;
        }
        var Offset=16;
        for(var Entry=0;Entry<Count;Entry++){
            const int HeaderSize=32+12+16;
            if(Bytes.Length-Offset<HeaderSize){
                Error=$"Entry {Entry} is truncated.";
                return false;
            }
            var Hash=Hash256.FromBytes(Bytes.Slice(Offset,32).ToArray());
            var X=BinaryPrimitives.ReadInt32LittleEndian(Bytes.Slice(Offset+32,4));
            var Y=BinaryPrimitives.ReadInt32LittleEndian(Bytes.Slice(Offset+36,4));
            var Z=BinaryPrimitives.ReadInt32LittleEndian(Bytes.Slice(Offset+40,4));
            var Min=new Vector3(BinaryPrimitives.ReadSingleLittleEndian(Bytes.Slice(Offset+44,4)),BinaryPrimitives.ReadSingleLittleEndian(Bytes.Slice(Offset+48,4)),BinaryPrimitives.ReadSingleLittleEndian(Bytes.Slice(Offset+52,4)));
            var Voxel=BinaryPrimitives.ReadSingleLittleEndian(Bytes.Slice(Offset+56,4));
            Offset+=HeaderSize;
            if(X==0&&Y==0&&Z==0){
                Result.Add((Hash,null));
                continue;
            }
            if(X<1||Y<1||Z<1||X>MaximumResolution||Y>MaximumResolution||Z>MaximumResolution
                ||!float.IsFinite(Min.X)||!float.IsFinite(Min.Y)||!float.IsFinite(Min.Z)||!float.IsFinite(Voxel)||!(Voxel>0f)){
                Error=$"Entry {Entry} has an invalid volume.";
                return false;
            }
            var Voxels=X*Y*Z;
            if(Bytes.Length-Offset<Voxels*2){
                Error=$"Entry {Entry} is truncated.";
                return false;
            }
            var Distances=new Half[Voxels];
            for(var Index=0;Index<Voxels;Index++){
                var Value=BinaryPrimitives.ReadHalfLittleEndian(Bytes.Slice(Offset+Index*2,2));
                if(!Half.IsFinite(Value)){
                    Error=$"Entry {Entry} has a distance that is not finite.";
                    return false;
                }
                Distances[Index]=Value;
            }
            Offset+=Voxels*2;
            Result.Add((Hash,new MeshDistanceField(Hash,X,Y,Z,Min,Voxel,Distances)));
        }
        if(Offset!=Bytes.Length){
            Error="Trailing bytes after the last entry.";
            return false;
        }
        return true;
    }
}
