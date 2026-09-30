using CSharpGameEngine.Bridge.Hosting;
using CSharpGameEngine.Content;
using CSharpGameEngine.Import;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Physics.Cli;
using CSharpGameEngine.Runtime.Physics;
using CSharpGameEngine.Runtime.Simulation;
using System.Globalization;

if(BridgeServeCommand.TryParse(args,out var PipeName,out var OwnerProcessId)){
    await BridgeServerHost.RunArgumentsAsync(
        PipeName,PhysicsBridgeWorker.Run,TimeSpan.FromMinutes(15),OwnerProcessId:OwnerProcessId);
    return 0;
}

return args.Length==0?Usage():args[0].ToLowerInvariant() switch {
    "cook"=>Cook(args),
    "prefracture"=>Prefracture(args),
    "inspect-prefracture"=>Print(PhysicsBridgeWorker.InspectPrefracture(args,CancellationToken.None)),
    "bake"=>BakeWithConsoleCancellation(args),
    _=>Usage(),
};

static int Cook(string[] Args){
    if(Args.Length<4){ return Usage(); }
    string Source=Path.GetFullPath(Args[1]);
    string RepositoryRoot=Path.GetFullPath(Args[2]);
    string AssetGuid=Args[3];
    string Settings=Args.Length>4?Args[4]:"static-mesh";
    string Seed=Args.Length>5?Args[5]:"0";
    if(!TryLoadMesh(Source,out byte[]Bytes,out LogicalVector3[]Vertices,out int[]Indices,out int ExitCode)){
        return ExitCode;
    }
    PhysicsShapeAsset Asset;
    try {
        Asset=Settings switch {
            "static-mesh"=>PhysicsShapeAsset.CreateStaticTriangleMesh(AssetGuid,Vertices,Indices),
            "convex"=>PhysicsShapeAsset.CreateConvex(AssetGuid,Vertices.Distinct().ToArray()),
            _=>throw new ArgumentException("Settings must be 'static-mesh' or 'convex'."),
        };
    }
    catch(Exception Exception)when(Exception is ArgumentException or OverflowException){
        Console.Error.WriteLine(Exception.Message);
        return 4;
    }
    PhysicsCookingInput Input=new(
        Bytes,AssetGuid,"provider-neutral-ruf-v1",Settings,"1,1,1",Seed,
        "cge-physics-cooker-3","physics-shape-artifact-1","LogicalScalar-Q64.64");
    PhysicsCookResult Result=PhysicsShapeCooker.Cook(RepositoryRoot,Input,Asset);
    if(!Result.Success){
        Console.Error.WriteLine(Result.Error);
        return 5;
    }
    Console.WriteLine(Result.ArtifactPath);
    return 0;
}

static int Prefracture(string[] Args){
    if(Args.Length<5){ return Usage(); }
    string Source=Path.GetFullPath(Args[1]);
    string RepositoryRoot=Path.GetFullPath(Args[2]);
    string AssetGuid=Args[3];
    if(!int.TryParse(Args[4],NumberStyles.Integer,CultureInfo.InvariantCulture,out int FragmentCount)
        ||FragmentCount is <2 or >256){
        Console.Error.WriteLine("Fragment count must be an invariant integer from 2 through 256.");
        return 2;
    }
    int Seed=0;
    if(Args.Length>5&&!int.TryParse(Args[5],NumberStyles.Integer,CultureInfo.InvariantCulture,out Seed)){
        Console.Error.WriteLine("Seed must be an invariant 32-bit integer.");
        return 2;
    }
    LogicalScalar Density=LogicalScalar.One;
    if(Args.Length>6&&(!LogicalScalar.TryParseInvariant(Args[6],out Density)
        ||Density<=LogicalScalar.Zero)){
        Console.Error.WriteLine("Density must be a positive LogicalScalar.");
        return 2;
    }
    LogicalScalar BondStrength=LogicalScalar.One;
    if(Args.Length>7&&(!LogicalScalar.TryParseInvariant(Args[7],out BondStrength)
        ||BondStrength<=LogicalScalar.Zero)){
        Console.Error.WriteLine("Bond strength per area must be a positive LogicalScalar.");
        return 2;
    }
    if(!TryLoadMesh(Source,out byte[]Bytes,out LogicalVector3[]Vertices,out int[]Indices,out int ExitCode)){
        return ExitCode;
    }
    PhysicsShapeAsset SourceShape;
    try {
        SourceShape=PhysicsShapeAsset.CreateStaticTriangleMesh(AssetGuid,Vertices,Indices);
    }
    catch(Exception Exception)when(Exception is ArgumentException or OverflowException){
        Console.Error.WriteLine(Exception.Message);
        return 4;
    }
    PrefractureCookSettings Settings=new(FragmentCount,Seed,Density,BondStrength);
    if(!PrefractureCooker.TryGenerate(SourceShape,Settings,out PrefractureAsset?Asset,out string Error)){
        Console.Error.WriteLine(Error);
        return 5;
    }
    string CanonicalSettings=$"prefracture:{FragmentCount.ToString(CultureInfo.InvariantCulture)}"
        +$";density={Density.ToStringInvariant()};bond={BondStrength.ToStringInvariant()}";
    PhysicsCookingInput Input=new(
        Bytes,AssetGuid,"provider-neutral-ruf-v1",CanonicalSettings,"1,1,1",
        Seed.ToString(CultureInfo.InvariantCulture),"cge-physics-cooker-4",
        "prefracture-artifact-1","LogicalScalar-Q64.64");
    PhysicsCookResult Result=PrefractureArtifactCooker.Cook(RepositoryRoot,Input,Asset!);
    if(!Result.Success){
        Console.Error.WriteLine(Result.Error);
        return 6;
    }
    Console.WriteLine(Result.ArtifactPath);
    return 0;
}

