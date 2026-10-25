using System;
using System.Collections.Generic;
using System.Linq;
namespace CSharpGameEngine.ShaderGraph;
public static class ShaderGraphValidator {
    public static ShaderGraphValidationResult Validate(ShaderGraphDocument Document) {
        var Result = new ShaderGraphValidationResult();
        if(!Enum.IsDefined(typeof(ShaderGraphTargetKind),Document.Target.Kind)){
            Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.UnknownTarget,ShaderGraphDiagnosticSeverity.Error,"Shader Graph target is unsupported."));
            return Result;
        }
        var SubgraphById = new Dictionary<StableId, ShaderGraphSubgraph>();
        foreach (var Subgraph in Document.Subgraphs) {
            if (SubgraphById.ContainsKey(Subgraph.Id)) {
                // A copied or merged file can hold one identity twice; the emitter keys subgraphs and nodes by identity.
                Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.DuplicateIdentity, ShaderGraphDiagnosticSeverity.Error, $"Subgraph identity '{Subgraph.Id}' is duplicated.", SubgraphId: Subgraph.Id));
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
        var NodeById = new Dictionary<StableId, ShaderGraphNode>();
        foreach (var Node in Subgraph.Nodes) {
            if (NodeById.ContainsKey(Node.Id)) {
                Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.DuplicateIdentity, ShaderGraphDiagnosticSeverity.Error, $"Node identity '{Node.Id}' is duplicated.", NodeId: Node.Id, SubgraphId: Subgraph.Id));
                continue;
            }
            NodeById.Add(Node.Id, Node);
        }
        var InEdges = new Dictionary<StableId, List<ShaderGraphEdge>>();
        foreach (var Edge in Subgraph.Edges) {
            if (!InEdges.ContainsKey(Edge.ToNodeId)) {
                InEdges[Edge.ToNodeId] = new List<ShaderGraphEdge>();
            }
            InEdges[Edge.ToNodeId].Add(Edge);
        }
        ValidateRegions(Subgraph,NodeById,Result);

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
    private static void ValidateRegions(ShaderGraphSubgraph Subgraph,Dictionary<StableId,ShaderGraphNode>NodeById,ShaderGraphValidationResult Result) {
        var RegionById=new Dictionary<StableId,ShaderGraphRegion>();
        foreach(var Region in Subgraph.Regions) {
            if(Region.Id.IsEmpty||RegionById.ContainsKey(Region.Id)) {
                Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.RegionInvalidControl,ShaderGraphDiagnosticSeverity.Error,$"Region identity '{Region.Id}' is empty or duplicated.",SubgraphId:Subgraph.Id,RegionId:Region.Id));
                continue;
            }
            RegionById.Add(Region.Id,Region);
        }
        var Membership=new Dictionary<StableId,StableId>();
        var BranchMembership=new Dictionary<StableId,ShaderGraphRegionBranch>();
        var ControlOwners=new Dictionary<StableId,StableId>();
        foreach(var Region in RegionById.Values) {
            var PrimaryBranch=Region.Kind==ShaderGraphRegionKind.If?ShaderGraphRegionBranch.Then:ShaderGraphRegionBranch.Body;
            var Members=Region.NodeIds.Select(NodeId=>(NodeId,PrimaryBranch)).Concat(Region.ElseNodeIds.Select(NodeId=>(NodeId,ShaderGraphRegionBranch.Else)));
            foreach(var Member in Members) {
                var NodeId=Member.NodeId;
                if(!NodeById.ContainsKey(NodeId)) {
                    Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.RegionInvalidNode,ShaderGraphDiagnosticSeverity.Error,$"Region '{Region.Id}' references unknown node '{NodeId}'.",SubgraphId:Subgraph.Id,RegionId:Region.Id));
                    continue;
                }
                if(Region.Kind!=ShaderGraphRegionKind.Comment) {
                    if(Membership.ContainsKey(NodeId)) {
                        Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.RegionOverlap,ShaderGraphDiagnosticSeverity.Error,$"Node '{NodeId}' belongs to multiple structured regions.",NodeId,Subgraph.Id,Region.Id));
                    } else {
                        Membership.Add(NodeId,Region.Id);
                        BranchMembership.Add(NodeId,Member.Item2);
                    }
                }
            }
            if(Region.Kind==ShaderGraphRegionKind.Comment) { continue; }
            var ExpectedType=Region.Kind==ShaderGraphRegionKind.If?BuiltInNodeCatalog.IfRegionType:BuiltInNodeCatalog.ForRangeRegionType;
            if(!NodeById.TryGetValue(Region.ControlNodeId,out var Control)||Control.Type!=ExpectedType) {
                Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.RegionInvalidControl,ShaderGraphDiagnosticSeverity.Error,$"Region '{Region.Id}' requires a '{ExpectedType}' control node.",Region.ControlNodeId,Subgraph.Id,Region.Id));
            } else if(ControlOwners.ContainsKey(Region.ControlNodeId)) {
                Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.RegionInvalidControl,ShaderGraphDiagnosticSeverity.Error,$"Control node '{Region.ControlNodeId}' is assigned to multiple regions.",Region.ControlNodeId,Subgraph.Id,Region.Id));
            } else {
                ControlOwners.Add(Region.ControlNodeId,Region.Id);
            }
            if(Membership.ContainsKey(Region.ControlNodeId)) {
                Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.RegionOverlap,ShaderGraphDiagnosticSeverity.Error,$"Control node '{Region.ControlNodeId}' cannot be a region body member.",Region.ControlNodeId,Subgraph.Id,Region.Id));
            }
            if(Region.Kind==ShaderGraphRegionKind.ForRange) {
                if(Region.ElseNodeIds.Count>0) {
                    Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.RegionInvalidNode,ShaderGraphDiagnosticSeverity.Error,$"ForRange region '{Region.Id}' cannot contain an Else branch.",SubgraphId:Subgraph.Id,RegionId:Region.Id));
                }
                if(Control is not null&&(!BuiltInNodeCatalog.TryGetStaticLoopBounds(Control,out var Start,out var EndExclusive,out var MaxIterations)||Start<0||EndExclusive<Start||MaxIterations<=0||MaxIterations>4096||EndExclusive-Start>MaxIterations)) {
                    Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.ForRangeInvalidBounds,ShaderGraphDiagnosticSeverity.Error,"ForRange requires static non-negative bounds and MaxIterations in 1..4096 that covers the range.",Region.ControlNodeId,Subgraph.Id,Region.Id));
                }
            }
            ValidateParentRegion(Region,RegionById,Subgraph.Id,Result);
        }
        foreach(var Node in Subgraph.Nodes.Where(Node=>BuiltInNodeCatalog.IsStructuredRegionControl(Node.Type)&&!ControlOwners.ContainsKey(Node.Id))) {
            Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.RegionInvalidControl,ShaderGraphDiagnosticSeverity.Error,$"Structured control node '{Node.Id}' is not owned by a region.",Node.Id,Subgraph.Id));
        }
        foreach(var Node in Subgraph.Nodes.Where(Node=>BuiltInNodeCatalog.IsReturn(Node.Type))) {
            if(!BuiltInNodeCatalog.TryGetReturnValueType(Node,out var ValueType)||Subgraph.Outputs.Count==0) {
                Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.ReturnInvalidScope,ShaderGraphDiagnosticSeverity.Error,$"Return node '{Node.Id}' requires a valid ValueType and a subgraph output.",Node.Id,Subgraph.Id));
                continue;
            }
            var OutputName=Node.GetArgumentValue("OutputName");
            var Outputs=string.IsNullOrWhiteSpace(OutputName)?Subgraph.Outputs:Subgraph.Outputs.Where(Output=>Output.Name==OutputName).ToList();
            if(!Outputs.Any(Output=>Output.Type==ValueType)) {
                Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.ReturnInvalidScope,ShaderGraphDiagnosticSeverity.Error,$"Return node '{Node.Id}' does not match output '{OutputName}' and type '{ValueType}'.",Node.Id,Subgraph.Id));
            }
        }
        foreach(var Node in Subgraph.Nodes.Where(Node=>BuiltInNodeCatalog.IsLoopIndex(Node.Type))) {
            if(!Membership.TryGetValue(Node.Id,out var RegionId)||!HasForRangeAncestor(RegionId,RegionById)) {
                Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.LoopIndexInvalidScope,ShaderGraphDiagnosticSeverity.Error,$"LoopIndex node '{Node.Id}' must be inside a ForRange region.",Node.Id,Subgraph.Id));
            }
        }
        foreach(var Node in Subgraph.Nodes.Where(Node=>Membership.ContainsKey(Node.Id)&&BuiltInNodeCatalog.TryGetDefinition(Node.Type,out var Definition)&&Definition.IsOutputNode)) {
            Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.RegionValueEscape,ShaderGraphDiagnosticSeverity.Error,$"Output node '{Node.Id}' cannot be placed inside a structured region; use Return instead.",Node.Id,Subgraph.Id,Membership[Node.Id]));
        }
        ValidateRegionValueFlow(Subgraph,RegionById,Membership,BranchMembership,ControlOwners,Result);
    }
    private static void ValidateParentRegion(ShaderGraphRegion Region,Dictionary<StableId,ShaderGraphRegion>RegionById,StableId SubgraphId,ShaderGraphValidationResult Result) {
        if(Region.ParentRegionId.IsEmpty) {
            if(Region.ParentBranch!=ShaderGraphRegionBranch.Body) {
                Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.RegionInvalidControl,ShaderGraphDiagnosticSeverity.Error,$"Top-level region '{Region.Id}' must use the Body parent branch.",SubgraphId:SubgraphId,RegionId:Region.Id));
            }
            return;
        }
        if(!RegionById.TryGetValue(Region.ParentRegionId,out var Parent)||Parent.Kind==ShaderGraphRegionKind.Comment||Parent.Id==Region.Id) {
            Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.RegionInvalidControl,ShaderGraphDiagnosticSeverity.Error,$"Region '{Region.Id}' has an invalid structured parent.",SubgraphId:SubgraphId,RegionId:Region.Id));
            return;
        }
        if((Parent.Kind==ShaderGraphRegionKind.If&&Region.ParentBranch==ShaderGraphRegionBranch.Body)||(Parent.Kind==ShaderGraphRegionKind.ForRange&&Region.ParentBranch!=ShaderGraphRegionBranch.Body)) {
            Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.RegionInvalidControl,ShaderGraphDiagnosticSeverity.Error,$"Region '{Region.Id}' has an invalid parent branch.",SubgraphId:SubgraphId,RegionId:Region.Id));
        }
        var Seen=new HashSet<StableId>{Region.Id};
        var Current=Region;
        while(!Current.ParentRegionId.IsEmpty&&RegionById.TryGetValue(Current.ParentRegionId,out Current)) {
            if(!Seen.Add(Current.Id)) {
                Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.RegionInvalidControl,ShaderGraphDiagnosticSeverity.Error,$"Region parent cycle detected at '{Current.Id}'.",SubgraphId:SubgraphId,RegionId:Region.Id));
                return;
            }
        }
    }
    private static bool HasForRangeAncestor(StableId RegionId,Dictionary<StableId,ShaderGraphRegion>RegionById) {
        var Seen=new HashSet<StableId>();
        while(!RegionId.IsEmpty&&Seen.Add(RegionId)&&RegionById.TryGetValue(RegionId,out var Region)) {
            if(Region.Kind==ShaderGraphRegionKind.ForRange) { return true; }
            RegionId=Region.ParentRegionId;
        }
        return false;
    }
    private static void ValidateRegionValueFlow(ShaderGraphSubgraph Subgraph,Dictionary<StableId,ShaderGraphRegion>RegionById,Dictionary<StableId,StableId>Membership,Dictionary<StableId,ShaderGraphRegionBranch>BranchMembership,Dictionary<StableId,StableId>ControlOwners,ShaderGraphValidationResult Result) {
        (StableId RegionId,ShaderGraphRegionBranch Branch) ExecutionLocation(StableId NodeId) {
            if(Membership.TryGetValue(NodeId,out var RegionId)) {
                return (RegionId,BranchMembership[NodeId]);
            }
            if(ControlOwners.TryGetValue(NodeId,out RegionId)&&RegionById.TryGetValue(RegionId,out var Region)) {
                return (Region.ParentRegionId,Region.ParentBranch);
            }
            return (default,ShaderGraphRegionBranch.Body);
        }
        bool TryGetBranchAtAncestor((StableId RegionId,ShaderGraphRegionBranch Branch) Location,StableId Ancestor,out ShaderGraphRegionBranch Branch) {
            Branch=Location.Branch;
            if(Location.RegionId==Ancestor) {
                return true;
            }
            var CurrentId=Location.RegionId;
            var Seen=new HashSet<StableId>();
            while(!CurrentId.IsEmpty&&Seen.Add(CurrentId)&&RegionById.TryGetValue(CurrentId,out var Current)) {
                if(Current.ParentRegionId==Ancestor) {
                    Branch=Current.ParentBranch;
                    return true;
                }
                CurrentId=Current.ParentRegionId;
            }
            return false;
        }
        foreach(var Edge in Subgraph.Edges) {
            var Source=ExecutionLocation(Edge.FromNodeId);
            var Target=ExecutionLocation(Edge.ToNodeId);
            if(Source.RegionId.IsEmpty) {
                continue;
            }
            if(!TryGetBranchAtAncestor(Target,Source.RegionId,out var TargetBranch)||TargetBranch!=Source.Branch) {
                Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.RegionValueEscape,ShaderGraphDiagnosticSeverity.Error,$"Edge '{Edge.Id}' carries a value out of its structured branch in region '{Source.RegionId}'.",Edge.FromNodeId,Subgraph.Id,Source.RegionId));
            }
        }
    }
    private static bool TryGetNodePorts(ShaderGraphNode Node, Dictionary<StableId, ShaderGraphSubgraph> SubgraphById, out List<ShaderGraphPortTemplate> Ports, ShaderGraphValidationResult Result, StableId ContextSubgraphId) {
        if(BuiltInNodeCatalog.IsReturn(Node.Type)) {
            if(!BuiltInNodeCatalog.TryGetReturnValueType(Node,out var ValueType)) {
                Ports=new List<ShaderGraphPortTemplate>();
                Result.Diagnostics.Add(new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.ReturnInvalidScope,ShaderGraphDiagnosticSeverity.Error,$"Return node '{Node.Id}' has an invalid ValueType.",Node.Id,ContextSubgraphId));
                return false;
            }
            Ports=new List<ShaderGraphPortTemplate> { new ShaderGraphPortTemplate { Name="Value",Direction=ShaderGraphPortDirection.Input,Type=ValueType,IsRequired=true } };
            return true;
        }
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
        // A duplicated node identity is reported by ValidateSubgraph; the cycle check sees each identity once.
        var NodeIds = Subgraph.Nodes.Select(Node => Node.Id).Distinct().ToList();
        var InCount = NodeIds.ToDictionary(NodeId => NodeId, _ => 0);
        var Adj = NodeIds.ToDictionary(NodeId => NodeId, _ => new List<StableId>());
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
