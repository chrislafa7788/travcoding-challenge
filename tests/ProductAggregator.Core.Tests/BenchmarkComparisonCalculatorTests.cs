using ProductAggregator.Core.Models;

namespace ProductAggregator.Core.Tests;

public class BenchmarkComparisonCalculatorTests
{
    [Theory]
    [InlineData(1, 1711, 1711)]
    [InlineData(5, 9736, 1947.2)]
    [InlineData(10, 20263, 2026.3)]
    public void GetEstimatedBaseline_ReturnsDocumentedMeasurements(
        int productCount,
        long expectedTotal,
        double expectedAverage)
    {
        var baseline = PerformanceBaseline.GetEstimatedBaseline(productCount);

        Assert.Equal(expectedTotal, baseline.ProcessingTimeMs);
        Assert.Equal(expectedAverage, baseline.AverageTimePerProduct);
    }

    [Fact]
    public void Create_CalculatesImprovementAgainstDocumentedBaseline()
    {
        var comparison = BenchmarkComparisonCalculator.Create(productCount: 10, currentProcessingTimeMs: 622);

        Assert.Equal(20263, comparison.EstimatedBaseline.ProcessingTimeMs);
        Assert.Equal(622, comparison.Current.ProcessingTimeMs);
        Assert.Equal(96.9, comparison.ImprovementPercent);
    }
}
