using CSharpGameEngine.ShaderArtifacts;
using CSharpGameEngine.ShaderGraph;
using CSharpGameEngine.ShaderGraph.Cli;
namespace CSharpGameEngine.ShaderGraph.Cli.Tests;
public sealed class ShaderGraphCompilerTests {
    public static TheoryData<string,ShaderArtifactTargetKind,int>ProductionGraphs=>new() { { "Triangle",ShaderArtifactTargetKind.Triangle,2 },{ "Model",ShaderArtifactTargetKind.Model,2 },{ "Stage",ShaderArtifactTargetKind.Stage,14 } };
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
            if(Target==ShaderArtifactTargetKind.Stage) {
                string BuiltSource=File.ReadAllText(HlslPath);
                Assert.Contains("PreviewPSMain",BuiltSource,StringComparison.Ordinal);
                Assert.Contains("PreviewCleanPSMain",BuiltSource,StringComparison.Ordinal);
                Assert.Contains("OverlayVSMain",BuiltSource,StringComparison.Ordinal);
                Assert.Contains("OverlayPSMain",BuiltSource,StringComparison.Ordinal);
                Assert.Contains(Artifact.Entries,Entry=>Entry.LogicalName=="PreviewPSMain");
                Assert.Contains(Artifact.Entries,Entry=>Entry.LogicalName=="PreviewCleanPSMain");
                Assert.Contains(Artifact.Entries,Entry=>Entry.LogicalName=="OverlayVSMain");
                Assert.Contains(Artifact.Entries,Entry=>Entry.LogicalName=="OverlayPSMain");
                // M0104: the screen layer's entries (ADR-0078).
                Assert.Contains("ScreenVSMain",BuiltSource,StringComparison.Ordinal);
                Assert.Contains("ScreenPSMain",BuiltSource,StringComparison.Ordinal);
                Assert.Contains(Artifact.Entries,Entry=>Entry.LogicalName=="ScreenVSMain");
                Assert.Contains(Artifact.Entries,Entry=>Entry.LogicalName=="ScreenPSMain");
                // The Stage graph owns depth and a single vertex/pixel interface. Legacy ABI names have been removed.
                Assert.Contains("StagePixelOutput PSMain(VertexOutput input)",BuiltSource,StringComparison.Ordinal);
                Assert.Contains("StagePixelOutput CompleteStageSurface(VertexOutput input, float4 color)",BuiltSource,StringComparison.Ordinal);
                Assert.Contains("float Depth : SV_DepthGreaterEqual;",BuiltSource,StringComparison.Ordinal);
                Assert.Contains("float2 ClipDistance : SV_ClipDistance0;",BuiltSource,StringComparison.Ordinal);
                Assert.DoesNotContain("struct RadialPixelInput",BuiltSource,StringComparison.Ordinal);
                Assert.DoesNotContain("struct RadialVertexOutput",BuiltSource,StringComparison.Ordinal);
                Assert.DoesNotContain("ToVertexOutput(",BuiltSource,StringComparison.Ordinal);
                Assert.Equal(new[]{
                    "VSMain","PSMain","VSShadowMain","FullscreenVS","PostProcessPS","PreviewPSMain","PreviewCleanPSMain",
                    "OverlayVSMain","OverlayPSMain","ScreenVSMain","ScreenPSMain","ObserverVSMain","SurfacePrepassPSMain","LumenCardPSMain",
                }.OrderBy(Name=>Name,StringComparer.Ordinal),Artifact.Entries.Select(Entry=>Entry.LogicalName).OrderBy(Name=>Name,StringComparer.Ordinal));
                Assert.Contains(Artifact.Entries,Entry=>Entry.LogicalName=="ObserverVSMain");
                // M0111: the Lumen surface prepass writes the normal and roughness and the distance; the card capture writes three targets.
                Assert.Contains(Artifact.Entries,Entry=>Entry.LogicalName=="SurfacePrepassPSMain");
                Assert.Contains(Artifact.Entries,Entry=>Entry.LogicalName=="LumenCardPSMain");
                Assert.Contains("SurfacePrepassOutput SurfacePrepassPSMain(VertexOutput input)",BuiltSource,StringComparison.Ordinal);
                Assert.Contains("float4 Emissive : SV_Target2;",BuiltSource,StringComparison.Ordinal);
                Assert.Contains("Texture2D LumenDiffuseTexture : register(t16);",BuiltSource,StringComparison.Ordinal);
            }
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
        Assert.Equal(5,Resolved.Source.Dependencies.Count);
        Assert.Equal(5,Resolved.DependencyPaths.Count);
        Assert.NotEqual(Resolved.SemanticHash,Resolved.DependencyHash);
        var Export=HlslSyntaxGraphConverter.ExportWithSourceMap(Resolved.Composed);
        var Pbr=ShaderGraphCanonicalization.FromJson(File.ReadAllText(Resolved.DependencyPaths.Single(PathValue=>Path.GetFileName(PathValue).Equals("Pbr.cgesubgraph",StringComparison.OrdinalIgnoreCase))))!;
        var PbrRoot=Pbr.Subgraphs.Single(Subgraph=>Subgraph.Id==Pbr.RootSubgraphId);
        var PbrTranslation=PbrRoot.Nodes.Single(Node=>Node.GetArgumentValue(HlslSyntaxGraphContract.ClrTypeArgument)=="UnityShaderParser.HLSL.TranslationUnitNode");
        var PbrDeclarationId=PbrRoot.Edges.First(Edge=>Edge.ToNodeId==PbrTranslation.Id).FromNodeId;
        Assert.Contains(Export.SourceMap.Entries,Entry=>Entry.NodeId==PbrDeclarationId.ToString());
        var Hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Export.Hlsl)));
        // ADR-0069 (sky light SH, physical fog, Unreal filmic output, cascaded sun shadows), ADR-0075 (world-space
        // procedural grid), M0099 Phase 0 (grid anchor and fog altitude of the render origin), M0099 Phase 3 (FrameCB
        // depth-mapping packet), the M0099 conservative raster matrix and M0111 (FrameCB.LumenParams, t16/t17 and PSMain's
        // Lumen branch, tools/ShaderGraphMaintenance/edits/lumen_2026_10_24.py) and M0119 (the logarithmic depth functions
        // become the Depth subgraph, edits/depth_subgraph_2026_10_25.py) intentionally changed the Stage program; the
        // migrate/extract round trip was verified byte-identical before those edits.
        Assert.True(Hash=="0A8C045A56F0B3A18838C85ACAD86987550F4395DB687879E1CDF901A0D9A7AE",Hash);
    }
    [Fact]
    public void ProductionStage_DepthEncodingAvoidsHardwareTranscendentals() {
        // M0100/ADR-0084: a GPU's log2 and exp2 are approximations (an RTX 4060 Ti put a 4 um surface's depth 3 % off) and
        // WARP evaluates them exactly, so no WARP test can see it. The depth functions every pass shares (the Depth
        // subgraph) must stay on float arithmetic. The rest of the Stage program (fog, BRDF) does use exp and pow.
        var Hlsl=System.Text.RegularExpressions.Regex.Replace(HlslSyntaxGraphConverter.ExportWithSourceMap(ShaderGraphDependencyComposition.Resolve(GraphPath("Stage")).Composed).Hlsl,@"//[^\n]*",string.Empty);
        foreach(var Name in new[]{"RadialLog2OnePlus","RadialSafeLength","RadialG","EncodeRadialDepth","IsBeyondRadialFarPlane"}) {
            var Start=Hlsl.IndexOf(" "+Name+"(",StringComparison.Ordinal);
            Assert.True(Start>=0,Name+" is not defined by the Stage program.");
            var Open=Hlsl.IndexOf('{',Start);
            var Depth=0;
            var End=Open;
            for(;End<Hlsl.Length;End++) {
                if(Hlsl[End]=='{') { Depth++; }
                else if(Hlsl[End]=='}' && --Depth==0) { break; }
            }
            var Body=Hlsl[Start..(End+1)];
            Assert.DoesNotMatch(@"\b(log|log2|log10|exp|exp2|pow)\s*\(",Body);
        }
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
    [Fact]
    public void Subgraph_ValidatesWithoutRequestingARootArtifact() {
        var DirectoryPath=Path.Combine(Path.GetTempPath(),"CgeShaderGraphSubgraph",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        try {
            var GraphPath=Path.Combine(DirectoryPath,"Reusable.cgesubgraph");
            var ArtifactPath=Path.Combine(DirectoryPath,"Reusable.cgeshaderartifact");
            File.WriteAllText(GraphPath,ShaderGraphCanonicalization.ToCanonicalJson(CreateSubgraph()));
            Assert.Equal(0,global::ShaderGraphCli.Run(["validate",GraphPath]));
            var OriginalError=Console.Error;
            using var Error=new StringWriter();
            try {
                Console.SetError(Error);
                Assert.Equal(1,global::ShaderGraphCli.Run(["build",GraphPath,ArtifactPath]));
            } finally { Console.SetError(OriginalError); }
            Assert.Contains("Subgraph",Error.ToString(),StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(ArtifactPath));
        } finally { Directory.Delete(DirectoryPath,true); }
    }
    /// <summary>
    /// A failure is one line in MSBuild's canonical form at the graph, so a build lists its cause. Diagnostics were written
    /// as "SubgraphMissing: ..." and an unreadable file as a stack trace, which MSBuild's Exec logs only as messages: the
    /// error list showed nothing but MSB3073 (the command exited with code 1).
    /// </summary>
    [Fact]
    public void Failures_AreCanonicalBuildErrorsAtTheGraph() {
        var DirectoryPath=Path.Combine(Path.GetTempPath(),"CgeShaderGraphErrors",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        try {
            var Missing=Path.Combine(DirectoryPath,"Missing.cgesubgraph");
            var Document=CreateSubgraph();
            Document.RootSubgraphId=StableId.New();
            File.WriteAllText(Missing,ShaderGraphCanonicalization.ToCanonicalJson(Document));
            var Empty=Path.Combine(DirectoryPath,"Empty.cgesubgraph");
            File.WriteAllText(Empty,string.Empty);
            foreach(var (GraphPath,Expected) in new[]{(Missing,": error SubgraphMissing: Root subgraph is missing."),(Empty,": error: ")}) {
                var OriginalError=Console.Error;
                using var Error=new StringWriter();
                try {
                    Console.SetError(Error);
                    Assert.Equal(1,global::ShaderGraphCli.Run(["validate",GraphPath]));
                } finally { Console.SetError(OriginalError); }
                var Lines=Error.ToString().Split(new[]{Environment.NewLine},StringSplitOptions.RemoveEmptyEntries);
                Assert.Contains(Lines,Line=>Line.StartsWith(GraphPath+Expected,StringComparison.Ordinal));
                Assert.DoesNotContain(Lines,Line=>Line.TrimStart().StartsWith("at ",StringComparison.Ordinal));
            }
        } finally { Directory.Delete(DirectoryPath,true); }
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
    private static ShaderGraphDocument CreateSubgraph() {
        var RootId=StableId.New();
        var ValueId=StableId.New();
        var OutputId=StableId.New();
        return new ShaderGraphDocument {
            Target=new ShaderGraphTarget { Kind=ShaderGraphTargetKind.Model,Name="Subgraph" },
            RootSubgraphId=RootId,
            Subgraphs={new ShaderGraphSubgraph {
                Id=RootId,
                Name="Reusable",
                Outputs={new ShaderGraphPort { Name="Out",Type=ShaderGraphValueType.Float4 }},
                Nodes={
                    new ShaderGraphNode { Id=ValueId,Type="ConstantFloat4",Arguments={new ShaderGraphArgument { Name="Value",Value="0,0,0,1" }}},
                    new ShaderGraphNode { Id=OutputId,Type="OutputFloat4" }
                },
                Edges={new ShaderGraphEdge { Id=StableId.New(),FromNodeId=ValueId,FromPort="Out",ToNodeId=OutputId,ToPort="In" }}
            }}
        };
    }
}
