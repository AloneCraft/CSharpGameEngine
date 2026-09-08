using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CSharpGameEngine.Runtime.Physics;

public readonly record struct PhysicsCookingInput(
    ReadOnlyMemory<byte> SourceAssetBytes,string AssetGuid,string ImportVersion,
    string Settings,string Scale,string Seed,string CookerVersion,string SchemaVersion,string ScalarVersion);

public static class PhysicsCookingKey {
    public static string Compute(in PhysicsCookingInput Input){
        using IncrementalHash Hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Hash.AppendData(Input.SourceAssetBytes.Span);
        Append(Hash,Input.AssetGuid);
        Append(Hash,Input.ImportVersion);
        Append(Hash,Input.Settings);
        Append(Hash,Input.Scale);
        Append(Hash,Input.Seed);
        Append(Hash,Input.CookerVersion);
        Append(Hash,Input.SchemaVersion);
        Append(Hash,Input.ScalarVersion);
        return Convert.ToHexString(Hash.GetHashAndReset()).ToLowerInvariant();
    }

    public static string GetArtifactDirectory(string RepositoryRoot,string AssetGuid,string Key){
        ArgumentException.ThrowIfNullOrWhiteSpace(RepositoryRoot);
        ValidatePathSegment(AssetGuid,nameof(AssetGuid));
        ArgumentException.ThrowIfNullOrWhiteSpace(Key);
        return Path.Combine(RepositoryRoot,".artifacts","physics",AssetGuid,Key);
    }

    private static void ValidatePathSegment(string Value,string ParameterName){
        ArgumentException.ThrowIfNullOrWhiteSpace(Value,ParameterName);
        if(Value is "." or ".."||Value.IndexOfAny(Path.GetInvalidFileNameChars())>=0
            ||Value.Contains(Path.DirectorySeparatorChar)||Value.Contains(Path.AltDirectorySeparatorChar)){
            throw new ArgumentException("Artifact identity must be one canonical path segment.",ParameterName);
        }
    }

    private static void Append(IncrementalHash Hash,string Value){
        byte[] Bytes=Encoding.UTF8.GetBytes(Value??string.Empty);
        Span<byte> Length=stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(Length,Bytes.Length);
        Hash.AppendData(Length);
        Hash.AppendData(Bytes);
    }
}

public readonly record struct PhysicsCookResult(
    bool Success,bool Reused,string Key,string ArtifactPath,string ManifestPath,string Error);

public static class PhysicsShapeCooker {
    public const string ArtifactFileName="shape.cgephysics";
    public const string ManifestFileName="manifest.json";

