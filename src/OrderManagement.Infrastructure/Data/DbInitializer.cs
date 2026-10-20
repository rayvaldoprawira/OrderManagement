using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderManagement.Domain.Entities;

namespace OrderManagement.Infrastructure.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(OrderDbContext context, ILogger logger, CancellationToken cancellationToken = default)
    {
        try
        {
            if (context.Database.IsSqlServer())
            {
                await context.Database.MigrateAsync(cancellationToken);
            }
            else
            {
                await context.Database.EnsureCreatedAsync(cancellationToken);
            }

            if (!await context.Products.AnyAsync(cancellationToken))
            {
                logger.LogInformation("Seeding initial products into database...");

                var products = new List<Product>
                {
                    new()
                    {
                        Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                        Name = "Product X (Concurrency Test Item)",
                        StockQuantity = 15,
                        Price = 100000m
                    },
                    new()
                    {
                        Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                        Name = "Laptop ThinkPad Workstation",
                        StockQuantity = 50,
                        Price = 18500000m
                    },
                    new()
                    {
                        Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                        Name = "Wireless Mechanical Keyboard",
                        StockQuantity = 100,
                        Price = 1250000m
                    },
                    new()
                    {
                        Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                        Name = "Ergonomic Office Chair",
                        StockQuantity = 5,
                        Price = 3200000m
                    }
                };

                await context.Products.AddRangeAsync(products, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Database seeded successfully with {Count} products.", products.Count);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding the database.");
            throw;
        }
    }
}
