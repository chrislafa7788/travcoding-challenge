using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ProductAggregator.Core.Factories;
using ProductAggregator.Core.Interfaces;
using ProductAggregator.Core.Models;
using ProductAggregator.Core.Services;
using ProductAggregator.Core.Services.Caching;
using ProductAggregator.Core.Services.MockProviders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AggregationOptions>(
    builder.Configuration.GetSection(AggregationOptions.SectionName));

builder.Services.AddMemoryCache();

// Add controllers
builder.Services.AddControllers();

// Add OpenAPI/Swagger
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();

RegisterCachedPriceProvider<MockPriceProviderA>(builder.Services);
RegisterCachedPriceProvider<MockPriceProviderB>(builder.Services);
RegisterCachedPriceProvider<MockPriceProviderC>(builder.Services);
RegisterCachedStockProvider<MockStockProviderEast>(builder.Services);
RegisterCachedStockProvider<MockStockProviderWest>(builder.Services);

builder.Services.AddSingleton<IProviderFactory, ProviderFactory>();

// Product Aggregator Service
builder.Services.AddScoped<IProductAggregatorService, ProductAggregatorService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapControllers();

app.MapGet("/", () => Results.Ok(new
{
    title = "Product Aggregator API - Code Challenge",
    description = "Optimize the product aggregation service for better performance",
    endpoints = new
    {
        aggregate = "POST /api/products/aggregate",
        getProduct = "GET /api/products/{productId}",
        benchmark = "GET /api/products/benchmark?productCount=10",
        benchmarkCompare = "GET /api/products/benchmark/compare?productCount=10"
    }
}));

app.Run();

static void RegisterCachedPriceProvider<TProvider>(IServiceCollection services)
    where TProvider : class, IPriceProvider
{
    services.AddSingleton<TProvider>();
    services.AddSingleton<IPriceProvider>(sp => new CachingPriceProvider(
        sp.GetRequiredService<TProvider>(),
        sp.GetRequiredService<IMemoryCache>(),
        sp.GetRequiredService<IOptions<AggregationOptions>>(),
        sp.GetRequiredService<ILogger<CachingPriceProvider>>()));
}

static void RegisterCachedStockProvider<TProvider>(IServiceCollection services)
    where TProvider : class, IStockProvider
{
    services.AddSingleton<TProvider>();
    services.AddSingleton<IStockProvider>(sp => new CachingStockProvider(
        sp.GetRequiredService<TProvider>(),
        sp.GetRequiredService<IMemoryCache>(),
        sp.GetRequiredService<IOptions<AggregationOptions>>(),
        sp.GetRequiredService<ILogger<CachingStockProvider>>()));
}
