using Linteum.BlazorApp.LocalDTO;
using Linteum.Shared;
using Microsoft.JSInterop;

namespace Linteum.BlazorApp.Components.Pages;

public partial class CanvasPage
{
    private bool _isBrushEnabled;
    private bool _isEraserBrushEnabled;
    private bool _isTextSelectionPersistenceEnabled;
    private int _selectedEraserSize = 1;
    private string? _selectedBrushColorHex;
    private TextCaretPreviewState _textCaretPreview = TextCaretPreviewState.Hidden;

    private bool IsDragToolEnabled => _isBrushEnabled || _isEraserBrushEnabled;
    private bool ShowTextCaret => _canvas?.CanvasMode == CanvasMode.FreeDraw && _clickedPixel.HasValue && _textCaretPreview.IsVisible;

    private async Task HandleBrushToggledAsync(bool isEnabled)
    {
        var shouldResetStroke = !isEnabled || _isEraserBrushEnabled;
        _isBrushEnabled = isEnabled;
        if (isEnabled)
        {
            _isEraserBrushEnabled = false;
        }

        if (shouldResetStroke)
        {
            ResetBrushStroke();
        }

        await SyncBrushModeAsync();
        await InvokeAsync(StateHasChanged);
    }

    private async Task HandleEraserBrushToggledAsync(bool isEnabled)
    {
        var shouldResetStroke = !isEnabled || _isBrushEnabled;
        _isEraserBrushEnabled = isEnabled;
        if (isEnabled)
        {
            _isBrushEnabled = false;
        }

        if (shouldResetStroke)
        {
            ResetBrushStroke();
        }

        await SyncBrushModeAsync();
        await InvokeAsync(StateHasChanged);
    }

    private Task HandleSelectedEraserSizeChangedAsync(int size)
    {
        _selectedEraserSize = size;
        return SyncBrushPreviewAndStateAsync();
    }

    private Task HandleSelectedBrushColorHexChangedAsync(string? colorHex)
    {
        _selectedBrushColorHex = colorHex;
        return SyncBrushPreviewAndStateAsync();
    }

    private Task HandleTextCaretPreviewChanged(TextCaretPreviewState state)
    {
        _textCaretPreview = state;
        return SyncTextCaretPreviewAsync();
    }

    private Task HandleTextSelectionPersistenceChanged(bool isEnabled)
    {
        _isTextSelectionPersistenceEnabled = isEnabled;
        return SyncTextModeStateAsync();
    }

    private async Task SyncTextCaretPreviewAsync()
    {
        await SyncTextModeStateAsync();
        await InvokeAsync(StateHasChanged);
    }

    private async Task SyncTextModeStateAsync()
    {
        if (!_jsViewportInitialized)
        {
            return;
        }

        try
        {
            await JSRuntime.InvokeVoidAsync("canvasViewport.setSelectionPersistence", _isTextSelectionPersistenceEnabled);
        }
        catch (Exception ex)
        {
            _nlog.Warn(ex, "Failed to sync text selection persistence to canvas viewport");
        }
    }

    private async Task SyncBrushModeAsync()
    {
        if (!_jsViewportInitialized)
        {
            return;
        }

        try
        {
            await JSRuntime.InvokeVoidAsync("canvasViewport.setBrushEnabled", IsDragToolEnabled);
            await JSRuntime.InvokeVoidAsync("canvasViewport.setBrushPreview", _isEraserBrushEnabled, _selectedBrushColorHex, _selectedEraserSize);
        }
        catch (Exception ex)
        {
            _nlog.Warn(ex, "Failed to sync brush mode to canvas viewport");
        }
    }

    private async Task SyncBrushPreviewAndStateAsync()
    {
        await SyncBrushModeAsync();
        await InvokeAsync(StateHasChanged);
    }

    private void ResetBrushStroke()
    {
        lock (_brushStrokeLock)
        {
            _brushStrokeActive = false;
            _brushStrokePixels.Clear();
            _activeStrokeId = null;
            _strokeChunkSequence = 0;
            _strokeChunkStartedAtUtc = default;
        }
    }

    private string GetTextCaretStyle()
    {
        if (!_clickedPixel.HasValue || _canvas == null)
        {
            return string.Empty;
        }

        var x = _clickedPixel.Value.X + _textCaretPreview.Margin;
        var y = _clickedPixel.Value.Y + _textCaretPreview.Margin;
        var color = string.IsNullOrWhiteSpace(_textCaretPreview.ColorHex) ? "#1e4f9d" : _textCaretPreview.ColorHex;
        return string.Join(' ',
            $"left:calc({x} * 100% / {_canvas.Width});",
            $"top:calc({y} * 100% / {_canvas.Height});",
            $"width:max(1px, calc(100% / {_canvas.Width}));",
            $"height:max(1px, calc({Math.Max(1, _textCaretPreview.LineHeight)} * 100% / {_canvas.Height}));",
            $"background:{color};");
    }
}
