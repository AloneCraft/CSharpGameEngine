using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>A line of the fracture overlay: a bond between two fragment centres, or (<see cref="Outline"/>) an edge of a fragment.</summary>
internal readonly record struct ScenePhysicsFractureBond(
    Vector3 First,Vector3 Second,float Strength,bool Outline=false);
internal readonly record struct PrefractureDebugLoadResult(
    IReadOnlyList<ScenePhysicsFractureBond>Bonds,string?Diagnostic);
/// <summary>
/// What the Fracture preview cuts for one Destructible (ADR-0078/M0113, UE's Fracture mode): its collider box, and the count, seed, clusters and material
/// numbers the build's Cook module would use. A member the scene leaves out has the component's default.
/// </summary>
internal readonly record struct FracturePreviewRequest(
    string Name,Vector3 HalfExtents,Vector3 Center,int FragmentCount,int Seed,int ClusterCount,float Density,float BondStrength);

/// <summary>
/// The Scene view's fracture overlay and preview (ADR-0078/M0113). For each <c>CSharpGameEngine.Destruction.Destructible</c> of the scene, the box of its
/// <c>Collider</c> is cut out of process (<c>Physics.Cli preview-prefracture</c>, the same box and cooker as the build's Cook module) with the node's
/// FragmentCount, Seed and ClusterCount, so a changed count or seed in the Inspector shows its fragments' outlines and bonds at once, before a build. A
/// destructible cut from a <c>SourceMesh</c> shows the cooked asset of the last build (<c>bin\**\Assets\Destruction\&lt;name&gt;.cgefracture</c>) instead.
/// </summary>
internal static class PrefractureDebugService {
    private const string DestructibleTypeName="global::CSharpGameEngine.Destruction.Destructible";
    private const string ColliderTypeName="global::CSharpGameEngine.Runtime.Physics.Collider";
    private const int MaximumLines=4096;
    private static readonly TimeSpan InspectTimeout=new(0,2,0);
    // The component's defaults (plugins/CSharpGameEngine.Destruction/Runtime/DestructionDefaults.cs); the tests compare the two.
    internal const int DefaultFragmentCount=16;
    internal const int DefaultSeed=0;
    internal const int DefaultClusterCount=0;
    internal const float DefaultDensity=500f;
    internal const float DefaultBondStrength=2000f;

    internal static PrefractureDebugLoadResult Load(
        SceneModel Model,string RepoRoot,CancellationToken CancellationToken){
        if(Model?.Root is null){ return new PrefractureDebugLoadResult([],null); }
        string ProjectRoot=PhysicsSceneBakeService.FindProjectRoot(RepoRoot,Model.FilePath);
        List<string>Diagnostics=[];
        List<(SceneNode Node,string[]Arguments)>Requests=[];
        foreach(SceneNode Node in Model.EnumerateNodes()){
            if(Node.FindComponent(DestructibleTypeName) is null){ continue; }
            if(FindFractureAsset(Node) is { }Name&&ReadSourceMesh(Node) is { Length:>0 }){
                string?Cooked=FindCookedAsset(ProjectRoot,Name);
                if(Cooked is null){
                    Diagnostics.Add($"The cooked fracture '{Name}' was not found under {ProjectRoot}\\bin (build the game to cook its SourceMesh).");
                    continue;
                }
                Requests.Add((Node,["inspect-prefracture",Cooked,string.Empty]));
                continue;
            }
            if(!TryReadPreviewRequest(Node,out FracturePreviewRequest Request,out string Problem)){
                Diagnostics.Add(Problem);
                continue;
            }
            Requests.Add((Node,PreviewArguments(Request,string.Empty)));
        }
        if(Requests.Count==0){
            return new PrefractureDebugLoadResult([],Diagnostics.Count==0?null:string.Join(Environment.NewLine,Diagnostics));
        }
        CancellationToken.ThrowIfCancellationRequested();
        // The cut runs in the physics host, which needs no game (M0096 G).
        string HostHash=PhysicsSceneBakeService.ComputeHostHash(RepoRoot);
        string Host=PhysicsSceneBakeService.PrepareExactHost(RepoRoot,HostHash,CancellationToken);
        Dictionary<NodePath,Matrix4x4>Worlds=BuildWorldTransforms(Model.Root);
        List<ScenePhysicsFractureBond>Lines=[];
        foreach((SceneNode Node,string[]Template)in Requests){
            CancellationToken.ThrowIfCancellationRequested();
            string Temporary=Path.Combine(Path.GetTempPath(),$"cge-prefracture-{Guid.NewGuid():N}.json");
            string[]Arguments=[..Template];
            Arguments[Arguments[0]=="inspect-prefracture"?2:7]=Temporary;
            try {
                ExternalBridgeProcess.RunExecutableCommandProducingFile(
                    Host,$"tools/CSharpGameEngine.Physics.Cli/{HostHash}",
                    Arguments,Temporary,RepoRoot,InspectTimeout,
                    UseResidentServer:true,CancellationToken);
                PrefractureInspectionDocument?Document=JsonSerializer.Deserialize<PrefractureInspectionDocument>(File.ReadAllText(Temporary));
                string Error="document is missing";
                if(Document is null||!TryValidate(Document,out Error)){
                    Diagnostics.Add($"Invalid fracture preview for {Node.DisplayName}: {Error}");
                    continue;
                }
                if(!Worlds.TryGetValue(Node.Path,out Matrix4x4 World)){ continue; }
                if(!AppendLines(Lines,Document,World)){
                    Diagnostics.Add($"The fracture overlay was capped at {MaximumLines} lines.");
                    break;
                }
            }
            catch(Exception Exception)when(Exception is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or TimeoutException){
                Diagnostics.Add($"The fracture preview of {Node.DisplayName} failed: {Exception.Message}");
            }
            finally {
                try { if(File.Exists(Temporary)){ File.Delete(Temporary); } }
                catch(Exception Exception)when(Exception is IOException or UnauthorizedAccessException){
                    Diagnostics.Add($"Failed to remove the fracture preview output: {Exception.Message}");
                }
            }
        }
        return new PrefractureDebugLoadResult(Lines,Diagnostics.Count==0?null:string.Join(Environment.NewLine,Diagnostics));
    }

