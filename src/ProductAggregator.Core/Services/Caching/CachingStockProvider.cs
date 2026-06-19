using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductAggregator.Core.Interfaces;
using ProductAggregator.Core.Models;

namespace ProductAggregator.Core.Services.Caching;

public sealed class CachingStockProvider : IStockProvider
{
    private readonly IStockProvider _inner;
    private readonly IMemoryCache _cache;
    private readonly AggregationOptions _options;
    private readonly ILogger<CachingStockProvider> _logger;

    public CachingStockProvider(
        IStockProvider inner,
        IMemoryCache cache,
        IOptions<AggregationOptions> options,
        ILogger<CachingStockProvider> logger)
    {
        _inner = inner;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public string ProviderId => _inner.ProviderId;
    public string ProviderName => _inner.ProviderName;

    public async Task<StockProviderResponse> GetStockAsync(
        string productId,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = ProviderCacheKeys.Stock(ProviderId, productId);

        if (TryGetCached(cacheKey, out StockProviderResponse? cached))
        {
            _logger.LogDebug("Cache hit for stock provider {ProviderId}, product {ProductId}", ProviderId, productId);
            return cached!;
        }

        var response = await _inner.GetStockAsync(productId, cancellationToken);
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

    private void CacheIfSuccessful(string cacheKey, StockProviderResponse response)
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
