using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
namespace CSharpGameEngine.SceneAnalysis.Json;
/// <summary>
/// Scene JSON ドキュメントと等価な C# Scene クラス（ADR-0070）。Scene Designer は JSON ファイルのパスを与えて解析用コンパイルに加えるため、既存の
/// Scene DSL アナライザーから、C# Scene クラスと同じ <see cref="SceneModel"/>（型、メンバーシンボル、定数値）を Hierarchy、Inspector、
/// Scene ビューに提供できる。ディスクには書き込まない。
/// </summary>
internal static class SceneJsonDesignerSource {
    private const string NodeType="global::CSharpGameEngine.Entities.Node";
    private const string SceneInterface="global::CSharpGameEngine.Entities.IScene";
    private const string StartupSceneAttribute="global::CSharpGameEngine.Entities.StartupScene";
    /// <summary>ドキュメントに問題があるかルートがない場合は <c>null</c>。</summary>
    internal static string?Build(SceneJsonScene Scene){
        if(Scene.HasErrors||Scene.Root is null){ return null; }
        var Builder=new StringBuilder();
        Builder.Append("// Scene Designer view of a scene JSON document (ADR-0070).\n");
        if(Scene.Namespace is not null){ Builder.Append("namespace ").Append(Scene.Namespace).Append(";\n"); }
        // 起動 Scene は C# クラスと同様に1として読み込む。それ以外の場合、Designer での編集は起動順を
        // 含めずにドキュメントを書き込むと、ゲームが Scene の読み込みを停止する。
        if(Scene.StartupOrder is { }Order){
            Builder.Append('[').Append(StartupSceneAttribute).Append("(Order = ").Append(Order.ToString(CultureInfo.InvariantCulture)).Append(")]\n");
        }
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
