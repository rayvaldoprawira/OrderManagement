using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.DTOs;

public record CreateOrderItemRequest(
    Guid ProductId,
    int Quantity
);

public record CreateOrderRequest(
    string CustomerId,
    string ShippingAddress,
    List<CreateOrderItemRequest> Items
);

public record UpdateOrderStatusRequest(
    OrderStatus Status
);

public record OrderItemResponse(
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal Subtotal
);

public record OrderResponse(
    Guid Id,
    string CustomerId,
    string ShippingAddress,
    string Status,
    decimal TotalAmount,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    string ConcurrencyToken,
    List<OrderItemResponse> Items
);

public record ProductResponse(
    Guid Id,
    string Name,
    int StockQuantity,
    decimal Price
);

public record CreateProductRequest(
    string Name,
    int StockQuantity,
    decimal Price
);

public record OrderListQuery(
    string? CustomerId = null,
    OrderStatus? Status = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    int PageNumber = 1,
    int PageSize = 10
);

public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int PageNumber { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
    public IReadOnlyList<string>? Errors { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public static ApiResponse<T> Ok(T data, string message = "Success", string correlationId = "") =>
        new() { Success = true, Message = message, Data = data, CorrelationId = correlationId };

    public static ApiResponse<T> Fail(string message, IEnumerable<string>? errors = null, string correlationId = "") =>
        new() { Success = false, Message = message, Errors = errors?.ToList(), CorrelationId = correlationId };
}
