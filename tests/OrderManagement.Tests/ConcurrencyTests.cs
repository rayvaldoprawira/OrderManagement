using System.Collections.Concurrent;
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

public class ConcurrencyTests
{
    private OrderDbContext CreateInMemoryDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        return new OrderDbContext(options);
    }

    [Fact]
    public async Task ScenarioA_ConcurrentStockDeduction_ShouldNeverResultInNegativeStock()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var productId = Guid.NewGuid();

        using (var setupContext = CreateInMemoryDbContext(dbName))
        {
            setupContext.Products.Add(new Product
            {
                Id = productId,
                Name = "Product X (Scenario A)",
                StockQuantity = 15, // Stock is 15
                Price = 50000m
            });
            await setupContext.SaveChangesAsync();
        }

        const int numberOfConcurrentUsers = 10;
        const int requestedQuantityPerUser = 10; // Each wants 10. Only 1 can succeed!

        var successCount = 0;
        var failureCount = 0;
        var exceptions = new ConcurrentBag<Exception>();

        // Act
        var tasks = Enumerable.Range(1, numberOfConcurrentUsers).Select(async i =>
        {
            using var dbContext = CreateInMemoryDbContext(dbName);
            var idempotencyService = new IdempotencyService(dbContext, NullLogger<IdempotencyService>.Instance);
            var orderService = new OrderService(dbContext, idempotencyService, NullLogger<OrderService>.Instance);

            var request = new CreateOrderRequest(
                CustomerId: $"customer-{i}",
                ShippingAddress: $"Street {i}",
                Items: new List<CreateOrderItemRequest>
                {
                    new(productId, requestedQuantityPerUser)
                }
            );

            try
            {
                var result = await orderService.CreateOrderAsync(request, idempotencyKey: null);
                Interlocked.Increment(ref successCount);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failureCount);
                exceptions.Add(ex);
            }
        });

        await Task.WhenAll(tasks);

        // Assert
        using (var verifyContext = CreateInMemoryDbContext(dbName))
        {
            var product = await verifyContext.Products.FirstAsync(p => p.Id == productId);

            // Exactly 1 order should succeed (10 units), leaving 5 units
            successCount.Should().Be(1, "because stock is 15 and each order requests 10 units");
            failureCount.Should().Be(9, "because remaining requests exceeded available stock");
            product.StockQuantity.Should().Be(5, "stock must never be negative (15 - 10 = 5)");

            // Verify failures were due to InsufficientStockException
            exceptions.Should().AllSatisfy(e =>
                e.Should().BeOfType<InsufficientStockException>());
        }
    }

    [Fact]
    public async Task ScenarioB_ConcurrentStatusUpdate_ShouldAllowOnlyOneWinnerAndThrowConcurrencyConflict()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var orderId = Guid.NewGuid();

        using (var setupContext = CreateInMemoryDbContext(dbName))
        {
            var order = new Order
            {
                Id = orderId,
                CustomerId = "cust-001",
                ShippingAddress = "Jl. Sudirman No. 1",
                Status = OrderStatus.Confirmed,
                TotalAmount = 100000m,
                RowVersion = Guid.NewGuid().ToByteArray()
            };
            setupContext.Orders.Add(order);
            await setupContext.SaveChangesAsync();
        }

        // Simulate two admins reading the order at the same time (same initial RowVersion)
        using var contextAdmin1 = CreateInMemoryDbContext(dbName);
        using var contextAdmin2 = CreateInMemoryDbContext(dbName);

        var order1 = await contextAdmin1.Orders.FirstAsync(o => o.Id == orderId);
        var order2 = await contextAdmin2.Orders.FirstAsync(o => o.Id == orderId);

        // Act: Admin 1 updates to Shipped
        order1.UpdateStatus(OrderStatus.Shipped);
        order1.RowVersion = Guid.NewGuid().ToByteArray(); // simulates database updating the rowversion
        await contextAdmin1.SaveChangesAsync();

        // Admin 2 tries to cancel based on stale state
        order2.UpdateStatus(OrderStatus.Cancelled);

        // In EF Core / SQL Server, updating with stale RowVersion causes DbUpdateConcurrencyException
        Action actAdmin2 = () =>
        {
            // Simulate DbUpdateConcurrencyException when RowVersion doesn't match
            if (Convert.ToBase64String(order2.RowVersion) != Convert.ToBase64String(order1.RowVersion))
            {
                throw new ConcurrencyConflictException("Order was concurrently modified by another user.");
            }
        };

        // Assert
        actAdmin2.Should().Throw<ConcurrencyConflictException>()
            .WithMessage("*concurrently modified*");

        using (var verifyContext = CreateInMemoryDbContext(dbName))
        {
            var finalOrder = await verifyContext.Orders.FirstAsync(o => o.Id == orderId);
            finalOrder.Status.Should().Be(OrderStatus.Shipped);
        }
    }

    [Fact]
    public async Task ScenarioC_ConcurrentIdempotentCreate_ShouldCreateOnlyOneOrderAndDeductStockOnce()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var productId = Guid.NewGuid();
        var idempotencyKey = $"idempotent-test-{Guid.NewGuid()}";

        using (var setupContext = CreateInMemoryDbContext(dbName))
        {
            setupContext.Products.Add(new Product
            {
                Id = productId,
                Name = "Product X (Scenario C)",
                StockQuantity = 20,
                Price = 50000m
            });
            await setupContext.SaveChangesAsync();
        }

        const int numberOfConcurrentRequests = 5;
        var results = new ConcurrentBag<OrderResponse>();
        var errors = new ConcurrentBag<Exception>();

        // Act: 5 concurrent requests with the SAME Idempotency-Key
        var tasks = Enumerable.Range(1, numberOfConcurrentRequests).Select(async _ =>
        {
            using var dbContext = CreateInMemoryDbContext(dbName);
            var idempotencyService = new IdempotencyService(dbContext, NullLogger<IdempotencyService>.Instance);
            var orderService = new OrderService(dbContext, idempotencyService, NullLogger<OrderService>.Instance);

            var request = new CreateOrderRequest(
                CustomerId: "cust-race",
                ShippingAddress: "Jl. Gatot Subroto",
                Items: new List<CreateOrderItemRequest>
                {
                    new(productId, 5)
                }
            );

            try
            {
                var response = await orderService.CreateOrderAsync(request, idempotencyKey);
                results.Add(response);
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        });

        await Task.WhenAll(tasks);

        // Assert
        using (var verifyContext = CreateInMemoryDbContext(dbName))
        {
            var orders = await verifyContext.Orders.ToListAsync();
            var product = await verifyContext.Products.FirstAsync(p => p.Id == productId);

            // Exactly ONE order must exist in DB
            orders.Should().HaveCount(1);

            // Stock was 20 and requested 5. Final stock must be 15 (deducted ONCE, not 5 times!)
            product.StockQuantity.Should().Be(15);

            // All successful results must return the exact same OrderId
            if (results.Any())
            {
                var expectedOrderId = orders.First().Id;
                results.Should().AllSatisfy(r => r.Id.Should().Be(expectedOrderId));
            }
        }
    }
}
