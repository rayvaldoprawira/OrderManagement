using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.DTOs;

/// <summary>
/// Item produk yang dipesan dalam order baru.
/// </summary>
/// <param name="ProductId">ID unik produk yang dipesan.</param>
/// <param name="Quantity">Jumlah kuantitas yang dipesan (minimal 1).</param>
public record CreateOrderItemRequest(
    Guid ProductId,
    int Quantity
);

/// <summary>
/// Data permintaan pembuatan order baru.
/// </summary>
/// <param name="CustomerId">Identitas atau ID pelanggan pembuat pesanan.</param>
/// <param name="ShippingAddress">Alamat tujuan pengiriman pesanan.</param>
/// <param name="Items">Daftar item produk yang dipesan.</param>
public record CreateOrderRequest(
    string CustomerId,
    string ShippingAddress,
    List<CreateOrderItemRequest> Items
);

/// <summary>
/// Permintaan pembaruan status pesanan.
/// </summary>
/// <param name="Status">Status baru pesanan (Pending, Processing, Completed, Cancelled).</param>
public record UpdateOrderStatusRequest(
    OrderStatus Status
);

/// <summary>
/// Informasi detail item dalam pesanan.
/// </summary>
public record OrderItemResponse(
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal Subtotal
);

/// <summary>
/// Informasi lengkap detail pesanan.
/// </summary>
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

/// <summary>
/// Informasi master produk dan ketersediaan stok.
/// </summary>
public record ProductResponse(
    Guid Id,
    string Name,
    int StockQuantity,
    decimal Price
);

/// <summary>
/// Data pembuatan master produk baru.
/// </summary>
public record CreateProductRequest(
    string Name,
    int StockQuantity,
    decimal Price
);

/// <summary>
/// Kriteria filter dan pagination untuk daftar order.
/// </summary>
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
