using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductAggregator.Core.Interfaces;
using ProductAggregator.Core.Models;

namespace ProductAggregator.Core.Services;

public class ProductAggregatorService : IProductAggregatorService
{
    private readonly IProviderFactory _providerFactory;
    private readonly AggregationOptions _options;
    private readonly ILogger<ProductAggregatorService> _logger;

    public ProductAggregatorService(
        IProviderFactory providerFactory,
        IOptions<AggregationOptions> options,
        ILogger<ProductAggregatorService> logger)
    {
        _providerFactory = providerFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AggregatedProductResponse> AggregateProductsAsync(
        AggregatedProductRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = new AggregatedProductResponse
        {
            TotalRequested = request.ProductIds.Count
        };

        var maxConcurrency = Math.Max(1, _options.MaxConcurrentProducts);
        var productResults = new ProductAggregationResult[request.ProductIds.Count];

        await Parallel.ForEachAsync(
            request.ProductIds.Select((productId, index) => (productId, index)),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = maxConcurrency,
                CancellationToken = cancellationToken
            },
            async (item, ct) =>
            {
                productResults[item.index] = await AggregateProductAsync(item.productId, request, ct);
            });

        foreach (var result in productResults)
        {
            if (result.Error != null)
            {
                response.Errors.Add(result.Error);
            }
            else if (result.Product != null)
            {
                response.Products.Add(result.Product);
                response.TotalSuccessful++;
            }

            response.ProviderErrors.AddRange(result.ProviderErrors);
            response.Warnings.AddRange(result.Warnings);
        }

        stopwatch.Stop();
        response.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;

        if (response.ProviderErrors.Count > 0)
        {
            _logger.LogWarning(
                "Aggregation completed with {ProviderErrorCount} provider error(s) across {ProductCount} product(s)",
                response.ProviderErrors.Count,
                response.TotalSuccessful);
        }

        return response;
    }

    public async Task<Product?> GetProductAsync(string productId, CancellationToken cancellationToken = default)
    {
        var request = new AggregatedProductRequest
        {
            ProductIds = new List<string> { productId }
        };

        var result = await AggregateProductAsync(productId, request, cancellationToken);
        return result.Product;
    }

    private async Task<ProductAggregationResult> AggregateProductAsync(
        string productId,
        AggregatedProductRequest request,
        CancellationToken cancellationToken)
    {
        var productStopwatch = Stopwatch.StartNew();

        try
        {
            var product = new Product
            {
                Id = productId,
                Name = $"Product {productId}",
                Description = $"Description for product {productId}",
                Category = GetCategoryFromId(productId)
            };

            var providerErrors = new List<ProviderError>();
            var priceProviders = _providerFactory.GetPriceProviders();
            var stockProviders = _providerFactory.GetStockProviders();
            var expectedPriceProviders = request.IncludePrices ? priceProviders.Count() : 0;
            var expectedStockProviders = request.IncludeStock ? stockProviders.Count() : 0;
            var successfulPriceProviders = 0;
            var successfulStockProviders = 0;

            var pricesTask = request.IncludePrices
                ? FetchPricesAsync(productId, cancellationToken)
                : null;
            var stockTask = request.IncludeStock
                ? FetchStockAsync(productId, cancellationToken)
                : null;

            if (pricesTask != null && stockTask != null)
            {
                await Task.WhenAll(pricesTask, stockTask);
                var pricesResult = await pricesTask;
                var stockResult = await stockTask;

                product.Prices.AddRange(pricesResult.Prices);
                product.StockLevels.AddRange(stockResult.StockInfos);
                providerErrors.AddRange(pricesResult.Errors);
                providerErrors.AddRange(stockResult.Errors);
                successfulPriceProviders = pricesResult.Prices.Count;
                successfulStockProviders = expectedStockProviders - stockResult.Errors.Count;
            }
            else if (pricesTask != null)
            {
                var pricesResult = await pricesTask;
                product.Prices.AddRange(pricesResult.Prices);
                providerErrors.AddRange(pricesResult.Errors);
                successfulPriceProviders = pricesResult.Prices.Count;
            }
            else if (stockTask != null)
            {
                var stockResult = await stockTask;
                product.StockLevels.AddRange(stockResult.StockInfos);
                providerErrors.AddRange(stockResult.Errors);
                successfulStockProviders = expectedStockProviders - stockResult.Errors.Count;
            }

            productStopwatch.Stop();

            var warnings = BuildWarnings(productId, providerErrors);

            _logger.LogInformation(
                "Aggregated product {ProductId} in {DurationMs}ms. Price providers: {SuccessfulPriceProviders}/{ExpectedPriceProviders}, stock providers: {SuccessfulStockProviders}/{ExpectedStockProviders}, provider failures: {FailureCount}",
                productId,
                productStopwatch.ElapsedMilliseconds,
                successfulPriceProviders,
                expectedPriceProviders,
                successfulStockProviders,
                expectedStockProviders,
                providerErrors.Count);

            return new ProductAggregationResult(product, null, providerErrors, warnings);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            productStopwatch.Stop();

            _logger.LogError(
                ex,
                "Failed to aggregate product {ProductId} after {DurationMs}ms",
                productId,
                productStopwatch.ElapsedMilliseconds);

            return new ProductAggregationResult(
                null,
                $"Failed to process product {productId}: {ex.Message}",
                [],
                []);
        }
    }

