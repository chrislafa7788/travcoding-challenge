using Microsoft.AspNetCore.Mvc;
using ProductAggregator.Core.Interfaces;
using ProductAggregator.Core.Models;

namespace ProductAggregator.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IProductAggregatorService _aggregatorService;
    private readonly ILogger<ProductsController> _logger;

    public ProductsController(
        IProductAggregatorService aggregatorService,
        ILogger<ProductsController> logger)
    {
        _aggregatorService = aggregatorService;
        _logger = logger;
    }

    /// <summary>
    /// Gets aggregated product information for multiple products.
    /// </summary>
    /// <param name="request">Request containing product IDs and options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Aggregated product response.</returns>
    [HttpPost("aggregate")]
    [ProducesResponseType(typeof(AggregatedProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AggregatedProductResponse>> AggregateProducts(
        [FromBody] AggregatedProductRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ProductIds == null || request.ProductIds.Count == 0)
        {
            return BadRequest("At least one product ID is required.");
        }

        if (request.ProductIds.Count > 50)
        {
            return BadRequest("Maximum 50 products per request.");
        }

        _logger.LogInformation(
            "Received aggregation request for {Count} products",
            request.ProductIds.Count);

        var response = await _aggregatorService.AggregateProductsAsync(request, cancellationToken);
        
        return Ok(response);
    }

    /// <summary>
    /// Gets aggregated product information for a single product.
    /// </summary>
    /// <param name="productId">The product identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The aggregated product.</returns>
    [HttpGet("{productId}")]
    [ProducesResponseType(typeof(Product), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Product>> GetProduct(
        string productId,
        CancellationToken cancellationToken)
    {
        var product = await _aggregatorService.GetProductAsync(productId, cancellationToken);
        
        if (product == null)
        {
            return NotFound($"Product {productId} not found.");
        }

        return Ok(product);
    }

    /// <summary>
    /// Benchmark endpoint to compare implementations.
    /// </summary>
    [HttpGet("benchmark")]
    [ProducesResponseType(typeof(BenchmarkResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<BenchmarkResult>> RunBenchmark(
        [FromQuery] int productCount = 10,
        CancellationToken cancellationToken = default)
    {
        productCount = Math.Clamp(productCount, 1, 20);
        
        var productIds = Enumerable.Range(1, productCount)
            .Select(i => $"PROD-{i:D4}")
            .ToList();

        var request = new AggregatedProductRequest { ProductIds = productIds };
        
        var response = await _aggregatorService.AggregateProductsAsync(request, cancellationToken);

        return Ok(new BenchmarkResult
        {
            ProductCount = productCount,
            ProcessingTimeMs = response.ProcessingTimeMs,
            SuccessfulProducts = response.TotalSuccessful,
            AverageTimePerProduct = response.ProcessingTimeMs / (double)productCount,
            Errors = response.Errors
        });
    }

    /// <summary>
    /// Compares current benchmark results against the documented sequential baseline.
    /// </summary>
    [HttpGet("benchmark/compare")]
    [ProducesResponseType(typeof(BenchmarkComparisonResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<BenchmarkComparisonResult>> CompareBenchmark(
        [FromQuery] int productCount = 10,
        CancellationToken cancellationToken = default)
    {
        productCount = Math.Clamp(productCount, 1, 20);

        var productIds = Enumerable.Range(1, productCount)
            .Select(i => $"PROD-{i:D4}")
            .ToList();

        var request = new AggregatedProductRequest { ProductIds = productIds };
        var response = await _aggregatorService.AggregateProductsAsync(request, cancellationToken);

        return Ok(BenchmarkComparisonCalculator.Create(productCount, response.ProcessingTimeMs));
    }
}

public class BenchmarkResult
{
    public int ProductCount { get; set; }
    public long ProcessingTimeMs { get; set; }
    public int SuccessfulProducts { get; set; }
    public double AverageTimePerProduct { get; set; }
    public List<string> Errors { get; set; } = new();
}

