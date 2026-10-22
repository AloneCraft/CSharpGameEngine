using CSharpGameEngine.Authoring;
namespace Weather;
/// <summary>A cloud that rains. The sample plugin's component (ADR-0079).</summary>
public struct Rain {
    /// <summary>Millimeters per hour.</summary>
    public float Rate;
}
/// <summary>How hard it rains. The Lightning sample plugin has a component of the same name; the generator names both apart.</summary>
public struct Intensity {
    public float Value;
}
/// <summary>The sample plugin's data type: its values are assets (Assets/Storm.cgeasset).</summary>
[DataAsset]
public struct StormSetup {
    public float RateMultiplier;
    public int Gusts;
}
/// <summary>
/// The sample plugin's service: every Weather system gets the same one, and the game disposes it at shutdown, when it prints
/// what it saw (the package check reads the line).
/// </summary>
[EngineService]
public sealed class WeatherLog:IDisposable {
    public int Updates;
    public float Rainfall;
    public void Dispose()=>Console.WriteLine(System.FormattableString.Invariant($"CGE-PLUGIN-SAMPLE Weather updates={this.Updates} rainfall={this.Rainfall:F1} gusts={Assets.Plugins.Weather.Storm.Gusts}"));
}
