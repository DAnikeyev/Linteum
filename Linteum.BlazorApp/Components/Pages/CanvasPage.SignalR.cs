using Linteum.BlazorApp.Components.Notification;
using Linteum.Shared;
using Linteum.Shared.DTO;
using Microsoft.AspNetCore.SignalR.Client;

namespace Linteum.BlazorApp.Components.Pages;

public partial class CanvasPage
{
    protected override async Task OnInitializedAsync()
    {
        _brushFlushLoopTask = RunBrushFlushLoopAsync(_brushFlushLoopCts.Token);
        _eraseFlushLoopTask = RunEraseFlushLoopAsync(_eraseFlushLoopCts.Token);
        _confirmedPlaybackLoopTask = RunConfirmedPlaybackLoopAsync(_confirmedPlaybackLoopCts.Token);
        await Task.CompletedTask;
    }

    private async Task EnsureInteractiveReadyAsync()
    {
        if (_interactiveReady || _interactiveInitializationInProgress || IsDisposed)
        {
            return;
        }

        _interactiveInitializationInProgress = true;

        try
        {
            var apiBase = Configuration["ApiBaseUrl"];
            if (string.IsNullOrEmpty(apiBase))
            {
                _nlog.Warn("ApiBaseUrl missing from config, defaulting to /canvashub relative to page.");
                apiBase = NavigationManager.BaseUri;
            }

            var hubUrl = $"{apiBase.TrimEnd('/')}/canvashub";
            var sessionId = await LocalStorageService.GetItemAsync<string>(LocalStorageKey.SessionId);
            _currentSessionId = sessionId;
            _currentUserId = await LocalStorageService.GetItemAsync<Guid?>(LocalStorageKey.UserId);
            _currentUserName = await LocalStorageService.GetItemAsync<string>(LocalStorageKey.UserName);

            _hubConnection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    if (!string.IsNullOrWhiteSpace(sessionId))
                    {
                        options.Headers[CustomHeaders.SessionId] = sessionId;
                    }

                    if (Environment.IsDevelopment())
                    {
                        options.HttpMessageHandlerFactory = handler =>
                        {
                            if (handler is HttpClientHandler clientHandler)
                            {
                                clientHandler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
                            }

                            return handler;
                        };
                        options.WebSocketConfiguration = socketOptions =>
                        {
                            socketOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
                        };
                    }
                })
                .WithAutomaticReconnect()
                .Build();

            _nlog.Info("SignalR HubConnection built for {HubUrl}, starting connection...", hubUrl);

            _hubConnection.On<PixelDto>("ReceivePixelUpdate", pixel =>
                InvokeAsync(() => HandlePixelUpdatedAsync(pixel)));

            _hubConnection.On<List<PixelDto>>("ReceivePixelBatchUpdate", pixels =>
                InvokeAsync(async () =>
                {
                    if (pixels.Count == 0)
                    {
                        return;
                    }

                    foreach (var pixel in pixels)
                    {
                        await HandlePixelUpdatedAsync(pixel);
                    }
                }));

            _hubConnection.On<ConfirmedPixelPlaybackBatchDto>(
                ReceiveConfirmedPixelPlaybackBatchEventName,
                playbackBatch => InvokeAsync(() => EnqueueConfirmedPixelPlaybackAsync(playbackBatch)));

            _hubConnection.On<ConfirmedPixelDeletionPlaybackBatchDto>(
                ReceiveConfirmedPixelDeletionPlaybackBatchEventName,
                playbackBatch => InvokeAsync(() => EnqueueConfirmedPixelDeletionPlaybackAsync(playbackBatch)));

            _hubConnection.On<List<string>>("UpdateOnlineUsers", users =>
            {
                _onlineUsers = users;
                return InvokeAsync(StateHasChanged);
            });

            _hubConnection.On<CanvasChatMessageDto>(
                ReceiveCanvasChatMessageEventName,
                chatMessage => InvokeAsync(() => HandleCanvasChatMessageAsync(chatMessage)));

