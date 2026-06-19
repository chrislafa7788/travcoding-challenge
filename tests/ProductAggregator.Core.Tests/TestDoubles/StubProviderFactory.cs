using ProductAggregator.Core.Interfaces;
using ProductAggregator.Core.Models;

namespace ProductAggregator.Core.Tests.TestDoubles;

internal sealed class StubProviderFactory : IProviderFactory
{
    private readonly IReadOnlyList<IPriceProvider> _priceProviders;
    private readonly IReadOnlyList<IStockProvider> _stockProviders;

    public StubProviderFactory(
        IEnumerable<IPriceProvider> priceProviders,
        IEnumerable<IStockProvider> stockProviders)
    {
        _priceProviders = priceProviders.ToList();
        _stockProviders = stockProviders.ToList();
    }

    public IEnumerable<IPriceProvider> GetPriceProviders() => _priceProviders;

    public IEnumerable<IStockProvider> GetStockProviders() => _stockProviders;

    public IPriceProvider? GetPriceProvider(string providerId) =>
        _priceProviders.FirstOrDefault(provider => provider.ProviderId == providerId);

    public IStockProvider? GetStockProvider(string providerId) =>
        _stockProviders.FirstOrDefault(provider => provider.ProviderId == providerId);
}
