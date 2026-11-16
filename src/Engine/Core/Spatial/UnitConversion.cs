namespace CSharpGameEngine.Spatial.Abstractions;

/// <summary>
/// 単位変換は軸変換とは別の段階として扱う（Docs/Specifications/Spatial-RUF.mdの「Units」：
/// 「外部軸変換 → 単位変換 → 意味上のRUF正規化」を参照）。このエンジンのランタイム単位は、距離がメートル、
/// 角度APIがラジアン、時間が秒、スケールが無次元。以下のヘルパーはアセット作成／インポート境界にのみ存在し、
/// ソースツールのセンチメートルや度数をRUF空間のランタイムデータに変換するために使う。
/// </summary>
public static class UnitConversion {
    private const float MetersPerCentimeter=0.01f;

    public static float MetersFromCentimeters(float Centimeters)=>Centimeters*MetersPerCentimeter;

    public static float CentimetersFromMeters(float Meters)=>Meters/MetersPerCentimeter;

    public static float RadiansFromDegrees(float Degrees)=>Degrees*(MathF.PI/180f);

    public static float DegreesFromRadians(float Radians)=>Radians*(180f/MathF.PI);
}