    /// <summary>The bonds (centre to centre) and fragment outlines of one inspection document, placed by the node's world matrix; false when the cap is reached.</summary>
    internal static bool AppendLines(List<ScenePhysicsFractureBond>Lines,PrefractureInspectionDocument Document,Matrix4x4 World){
        foreach(PrefractureInspectionBond Bond in Document.Bonds){
            if(Lines.Count>=MaximumLines){ return false; }
            Lines.Add(new ScenePhysicsFractureBond(
                Vector3.Transform(Document.Fragments[Bond.First].Position,World),
                Vector3.Transform(Document.Fragments[Bond.Second].Position,World),Bond.Strength));
        }
        foreach(PrefractureInspectionFragment Fragment in Document.Fragments){
            for(int Index=0;Index+5<Fragment.Edges.Length;Index+=6){
                if(Lines.Count>=MaximumLines){ return false; }
                Lines.Add(new ScenePhysicsFractureBond(
                    Vector3.Transform(new Vector3(Fragment.Edges[Index],Fragment.Edges[Index+1],Fragment.Edges[Index+2]),World),
                    Vector3.Transform(new Vector3(Fragment.Edges[Index+3],Fragment.Edges[Index+4],Fragment.Edges[Index+5]),World),0f,Outline:true));
            }
        }
        return true;
    }

    internal static string?FindFractureAsset(SceneNode Node){
        SceneComponent?Component=Node.FindComponent(DestructibleTypeName);
        SceneComponentMember?Member=Component?.FindMember("FractureAsset");
        return Member?.Value.ConstantValue is string Value&&!string.IsNullOrWhiteSpace(Value)
            ?Value
            :null;
    }

    private static string?ReadSourceMesh(SceneNode Node)=>
        Node.FindComponent(DestructibleTypeName)?.FindMember("SourceMesh")?.Value.ConstantValue as string;

