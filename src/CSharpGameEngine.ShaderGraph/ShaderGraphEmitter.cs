using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
namespace CSharpGameEngine.ShaderGraph;
public sealed class ShaderGraphSourceMapEntry {
    public string NodeId { get; set; } = string.Empty;
    public int StartLine { get; set; }
    public int EndLine { get; set; }
}
public sealed class ShaderGraphSourceMap {
    public List<ShaderGraphSourceMapEntry> Entries { get; set; } = new List<ShaderGraphSourceMapEntry>();
}
public sealed class ShaderGraphEmitResult {
    public bool Success { get; set; }
    public string Hlsl { get; set; } = string.Empty;
    public ShaderGraphSourceMap SourceMap { get; set; } = new ShaderGraphSourceMap();
    public List<ShaderGraphDiagnostic> Diagnostics { get; set; } = new List<ShaderGraphDiagnostic>();
}
public static class ShaderGraphEmitter {
    private sealed class TypedValue {
        public ShaderGraphValueType Type { get; set; }
        public string Expression { get; set; } = string.Empty;
    }
    private sealed class ShaderGraphIrInstruction {
        public StableId NodeId { get; set; }
        public ShaderGraphNode Node { get; set; } = new ShaderGraphNode();
        public BuiltInNodeDefinition Definition { get; set; } = new BuiltInNodeDefinition("", ShaderGraphBuiltInCategory.Utility, ShaderGraphOpcode.Constant, new List<ShaderGraphPortTemplate>());
        public List<ShaderGraphPortTemplate> Ports { get; set; } = new List<ShaderGraphPortTemplate>();
    }
    private sealed class OutputCandidate {
        public string? NameHint { get; set; }
        public ShaderGraphValueType Type { get; set; }
        public string Expression { get; set; } = string.Empty;
    }
    private const string MainFunctionPrefix = "SG";
    public static ShaderGraphEmitResult Emit(ShaderGraphDocument Document) {
        var Validation = ShaderGraphValidator.Validate(Document);
        if (!Validation.IsValid) {
            return new ShaderGraphEmitResult {
                Success = false,
                Diagnostics = Validation.Diagnostics
            };
        }
        if (!Document.Subgraphs.ToDictionary(Subgraph => Subgraph.Id, Subgraph => Subgraph).TryGetValue(Document.RootSubgraphId, out var RootSubgraph)) {
            return new ShaderGraphEmitResult {
                Success = false,
                Diagnostics = new List<ShaderGraphDiagnostic> {
                    new ShaderGraphDiagnostic(ShaderGraphDiagnosticCode.SubgraphMissing, ShaderGraphDiagnosticSeverity.Error, "Root subgraph is missing.")
                }
            };
        }
        var SubgraphById = Document.Subgraphs.ToDictionary(Subgraph => Subgraph.Id, Subgraph => Subgraph);
        var Contract = BuiltInNodeCatalog.GetEntryPointContract(Document.Target.Kind, Document.EntryPoint);
        var Lines = new List<string>();
        var SourceMap = new ShaderGraphSourceMap();
        var Emitted = new HashSet<StableId>();
        EmitSubgraphFunctions(Document.RootSubgraphId, SubgraphById, Lines, SourceMap, Emitted);
        EmitEntryPoint(Document.RootSubgraphId, RootSubgraph, Contract, Lines);
        return new ShaderGraphEmitResult {
            Success = true,
            Hlsl = string.Join(Environment.NewLine, Lines),
            SourceMap = SourceMap,
            Diagnostics = Validation.Diagnostics
        };
    }
    private static void EmitSubgraphFunctions(StableId SubgraphId, Dictionary<StableId, ShaderGraphSubgraph> SubgraphById, List<string> Lines, ShaderGraphSourceMap SourceMap, HashSet<StableId> Emitted) {
        if (!SubgraphById.TryGetValue(SubgraphId, out var Subgraph) || !Emitted.Add(Subgraph.Id)) {
            return;
        }
        var Called = new List<StableId>();
        foreach (var Node in Subgraph.Nodes.Where(Node => Node.Type == BuiltInNodeCatalog.SubgraphCallType).OrderBy(Node => Node.Id.ToString(), StringComparer.Ordinal)) {
            var IdText = Node.GetArgumentValue("SubgraphId");
            if (StableId.TryParse(IdText, out var CalledId) && SubgraphById.ContainsKey(CalledId)) {
                Called.Add(CalledId);
            }
        }
        foreach (var CalledId in Called.OrderBy(Id => Id.ToString(), StringComparer.Ordinal)) {
            EmitSubgraphFunctions(CalledId, SubgraphById, Lines, SourceMap, Emitted);
        }
        EmitSubgraphFunction(Subgraph, SubgraphById, Lines, SourceMap);
    }
    private static void EmitSubgraphFunction(ShaderGraphSubgraph Subgraph, Dictionary<StableId, ShaderGraphSubgraph> SubgraphById, List<string> Lines, ShaderGraphSourceMap SourceMap) {
        var OrderedNodes = TopologicalOrder(Subgraph);
        var InEdges = Subgraph.Edges.GroupBy(Edge => Edge.ToNodeId).ToDictionary(Group => Group.Key, Group => Group.ToList());
        var Instructions = BuildInstructions(Subgraph);
        var ValueMap = new Dictionary<(StableId, string), TypedValue>();
        var OutputCandidates = new List<OutputCandidate>();
        var FunctionName = GetSubgraphFunctionName(Subgraph.Id);
        var Parameters = new List<string>();
        foreach (var Input in Subgraph.Inputs.OrderBy(Port => Port.Name, StringComparer.Ordinal)) {
            Parameters.Add($"in {GetTypeName(Input.Type)} {GetParameterName(Input.Name)}");
        }
        foreach (var Output in Subgraph.Outputs.OrderBy(Port => Port.Name, StringComparer.Ordinal)) {
            Parameters.Add($"out {GetTypeName(Output.Type)} {GetOutputParameterName(Subgraph.Id, Output.Name)}");
        }
        if (Parameters.Count == 0) {
            Lines.Add($"void {FunctionName}(){{");
        } else {
            Lines.Add($"void {FunctionName}({string.Join(",", Parameters)}){{");
        }
        foreach (var Instruction in Instructions.OrderBy(Item => OrderedNodes.IndexOf(Item.Node))) {
            EmitInstruction(Subgraph, Instruction, SubgraphById, InEdges, ValueMap, OutputCandidates, Lines, SourceMap);
        }
        foreach (var Output in Subgraph.Outputs.OrderBy(Port => Port.Name, StringComparer.Ordinal)) {
            var Assignment = ResolveSubgraphOutputExpression(Output, OutputCandidates);
            var ParameterName = GetOutputParameterName(Subgraph.Id, Output.Name);
            AddLine(Lines, SourceMap, string.Empty, $"    {ParameterName}={Assignment};");
        }
        Lines.Add("}");
    }
    private static void EmitEntryPoint(StableId RootSubgraphId, ShaderGraphSubgraph RootSubgraph, ShaderGraphEntryPointContract Contract, List<string> Lines) {
        var ReturnType = GetTypeName(Contract.ReturnType);
        var FunctionName = Contract.Name;
        Lines.Add($"{ReturnType} {FunctionName}(){{");
        if (RootSubgraph.Outputs.Count == 0) {
            Lines.Add($"    return {GetDefaultValue(Contract.ReturnType)};");
            Lines.Add("}");
            return;
        }
        foreach (var Output in RootSubgraph.Outputs.OrderBy(Port => Port.Name, StringComparer.Ordinal)) {
            Lines.Add($"    {GetTypeName(Output.Type)} {GetOutputParameterName(RootSubgraphId, Output.Name)}={GetDefaultValue(Output.Type)};");
        }
        var CallArgs = string.Join(",", RootSubgraph.Outputs.OrderBy(Port => Port.Name, StringComparer.Ordinal).Select(Output => GetOutputParameterName(RootSubgraphId, Output.Name)));
        if (!string.IsNullOrEmpty(CallArgs)) {
            Lines.Add($"    {GetSubgraphFunctionName(RootSubgraphId)}({CallArgs});");
        } else {
            Lines.Add($"    {GetSubgraphFunctionName(RootSubgraphId)}();");
        }
        var First = RootSubgraph.Outputs.OrderBy(Port => Port.Name, StringComparer.Ordinal).First();
        var Value = GetOutputParameterName(RootSubgraphId, First.Name);
        var Casted = CastValue(Contract.ReturnType, First.Type, Value);
        Lines.Add($"    return {Casted};");
        Lines.Add("}");
    }
    private static List<ShaderGraphIrInstruction> BuildInstructions(ShaderGraphSubgraph Subgraph) {
        var Instructions = new List<ShaderGraphIrInstruction>(Subgraph.Nodes.Count);
        foreach (var Node in Subgraph.Nodes.OrderBy(Node => Node.Id.ToString(), StringComparer.Ordinal)) {
            if (Node.Type == BuiltInNodeCatalog.SubgraphCallType) {
                var CallDefinition = BuiltInNodeCatalog.GetDefinition(BuiltInNodeCatalog.SubgraphCallType);
                if (CallDefinition is not null) {
                    Instructions.Add(new ShaderGraphIrInstruction {
                        NodeId = Node.Id,
                        Node = Node,
                        Definition = CallDefinition,
                        Ports = new List<ShaderGraphPortTemplate>(CallDefinition.Ports)
                    });
                }
                continue;
            }
            if (BuiltInNodeCatalog.TryGetDefinition(Node.Type, out var Definition)) {
                Instructions.Add(new ShaderGraphIrInstruction {
                    NodeId = Node.Id,
                    Node = Node,
                    Definition = Definition,
                    Ports = new List<ShaderGraphPortTemplate>(Definition.Ports)
                });
            }
        }
        return Instructions;
    }
    private static void EmitInstruction(ShaderGraphSubgraph Subgraph, ShaderGraphIrInstruction Instruction, Dictionary<StableId, ShaderGraphSubgraph> SubgraphById, Dictionary<StableId, List<ShaderGraphEdge>> InEdges, Dictionary<(StableId, string), TypedValue> ValueMap, List<OutputCandidate> OutputCandidates, List<string> Lines, ShaderGraphSourceMap SourceMap) {
        if (Instruction.Definition.IsSubgraphCall) {
            EmitSubgraphCall(Instruction.Node, SubgraphById, InEdges, ValueMap, Lines, SourceMap);
            return;
        }
        if (Instruction.Definition.IsOutputNode) {
            EmitOutputNode(Instruction.Node, InEdges, ValueMap, OutputCandidates);
            return;
        }
        var Output = Instruction.Ports.FirstOrDefault(Port => Port.Direction == ShaderGraphPortDirection.Output);
        if (Output is null) {
            return;
        }
        var Expression = BuildExpression(Instruction, InEdges, ValueMap);
        if (string.IsNullOrEmpty(Expression)) {
            return;
        }
        var Variable = GetNodeVariableName(Instruction.NodeId);
        AddLine(Lines, SourceMap, Instruction.NodeId.ToString(), $"    {GetTypeName(Output.Type)} {Variable}={Expression};");
        ValueMap[(Instruction.NodeId, Output.Name)] = new TypedValue { Type = Output.Type, Expression = Variable };
    }
    private static void EmitOutputNode(ShaderGraphNode Node, Dictionary<StableId, List<ShaderGraphEdge>> InEdges, Dictionary<(StableId, string), TypedValue> ValueMap, List<OutputCandidate> OutputCandidates) {
        var Type = Node.Type switch {
            "OutputFloat" => ShaderGraphValueType.Float,
            "OutputFloat2" => ShaderGraphValueType.Float2,
            "OutputFloat3" => ShaderGraphValueType.Float3,
            "OutputFloat4" => ShaderGraphValueType.Float4,
            _ => ShaderGraphValueType.Unknown
        };
        if (Type == ShaderGraphValueType.Unknown) {
            return;
        }
        var Input = GetNodeInputValue(Node.Id, "In", InEdges, ValueMap, Type);
        OutputCandidates.Add(new OutputCandidate {
            NameHint = Node.Name,
            Type = Type,
            Expression = Input.Expression
        });
    }
    private static void EmitSubgraphCall(ShaderGraphNode Node, Dictionary<StableId, ShaderGraphSubgraph> SubgraphById, Dictionary<StableId, List<ShaderGraphEdge>> InEdges, Dictionary<(StableId, string), TypedValue> ValueMap, List<string> Lines, ShaderGraphSourceMap SourceMap) {
        if (!StableId.TryParse(Node.GetArgumentValue("SubgraphId"), out var SubgraphId) || !SubgraphById.TryGetValue(SubgraphId, out var Subgraph)) {
            return;
        }
        var Arguments = new List<string>();
        foreach (var Input in Subgraph.Inputs.OrderBy(Port => Port.Name, StringComparer.Ordinal)) {
            var InputValue = GetNodeInputValue(Node.Id, Input.Name, InEdges, ValueMap, Input.Type);
            Arguments.Add(InputValue.Expression);
        }
        foreach (var Output in Subgraph.Outputs.OrderBy(Port => Port.Name, StringComparer.Ordinal)) {
            var OutVar = GetNodeOutputVariableName(Node.Id, Output.Name);
            Arguments.Add(OutVar);
            ValueMap[(Node.Id, Output.Name)] = new TypedValue { Type = Output.Type, Expression = OutVar };
        }
        AddLine(Lines, SourceMap, Node.Id.ToString(), $"    {GetSubgraphFunctionName(SubgraphId)}({string.Join(",", Arguments)});");
    }
    private static string ResolveSubgraphOutputExpression(ShaderGraphPort Output, List<OutputCandidate> OutputCandidates) {
        foreach (var Candidate in OutputCandidates.Where(Candidate => string.Equals(Candidate.NameHint, Output.Name, StringComparison.Ordinal))) {
            if (Candidate.Type == Output.Type) {
                return Candidate.Expression;
            }
        }
        foreach (var Candidate in OutputCandidates.Where(Candidate => Candidate.Type == Output.Type)) {
            return Candidate.Expression;
        }
        return GetDefaultValue(Output.Type);
    }
    private static string BuildExpression(ShaderGraphIrInstruction Instruction, Dictionary<StableId, List<ShaderGraphEdge>> InEdges, Dictionary<(StableId, string), TypedValue> ValueMap) {
        var Definition = Instruction.Definition;
        if (Definition.Opcode == ShaderGraphOpcode.Constant) {
            var Type = Definition.Ports.First(Port => Port.Direction == ShaderGraphPortDirection.Output).Type;
            var ValueText = Instruction.Node.GetArgumentValue("Value");
            return BuildLiteral(Type, ValueText);
        }
        if (Definition.Opcode == ShaderGraphOpcode.Dot) {
            var A = GetNodeInputValue(Instruction.NodeId, "A", InEdges, ValueMap).Expression;
            var B = GetNodeInputValue(Instruction.NodeId, "B", InEdges, ValueMap).Expression;
            return $"dot({A},{B})";
        }
        if (Definition.Opcode == ShaderGraphOpcode.Length) {
            var In = GetNodeInputValue(Instruction.NodeId, "In", InEdges, ValueMap).Expression;
            return $"length({In})";
        }
        if (Definition.Opcode == ShaderGraphOpcode.Normalize) {
            var In = GetNodeInputValue(Instruction.NodeId, "In", InEdges, ValueMap).Expression;
            return $"normalize({In})";
        }
        if (Definition.Opcode == ShaderGraphOpcode.Construct) {
            var X = GetNodeInputValue(Instruction.NodeId, "X", InEdges, ValueMap).Expression;
            var Y = GetNodeInputValue(Instruction.NodeId, "Y", InEdges, ValueMap).Expression;
            if (Definition.Ports.Any(Port => Port.Name == "Z")) {
                var Z = GetNodeInputValue(Instruction.NodeId, "Z", InEdges, ValueMap).Expression;
                return $"{GetTypeName(GetOutputType(Instruction))}({X},{Y},{Z})";
            }
            return $"{GetTypeName(GetOutputType(Instruction))}({X},{Y})";
        }
        if (Definition.Opcode == ShaderGraphOpcode.Swizzle) {
            var In = GetNodeInputValue(Instruction.NodeId, "In", InEdges, ValueMap).Expression;
            return $"float2({In}.x,{In}.y)";
        }
        var Left = GetNodeInputValue(Instruction.NodeId, "A", InEdges, ValueMap).Expression;
        var Right = GetNodeInputValue(Instruction.NodeId, "B", InEdges, ValueMap).Expression;
        var T = GetNodeInputValue(Instruction.NodeId, "T", InEdges, ValueMap, ShaderGraphValueType.Float).Expression;
        return Definition.Opcode switch {
            ShaderGraphOpcode.Add => $"{Left}+{Right}",
            ShaderGraphOpcode.Subtract => $"{Left}-{Right}",
            ShaderGraphOpcode.Multiply => $"{Left}*{Right}",
            ShaderGraphOpcode.Divide => $"{Left}/{Right}",
            ShaderGraphOpcode.Lerp => $"lerp({Left},{Right},{T})",
            _ => string.Empty
        };
    }
    private static ShaderGraphValueType GetOutputType(ShaderGraphIrInstruction Instruction) {
        foreach (var Port in Instruction.Ports) {
            if (Port.Direction == ShaderGraphPortDirection.Output) {
                return Port.Type;
            }
        }
        return ShaderGraphValueType.Float;
    }
    private static TypedValue GetNodeInputValue(StableId NodeId, string PortName, Dictionary<StableId, List<ShaderGraphEdge>> InEdges, Dictionary<(StableId, string), TypedValue> ValueMap, ShaderGraphValueType FallbackType = ShaderGraphValueType.Float) {
        if (!InEdges.TryGetValue(NodeId, out var Edges)) {
            return new TypedValue { Type = FallbackType, Expression = GetDefaultValue(FallbackType) };
        }
        var Edge = Edges.FirstOrDefault(Edge => string.Equals(Edge.ToPort, PortName, StringComparison.Ordinal));
        if (Edge is null) {
            return new TypedValue { Type = FallbackType, Expression = GetDefaultValue(FallbackType) };
        }
        return ValueMap.TryGetValue((Edge.FromNodeId, Edge.FromPort), out var Value) ? Value : new TypedValue { Type = FallbackType, Expression = GetDefaultValue(FallbackType) };
    }
    private static string BuildLiteral(ShaderGraphValueType Type, string? ValueText) {
        return Type switch {
            ShaderGraphValueType.Float => FormatFloat(ValueText),
            ShaderGraphValueType.Float2 => FormatVector(2, ValueText),
            ShaderGraphValueType.Float3 => FormatVector(3, ValueText),
            ShaderGraphValueType.Float4 => FormatVector(4, ValueText),
            ShaderGraphValueType.Int => ParseInt(ValueText).ToString(CultureInfo.InvariantCulture),
            ShaderGraphValueType.Bool => ParseBool(ValueText).ToString().ToLowerInvariant(),
            _ => GetDefaultValue(Type)
        };
    }
    private static string FormatVector(int Channels, string? ValueText) {
        var Raw = SplitConstants(ValueText).ToList();
        if (Raw.Count == 0) {
            Raw.Add("0");
        }
        if (Raw.Count == 1) {
            var Single = Raw[0];
            while (Raw.Count < Channels) {
                Raw.Add(Single);
            }
        } else if (Raw.Count < Channels) {
            var Last = Raw[Raw.Count-1];
            while (Raw.Count < Channels) {
                Raw.Add(Last);
            }
        } else if (Raw.Count > Channels) {
            Raw = Raw.Take(Channels).ToList();
        }
        var Values = string.Join(",", Raw.Select(Part => FormatFloat(Part)));
        return $"{GetVectorTypeName(Channels)}({Values})";
    }
    private static IEnumerable<string> SplitConstants(string? ValueText) {
        if (string.IsNullOrWhiteSpace(ValueText)) {
            return Array.Empty<string>();
        }
        return ValueText!.Split(new[] { ',', ' ', ';', '\\', ')', '(' },StringSplitOptions.RemoveEmptyEntries).Select(Part=>Part.Trim()).Where(Part=>!string.IsNullOrWhiteSpace(Part));
    }
    private static bool ParseBool(string? ValueText) {
        return bool.TryParse(ValueText, out var Value) && Value;
    }
    private static int ParseInt(string? ValueText) {
        if (int.TryParse(ValueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var Value)) {
            return Value;
        }
        return 0;
    }
    private static string FormatFloat(string? ValueText) {
        if (float.TryParse(ValueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var Value)) {
            var Text = Value.ToString("R", CultureInfo.InvariantCulture);
            if (Text.IndexOf('.')<0&&Text.IndexOf('e')<0&&Text.IndexOf('E')<0) {
                Text += ".0";
            }
            if (!Text.EndsWith("f",StringComparison.Ordinal)&&!Text.EndsWith("F",StringComparison.Ordinal)) {
                Text += "f";
            }
            return Text;
        }
        return "0.0f";
    }
    private static string CastValue(ShaderGraphValueType ReturnType, ShaderGraphValueType SourceType, string Expression) {
        if (SourceType == ReturnType) {
            return Expression;
        }
        if (ReturnType == ShaderGraphValueType.Float4 && SourceType == ShaderGraphValueType.Float) {
            return $"float4({Expression},0.0f,0.0f,1.0f)";
        }
        return Expression;
    }
    private static string GetTypeName(ShaderGraphValueType Type) {
        return Type switch {
            ShaderGraphValueType.Float => "float",
            ShaderGraphValueType.Float2 => "float2",
            ShaderGraphValueType.Float3 => "float3",
            ShaderGraphValueType.Float4 => "float4",
            ShaderGraphValueType.Int => "int",
            ShaderGraphValueType.Bool => "bool",
            _ => "float"
        };
    }
    private static string GetVectorTypeName(int Channels) {
        return Channels switch {
            2 => "float2",
            3 => "float3",
            4 => "float4",
            _ => "float"
        };
    }
    private static string GetDefaultValue(ShaderGraphValueType Type) {
        return Type switch {
            ShaderGraphValueType.Float => "0.0f",
            ShaderGraphValueType.Float2 => "float2(0.0f,0.0f)",
            ShaderGraphValueType.Float3 => "float3(0.0f,0.0f,0.0f)",
            ShaderGraphValueType.Float4 => "float4(0.0f,0.0f,0.0f,0.0f)",
            ShaderGraphValueType.Int => "0",
            ShaderGraphValueType.Bool => "false",
            _ => "0"
        };
    }
    private static string GetNodeVariableName(StableId NodeId) {
        return $"v_{NodeId.ToString().Replace("-", string.Empty)}";
    }
    private static string GetOutputParameterName(StableId SubgraphId, string Name) {
        return $"o_{SubgraphId.ToString().Replace("-", string.Empty)}_{SanitizeIdentifier(Name)}";
    }
    private static string GetNodeOutputVariableName(StableId NodeId, string Name) {
        return $"n_{NodeId.ToString().Replace("-", string.Empty)}_{SanitizeIdentifier(Name)}";
    }
    private static string GetParameterName(string Name) {
        return SanitizeIdentifier(Name);
    }
    private static string GetSubgraphFunctionName(StableId SubgraphId) {
        return $"{MainFunctionPrefix}_{SubgraphId.ToString().Replace("-", string.Empty)}";
    }
    private static string SanitizeIdentifier(string Name) {
        if (string.IsNullOrWhiteSpace(Name)) {
            return "Value";
        }
        var Builder = new StringBuilder(Name.Length);
        foreach (var Char in Name) {
            Builder.Append(char.IsLetterOrDigit(Char) || Char == '_' ? Char : '_');
        }
        var Result = Builder.ToString();
        if (Result.Length == 0) {
            return "Value";
        }
        if (char.IsDigit(Result[0])) {
            return $"P{Result}";
        }
        return Result;
    }
    private static void AddLine(List<string> Lines, ShaderGraphSourceMap SourceMap, string NodeId, string Line) {
        var Start = Lines.Count + 1;
        Lines.Add(Line);
        if (!string.IsNullOrWhiteSpace(NodeId)) {
            SourceMap.Entries.Add(new ShaderGraphSourceMapEntry {
                NodeId = NodeId,
                StartLine = Start,
                EndLine = Start
            });
        }
    }
    private static List<ShaderGraphNode> TopologicalOrder(ShaderGraphSubgraph Subgraph) {
        var InCounts = Subgraph.Nodes.ToDictionary(Node => Node.Id, _ => 0);
        var Adjacency = Subgraph.Nodes.ToDictionary(Node => Node.Id, _ => new List<StableId>());
        var NodeIds = new HashSet<StableId>(Subgraph.Nodes.Select(Node=>Node.Id));
        foreach (var Edge in Subgraph.Edges) {
            if (!NodeIds.Contains(Edge.FromNodeId) || !NodeIds.Contains(Edge.ToNodeId)) {
                continue;
            }
            InCounts[Edge.ToNodeId]++;
            Adjacency[Edge.FromNodeId].Add(Edge.ToNodeId);
        }
        var Queue = InCounts.Where(Pair => Pair.Value == 0).Select(Pair => Pair.Key).OrderBy(Id => Id.ToString(), StringComparer.Ordinal).ToList();
        var Ordered = new List<ShaderGraphNode>(Subgraph.Nodes.Count);
        while (Queue.Count > 0) {
            var NodeId = Queue[0];
            Queue.RemoveAt(0);
            Ordered.Add(Subgraph.Nodes.First(Node => Node.Id == NodeId));
            foreach (var Next in Adjacency[NodeId].OrderBy(Id => Id.ToString(), StringComparer.Ordinal)) {
                InCounts[Next]--;
                if (InCounts[Next] == 0) {
                    Queue.Add(Next);
                    Queue = Queue.OrderBy(Id => Id.ToString(), StringComparer.Ordinal).ToList();
                }
            }
        }
        foreach (var Node in Subgraph.Nodes) {
            if (!Ordered.Contains(Node)) {
                Ordered.Add(Node);
            }
        }
        return Ordered;
    }
}