static int BakeWithConsoleCancellation(string[]Args){
    using CancellationTokenSource Cancellation=new();
    ConsoleCancelEventHandler Handler=(_,Event)=>{
        Event.Cancel=true;
        Cancellation.Cancel();
    };
    Console.CancelKeyPress+=Handler;
    try {
        (int ExitCode,string Output)=PhysicsBridgeWorker.Bake(Args,Cancellation.Token);
        if(Output.Length>0){
            if(ExitCode==0){ Console.WriteLine(Output); }
            else { Console.Error.WriteLine(Output); }
        }
        return ExitCode;
    }
    finally {
        Console.CancelKeyPress-=Handler;
    }
}

static int Print((int ExitCode,string Output)Result){
    if(Result.Output.Length>0){
        if(Result.ExitCode==0){ Console.WriteLine(Result.Output); }
        else { Console.Error.WriteLine(Result.Output); }
    }
    return Result.ExitCode;
}

static bool TryLoadMesh(
    string Source,out byte[]Bytes,out LogicalVector3[]Vertices,out int[]Indices,out int ExitCode){
    Bytes=[];
    Vertices=[];
    Indices=[];
    ExitCode=0;
    if(!File.Exists(Source)){
        Console.Error.WriteLine($"Source asset not found: {Source}");
        ExitCode=2;
        return false;
    }
    Bytes=File.ReadAllBytes(Source);
    ContentMeshImportResult Imported=Path.GetExtension(Source).ToLowerInvariant() switch {
        ".gltf" or ".glb"=>ProviderNeutralImport.ImportGltf(Source),
        ".fbx"=>ProviderNeutralImport.ImportFbx(Source),
        _=>ContentMeshImportResult.Failed([new ContentImportDiagnostic(
            ContentImportDiagnosticSeverity.Error,"PhysicsCooker","provider-import",
            "Physics cooking supports .gltf, .glb, and .fbx source assets.")]),
    };
    if(!Imported.Success||Imported.Mesh is not { }Mesh){
        foreach(ContentImportDiagnostic Diagnostic in Imported.Diagnostics){
            Console.Error.WriteLine($"{Diagnostic.Severity}: {Diagnostic.Phase}: {Diagnostic.Message}");
        }
        ExitCode=3;
        return false;
    }
    try {
        Vertices=new LogicalVector3[Mesh.Positions.Length];
        for(int Index=0;Index<Vertices.Length;Index++){
            System.Numerics.Vector3 Position=Mesh.Positions[Index];
            Vertices[Index]=new LogicalVector3(
                LogicalScalar.FromSingle(Position.X),
                LogicalScalar.FromSingle(Position.Y),
                LogicalScalar.FromSingle(Position.Z));
        }
        Indices=new int[Mesh.Indices.Length];
        for(int Index=0;Index<Indices.Length;Index++){ Indices[Index]=checked((int)Mesh.Indices[Index]); }
        return true;
    }
    catch(Exception Exception)when(Exception is ArgumentException or OverflowException){
        Console.Error.WriteLine(Exception.Message);
        ExitCode=4;
        return false;
    }
}

static int Usage(){
    Console.Error.WriteLine("CSharpGameEngine.Physics.Cli cook <sourceAsset> <repositoryRoot> <assetGuid> [static-mesh|convex] [seed]");
    Console.Error.WriteLine("CSharpGameEngine.Physics.Cli prefracture <sourceAsset> <repositoryRoot> <assetGuid> <fragmentCount> [seed] [density] [bondStrengthPerArea]");
    Console.Error.WriteLine("CSharpGameEngine.Physics.Cli inspect-prefracture <artifact.cgefracture> <output.json>");
    Console.Error.WriteLine("CSharpGameEngine.Physics.Cli bake <fullyQualifiedSceneType> <output.cgephyscache> <tickCount> <dependencyHash>");
    Console.Error.WriteLine("CSharpGameEngine.Physics.Cli --serve <pipeName>");
    return 1;
}
