using System;
using System.Collections.Generic;
using System.Linq;
namespace CSharpGameEngine.ShaderGraph;
public static class ShaderGraphValidator {
    public static ShaderGraphValidationResult Validate(ShaderGraphDocument Document) {
        var Result = new ShaderGraphValidationResult();
        var SubgraphById = new Dictionary<StableId, ShaderGraphSubgraph>();
        foreach (var Subgraph in Document.Subgraphs) {
            if (SubgraphById.ContainsKey(Subgraph.Id)) {
                continue;
            }
            SubgraphById.Add(Subgraph.Id,Subgraph);
        }
        if (Document.RootSubgraphId == default || !SubgraphById.ContainsKey(Document.RootSubgraphId)) {
            Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.SubgraphMissing, ShaderGraphDiagnosticSeverity.Error, "Root subgraph is missing.", SubgraphId: Document.RootSubgraphId));
        }
        var Contract = BuiltInNodeCatalog.GetEntryPointContract(Document.Target.Kind);
        if (Document.EntryPoint is not null && (Document.EntryPoint.Name != Contract.Name || Document.EntryPoint.ReturnType != Contract.ReturnType || Document.EntryPoint.TargetKind != Contract.TargetKind)) {
            Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.EntryPointMismatch, ShaderGraphDiagnosticSeverity.Error, "Document entry point does not match catalog contract."));
        }
        foreach (var Subgraph in Document.Subgraphs) {
            ValidateSubgraph(Subgraph, SubgraphById, Result);
        }
        ValidateSubgraphRecursion(SubgraphById, Result);
        return Result;
    }
    private static void ValidateSubgraph(ShaderGraphSubgraph Subgraph, Dictionary<StableId, ShaderGraphSubgraph> SubgraphById, ShaderGraphValidationResult Result) {
        var NodeById = Subgraph.Nodes.ToDictionary(Node => Node.Id, Node => Node);
        var InEdges = new Dictionary<StableId, List<ShaderGraphEdge>>();
        foreach (var Edge in Subgraph.Edges) {
            if (!InEdges.ContainsKey(Edge.ToNodeId)) {
                InEdges[Edge.ToNodeId] = new List<ShaderGraphEdge>();
            }
            InEdges[Edge.ToNodeId].Add(Edge);
        }
        foreach (var Region in Subgraph.Regions) {
            foreach (var NodeId in Region.NodeIds) {
                if (!NodeById.ContainsKey(NodeId)) {
                    Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.RegionInvalidNode, ShaderGraphDiagnosticSeverity.Error, $"Region '{Region.Id}' references unknown node '{NodeId}'.", SubgraphId: Subgraph.Id));
                }
            }
        }
        foreach (var Node in Subgraph.Nodes) {
            if(HlslSyntaxGraphContract.IsSyntaxNode(Node)) {
                if(!HlslSyntaxGraphContract.TryValidateNode(Node,out var SyntaxError)) {
                    Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.UnknownNode,ShaderGraphDiagnosticSeverity.Error,SyntaxError,Node.Id,Subgraph.Id));
                    continue;
                }
                foreach(var RequiredPort in HlslSyntaxGraphContract.GetRequiredChildPorts(Node)) {
                    if(!InEdges.TryGetValue(Node.Id,out var SyntaxIncoming)||!SyntaxIncoming.Any(Edge=>Edge.ToPort==RequiredPort)) {
                        Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.MissingRequiredPort,ShaderGraphDiagnosticSeverity.Error,$"HLSL syntax node '{Node.Id}' missing child port '{RequiredPort}'.",Node.Id,Subgraph.Id));
                    }
                }
                continue;
            }
            if (BuiltInNodeCatalog.IsForRange(Node.Type)) {
                Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.ForRangeForbidden, ShaderGraphDiagnosticSeverity.Error, "ForRange is disabled without static max bounds.", NodeId: Node.Id, SubgraphId: Subgraph.Id));
                continue;
            }
            if (!TryGetNodePorts(Node, SubgraphById, out var NodePorts, Result, Subgraph.Id)) {
                continue;
            }
            foreach (var Port in NodePorts.Where(Port => Port.Direction == ShaderGraphPortDirection.Input && Port.IsRequired)) {
                if (!InEdges.TryGetValue(Node.Id, out var Incoming) || !Incoming.Any(Edge => Edge.ToNodeId == Node.Id && string.Equals(Edge.ToPort, Port.Name, StringComparison.Ordinal))) {
                    Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.MissingRequiredPort, ShaderGraphDiagnosticSeverity.Error, $"Node '{Node.Id}' missing required port '{Port.Name}'.", NodeId: Node.Id, SubgraphId: Subgraph.Id));
                }
            }
        }
        foreach (var Edge in Subgraph.Edges) {
            if (!NodeById.TryGetValue(Edge.FromNodeId, out var FromNode)) {
                Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.UnknownNode, ShaderGraphDiagnosticSeverity.Error, $"Edge '{Edge.Id}' references missing source node '{Edge.FromNodeId}'."));
                continue;
            }
            if (!NodeById.TryGetValue(Edge.ToNodeId, out var ToNode)) {
                Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.UnknownNode, ShaderGraphDiagnosticSeverity.Error, $"Edge '{Edge.Id}' references missing destination node '{Edge.ToNodeId}'."));
                continue;
            }
            if(HlslSyntaxGraphContract.IsSyntaxNode(FromNode)||HlslSyntaxGraphContract.IsSyntaxNode(ToNode)) {
                if(!HlslSyntaxGraphContract.TryValidateEdge(FromNode,Edge,ToNode,out var SyntaxEdgeError)) {
                    Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.TypeMismatch,ShaderGraphDiagnosticSeverity.Error,SyntaxEdgeError,ToNode.Id,Subgraph.Id));
                }
                continue;
            }
            if (!TryGetNodePorts(FromNode, SubgraphById, out var FromPorts, Result, Subgraph.Id) || !TryGetNodePorts(ToNode, SubgraphById, out var ToPorts, Result, Subgraph.Id)) {
                continue;
            }
            var FromPort = FromPorts.FirstOrDefault(Port => string.Equals(Port.Name, Edge.FromPort, StringComparison.Ordinal) && Port.Direction == ShaderGraphPortDirection.Output);
            var ToPort = ToPorts.FirstOrDefault(Port => string.Equals(Port.Name, Edge.ToPort, StringComparison.Ordinal) && Port.Direction == ShaderGraphPortDirection.Input);
            if (FromPort is null) {
                Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.UnknownPort, ShaderGraphDiagnosticSeverity.Error, $"Edge '{Edge.Id}' references unknown output port '{Edge.FromPort}' on '{FromNode.Id}'.", NodeId: FromNode.Id, SubgraphId: Subgraph.Id));
            }
            if (ToPort is null) {
                Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.UnknownPort, ShaderGraphDiagnosticSeverity.Error, $"Edge '{Edge.Id}' references unknown input port '{Edge.ToPort}' on '{ToNode.Id}'.", NodeId: ToNode.Id, SubgraphId: Subgraph.Id));
            }
            if (FromPort is not null && ToPort is not null && FromPort.Type != ToPort.Type) {
                Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.TypeMismatch, ShaderGraphDiagnosticSeverity.Error, $"Type mismatch on edge '{Edge.Id}': {FromPort.Type} -> {ToPort.Type}.", NodeId: ToNode.Id, SubgraphId: Subgraph.Id));
            }
        }
        ValidateCycle(Subgraph, Subgraph.Edges, Result);
    }
    private static bool TryGetNodePorts(ShaderGraphNode Node, Dictionary<StableId, ShaderGraphSubgraph> SubgraphById, out List<ShaderGraphPortTemplate> Ports, ShaderGraphValidationResult Result, StableId ContextSubgraphId) {
        if (Node.Type == BuiltInNodeCatalog.SubgraphCallType) {
            var Argument = Node.GetArgumentValue("SubgraphId");
            if (!StableId.TryParse(Argument, out var TargetSubgraphId)) {
                Ports = new List<ShaderGraphPortTemplate>();
                Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.SubgraphMissing, ShaderGraphDiagnosticSeverity.Error, $"SubgraphCall '{Node.Id}' has invalid SubgraphId '{Argument}'.", NodeId: Node.Id, SubgraphId: ContextSubgraphId));
                return false;
            }
            if (!SubgraphById.TryGetValue(TargetSubgraphId, out var TargetSubgraph)) {
                Ports = new List<ShaderGraphPortTemplate>();
                Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.SubgraphMissing, ShaderGraphDiagnosticSeverity.Error, $"SubgraphCall '{Node.Id}' references missing subgraph '{TargetSubgraphId}'.", NodeId: Node.Id, SubgraphId: ContextSubgraphId));
                return false;
            }
            Ports = new List<ShaderGraphPortTemplate>(TargetSubgraph.Inputs.Count + TargetSubgraph.Outputs.Count);
            foreach (var Port in TargetSubgraph.Inputs.OrderBy(Port => Port.Name, StringComparer.Ordinal)) {
                Ports.Add(new ShaderGraphPortTemplate { Name = Port.Name, Direction = ShaderGraphPortDirection.Input, Type = Port.Type, IsRequired = true });
            }
            foreach (var Port in TargetSubgraph.Outputs.OrderBy(Port => Port.Name, StringComparer.Ordinal)) {
                Ports.Add(new ShaderGraphPortTemplate { Name = Port.Name, Direction = ShaderGraphPortDirection.Output, Type = Port.Type, IsRequired = true });
            }
            return true;
        }
        if (!BuiltInNodeCatalog.TryGetDefinition(Node.Type, out var Definition)) {
            Ports = new List<ShaderGraphPortTemplate>();
            Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.UnknownNode, ShaderGraphDiagnosticSeverity.Error, $"Unknown node type '{Node.Type}'.", NodeId: Node.Id, SubgraphId: ContextSubgraphId));
            return false;
        }
        Ports = new List<ShaderGraphPortTemplate>(Definition.Ports);
        return true;
    }
    private static void ValidateCycle(ShaderGraphSubgraph Subgraph, List<ShaderGraphEdge> Edges, ShaderGraphValidationResult Result) {
        var InCount = Subgraph.Nodes.ToDictionary(Node => Node.Id, _ => 0);
        var Adj = Subgraph.Nodes.ToDictionary(Node => Node.Id, _ => new List<StableId>());
        foreach (var Edge in Edges) {
            if (!InCount.ContainsKey(Edge.FromNodeId) || !InCount.ContainsKey(Edge.ToNodeId)) {
                continue;
            }
            InCount[Edge.ToNodeId]++;
            Adj[Edge.FromNodeId].Add(Edge.ToNodeId);
        }
        var State = new Dictionary<StableId, int>(InCount.Count);
        foreach (var NodeId in InCount.Keys) {
            State[NodeId] = 0;
        }
        foreach (var NodeId in InCount.Keys.Where(NodeId => State[NodeId] == 0)) {
            if (DetectCycle(NodeId, Adj, State)) {
                Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.CycleDetected, ShaderGraphDiagnosticSeverity.Error, $"Cycle detected in subgraph '{Subgraph.Id}'.", SubgraphId: Subgraph.Id));
                return;
            }
        }
    }
    private static bool DetectCycle(StableId NodeId, Dictionary<StableId, List<StableId>> Adj, Dictionary<StableId, int> State) {
        State[NodeId] = 1;
        foreach (var Next in Adj[NodeId]) {
            if (State[Next] == 0) {
                if (DetectCycle(Next, Adj, State)) {
                    return true;
                }
            } else if (State[Next] == 1) {
                return true;
            }
        }
        State[NodeId] = 2;
        return false;
    }
    private static void ValidateSubgraphRecursion(Dictionary<StableId, ShaderGraphSubgraph> SubgraphById, ShaderGraphValidationResult Result) {
        var CallGraph = new Dictionary<StableId, List<StableId>>();
        foreach (var SubgraphId in SubgraphById.Keys) {
            CallGraph[SubgraphId] = new List<StableId>();
        }
        foreach (var Subgraph in SubgraphById.Values) {
            foreach (var Node in Subgraph.Nodes.Where(Node => Node.Type == BuiltInNodeCatalog.SubgraphCallType)) {
                var Argument = Node.GetArgumentValue("SubgraphId");
                if (StableId.TryParse(Argument, out var TargetSubgraphId) && SubgraphById.ContainsKey(TargetSubgraphId)) {
                    CallGraph[Subgraph.Id].Add(TargetSubgraphId);
                }
            }
        }
        var State = new Dictionary<StableId, int>(CallGraph.Count);
        foreach (var SubgraphId in CallGraph.Keys) {
            State[SubgraphId] = 0;
        }
        foreach (var SubgraphId in CallGraph.Keys.OrderBy(Id => Id.ToString())) {
            if (State[SubgraphId] != 0) {
                continue;
            }
            if (DetectSubgraphCycle(SubgraphId, CallGraph, State)) {
                Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.SubgraphRecursion, ShaderGraphDiagnosticSeverity.Error, $"Subgraph recursion detected at '{SubgraphId}'.", SubgraphId: SubgraphId));
                return;
            }
        }
    }
    private static bool DetectSubgraphCycle(StableId SubgraphId, Dictionary<StableId, List<StableId>> CallGraph, Dictionary<StableId, int> State) {
        State[SubgraphId] = 1;
        foreach (var Target in CallGraph[SubgraphId]) {
            if (State[Target] == 1) {
                return true;
            }
            if (State[Target] == 0 && DetectSubgraphCycle(Target, CallGraph, State)) {
                return true;
            }
        }
        State[SubgraphId] = 2;
        return false;
    }
}
