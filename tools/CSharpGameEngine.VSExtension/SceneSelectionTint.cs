using System.Collections.Generic;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// 選択ハイライトの強さ(シェーダの<c>Selected</c>定数)を決める純粋関数。
///
/// <para>
/// <see cref="SceneRenderer"/>の定数バッファ更新はGPUリソースを要してユニットテストできないため、
/// 「どのオブジェクトをどの強さで光らせるか」の判断だけをここへ切り出している(このリポジトリが
/// <c>SceneDesignerContextMenuPolicy</c>等で踏襲している方針)。
/// </para>
///
/// <para>
/// シェーダ側は<c>shaded + Selected * SelectionTint</c>と加算しているだけなので、0..1の中間値を
/// 入れれば濃淡が付く。これを利用して primary と副選択を見分けられるようにする —
/// M0020 Phase 2まではハイライトが primary 1件のみで、Ctrl+LMBで複数選択しても見た目が変わらず
/// 「複数選択ができない」と観測されていた(実機検証#5)。
/// </para>
/// </summary>
internal static class SceneSelectionTint {
    /// <summary>選択の主対象(最後に選んだノード)。</summary>
    internal const float PrimaryWeight=1f;

    /// <summary>複数選択のうち primary 以外。primary より弱く光らせて主従を見分けられるようにする。</summary>
    internal const float SecondaryWeight=0.5f;

    internal const float UnselectedWeight=0f;

    internal static float GetWeight(NodePath Candidate,NodePath?PrimaryPath,IReadOnlyCollection<NodePath>?SelectedPaths){
        if(PrimaryPath is { } Primary&&Candidate.Equals(Primary)){
            return PrimaryWeight;
        }

        if(SelectedPaths is null){
            return UnselectedWeight;
        }

        foreach(NodePath Selected in SelectedPaths){
            if(Candidate.Equals(Selected)){
                return SecondaryWeight;
            }
        }

        return UnselectedWeight;
    }
}
