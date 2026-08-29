using System;
using System.Collections.Generic;
namespace CSharpGameEngine.ShaderGraph;
public sealed class BuiltInNodeDefinition {
    public string Type { get; }
    public ShaderGraphBuiltInCategory Category { get; }
    public ShaderGraphOpcode Opcode { get; }
    public List<ShaderGraphPortTemplate> Ports { get; }
    public bool IsSubgraphCall { get; }
    public bool IsOutputNode { get; }
    public bool IsSubgraphEntryOutput { get; }
    public BuiltInNodeDefinition(string Type, ShaderGraphBuiltInCategory Category, ShaderGraphOpcode Opcode, List<ShaderGraphPortTemplate> Ports, bool IsSubgraphCall = false, bool IsOutputNode = false, bool IsSubgraphEntryOutput = false) {
        this.Type = Type;
        this.Category = Category;
        this.Opcode = Opcode;
        this.Ports = Ports;
        this.IsSubgraphCall = IsSubgraphCall;
        this.IsOutputNode = IsOutputNode;
        this.IsSubgraphEntryOutput = IsSubgraphEntryOutput;
    }
}
public static class BuiltInNodeCatalog {
    public const string SubgraphCallType = "SubgraphCall";
    public const string ForRangeType = "ForRange";
    public const string IfRegionType = "IfRegion";
    public const string ForRangeRegionType = "ForRangeRegion";
    public const string ReturnType = "Return";
    public const string LoopIndexType = "LoopIndex";
    private static readonly Dictionary<string, BuiltInNodeDefinition> Definitions = new(StringComparer.Ordinal);
    private static readonly Dictionary<ShaderGraphTargetKind, ShaderGraphEntryPointContract> EntryPointContracts = new Dictionary<ShaderGraphTargetKind, ShaderGraphEntryPointContract>() {
        [ShaderGraphTargetKind.Stage] = new ShaderGraphEntryPointContract { TargetKind = ShaderGraphTargetKind.Stage, Name = "StageMain", ReturnType = ShaderGraphValueType.Float4 },
        [ShaderGraphTargetKind.Model] = new ShaderGraphEntryPointContract { TargetKind = ShaderGraphTargetKind.Model, Name = "ModelMain", ReturnType = ShaderGraphValueType.Float4 },
        [ShaderGraphTargetKind.Triangle] = new ShaderGraphEntryPointContract { TargetKind = ShaderGraphTargetKind.Triangle, Name = "TriangleMain", ReturnType = ShaderGraphValueType.Float4 }
    };
    static BuiltInNodeCatalog() {
        Register(new("ConstantFloat", ShaderGraphBuiltInCategory.Constant, ShaderGraphOpcode.Constant, [
            new ShaderGraphPortTemplate { Name = "Out", Direction = ShaderGraphPortDirection.Output, Type = ShaderGraphValueType.Float, IsRequired = true }
        ], IsSubgraphEntryOutput: false));
        Register(new("ConstantFloat2", ShaderGraphBuiltInCategory.Constant, ShaderGraphOpcode.Constant, [
            new ShaderGraphPortTemplate { Name = "Out", Direction = ShaderGraphPortDirection.Output, Type = ShaderGraphValueType.Float2, IsRequired = true }
        ]));
        Register(new("ConstantFloat3", ShaderGraphBuiltInCategory.Constant, ShaderGraphOpcode.Constant, [
            new ShaderGraphPortTemplate { Name = "Out", Direction = ShaderGraphPortDirection.Output, Type = ShaderGraphValueType.Float3, IsRequired = true }
        ]));
        Register(new("ConstantFloat4", ShaderGraphBuiltInCategory.Constant, ShaderGraphOpcode.Constant, [
            new ShaderGraphPortTemplate { Name = "Out", Direction = ShaderGraphPortDirection.Output, Type = ShaderGraphValueType.Float4, IsRequired = true }
        ]));
        Register(new("ConstantInt", ShaderGraphBuiltInCategory.Constant, ShaderGraphOpcode.Constant, [
            new ShaderGraphPortTemplate { Name = "Out", Direction = ShaderGraphPortDirection.Output, Type = ShaderGraphValueType.Int, IsRequired = true }
        ]));
        Register(new("ConstantBool", ShaderGraphBuiltInCategory.Constant, ShaderGraphOpcode.Constant, [
            new ShaderGraphPortTemplate { Name = "Out", Direction = ShaderGraphPortDirection.Output, Type = ShaderGraphValueType.Bool, IsRequired = true }
        ]));
        Register(new("AddFloat", ShaderGraphBuiltInCategory.Arithmetic, ShaderGraphOpcode.Add, [
            new ShaderGraphPortTemplate { Name = "A", Direction = ShaderGraphPortDirection.Input, Type = ShaderGraphValueType.Float, IsRequired = true },
            new ShaderGraphPortTemplate { Name = "B", Direction = ShaderGraphPortDirection.Input, Type = ShaderGraphValueType.Float, IsRequired = true },
            new ShaderGraphPortTemplate { Name = "Out", Direction = ShaderGraphPortDirection.Output, Type = ShaderGraphValueType.Float, IsRequired = true }
        ]));
        Register(new("AddFloat2", ShaderGraphBuiltInCategory.Arithmetic, ShaderGraphOpcode.Add, [
            new ShaderGraphPortTemplate { Name = "A", Direction = ShaderGraphPortDirection.Input, Type = ShaderGraphValueType.Float2, IsRequired = true },
            new ShaderGraphPortTemplate { Name = "B", Direction = ShaderGraphPortDirection.Input, Type = ShaderGraphValueType.Float2, IsRequired = true },
            new ShaderGraphPortTemplate { Name = "Out", Direction = ShaderGraphPortDirection.Output, Type = ShaderGraphValueType.Float2, IsRequired = true }
        ]));
        Register(new("AddFloat3", ShaderGraphBuiltInCategory.Arithmetic, ShaderGraphOpcode.Add, [
            new ShaderGraphPortTemplate { Name = "A", Direction = ShaderGraphPortDirection.Input, Type = ShaderGraphValueType.Float3, IsRequired = true },
            new ShaderGraphPortTemplate { Name = "B", Direction = ShaderGraphPortDirection.Input, Type = ShaderGraphValueType.Float3, IsRequired = true },
            new ShaderGraphPortTemplate { Name = "Out", Direction = ShaderGraphPortDirection.Output, Type = ShaderGraphValueType.Float3, IsRequired = true }
        ]));
        Register(new("AddFloat4", ShaderGraphBuiltInCategory.Arithmetic, ShaderGraphOpcode.Add, [
            new ShaderGraphPortTemplate { Name = "A", Direction = ShaderGraphPortDirection.Input, Type = ShaderGraphValueType.Float4, IsRequired = true },
            new ShaderGraphPortTemplate { Name = "B", Direction = ShaderGraphPortDirection.Input, Type = ShaderGraphValueType.Float4, IsRequired = true },
            new ShaderGraphPortTemplate { Name = "Out", Direction = ShaderGraphPortDirection.Output, Type = ShaderGraphValueType.Float4, IsRequired = true }
        ]));
        Register(new("SubtractFloat", ShaderGraphBuiltInCategory.Arithmetic, ShaderGraphOpcode.Subtract, [
            new ShaderGraphPortTemplate { Name = "A", Direction = ShaderGraphPortDirection.Input, Type = ShaderGraphValueType.Float, IsRequired = true },
            new ShaderGraphPortTemplate { Name = "B", Direction = ShaderGraphPortDirection.Input, Type = ShaderGraphValueType.Float, IsRequired = true },
            new ShaderGraphPortTemplate { Name = "Out", Direction = ShaderGraphPortDirection.Output, Type = ShaderGraphValueType.Float, IsRequired = true }
        ]));
        Register(new("MultiplyFloat", ShaderGraphBuiltInCategory.Arithmetic, ShaderGraphOpcode.Multiply, [
            new ShaderGraphPortTemplate { Name = "A", Direction = ShaderGraphPortDirection.Input, Type = ShaderGraphValueType.Float, IsRequired = true },
            new ShaderGraphPortTemplate { Name = "B", Direction = ShaderGraphPortDirection.Input, Type = ShaderGraphValueType.Float, IsRequired = true },
            new ShaderGraphPortTemplate { Name = "Out", Direction = ShaderGraphPortDirection.Output, Type = ShaderGraphValueType.Float, IsRequired = true }
        ]));
        Register(new("MultiplyFloat2", ShaderGraphBuiltInCategory.Arithmetic, ShaderGraphOpcode.Multiply, [
            new ShaderGraphPortTemplate { Name = "A", Direction = ShaderGraphPortDirection.Input, Type = ShaderGraphValueType.Float2, IsRequired = true },
            new ShaderGraphPortTemplate { Name = "B", Direction = ShaderGraphPortDirection.Input, Type = ShaderGraphValueType.Float2, IsRequired = true },
            new ShaderGraphPortTemplate { Name = "Out", Direction = ShaderGraphPortDirection.Output, Type = ShaderGraphValueType.Float2, IsRequired = true }
        ]));
        Register(new("DivideFloat", ShaderGraphBuiltInCategory.Arithmetic, ShaderGraphOpcode.Divide, [
            new ShaderGraphPortTemplate { Name = "A", Direction = ShaderGraphPortDirection.Input, Type = ShaderGraphValueType.Float, IsRequired = true },
            new ShaderGraphPortTemplate { Name = "B", Direction = ShaderGraphPortDirection.Input, Type = ShaderGraphValueType.Float, IsRequired = true },
            new ShaderGraphPortTemplate { Name = "Out", Direction = ShaderGraphPortDirection.Output, Type = ShaderGraphValueType.Float, IsRequired = true }
        ]));
        Register(new("LerpFloat", ShaderGraphBuiltInCategory.Arithmetic, ShaderGraphOpcode.Lerp, [
            new ShaderGraphPortTemplate { Name = "A", Direction = ShaderGraphPortDirection.Input, Type = ShaderGraphValueType.Float, IsRequired = true },
            new ShaderGraphPortTemplate { Name = "B", Direction = ShaderGraphPortDirection.Input, Type = ShaderGraphValueType.Float, IsRequired = true },
            new ShaderGraphPortTemplate { Name = "T", Direction = ShaderGraphPortDirection.Input, Type = ShaderGraphValueType.Float, IsRequired = true },
            new ShaderGraphPortTemplate { Name = "Out", Direction = ShaderGraphPortDirection.Output, Type = ShaderGraphValueType.Float, IsRequired = true }
        ]));
        Register(new("DotFloat3", ShaderGraphBuiltInCategory.Arithmetic, ShaderGraphOpcode.Dot, [
            new ShaderGraphPortTemplate { Name = "A", Direction = ShaderGraphPortDirection.Input, Type = ShaderGraphValueType.Float3, IsRequired = true },
            new ShaderGraphPortTemplate { Name = "B", Direction = ShaderGraphPortDirection.Input, Type = ShaderGraphValueType.Float3, IsRequired = true },
            new ShaderGraphPortTemplate { Name = "Out", Direction = ShaderGraphPortDirection.Output, Type = ShaderGraphValueType.Float, IsRequired = true }
        ]));
        Register(new("LengthFloat3", ShaderGraphBuiltInCategory.Arithmetic, ShaderGraphOpcode.Length, [
            new ShaderGraphPortTemplate { Name = "In", Direction = ShaderGraphPortDirection.Input, Type = ShaderGraphValueType.Float3, IsRequired = true },
            new ShaderGraphPortTemplate { Name = "Out", Direction = ShaderGraphPortDirection.Output, Type = ShaderGraphValueType.Float, IsRequired = true }
        ]));
        Register(new("NormalizeFloat3", ShaderGraphBuiltInCategory.Arithmetic, ShaderGraphOpcode.Normalize, [
            new ShaderGraphPortTemplate { Name = "In", Direction = ShaderGraphPortDirection.Input, Type = ShaderGraphValueType.Float3, IsRequired = true },
            new ShaderGraphPortTemplate { Name = "Out", Direction = ShaderGraphPortDirection.Output, Type = ShaderGraphValueType.Float3, IsRequired = true }
        ]));
        Register(new("ConstructFloat2", ShaderGraphBuiltInCategory.Vector, ShaderGraphOpcode.Construct, [
            new ShaderGraphPortTemplate { Name = "X", Direction = ShaderGraphPortDirection.Input, Type = ShaderGraphValueType.Float, IsRequired = true },
            new ShaderGraphPortTemplate { Name = "Y", Direction = ShaderGraphPortDirection.Input, Type = ShaderGraphValueType.Float, IsRequired = true },
            new ShaderGraphPortTemplate { Name = "Out", Direction = ShaderGraphPortDirection.Output, Type = ShaderGraphValueType.Float2, IsRequired = true }
        ]));
        Register(new("ConstructFloat3", ShaderGraphBuiltInCategory.Vector, ShaderGraphOpcode.Construct, [
            new ShaderGraphPortTemplate { Name = "X", Direction = ShaderGraphPortDirection.Input, Type = ShaderGraphValueType.Float, IsRequired = true },
            new ShaderGraphPortTemplate { Name = "Y", Direction = ShaderGraphPortDirection.Input, Type = ShaderGraphValueType.Float, IsRequired = true },
            new ShaderGraphPortTemplate { Name = "Z", Direction = ShaderGraphPortDirection.Input, Type = ShaderGraphValueType.Float, IsRequired = true },
            new ShaderGraphPortTemplate { Name = "Out", Direction = ShaderGraphPortDirection.Output, Type = ShaderGraphValueType.Float3, IsRequired = true }
        ]));
        Register(new("SwizzleXY", ShaderGraphBuiltInCategory.Vector, ShaderGraphOpcode.Swizzle, [
            new ShaderGraphPortTemplate { Name = "In", Direction = ShaderGraphPortDirection.Input, Type = ShaderGraphValueType.Float3, IsRequired = true },
            new ShaderGraphPortTemplate { Name = "Out", Direction = ShaderGraphPortDirection.Output, Type = ShaderGraphValueType.Float2, IsRequired = true }
        ]));
        Register(new("OutputFloat", ShaderGraphBuiltInCategory.Output, ShaderGraphOpcode.Output, [
            new ShaderGraphPortTemplate { Name = "In", Direction = ShaderGraphPortDirection.Input, Type = ShaderGraphValueType.Float, IsRequired = true }
        ], IsOutputNode: true));
        Register(new("OutputFloat2", ShaderGraphBuiltInCategory.Output, ShaderGraphOpcode.Output, [
            new ShaderGraphPortTemplate { Name = "In", Direction = ShaderGraphPortDirection.Input, Type = ShaderGraphValueType.Float2, IsRequired = true }
        ], IsOutputNode: true));
        Register(new("OutputFloat3", ShaderGraphBuiltInCategory.Output, ShaderGraphOpcode.Output, [
            new ShaderGraphPortTemplate { Name = "In", Direction = ShaderGraphPortDirection.Input, Type = ShaderGraphValueType.Float3, IsRequired = true }
        ], IsOutputNode: true));
        Register(new("OutputFloat4", ShaderGraphBuiltInCategory.Output, ShaderGraphOpcode.Output, [
            new ShaderGraphPortTemplate { Name = "In", Direction = ShaderGraphPortDirection.Input, Type = ShaderGraphValueType.Float4, IsRequired = true }
        ], IsOutputNode: true));
        Register(new(IfRegionType, ShaderGraphBuiltInCategory.Control, ShaderGraphOpcode.IfRegion, [
            new ShaderGraphPortTemplate { Name = "Condition", Direction = ShaderGraphPortDirection.Input, Type = ShaderGraphValueType.Bool, IsRequired = true }
        ]));
        Register(new(ForRangeRegionType, ShaderGraphBuiltInCategory.Control, ShaderGraphOpcode.ForRangeRegion, new List<ShaderGraphPortTemplate>()));
        Register(new(ReturnType, ShaderGraphBuiltInCategory.Control, ShaderGraphOpcode.Return, [
            new ShaderGraphPortTemplate { Name = "Value", Direction = ShaderGraphPortDirection.Input, Type = ShaderGraphValueType.Unknown, IsRequired = true }
        ]));
        Register(new(LoopIndexType, ShaderGraphBuiltInCategory.Control, ShaderGraphOpcode.LoopIndex, [
            new ShaderGraphPortTemplate { Name = "Out", Direction = ShaderGraphPortDirection.Output, Type = ShaderGraphValueType.Int, IsRequired = true }
        ]));
        Register(new("SubgraphCall", ShaderGraphBuiltInCategory.Subgraph, ShaderGraphOpcode.SubgraphCall, new List<ShaderGraphPortTemplate>(), IsSubgraphCall: true));
    }
    private static void Register(BuiltInNodeDefinition Definition) {
        Definitions[Definition.Type] = Definition;
    }
    public static bool TryGetDefinition(string Type, out BuiltInNodeDefinition Definition) {
        return Definitions.TryGetValue(Type, out Definition);
    }
    public static IReadOnlyList<BuiltInNodeDefinition>GetDefinitions() {
        return Definitions.Values.OrderBy(Definition=>Definition.Type,StringComparer.Ordinal).ToArray();
    }
    public static BuiltInNodeDefinition? GetDefinition(string Type) {
        if (Definitions.TryGetValue(Type, out var Definition)) {
            return Definition;
        }
        return null;
    }
    public static ShaderGraphEntryPointContract GetEntryPointContract(ShaderGraphTargetKind TargetKind, ShaderGraphEntryPointContract? OverrideContract = null) {
        if (OverrideContract is not null) {
            return OverrideContract;
        }
        if (EntryPointContracts.TryGetValue(TargetKind, out var Contract)) {
            return Contract;
        }
        return new ShaderGraphEntryPointContract { TargetKind = TargetKind, Name = "Main", ReturnType = ShaderGraphValueType.Float4 };
    }
    public static bool IsForRange(string Type) {
        return string.Equals(Type, ForRangeType, StringComparison.Ordinal);
    }
    public static bool IsStructuredRegionControl(string Type) {
        return string.Equals(Type, IfRegionType, StringComparison.Ordinal)||string.Equals(Type, ForRangeRegionType, StringComparison.Ordinal);
    }
    public static bool IsReturn(string Type)=>string.Equals(Type,ReturnType,StringComparison.Ordinal);
    public static bool IsLoopIndex(string Type)=>string.Equals(Type,LoopIndexType,StringComparison.Ordinal);
    public static bool TryGetReturnValueType(ShaderGraphNode Node,out ShaderGraphValueType ValueType) {
        return Enum.TryParse(Node.GetArgumentValue("ValueType"),true,out ValueType)&&ValueType!=ShaderGraphValueType.Unknown;
    }
    public static bool TryGetStaticLoopBounds(ShaderGraphNode Node,out int Start,out int EndExclusive,out int MaxIterations) {
        Start=0;
        EndExclusive=0;
        MaxIterations=0;
        return int.TryParse(Node.GetArgumentValue("Start"),System.Globalization.NumberStyles.Integer,System.Globalization.CultureInfo.InvariantCulture,out Start)
            &&int.TryParse(Node.GetArgumentValue("EndExclusive"),System.Globalization.NumberStyles.Integer,System.Globalization.CultureInfo.InvariantCulture,out EndExclusive)
            &&int.TryParse(Node.GetArgumentValue("MaxIterations"),System.Globalization.NumberStyles.Integer,System.Globalization.CultureInfo.InvariantCulture,out MaxIterations);
    }
    public static bool IsOutputNode(BuiltInNodeDefinition Definition) {
        return Definition.IsOutputNode;
    }
}
