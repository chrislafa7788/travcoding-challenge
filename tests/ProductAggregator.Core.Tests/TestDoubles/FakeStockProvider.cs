using ProductAggregator.Core.Interfaces;
using ProductAggregator.Core.Models;

namespace ProductAggregator.Core.Tests.TestDoubles;

internal sealed class FakeStockProvider : IStockProvider
{
    public string ProviderId { get; init; } = "STOCK_TEST";
    public string ProviderName { get; init; } = "Test Stock Provider";
    public bool ShouldFail { get; init; }
    public bool ShouldThrow { get; init; }
    public int DelayMs { get; init; }

    public int CallCount { get; private set; }

    public Task<StockProviderResponse> GetStockAsync(
        string productId,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        cancellationToken.ThrowIfCancellationRequested();

        if (ShouldThrow)
        {
            throw new InvalidOperationException("Simulated provider exception.");
        }

        return GetStockInternalAsync(productId, cancellationToken);
    }

    private async Task<StockProviderResponse> GetStockInternalAsync(
        string productId,
        CancellationToken cancellationToken)
    {
        if (DelayMs > 0)
        {
            await Task.Delay(DelayMs, cancellationToken);
        }

        if (ShouldFail)
        {
            return new StockProviderResponse
            {
                Success = false,
                ProductId = productId,
                ErrorMessage = "Simulated provider failure."
            };
        }

        return new StockProviderResponse
        {
            Success = true,
            ProductId = productId,
            StockInfos =
            [
                new StockInfo
                {
                    WarehouseId = $"WH_{ProviderId}",
                    WarehouseName = $"{ProviderId} Warehouse",
                    Location = ProviderId,
                    Quantity = 10
                }
            ]
        };
    }
}
