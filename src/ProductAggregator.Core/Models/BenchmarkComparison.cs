namespace ProductAggregator.Core.Models;

public class BenchmarkMetrics
{
    public long ProcessingTimeMs { get; set; }
    public double AverageTimePerProduct { get; set; }
}

public class BenchmarkComparisonResult
{
    public int ProductCount { get; set; }
    public BenchmarkMetrics Current { get; set; } = new();
    public BenchmarkMetrics EstimatedBaseline { get; set; } = new();
    public double ImprovementPercent { get; set; }
}

/// <summary>
/// Documented sequential baseline measurements from the original implementation.
/// </summary>
public static class PerformanceBaseline
{
    private static readonly Dictionary<int, BenchmarkMetrics> Measured = new()
    {
        [1] = new BenchmarkMetrics { ProcessingTimeMs = 1711, AverageTimePerProduct = 1711 },
        [5] = new BenchmarkMetrics { ProcessingTimeMs = 9736, AverageTimePerProduct = 1947.2 },
        [10] = new BenchmarkMetrics { ProcessingTimeMs = 20263, AverageTimePerProduct = 2026.3 }
    };

    private const double DefaultAverageTimePerProduct = 2026.3;

    public static BenchmarkMetrics GetEstimatedBaseline(int productCount)
    {
        if (Measured.TryGetValue(productCount, out var metrics))
        {
            return new BenchmarkMetrics
            {
                ProcessingTimeMs = metrics.ProcessingTimeMs,
                AverageTimePerProduct = metrics.AverageTimePerProduct
            };
        }

        return new BenchmarkMetrics
        {
            ProcessingTimeMs = (long)(DefaultAverageTimePerProduct * productCount),
            AverageTimePerProduct = DefaultAverageTimePerProduct
        };
    }
}

public static class BenchmarkComparisonCalculator
{
    public static BenchmarkComparisonResult Create(int productCount, long currentProcessingTimeMs)
    {
        var baseline = PerformanceBaseline.GetEstimatedBaseline(productCount);
        var current = new BenchmarkMetrics
        {
            ProcessingTimeMs = currentProcessingTimeMs,
            AverageTimePerProduct = currentProcessingTimeMs / (double)productCount
        };

        var improvementPercent = baseline.ProcessingTimeMs <= 0
            ? 0
            : (baseline.ProcessingTimeMs - current.ProcessingTimeMs) * 100.0 / baseline.ProcessingTimeMs;

        return new BenchmarkComparisonResult
        {
            ProductCount = productCount,
            Current = current,
            EstimatedBaseline = baseline,
            ImprovementPercent = Math.Round(improvementPercent, 1)
        };
    }
}
