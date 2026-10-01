using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.VSExtension;

/// <summary>One scene to bake. <see cref="ProjectFile"/> is the scene's game, whose build the Bake runs (M0096 G).</summary>
internal readonly record struct PhysicsSceneBakeRequest(
    string SceneName,string OutputPath,string DependencyHash,int TickCount,string ProjectFile);
internal readonly record struct PhysicsSceneBakeBatchResult(
    bool Succeeded,int CompletedCount,string Message,IReadOnlyList<string>OutputPaths,
    AuthoringOperationSnapshot Operation);

/// <summary>
/// Bakes scenes through the exact Physics host. M0096 G: the host is built from the engine alone and the scene's own
/// game is built separately; the host loads that build (<c>bake --assembly</c>) and runs the game's generated Bake host.
/// </summary>
internal static class PhysicsSceneBakeService {
    internal const int DefaultTickCount=600;
    private const string BridgeProjectRelativePath="tools/CSharpGameEngine.Physics.Cli";
    private static readonly TimeSpan BuildTimeout=new(0,5,0);
    private static readonly TimeSpan BakeTimeout=new(0,15,0);

    internal static IReadOnlyList<PhysicsSceneBakeRequest>CreateRequests(
        string RepoRoot,IReadOnlyList<ProjectSceneCatalogEntry>Entries,int TickCount=DefaultTickCount){
        if(string.IsNullOrWhiteSpace(RepoRoot)){ throw new ArgumentException(nameof(RepoRoot)); }
        if(Entries is null||Entries.Count==0){ throw new ArgumentException("At least one Scene is required.",nameof(Entries)); }
        if(TickCount<1){ throw new ArgumentOutOfRangeException(nameof(TickCount)); }
        string CanonicalRoot=Path.GetFullPath(RepoRoot).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        Dictionary<string,string>HashByProject=new(StringComparer.OrdinalIgnoreCase);
        List<PhysicsSceneBakeRequest>Requests=[];
        HashSet<string>SceneNames=new(StringComparer.Ordinal);
        foreach(ProjectSceneCatalogEntry Entry in Entries){
            string SceneName=Entry.Key.ClassFullyQualifiedName.Replace("global::",string.Empty);
            if(!SceneNames.Add(SceneName)){ throw new InvalidOperationException($"Duplicate Scene Bake request: {SceneName}"); }
            string ProjectFile=FindProjectFile(CanonicalRoot,Entry.FilePath);
            string ProjectRoot=Path.GetDirectoryName(ProjectFile)!;
            if(!HashByProject.TryGetValue(ProjectRoot,out string?DependencyHash)){
                DependencyHash=ComputeDependencyHash(CanonicalRoot,ProjectRoot);
                HashByProject.Add(ProjectRoot,DependencyHash);
            }
            string OutputDirectory=Path.Combine(ProjectRoot,"Assets","Physics","Bakes");
            string OutputName=Sanitize(SceneName)+"-"+HashText(SceneName).Substring(0,10)+".cgephyscache";
            Requests.Add(new PhysicsSceneBakeRequest(
                SceneName,Path.Combine(OutputDirectory,OutputName),DependencyHash,TickCount,ProjectFile));
        }
        return Requests;
    }

