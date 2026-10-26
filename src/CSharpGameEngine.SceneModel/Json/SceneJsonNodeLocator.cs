using Microsoft.CodeAnalysis.Text;
namespace CSharpGameEngine.SceneAnalysis.Json;
/// <summary>
/// Scene JSON テキスト内にあるノードオブジェクトの範囲（ADR-0070）。Designer モデルの位置は生成された C# ビューを指すため、Scene Designer はユーザーが見てい
/// る JSON 上のノード位置をここから取得する。ドキュメント構造だけを読み取り、コンパイルは不要。
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
