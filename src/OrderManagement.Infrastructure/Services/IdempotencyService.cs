using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderManagement.Application.DTOs;
using OrderManagement.Application.Interfaces;
using OrderManagement.Domain.Entities;
using OrderManagement.Domain.Exceptions;
using OrderManagement.Infrastructure.Data;

namespace OrderManagement.Infrastructure.Services;

public class IdempotencyService : IIdempotencyService
{
    private readonly OrderDbContext _dbContext;
    private readonly ILogger<IdempotencyService> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public IdempotencyService(OrderDbContext dbContext, ILogger<IdempotencyService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<(bool IsProcessed, OrderResponse? CachedResponse)> TryAcquireOrGetCachedAsync(
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Key == idempotencyKey, cancellationToken);

        if (existing != null)
        {
            if (existing.RequestHash != requestHash)
            {
                throw new IdempotencyConflictException(
                    $"Idempotency-Key '{idempotencyKey}' was previously used with a different request payload.");
            }

            if (existing.Status == "Completed" && !string.IsNullOrEmpty(existing.ResponseBody))
            {
                _logger.LogInformation("Returning cached idempotent response for key {Key}", idempotencyKey);
                var cached = JsonSerializer.Deserialize<OrderResponse>(existing.ResponseBody, JsonOptions);
                return (true, cached);
            }

            if (existing.Status == "Processing")
            {
                // Wait briefly in case concurrent request is just finishing
                for (int i = 0; i < 5; i++)
                {
                    await Task.Delay(200, cancellationToken);
                    var refreshed = await _dbContext.IdempotencyRecords
                        .AsNoTracking()
                        .FirstOrDefaultAsync(r => r.Key == idempotencyKey, cancellationToken);

                    if (refreshed?.Status == "Completed" && !string.IsNullOrEmpty(refreshed.ResponseBody))
                    {
                        var cached = JsonSerializer.Deserialize<OrderResponse>(refreshed.ResponseBody, JsonOptions);
                        return (true, cached);
                    }
                }

                throw new IdempotencyConflictException(
                    $"A request with Idempotency-Key '{idempotencyKey}' is currently being processed. Please wait and retry.");
            }
        }

        // Try to insert a new Processing record
        var record = new IdempotencyRecord
        {
            Key = idempotencyKey,
            RequestHash = requestHash,
            Status = "Processing",
            CreatedAtUtc = DateTime.UtcNow
        };

        try
        {
            _dbContext.IdempotencyRecords.Add(record);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return (false, null);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Concurrent race detected on Idempotency-Key {Key}", idempotencyKey);

            // Detach to keep DbContext clean
            _dbContext.Entry(record).State = EntityState.Detached;

            // Wait briefly to see if winning thread completed
            for (int i = 0; i < 5; i++)
            {
                await Task.Delay(200, cancellationToken);
                var winnerRecord = await _dbContext.IdempotencyRecords
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Key == idempotencyKey, cancellationToken);

                if (winnerRecord?.Status == "Completed" && !string.IsNullOrEmpty(winnerRecord.ResponseBody))
                {
                    var cached = JsonSerializer.Deserialize<OrderResponse>(winnerRecord.ResponseBody, JsonOptions);
                    return (true, cached);
                }
            }

            throw new IdempotencyConflictException(
                $"Concurrent request with Idempotency-Key '{idempotencyKey}' is already being processed.");
        }
    }

    public async Task SaveCompletedResponseAsync(
        string idempotencyKey,
        int statusCode,
        OrderResponse response,
        CancellationToken cancellationToken = default)
    {
        var record = await _dbContext.IdempotencyRecords
            .FirstOrDefaultAsync(r => r.Key == idempotencyKey, cancellationToken);

        if (record != null)
        {
            record.Status = "Completed";
            record.ResponseStatusCode = statusCode;
            record.ResponseBody = JsonSerializer.Serialize(response, JsonOptions);
            record.CompletedAtUtc = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Saved completed idempotent response for key {Key}", idempotencyKey);
        }
    }

    public async Task MarkFailedAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var record = await _dbContext.IdempotencyRecords
                .FirstOrDefaultAsync(r => r.Key == idempotencyKey, cancellationToken);

            if (record != null)
            {
                // Remove failed attempt so user can retry
                _dbContext.IdempotencyRecords.Remove(record);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean up idempotency record {Key}", idempotencyKey);
        }
    }
}
