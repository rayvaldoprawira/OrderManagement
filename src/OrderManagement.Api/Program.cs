using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using OrderManagement.Api.Middleware;
using OrderManagement.Application.Interfaces;
using OrderManagement.Application.Validators;
using OrderManagement.Infrastructure.Data;
using OrderManagement.Infrastructure.Services;
using Serilog;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog with structured logging & CorrelationId enricher
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container
builder.Services.AddControllers();

// EF Core SQL Server
builder.Services.AddDbContext<OrderDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    options.UseSqlServer(connectionString, sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorNumbersToAdd: null);
    });
});

// FluentValidation
builder.Services.AddValidatorsFromAssemblyContaining<CreateOrderRequestValidator>();

// Application Services
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IIdempotencyService, IdempotencyService>();

// Swagger / OpenAPI documentation
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Order Management REST API",
        Version = "v1",
        Description = "### Sistem Order Management & Inventory\n\n" +
                      "Fitur Utama API:\n" +
                      "- **Pencegahan Double Order**: Mendukung idempotency via header `Idempotency-Key` (UUID) pada pembuatan order.\n" +
                      "- **Manajemen Stok Real-Time**: Validasi dan alokasi stok atomik untuk mencegah stok minus akibat concurrent orders.\n" +
                      "- **Optimistic Concurrency**: Pengendalian konflik update status order menggunakan Concurrency Token.\n" +
                      "- **Distributed Tracing**: Pelacakan setiap request dengan Correlation ID (`X-Correlation-Id`).\n\n" +
                      "*Semua endpoint mengembalikan struktur respons terstandar `ApiResponse<T>`.*"
    });

    // Masukkan XML comments dari OrderManagement.Api
    var apiXmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var apiXmlPath = Path.Combine(AppContext.BaseDirectory, apiXmlFile);
    if (File.Exists(apiXmlPath))
    {
        c.IncludeXmlComments(apiXmlPath, includeControllerXmlComments: true);
    }

    // Masukkan XML comments dari OrderManagement.Application
    var appXmlPath = Path.Combine(AppContext.BaseDirectory, "OrderManagement.Application.xml");
    if (File.Exists(appXmlPath))
    {
        c.IncludeXmlComments(appXmlPath);
    }
});

var app = builder.Build();

// Automated Database Migration & Seeding on Startup
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    var dbContext = services.GetRequiredService<OrderDbContext>();

    try
    {
        await DbInitializer.InitializeAsync(dbContext, logger);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to run database migrations or initial seeding.");
    }
}

// Global Exception & Correlation ID Middleware
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Configure HTTP pipeline - Swagger UI & OpenAPI
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Order Management API v1");
    c.RoutePrefix = "swagger"; // Standard Swagger UI URL: http://localhost:<port>/swagger
    c.DocumentTitle = "Order Management API Documentation";
    c.DisplayRequestDuration();
    c.EnableFilter();
    c.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
    c.DefaultModelsExpandDepth(1);
});

// Otomatis redirect dari root "/" ke "/swagger"
app.MapGet("/", () => Results.Redirect("/swagger"));

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }
