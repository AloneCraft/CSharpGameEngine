using CSharpGameEngine.ShaderGraph;
using System.Diagnostics;
using System.IO.Compression;
namespace CSharpGameEngine.VSExtension.Tests;
public sealed class AssetGraphBundleTests {
    [Fact]
    public async Task ThePackagedStageBundleBuildsWithThePackagedCompiler(){
        var Root=Path.Combine(Path.GetTempPath(),"CgePackagedGraph-"+Guid.NewGuid().ToString("N"));
        var PackageRoot=Path.Combine(Root,"package");
        Directory.CreateDirectory(Root);
        try {
            ZipFile.ExtractToDirectory(Path.Combine(AppContext.BaseDirectory,"TestAssets","CSharpGameEngine.VSExtension.vsix"),PackageRoot);
            var Output=Path.Combine(Root,"output");
            var GraphPath=Path.Combine(Output,"MyStage.cgeshadergraph");
            var Files=ShaderGraphTemplateWizard.PlanStageBundle(Path.Combine(PackageRoot,"ShaderGraphTemplates"),GraphPath);
            Directory.CreateDirectory(Output);
            foreach(var Entry in Files){ File.WriteAllText(Entry.Key,Entry.Value); }
            var Artifact=Path.Combine(Output,"MyStage.cgeshaderartifact");
            var Start=new ProcessStartInfo(Path.Combine(PackageRoot,"Bridges","CSharpGameEngine.ShaderGraph.Cli","CSharpGameEngine.ShaderGraph.Cli.exe")){
                Arguments="build \""+GraphPath+"\" \""+Artifact+"\"",WorkingDirectory=Output,
                UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,
            };
            using var Process=System.Diagnostics.Process.Start(Start)??throw new InvalidOperationException("Packaged compiler did not start.");
            try {
                var StandardOutput=Process.StandardOutput.ReadToEndAsync();
                var StandardError=Process.StandardError.ReadToEndAsync();
                Assert.True(await Task.Run(()=>Process.WaitForExit(120000)),"Packaged compiler timed out.");
                Assert.True(Process.ExitCode==0,(await StandardOutput)+(await StandardError));
                Assert.True(File.Exists(Artifact));
                Assert.True(new FileInfo(Artifact).Length>0);
            } finally {
                if(!Process.HasExited){ Process.Kill();Process.WaitForExit(5000); }
            }
        } finally {
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(Root),StringComparison.OrdinalIgnoreCase);
            Directory.Delete(Root,true);
        }
    }
    private static string RepositoryRoot(){
        for(var Directory=new DirectoryInfo(AppContext.BaseDirectory);Directory is not null;Directory=Directory.Parent){
            if(File.Exists(Path.Combine(Directory.FullName,"CSharpGameEngine.slnx"))){ return Directory.FullName; }
        }
        throw new InvalidOperationException("Repository root unavailable.");
    }
    [Fact]
    public void StageBundlePlanningDoesNotWriteAndProducesResolvableRemappedDependencies(){
        var DestinationDirectory=Path.Combine(Path.GetTempPath(),"CgeGraphBundle-"+Guid.NewGuid().ToString("N"));
        var Destination=Path.Combine(DestinationDirectory,"MyStage.cgeshadergraph");
        var TemplateDirectory=Path.Combine(RepositoryRoot(),"assets","Shaders");
        var Files=ShaderGraphTemplateWizard.PlanStageBundle(TemplateDirectory,Destination);
        Assert.False(Directory.Exists(DestinationDirectory));
        Assert.True(Files.Count>1);
        var Graph=ShaderGraphCanonicalization.FromJson(Files[Destination])!;
        Assert.NotEqual(ShaderGraphCanonicalization.FromJson(File.ReadAllText(Path.Combine(TemplateDirectory,"Stage.cgeshadergraph")))!.Id,Graph.Id);
        Assert.All(Graph.Dependencies,Dependency=>{
            Assert.StartsWith("MyStage.",Dependency.Path);
            var PathValue=Path.Combine(DestinationDirectory,Dependency.Path);
            Assert.True(Files.ContainsKey(PathValue));
            Assert.Equal(Dependency.Id,ShaderGraphCanonicalization.FromJson(Files[PathValue])!.Id);
        });
        Directory.CreateDirectory(DestinationDirectory);
        try {
            foreach(var FileValue in Files){ File.WriteAllText(FileValue.Key,FileValue.Value); }
            var Document=ShaderGraphEditorDocument.Load(Destination);
            var Validation=Document.Validate();
            Assert.True(Validation.IsValid,string.Join(Environment.NewLine,Validation.Diagnostics.Select(Diagnostic=>Diagnostic.Message)));
        } finally {
            foreach(var PathValue in Files.Keys){ if(File.Exists(PathValue)){ File.Delete(PathValue); } }
            Directory.Delete(DestinationDirectory);
        }
    }
    [Fact]
    public void RepeatedDependencyPathsMustAgreeOnIdentity(){
        var Root=Path.Combine(Path.GetTempPath(),"CgeGraphIdentity-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        var Source=Path.Combine(Root,"Stage.cgeshadergraph");
        var DependencyPath=Path.Combine(Root,"Shared.cgesubgraph");
        var Dependency=new ShaderGraphDocument { Id=StableId.New(),RootSubgraphId=StableId.New() };
        var Graph=new ShaderGraphDocument { Id=StableId.New(),RootSubgraphId=StableId.New() };
        Graph.Dependencies.Add(new ShaderGraphDependency { Id=Dependency.Id,Path="Shared.cgesubgraph" });
        Graph.Dependencies.Add(new ShaderGraphDependency { Id=StableId.New(),Path="Shared.cgesubgraph" });
        File.WriteAllText(Source,ShaderGraphCanonicalization.ToCanonicalJson(Graph));
        File.WriteAllText(DependencyPath,ShaderGraphCanonicalization.ToCanonicalJson(Dependency));
        try {
            Assert.Throws<InvalidDataException>(()=>ShaderGraphTemplateWizard.PlanStageBundle(Root,Path.Combine(Root,"output","MyStage.cgeshadergraph")));
            Assert.False(Directory.Exists(Path.Combine(Root,"output")));
        } finally { File.Delete(Source);File.Delete(DependencyPath);Directory.Delete(Root); }
    }
    [Fact]
    public void ARecursiveTemplateIsRejectedBeforeAnyDestinationIsWritten(){
        var Root=Path.Combine(Path.GetTempPath(),"CgeGraphCycle-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        var Source=Path.Combine(Root,"Stage.cgeshadergraph");
        var Graph=new ShaderGraphDocument { Id=StableId.New(),RootSubgraphId=StableId.New() };
        Graph.Dependencies.Add(new ShaderGraphDependency { Id=Graph.Id,Path="Stage.cgeshadergraph" });
        File.WriteAllText(Source,ShaderGraphCanonicalization.ToCanonicalJson(Graph));
        try {
            Assert.Throws<InvalidDataException>(()=>ShaderGraphTemplateWizard.PlanStageBundle(Root,Path.Combine(Root,"output","MyStage.cgeshadergraph")));
            Assert.False(Directory.Exists(Path.Combine(Root,"output")));
        } finally { File.Delete(Source);Directory.Delete(Root); }
    }
}
