using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.Application.DTOs;
using OrderManagement.Application.Interfaces;

namespace OrderManagement.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly IValidator<CreateOrderRequest> _createOrderValidator;
    private readonly IValidator<UpdateOrderStatusRequest> _updateStatusValidator;
    private readonly IValidator<OrderListQuery> _listQueryValidator;

    public OrdersController(
        IOrderService orderService,
        IValidator<CreateOrderRequest> createOrderValidator,
        IValidator<UpdateOrderStatusRequest> updateStatusValidator,
        IValidator<OrderListQuery> listQueryValidator)
    {
        _orderService = orderService;
        _createOrderValidator = createOrderValidator;
        _updateStatusValidator = updateStatusValidator;
        _listQueryValidator = listQueryValidator;
    }

    private string GetCorrelationId() =>
        HttpContext.Items["CorrelationId"]?.ToString() ?? HttpContext.TraceIdentifier;

    /// <summary>
    /// Membuat order baru. Mendukung idempotency via header 'Idempotency-Key' untuk mencegah double order.
    /// </summary>
    /// <param name="request">Payload pesanan yang mencakup CustomerId, ShippingAddress, dan daftar item produk.</param>
    /// <param name="idempotencyKey">UUID unik idempotency key pada header HTTP untuk mencegah double submit jika tombol diklik berulang kali.</param>
    /// <param name="cancellationToken">Cancellation token pembatalan request.</param>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<OrderResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateOrder(
        [FromBody] CreateOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        await _createOrderValidator.ValidateAndThrowAsync(request, cancellationToken);

        var order = await _orderService.CreateOrderAsync(request, idempotencyKey, cancellationToken);

        var response = ApiResponse<OrderResponse>.Ok(order, "Order created successfully", GetCorrelationId());
        return CreatedAtAction(nameof(GetOrderById), new { id = order.Id }, response);
    }

    /// <summary>
    /// Mengambil detail informasi order berdasarkan Order ID.
    /// </summary>
    /// <param name="id">ID unik (GUID) dari order.</param>
    /// <param name="cancellationToken">Cancellation token pembatalan request.</param>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<OrderResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrderById(Guid id, CancellationToken cancellationToken)
    {
        var order = await _orderService.GetOrderByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<OrderResponse>.Ok(order, "Order retrieved successfully", GetCorrelationId()));
    }

    /// <summary>
    /// Menampilkan daftar order dengan filter (customerId, status, rentang tanggal) dan pagination.
    /// </summary>
    /// <param name="query">Kriteria query filter dan pagination (PageNumber, PageSize).</param>
    /// <param name="cancellationToken">Cancellation token pembatalan request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<OrderResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListOrders([FromQuery] OrderListQuery query, CancellationToken cancellationToken)
    {
        await _listQueryValidator.ValidateAndThrowAsync(query, cancellationToken);

        var result = await _orderService.ListOrdersAsync(query, cancellationToken);
        return Ok(ApiResponse<PagedResult<OrderResponse>>.Ok(result, "Orders listed successfully", GetCorrelationId()));
    }

    /// <summary>
    /// Memperbarui status order dengan validasi transisi status (state machine) dan optimistic concurrency check.
    /// </summary>
    /// <param name="id">ID unik (GUID) dari order.</param>
    /// <param name="request">Status baru pesanan (Pending -> Processing -> Completed/Cancelled).</param>
    /// <param name="cancellationToken">Cancellation token pembatalan request.</param>
    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(ApiResponse<OrderResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateOrderStatus(
        Guid id,
        [FromBody] UpdateOrderStatusRequest request,
        CancellationToken cancellationToken)
    {
        await _updateStatusValidator.ValidateAndThrowAsync(request, cancellationToken);

        var order = await _orderService.UpdateOrderStatusAsync(id, request.Status, cancellationToken);
        return Ok(ApiResponse<OrderResponse>.Ok(order, "Order status updated successfully", GetCorrelationId()));
    }

    /// <summary>
    /// Membatalkan order dan secara otomatis mengembalikan stok barang ke inventory.
    /// </summary>
    /// <param name="id">ID unik (GUID) dari order yang ingin dibatalkan.</param>
    /// <param name="cancellationToken">Cancellation token pembatalan request.</param>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(typeof(ApiResponse<OrderResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelOrder(Guid id, CancellationToken cancellationToken)
    {
        var order = await _orderService.CancelOrderAsync(id, cancellationToken);
        return Ok(ApiResponse<OrderResponse>.Ok(order, "Order cancelled and stock restored successfully", GetCorrelationId()));
    }
}
