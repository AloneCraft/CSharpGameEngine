using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace CSharpGameEngine.Runtime.Physics;

/// <summary>
/// 閉じた凸形状の元形状を分割する方法（ADR-0078/M0113）。<see cref="ClusterCount"/> は破片をまとめる最上位クラスターの数。0 なら √
/// <see cref="FragmentCount"/> に最も近い整数を選び、1 なら階層を作らない。<see cref="ClusterLevels"/>（M0124、1～4、UE Geometry
///  Collection の階層）は、各階層のクラスターをそれぞれ、含まれる破片数の平方根に最も近い数の連結クラスターに順次分割する。2 なら、大きな塊に分かれた後、それぞれが小さな塊に分かれ、最後に
/// 破片になる。
/// </summary>
public readonly record struct PrefractureCookSettings(
    int FragmentCount,int Seed,LogicalScalar Density,LogicalScalar BondStrengthPerArea,int ClusterCount=0,int ClusterLevels=1){
    /// <summary>調理処理が作成するクラスター階層の最大段数。</summary>
    public const int MaximumClusterLevels=4;
}

public sealed class FractureMesh {
    /// <summary>元形状の表面にある三角形の材質スロット。</summary>
    public const byte OuterSlot=0;
    /// <summary>ボロノイ分割面（元形状の内側、UE の interior material）にある三角形の材質スロット。</summary>
    public const byte InteriorSlot=1;
    private struct EdgeUse {
        internal int Count;
        internal int OrientationBalance;
        internal int FirstTriangle;
        internal int SecondTriangle;
    }

    private readonly LogicalVector3[]VertexStorage;
    private readonly int[]IndexStorage;
    private readonly byte[]SlotStorage;

    internal FractureMesh(IReadOnlyList<LogicalVector3>Vertices,IReadOnlyList<int>Indices,IReadOnlyList<byte>?Slots=null){
        ArgumentNullException.ThrowIfNull(Vertices);
        ArgumentNullException.ThrowIfNull(Indices);
        if(Vertices.Count<4||Indices.Count<12||Indices.Count%3!=0){
            throw new ArgumentException("A fracture mesh must contain a closed triangulated volume.");
        }
        this.VertexStorage=Vertices.ToArray();
        this.IndexStorage=Indices.ToArray();
        this.SlotStorage=Slots is null?new byte[this.IndexStorage.Length/3]:Slots.ToArray();
        if(this.SlotStorage.Length!=this.IndexStorage.Length/3||this.SlotStorage.Any(static Slot=>Slot>InteriorSlot)){
            throw new ArgumentException("A fracture mesh needs one known material slot per triangle.",nameof(Slots));
        }
        for(int Index=0;Index<this.IndexStorage.Length;Index++){
            if((uint)this.IndexStorage[Index]>=(uint)this.VertexStorage.Length){
                throw new ArgumentOutOfRangeException(nameof(Indices));
            }
        }
    }

    public IReadOnlyList<LogicalVector3>Vertices=>this.VertexStorage;
    public IReadOnlyList<int>Indices=>this.IndexStorage;
    /// <summary>各三角形の材質スロット（<see cref="OuterSlot"/> または <see cref="InteriorSlot"/>）。</summary>
    public IReadOnlyList<byte>Slots=>this.SlotStorage;
    /// <summary>三角形が囲む符号付き体積（m³）。頂点の巻き順が外向きなら正。</summary>
    public LogicalScalar SignedVolume(){
        var SixVolume=LogicalScalar.Zero;
        for(var Index=0;Index<this.IndexStorage.Length;Index+=3){
            SixVolume+=LogicalVector3.Dot(this.VertexStorage[this.IndexStorage[Index]],LogicalVector3.Cross(this.VertexStorage[this.IndexStorage[Index+1]],this.VertexStorage[this.IndexStorage[Index+2]]));
        }
        return SixVolume/LogicalScalar.FromInt64(6);
    }

    public bool IsClosedManifold(){
        return this.TryGetTopologyError(out _);
    }

    internal bool TryGetTopologyError(out string Error){
        Dictionary<(int First,int Second),EdgeUse>Edges=[];
        bool[]Referenced=new bool[this.VertexStorage.Length];
        int TriangleCount=this.IndexStorage.Length/3;
        for(int Index=0;Index<this.IndexStorage.Length;Index+=3){
            int A=this.IndexStorage[Index];
            int B=this.IndexStorage[Index+1];
            int C=this.IndexStorage[Index+2];
            if(A==B||B==C||C==A){
                Error=$"triangle {Index/3} is degenerate ({A},{B},{C})";
                return false;
            }
            if(LogicalVector3.Cross(
                this.VertexStorage[B]-this.VertexStorage[A],
                this.VertexStorage[C]-this.VertexStorage[A]).LengthSquared.IsZero){
                Error=$"triangle {Index/3} has zero area";
                return false;
            }
            Referenced[A]=true;
            Referenced[B]=true;
            Referenced[C]=true;
            int Triangle=Index/3;
            AddEdge(Edges,A,B,Triangle);
            AddEdge(Edges,B,C,Triangle);
            AddEdge(Edges,C,A,Triangle);
        }
        for(int Index=0;Index<Referenced.Length;Index++){
            if(Referenced[Index]){ continue; }
            Error=$"vertex {Index} is not referenced";
            return false;
        }
        int[]Parents=new int[TriangleCount];
        for(int Index=0;Index<Parents.Length;Index++){ Parents[Index]=Index; }
        foreach(KeyValuePair<(int First,int Second),EdgeUse>Edge in Edges){
            EdgeUse Use=Edge.Value;
            if(Use.Count!=2){
                Error=$"edge ({Edge.Key.First},{Edge.Key.Second}) is shared {Use.Count} time(s)";
                return false;
            }
            if(Use.OrientationBalance!=0){
                Error=$"edge ({Edge.Key.First},{Edge.Key.Second}) has inconsistent winding";
                return false;
            }
            Union(Parents,Use.FirstTriangle,Use.SecondTriangle);
        }
        int Root=Find(Parents,0);
        for(int Triangle=1;Triangle<Parents.Length;Triangle++){
            if(Find(Parents,Triangle)==Root){ continue; }
            Error=$"triangle {Triangle} belongs to a disconnected shell";
            return false;
        }
        Error=Edges.Count==0?"the mesh has no edges":string.Empty;
        return Edges.Count>0;
    }

    private static void AddEdge(
        Dictionary<(int First,int Second),EdgeUse>Edges,int First,int Second,int Triangle){
        (int First,int Second)Key=First<Second?(First,Second):(Second,First);
        Edges.TryGetValue(Key,out EdgeUse Use);
        if(Use.Count==0){ Use.FirstTriangle=Triangle; }
        else if(Use.Count==1){ Use.SecondTriangle=Triangle; }
        Use.Count++;
        Use.OrientationBalance+=First<Second?1:-1;
        Edges[Key]=Use;
    }

    private static int Find(int[]Parents,int Value){
        while(Parents[Value]!=Value){
            Parents[Value]=Parents[Parents[Value]];
            Value=Parents[Value];
        }
        return Value;
    }

    private static void Union(int[]Parents,int First,int Second){
        int FirstRoot=Find(Parents,First);
        int SecondRoot=Find(Parents,Second);
        if(FirstRoot!=SecondRoot){ Parents[SecondRoot]=FirstRoot; }
    }
}

