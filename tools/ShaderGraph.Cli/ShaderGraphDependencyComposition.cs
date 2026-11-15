using System.Security.Cryptography;
using System.Text;
using CSharpGameEngine.ShaderGraph;
namespace CSharpGameEngine.ShaderGraph.Cli;
internal sealed class ResolvedShaderGraph {
    internal required ShaderGraphDocument Source { get;init; }
    internal required ShaderGraphDocument Composed { get;init; }
    internal required string SemanticHash { get;init; }
    internal required string DependencyHash { get;init; }
    internal required IReadOnlyList<string>DependencyPaths { get;init; }
}
internal static class ShaderGraphDependencyComposition {
    private const string TranslationUnitType="UnityShaderParser.HLSL.TranslationUnitNode";
    internal static ResolvedShaderGraph Resolve(string GraphPath) {
        return ResolveCore(Path.GetFullPath(GraphPath),new List<string>());
    }
    private static ResolvedShaderGraph ResolveCore(string GraphPath,List<string>Stack) {
        var CycleIndex=Stack.FindIndex(PathValue=>string.Equals(PathValue,GraphPath,StringComparison.OrdinalIgnoreCase));
        if(CycleIndex>=0) {
            throw new InvalidDataException("External Subgraph recursion: "+string.Join(" -> ",Stack.Skip(CycleIndex).Append(GraphPath)));
        }
        if(!File.Exists(GraphPath)) {
            throw new FileNotFoundException("Shader Graph dependency is missing.",GraphPath);
        }
        Stack.Add(GraphPath);
        try {
            var Source=Read(GraphPath);
            var Composed=Clone(Source);
            var DependencyRecords=new List<string>();
            var DependencyPaths=new List<string>();
            foreach(var Dependency in Source.Dependencies.OrderBy(Item=>NormalizePath(Item.Path),StringComparer.Ordinal)) {
                if(string.IsNullOrWhiteSpace(Dependency.Path)) {
                    throw new InvalidDataException($"Shader Graph '{GraphPath}' contains an empty dependency path.");
                }
                var DependencyPath=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(GraphPath)!,Dependency.Path));
                var Resolved=ResolveCore(DependencyPath,Stack);
                if(Resolved.Source.Id!=Dependency.Id) {
                    throw new InvalidDataException($"Shader Graph dependency identity mismatch for '{DependencyPath}': expected {Dependency.Id}, actual {Resolved.Source.Id}.");
                }
                Merge(Composed,Resolved.Composed,DependencyPath);
                var Relative=NormalizePath(Path.GetRelativePath(Path.GetDirectoryName(GraphPath)!,DependencyPath));
                DependencyRecords.Add(Relative+":"+Dependency.Id+":"+Resolved.DependencyHash);
                DependencyPaths.Add(DependencyPath);
                DependencyPaths.AddRange(Resolved.DependencyPaths);
            }
            Composed.Dependencies.Clear();
            var SemanticHash=ShaderGraphCanonicalization.ComputeSemanticHash(Source);
            var DependencyHash=DependencyRecords.Count==0?SemanticHash:Hash(string.Join("\n",new[]{SemanticHash}.Concat(DependencyRecords)));
            return new ResolvedShaderGraph {
                Source=Source,
                Composed=Composed,
                SemanticHash=SemanticHash,
                DependencyHash=DependencyHash,
                DependencyPaths=DependencyPaths.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(PathValue=>PathValue,StringComparer.OrdinalIgnoreCase).ToArray(),
            };
        } finally {
            Stack.RemoveAt(Stack.Count-1);
        }
    }
    internal static ShaderGraphDocument ExtractSyntaxDeclarations(ShaderGraphDocument Source,IReadOnlySet<int>Indices,string Name,string RelativePath,out ShaderGraphDocument Dependency) {
        var Root=Source.Subgraphs.Single(Subgraph=>Subgraph.Id==Source.RootSubgraphId);
        var TranslationUnit=FindTranslationUnit(Root)??throw new InvalidDataException("Extraction requires a typed HLSL syntax graph.");
        var TopEdges=Root.Edges.Where(Edge=>Edge.ToNodeId==TranslationUnit.Id).ToArray();
        var Selected=TopEdges.Where(Edge=>Indices.Contains(ParseIndex(Edge.ToPort))).ToArray();
        if(Selected.Length!=Indices.Count) {
            var Found=Selected.Select(Edge=>ParseIndex(Edge.ToPort)).ToHashSet();
            throw new InvalidDataException("Missing declaration indices: "+string.Join(",",Indices.Where(Index=>!Found.Contains(Index))));
        }
        var Remaining=TopEdges.Except(Selected).ToArray();
        var SelectedNodes=ReachableNodes(Root,Selected.Select(Edge=>Edge.FromNodeId));
        var RemainingNodes=ReachableNodes(Root,Remaining.Select(Edge=>Edge.FromNodeId));
        var Shared=SelectedNodes.Intersect(RemainingNodes).ToArray();
        if(Shared.Length>0) {
            throw new InvalidDataException("Cannot extract declarations with shared syntax nodes: "+string.Join(",",Shared));
        }
        var DependencyRootId=StableId.New();
        var DependencyTranslationId=StableId.New();
        var DependencyTranslation=new ShaderGraphNode {
            Id=DependencyTranslationId,
            Type=TranslationUnit.Type,
            Name=TranslationUnit.Name,
            Arguments=TranslationUnit.Arguments.Where(Argument=>Argument.Name==HlslSyntaxGraphContract.ClrTypeArgument||Selected.Any(Edge=>Argument.Name==HlslSyntaxGraphContract.ChildPortPrefix+Edge.ToPort)).Select(CloneArgument).ToList(),
        };
        var DependencyRoot=new ShaderGraphSubgraph {
            Id=DependencyRootId,
            Name=Name,
            Nodes=Root.Nodes.Where(Node=>SelectedNodes.Contains(Node.Id)).Select(CloneNode).Prepend(DependencyTranslation).ToList(),
            Edges=Root.Edges.Where(Edge=>SelectedNodes.Contains(Edge.FromNodeId)&&SelectedNodes.Contains(Edge.ToNodeId)).Select(CloneEdge).Concat(Selected.Select(Edge=>new ShaderGraphEdge {
                Id=Edge.Id,
                FromNodeId=Edge.FromNodeId,
                FromPort=Edge.FromPort,
                ToNodeId=DependencyTranslationId,
                ToPort=Edge.ToPort,
            })).ToList(),
            Regions=Root.Regions.Where(Region=>Region.NodeIds.Concat(Region.ElseNodeIds).All(SelectedNodes.Contains)&&(Region.ControlNodeId.IsEmpty||SelectedNodes.Contains(Region.ControlNodeId))).Select(CloneRegion).ToList(),
        };
        var DependencyLayout=new ShaderGraphEditorLayout {
            Nodes=Source.Layout?.Nodes.Where(Node=>SelectedNodes.Contains(Node.NodeId)).Select(CloneLayout).Append(new ShaderGraphNodeLayout { NodeId=DependencyTranslationId,X=0,Y=0 }).ToList()??new List<ShaderGraphNodeLayout>(),
            Regions=Source.Layout?.Regions.Where(Region=>DependencyRoot.Regions.Any(Item=>Item.Id==Region.RegionId)).Select(CloneRegionLayout).ToList()??new List<ShaderGraphRegionLayout>(),
        };
        Dependency=new ShaderGraphDocument {
            Version=Source.Version,
            Id=StableId.New(),
            Target=new ShaderGraphTarget { Kind=Source.Target.Kind,Name=Source.Target.Name },
            RootSubgraphId=DependencyRootId,
            Subgraphs={ DependencyRoot },
            Layout=DependencyLayout,
        };
        var Result=Clone(Source);
        var ResultRoot=Result.Subgraphs.Single(Subgraph=>Subgraph.Id==Result.RootSubgraphId);
        var ResultTranslation=FindTranslationUnit(ResultRoot)!;
        ResultRoot.Nodes.RemoveAll(Node=>SelectedNodes.Contains(Node.Id));
        ResultRoot.Edges.RemoveAll(Edge=>SelectedNodes.Contains(Edge.FromNodeId)||SelectedNodes.Contains(Edge.ToNodeId));
        ResultTranslation.Arguments.RemoveAll(Argument=>Selected.Any(Edge=>Argument.Name==HlslSyntaxGraphContract.ChildPortPrefix+Edge.ToPort));
        ResultRoot.Regions.ForEach(Region=>{
            Region.NodeIds.RemoveAll(NodeId=>SelectedNodes.Contains(NodeId));
            Region.ElseNodeIds.RemoveAll(NodeId=>SelectedNodes.Contains(NodeId));
        });
        ResultRoot.Regions.RemoveAll(Region=>(!Region.ControlNodeId.IsEmpty&&SelectedNodes.Contains(Region.ControlNodeId))||(Region.Kind==ShaderGraphRegionKind.Comment&&Region.NodeIds.Count==0&&Region.ElseNodeIds.Count==0));
        Result.Layout?.Nodes.RemoveAll(Node=>SelectedNodes.Contains(Node.NodeId));
        Result.Dependencies.Add(new ShaderGraphDependency { Id=Dependency.Id,Path=NormalizePath(RelativePath) });
        return Result;
    }
    private static void Merge(ShaderGraphDocument Parent,ShaderGraphDocument Child,string ChildPath) {
        var ParentRoot=Parent.Subgraphs.Single(Subgraph=>Subgraph.Id==Parent.RootSubgraphId);
        var ChildRoot=Child.Subgraphs.Single(Subgraph=>Subgraph.Id==Child.RootSubgraphId);
        var ParentTranslation=FindTranslationUnit(ParentRoot);
        var ChildTranslation=FindTranslationUnit(ChildRoot);
        if(ParentTranslation is null||ChildTranslation is null) {
            foreach(var Subgraph in Child.Subgraphs) {
                if(Parent.Subgraphs.Any(Existing=>Existing.Id==Subgraph.Id)) {
                    throw new InvalidDataException($"Subgraph ID collision while composing '{ChildPath}': {Subgraph.Id}.");
                }
                Parent.Subgraphs.Add(Subgraph);
            }
            return;
        }
        var ExistingNodeIds=ParentRoot.Nodes.Select(Node=>Node.Id).ToHashSet();
        foreach(var Node in ChildRoot.Nodes.Where(Node=>Node.Id!=ChildTranslation.Id)) {
            if(!ExistingNodeIds.Add(Node.Id)) {
                throw new InvalidDataException($"Node ID collision while composing '{ChildPath}': {Node.Id}.");
            }
            ParentRoot.Nodes.Add(Node);
        }
        foreach(var Edge in ChildRoot.Edges.Where(Edge=>Edge.ToNodeId!=ChildTranslation.Id)) {
            ParentRoot.Edges.Add(Edge);
        }
        var ExistingSlots=ParentRoot.Edges.Where(Edge=>Edge.ToNodeId==ParentTranslation.Id).Select(Edge=>Edge.ToPort).ToHashSet(StringComparer.Ordinal);
        foreach(var Edge in ChildRoot.Edges.Where(Edge=>Edge.ToNodeId==ChildTranslation.Id)) {
            if(!ExistingSlots.Add(Edge.ToPort)) {
                throw new InvalidDataException($"Declaration slot collision while composing '{ChildPath}': {Edge.ToPort}.");
            }
            ParentRoot.Edges.Add(new ShaderGraphEdge {
                Id=Edge.Id,
                FromNodeId=Edge.FromNodeId,
                FromPort=Edge.FromPort,
                ToNodeId=ParentTranslation.Id,
                ToPort=Edge.ToPort,
            });
            var ChildArgument=ChildTranslation.Arguments.FirstOrDefault(Argument=>Argument.Name==HlslSyntaxGraphContract.ChildPortPrefix+Edge.ToPort);
            if(ChildArgument is not null) {
                ParentTranslation.Arguments.Add(CloneArgument(ChildArgument));
            }
        }
        ParentRoot.Regions.AddRange(ChildRoot.Regions);
        if(Parent.Layout is not null&&Child.Layout is not null) {
            Parent.Layout.Nodes.AddRange(Child.Layout.Nodes.Where(Node=>Node.NodeId!=ChildTranslation.Id));
            Parent.Layout.Regions.AddRange(Child.Layout.Regions);
        }
        foreach(var Subgraph in Child.Subgraphs.Where(Subgraph=>Subgraph.Id!=Child.RootSubgraphId)) {
            if(Parent.Subgraphs.Any(Existing=>Existing.Id==Subgraph.Id)) {
                throw new InvalidDataException($"Subgraph ID collision while composing '{ChildPath}': {Subgraph.Id}.");
            }
            Parent.Subgraphs.Add(Subgraph);
        }
    }
    private static ShaderGraphDocument Read(string PathValue) {
        return ShaderGraphCanonicalization.FromJson(File.ReadAllText(PathValue))??throw new InvalidDataException($"Shader Graph '{PathValue}' is empty.");
    }
    private static ShaderGraphDocument Clone(ShaderGraphDocument Document) {
        return ShaderGraphCanonicalization.FromJson(ShaderGraphCanonicalization.ToCanonicalJson(Document))!;
    }
    private static ShaderGraphNode?FindTranslationUnit(ShaderGraphSubgraph Root) {
        return Root.Nodes.SingleOrDefault(Node=>Node.GetArgumentValue(HlslSyntaxGraphContract.ClrTypeArgument)==TranslationUnitType);
    }
    private static HashSet<StableId>ReachableNodes(ShaderGraphSubgraph Root,IEnumerable<StableId>Roots) {
        var Incoming=Root.Edges.Where(Edge=>Edge.ToNodeId!=FindTranslationUnit(Root)?.Id).GroupBy(Edge=>Edge.ToNodeId).ToDictionary(Group=>Group.Key,Group=>Group.Select(Edge=>Edge.FromNodeId).ToArray());
        var Result=new HashSet<StableId>();
        var Pending=new Stack<StableId>(Roots);
        while(Pending.Count>0) {
            var Id=Pending.Pop();
            if(!Result.Add(Id)||!Incoming.TryGetValue(Id,out var Children)) {
                continue;
            }
            foreach(var Child in Children) {
                Pending.Push(Child);
            }
        }
        return Result;
    }
    private static int ParseIndex(string Port) {
        var Open=Port.IndexOf('[');
        var Close=Port.IndexOf(']');
        return Open>=0&&Close>Open&&int.TryParse(Port.Substring(Open+1,Close-Open-1),out var Index)?Index:-1;
    }
    private static string NormalizePath(string PathValue)=>PathValue.Replace('\\','/');
    private static string Hash(string Value) {
        using var Hasher=SHA256.Create();
        return Convert.ToHexString(Hasher.ComputeHash(Encoding.UTF8.GetBytes(Value)));
    }
    private static ShaderGraphNode CloneNode(ShaderGraphNode Node)=>new(){ Id=Node.Id,Type=Node.Type,Name=Node.Name,Arguments=Node.Arguments.Select(CloneArgument).ToList() };
    private static ShaderGraphArgument CloneArgument(ShaderGraphArgument Argument)=>new(){ Name=Argument.Name,Value=Argument.Value };
    private static ShaderGraphEdge CloneEdge(ShaderGraphEdge Edge)=>new(){ Id=Edge.Id,FromNodeId=Edge.FromNodeId,FromPort=Edge.FromPort,ToNodeId=Edge.ToNodeId,ToPort=Edge.ToPort };
    private static ShaderGraphRegion CloneRegion(ShaderGraphRegion Region)=>new(){ Id=Region.Id,Kind=Region.Kind,Name=Region.Name,ControlNodeId=Region.ControlNodeId,ParentRegionId=Region.ParentRegionId,ParentBranch=Region.ParentBranch,NodeIds=Region.NodeIds.ToList(),ElseNodeIds=Region.ElseNodeIds.ToList() };
    private static ShaderGraphNodeLayout CloneLayout(ShaderGraphNodeLayout Node)=>new(){ NodeId=Node.NodeId,X=Node.X,Y=Node.Y };
    private static ShaderGraphRegionLayout CloneRegionLayout(ShaderGraphRegionLayout Region)=>new(){ RegionId=Region.RegionId,X=Region.X,Y=Region.Y,Width=Region.Width,Height=Region.Height };
}
