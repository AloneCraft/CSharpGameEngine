using System.Diagnostics;
namespace CSharpGameEngine.FeatureStages;
public sealed class GitChangedPathProvider:IChangedPathProvider {
    public async Task<IReadOnlyList<string>>GetChangedPathsAsync(string RepositoryRoot,CancellationToken CancellationToken){
        var Result=await RunGitAsync(RepositoryRoot,["status","--porcelain=v1","-z","--untracked-files=all"],CancellationToken).ConfigureAwait(false);
        if(Result.ExitCode!=0){ throw new InvalidOperationException($"git status failed: {Result.StandardError}"); }
        return ParsePorcelainZ(Result.StandardOutput);
    }
    public static IReadOnlyList<string>ParsePorcelainZ(string Output){
        var Paths=new List<string>();
        string[]Entries=Output.Split((char)0,StringSplitOptions.RemoveEmptyEntries);
        for(int Index=0;Index<Entries.Length;Index++){
            string Entry=Entries[Index];
            if(Entry.Length<4||Entry[2]!=' '){ continue; }
            string Status=Entry[..2];
            AddPath(Entry[3..]);
            if((Status.Contains('R')||Status.Contains('C'))&&Index+1<Entries.Length){
                AddPath(Entries[++Index]);
            }
        }
        return Paths.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static PathValue=>PathValue,StringComparer.OrdinalIgnoreCase).ToArray();

        void AddPath(string PathValue){
            string Path=PathValue.Replace((char)92,'/').Trim('/');
            if(Path.Length>0){ Paths.Add(Path); }
        }
    }
    internal static async Task<(string Commit,bool IsDirty)>ReadProvenanceAsync(string RepositoryRoot,CancellationToken CancellationToken){
        var CommitResult=await RunGitAsync(RepositoryRoot,["rev-parse","HEAD"],CancellationToken).ConfigureAwait(false);
        var StatusResult=await RunGitAsync(RepositoryRoot,["status","--porcelain=v1","--untracked-files=all"],CancellationToken).ConfigureAwait(false);
        var Commit=CommitResult.ExitCode==0?CommitResult.StandardOutput.Trim():"unknown";
        var IsDirty=StatusResult.ExitCode!=0||!string.IsNullOrWhiteSpace(StatusResult.StandardOutput);
        return(Commit,IsDirty);
    }
    private static async Task<ProcessResult>RunGitAsync(string RepositoryRoot,IReadOnlyList<string>Arguments,CancellationToken CancellationToken){
        var StartInfo=new ProcessStartInfo(){FileName="git",WorkingDirectory=RepositoryRoot,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
        foreach(var Argument in Arguments){ StartInfo.ArgumentList.Add(Argument); }
        using var Process=new Process(){StartInfo=StartInfo};
        if(!Process.Start()){ throw new InvalidOperationException("Could not start git."); }
        var OutputTask=Process.StandardOutput.ReadToEndAsync(CancellationToken);
        var ErrorTask=Process.StandardError.ReadToEndAsync(CancellationToken);
        await Process.WaitForExitAsync(CancellationToken).ConfigureAwait(false);
        return new ProcessResult(Process.ExitCode,await OutputTask.ConfigureAwait(false),await ErrorTask.ConfigureAwait(false));
    }
    private sealed record ProcessResult(int ExitCode,string StandardOutput,string StandardError);
}
public static class FeatureStageRepositoryLocator {
    public static string Find(string?StartDirectory=null){
        var Current=(DirectoryInfo?)new DirectoryInfo(Path.GetFullPath(StartDirectory??Environment.CurrentDirectory));
        while(Current is not null){
            if(File.Exists(Path.Combine(Current.FullName,"CSharpGameEngine.slnx"))){ return Current.FullName; }
            Current=Current.Parent;
        }
        throw new DirectoryNotFoundException("CSharpGameEngine.slnx repository root was not found.");
    }
}
