using System;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// 常時表示の「ヒエラルキー」ツールウィンドウ(<see cref="HierarchyToolWindowPane"/>)向けの
/// プロセス内メディエータ。C# ソースが唯一の正本なので、ここで運ぶモデルはライブ Roslyn 解析の
/// 結果(<see cref="SceneModel"/>)であり、`.scene` ファイルではない(ADR-0040)。
///
/// <para>
/// ヒエラルキーでノードを選択すると、Scene デザイナーへの一方向のハイライト要求を発行する
/// (ADR-0020 以来の一方向性。ビューポート側のクリックからヒエラルキーを選択する逆経路は
/// M0020 Phase 2 で <see cref="SceneSelectionService"/> 側へ入る)。
/// </para>
///
/// <para>
/// 旧 `.scene` JSON チャンネル(<c>ActiveScene</c>/<c>RequestReload</c>/<c>ReloadRequested</c>)と、
/// 購読者を持たなかった <c>AllSceneModels</c> 系は M0020 Phase 0 で削除した。Scene デザイナーは
/// <see cref="SceneEditorSessionService"/> の <see cref="SceneDocumentSnapshot"/> から対象を解決する。
/// </para>
/// </summary>
internal enum HierarchyEditKind {
    AddChild,
    Rename,
    Remove,
    Reparent,
    Reorder,
}

internal sealed record HierarchyEditRequest(
    HierarchyEditKind Kind,
    IReadOnlyList<NodePath> Paths,
    NodePath?TargetPath=null,
    int TargetIndex=-1,
    string?NewName=null);
internal static class SceneEditorHub {
    /// <summary>Raised whenever the analysed scene changes - including to null, when the active document declares none.</summary>
    public static event Action<SceneModel?>?SceneModelChanged;

    /// <summary>Selection is keyed by <see cref="NodePath"/>: names are optional and not unique, and text spans move on every edit.</summary>
    public static event Action<NodePath?>?SelectedNodePathChanged;
    public static event Action<HierarchyNodeSelection>?HierarchySelectionChanged;
    public static event Action<NodePath>?HierarchyNavigationRequested;
    public static event Action<HierarchyEditRequest>?HierarchyEditRequested;

    internal static void RequestHierarchyEdit(HierarchyEditRequest Request)=>
        HierarchyEditRequested?.Invoke(Request);

    public static SceneModel?ActiveSceneModel { get;private set;}

    public static NodePath?SelectedNodePath { get;private set;}
    public static HierarchyNodeSelection HierarchySelection { get;private set;}=HierarchyNodeSelection.Empty;

    public static void PublishSceneModel(SceneModel?Model){
        ActiveSceneModel=Model;
        SceneModelChanged?.Invoke(Model);
    }

    public static void PublishSelectedNodePath(NodePath?Path){
        SelectedNodePath=Path;
        SelectedNodePathChanged?.Invoke(Path);
    }

    public static void PublishHierarchySelection(HierarchyNodeSelection Selection){
        HierarchySelection=Selection;
        PublishSelectedNodePath(Selection.PrimaryPath);
        HierarchySelectionChanged?.Invoke(Selection);
    }

    public static void RequestHierarchyNavigation(NodePath Path)=>
        HierarchyNavigationRequested?.Invoke(Path);

    /// <summary>
    /// M0051 Step 1-4: Scene Designer本体(3Dビューポート)以外の編集面 — Hierarchy / Inspector —
    /// が今キーボードフォーカスを持っているか。Ctrl+Sの検知は<see cref="SceneHwndHost"/>の
    /// <c>GetKeyState</c>ポーリング1本しか無く、以前はビューポートのフォーカスだけを条件にしていた
    /// ため、<b>Hierarchyで編集した直後のCtrl+Sが保存へ届かなかった</b>(実機報告の直接原因)。
    ///
    /// <para>
    /// 各サーフェスが自分のフォーカス状態を名前付きで登録し、ここで論理和を取る。ウィンドウが
    /// 閉じられたら<see cref="SetSurfaceFocus"/>にfalseを渡すか登録ごと消えるため、閉じた
    /// ウィンドウのフォーカスが残り続けることはない。
    /// </para>
    /// </summary>
    private static readonly System.Collections.Generic.Dictionary<string,bool>SurfaceFocus=
        new(System.StringComparer.Ordinal);

    internal static void SetSurfaceFocus(string SurfaceName,bool HasFocus){
        SurfaceFocus[SurfaceName]=HasFocus;
    }

    internal static bool AnyAuxiliarySurfaceHasFocus{
        get{
            foreach(System.Collections.Generic.KeyValuePair<string,bool>Entry in SurfaceFocus){
                if(Entry.Value){
                    return true;
                }
            }

            return false;
        }
    }
}
