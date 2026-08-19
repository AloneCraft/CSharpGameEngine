using System.Text;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>ツリーを作り直すかどうか。<see cref="HierarchyPopulatePolicy"/>の結果。</summary>
internal enum HierarchyPopulateAction {
    /// <summary>作り直す。</summary>
    Apply,

    /// <summary>内容が変わっていないので何もしない(冪等化)。</summary>
    Skip,

    /// <summary>インラインリネーム中。確定/取り消しまで保留する。</summary>
    Defer,
}

/// <summary>
/// M0051 Step 1-7: <c>HierarchyToolWindowControl.Populate</c>がツリーを作り直してよいかの純粋な判定。
///
/// <para>
/// <b>解決する不具合</b>: <c>Populate</c>は<c>SceneNodeViewModel.BuildWithSelection</c>で
/// ビューモデルを新規生成して<c>ItemsSource</c>を差し替えるため、インラインリネーム中のノード
/// (<c>IsRenaming=true</c>)ごと破棄され、Enterを受けるTextBoxが消えて<b>リネームが確定しない</b>。
/// 発行の門番(<see cref="SceneHubPublicationPolicy"/>)はDocumentがdirtyのときしか止めないが、
/// <b>リネーム確定前はまだ編集していないのでdirtyではない</b>ため素通りする。
/// </para>
///
/// <para>
/// Step 1-6 Stage Aで再解析が確実に届くようになるまでは、「解析が来ない/いつまでも遅延する」という
/// 欠陥が偶然リネームを守っていた。Stage Aは実バグの修正なので取り消さない —
/// <b>「再解析は頻繁に来る」が正しい前提であり、Hierarchy側がそれに耐えるべき</b>。
/// </para>
///
/// <para>
/// <b>2段構え</b>にするのが要点:
/// <list type="number">
/// <item><b>Skip(冪等化)</b>: 内容が変わっていないなら差し替えない。VSの無関係なworkspace変更で
/// 毎回木を作り直していたのがM0046のフォーカス強奪の温床でもあった。これでリネームに限らず
/// <b>全ての一時状態</b>(ドラッグ中の掴み、選択、展開状態)が守られる。</item>
/// <item><b>Defer(保留)</b>: リネーム中は内容が変わっていても差し替えない。
/// <b>ドラッグ中には掛けない</b> — <c>DoDragDrop</c>はモーダルループを回していつ終わるか読めない。
/// ドラッグはSkip側で十分守れる。</item>
/// </list>
/// </para>
/// </summary>
internal static class HierarchyPopulatePolicy {
    internal static HierarchyPopulateAction Decide(bool IsInlineRenaming,bool ContentUnchanged){
        if(ContentUnchanged){
            return HierarchyPopulateAction.Skip;
        }

        return IsInlineRenaming?HierarchyPopulateAction.Defer:HierarchyPopulateAction.Apply;
    }

    /// <summary>
    /// 保留を解除するとき、リネームを取り消してから反映すべきか。
    /// 対象ノードがもう存在しないなら確定しても<c>TryGetId</c>が失敗するので、先に取り消す。
    /// </summary>
    internal static bool ShouldCancelRenameOnRelease(bool RenamedPathStillExists)=>!RenamedPathStillExists;
}

/// <summary>
/// M0051 Step 1-7: ツリーの再構築が必要かを判断するための、Hierarchyが実際に描画する内容の署名。
///
/// <para>
/// <see cref="SceneModelStructuralSignature"/>ではなく専用にしているのは、Hierarchyが
/// <b>アイコン</b>も描くため。アイコンはComponentの型シンボルが解決できたかで変わるので、
/// 解決状態を署名へ混ぜないと「参照解決が遅れて後から解決された」ケースでアイコンが更新されない。
/// </para>
///
/// <para>
/// Sceneが未選択(<see langword="null"/>)の状態と、Sceneはあるが<c>Root</c>が無い状態は
/// <b>区別する</b> — 前者は空メッセージ、後者は空のツリーで、表示が違う。
/// </para>
/// </summary>
internal static class HierarchyContentSignature {
    private const string NoModel="(no-model)";
    private const string NoRoot="(no-root)";

    internal static string Compute(SceneModel?Model){
        if(Model is null){
            return NoModel;
        }

        StringBuilder Builder=new();
        Builder.Append(Model.FilePath).Append('\u001d');
        Builder.Append(Model.ClassFullyQualifiedName).Append('\u001e');
        if(Model.Root is { }Root){
            AppendNode(Builder,Root);
        } else {
            Builder.Append(NoRoot);
        }

        return Builder.ToString();
    }

    private static void AppendNode(StringBuilder Builder,SceneNode Node){
        Builder.Append("N[").Append(Node.Path.ToDisplayString()).Append(']');
        Builder.Append(Node.Name??"(unnamed)").Append('\u001f');

        Builder.Append("C{");
        foreach(SceneComponent Component in Node.Components){
            Builder.Append(Component.TypeFullyQualifiedName);
            // アイコン解決に効く。型シンボルが後から解決されたら署名も変わる。
            Builder.Append(Component.TypeSymbol is null?"?":"!").Append(',');
        }

        Builder.Append('}').Append('\u001e');

        foreach(SceneNode Child in Node.Children){
            AppendNode(Builder,Child);
        }

        Builder.Append("/N").Append('\u001e');
    }
}
