using ProductAggregator.Core.Interfaces;
using ProductAggregator.Core.Models;

namespace ProductAggregator.Core.Tests.TestDoubles;

internal sealed class FakePriceProvider : IPriceProvider
{
    public string ProviderId { get; init; } = "PRICE_TEST";
    public string ProviderName { get; init; } = "Test Price Provider";
    public bool ShouldFail { get; init; }
    public bool ShouldThrow { get; init; }
    public int DelayMs { get; init; }

    public int CallCount { get; private set; }

    public Task<PriceProviderResponse> GetPriceAsync(
        string productId,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        cancellationToken.ThrowIfCancellationRequested();

        if (ShouldThrow)
        {
            throw new InvalidOperationException("Simulated provider exception.");
        }

        return GetPriceInternalAsync(productId, cancellationToken);
    }

    private async Task<PriceProviderResponse> GetPriceInternalAsync(
        string productId,
        CancellationToken cancellationToken)
    {
        if (DelayMs > 0)
        {
            await Task.Delay(DelayMs, cancellationToken);
        }

        if (ShouldFail)
        {
            return new PriceProviderResponse
            {
                Success = false,
                ProductId = productId,
                ErrorMessage = "Simulated provider failure."
            };
        }

        return new PriceProviderResponse
        {
            Success = true,
            ProductId = productId,
            PriceInfo = new PriceInfo
            {
                ProviderId = ProviderId,
                ProviderName = ProviderName,
                Price = 99.99m,
                Currency = "USD",
                LastUpdated = DateTime.UtcNow
            }
        };
    }
}
