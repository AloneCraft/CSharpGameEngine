using Microsoft.CodeAnalysis.Text;
namespace CSharpGameEngine.SceneAnalysis.Json;
/// <summary>
/// The span of a node's object in a scene JSON text (ADR-0070). The designer model's
/// locations point into its generated C# view, so the Scene Designer asks here where a node is in the
/// JSON the user sees. Only the document structure is read; no compilation is needed.
/// </summary>
internal static class SceneJsonNodeLocator {
    internal static bool TryFind(string JsonText,NodePath Path,out TextSpan Span){
        Span=default;
        SceneJsonValue?Node;
        try {
            Node=SceneJsonReader.Parse(JsonText).Get("root");
        } catch(SceneJsonException){
            return false;
        }
        foreach(var Index in Path.Indices){
            if(Node?.Get("children") is not { Kind:SceneJsonKind.Array }Children||Index<0||Index>=Children.Items.Count){ return false; }
            Node=Children.Items[Index];
        }
        if(Node is not { Kind:SceneJsonKind.Object }){ return false; }
        Span=TextSpan.FromBounds(Node.Start,Node.End);
        return true;
    }
}
