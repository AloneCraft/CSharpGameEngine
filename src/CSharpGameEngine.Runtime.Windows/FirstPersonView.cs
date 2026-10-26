using System.Numerics;
using CSharpGameEngine.Entities;
using RenderCamera=CSharpGameEngine.Rendering.View.RenderCamera;
namespace CSharpGameEngine.Runtime.Windows;
/// <summary>
/// 1つのメッシュをどう描画するかを指定する（ADR-0078/M0107）。UEのプリミティブコンポーネントにおけるOwnerNoSee／OnlyOwnerSeeとCastShadowに相当する。
/// <see cref="VisibleToCamera"/>が無効なメッシュはカメラに映らないが、影は落とす（例：一人称キャラクターのボディ）。
/// <see cref="CastsShadow"/>が無効なら映るが、影は落とさない（例：一人称視点の腕）。このコンポーネントがないEntityは通常どおり描画され、影も落とす。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Rendering,10)]
public struct MeshVisibility() {
    public bool VisibleToCamera=true;
    public bool CastsShadow=true;
}
/// <summary>
/// ゲーム実行中に変更する、メッシュのマテリアル発光係数（ADR-0078/M0109）。UEの動的マテリアルインスタンスにスカラー値を与え、
/// 発光入力を変更する方式に相当する。Vehicleのブレーキライト（<c>BrakeLightPower</c>）が使用例。
/// メッシュのマテリアルはそれぞれ固有の発光色とテクスチャを保持し、これらに<see cref="Scale"/>を掛ける。
/// このコンポーネントのないEntityは、アセット作成時のマテリアルで描画する。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Rendering,12)]
public struct MeshEmissive() {
    /// <summary>マテリアルの発光に掛ける係数（0以上。1ならアセット作成時の値のまま）。</summary>
    public float Scale=1f;
}
/// <summary>
/// UEのプリミティブに対する一人称描画（FirstPersonPrimitiveType.FIRST_PERSON、ADR-0078/M0107）。
/// メッシュはカメラの水平視野角をビュー本来の<see cref="DefaultFieldOfViewDegrees"/>ではなく<see cref="FieldOfViewDegrees"/>とし、
/// 壁に突き出ないよう<see cref="Scale"/>でカメラ側へ寄せて描画する。どちらもメッシュのカメラ空間位置を変更する
///（<see cref="FirstPersonViewWarp"/>）ため、レンダラーに別の投影処理は不要。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Rendering,11)]
public struct FirstPersonView() {
    /// <summary>UEのFirstPersonFieldOfView（水平角）。FirstPersonテンプレートでは70。</summary>
    public float FieldOfViewDegrees=70f;
    /// <summary>カメラの描画に使う水平視野角（カメラマネージャーのDefaultFOV）。FirstPersonテンプレートでは90。</summary>
    public float DefaultFieldOfViewDegrees=90f;
    /// <summary>UEのFirstPersonScale。FirstPersonテンプレートでは0.6。</summary>
    public float Scale=0.6f;
}
/// <summary>メッシュに一人称視野角とスケールを適用するカメラ空間内の変換。</summary>
public static class FirstPersonViewWarp {
    /// <summary>
    /// メッシュの画面位置を広げる倍率：<c>tan(default / 2) / tan(first person / 2)</c>。
    /// カメラ本来の投影で見たとき、カメラ空間の点(x, y, z)が、狭い一人称投影と同じ深度で置かれる位置に見えるよう、xとyにこの値を掛ける。
    /// 視野角が0～180度の範囲外なら1を返す。
    /// </summary>
    public static float Spread(in FirstPersonView View){
        if(!(View.FieldOfViewDegrees>0f&&View.FieldOfViewDegrees<180f)||!(View.DefaultFieldOfViewDegrees>0f&&View.DefaultFieldOfViewDegrees<180f)){
            return 1f;
        }
        return MathF.Tan(View.DefaultFieldOfViewDegrees*MathF.PI/360f)/MathF.Tan(View.FieldOfViewDegrees*MathF.PI/360f);
    }
    /// <summary>
    /// オブジェクトのカメラ相対ワールド行列の後に掛け、一人称プリミティブにする行ベクトル行列。
    /// カメラ座標系のxとyに<see cref="Spread"/>を掛け、すべての要素に<see cref="FirstPersonView.Scale"/>を掛けて視点周りにスケールする。
    /// スケールが正でない場合は単位行列。
    /// </summary>
    public static Matrix4x4 Matrix(in RenderCamera Camera,in FirstPersonView View){
        if(!(View.Scale>0f)||!float.IsFinite(View.Scale)){
            return Matrix4x4.Identity;
        }
        var Spread=FirstPersonViewWarp.Spread(View);
        var Eye=Camera.GetEyePosition();
        var Rotation=Camera.GetView();
        Rotation.M41=0f;
        Rotation.M42=0f;
        Rotation.M43=0f;
        if(!Matrix4x4.Invert(Rotation,out var Inverse)){
            return Matrix4x4.Identity;
        }
        var Scale=Matrix4x4.CreateScale(View.Scale*Spread,View.Scale*Spread,View.Scale);
        return Matrix4x4.CreateTranslation(-Eye)*Rotation*Scale*Inverse*Matrix4x4.CreateTranslation(Eye);
    }
}
