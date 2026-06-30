using Linteum.BlazorApp.Api;
using Linteum.BlazorApp.Components.Notification;
using Linteum.Shared;
using Linteum.Shared.DTO;
using Microsoft.AspNetCore.SignalR.Client;

namespace Linteum.BlazorApp.Components.Pages;

public partial class CanvasPage
{
    private static readonly TimeSpan CanvasInitializationTimeout = TimeSpan.FromSeconds(20);

    private bool _initTimedOut;
    private int _initSecondsRemaining;
    private bool _retryInProgress;
    private bool _interactiveReady;
    private bool _interactiveInitializationInProgress;
    private bool _pendingCanvasLoad = true;
    private CancellationTokenSource? _canvasInitializationTimeoutCts;

    /// <summary>
    /// True when the in-flight load no longer matches the canvas the page is showing — either the
    /// load version was superseded, the route parameter changed, or the component was disposed.
    /// </summary>
    private bool IsStaleLoad(int loadVersion, string requestedName)
        => loadVersion != _canvasLoadVersion || requestedName != canvasName || IsDisposed;

    private string GetCanvasImageUrl(string canvasNameValue)
    {
        var baseUrl = $"{NavigationManager.BaseUri.TrimEnd('/')}/_canvas-image/{Uri.EscapeDataString(canvasNameValue)}";
        return _canvasImageVersion > 0 ? $"{baseUrl}?v={_canvasImageVersion}" : baseUrl;
    }

    private void RestartCanvasInitializationTimeout(string requestedCanvasName, int loadVersion)
    {
        CancelCanvasInitializationTimeout();

        if (IsDisposed)
        {
            return;
        }

        var cancellationTokenSource = new CancellationTokenSource();
        _canvasInitializationTimeoutCts = cancellationTokenSource;
        _ = WatchCanvasInitializationTimeoutAsync(requestedCanvasName, loadVersion, cancellationTokenSource.Token);
    }

