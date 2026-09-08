using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

internal readonly record struct ScenePhysicsFractureBond(
    Vector3 First,Vector3 Second,float Strength);
internal readonly record struct PrefractureDebugLoadResult(
    IReadOnlyList<ScenePhysicsFractureBond>Bonds,string?Diagnostic);

internal static class PrefractureDebugService {
    private const string DestructibleTypeName="global::CSharpGameEngine.Runtime.Physics.Destructible";
    private const int MaximumBonds=2048;
    private static readonly TimeSpan InspectTimeout=new(0,2,0);

    internal static PrefractureDebugLoadResult Load(
        SceneModel Model,string RepoRoot,CancellationToken CancellationToken){
        if(Model?.Root is null){ return new PrefractureDebugLoadResult([],null); }
        string ProjectRoot=PhysicsSceneBakeService.FindProjectRoot(RepoRoot,Model.FilePath);
        List<(SceneNode Node,string ArtifactPath)>References=[];
        List<string>Diagnostics=[];
        foreach(SceneNode Node in Model.EnumerateNodes()){
            string?Relative=FindFractureAsset(Node);
            if(Relative is null){ continue; }
            string ArtifactPath=Path.IsPathRooted(Relative)
                ?Path.GetFullPath(Relative)
                :Path.GetFullPath(Path.Combine(ProjectRoot,"Assets",Relative));
            if(!File.Exists(ArtifactPath)){
                Diagnostics.Add($"Prefracture artifact not found: {ArtifactPath}");
                continue;
            }
            References.Add((Node,ArtifactPath));
        }
        if(References.Count==0){
            return new PrefractureDebugLoadResult([],Diagnostics.Count==0?null:string.Join(Environment.NewLine,Diagnostics));
        }
        CancellationToken.ThrowIfCancellationRequested();
        string DependencyHash=PhysicsSceneBakeService.ComputeDependencyHash(RepoRoot);
        string Host=PhysicsSceneBakeService.PrepareExactHost(RepoRoot,DependencyHash,CancellationToken);
        Dictionary<string,PrefractureInspectionDocument>Documents=new(StringComparer.OrdinalIgnoreCase);
        foreach(string ArtifactPath in References.Select(static Reference=>Reference.ArtifactPath).Distinct(StringComparer.OrdinalIgnoreCase)){
            CancellationToken.ThrowIfCancellationRequested();
            string Temporary=Path.Combine(Path.GetTempPath(),$"cge-prefracture-{Guid.NewGuid():N}.json");
            try {
                ExternalBridgeProcess.RunExecutableCommandProducingFile(
                    Host,$"tools/CSharpGameEngine.Physics.Cli/{DependencyHash}",
                    ["inspect-prefracture",ArtifactPath,Temporary],Temporary,RepoRoot,InspectTimeout,
                    UseResidentServer:true,CancellationToken);
                PrefractureInspectionDocument?Document=JsonSerializer.Deserialize<PrefractureInspectionDocument>(File.ReadAllText(Temporary));
                string Error="document is missing";
                if(Document is null||!TryValidate(Document,out Error)){
                    Diagnostics.Add($"Invalid prefracture inspection for {ArtifactPath}: {Error}");
                    continue;
                }
                Documents.Add(ArtifactPath,Document);
            }
            catch(Exception Exception)when(Exception is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or TimeoutException){
                Diagnostics.Add($"Prefracture inspection failed for {ArtifactPath}: {Exception.Message}");
            }
            finally {
                try { if(File.Exists(Temporary)){ File.Delete(Temporary); } }
                catch(Exception Exception)when(Exception is IOException or UnauthorizedAccessException){
                    Diagnostics.Add($"Failed to remove prefracture inspection output: {Exception.Message}");
                }
            }
        }
        Dictionary<NodePath,Matrix4x4>Worlds=BuildWorldTransforms(Model.Root);
        List<ScenePhysicsFractureBond>Bonds=[];
        foreach((SceneNode Node,string ArtifactPath)in References){
            if(!Documents.TryGetValue(ArtifactPath,out PrefractureInspectionDocument?Document)
                ||!Worlds.TryGetValue(Node.Path,out Matrix4x4 World)){ continue; }
            foreach(PrefractureInspectionBond Bond in Document.Bonds){
                if(Bonds.Count>=MaximumBonds){
                    Diagnostics.Add($"Prefracture debug overlay was capped at {MaximumBonds} bonds.");
                    return new PrefractureDebugLoadResult(Bonds,string.Join(Environment.NewLine,Diagnostics));
                }
                Vector3 First=Vector3.Transform(Document.Fragments[Bond.First].Position,World);
                Vector3 Second=Vector3.Transform(Document.Fragments[Bond.Second].Position,World);
                Bonds.Add(new ScenePhysicsFractureBond(First,Second,Bond.Strength));
            }
        }
        return new PrefractureDebugLoadResult(
            Bonds,Diagnostics.Count==0?null:string.Join(Environment.NewLine,Diagnostics));
    }

    internal static string?FindFractureAsset(SceneNode Node){
        SceneComponent?Component=Node.FindComponent(DestructibleTypeName);
        SceneComponentMember?Member=Component?.FindMember("FractureAsset");
        return Member?.Value.ConstantValue is string Value&&!string.IsNullOrWhiteSpace(Value)
            ?Value
            :null;
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

    private static bool TryValidate(PrefractureInspectionDocument Document,out string Error){
        Error=string.Empty;
        if(string.IsNullOrWhiteSpace(Document.SourceHash)||Document.Fragments.Length is <2 or >256){ Error="fragment set is invalid";return false; }
        for(int Index=0;Index<Document.Fragments.Length;Index++){
            PrefractureInspectionFragment Fragment=Document.Fragments[Index];
            if(Fragment.Index!=Index||!IsFinite(Fragment.Position)){ Error="fragment center is invalid";return false; }
        }
        foreach(PrefractureInspectionBond Bond in Document.Bonds){
            if(Bond.First<0||Bond.Second<=Bond.First||Bond.Second>=Document.Fragments.Length
                ||!IsFinite(Bond.Strength)||Bond.Strength<=0f){ Error="bond is invalid";return false; }
        }
        return true;
    }

    private static bool IsFinite(Vector3 Value)=>IsFinite(Value.X)&&IsFinite(Value.Y)&&IsFinite(Value.Z);
    private static bool IsFinite(float Value)=>!float.IsNaN(Value)&&!float.IsInfinity(Value);
    private sealed class PrefractureInspectionDocument { public string SourceHash { get;set; }=string.Empty;public PrefractureInspectionFragment[]Fragments { get;set; }=[];public PrefractureInspectionBond[]Bonds { get;set; }=[]; }
    private sealed class PrefractureInspectionFragment { public int Index { get;set; }public float R { get;set; }public float U { get;set; }public float F { get;set; }public Vector3 Position=>new(R,U,F); }
    private sealed class PrefractureInspectionBond { public int First { get;set; }public int Second { get;set; }public float Strength { get;set; } }
}