    /// <summary>
    /// Reads what the preview cuts: the Destructible's name, count, seed, clusters, density and strength, and the node's box <c>Collider</c>. False with the
    /// problem when the node has no box collider or a value is out of the cook's range (2-256 fragments, positive sizes and numbers).
    /// </summary>
    internal static bool TryReadPreviewRequest(SceneNode Node,out FracturePreviewRequest Request,out string Problem){
        Request=default;
        Problem=string.Empty;
        SceneComponent?Destructible=Node.FindComponent(DestructibleTypeName);
        if(Destructible is null){ Problem=$"{Node.DisplayName} has no Destructible.";return false; }
        string Name=FindFractureAsset(Node)??string.Empty;
        int Count=ReadInt(Destructible,"FragmentCount",DefaultFragmentCount);
        int Seed=ReadInt(Destructible,"Seed",DefaultSeed);
        int Clusters=ReadInt(Destructible,"ClusterCount",DefaultClusterCount);
        float Density=ReadFloat(Destructible,"Density",DefaultDensity);
        float Strength=ReadFloat(Destructible,"BondStrength",DefaultBondStrength);
        SceneComponent?Collider=Node.FindComponent(ColliderTypeName);
        if(Collider is null){ Problem=$"The Destructible of {Node.DisplayName} has no Collider to cut.";return false; }
        if(Collider.FindMember("Shape")?.Value.ReferencedSymbol?.Name is { }Shape&&Shape!="Box"){
            Problem=$"The Destructible of {Node.DisplayName} has a {Shape} collider; the preview cuts a box.";
            return false;
        }
        Vector3 Half=ReadVector(Collider.FindMember("HalfExtents")?.Value,new Vector3(0.5f));
        Vector3 Center=ReadVector(Collider.FindMember("Center")?.Value,Vector3.Zero);
        if(Count is <2 or >256){ Problem=$"The Destructible of {Node.DisplayName} asks for {Count} fragments; a fracture has 2 to 256.";return false; }
        if(!(Half.X>0f&&Half.Y>0f&&Half.Z>0f)||!IsFinite(Half)||!IsFinite(Center)){ Problem=$"The Collider of {Node.DisplayName} has no volume.";return false; }
        if(!(Density>0f)||!(Strength>0f)||Clusters<0){ Problem=$"The Destructible of {Node.DisplayName} needs a positive Density and BondStrength.";return false; }
        Request=new FracturePreviewRequest(Name,Half,Center,Count,Seed,Clusters,Density,Strength);
        return true;
    }

    /// <summary>The <c>preview-prefracture</c> command line of a request (numbers in the invariant culture, round-trip float text).</summary>
    internal static string[]PreviewArguments(in FracturePreviewRequest Request,string OutputPath)=>[
        "preview-prefracture",
        Number(Request.HalfExtents.X),Number(Request.HalfExtents.Y),Number(Request.HalfExtents.Z),
        Request.FragmentCount.ToString(CultureInfo.InvariantCulture),Request.Seed.ToString(CultureInfo.InvariantCulture),
        Request.ClusterCount.ToString(CultureInfo.InvariantCulture),OutputPath,
        Number(Request.Center.X),Number(Request.Center.Y),Number(Request.Center.Z),
        Number(Request.Density),Number(Request.BondStrength),
    ];

    private static string Number(float Value)=>Value.ToString("R",CultureInfo.InvariantCulture);