/// <summary>
/// <c>.cgefracture</c> アーティファクト形式（L6、ADR-0078/M0113）。リトルエンディアンで、すべてのスカラーは16バイトの Q64.64
/// <see cref="LogicalScalar"/>、文字列は7ビット長付き UTF-8。クラスター1階層向けに書き込むバージョン2（<c>CGEFRAC2</c>）は、ソースハッシュ、シード、密度
/// 、面積あたりの強度、破片数に続き、各破片のインデックス、形状 ID、中心、質量、ボロノイサイト、クラスター、頂点、インデックス、三角形ごとの材質スロット1バイトを格納し、その後にボンド（最初、2番目、
/// 面積、強度）を格納する。2～4階層向けに書き込むバージョン3（<c>CGEFRAC3</c>、M0124）は、同じ内容に加え、破片数の後に段数（int32）を格納し、各破片について単一のクラスターの代
/// わりに、各階層のクラスター番号（最上位から）を格納する。1階層のアセットはバージョン2で書き込むため、M0113 が生成したものとバイト列が一致する。サイト、クラスター、材質スロット、面積、設定値を持
/// たないバージョン1（<c>CGEFRAC1</c>、M0055）も引き続き読み込む。読み込み時に切り詰め、末尾の余分なデータ、範囲外の値、非正規形式を検出した場合は例外を投げずに拒否する。
/// </summary>
public static class PrefractureArtifact {
    private static ReadOnlySpan<byte>MagicVersion1=>"CGEFRAC1"u8;
    private static ReadOnlySpan<byte>MagicVersion2=>"CGEFRAC2"u8;
    private static ReadOnlySpan<byte>Magic=>"CGEFRAC3"u8;
    private static readonly UTF8Encoding StrictUtf8=new(false,true);
    /// <summary><see cref="Write"/> が書き込む最新バージョン（クラスター階層が複数あるアセットの場合。<see cref="VersionOf"/> を参照）。</summary>
    public const int Version=3;
    /// <summary><paramref name="Asset"/> に対して <see cref="Write"/> が書き込むバージョン。クラスターが複数階層なら3、それ以外なら2。</summary>
    public static int VersionOf(PrefractureAsset Asset){
        ArgumentNullException.ThrowIfNull(Asset);
        return Asset.ClusterLevelCount>1?3:2;
    }
    private const int MaximumStringBytes=4096;
    private const int MaximumFragments=256;
    private const int MaximumVerticesPerFragment=65_536;
    private const int MaximumIndicesPerFragment=1_000_000;

    public static void Write(Stream Output,PrefractureAsset Asset){
        ArgumentNullException.ThrowIfNull(Output);
        ArgumentNullException.ThrowIfNull(Asset);
        if(string.IsNullOrWhiteSpace(Asset.SourceHash)||!Asset.IsCanonicalArtifact()
            ||Asset.Fragments.Count is <2 or >MaximumFragments
            ||Asset.Sites.Count!=Asset.Fragments.Count||Asset.Clusters.Count!=Asset.Fragments.Count){
            throw new ArgumentException("Prefracture asset is not canonical.",nameof(Asset));
        }
        var FileVersion=VersionOf(Asset);
        if(Asset.ClusterLevelCount>PrefractureCookSettings.MaximumClusterLevels){ throw new ArgumentException("Prefracture asset has too many cluster levels.",nameof(Asset)); }
        using BinaryWriter Writer=new(Output,Encoding.UTF8,true);
        Writer.Write(FileVersion==3?Magic:MagicVersion2);
        Writer.Write(FileVersion);
        Writer.Write(Asset.SourceHash);
        Writer.Write(Asset.Seed);
        WriteScalar(Writer,Asset.Density);
        WriteScalar(Writer,Asset.BondStrengthPerArea);
        Writer.Write(Asset.Fragments.Count);
        if(FileVersion==3){ Writer.Write(Asset.ClusterLevelCount); }
        for(var Index=0;Index<Asset.Fragments.Count;Index++){
            var Fragment=Asset.Fragments[Index];
            var Mesh=Asset.Meshes[Index];
            Writer.Write(Fragment.Index);
            Writer.Write(Fragment.ShapeId);
            WriteVector(Writer,Fragment.LocalCenter);
            WriteScalar(Writer,Fragment.Mass);
            WriteVector(Writer,Asset.Sites[Index]);
            for(var Level=0;Level<Asset.ClusterLevelCount;Level++){ Writer.Write(Asset.ClusterOf(Index,Level)); }
            Writer.Write(Mesh.Vertices.Count);
            foreach(var Vertex in Mesh.Vertices){ WriteVector(Writer,Vertex); }
            Writer.Write(Mesh.Indices.Count);
            foreach(var MeshIndex in Mesh.Indices){ Writer.Write(MeshIndex); }
            foreach(var Slot in Mesh.Slots){ Writer.Write(Slot); }
        }
        Writer.Write(Asset.Bonds.Count);
        foreach(var Bond in Asset.Bonds){
            Writer.Write(Bond.FirstFragment);
            Writer.Write(Bond.SecondFragment);
            WriteScalar(Writer,Bond.Area);
            WriteScalar(Writer,Bond.Strength);
        }
    }

    /// <summary><see cref="Write"/> が書き込むバイト列。同じアセットからは常に同じバイト列が得られる。</summary>
    public static byte[]ToBytes(PrefractureAsset Asset){
        using MemoryStream Stream=new();
        Write(Stream,Asset);
        return Stream.ToArray();
    }

    public static bool TryRead(Stream Input,out PrefractureAsset?Asset,out string Error){
        ArgumentNullException.ThrowIfNull(Input);
        Asset=null;
        Error=string.Empty;
        try {
            Span<byte>MagicBytes=stackalloc byte[Magic.Length];
            Input.ReadExactly(MagicBytes);
            var FileVersion=MagicBytes.SequenceEqual(Magic)?3:MagicBytes.SequenceEqual(MagicVersion2)?2:MagicBytes.SequenceEqual(MagicVersion1)?1:0;
            if(FileVersion==0){ throw new InvalidDataException(); }
            using BinaryReader Reader=new(Input,Encoding.UTF8,true);
            if(Reader.ReadInt32()!=FileVersion){ throw new InvalidDataException(); }
            var SourceHash=ReadString(Reader);
            if(string.IsNullOrWhiteSpace(SourceHash)){ throw new InvalidDataException(); }
            var Seed=Reader.ReadInt32();
            var Density=FileVersion>=2?ReadScalar(Reader):LogicalScalar.Zero;
            var StrengthPerArea=FileVersion>=2?ReadScalar(Reader):LogicalScalar.Zero;
            if(Density<LogicalScalar.Zero||StrengthPerArea<LogicalScalar.Zero){ throw new InvalidDataException(); }
            var FragmentCount=ReadCount(Reader,2,MaximumFragments);
            var Levels=FileVersion>=3?ReadCount(Reader,2,PrefractureCookSettings.MaximumClusterLevels):FileVersion==2?1:0;
            var SubClusters=new int[Math.Max(0,Levels-1)][];
            for(var Level=0;Level<SubClusters.Length;Level++){ SubClusters[Level]=new int[FragmentCount]; }
            var Fragments=new FractureFragment[FragmentCount];
            var Meshes=new FractureMesh[FragmentCount];
            var Shapes=new PhysicsShapeAsset[FragmentCount];
            var Sites=new LogicalVector3[FileVersion>=2?FragmentCount:0];
            var Clusters=new int[FileVersion>=2?FragmentCount:0];
            for(var FragmentIndex=0;FragmentIndex<FragmentCount;FragmentIndex++){
                if(Reader.ReadInt32()!=FragmentIndex){ throw new InvalidDataException(); }
                var ShapeId=ReadString(Reader);
                var Center=ReadVector(Reader);
                var Mass=ReadScalar(Reader);
                if(Mass<=LogicalScalar.Zero){ throw new InvalidDataException(); }
                if(FileVersion>=2){
                    Sites[FragmentIndex]=ReadVector(Reader);
                    Clusters[FragmentIndex]=Reader.ReadInt32();
                    foreach(var Level in SubClusters){ Level[FragmentIndex]=Reader.ReadInt32(); }
                }
                var VertexCount=ReadCount(Reader,4,MaximumVerticesPerFragment);
                var Vertices=new LogicalVector3[VertexCount];
                for(var VertexIndex=0;VertexIndex<VertexCount;VertexIndex++){ Vertices[VertexIndex]=ReadVector(Reader); }
                var IndexCount=ReadCount(Reader,12,MaximumIndicesPerFragment);
                if(IndexCount%3!=0){ throw new InvalidDataException(); }
                var Indices=new int[IndexCount];
                for(var MeshIndex=0;MeshIndex<IndexCount;MeshIndex++){ Indices[MeshIndex]=Reader.ReadInt32(); }
                byte[]?Slots=null;
                if(FileVersion>=2){
                    Slots=Reader.ReadBytes(IndexCount/3);
                    if(Slots.Length!=IndexCount/3){ throw new EndOfStreamException(); }
                }
                var Mesh=new FractureMesh(Vertices,Indices,Slots);
                if(!Mesh.IsClosedManifold()){ throw new InvalidDataException(); }
                Fragments[FragmentIndex]=new FractureFragment(FragmentIndex,ShapeId,Center,Mass);
                Meshes[FragmentIndex]=Mesh;
                Shapes[FragmentIndex]=PhysicsShapeAsset.CreateConvexPolyhedron(ShapeId,Vertices,Indices);
            }
            var MaximumBonds=checked(FragmentCount*(FragmentCount-1)/2);
            var BondCount=ReadCount(Reader,0,MaximumBonds);
            var Bonds=new FractureBond[BondCount];
            for(var BondIndex=0;BondIndex<BondCount;BondIndex++){
                var First=Reader.ReadInt32();
                var Second=Reader.ReadInt32();
                var Area=FileVersion>=2?ReadScalar(Reader):LogicalScalar.Zero;
                Bonds[BondIndex]=new FractureBond(First,Second,ReadScalar(Reader),Area);
            }
            PrefractureAsset Candidate=new(){
                SourceHash=SourceHash,Seed=Seed,Fragments=Fragments,Bonds=Bonds,
                Meshes=Meshes,CollisionShapes=Shapes,Sites=Sites,Clusters=Clusters,SubClusters=SubClusters,
                Density=Density,BondStrengthPerArea=StrengthPerArea,
            };
            if(!Candidate.IsCanonicalArtifact()||Input.ReadByte()!=-1){ throw new InvalidDataException(); }
            Asset=Candidate;
            return true;
        }
        catch(Exception Exception)when(Exception is EndOfStreamException or IOException or InvalidDataException
            or DecoderFallbackException or ArgumentException or OverflowException){
            Asset=null;
            Error="Prefracture artifact is truncated, malformed, or non-canonical.";
            return false;
        }
    }