    internal static PhysicsSceneBakeBatchResult Bake(
        string RepoRoot,IReadOnlyList<ProjectSceneCatalogEntry>Entries,Guid RequestId,
        Action<AuthoringHostProgress>?ReportProgress,CancellationToken CancellationToken,
        Func<string,string,CancellationToken,string>?PrepareHost=null,
        Func<string,PhysicsSceneBakeRequest,CancellationToken,string>?PrepareGame=null,
        Action<string,string,PhysicsSceneBakeRequest,CancellationToken>?BakeRequest=null){
        if(RequestId==Guid.Empty){ throw new ArgumentException(nameof(RequestId)); }
        AuthoringOperationSnapshot Operation=AuthoringOperationSnapshot.Start(
            RequestId,AuthoringHostOperation.Bake,"Preparing","Preparing Physics Bake.");
        AuthoringHostProgress?LastProgress=null;
        ulong Sequence=0;
        List<string>Outputs=[];
        string HostHash=string.Empty;
        PrepareHost??=PrepareExactHost;
        PrepareGame??=static(Root,Request,Token)=>PrepareGameBuild(Root,Request.ProjectFile,Request.DependencyHash,Token);
        BakeRequest??=(ExactHost,GameAssembly,Request,Token)=>
            ExternalBridgeProcess.RunExecutableCommandProducingFile(
                ExactHost,$"{BridgeProjectRelativePath}/{HostHash}",
                ["bake","--assembly",GameAssembly,Request.SceneName,Request.OutputPath,
                    Request.TickCount.ToString(CultureInfo.InvariantCulture),Request.DependencyHash],
                Request.OutputPath,RepoRoot,BakeTimeout,UseResidentServer:true,Token);

        void Report(float Fraction,string Phase,string Summary){
            AuthoringHostProgress Progress=new(){
                RequestId=RequestId,Sequence=++Sequence,Fraction=Fraction,Phase=Phase,
            };
            LastProgress=Progress;
            Operation=Operation.ApplyProgress(Progress,Summary);
            ReportProgress?.Invoke(Progress);
        }

        try {
            Report(0.02f,"Preparing","Resolving Scene Bake inputs.");
            IReadOnlyList<PhysicsSceneBakeRequest>Requests=CreateRequests(RepoRoot,Entries);
            CancellationToken.ThrowIfCancellationRequested();
            Report(0.08f,"Host","Preparing the exact Physics Bake Host.");
            HostHash=ComputeHostHash(RepoRoot);
            string ExactHost=PrepareHost(RepoRoot,HostHash,CancellationToken);
            Report(0.15f,"Host","Exact Physics Bake Host is ready.");
            Dictionary<string,string>GameByProject=new(StringComparer.OrdinalIgnoreCase);
            for(int Index=0;Index<Requests.Count;Index++){
                PhysicsSceneBakeRequest Request=Requests[Index];
                CancellationToken.ThrowIfCancellationRequested();
                float Started=0.15f+(0.8f*Index/Requests.Count);
                if(!GameByProject.TryGetValue(Request.ProjectFile,out string?GameAssembly)){
                    Report(Started,"Build",$"Building {Path.GetFileNameWithoutExtension(Request.ProjectFile)} for the Bake.");
                    GameAssembly=PrepareGame(RepoRoot,Request,CancellationToken);
                    GameByProject.Add(Request.ProjectFile,GameAssembly);
                }
                Report(Started,"Bake",$"Baking {Request.SceneName}.");
                BakeRequest(ExactHost,GameAssembly,Request,CancellationToken);
                Outputs.Add(Request.OutputPath);
                float Completed=0.15f+(0.8f*(Index+1)/Requests.Count);
                Report(Completed,"Bake",$"Baked {Request.SceneName}.");
            }
            LastProgress=new AuthoringHostProgress {
                RequestId=RequestId,Sequence=++Sequence,Fraction=1f,Phase="Completed",
            };
            Operation=Operation.Complete(new AuthoringHostResult {
                RequestId=RequestId,State=AuthoringHostTerminalState.Succeeded,ExitCode=0,
                Output=$"Baked {Outputs.Count.ToString(CultureInfo.InvariantCulture)} Scene(s).",
                Progress=LastProgress,
            });
            return new PhysicsSceneBakeBatchResult(
                Operation.State==AuthoringOperationUiState.Succeeded,Outputs.Count,
                Operation.Summary,Outputs,Operation);
        }
        catch(OperationCanceledException){
            Operation=Operation.Complete(FailureResult(
                RequestId,AuthoringHostTerminalState.Cancelled,"HOST_CANCELLED",
                "Physics Bake was cancelled.",LastProgress));
            return new PhysicsSceneBakeBatchResult(
                false,Outputs.Count,Operation.Summary,Outputs,Operation);
        }
        catch(Exception Exception)when(Exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or TimeoutException){
            if(Exception is AuthoringHostOperationException HostFailure){
                AuthoringHostTerminalState State=HostFailure.Result.State
                    ==AuthoringHostTerminalState.Cancelled
                    ?AuthoringHostTerminalState.Cancelled
                    :AuthoringHostTerminalState.Failed;
                Operation=Operation.Complete(new AuthoringHostResult {
                    RequestId=RequestId,State=State,ExitCode=Math.Max(1,HostFailure.Result.ExitCode),
                    Output=HostFailure.Result.Output,Progress=LastProgress,
                    Diagnostics=HostFailure.Result.Diagnostics,
                });
                return new PhysicsSceneBakeBatchResult(
                    false,Outputs.Count,Operation.Summary,Outputs,Operation);
            }
            string Code=Exception switch {
                TimeoutException=>"BAKE_TIMED_OUT",
                IOException=>"BAKE_IO_FAILED",
                UnauthorizedAccessException=>"BAKE_ACCESS_DENIED",
                _=>"BAKE_FAILED",
            };
            Operation=Operation.Complete(FailureResult(
                RequestId,AuthoringHostTerminalState.Failed,Code,Exception.Message,LastProgress));
            return new PhysicsSceneBakeBatchResult(
                false,Outputs.Count,Operation.Summary,Outputs,Operation);
        }
    }

