using CSharpGameEngine.Entities;
namespace Weather;
/// <summary>Adds each cloud's rain, scaled by the Storm data asset, to the shared log.</summary>
internal struct RainFall:ISystem {
    public void Update(in Rain Rain,in Intensity Intensity,WeatherLog Log){
        Log.Updates++;
        Log.Rainfall+=Rain.Rate*Intensity.Value*Assets.Plugins.Weather.Storm.RateMultiplier;
    }
}
