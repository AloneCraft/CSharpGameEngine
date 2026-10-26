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
/// <summary>
/// A bond between two touching fragments (ADR-0078/M0113). <see cref="Strength"/> is the strain (an impulse, N·s) that breaks it, the shared face's
/// <see cref="Area"/> (m²) times the cook's strength per area. Bonds are undirected: one entry per pair, <see cref="FirstFragment"/> &lt; <see cref="SecondFragment"/>.
/// A version-1 artifact has no area (zero).
/// </summary>
public readonly record struct FractureBond(int FirstFragment,int SecondFragment,LogicalScalar Strength,LogicalScalar Area=default);

/// <summary>
/// A cooked prefracture (L6, ADR-0078/M0113, UE's Geometry Collection): the fragments of one closed convex source, each a closed mesh (with a material slot per
/// triangle: <see cref="FractureMesh.OuterSlot"/> for the source's surface, <see cref="FractureMesh.InteriorSlot"/> for a Voronoi cut), its convex collision
/// shape, centre, mass and Voronoi site; the bond graph; and one level of clusters (<see cref="Clusters"/>: the cluster of each fragment), the hierarchy the
/// runtime breaks from the top down. Coordinates are the source's own (the destructible node's local frame).
/// </summary>
public sealed class PrefractureAsset {
    public required string SourceHash { get;init; }
    public required int Seed { get;init; }
    public required IReadOnlyList<FractureFragment> Fragments { get;init; }
    public required IReadOnlyList<FractureBond> Bonds { get;init; }
    public IReadOnlyList<FractureMesh> Meshes { get;init; }=[];
    public IReadOnlyList<PhysicsShapeAsset> CollisionShapes { get;init; }=[];
    /// <summary>The Voronoi site of each fragment (the seed point whose cell it is). A point belongs to the fragment of the nearest site. Empty in version 1.</summary>
    public IReadOnlyList<LogicalVector3> Sites { get;init; }=[];
    /// <summary>The cluster of each fragment, numbered 0.. in the order of each cluster's lowest fragment. Empty in version 1 (each fragment its own cluster).</summary>
    public IReadOnlyList<int> Clusters { get;init; }=[];
    /// <summary>kg/m³ the masses were made with (0 in version 1).</summary>
    public LogicalScalar Density { get;init; }
    /// <summary>N·s/m² the bond strengths were made with (0 in version 1).</summary>
    public LogicalScalar BondStrengthPerArea { get;init; }
    /// <summary>
    /// The deeper levels of the cluster hierarchy (M0124, UE's Geometry Collection levels below the first): <c>SubClusters[0]</c> is level 1, each fragment's
    /// cluster inside its <see cref="Clusters"/> cluster, and so on. Each level's clusters lie inside one cluster of the level above, are connected, and are
    /// numbered 0.. in the order of their lowest fragment. Empty for one level (version 1 and 2 assets).
    /// </summary>
    public IReadOnlyList<IReadOnlyList<int>> SubClusters { get;init; }=[];
    /// <summary>How many clusters <see cref="Clusters"/> names (the fragment count when there are none).</summary>
    public int ClusterCount=>this.Clusters.Count==0?this.Fragments.Count:this.Clusters.Max()+1;
    /// <summary>How many levels of clusters the asset has: 0 without clusters, 1 with <see cref="Clusters"/> only, more with <see cref="SubClusters"/>.</summary>
    public int ClusterLevelCount=>this.Clusters.Count==0?0:1+this.SubClusters.Count;
    /// <summary>The cluster of <paramref name="Fragment"/> (itself when the asset has no clusters).</summary>
    public int ClusterOf(int Fragment)=>this.Clusters.Count==0?Fragment:this.Clusters[Fragment];
    /// <summary>The cluster of <paramref name="Fragment"/> at <paramref name="Level"/> (0 is <see cref="Clusters"/>).</summary>
    public int ClusterOf(int Fragment,int Level)=>Level==0?this.ClusterOf(Fragment):this.SubClusters[Level-1][Fragment];
    /// <summary>
    /// How many levels, from the top, put two fragments in one cluster (M0124): 0 for fragments of two top-level clusters (or an asset without clusters), up to
    /// <see cref="ClusterLevelCount"/> for two fragments of one deepest cluster. A bond's run-time strength is its strength times the cluster strength scale
    /// to this power, so the outer clusters part first.
    /// </summary>
    public int SharedClusterLevels(int First,int Second){
        if(this.Clusters.Count==0){ return 0; }
        var Shared=0;
        while(Shared<this.ClusterLevelCount&&this.ClusterOf(First,Shared)==this.ClusterOf(Second,Shared)){ Shared++; }
        return Shared;
    }

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
                ||Bond.SecondFragment>=this.Fragments.Count||Bond.Strength<=LogicalScalar.Zero||Bond.Area<LogicalScalar.Zero){ return false; }
        }
        if(this.Sites.Count!=0&&this.Sites.Count!=this.Fragments.Count){ return false; }
        if(this.Clusters.Count==0&&this.SubClusters.Count!=0){ return false; }
        for(var Level=0;Level<this.ClusterLevelCount;Level++){
            var Numbers=Level==0?this.Clusters:this.SubClusters[Level-1];
            if(Numbers.Count!=this.Fragments.Count){ return false; }
            // Canonical numbering: a cluster's number is the count of clusters whose lowest fragment comes before its own.
            var Next=0;
            foreach(var Cluster in Numbers){
                if(Cluster<0||Cluster>Next){ return false; }
                if(Cluster==Next){ Next++; }
            }
            if(Level==0){ continue; }
            // A cluster of a deeper level lies inside one cluster of the level above.
            var Parent=new int[Next];
            Array.Fill(Parent,-1);
            for(var Fragment=0;Fragment<Numbers.Count;Fragment++){
                var Above=this.ClusterOf(Fragment,Level-1);
                if(Parent[Numbers[Fragment]]<0){ Parent[Numbers[Fragment]]=Above; }
                else if(Parent[Numbers[Fragment]]!=Above){ return false; }
            }
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
        for(var Level=0;Level<this.ClusterLevelCount;Level++){
            // Each cluster of every level is one connected piece of the bond graph (the runtime releases a cluster as one body).
            var ClusterParents=new int[this.Fragments.Count];
            for(var Index=0;Index<ClusterParents.Length;Index++){ ClusterParents[Index]=Index; }
            foreach(var Bond in this.Bonds){
                if(this.ClusterOf(Bond.FirstFragment,Level)==this.ClusterOf(Bond.SecondFragment,Level)){ Union(ClusterParents,Bond.FirstFragment,Bond.SecondFragment); }
            }
            var ClusterRoots=new Dictionary<int,int>();
            for(var Index=0;Index<ClusterParents.Length;Index++){
                var ClusterRoot=Find(ClusterParents,Index);
                if(ClusterRoots.TryGetValue(this.ClusterOf(Index,Level),out var Existing)){
                    if(Existing!=ClusterRoot){ return false; }
                }
                else {
                    ClusterRoots.Add(this.ClusterOf(Index,Level),ClusterRoot);
                }
            }
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