    private static AuthoringHostResult FailureResult(
        Guid RequestId,AuthoringHostTerminalState State,string Code,string Message,
        AuthoringHostProgress?Progress)=>new(){
        RequestId=RequestId,State=State,ExitCode=1,Output=Message,Progress=Progress,
        Diagnostics=[new AuthoringHostDiagnostic {
            Severity=AuthoringHostDiagnosticSeverity.Error,Code=Code,Message=Message,
        }],
    };

    /// <summary>
    /// The identity of the exact Physics host: the engine sources and the host's own tools. No game is part of it,
    /// because the host loads the game build it is given (M0096 G).
    /// </summary>
    internal static string ComputeHostHash(string RepoRoot)=>HashTree(RepoRoot,static Relative=>
        (IsUnder(Relative,"src")||IsUnder(Relative,Path.Combine("tools","CSharpGameEngine.Physics.Cli"))
            ||IsUnder(Relative,Path.Combine("tools","CSharpGameEngine.Bridge.Hosting"))||IsRootFile(Relative))
        &&IsSourceFile(Relative));

    /// <summary>
    /// The identity of a scene's game build, which the Bake writes into the cache: the engine sources, the game build
    /// rules, and the scene's own project with all of its assets except the Bake outputs.
    /// </summary>
    internal static string ComputeDependencyHash(string RepoRoot,string ProjectRoot){
        string CanonicalRoot=Path.GetFullPath(RepoRoot).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        string Project=Path.GetFullPath(ProjectRoot).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        if(!string.Equals(Project,CanonicalRoot,StringComparison.OrdinalIgnoreCase)
            &&!Project.StartsWith(CanonicalRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)){
            throw new InvalidOperationException($"The game project is outside the repository: {ProjectRoot}");
        }
        string ProjectRelative=Project.Substring(CanonicalRoot.Length).TrimStart(Path.DirectorySeparatorChar);
        string Assets=Path.Combine(ProjectRelative,"Assets");
        string Bakes=Path.Combine(Assets,"Physics","Bakes");
        return HashTree(CanonicalRoot,Relative=>
            !IsUnder(Relative,Bakes)
            &&(IsUnder(Relative,Assets)
                ||(IsUnder(Relative,"src")||IsUnder(Relative,"build")||IsUnder(Relative,ProjectRelative)||IsRootFile(Relative))
                    &&IsSourceFile(Relative)));
    }

    private static string HashTree(string RepoRoot,Func<string,bool>Include){
        string CanonicalRoot=Path.GetFullPath(RepoRoot).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        string[]Files=[..Directory.EnumerateFiles(CanonicalRoot,"*",SearchOption.AllDirectories)
            .Where(Path=>ShouldHash(CanonicalRoot,Path,Include))
            .OrderBy(Path=>Path.Substring(CanonicalRoot.Length),StringComparer.OrdinalIgnoreCase)];
        using SHA256 Hash=SHA256.Create();
        using CryptoStream Canonical=new(Stream.Null,Hash,CryptoStreamMode.Write);
        byte[]Separator=[0];
        byte[]Buffer=new byte[65_536];
        foreach(string FilePath in Files){
            string Relative=FilePath.Substring(CanonicalRoot.Length)
                .TrimStart(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)
                .Replace(Path.DirectorySeparatorChar,'/');
            byte[]Name=Encoding.UTF8.GetBytes(Relative);
            Canonical.Write(Name,0,Name.Length);Canonical.Write(Separator,0,1);
            FileInfo Before=new(FilePath);
            byte[]Length=BitConverter.GetBytes(Before.Length);
            Canonical.Write(Length,0,Length.Length);
            using(FileStream Input=new(FilePath,FileMode.Open,FileAccess.Read,FileShare.Read)){
                int Read;
                while((Read=Input.Read(Buffer,0,Buffer.Length))>0){ Canonical.Write(Buffer,0,Read); }
            }
            FileInfo After=new(FilePath);After.Refresh();
            if(Before.Length!=After.Length||Before.LastWriteTimeUtc!=After.LastWriteTimeUtc){
                throw new IOException($"Bake dependency changed while hashing: {Relative}");
            }
        }
        Canonical.FlushFinalBlock();
        return BitConverter.ToString(Hash.Hash!).Replace("-",string.Empty);
    }

