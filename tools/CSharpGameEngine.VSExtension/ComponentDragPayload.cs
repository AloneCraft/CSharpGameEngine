using System;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// M0052: パレットからHierarchy/Inspectorへドラッグする際に運ぶデータ。
///
/// <para>
/// <c>HierarchyToolWindowControl.HierarchyDragPayload</c>(Node Reparent用)を流用しないのは、
/// あれがprivate nestedで他ツールウィンドウから型として参照できないうえ、運ぶ意味も違うため。
/// ドロップ側は<see cref="System.Windows.DataObject"/>の型で2種類のドラッグを見分ける。
/// </para>
/// </summary>
internal sealed record ComponentDragPayload(
    string ComponentTypeFullyQualifiedName,
    string ComponentExpression,
    string DisplayName);

/// <summary>ドロップを受け付けられるか。<see cref="ComponentDropPolicy"/>の結果。</summary>
internal enum ComponentDropVerdict {
    Accept,

    /// <summary>Hierarchyの空白へ落とす。Root直下へ新規Entityを作る。</summary>
    AcceptCreateRootChild,

    /// <summary>Sceneが開かれていない。</summary>
    RejectNoScene,

    /// <summary>ドロップ位置にノードが無い(Hierarchyの空白部分、Inspectorで未選択)。</summary>
    RejectNoTarget,

    /// <summary>同じ型のComponentを既に持っている。</summary>
    RejectDuplicate,

    /// <summary>互換用。編集可能なNodeにはComponentsコレクションを生成するため、通常は返さない。</summary>
    RejectNoComponentsList,
}

/// <summary>
/// M0052: Componentのドロップ可否。<c>DragOver</c>のカーソル表示と、<c>Drop</c>後の説明文の
/// <b>両方を同じ関数から作る</b> — プレビューと結果が食い違うのが一番わかりにくいため。
///
/// <para>
/// <b>この判定は最終決定ではない。</b> 実際に追加できるかを決めるのは
/// <see cref="SceneModelEditApplier.TryApplyAddComponent"/>で、そちらはDocumentの予測モデル
/// (未保存の追加を含む)を見る。Inspectorは同じ予測モデルを持っているので判定が一致するが、
/// Hierarchyは<see cref="SceneEditorHub.ActiveSceneModel"/>(実解析の結果)しか持たないため、
/// 未保存の追加が絡む一瞬だけプレビューが甘くなることがある。その場合でも適用側が拒否し、
/// ステータスバーに理由が出るので、誤った結果が黙って通ることはない。
/// </para>
/// </summary>
internal static class ComponentDropPolicy {
    internal static ComponentDropVerdict Evaluate(
        bool HasScene,bool TargetResolved,bool TargetHasComponentsList,bool TargetAlreadyHasType,
        bool CanCreateRootChild=false){
        if(!HasScene){
            return ComponentDropVerdict.RejectNoScene;
        }

        if(!TargetResolved){
            return CanCreateRootChild?ComponentDropVerdict.AcceptCreateRootChild:ComponentDropVerdict.RejectNoTarget;
        }

        if(TargetAlreadyHasType){
            return ComponentDropVerdict.RejectDuplicate;
        }

        _=TargetHasComponentsList;
        return ComponentDropVerdict.Accept;
    }

    internal static bool Accepts(ComponentDropVerdict Verdict)=>Verdict is ComponentDropVerdict.Accept or ComponentDropVerdict.AcceptCreateRootChild;

    internal static string Describe(ComponentDropVerdict Verdict,string DisplayName)=>Verdict switch {
        ComponentDropVerdict.Accept=>$"{DisplayName} を追加します。",
        ComponentDropVerdict.AcceptCreateRootChild=>$"Root 直下に {DisplayName} を持つ新規Entityを作成します。",
        ComponentDropVerdict.RejectNoScene=>"シーンが開かれていません。",
        ComponentDropVerdict.RejectNoTarget=>"追加先のノードがありません。",
        ComponentDropVerdict.RejectDuplicate=>$"このEntityには既に {DisplayName} があります。",
        ComponentDropVerdict.RejectNoComponentsList=>
            $"このEntityの Components コレクションを作成できません。",
        _=>throw new ArgumentOutOfRangeException(nameof(Verdict),Verdict,"未知のドロップ判定です。"),
    };
}
