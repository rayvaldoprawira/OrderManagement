namespace OrderManagement.Domain.Exceptions;

public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message) { }
}

public class ProductNotFoundException : DomainException
{
    public Guid ProductId { get; }

    public ProductNotFoundException(Guid productId) 
        : base($"Product with ID '{productId}' was not found.")
    {
        ProductId = productId;
    }
}

public class OrderNotFoundException : DomainException
{
    public Guid OrderId { get; }

    public OrderNotFoundException(Guid orderId) 
        : base($"Order with ID '{orderId}' was not found.")
    {
        OrderId = orderId;
    }
}

public class InsufficientStockException : DomainException
{
    public Guid ProductId { get; }
    public string ProductName { get; }
    public int AvailableStock { get; }
    public int RequestedQuantity { get; }

    public InsufficientStockException(Guid productId, string productName, int availableStock, int requestedQuantity)
        : base($"Insufficient stock for product '{productName}' (ID: {productId}). Available: {availableStock}, Requested: {requestedQuantity}.")
    {
        ProductId = productId;
        ProductName = productName;
        AvailableStock = availableStock;
        RequestedQuantity = requestedQuantity;
    }
}

public class InvalidOrderStateTransitionException : DomainException
{
    public InvalidOrderStateTransitionException(string message) : base(message) { }
}

public class ConcurrencyConflictException : DomainException
{
    public ConcurrencyConflictException(string message) : base(message) { }
}

public class IdempotencyConflictException : DomainException
{
    public IdempotencyConflictException(string message) : base(message) { }
}