            _hubConnection.On(SessionExpiredEventName, () =>
                InvokeAsync(HandleSessionExpiredAsync));

            _hubConnection.On<IReadOnlyCollection<CoordinateDto>>("PixelsDeleted", coordinates =>
                InvokeAsync(() => HandlePixelsDeletedAsync(coordinates)));

            _hubConnection.On<string>("CanvasErased", canvasNameFromHub =>
                InvokeAsync(() => OnCanvasErasedFromHubAsync(canvasNameFromHub)));

            _hubConnection.On<string>("CanvasDeleted", canvasNameFromHub =>
                InvokeAsync(() => OnCanvasDeletedFromHubAsync(canvasNameFromHub)));

            _hubConnection.On<CanvasMaintenanceProgressDto>(
                CanvasMaintenanceProgressEventName,
                progress => InvokeAsync(() => HandleCanvasMaintenanceProgressAsync(progress)));

            _hubConnection.On<IReadOnlyCollection<CanvasIncomeUpdateDto>>("ReceiveCanvasIncomeUpdates", updates =>
                InvokeAsync(() => HandleCanvasIncomeUpdatesAsync(updates)));

            _hubConnection.Reconnecting += error => InvokeAsync(() => OnHubReconnectingAsync(error));
            _hubConnection.Reconnected += connectionId => InvokeAsync(() => OnHubReconnectedAsync(connectionId));
            _hubConnection.Closed += error => InvokeAsync(() => OnHubClosedAsync(error));