    internal static string PrepareExactHost(string RepoRoot,string HostHash,CancellationToken CancellationToken){
        string HostDirectory=Path.Combine(RepoRoot,".artifacts","PhysicsBakeHost",HostHash);
        string BuildArtifacts=Path.Combine(HostDirectory,"build");
        string Executable=GetExactHostExecutablePath(RepoRoot,HostHash);
        string RuntimeConfig=Path.ChangeExtension(Executable,".runtimeconfig.json");
        string Marker=Path.Combine(HostDirectory,"dependency.sha256");
        if(File.Exists(Executable)&&File.Exists(RuntimeConfig)&&File.Exists(Marker)
            &&string.Equals(File.ReadAllText(Marker).Trim(),HostHash,StringComparison.Ordinal)){
            return Executable;
        }
        using Mutex BuildMutex=AcquireBuildLock($"CSharpGameEngine.PhysicsBakeHost.{HostHash}","Physics Bake Host",CancellationToken);
        try {
            if(File.Exists(Executable)&&File.Exists(RuntimeConfig)&&File.Exists(Marker)
                &&string.Equals(File.ReadAllText(Marker).Trim(),HostHash,StringComparison.Ordinal)){
                return Executable;
            }
            Directory.CreateDirectory(BuildArtifacts);
            string Project=Path.Combine(RepoRoot,"tools","CSharpGameEngine.Physics.Cli","CSharpGameEngine.Physics.Cli.csproj");
            StringBuilder Output=new();
            string Solution=Path.Combine(RepoRoot,"CSharpGameEngine.slnx");
            int RestoreExitCode=RunDotnet(
                $"restore \"{Solution}\" -p:ArtifactsPath=\"{BuildArtifacts}\" --nologo",
                RepoRoot,BuildTimeout,CancellationToken,Output);
            int BuildExitCode=RestoreExitCode==0?RunDotnet(
                $"build \"{Project}\" -c Debug -p:ArtifactsPath=\"{BuildArtifacts}\" --no-restore --nologo -m:1",
                RepoRoot,BuildTimeout,CancellationToken,Output):RestoreExitCode;
            if(BuildExitCode!=0||!File.Exists(Executable)||!File.Exists(RuntimeConfig)){
                throw new InvalidOperationException($"Exact Physics Bake Host build failed.{Environment.NewLine}{Output}");
            }
            File.WriteAllText(Marker,HostHash,Encoding.ASCII);
            return Executable;
        }
        finally { BuildMutex.ReleaseMutex(); }
    }

