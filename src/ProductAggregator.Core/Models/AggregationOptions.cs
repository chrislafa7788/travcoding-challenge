namespace ProductAggregator.Core.Models;

public class AggregationOptions
{
    public const string SectionName = "Aggregation";

    public int MaxConcurrentProducts { get; set; } = 5;

    public int CacheTtlSeconds { get; set; } = 30;
}
