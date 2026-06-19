namespace ProductAggregator.Core.Models;

/// <summary>
/// Request to aggregate product information.
/// </summary>
public class AggregatedProductRequest
{
    public List<string> ProductIds { get; set; } = new();
    public bool IncludePrices { get; set; } = true;
    public bool IncludeStock { get; set; } = true;
}

/// <summary>
/// Response containing aggregated products.
/// </summary>
public class AggregatedProductResponse
{
    public List<Product> Products { get; set; } = new();
    public int TotalRequested { get; set; }
    public int TotalSuccessful { get; set; }
    public long ProcessingTimeMs { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<ProviderError> ProviderErrors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
