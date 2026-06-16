using System.Diagnostics;
using ProductAggregator.Core.Interfaces;
using ProductAggregator.Core.Models;

namespace ProductAggregator.Core.Services;

public class ProductAggregatorService : IProductAggregatorService
{
    private readonly IEnumerable<IPriceProvider> _priceProviders;
    private readonly IEnumerable<IStockProvider> _stockProviders;

    public ProductAggregatorService(
        IEnumerable<IPriceProvider> priceProviders,
        IEnumerable<IStockProvider> stockProviders)
    {
        _priceProviders = priceProviders;
        _stockProviders = stockProviders;
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

        foreach (var productId in request.ProductIds)
        {
            try
            {
                var product = await GetProductInternalAsync(productId, request, cancellationToken);
                if (product != null)
                {
                    response.Products.Add(product);
                    response.TotalSuccessful++;
                }
            }
            catch (Exception ex)
            {
                response.Errors.Add($"Failed to process product {productId}: {ex.Message}");
            }
        }

        stopwatch.Stop();
        response.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;

        return response;
    }

    public async Task<Product?> GetProductAsync(string productId, CancellationToken cancellationToken = default)
    {
        var request = new AggregatedProductRequest
        {
            ProductIds = new List<string> { productId }
        };

        return await GetProductInternalAsync(productId, request, cancellationToken);
    }

    private async Task<Product?> GetProductInternalAsync(
        string productId,
        AggregatedProductRequest request,
        CancellationToken cancellationToken)
    {
        var product = new Product
        {
            Id = productId,
            Name = $"Product {productId}",
            Description = $"Description for product {productId}",
            Category = GetCategoryFromId(productId)
        };

        var pricesTask = request.IncludePrices
            ? FetchPricesAsync(productId, cancellationToken)
            : null;
        var stockTask = request.IncludeStock
            ? FetchStockAsync(productId, cancellationToken)
            : null;

        if (pricesTask != null && stockTask != null)
        {
            await Task.WhenAll(pricesTask, stockTask);
            product.Prices.AddRange(await pricesTask);
            product.StockLevels.AddRange(await stockTask);
        }
        else if (pricesTask != null)
        {
            product.Prices.AddRange(await pricesTask);
        }
        else if (stockTask != null)
        {
            product.StockLevels.AddRange(await stockTask);
        }

        return product;
    }

    private async Task<List<PriceInfo>> FetchPricesAsync(
        string productId,
        CancellationToken cancellationToken)
    {
        var tasks = _priceProviders.Select(provider => FetchPriceFromProviderAsync(provider, productId, cancellationToken));
        var results = await Task.WhenAll(tasks);
        return results.Where(price => price != null).Cast<PriceInfo>().ToList();
    }

    private static async Task<PriceInfo?> FetchPriceFromProviderAsync(
        IPriceProvider provider,
        string productId,
        CancellationToken cancellationToken)
    {
        try
        {
            var priceResponse = await provider.GetPriceAsync(productId, cancellationToken);
            if (priceResponse.Success && priceResponse.PriceInfo != null)
            {
                return priceResponse.PriceInfo;
            }
        }
        catch
        {
            // Provider failed, continue with next
        }

        return null;
    }

    private async Task<List<StockInfo>> FetchStockAsync(
        string productId,
        CancellationToken cancellationToken)
    {
        var tasks = _stockProviders.Select(provider => FetchStockFromProviderAsync(provider, productId, cancellationToken));
        var results = await Task.WhenAll(tasks);
        return results.Where(stockInfos => stockInfos != null).SelectMany(stockInfos => stockInfos!).ToList();
    }

    private static async Task<IReadOnlyList<StockInfo>?> FetchStockFromProviderAsync(
        IStockProvider provider,
        string productId,
        CancellationToken cancellationToken)
    {
        try
        {
            var stockResponse = await provider.GetStockAsync(productId, cancellationToken);
            if (stockResponse.Success)
            {
                return stockResponse.StockInfos;
            }
        }
        catch
        {
            // Provider failed, continue with next
        }

        return null;
    }

    private static string GetCategoryFromId(string productId)
    {
        var hash = Math.Abs(productId.GetHashCode());
        var categories = new[] { "Electronics", "Clothing", "Home", "Sports", "Books", "Toys" };
        return categories[hash % categories.Length];
    }
}
