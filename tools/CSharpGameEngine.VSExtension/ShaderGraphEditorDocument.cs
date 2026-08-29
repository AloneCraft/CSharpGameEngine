using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CSharpGameEngine.ShaderGraph;
namespace CSharpGameEngine.VSExtension;
internal sealed class ShaderGraphEditorDocument {
    private readonly Stack<string>UndoStack=new();
    private readonly Stack<string>RedoStack=new();
    internal ShaderGraphEditorDocument(string FilePath,ShaderGraphDocument Graph) {
        this.FilePath=Path.GetFullPath(FilePath);
        this.Graph=Graph;
    }
    internal string FilePath { get; private set; }
    internal ShaderGraphDocument Graph { get; private set; }
    internal bool IsDirty { get; private set; }
    internal bool CanUndo=>this.UndoStack.Count>0;
    internal bool CanRedo=>this.RedoStack.Count>0;
    internal event Action?Changed;
    internal static ShaderGraphEditorDocument Load(string FilePath) {
        var Json=File.ReadAllText(FilePath);
        var Graph=ShaderGraphCanonicalization.FromJson(Json)??throw new InvalidDataException("Shader Graph JSON is empty.");
        return new ShaderGraphEditorDocument(FilePath,Graph);
    }
    internal ShaderGraphValidationResult Validate()=>ShaderGraphValidator.Validate(this.Graph);
    internal IReadOnlyList<ShaderGraphNode>GetNodes() {
        return this.Graph.Subgraphs.Single(Subgraph=>Subgraph.Id==this.Graph.RootSubgraphId).Nodes;
    }
    internal IReadOnlyList<string>GetPaletteNodeTypes(string?Search) {
        var Query=Search??string.Empty;
        return this.GetNodes().Select(Node=>Node.Type).Distinct(StringComparer.Ordinal).Where(Type=>Type.IndexOf(Query,StringComparison.OrdinalIgnoreCase)>=0).OrderBy(Type=>Type,StringComparer.Ordinal).ToArray();
    }
    internal StableId?AddFromPrototype(string NodeType,float X,float Y) {
        var Root=this.GetRoot();
        var Prototype=Root.Nodes.FirstOrDefault(Node=>string.Equals(Node.Type,NodeType,StringComparison.Ordinal));
        if(Prototype is null) {
            return null;
        }
        this.BeginMutation();
        var Node=CloneNode(Prototype,StableId.New());
        Node.Name=Prototype.Name is null?NodeType:Prototype.Name+" Copy";
        Root.Nodes.Add(Node);
        this.EnsureLayout().Nodes.Add(new ShaderGraphNodeLayout { NodeId=Node.Id,X=X,Y=Y });
        this.EndMutation();
        return Node.Id;
    }
    internal IReadOnlyList<StableId>Duplicate(IReadOnlyCollection<StableId>NodeIds) {
        var Root=this.GetRoot();
        var Selected=new HashSet<StableId>(NodeIds);
        var Nodes=Root.Nodes.Where(Node=>Selected.Contains(Node.Id)&&Node.GetArgumentValue(HlslSyntaxGraphContract.ClrTypeArgument)!="UnityShaderParser.HLSL.TranslationUnitNode").ToArray();
        if(Nodes.Length==0) {
            return Array.Empty<StableId>();
        }
        this.BeginMutation();
        var IdMap=Nodes.ToDictionary(Node=>Node.Id,_=>StableId.New());
        foreach(var Node in Nodes) {
            var Clone=CloneNode(Node,IdMap[Node.Id]);
            Clone.Name=(Node.Name??Node.Type)+" Copy";
            Root.Nodes.Add(Clone);
        }
        foreach(var Edge in Root.Edges.Where(Edge=>IdMap.ContainsKey(Edge.FromNodeId)&&IdMap.ContainsKey(Edge.ToNodeId)).ToArray()) {
            Root.Edges.Add(new ShaderGraphEdge { FromNodeId=IdMap[Edge.FromNodeId],FromPort=Edge.FromPort,ToNodeId=IdMap[Edge.ToNodeId],ToPort=Edge.ToPort });
        }
        var Layout=this.EnsureLayout();
        foreach(var Existing in Layout.Nodes.Where(Item=>IdMap.ContainsKey(Item.NodeId)).ToArray()) {
            Layout.Nodes.Add(new ShaderGraphNodeLayout { NodeId=IdMap[Existing.NodeId],X=Existing.X+32,Y=Existing.Y+32 });
        }
        this.EndMutation();
        return IdMap.Values.ToArray();
    }
    internal bool Delete(IReadOnlyCollection<StableId>NodeIds) {
        var Root=this.GetRoot();
        var Selected=new HashSet<StableId>(NodeIds);
        Selected.RemoveWhere(Id=>Root.Nodes.Any(Node=>Node.Id==Id&&Node.GetArgumentValue(HlslSyntaxGraphContract.ClrTypeArgument)=="UnityShaderParser.HLSL.TranslationUnitNode"));
        if(Selected.Count==0||!Root.Nodes.Any(Node=>Selected.Contains(Node.Id))) {
            return false;
        }
        this.BeginMutation();
        Root.Nodes.RemoveAll(Node=>Selected.Contains(Node.Id));
        Root.Edges.RemoveAll(Edge=>Selected.Contains(Edge.FromNodeId)||Selected.Contains(Edge.ToNodeId));
        Root.Regions.ForEach(Region=>Region.NodeIds.RemoveAll(Id=>Selected.Contains(Id)));
        this.EnsureLayout().Nodes.RemoveAll(Item=>Selected.Contains(Item.NodeId));
        this.EndMutation();
        return true;
    }
    internal StableId?CreateRegion(IReadOnlyCollection<StableId>NodeIds,string Name) {
        var Root=this.GetRoot();
        var Existing=new HashSet<StableId>(Root.Nodes.Select(Node=>Node.Id));
        var Included=NodeIds.Where(Existing.Contains).Distinct().ToList();
        if(Included.Count==0) {
            return null;
        }
        this.BeginMutation();
        var Region=new ShaderGraphRegion { Id=StableId.New(),Name=Name,NodeIds=Included };
        Root.Regions.Add(Region);
        var Layout=this.EnsureLayout();
        var NodeLayouts=Layout.Nodes.Where(Item=>Included.Contains(Item.NodeId)).ToArray();
        var Left=NodeLayouts.Length==0?0:NodeLayouts.Min(Item=>Item.X)-24;
        var Top=NodeLayouts.Length==0?0:NodeLayouts.Min(Item=>Item.Y)-48;
        var Right=NodeLayouts.Length==0?320:NodeLayouts.Max(Item=>Item.X)+280;
        var Bottom=NodeLayouts.Length==0?180:NodeLayouts.Max(Item=>Item.Y)+120;
        Layout.Regions.Add(new ShaderGraphRegionLayout { RegionId=Region.Id,X=Left,Y=Top,Width=Right-Left,Height=Bottom-Top });
        this.EndMutation();
        return Region.Id;
    }    internal bool TryConnect(StableId FromNodeId,StableId ToNodeId,string ToPort,out string Error) {
        var Root=this.GetRoot();
        var From=Root.Nodes.FirstOrDefault(Node=>Node.Id==FromNodeId);
        var To=Root.Nodes.FirstOrDefault(Node=>Node.Id==ToNodeId);
        if(From is null||To is null) {
            Error="Connection endpoint is missing.";
            return false;
        }
        var Edge=new ShaderGraphEdge { FromNodeId=FromNodeId,FromPort=HlslSyntaxGraphContract.SyntaxOutputPort,ToNodeId=ToNodeId,ToPort=ToPort };
        if(!HlslSyntaxGraphContract.TryValidateEdge(From,Edge,To,out Error)) {
            return false;
        }
        this.BeginMutation();
        Root.Edges.RemoveAll(Existing=>Existing.ToNodeId==ToNodeId&&string.Equals(Existing.ToPort,ToPort,StringComparison.Ordinal));
        Root.Edges.Add(Edge);
        this.EndMutation();
        return true;
    }
    internal bool SetArgument(StableId NodeId,string Name,string Value) {
        var Node=this.GetRoot().Nodes.FirstOrDefault(Item=>Item.Id==NodeId);
        var Argument=Node?.Arguments.FirstOrDefault(Item=>string.Equals(Item.Name,Name,StringComparison.Ordinal));
        if(Argument is null||string.Equals(Argument.Value,Value,StringComparison.Ordinal)) {
            return false;
        }
        this.BeginMutation();
        Argument.Value=Value;
        this.EndMutation();
        return true;
    }
    internal bool MoveNode(StableId NodeId,float X,float Y) {
        var Layout=this.EnsureLayout();
        var NodeLayout=Layout.Nodes.FirstOrDefault(Item=>Item.NodeId==NodeId);
        if(NodeLayout is null) {
            this.BeginMutation();
            Layout.Nodes.Add(new ShaderGraphNodeLayout { NodeId=NodeId,X=X,Y=Y });
            this.EndMutation();
            return true;
        }
        if(NodeLayout.X==X&&NodeLayout.Y==Y) {
            return false;
        }
        this.BeginMutation();
        NodeLayout.X=X;
        NodeLayout.Y=Y;
        this.EndMutation();
        return true;
    }
    internal void Save(string?NewPath=null) {
        if(!string.IsNullOrWhiteSpace(NewPath)) {
            this.FilePath=Path.GetFullPath(NewPath!);
        }
        File.WriteAllText(this.FilePath,ShaderGraphCanonicalization.ToCanonicalJson(this.Graph));
        this.IsDirty=false;
        this.Changed?.Invoke();
    }
    internal void Reload() {
        var Reloaded=Load(this.FilePath);
        this.Graph=Reloaded.Graph;
        this.UndoStack.Clear();
        this.RedoStack.Clear();
        this.IsDirty=false;
        this.Changed?.Invoke();
    }
    internal bool Undo() {
        if(this.UndoStack.Count==0) {
            return false;
        }
        this.RedoStack.Push(ShaderGraphCanonicalization.ToCanonicalJson(this.Graph));
        this.Graph=ShaderGraphCanonicalization.FromJson(this.UndoStack.Pop())!;
        this.IsDirty=true;
        this.Changed?.Invoke();
        return true;
    }
    internal bool Redo() {
        if(this.RedoStack.Count==0) {
            return false;
        }
        this.UndoStack.Push(ShaderGraphCanonicalization.ToCanonicalJson(this.Graph));
        this.Graph=ShaderGraphCanonicalization.FromJson(this.RedoStack.Pop())!;
        this.IsDirty=true;
        this.Changed?.Invoke();
        return true;
    }
    private ShaderGraphSubgraph GetRoot()=>this.Graph.Subgraphs.Single(Subgraph=>Subgraph.Id==this.Graph.RootSubgraphId);
    private ShaderGraphEditorLayout EnsureLayout()=>this.Graph.Layout??(this.Graph.Layout=new ShaderGraphEditorLayout());
    private void BeginMutation() {
        this.UndoStack.Push(ShaderGraphCanonicalization.ToCanonicalJson(this.Graph));
        this.RedoStack.Clear();
    }
    private void EndMutation() {
        this.IsDirty=true;
        this.Changed?.Invoke();
    }
    private static ShaderGraphNode CloneNode(ShaderGraphNode Node,StableId Id) {
        return new ShaderGraphNode { Id=Id,Type=Node.Type,Name=Node.Name,Arguments=Node.Arguments.Select(Argument=>new ShaderGraphArgument { Name=Argument.Name,Value=Argument.Value }).ToList() };
    }
}