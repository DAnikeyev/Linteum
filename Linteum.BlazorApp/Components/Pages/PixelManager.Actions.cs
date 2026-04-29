using Linteum.BlazorApp.Components.Notification;
using Linteum.Shared.DTO;
using Microsoft.AspNetCore.Components;

namespace Linteum.BlazorApp.Components.Pages;

public partial class PixelManager
{
    private async Task SelectColorAsync(ColorDto color)
    {
        _selectedColorId = color.Id;
        await NotifySelectedBrushColorChangedAsync();
    }

    private async Task SetActiveToolAsync(DrawingTool tool)
    {
        _activeTool = tool;
        CloseTextColorMenus();

        if (IsBrushEnabled && BrushToggleDisabled)
        {
            await SetBrushEnabledAsync(false);
        }

        if (IsEraserBrushEnabled && EraserToggleDisabled)
        {
            await SetEraserBrushEnabledAsync(false);
        }

        await NotifySelectedBrushColorChangedAsync();
        await NotifyTextCaretPreviewChangedAsync();
    }

    public (int ColorId, string ColorHex)? GetBrushPaintSettings()
    {
        if (BrushToggleDisabled || !IsBrushEnabled || SelectedColor == null)
        {
            return null;
        }

        return (SelectedColor.Id, SelectedColor.HexValue);
    }

    public Task<PixelBatchChangeResultDto> PaintBatchAsync(IReadOnlyCollection<CoordinateDto> coordinates, int colorId, long price = 0, string? masterPassword = null, StrokePlaybackMetadataDto? playback = null)
    {
        if (Canvas == null)
        {
            throw new InvalidOperationException("No canvas loaded.");
        }

        return ApiClient.PaintBatch(Canvas, coordinates, colorId, price, masterPassword, playback);
    }

    private Task ToggleBrushAsync() => SetBrushEnabledAsync(!IsBrushEnabled);

    private async Task SetBrushEnabledAsync(bool isEnabled)
    {
        if (IsBrushEnabled == isEnabled && (!isEnabled || !IsEraserBrushEnabled))
        {
            return;
        }

        if (isEnabled && IsEraserBrushEnabled)
        {
            IsEraserBrushEnabled = false;
            if (IsEraserBrushEnabledChanged.HasDelegate)
            {
                await IsEraserBrushEnabledChanged.InvokeAsync(false);
            }
        }

        IsBrushEnabled = isEnabled;
        if (IsBrushEnabledChanged.HasDelegate)
        {
            await IsBrushEnabledChanged.InvokeAsync(isEnabled);
        }

        await NotifySelectedBrushColorChangedAsync();
    }

    private Task ToggleEraserBrushAsync() => SetEraserBrushEnabledAsync(!IsEraserBrushEnabled);

    private async Task SetEraserBrushEnabledAsync(bool isEnabled)
    {
        if (IsEraserBrushEnabled == isEnabled && (!isEnabled || !IsBrushEnabled))
        {
            return;
        }

        if (isEnabled && IsBrushEnabled)
        {
            IsBrushEnabled = false;
            if (IsBrushEnabledChanged.HasDelegate)
            {
                await IsBrushEnabledChanged.InvokeAsync(false);
            }
        }

        IsEraserBrushEnabled = isEnabled;
        if (IsEraserBrushEnabledChanged.HasDelegate)
        {
            await IsEraserBrushEnabledChanged.InvokeAsync(isEnabled);
        }

        await NotifySelectedBrushColorChangedAsync();
    }

    private async Task SetSelectedEraserSizeAsync(int size)
    {
        if (SelectedEraserSize == size)
        {
            return;
        }

        SelectedEraserSize = size;
        if (SelectedEraserSizeChanged.HasDelegate)
        {
            await SelectedEraserSizeChanged.InvokeAsync(size);
        }
    }

    private string GetToolTabClass(DrawingTool tool) => _activeTool == tool ? "active" : string.Empty;

    private void ToggleTextForegroundMenu()
    {
        _isTextForegroundMenuOpen = !_isTextForegroundMenuOpen;
        if (_isTextForegroundMenuOpen)
        {
            _isTextBackgroundMenuOpen = false;
        }
    }

    private void ToggleTextBackgroundMenu()
    {
        _isTextBackgroundMenuOpen = !_isTextBackgroundMenuOpen;
        if (_isTextBackgroundMenuOpen)
        {
            _isTextForegroundMenuOpen = false;
        }
    }