    /// <summary><paramref name="Bytes"/> を読み込む（<see cref="TryRead(Stream,out PrefractureAsset?,out string)"/> を参照）。</summary>
    public static bool TryRead(byte[]Bytes,out PrefractureAsset?Asset,out string Error){
        ArgumentNullException.ThrowIfNull(Bytes);
        using MemoryStream Stream=new(Bytes,false);
        return TryRead(Stream,out Asset,out Error);
    }

    private static int ReadCount(BinaryReader Reader,int Minimum,int Maximum){
        int Count=Reader.ReadInt32();
        return Count>=Minimum&&Count<=Maximum?Count:throw new InvalidDataException();
    }

    private static string ReadString(BinaryReader Reader){
        int Length=Reader.Read7BitEncodedInt();
        if(Length is <1 or >MaximumStringBytes){ throw new InvalidDataException(); }
        byte[]Bytes=Reader.ReadBytes(Length);
        if(Bytes.Length!=Length){ throw new EndOfStreamException(); }
        return StrictUtf8.GetString(Bytes);
    }

    private static void WriteVector(BinaryWriter Writer,in LogicalVector3 Value){
        WriteScalar(Writer,Value.R);
        WriteScalar(Writer,Value.U);
        WriteScalar(Writer,Value.F);
    }

    private static LogicalVector3 ReadVector(BinaryReader Reader)=>
        new(ReadScalar(Reader),ReadScalar(Reader),ReadScalar(Reader));

    private static void WriteScalar(BinaryWriter Writer,LogicalScalar Value){
        Span<byte>Bytes=stackalloc byte[LogicalScalar.SerializedSize];
        Value.WriteLittleEndian(Bytes);
        Writer.Write(Bytes);
    }

    private static LogicalScalar ReadScalar(BinaryReader Reader){
        Span<byte>Bytes=stackalloc byte[LogicalScalar.SerializedSize];
        Reader.ReadExactly(Bytes);
        return LogicalScalar.TryReadLittleEndian(Bytes,out LogicalScalar Value)
            ?Value:throw new InvalidDataException();
    }
}

public static class PrefractureArtifactCooker {
    public const string ArtifactFileName="fracture.cgefracture";
    public const string ManifestFileName="manifest.json";

    public static PhysicsCookResult Cook(
        string RepositoryRoot,in PhysicsCookingInput Input,PrefractureAsset Asset){
        ArgumentException.ThrowIfNullOrWhiteSpace(RepositoryRoot);
        ArgumentNullException.ThrowIfNull(Asset);
        if(!Asset.IsCanonicalArtifact()){
            throw new ArgumentException("Prefracture asset is not canonical.",nameof(Asset));
        }
        string Key=PhysicsCookingKey.Compute(Input);
        string FinalDirectory=Path.GetFullPath(
            PhysicsCookingKey.GetArtifactDirectory(RepositoryRoot,Input.AssetGuid,Key));
        string ArtifactPath=Path.Combine(FinalDirectory,ArtifactFileName);
        string ManifestPath=Path.Combine(FinalDirectory,ManifestFileName);
        if(Directory.Exists(FinalDirectory)){
            return ValidateExisting(FinalDirectory,Input.AssetGuid,Key,Asset)
                ?new PhysicsCookResult(true,true,Key,ArtifactPath,ManifestPath,string.Empty)
                :new PhysicsCookResult(false,false,Key,ArtifactPath,ManifestPath,
                    "The existing prefracture artifact does not match its immutable cooking identity.");
        }
        string Parent=Path.GetDirectoryName(FinalDirectory)
            ??throw new InvalidOperationException("Prefracture artifact directory has no parent.");
        Directory.CreateDirectory(Parent);
        string Temporary=Path.Combine(Parent,$".{Key}.tmp-{Guid.NewGuid():N}");
        try {
            Directory.CreateDirectory(Temporary);
            string TemporaryArtifact=Path.Combine(Temporary,ArtifactFileName);
            using(FileStream Stream=new(
                TemporaryArtifact,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)){
                PrefractureArtifact.Write(Stream,Asset);
                Stream.Flush(true);
            }
            string ArtifactHash=ComputeFileHash(TemporaryArtifact);
            WriteManifest(
                Path.Combine(Temporary,ManifestFileName),Input.AssetGuid,Key,ArtifactHash,Asset);
            if(!ValidateExisting(Temporary,Input.AssetGuid,Key,Asset)){
                return new PhysicsCookResult(false,false,Key,ArtifactPath,ManifestPath,
                    "The staged prefracture artifact failed canonical validation.");
            }
            try { Directory.Move(Temporary,FinalDirectory); }
            catch(IOException)when(Directory.Exists(FinalDirectory)){
                return ValidateExisting(FinalDirectory,Input.AssetGuid,Key,Asset)
                    ?new PhysicsCookResult(true,true,Key,ArtifactPath,ManifestPath,string.Empty)
                    :new PhysicsCookResult(false,false,Key,ArtifactPath,ManifestPath,
                        "A concurrent prefracture cook published different content for the same immutable key.");
            }
            return new PhysicsCookResult(true,false,Key,ArtifactPath,ManifestPath,string.Empty);
        }
        catch(Exception Exception)when(Exception is IOException or UnauthorizedAccessException or InvalidDataException){
            return new PhysicsCookResult(false,false,Key,ArtifactPath,ManifestPath,Exception.Message);
        }
        finally {
            if(Directory.Exists(Temporary)&&IsDirectChild(Parent,Temporary)){ Directory.Delete(Temporary,true); }
        }
    }

