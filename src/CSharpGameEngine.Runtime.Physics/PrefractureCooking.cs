using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace CSharpGameEngine.Runtime.Physics;

public readonly record struct PrefractureCookSettings(
    int FragmentCount,int Seed,LogicalScalar Density,LogicalScalar BondStrengthPerArea);

public sealed class FractureMesh {
    private struct EdgeUse {
        internal int Count;
        internal int OrientationBalance;
        internal int FirstTriangle;
        internal int SecondTriangle;
    }

    private readonly LogicalVector3[]VertexStorage;
    private readonly int[]IndexStorage;

    internal FractureMesh(IReadOnlyList<LogicalVector3>Vertices,IReadOnlyList<int>Indices){
        ArgumentNullException.ThrowIfNull(Vertices);
        ArgumentNullException.ThrowIfNull(Indices);
        if(Vertices.Count<4||Indices.Count<12||Indices.Count%3!=0){
            throw new ArgumentException("A fracture mesh must contain a closed triangulated volume.");
        }
        this.VertexStorage=Vertices.ToArray();
        this.IndexStorage=Indices.ToArray();
        for(int Index=0;Index<this.IndexStorage.Length;Index++){
            if((uint)this.IndexStorage[Index]>=(uint)this.VertexStorage.Length){
                throw new ArgumentOutOfRangeException(nameof(Indices));
            }
        }
    }

    public IReadOnlyList<LogicalVector3>Vertices=>this.VertexStorage;
    public IReadOnlyList<int>Indices=>this.IndexStorage;

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

public static class PrefractureArtifact {
    private static ReadOnlySpan<byte>Magic=>"CGEFRAC1"u8;
    private static readonly UTF8Encoding StrictUtf8=new(false,true);
    private const int Version=1;
    private const int MaximumStringBytes=4096;
    private const int MaximumFragments=256;
    private const int MaximumVerticesPerFragment=65_536;
    private const int MaximumIndicesPerFragment=1_000_000;

    public static void Write(Stream Output,PrefractureAsset Asset){
        ArgumentNullException.ThrowIfNull(Output);
        ArgumentNullException.ThrowIfNull(Asset);
        if(string.IsNullOrWhiteSpace(Asset.SourceHash)||!Asset.IsCanonicalArtifact()
            ||Asset.Fragments.Count is <2 or >MaximumFragments){
            throw new ArgumentException("Prefracture asset is not canonical.",nameof(Asset));
        }
        using BinaryWriter Writer=new(Output,Encoding.UTF8,true);
        Writer.Write(Magic);
        Writer.Write(Version);
        Writer.Write(Asset.SourceHash);
        Writer.Write(Asset.Seed);
        Writer.Write(Asset.Fragments.Count);
        for(int Index=0;Index<Asset.Fragments.Count;Index++){
            FractureFragment Fragment=Asset.Fragments[Index];
            FractureMesh Mesh=Asset.Meshes[Index];
            Writer.Write(Fragment.Index);
            Writer.Write(Fragment.ShapeId);
            WriteVector(Writer,Fragment.LocalCenter);
            WriteScalar(Writer,Fragment.Mass);
            Writer.Write(Mesh.Vertices.Count);
            foreach(LogicalVector3 Vertex in Mesh.Vertices){ WriteVector(Writer,Vertex); }
            Writer.Write(Mesh.Indices.Count);
            foreach(int MeshIndex in Mesh.Indices){ Writer.Write(MeshIndex); }
        }
        Writer.Write(Asset.Bonds.Count);
        foreach(FractureBond Bond in Asset.Bonds){
            Writer.Write(Bond.FirstFragment);
            Writer.Write(Bond.SecondFragment);
            WriteScalar(Writer,Bond.Strength);
        }
    }

