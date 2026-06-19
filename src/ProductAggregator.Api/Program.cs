using ProductAggregator.Core.Factories;
using ProductAggregator.Core.Interfaces;
using ProductAggregator.Core.Models;
using ProductAggregator.Core.Services;
using ProductAggregator.Core.Services.MockProviders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AggregationOptions>(
    builder.Configuration.GetSection(AggregationOptions.SectionName));

// Add controllers
builder.Services.AddControllers();

// Add OpenAPI/Swagger
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();

// Provider Factory
builder.Services.AddSingleton<IProviderFactory, ProviderFactory>();

// Individual providers
builder.Services.AddSingleton<IPriceProvider, MockPriceProviderA>();
builder.Services.AddSingleton<IPriceProvider, MockPriceProviderB>();
builder.Services.AddSingleton<IPriceProvider, MockPriceProviderC>();
builder.Services.AddSingleton<IStockProvider, MockStockProviderEast>();
builder.Services.AddSingleton<IStockProvider, MockStockProviderWest>();

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
        benchmark = "GET /api/products/benchmark?productCount=10"
    }
}));

app.Run();