    public static bool ValidateExisting(
        string DirectoryPath,string AssetGuid,string Key,PrefractureAsset Expected){
        try {
            string ArtifactPath=Path.Combine(DirectoryPath,ArtifactFileName);
            string ManifestPath=Path.Combine(DirectoryPath,ManifestFileName);
            string[]Entries=Directory.GetFileSystemEntries(DirectoryPath);
            if(Entries.Length!=2||!File.Exists(ArtifactPath)||!File.Exists(ManifestPath)
                ||new FileInfo(ManifestPath).Length>65_536){ return false; }
            using FileStream ArtifactStream=File.OpenRead(ArtifactPath);
            if(!PrefractureArtifact.TryRead(ArtifactStream,out PrefractureAsset? Actual,out _)
                ||Actual is null||Actual.SourceHash!=Expected.SourceHash||Actual.Seed!=Expected.Seed
                ||Actual.Fragments.Count!=Expected.Fragments.Count||Actual.Bonds.Count!=Expected.Bonds.Count
                ||Actual.ClusterLevelCount!=Expected.ClusterLevelCount){
                return false;
            }
            for(int Index=0;Index<Actual.CollisionShapes.Count;Index++){
                if(Actual.Fragments[Index]!=Expected.Fragments[Index]
                    ||Actual.CollisionShapes[Index].Fingerprint!=Expected.CollisionShapes[Index].Fingerprint){ return false; }
            }
            if(!Actual.Bonds.SequenceEqual(Expected.Bonds)){ return false; }
            using JsonDocument Document=JsonDocument.Parse(
                File.ReadAllBytes(ManifestPath),
                new JsonDocumentOptions { AllowTrailingCommas=false,CommentHandling=JsonCommentHandling.Disallow,MaxDepth=16 });
            JsonElement Root=Document.RootElement;
            if(Root.ValueKind!=JsonValueKind.Object||Root.EnumerateObject().Count()!=9){ return false; }
            return ReadString(Root,"assetGuid")==AssetGuid
                &&ReadString(Root,"key")==Key
                &&ReadString(Root,"sourceHash")==Expected.SourceHash
                &&ReadInt32(Root,"seed")==Expected.Seed
                &&ReadInt32(Root,"fragmentCount")==Expected.Fragments.Count
                &&ReadInt32(Root,"bondCount")==Expected.Bonds.Count
                &&ReadString(Root,"artifactHash")==ComputeFileHash(ArtifactPath)
                &&ReadString(Root,"schema")==$"prefracture-artifact-{PrefractureArtifact.VersionOf(Expected)}"
                &&ReadString(Root,"scalar")=="LogicalScalar-Q64.64";
        }
        catch(Exception Exception)when(Exception is IOException or UnauthorizedAccessException or InvalidDataException
            or JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException){
            return false;
        }
    }

    private static void WriteManifest(
        string PathValue,string AssetGuid,string Key,string ArtifactHash,PrefractureAsset Asset){
        using FileStream Stream=new(
            PathValue,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough);
        using(Utf8JsonWriter Writer=new(Stream,new JsonWriterOptions { Indented=false })){
            Writer.WriteStartObject();
            Writer.WriteString("assetGuid",AssetGuid);
            Writer.WriteString("key",Key);
            Writer.WriteString("sourceHash",Asset.SourceHash);
            Writer.WriteNumber("seed",Asset.Seed);
            Writer.WriteNumber("fragmentCount",Asset.Fragments.Count);
            Writer.WriteNumber("bondCount",Asset.Bonds.Count);
            Writer.WriteString("artifactHash",ArtifactHash);
            Writer.WriteString("schema",$"prefracture-artifact-{PrefractureArtifact.VersionOf(Asset)}");
            Writer.WriteString("scalar","LogicalScalar-Q64.64");
            Writer.WriteEndObject();
        }
        Stream.Flush(true);
    }

    private static string ComputeFileHash(string PathValue){
        using FileStream Stream=File.OpenRead(PathValue);
        return Convert.ToHexString(SHA256.HashData(Stream)).ToLowerInvariant();
    }

    private static string ReadString(JsonElement Root,string Name)=>
        Root.TryGetProperty(Name,out JsonElement Value)&&Value.ValueKind==JsonValueKind.String
            ?Value.GetString()??throw new InvalidDataException()
            :throw new InvalidDataException();

    private static int ReadInt32(JsonElement Root,string Name)=>
        Root.TryGetProperty(Name,out JsonElement Value)&&Value.TryGetInt32(out int Result)
            ?Result:throw new InvalidDataException();

    private static bool IsDirectChild(string Parent,string Child)=>
        StringComparer.OrdinalIgnoreCase.Equals(
            Path.GetFullPath(Parent).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetDirectoryName(Path.GetFullPath(Child))?.TrimEnd(Path.DirectorySeparatorChar));
}

public static class PrefractureCooker {
    private static readonly LogicalScalar GeometryTolerance=LogicalScalar.FromSingle(0.00001f);
    private static readonly LogicalScalar GeometryToleranceSquared=GeometryTolerance*GeometryTolerance;
    private static readonly LogicalScalar Two=LogicalScalar.FromInt64(2);
    private static readonly LogicalScalar Three=LogicalScalar.FromInt64(3);
    private static readonly LogicalScalar Four=LogicalScalar.FromInt64(4);
    private static readonly LogicalScalar Six=LogicalScalar.FromInt64(6);
    /// <summary>共有面積がこれより小さい（1 mm²）場合、ボンドではなく角での接触とみなす。</summary>
    private static readonly LogicalScalar MinimumBondArea=LogicalScalar.FromSingle(0.000001f);

    private readonly record struct ClipPlane(LogicalVector3 Normal,LogicalScalar Distance,int Neighbor);
    private sealed class PolygonFace(List<LogicalVector3>Vertices,int Neighbor) {
        internal List<LogicalVector3>Vertices{get;}=Vertices;
        internal int Neighbor{get;}=Neighbor;
        internal PolygonFace Clone()=>new([..this.Vertices],this.Neighbor);
    }
    private sealed record Cell(
        FractureFragment Fragment,FractureMesh Mesh,PhysicsShapeAsset Collision,
        Dictionary<int,LogicalScalar>NeighborAreas);

