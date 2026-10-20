using System.Net;
using System.Text.Json;
using FluentValidation;
using OrderManagement.Application.DTOs;
using OrderManagement.Domain.Exceptions;

namespace OrderManagement.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var correlationId = context.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString("N");

        var (statusCode, message, errors) = exception switch
        {
            ValidationException valEx => (
                HttpStatusCode.BadRequest,
                "Request validation failed.",
                valEx.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}").ToList()
            ),
            ProductNotFoundException or OrderNotFoundException => (
                HttpStatusCode.NotFound,
                exception.Message,
                (List<string>?)null
            ),
            InsufficientStockException => (
                HttpStatusCode.UnprocessableEntity,
                exception.Message,
                (List<string>?)null
            ),
            InvalidOrderStateTransitionException => (
                HttpStatusCode.BadRequest,
                exception.Message,
                (List<string>?)null
            ),
            ConcurrencyConflictException or IdempotencyConflictException => (
                HttpStatusCode.Conflict,
                exception.Message,
                (List<string>?)null
            ),
            ArgumentException or InvalidOperationException => (
                HttpStatusCode.BadRequest,
                exception.Message,
                (List<string>?)null
            ),
            _ => (
                HttpStatusCode.InternalServerError,
                "An unexpected server error occurred.",
                (List<string>?)null
            )
        };

        if (statusCode == HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "[CorrelationId: {CorrelationId}] Unhandled exception: {Message}",
                correlationId, exception.Message);
        }
        else
        {
            _logger.LogWarning("[CorrelationId: {CorrelationId}] Domain/Client exception [{StatusCode}]: {Message}",
                correlationId, (int)statusCode, message);
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var response = ApiResponse<object>.Fail(message, errors, correlationId);
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

        await context.Response.WriteAsync(json);
    }
}
