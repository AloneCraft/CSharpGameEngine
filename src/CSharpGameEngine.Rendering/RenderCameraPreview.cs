using System.Numerics;

namespace CSharpGameEngine.Rendering;

/// <summary>
/// プレゼンテーション面に埋め込む、カメラ相対のサムネイル描画パス1つ。矩形は面の左上を原点としたピクセル座標で指定し、プロバイダーが現在の面の範囲にクリップする。除外対象は任意で、サムネイルに限り作成元
/// のカメラプロキシを描画しない。<see cref="Depth"/> には、この投影で検証済みの深度符号化方式を格納する。観測者ビューの場合は、圧縮されたワールドの中心も深度パケットに格納し、
/// <see cref="ViewProjection"/> は回転のみのビュー行列と投影行列の積とする。<see cref="MarkRadialCenter"/> が有効なら、観測者の中心位置に
/// メインカメラの視錐台を描画する。
/// </summary>
public readonly record struct RenderCameraPreview(
    int X,int Y,int Width,int Height,
    Matrix4x4 ViewProjection,Vector3 CameraPosition,RenderViewDepth Depth,
    int ExcludedObjectIndex=-1,
    bool MarkRadialCenter=false){
    public const int MaximumCount=3;
}

/// <summary>サムネイル描画パスが黙って失われず、準備されたことを示す観測可能な事実。</summary>
public readonly record struct RenderCameraPreviewDiagnostics(
    int SceneObjectCount,int FrustumCandidateCount,int DrawCallCount,
    uint ViewProjectionHash);