    public static bool TryGenerate(
        PhysicsShapeAsset Source,in PrefractureCookSettings Settings,
        out PrefractureAsset?Asset,out string Error){
        ArgumentNullException.ThrowIfNull(Source);
        Asset=null;
        Error=string.Empty;
        if(Source.Kind!=PhysicsShapeAssetKind.StaticTriangleMesh){
            Error="FRACTURE001: Prefracture input must be a closed convex static triangle mesh.";
            return false;
        }
        if(Settings.FragmentCount is <2 or >256||Settings.Density<=LogicalScalar.Zero
            ||Settings.BondStrengthPerArea<=LogicalScalar.Zero||Settings.ClusterCount<0
            ||Settings.ClusterLevels is <1 or >PrefractureCookSettings.MaximumClusterLevels){
            Error="FRACTURE002: Prefracture settings require 2-256 fragments, positive density and bond strength, a cluster count of 0 or more and 1 to 4 cluster levels.";
            return false;
        }
        try {
            if(!TryBuildSourcePolyhedron(Source,out List<PolygonFace>?SourceFaces,out List<ClipPlane>?SourcePlanes,out Error)){
                return false;
            }
            if(!TryGenerateSeeds(Source.Bounds,SourcePlanes,Settings.FragmentCount,Settings.Seed,Source.Fingerprint,out LogicalVector3[]Seeds)){
                Error="FRACTURE005: The requested number of distinct Voronoi seeds could not be placed inside the source volume.";
                return false;
            }
            List<Cell>Cells=new(Settings.FragmentCount);
            for(int FragmentIndex=0;FragmentIndex<Settings.FragmentCount;FragmentIndex++){
                List<PolygonFace>Faces=SourceFaces.Select(static Face=>Face.Clone()).ToList();
                for(int OtherIndex=0;OtherIndex<Seeds.Length;OtherIndex++){
                    if(OtherIndex==FragmentIndex){ continue; }
                    LogicalVector3 Delta=Seeds[OtherIndex]-Seeds[FragmentIndex];
                    if(!Delta.TryNormalize(out LogicalVector3 Normal)){
                        Error="FRACTURE005: Prefracture seeds are not distinct.";
                        return false;
                    }
                    if(!LogicalScalar.TrySquareRoot(Delta.LengthSquared,out LogicalScalar DeltaLength)
                        ||DeltaLength.IsZero){
                        Error="FRACTURE005: Prefracture seeds are not distinct.";
                        return false;
                    }
                    LogicalScalar Distance=(Seeds[OtherIndex].LengthSquared-Seeds[FragmentIndex].LengthSquared)
                        /(Two*DeltaLength);
                    if(!Clip(Faces,new ClipPlane(Normal,Distance,OtherIndex),out _)){
                        Error="FRACTURE006: A Voronoi fragment collapsed below the representable geometry tolerance.";
                        return false;
                    }
                }
                if(!TryBuildCell(Source.Id,FragmentIndex,Faces,Settings.Density,out Cell CellValue,out Error)){
                    return false;
                }
                Cells.Add(CellValue);
            }
            // 2つのセルで共有する面を、それぞれの最終ポリゴン上で測る。両側で同じ1つの面を測るため、面積は一致する（ボンドは対称）。
            List<FractureBond>Bonds=[];
            for(int First=0;First<Cells.Count;First++){
                for(int Second=First+1;Second<Cells.Count;Second++){
                    var HasFirst=Cells[First].NeighborAreas.TryGetValue(Second,out LogicalScalar FirstArea);
                    var HasSecond=Cells[Second].NeighborAreas.TryGetValue(First,out LogicalScalar SecondArea);
                    var Larger=FirstArea>SecondArea?FirstArea:SecondArea;
                    if(!HasFirst||!HasSecond){
                        if(Larger>MinimumBondArea){
                            Error=$"FRACTURE007: Fragments {First} and {Second} disagree on their shared face (one side has {(HasFirst?FirstArea:SecondArea).ToStringInvariant()} m², the other none).";
                            return false;
                        }
                        continue;
                    }
                    if(Abs(FirstArea-SecondArea)>Larger/LogicalScalar.FromInt64(1000)+GeometryToleranceSquared){
                        Error=$"FRACTURE007: Fragments {First} and {Second} disagree on their shared face ({FirstArea.ToStringInvariant()} m² and {SecondArea.ToStringInvariant()} m²).";
                        return false;
                    }
                    var Area=FirstArea<SecondArea?FirstArea:SecondArea;
                    if(Area<=MinimumBondArea){ continue; }
                    Bonds.Add(new FractureBond(First,Second,Area*Settings.BondStrengthPerArea,Area));
                }
            }
            var Levels=BuildClusterLevels(Cells.Count,Bonds,Settings.ClusterCount,Settings.ClusterLevels);
            Asset=new PrefractureAsset {
                SourceHash=Source.Fingerprint,
                Seed=Settings.Seed,
                Fragments=Cells.Select(static CellValue=>CellValue.Fragment).ToArray(),
                Bonds=Bonds,
                Meshes=Cells.Select(static CellValue=>CellValue.Mesh).ToArray(),
                CollisionShapes=Cells.Select(static CellValue=>CellValue.Collision).ToArray(),
                Sites=Seeds,
                Clusters=Levels[0],
                SubClusters=Levels[1..],
                Density=Settings.Density,
                BondStrengthPerArea=Settings.BondStrengthPerArea,
            };
            if(!Asset.IsDeterministicallyOrdered()||!Asset.IsCanonicalArtifact()){
                Asset=null;
                Error="FRACTURE007: Generated prefracture data violates its canonical invariants (ordering, closed fragments, one connected bond graph, connected clusters).";
                return false;
            }
            return true;
        }
        catch(Exception Exception)when(Exception is OverflowException or ArgumentException or InvalidOperationException){
            Asset=null;
            Error=Exception.Message;
            return false;
        }
    }

    private static bool TryBuildSourcePolyhedron(
        PhysicsShapeAsset Source,out List<PolygonFace>Faces,out List<ClipPlane>Planes,out string Error){
        Faces=CreateBoundsFaces(Source.Bounds);
        Planes=[];
        Error=string.Empty;
        if(!IsClosedSourceMesh(Source)){
            Error="FRACTURE001: Prefracture input must be a closed two-manifold mesh.";
            return false;
        }
        LogicalVector3 Center=LogicalVector3.Zero;
        foreach(LogicalVector3 Vertex in Source.Vertices){ Center+=Vertex; }
        Center/=LogicalScalar.FromInt64(Source.Vertices.Count);
        for(int Index=0;Index<Source.Indices.Count;Index+=3){
            LogicalVector3 A=Source.Vertices[Source.Indices[Index]];
            LogicalVector3 B=Source.Vertices[Source.Indices[Index+1]];
            LogicalVector3 C=Source.Vertices[Source.Indices[Index+2]];
            if(!LogicalVector3.Cross(B-A,C-A).TryNormalize(out LogicalVector3 Normal)){
                Error="FRACTURE004: Prefracture source contains a degenerate triangle.";
                return false;
            }
            LogicalScalar Distance=LogicalVector3.Dot(Normal,A);
            if(LogicalVector3.Dot(Normal,Center)>Distance){
                Normal=-Normal;
                Distance=-Distance;
            }
            bool Duplicate=Planes.Any(Plane=>
                LogicalVector3.Dot(Plane.Normal,Normal)>=LogicalScalar.One-GeometryTolerance
                &&Abs(Plane.Distance-Distance)<=GeometryTolerance);
            if(!Duplicate){ Planes.Add(new ClipPlane(Normal,Distance,-1)); }
        }
        foreach(ClipPlane Plane in Planes){
            foreach(LogicalVector3 Vertex in Source.Vertices){
                if(LogicalVector3.Dot(Plane.Normal,Vertex)>Plane.Distance+GeometryTolerance){
                    Error="FRACTURE003: Prefracture currently requires a convex source mesh.";
                    return false;
                }
            }
            if(!Clip(Faces,Plane,out _)){
                Error="FRACTURE004: Source mesh planes do not enclose a representable volume.";
                return false;
            }
        }
        if(Faces.Count<4){
            Error="FRACTURE004: Source mesh planes do not enclose a representable volume.";
            return false;
        }
        return true;
    }

    private static bool IsClosedSourceMesh(PhysicsShapeAsset Source){
        Dictionary<(LogicalVector3 First,LogicalVector3 Second),int>Edges=[];
        for(int Index=0;Index<Source.Indices.Count;Index+=3){
            LogicalVector3 A=Source.Vertices[Source.Indices[Index]];
            LogicalVector3 B=Source.Vertices[Source.Indices[Index+1]];
            LogicalVector3 C=Source.Vertices[Source.Indices[Index+2]];
            AddGeometricEdge(Edges,A,B);
            AddGeometricEdge(Edges,B,C);
            AddGeometricEdge(Edges,C,A);
        }
        return Edges.Count>0&&Edges.Values.All(static Count=>Count==2);
    }

