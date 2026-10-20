using Microsoft.EntityFrameworkCore;
using OrderManagement.Domain.Entities;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Infrastructure.Data;

public class OrderDbContext : DbContext
{
    public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Product Configuration
        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Name)
                .IsRequired()
                .HasMaxLength(200);
            entity.Property(p => p.Price)
                .HasPrecision(18, 2);
            entity.Property(p => p.StockQuantity)
                .IsRequired();
            
            // Check constraint to ensure stock is NEVER negative in the database
            entity.ToTable(t => t.HasCheckConstraint("CK_Products_StockQuantity_NonNegative", "[StockQuantity] >= 0"));

            // Optimistic concurrency row version
            entity.Property(p => p.RowVersion)
                .IsRowVersion();
        });

        // Order Configuration
        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(o => o.Id);
            entity.Property(o => o.CustomerId)
                .IsRequired()
                .HasMaxLength(100);
            entity.Property(o => o.ShippingAddress)
                .IsRequired()
                .HasMaxLength(500);
            entity.Property(o => o.TotalAmount)
                .HasPrecision(18, 2);
            entity.Property(o => o.Status)
                .HasConversion<string>()
                .HasMaxLength(50)
                .IsRequired();

            // RowVersion for optimistic locking during concurrent status updates
            entity.Property(o => o.RowVersion)
                .IsRowVersion();

            entity.HasMany(o => o.Items)
                .WithOne(i => i.Order)
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(o => o.CustomerId);
            entity.HasIndex(o => o.Status);
            entity.HasIndex(o => o.CreatedAtUtc);
        });

        // OrderItem Configuration
        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.HasKey(i => i.Id);
            entity.Property(i => i.UnitPrice)
                .HasPrecision(18, 2);
            entity.Property(i => i.Quantity)
                .IsRequired();

            entity.ToTable(t => t.HasCheckConstraint("CK_OrderItems_Quantity_Positive", "[Quantity] > 0"));

            entity.HasOne(i => i.Product)
                .WithMany()
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // IdempotencyRecord Configuration
        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.HasKey(r => r.Key);
            entity.Property(r => r.Key)
                .HasMaxLength(150);
            entity.Property(r => r.RequestHash)
                .IsRequired()
                .HasMaxLength(128);
            entity.Property(r => r.Status)
                .IsRequired()
                .HasMaxLength(50);
        });
    }
}