            _connectionStatus = HubConnectionStatus.Connecting;
            await _hubConnection.StartAsync();
            _connectionStatus = HubConnectionStatus.Connected;
        }
        catch (Exception ex)
        {
            _connectionStatus = HubConnectionStatus.Disconnected;
            _nlog.Error(ex, "Failed to start SignalR connection");
        }
        finally
        {
            _interactiveReady = true;
            _interactiveInitializationInProgress = false;
        }
    }

    private async Task HandleSessionExpiredAsync()
    {
        _nlog.Info("Received session expiration notification from SignalR hub.");
        await LocalStorageService.ClearAsync();
        ApiClient.ClearSession();
        NavigationManager.NavigateTo("/login", true);
    }

    private Task HandleCanvasChatMessageAsync(CanvasChatMessageDto? chatMessage)
    {
        if (chatMessage == null || string.IsNullOrWhiteSpace(chatMessage.CanvasName) || string.IsNullOrWhiteSpace(chatMessage.Message))
        {
            return Task.CompletedTask;
        }

        CanvasChatState.AddMessage(chatMessage);
        return InvokeAsync(StateHasChanged);
    }

    private async Task<bool> SendCanvasChatMessageAsync(string message)
    {
        var currentCanvasName = _canvas?.Name ?? canvasName;
        if (string.IsNullOrWhiteSpace(currentCanvasName))
        {
            return false;
        }

        try
        {
            await ApiClient.SendCanvasChatMessageAsync(currentCanvasName, message);
            return true;
        }
        catch (Exception ex)
        {
            _nlog.Warn(ex, "Failed to send canvas chat message for canvas {CanvasName}", currentCanvasName);
            await NotifyAsync(new CustomNotification
            {
                Message = $"Error sending chat message: {ex.Message}",
                Type = NotificationType.Error,
            });
            return false;
        }
    }

    private async Task RefreshGoldAsync()
    {
        if (_canvas?.CanvasMode != CanvasMode.Economy)
        {
            _gold = 0;
            return;
        }

        try
        {
            _gold = await ApiClient.GetCurrentGoldAsync(_canvas.Id);
        }
        catch (Exception ex)
        {
            _nlog.Warn(ex, "Failed to refresh gold for canvas {CanvasName}", _canvas.Name);
            _gold = 0;
        }
    }

    private async Task HandleCanvasIncomeUpdatesAsync(IReadOnlyCollection<CanvasIncomeUpdateDto> updates)
    {
        if (_canvas?.CanvasMode != CanvasMode.Economy || string.IsNullOrWhiteSpace(_currentUserName))
        {
            return;
        }

        var currentUserUpdate = updates.FirstOrDefault(update =>
            string.Equals(update.UserName, _currentUserName, StringComparison.Ordinal));

        if (currentUserUpdate == null)
        {
            return;
        }

        _gold = currentUserUpdate.NewBalance;

        await NotifyAsync(new CustomNotification
        {
            Message = $"Received {currentUserUpdate.Amount} gold as income.",
            Type = NotificationType.Success,
        });

        await InvokeAsync(StateHasChanged);
    }

    private async Task HandleCanvasMaintenanceProgressAsync(CanvasMaintenanceProgressDto? progress)
    {
        if (progress == null)
        {
            return;
        }

        var currentCanvasName = _canvas?.Name ?? canvasName;
        if (!string.Equals(currentCanvasName, progress.CanvasName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _maintenanceProgress = progress;

        if (string.Equals(progress.Status, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            await NotifyAsync(new CustomNotification
            {
                Message = progress.Message ?? $"Canvas {progress.CanvasName} {progress.Operation.ToLowerInvariant()} failed.",
                Type = NotificationType.Error,
            });
        }

        await InvokeAsync(StateHasChanged);
    }

    private Task HandleCanvasErasedLocallyAsync()
    {
        _suppressNextEraseNotification = true;
        return HandleCanvasErasedAsync(showNotification: false);
    }

    private Task HandleCanvasDeletedLocallyAsync()
    {
        _suppressNextDeleteNotification = true;
        return HandleCanvasDeletedAsync(showNotification: false);
    }

    private Task OnCanvasErasedFromHubAsync(string erasedCanvasName)
    {
        var currentCanvasName = _canvas?.Name ?? canvasName;
        if (!string.Equals(currentCanvasName, erasedCanvasName, StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        var showNotification = !_suppressNextEraseNotification;
        _suppressNextEraseNotification = false;
        return HandleCanvasErasedAsync(showNotification);
    }

    private Task OnCanvasDeletedFromHubAsync(string deletedCanvasName)
    {
        var currentCanvasName = _canvas?.Name ?? canvasName;
        if (!string.Equals(currentCanvasName, deletedCanvasName, StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        var showNotification = !_suppressNextDeleteNotification;
        _suppressNextDeleteNotification = false;
        return HandleCanvasDeletedAsync(showNotification);
    }

    private async Task HandleCanvasErasedAsync(bool showNotification)
    {
        if (_canvas == null || _isHandlingCanvasErase)
        {
            return;
        }

        _isHandlingCanvasErase = true;
        try
        {
            lock (_brushStrokeLock)
            {
                _brushStrokeActive = false;
                _brushStrokePixels.Clear();
                _pendingBrushPixels.Clear();
                _pendingErasePixels.Clear();
                _clearedErasePixels.Clear();
                _pendingEraseCoordinates.Clear();
            }

            ApiClient.ClearCanvasCache(_canvas.Name);
            if (_clickedPixelData?.Id.HasValue == true)
            {
                ApiClient.InvalidateHistoryCache(_clickedPixelData.Id.Value);
            }

            _maintenanceProgress = null;
            _clickedPixelData = null;
            await LoadCanvasImage();

            if (showNotification)
            {
                await NotifyAsync(new CustomNotification
                {
                    Message = $"Canvas {_canvas.Name} was erased.",
                    Type = NotificationType.Info,
                });
            }

            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            _nlog.Warn(ex, "Failed to refresh erased canvas {CanvasName}", _canvas.Name);
        }
        finally
        {
            _isHandlingCanvasErase = false;
        }
    }

    private async Task HandleCanvasDeletedAsync(bool showNotification)
    {
        if (_canvas == null || _isHandlingCanvasDelete)
        {
            return;
        }

        _isHandlingCanvasDelete = true;
        var deletedCanvasName = _canvas.Name;

        try
        {
            lock (_brushStrokeLock)
            {
                _brushStrokeActive = false;
                _brushStrokePixels.Clear();
                _pendingBrushPixels.Clear();
                _pendingErasePixels.Clear();
                _clearedErasePixels.Clear();
                _pendingEraseCoordinates.Clear();
            }

            ApiClient.ClearCanvasCache(deletedCanvasName);
            if (_clickedPixelData?.Id.HasValue == true)
            {
                ApiClient.InvalidateHistoryCache(_clickedPixelData.Id.Value);
            }

            _maintenanceProgress = null;
            _clickedPixel = null;
            _clickedPixelData = null;
            _onlineUsers = [];

            if (Sidebar != null)
            {
                await Sidebar.RemoveCanvasAsync(deletedCanvasName);
                await Sidebar.RefreshCanvases();
            }

            if (_hubConnection?.State == HubConnectionState.Connected && string.Equals(_connectedGroup, deletedCanvasName, StringComparison.OrdinalIgnoreCase))
            {
                await _hubConnection.InvokeAsync("LeaveCanvasGroup", deletedCanvasName);
                _connectedGroup = null;
            }

            if (showNotification)
            {
                await NotifyAsync(new CustomNotification
                {
                    Message = $"Canvas {deletedCanvasName} was deleted by its creator.",
                    Type = NotificationType.Info,
                });
            }

            NavigationManager.NavigateTo(DefaultConfig.DefaultPage);
        }
        catch (Exception ex)
        {
            _nlog.Warn(ex, "Failed to handle deleted canvas {CanvasName}", deletedCanvasName);
        }
    }

    private async Task NotifyAsync(CustomNotification note)
    {
        try
        {
            await NotificationService.NotifyAsync(note);
        }
        catch (Exception ex)
        {
            _nlog.Warn(ex, "NotificationService.Writer.WriteAsync failed");
        }
    }

    private Task OnHubReconnectingAsync(Exception? error)
    {
        _connectionStatus = HubConnectionStatus.Reconnecting;
        _nlog.Info("SignalR connection reconnecting. {ErrorMessage}", error?.Message ?? "Unknown");
        return RequestRenderAsync();
    }

    private async Task OnHubReconnectedAsync(string? connectionId)
    {
        _connectionStatus = HubConnectionStatus.Connected;
        var currentName = _canvas?.Name ?? canvasName;
        _nlog.Info("SignalR reconnected ({ConnectionId}); rejoining canvas {CanvasName} and reconciling.", connectionId, currentName);

        // P-RT-02: a reconnect gets a new connection id, and the server dropped the previous
        // connection's group memberships on disconnect. Clear our cached group so we rejoin the
        // CURRENT canvas (not a stale _connectedGroup from before the disconnect).
        _connectedGroup = null;
        var subscriptionSeq = await EnsureJoinedCurrentCanvasAsync(currentName);
        if (IsDisposed)
        {
            return;
        }

        var loadVersion = Volatile.Read(ref _canvasLoadVersion);

        // If the server's sequence went backwards relative to what we last reconciled, the API
        // process restarted and the in-memory buffer reset. The buffer cannot cover the gap, so
        // reload the snapshot (which itself triggers a fresh reconcile afterwards).
        if (subscriptionSeq > 0 && _lastReconciledSeq > 0 && subscriptionSeq <= _lastReconciledSeq)
        {
            _nlog.Info("Canvas {CanvasName} event sequence reset (server restart?); reloading image.", currentName);
            await LoadCanvasImage();
            _lastReconciledSeq = subscriptionSeq;
            await RequestRenderAsync();
            return;
        }

        if (subscriptionSeq > 0)
        {
            // Reconcile from the last sequence we had applied up to now; covers the disconnect gap.
            _ = TryReconcileCanvasAsync(currentName, loadVersion, _lastReconciledSeq, allowImageReloadFallback: true);
        }

        await RequestRenderAsync();
    }

    private Task OnHubClosedAsync(Exception? error)
    {
        _connectionStatus = HubConnectionStatus.Disconnected;
        _nlog.Warn("SignalR connection closed and will not auto-reconnect. {ErrorMessage}", error?.Message ?? "Unknown");
        return RequestRenderAsync();
    }

    /// <summary>
    /// Makes sure the hub is subscribed to <paramref name="canvasName"/> (leaving a stale group
    /// first if any), and returns the high-water event sequence the server reported at join time.
    /// The sequence is captured server-side AFTER group membership is established, so any event
    /// with seq &lt;= the returned value either predates this join or was also delivered live, and
    /// any higher-seq event will arrive live — a clean, race-free reconcile boundary.
    /// </summary>
    private async Task<long> EnsureJoinedCurrentCanvasAsync(string canvasName)
    {
        if (_hubConnection?.State != HubConnectionState.Connected)
        {
            return 0;
        }

        if (string.Equals(_connectedGroup, canvasName, StringComparison.Ordinal))
        {
            return 0;
        }

        try
        {
            if (!string.IsNullOrEmpty(_connectedGroup))
            {
                await _hubConnection.InvokeAsync("LeaveCanvasGroup", _connectedGroup);
            }

            var subscriptionSeq = await _hubConnection.InvokeAsync<long>("JoinCanvasGroup", canvasName);
            _connectedGroup = canvasName;
            return subscriptionSeq;
        }
        catch (Exception ex) when (IsExpectedUiShutdownException(ex) || IsDisposed)
        {
            return 0;
        }
        catch (Exception ex)
        {
            _nlog.Warn(ex, "Failed to join canvas group {CanvasName}", canvasName);
            return 0;
        }
    }

    /// <summary>
    /// Non-blocking post-job that fetches the pixel-change events the client missed between its
    /// canvas snapshot and its live subscription (the load gap) — or, on reconnect, between the
    /// last sequence it applied and now — and replays them in sequence order. Pixel writes are
    /// last-writer-wins per coordinate, so re-applying an event the client already has is
    /// idempotent. If the buffer has already evicted the needed range (long disconnect / slow
    /// load), it falls back to a full image reload. Never delays the main canvas load: callers fire
    /// it with discard (<c>_ =</c>) after the snapshot is on screen.
    /// </summary>
    private async Task TryReconcileCanvasAsync(string canvasName, int loadVersion, long afterSeq, bool allowImageReloadFallback)
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

        if (_reconcileInProgress)
        {
            return;
        }

        _reconcileInProgress = true;
        var needsImageReload = false;
        try
        {
            List<CanvasChangeEntryDto>? entries;
            try
            {
                entries = await _hubConnection.InvokeAsync<List<CanvasChangeEntryDto>>(
                    "GetCanvasChanges", canvasName, afterSeq, long.MaxValue);
            }
            catch (Exception ex) when (IsExpectedUiShutdownException(ex) || IsDisposed)
            {
                return;
            }
            catch (Exception ex)
            {
                _nlog.Warn(ex, "Failed to fetch canvas changes for reconcile on {CanvasName}", canvasName);
                return;
            }

            if (IsDisposed || loadVersion != Volatile.Read(ref _canvasLoadVersion) || !IsCurrentCanvasByName(canvasName))
            {
                return;
            }

            if (entries == null || entries.Count == 0)
            {
                // Nothing changed in the window; advance our marker so a later reconnect reconcile
                // starts from the right place.
                if (afterSeq > _lastReconciledSeq)
                {
                    _lastReconciledSeq = afterSeq;
                }
                return;
            }

            // Gap: the buffer's oldest entry in range does not immediately follow our anchor, so the
            // buffer has already evicted the events we needed. We cannot reconstruct the gap from
            // the buffer -> fall back to a fresh snapshot.
            if (entries[0].Seq > afterSeq + 1)
            {
                if (allowImageReloadFallback)
                {
                    _nlog.Info("Reconcile gap for {CanvasName}: expected seq {Expected} but oldest available is {Oldest}. Reloading canvas image.", canvasName, afterSeq + 1, entries[0].Seq);
                    needsImageReload = true;
                }
                return;
            }

            var lastAppliedSeq = afterSeq;
            foreach (var entry in entries)
            {
                if (IsDisposed || loadVersion != Volatile.Read(ref _canvasLoadVersion) || !IsCurrentCanvasByName(canvasName))
                {
                    return;
                }

                await ApplyCanvasChangeEntryAsync(entry);
                lastAppliedSeq = entry.Seq;
            }

            _lastReconciledSeq = lastAppliedSeq;

            // Truncation: we hit the response cap, so there may be more events beyond what we
            // fetched. Reload the snapshot to guarantee a correct final state.
            if (entries.Count >= CanvasReconcileLimits.MaxEntriesPerResponse && allowImageReloadFallback)
            {
                _nlog.Info("Reconcile for {CanvasName} hit the response cap; reloading canvas image to be safe.", canvasName);
                needsImageReload = true;
            }
        }
        finally
        {
            _reconcileInProgress = false;
        }

        // Perform the fallback reload AFTER releasing the reconcile guard, so the reload's own
        // scheduled reconcile (recent path) is not blocked by this still-in-progress reconcile.
        if (needsImageReload && !IsDisposed && loadVersion == Volatile.Read(ref _canvasLoadVersion) && IsCurrentCanvasByName(canvasName))
        {
            await LoadCanvasImage();
        }
    }

    /// <summary>
    /// Load-gap post-job: fetches the most recent buffered events and re-applies them idempotently.
    /// A canvas snapshot is rendered only moments before this runs, so the newest buffered entries
    /// always cover the window between that snapshot and the live subscription (including any live
    /// update the freshly painted image may have clobbered). No sequence anchor is needed, so there
    /// is nothing to go stale and no reload loop is possible. Fire-and-forget; never blocks the load.
    /// </summary>
    private async Task TryReconcileRecentCanvasAsync(string canvasName, int loadVersion)
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

        if (_reconcileInProgress)
        {
            return;
        }

        _reconcileInProgress = true;
        try
        {
            List<CanvasChangeEntryDto>? entries;
            try
            {
                entries = await _hubConnection.InvokeAsync<List<CanvasChangeEntryDto>>(
                    "GetRecentCanvasChanges", canvasName, CanvasReconcileLimits.MaxEntriesPerResponse);
            }
            catch (Exception ex) when (IsExpectedUiShutdownException(ex) || IsDisposed)
            {
                return;
            }
            catch (Exception ex)
            {
                _nlog.Warn(ex, "Failed to fetch recent canvas changes for reconcile on {CanvasName}", canvasName);
                return;
            }

            if (IsDisposed || loadVersion != Volatile.Read(ref _canvasLoadVersion) || !IsCurrentCanvasByName(canvasName))
            {
                return;
            }

            if (entries == null || entries.Count == 0)
            {
                return;
            }

            var lastAppliedSeq = _lastReconciledSeq;
            foreach (var entry in entries)
            {
                if (IsDisposed || loadVersion != Volatile.Read(ref _canvasLoadVersion) || !IsCurrentCanvasByName(canvasName))
                {
                    return;
                }

                await ApplyCanvasChangeEntryAsync(entry);
                if (entry.Seq > lastAppliedSeq)
                {
                    lastAppliedSeq = entry.Seq;
                }
            }

            _lastReconciledSeq = lastAppliedSeq;
        }
        finally
        {
            _reconcileInProgress = false;
        }
    }

    private async Task ApplyCanvasChangeEntryAsync(CanvasChangeEntryDto entry)
    {
        if (entry.Pixels.Count > 0)
        {
            foreach (var pixel in entry.Pixels)
            {
                await HandlePixelUpdatedAsync(pixel, suppressRipple: true);
            }
        }

        if (entry.DeletedCoordinates.Count > 0)
        {
            await HandlePixelsDeletedAsync(entry.DeletedCoordinates);
        }
    }
}

