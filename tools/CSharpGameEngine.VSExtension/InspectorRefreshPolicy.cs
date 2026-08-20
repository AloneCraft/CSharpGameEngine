using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CSharpGameEngine.VSExtension;

/// <summary>行を作り直すかどうか。<see cref="InspectorRefreshPolicy"/>の結果。</summary>
internal enum InspectorRefreshAction {
    /// <summary>作り直す。</summary>
    Apply,

    /// <summary>内容が変わっていないので何もしない(冪等化)。</summary>
    Skip,

    /// <summary>このパネル内で入力中。フォーカスが外れるまで保留する。</summary>
    Defer,
}

/// <summary>
/// M0052: <c>InspectorToolWindowControl.TryRefresh</c>が行を作り直してよいかの純粋な判定。
/// <see cref="HierarchyPopulatePolicy"/>のInspector版で、判断の構造をそちらに揃えている。
///
/// <para>
/// <b>解決する不具合</b>: 従来のガードは<c>IsKeyboardFocusWithin</c>だけだった。これはタイプ入力中の
/// TextBoxは守るが、<b>Expanderの開閉はマウスだけで行うためフォーカスはパネル外にあることが多く</b>、
/// 再解析のたびに<c>ItemsSource</c>が差し替わって展開状態もスクロール位置も飛ぶ。Step 1-7で
/// Hierarchyのインラインリネームが壊れたのとまったく同じ構図なので、対策も同じく
/// <b>内容署名によるSkip</b>を土台に置く。フォーカスによるDeferはその上の追加防御でしかない。
/// </para>
/// </summary>
internal static class InspectorRefreshPolicy {
    /// <summary>
    /// <paramref name="Force"/>は編集を確定した直後など、呼び出し側が「今の表示は確実に古い」と
    /// 知っている場合の明示指定。署名が同じでも作り直す — 確定した値をUIへ確実に映し戻すため。
    /// </summary>
    internal static InspectorRefreshAction Decide(bool HasKeyboardFocusWithin,bool ContentUnchanged,bool Force){
        if(Force){
            return InspectorRefreshAction.Apply;
        }

        if(ContentUnchanged){
            return InspectorRefreshAction.Skip;
        }

        return HasKeyboardFocusWithin?InspectorRefreshAction.Defer:InspectorRefreshAction.Apply;
    }
}

/// <summary>
/// M0052: 行の再構築が必要かを判断するための、Inspectorが実際に描画する内容の署名。
///
/// <para>
/// Sceneセッションが無い(<see langword="null"/>)状態と、セッションはあるが表示すべきComponentが
/// 無い(空のリスト)状態は<b>区別する</b>。どちらも空メッセージを出すが、同じ署名にしてしまうと
/// 一方から他方への遷移がSkipされ、後から本当に表示すべきものが来ても描き直されない。
/// </para>
/// </summary>
internal static class InspectorContentSignature {
    private const string NoSelection="(no-selection)";
    private const char FieldSeparator=(char)0x1f;
    private const char GroupSeparator=(char)0x1e;

    internal static string Compute(IReadOnlyList<SceneInspectorComponentGroup>?Groups){
        if(Groups is null){
            return NoSelection;
        }

        StringBuilder Builder=new();
        foreach(var Group in Groups){
            Builder.Append(Group.NodeId.Value.ToString(CultureInfo.InvariantCulture)).Append('@');
            Builder.Append(Group.ComponentTypeFullyQualifiedName).Append('{');
            AppendRows(Builder,Group.Rows);
            Builder.Append('}').Append(GroupSeparator);
        }

        return Builder.ToString();
    }

    /// <summary>
    /// <see cref="SceneInspectorNestedRow"/>を再帰的に辿る。ネストした値が変化した(例:
    /// <c>Vector3</c>のXだけ変わった)場合にも署名を変えないと、そのネストだけ古い表示のまま
    /// Skipされ続ける。
    /// </summary>
    private static void AppendRows(StringBuilder Builder,IReadOnlyList<SceneInspectorRow>Rows){
        foreach(var Row in Rows){
            switch(Row){
                case SceneInspectorMemberRow Leaf:
                    Builder.Append(Leaf.Member.MemberName).Append('=');
                    Builder.Append(Leaf.Member.SourceExpression).Append(FieldSeparator);
                    // 編集UIの種類が変われば描くコントロールも変わる。値が同じでも作り直しが要る。
                    Builder.Append((int)Leaf.Member.EditorKind).Append(FieldSeparator);
                    break;
                case SceneInspectorNestedRow Nested:
                    Builder.Append(Nested.DisplayName).Append('(');
                    AppendRows(Builder,Nested.Rows);
                    Builder.Append(')').Append(FieldSeparator);
                    break;
            }
        }
    }
}

/// <summary>
/// Expanderの展開状態を再構築を跨いで覚えるためのキー。
///
/// <para>
/// <b>Node込みにするのが要点</b>。Component完全修飾名だけをキーにすると、あるNodeで
/// <c>Transform</c>を畳んだ状態が別Nodeの<c>Transform</c>へ伝染して驚く。
/// </para>
/// </summary>
internal static class InspectorExpansionKey {
    internal static string Compute(EditorNodeId NodeId,string ComponentTypeFullyQualifiedName)=>
        NodeId.Value.ToString(CultureInfo.InvariantCulture)+"@"+ComponentTypeFullyQualifiedName;

    /// <summary>
    /// ネストした<see cref="SceneInspectorNestedRow"/>用。Component内でのメンバーパス
    /// (例: <c>["Position"]</c>)を末尾に足すので、Component自体のキーとは形が異なり衝突しない
    /// (パスは常に1要素以上)。
    /// </summary>
    internal static string Compute(
        EditorNodeId NodeId,string ComponentTypeFullyQualifiedName,IReadOnlyList<string>MemberPath)=>
        Compute(NodeId,ComponentTypeFullyQualifiedName)+"#"+string.Join(".",MemberPath);
}

/// <summary>
/// M0052: 追加したばかりのComponentを自動で展開して見せるための判定。
///
/// <para>
/// 「前回との差分」では実装できない — 外部編集やUndoでComponentが復活した場合と、ユーザーが
/// 今追加した場合を区別できないため。追加を要求した側が明示的にキーを預け、着地したら消す。
/// </para>
///
/// <para>
/// <b>member 0件では消さない</b>のが肝。<see cref="SceneModelEditApplier"/>のメモリ内適用は
/// memberが空のComponentを挿すので、そこで消してしまうと「展開されたが中身は空」で終わり、
/// 本命であるRoslyn再解析の着地(=値を設定できるようになる瞬間)を待てない。
/// </para>
/// </summary>
internal static class InspectorRevealPolicy {
    internal static bool ShouldExpand(string?PendingKey,string GroupKey)=>
        PendingKey is not null&&string.Equals(PendingKey,GroupKey,System.StringComparison.Ordinal);

    /// <summary>
    /// 対象がもう無いなら待つ理由が無いので消す。memberが載ったら目的を達したので消す。
    /// どちらでもない(=解析待ち)間だけ保留を続ける。
    /// </summary>
    internal static bool ShouldClearPending(bool GroupPresent,int GroupMemberCount)=>
        !GroupPresent||GroupMemberCount>0;
}
