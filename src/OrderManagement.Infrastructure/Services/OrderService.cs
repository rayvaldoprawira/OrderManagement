using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderManagement.Application.DTOs;
using OrderManagement.Application.Interfaces;
using OrderManagement.Domain.Entities;
using OrderManagement.Domain.Enums;
using OrderManagement.Domain.Exceptions;
using OrderManagement.Infrastructure.Data;

namespace OrderManagement.Infrastructure.Services;

public class OrderService : IOrderService
{
    private readonly OrderDbContext _dbContext;
    private readonly IIdempotencyService _idempotencyService;
    private readonly ILogger<OrderService> _logger;
    private static readonly SemaphoreSlim InMemoryLock = new(1, 1);

    public OrderService(
        OrderDbContext dbContext,
        IIdempotencyService idempotencyService,
        ILogger<OrderService> logger)
    {
        _dbContext = dbContext;
        _idempotencyService = idempotencyService;
        _logger = logger;
    }

    public async Task<OrderResponse> CreateOrderAsync(
        CreateOrderRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        string? requestHash = null;

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            requestHash = ComputeRequestHash(request);
            var (isProcessed, cachedResponse) = await _idempotencyService.TryAcquireOrGetCachedAsync(
                idempotencyKey, requestHash, cancellationToken);

            if (isProcessed && cachedResponse != null)
            {
                _logger.LogInformation("Returning idempotent response for key {Key}", idempotencyKey);
                return cachedResponse;
            }
        }

        try
        {
            var response = await ExecuteCreateOrderAsync(request, cancellationToken);

            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                await _idempotencyService.SaveCompletedResponseAsync(idempotencyKey, 201, response, cancellationToken);
            }