    /// <summary>The newest cooked asset of that name under the project's build output.</summary>
    internal static string?FindCookedAsset(string ProjectRoot,string Name){
        string Bin=Path.Combine(ProjectRoot,"bin");
        if(!Directory.Exists(Bin)){ return null; }
        return Directory.EnumerateFiles(Bin,Name+".cgefracture",SearchOption.AllDirectories)
            .Where(static Path=>Path.Replace('/','\\').Contains("\\Assets\\Destruction\\"))
            .OrderByDescending(File.GetLastWriteTimeUtc).ThenBy(static Path=>Path,StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }

    private static int ReadInt(SceneComponent Component,string Name,int Default)=>
        Component.FindMember(Name)?.Value.ConstantValue is { }Value&&TryInteger(Value,out long Number)&&Number is >=int.MinValue and <=int.MaxValue
            ?(int)Number:Default;

    private static float ReadFloat(SceneComponent Component,string Name,float Default)=>
        ReadNumber(Component.FindMember(Name)?.Value,out float Value)?Value:Default;

    /// <summary>A number member: a literal, or a value whose source text is a number (a scene's <c>LogicalScalar</c>).</summary>
    private static bool ReadNumber(SceneValue?Value,out float Result){
        Result=0f;
        if(Value is null){ return false; }
        if(Value.ConstantValue is { }Constant&&TryNumber(Constant,out float Number)){ Result=Number;return true; }
        foreach(SceneValue Argument in Value.Arguments){ return ReadNumber(Argument,out Result); }
        string Text=Value.SourceExpressionText.Trim().TrimEnd('f','F','m','M','d','D');
        return float.TryParse(Text,NumberStyles.Float,CultureInfo.InvariantCulture,out Result)&&IsFinite(Result);
    }

    /// <summary>An integral constant exactly (a seed can be past float's 24 bits), or a whole-numbered floating constant.</summary>
    private static bool TryInteger(object Value,out long Number){
        Number=0;
        if(Value is sbyte or byte or short or ushort or int or uint or long or ulong){
            try { Number=Convert.ToInt64(Value,CultureInfo.InvariantCulture);return true; }
            catch(OverflowException){ return false; }
        }
        if(!TryNumber(Value,out float Single)||Single>int.MaxValue||Single<int.MinValue||(long)Single!=Single){ return false; }
        Number=(long)Single;
        return true;
    }

    /// <summary>A numeric constant as a finite float (the preview's boundary type).</summary>
    private static bool TryNumber(object Value,out float Number){
        Number=0f;
        if(Value is string or bool or char){ return false; }
        try { Number=Convert.ToSingle(Value,CultureInfo.InvariantCulture); }
        catch(Exception Exception)when(Exception is InvalidCastException or FormatException or OverflowException){ return false; }
        return IsFinite(Number);
    }

    /// <summary>A vector member: <c>{ R, U, F }</c> members or three positional arguments; missing parts keep the default's.</summary>
    private static Vector3 ReadVector(SceneValue?Value,Vector3 Default){
        if(Value is null){ return Default; }
        Vector3 Result=Default;
        if(Value.Arguments.Length==3){
            ReadPart(Value.Arguments[0],ref Result.X);
            ReadPart(Value.Arguments[1],ref Result.Y);
            ReadPart(Value.Arguments[2],ref Result.Z);
        }
        foreach(SceneComponentMember Member in Value.Members){
            switch(Member.MemberName){
                case "R": ReadPart(Member.Value,ref Result.X);break;
                case "U": ReadPart(Member.Value,ref Result.Y);break;
                case "F": ReadPart(Member.Value,ref Result.Z);break;
            }
        }
        return Result;
        static void ReadPart(SceneValue Part,ref float Target){
            if(ReadNumber(Part,out float Number)){ Target=Number; }
        }
    }

    private static Dictionary<NodePath,Matrix4x4>BuildWorldTransforms(SceneNode Root){
        Dictionary<NodePath,Matrix4x4>Worlds=[];
        Append(Root,Matrix4x4.Identity);
        return Worlds;
        void Append(SceneNode Node,Matrix4x4 Parent){
            Matrix4x4 World=(SceneTransformBinding.TryRead(Node,out SceneTransformValue Local)
                ?SceneTransformMath.ToMatrix(Local):Matrix4x4.Identity)*Parent;
            Worlds.Add(Node.Path,World);
            foreach(SceneNode Child in Node.Children){ Append(Child,World); }
        }
    }

    internal static bool TryValidate(PrefractureInspectionDocument Document,out string Error){
        Error=string.Empty;
        if(string.IsNullOrWhiteSpace(Document.SourceHash)||Document.Fragments.Length is <2 or >256){ Error="fragment set is invalid";return false; }
        for(int Index=0;Index<Document.Fragments.Length;Index++){
            PrefractureInspectionFragment Fragment=Document.Fragments[Index];
            if(Fragment.Index!=Index||!IsFinite(Fragment.Position)){ Error="fragment center is invalid";return false; }
            if(Fragment.Edges.Length%6!=0||Fragment.Edges.Any(static Value=>!IsFinite(Value))){ Error="fragment outline is invalid";return false; }
        }
        foreach(PrefractureInspectionBond Bond in Document.Bonds){
            if(Bond.First<0||Bond.Second<=Bond.First||Bond.Second>=Document.Fragments.Length
                ||!IsFinite(Bond.Strength)||Bond.Strength<=0f){ Error="bond is invalid";return false; }
        }
        return true;
    }

    private static bool IsFinite(Vector3 Value)=>IsFinite(Value.X)&&IsFinite(Value.Y)&&IsFinite(Value.Z);
    private static bool IsFinite(float Value)=>!float.IsNaN(Value)&&!float.IsInfinity(Value);
    internal sealed class PrefractureInspectionDocument { public string SourceHash { get;set; }=string.Empty;public int ClusterCount { get;set; }public PrefractureInspectionFragment[]Fragments { get;set; }=[];public PrefractureInspectionBond[]Bonds { get;set; }=[]; }
    internal sealed class PrefractureInspectionFragment { public int Index { get;set; }public float R { get;set; }public float U { get;set; }public float F { get;set; }public int Cluster { get;set; }public float[]Edges { get;set; }=[];public Vector3 Position=>new(R,U,F); }
    internal sealed class PrefractureInspectionBond { public int First { get;set; }public int Second { get;set; }public float Strength { get;set; }public float Area { get;set; } }
}
