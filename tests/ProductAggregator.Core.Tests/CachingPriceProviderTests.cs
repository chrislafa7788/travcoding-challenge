using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProductAggregator.Core.Models;
using ProductAggregator.Core.Services.Caching;
using ProductAggregator.Core.Tests.TestDoubles;

namespace ProductAggregator.Core.Tests;

public class CachingPriceProviderTests
{
    [Fact]
    public async Task GetPriceAsync_UsesCacheOnSecondCall()
    {
        var inner = new FakePriceProvider { ProviderId = "PRICE_A" };
        var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new CachingPriceProvider(
            inner,
            cache,
            Options.Create(new AggregationOptions { CacheTtlSeconds = 30 }),
            NullLogger<CachingPriceProvider>.Instance);

        await provider.GetPriceAsync("PROD-001");
        await provider.GetPriceAsync("PROD-001");

        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task GetPriceAsync_DoesNotCacheFailedResponses()
    {
        var inner = new FakePriceProvider { ProviderId = "PRICE_A", ShouldFail = true };
        var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new CachingPriceProvider(
            inner,
            cache,
            Options.Create(new AggregationOptions { CacheTtlSeconds = 30 }),
            NullLogger<CachingPriceProvider>.Instance);

        await provider.GetPriceAsync("PROD-001");
        await provider.GetPriceAsync("PROD-001");

        Assert.Equal(2, inner.CallCount);
    }
}
