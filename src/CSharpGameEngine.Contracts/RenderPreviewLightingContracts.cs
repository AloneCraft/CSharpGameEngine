namespace CSharpGameEngine.Contracts;
/// <summary>
/// プレビューホストが毎フレーム受け取る照明と環境（プロトコル 10、M0127）。単位は <c>CSharpGameEngine.Lighting</c> の型付き値と同じ
/// （照度は lux、光束は lm、距離は m、角度は度、色はリニア RGB）で、ホストは <c>RenderServer</c> の型付きライト API に変換する。
/// ライトは毎フレームの加算式なので、クライアントは毎フレーム同じ内容を送る。背景・アンビエント・環境のように永続する項目は
/// <see cref="EnvironmentRevision"/> が変わったときだけホストが適用する（空の再適用で大気の再構築などを起こさないため）。
/// ホストは Render Server のクライアントであり、ここにはレンダラー独自の概念を持ち込まない。
/// </summary>
public sealed class RenderPreviewLighting {
    /// <summary>ホストが背景・アンビエント・環境を適用した版。変えると次の UpdateFrame で再適用する。</summary>
    public int EnvironmentRevision { get;set; }
    /// <summary>平坦なアンビエント（リニア RGB 3 値）。</summary>
    public float[]Ambient { get;set; }=[0.4f,0.4f,0.4f];
    /// <summary>背景色（表示される RGB 0..1 の 3 値）。null ならレンダラーの既定のクリア色。</summary>
    public float[]?Background { get;set; }
    /// <summary>環境（大気・露出・AO）。null ならレンダラーの従来経路。</summary>
    public RenderPreviewEnvironment?Environment { get;set; }
    public RenderPreviewDirectionalLight[]Directionals { get;set; }=[];
    public RenderPreviewPointLight[]Points { get;set; }=[];
    public RenderPreviewSpotLight[]Spots { get;set; }=[];
}
public sealed class RenderPreviewEnvironment {
    /// <summary>物理ベースの大気（地球プリセット）とそのスカイライト。</summary>
    public bool Atmosphere { get;set; }
    /// <summary>スカイライトの強さ（大気が有効なときだけ使う）。</summary>
    public float SkyLightIntensity { get;set; }=1f;
    /// <summary>true なら物理カメラの固定 <see cref="ExposureEv100"/>、false なら従来の露出。</summary>
    public bool PhysicalExposure { get;set; }
    public float ExposureEv100 { get;set; }
    /// <summary>露出補正（段）。物理カメラの露出に 2^補正 を掛ける。</summary>
    public float ExposureCompensation { get;set; }
    /// <summary>true なら Unreal のフィルム調トーンマッパー（sRGB 変換込み）、false なら従来の ACES 近似。</summary>
    public bool FilmicToneMapper { get;set; }
    public float BloomIntensity { get;set; }=1.8f;
    public bool AmbientOcclusion { get;set; }
}
public sealed class RenderPreviewDirectionalLight {
    /// <summary>光が進む向き（ワールド、3 値）。</summary>
    public float[]Forward { get;set; }=[0f,-1f,0f];
    public float[]Color { get;set; }=[1f,1f,1f];
    public float IlluminanceLux { get;set; }=2f;
    public bool CastsShadows { get;set; }
    public int ShadowPriority { get;set; }
    public float ShadowBiasMeters { get;set; }=0.0015f;
}
public sealed class RenderPreviewPointLight {
    public float[]Position { get;set; }=[0f,0f,0f];
    public float[]Color { get;set; }=[1f,1f,1f];
    public float FluxLumens { get;set; }=800f;
    public float RangeMeters { get;set; }=10f;
    public bool CastsShadows { get;set; }
    public int ShadowPriority { get;set; }
    public float ShadowBiasMeters { get;set; }=0.005f;
}
public sealed class RenderPreviewSpotLight {
    public float[]Position { get;set; }=[0f,0f,0f];
    public float[]Forward { get;set; }=[0f,-1f,0f];
    public float[]Color { get;set; }=[1f,1f,1f];
    public float FluxLumens { get;set; }=800f;
    public float RangeMeters { get;set; }=10f;
    public float InnerConeDegrees { get;set; }=30f;
    public float OuterConeDegrees { get;set; }=45f;
    public bool CastsShadows { get;set; }
    public int ShadowPriority { get;set; }
    public float ShadowBiasMeters { get;set; }=0.005f;
}
/// <summary>マテリアルのワールドグリッド（ADR-0075）。床にメートル基準の格子を描く。</summary>
public sealed class RenderPreviewWorldGrid {
    public float GridSizeMeters { get;set; }=1f;
    public int SubGridDivisions { get;set; }=10;
    public float WireHalfWidth { get;set; }=0.01f;
    public float[]GridColor { get;set; }=[0.2f,0.2f,0.2f];
    public float[]SubGridColor { get;set; }=[0.35f,0.35f,0.35f];
}
/// <summary><see cref="RenderPreviewLighting"/> の検証。プロトコル検証から呼ぶ。</summary>
public static class RenderPreviewLightingValidation {
    public const int MaximumDirectionalLights=4;
    public const int MaximumPointLights=8;
    public const int MaximumSpotLights=8;
    public const float MaximumIntensity=1.0e7f;
    public const float MaximumExposureEv100=40f;
    public const int MinimumGridDivisions=1;
    public const int MaximumGridDivisions=64;
    public static string?Validate(RenderPreviewLighting?Lighting){
        if(Lighting is null){ return null; }
        if(!Vector(Lighting.Ambient,3,0f,MaximumIntensity)){ return "Lighting ambient needs three finite non-negative values."; }
        if(Lighting.Background is not null&&!Vector(Lighting.Background,3,0f,1f)){ return "Lighting background needs three values between 0 and 1."; }
        if(Lighting.Environment is{ }Environment){
            if(!Finite(Environment.SkyLightIntensity)||Environment.SkyLightIntensity<0f||Environment.SkyLightIntensity>MaximumIntensity){ return "Environment sky light intensity is out of range."; }
            if(!Finite(Environment.ExposureEv100)||Math.Abs(Environment.ExposureEv100)>MaximumExposureEv100
                ||!Finite(Environment.ExposureCompensation)||Math.Abs(Environment.ExposureCompensation)>MaximumExposureEv100){ return "Environment exposure EV100 is out of range."; }
            if(!Finite(Environment.BloomIntensity)||Environment.BloomIntensity<0f||Environment.BloomIntensity>16f){ return "Environment bloom intensity is out of range."; }
        }
        if(Lighting.Directionals is null||Lighting.Directionals.Length>MaximumDirectionalLights){ return $"Directional light count must be between 0 and {MaximumDirectionalLights}."; }
        if(Lighting.Points is null||Lighting.Points.Length>MaximumPointLights){ return $"Point light count must be between 0 and {MaximumPointLights}."; }
        if(Lighting.Spots is null||Lighting.Spots.Length>MaximumSpotLights){ return $"Spot light count must be between 0 and {MaximumSpotLights}."; }
        for(var Index=0;Index<Lighting.Directionals.Length;Index++){
            var Light=Lighting.Directionals[Index];
            if(Light is null||!Direction(Light.Forward)||!Vector(Light.Color,3,0f,MaximumIntensity)||!Finite(Light.IlluminanceLux)||Light.IlluminanceLux<0f||Light.IlluminanceLux>MaximumIntensity
                ||!Finite(Light.ShadowBiasMeters)||Light.ShadowBiasMeters<0f){
                return $"Directional light {Index} is invalid.";
            }
        }
        for(var Index=0;Index<Lighting.Points.Length;Index++){
            var Light=Lighting.Points[Index];
            if(Light is null||!Vector(Light.Position,3,-MaximumIntensity,MaximumIntensity)||!Vector(Light.Color,3,0f,MaximumIntensity)||!Finite(Light.FluxLumens)||Light.FluxLumens<0f||Light.FluxLumens>MaximumIntensity
                ||!Finite(Light.RangeMeters)||Light.RangeMeters<=0f||!Finite(Light.ShadowBiasMeters)||Light.ShadowBiasMeters<0f){
                return $"Point light {Index} is invalid.";
            }
        }
        for(var Index=0;Index<Lighting.Spots.Length;Index++){
            var Light=Lighting.Spots[Index];
            if(Light is null||!Vector(Light.Position,3,-MaximumIntensity,MaximumIntensity)||!Direction(Light.Forward)||!Vector(Light.Color,3,0f,MaximumIntensity)||!Finite(Light.FluxLumens)||Light.FluxLumens<0f||Light.FluxLumens>MaximumIntensity
                ||!Finite(Light.RangeMeters)||Light.RangeMeters<=0f||!Finite(Light.InnerConeDegrees)||!Finite(Light.OuterConeDegrees)||Light.OuterConeDegrees<=0f||Light.OuterConeDegrees>180f
                ||Light.InnerConeDegrees<=0f||Light.InnerConeDegrees>Light.OuterConeDegrees||!Finite(Light.ShadowBiasMeters)||Light.ShadowBiasMeters<0f){
                return $"Spot light {Index} is invalid.";
            }
        }
        return null;
    }
    public static string?Validate(RenderPreviewWorldGrid?Grid,int MaterialIndex){
        if(Grid is null){ return null; }
        if(!Finite(Grid.GridSizeMeters)||Grid.GridSizeMeters<=0f||Grid.SubGridDivisions<MinimumGridDivisions||Grid.SubGridDivisions>MaximumGridDivisions
            ||!Finite(Grid.WireHalfWidth)||Grid.WireHalfWidth<=0f||Grid.WireHalfWidth>=0.5f
            ||!Vector(Grid.GridColor,3,0f,MaximumIntensity)||!Vector(Grid.SubGridColor,3,0f,MaximumIntensity)){
            return $"Material {MaterialIndex} world grid is invalid.";
        }
        return null;
    }
    private static bool Finite(float Value)=>!float.IsNaN(Value)&&!float.IsInfinity(Value);
    private static bool Vector(float[]?Values,int Length,float Minimum,float Maximum){
        if(Values is null||Values.Length!=Length){ return false; }
        for(var Index=0;Index<Values.Length;Index++){
            if(!Finite(Values[Index])||Values[Index]<Minimum||Values[Index]>Maximum){ return false; }
        }
        return true;
    }
    private static bool Direction(float[]?Values){
        if(!Vector(Values,3,-MaximumIntensity,MaximumIntensity)){ return false; }
        return Values![0]*Values[0]+Values[1]*Values[1]+Values[2]*Values[2]>1e-12f;
    }
}