    public static PhysicsCookResult Cook(
        string RepositoryRoot,in PhysicsCookingInput Input,PhysicsShapeAsset Asset){
        ArgumentException.ThrowIfNullOrWhiteSpace(RepositoryRoot);
        ArgumentNullException.ThrowIfNull(Asset);
        string Key=PhysicsCookingKey.Compute(Input);
        string FinalDirectory=Path.GetFullPath(PhysicsCookingKey.GetArtifactDirectory(RepositoryRoot,Input.AssetGuid,Key));
        string ArtifactPath=Path.Combine(FinalDirectory,ArtifactFileName);
        string ManifestPath=Path.Combine(FinalDirectory,ManifestFileName);
        if(Directory.Exists(FinalDirectory)){
            return ValidateExisting(FinalDirectory,Input.AssetGuid,Key,Asset.Fingerprint)
                ?new PhysicsCookResult(true,true,Key,ArtifactPath,ManifestPath,string.Empty)
                :new PhysicsCookResult(false,false,Key,ArtifactPath,ManifestPath,
                    "The existing physics artifact does not match its immutable cooking identity.");
        }
        string Parent=Path.GetDirectoryName(FinalDirectory)
            ??throw new InvalidOperationException("Physics artifact directory has no parent.");
        Directory.CreateDirectory(Parent);
        string Temporary=Path.Combine(Parent,$".{Key}.tmp-{Guid.NewGuid():N}");
        try {
            Directory.CreateDirectory(Temporary);
            string TemporaryArtifact=Path.Combine(Temporary,ArtifactFileName);
            using(FileStream Stream=new(TemporaryArtifact,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)){
                PhysicsShapeArtifact.Write(Stream,Asset);
                Stream.Flush(true);
            }
            string TemporaryManifest=Path.Combine(Temporary,ManifestFileName);
            WriteManifest(TemporaryManifest,Input.AssetGuid,Key,Asset);
            if(!ValidateExisting(Temporary,Input.AssetGuid,Key,Asset.Fingerprint)){
                return new PhysicsCookResult(false,false,Key,ArtifactPath,ManifestPath,
                    "The staged physics artifact failed canonical validation.");
            }
            try {
                Directory.Move(Temporary,FinalDirectory);
            }
            catch(IOException)when(Directory.Exists(FinalDirectory)){
                return ValidateExisting(FinalDirectory,Input.AssetGuid,Key,Asset.Fingerprint)
                    ?new PhysicsCookResult(true,true,Key,ArtifactPath,ManifestPath,string.Empty)
                    :new PhysicsCookResult(false,false,Key,ArtifactPath,ManifestPath,
                        "A concurrent cook published different content for the same immutable key.");
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
        string DirectoryPath,string AssetGuid,string Key,string ExpectedShapeFingerprint){
        try {
            string ArtifactPath=Path.Combine(DirectoryPath,ArtifactFileName);
            string ManifestPath=Path.Combine(DirectoryPath,ManifestFileName);
            if(!File.Exists(ArtifactPath)||!File.Exists(ManifestPath)){ return false; }
            string[] Entries=Directory.GetFileSystemEntries(DirectoryPath);
            if(Entries.Length!=2
                ||!Entries.Any(Entry=>StringComparer.Ordinal.Equals(Path.GetFileName(Entry),ArtifactFileName))
                ||!Entries.Any(Entry=>StringComparer.Ordinal.Equals(Path.GetFileName(Entry),ManifestFileName))){ return false; }
            if(new FileInfo(ManifestPath).Length>65_536){ return false; }
            using FileStream Artifact=File.OpenRead(ArtifactPath);
            if(!PhysicsShapeArtifact.TryRead(Artifact,out PhysicsShapeAsset? Shape,out _)
                ||Shape is null||!StringComparer.Ordinal.Equals(Shape.Fingerprint,ExpectedShapeFingerprint)){ return false; }
            using JsonDocument Document=JsonDocument.Parse(
                File.ReadAllBytes(ManifestPath),
                new JsonDocumentOptions { AllowTrailingCommas=false,CommentHandling=JsonCommentHandling.Disallow,MaxDepth=16 });
            JsonElement Root=Document.RootElement;
            if(Root.ValueKind!=JsonValueKind.Object||Root.EnumerateObject().Count()!=7){ return false; }
            return ReadString(Root,"assetGuid")==AssetGuid
                &&ReadString(Root,"key")==Key
                &&ReadString(Root,"shapeId")==Shape.Id
                &&ReadString(Root,"shapeKind")==Shape.Kind.ToString()
                &&ReadString(Root,"shapeFingerprint")==ExpectedShapeFingerprint
                &&ReadString(Root,"schema")=="physics-shape-artifact-1"
                &&ReadString(Root,"scalar")=="LogicalScalar-Q64.64";
        }
        catch(Exception Exception)when(Exception is IOException or UnauthorizedAccessException or InvalidDataException
            or JsonException or KeyNotFoundException or InvalidOperationException){
            return false;
        }
    }

    private static void WriteManifest(
        string PathValue,string AssetGuid,string Key,PhysicsShapeAsset Asset){
        using FileStream Stream=new(PathValue,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough);
        using(Utf8JsonWriter Writer=new(Stream,new JsonWriterOptions { Indented=false })){
            Writer.WriteStartObject();
            Writer.WriteString("assetGuid",AssetGuid);
            Writer.WriteString("key",Key);
            Writer.WriteString("shapeId",Asset.Id);
            Writer.WriteString("shapeKind",Asset.Kind.ToString());
            Writer.WriteString("shapeFingerprint",Asset.Fingerprint);
            Writer.WriteString("schema","physics-shape-artifact-1");
            Writer.WriteString("scalar","LogicalScalar-Q64.64");
            Writer.WriteEndObject();
        }
        Stream.Flush(true);
    }

    private static string ReadString(JsonElement Root,string Name)=>
        Root.TryGetProperty(Name,out JsonElement Value)&&Value.ValueKind==JsonValueKind.String
            ?Value.GetString()??throw new InvalidDataException()
            :throw new InvalidDataException();

    private static bool IsDirectChild(string Parent,string Child)=>
        StringComparer.OrdinalIgnoreCase.Equals(
            Path.GetFullPath(Parent).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetDirectoryName(Path.GetFullPath(Child))?.TrimEnd(Path.DirectorySeparatorChar));
}

public readonly record struct FractureFragment(int Index,string ShapeId,LogicalVector3 LocalCenter,LogicalScalar Mass);
public readonly record struct FractureBond(int FirstFragment,int SecondFragment,LogicalScalar Strength);

public sealed class PrefractureAsset {
    public required string SourceHash { get;init; }
    public required int Seed { get;init; }
    public required IReadOnlyList<FractureFragment> Fragments { get;init; }
    public required IReadOnlyList<FractureBond> Bonds { get;init; }
    public IReadOnlyList<FractureMesh> Meshes { get;init; }=[];
    public IReadOnlyList<PhysicsShapeAsset> CollisionShapes { get;init; }=[];

    public bool IsDeterministicallyOrdered(){
        for(int Index=1;Index<this.Fragments.Count;Index++){
            if(this.Fragments[Index-1].Index>=this.Fragments[Index].Index){ return false; }
        }
        for(int Index=1;Index<this.Bonds.Count;Index++){
            FractureBond Previous=this.Bonds[Index-1];
            FractureBond Current=this.Bonds[Index];
            if(Previous.FirstFragment>Current.FirstFragment
                ||Previous.FirstFragment==Current.FirstFragment&&Previous.SecondFragment>=Current.SecondFragment){ return false; }
        }
        if(this.Meshes.Count!=0&&this.Meshes.Count!=this.Fragments.Count){ return false; }
        if(this.CollisionShapes.Count!=0&&this.CollisionShapes.Count!=this.Fragments.Count){ return false; }
        for(int Index=0;Index<this.Fragments.Count;Index++){
            FractureFragment Fragment=this.Fragments[Index];
            if(Fragment.Index!=Index||Fragment.Mass<=LogicalScalar.Zero){ return false; }
            if(this.CollisionShapes.Count!=0
                &&!StringComparer.Ordinal.Equals(Fragment.ShapeId,this.CollisionShapes[Index].Id)){ return false; }
        }
        foreach(FractureBond Bond in this.Bonds){
            if(Bond.FirstFragment<0||Bond.SecondFragment<=Bond.FirstFragment
                ||Bond.SecondFragment>=this.Fragments.Count||Bond.Strength<=LogicalScalar.Zero){ return false; }
        }
        return true;
    }

    public bool IsCanonicalArtifact(){
        if(!this.IsDeterministicallyOrdered()||this.Fragments.Count<2
            ||this.Meshes.Count!=this.Fragments.Count
            ||this.CollisionShapes.Count!=this.Fragments.Count){
            return false;
        }
        for(int Index=0;Index<this.Fragments.Count;Index++){
            FractureFragment Fragment=this.Fragments[Index];
            FractureMesh Mesh=this.Meshes[Index];
            PhysicsShapeAsset Shape=this.CollisionShapes[Index];
            if(string.IsNullOrWhiteSpace(Fragment.ShapeId)||!Mesh.IsClosedManifold()
                ||Shape.Kind!=PhysicsShapeAssetKind.Convex
                ||!StringComparer.Ordinal.Equals(Fragment.ShapeId,Shape.Id)
                ||!Shape.Vertices.SequenceEqual(Mesh.Vertices)){
                return false;
            }
        }
        int[]Parents=new int[this.Fragments.Count];
        for(int Index=0;Index<Parents.Length;Index++){ Parents[Index]=Index; }
        foreach(FractureBond Bond in this.Bonds){ Union(Parents,Bond.FirstFragment,Bond.SecondFragment); }
        int Root=Find(Parents,0);
        for(int Index=1;Index<Parents.Length;Index++){
            if(Find(Parents,Index)!=Root){ return false; }
        }
        return true;
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

public readonly record struct FractureBreak(ulong ParentStableId,int BondIndex,int FirstFragment,int SecondFragment);

public sealed class PrefractureRuntime {
    private readonly Dictionary<(ulong ParentStableId,int BondIndex),LogicalScalar> Accumulated=[];
    private readonly HashSet<(ulong ParentStableId,int BondIndex)>Broken=[];

    public void Accumulate(ulong ParentStableId,int BondIndex,LogicalScalar Impulse){
        (ulong ParentStableId,int BondIndex) Key=(ParentStableId,BondIndex);
        if(this.Broken.Contains(Key)){ return; }
        this.Accumulated.TryGetValue(Key,out LogicalScalar Current);
        this.Accumulated[Key]=Current+Impulse;
    }

    public int Resolve(ulong ParentStableId,PrefractureAsset Asset,List<FractureBreak> Results){
        ArgumentNullException.ThrowIfNull(Asset);
        ArgumentNullException.ThrowIfNull(Results);
        int Start=Results.Count;
        for(int Index=0;Index<Asset.Bonds.Count;Index++){
            FractureBond Bond=Asset.Bonds[Index];
            (ulong ParentStableId,int BondIndex)Key=(ParentStableId,Index);
            if(this.Accumulated.TryGetValue(Key,out LogicalScalar Total)&&Total>=Bond.Strength&&this.Broken.Add(Key)){
                Results.Add(new FractureBreak(ParentStableId,Index,Bond.FirstFragment,Bond.SecondFragment));
                this.Accumulated.Remove(Key);
            }
        }
        return Results.Count-Start;
    }

    public void Remove(ulong ParentStableId){
        List<(ulong ParentStableId,int BondIndex)>Keys=[];
        foreach((ulong ParentStableId,int BondIndex) Key in this.Accumulated.Keys){
            if(Key.ParentStableId==ParentStableId){ Keys.Add(Key); }
        }
        foreach((ulong ParentStableId,int BondIndex) Key in Keys){ this.Accumulated.Remove(Key); }
        this.Broken.RemoveWhere(Key=>Key.ParentStableId==ParentStableId);
    }
}