    private void CancelCanvasInitializationTimeout()
    {
        var cancellationTokenSource = Interlocked.Exchange(ref _canvasInitializationTimeoutCts, null);
        if (cancellationTokenSource == null)
        {
            return;
        }

        try
        {
            cancellationTokenSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        cancellationTokenSource.Dispose();
    }

    private async Task WatchCanvasInitializationTimeoutAsync(string requestedCanvasName, int loadVersion, CancellationToken cancellationToken)
    {
        var totalSeconds = (int)Math.Ceiling(CanvasInitializationTimeout.TotalSeconds);
        try
        {
            for (var remaining = totalSeconds; remaining > 0; remaining--)
            {
                _initSecondsRemaining = remaining;
                await RequestRenderAsync();

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                if (cancellationToken.IsCancellationRequested || IsDisposed)
                {
                    return;
                }

                if (loadVersion != Volatile.Read(ref _canvasLoadVersion)
                    || !string.Equals(requestedCanvasName, canvasName, StringComparison.Ordinal)
                    || _canvasReady)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (IsExpectedUiShutdownException(ex) || IsDisposed)
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested || IsDisposed)
        {
            return;
        }

        if (loadVersion != Volatile.Read(ref _canvasLoadVersion)
            || !string.Equals(requestedCanvasName, canvasName, StringComparison.Ordinal)
            || _canvasReady)
        {
            return;
        }

        // P-RT-04: do NOT force a full page reload (which discards pending strokes). Surface the
        // stall and offer a manual retry that rebuilds the connection / reloads the canvas.
        _nlog.Warn(
            "Canvas {CanvasName} did not initialize within {TimeoutSeconds} seconds. Showing retry UI.",
            requestedCanvasName,
            CanvasInitializationTimeout.TotalSeconds);

        _initTimedOut = true;
        await RequestRenderAsync();
    }

    private async Task RetryInitializationAsync()
    {
        if (_retryInProgress || IsDisposed)
        {
            return;
        }

        _retryInProgress = true;
        _initTimedOut = false;
        var loadVersion = Interlocked.Increment(ref _canvasLoadVersion);
        RestartCanvasInitializationTimeout(canvasName, loadVersion);

        try
        {
            if (_hubConnection != null)
            {
                try
                {
                    await _hubConnection.DisposeAsync();
                }
                catch (Exception ex) when (IsExpectedUiShutdownException(ex))
                {
                }

                _hubConnection = null;
            }

            _interactiveReady = false;
            _interactiveInitializationInProgress = false;
            _connectedGroup = null;
            _connectionStatus = HubConnectionStatus.Connecting;

            await EnsureInteractiveReadyAsync();
            if (IsDisposed)
            {
                return;
            }

            await RequestRenderAsync();

            if (_hubConnection?.State == HubConnectionState.Connected)
            {
                await LoadRequestedCanvasAsync(canvasName, loadVersion);
            }
        }
        catch (Exception ex) when (IsExpectedUiShutdownException(ex) || IsDisposed)
        {
        }
        catch (Exception ex)
        {
            _nlog.Warn(ex, "Manual canvas initialization retry failed for {CanvasName}", canvasName);
        }
        finally
        {
            _retryInProgress = false;
        }
    }

    private async Task LoadRequestedCanvasAsync(string requestedCanvasName, int loadVersion)
    {
        if (!_interactiveReady || IsDisposed)
        {
            _pendingCanvasLoad = true;
            return;
        }

        _pendingCanvasLoad = false;

        try
        {
            _colors ??= await ApiClient.GetColorsAsync();
            if (IsStaleLoad(loadVersion, requestedCanvasName))
            {
                return;
            }

            var loadedCanvas = await ApiClient.GetCanvas(requestedCanvasName);
            if (IsStaleLoad(loadVersion, requestedCanvasName))
            {
                return;
            }

            _canvas = loadedCanvas;
            _loadedCanvasName = requestedCanvasName;
            if (Sidebar != null)
            {
                await Sidebar.RefreshCanvases();
            }
            if (_canvas.CanvasMode != CanvasMode.FreeDraw)
            {
                _isBrushEnabled = false;
                _isEraserBrushEnabled = false;
            }

            await RefreshGoldAsync();
            await LoadCanvasImage(loadVersion);

            // Subscribe to live updates. EnsureJoinedCurrentCanvasAsync captures the server's
            // high-water event sequence at join time; TryReconcileCanvasAsync (triggered after the
            // image finishes loading) uses it to backfill any events that landed between the
            // snapshot and this subscription (P-RT-02 / load-gap fix).
            await EnsureJoinedCurrentCanvasAsync(requestedCanvasName);

            _clickedPixel = null;
            _clickedPixelData = null;

            if (_hasRendered && _renderer != null)
            {
                await InitJsViewport(loadVersion);
            }
        }
        catch (CanvasPasswordRequiredException)
        {
            // The canvas is password-protected and the caller isn't subscribed. Guests and anyone
            // opening it by link can't join here — notify and send them to the home canvas.
            await HandlePasswordProtectedCanvasAsync(requestedCanvasName, loadVersion);
        }
        catch (Exception ex)
        {
            if (IsCanvasNotFoundException(ex))
            {
                await HandleMissingCanvasAsync(requestedCanvasName, loadVersion);
                return;
            }

            _nlog.Warn(ex, "Failed to load canvas {CanvasName}", canvasName);
        }
    }

    private async Task HandlePasswordProtectedCanvasAsync(string requestedCanvasName, int loadVersion)
    {
        if (IsStaleLoad(loadVersion, requestedCanvasName))
        {
            return;
        }

        CancelCanvasInitializationTimeout();
        _canvasReady = true;
        _loadedCanvasName = null;
        await RequestRenderAsync();

        await NotificationService.NotifyAsync(new CustomNotification
        {
            Message = "This canvas is password-protected. Subscribe to it from the canvas list with its password to join.",
            Type = NotificationType.Error,
        });

        try
        {
            await Task.Delay(500);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (IsStaleLoad(loadVersion, requestedCanvasName))
        {
            return;
        }

        NavigationManager.NavigateTo(DefaultConfig.DefaultPage, replace: true);
    }

    private async Task HandleMissingCanvasAsync(string requestedCanvasName, int loadVersion)
    {
        if (IsStaleLoad(loadVersion, requestedCanvasName))
        {
            return;
        }

        CancelCanvasInitializationTimeout();
        _canvasReady = true;
        _loadedCanvasName = null;
        await RequestRenderAsync();

        await NotificationService.NotifyAsync(new CustomNotification
        {
            Message = "Canvas with this name does not exist. Returning to home",
            Type = NotificationType.Error,
        });

        try
        {
            await Task.Delay(500);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (IsStaleLoad(loadVersion, requestedCanvasName))
        {
            return;
        }

        NavigationManager.NavigateTo(DefaultConfig.DefaultPage, replace: true);
    }

    private async Task LoadCanvasImage(int? loadVersion = null)
    {
        if (_canvas == null || _renderer == null)
        {
            return;
        }

        var canvasSnapshot = _canvas;
        var versionSnapshot = loadVersion ?? _canvasLoadVersion;
        try
        {
            var imageUrl = GetCanvasImageUrl(canvasSnapshot.Name);
            if (!IsCurrentCanvasLoad(versionSnapshot, canvasSnapshot) || _renderer == null)
            {
                return;
            }

            await _renderer.LoadImageFromUrlAsync(imageUrl, _currentSessionId);
        }
        catch (Exception ex) when (IsExpectedUiShutdownException(ex) || !IsCurrentCanvasLoad(versionSnapshot, canvasSnapshot))
        {
        }
        catch (Exception ex)
        {
            _nlog.Warn(ex, "Failed to load image for canvas {CanvasName}", canvasName);
        }

        // Post-job (non-blocking): backfill any pixel changes that happened between this snapshot
        // and our live SignalR subscription, and undo any live update the freshly painted image may
        // have clobbered. Fire-and-forget so it never delays the visible load.
        ScheduleCanvasReconcile(canvasSnapshot.Name, versionSnapshot);
    }

    private void ScheduleCanvasReconcile(string canvasName, int loadVersion)
    {
        if (IsDisposed || loadVersion != Volatile.Read(ref _canvasLoadVersion))
        {
            return;
        }

        if (!IsCurrentCanvasByName(canvasName))
        {
            return;
        }

        if (_hubConnection?.State != HubConnectionState.Connected)
        {
            return;
        }

        if (!string.Equals(_connectedGroup, canvasName, StringComparison.Ordinal))
        {
            return;
        }

        _ = TryReconcileRecentCanvasAsync(canvasName, loadVersion);
    }
}
