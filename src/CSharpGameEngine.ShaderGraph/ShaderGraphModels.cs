using System;
using System.Collections.Generic;
using System.Linq;
namespace CSharpGameEngine.ShaderGraph;
public enum ShaderGraphTargetKind { Stage=0 }
public enum ShaderGraphValueType { Unknown, Float, Float2, Float3, Float4, Int, Bool, Texture2D, SamplerState }
public enum ShaderGraphPortDirection { Input, Output }
public enum ShaderGraphDiagnosticSeverity { Error, Warning }
public enum ShaderGraphDiagnosticCode { UnknownNode, UnknownPort, MissingRequiredPort, TypeMismatch, CycleDetected, SubgraphRecursion, SubgraphMissing, ForRangeForbidden, ForRangeInvalidBounds, RegionInvalidNode, RegionInvalidControl, RegionOverlap, RegionValueEscape, ReturnInvalidScope, LoopIndexInvalidScope, MissingEntryPoint, EntryPointMismatch, DuplicateIdentity, UnknownTarget }
public enum ShaderGraphBuiltInCategory { Constant, Arithmetic, Vector, Output, Utility, Control, Subgraph }
public enum ShaderGraphOpcode { Constant, Add, Subtract, Multiply, Divide, Lerp, Dot, Length, Normalize, Swizzle, Construct, Output, IfRegion, ForRangeRegion, Return, LoopIndex, SubgraphCall }
public enum ShaderGraphRegionKind { Comment, If, ForRange }
public enum ShaderGraphRegionBranch { Body, Then, Else }
public sealed class ShaderGraphEntryPointContract {
    public ShaderGraphTargetKind TargetKind { get; set; }
    public string Name { get; set; } = "Main";
    public ShaderGraphValueType ReturnType { get; set; } = ShaderGraphValueType.Float4;
    public string? ReturnSemantic { get; set; }
}
public sealed class ShaderGraphTarget {
    public ShaderGraphTargetKind Kind { get; set; }
    public string? Name { get; set; }
}
public sealed class ShaderGraphDocument {
    public int Version { get; set; } = 1;
    public StableId Id { get; set; } = StableId.New();
    public ShaderGraphTarget Target { get; set; } = new ShaderGraphTarget();
    public ShaderGraphEntryPointContract? EntryPoint { get; set; }
    public StableId RootSubgraphId { get; set; }
    public List<ShaderGraphSubgraph> Subgraphs { get; set; } = new List<ShaderGraphSubgraph>();
    public List<ShaderGraphDependency> Dependencies { get; set; } = new List<ShaderGraphDependency>();
    public ShaderGraphEditorLayout? Layout { get; set; }
}
public sealed class ShaderGraphDependency {
    public StableId Id { get; set; }
    public string Path { get; set; } = string.Empty;
}
public sealed class ShaderGraphSubgraph {
    public StableId Id { get; set; }
    public string? Name { get; set; }
    public List<ShaderGraphPort> Inputs { get; set; } = new List<ShaderGraphPort>();
    public List<ShaderGraphPort> Outputs { get; set; } = new List<ShaderGraphPort>();
    public List<ShaderGraphNode> Nodes { get; set; } = new List<ShaderGraphNode>();
    public List<ShaderGraphEdge> Edges { get; set; } = new List<ShaderGraphEdge>();
    public List<ShaderGraphRegion> Regions { get; set; } = new List<ShaderGraphRegion>();
}
public sealed class ShaderGraphNode {
    public StableId Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? Name { get; set; }
    public List<ShaderGraphArgument> Arguments { get; set; } = new List<ShaderGraphArgument>();
    public string? GetArgumentValue(string Name) {
        var Argument = this.Arguments.FirstOrDefault(Argument => string.Equals(Argument.Name, Name, StringComparison.Ordinal));
        return Argument?.Value;
    }
}
public sealed class ShaderGraphArgument {
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
public sealed class ShaderGraphPort {
    public string Name { get; set; } = string.Empty;
    public ShaderGraphValueType Type { get; set; } = ShaderGraphValueType.Unknown;
}
public sealed class ShaderGraphEdge {
    public StableId Id { get; set; } = StableId.New();
    public StableId FromNodeId { get; set; }
    public string FromPort { get; set; } = string.Empty;
    public StableId ToNodeId { get; set; }
    public string ToPort { get; set; } = string.Empty;
}
public sealed class ShaderGraphRegion {
    public StableId Id { get; set; }
    public ShaderGraphRegionKind Kind { get; set; }
    public string? Name { get; set; }
    public StableId ControlNodeId { get; set; }
    public StableId ParentRegionId { get; set; }
    public ShaderGraphRegionBranch ParentBranch { get; set; }
    public List<StableId> NodeIds { get; set; } = new List<StableId>();
    public List<StableId> ElseNodeIds { get; set; } = new List<StableId>();
}
public sealed class ShaderGraphEditorLayout {
    public List<ShaderGraphNodeLayout> Nodes { get; set; } = new List<ShaderGraphNodeLayout>();
    public List<ShaderGraphRegionLayout> Regions { get; set; } = new List<ShaderGraphRegionLayout>();
}
public sealed class ShaderGraphNodeLayout {
    public StableId NodeId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
}
public sealed class ShaderGraphRegionLayout {
    public StableId RegionId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
}
public sealed class ShaderGraphPortTemplate {
    public string Name { get; set; } = string.Empty;
    public ShaderGraphPortDirection Direction { get; set; }
    public ShaderGraphValueType Type { get; set; } = ShaderGraphValueType.Unknown;
    public bool IsRequired { get; set; } = true;
}
public sealed class ShaderGraphDiagnostic {
    public ShaderGraphDiagnosticCode Code { get; }
    public ShaderGraphDiagnosticSeverity Severity { get; }
    public string Message { get; }
    public StableId? NodeId { get; }
    public StableId? SubgraphId { get; }
    public StableId? RegionId { get; }
    public ShaderGraphDiagnostic(ShaderGraphDiagnosticCode Code, ShaderGraphDiagnosticSeverity Severity, string Message, StableId? NodeId = null, StableId? SubgraphId = null, StableId? RegionId = null) {
        this.Code = Code;
        this.Severity = Severity;
        this.Message = Message;
        this.NodeId = NodeId;
        this.SubgraphId = SubgraphId;
        this.RegionId = RegionId;
    }
}
public sealed class ShaderGraphValidationResult {
    public List<ShaderGraphDiagnostic> Diagnostics { get; } = new List<ShaderGraphDiagnostic>();
    public bool IsValid => this.Diagnostics.All(Diagnostic => Diagnostic.Severity != ShaderGraphDiagnosticSeverity.Error);
}
