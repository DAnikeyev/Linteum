using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Linteum.Api.Hubs;
using Linteum.Infrastructure;
using Linteum.Shared.DTO;
using Microsoft.AspNetCore.SignalR;

namespace Linteum.Api.Services;

public interface ICanvasMaintenanceQueue
{
    ValueTask<CanvasMaintenanceQueueResult> QueueEraseAsync(QueuedCanvasMaintenanceRequest request, CancellationToken cancellationToken = default);
    ValueTask<CanvasMaintenanceQueueResult> QueueDeleteAsync(QueuedCanvasMaintenanceRequest request, CancellationToken cancellationToken = default);
}

public sealed record QueuedCanvasMaintenanceRequest(
    Guid UserId,
    Guid CanvasId,
    string CanvasName,
    string RequestedBy,
    DateTime RequestedAtUtc);

public sealed record CanvasMaintenanceQueueResult(bool Queued, bool AlreadyQueued);

public class CanvasMaintenanceQueueService : BackgroundService, ICanvasMaintenanceQueue
{
    private readonly Channel<QueuedCanvasMaintenanceWorkItem> _queue = Channel.CreateUnbounded<QueuedCanvasMaintenanceWorkItem>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
    private readonly ConcurrentDictionary<string, byte> _queuedOperations = new(StringComparer.OrdinalIgnoreCase);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<CanvasHub> _hubContext;
    private readonly ILogger<CanvasMaintenanceQueueService> _logger;

    public CanvasMaintenanceQueueService(
        IServiceScopeFactory scopeFactory,
        IHubContext<CanvasHub> hubContext,
        ILogger<CanvasMaintenanceQueueService> logger)
    {
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _logger = logger;
    }

    public ValueTask<CanvasMaintenanceQueueResult> QueueEraseAsync(QueuedCanvasMaintenanceRequest request, CancellationToken cancellationToken = default) =>
        QueueAsync(CanvasMaintenanceOperation.Erase, request, cancellationToken);

