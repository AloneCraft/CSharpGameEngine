using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace CSharpGameEngine.SceneAnalysis;

/// <summary>
/// SceneModel上の要素が由来するソース位置。<b>ナビゲーションと書き換えのためだけに使い、ノードの同一性には
/// 決して使わない</b>(同一性は<see cref="NodePath"/>)。<see cref="Span"/>は<b>それを生成した解析結果の中
/// でのみ有効</b>であり、解析を跨いで保持してはならない(ADR-0040、SceneSourceEditorは書き換え直前に必ず
/// 再解析する)。
/// </summary>
internal readonly struct SceneSourceLocation : IEquatable<SceneSourceLocation> {
    internal readonly string FilePath;
    internal readonly TextSpan Span;
    internal readonly LinePositionSpan LineSpan;

    internal SceneSourceLocation(string FilePath,TextSpan Span,LinePositionSpan LineSpan){
        this.FilePath=FilePath;
        this.Span=Span;
        this.LineSpan=LineSpan;
    }

    internal static SceneSourceLocation None=>new(string.Empty,default,default);

    internal static SceneSourceLocation FromNode(SyntaxNode Node){
        FileLinePositionSpan Mapped=Node.GetLocation().GetLineSpan();
        return new SceneSourceLocation(Mapped.Path,Node.Span,Mapped.Span);
    }

    internal static SceneSourceLocation FromToken(SyntaxToken Token){
        FileLinePositionSpan Mapped=Token.GetLocation().GetLineSpan();
        return new SceneSourceLocation(Mapped.Path,Token.Span,Mapped.Span);
    }

    /// <summary>
    /// JSON Scene(<c>*.scene.json</c>)用。正確なテキスト位置はまだ追跡していない(将来Phase)ため、
    /// ファイルパスだけを持つ — <see cref="SceneSourceMarkerService"/>はこれでもファイルを開く
    /// ところまでは劣化なく動く(<see cref="Span"/>が空なら先頭位置へフォールバックする)。
    /// </summary>
    internal static SceneSourceLocation FromJsonFile(string FilePath)=>new(FilePath,default,default);

    public bool Equals(SceneSourceLocation Other){
        return string.Equals(this.FilePath,Other.FilePath,StringComparison.Ordinal)
            &&this.Span==Other.Span;
    }

    public override bool Equals(object?Obj)=>Obj is SceneSourceLocation Other&&this.Equals(Other);

    public override int GetHashCode(){
        unchecked {
            // default(SceneSourceLocation)ではFilePathがnullになりうるため、コンストラクター経由でない
            // 値も安全に扱う(structのdefaultは言語仕様上どうしても作れる)。
            var Hash=17;
            Hash=(Hash*31)+(this.FilePath is null?0:StringComparer.Ordinal.GetHashCode(this.FilePath));
            Hash=(Hash*31)+this.Span.GetHashCode();
            return Hash;
        }
    }

    public override string ToString()=> $"{this.FilePath}({this.LineSpan.Start.Line+1},{this.LineSpan.Start.Character+1})";
}
