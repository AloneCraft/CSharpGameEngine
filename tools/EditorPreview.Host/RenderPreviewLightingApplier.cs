using System.Numerics;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Lighting;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Rendering.Server;
namespace CSharpGameEngine.EditorPreview.Host;
/// <summary>
/// ワイヤの <see cref="RenderPreviewLighting"/> を、共通の <see cref="RenderServer"/> の型付きライト・環境 API へ変換して適用する（プロトコル 10、M0127）。
/// ホストにレンダラー独自の概念は持たせず、変換だけを行う。背景・アンビエント・環境は <see cref="RenderPreviewLighting.EnvironmentRevision"/> が変わったとき
/// （<paramref name="ApplyEnvironment"/>）だけ適用し、ライトは加算式（1 フレームで消費される）なので毎フレーム適用する。
/// </summary>
internal static class RenderPreviewLightingApplier {
    internal static void Apply(RenderServer Server,RenderPreviewLighting Lighting,bool ApplyEnvironment){
        if(ApplyEnvironment){
            Server.SetAmbientLight(ToVector(Lighting.Ambient));
            Server.SetEnvironmentLighting(Environment(Lighting));
        }
        ApplyLights(Server,Lighting);
    }
    internal static EnvironmentLightingSettings Environment(RenderPreviewLighting Lighting){
        var Settings=EnvironmentLightingSettings.Legacy with { Background=Lighting.Background is{ }Color?new PixelColor(ToByte(Color[0]),ToByte(Color[1]),ToByte(Color[2]),255):null };
        if(Lighting.Environment is not{ }Source){ return Settings; }
        if(Source.Atmosphere){
            Settings=Settings with { Atmosphere=SkyAtmosphereSettings.Earth,SkyLight=new SkyLightSettings(Source.SkyLightIntensity,Vector3.Zero) };
        }
        if(Source.PhysicalExposure){
            Settings=Settings with { Exposure=new ExposureSettings(ExposureMode.PhysicalCamera,Source.ExposureEv100,Source.ExposureCompensation) };
        }
        Settings=Settings with {
            ToneMapper=Source.FilmicToneMapper?ToneMapper.UnrealFilmicSrgb:ToneMapper.LegacyAces,
            BloomIntensity=Source.BloomIntensity,
            AmbientOcclusion=Source.AmbientOcclusion?new AmbientOcclusionSettings(0.5f,0.5f):null,
        };
        return Settings;
    }
    private static void ApplyLights(RenderServer Server,RenderPreviewLighting Lighting){
        if(Lighting.Directionals.Length==0){
            // 持続型の単一ディレクショナルを明示的に消す（セッション作成時の既定の太陽を残さない）。
            Server.SetDirectionalLight(new DirectionalLightSubmission(-Vector3.UnitY,new LinearLightColor(1f,1f,1f),new Illuminance(0f)));
        }
        for(var Index=0;Index<Lighting.Directionals.Length;Index++){
            var Source=Lighting.Directionals[Index];
            var Submission=new DirectionalLightSubmission(Vector3.Normalize(ToVector(Source.Forward)),Color(Source.Color),new Illuminance(Source.IlluminanceLux),Shadow(Source.CastsShadows,Source.ShadowPriority,Source.ShadowBiasMeters));
            if(Index==0){ Server.SetDirectionalLight(Submission); } else { Server.AddDirectionalLight(Submission); }
        }
        foreach(var Source in Lighting.Points){
            Server.AddPointLight(new PointLightSubmission(ToVector(Source.Position),Color(Source.Color),new LuminousFlux(Source.FluxLumens),new LightRange(Source.RangeMeters),Shadow(Source.CastsShadows,Source.ShadowPriority,Source.ShadowBiasMeters)));
        }
        foreach(var Source in Lighting.Spots){
            Server.AddSpotLight(new SpotLightSubmission(ToVector(Source.Position),Vector3.Normalize(ToVector(Source.Forward)),Color(Source.Color),new LuminousFlux(Source.FluxLumens),new LightRange(Source.RangeMeters),new SpotConeAngles(Source.InnerConeDegrees,Source.OuterConeDegrees),Shadow(Source.CastsShadows,Source.ShadowPriority,Source.ShadowBiasMeters)));
        }
    }
    private static LightShadowSettings?Shadow(bool Casts,int Priority,float BiasMeters)=>Casts?new LightShadowSettings(Priority,new ShadowBias(BiasMeters)):null;
    private static LinearLightColor Color(float[]Values)=>new(Values[0],Values[1],Values[2]);
    private static Vector3 ToVector(float[]Values)=>new(Values[0],Values[1],Values[2]);
    private static byte ToByte(float Display)=>(byte)MathF.Round(Math.Min(Math.Max(Display,0f),1f)*255f);
}
