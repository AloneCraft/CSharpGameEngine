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
            Assert.True(global::ShaderGraphCli.CanReuseArtifact(ArtifactPath,Target,ShaderGraphDependencyComposition.Resolve(GraphPath(Name)).SemanticHash,ShaderGraphDependencyComposition.Resolve(GraphPath(Name)).DependencyHash,"1.1.0-Release"));
            File.SetAttributes(ArtifactPath,FileAttributes.ReadOnly);
            try {
                Assert.Equal(0,global::ShaderGraphCli.Run(["build",GraphPath(Name),ArtifactPath,HlslPath,"Release"]));
            } finally { File.SetAttributes(ArtifactPath,FileAttributes.Normal); }
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
        var Paths=new[]{GraphPath(Name)}.Concat(Name=="Stage"?Directory.GetFiles(Path.GetDirectoryName(GraphPath(Name))!,"*.cgesubgraph"):Array.Empty<string>());
        foreach(var PathValue in Paths) {
            var Json=File.ReadAllText(PathValue);
            Assert.DoesNotContain("Property.Code",Json,StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SourceText",Json,StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Property.Token",Json,StringComparison.OrdinalIgnoreCase);
        }
    }
    [Fact]
    public void ProductionStage_ExternalSubgraphsPreserveGoldenHlsl() {
        var Resolved=ShaderGraphDependencyComposition.Resolve(GraphPath("Stage"));
        Assert.Equal(4,Resolved.Source.Dependencies.Count);
        Assert.Equal(4,Resolved.DependencyPaths.Count);
        Assert.NotEqual(Resolved.SemanticHash,Resolved.DependencyHash);
        var Export=HlslSyntaxGraphConverter.ExportWithSourceMap(Resolved.Composed);
        var Pbr=ShaderGraphCanonicalization.FromJson(File.ReadAllText(Resolved.DependencyPaths.Single(PathValue=>Path.GetFileName(PathValue).Equals("Pbr.cgesubgraph",StringComparison.OrdinalIgnoreCase))))!;
        var PbrRoot=Pbr.Subgraphs.Single(Subgraph=>Subgraph.Id==Pbr.RootSubgraphId);
        var PbrTranslation=PbrRoot.Nodes.Single(Node=>Node.GetArgumentValue(HlslSyntaxGraphContract.ClrTypeArgument)=="UnityShaderParser.HLSL.TranslationUnitNode");
        var PbrDeclarationId=PbrRoot.Edges.First(Edge=>Edge.ToNodeId==PbrTranslation.Id).FromNodeId;
        Assert.Contains(Export.SourceMap.Entries,Entry=>Entry.NodeId==PbrDeclarationId.ToString());
        var Hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Export.Hlsl)));
        Assert.Equal("67C5652958550327DFB90C2059E1FE5778A0D0B59B0787D21919314A9D67DF81",Hash);
    }
    [Fact]
    public void DependencySemanticChange_ChangesOnlyDependencyHash() {
        var DirectoryPath=Path.Combine(Path.GetTempPath(),"CgeShaderGraphDependencies",Guid.NewGuid().ToString("N"));
        try {
            var StagePath=CopyStageFixture(DirectoryPath);
            var Before=ShaderGraphDependencyComposition.Resolve(StagePath);
            var PbrPath=Before.DependencyPaths.Single(PathValue=>Path.GetFileName(PathValue).Equals("Pbr.cgesubgraph",StringComparison.OrdinalIgnoreCase));
            var Pbr=ShaderGraphCanonicalization.FromJson(File.ReadAllText(PbrPath))!;
            Pbr.Subgraphs[0].Nodes[0].Name=(Pbr.Subgraphs[0].Nodes[0].Name??string.Empty)+" Changed";
            File.WriteAllText(PbrPath,ShaderGraphCanonicalization.ToCanonicalJson(Pbr));
            var After=ShaderGraphDependencyComposition.Resolve(StagePath);
            Assert.Equal(Before.SemanticHash,After.SemanticHash);
            Assert.NotEqual(Before.DependencyHash,After.DependencyHash);
        } finally { if(Directory.Exists(DirectoryPath)){Directory.Delete(DirectoryPath,true);} }
    }
    [Fact]
    public void ExternalDependencyResolver_RejectsMissingMismatchAndRecursion() {
        var Root=Path.Combine(Path.GetTempPath(),"CgeShaderGraphDependencyErrors",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        try {
            var MissingPath=CopyStageFixture(Path.Combine(Root,"missing"));
            var Missing=ShaderGraphCanonicalization.FromJson(File.ReadAllText(MissingPath))!;
            Missing.Dependencies[0].Path="Missing.cgesubgraph";
            File.WriteAllText(MissingPath,ShaderGraphCanonicalization.ToCanonicalJson(Missing));
            Assert.Throws<FileNotFoundException>(()=>ShaderGraphDependencyComposition.Resolve(MissingPath));

            var MismatchPath=CopyStageFixture(Path.Combine(Root,"mismatch"));
            var Mismatch=ShaderGraphCanonicalization.FromJson(File.ReadAllText(MismatchPath))!;
            Mismatch.Dependencies[0].Id=StableId.New();
            File.WriteAllText(MismatchPath,ShaderGraphCanonicalization.ToCanonicalJson(Mismatch));
            Assert.Contains("identity mismatch",Assert.Throws<InvalidDataException>(()=>ShaderGraphDependencyComposition.Resolve(MismatchPath)).Message,StringComparison.OrdinalIgnoreCase);

            var RecursivePath=CopyStageFixture(Path.Combine(Root,"recursion"));
            var RecursiveRoot=ShaderGraphCanonicalization.FromJson(File.ReadAllText(RecursivePath))!;
            var DependencyPath=Path.Combine(Path.GetDirectoryName(RecursivePath)!,RecursiveRoot.Dependencies[0].Path);
            var Dependency=ShaderGraphCanonicalization.FromJson(File.ReadAllText(DependencyPath))!;
            Dependency.Dependencies.Add(new ShaderGraphDependency { Id=RecursiveRoot.Id,Path=Path.GetFileName(RecursivePath) });
            File.WriteAllText(DependencyPath,ShaderGraphCanonicalization.ToCanonicalJson(Dependency));
            Assert.Contains("recursion",Assert.Throws<InvalidDataException>(()=>ShaderGraphDependencyComposition.Resolve(RecursivePath)).Message,StringComparison.OrdinalIgnoreCase);
        } finally { Directory.Delete(Root,true); }
    }
    private static string CopyStageFixture(string DirectoryPath) {
        Directory.CreateDirectory(DirectoryPath);
        var SourceDirectory=Path.GetDirectoryName(GraphPath("Stage"))!;
        File.Copy(GraphPath("Stage"),Path.Combine(DirectoryPath,"Stage.cgeshadergraph"));
        foreach(var DependencyPath in Directory.GetFiles(SourceDirectory,"*.cgesubgraph")) { File.Copy(DependencyPath,Path.Combine(DirectoryPath,Path.GetFileName(DependencyPath))); }
        return Path.Combine(DirectoryPath,"Stage.cgeshadergraph");
    }
    private static ShaderGraphDocument ReadGraph(string Name)=>ShaderGraphDependencyComposition.Resolve(GraphPath(Name)).Composed;
    private static string GraphPath(string Name)=>Path.Combine(AppContext.BaseDirectory,"Graphs",Name+".cgeshadergraph");
}