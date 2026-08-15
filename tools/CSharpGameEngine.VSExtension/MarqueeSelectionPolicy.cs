using System.Collections.Generic;
using System.Linq;

namespace CSharpGameEngine.VSExtension;

/// <summary>矩形選択をセッションの複数選択へ結合した結果。</summary>
internal readonly struct MarqueeSelectionUpdate {
    internal MarqueeSelectionUpdate(IReadOnlyList<EditorNodeId>NodeIds,EditorNodeId?PrimaryNodeId){
        this.NodeIds=NodeIds;
        this.PrimaryNodeId=PrimaryNodeId;
    }

    internal IReadOnlyList<EditorNodeId>NodeIds { get; }

    internal EditorNodeId?PrimaryNodeId { get; }
}

/// <summary>
/// WPFやsessionを知らない矩形選択結合ポリシー。通常ドラッグは置換、Ctrlドラッグは既存選択へ
/// 加算する。矩形内の最終ノードをprimaryにしてHierarchyの単一表示先を決定する。
/// </summary>
internal static class MarqueeSelectionPolicy {
    internal static MarqueeSelectionUpdate Combine(
        IReadOnlyList<EditorNodeId>Current,IReadOnlyList<EditorNodeId>Hits,
        bool Additive,EditorNodeId?CurrentPrimary){
        List<EditorNodeId>Updated=Additive?[..Current]:[];
        HashSet<EditorNodeId>Seen=[..Updated];
        EditorNodeId?LastNew=null;
        foreach(EditorNodeId Hit in Hits){
            if(Seen.Add(Hit)){
                Updated.Add(Hit);
                LastNew=Hit;
            } else if(!Additive){
                LastNew=Hit;
            }
        }

        EditorNodeId?Primary=LastNew??(Additive?CurrentPrimary:null);
        if(Primary is { }Candidate&&!Updated.Contains(Candidate)){
            Primary=Updated.Count>0?Updated[^1]:null;
        }

        return new MarqueeSelectionUpdate(Updated,Primary);
    }
}
