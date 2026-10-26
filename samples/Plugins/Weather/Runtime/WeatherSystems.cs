using CSharpGameEngine.Entities;
namespace Weather;
/// <summary>各雲の降雨量にStormデータアセットの倍率を適用し、共有ログへ加算する。</summary>
internal struct RainFall:ISystem {
    public void Update(in Rain Rain,in Intensity Intensity,WeatherLog Log){
        Log.Updates++;
        Log.Rainfall+=Rain.Rate*Intensity.Value*Assets.Plugins.Weather.Storm.RateMultiplier;
    }
}
