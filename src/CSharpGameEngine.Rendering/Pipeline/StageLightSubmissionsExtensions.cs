using System.Numerics;
namespace CSharpGameEngine.Rendering.Pipeline;
/// <summary>
/// 物理単位と型付きの光源の提出を、<see cref="StageLightSubmissions"/> の提出へ写す（M0120 単位 5b-2 で L0 の
/// <c>StagePhysicalLightApi</c>・<c>TypedLightBackend</c>・<c>EmissivePatchSubmission</c> から式を変えずに移した）。
/// 物理単位の方向は Transform のローカル +Z、つまり光が進む向き。<see cref="StageLightSubmissions.SetDirectionalLight"/> などの、面から光源へ向かう向きとは逆。
/// </summary>
public static class StageLightSubmissionsExtensions {
    private const float MinimumDirectionLengthSquared=1e-12f;
    /// <summary>物理単位の単一の方向光（照度 lux）。</summary>
    public static void SetPhysicalDirectionalLight(this StageLightSubmissions Lights,Vector3 LightForward,Vector3 LinearRgb,float IlluminanceLux,bool CastsShadows,int ShadowPriority,float ShadowBiasMeters){
        ArgumentNullException.ThrowIfNull(Lights);
        ValidateShadowSettings(ShadowBiasMeters);
        Lights.SetDirectionalLight(-NormalizeForward(LightForward),LinearRgb,IlluminanceLux,CastsShadows,ShadowBiasMeters,ShadowPriority);
    }
    /// <summary>このフレームに物理単位の方向光を 1 つ追加する（4 灯まで）。</summary>
    public static void AddPhysicalDirectionalLight(this StageLightSubmissions Lights,Vector3 LightForward,Vector3 LinearRgb,float IlluminanceLux,bool CastsShadows,int ShadowPriority,float ShadowBiasMeters){
        ArgumentNullException.ThrowIfNull(Lights);
        ValidateShadowSettings(ShadowBiasMeters);
        Lights.AddDirectionalLightTowardLight(-NormalizeForward(LightForward),LinearRgb,IlluminanceLux,CastsShadows,ShadowPriority,ShadowBiasMeters);
    }
    /// <summary>このフレームに物理単位の点光源（光束 lm）を 1 つ追加する。</summary>
    public static void AddPhysicalPointLight(this StageLightSubmissions Lights,Vector3 Position,Vector3 LinearRgb,float LuminousFluxLumens,float RangeMeters,bool CastsShadows,int ShadowPriority,float ShadowBiasMeters){
        ArgumentNullException.ThrowIfNull(Lights);
        PhysicalLightUnits.ValidateLightRange(RangeMeters);
        ValidateShadowSettings(ShadowBiasMeters);
        Lights.AddPointLight(Position,LinearRgb,PhysicalLightUnits.PointLumensToCandela(LuminousFluxLumens),RangeMeters,ShadowPriority,CastsShadows,ShadowBiasMeters);
    }
    /// <summary>このフレームに物理単位のスポットライト（光束 lm、円錐は全角の度）を 1 つ追加する。</summary>
    public static void AddPhysicalSpotLight(this StageLightSubmissions Lights,Vector3 Position,Vector3 LightForward,Vector3 LinearRgb,
        float LuminousFluxLumens,float RangeMeters,float InnerConeFullAngleDegrees,float OuterConeFullAngleDegrees,
        bool CastsShadows,int ShadowPriority,float ShadowBiasMeters){
        ArgumentNullException.ThrowIfNull(Lights);
        PhysicalLightUnits.ValidateLightRange(RangeMeters);
        PhysicalLightUnits.ValidateSpotAngles(InnerConeFullAngleDegrees,OuterConeFullAngleDegrees);
        ValidateShadowSettings(ShadowBiasMeters);
        Lights.AddSpotLight(Position,NormalizeForward(LightForward),LinearRgb,
            PhysicalLightUnits.SpotLumensToCandela(LuminousFluxLumens,InnerConeFullAngleDegrees,OuterConeFullAngleDegrees),
            RangeMeters,
            PhysicalLightUnits.FullConeAngleDegreesToHalfAngleRadians(InnerConeFullAngleDegrees),
            PhysicalLightUnits.FullConeAngleDegreesToHalfAngleRadians(OuterConeFullAngleDegrees),ShadowPriority,CastsShadows,ShadowBiasMeters);
    }
    /// <summary>このフレームに物理単位の片面の矩形の光源（輝度 nit）を 1 つ追加する。右と上は光の向きから決める。</summary>
    public static void AddPhysicalAreaLight(this StageLightSubmissions Lights,Vector3 Position,Vector3 LightForward,Vector3 LinearRgb,
        float LuminanceNits,float WidthMeters,float HeightMeters,float RangeMeters,
        bool CastsShadows,int ShadowPriority,float ShadowBiasMeters){
        ArgumentNullException.ThrowIfNull(Lights);
        PhysicalLightUnits.ValidateLightRange(RangeMeters);
        _=PhysicalLightUnits.AreaNitsToNormalCandela(LuminanceNits,WidthMeters,HeightMeters);
        ValidateShadowSettings(ShadowBiasMeters);
        var Forward=NormalizeForward(LightForward);
        var Reference=MathF.Abs(Vector3.Dot(Forward,Vector3.UnitY))>0.99f?Vector3.UnitX:Vector3.UnitY;
        var Right=Vector3.Normalize(Vector3.Cross(Reference,Forward));
        var Up=Vector3.Normalize(Vector3.Cross(Forward,Right));
        Lights.AddAreaLight(Position,Forward,Right,Up,LinearRgb,LuminanceNits,WidthMeters,HeightMeters,RangeMeters,
            ShadowPriority,CastsShadows,ShadowBiasMeters,PhysicalLightKind.Area);
    }
    /// <summary>型付きの単一の方向光。</summary>
    public static void SetLight(this StageLightSubmissions Lights,in DirectionalLightSubmission Value){
        Value.Validate();
        var Shadow=Value.Shadow;
        Lights.SetPhysicalDirectionalLight(Value.Forward,new Vector3(Value.Color.R,Value.Color.G,Value.Color.B),Value.Illuminance.Lux,Shadow.HasValue,Shadow?.Priority??0,Shadow?.Bias.Meters??0f);
    }
    /// <summary>型付きの方向光の追加。</summary>
    public static void Submit(this StageLightSubmissions Lights,in DirectionalLightSubmission Value){
        Value.Validate();
        var Color=new Vector3(Value.Color.R,Value.Color.G,Value.Color.B);
        var Shadow=Value.Shadow;
        Lights.AddPhysicalDirectionalLight(Value.Forward,Color,Value.Illuminance.Lux,Shadow.HasValue,Shadow?.Priority??0,Shadow?.Bias.Meters??0f);
    }
    /// <summary>型付きの点光源の追加。</summary>
    public static void Submit(this StageLightSubmissions Lights,in PointLightSubmission Value){
        Value.Validate();
        var Color=new Vector3(Value.Color.R,Value.Color.G,Value.Color.B);
        var Shadow=Value.Shadow;
        Lights.AddPhysicalPointLight(Value.Position,Color,Value.Flux.Lumens,Value.Range.Meters,Shadow.HasValue,Shadow?.Priority??0,Shadow?.Bias.Meters??0f);
    }
    /// <summary>型付きのスポットライトの追加。</summary>
    public static void Submit(this StageLightSubmissions Lights,in SpotLightSubmission Value){
        Value.Validate();
        var Color=new Vector3(Value.Color.R,Value.Color.G,Value.Color.B);
        var Shadow=Value.Shadow;
        Lights.AddPhysicalSpotLight(Value.Position,Value.Forward,Color,Value.Flux.Lumens,Value.Range.Meters,Value.Cone.InnerFullAngleDegrees,Value.Cone.OuterFullAngleDegrees,Shadow.HasValue,Shadow?.Priority??0,Shadow?.Bias.Meters??0f);
    }
    /// <summary>型付きの矩形の光源の追加。右と上は提出の上方向から決める。</summary>
    public static void Submit(this StageLightSubmissions Lights,in RectAreaLightSubmission Value){
        Value.Validate();
        var Color=new Vector3(Value.Color.R,Value.Color.G,Value.Color.B);
        var Shadow=Value.Shadow;
        var Forward=Vector3.Normalize(Value.Forward);
        var Right=Vector3.Normalize(Vector3.Cross(Vector3.Normalize(Value.Up),Forward));
        var Up=Vector3.Normalize(Vector3.Cross(Forward,Right));
        Lights.AddAreaLight(Value.Position,Forward,Right,Up,Color,Value.Luminance.Nits,Value.Size.WidthMeters,Value.Size.HeightMeters,Value.Range.Meters,Shadow?.Priority??0,Shadow.HasValue,Shadow?.Bias.Meters??0f,PhysicalLightKind.Area);
    }
    /// <summary>発光面のパッチを、片面の矩形の光源として提出する。</summary>
    public static void SubmitDirectLighting(this StageLightSubmissions Lights,IReadOnlyList<EmissivePatch>Patches,float RangeMeters,bool CastsShadows,int ShadowPriority,float ShadowBiasMeters){
        ArgumentNullException.ThrowIfNull(Lights);
        ArgumentNullException.ThrowIfNull(Patches);
        foreach(EmissivePatch Patch in Patches){
            Lights.SubmitEmissivePatch(Patch,RangeMeters,CastsShadows,ShadowPriority,ShadowBiasMeters);
        }
    }
    /// <summary>発光面のパッチ 1 つを、片面の矩形の光源として提出する。</summary>
    public static void SubmitEmissivePatch(this StageLightSubmissions Lights,in EmissivePatch Patch,float RangeMeters,bool CastsShadows,int ShadowPriority,float ShadowBiasMeters){
        Lights.AddAreaLight(Patch.Center,Patch.Forward,Patch.Right,Patch.Up,Patch.LinearRgb,Patch.LuminanceNits,
            Patch.WidthMeters,Patch.HeightMeters,RangeMeters,ShadowPriority,CastsShadows,ShadowBiasMeters,PhysicalLightKind.Emissive);
    }
    private static Vector3 NormalizeForward(Vector3 LightForward){
        if(!float.IsFinite(LightForward.X)||!float.IsFinite(LightForward.Y)||!float.IsFinite(LightForward.Z)||LightForward.LengthSquared()<=MinimumDirectionLengthSquared){
            throw new ArgumentOutOfRangeException(nameof(LightForward),"Light forward must be a finite non-zero vector.");
        }
        return Vector3.Normalize(LightForward);
    }
    private static void ValidateShadowSettings(float ShadowBiasMeters){
        if(!float.IsFinite(ShadowBiasMeters)||ShadowBiasMeters<0f){
            throw new ArgumentOutOfRangeException(nameof(ShadowBiasMeters),"Shadow bias must be finite and non-negative meters.");
        }
    }
}