    public ValueTask<CanvasMaintenanceQueueResult> QueueDeleteAsync(QueuedCanvasMaintenanceRequest request, CancellationToken cancellationToken = default) =>
        QueueAsync(CanvasMaintenanceOperation.Delete, request, cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync(stoppingToken))
            {
                while (_queue.Reader.TryRead(out var workItem))
                {
                    await ProcessWorkItemAsync(workItem, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async ValueTask<CanvasMaintenanceQueueResult> QueueAsync(CanvasMaintenanceOperation operation, QueuedCanvasMaintenanceRequest request, CancellationToken cancellationToken)
    {
        var operationKey = BuildOperationKey(operation, request.CanvasId);
        if (!_queuedOperations.TryAdd(operationKey, 0))
        {
            _logger.LogInformation(
                "Canvas {Operation} request already queued for {CanvasName} ({CanvasId}) by user {UserId}.",
                operation,
                request.CanvasName,
                request.CanvasId,
                request.UserId);
            await PublishProgressAsync(
                request,
                operation,
                status: "Queued",
                message: $"Canvas {request.CanvasName} {GetOperationVerb(operation)} is already queued.",
                CancellationToken.None);
            return new CanvasMaintenanceQueueResult(Queued: true, AlreadyQueued: true);
        }

        await _queue.Writer.WriteAsync(new QueuedCanvasMaintenanceWorkItem(operationKey, operation, request), cancellationToken);
        _logger.LogInformation(
            "Queued canvas {Operation} request for {CanvasName} ({CanvasId}) by user {UserId}. RequestedAtUtc={RequestedAtUtc}",
            operation,
            request.CanvasName,
            request.CanvasId,
            request.UserId,
            request.RequestedAtUtc);
        await PublishProgressAsync(
            request,
            operation,
            status: "Queued",
            message: $"Canvas {request.CanvasName} {GetOperationVerb(operation)} was queued and will run in the background.",
            CancellationToken.None);

        return new CanvasMaintenanceQueueResult(Queued: true, AlreadyQueued: false);
    }

    private async Task ProcessWorkItemAsync(QueuedCanvasMaintenanceWorkItem workItem, CancellationToken stoppingToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repoManager = scope.ServiceProvider.GetRequiredService<RepositoryManager>();

            _logger.LogInformation(
                "Starting queued canvas {Operation} for {CanvasName} ({CanvasId}). RequestedBy={RequestedBy}, UserId={UserId}, RequestedAtUtc={RequestedAtUtc}",
                workItem.Operation,
                workItem.Request.CanvasName,
                workItem.Request.CanvasId,
                workItem.Request.RequestedBy,
                workItem.Request.UserId,
                workItem.Request.RequestedAtUtc);
            await PublishProgressAsync(
                workItem.Request,
                workItem.Operation,
                status: "Running",
                message: workItem.Operation == CanvasMaintenanceOperation.Erase
                    ? $"Canvas {workItem.Request.CanvasName} erase is running. Deleting pixels directly in the database and letting history cleanup cascade."
                    : $"Canvas {workItem.Request.CanvasName} deletion is running. Removing the canvas and cascading related rows in the database.",
                stoppingToken);

            var succeeded = workItem.Operation switch
            {
                CanvasMaintenanceOperation.Erase => await repoManager.CanvasRepository.TryEraseCanvasByName(workItem.Request.CanvasName),
                CanvasMaintenanceOperation.Delete => await repoManager.CanvasRepository.TryDeleteCanvasByName(workItem.Request.CanvasName),
                _ => false,
            };

            stopwatch.Stop();
            if (!succeeded)
            {
                _logger.LogWarning(
                    "Queued canvas {Operation} failed for {CanvasName} ({CanvasId}) after {ElapsedMs} ms.",
                    workItem.Operation,
                    workItem.Request.CanvasName,
                    workItem.Request.CanvasId,
                    stopwatch.ElapsedMilliseconds);
                await PublishProgressAsync(
                    workItem.Request,
                    workItem.Operation,
                    status: "Failed",
                    message: $"Canvas {workItem.Request.CanvasName} {GetOperationVerb(workItem.Operation)} failed. Check server logs for details.",
                    stoppingToken);
                return;
            }

            _logger.LogInformation(
                "Queued canvas {Operation} finished successfully for {CanvasName} ({CanvasId}) in {ElapsedMs} ms.",
                workItem.Operation,
                workItem.Request.CanvasName,
                workItem.Request.CanvasId,
                stopwatch.ElapsedMilliseconds);
            await PublishProgressAsync(
                workItem.Request,
                workItem.Operation,
                status: "Completed",
                message: workItem.Operation == CanvasMaintenanceOperation.Erase
                    ? $"Canvas {workItem.Request.CanvasName} erase finished successfully."
                    : $"Canvas {workItem.Request.CanvasName} deletion finished successfully.",
                stoppingToken);

            try
            {
                var eventName = workItem.Operation == CanvasMaintenanceOperation.Erase
                    ? CanvasHub.CanvasErasedEventName
                    : CanvasHub.CanvasDeletedEventName;
                await _hubContext.Clients.Group(workItem.Request.CanvasName).SendAsync(eventName, workItem.Request.CanvasName, stoppingToken);
                _logger.LogInformation(
                    "Broadcasted canvas {Operation} completion for {CanvasName} ({CanvasId}).",
                    workItem.Operation,
                    workItem.Request.CanvasName,
                    workItem.Request.CanvasId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Canvas {Operation} completed for {CanvasName} ({CanvasId}) but completion broadcast failed.",
                    workItem.Operation,
                    workItem.Request.CanvasName,
                    workItem.Request.CanvasId);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(
                ex,
                "Unexpected error while processing queued canvas {Operation} for {CanvasName} ({CanvasId}) after {ElapsedMs} ms.",
                workItem.Operation,
                workItem.Request.CanvasName,
                workItem.Request.CanvasId,
                stopwatch.ElapsedMilliseconds);
            await PublishProgressAsync(
                workItem.Request,
                workItem.Operation,
                status: "Failed",
                message: $"Canvas {workItem.Request.CanvasName} {GetOperationVerb(workItem.Operation)} failed unexpectedly. Check server logs for details.",
                stoppingToken);
        }
        finally
        {
            _queuedOperations.TryRemove(workItem.OperationKey, out _);
        }
    }

    private async Task PublishProgressAsync(
        QueuedCanvasMaintenanceRequest request,
        CanvasMaintenanceOperation operation,
        string status,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await _hubContext.Clients.Group(request.CanvasName).SendAsync(
                CanvasHub.CanvasMaintenanceProgressEventName,
                new CanvasMaintenanceProgressDto
                {
                    CanvasName = request.CanvasName,
                    Operation = operation.ToString(),
                    Status = status,
                    Message = message,
                    UpdatedAtUtc = DateTime.UtcNow,
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to broadcast canvas maintenance progress for {CanvasName} ({CanvasId}). Operation={Operation}, Status={Status}",
                request.CanvasName,
                request.CanvasId,
                operation,
                status);
        }
    }

    private static string GetOperationVerb(CanvasMaintenanceOperation operation) =>
        operation == CanvasMaintenanceOperation.Erase ? "erase" : "deletion";

    private static string BuildOperationKey(CanvasMaintenanceOperation operation, Guid canvasId) =>
        $"{operation}:{canvasId:N}";

    private sealed record QueuedCanvasMaintenanceWorkItem(
        string OperationKey,
        CanvasMaintenanceOperation Operation,
        QueuedCanvasMaintenanceRequest Request);

    private enum CanvasMaintenanceOperation
    {
        Erase,
        Delete,
    }
}