    private static void AddGeometricEdge(
        Dictionary<(LogicalVector3 First,LogicalVector3 Second),int>Edges,
        LogicalVector3 First,LogicalVector3 Second){
        (LogicalVector3 First,LogicalVector3 Second)Key=Compare(First,Second)<=0?(First,Second):(Second,First);
        Edges.TryGetValue(Key,out int Count);
        Edges[Key]=Count+1;
    }

    private static List<PolygonFace>CreateBoundsFaces(in PhysicsAabb Bounds){
        LogicalVector3 Min=Bounds.Min;
        LogicalVector3 Max=Bounds.Max;
        return [
            new([new(Max.R,Min.U,Min.F),new(Max.R,Max.U,Min.F),new(Max.R,Max.U,Max.F),new(Max.R,Min.U,Max.F)],-1),
            new([new(Min.R,Min.U,Min.F),new(Min.R,Min.U,Max.F),new(Min.R,Max.U,Max.F),new(Min.R,Max.U,Min.F)],-1),
            new([new(Min.R,Max.U,Min.F),new(Min.R,Max.U,Max.F),new(Max.R,Max.U,Max.F),new(Max.R,Max.U,Min.F)],-1),
            new([new(Min.R,Min.U,Min.F),new(Max.R,Min.U,Min.F),new(Max.R,Min.U,Max.F),new(Min.R,Min.U,Max.F)],-1),
            new([new(Min.R,Min.U,Max.F),new(Max.R,Min.U,Max.F),new(Max.R,Max.U,Max.F),new(Min.R,Max.U,Max.F)],-1),
            new([new(Min.R,Min.U,Min.F),new(Min.R,Max.U,Min.F),new(Max.R,Max.U,Min.F),new(Max.R,Min.U,Min.F)],-1),
        ];
    }

    private static bool Clip(List<PolygonFace>Faces,in ClipPlane Plane,out LogicalScalar CapArea){
        List<PolygonFace>Clipped=[];
        List<LogicalVector3>Intersections=[];
        bool AnyOutside=false;
        foreach(PolygonFace Face in Faces){
            List<LogicalVector3>Output=[];
            LogicalVector3 Previous=SnapToPlane(Face.Vertices[^1],Plane,out LogicalScalar PreviousDistance);
            bool PreviousInside=PreviousDistance<=LogicalScalar.Zero;
            AnyOutside|=!PreviousInside;
            foreach(LogicalVector3 UnsnappedCurrent in Face.Vertices){
                LogicalVector3 Current=SnapToPlane(UnsnappedCurrent,Plane,out LogicalScalar CurrentDistance);
                bool CurrentInside=CurrentDistance<=LogicalScalar.Zero;
                AnyOutside|=!CurrentInside;
                if(CurrentInside!=PreviousInside){
                    LogicalScalar Denominator=PreviousDistance-CurrentDistance;
                    if(!Denominator.IsZero){
                        LogicalVector3 Intersection=Previous+(Current-Previous)*(PreviousDistance/Denominator);
                        Intersection=SnapToPlane(Intersection,Plane,out _);
                        AddUnique(Intersections,Intersection);
                        Output.Add(Intersection);
                    }
                }
                if(CurrentInside){
                    Output.Add(Current);
                    if(CurrentDistance.IsZero){ AddUnique(Intersections,Current); }
                }
                Previous=Current;
                PreviousDistance=CurrentDistance;
                PreviousInside=CurrentInside;
            }
            CleanPolygon(Output);
            if(Output.Count>=3){ Clipped.Add(new PolygonFace(Output,Face.Neighbor)); }
        }
        CapArea=LogicalScalar.Zero;
        if(Clipped.Count==0){
            Faces.Clear();
            return false;
        }
        CleanPolygon(Intersections);
        if(AnyOutside&&Intersections.Count>=3){
            SortCap(Intersections,Plane.Normal);
            CleanPolygon(Intersections);
            if(Intersections.Count>=3){
                CapArea=CalculateArea(Intersections,Plane.Normal);
                Clipped.Add(new PolygonFace(Intersections,Plane.Neighbor));
            }
        }
        Faces.Clear();
        Faces.AddRange(Clipped);
        return Faces.Count>=4;
    }

    private static LogicalVector3 SnapToPlane(
        LogicalVector3 Value,in ClipPlane Plane,out LogicalScalar SignedDistance){
        SignedDistance=LogicalVector3.Dot(Plane.Normal,Value)-Plane.Distance;
        if(Abs(SignedDistance)>GeometryTolerance){ return Value; }
        LogicalScalar NormalLengthSquared=Plane.Normal.LengthSquared;
        if(NormalLengthSquared.IsZero){ return Value; }
        Value-=Plane.Normal*(SignedDistance/NormalLengthSquared);
        SignedDistance=LogicalScalar.Zero;
        return Value;
    }

    private static void AddUnique(List<LogicalVector3>Values,LogicalVector3 Candidate){
        foreach(LogicalVector3 Existing in Values){
            if((Existing-Candidate).LengthSquared<=GeometryToleranceSquared){ return; }
        }
        Values.Add(Candidate);
    }

    private static void CleanPolygon(List<LogicalVector3>Vertices){
        for(int Index=Vertices.Count-1;Index>=0&&Vertices.Count>=2;Index--){
            int Previous=(Index+Vertices.Count-1)%Vertices.Count;
            if((Vertices[Previous]-Vertices[Index]).LengthSquared<=GeometryToleranceSquared){
                Vertices.RemoveAt(Index);
            }
        }
        bool Removed;
        do {
            Removed=false;
            for(int Index=0;Index<Vertices.Count&&Vertices.Count>=3;Index++){
                LogicalVector3 Previous=Vertices[(Index+Vertices.Count-1)%Vertices.Count];
                LogicalVector3 Current=Vertices[Index];
                LogicalVector3 Next=Vertices[(Index+1)%Vertices.Count];
                if(LogicalVector3.Cross(Current-Previous,Next-Current).LengthSquared.IsZero){
                    Vertices.RemoveAt(Index);
                    Removed=true;
                    break;
                }
            }
        }while(Removed);
    }

    private static void SortCap(List<LogicalVector3>Vertices,LogicalVector3 Normal){
        LogicalVector3 Center=LogicalVector3.Zero;
        foreach(LogicalVector3 Vertex in Vertices){ Center+=Vertex; }
        Center/=LogicalScalar.FromInt64(Vertices.Count);
        if(!(Vertices[0]-Center).TryNormalize(out LogicalVector3 AxisR)){ return; }
        LogicalVector3 AxisU=LogicalVector3.Cross(Normal,AxisR);
        for(int Index=1;Index<Vertices.Count;Index++){
            LogicalVector3 Value=Vertices[Index];
            int Destination=Index;
            while(Destination>0&&CompareAround(Value,Vertices[Destination-1],Center,AxisR,AxisU)<0){
                Vertices[Destination]=Vertices[Destination-1];
                Destination--;
            }
            Vertices[Destination]=Value;
        }
    }

    private static int CompareAround(
        LogicalVector3 Left,LogicalVector3 Right,LogicalVector3 Center,
        LogicalVector3 AxisR,LogicalVector3 AxisU){
        LogicalVector3 LeftDelta=Left-Center;
        LogicalVector3 RightDelta=Right-Center;
        LogicalScalar LeftR=LogicalVector3.Dot(LeftDelta,AxisR);
        LogicalScalar LeftU=LogicalVector3.Dot(LeftDelta,AxisU);
        LogicalScalar RightR=LogicalVector3.Dot(RightDelta,AxisR);
        LogicalScalar RightU=LogicalVector3.Dot(RightDelta,AxisU);
        bool LeftUpper=LeftU>LogicalScalar.Zero||LeftU.IsZero&&LeftR>=LogicalScalar.Zero;
        bool RightUpper=RightU>LogicalScalar.Zero||RightU.IsZero&&RightR>=LogicalScalar.Zero;
        if(LeftUpper!=RightUpper){ return LeftUpper?-1:1; }
        LogicalScalar Cross=LeftR*RightU-LeftU*RightR;
        if(!Cross.IsZero){ return Cross>LogicalScalar.Zero?-1:1; }
        return LeftDelta.LengthSquared.CompareTo(RightDelta.LengthSquared);
    }

