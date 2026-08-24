using System.Text.Json;
using CSharpGameEngine.Runtime.Physics;
using CSharpGameEngine.Runtime.Simulation;

return args.Length==0?Usage():args[0].ToLowerInvariant() switch {
    "cook"=>Cook(args),
    "bake"=>Bake(args),
    _=>Usage(),
};

static int Cook(string[] Args){
    if(Args.Length<4){ return Usage(); }
    string Source=Path.GetFullPath(Args[1]);
    string RepositoryRoot=Path.GetFullPath(Args[2]);
    string AssetGuid=Args[3];
    if(!File.Exists(Source)){
        Console.Error.WriteLine($"Source asset not found: {Source}");
        return 2;
    }
    byte[] Bytes=File.ReadAllBytes(Source);
    string Settings=Args.Length>4?Args[4]:"default";
    string Seed=Args.Length>5?Args[5]:"0";
    PhysicsCookingInput Input=new(Bytes,AssetGuid,"ruf-meters-v1",Settings,"1,1,1",Seed,"cge-physics-cooker-1","physics-schema-1","q32.32");
    string Key=PhysicsCookingKey.Compute(Input);
    string Directory=PhysicsCookingKey.GetArtifactDirectory(RepositoryRoot,AssetGuid,Key);
    System.IO.Directory.CreateDirectory(Directory);
    string Manifest=Path.Combine(Directory,"manifest.json");
    File.WriteAllText(Manifest,JsonSerializer.Serialize(new {
        source=Source,assetGuid=AssetGuid,key=Key,settings=Settings,seed=Seed,
        cooker="cge-physics-cooker-1",schema="physics-schema-1",scalar="q32.32",
    },new JsonSerializerOptions { WriteIndented=true }));
    Console.WriteLine(Manifest);
    return 0;
}

static int Bake(string[] Args){
    if(Args.Length<4){ return Usage(); }
    string Output=Path.GetFullPath(Args[1]);
    string Profile=Args[2];
    string DependencyHash=Args[3];
    if(Profile is not ("Logical128" or "Logical192" or "Logical256")){
        Console.Error.WriteLine("Profile must be Logical128, Logical192, or Logical256.");
        return 2;
    }
    Directory.CreateDirectory(Path.GetDirectoryName(Output)??Environment.CurrentDirectory);
    using FileStream Stream=File.Create(Output);
    SimulationCacheIdentity Identity=new("physics",Profile,60,DependencyHash,PhysicsCacheSerializer.SchemaVersion);
    PhysicsCacheSerializer.Write(Stream,Identity,[]);
    Console.WriteLine(Output);
    return 0;
}

static int Usage(){
    Console.Error.WriteLine("CSharpGameEngine.Physics.Cli cook <sourceAsset> <repositoryRoot> <assetGuid> [settings] [seed]");
    Console.Error.WriteLine("CSharpGameEngine.Physics.Cli bake <output.cgephyscache> <Logical128|Logical192|Logical256> <dependencyHash>");
    return 1;
}
