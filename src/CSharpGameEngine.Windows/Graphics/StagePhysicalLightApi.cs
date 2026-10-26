using System.Numerics;
using CSharpGameEngine.Rendering.Pipeline;
namespace CSharpGameEngine.Graphics.D3D12;
/// <summary>
/// 物理ライティング用の公開送信API。レンダラーのクラスタ化GPUストレージを進化させる間、アセット作成時の自然単位を既存のレンダラー用測光シェーダー入力へ変換する。
/// 方向は常にTransformのローカル+Z、つまり光が進む方向を示す。従来の面から光源へ向かうベクトルではない。
/// </summary>
public static class StagePhysicalLightApi {
    private const float MinimumDirectionLengthSquared=1e-12f;

    public static void SetDirectionalLight(
        this StageRenderer Renderer,Vector3 LightForward,Vector3 LinearRgb,float IlluminanceLux,
        bool CastsShadows,int ShadowPriority,float ShadowBiasMeters){
        ArgumentNullException.ThrowIfNull(Renderer);
        ValidateShadowSettings(ShadowPriority,ShadowBiasMeters);
        // 従来のレンダラーでは、逆向きの（面から光源へ向かう）規約を使う。シャドウパスは
        // 現状ディレクショナルライトのみを扱う。明示的な設定値はここで検証し、
        // 既存パスを置き換えるアトラス経路で使用する。
        Renderer.SetDirectionalLight(
            -NormalizeForward(LightForward),LinearRgb,IlluminanceLux,CastsShadows,ShadowBiasMeters,ShadowPriority);
    }

    /// <summary>
    /// 今フレームに物理ディレクショナルライトを1つ追加する。レンダラーが保持するのは最大4つ。
    /// 唯一の従来式シャドウマップには、優先度が最も高いシャドウキャスターを使う。
    /// </summary>
    public static void AddDirectionalLight(
        this StageRenderer Renderer,Vector3 LightForward,Vector3 LinearRgb,float IlluminanceLux,
        bool CastsShadows,int ShadowPriority,float ShadowBiasMeters){
        ArgumentNullException.ThrowIfNull(Renderer);
        ValidateShadowSettings(ShadowPriority,ShadowBiasMeters);
        Renderer.AddDirectionalLightTowardLight(
            -NormalizeForward(LightForward),LinearRgb,IlluminanceLux,
            CastsShadows,ShadowPriority,ShadowBiasMeters);
    }

    public static void AddPointLight(
        this StageRenderer Renderer,Vector3 Position,Vector3 LinearRgb,float LuminousFluxLumens,
        float RangeMeters,bool CastsShadows,int ShadowPriority,float ShadowBiasMeters){
        ArgumentNullException.ThrowIfNull(Renderer);
        PhysicalLightUnits.ValidateLightRange(RangeMeters);
        ValidateShadowSettings(ShadowPriority,ShadowBiasMeters);
        Renderer.AddPointLight(Position,LinearRgb,PhysicalLightUnits.PointLumensToCandela(LuminousFluxLumens),
            RangeMeters,ShadowPriority,CastsShadows,ShadowBiasMeters);
    }

    public static void AddSpotLight(
        this StageRenderer Renderer,Vector3 Position,Vector3 LightForward,Vector3 LinearRgb,
        float LuminousFluxLumens,float RangeMeters,float InnerConeFullAngleDegrees,float OuterConeFullAngleDegrees,
        bool CastsShadows,int ShadowPriority,float ShadowBiasMeters){
        ArgumentNullException.ThrowIfNull(Renderer);
        PhysicalLightUnits.ValidateLightRange(RangeMeters);
        PhysicalLightUnits.ValidateSpotAngles(InnerConeFullAngleDegrees,OuterConeFullAngleDegrees);
        ValidateShadowSettings(ShadowPriority,ShadowBiasMeters);
        Renderer.AddSpotLight(
            Position,NormalizeForward(LightForward),LinearRgb,
            PhysicalLightUnits.SpotLumensToCandela(
                LuminousFluxLumens,InnerConeFullAngleDegrees,OuterConeFullAngleDegrees),
            RangeMeters,
            PhysicalLightUnits.FullConeAngleDegreesToHalfAngleRadians(InnerConeFullAngleDegrees),
            PhysicalLightUnits.FullConeAngleDegreesToHalfAngleRadians(OuterConeFullAngleDegrees),ShadowPriority,CastsShadows,ShadowBiasMeters);
    }

    /// <summary>
    /// 片面の矩形ライトを、半球に制限した決定的な4サンプルとして送信する。4つの一様サンプルは総Lambert光束
    ///（π × nits × 面積）を維持し、Entityの実際のTransformに従う。矩形用LTC経路を選ぶまでの互換経路。
    /// </summary>
    public static void AddAreaLight(
        this StageRenderer Renderer,Vector3 Position,Vector3 LightForward,Vector3 LinearRgb,
        float LuminanceNits,float WidthMeters,float HeightMeters,float RangeMeters,
        bool CastsShadows,int ShadowPriority,float ShadowBiasMeters){
        ArgumentNullException.ThrowIfNull(Renderer);
        PhysicalLightUnits.ValidateLightRange(RangeMeters);
        _=PhysicalLightUnits.AreaNitsToNormalCandela(LuminanceNits,WidthMeters,HeightMeters);
        ValidateShadowSettings(ShadowPriority,ShadowBiasMeters);
        var Forward=NormalizeForward(LightForward);
        var Reference=MathF.Abs(Vector3.Dot(Forward,Vector3.UnitY))>0.99f?Vector3.UnitX:Vector3.UnitY;
        var Right=Vector3.Normalize(Vector3.Cross(Reference,Forward));
        var Up=Vector3.Normalize(Vector3.Cross(Forward,Right));
        Renderer.AddAreaLight(
            Position,Forward,Right,Up,LinearRgb,LuminanceNits,WidthMeters,HeightMeters,RangeMeters,
            ShadowPriority,CastsShadows,ShadowBiasMeters,PhysicalLightKind.Area);
    }

    private static Vector3 NormalizeForward(Vector3 LightForward){
        if(!float.IsFinite(LightForward.X)||!float.IsFinite(LightForward.Y)||!float.IsFinite(LightForward.Z)||LightForward.LengthSquared()<=MinimumDirectionLengthSquared){
            throw new ArgumentOutOfRangeException(nameof(LightForward),"Light forward must be a finite non-zero vector.");
        }
        return Vector3.Normalize(LightForward);
    }

    private static void ValidateShadowSettings(int ShadowPriority,float ShadowBiasMeters){
        if(!float.IsFinite(ShadowBiasMeters)||ShadowBiasMeters<0f){
            throw new ArgumentOutOfRangeException(nameof(ShadowBiasMeters),"Shadow bias must be finite and non-negative meters.");
        }
    }
}
