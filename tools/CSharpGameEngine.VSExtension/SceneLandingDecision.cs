using System.Collections.Generic;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// M0051 Step 1-7: 自分の書き込み(保存の書き戻し、Undo/Redoのテキスト同期)が着地したとき、
/// <see cref="SceneEditDocument"/>を維持してよいかの<b>純粋な</b>判定。
///
/// <para>
/// <b>なぜ独立したクラスなのか(Step 1-6の実機不具合の再発防止)</b>:
/// Step 1-6はこの判定を<c>SceneDiffEngine.Compare(Document.Projection,RealProjection).IsEmpty</c>で
/// 済ませていたが、<see cref="SceneDiffEngine"/>は<b>Rendererの増分更新分類</b>が目的であり、
/// Componentの<c>EmitExpressionText</c>をOrdinal比較する。一方
/// <see cref="SceneModelEditApplier"/>の予測モデルは<b>この式テキストを古いまま引き継ぐ</b>
/// (<c>WithMembers</c>。同クラスのテストが「<c>ComponentChanged</c>のみ許容」として明文化している)。
/// そのためギズモ編集の着地では差分が必ず1件出て、<b>実機で一度も</b>
/// <see cref="SceneLandingAction.KeepDocument"/>にならなかった。
/// 目的の違う判定へ相乗りしたことが原因なので、専用の判定として分離する。
/// </para>
///
/// <para>
/// <b>判定材料の作り方まで純粋関数に含めるのが要点</b>。Step 1-6はポリシー自体は正しかったが
/// 「第2引数を実機でどう作るか」がテストの外にあったため、ユニットテストでは永久に捕まらなかった。
/// ここは<see cref="SceneModel"/>と<see cref="SceneProjection"/>だけを受け取り、VS SDKに依存しない。
/// </para>
/// </summary>
internal static class SceneLandingDecision {
    /// <summary>
    /// <paramref name="RealModel"/>/<paramref name="RealProjection"/>は着地した実解析の結果、
    /// <paramref name="DocumentModel"/>/<paramref name="DocumentProjection"/>はメモリ内の正本。
    /// </summary>
    internal static SceneLandingAction Evaluate(
        SceneModel?RealModel,SceneProjection?RealProjection,
        SceneModel DocumentModel,SceneProjection DocumentProjection,
        bool IsOwnLanding){
        if(!IsOwnLanding){
            return SceneLandingAction.Rebase;
        }

        // 解析エラー中。ここで LastKnownGoodModel へフォールバックしてはいけない —
        // 前回の良い結果と比較して「一致」と誤判定し、いま届いたエラー状態を見逃す。
        // 触らずに次の正常な解析を待つ。
        if(RealModel is null||RealModel.HasErrors||RealModel.Root is null||RealProjection is null){
            return SceneLandingAction.Defer;
        }

        // (a) 次の保存で未書き出し履歴の Operation.NodeId が実投影上で解決できること。
        if(!SceneProjectionIdentityComparer.Matches(DocumentProjection,RealProjection)){
            return SceneLandingAction.Rebase;
        }

        // (b) 「C#テキスト == History[0..TextWatermark) を再生した状態」が意味レベルで成り立つこと。
        // (a)だけでは、書き込みから着地までの窓でユーザーが値を手打ちしたケースを検知できず、
        // その編集が表示から永久に落ちる(Path構造が同じならIDは一致してしまうため)。
        return SceneModelStructuralSignature.Compute(DocumentModel)
            ==SceneModelStructuralSignature.Compute(RealModel)
            ?SceneLandingAction.KeepDocument
            :SceneLandingAction.Rebase;
    }
}

/// <summary>
/// M0051 Step 1-7: 2つの投影が<b>同じNodePathへ同じ<see cref="EditorNodeId"/>を割り当てているか</b>を
/// 全単射で確かめる純粋な比較。
///
/// <para>
/// 片方向で見てはいけない — Document側にしか無いPathとReal側にしか無いPathの一方を取りこぼす。
/// 件数の比較を先に置いて短絡する。
/// </para>
/// </summary>
internal static class SceneProjectionIdentityComparer {
    internal static bool Matches(SceneProjection Left,SceneProjection Right){
        if(Left.Nodes.Count!=Right.Nodes.Count){
            return false;
        }

        foreach(var Node in Left.Nodes){
            if(!Right.TryGetId(Node.Path,out var RightId)||!RightId.Equals(Node.Id)){
                return false;
            }
        }

        // 件数が同じで、左の全Pathが右に同じIdで存在するなら、Path集合は一致している
        // (Pathは投影内で一意なため)。Id側の重複はSceneProjectionの構築時に排除済み。
        return true;
    }

    /// <summary>デバッグ・診断用。一致しない最初のPathを返す(全て一致するならnull)。</summary>
    internal static string?FindFirstMismatch(SceneProjection Left,SceneProjection Right){
        if(Left.Nodes.Count!=Right.Nodes.Count){
            return $"ノード数が違います({Left.Nodes.Count} / {Right.Nodes.Count})";
        }

        foreach(var Node in Left.Nodes){
            if(!Right.TryGetId(Node.Path,out var RightId)){
                return $"右に存在しないPath: {Node.Path.ToDisplayString()}";
            }
            if(!RightId.Equals(Node.Id)){
                return $"Idが違います: {Node.Path.ToDisplayString()}";
            }
        }

        return null;
    }
}