    public static bool TryRead(Stream Input,out PrefractureAsset?Asset,out string Error){
        ArgumentNullException.ThrowIfNull(Input);
        Asset=null;
        Error=string.Empty;
        try {
            Span<byte>MagicBytes=stackalloc byte[Magic.Length];
            Input.ReadExactly(MagicBytes);
            if(!MagicBytes.SequenceEqual(Magic)){ throw new InvalidDataException(); }
            using BinaryReader Reader=new(Input,Encoding.UTF8,true);
            if(Reader.ReadInt32()!=Version){ throw new InvalidDataException(); }
            string SourceHash=ReadString(Reader);
            if(string.IsNullOrWhiteSpace(SourceHash)){ throw new InvalidDataException(); }
            int Seed=Reader.ReadInt32();
            int FragmentCount=ReadCount(Reader,2,MaximumFragments);
            FractureFragment[]Fragments=new FractureFragment[FragmentCount];
            FractureMesh[]Meshes=new FractureMesh[FragmentCount];
            PhysicsShapeAsset[]Shapes=new PhysicsShapeAsset[FragmentCount];
            for(int FragmentIndex=0;FragmentIndex<FragmentCount;FragmentIndex++){
                int Index=Reader.ReadInt32();
                if(Index!=FragmentIndex){ throw new InvalidDataException(); }
                string ShapeId=ReadString(Reader);
                LogicalVector3 Center=ReadVector(Reader);
                LogicalScalar Mass=ReadScalar(Reader);
                if(Mass<=LogicalScalar.Zero){ throw new InvalidDataException(); }
                int VertexCount=ReadCount(Reader,4,MaximumVerticesPerFragment);
                LogicalVector3[]Vertices=new LogicalVector3[VertexCount];
                for(int VertexIndex=0;VertexIndex<VertexCount;VertexIndex++){ Vertices[VertexIndex]=ReadVector(Reader); }
                int IndexCount=ReadCount(Reader,12,MaximumIndicesPerFragment);
                if(IndexCount%3!=0){ throw new InvalidDataException(); }
                int[]Indices=new int[IndexCount];
                for(int MeshIndex=0;MeshIndex<IndexCount;MeshIndex++){ Indices[MeshIndex]=Reader.ReadInt32(); }
                FractureMesh Mesh=new(Vertices,Indices);
                if(!Mesh.IsClosedManifold()){ throw new InvalidDataException(); }
                PhysicsShapeAsset Shape=PhysicsShapeAsset.CreateConvex(ShapeId,Vertices);
                Fragments[FragmentIndex]=new FractureFragment(FragmentIndex,ShapeId,Center,Mass);
                Meshes[FragmentIndex]=Mesh;
                Shapes[FragmentIndex]=Shape;
            }
            int MaximumBonds=checked(FragmentCount*(FragmentCount-1)/2);
            int BondCount=ReadCount(Reader,0,MaximumBonds);
            FractureBond[]Bonds=new FractureBond[BondCount];
            for(int BondIndex=0;BondIndex<BondCount;BondIndex++){
                Bonds[BondIndex]=new FractureBond(
                    Reader.ReadInt32(),Reader.ReadInt32(),ReadScalar(Reader));
            }
            PrefractureAsset Candidate=new(){
                SourceHash=SourceHash,Seed=Seed,Fragments=Fragments,Bonds=Bonds,
                Meshes=Meshes,CollisionShapes=Shapes,
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
                ||Actual.Fragments.Count!=Expected.Fragments.Count||Actual.Bonds.Count!=Expected.Bonds.Count){
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
                &&ReadString(Root,"schema")=="prefracture-artifact-1"
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
            Writer.WriteString("schema","prefracture-artifact-1");
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
            Error="Prefracture input must be a closed convex static triangle mesh.";
            return false;
        }
        if(Settings.FragmentCount is <2 or >256||Settings.Density<=LogicalScalar.Zero
            ||Settings.BondStrengthPerArea<=LogicalScalar.Zero){
            Error="Prefracture settings require 2-256 fragments and positive density and bond strength.";
            return false;
        }
        try {
            if(!TryBuildSourcePolyhedron(Source,out List<PolygonFace>?SourceFaces,out List<ClipPlane>?SourcePlanes,out Error)){
                return false;
            }
            if(!TryGenerateSeeds(Source.Bounds,SourcePlanes,Settings.FragmentCount,Settings.Seed,Source.Fingerprint,out LogicalVector3[]Seeds)){
                Error="The requested number of distinct Voronoi seeds could not be placed inside the source volume.";
                return false;
            }
            List<Cell>Cells=new(Settings.FragmentCount);
            for(int FragmentIndex=0;FragmentIndex<Settings.FragmentCount;FragmentIndex++){
                List<PolygonFace>Faces=SourceFaces.Select(static Face=>Face.Clone()).ToList();
                Dictionary<int,LogicalScalar>NeighborAreas=[];
                for(int OtherIndex=0;OtherIndex<Seeds.Length;OtherIndex++){
                    if(OtherIndex==FragmentIndex){ continue; }
                    LogicalVector3 Delta=Seeds[OtherIndex]-Seeds[FragmentIndex];
                    if(!Delta.TryNormalize(out LogicalVector3 Normal)){
                        Error="Prefracture seeds are not distinct.";
                        return false;
                    }
                    if(!LogicalScalar.TrySquareRoot(Delta.LengthSquared,out LogicalScalar DeltaLength)
                        ||DeltaLength.IsZero){
                        Error="Prefracture seeds are not distinct.";
                        return false;
                    }
                    LogicalScalar Distance=(Seeds[OtherIndex].LengthSquared-Seeds[FragmentIndex].LengthSquared)
                        /(Two*DeltaLength);
                    if(!Clip(Faces,new ClipPlane(Normal,Distance,OtherIndex),out LogicalScalar CapArea)){
                        Error="A Voronoi fragment collapsed below the representable geometry tolerance.";
                        return false;
                    }
                    if(CapArea>GeometryToleranceSquared){ NeighborAreas[OtherIndex]=CapArea; }
                }
                if(!TryBuildCell(Source.Id,FragmentIndex,Faces,Settings.Density,NeighborAreas,out Cell CellValue,out Error)){
                    return false;
                }
                Cells.Add(CellValue);
            }
            List<FractureBond>Bonds=[];
            for(int First=0;First<Cells.Count;First++){
                for(int Second=First+1;Second<Cells.Count;Second++){
                    if(!Cells[First].NeighborAreas.TryGetValue(Second,out LogicalScalar FirstArea)
                        ||!Cells[Second].NeighborAreas.TryGetValue(First,out LogicalScalar SecondArea)){ continue; }
                    LogicalScalar Area=FirstArea<SecondArea?FirstArea:SecondArea;
                    if(Area<=GeometryToleranceSquared){ continue; }
                    Bonds.Add(new FractureBond(First,Second,Area*Settings.BondStrengthPerArea));
                }
            }
            Asset=new PrefractureAsset {
                SourceHash=Source.Fingerprint,
                Seed=Settings.Seed,
                Fragments=Cells.Select(static CellValue=>CellValue.Fragment).ToArray(),
                Bonds=Bonds,
                Meshes=Cells.Select(static CellValue=>CellValue.Mesh).ToArray(),
                CollisionShapes=Cells.Select(static CellValue=>CellValue.Collision).ToArray(),
            };
            if(!Asset.IsDeterministicallyOrdered()){
                Asset=null;
                Error="Generated prefracture data violates its canonical ordering invariant.";
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
            Error="Prefracture input must be a closed two-manifold mesh.";
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
                Error="Prefracture source contains a degenerate triangle.";
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
                    Error="Prefracture currently requires a convex source mesh.";
                    return false;
                }
            }
            if(!Clip(Faces,Plane,out _)){
                Error="Source mesh planes do not enclose a representable volume.";
                return false;
            }
        }
        return Faces.Count>=4;
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

    private static bool TryBuildCell(
        string SourceId,int FragmentIndex,List<PolygonFace>Faces,LogicalScalar Density,
        Dictionary<int,LogicalScalar>NeighborAreas,out Cell CellValue,out string Error){
        CellValue=null!;
        Error=string.Empty;
        List<LogicalVector3>WorldVertices=[];
        List<int>Indices=[];
        foreach(PolygonFace Face in Faces){
            if(Face.Vertices.Count<3){ continue; }
            int First=GetVertexIndex(WorldVertices,Face.Vertices[0]);
            for(int Index=1;Index<Face.Vertices.Count-1;Index++){
                int Second=GetVertexIndex(WorldVertices,Face.Vertices[Index]);
                int Third=GetVertexIndex(WorldVertices,Face.Vertices[Index+1]);
                if(First==Second||Second==Third||Third==First){ continue; }
                Indices.Add(First);
                Indices.Add(Second);
                Indices.Add(Third);
            }
        }
        if(WorldVertices.Count<4||Indices.Count<12){
            Error="A generated Voronoi fragment has no closed volume.";
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
            Error="A generated Voronoi fragment has zero representable volume.";
            return false;
        }
        LogicalVector3 Center=WeightedCenter/(Four*SignedSixVolume);
        LogicalScalar Mass=Abs(SignedSixVolume)/Six*Density;
        LogicalVector3[]LocalVertices=WorldVertices.Select(Vertex=>Vertex-Center).ToArray();
        FractureMesh Mesh=new(LocalVertices,Indices);
        if(!Mesh.TryGetTopologyError(out string TopologyError)){
            Error=$"Voronoi fragment {FragmentIndex} is not a closed two-manifold mesh "
                +$"({WorldVertices.Count} vertices, {Indices.Count/3} triangles, {Faces.Count} faces): {TopologyError}.";
            return false;
        }
        string ShapeId=$"{SourceId}.fragment.{FragmentIndex:D4}";
        PhysicsShapeAsset Collision=PhysicsShapeAsset.CreateConvex(ShapeId,LocalVertices);
        FractureFragment Fragment=new(FragmentIndex,ShapeId,Center,Mass);
        CellValue=new Cell(Fragment,Mesh,Collision,NeighborAreas);
        return true;
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
