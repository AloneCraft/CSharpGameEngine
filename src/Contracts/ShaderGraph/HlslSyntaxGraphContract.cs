using System;
using System.Collections.Generic;
using System.Linq;
namespace CSharpGameEngine.ShaderGraph;
public static class HlslSyntaxGraphContract {
    public const string NodeTypePrefix="Hlsl.Syntax.";
    public const string SchemaVersionArgument="SchemaVersion";
    public const string ClrTypeArgument="ClrType";
    public const string BaseTypesArgument="BaseTypes";
    public const string ChildPortPrefix="ChildPort.";
    public const string PropertyPrefix="Property.";
    public const string SyntaxOutputPort="Syntax";
    public static bool IsSyntaxNode(ShaderGraphNode Node) {
        return Node.Type.StartsWith(NodeTypePrefix,StringComparison.Ordinal);
    }
    public static bool TryValidateNode(ShaderGraphNode Node,out string Error) {
        Error=string.Empty;
        if(!IsSyntaxNode(Node)) {
            Error="Node is not an HLSL syntax node.";
            return false;
        }
        if(Node.GetArgumentValue(SchemaVersionArgument)!="1") {
            Error="HLSL syntax node schema version is invalid.";
            return false;
        }
        var ClrType=Node.GetArgumentValue(ClrTypeArgument);
        if(ClrType is null||string.IsNullOrWhiteSpace(ClrType)||!ClrType.StartsWith("UnityShaderParser.HLSL.",StringComparison.Ordinal)) {
            Error="HLSL syntax node CLR type is invalid.";
            return false;
        }
        if(!Node.Type.Equals(NodeTypePrefix+ClrType.Substring("UnityShaderParser.HLSL.".Length),StringComparison.Ordinal)) {
            Error="HLSL syntax node type does not match its CLR type.";
            return false;
        }
        foreach(var Argument in Node.Arguments) {
            if(Argument.Name.StartsWith(PropertyPrefix,StringComparison.Ordinal)) {
                var PropertyName=Argument.Name.Substring(PropertyPrefix.Length);
                if(PropertyName.IndexOf("Code",StringComparison.OrdinalIgnoreCase)>=0||PropertyName.IndexOf("Source",StringComparison.OrdinalIgnoreCase)>=0||PropertyName.IndexOf("Token",StringComparison.OrdinalIgnoreCase)>=0) {
                    Error="Raw HLSL source and token payloads are forbidden in Shader Graph.";
                    return false;
                }
            }
        }
        return true;
    }
    public static bool TryValidateEdge(ShaderGraphNode FromNode,ShaderGraphEdge Edge,ShaderGraphNode ToNode,out string Error) {
        Error=string.Empty;
        if(!IsSyntaxNode(FromNode)||!IsSyntaxNode(ToNode)) {
            Error="HLSL syntax edges cannot connect to non-syntax nodes.";
            return false;
        }
        if(Edge.FromPort!=SyntaxOutputPort) {
            Error="HLSL syntax edge source port must be Syntax.";
            return false;
        }
        var ChildContract=ToNode.Arguments.FirstOrDefault(Argument=>Argument.Name==ChildPortPrefix+Edge.ToPort);
        if(ChildContract is null) {
            Error=$"HLSL syntax destination port '{Edge.ToPort}' is not declared.";
            return false;
        }
        var SourceType=FromNode.GetArgumentValue(ClrTypeArgument);
        var BaseTypes=(FromNode.GetArgumentValue(BaseTypesArgument)??string.Empty).Split(new[]{';'},StringSplitOptions.RemoveEmptyEntries);
        if(!string.Equals(SourceType,ChildContract.Value,StringComparison.Ordinal)&&!BaseTypes.Contains(ChildContract.Value,StringComparer.Ordinal)) {
            Error=$"HLSL syntax type mismatch: '{SourceType}' cannot bind to '{ChildContract.Value}'.";
            return false;
        }
        return true;
    }
    public static IReadOnlyList<string>GetRequiredChildPorts(ShaderGraphNode Node) {
        return Node.Arguments.Where(Argument=>Argument.Name.StartsWith(ChildPortPrefix,StringComparison.Ordinal)).Select(Argument=>Argument.Name.Substring(ChildPortPrefix.Length)).OrderBy(Name=>Name,StringComparer.Ordinal).ToArray();
    }
}