using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OrderManagement.Application.DTOs;
using OrderManagement.Domain.Entities;
using OrderManagement.Domain.Enums;
using OrderManagement.Domain.Exceptions;
using OrderManagement.Infrastructure.Data;
using OrderManagement.Infrastructure.Services;

namespace OrderManagement.Tests;

public class OrderServiceTests
{
    private OrderDbContext CreateInMemoryDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        return new OrderDbContext(options);
    }

    [Fact]
    public async Task CreateOrder_WithValidData_ShouldSucceedAndDeductStock()
    {
        var dbName = Guid.NewGuid().ToString();
        var productId = Guid.NewGuid();

        using (var context = CreateInMemoryDbContext(dbName))
        {
            context.Products.Add(new Product
            {
                Id = productId,
                Name = "Test Monitor",
                StockQuantity = 20,
                Price = 2500000m
            });
            await context.SaveChangesAsync();
        }

        using (var context = CreateInMemoryDbContext(dbName))
        {
            var idempotencyService = new IdempotencyService(context, NullLogger<IdempotencyService>.Instance);
            var orderService = new OrderService(context, idempotencyService, NullLogger<OrderService>.Instance);

            var request = new CreateOrderRequest(
                CustomerId: "cust-1",
                ShippingAddress: "Jakarta Selatan",
                Items: new List<CreateOrderItemRequest>
                {
                    new(productId, 3)
                }
            );

            var response = await orderService.CreateOrderAsync(request, idempotencyKey: null);

            response.Should().NotBeNull();
            response.CustomerId.Should().Be("cust-1");
            response.Status.Should().Be("Pending");
            response.TotalAmount.Should().Be(7500000m);
            response.Items.Should().HaveCount(1);
            response.Items[0].Quantity.Should().Be(3);

            var product = await context.Products.FirstAsync(p => p.Id == productId);
            product.StockQuantity.Should().Be(17);
        }
    }

    [Fact]
    public async Task CreateOrder_WithInsufficientStock_ShouldThrowInsufficientStockException()
    {
        var dbName = Guid.NewGuid().ToString();
        var productId = Guid.NewGuid();

        using (var context = CreateInMemoryDbContext(dbName))
        {
            context.Products.Add(new Product
            {
                Id = productId,
                Name = "Limited Item",
                StockQuantity = 2,
                Price = 100000m
            });
            await context.SaveChangesAsync();
        }

        using (var context = CreateInMemoryDbContext(dbName))
        {
            var idempotencyService = new IdempotencyService(context, NullLogger<IdempotencyService>.Instance);
            var orderService = new OrderService(context, idempotencyService, NullLogger<OrderService>.Instance);

            var request = new CreateOrderRequest(
                CustomerId: "cust-2",
                ShippingAddress: "Bandung",
                Items: new List<CreateOrderItemRequest>
                {
                    new(productId, 5) // Requesting 5, available 2
                }
            );

            var act = () => orderService.CreateOrderAsync(request, idempotencyKey: null);

            await act.Should().ThrowAsync<InsufficientStockException>()
                .WithMessage("*Insufficient stock*");

            var product = await context.Products.FirstAsync(p => p.Id == productId);
            product.StockQuantity.Should().Be(2); // Stock remains unchanged
        }
    }

    [Fact]
    public async Task CancelOrder_ShouldRestoreInventoryStock()
    {
        var dbName = Guid.NewGuid().ToString();
        var productId = Guid.NewGuid();

        using (var context = CreateInMemoryDbContext(dbName))
        {
            context.Products.Add(new Product
            {
                Id = productId,
                Name = "Desk Lamp",
                StockQuantity = 10,
                Price = 150000m
            });
            await context.SaveChangesAsync();
        }

        using (var context = CreateInMemoryDbContext(dbName))
        {
            var idempotencyService = new IdempotencyService(context, NullLogger<IdempotencyService>.Instance);
            var orderService = new OrderService(context, idempotencyService, NullLogger<OrderService>.Instance);

            // Create order with 4 items
            var createRequest = new CreateOrderRequest(
                CustomerId: "cust-cancel",
                ShippingAddress: "Surabaya",
                Items: new List<CreateOrderItemRequest> { new(productId, 4) }
            );

            var created = await orderService.CreateOrderAsync(createRequest, idempotencyKey: null);

            var productAfterCreate = await context.Products.FirstAsync(p => p.Id == productId);
            productAfterCreate.StockQuantity.Should().Be(6); // 10 - 4 = 6

            // Now cancel the order
            var cancelled = await orderService.CancelOrderAsync(created.Id);
            cancelled.Status.Should().Be("Cancelled");

            // Verify stock is restored
            var productAfterCancel = await context.Products.FirstAsync(p => p.Id == productId);
            productAfterCancel.StockQuantity.Should().Be(10); // Restored to 10
        }
    }

    [Fact]
    public async Task ListOrders_WithFilters_ShouldReturnFilteredPagedResult()
    {
        var dbName = Guid.NewGuid().ToString();

        using (var context = CreateInMemoryDbContext(dbName))
        {
            context.Orders.AddRange(
                new Order { Id = Guid.NewGuid(), CustomerId = "cust-A", Status = OrderStatus.Pending },
                new Order { Id = Guid.NewGuid(), CustomerId = "cust-A", Status = OrderStatus.Confirmed },
                new Order { Id = Guid.NewGuid(), CustomerId = "cust-B", Status = OrderStatus.Pending }
            );
            await context.SaveChangesAsync();
        }

        using (var context = CreateInMemoryDbContext(dbName))
        {
            var idempotencyService = new IdempotencyService(context, NullLogger<IdempotencyService>.Instance);
            var orderService = new OrderService(context, idempotencyService, NullLogger<OrderService>.Instance);

            var query = new OrderListQuery(CustomerId: "cust-A", Status: OrderStatus.Pending);
            var result = await orderService.ListOrdersAsync(query);

            result.TotalCount.Should().Be(1);
            result.Items.Should().HaveCount(1);
            result.Items[0].CustomerId.Should().Be("cust-A");
            result.Items[0].Status.Should().Be("Pending");
        }
    }
}
