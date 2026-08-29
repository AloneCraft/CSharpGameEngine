using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
namespace CSharpGameEngine.VSExtension;
internal sealed class ShaderGraphEditorBuildResult {
    internal ShaderGraphEditorBuildResult(bool Success,string Message,string?GeneratedHlslPath) { this.Success=Success;this.Message=Message;this.GeneratedHlslPath=GeneratedHlslPath; }
    internal bool Success { get; }
    internal string Message { get; }
    internal string?GeneratedHlslPath { get; }
}
internal static class ShaderGraphEditorBuildService {
    internal static ShaderGraphEditorBuildResult BuildAndPublish(string GraphPath) {
        var RepoRoot=RepoLocator.ResolveRepoRoot();
        if(RepoRoot is null) { return new ShaderGraphEditorBuildResult(false,"Repository root is unavailable.",null); }
        var CliPath=FindCli(RepoRoot);
        if(CliPath is null) { return new ShaderGraphEditorBuildResult(false,"Shader Graph CLI is not built. Build CSharpGameEngine.ShaderGraph.Cli first.",null); }
        var Name=Path.GetFileNameWithoutExtension(GraphPath);
        var OutputDirectory=Path.Combine(RepoRoot,".artifacts","ShaderGraph","Editor");
        Directory.CreateDirectory(OutputDirectory);
        var ArtifactPath=Path.Combine(OutputDirectory,Name+".cgeshaderartifact");
        var HlslPath=Path.Combine(OutputDirectory,Name+".generated.hlsl");
        var Build=Run(CliPath,$"build \"{GraphPath}\" \"{ArtifactPath}\" \"{HlslPath}\" Debug",RepoRoot);
        if(Build.ExitCode!=0) { return new ShaderGraphEditorBuildResult(false,Build.Output,HlslPath); }
        var Published=0;
        foreach(var Existing in FindExistingRuntimeArtifacts(RepoRoot,Name+".cgeshaderartifact")) {
            var Publish=Run(CliPath,$"publish \"{ArtifactPath}\" \"{Existing}\"",RepoRoot);
            if(Publish.ExitCode!=0) { return new ShaderGraphEditorBuildResult(false,Publish.Output,HlslPath); }
            Published++;
        }
        return new ShaderGraphEditorBuildResult(true,$"Shader artifact built; published to {Published} active output(s).",HlslPath);
    }
    private static string?FindCli(string RepoRoot) {
        foreach(var Configuration in new[]{"Debug","Release"}) {
            var Candidate=Path.Combine(RepoRoot,"tools","CSharpGameEngine.ShaderGraph.Cli","bin","x64",Configuration,"net10.0-windows10.0.19041.0","CSharpGameEngine.ShaderGraph.Cli.exe");
            if(File.Exists(Candidate)) { return Candidate; }
        }
        return null;
    }
    private static IEnumerable<string>FindExistingRuntimeArtifacts(string RepoRoot,string FileName) {
        foreach(var RootName in new[]{"UserEcsProject","tests","tools"}) {
            var Root=Path.Combine(RepoRoot,RootName);
            if(!Directory.Exists(Root)) { continue; }
            foreach(var Candidate in Directory.EnumerateFiles(Root,FileName,SearchOption.AllDirectories)) {
                var Segments=Candidate.Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
                if(Segments.Any(Segment=>string.Equals(Segment,"bin",StringComparison.OrdinalIgnoreCase))&&Segments.Any(Segment=>string.Equals(Segment,"Shaders",StringComparison.OrdinalIgnoreCase))) { yield return Candidate; }
            }
        }
    }
    private static (int ExitCode,string Output)Run(string Executable,string Arguments,string WorkingDirectory) {
        var StartInfo=new ProcessStartInfo(Executable,Arguments) { WorkingDirectory=WorkingDirectory,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
        using(var ChildProcess=System.Diagnostics.Process.Start(StartInfo)) {
            if(ChildProcess is null) { return new ValueTuple<int,string>(-1,"Failed to start Shader Graph CLI."); }
            var Output=ChildProcess.StandardOutput.ReadToEnd();
            var Error=ChildProcess.StandardError.ReadToEnd();
            ChildProcess.WaitForExit();
            string Combined=(Output+Environment.NewLine+Error).Trim();
            return new ValueTuple<int,string>(ChildProcess.ExitCode,Combined);
        }
    }
}