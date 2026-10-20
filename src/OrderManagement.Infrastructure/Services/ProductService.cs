using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderManagement.Application.DTOs;
using OrderManagement.Application.Interfaces;
using OrderManagement.Domain.Entities;
using OrderManagement.Domain.Exceptions;
using OrderManagement.Infrastructure.Data;

namespace OrderManagement.Infrastructure.Services;

public class ProductService : IProductService
{
    private readonly OrderDbContext _dbContext;
    private readonly ILogger<ProductService> _logger;

    public ProductService(OrderDbContext dbContext, ILogger<ProductService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ProductResponse>> GetAllProductsAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Products
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new ProductResponse(p.Id, p.Name, p.StockQuantity, p.Price))
            .ToListAsync(cancellationToken);
    }

    public async Task<ProductResponse> GetProductByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await _dbContext.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (product == null)
        {
            throw new ProductNotFoundException(id);
        }

        return new ProductResponse(product.Id, product.Name, product.StockQuantity, product.Price);
    }

    public async Task<ProductResponse> CreateProductAsync(CreateProductRequest request, CancellationToken cancellationToken = default)
    {
        var product = new Product
        {
            Name = request.Name,
            StockQuantity = request.StockQuantity,
            Price = request.Price
        };

        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created new product {ProductId} with name '{Name}' and stock {StockQuantity}",
            product.Id, product.Name, product.StockQuantity);

        return new ProductResponse(product.Id, product.Name, product.StockQuantity, product.Price);
    }
}
