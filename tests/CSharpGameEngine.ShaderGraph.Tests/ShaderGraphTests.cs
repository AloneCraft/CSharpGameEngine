using Xunit;
namespace CSharpGameEngine.ShaderGraph.Tests;
public class ShaderGraphTests {
    [Fact]
    public void CanonicalRoundTrip_PreservesSemantics() {
        var Graph = BuildRoundTripGraph();
        var Json = ShaderGraphCanonicalization.ToCanonicalJson(Graph);
        var Parsed = ShaderGraphCanonicalization.FromJson(Json);
        Assert.NotNull(Parsed);
        var ParsedJson = ShaderGraphCanonicalization.ToCanonicalJson(Parsed!);
        Assert.Equal(Json, ParsedJson);
    }
    /// <summary>
    /// A JSON null where the graph needs a value is malformed JSON that names its path. System.Text.Json put it into the
    /// model, and validation, emission and the canonical form then threw a NullReferenceException that named nothing (a
    /// mutation fuzz of the Triangle graph hit it in 161 of 3000 documents).
    /// </summary>
    [Theory]
    [InlineData("target", "$.target")]
    [InlineData("nodes[0].type", "$.subgraphs[0].nodes[0].type")]
    [InlineData("nodes[0]", "$.subgraphs[0].nodes[0]")]
    [InlineData("nodes[0].arguments", "$.subgraphs[0].nodes[0].arguments")]
    [InlineData("edges", "$.subgraphs[0].edges")]
    [InlineData("layout.nodes[0]", "$.layout.nodes[0]")]
    public void FromJson_RejectsANullTheGraphNeeds(string Where, string Path) {
        var Graph = System.Text.Json.Nodes.JsonNode.Parse(ShaderGraphCanonicalization.ToCanonicalJson(BuildLayoutAwareGraph("null")))!;
        var Subgraph = Graph["subgraphs"]![0]!;
        switch (Where) {
            case "target": Graph["target"] = null; break;
            case "nodes[0].type": Subgraph["nodes"]![0]!["type"] = null; break;
            case "nodes[0]": Subgraph["nodes"]![0] = null; break;
            case "nodes[0].arguments": Subgraph["nodes"]![0]!["arguments"] = null; break;
            case "edges": Subgraph["edges"] = null; break;
            case "layout.nodes[0]": Graph["layout"]!["nodes"]![0] = null; break;
        }
        var Error = Assert.Throws<System.Text.Json.JsonException>(() => ShaderGraphCanonicalization.FromJson(Graph.ToJsonString()));
        Assert.Contains(" at " + Path + ".", Error.Message);
    }
    /// <summary>
    /// One identity twice (a node copied by hand in the JSON, a merged file) is a validation error, and emission reports it
    /// instead of throwing. The validator and the emitter keyed nodes and subgraphs by identity and threw "An item with the
    /// same key has already been added" (a structural fuzz of the Triangle graph hit it in 371 of 3000 documents).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicatedIdentity_IsAValidationError(bool Subgraph) {
        var Graph = BuildLayoutAwareGraph("duplicate");
        var Root = Graph.Subgraphs[0];
        if (Subgraph) {
            Graph.Subgraphs.Add(new ShaderGraphSubgraph { Id = Root.Id });
        } else {
            Root.Nodes.Add(new ShaderGraphNode { Id = Root.Nodes[0].Id, Type = Root.Nodes[0].Type, Arguments = { new ShaderGraphArgument { Name = "Value", Value = "2.0" } } });
        }
        Assert.Contains(ShaderGraphValidator.Validate(Graph).Diagnostics, Diagnostic => Diagnostic.Code == ShaderGraphDiagnosticCode.DuplicateIdentity);
        var Result = ShaderGraphEmitter.Emit(Graph);
        Assert.False(Result.Success);
        Assert.Contains(Result.Diagnostics, Diagnostic => Diagnostic.Code == ShaderGraphDiagnosticCode.DuplicateIdentity);
    }
    [Fact]
    public void SemanticHash_IgnoresEditorLayout() {
        var Left=BuildLayoutAwareGraph("left");
        var Right=ShaderGraphCanonicalization.FromJson(ShaderGraphCanonicalization.ToCanonicalJson(Left))!;
        Right.Layout=new ShaderGraphEditorLayout();
        var LeftHash = ShaderGraphCanonicalization.ComputeSemanticHash(Left);
        var RightHash = ShaderGraphCanonicalization.ComputeSemanticHash(Right);
        Assert.Equal(LeftHash, RightHash);
    }
    [Fact]
    public void Dependencies_AreCanonicalAndContributeToSemanticHash() {
        var Graph=BuildLayoutAwareGraph("dependencies");
        Graph.Dependencies.Add(new ShaderGraphDependency { Id=StableId.New(),Path="z\\Shared.cgesubgraph" });
        Graph.Dependencies.Add(new ShaderGraphDependency { Id=StableId.New(),Path="a/Lighting.cgesubgraph" });
        var Json=ShaderGraphCanonicalization.ToCanonicalJson(Graph);
        var Parsed=ShaderGraphCanonicalization.FromJson(Json)!;
        Assert.Equal(new[]{"a/Lighting.cgesubgraph","z/Shared.cgesubgraph"},Parsed.Dependencies.Select(Dependency=>Dependency.Path));
        var Before=ShaderGraphCanonicalization.ComputeSemanticHash(Parsed);
        Parsed.Dependencies[0].Path="a/Changed.cgesubgraph";
        Assert.NotEqual(Before,ShaderGraphCanonicalization.ComputeSemanticHash(Parsed));
    }
    [Fact]
    public void Validator_DetectsTypeMismatch() {
        var Graph = BuildTypeMismatchGraph();
        var Result = ShaderGraphValidator.Validate(Graph);
        Assert.Contains(Result.Diagnostics, Item => Item.Code == ShaderGraphDiagnosticCode.TypeMismatch);
    }
    [Fact]
    public void Validator_DetectsCycle() {
        var Graph = BuildCycleGraph();
        var Result = ShaderGraphValidator.Validate(Graph);
        Assert.Contains(Result.Diagnostics, Item => Item.Code == ShaderGraphDiagnosticCode.CycleDetected);
    }
    [Fact]
    public void Validator_DetectsUnknownNode() {
        var Graph = BuildUnknownNodeGraph();
        var Result = ShaderGraphValidator.Validate(Graph);
        Assert.Contains(Result.Diagnostics, Item => Item.Code == ShaderGraphDiagnosticCode.UnknownNode);
    }
    [Fact]
    public void Validator_DetectsRegionUnknownNode() {
        var Graph = BuildRegionGraph();
        var Result = ShaderGraphValidator.Validate(Graph);
        Assert.Contains(Result.Diagnostics, Item => Item.Code == ShaderGraphDiagnosticCode.RegionInvalidNode);
    }
    [Fact]
    public void Validator_DetectsSubgraphRecursion() {
        var Graph = BuildSubgraphRecursionGraph();
        var Result = ShaderGraphValidator.Validate(Graph);
        Assert.Contains(Result.Diagnostics, Item => Item.Code == ShaderGraphDiagnosticCode.SubgraphRecursion);
    }
    [Fact]
    public void Validator_DetectsForRange() {
        var Graph = BuildForRangeGraph();
        var Result = ShaderGraphValidator.Validate(Graph);
        Assert.Contains(Result.Diagnostics, Item => Item.Code == ShaderGraphDiagnosticCode.ForRangeForbidden);
    }
    [Fact]
    public void StructuredIf_EmitsBranchesReturnsAndSourceMap() {
        var Graph=BuildStructuredIfGraph();
        var Result=ShaderGraphEmitter.Emit(Graph);
        Assert.True(Result.Success,string.Join(Environment.NewLine,Result.Diagnostics.Select(Item=>Item.Message)));
        Assert.Contains("if(",Result.Hlsl);
        Assert.Contains("}else{",Result.Hlsl);
        Assert.Equal(2,Result.Hlsl.Split(new[]{"return;"},StringSplitOptions.None).Length-1);
        var StructuredNodes=Graph.Subgraphs[0].Regions[0].NodeIds.Concat(Graph.Subgraphs[0].Regions[0].ElseNodeIds).Append(Graph.Subgraphs[0].Regions[0].ControlNodeId);
        Assert.All(StructuredNodes,NodeId=>Assert.Contains(Result.SourceMap.Entries,Entry=>Entry.NodeId==NodeId.ToString()));
    }
    [Fact]
    public void StructuredForRange_EmitsStaticBoundAndLoopIndex() {
        var Graph=BuildStructuredForRangeGraph();
        var Result=ShaderGraphEmitter.Emit(Graph);
        Assert.True(Result.Success,string.Join(Environment.NewLine,Result.Diagnostics.Select(Item=>Item.Message)));
        Assert.Contains("for(int index=0;index<4&&index-0<4;index++){",Result.Hlsl);
        Assert.Contains("=index;",Result.Hlsl);
    }
    [Fact]
    public void StructuredForRange_RejectsUncoveredRange() {
        var Graph=BuildStructuredForRangeGraph();
        var Control=Graph.Subgraphs[0].Nodes.Single(Node=>Node.Type==BuiltInNodeCatalog.ForRangeRegionType);
        Control.Arguments.Single(Argument=>Argument.Name=="MaxIterations").Value="3";
        var Result=ShaderGraphValidator.Validate(Graph);
        Assert.Contains(Result.Diagnostics,Item=>Item.Code==ShaderGraphDiagnosticCode.ForRangeInvalidBounds);
    }
    [Fact]
    public void StructuredRegion_RejectsValueCrossingIfBranches() {
        var Graph=BuildStructuredIfGraph();
        var Region=Graph.Subgraphs[0].Regions[0];
        var ThenConstant=Graph.Subgraphs[0].Nodes.Single(Node=>Node.Id==Region.NodeIds[0]);
        var ElseReturn=Graph.Subgraphs[0].Nodes.Single(Node=>Node.Id==Region.ElseNodeIds[1]);
        Graph.Subgraphs[0].Edges.RemoveAll(Edge=>Edge.ToNodeId==ElseReturn.Id);
        Graph.Subgraphs[0].Edges.Add(new ShaderGraphEdge { Id=StableId.New(),FromNodeId=ThenConstant.Id,FromPort="Out",ToNodeId=ElseReturn.Id,ToPort="Value" });
        var Result=ShaderGraphValidator.Validate(Graph);
        Assert.Contains(Result.Diagnostics,Item=>Item.Code==ShaderGraphDiagnosticCode.RegionValueEscape);
    }
    [Fact]
    public void StructuredRegion_RejectsOutputNodeAndOverlap() {
        var Graph=BuildStructuredIfGraph();
        var Root=Graph.Subgraphs[0];
        var Region=Root.Regions[0];
        var Output=new ShaderGraphNode { Id=StableId.New(),Type="OutputFloat4" };
        Root.Nodes.Add(Output);
        Root.Edges.Add(new ShaderGraphEdge { Id=StableId.New(),FromNodeId=Region.NodeIds[0],FromPort="Out",ToNodeId=Output.Id,ToPort="In" });
        Region.NodeIds.Add(Output.Id);
        var ForControl=new ShaderGraphNode { Id=StableId.New(),Type=BuiltInNodeCatalog.ForRangeRegionType,Arguments={
            new ShaderGraphArgument { Name="Start",Value="0" },
            new ShaderGraphArgument { Name="EndExclusive",Value="1" },
            new ShaderGraphArgument { Name="MaxIterations",Value="1" }
        } };
        Root.Nodes.Add(ForControl);
        Root.Regions.Add(new ShaderGraphRegion { Id=StableId.New(),Kind=ShaderGraphRegionKind.ForRange,ControlNodeId=ForControl.Id,NodeIds={ Region.NodeIds[0] } });
        var Result=ShaderGraphValidator.Validate(Graph);
        Assert.Contains(Result.Diagnostics,Item=>Item.Code==ShaderGraphDiagnosticCode.RegionOverlap);
        Assert.Contains(Result.Diagnostics,Item=>Item.Code==ShaderGraphDiagnosticCode.RegionValueEscape&&Item.NodeId==Output.Id);
    }
    [Fact]
    public void StructuredReturn_RequiresMatchingGraphOutput() {
        var Graph=BuildStructuredIfGraph();
        var Return=Graph.Subgraphs[0].Nodes.First(Node=>Node.Type==BuiltInNodeCatalog.ReturnType);
        Return.Arguments.Single(Argument=>Argument.Name=="ValueType").Value="Float3";
        var Result=ShaderGraphValidator.Validate(Graph);
        Assert.Contains(Result.Diagnostics,Item=>Item.Code==ShaderGraphDiagnosticCode.ReturnInvalidScope&&Item.NodeId==Return.Id);
    }
    [Fact]
    public void LoopIndex_OutsideForRangeIsRejected() {
        var Graph=BuildStructuredForRangeGraph();
        Graph.Subgraphs[0].Regions.Clear();
        var Result=ShaderGraphValidator.Validate(Graph);
        Assert.Contains(Result.Diagnostics,Item=>Item.Code==ShaderGraphDiagnosticCode.LoopIndexInvalidScope);
    }
    [Fact]
    public void StructuredRegionFields_AreCanonicalAndSemantic() {
        var Graph=BuildStructuredIfGraph();
        var Json=ShaderGraphCanonicalization.ToCanonicalJson(Graph);
        var Parsed=ShaderGraphCanonicalization.FromJson(Json)!;
        Assert.Equal(Json,ShaderGraphCanonicalization.ToCanonicalJson(Parsed));
        var Before=ShaderGraphCanonicalization.ComputeSemanticHash(Parsed);
        Parsed.Subgraphs[0].Regions[0].ParentBranch=ShaderGraphRegionBranch.Else;
        Assert.NotEqual(Before,ShaderGraphCanonicalization.ComputeSemanticHash(Parsed));
    }
    [Fact]
    public void Emitter_ProducesDeterministicHlsl() {
        var Graph = BuildEmitterGraph();
        var First = ShaderGraphEmitter.Emit(Graph);
        var Second = ShaderGraphEmitter.Emit(Graph);
        Assert.True(First.Success);
        Assert.True(Second.Success);
        Assert.Equal(First.Hlsl, Second.Hlsl);
        Assert.NotEmpty(First.SourceMap.Entries);
    }
    [Fact]
    public void Validator_RejectsEntryPointMismatch() {
        var Graph = BuildRoundTripGraph();
        var Modified = new ShaderGraphDocument {
            Version = Graph.Version,
            Id = Graph.Id,
            Target = Graph.Target,
            EntryPoint = new ShaderGraphEntryPointContract { TargetKind = (ShaderGraphTargetKind)1, Name = "WrongName", ReturnType = ShaderGraphValueType.Float },
            RootSubgraphId = Graph.RootSubgraphId,
            Subgraphs = Graph.Subgraphs,
            Layout = Graph.Layout
        };
        var Result = ShaderGraphValidator.Validate(Modified);
        Assert.Contains(Result.Diagnostics, Item => Item.Code == ShaderGraphDiagnosticCode.EntryPointMismatch);
    }
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Validator_RejectsRetiredTargetsEvenWithoutAnEntryPoint(int Target){
        var Graph=BuildRoundTripGraph();
        Graph.Target.Kind=(ShaderGraphTargetKind)Target;
        Graph.EntryPoint=null;
        var Result=ShaderGraphValidator.Validate(Graph);
        Assert.False(Result.IsValid);
        Assert.Contains(Result.Diagnostics,Item=>Item.Code==ShaderGraphDiagnosticCode.UnknownTarget);
    }
    private static ShaderGraphDocument BuildRoundTripGraph() {
        var Root = StableId.New();
        var Node = StableId.New();
        return new ShaderGraphDocument {
            Target = new ShaderGraphTarget { Kind = ShaderGraphTargetKind.Stage, Name = "Test" },
            EntryPoint = new ShaderGraphEntryPointContract { TargetKind = ShaderGraphTargetKind.Stage, Name = "StageMain", ReturnType = ShaderGraphValueType.Float4 },
            RootSubgraphId = Root,
            Subgraphs = {
                new ShaderGraphSubgraph {
                    Id = Root,
                    Nodes = {
                        new ShaderGraphNode {
                            Id = Node,
                            Type = "ConstantFloat",
                            Arguments = { new ShaderGraphArgument { Name = "Value", Value = "1.0" } }
                        }
                    },
                    Edges = {
                        new ShaderGraphEdge {
                            Id = StableId.New(),
                            FromNodeId = Node,
                            ToNodeId = Node,
                            FromPort = "Out",
                            ToPort = "In"
                        }
                    },
                    Outputs = { new ShaderGraphPort { Name = "Out", Type = ShaderGraphValueType.Float } },
                    Regions = { }
                }
            }
        };
    }
    private static ShaderGraphDocument BuildLayoutAwareGraph(string LayoutLabel) {
        var Root = StableId.New();
        var Node = StableId.New();
        return new ShaderGraphDocument {
            RootSubgraphId = Root,
            Layout = new ShaderGraphEditorLayout {
                Nodes = { new ShaderGraphNodeLayout { NodeId = Node, X = LayoutLabel == "left" ? 1 : 2, Y = 0 } },
                Regions = { }
            },
            Subgraphs = {
                new ShaderGraphSubgraph {
                    Id = Root,
                    Inputs = { new ShaderGraphPort { Name = "In", Type = ShaderGraphValueType.Float } },
                    Outputs = { new ShaderGraphPort { Name = "Out", Type = ShaderGraphValueType.Float } },
                    Nodes = {
                        new ShaderGraphNode {
                            Id = Node,
                            Type = "ConstantFloat",
                            Arguments = { new ShaderGraphArgument { Name = "Value", Value = "1.0" } }
                        }
                    }
                }
            }
        };
    }
    private static ShaderGraphDocument BuildTypeMismatchGraph() {
        var Root = StableId.New();
        var ConstantFloat2 = StableId.New();
        var Output = StableId.New();
        return new ShaderGraphDocument {
            RootSubgraphId = Root,
            Subgraphs = {
                new ShaderGraphSubgraph {
                    Id = Root,
                    Nodes = {
                        new ShaderGraphNode { Id = ConstantFloat2, Type = "ConstantFloat2", Arguments = { new ShaderGraphArgument { Name = "Value", Value = "0.0" } } },
                        new ShaderGraphNode { Id = Output, Type = "OutputFloat" }
                    },
                    Edges = {
                        new ShaderGraphEdge {
                            Id = StableId.New(),
                            FromNodeId = ConstantFloat2,
                            FromPort = "Out",
                            ToNodeId = Output,
                            ToPort = "In"
                        }
                    }
                }
            }
        };
    }
    private static ShaderGraphDocument BuildCycleGraph() {
        var Root = StableId.New();
        var AddA = StableId.New();
        var AddB = StableId.New();
        var C1 = StableId.New();
        var C2 = StableId.New();
        return new ShaderGraphDocument {
            RootSubgraphId = Root,
            Subgraphs = {
                new ShaderGraphSubgraph {
                    Id = Root,
                    Nodes = {
                        new ShaderGraphNode { Id = AddA, Type = "AddFloat" },
                        new ShaderGraphNode { Id = AddB, Type = "AddFloat" },
                        new ShaderGraphNode { Id = C1, Type = "ConstantFloat", Arguments = { new ShaderGraphArgument { Name = "Value", Value = "1.0" } } },
                        new ShaderGraphNode { Id = C2, Type = "ConstantFloat", Arguments = { new ShaderGraphArgument { Name = "Value", Value = "2.0" } } }
                    },
                    Edges = {
                        new ShaderGraphEdge { Id = StableId.New(), FromNodeId = C1, FromPort = "Out", ToNodeId = AddA, ToPort = "A" },
                        new ShaderGraphEdge { Id = StableId.New(), FromNodeId = C2, FromPort = "Out", ToNodeId = AddA, ToPort = "B" },
                        new ShaderGraphEdge { Id = StableId.New(), FromNodeId = AddA, FromPort = "Out", ToNodeId = AddB, ToPort = "A" },
                        new ShaderGraphEdge { Id = StableId.New(), FromNodeId = AddB, FromPort = "Out", ToNodeId = AddA, ToPort = "B" }
                    }
                }
            }
        };
    }
    private static ShaderGraphDocument BuildUnknownNodeGraph() {
        var Root = StableId.New();
        return new ShaderGraphDocument {
            RootSubgraphId = Root,
            Subgraphs = {
                new ShaderGraphSubgraph {
                    Id = Root,
                    Nodes = { new ShaderGraphNode { Id = StableId.New(), Type = "NotARealNode" } }
                }
            }
        };
    }
    private static ShaderGraphDocument BuildSubgraphRecursionGraph() {
        var Root = StableId.New();
        var A = StableId.New();
        var B = StableId.New();
        return new ShaderGraphDocument {
            RootSubgraphId = Root,
            Subgraphs = {
                new ShaderGraphSubgraph {
                    Id = Root,
                    Nodes = {
                        new ShaderGraphNode {
                            Id = A,
                            Type = "SubgraphCall",
                            Arguments = { new ShaderGraphArgument { Name = "SubgraphId", Value = B.ToString() } }
                        }
                    },
                    Outputs = { new ShaderGraphPort { Name = "Out", Type = ShaderGraphValueType.Float } }
                },
                new ShaderGraphSubgraph {
                    Id = B,
                    Inputs = { new ShaderGraphPort { Name = "In", Type = ShaderGraphValueType.Float } },
                    Outputs = { new ShaderGraphPort { Name = "Out", Type = ShaderGraphValueType.Float } },
                    Nodes = {
                        new ShaderGraphNode {
                            Id = B,
                            Type = "SubgraphCall",
                            Arguments = { new ShaderGraphArgument { Name = "SubgraphId", Value = Root.ToString() } }
                        }
                    }
                }
            }
        };
    }
    private static ShaderGraphDocument BuildRegionGraph() {
        var Root = StableId.New();
        return new ShaderGraphDocument {
            RootSubgraphId = Root,
            Subgraphs = {
                new ShaderGraphSubgraph {
                    Id = Root,
                    Regions = { new ShaderGraphRegion { Id = StableId.New(), Name = "R", NodeIds = { StableId.New() } } }
                }
            }
        };
    }
    private static ShaderGraphDocument BuildForRangeGraph() {
        var Root = StableId.New();
        return new ShaderGraphDocument {
            RootSubgraphId = Root,
            Subgraphs = {
                new ShaderGraphSubgraph {
                    Id = Root,
                    Nodes = {
                        new ShaderGraphNode {
                            Id = StableId.New(),
                            Type = "ForRange"
                        }
                    }
                }
            }
        };
    }
    private static ShaderGraphDocument BuildStructuredIfGraph() {
        var Root=StableId.New();
        var Condition=StableId.New();
        var Control=StableId.New();
        var ThenValue=StableId.New();
        var ThenReturn=StableId.New();
        var ElseValue=StableId.New();
        var ElseReturn=StableId.New();
        var Region=StableId.New();
        return new ShaderGraphDocument {
            Target=new ShaderGraphTarget { Kind=ShaderGraphTargetKind.Stage },
            EntryPoint=new ShaderGraphEntryPointContract { TargetKind=ShaderGraphTargetKind.Stage,Name="StageMain",ReturnType=ShaderGraphValueType.Float4 },
            RootSubgraphId=Root,
            Subgraphs={
                new ShaderGraphSubgraph {
                    Id=Root,
                    Outputs={ new ShaderGraphPort { Name="Out",Type=ShaderGraphValueType.Float4 } },
                    Nodes={
                        new ShaderGraphNode { Id=Condition,Type="ConstantBool",Arguments={ new ShaderGraphArgument { Name="Value",Value="true" } } },
                        new ShaderGraphNode { Id=Control,Type=BuiltInNodeCatalog.IfRegionType },
                        new ShaderGraphNode { Id=ThenValue,Type="ConstantFloat4",Arguments={ new ShaderGraphArgument { Name="Value",Value="1,0,0,1" } } },
                        new ShaderGraphNode { Id=ThenReturn,Type=BuiltInNodeCatalog.ReturnType,Arguments={ new ShaderGraphArgument { Name="ValueType",Value="Float4" },new ShaderGraphArgument { Name="OutputName",Value="Out" } } },
                        new ShaderGraphNode { Id=ElseValue,Type="ConstantFloat4",Arguments={ new ShaderGraphArgument { Name="Value",Value="0,0,1,1" } } },
                        new ShaderGraphNode { Id=ElseReturn,Type=BuiltInNodeCatalog.ReturnType,Arguments={ new ShaderGraphArgument { Name="ValueType",Value="Float4" },new ShaderGraphArgument { Name="OutputName",Value="Out" } } }
                    },
                    Edges={
                        new ShaderGraphEdge { Id=StableId.New(),FromNodeId=Condition,FromPort="Out",ToNodeId=Control,ToPort="Condition" },
                        new ShaderGraphEdge { Id=StableId.New(),FromNodeId=ThenValue,FromPort="Out",ToNodeId=ThenReturn,ToPort="Value" },
                        new ShaderGraphEdge { Id=StableId.New(),FromNodeId=ElseValue,FromPort="Out",ToNodeId=ElseReturn,ToPort="Value" }
                    },
                    Regions={
                        new ShaderGraphRegion { Id=Region,Kind=ShaderGraphRegionKind.If,Name="SelectColor",ControlNodeId=Control,NodeIds={ ThenValue,ThenReturn },ElseNodeIds={ ElseValue,ElseReturn } }
                    }
                }
            }
        };
    }
    private static ShaderGraphDocument BuildStructuredForRangeGraph() {
        var Root=StableId.New();
        var Control=StableId.New();
        var Index=StableId.New();
        var Region=StableId.New();
        return new ShaderGraphDocument {
            Target=new ShaderGraphTarget { Kind=ShaderGraphTargetKind.Stage },
            EntryPoint=new ShaderGraphEntryPointContract { TargetKind=ShaderGraphTargetKind.Stage,Name="StageMain",ReturnType=ShaderGraphValueType.Float4 },
            RootSubgraphId=Root,
            Subgraphs={
                new ShaderGraphSubgraph {
                    Id=Root,
                    Outputs={ new ShaderGraphPort { Name="Out",Type=ShaderGraphValueType.Float4 } },
                    Nodes={
                        new ShaderGraphNode { Id=Control,Type=BuiltInNodeCatalog.ForRangeRegionType,Arguments={
                            new ShaderGraphArgument { Name="Start",Value="0" },
                            new ShaderGraphArgument { Name="EndExclusive",Value="4" },
                            new ShaderGraphArgument { Name="MaxIterations",Value="4" },
                            new ShaderGraphArgument { Name="Iterator",Value="index" }
                        } },
                        new ShaderGraphNode { Id=Index,Type=BuiltInNodeCatalog.LoopIndexType }
                    },
                    Regions={
                        new ShaderGraphRegion { Id=Region,Kind=ShaderGraphRegionKind.ForRange,Name="FourSamples",ControlNodeId=Control,NodeIds={ Index } }
                    }
                }
            }
        };
    }
    private static ShaderGraphDocument BuildEmitterGraph() {
        var Root = StableId.New();
        var C1 = StableId.New();
        var C2 = StableId.New();
        var Add = StableId.New();
        var Out = StableId.New();
        return new ShaderGraphDocument {
            Target = new ShaderGraphTarget { Kind = ShaderGraphTargetKind.Stage },
            EntryPoint = new ShaderGraphEntryPointContract { TargetKind = ShaderGraphTargetKind.Stage, Name = "StageMain", ReturnType = ShaderGraphValueType.Float4 },
            RootSubgraphId = Root,
            Subgraphs = {
                new ShaderGraphSubgraph {
                    Id = Root,
                    Inputs = { },
                    Outputs = { new ShaderGraphPort { Name = "Out", Type = ShaderGraphValueType.Float4 } },
                    Nodes = {
                        new ShaderGraphNode { Id = C1, Type = "ConstantFloat", Arguments = { new ShaderGraphArgument { Name = "Value", Value = "1.0" } } },
                        new ShaderGraphNode { Id = C2, Type = "ConstantFloat", Arguments = { new ShaderGraphArgument { Name = "Value", Value = "2.0" } } },
                        new ShaderGraphNode { Id = Add, Type = "AddFloat" },
                        new ShaderGraphNode { Id = Out, Type = "OutputFloat" }
                    },
                    Edges = {
                        new ShaderGraphEdge { Id = StableId.New(), FromNodeId = C1, FromPort = "Out", ToNodeId = Add, ToPort = "A" },
                        new ShaderGraphEdge { Id = StableId.New(), FromNodeId = C2, FromPort = "Out", ToNodeId = Add, ToPort = "B" },
                        new ShaderGraphEdge { Id = StableId.New(), FromNodeId = Add, FromPort = "Out", ToNodeId = Out, ToPort = "In" }
                    }
                }
            }
        };
    }
}
