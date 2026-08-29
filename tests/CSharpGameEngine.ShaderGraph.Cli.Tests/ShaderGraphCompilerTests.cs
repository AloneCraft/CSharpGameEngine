using CSharpGameEngine.ShaderArtifacts;
using CSharpGameEngine.ShaderGraph;
using CSharpGameEngine.ShaderGraph.Cli;
namespace CSharpGameEngine.ShaderGraph.Cli.Tests;
public sealed class ShaderGraphCompilerTests {
    public static TheoryData<string,ShaderArtifactTargetKind,int>ProductionGraphs=>new() { { "Triangle",ShaderArtifactTargetKind.Triangle,2 },{ "Model",ShaderArtifactTargetKind.Model,2 },{ "Stage",ShaderArtifactTargetKind.Stage,5 } };
    [Theory]
    [MemberData(nameof(ProductionGraphs))]
    public void ProductionGraph_ExportsDeterministicallyWithNodeSourceMap(string Name,ShaderArtifactTargetKind Target,int EntryCount) {
        var Graph=ReadGraph(Name);
        Assert.True(ShaderGraphValidator.Validate(Graph).IsValid);
        var First=HlslSyntaxGraphConverter.ExportWithSourceMap(Graph);
        var Second=HlslSyntaxGraphConverter.ExportWithSourceMap(Graph);
        Assert.Equal(First.Hlsl,Second.Hlsl);
        Assert.Equal(First.SourceMap.Entries.Select(Entry=>(Entry.NodeId,Entry.StartLine,Entry.EndLine)),Second.SourceMap.Entries.Select(Entry=>(Entry.NodeId,Entry.StartLine,Entry.EndLine)));
        Assert.NotEmpty(First.SourceMap.Entries);
        Assert.All(First.SourceMap.Entries,Entry=>Assert.True(Entry.StartLine>0&&Entry.EndLine>=Entry.StartLine));
        Assert.Equal(EntryCount,ShaderArtifactAbi.GetEntries(Target).Count);
    }
    [Theory]
    [MemberData(nameof(ProductionGraphs))]
    public void ProductionGraph_BuildsDeterministicArtifactWithFixedAbi(string Name,ShaderArtifactTargetKind Target,int EntryCount) {
        var DirectoryPath=Path.Combine(Path.GetTempPath(),"CgeShaderGraphTests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        try {
            var ArtifactPath=Path.Combine(DirectoryPath,Name+".cgeshaderartifact");
            var HlslPath=Path.Combine(DirectoryPath,Name+".generated.hlsl");
            Assert.Equal(0,global::ShaderGraphCli.Run(["build",GraphPath(Name),ArtifactPath,HlslPath,"Release"]));
            var First=File.ReadAllBytes(ArtifactPath);
            Assert.Equal(0,global::ShaderGraphCli.Run(["build",GraphPath(Name),ArtifactPath,HlslPath,"Release"]));
            Assert.Equal(First,File.ReadAllBytes(ArtifactPath));
            Assert.True(File.Exists(HlslPath+".map.json"));
            using var Stream=File.OpenRead(ArtifactPath);
            Assert.True(ShaderArtifactSerializer.TryRead(Stream,Target,ShaderArtifactAbi.GetHash(Target),out var Artifact,out var Error),Error);
            Assert.NotNull(Artifact);
            Assert.Equal(EntryCount,Artifact!.Entries.Count);
            Assert.All(Artifact.Entries,Entry=>{Assert.NotEmpty(Entry.Dxil);Assert.NotEmpty(Entry.Reflection);Assert.NotEmpty(Entry.PsoKey);});
        } finally { Directory.Delete(DirectoryPath,true); }
    }
    [Theory]
    [InlineData("Triangle")]
    [InlineData("Model")]
    [InlineData("Stage")]
    public void ProductionGraph_ContainsNoRawHlslPayload(string Name) {
        var Json=File.ReadAllText(GraphPath(Name));
        Assert.DoesNotContain("Property.Code",Json,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SourceText",Json,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Property.Token",Json,StringComparison.OrdinalIgnoreCase);
    }
    private static ShaderGraphDocument ReadGraph(string Name)=>ShaderGraphCanonicalization.FromJson(File.ReadAllText(GraphPath(Name)))!;
    private static string GraphPath(string Name)=>Path.Combine(AppContext.BaseDirectory,"Graphs",Name+".cgeshadergraph");
}