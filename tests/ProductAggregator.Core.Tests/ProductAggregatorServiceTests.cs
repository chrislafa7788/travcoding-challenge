using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProductAggregator.Core.Models;
using ProductAggregator.Core.Services;
using ProductAggregator.Core.Tests.TestDoubles;

namespace ProductAggregator.Core.Tests;

public class ProductAggregatorServiceTests
{
    [Fact]
    public async Task AggregateProductsAsync_InvokesAllRegisteredProviders()
    {
        var priceA = new FakePriceProvider { ProviderId = "PRICE_A" };
        var priceB = new FakePriceProvider { ProviderId = "PRICE_B" };
        var stockEast = new FakeStockProvider { ProviderId = "STOCK_EAST" };
        var service = CreateService(new StubProviderFactory(
            [priceA, priceB],
            [stockEast]));

        var response = await service.AggregateProductsAsync(new AggregatedProductRequest
        {
            ProductIds = ["PROD-001"]
        });

        Assert.Equal(1, response.TotalSuccessful);
        Assert.Equal(2, response.Products[0].Prices.Count);
        Assert.Single(response.Products[0].StockLevels);
        Assert.Equal(1, priceA.CallCount);
        Assert.Equal(1, priceB.CallCount);
        Assert.Equal(1, stockEast.CallCount);
    }

    [Fact]
    public async Task AggregateProductsAsync_ContinuesWhenProviderFails()
    {
        var failingPrice = new FakePriceProvider { ProviderId = "PRICE_A", ShouldFail = true };
        var healthyPrice = new FakePriceProvider { ProviderId = "PRICE_B" };
        var service = CreateService(new StubProviderFactory(
            [failingPrice, healthyPrice],
            []));

        var response = await service.AggregateProductsAsync(new AggregatedProductRequest
        {
            ProductIds = ["PROD-001"],
            IncludeStock = false
        });

        Assert.Equal(1, response.TotalSuccessful);
        Assert.Single(response.Products[0].Prices);
        Assert.Single(response.ProviderErrors);
        Assert.Equal("PRICE_A", response.ProviderErrors[0].ProviderId);
        Assert.Single(response.Warnings);
    }

    [Fact]
    public async Task AggregateProductsAsync_PropagatesCancellationToken()
    {
        var slowPrice = new FakePriceProvider { ProviderId = "PRICE_A", DelayMs = 5000 };
        var service = CreateService(new StubProviderFactory([slowPrice], []));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.AggregateProductsAsync(
                new AggregatedProductRequest { ProductIds = ["PROD-001"], IncludeStock = false },
                cts.Token));
    }

    [Fact]
    public async Task AggregateProductsAsync_ReportsProviderTimeoutAsPartialFailure()
    {
        var slowPrice = new FakePriceProvider { ProviderId = "PRICE_A", DelayMs = 5000 };
        var fastPrice = new FakePriceProvider { ProviderId = "PRICE_B" };
        var service = CreateService(
            new StubProviderFactory([slowPrice, fastPrice], []),
            options => options.ProviderTimeoutMs = 50);

        var response = await service.AggregateProductsAsync(new AggregatedProductRequest
        {
            ProductIds = ["PROD-001"],
            IncludeStock = false
        });

        Assert.Equal(1, response.TotalSuccessful);
        Assert.Single(response.Products[0].Prices);
        Assert.Single(response.ProviderErrors);
        Assert.Contains("timed out", response.ProviderErrors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AggregateProductsAsync_PreservesProductOrder()
    {
        var price = new FakePriceProvider { ProviderId = "PRICE_A" };
        var service = CreateService(new StubProviderFactory([price], []));

        var response = await service.AggregateProductsAsync(new AggregatedProductRequest
        {
            ProductIds = ["PROD-001", "PROD-002", "PROD-003"],
            IncludeStock = false
        });

        Assert.Equal(["PROD-001", "PROD-002", "PROD-003"], response.Products.Select(product => product.Id));
    }

    private static ProductAggregatorService CreateService(
        StubProviderFactory providerFactory,
        Action<AggregationOptions>? configure = null)
    {
        var options = new AggregationOptions
        {
            MaxConcurrentProducts = 5,
            CacheTtlSeconds = 0,
            ProviderTimeoutMs = 2000
        };
        configure?.Invoke(options);

        return new ProductAggregatorService(
            providerFactory,
            Options.Create(options),
            NullLogger<ProductAggregatorService>.Instance);
    }
}
