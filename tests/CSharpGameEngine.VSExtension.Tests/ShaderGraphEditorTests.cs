using System.IO.Compression;
using System.Xml.Linq;
using CSharpGameEngine.ShaderGraph;
namespace CSharpGameEngine.VSExtension.Tests;
public sealed class ShaderGraphEditorTests {
    private static string SubgraphPath=>Path.Combine(AppContext.BaseDirectory,"TestAssets","ItemTemplates","CSharp","GameEngine","ShaderSubgraph","ShaderSubgraph.cgesubgraph");
    [Fact]
    public void Document_DuplicateUndoRedoAndDeletePreserveGraphIntegrity() {
        var Temporary=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".cgesubgraph");
        File.Copy(SubgraphPath,Temporary);
        try {
            var Document=ShaderGraphEditorDocument.Load(Temporary);
            var OriginalIds=Document.GetNodes().Select(Node=>Node.Id).ToArray();
            var Added=Document.Duplicate(OriginalIds);
            Assert.Equal(2,Added.Count);
            Assert.Equal(4,Document.GetNodes().Count);
            Assert.True(Document.Undo());
            Assert.Equal(2,Document.GetNodes().Count);
            Assert.True(Document.Redo());
            Assert.Equal(4,Document.GetNodes().Count);
            Assert.True(Document.Delete(Added));
            Assert.Equal(2,Document.GetNodes().Count);
            Assert.True(Document.Validate().IsValid);
        } finally { File.Delete(Temporary); }
    }
    [Fact]
    public void Document_LayoutOnlyMoveDoesNotChangeSemanticHash() {
        var Document=ShaderGraphEditorDocument.Load(SubgraphPath);
        var Node=Document.GetNodes().First();
        var Before=ShaderGraphCanonicalization.ComputeSemanticHash(Document.Graph);
        Assert.True(Document.MoveNode(Node.Id,900,700));
        var After=ShaderGraphCanonicalization.ComputeSemanticHash(Document.Graph);
        Assert.Equal(Before,After);
    }
    [Fact]
    public void Document_RejectsConnectionToUndeclaredTypedPort() {
        var Document=ShaderGraphEditorDocument.Load(SubgraphPath);
        var Nodes=Document.GetNodes();
        Assert.False(Document.TryConnect(Nodes[0].Id,Nodes[1].Id,"Missing",out var Error));
        Assert.NotEmpty(Error);
    }
    [Fact]
    public void Document_PaletteCreatesBuiltInNodeWithoutPrototype() {
        var Graph=BuildStructuredEditorGraph();
        Graph.Subgraphs[0].Nodes.Clear();
        Graph.Layout!.Nodes.Clear();
        var Document=new ShaderGraphEditorDocument(Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".cgeshadergraph"),Graph);
        Assert.Contains("ConstantBool",Document.GetPaletteNodeTypes("Bool"));
        var NodeId=Document.AddFromPrototype("ConstantBool",100,120);
        Assert.NotNull(NodeId);
        var Node=Document.GetNodes().Single();
        Assert.Equal("ConstantBool",Node.Type);
        Assert.Equal("false",Node.GetArgumentValue("Value"));
    }
    [Fact]
    public void Document_CreatesTypedIfElseReturns() {
        var Graph=BuildStructuredEditorGraph();
        var Root=Graph.Subgraphs.Single();
        var Condition=Root.Nodes.Single(Node=>Node.Type=="ConstantBool");
        var Values=Root.Nodes.Where(Node=>Node.Type=="ConstantFloat4").ToArray();
        var Document=new ShaderGraphEditorDocument(Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".cgeshadergraph"),Graph);
        var RegionId=Document.CreateIfRegion(Condition.Id,new[]{Values[0].Id},new[]{Values[1].Id},"Choose Color");
        Assert.NotNull(RegionId);
        Assert.NotNull(Document.CreateReturn(Values[0].Id));
        Assert.NotNull(Document.CreateReturn(Values[1].Id));
        var Region=Root.Regions.Single(Item=>Item.Id==RegionId);
        Assert.Equal(ShaderGraphRegionKind.If,Region.Kind);
        Assert.Equal(2,Region.NodeIds.Count);
        Assert.Equal(2,Region.ElseNodeIds.Count);
        Assert.True(Document.Validate().IsValid,string.Join(Environment.NewLine,Document.Validate().Diagnostics.Select(Item=>Item.Message)));
        Assert.True(ShaderGraphEmitter.Emit(Document.Graph).Success);
        Assert.True(Document.Undo());
        Assert.Single(Document.Graph.Subgraphs.Single().Regions[0].ElseNodeIds);
    }
    [Fact]
    public void Document_DuplicatesCompleteStructuredRegion() {
        var Graph=BuildStructuredEditorGraph();
        var Root=Graph.Subgraphs.Single();
        var Condition=Root.Nodes.Single(Node=>Node.Type=="ConstantBool");
        var Values=Root.Nodes.Where(Node=>Node.Type=="ConstantFloat4").ToArray();
        var Document=new ShaderGraphEditorDocument(Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".cgeshadergraph"),Graph);
        Assert.NotNull(Document.CreateIfRegion(Condition.Id,new[]{Values[0].Id},new[]{Values[1].Id},"Choose Color"));
        Assert.NotNull(Document.CreateReturn(Values[0].Id));
        Assert.NotNull(Document.CreateReturn(Values[1].Id));
        var OriginalNodeIds=Document.GetNodes().Select(Node=>Node.Id).ToArray();
        Assert.Equal(OriginalNodeIds.Length,Document.Duplicate(OriginalNodeIds).Count);
        Assert.Equal(2,Document.Graph.Subgraphs.Single().Regions.Count);
        Assert.True(Document.Validate().IsValid,string.Join(Environment.NewLine,Document.Validate().Diagnostics.Select(Item=>Item.Message)));
    }
    [Fact]
    public void Document_CreatesBoundedForRangeWithLoopIndex() {
        var Graph=BuildStructuredEditorGraph();
        Graph.Subgraphs[0].Nodes.RemoveAll(Node=>Node.Type!="ConstantBool");
        var Document=new ShaderGraphEditorDocument(Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".cgeshadergraph"),Graph);
        var RegionId=Document.CreateForRangeRegion(Array.Empty<StableId>(),0,8,8,"sampleIndex","Samples");
        Assert.NotNull(RegionId);
        var Root=Document.Graph.Subgraphs.Single();
        var Region=Root.Regions.Single(Item=>Item.Id==RegionId);
        Assert.Equal(ShaderGraphRegionKind.ForRange,Region.Kind);
        Assert.Contains(Root.Nodes,Node=>Node.Type==BuiltInNodeCatalog.LoopIndexType);
        Assert.True(Document.Validate().IsValid,string.Join(Environment.NewLine,Document.Validate().Diagnostics.Select(Item=>Item.Message)));
        Assert.Contains("for(int sampleIndex=0;sampleIndex<8&&sampleIndex-0<8;sampleIndex++){",ShaderGraphEmitter.Emit(Document.Graph).Hlsl);
    }
    [Fact]
    public void Document_BuiltInConnectionRequiresExactType() {
        var Graph=BuildStructuredEditorGraph();
        var Root=Graph.Subgraphs.Single();
        var Bool=Root.Nodes.Single(Node=>Node.Type=="ConstantBool");
        var Float4=Root.Nodes.First(Node=>Node.Type=="ConstantFloat4");
        var Add=new ShaderGraphNode { Id=StableId.New(),Type="AddFloat4" };
        Root.Nodes.Add(Add);
        var Document=new ShaderGraphEditorDocument(Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".cgeshadergraph"),Graph);
        Assert.False(Document.TryConnect(Bool.Id,Add.Id,"A",out var Error));
        Assert.Contains("mismatch",Error,StringComparison.OrdinalIgnoreCase);
        Assert.True(Document.TryConnect(Float4.Id,Add.Id,"A",out Error),Error);
    }
    [Fact]
    public void Vsix_ContainsEditorFactoryAndLocalizedGraphTemplates() {
        var VsixPath=Path.Combine(AppContext.BaseDirectory,"TestAssets","CSharpGameEngine.VSExtension.vsix");
        using var Archive=ZipFile.OpenRead(VsixPath);
        foreach(var Culture in new[]{"1033","1041"}) {
            Assert.NotNull(Archive.GetEntry($"ItemTemplates/CSharp/GameEngine/{Culture}/ShaderGraph/ShaderGraph.vstemplate"));
            Assert.NotNull(Archive.GetEntry($"ItemTemplates/CSharp/GameEngine/{Culture}/ShaderSubgraph/ShaderSubgraph.vstemplate"));
        }
        Assert.NotNull(Archive.GetEntry("ShaderGraphTemplates/Stage.cgeshadergraph"));
        Assert.NotNull(Archive.GetEntry("ShaderGraphTemplates/Pbr.cgesubgraph"));
        Assert.NotNull(Archive.GetEntry("ShaderGraphTemplates/Shadow.cgesubgraph"));
        Assert.NotNull(Archive.GetEntry("ShaderGraphTemplates/Pcss.cgesubgraph"));
        Assert.NotNull(Archive.GetEntry("ShaderGraphTemplates/Aces.cgesubgraph"));
        Assert.NotNull(Archive.GetEntry("ShaderGraphTemplates/Depth.cgesubgraph"));
        Assert.Null(Archive.GetEntry("ShaderGraphTemplates/Model.cgeshadergraph"));
        Assert.Null(Archive.GetEntry("ShaderGraphTemplates/Triangle.cgeshadergraph"));
        var PkgDef=Archive.GetEntry("CSharpGameEngine.VSExtension.pkgdef");
        Assert.NotNull(PkgDef);
        using var Reader=new StreamReader(PkgDef!.Open());
        var Text=Reader.ReadToEnd();
        Assert.Contains("cgeshadergraph",Text,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cgesubgraph",Text,StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ShaderGraphEditorFactory.FactoryGuidString,Text,StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void GraphTemplate_RemapsAndCopiesExternalSubgraphs() {
        var Root=Path.Combine(Path.GetTempPath(),"CgeShaderGraphTemplate",Guid.NewGuid().ToString("N"));
        var SourceDirectory=Path.Combine(Root,"source");
        var DestinationDirectory=Path.Combine(Root,"destination");
        Directory.CreateDirectory(SourceDirectory);
        Directory.CreateDirectory(DestinationDirectory);
        try {
            var DependencyRoot=StableId.New();
            var Dependency=new ShaderGraphDocument { Id=StableId.New(),RootSubgraphId=DependencyRoot,Subgraphs={new ShaderGraphSubgraph { Id=DependencyRoot,Name="PBR" }} };
            File.WriteAllText(Path.Combine(SourceDirectory,"Pbr.cgesubgraph"),ShaderGraphCanonicalization.ToCanonicalJson(Dependency));
            var GraphRoot=StableId.New();
            var Condition=StableId.New();
            var Control=StableId.New();
            var ThenNode=StableId.New();
            var ElseNode=StableId.New();
            var RegionId=StableId.New();
            var Graph=new ShaderGraphDocument {
                Id=StableId.New(),
                RootSubgraphId=GraphRoot,
                Subgraphs={new ShaderGraphSubgraph {
                    Id=GraphRoot,
                    Nodes={
                        new ShaderGraphNode { Id=Condition,Type="ConstantBool",Arguments={new ShaderGraphArgument { Name="Value",Value="true" }} },
                        new ShaderGraphNode { Id=Control,Type=BuiltInNodeCatalog.IfRegionType },
                        new ShaderGraphNode { Id=ThenNode,Type="ConstantFloat",Arguments={new ShaderGraphArgument { Name="Value",Value="1" }} },
                        new ShaderGraphNode { Id=ElseNode,Type="ConstantFloat",Arguments={new ShaderGraphArgument { Name="Value",Value="0" }} }
                    },
                    Edges={new ShaderGraphEdge { Id=StableId.New(),FromNodeId=Condition,FromPort="Out",ToNodeId=Control,ToPort="Condition" }},
                    Regions={new ShaderGraphRegion { Id=RegionId,Kind=ShaderGraphRegionKind.If,ControlNodeId=Control,NodeIds={ThenNode},ElseNodeIds={ElseNode} }}
                }},
                Dependencies={new ShaderGraphDependency { Id=Dependency.Id,Path="Pbr.cgesubgraph" }}
            };            var Destination=Path.Combine(DestinationDirectory,"MyStage.cgeshadergraph");
            var Created=new List<string>();
            var Remapped=ShaderGraphTemplateWizard.RemapBundle(Graph,SourceDirectory,Destination,Created.Add);
            Assert.Single(Created);
            Assert.Equal("MyStage.Pbr.cgesubgraph",Remapped.Dependencies.Single().Path);
            var Copied=ShaderGraphCanonicalization.FromJson(File.ReadAllText(Created.Single()))!;
            Assert.Equal(Remapped.Dependencies.Single().Id,Copied.Id);
            Assert.NotEqual(Dependency.Id,Copied.Id);
            var RemappedRoot=Remapped.Subgraphs.Single(Subgraph=>Subgraph.Id==Remapped.RootSubgraphId);
            var RemappedRegion=Assert.Single(RemappedRoot.Regions);
            Assert.Equal(ShaderGraphRegionKind.If,RemappedRegion.Kind);
            Assert.NotEqual(RegionId,RemappedRegion.Id);
            Assert.NotEqual(Control,RemappedRegion.ControlNodeId);
            Assert.Contains(RemappedRoot.Nodes,Node=>Node.Id==RemappedRegion.ControlNodeId&&Node.Type==BuiltInNodeCatalog.IfRegionType);
            Assert.Single(RemappedRegion.NodeIds);
            Assert.Single(RemappedRegion.ElseNodeIds);
        } finally { Directory.Delete(Root,true); }
    }
    [Fact]
    public void Templates_RegisterGraphAndSubgraphWizards() {
        var Root=Path.Combine(AppContext.BaseDirectory,"TestAssets","ItemTemplates","CSharp","GameEngine");
        var Graph=XDocument.Load(Path.Combine(Root,"ShaderGraph","ShaderGraph.vstemplate")).ToString();
        var Subgraph=XDocument.Load(Path.Combine(Root,"ShaderSubgraph","ShaderSubgraph.vstemplate")).ToString();
        Assert.Contains(typeof(ShaderGraphTemplateWizard).FullName!,Graph);
        Assert.Contains(typeof(ShaderSubgraphTemplateWizard).FullName!,Subgraph);
    }
    [Fact]
    public void AutomationIds_AreStableAndUnique() {
        var StaticIds=ShaderGraphAutomationIds.StaticIds;
        Assert.NotEmpty(StaticIds);
        Assert.Equal(StaticIds.Count,StaticIds.Distinct(StringComparer.Ordinal).Count());
        Assert.All(StaticIds,Id=>Assert.StartsWith("Cge.ShaderGraph.",Id,StringComparison.Ordinal));
        var NodeId=StableId.New();
        Assert.Equal($"Cge.ShaderGraph.Node.{NodeId}",ShaderGraphAutomationIds.Node(NodeId));
        Assert.Equal($"Cge.ShaderGraph.Argument.{NodeId}.Value",ShaderGraphAutomationIds.Argument(NodeId,"Value"));
    }
    /// <summary>
    /// CLI の出力は、どちらのパイプも満杯にならないように読み取る。以前は標準出力を最後まで読んでから標準エラーを読んでいたため、CLI が標準エラーへパイプバッファーを超える量（大量の診断や長い例外など）を書き込むと、読み手を永久に待つ状態になった。UI スレッドでグラフを保存・ビルドする Visual Studio もそのままハングしていた。
    /// </summary>
    [Fact]
    public async Task BuildService_ReadsALargeErrorStreamWithoutBlocking() {
        var Shell=Environment.GetEnvironmentVariable("ComSpec")??"cmd.exe";
        var Running=Task.Run(()=>ShaderGraphEditorBuildService.Run(Shell,"/d /c \"for /L %i in (1,1,3000) do @echo diagnostic line %i 1>&2\"",Path.GetTempPath()));
        Assert.Same(Running,await Task.WhenAny(Running,Task.Delay(TimeSpan.FromSeconds(60))));
        var (ExitCode,Output)=await Running;
        Assert.Equal(0,ExitCode);
        Assert.Contains("diagnostic line 3000",Output,StringComparison.Ordinal);
    }
    [Fact]
    public void BuildService_ValidatesSubgraphsWithoutPublishingArtifacts() {
        Assert.Equal(ShaderGraphEditorBuildOperation.ValidateSubgraph,ShaderGraphEditorBuildService.GetOperation(SubgraphPath));
    }
    private static ShaderGraphDocument BuildStructuredEditorGraph() {
        var RootId=StableId.New();
        var Condition=StableId.New();
        var ThenValue=StableId.New();
        var ElseValue=StableId.New();
        return new ShaderGraphDocument {
            Target=new ShaderGraphTarget { Kind=ShaderGraphTargetKind.Stage },
            EntryPoint=new ShaderGraphEntryPointContract { TargetKind=ShaderGraphTargetKind.Stage,Name="StageMain",ReturnType=ShaderGraphValueType.Float4 },
            RootSubgraphId=RootId,
            Layout=new ShaderGraphEditorLayout { Nodes={
                new ShaderGraphNodeLayout { NodeId=Condition,X=80,Y=80 },
                new ShaderGraphNodeLayout { NodeId=ThenValue,X=420,Y=40 },
                new ShaderGraphNodeLayout { NodeId=ElseValue,X=420,Y=220 }
            } },
            Subgraphs={
                new ShaderGraphSubgraph {
                    Id=RootId,
                    Outputs={ new ShaderGraphPort { Name="Out",Type=ShaderGraphValueType.Float4 } },
                    Nodes={
                        new ShaderGraphNode { Id=Condition,Type="ConstantBool",Arguments={ new ShaderGraphArgument { Name="Value",Value="true" } } },
                        new ShaderGraphNode { Id=ThenValue,Type="ConstantFloat4",Arguments={ new ShaderGraphArgument { Name="Value",Value="1,0,0,1" } } },
                        new ShaderGraphNode { Id=ElseValue,Type="ConstantFloat4",Arguments={ new ShaderGraphArgument { Name="Value",Value="0,0,1,1" } } }
                    }
                }
            }
        };
    }}
