using OrderManagement.Application.DTOs;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Interfaces;

public interface IOrderService
{
    Task<OrderResponse> CreateOrderAsync(CreateOrderRequest request, string? idempotencyKey, CancellationToken cancellationToken = default);
    Task<OrderResponse> GetOrderByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PagedResult<OrderResponse>> ListOrdersAsync(OrderListQuery query, CancellationToken cancellationToken = default);
    Task<OrderResponse> UpdateOrderStatusAsync(Guid id, OrderStatus newStatus, CancellationToken cancellationToken = default);
    Task<OrderResponse> CancelOrderAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IProductService
{
    Task<IReadOnlyList<ProductResponse>> GetAllProductsAsync(CancellationToken cancellationToken = default);
    Task<ProductResponse> GetProductByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProductResponse> CreateProductAsync(CreateProductRequest request, CancellationToken cancellationToken = default);
}

public interface IIdempotencyService
{
    Task<(bool IsProcessed, OrderResponse? CachedResponse)> TryAcquireOrGetCachedAsync(
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken = default);

    Task SaveCompletedResponseAsync(
        string idempotencyKey,
        int statusCode,
        OrderResponse response,
        CancellationToken cancellationToken = default);

    Task MarkFailedAsync(string idempotencyKey, CancellationToken cancellationToken = default);
}
