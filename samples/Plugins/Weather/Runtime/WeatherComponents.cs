using CSharpGameEngine.Authoring;
namespace Weather;
/// <summary>雨を降らせる雲。サンプルプラグインのコンポーネント（ADR-0079）。</summary>
public struct Rain {
    /// <summary>1時間あたりのミリメートル。</summary>
    public float Rate;
}
/// <summary>降雨の強さ。同名のコンポーネントがLightningサンプルプラグインにもあるため、ジェネレーターが両者を区別して命名する。</summary>
public struct Intensity {
    public float Value;
}
/// <summary>サンプルプラグインのデータ型。値はアセット（Assets/Storm.cgeasset）。</summary>
[DataAsset]
public struct StormSetup {
    public float RateMultiplier;
    public int Gusts;
}
/// <summary>
/// サンプルプラグインのサービス。すべてのWeatherシステムが同じインスタンスを受け取り、終了時にゲームが破棄する。その際、観測内容を出力する
///（パッケージ検査ではその行を読み取る）。
/// </summary>
[EngineService]
public sealed class WeatherLog:IDisposable {
    public int Updates;
    public float Rainfall;
    public void Dispose()=>Console.WriteLine(System.FormattableString.Invariant($"CGE-PLUGIN-SAMPLE Weather updates={this.Updates} rainfall={this.Rainfall:F1} gusts={Assets.Plugins.Weather.Storm.Gusts}"));
}
