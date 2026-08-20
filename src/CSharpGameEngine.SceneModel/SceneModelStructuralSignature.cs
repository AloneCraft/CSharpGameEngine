using System.Collections.Immutable;
using System.Text;

namespace CSharpGameEngine.SceneAnalysis;

/// <summary>
/// M0051: <see cref="SceneModel"/>の構造と値だけを取り出した正規化文字列。
/// <b>書き戻しの往復検証(round-trip verification)専用</b>である。
///
/// <para>
/// M0049はユーザーの「C#に反映されていない」という報告が動機だった。書き戻しが黙って壊れることが
/// 二度と起きないよう、書き戻した構文木を<see cref="SceneDeclarationAnalyzer"/>で<b>読み直して</b>
/// 再構築した<see cref="SceneModel"/>と、書き戻す前の意図した<see cref="SceneModel"/>の署名を
/// 比較する。一致しなければ書き戻し自体を中止する — 「壊れやすい」を「壊れたら必ず判る」に変える。
/// </para>
///
/// <para>
/// <b>意図的に含めないもの</b>: <see cref="SceneSourceLocation"/>(書き戻すたびに必ず変わる)、
/// <c>EditorNodeId</c>(セッション内の同一性であってソースの内容ではない)、
/// <see cref="SceneValue.SourceExpressionText"/>(ユーザーが書いた式の見た目。
/// <c>12f*MathF.PI/180f</c>のような定数式は畳み込み後の値が同じなら等価とみなしたい)。
/// 比較に使うのは<see cref="SceneValue.EmitExpressionText"/> — これは完全修飾済みで
/// 生成コードへ実際に出る文字列であり、「Runtimeにとって同じ意味か」を過不足なく表す。
/// </para>
///
/// <para>
/// <b><see cref="SceneComponent.EmitExpressionText"/>は使わない。</b>これはComponent全体の
/// <c>new T { … }</c>式を1つの文字列で持つが、<c>SceneModelEditApplier.WithMembers</c>が
/// メンバーを差し替えても<b>この文字列だけは古いまま引き継がれる</b>(M0051の実装中に実測で発見)。
/// 解析直後のModelでは正しいが、セッション中に編集されたModelでは陳腐化しているため、
/// これで比較すると「正しく書き戻したのに不一致」という誤検知になる。個々の
/// <see cref="SceneComponentMember"/>から組み立て直すことで、解析由来のModelと編集由来のModelの
/// どちらでも同じ署名が得られる。
/// </para>
/// </summary>
internal static class SceneModelStructuralSignature {
    internal static string Compute(SceneModel Model){
        StringBuilder Builder=new();
        Builder.Append(Model.Namespace??"(global)").Append('\u001d');
        Builder.Append(Model.ClassName).Append('\u001d');
        Builder.Append(Model.IsStartupScene?"startup":"-").Append('\u001d');
        Builder.Append(Model.StartupOrder).Append('\u001e');

        if(Model.Root is { } Root){
            AppendNode(Builder,Root);
        } else {
            Builder.Append("(no-root)");
        }

        return Builder.ToString();
    }

    private static void AppendNode(StringBuilder Builder,SceneNode Node){
        // Pathを含めることで、同じ内容のノードが別の位置に居る場合を確実に区別する。
        Builder.Append("N[").Append(Node.Path.ToDisplayString()).Append(']');
        Builder.Append(Node.Name??"(unnamed)").Append('\u001f');

        Builder.Append("C{");
        foreach(var Component in Node.Components){
            Builder.Append(Component.TypeFullyQualifiedName).Append('=');
            AppendMembers(Builder,Component.Members);
            Builder.Append(';');
        }
        Builder.Append('}');

        Builder.Append("K{");
        foreach(var Child in Node.Children){
            AppendNode(Builder,Child);
        }
        Builder.Append('}');
    }

    /// <summary>
    /// メンバーは宣言順に並べる。順序が変われば書き戻し結果のテキストも変わるため、
    /// 「意味が同じかどうか」の判定として順序を無視するのは正しくない。
    /// </summary>
    private static void AppendMembers(StringBuilder Builder,ImmutableArray<SceneComponentMember>Members){
        Builder.Append('{');
        foreach(var Member in Members){
            Builder.Append(Member.MemberName).Append('=');
            Builder.Append(Member.Value.EmitExpressionText).Append(',');
        }
        Builder.Append('}');
    }
}