    private static LogicalScalar CalculateArea(IReadOnlyList<LogicalVector3>Vertices,LogicalVector3 Normal){
        LogicalVector3 AreaVector=LogicalVector3.Zero;
        for(int Index=0;Index<Vertices.Count;Index++){
            AreaVector+=LogicalVector3.Cross(Vertices[Index],Vertices[(Index+1)%Vertices.Count]);
        }
        return Abs(LogicalVector3.Dot(AreaVector,Normal))/Two;
    }

    /// <summary>
    /// 1つのボロノイセルを破片として表す。切り取った面を扇状に三角形分割し、外向きの巻き順と正の体積を持つ閉じたメッシュにする。各三角形の材質スロット（隣のセルの面で切った面には
    /// <see cref="FractureMesh.InteriorSlot"/> を指定）、各隣接セルとの共有面積、物理衝突に使う凸多面体を保持する。
    /// </summary>
    private static bool TryBuildCell(
        string SourceId,int FragmentIndex,List<PolygonFace>Faces,LogicalScalar Density,
        out Cell CellValue,out string Error){
        CellValue=null!;
        Error=string.Empty;
        List<LogicalVector3>WorldVertices=[];
        List<int>Indices=[];
        List<byte>Slots=[];
        Dictionary<int,LogicalScalar>NeighborAreas=[];
        foreach(PolygonFace Face in Faces){
            if(Face.Vertices.Count<3){ continue; }
            if(Face.Neighbor>=0){
                var FaceArea=PolygonArea(Face.Vertices);
                NeighborAreas.TryGetValue(Face.Neighbor,out var Existing);
                NeighborAreas[Face.Neighbor]=Existing+FaceArea;
            }
            var Slot=Face.Neighbor>=0?FractureMesh.InteriorSlot:FractureMesh.OuterSlot;
            int First=GetVertexIndex(WorldVertices,Face.Vertices[0]);
            for(int Index=1;Index<Face.Vertices.Count-1;Index++){
                int Second=GetVertexIndex(WorldVertices,Face.Vertices[Index]);
                int Third=GetVertexIndex(WorldVertices,Face.Vertices[Index+1]);
                if(First==Second||Second==Third||Third==First){ continue; }
                Indices.Add(First);
                Indices.Add(Second);
                Indices.Add(Third);
                Slots.Add(Slot);
            }
        }
        if(WorldVertices.Count<4||Indices.Count<12){
            Error="FRACTURE006: A generated Voronoi fragment has no closed volume.";
            return false;
        }
        LogicalScalar SignedSixVolume=LogicalScalar.Zero;
        LogicalVector3 WeightedCenter=LogicalVector3.Zero;
        for(int Index=0;Index<Indices.Count;Index+=3){
            LogicalVector3 A=WorldVertices[Indices[Index]];
            LogicalVector3 B=WorldVertices[Indices[Index+1]];
            LogicalVector3 C=WorldVertices[Indices[Index+2]];
            LogicalScalar SixVolume=LogicalVector3.Dot(A,LogicalVector3.Cross(B,C));
            SignedSixVolume+=SixVolume;
            WeightedCenter+=(A+B+C)*SixVolume;
        }
        if(Abs(SignedSixVolume)<=GeometryToleranceSquared){
            Error="FRACTURE006: A generated Voronoi fragment has zero representable volume.";
            return false;
        }
        if(SignedSixVolume<LogicalScalar.Zero){
            // 外向きの巻き順を正規形式とする。レンダラーと物理処理の両方が、この順序から法線を読み取る。
            for(int Index=0;Index<Indices.Count;Index+=3){ (Indices[Index+1],Indices[Index+2])=(Indices[Index+2],Indices[Index+1]); }
        }
        LogicalVector3 Center=WeightedCenter/(Four*SignedSixVolume);
        LogicalScalar Mass=Abs(SignedSixVolume)/Six*Density;
        LogicalVector3[]LocalVertices=WorldVertices.Select(Vertex=>Vertex-Center).ToArray();
        FractureMesh Mesh=new(LocalVertices,Indices,Slots);
        if(!Mesh.TryGetTopologyError(out string TopologyError)){
            Error=$"FRACTURE006: Voronoi fragment {FragmentIndex} is not a closed two-manifold mesh "
                +$"({WorldVertices.Count} vertices, {Indices.Count/3} triangles, {Faces.Count} faces): {TopologyError}.";
            return false;
        }
        string ShapeId=$"{SourceId}.fragment.{FragmentIndex:D4}";
        PhysicsShapeAsset Collision=PhysicsShapeAsset.CreateConvexPolyhedron(ShapeId,LocalVertices,Indices);
        FractureFragment Fragment=new(FragmentIndex,ShapeId,Center,Mass);
        CellValue=new Cell(Fragment,Mesh,Collision,NeighborAreas);
        return true;
    }

    /// <summary>平面ポリゴンの面積（Newell ベクトルの長さの半分）。</summary>
    private static LogicalScalar PolygonArea(IReadOnlyList<LogicalVector3>Vertices){
        var AreaVector=LogicalVector3.Zero;
        for(var Index=0;Index<Vertices.Count;Index++){
            AreaVector+=LogicalVector3.Cross(Vertices[Index],Vertices[(Index+1)%Vertices.Count]);
        }
        return LogicalScalar.TrySquareRoot(AreaVector.LengthSquared,out var Length)?Length/Two:LogicalScalar.Zero;
    }

    /// <summary>
    /// クラスター階層（M0124）。レベル0では、要求された数に応じてすべての破片を <see cref="BuildClusters"/> でまとめる。各下位レベルでは、上位レベルの各クラスターを、その
    /// クラスター内の破片とボンドだけを使って、破片数の平方根に最も近い整数個のクラスターに分割する。ただし破片が2つ以下なら1クラスターとし、個々の破片の段までまとまりを保つ。各レベルのクラスター番号は、
    /// 最小破片番号の順に正規化する。そのため、下位階層のクラスターは上位階層のクラスター内に入れ子になる。
    /// </summary>
    private static int[][]BuildClusterLevels(int FragmentCount,IReadOnlyList<FractureBond>Bonds,int RequestedClusters,int LevelCount){
        var Levels=new int[LevelCount][];
        Levels[0]=BuildClusters([.. Enumerable.Range(0,FragmentCount)],Bonds,RequestedClusters);
        for(var Level=1;Level<LevelCount;Level++){
            var Above=Levels[Level-1];
            var Numbers=new int[FragmentCount];
            var Offset=0;
            var AboveCount=Above.Max()+1;
            for(var Cluster=0;Cluster<AboveCount;Cluster++){
                var Members=Enumerable.Range(0,FragmentCount).Where(Fragment=>Above[Fragment]==Cluster).ToArray();
                var Local=BuildClusters(Members,Bonds,0);
                for(var Index=0;Index<Members.Length;Index++){ Numbers[Members[Index]]=Offset+Local[Index]; }
                Offset+=Local.Max()+1;
            }
            Levels[Level]=Canonical(Numbers);
        }
        return Levels;
    }

    /// <summary>最小破片番号の順に、クラスター番号を0から振り直す。</summary>
    private static int[]Canonical(int[]Numbers){
        var Renumbered=new Dictionary<int,int>();
        var Result=new int[Numbers.Length];
        for(var Index=0;Index<Numbers.Length;Index++){
            if(!Renumbered.TryGetValue(Numbers[Index],out var Number)){
                Number=Renumbered.Count;
                Renumbered.Add(Numbers[Index],Number);
            }
            Result[Index]=Number;
        }
        return Result;
    }

