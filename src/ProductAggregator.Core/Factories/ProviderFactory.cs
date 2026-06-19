using ProductAggregator.Core.Interfaces;

namespace ProductAggregator.Core.Factories;

/// <summary>
/// Factory for resolving provider instances registered in DI.
/// </summary>
public class ProviderFactory : IProviderFactory
{
    private readonly Dictionary<string, IPriceProvider> _priceProviders;
    private readonly Dictionary<string, IStockProvider> _stockProviders;

    public ProviderFactory(
        IEnumerable<IPriceProvider> priceProviders,
        IEnumerable<IStockProvider> stockProviders)
    {
        _priceProviders = priceProviders.ToDictionary(provider => provider.ProviderId);
        _stockProviders = stockProviders.ToDictionary(provider => provider.ProviderId);
    }

    public IEnumerable<IPriceProvider> GetPriceProviders() => _priceProviders.Values;

    public IEnumerable<IStockProvider> GetStockProviders() => _stockProviders.Values;

    public IPriceProvider? GetPriceProvider(string providerId)
    {
        _priceProviders.TryGetValue(providerId, out var provider);
        return provider;
    }

    public IStockProvider? GetStockProvider(string providerId)
    {
        _stockProviders.TryGetValue(providerId, out var provider);
        return provider;
    }
}
