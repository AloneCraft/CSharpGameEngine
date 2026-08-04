using UserGameProject.Entities;

namespace UserGameProject.Tests;

/// <summary>
/// Sanity checks for the measurement harness itself. These do not assert any timing threshold -
/// the harness must only report what it actually measured, so the assertions cover the invariants
/// (min &lt;= median, the action really runs the requested number of times, per-item scaling).
/// </summary>
public class BenchmarkHarnessTests
{
    [Fact]
    public void Measure_RunsWarmupAndMeasuredIterations()
    {
        int calls = 0;

        BenchmarkHarness.Measure("test", warmupIterations: 3, iterations: 5, itemsPerIteration: 1, () => calls++);

        Assert.Equal(8, calls);
    }

    [Fact]
    public void Measure_ReportsMinNotGreaterThanMedian()
    {
        BenchmarkResult result = BenchmarkHarness.Measure(
            "test",
            warmupIterations: 1,
            iterations: 9,
            itemsPerIteration: 100,
            () => Thread.SpinWait(200));

        Assert.True(result.MinMs <= result.MedianMs, $"min {result.MinMs} must not exceed median {result.MedianMs}");
        Assert.True(result.MedianMs > 0d, "median must be a real positive measurement");
    }

    [Fact]
    public void Measure_CarriesLabelAndCountsThrough()
    {
        BenchmarkResult result = BenchmarkHarness.Measure(
            "AoS  MoveAll",
            warmupIterations: 0,
            iterations: 3,
            itemsPerIteration: 512,
            () => Thread.SpinWait(50));

        Assert.Equal("AoS  MoveAll", result.Label);
        Assert.Equal(3, result.Iterations);
        Assert.Equal(512, result.ItemsPerIteration);
    }

    [Fact]
    public void MedianNanosecondsPerItem_ScalesWithItemCount()
    {
        BenchmarkResult result = new("test", Iterations: 1, ItemsPerIteration: 1000, MinMs: 1d, MedianMs: 2d);

        // 2 ms spread over 1000 items = 2000 ns per item.
        Assert.Equal(2000d, result.MedianNanosecondsPerItem, precision: 6);
    }

    [Fact]
    public void MedianNanosecondsPerItem_WithZeroItems_DoesNotDivideByZero()
    {
        BenchmarkResult result = new("test", Iterations: 1, ItemsPerIteration: 0, MinMs: 1d, MedianMs: 2d);

        Assert.Equal(0d, result.MedianNanosecondsPerItem);
    }

    [Fact]
    public void Measure_WithNonPositiveIterations_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BenchmarkHarness.Measure("test", warmupIterations: 0, iterations: 0, itemsPerIteration: 1, () => { }));
    }
}