    /// <summary>
    /// <paramref name="Members"/> のボンドグラフ上に作る1階層分のクラスター（破片インデックス昇順。UE の cluster hierarchy、M0113）。結果は各メンバーにつ
    /// いて返す。目標サイズは ⌈メンバー数 / クラスター数⌉（クラスター数が0ならメンバー数の平方根に最も近い整数）。未割り当ての最小メンバーから幅優先で、他のメンバーへのボンドを昇順にたどってクラスター
    /// を拡張する。そのため各クラスターは常に連結し、結果はボンドグラフだけで決まる。クラスター番号は先頭メンバーの順に付ける。全破片を対象にした場合、M0113 が調理した1階層と番号も含めて一致する。
    /// </summary>
    private static int[]BuildClusters(int[]Members,IReadOnlyList<FractureBond>Bonds,int RequestedClusters){
        var FragmentCount=Members.Length;
        var LocalOf=new Dictionary<int,int>();
        for(var Index=0;Index<Members.Length;Index++){ LocalOf.Add(Members[Index],Index); }
        List<(int First,int Second)>Links=[];
        foreach(var Bond in Bonds){
            if(LocalOf.TryGetValue(Bond.FirstFragment,out var First)&&LocalOf.TryGetValue(Bond.SecondFragment,out var Second)){ Links.Add((First,Second)); }
        }
        // √members に最も近い整数を整数演算で求める（k² ≤ n < (k+1)²。n が2つの平方数の中間になることはない）。
        var Root=1;
        while((Root+1)*(Root+1)<=FragmentCount){ Root++; }
        if(FragmentCount-Root*Root>(Root+1)*(Root+1)-FragmentCount){ Root++; }
        var Wanted=RequestedClusters>0?RequestedClusters:Root;
        Wanted=Math.Clamp(Wanted,1,FragmentCount);
        var TargetSize=(FragmentCount+Wanted-1)/Wanted;
        var Neighbors=new List<int>[FragmentCount];
        for(var Index=0;Index<FragmentCount;Index++){ Neighbors[Index]=[]; }
        foreach(var (First,Second) in Links){
            Neighbors[First].Add(Second);
            Neighbors[Second].Add(First);
        }
        foreach(var List in Neighbors){ List.Sort(); }
        var Clusters=new int[FragmentCount];
        Array.Fill(Clusters,-1);
        var Next=0;
        Queue<int>Frontier=new();
        for(var Start=0;Start<FragmentCount;Start++){
            if(Clusters[Start]>=0){ continue; }
            var Size=0;
            Frontier.Clear();
            Frontier.Enqueue(Start);
            Clusters[Start]=Next;
            Size++;
            while(Frontier.Count>0&&Size<TargetSize){
                var Current=Frontier.Dequeue();
                foreach(var Neighbor in Neighbors[Current]){
                    if(Size>=TargetSize){ break; }
                    if(Clusters[Neighbor]>=0){ continue; }
                    Clusters[Neighbor]=Next;
                    Size++;
                    Frontier.Enqueue(Neighbor);
                }
            }
            Next++;
        }
        // 隣接クラスターが先に割り当てられて目標サイズに届かなかったクラスターは、共有するボンドが最も多い隣接クラスターに加える。同数なら番号が小さい方を選ぶ。
        // 要求数よりクラスター数が多い場合も、各統合後に両方の連結性を保つ。その後、番号を再び正規化する。
        while(Next>Wanted){
            var Sizes=new int[Next];
            foreach(var Cluster in Clusters){ Sizes[Cluster]++; }
            var Smallest=-1;
            for(var Cluster=0;Cluster<Next;Cluster++){
                if(Sizes[Cluster]*2<TargetSize&&(Smallest<0||Sizes[Cluster]<Sizes[Smallest])){ Smallest=Cluster; }
            }
            if(Smallest<0){ break; }
            var Shared=new int[Next];
            foreach(var (FirstLocal,SecondLocal) in Links){
                var First=Clusters[FirstLocal];
                var Second=Clusters[SecondLocal];
                if(First==Smallest&&Second!=Smallest){ Shared[Second]++; }
                if(Second==Smallest&&First!=Smallest){ Shared[First]++; }
            }
            var Into=-1;
            for(var Cluster=0;Cluster<Next;Cluster++){
                if(Shared[Cluster]>0&&(Into<0||Shared[Cluster]>Shared[Into])){ Into=Cluster; }
            }
            if(Into<0){ break; }
            for(var Index=0;Index<FragmentCount;Index++){
                if(Clusters[Index]==Smallest){ Clusters[Index]=Into; }
            }
            Clusters=Canonical(Clusters);
            Next=Clusters.Max()+1;
        }
        return Clusters;
    }
    private static int GetVertexIndex(List<LogicalVector3>Vertices,LogicalVector3 Candidate){
        for(int Index=0;Index<Vertices.Count;Index++){
            if((Vertices[Index]-Candidate).LengthSquared<=GeometryToleranceSquared){ return Index; }
        }
        Vertices.Add(Candidate);
        return Vertices.Count-1;
    }

    private static bool TryGenerateSeeds(
        in PhysicsAabb Bounds,IReadOnlyList<ClipPlane>SourcePlanes,int Count,int Seed,string Fingerprint,
        out LogicalVector3[]Seeds){
        Seeds=new LogicalVector3[Count];
        ulong State=unchecked((ulong)(uint)Seed)^FingerprintSeed(Fingerprint)^0x9e3779b97f4a7c15UL;
        int Created=0;
        int Attempts=0;
        while(Created<Count&&Attempts<checked(Count*4096)){
            Attempts++;
            LogicalVector3 Fraction=new(NextFraction(ref State),NextFraction(ref State),NextFraction(ref State));
            LogicalVector3 Size=Bounds.Max-Bounds.Min;
            LogicalVector3 Candidate=Bounds.Min+new LogicalVector3(
                Size.R*Fraction.R,Size.U*Fraction.U,Size.F*Fraction.F);
            if(SourcePlanes.Any(Plane=>LogicalVector3.Dot(Plane.Normal,Candidate)>Plane.Distance-GeometryTolerance)){ continue; }
            bool Duplicate=false;
            for(int Index=0;Index<Created;Index++){
                if((Seeds[Index]-Candidate).LengthSquared<=GeometryToleranceSquared){ Duplicate=true;break; }
            }
            if(Duplicate){ continue; }
            Seeds[Created++]=Candidate;
        }
        return Created==Count;
    }

    private static LogicalScalar NextFraction(ref ulong State){
        State+=0x9e3779b97f4a7c15UL;
        ulong Value=State;
        Value=(Value^(Value>>30))*0xbf58476d1ce4e5b9UL;
        Value=(Value^(Value>>27))*0x94d049bb133111ebUL;
        Value^=Value>>31;
        uint Fraction=unchecked((uint)(Value>>32));
        return LogicalScalar.FromRaw(((Int128)Fraction<<32)+(Int128.One<<31));
    }

    private static ulong FingerprintSeed(string Fingerprint){
        if(Fingerprint.Length>=16&&ulong.TryParse(
            Fingerprint.AsSpan(0,16),System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture,out ulong Value)){
            return Value;
        }
        return 0;
    }

    private static int Compare(LogicalVector3 Left,LogicalVector3 Right){
        int Result=Left.R.Raw.CompareTo(Right.R.Raw);
        if(Result!=0){ return Result; }
        Result=Left.U.Raw.CompareTo(Right.U.Raw);
        return Result!=0?Result:Left.F.Raw.CompareTo(Right.F.Raw);
    }

    private static LogicalScalar Abs(LogicalScalar Value)=>Value<LogicalScalar.Zero?-Value:Value;
}
