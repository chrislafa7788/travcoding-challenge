namespace ProductAggregator.Core.Models;

public class ProviderError
{
    public string ProductId { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
