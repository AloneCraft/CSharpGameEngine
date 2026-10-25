using CSharpGameEngine.Numerics;
using CSharpGameEngine.Runtime.Physics;
using System.Globalization;
using System.Text.Json;

namespace CSharpGameEngine.Physics.Cli;

internal static class PhysicsBridgeWorker {
    internal static(int ExitCode,string Output)Run(string[]Arguments,CancellationToken CancellationToken){
        return Arguments switch {
            ["bake","--assembly",_,_,_,_,_]=>Bake(Arguments,CancellationToken),
            ["replay","--assembly",_,_,_,_,_,_]=>Replay(Arguments,CancellationToken),
            ["inspect-prefracture",_,_]=>InspectPrefracture(Arguments,CancellationToken),
            ["preview-prefracture",..]=>PreviewPrefracture(Arguments,CancellationToken),
            _=>(1,"Physics resident host accepts Bake, replay and prefracture inspection and preview requests."),
        };
    }

    /// <summary>
    /// ADR-0078/M0113: <c>replay --assembly &lt;game.dll&gt; &lt;scene&gt; &lt;cache&gt; &lt;output&gt; &lt;tickCount&gt; &lt;dependencyHash&gt;</c> plays a baked cache back in the
    /// game and writes what the world held as a cache. Equal bytes to the bake mean the playback reproduced it (bodies, poses, events and bond breaks).
    /// </summary>
    internal static(int ExitCode,string Output)Replay(string[]Arguments,CancellationToken CancellationToken){
        if(Arguments is not ["replay","--assembly",var AssemblyArgument,var SceneName,var CacheArgument,var OutputArgument,var TickArgument,var DependencyHash]){
            return(1,"Physics replay requires --assembly <game.dll> and five arguments.");
        }
        string GameAssembly;
        string CachePath;
        string Output;
        try {
            GameAssembly=Path.GetFullPath(AssemblyArgument);
            CachePath=Path.GetFullPath(CacheArgument);
            Output=Path.GetFullPath(OutputArgument);
        }
        catch(Exception Exception)when(Exception is ArgumentException or NotSupportedException){ return(2,Exception.Message); }
        if(!File.Exists(GameAssembly)){ return(2,$"The game assembly was not found: {GameAssembly}"); }
        if(!File.Exists(CachePath)){ return(2,$"The physics cache was not found: {CachePath}"); }
        if(!int.TryParse(TickArgument,NumberStyles.Integer,CultureInfo.InvariantCulture,out int TickCount)||TickCount<1){
            return(2,"Tick count is outside the supported physics cache range.");
        }
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(Output)??Environment.CurrentDirectory);
            using FileStream Cache=File.OpenRead(CachePath);
            using FileStream Stream=new(Output,FileMode.Create,FileAccess.Write,FileShare.None);
            if(!GameBake.TryReplay(GameAssembly,SceneName,TickCount,DependencyHash,Cache,Stream,CancellationToken,out string Error)){
                return(CancellationToken.IsCancellationRequested?130:4,Error);
            }
            return(0,Output);
        }
        catch(Exception Exception)when(Exception is IOException or UnauthorizedAccessException){ return(3,Exception.Message); }
    }

    internal static(int ExitCode,string Output)Bake(string[]Arguments,CancellationToken CancellationToken){
        if(Arguments is not ["bake","--assembly",var AssemblyArgument,var SceneName,var OutputArgument,var TickArgument,var DependencyHash]){
            return(1,"Physics bake requires --assembly <game.dll> and four arguments.");
        }
        string GameAssembly;
        string Output;
        try {
            GameAssembly=Path.GetFullPath(AssemblyArgument);
            Output=Path.GetFullPath(OutputArgument);
        }
        catch(Exception Exception)when(Exception is ArgumentException or NotSupportedException){ return(2,Exception.Message); }
        if(!File.Exists(GameAssembly)){ return(2,$"The game assembly was not found: {GameAssembly}"); }
        // The upper bound is the game's physics cache limit, which the game's generated TryBake checks.
        if(!int.TryParse(TickArgument,NumberStyles.Integer,CultureInfo.InvariantCulture,out int TickCount)||TickCount<1){
            return(2,"Tick count is outside the supported physics cache range.");
        }
        if(string.IsNullOrWhiteSpace(SceneName)||string.IsNullOrWhiteSpace(DependencyHash)){
            return(2,"A fully-qualified scene name and dependency hash are required.");
        }
        if(CancellationToken.IsCancellationRequested){ return(130,"Physics Bake was cancelled before it started."); }

        string DirectoryPath=Path.GetDirectoryName(Output)??Environment.CurrentDirectory;
        string TemporaryPath=Path.Combine(DirectoryPath,$".{Path.GetFileName(Output)}.{Guid.NewGuid():N}.tmp");
        int ExitCode=0;
        string Result=string.Empty;
        try {
            Directory.CreateDirectory(DirectoryPath);
            using(FileStream Stream=new(
                TemporaryPath,FileMode.CreateNew,FileAccess.Write,FileShare.None,65_536,
                FileOptions.SequentialScan|FileOptions.WriteThrough)){
                if(!GameBake.TryBake(
                    GameAssembly,SceneName,TickCount,DependencyHash,Stream,CancellationToken,out string Error)){
                    return(CancellationToken.IsCancellationRequested?130:4,Error);
                }
                Stream.Flush(true);
            }
            if(File.Exists(Output)){ File.Replace(TemporaryPath,Output,null,true); }
            else { File.Move(TemporaryPath,Output); }
            Result=Output;
        }
        catch(Exception Exception)when(Exception is IOException or UnauthorizedAccessException or ArgumentException){
            ExitCode=3;
            Result=Exception.Message;
        }
        finally {
            try {
                if(File.Exists(TemporaryPath)){ File.Delete(TemporaryPath); }
            }
            catch(Exception Exception)when(Exception is IOException or UnauthorizedAccessException){
                ExitCode=ExitCode==0?3:ExitCode;
                Result=string.IsNullOrEmpty(Result)
                    ?$"Failed to remove temporary Bake output: {Exception.Message}"
                    :$"{Result}{Environment.NewLine}Failed to remove temporary Bake output: {Exception.Message}";
            }
        }
        return(ExitCode,Result);
    }

    internal static(int ExitCode,string Output)InspectPrefracture(
        string[]Arguments,CancellationToken CancellationToken){
        if(Arguments.Length!=3){ return(1,"Prefracture inspection requires an artifact and output JSON path."); }
        if(CancellationToken.IsCancellationRequested){ return(130,"Prefracture inspection was cancelled."); }
        try {
            string ArtifactPath=Path.GetFullPath(Arguments[1]);
            string OutputPath=Path.GetFullPath(Arguments[2]);
            using FileStream Input=File.OpenRead(ArtifactPath);
            if(!PrefractureArtifact.TryRead(Input,out PrefractureAsset?Asset,out string Error)||Asset is null){
                return(4,Error);
            }
            WriteDocument(OutputPath,Asset);
            return(0,OutputPath);
        }
        catch(Exception Exception)when(Exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException){
            return(3,Exception.Message);
        }
    }

    /// <summary>
    /// ADR-0078/M0113, the Visual Studio extension's Fracture preview: <c>preview-prefracture &lt;halfR&gt; &lt;halfU&gt; &lt;halfF&gt; &lt;fragmentCount&gt; &lt;seed&gt;
    /// &lt;clusterCount&gt; &lt;output.json&gt; [centerR centerU centerF] [density] [bondStrength]</c> cuts a box collider as the build's Cook module would (the same
    /// twelve-triangle box and cooker) and writes the inspection document with each fragment's edges, so a changed count or seed shows at once, before a build.
    /// A refused cut (FRACTUREnnn) exits with 5 and its message.
    /// </summary>
    internal static(int ExitCode,string Output)PreviewPrefracture(string[]Arguments,CancellationToken CancellationToken){
        if(Arguments.Length is not (8 or 11 or 12 or 13)){
            return(1,"Prefracture preview requires halfR halfU halfF fragmentCount seed clusterCount output.json [centerR centerU centerF] [density] [bondStrength].");
        }
        if(CancellationToken.IsCancellationRequested){ return(130,"Prefracture preview was cancelled."); }
        static bool Scalar(string Text,out LogicalScalar Value)=>LogicalScalar.TryParseInvariant(Text,out Value);
        static bool Whole(string Text,out int Value)=>int.TryParse(Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out Value);
        if(!Scalar(Arguments[1],out var HalfR)||!Scalar(Arguments[2],out var HalfU)||!Scalar(Arguments[3],out var HalfF)
            ||HalfR<=LogicalScalar.Zero||HalfU<=LogicalScalar.Zero||HalfF<=LogicalScalar.Zero){
            return(2,"The half extents must be positive numbers.");
        }
        if(!Whole(Arguments[4],out var Count)||!Whole(Arguments[5],out var Seed)||!Whole(Arguments[6],out var Clusters)){
            return(2,"The fragment count, seed and cluster count must be whole numbers.");
        }
        var Center=LogicalVector3.Zero;
        if(Arguments.Length>=11){
            if(!Scalar(Arguments[8],out var CenterR)||!Scalar(Arguments[9],out var CenterU)||!Scalar(Arguments[10],out var CenterF)){ return(2,"The centre must be three numbers."); }
            Center=new LogicalVector3(CenterR,CenterU,CenterF);
        }
        var Density=LogicalScalar.FromInt64(500);
        var Strength=LogicalScalar.FromInt64(2000);
        if(Arguments.Length>=12&&(!Scalar(Arguments[11],out Density)||Density<=LogicalScalar.Zero)){ return(2,"The density must be a positive number."); }
        if(Arguments.Length>=13&&(!Scalar(Arguments[12],out Strength)||Strength<=LogicalScalar.Zero)){ return(2,"The bond strength must be a positive number."); }
        try {
            var Half=new LogicalVector3(HalfR,HalfU,HalfF);
            var Vertices=new LogicalVector3[8];
            for(var Index=0;Index<8;Index++){
                Vertices[Index]=Center+new LogicalVector3((Index&1)==0?-Half.R:Half.R,(Index&2)==0?-Half.U:Half.U,(Index&4)==0?-Half.F:Half.F);
            }
            int[]Indices=[0,4,6,0,6,2,1,3,7,1,7,5,0,1,5,0,5,4,2,6,7,2,7,3,0,2,3,0,3,1,4,5,7,4,7,6];
            var Source=PhysicsShapeAsset.CreateStaticTriangleMesh("preview",Vertices,Indices);
            if(!PrefractureCooker.TryGenerate(Source,new PrefractureCookSettings(Count,Seed,Density,Strength,Clusters),out var Asset,out var Error)||Asset is null){
                return(5,Error);
            }
            var OutputPath=Path.GetFullPath(Arguments[7]);
            WriteDocument(OutputPath,Asset);
            return(0,OutputPath);
        }
        catch(Exception Exception)when(Exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or OverflowException){
            return(3,Exception.Message);
        }
    }

    private static void WriteDocument(string OutputPath,PrefractureAsset Asset){
        PrefractureInspectionDocument Document=new(){
            SourceHash=Asset.SourceHash,
            ClusterCount=Asset.ClusterCount,
            Fragments=[..Asset.Fragments.Select(Fragment=>new PrefractureInspectionFragment {
                Index=Fragment.Index,
                R=ToSingle(Fragment.LocalCenter.R),U=ToSingle(Fragment.LocalCenter.U),F=ToSingle(Fragment.LocalCenter.F),
                Cluster=Asset.ClusterOf(Fragment.Index),
                Edges=FragmentEdges(Asset,Fragment.Index),
            })],
            Bonds=[..Asset.Bonds.Select(static Bond=>new PrefractureInspectionBond {
                First=Bond.FirstFragment,Second=Bond.SecondFragment,Strength=ToSingle(Bond.Strength),Area=ToSingle(Bond.Area),
            })],
        };
        string?DirectoryPath=Path.GetDirectoryName(OutputPath);
        if(!string.IsNullOrEmpty(DirectoryPath)){ Directory.CreateDirectory(DirectoryPath); }
        File.WriteAllText(OutputPath,JsonSerializer.Serialize(Document));
    }

    /// <summary>The outline of a fragment in the source's frame: each edge between two faces that are not coplanar, as six floats (R, U, F twice).</summary>
    private static float[]FragmentEdges(PrefractureAsset Asset,int Fragment){
        var Mesh=Asset.Meshes[Fragment];
        var Center=Asset.Fragments[Fragment].LocalCenter;
        Dictionary<(int,int),List<LogicalVector3>>Faces=[];
        for(var Triangle=0;Triangle<Mesh.Indices.Count/3;Triangle++){
            var A=Mesh.Vertices[Mesh.Indices[Triangle*3]];
            var B=Mesh.Vertices[Mesh.Indices[Triangle*3+1]];
            var C=Mesh.Vertices[Mesh.Indices[Triangle*3+2]];
            var Normal=LogicalVector3.Cross(B-A,C-A).TryNormalize(out var Unit)?Unit:LogicalVector3.Zero;
            foreach(var (First,Second) in (ReadOnlySpan<(int,int)>)[(Mesh.Indices[Triangle*3],Mesh.Indices[Triangle*3+1]),(Mesh.Indices[Triangle*3+1],Mesh.Indices[Triangle*3+2]),(Mesh.Indices[Triangle*3+2],Mesh.Indices[Triangle*3])]){
                var Key=First<Second?(First,Second):(Second,First);
                if(!Faces.TryGetValue(Key,out var Normals)){ Faces[Key]=Normals=[]; }
                Normals.Add(Normal);
            }
        }
        List<float>Edges=[];
        var Flat=LogicalScalar.One-LogicalScalar.FromSingle(0.0001f);
        foreach(var ((First,Second),Normals) in Faces.OrderBy(static Pair=>Pair.Key.Item1).ThenBy(static Pair=>Pair.Key.Item2)){
            if(Normals.Count==2&&LogicalVector3.Dot(Normals[0],Normals[1])>=Flat){ continue; }
            foreach(var Point in (ReadOnlySpan<LogicalVector3>)[Mesh.Vertices[First]+Center,Mesh.Vertices[Second]+Center]){
                Edges.Add(ToSingle(Point.R));
                Edges.Add(ToSingle(Point.U));
                Edges.Add(ToSingle(Point.F));
            }
        }
        return [.. Edges];
    }

    private static float ToSingle(LogicalScalar Value)=>Value.TryToSingle(out float Result)
        ?Result:throw new InvalidDataException("Prefracture debug value exceeds the float adapter contract.");

    private sealed class PrefractureInspectionDocument {
        public string SourceHash { get;set; }=string.Empty;
        public int ClusterCount { get;set; }
        public PrefractureInspectionFragment[]Fragments { get;set; }=[];
        public PrefractureInspectionBond[]Bonds { get;set; }=[];
    }
    private sealed class PrefractureInspectionFragment { public int Index { get;set; }public float R { get;set; }public float U { get;set; }public float F { get;set; }public int Cluster { get;set; }public float[]Edges { get;set; }=[]; }
    private sealed class PrefractureInspectionBond { public int First { get;set; }public int Second { get;set; }public float Strength { get;set; }public float Area { get;set; } }
}
