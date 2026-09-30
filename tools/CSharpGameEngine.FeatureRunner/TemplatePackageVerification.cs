using System.Diagnostics;
using CSharpGameEngine.ProjectTemplates;
namespace CSharpGameEngine.FeatureStages;
/// <summary>
/// The product end-to-end check of ADR-0065/0068. The ThirdPerson template is generated into a
/// temporary folder outside the repository, exactly as the Visual Studio wizard does. It is restored
/// from a feed that holds only the packed CSharpGameEngine.Sdk package, built, and played with the
/// scripted input of rigid.character-controller. No generated build input may point back into the
/// repository.
/// </summary>
public static class TemplatePackageVerification {
    public const string ProjectName="My ThirdPerson";
    public static async Task<int>RunAsync(string RepositoryRoot,string Configuration,string Adapter,TextWriter Output,TextWriter Error,CancellationToken CancellationToken){
        var Package=Directory.Exists(Path.Combine(RepositoryRoot,".artifacts","packages"))
            ?Directory.GetFiles(Path.Combine(RepositoryRoot,".artifacts","packages"),GameProjectGenerator.SdkPackageId+".*.nupkg").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            :null;
        if(Package is null){
            await Error.WriteLineAsync("No CSharpGameEngine.Sdk package in .artifacts/packages; run dotnet pack packaging/CSharpGameEngine.Sdk first.").ConfigureAwait(false);
            return 2;
        }
        var Version=GameProjectGenerator.VersionOfPackage(Package);
        var Work=Path.Combine(Path.GetTempPath(),"cge-template-e2e",Guid.NewGuid().ToString("N"));
        var Feed=Path.Combine(Work,"feed");
        GameProjectGenerator.PublishToFeed(Package,Feed);
        var ProjectDirectory=Path.Combine(Work,ProjectName);
        var ProjectFile=GameProjectGenerator.Generate(Path.Combine(RepositoryRoot,"templates","CSharpGameEngine.Templates.ThirdPerson"),ProjectDirectory,ProjectName,Version,Feed);
        await Output.WriteLineAsync($"CGE-TEMPLATE-PACKAGE generated {ProjectFile} from {Path.GetFileName(Package)}").ConfigureAwait(false);
        // The package keeps its version while it is rebuilt, so drop a cached copy to restore this exact build.
        var CachedPackage=Path.Combine(Environment.GetEnvironmentVariable("NUGET_PACKAGES")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".nuget","packages"),GameProjectGenerator.SdkPackageId.ToLowerInvariant(),Version);
        if(Directory.Exists(CachedPackage)){
            Directory.Delete(CachedPackage,true);
        }
        var Build=await RunDotnetAsync(["build",ProjectFile,"--configuration",Configuration,"--nologo"],ProjectDirectory,CancellationToken).ConfigureAwait(false);
        await Output.WriteAsync(Build.Output).ConfigureAwait(false);
        if(Build.ExitCode!=0){
            await Error.WriteLineAsync($"The generated project failed to build ({Build.ExitCode}).").ConfigureAwait(false);
            return 1;
        }
        var Leaks=FindRepositoryReferences(ProjectDirectory,RepositoryRoot);
        foreach(var Leak in Leaks){
            await Error.WriteLineAsync("Generated build input refers to the repository: "+Leak).ConfigureAwait(false);
        }
        if(Leaks.Count>0){
            return 1;
        }
        var Executable=Path.Combine(ProjectDirectory,"bin",Configuration,"net10.0-windows10.0.19041.0","win-x64",ProjectName+".exe");
        var Evidence=Path.Combine(RepositoryRoot,".artifacts","feature-evidence","vsix.project-template",Adapter);
        var Result=await ThirdPersonCharacterVerification.RunGameAsync(Executable,Evidence,Adapter,Output,Error,CancellationToken).ConfigureAwait(false);
        if(Result==0){
            try {
                Directory.Delete(Work,true);
            }
            catch(IOException){
                // A still-running compiler server can hold a file briefly; the folder is under %TEMP%.
            }
        }
        return Result;
    }
    /// <summary>Restore and build outputs that mention the repository root (the generated project must not depend on it).</summary>
    public static IReadOnlyList<string>FindRepositoryReferences(string ProjectDirectory,string RepositoryRoot){
        var Root=Path.GetFullPath(RepositoryRoot).TrimEnd(Path.DirectorySeparatorChar);
        string[]Candidates=[.. Directory.EnumerateFiles(Path.Combine(ProjectDirectory,"obj"),"*.nuget.g.*"),Path.Combine(ProjectDirectory,"obj","project.assets.json"),.. Directory.EnumerateFiles(Path.Combine(ProjectDirectory,"bin"),"*.deps.json",SearchOption.AllDirectories)];
        List<string>Leaks=[];
        foreach(var Candidate in Candidates.Where(File.Exists)){
            var Text=File.ReadAllText(Candidate);
            if(Text.Contains(Root,StringComparison.OrdinalIgnoreCase)||Text.Contains(Root.Replace("\\","\\\\",StringComparison.Ordinal),StringComparison.OrdinalIgnoreCase)){
                Leaks.Add(Candidate);
            }
        }
        return Leaks;
    }
    private static async Task<(int ExitCode,string Output)>RunDotnetAsync(IReadOnlyList<string>Arguments,string WorkingDirectory,CancellationToken CancellationToken){
        var Start=new ProcessStartInfo("dotnet") { WorkingDirectory=WorkingDirectory,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true };
        foreach(var Argument in Arguments){
            Start.ArgumentList.Add(Argument);
        }
        // The generated project must not see the repository's MSBuild environment.
        Start.Environment.Remove("MSBuildExtensionsPath");
        Start.Environment.Remove("MSBUILD_EXE_PATH");
        using var Process=System.Diagnostics.Process.Start(Start)??throw new InvalidOperationException("Could not start dotnet.");
        var Standard=Process.StandardOutput.ReadToEndAsync(CancellationToken);
        var Errors=Process.StandardError.ReadToEndAsync(CancellationToken);
        await Process.WaitForExitAsync(CancellationToken).ConfigureAwait(false);
        return (Process.ExitCode,await Standard.ConfigureAwait(false)+await Errors.ConfigureAwait(false));
    }
}
