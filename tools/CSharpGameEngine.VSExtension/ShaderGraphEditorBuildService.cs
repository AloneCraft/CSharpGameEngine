using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
namespace CSharpGameEngine.VSExtension;
internal enum ShaderGraphEditorBuildOperation { BuildRoot,ValidateSubgraph }
internal sealed class ShaderGraphEditorBuildResult {
    internal ShaderGraphEditorBuildResult(bool Success,string Message,string?GeneratedHlslPath) { this.Success=Success;this.Message=Message;this.GeneratedHlslPath=GeneratedHlslPath; }
    internal bool Success { get; }
    internal string Message { get; }
    internal string?GeneratedHlslPath { get; }
}
internal static class ShaderGraphEditorBuildService {
    internal static ShaderGraphEditorBuildOperation GetOperation(string GraphPath)=>
        string.Equals(Path.GetExtension(GraphPath),".cgesubgraph",StringComparison.OrdinalIgnoreCase)?ShaderGraphEditorBuildOperation.ValidateSubgraph:ShaderGraphEditorBuildOperation.BuildRoot;
    internal static ShaderGraphEditorBuildResult BuildAndPublish(string GraphPath) {
        // The graph's game project decides where outputs go (ADR-0065); a graph outside any project builds beside itself.
        var Context=CgeProjectContext.Resolve(GraphPath);
        var RepoRoot=Context?.WorkspaceRoot??Path.GetDirectoryName(Path.GetFullPath(GraphPath))!;
        var CliPath=FindCli(Context?.EngineRoot);
        if(CliPath is null) { return new ShaderGraphEditorBuildResult(false,"Shader Graph CLI is not available. Reinstall the extension or build CSharpGameEngine.ShaderGraph.Cli.",null); }
        if(GetOperation(GraphPath)==ShaderGraphEditorBuildOperation.ValidateSubgraph) {
            var Validation=Run(CliPath,"validate "+Quote(GraphPath),RepoRoot);
            return Validation.ExitCode==0
                ?new ShaderGraphEditorBuildResult(true,"Shader Subgraph validated.",null)
                :new ShaderGraphEditorBuildResult(false,Validation.Output,null);
        }
        var Name=Path.GetFileNameWithoutExtension(GraphPath);
        var OutputDirectory=Path.Combine(RepoRoot,".artifacts","ShaderGraph","Editor");
        Directory.CreateDirectory(OutputDirectory);
        var ArtifactPath=Path.Combine(OutputDirectory,Name+".cgeshaderartifact");
        var HlslPath=Path.Combine(OutputDirectory,Name+".generated.hlsl");
        var Build=Run(CliPath,"build "+Quote(GraphPath)+" "+Quote(ArtifactPath)+" "+Quote(HlslPath)+" Debug",RepoRoot);
        if(Build.ExitCode!=0) { return new ShaderGraphEditorBuildResult(false,Build.Output,HlslPath); }
        var Published=0;
        foreach(var Existing in FindExistingRuntimeArtifacts(Context,Name+".cgeshaderartifact")) {
            var Publish=Run(CliPath,"publish "+Quote(ArtifactPath)+" "+Quote(Existing),RepoRoot);
            if(Publish.ExitCode!=0) { return new ShaderGraphEditorBuildResult(false,Publish.Output,HlslPath); }
            Published++;
        }
        return new ShaderGraphEditorBuildResult(true,$"Shader artifact built; published to {Published} active output(s).",HlslPath);
    }
    /// <summary>The compiler shipped with the extension, else a repository build (SourceMode development).</summary>
    private static string?FindCli(string?EngineRoot) {
        var Bundled=Path.Combine(BridgeExecutableLocator.ExtensionDirectory,"Bridges","CSharpGameEngine.ShaderGraph.Cli","CSharpGameEngine.ShaderGraph.Cli.exe");
        if(File.Exists(Bundled)) { return Bundled; }
        if(EngineRoot is null) { return null; }
        var RepoRoot=EngineRoot;
        foreach(var Configuration in new[]{"Debug","Release"}) {
            foreach(var Architecture in new[]{"x64",string.Empty}) {
                var Candidate=string.IsNullOrEmpty(Architecture)
                    ?Path.Combine(RepoRoot,"tools","CSharpGameEngine.ShaderGraph.Cli","bin",Configuration,"net10.0-windows10.0.19041.0","CSharpGameEngine.ShaderGraph.Cli.exe")
                    :Path.Combine(RepoRoot,"tools","CSharpGameEngine.ShaderGraph.Cli","bin",Architecture,Configuration,"net10.0-windows10.0.19041.0","CSharpGameEngine.ShaderGraph.Cli.exe");
                if(File.Exists(Candidate)) { return Candidate; }
            }
        }
        return null;
    }
    /// <summary>Published copies in the game project's outputs, plus the engine repository's tests and tools in SourceMode.</summary>
    private static IEnumerable<string>FindExistingRuntimeArtifacts(CgeProjectContext?Context,string FileName) {
        if(Context is null) { yield break; }
        List<string>Roots=[Context.ProjectDirectory];
        if(Context.EngineRoot is{ }EngineRoot) { Roots.Add(Path.Combine(EngineRoot,"tests"));Roots.Add(Path.Combine(EngineRoot,"tools")); }
        foreach(var Root in Roots) {
            if(!Directory.Exists(Root)) { continue; }
            foreach(var Candidate in Directory.EnumerateFiles(Root,FileName,SearchOption.AllDirectories)) {
                var Segments=Candidate.Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
                if(Segments.Any(Segment=>string.Equals(Segment,"bin",StringComparison.OrdinalIgnoreCase))&&Segments.Any(Segment=>string.Equals(Segment,"Shaders",StringComparison.OrdinalIgnoreCase))) { yield return Candidate; }
            }
        }
    }
    private static string Quote(string Value)=>ExternalBridgeProcess.QuoteArgument(Value);
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
