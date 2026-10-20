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
    /// Creates a new order. Supports idempotency via the 'Idempotency-Key' HTTP header.
    /// </summary>
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
    /// Retrieves order details by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<OrderResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrderById(Guid id, CancellationToken cancellationToken)
    {
        var order = await _orderService.GetOrderByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<OrderResponse>.Ok(order, "Order retrieved successfully", GetCorrelationId()));
    }

    /// <summary>
    /// Lists orders with filtering (customerId, status, date range) and pagination.
    /// </summary>
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
    /// Updates the order status with state-machine transition checks and optimistic concurrency validation.
    /// </summary>
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
    /// Cancels an order and automatically restores its inventory stock.
    /// </summary>
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