    private void CloseTextColorMenus()
    {
        _isTextForegroundMenuOpen = false;
        _isTextBackgroundMenuOpen = false;
    }

    private async Task SelectTextForegroundAsync(int colorId)
    {
        _textForegroundColorId = colorId;
        _isTextForegroundMenuOpen = false;
        await NotifyTextCaretPreviewChangedAsync();
    }

    private void SelectTextBackground(int? colorId)
    {
        _textBackgroundColorValue = colorId?.ToString() ?? string.Empty;
        _isTextBackgroundMenuOpen = false;
    }

    private async Task OnTextFontSizeChanged(ChangeEventArgs args)
    {
        if (int.TryParse(args.Value?.ToString(), out var fontSize) && TextFontSizes.Contains(fontSize))
        {
            _selectedTextFontSize = fontSize;
            await NotifyTextCaretPreviewChangedAsync();
        }
    }

    private async Task PaintClick()
    {
        if (Canvas == null)
        {
            await NotifyAsync(new CustomNotification
            {
                Message = "No canvas loaded.",
                Type = NotificationType.Info,
            });
            return;
        }

        if (SelectedColor == null || !ClickedPixel.HasValue)
        {
            await NotifyAsync(new CustomNotification
            {
                Message = "No color selected or pixel clicked.",
                Type = NotificationType.Info,
            });
            return;
        }

        if (IsEconomyCanvas && !HasValidEconomyBid)
        {
            await NotifyAsync(new CustomNotification
            {
                Message = EconomyValidationMessage ?? "Invalid bid.",
                Type = NotificationType.Info,
            });
            return;
        }

        if (IsNormalCanvas && !HasRemainingNormalQuota)
        {
            await NotifyAsync(new CustomNotification
            {
                Message = "Normal mode allows up to 100 successful pixel changes per day on this canvas.",
                Type = NotificationType.Info,
            });
            return;
        }

        try
        {
            var price = IsEconomyCanvas && TryGetEconomyBid(out var bid) ? bid : 0;
            var newPixel = await ApiClient.Paint(ClickedPixel.Value, Canvas, SelectedColor.Id, price);
            CanvasRenderer?.EnqueuePixel(newPixel.X, newPixel.Y, SelectedColor.HexValue, suppressRipple: true);

            var message = IsEconomyCanvas
                ? $"Bid {newPixel.Price} gold for pixel at ({newPixel.X}, {newPixel.Y}) with color {SelectedColor.Name ?? SelectedColor.HexValue}."
                : $"Painted pixel at ({newPixel.X}, {newPixel.Y}) with color {SelectedColor.Name ?? SelectedColor.HexValue}.";

            await NotifyAsync(new CustomNotification
            {
                Message = message,
                Type = NotificationType.Success,
            });

            ClickedPixelData = newPixel;
            if (IsEconomyCanvas)
            {
                Gold = Math.Max(0, Gold - price);
                _economyBidText = (newPixel.Price + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                _economyBidPixel = ClickedPixel;
                if (OnEconomyBalanceChanged.HasDelegate)
                {
                    await OnEconomyBalanceChanged.InvokeAsync();
                }
            }
            else if (IsNormalCanvas)
            {
                await RefreshNormalModeQuotaAsync(force: true);
            }

            if (newPixel.Id.HasValue)
            {
                PrevPixelId = newPixel.Id.Value;
                _prevClickedPixel = ClickedPixel;
                _skipNextHistoryRefreshPixelId = newPixel.Id.Value;
                _pixelChangeHistory = await ApiClient.GetHistoryAsync(newPixel.Id.Value, useCache: false);
            }
        }
        catch (Exception ex)
        {
            _nlog.Error(ex, "Error painting pixel");
            await NotifyAsync(new CustomNotification
            {
                Message = $"Error painting pixel: {ex.Message}",
                Type = NotificationType.Error,
            });
        }
    }

    private async Task PaintTextClick()
    {
        if (Canvas == null)
        {
            await NotifyAsync(new CustomNotification
            {
                Message = "No canvas loaded.",
                Type = NotificationType.Info,
            });
            return;
        }

        if (!ClickedPixel.HasValue)
        {
            await NotifyAsync(new CustomNotification
            {
                Message = "Select a pixel to place the text caret.",
                Type = NotificationType.Info,
            });
            return;
        }

        if (SelectedTextForegroundColor == null)
        {
            await NotifyAsync(new CustomNotification
            {
                Message = "Select a foreground color for the text.",
                Type = NotificationType.Info,
            });
            return;
        }

        if (string.IsNullOrWhiteSpace(_textContent))
        {
            await NotifyAsync(new CustomNotification
            {
                Message = "Enter some text to draw.",
                Type = NotificationType.Info,
            });
            return;
        }

        try
        {
            await ApiClient.PaintTextAsync(
                ClickedPixel.Value,
                Canvas,
                _textContent,
                SelectedTextForegroundColor.Id,
                SelectedTextBackgroundColorId,
                _selectedTextFontSize);

            await NotifyAsync(new CustomNotification
            {
                Message = $"Queued text drawing at ({ClickedPixel.Value.X}, {ClickedPixel.Value.Y}) with font size {_selectedTextFontSize}.",
                Type = NotificationType.Success,
            });
        }
        catch (Exception ex)
        {
            _nlog.Error(ex, "Error queueing text drawing");
            await NotifyAsync(new CustomNotification
            {
                Message = $"Error queueing text drawing: {ex.Message}",
                Type = NotificationType.Error,
            });
        }
    }

    private void PromptEraseCanvas()
    {
        if (Canvas == null || !CanEraseCanvas || _isManagingCanvas)
        {
            return;
        }

        _pendingCanvasAction = CanvasManagementAction.Erase;
    }

    private void PromptDeleteCanvas()
    {
        if (Canvas == null || !CanDeleteCanvas || _isManagingCanvas)
        {
            return;
        }

        _pendingCanvasAction = CanvasManagementAction.Delete;
    }

    private void CancelCanvasAction()
    {
        if (_isManagingCanvas)
        {
            return;
        }

        _pendingCanvasAction = CanvasManagementAction.None;
    }

    private Task ConfirmCanvasActionAsync() => _pendingCanvasAction switch
    {
        CanvasManagementAction.Erase => EraseCanvasAsync(),
        CanvasManagementAction.Delete => DeleteCanvasAsync(),
        _ => Task.CompletedTask,
    };

    private async Task EraseCanvasAsync()
    {
        if (Canvas == null || !CanEraseCanvas || _isManagingCanvas || _pendingCanvasAction != CanvasManagementAction.Erase)
        {
            return;
        }

        _pendingCanvasAction = CanvasManagementAction.None;
        _isManagingCanvas = true;
        try
        {
            var result = await ApiClient.EraseCanvasAsync(Canvas.Name);
            if (result.Completed && OnCanvasErased.HasDelegate)
            {
                await OnCanvasErased.InvokeAsync();
            }

            await NotifyAsync(new CustomNotification
            {
                Message = result.Message ?? (result.Queued
                    ? $"Canvas {Canvas.Name} erase was queued."
                    : $"Canvas {Canvas.Name} was erased."),
                Type = result.Queued ? NotificationType.Info : NotificationType.Success,
            });
        }
        catch (Exception ex)
        {
            _nlog.Error(ex, "Error erasing canvas {CanvasName}", Canvas.Name);
            await NotifyAsync(new CustomNotification
            {
                Message = $"Error erasing canvas: {ex.Message}",
                Type = NotificationType.Error,
            });
        }
        finally
        {
            _isManagingCanvas = false;
        }
    }

    private async Task DeleteCanvasAsync()
    {
        if (Canvas == null || !CanDeleteCanvas || _isManagingCanvas || _pendingCanvasAction != CanvasManagementAction.Delete)
        {
            return;
        }

        _pendingCanvasAction = CanvasManagementAction.None;
        _isManagingCanvas = true;
        try
        {
            var result = await ApiClient.DeleteCanvasAsync(Canvas.Name);
            if (result.Completed && OnCanvasDeleted.HasDelegate)
            {
                await OnCanvasDeleted.InvokeAsync();
            }

            await NotifyAsync(new CustomNotification
            {
                Message = result.Message ?? (result.Queued
                    ? $"Canvas {Canvas.Name} deletion was queued."
                    : $"Canvas {Canvas.Name} was deleted."),
                Type = result.Queued ? NotificationType.Info : NotificationType.Success,
            });
        }
        catch (Exception ex)
        {
            _nlog.Error(ex, "Error deleting canvas {CanvasName}", Canvas.Name);
            await NotifyAsync(new CustomNotification
            {
                Message = $"Error deleting canvas: {ex.Message}",
                Type = NotificationType.Error,
            });
        }
        finally
        {
            _isManagingCanvas = false;
        }
    }
}



