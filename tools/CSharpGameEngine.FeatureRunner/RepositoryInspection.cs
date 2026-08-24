using System.Diagnostics;
namespace CSharpGameEngine.FeatureStages;
public sealed class GitChangedPathProvider:IChangedPathProvider {
    public async Task<IReadOnlyList<string>>GetChangedPathsAsync(string RepositoryRoot,CancellationToken CancellationToken){
        var Result=await RunGitAsync(RepositoryRoot,["status","--porcelain=v1","--untracked-files=all"],CancellationToken).ConfigureAwait(false);
        if(Result.ExitCode!=0){ throw new InvalidOperationException($"git status failed: {Result.StandardError}"); }
        var Paths=new List<string>();
        foreach(var Line in Result.StandardOutput.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries)){
            if(Line.Length<4){ continue; }
            var PathValue=Line.Substring(3);
            var RenameSeparator=PathValue.LastIndexOf(" -> ",StringComparison.Ordinal);
            if(RenameSeparator>=0){ PathValue=PathValue.Substring(RenameSeparator+4); }
            Paths.Add(PathValue.Trim('"').Replace('\\','/'));
        }
        return Paths.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static PathValue=>PathValue,StringComparer.OrdinalIgnoreCase).ToArray();
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
