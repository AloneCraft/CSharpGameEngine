using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace CSharpGameEngine.VSExtension;
internal enum PreviewLightKind { Directional, Point, Spot }
/// <summary><see cref="RendererDefault"/> はレンダラー既定のクリア色（現在のアセットプレビューの青）。空の大気を使うときは <see cref="PreviewEnvironment.Atmosphere"/> が背景を置き換える。</summary>
internal enum PreviewBackgroundMode { RendererDefault, Solid }
internal enum PreviewProjection { Perspective, Orthographic }
internal sealed class PreviewBackground {
    public PreviewBackgroundMode Mode { get;set;}=PreviewBackgroundMode.RendererDefault;
    /// <summary>表示される色（RGB 0..1）。<see cref="Mode"/> が <see cref="PreviewBackgroundMode.Solid"/> のときだけ使う。</summary>
    public float[]Color { get;set;}=[0.2f,0.2f,0.2f];
}
internal sealed class PreviewAmbient {
    /// <summary>リニア RGB。</summary>
    public float[]Color { get;set;}=[1f,1f,1f];
    public float Intensity { get;set;}=0.4f;
}
/// <summary>
/// プレビューのライト。単位は型付きライト（ADR-0064）と同じで、方向ライトは lux、点・スポットは lm。
/// 方向は <see cref="YawDegrees"/>／<see cref="PitchDegrees"/> で「光が進む向き」を表す（ピッチが負で下向き）。
/// 点・スポットの位置と範囲はプレビュー対象の境界半径に対する倍率で、光束は境界半径の 2 乗に比例させる（大きさの違う資産でも同じ見え方にする）。
/// </summary>
internal sealed class PreviewLight {
    public PreviewLightKind Kind { get;set;}=PreviewLightKind.Directional;
    public string Name { get;set;}=string.Empty;
    public float[]Color { get;set;}=[1f,1f,1f];
    public float Intensity { get;set;}=2f;
    public float YawDegrees { get;set;}
    public float PitchDegrees { get;set;}=-45f;
    public float[]PositionRadii { get;set;}=[0f,2f,0f];
    public float RangeRadii { get;set;}=8f;
    public float InnerConeDegrees { get;set;}=30f;
    public float OuterConeDegrees { get;set;}=45f;
    public bool CastsShadows { get;set;}
}
internal sealed class PreviewEnvironment {
    /// <summary>物理ベースの空（大気）とスカイライト。背景はこの空になる。</summary>
    public bool Atmosphere { get;set;}
    public float SkyLightIntensity { get;set;}=1f;
    public bool PhysicalExposure { get;set;}
    public float ExposureEv100 { get;set;}
    public float ExposureCompensation { get;set;}
    public bool FilmicToneMapper { get;set;}
    public float BloomIntensity { get;set;}=1.8f;
    public bool AmbientOcclusion { get;set;}
    /// <summary>レンダラーの従来経路と同じ（ホストへ環境を送らない）。</summary>
    [JsonIgnore]
    public bool IsLegacy=>!this.Atmosphere&&!this.PhysicalExposure&&!this.FilmicToneMapper&&!this.AmbientOcclusion&&Math.Abs(this.BloomIntensity-1.8f)<1e-6f;
}
internal sealed class PreviewFloor {
    public bool Visible { get;set;}
    /// <summary>半辺の長さ（境界半径の倍数）。</summary>
    public float SizeRadii { get;set;}=20f;
    public float[]Color { get;set;}=[0.5f,0.5f,0.5f];
    public float Roughness { get;set;}=1f;
    public bool Grid { get;set;}
    /// <summary>グリッドの間隔（m）。0 なら境界半径に合わせて 10 のべき乗を自動で選ぶ。</summary>
    public float GridSizeMeters { get;set;}
    public float[]GridColor { get;set;}=[0.25f,0.25f,0.25f];
    public float[]SubGridColor { get;set;}=[0.38f,0.38f,0.38f];
}
internal sealed class PreviewCameraDefaults {
    /// <summary>対象から視点へ向かう方位角（度）。</summary>
    public float YawDegrees { get;set;}=45f;
    /// <summary>対象から視点へ向かう仰角（度）。</summary>
    public float ElevationDegrees { get;set;}=27.9f;
    public float FovDegrees { get;set;}=45f;
    public PreviewProjection Projection { get;set;}=PreviewProjection.Perspective;
}
/// <summary>
/// アセットプレビューの環境（ライト・背景・環境光・床・初期カメラ）を表すテンプレート（M0127）。保存形式は JSON（拡張子 <c>.cgepreview</c>）で、
/// ライトは型付きライトの単位をそのまま使う。プレビュー対象のアセットはテンプレートに書き込まない（実行時に環境へ置く）。
/// 組み込みの標準テンプレートはコードが提供し、ディスクには書かない（<see cref="BuiltIn"/>）。
/// </summary>
internal sealed class PreviewLevelTemplate {
    internal const int CurrentSchemaVersion=1;
    internal const int MaximumDirectionalLights=4;
    internal const int MaximumPointLights=8;
    internal const int MaximumSpotLights=8;
    internal const int MaximumLights=MaximumDirectionalLights+MaximumPointLights+MaximumSpotLights;
    private static readonly JsonSerializerOptions Options=CreateOptions();
    public int SchemaVersion { get;set;}=CurrentSchemaVersion;
    public string DisplayName { get;set;}=string.Empty;
    public string Description { get;set;}=string.Empty;
    public PreviewBackground Background { get;set;}=new();
    public PreviewAmbient Ambient { get;set;}=new();
    public List<PreviewLight>Lights { get;set;}=[];
    public PreviewEnvironment Environment { get;set;}=new();
    public PreviewFloor Floor { get;set;}=new();
    public PreviewCameraDefaults Camera { get;set;}=new();
    /// <summary>ファイル名（拡張子なし）で、名前変更で変わらない識別子。ファイルには書かない。</summary>
    [JsonIgnore]
    public string Id { get;set;}=string.Empty;
    /// <summary>コードが提供する標準テンプレート。ファイルには書かず、削除・上書き・名前変更はできない。</summary>
    [JsonIgnore]
    public bool BuiltIn { get;set;}
    private static JsonSerializerOptions CreateOptions(){
        var Result=new JsonSerializerOptions { WriteIndented=true,PropertyNameCaseInsensitive=true,ReadCommentHandling=JsonCommentHandling.Skip,AllowTrailingCommas=true };
        Result.Converters.Add(new JsonStringEnumConverter());
        return Result;
    }
    internal string ToJson()=>JsonSerializer.Serialize(this,Options);
    /// <summary>JSON から読む。壊れている・版が新しすぎる・値が範囲外の場合は <paramref name="Error"/> に理由を入れて null を返す。</summary>
    internal static PreviewLevelTemplate?TryParse(string Json,out string?Error){
        Error=null;
        PreviewLevelTemplate?Result;
        try {
            Result=JsonSerializer.Deserialize<PreviewLevelTemplate>(Json,Options);
        } catch(Exception Failure) when(Failure is JsonException or NotSupportedException or ArgumentException or InvalidOperationException){
            Error=Failure.Message;
            return null;
        }
        if(Result is null){ Error="The preview level is empty."; return null; }
        Error=Result.Validate();
        return Error is null?Result:null;
    }
    internal PreviewLevelTemplate Clone(){
        var Copy=JsonSerializer.Deserialize<PreviewLevelTemplate>(this.ToJson(),Options)!;
        Copy.Id=this.Id;
        Copy.BuiltIn=this.BuiltIn;
        return Copy;
    }
    /// <summary>範囲外・形式不正の最初の理由。問題がなければ null。</summary>
    internal string?Validate(){
        if(this.SchemaVersion<1||this.SchemaVersion>CurrentSchemaVersion){ return $"Unsupported preview level schema version {this.SchemaVersion}; expected {CurrentSchemaVersion}."; }
        if(string.IsNullOrWhiteSpace(this.DisplayName)){ return "DisplayName is required."; }
        if(this.Background is null||this.Ambient is null||this.Lights is null||this.Environment is null||this.Floor is null||this.Camera is null){ return "A preview level section is missing."; }
        if(!Enum.IsDefined(typeof(PreviewBackgroundMode),this.Background.Mode)){ return "Background mode is invalid."; }
        if(!Color(this.Background.Color,0f,1f)){ return "Background color needs three values between 0 and 1."; }
        if(!Color(this.Ambient.Color,0f,16f)||!Finite(this.Ambient.Intensity)||this.Ambient.Intensity<0f||this.Ambient.Intensity>16f){ return "Ambient needs a color and an intensity between 0 and 16."; }
        if(this.Lights.Count>MaximumLights){ return $"At most {MaximumLights} lights are supported."; }
        if(this.Lights.Count(static Light=>Light?.Kind==PreviewLightKind.Directional)>MaximumDirectionalLights){ return $"At most {MaximumDirectionalLights} directional lights are supported."; }
        if(this.Lights.Count(static Light=>Light?.Kind==PreviewLightKind.Point)>MaximumPointLights){ return $"At most {MaximumPointLights} point lights are supported."; }
        if(this.Lights.Count(static Light=>Light?.Kind==PreviewLightKind.Spot)>MaximumSpotLights){ return $"At most {MaximumSpotLights} spot lights are supported."; }
        for(var Index=0;Index<this.Lights.Count;Index++){
            var Light=this.Lights[Index];
            if(Light is null||!Enum.IsDefined(typeof(PreviewLightKind),Light.Kind)){ return $"Light {Index} is invalid."; }
            if(!Color(Light.Color,0f,16f)||!Finite(Light.Intensity)||Light.Intensity<0f||Light.Intensity>1.0e6f){ return $"Light {Index} color or intensity is out of range."; }
            if(!Finite(Light.YawDegrees)||!Finite(Light.PitchDegrees)||Math.Abs(Light.PitchDegrees)>90f){ return $"Light {Index} direction is out of range."; }
            if(Light.Kind!=PreviewLightKind.Directional){
                if(Light.PositionRadii is not{ Length:3 }||!Light.PositionRadii.All(Finite)||Light.PositionRadii.Any(static Value=>Math.Abs(Value)>1000f)){ return $"Light {Index} position is out of range."; }
                if(!Finite(Light.RangeRadii)||Light.RangeRadii<=0f||Light.RangeRadii>1000f){ return $"Light {Index} range is out of range."; }
            }
            if(Light.Kind==PreviewLightKind.Spot&&(!Finite(Light.InnerConeDegrees)||!Finite(Light.OuterConeDegrees)||Light.InnerConeDegrees<=0f||Light.InnerConeDegrees>Light.OuterConeDegrees||Light.OuterConeDegrees>180f)){
                return $"Light {Index} cone angles are out of range.";
            }
        }
        var Env=this.Environment;
        if(!Finite(Env.SkyLightIntensity)||Env.SkyLightIntensity<0f||Env.SkyLightIntensity>1000f||!Finite(Env.ExposureEv100)||Math.Abs(Env.ExposureEv100)>40f
            ||!Finite(Env.ExposureCompensation)||Math.Abs(Env.ExposureCompensation)>40f||!Finite(Env.BloomIntensity)||Env.BloomIntensity<0f||Env.BloomIntensity>16f){
            return "Environment values are out of range.";
        }
        var Floor=this.Floor;
        if(!Finite(Floor.SizeRadii)||Floor.SizeRadii<=0f||Floor.SizeRadii>1000f||!Color(Floor.Color,0f,1f)||!Finite(Floor.Roughness)||Floor.Roughness<0f||Floor.Roughness>1f
            ||!Finite(Floor.GridSizeMeters)||Floor.GridSizeMeters<0f||!Color(Floor.GridColor,0f,16f)||!Color(Floor.SubGridColor,0f,16f)){
            return "Floor values are out of range.";
        }
        var Camera=this.Camera;
        if(!Finite(Camera.YawDegrees)||!Finite(Camera.ElevationDegrees)||Camera.ElevationDegrees<-80f||Camera.ElevationDegrees>80f||!Finite(Camera.FovDegrees)||Camera.FovDegrees<5f||Camera.FovDegrees>120f
            ||!Enum.IsDefined(typeof(PreviewProjection),Camera.Projection)){
            return "Camera defaults are out of range.";
        }
        return null;
    }
    private static bool Finite(float Value)=>!float.IsNaN(Value)&&!float.IsInfinity(Value);
    private static bool Color(float[]?Values,float Minimum,float Maximum)=>Values is{ Length:3 }&&Values.All(Value=>Finite(Value)&&Value>=Minimum&&Value<=Maximum);
}
