using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductAggregator.Core.Interfaces;
using ProductAggregator.Core.Models;

namespace ProductAggregator.Core.Services.Caching;

public sealed class CachingPriceProvider : IPriceProvider
{
    private readonly IPriceProvider _inner;
    private readonly IMemoryCache _cache;
    private readonly AggregationOptions _options;
    private readonly ILogger<CachingPriceProvider> _logger;

    public CachingPriceProvider(
        IPriceProvider inner,
        IMemoryCache cache,
        IOptions<AggregationOptions> options,
        ILogger<CachingPriceProvider> logger)
    {
        _inner = inner;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public string ProviderId => _inner.ProviderId;
    public string ProviderName => _inner.ProviderName;

    public async Task<PriceProviderResponse> GetPriceAsync(
        string productId,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = ProviderCacheKeys.Price(ProviderId, productId);

        if (TryGetCached(cacheKey, out PriceProviderResponse? cached))
        {
            _logger.LogDebug("Cache hit for price provider {ProviderId}, product {ProductId}", ProviderId, productId);
            return cached!;
        }

        var response = await _inner.GetPriceAsync(productId, cancellationToken);
        CacheIfSuccessful(cacheKey, response);

        return response;
    }

    private bool TryGetCached<T>(string cacheKey, out T? value)
    {
        value = default;
        if (_options.CacheTtlSeconds <= 0)
        {
            return false;
        }

        return _cache.TryGetValue(cacheKey, out value);
    }

    private void CacheIfSuccessful(string cacheKey, PriceProviderResponse response)
    {
        if (_options.CacheTtlSeconds <= 0 || !response.Success)
        {
            return;
        }

        _cache.Set(
            cacheKey,
            response,
            TimeSpan.FromSeconds(_options.CacheTtlSeconds));
    }
}
