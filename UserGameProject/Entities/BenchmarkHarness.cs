using System.Diagnostics;

namespace UserGameProject.Entities;

/// <summary>
/// 1シナリオ分の実測結果。時間はすべて実際に計測した値であり、推定値は含まない。
/// </summary>
public readonly record struct BenchmarkResult(string Label, int Iterations, int ItemsPerIteration, double MinMs, double MedianMs)
{
    /// <summary>1件あたりのナノ秒(中央値ベース)。レイアウト間の比較にはこの値を使う。</summary>
    public double MedianNanosecondsPerItem =>
        this.ItemsPerIteration == 0 ? 0d : this.MedianMs * 1_000_000d / this.ItemsPerIteration;
}

/// <summary>
/// ウォームアップ + 複数試行 + 中央値/最小値レポートだけを行う最小の計測ハーネス。
/// BenchmarkDotNetのような外れ値検出・統計処理は行わないため、得られる値は
/// 「同一プロセス・同一条件で実測した相対比較」として読むこと。
/// </summary>
public static class BenchmarkHarness
{
    public static BenchmarkResult Measure(string label, int warmupIterations, int iterations, int itemsPerIteration, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentOutOfRangeException.ThrowIfNegative(warmupIterations);
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);

        for (int i = 0; i < warmupIterations; i++)
        {
            action();
        }

        double[] samples = new double[iterations];

        for (int i = 0; i < iterations; i++)
        {
            long start = Stopwatch.GetTimestamp();
            action();
            long end = Stopwatch.GetTimestamp();
            samples[i] = (end - start) * 1000d / Stopwatch.Frequency;
        }

        Array.Sort(samples);

        double median = samples.Length % 2 == 1
            ? samples[samples.Length / 2]
            : (samples[(samples.Length / 2) - 1] + samples[samples.Length / 2]) / 2d;

        return new BenchmarkResult(label, iterations, itemsPerIteration, samples[0], median);
    }

    /// <summary>
    /// AoS/SoAの結果を1行ずつ、末尾に相対比を付けて出力する。
    /// </summary>
    public static void PrintComparison(string scenario, BenchmarkResult aos, BenchmarkResult soa)
    {
        Console.WriteLine();
        Console.WriteLine($"--- {scenario} ---");
        PrintRow(aos);
        PrintRow(soa);

        if (aos.MedianMs > 0d && soa.MedianMs > 0d)
        {
            double ratio = aos.MedianMs / soa.MedianMs;
            string faster = ratio > 1d ? "SoA" : "AoS";
            double factor = ratio > 1d ? ratio : 1d / ratio;
            Console.WriteLine($"    => {faster} is {factor:F2}x faster (median)");
        }
    }

    private static void PrintRow(BenchmarkResult result)
    {
        Console.WriteLine(
            $"  {result.Label,-28} median {result.MedianMs,9:F3} ms   min {result.MinMs,9:F3} ms   {result.MedianNanosecondsPerItem,8:F2} ns/entity");
    }
}
