using CSharpGameEngine.Numerics;
using CSharpGameEngine.Runtime.Physics;
using System.Globalization;
using System.Text.Json;

namespace CSharpGameEngine.Physics.Cli;

internal static class PhysicsBridgeWorker {
    internal static(int ExitCode,string Output)Run(string[]Arguments,CancellationToken CancellationToken){
        return Arguments switch {
            ["bake",_,_,_,_]=>Bake(Arguments,CancellationToken),
            ["inspect-prefracture",_,_]=>InspectPrefracture(Arguments,CancellationToken),
            _=>(1,"Physics resident host accepts Bake and prefracture inspection requests."),
        };
    }

    internal static(int ExitCode,string Output)Bake(string[]Arguments,CancellationToken CancellationToken){
        if(Arguments.Length!=5){ return(1,"Physics bake requires exactly four arguments."); }
        string SceneName=Arguments[1];
        string Output;
        try { Output=Path.GetFullPath(Arguments[2]); }
        catch(Exception Exception)when(Exception is ArgumentException or NotSupportedException){ return(2,Exception.Message); }
        if(!int.TryParse(Arguments[3],NumberStyles.Integer,CultureInfo.InvariantCulture,out int TickCount)
            ||TickCount is <1 or >PhysicsCacheSerializer.MaximumFrameCount){
            return(2,"Tick count is outside the supported physics cache range.");
        }
        string DependencyHash=Arguments[4];
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
                if(!CSharpGameEngine.Generated.GeneratedSceneBakeHost.TryBake(
                    SceneName,TickCount,DependencyHash,Stream,CancellationToken,out string Error)){
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
            PrefractureInspectionDocument Document=new(){
                SourceHash=Asset.SourceHash,
                Fragments=[..Asset.Fragments.Select(static Fragment=>new PrefractureInspectionFragment {
                    Index=Fragment.Index,
                    R=ToSingle(Fragment.LocalCenter.R),U=ToSingle(Fragment.LocalCenter.U),F=ToSingle(Fragment.LocalCenter.F),
                })],
                Bonds=[..Asset.Bonds.Select(static Bond=>new PrefractureInspectionBond {
                    First=Bond.FirstFragment,Second=Bond.SecondFragment,Strength=ToSingle(Bond.Strength),
                })],
            };
            string?DirectoryPath=Path.GetDirectoryName(OutputPath);
            if(!string.IsNullOrEmpty(DirectoryPath)){ Directory.CreateDirectory(DirectoryPath); }
            File.WriteAllText(OutputPath,JsonSerializer.Serialize(Document));
            return(0,OutputPath);
        }
        catch(Exception Exception)when(Exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException){
            return(3,Exception.Message);
        }
    }

    private static float ToSingle(LogicalScalar Value)=>Value.TryToSingle(out float Result)
        ?Result:throw new InvalidDataException("Prefracture debug value exceeds the float adapter contract.");

    private sealed class PrefractureInspectionDocument {
        public string SourceHash { get;set; }=string.Empty;
        public PrefractureInspectionFragment[]Fragments { get;set; }=[];
        public PrefractureInspectionBond[]Bonds { get;set; }=[];
    }
    private sealed class PrefractureInspectionFragment { public int Index { get;set; }public float R { get;set; }public float U { get;set; }public float F { get;set; } }
    private sealed class PrefractureInspectionBond { public int First { get;set; }public int Second { get;set; }public float Strength { get;set; } }
}
