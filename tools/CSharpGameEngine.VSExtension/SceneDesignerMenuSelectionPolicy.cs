using System;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// 複数 Scene を持つファイルの動的サブメニューで、右クリック位置に対応する項目だけを
/// VS 標準の選択済み状態（<c>OLECMDF_LATCHED</c>）にします。通常の VS メニューは
/// 任意の FontWeight を指定できないため、チェック表示が一貫した強調手段です。
/// </summary>
internal static class SceneDesignerMenuSelectionPolicy {
    internal static bool IsLatched(string?TargetSceneFullyQualifiedName,SceneDesignerMenuItem Item)=>
        !string.IsNullOrEmpty(TargetSceneFullyQualifiedName)
        &&string.Equals(TargetSceneFullyQualifiedName,Item.FullyQualifiedName,StringComparison.Ordinal);
}