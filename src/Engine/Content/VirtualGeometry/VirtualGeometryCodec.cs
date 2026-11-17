using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Numerics;
using CSharpGameEngine.Content.Models;
namespace CSharpGameEngine.Content.VirtualGeometry;
/// <summary>モデルのメッシュ 1 つの、サブメッシュ 1 つ分の仮想ジオメトリ。</summary>
public sealed record VirtualGeometryEntry(int MeshIndex,int SubmeshIndex,VirtualGeometryMesh Mesh);
/// <summary>
/// 仮想ジオメトリをモデルの追加データ(<see cref="ModelRuntimeBlock"/>、種類 <see cref="Schema"/>)として読み書きする(ADR-0100 決定 3、M0130 単位 3)。
/// 形式はリトルエンディアンの固定長の並び。書くときは -0 を 0 にし、NaN と無限大を断る。読むときは、番号の範囲、塊の三角形の数、親子の関係、余りのバイトまで確かめる。
/// 同じ入力から同じバイト列を書く。
/// </summary>
public static class VirtualGeometryCodec {
    public const string Schema="cge.virtual-geometry/1";
    private const uint Magic=0x47564743u;
    private const ushort Version=1;
    private const int MaximumTrianglesPerCluster=128;
    public static ModelRuntimeBlock CreateBlock(IReadOnlyList<VirtualGeometryEntry>Entries)=>new(Schema,[..Write(Entries)]);
    /// <summary>モデルの仮想ジオメトリ。追加データが無ければ空。2 つ以上あれば断る。</summary>
    public static ImmutableArray<VirtualGeometryEntry>Read(ModelData Model){
        ArgumentNullException.ThrowIfNull(Model);
        var Blocks=Model.RuntimeBlocks.Where(static Block=>Block.Schema==Schema).ToArray();
        if(Blocks.Length==0){ return []; }
        if(Blocks.Length!=1){ throw new InvalidDataException("A model holds at most one virtual geometry block."); }
        return Read(Blocks[0].Payload.AsSpan());
    }
    public static byte[]Write(IReadOnlyList<VirtualGeometryEntry>Entries){
        ArgumentNullException.ThrowIfNull(Entries);
        var Ordered=Entries.OrderBy(static Entry=>Entry.MeshIndex).ThenBy(static Entry=>Entry.SubmeshIndex).ToArray();
        foreach(var Entry in Ordered){ Validate(Entry); }
        for(var Index=1;Index<Ordered.Length;Index++){
            if(Ordered[Index].MeshIndex==Ordered[Index-1].MeshIndex&&Ordered[Index].SubmeshIndex==Ordered[Index-1].SubmeshIndex){ throw new InvalidDataException("Two virtual geometry entries name the same submesh."); }
        }
        var Writer=new Writer();
        Writer.UInt(Magic);
        Writer.UShort(Version);
        Writer.Int(Ordered.Length);
        foreach(var Entry in Ordered){
            var Mesh=Entry.Mesh;
            Writer.Int(Entry.MeshIndex);
            Writer.Int(Entry.SubmeshIndex);
            Writer.Int(Mesh.RootGroup);
            Writer.Int(Mesh.LevelCount);
            Writer.Int(Mesh.Vertices.Length);
            Writer.Int(Mesh.Indices.Length);
            Writer.Int(Mesh.Clusters.Length);
            Writer.Int(Mesh.Groups.Length);
            foreach(var Vertex in Mesh.Vertices){
                Writer.Vector(Vertex.Position);
                Writer.Vector(Vertex.Normal);
                Writer.Float(Vertex.Tangent.X);Writer.Float(Vertex.Tangent.Y);Writer.Float(Vertex.Tangent.Z);Writer.Float(Vertex.Tangent.W);
                Writer.Float(Vertex.Uv.X);Writer.Float(Vertex.Uv.Y);
            }
            foreach(var Index in Mesh.Indices){ Writer.UInt(Index); }
            foreach(var Cluster in Mesh.Clusters){
                Writer.Int(Cluster.FirstIndex);
                Writer.Int(Cluster.IndexCount);
                Writer.Vector(Cluster.BoundsCentre);
                Writer.Float(Cluster.BoundsRadius);
                Writer.Int(Cluster.Group);
                Writer.Int(Cluster.ParentGroup);
            }
            foreach(var Group in Mesh.Groups){
                Writer.Vector(Group.BoundsCentre);
                Writer.Float(Group.BoundsRadius);
                Writer.Float(Group.Error);
                Writer.Int(Group.Level);
                Writer.Int(Group.FirstCluster);
                Writer.Int(Group.ClusterCount);
            }
        }
        return Writer.ToArray();
    }
    public static ImmutableArray<VirtualGeometryEntry>Read(ReadOnlySpan<byte>Bytes){
        var Reader=new Reader(Bytes.ToArray());
        if(Reader.UInt()!=Magic||Reader.UShort()!=Version){ throw new InvalidDataException("This is not a version 1 virtual geometry block."); }
        var Count=Reader.Count(Bytes.Length);
        var Entries=ImmutableArray.CreateBuilder<VirtualGeometryEntry>(Count);
        for(var Entry=0;Entry<Count;Entry++){
            int MeshIndex=Reader.Int(),SubmeshIndex=Reader.Int(),Root=Reader.Int(),Levels=Reader.Int();
            int VertexCount=Reader.Count(Bytes.Length),IndexCount=Reader.Count(Bytes.Length),ClusterCount=Reader.Count(Bytes.Length),GroupCount=Reader.Count(Bytes.Length);
            // 頂点 48 バイト(float 12 個)、番号 4 バイト、塊 32 バイト、まとまり 32 バイト。
            Reader.Require((long)VertexCount*48+(long)IndexCount*4+(long)ClusterCount*32+(long)GroupCount*32);
            var Vertices=new VirtualGeometryVertex[VertexCount];
            for(var Index=0;Index<VertexCount;Index++){
                Vertices[Index]=new(Reader.Vector(),Reader.Vector(),new Vector4(Reader.Float(),Reader.Float(),Reader.Float(),Reader.Float()),new Vector2(Reader.Float(),Reader.Float()));
            }
            var Indices=new uint[IndexCount];
            for(var Index=0;Index<IndexCount;Index++){ Indices[Index]=Reader.UInt(); }
            var Clusters=new VirtualGeometryCluster[ClusterCount];
            for(var Index=0;Index<ClusterCount;Index++){ Clusters[Index]=new(Reader.Int(),Reader.Int(),Reader.Vector(),Reader.Float(),Reader.Int(),Reader.Int()); }
            var Groups=new VirtualGeometryGroup[GroupCount];
            for(var Index=0;Index<GroupCount;Index++){ Groups[Index]=new(Reader.Vector(),Reader.Float(),Reader.Float(),Reader.Int(),Reader.Int(),Reader.Int()); }
            var Item=new VirtualGeometryEntry(MeshIndex,SubmeshIndex,new VirtualGeometryMesh(Vertices,Indices,Clusters,Groups,Root,Levels));
            Validate(Item);
            if(Entries.Count>0){
                var Previous=Entries[^1];
                if(MeshIndex<Previous.MeshIndex||(MeshIndex==Previous.MeshIndex&&SubmeshIndex<=Previous.SubmeshIndex)){ throw new InvalidDataException("Virtual geometry entries are not in submesh order."); }
            }
            Entries.Add(Item);
        }
        if(!Reader.AtEnd){ throw new InvalidDataException("A virtual geometry block has trailing bytes."); }
        return Entries.MoveToImmutable();
    }
    /// <summary>番号の範囲、塊の三角形の数、まとまりと塊の対応、根、有限の数を確かめる。</summary>
    private static void Validate(VirtualGeometryEntry Entry){
        var Mesh=Entry.Mesh;
        Require(Entry.MeshIndex>=0&&Entry.SubmeshIndex>=0,"Virtual geometry names a negative mesh or submesh.");
        Require(Mesh.Vertices.Length>0&&Mesh.Indices.Length>0&&Mesh.Clusters.Length>0&&Mesh.Groups.Length>0,"Virtual geometry is empty.");
        Require(Mesh.LevelCount>=1&&Mesh.LevelCount<=66,"Virtual geometry has an invalid level count.");
        Require(Mesh.RootGroup>=0&&Mesh.RootGroup<Mesh.Groups.Length,"The virtual geometry root is out of range.");
        foreach(var Vertex in Mesh.Vertices){
            Require(Finite(Vertex.Position)&&Finite(Vertex.Normal)&&float.IsFinite(Vertex.Tangent.X)&&float.IsFinite(Vertex.Tangent.Y)&&float.IsFinite(Vertex.Tangent.Z)&&float.IsFinite(Vertex.Tangent.W)&&float.IsFinite(Vertex.Uv.X)&&float.IsFinite(Vertex.Uv.Y),"A virtual geometry vertex is not finite.");
        }
        foreach(var Index in Mesh.Indices){ Require(Index<(uint)Mesh.Vertices.Length,"A virtual geometry index is out of range."); }
        for(var Index=0;Index<Mesh.Clusters.Length;Index++){
            var Cluster=Mesh.Clusters[Index];
            Require(Cluster.IndexCount>=3&&Cluster.IndexCount<=MaximumTrianglesPerCluster*3&&Cluster.IndexCount%3==0,"A cluster holds 1 to 128 complete triangles.");
            Require(Cluster.FirstIndex>=0&&(long)Cluster.FirstIndex+Cluster.IndexCount<=Mesh.Indices.Length,"A cluster's triangles are out of range.");
            Require(Finite(Cluster.BoundsCentre)&&float.IsFinite(Cluster.BoundsRadius)&&Cluster.BoundsRadius>=0f,"A cluster's bounds are invalid.");
            Require(Cluster.Group>=0&&Cluster.Group<Mesh.Groups.Length,"A cluster's group is out of range.");
            var Own=Mesh.Groups[Cluster.Group];
            Require(Index>=Own.FirstCluster&&Index<Own.FirstCluster+Own.ClusterCount,"A cluster is not inside the group that made it.");
            Require(Cluster.ParentGroup==-1?Cluster.Group==Mesh.RootGroup:Cluster.ParentGroup>=0&&Cluster.ParentGroup<Mesh.Groups.Length&&Cluster.ParentGroup!=Cluster.Group,"A cluster's parent group is invalid.");
        }
        foreach(var Group in Mesh.Groups){
            Require(Finite(Group.BoundsCentre)&&float.IsFinite(Group.BoundsRadius)&&Group.BoundsRadius>=0f&&float.IsFinite(Group.Error)&&Group.Error>=0f,"A group's bounds or error are invalid.");
            Require(Group.Level>=0&&Group.Level<Mesh.LevelCount,"A group's level is out of range.");
            Require(Group.ClusterCount>=1&&Group.FirstCluster>=0&&(long)Group.FirstCluster+Group.ClusterCount<=Mesh.Clusters.Length,"A group's clusters are out of range.");
        }
    }
    private static bool Finite(Vector3 Value)=>float.IsFinite(Value.X)&&float.IsFinite(Value.Y)&&float.IsFinite(Value.Z);
    private static void Require(bool Condition,string Message){ if(!Condition){ throw new InvalidDataException(Message); } }
    private sealed class Writer {
        private readonly List<byte>Bytes=[];
        private readonly byte[]Scratch=new byte[4];
        public void UInt(uint Value){ BinaryPrimitives.WriteUInt32LittleEndian(this.Scratch,Value);this.Bytes.AddRange(this.Scratch); }
        public void Int(int Value){ BinaryPrimitives.WriteInt32LittleEndian(this.Scratch,Value);this.Bytes.AddRange(this.Scratch); }
        public void UShort(ushort Value){ BinaryPrimitives.WriteUInt16LittleEndian(this.Scratch,Value);this.Bytes.Add(this.Scratch[0]);this.Bytes.Add(this.Scratch[1]); }
        /// <summary>-0 は 0 にして書く(同じ値が同じバイト列になるように)。</summary>
        public void Float(float Value){
            if(!float.IsFinite(Value)){ throw new InvalidDataException("Virtual geometry holds a non-finite number."); }
            BinaryPrimitives.WriteSingleLittleEndian(this.Scratch,Value==0f?0f:Value);
            this.Bytes.AddRange(this.Scratch);
        }
        public void Vector(Vector3 Value){ this.Float(Value.X);this.Float(Value.Y);this.Float(Value.Z); }
        public byte[]ToArray()=>[..this.Bytes];
    }
    private sealed class Reader(byte[]Bytes) {
        private int Offset;
        public bool AtEnd=>this.Offset==Bytes.Length;
        public void Require(long Length){ if(Length<0||Length>Bytes.Length-this.Offset){ throw new InvalidDataException("A virtual geometry block is truncated."); } }
        public uint UInt(){ this.Require(4);var Value=BinaryPrimitives.ReadUInt32LittleEndian(Bytes.AsSpan(this.Offset));this.Offset+=4;return Value; }
        public int Int(){ this.Require(4);var Value=BinaryPrimitives.ReadInt32LittleEndian(Bytes.AsSpan(this.Offset));this.Offset+=4;return Value; }
        public ushort UShort(){ this.Require(2);var Value=BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan(this.Offset));this.Offset+=2;return Value; }
        public float Float(){
            this.Require(4);
            var Value=BinaryPrimitives.ReadSingleLittleEndian(Bytes.AsSpan(this.Offset));
            this.Offset+=4;
            if(!float.IsFinite(Value)){ throw new InvalidDataException("Virtual geometry holds a non-finite number."); }
            return Value;
        }
        public Vector3 Vector()=>new(this.Float(),this.Float(),this.Float());
        /// <summary>個数。負の値と、残りのバイトより明らかに大きい値を断る。</summary>
        public int Count(int Limit){
            var Value=this.Int();
            if(Value<0||Value>Limit){ throw new InvalidDataException("A virtual geometry count is out of range."); }
            return Value;
        }
    }
}