    private async Task<(List<PriceInfo> Prices, List<ProviderError> Errors)> FetchPricesAsync(
        string productId,
        CancellationToken cancellationToken)
    {
        var tasks = _providerFactory.GetPriceProviders().Select(provider =>
            FetchPriceFromProviderAsync(provider, productId, cancellationToken));
        var results = await Task.WhenAll(tasks);

        var prices = new List<PriceInfo>();
        var errors = new List<ProviderError>();

        foreach (var (price, error) in results)
        {
            if (price != null)
            {
                prices.Add(price);
            }

            if (error != null)
            {
                errors.Add(error);
            }
        }

        return (prices, errors);
    }

    private async Task<(PriceInfo? Price, ProviderError? Error)> FetchPriceFromProviderAsync(
        IPriceProvider provider,
        string productId,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CreateProviderTimeoutSource(cancellationToken);
        var providerCancellationToken = timeoutSource?.Token ?? cancellationToken;

        try
        {
            var priceResponse = await provider.GetPriceAsync(productId, providerCancellationToken);
            if (priceResponse.Success && priceResponse.PriceInfo != null)
            {
                return (priceResponse.PriceInfo, null);
            }

            var message = priceResponse.ErrorMessage ?? "Price provider returned an unsuccessful response.";
            _logger.LogWarning(
                "Price provider {ProviderId} returned failure for product {ProductId}: {Message}",
                provider.ProviderId,
                productId,
                message);

            return (null, new ProviderError
            {
                ProductId = productId,
                ProviderId = provider.ProviderId,
                Message = message
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            return (null, CreateProviderTimeoutError(provider.ProviderId, productId, ex));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Price provider {ProviderId} threw for product {ProductId}",
                provider.ProviderId,
                productId);

            return (null, new ProviderError
            {
                ProductId = productId,
                ProviderId = provider.ProviderId,
                Message = ex.Message
            });
        }
    }

    private async Task<(List<StockInfo> StockInfos, List<ProviderError> Errors)> FetchStockAsync(
        string productId,
        CancellationToken cancellationToken)
    {
        var tasks = _providerFactory.GetStockProviders().Select(provider =>
            FetchStockFromProviderAsync(provider, productId, cancellationToken));
        var results = await Task.WhenAll(tasks);

        var stockInfos = new List<StockInfo>();
        var errors = new List<ProviderError>();

        foreach (var (stock, error) in results)
        {
            if (stock != null)
            {
                stockInfos.AddRange(stock);
            }

            if (error != null)
            {
                errors.Add(error);
            }
        }

        return (stockInfos, errors);
    }

    private async Task<(IReadOnlyList<StockInfo>? StockInfos, ProviderError? Error)> FetchStockFromProviderAsync(
        IStockProvider provider,
        string productId,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CreateProviderTimeoutSource(cancellationToken);
        var providerCancellationToken = timeoutSource?.Token ?? cancellationToken;

        try
        {
            var stockResponse = await provider.GetStockAsync(productId, providerCancellationToken);
            if (stockResponse.Success)
            {
                return (stockResponse.StockInfos, null);
            }

            var message = stockResponse.ErrorMessage ?? "Stock provider returned an unsuccessful response.";
            _logger.LogWarning(
                "Stock provider {ProviderId} returned failure for product {ProductId}: {Message}",
                provider.ProviderId,
                productId,
                message);

            return (null, new ProviderError
            {
                ProductId = productId,
                ProviderId = provider.ProviderId,
                Message = message
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            return (null, CreateProviderTimeoutError(provider.ProviderId, productId, ex));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Stock provider {ProviderId} threw for product {ProductId}",
                provider.ProviderId,
                productId);

            return (null, new ProviderError
            {
                ProductId = productId,
                ProviderId = provider.ProviderId,
                Message = ex.Message
            });
        }
    }

    private CancellationTokenSource? CreateProviderTimeoutSource(CancellationToken cancellationToken)
    {
        if (_options.ProviderTimeoutMs <= 0)
        {
            return null;
        }

        var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_options.ProviderTimeoutMs);
        return timeoutSource;
    }

    private ProviderError CreateProviderTimeoutError(
        string providerId,
        string productId,
        OperationCanceledException exception)
    {
        var message = $"Provider timed out after {_options.ProviderTimeoutMs}ms.";

        _logger.LogWarning(
            exception,
            "Provider {ProviderId} timed out for product {ProductId} after {TimeoutMs}ms",
            providerId,
            productId,
            _options.ProviderTimeoutMs);

        return new ProviderError
        {
            ProductId = productId,
            ProviderId = providerId,
            Message = message
        };
    }

    private static List<string> BuildWarnings(string productId, IReadOnlyList<ProviderError> providerErrors)
    {
        if (providerErrors.Count == 0)
        {
            return [];
        }

        return providerErrors
            .Select(error => $"Provider {error.ProviderId} failed for product {productId}: {error.Message}")
            .ToList();
    }

    private static string GetCategoryFromId(string productId)
    {
        var hash = Math.Abs(productId.GetHashCode());
        var categories = new[] { "Electronics", "Clothing", "Home", "Sports", "Books", "Toys" };
        return categories[hash % categories.Length];
    }

    private sealed record ProductAggregationResult(
        Product? Product,
        string? Error,
        List<ProviderError> ProviderErrors,
        List<string> Warnings);
}