    /// <summary>
    /// Builds the scene's game project once per dependency hash into the repository's artifacts and returns the game
    /// assembly. The marker holds the hash and the built assembly, so a finished build is reused.
    /// </summary>
    internal static string PrepareGameBuild(
        string RepoRoot,string ProjectFile,string DependencyHash,CancellationToken CancellationToken){
        string GameDirectory=Path.Combine(Path.GetFullPath(RepoRoot),".artifacts","PhysicsBakeGame",DependencyHash);
        string BuildArtifacts=Path.Combine(GameDirectory,"build");
        string Marker=Path.Combine(GameDirectory,"dependency.sha256");
        string?Built(){
            if(!File.Exists(Marker)){ return null; }
            string[]Lines=File.ReadAllLines(Marker);
            return Lines.Length==2&&string.Equals(Lines[0],DependencyHash,StringComparison.Ordinal)&&File.Exists(Lines[1])
                ?Lines[1]:null;
        }
        if(Built() is { }Existing){ return Existing; }
        using Mutex BuildMutex=AcquireBuildLock($"CSharpGameEngine.PhysicsBakeGame.{DependencyHash}","game build",CancellationToken);
        try {
            if(Built() is { }Concurrent){ return Concurrent; }
            Directory.CreateDirectory(BuildArtifacts);
            StringBuilder Output=new();
            string Properties=$"-p:Configuration=Debug -p:ArtifactsPath=\"{BuildArtifacts}\"";
            // The engine's game build also builds tools (the shader compiler) through MSBuild tasks, which a project
            // restore does not reach; in the engine repository the solution restore covers them, as for the host.
            string Solution=Path.Combine(RepoRoot,"CSharpGameEngine.slnx");
            string RestoreTarget=File.Exists(Solution)?Solution:ProjectFile;
            int ExitCode=RunDotnet(
                $"restore \"{RestoreTarget}\" -p:ArtifactsPath=\"{BuildArtifacts}\" --nologo",
                RepoRoot,BuildTimeout,CancellationToken,Output);
            if(ExitCode==0){
                ExitCode=RunDotnet(
                    $"build \"{ProjectFile}\" {Properties} --no-restore --nologo -m:1",RepoRoot,BuildTimeout,CancellationToken,Output);
            }
            string GameAssembly=string.Empty;
            if(ExitCode==0){
                StringBuilder Evaluation=new();
                ExitCode=RunDotnet(
                    $"msbuild \"{ProjectFile}\" {Properties} -getProperty:TargetPath -nologo",
                    RepoRoot,BuildTimeout,CancellationToken,Evaluation);
                GameAssembly=Evaluation.ToString().Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries)
                    .Select(static Line=>Line.Trim()).LastOrDefault(static Line=>Line.Length>0)??string.Empty;
                Output.Append(Evaluation);
            }
            if(ExitCode!=0||GameAssembly.Length==0||!File.Exists(GameAssembly)){
                throw new InvalidOperationException($"The game build for the Physics Bake failed.{Environment.NewLine}{Output}");
            }
            File.WriteAllLines(Marker,[DependencyHash,GameAssembly],new UTF8Encoding(false));
            return GameAssembly;
        }
        finally { BuildMutex.ReleaseMutex(); }
    }

    private static Mutex AcquireBuildLock(string Name,string Description,CancellationToken CancellationToken){
        Mutex BuildMutex=new(false,Name);
        bool Acquired=false;
        Stopwatch LockTimer=Stopwatch.StartNew();
        try {
            while(!Acquired&&LockTimer.Elapsed<=BuildTimeout){
                CancellationToken.ThrowIfCancellationRequested();
                try { Acquired=BuildMutex.WaitOne(100); }
                catch(AbandonedMutexException){ Acquired=true; }
            }
        }
        catch {
            BuildMutex.Dispose();
            throw;
        }
        if(!Acquired){
            BuildMutex.Dispose();
            throw new TimeoutException($"Timed out waiting for the exact {Description} build lock.");
        }
        return BuildMutex;
    }

    internal static string GetExactHostExecutablePath(string RepoRoot,string DependencyHash){
        if(string.IsNullOrWhiteSpace(RepoRoot)){ throw new ArgumentException(nameof(RepoRoot)); }
        if(string.IsNullOrWhiteSpace(DependencyHash)){ throw new ArgumentException(nameof(DependencyHash)); }
        return Path.Combine(
            Path.GetFullPath(RepoRoot),".artifacts","PhysicsBakeHost",DependencyHash,"build","bin",
            "CSharpGameEngine.Physics.Cli","debug_win-x64","CSharpGameEngine.Physics.Cli.exe");
    }

    private static int RunDotnet(
        string Arguments,string WorkingDirectory,TimeSpan Timeout,CancellationToken CancellationToken,
        StringBuilder Output){
        ProcessStartInfo StartInfo=new("dotnet",Arguments){
            WorkingDirectory=WorkingDirectory,UseShellExecute=false,RedirectStandardOutput=true,
            RedirectStandardError=true,CreateNoWindow=true,
        };
        using Process Process=new(){ StartInfo=StartInfo };
        Process.OutputDataReceived+=(_,Event)=>{ if(Event.Data is not null){ Output.AppendLine(Event.Data); } };
        Process.ErrorDataReceived+=(_,Event)=>{ if(Event.Data is not null){ Output.AppendLine(Event.Data); } };
        Process.Start();Process.BeginOutputReadLine();Process.BeginErrorReadLine();
        Stopwatch Timer=Stopwatch.StartNew();
        while(!Process.WaitForExit(100)){
            if(CancellationToken.IsCancellationRequested){
                try { Process.Kill(); }catch(InvalidOperationException){ }
                CancellationToken.ThrowIfCancellationRequested();
            }
            if(Timer.Elapsed<=Timeout){ continue; }
            try { Process.Kill(); }catch(InvalidOperationException){ }
            throw new TimeoutException("Exact Physics Bake Host build timed out.");
        }
        Process.WaitForExit();
        return Process.ExitCode;
    }

    private static bool ShouldHash(string Root,string FullPath,Func<string,bool>Include){
        string Relative=FullPath.Substring(Root.Length).TrimStart(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        string Normalized=Relative.Replace(Path.AltDirectorySeparatorChar,Path.DirectorySeparatorChar);
        string[]Segments=Normalized.Split(Path.DirectorySeparatorChar);
        if(Segments.Any(Segment=>Segment.Equals("bin",StringComparison.OrdinalIgnoreCase)
            ||Segment.Equals("obj",StringComparison.OrdinalIgnoreCase)||Segment.Equals(".git",StringComparison.OrdinalIgnoreCase)
            ||Segment.Equals(".vs",StringComparison.OrdinalIgnoreCase)||Segment.Equals(".artifacts",StringComparison.OrdinalIgnoreCase))){ return false; }
        return Include(Normalized);
    }

    /// <summary>Whether a repository-relative path lies in a directory; the empty directory is the whole repository.</summary>
    private static bool IsUnder(string Relative,string Directory)=>
        Directory.Length==0||Relative.StartsWith(Directory+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);

    private static bool IsRootFile(string Relative)=>Relative.IndexOf(Path.DirectorySeparatorChar)<0;

    private static bool IsSourceFile(string Relative){
        string Extension=Path.GetExtension(Relative);
        return Extension.Equals(".cs",StringComparison.OrdinalIgnoreCase)
            ||Extension.Equals(".csproj",StringComparison.OrdinalIgnoreCase)
            ||Extension.Equals(".shproj",StringComparison.OrdinalIgnoreCase)
            ||Extension.Equals(".projitems",StringComparison.OrdinalIgnoreCase)
            ||Extension.Equals(".props",StringComparison.OrdinalIgnoreCase)
            ||Extension.Equals(".targets",StringComparison.OrdinalIgnoreCase)
            ||Extension.Equals(".json",StringComparison.OrdinalIgnoreCase);
    }

    internal static string FindProjectRoot(string RepoRoot,string SceneFilePath)=>
        Path.GetDirectoryName(FindProjectFile(RepoRoot,SceneFilePath))!;

    /// <summary>The project file of the nearest directory above the scene that has one; it must be the only one there.</summary>
    internal static string FindProjectFile(string RepoRoot,string SceneFilePath){
        string FullPath=Path.GetFullPath(SceneFilePath);
        DirectoryInfo?Directory=new(Path.GetDirectoryName(FullPath)??RepoRoot);
        while(Directory is not null&&Directory.FullName.StartsWith(RepoRoot,StringComparison.OrdinalIgnoreCase)){
            string[]Projects=[..Directory.EnumerateFiles("*.csproj",SearchOption.TopDirectoryOnly).Select(static File=>File.FullName)];
            if(Projects.Length==1){ return Projects[0]; }
            if(Projects.Length>1){
                throw new InvalidOperationException($"The scene's directory {Directory.FullName} has more than one project.");
            }
            Directory=Directory.Parent;
        }
        throw new InvalidOperationException($"Scene project root was not found for {SceneFilePath}.");
    }

    private static string Sanitize(string Value){
        StringBuilder Result=new(Value.Length);
        foreach(char Character in Value){ Result.Append(char.IsLetterOrDigit(Character)||Character is '-' or '_'?Character:'_'); }
        return Result.ToString();
    }

    private static string HashText(string Value){
        using SHA256 Hash=SHA256.Create();
        return BitConverter.ToString(Hash.ComputeHash(Encoding.UTF8.GetBytes(Value))).Replace("-",string.Empty);
    }
}
