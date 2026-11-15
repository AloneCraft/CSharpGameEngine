using System.Numerics;
using CSharpGameEngine.Authoring;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Spatial.Abstractions;
using CSharpGameEngine.Spatial.Unity;
namespace CSharpGameEngine.Rendering.View;
/// <summary>
/// UE5 の ThirdPerson 形式のスプリングアーム軌道カメラ。固定距離からターゲット点の周りを yaw/pitch で回転する。純粋な System.Numerics の処理であり、グラフィックスデバイスなしでテストできる。
/// yaw は +Y 周りに +Z から +X へ向かう角度、pitch は +Y 方向への傾きとして測る。Forward/Right の方向計算は <see cref="UnityCoordinateConvention"/> (ADR-0026) に基づき RUF 空間 (<see cref="RufVector"/>/<see cref="RufQuaternion"/>) で導出し、各メソッドの戻り値でのみ <see cref="Vector3"/> に変換する。<see cref="GetView"/>/<see cref="GetProjection"/> は生の <see cref="Vector3"/>/<see cref="Matrix4x4"/> を使い続ける。D3D のビュー/クリップ空間規約は RUF の対象外であるため (Docs/Specifications/Spatial-RUF.md の「Rendering境界」を参照)。
/// </summary>
public struct RenderCamera {
    public const float MinPitchRadians=-80f*MathF.PI/180f;
    public const float MaxPitchRadians=40f*MathF.PI/180f;
    /// <summary>軌道カメラより広いピッチ範囲（フリービューでは、軌道衝突の制約がないため水平付近に保つ必要がない）。<see cref="CreateFirstPerson"/> を参照。</summary>
    public const float MinPitchRadiansFirstPerson=-85f*MathF.PI/180f;
    public const float MaxPitchRadiansFirstPerson=85f*MathF.PI/180f;
    public Vector3 Target;
    /// <summary>ワールド空間のカメラ上方向。Transform 駆動カメラでは、ロールを <see cref="GetView"/> に反映するため、解決済みローカル +Y 基底から設定する。</summary>
    public Vector3 Up;
    public float Yaw;
    public float Pitch;
    public float Distance;
    public float FovYRadians;
    public float NearZ;
    public float FarZ;
    /// <summary>HDR 放射輝度をディスプレイ露出に変換するための EV100。0なら従来の露出を維持する。</summary>
    public float ExposureEv100;
    /// <summary>ネイティブ対数深度の遷移距離 T（メートル）。</summary>
    public float RadialTransitionMeters;
    /// <summary>
    /// <see cref="ApplyMouseDelta"/> が適用するピッチ角の制限範囲。<c>Create*</c> ファクトリが設定する。<see cref="MinPitchRadians"/>
    ///  と <see cref="MaxPitchRadians"/> は軌道カメラの既定値のままなので、呼び出し側で引き続き直接比較できる。
    /// </summary>
    public float PitchClampMinRadians;
    public float PitchClampMaxRadians;
    /// <summary>直接オブジェクトを初期化した場合も、カメラファクトリと同じ対数深度の既定値を使う。</summary>
    public RenderCamera(){
        this.RadialTransitionMeters=RadialDepthCurve.DefaultTransitionMeters;
    }
    /// <summary>
    /// <c>[SceneFactory]</c>: Scene DSLから呼べる(ADR-0040)。Scene DSLは任意のメソッド呼び出しを
    /// 許可しないが、既存のstaticファクトリを全部オブジェクト初期化子へ展開させるのは実用的でないため、
    /// 明示的にオプトインする。引数自体はScene DSLの値式でなければならない。
    /// </summary>
    [SceneFactory]
    public static RenderCamera CreateDefault(Vector3 Target){
        return new RenderCamera {
            Target=Target,
            Up=Vector3.UnitY,
            Yaw=0f,
            Pitch=-15f*MathF.PI/180f,
            Distance=4.5f,
            FovYRadians=MathF.PI/4f,
            NearZ=0.1f,
            FarZ=1000f,
            ExposureEv100=0f,
            RadialTransitionMeters=RadialDepthCurve.DefaultTransitionMeters,
            PitchClampMinRadians=MinPitchRadians,
            PitchClampMaxRadians=MaxPitchRadians,
        };
    }
    /// <summary>
    /// 一人称視点カメラ。<see cref="Distance"/> は0なので、<see cref="Target"/> は視点の位置も兼ねる。呼び出し側はプレイヤーの視点位置に合わせ、毎フレームその値を
    /// 更新する。軌道カメラの <see cref="Target"/> も同様に、プレイヤー位置へ毎フレーム更新する。目と注視点が同じ位置になるのを避けるため、<see cref="GetView"/> は
    /// そこから別の目標点へ向かうのではなく、<see cref="GetForward"/> の方向を見る。
    /// </summary>
    [SceneFactory]
    public static RenderCamera CreateFirstPerson(Vector3 EyePosition){
        return new RenderCamera {
            Target=EyePosition,
            Up=Vector3.UnitY,
            Yaw=0f,
            Pitch=0f,
            Distance=0f,
            FovYRadians=MathF.PI/4f,
            // 非リバース Z の透視投影（CreatePerspectiveFieldOfView）では、深度バッファ精度の大半が、近平面から数単位以内に集中する。
            // そのため近平面と遠平面の比は、各値単独よりもはるかに重要。
            // 元の 0.05 / 1000（比率 20000:1）では、約38単位の室内規模のシーン全体で、
            // 壁や天井に目に見える Z ファイティングが発生した（M0004）。以前の垂直スライスシーンでは、
            // このような視点に近い値は、以前の垂直スライスシーンでは検証していなかった。
            // そこではカメラを、視界内にある2つのオブジェクトのすぐ横に置いていた。0.1 / 300（比率3000:1）なら、
            // この部屋の規模に十分な安全余裕を保ちながら、比率を約7分の1にできる。
            NearZ=0.1f,
            FarZ=300f,
            ExposureEv100=0f,
            RadialTransitionMeters=RadialDepthCurve.DefaultTransitionMeters,
            PitchClampMinRadians=MinPitchRadiansFirstPerson,
            PitchClampMaxRadians=MaxPitchRadiansFirstPerson,
        };
    }
    /// <summary>
    /// 指定したワールド境界全体が、固定された高めの斜め方向から見て画面内に収まるように構図を決める軌道カメラ。採用済みのサムネイルと共有 Preview Host のカメラ基準で使用している、正確な視線方向
    ///  (1, 0.75, 1) と距離の関係 Radius/sin(FovY/2)*1.5 を再利用する。異なる角度を持つ5つ目の「全体を表示」方式は作らない。
    /// <see cref="RenderCamera"/> には目の位置を直接指定するコンストラクターがないため、<see cref="GetForward"/> の仕様である Forward=(
    /// CosPitch*SinYaw, SinPitch, CosPitch*CosYaw) を逆算し、<see cref="Target"/> / <see cref="Yaw"/> /
    /// <see cref="Pitch"/> / <see cref="Distance"/> によるこの型の軌道表現に変換する。
    /// </summary>
    [SceneFactory]
    public static RenderCamera CreateBoundsFit(Vector3 BoundsMin,Vector3 BoundsMax){
        var Target=(BoundsMin+BoundsMax)/2f;
        var Radius=MathF.Max(Vector3.Distance(BoundsMin,BoundsMax)/2f,0.001f);
        var FovY=MathF.PI/4f;
        // Distance=Radius/sin(FovY/2) は、境界球を囲む視錐台が球にちょうど接する距離。
        // 半径は AABB の対角線の半分なので、この向きだけでなく、どの向きからもボックス全体を囲む。
        // その球が垂直視野角いっぱいにちょうど収まる。球のシルエットは外側のどこから見ても同じなので、視線方向に依存しない。
        // *1.1f はその正確な値にわずかな安全余裕を加えるだけ。
        // 任意の回転を想定した、採用済みサムネイル／プレビューの *1.5f / *3.0f という基準とは異なる。
        // 固定されたデモ角度なら、接する距離に近づけても
        // レベルのすべての角をフレーム内に保てる。
        var Distance=Radius/MathF.Sin(FovY/2f)*1.1f;
        var Forward=-Vector3.Normalize(new Vector3(1f,0.75f,1f));
        return new RenderCamera {
            Target=Target,
            Up=Vector3.UnitY,
            Yaw=MathF.Atan2(Forward.X,Forward.Z),
            Pitch=MathF.Asin(Forward.Y),
            Distance=Distance,
            FovYRadians=FovY,
            NearZ=0.1f,
            FarZ=Distance+(Radius*4f),
            ExposureEv100=0f,
            RadialTransitionMeters=RadialDepthCurve.DefaultTransitionMeters,
            PitchClampMinRadians=MinPitchRadians,
            PitchClampMaxRadians=MaxPitchRadians,
        };
    }
    /// <summary>目からターゲットへ向かう、ピッチを含む3D前方方向。</summary>
    public readonly Vector3 GetForward(){
        // 子側のピッチ（カメラのヨーを適用する前のローカル座標）を先に適用し、その後に親側のヨーを適用する。
        // RufQuaternion.operator* の演算規則を参照。ピッチは Right ではなく RufVector.Left を軸に回転させる。
        // Forward を Right のまわりに +Pitch 回転させると -Up 方向へ傾き、ここで求める向きと逆になる。
        // この型の仕様である「ピッチを上げると +Y 方向へ傾く」動作に合わせる。
        // 標準の軸角回転の式を手計算で確認し、次のテストでこの規則を固定した。
        // CameraTests.GetForward_MatchesLegacyTrigFormula_AcrossYawPitchSweep).
        var Facing=RufQuaternion.FromAxisAngle(RufVector.Up, this.Yaw)*RufQuaternion.FromAxisAngle(RufVector.Left, this.Pitch);
        var Forward=RufQuaternion.RotateVector(Facing,RufVector.Forward).Normalize();
        return(Vector3)Spatial<UnityCoordinateConvention>.ToXyz(Forward);
    }
    /// <summary>
    /// <see cref="GetForward"/> に平行な成分を取り除いた後の RenderCamera の上方向。既定値で構築された従来型カメラには上方向ベクトルが保存されていない。その場合はビュー
    /// が退化しないよう、ワールド上方向との互換性を維持する。
    /// </summary>
    public readonly Vector3 GetUp(){
        Vector3 Forward=this.GetForward();
        return TryNormalizeOrientation(Forward,this.Up,out _,out Vector3 Up)
            ?Up
            :GetFallbackUp(Forward);
    }
    /// <summary>
    /// 解決済み Transform の基底をこのカメラに適用する。<paramref name="Forward"/> は Transform のローカル +Z 方向、
    /// <paramref name="Up"/> はローカル +Y 方向。カメラを変更する前に両方を正規直交化する。有限値でない基底や潰れた基底を拒否した場合も、最後に有効だったビューを維持する。
    /// </summary>
    public bool TrySetWorldOrientation(Vector3 Forward,Vector3 Up){
        if(!TryNormalizeOrientation(Forward,Up,out Vector3 NormalizedForward,out Vector3 NormalizedUp)){
            return false;
        }
        this.Yaw=MathF.Atan2(NormalizedForward.X,NormalizedForward.Z);
        this.Pitch=MathF.Asin(Math.Clamp(NormalizedForward.Y,-1f,1f));
        this.Up=NormalizedUp;
        return true;
    }
    /// <summary>解決済み Transform の基底が、見た目上、従来の単位姿勢と同じか判定する。ローカル +Z 方向のロールだけを単位カメラと誤認しないよう、両軸を検査する。</summary>
    public static bool IsIdentityWorldOrientation(Vector3 Forward,Vector3 Up){
        const float CosineTolerance=1e-5f;
        return TryNormalizeOrientation(Forward,Up,out Vector3 NormalizedForward,out Vector3 NormalizedUp)
            &&Vector3.Dot(NormalizedForward,Vector3.UnitZ)>=1f-CosineTolerance
            &&Vector3.Dot(NormalizedUp,Vector3.UnitY)>=1f-CosineTolerance;
    }
    /// <summary>
    /// Transform のローカル +Z / +Y のワールド基底を正規化し、+Z 方向を維持する。上方向ベクトルを前方ベクトルへの射影で補正し、親の不均一スケールによる GPU ビュー行列の歪みを防ぐ。
    /// 潰れた基底や有限でない基底は拒否する。
    /// </summary>
    public static bool TryNormalizeOrientation(
        Vector3 Forward,Vector3 Up,
        out Vector3 NormalizedForward,out Vector3 NormalizedUp){
        if(!TryNormalize(Forward,out NormalizedForward)){
            NormalizedUp=default;
            return false;
        }
        Vector3 OrthogonalUp=Up-(NormalizedForward*Vector3.Dot(Up,NormalizedForward));
        return TryNormalize(OrthogonalUp,out NormalizedUp);
    }
    private static bool TryNormalize(Vector3 Value,out Vector3 Normalized){
        var LengthSquared=Value.LengthSquared();
        if(!float.IsFinite(Value.X)||!float.IsFinite(Value.Y)||!float.IsFinite(Value.Z)
            ||!float.IsFinite(LengthSquared)||LengthSquared<=1e-12f){
            Normalized=default;
            return false;
        }
        Normalized=Vector3.Normalize(Value);
        return true;
    }
    private static Vector3 GetFallbackUp(Vector3 Forward){
        Vector3 Reference=MathF.Abs(Forward.Y)<0.999f?Vector3.UnitY:Vector3.UnitZ;
        Vector3 Orthogonal=Reference-(Forward*Vector3.Dot(Reference,Forward));
        return Vector3.Normalize(Orthogonal);
    }
    /// <summary>XZ 平面に射影した前方方向（ピッチを除く）。WASD 移動の基準。</summary>
    public readonly Vector3 GetFlatForward(){
        return(Vector3)Spatial<UnityCoordinateConvention>.ToXyz(this.GetFlatForwardRuf());
    }
    /// <summary>
    /// XZ 平面上の右方向。Handedness を考慮する <see cref="Spatial{TConvention}.RightFromUpForward"/> から求める（ADR-0025）。
    /// <see cref="UnityCoordinateConvention"/>（恒等変換、LeftHanded）では、従来の
    /// <c>Cross(Vector3.UnitY, GetFlatForward())</c> による式と数学的に同一であることを示せる（
    /// CameraTests.GetFlatRight_MatchesLegacyCrossProductFormula_AcrossYawSweep で永続的に検証）。横移動したときに操作感が正しいかは、
    /// まだ実際に操作して確認する必要がある（ADR-0004 §6 の先例）。この変更で証明したのは式の内部一貫性までであり、左右がキーボード操作の直感と一致するかではない。
    /// </summary>
    public readonly Vector3 GetFlatRight(){
        var Right=Spatial<UnityCoordinateConvention>.RightFromUpForward(RufVector.Up, this.GetFlatForwardRuf());
        return(Vector3)Spatial<UnityCoordinateConvention>.ToXyz(Right);
    }
    private readonly RufVector GetFlatForwardRuf(){
        var YawOnly=RufQuaternion.FromAxisAngle(RufVector.Up, this.Yaw);
        return RufQuaternion.RotateVector(YawOnly,RufVector.Forward).Normalize();
    }
    public readonly Vector3 GetEyePosition(){
        return this.Target-(this.GetForward()* this.Distance);
    }
    /// <summary>
    /// Distance > 0（軌道カメラ）の場合は、従来どおり目から <see cref="Target"/> を見る。Distance == 0（
    /// <see cref="CreateFirstPerson"/> を参照）の場合は目と Target が一致するため、CreateLookAt の始点と終点が同じになり、ビュー方向が未定義または退化する
    /// 。その代わり、目の位置から <see cref="GetForward"/> の方向を見る。
    /// </summary>
    public readonly Matrix4x4 GetView(){
        var Eye= this.GetEyePosition();
        var LookAtPoint= this.Distance>1e-4f? this.Target:Eye+ this.GetForward();
        return BuildViewMatrix(Eye,LookAtPoint,this.GetUp());
    }
    public readonly Matrix4x4 GetProjection(float AspectRatio){
        return Matrix4x4.CreatePerspectiveFieldOfView(this.FovYRadians,AspectRatio, this.NearZ, this.FarZ);
    }
    /// <summary>実行時カメラとプレビューカメラで共有する右手系 LookAt の構築処理（ADR-0026）。</summary>
    public static Matrix4x4 BuildViewMatrix(Vector3 Eye,Vector3 Target,Vector3 Up)=>Matrix4x4.CreateLookAt(Eye,Target,Up);
    /// <summary>生のマウス差分入力をヨー角／ピッチ角に適用する。ジンバル反転を防ぐため、ピッチを制限する。</summary>
    public void ApplyMouseDelta(float DeltaX,float DeltaY,float YawSensitivity,float PitchSensitivity){
        this.Yaw+=DeltaX*YawSensitivity;
        this.Pitch=Math.Clamp(this.Pitch-(DeltaY*PitchSensitivity), this.PitchClampMinRadians, this.PitchClampMaxRadians);
    }
}
