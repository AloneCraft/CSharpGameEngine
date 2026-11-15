using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// メッシュ未宣言のノードはGizmoMeshFactoryのプレースホルダー立方体で代替描画される
/// (SceneModelRenderObjectBuilder)。カメラプレビューの視点はノードのワールド位置その
/// ものなので、カメラ自身のノードを描くと視点が立方体の内側に入り、NearZ既定値0.1が
/// 半径0.15をクリップできず、CullNoneで背面も描かれるためカードがtint色で塗り潰される。
/// </summary>
internal static class CameraPreviewDrawPolicy {
    /// <summary>メインビューポート(PassIndex==0)は常に描画。プレビューパスはそのカードの
    /// 視点であるノード自身だけを除外する。</summary>
    internal static bool ShouldDraw(int PassIndex,NodePath ObjectNodeId,NodePath CameraNodeId)=>
        PassIndex==0||!ObjectNodeId.Equals(CameraNodeId);
}
