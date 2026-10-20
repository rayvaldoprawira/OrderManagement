using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.Application.DTOs;
using OrderManagement.Application.Interfaces;

namespace OrderManagement.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _productService;
    private readonly IValidator<CreateProductRequest> _createProductValidator;

    public ProductsController(
        IProductService productService,
        IValidator<CreateProductRequest> createProductValidator)
    {
        _productService = productService;
        _createProductValidator = createProductValidator;
    }

    private string GetCorrelationId() =>
        HttpContext.Items["CorrelationId"]?.ToString() ?? HttpContext.TraceIdentifier;

    /// <summary>
    /// Mengambil semua daftar master produk beserta jumlah sisa stok saat ini.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token pembatalan request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ProductResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var products = await _productService.GetAllProductsAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ProductResponse>>.Ok(products, "Products retrieved successfully", GetCorrelationId()));
    }

    /// <summary>
    /// Mengambil detail satu produk berdasarkan Product ID.
    /// </summary>
    /// <param name="id">ID unik (GUID) dari produk.</param>
    /// <param name="cancellationToken">Cancellation token pembatalan request.</param>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ProductResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var product = await _productService.GetProductByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<ProductResponse>.Ok(product, "Product retrieved successfully", GetCorrelationId()));
    }

    /// <summary>
    /// Membuat master produk baru beserta alokasi stok awal dan harga.
    /// </summary>
    /// <param name="request">Data produk baru (Nama, StockQuantity, Price).</param>
    /// <param name="cancellationToken">Cancellation token pembatalan request.</param>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<ProductResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateProductRequest request, CancellationToken cancellationToken)
    {
        await _createProductValidator.ValidateAndThrowAsync(request, cancellationToken);
        var product = await _productService.CreateProductAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = product.Id },
            ApiResponse<ProductResponse>.Ok(product, "Product created successfully", GetCorrelationId()));
    }
}
