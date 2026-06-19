namespace ProductAggregator.Core.Services.Caching;

internal static class ProviderCacheKeys
{
    public static string Price(string providerId, string productId) =>
        $"{providerId}:{productId}:price";

    public static string Stock(string providerId, string productId) =>
        $"{providerId}:{productId}:stock";
}
