using OrderManagement.Domain.Enums;
using OrderManagement.Domain.Exceptions;

namespace OrderManagement.Domain.Entities;

public class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CustomerId { get; set; } = string.Empty;
    public string ShippingAddress { get; set; } = string.Empty;
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public decimal TotalAmount { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    // Concurrency token for optimistic locking on status updates
    public byte[] RowVersion { get; set; } = [];

    public List<OrderItem> Items { get; set; } = new();

    public void UpdateStatus(OrderStatus newStatus)
    {
        if (Status == OrderStatus.Delivered || Status == OrderStatus.Cancelled)
        {
            throw new InvalidOrderStateTransitionException(
                $"Order '{Id}' is in terminal state '{Status}' and cannot be modified.");
        }

        var isValidTransition = (Status, newStatus) switch
        {
            (OrderStatus.Pending, OrderStatus.Confirmed) => true,
            (OrderStatus.Pending, OrderStatus.Cancelled) => true,
            (OrderStatus.Confirmed, OrderStatus.Shipped) => true,
            (OrderStatus.Confirmed, OrderStatus.Cancelled) => true,
            (OrderStatus.Shipped, OrderStatus.Delivered) => true,
            _ => false
        };

        if (!isValidTransition)
        {
            throw new InvalidOrderStateTransitionException(
                $"Invalid status transition from '{Status}' to '{newStatus}' for order '{Id}'.");
        }

        Status = newStatus;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Cancel()
    {
        if (Status != OrderStatus.Pending && Status != OrderStatus.Confirmed)
        {
            throw new InvalidOrderStateTransitionException(
                $"Order '{Id}' cannot be cancelled because its current status is '{Status}'. Only Pending or Confirmed orders can be cancelled.");
        }

        Status = OrderStatus.Cancelled;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void CalculateTotal()
    {
        TotalAmount = Items.Sum(i => i.Subtotal);
    }
}