            return response;
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                await _idempotencyService.MarkFailedAsync(idempotencyKey, CancellationToken.None);
            }
            throw;
        }
    }

    private async Task<OrderResponse> ExecuteCreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        // Consolidate identical items by ProductId to prevent multiple deductions for the same product in one order
        var consolidatedItems = request.Items
            .GroupBy(i => i.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(i => i.Quantity) })
            .OrderBy(i => i.ProductId) // Sort to establish deterministic locking hierarchy and prevent deadlocks
            .ToList();

        var isSqlServer = _dbContext.Database.IsSqlServer();

        if (isSqlServer)
        {
            var strategy = _dbContext.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken);

                var order = new Order
                {
                    CustomerId = request.CustomerId,
                    ShippingAddress = request.ShippingAddress,
                    Status = OrderStatus.Pending,
                    CreatedAtUtc = DateTime.UtcNow
                };

                foreach (var item in consolidatedItems)
                {
                    // Scenario A: Atomic conditional stock deduction with row-level lock
                    var affectedRows = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                        $"UPDATE Products SET StockQuantity = StockQuantity - {item.Quantity} WHERE Id = {item.ProductId} AND StockQuantity >= {item.Quantity}",
                        cancellationToken);

                    if (affectedRows == 0)
                    {
                        var product = await _dbContext.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == item.ProductId, cancellationToken);
                        if (product == null)
                        {
                            throw new ProductNotFoundException(item.ProductId);
                        }

                        throw new InsufficientStockException(product.Id, product.Name, product.StockQuantity, item.Quantity);
                    }

                    var updatedProduct = await _dbContext.Products.AsNoTracking().FirstAsync(p => p.Id == item.ProductId, cancellationToken);

                    order.Items.Add(new OrderItem
                    {
                        ProductId = item.ProductId,
                        Quantity = item.Quantity,
                        UnitPrice = updatedProduct.Price
                    });
                }

                order.CalculateTotal();
                _dbContext.Orders.Add(order);
                await _dbContext.SaveChangesAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                _logger.LogInformation("Order {OrderId} successfully created for customer {CustomerId} with total {TotalAmount}",
                    order.Id, order.CustomerId, order.TotalAmount);

                return await GetOrderByIdAsync(order.Id, cancellationToken);
            });
        }
        else
        {
            // Fallback for InMemory provider / unit tests
            await InMemoryLock.WaitAsync(cancellationToken);
            try
            {
                var order = new Order
                {
                    CustomerId = request.CustomerId,
                    ShippingAddress = request.ShippingAddress,
                    Status = OrderStatus.Pending,
                    CreatedAtUtc = DateTime.UtcNow
                };

                foreach (var item in consolidatedItems)
                {
                    var product = await _dbContext.Products.FirstOrDefaultAsync(p => p.Id == item.ProductId, cancellationToken);
                    if (product == null)
                    {
                        throw new ProductNotFoundException(item.ProductId);
                    }

                    if (product.StockQuantity < item.Quantity)
                    {
                        throw new InsufficientStockException(product.Id, product.Name, product.StockQuantity, item.Quantity);
                    }

                    product.DeductStock(item.Quantity);

                    order.Items.Add(new OrderItem
                    {
                        ProductId = item.ProductId,
                        Quantity = item.Quantity,
                        UnitPrice = product.Price
                    });
                }

                order.CalculateTotal();
                _dbContext.Orders.Add(order);
                await _dbContext.SaveChangesAsync(cancellationToken);

                return await GetOrderByIdAsync(order.Id, cancellationToken);
            }
            finally
            {
                InMemoryLock.Release();
            }
        }
    }

    public async Task<OrderResponse> GetOrderByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        if (order == null)
        {
            throw new OrderNotFoundException(id);
        }

        return MapToResponse(order);
    }

    public async Task<PagedResult<OrderResponse>> ListOrdersAsync(
        OrderListQuery query,
        CancellationToken cancellationToken = default)
    {
        var dbQuery = _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .ThenInclude(i => i.Product)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.CustomerId))
        {
            dbQuery = dbQuery.Where(o => o.CustomerId == query.CustomerId);
        }

        if (query.Status.HasValue)
        {
            dbQuery = dbQuery.Where(o => o.Status == query.Status.Value);
        }

        if (query.FromDate.HasValue)
        {
            dbQuery = dbQuery.Where(o => o.CreatedAtUtc >= query.FromDate.Value);
        }

        if (query.ToDate.HasValue)
        {
            dbQuery = dbQuery.Where(o => o.CreatedAtUtc <= query.ToDate.Value);
        }

        var totalCount = await dbQuery.CountAsync(cancellationToken);

        var orders = await dbQuery
            .OrderByDescending(o => o.CreatedAtUtc)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<OrderResponse>
        {
            Items = orders.Select(MapToResponse).ToList(),
            PageNumber = query.PageNumber,
            PageSize = query.PageSize,
            TotalCount = totalCount
        };
    }

    public async Task<OrderResponse> UpdateOrderStatusAsync(
        Guid id,
        OrderStatus newStatus,
        CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        if (order == null)
        {
            throw new OrderNotFoundException(id);
        }

        if (newStatus == OrderStatus.Cancelled)
        {
            return await CancelOrderAsync(id, cancellationToken);
        }

        order.UpdateStatus(newStatus);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Order {OrderId} updated to status {Status}", order.Id, order.Status);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Scenario B: Concurrent status update detected via RowVersion
            _logger.LogWarning(ex, "Concurrency conflict occurred when updating status of order {OrderId}", id);
            var currentOrder = await _dbContext.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
            throw new ConcurrencyConflictException(
                $"Order status was concurrently modified by another user. Current status is '{currentOrder?.Status}'. Please refresh and try again.");
        }

        return await GetOrderByIdAsync(id, cancellationToken);
    }

    public async Task<OrderResponse> CancelOrderAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var isSqlServer = _dbContext.Database.IsSqlServer();

        if (isSqlServer)
        {
            var strategy = _dbContext.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken);

                var order = await _dbContext.Orders
                    .Include(o => o.Items)
                    .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

                if (order == null)
                {
                    throw new OrderNotFoundException(id);
                }

                // Domain rule: cancel validates that status is Pending or Confirmed
                order.Cancel();

                // Restore stock for all items
                foreach (var item in order.Items)
                {
                    await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                        $"UPDATE Products SET StockQuantity = StockQuantity + {item.Quantity} WHERE Id = {item.ProductId}",
                        cancellationToken);
                }

                try
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    _logger.LogInformation("Order {OrderId} cancelled and inventory stock restored.", order.Id);
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    _logger.LogWarning(ex, "Concurrency conflict occurred when cancelling order {OrderId}", id);
                    var currentOrder = await _dbContext.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
                    throw new ConcurrencyConflictException(
                        $"Order was concurrently modified by another transaction. Current status is '{currentOrder?.Status}'. Please refresh and try again.");
                }

                return await GetOrderByIdAsync(id, cancellationToken);
            });
        }
        else
        {
            await InMemoryLock.WaitAsync(cancellationToken);
            try
            {
                var order = await _dbContext.Orders
                    .Include(o => o.Items)
                    .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

                if (order == null)
                {
                    throw new OrderNotFoundException(id);
                }

                order.Cancel();

                foreach (var item in order.Items)
                {
                    var product = await _dbContext.Products.FirstOrDefaultAsync(p => p.Id == item.ProductId, cancellationToken);
                    product?.RestoreStock(item.Quantity);
                }

                try
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateConcurrencyException)
                {
                    var currentOrder = await _dbContext.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
                    throw new ConcurrencyConflictException(
                        $"Order was concurrently modified by another transaction. Current status is '{currentOrder?.Status}'. Please refresh and try again.");
                }

                return await GetOrderByIdAsync(id, cancellationToken);
            }
            finally
            {
                InMemoryLock.Release();
            }
        }
    }

    private static OrderResponse MapToResponse(Order order)
    {
        return new OrderResponse(
            order.Id,
            order.CustomerId,
            order.ShippingAddress,
            order.Status.ToString(),
            order.TotalAmount,
            order.CreatedAtUtc,
            order.UpdatedAtUtc,
            order.RowVersion != null ? Convert.ToBase64String(order.RowVersion) : string.Empty,
            order.Items.Select(i => new OrderItemResponse(
                i.ProductId,
                i.Product?.Name ?? "Unknown Product",
                i.Quantity,
                i.UnitPrice,
                i.Subtotal
            )).ToList()
        );
    }

    private static string ComputeRequestHash(CreateOrderRequest request)
    {
        var json = JsonSerializer.Serialize(request);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }
}
