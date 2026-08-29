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
        return this.GetNodes().Select(Node=>Node.Type).Concat(BuiltInNodeCatalog.GetDefinitions().Where(Definition=>Definition.Category!=ShaderGraphBuiltInCategory.Control&&!Definition.IsSubgraphCall).Select(Definition=>Definition.Type)).Distinct(StringComparer.Ordinal).Where(Type=>Type.IndexOf(Query,StringComparison.OrdinalIgnoreCase)>=0).OrderBy(Type=>Type,StringComparer.Ordinal).ToArray();
    }
    internal StableId?AddFromPrototype(string NodeType,float X,float Y) {
        var Root=this.GetRoot();
        var Prototype=Root.Nodes.FirstOrDefault(Node=>string.Equals(Node.Type,NodeType,StringComparison.Ordinal));
        ShaderGraphNode Node;
        if(Prototype is not null) {
            Node=CloneNode(Prototype,StableId.New());
            Node.Name=Prototype.Name is null?NodeType:Prototype.Name+" Copy";
        } else if(BuiltInNodeCatalog.TryGetDefinition(NodeType,out var Definition)&&Definition.Category!=ShaderGraphBuiltInCategory.Control&&!Definition.IsSubgraphCall) {
            Node=new ShaderGraphNode { Id=StableId.New(),Type=NodeType,Name=NodeType };
            if(Definition.Opcode==ShaderGraphOpcode.Constant) {
                var Output=Definition.Ports.First(Port=>Port.Direction==ShaderGraphPortDirection.Output);
                Node.Arguments.Add(new ShaderGraphArgument { Name="Value",Value=Output.Type==ShaderGraphValueType.Bool?"false":"0" });
            }
        } else {
            return null;
        }
        this.BeginMutation();
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
            Root.Edges.Add(new ShaderGraphEdge { Id=StableId.New(),FromNodeId=IdMap[Edge.FromNodeId],FromPort=Edge.FromPort,ToNodeId=IdMap[Edge.ToNodeId],ToPort=Edge.ToPort });
        }
        var RegionCandidates=Root.Regions.Where(Region=>{
            var Members=Region.NodeIds.Concat(Region.ElseNodeIds).ToArray();
            return Region.Kind==ShaderGraphRegionKind.Comment
                ?Members.Length>0&&Members.All(IdMap.ContainsKey)
                :IdMap.ContainsKey(Region.ControlNodeId)&&Members.All(IdMap.ContainsKey);
        }).ToList();
        while(RegionCandidates.RemoveAll(Region=>!Region.ParentRegionId.IsEmpty&&!RegionCandidates.Any(Parent=>Parent.Id==Region.ParentRegionId))>0) { }
        var RegionIdMap=RegionCandidates.ToDictionary(Region=>Region.Id,_=>StableId.New());
        foreach(var Region in RegionCandidates) {
            Root.Regions.Add(new ShaderGraphRegion {
                Id=RegionIdMap[Region.Id],
                Kind=Region.Kind,
                Name=(Region.Name??Region.Kind.ToString())+" Copy",
                ControlNodeId=Region.ControlNodeId.IsEmpty?default:IdMap[Region.ControlNodeId],
                ParentRegionId=Region.ParentRegionId.IsEmpty?default:RegionIdMap[Region.ParentRegionId],
                ParentBranch=Region.ParentBranch,
                NodeIds=Region.NodeIds.Select(Id=>IdMap[Id]).ToList(),
                ElseNodeIds=Region.ElseNodeIds.Select(Id=>IdMap[Id]).ToList(),
            });
        }
        var Layout=this.EnsureLayout();
        foreach(var Existing in Layout.Nodes.Where(Item=>IdMap.ContainsKey(Item.NodeId)).ToArray()) {
            Layout.Nodes.Add(new ShaderGraphNodeLayout { NodeId=IdMap[Existing.NodeId],X=Existing.X+32,Y=Existing.Y+32 });
        }
        foreach(var Existing in Layout.Regions.Where(Item=>RegionIdMap.ContainsKey(Item.RegionId)).ToArray()) {
            Layout.Regions.Add(new ShaderGraphRegionLayout { RegionId=RegionIdMap[Existing.RegionId],X=Existing.X+32,Y=Existing.Y+32,Width=Existing.Width,Height=Existing.Height });
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
        var RemovedRegionIds=new HashSet<StableId>(Root.Regions.Where(Region=>!Region.ControlNodeId.IsEmpty&&Selected.Contains(Region.ControlNodeId)).Select(Region=>Region.Id));
        while(Root.Regions.Any(Region=>RemovedRegionIds.Contains(Region.ParentRegionId)&&RemovedRegionIds.Add(Region.Id))) { }
        Root.Regions.RemoveAll(Region=>RemovedRegionIds.Contains(Region.Id));
        Root.Regions.ForEach(Region=>{
            Region.NodeIds.RemoveAll(Id=>Selected.Contains(Id));
            Region.ElseNodeIds.RemoveAll(Id=>Selected.Contains(Id));
        });
        this.EnsureLayout().Nodes.RemoveAll(Item=>Selected.Contains(Item.NodeId));
        this.EnsureLayout().Regions.RemoveAll(Item=>RemovedRegionIds.Contains(Item.RegionId));
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
    }
    internal StableId?CreateIfRegion(StableId ConditionNodeId,IReadOnlyCollection<StableId>ThenNodeIds,IReadOnlyCollection<StableId>ElseNodeIds,string Name) {
        var Root=this.GetRoot();
        if(!this.TryGetNodeOutput(ConditionNodeId,out var ConditionPort,out var ConditionType)||ConditionType!=ShaderGraphValueType.Bool) {
            return null;
        }
        var Then=ThenNodeIds.Where(Id=>Id!=ConditionNodeId).Distinct().ToList();
        var Else=ElseNodeIds.Where(Id=>Id!=ConditionNodeId&&!Then.Contains(Id)).Distinct().ToList();
        if(!this.CanCreateStructuredRegion(Then.Concat(Else))) {
            return null;
        }
        this.BeginMutation();
        var Control=new ShaderGraphNode { Id=StableId.New(),Type=BuiltInNodeCatalog.IfRegionType,Name=Name };
        Root.Nodes.Add(Control);
        Root.Edges.Add(new ShaderGraphEdge { Id=StableId.New(),FromNodeId=ConditionNodeId,FromPort=ConditionPort,ToNodeId=Control.Id,ToPort="Condition" });
        var Region=new ShaderGraphRegion { Id=StableId.New(),Kind=ShaderGraphRegionKind.If,Name=Name,ControlNodeId=Control.Id,NodeIds=Then,ElseNodeIds=Else };
        Root.Regions.Add(Region);
        this.AddStructuredRegionLayout(Region,Control,Then.Concat(Else));
        this.EndMutation();
        return Region.Id;
    }
    internal StableId?CreateForRangeRegion(IReadOnlyCollection<StableId>BodyNodeIds,int Start,int EndExclusive,int MaxIterations,string Iterator,string Name) {
        var Body=BodyNodeIds.Distinct().ToList();
        if(Start<0||EndExclusive<Start||MaxIterations<=0||MaxIterations>4096||EndExclusive-Start>MaxIterations||!this.CanCreateStructuredRegion(Body)) {
            return null;
        }
        var Root=this.GetRoot();
        this.BeginMutation();
        var Control=new ShaderGraphNode { Id=StableId.New(),Type=BuiltInNodeCatalog.ForRangeRegionType,Name=Name,Arguments={
            new ShaderGraphArgument { Name="Start",Value=Start.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            new ShaderGraphArgument { Name="EndExclusive",Value=EndExclusive.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            new ShaderGraphArgument { Name="MaxIterations",Value=MaxIterations.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            new ShaderGraphArgument { Name="Iterator",Value=string.IsNullOrWhiteSpace(Iterator)?"index":Iterator }
        } };
        var LoopIndex=new ShaderGraphNode { Id=StableId.New(),Type=BuiltInNodeCatalog.LoopIndexType,Name="Loop Index" };
        Root.Nodes.Add(Control);
        Root.Nodes.Add(LoopIndex);
        Body.Insert(0,LoopIndex.Id);
        var Region=new ShaderGraphRegion { Id=StableId.New(),Kind=ShaderGraphRegionKind.ForRange,Name=Name,ControlNodeId=Control.Id,NodeIds=Body };
        Root.Regions.Add(Region);
        this.AddStructuredRegionLayout(Region,Control,Body);
        var ControlLayout=this.EnsureLayout().Nodes.Single(Item=>Item.NodeId==Control.Id);
        this.EnsureLayout().Nodes.Add(new ShaderGraphNodeLayout { NodeId=LoopIndex.Id,X=ControlLayout.X+260,Y=ControlLayout.Y+64 });
        this.EndMutation();
        return Region.Id;
    }
    internal StableId?CreateReturn(StableId SourceNodeId,string?OutputName=null) {
        var Root=this.GetRoot();
        if(!this.TryGetNodeOutput(SourceNodeId,out var SourcePort,out var SourceType)) {
            return null;
        }
        var Output=string.IsNullOrWhiteSpace(OutputName)?Root.Outputs.FirstOrDefault(Item=>Item.Type==SourceType):Root.Outputs.FirstOrDefault(Item=>Item.Type==SourceType&&Item.Name==OutputName);
        if(Output is null) {
            return null;
        }
        this.BeginMutation();
        var Return=new ShaderGraphNode { Id=StableId.New(),Type=BuiltInNodeCatalog.ReturnType,Name="Return",Arguments={
            new ShaderGraphArgument { Name="ValueType",Value=SourceType.ToString() },
            new ShaderGraphArgument { Name="OutputName",Value=Output.Name }
        } };
        Root.Nodes.Add(Return);
        Root.Edges.Add(new ShaderGraphEdge { Id=StableId.New(),FromNodeId=SourceNodeId,FromPort=SourcePort,ToNodeId=Return.Id,ToPort="Value" });
        var Region=Root.Regions.FirstOrDefault(Item=>Item.Kind!=ShaderGraphRegionKind.Comment&&(Item.NodeIds.Contains(SourceNodeId)||Item.ElseNodeIds.Contains(SourceNodeId)));
        if(Region is not null) {
            if(Region.ElseNodeIds.Contains(SourceNodeId)) { Region.ElseNodeIds.Add(Return.Id); }
            else { Region.NodeIds.Add(Return.Id); }
        }
        var Layout=this.EnsureLayout();
        var SourceLayout=Layout.Nodes.FirstOrDefault(Item=>Item.NodeId==SourceNodeId);
        Layout.Nodes.Add(new ShaderGraphNodeLayout { NodeId=Return.Id,X=(SourceLayout?.X??0)+280,Y=SourceLayout?.Y??0 });
        this.EndMutation();
        return Return.Id;
    }
    internal bool TryGetNodeOutput(StableId NodeId,out string PortName,out ShaderGraphValueType Type) {
        var Node=this.GetRoot().Nodes.FirstOrDefault(Item=>Item.Id==NodeId);
        if(Node is not null&&this.TryGetBuiltInPorts(Node,out var Ports)) {
            var Output=Ports.FirstOrDefault(Port=>Port.Direction==ShaderGraphPortDirection.Output&&Port.Type!=ShaderGraphValueType.Unknown);
            if(Output is not null) {
                PortName=Output.Name;
                Type=Output.Type;
                return true;
            }
        }
        PortName=string.Empty;
        Type=ShaderGraphValueType.Unknown;
        return false;
    }
    internal IReadOnlyList<string>GetInputPortNames(StableId NodeId) {
        var Node=this.GetRoot().Nodes.FirstOrDefault(Item=>Item.Id==NodeId);
        if(Node is null) {
            return Array.Empty<string>();
        }
        if(this.TryGetBuiltInPorts(Node,out var Ports)) {
            return Ports.Where(Port=>Port.Direction==ShaderGraphPortDirection.Input).Select(Port=>Port.Name).ToArray();
        }
        return HlslSyntaxGraphContract.GetRequiredChildPorts(Node);
    }
    internal bool TryConnect(StableId FromNodeId,StableId ToNodeId,string ToPort,out string Error) {
        var Root=this.GetRoot();
        var From=Root.Nodes.FirstOrDefault(Node=>Node.Id==FromNodeId);
        var To=Root.Nodes.FirstOrDefault(Node=>Node.Id==ToNodeId);
        if(From is null||To is null) {
            Error="Connection endpoint is missing.";
            return false;
        }
        string FromPort;
        if(this.TryGetNodeOutput(FromNodeId,out FromPort,out var FromType)&&this.TryGetBuiltInPorts(To,out var ToPorts)) {
            var Input=ToPorts.FirstOrDefault(Port=>Port.Direction==ShaderGraphPortDirection.Input&&Port.Name==ToPort);
            if(Input is null||Input.Type!=FromType) {
                Error=Input is null?"Connection target port is missing.":$"Port type mismatch: {FromType} -> {Input.Type}.";
                return false;
            }
        } else {
            FromPort=HlslSyntaxGraphContract.SyntaxOutputPort;
            var Candidate=new ShaderGraphEdge { FromNodeId=FromNodeId,FromPort=FromPort,ToNodeId=ToNodeId,ToPort=ToPort };
            if(!HlslSyntaxGraphContract.TryValidateEdge(From,Candidate,To,out Error)) {
                return false;
            }
        }
        this.BeginMutation();
        Root.Edges.RemoveAll(Existing=>Existing.ToNodeId==ToNodeId&&string.Equals(Existing.ToPort,ToPort,StringComparison.Ordinal));
        Root.Edges.Add(new ShaderGraphEdge { Id=StableId.New(),FromNodeId=FromNodeId,FromPort=FromPort,ToNodeId=ToNodeId,ToPort=ToPort });
        this.EndMutation();
        Error=string.Empty;
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
    private bool CanCreateStructuredRegion(IEnumerable<StableId>NodeIds) {
        var Root=this.GetRoot();
        var Existing=Root.Nodes.Select(Node=>Node.Id).ToHashSet();
        var StructuredMembers=Root.Regions.Where(Region=>Region.Kind!=ShaderGraphRegionKind.Comment).SelectMany(Region=>Region.NodeIds.Concat(Region.ElseNodeIds).Append(Region.ControlNodeId)).ToHashSet();
        foreach(var NodeId in NodeIds) {
            var Node=Root.Nodes.FirstOrDefault(Item=>Item.Id==NodeId);
            if(Node is null||!Existing.Contains(NodeId)||StructuredMembers.Contains(NodeId)||BuiltInNodeCatalog.IsStructuredRegionControl(Node.Type)) {
                return false;
            }
            if(BuiltInNodeCatalog.TryGetDefinition(Node.Type,out var Definition)&&Definition.IsOutputNode) {
                return false;
            }
        }
        return true;
    }
    private void AddStructuredRegionLayout(ShaderGraphRegion Region,ShaderGraphNode Control,IEnumerable<StableId>MemberIds) {
        var Layout=this.EnsureLayout();
        var Members=MemberIds.ToHashSet();
        var NodeLayouts=Layout.Nodes.Where(Item=>Members.Contains(Item.NodeId)).ToArray();
        var Left=NodeLayouts.Length==0?320:NodeLayouts.Min(Item=>Item.X);
        var Top=NodeLayouts.Length==0?120:NodeLayouts.Min(Item=>Item.Y);
        var Right=NodeLayouts.Length==0?720:NodeLayouts.Max(Item=>Item.X)+280;
        var Bottom=NodeLayouts.Length==0?360:NodeLayouts.Max(Item=>Item.Y)+140;
        Layout.Nodes.Add(new ShaderGraphNodeLayout { NodeId=Control.Id,X=Left-260,Y=Top });
        Layout.Regions.Add(new ShaderGraphRegionLayout { RegionId=Region.Id,X=Left-284,Y=Top-48,Width=Right-Left+568,Height=Bottom-Top+72 });
    }
    private bool TryGetBuiltInPorts(ShaderGraphNode Node,out IReadOnlyList<ShaderGraphPortTemplate>Ports) {
        if(BuiltInNodeCatalog.IsReturn(Node.Type)) {
            if(BuiltInNodeCatalog.TryGetReturnValueType(Node,out var ReturnType)) {
                Ports=new[]{new ShaderGraphPortTemplate { Name="Value",Direction=ShaderGraphPortDirection.Input,Type=ReturnType,IsRequired=true }};
                return true;
            }
            Ports=Array.Empty<ShaderGraphPortTemplate>();
            return false;
        }
        if(Node.Type==BuiltInNodeCatalog.SubgraphCallType&&StableId.TryParse(Node.GetArgumentValue("SubgraphId"),out var SubgraphId)) {
            var Called=this.Graph.Subgraphs.FirstOrDefault(Subgraph=>Subgraph.Id==SubgraphId);
            if(Called is not null) {
                Ports=Called.Inputs.Select(Port=>new ShaderGraphPortTemplate { Name=Port.Name,Direction=ShaderGraphPortDirection.Input,Type=Port.Type,IsRequired=true }).Concat(Called.Outputs.Select(Port=>new ShaderGraphPortTemplate { Name=Port.Name,Direction=ShaderGraphPortDirection.Output,Type=Port.Type,IsRequired=true })).ToArray();
                return true;
            }
        }
        if(BuiltInNodeCatalog.TryGetDefinition(Node.Type,out var Definition)) {
            Ports=Definition.Ports;
            return true;
        }
        Ports=Array.Empty<ShaderGraphPortTemplate>();
        return false;
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
