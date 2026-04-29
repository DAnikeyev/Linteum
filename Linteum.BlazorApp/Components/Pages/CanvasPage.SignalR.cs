using Linteum.BlazorApp.Components.Notification;
using Linteum.Shared;
using Linteum.Shared.DTO;
using Microsoft.AspNetCore.SignalR.Client;

namespace Linteum.BlazorApp.Components.Pages;

public partial class CanvasPage
{
    protected override async Task OnInitializedAsync()
    {
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

            _hubConnection.Reconnected += async _ =>
            {
                if (!string.IsNullOrEmpty(_connectedGroup))
                {
                    await _hubConnection.InvokeAsync("JoinCanvasGroup", _connectedGroup);
                }
            };

            await _hubConnection.StartAsync();
        }
        catch (Exception ex)
        {
            _nlog.Error(ex, "Failed to start SignalR connection");
        }

        _brushFlushLoopTask = RunBrushFlushLoopAsync(_brushFlushLoopCts.Token);
        _eraseFlushLoopTask = RunEraseFlushLoopAsync(_eraseFlushLoopCts.Token);
        _confirmedPlaybackLoopTask = RunConfirmedPlaybackLoopAsync(_confirmedPlaybackLoopCts.Token);
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
}

