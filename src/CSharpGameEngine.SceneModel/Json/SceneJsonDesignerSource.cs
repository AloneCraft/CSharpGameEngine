using System.Text;
using Microsoft.CodeAnalysis.CSharp;
namespace CSharpGameEngine.SceneAnalysis.Json;
/// <summary>
/// The C# scene class equivalent to a <c>*.scene.json</c> document (ADR-0070). The Scene Designer adds
/// it to its analysis compilation, with the JSON file as its path, so the existing Scene DSL analyzer
/// yields the same <see cref="SceneModel"/> (types, member symbols, constant values) for the
/// Hierarchy, Inspector and Scene view as a C# scene class would. It is never written to disk.
/// </summary>
internal static class SceneJsonDesignerSource {
    private const string NodeType="global::CSharpGameEngine.Entities.Node";
    private const string SceneInterface="global::CSharpGameEngine.Entities.IScene";
    /// <summary><c>null</c> when the document has problems or no root.</summary>
    internal static string?Build(SceneJsonScene Scene){
        if(Scene.HasErrors||Scene.Root is null){ return null; }
        var Builder=new StringBuilder();
        Builder.Append("// Scene Designer view of a scene JSON document (ADR-0070).\n");
        if(Scene.Namespace is not null){ Builder.Append("namespace ").Append(Scene.Namespace).Append(";\n"); }
        Builder.Append("partial class ").Append(Scene.ClassName).Append(" : ").Append(SceneInterface).Append(" {\n");
        Builder.Append("    public ").Append(NodeType).Append(" Root => ");
        AppendNode(Builder,Scene.Root,2);
        Builder.Append(";\n}\n");
        return Builder.ToString();
    }
    private static void AppendNode(StringBuilder Builder,SceneJsonNode Node,int Indent){
        var Pad=new string(' ',Indent*4);
        Builder.Append("new ").Append(NodeType).Append(" {\n");
        if(Node.Name is not null){
            Builder.Append(Pad).Append("Name = ").Append(SymbolDisplay.FormatLiteral(Node.Name,quote:true)).Append(",\n");
        }
        Builder.Append(Pad).Append("Components = [\n");
        foreach(var Component in Node.Components){
            Builder.Append(Pad).Append("    ").Append(Component.EmitExpressionText).Append(",\n");
        }
        Builder.Append(Pad).Append("],\n");
        Builder.Append(Pad).Append("Children = [\n");
        foreach(var Child in Node.Children){
            Builder.Append(Pad).Append("    ");
            AppendNode(Builder,Child,Indent+2);
            Builder.Append(",\n");
        }
        Builder.Append(Pad).Append("],\n");
        Builder.Append(new string(' ',(Indent-1)*4)).Append('}');
    }
}
