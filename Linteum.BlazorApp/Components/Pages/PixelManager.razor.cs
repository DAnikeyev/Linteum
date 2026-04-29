using Linteum.BlazorApp.Components.Notification;
using Linteum.BlazorApp.LocalDTO;
using Linteum.BlazorApp.Services;
using Linteum.Shared;
using Linteum.Shared.DTO;
using Linteum.Shared.Helpers;
using Microsoft.AspNetCore.Components;
using NLog;
using System.Globalization;

namespace Linteum.BlazorApp.Components.Pages;

public partial class PixelManager : ComponentBase
{
    [Parameter]
    public CanvasDto? Canvas { get; set; }

    [Parameter]
    public (int X, int Y)? ClickedPixel { get; set; }

    [Parameter]
    public CanvasRenderer? CanvasRenderer { get; set; }

    [Parameter]
    public PixelDto? ClickedPixelData { get; set; }

    [Parameter]
    public long Gold { get; set; }

    [Parameter]
    public EventCallback OnEconomyBalanceChanged { get; set; }

    [Parameter]
    public List<string> OnlineUsers { get; set; } = [];

    [Parameter]
    public bool IsMobileLayout { get; set; }

    [Parameter]
    public EventCallback<TextCaretPreviewState> OnTextCaretPreviewChanged { get; set; }

    [Parameter]
    public EventCallback<bool> OnTextSelectionPersistenceChanged { get; set; }

    [Parameter]
    public bool IsBrushEnabled { get; set; }

    [Parameter]
    public EventCallback<bool> IsBrushEnabledChanged { get; set; }

    [Parameter]
    public bool IsEraserBrushEnabled { get; set; }

    [Parameter]
    public EventCallback<bool> IsEraserBrushEnabledChanged { get; set; }

    [Parameter]
    public int SelectedEraserSize { get; set; } = 1;

    [Parameter]
    public EventCallback<int> SelectedEraserSizeChanged { get; set; }

    [Parameter]
    public EventCallback OnCanvasErased { get; set; }

    [Parameter]
    public EventCallback OnCanvasDeleted { get; set; }

    [Parameter]
    public EventCallback<string?> SelectedBrushColorHexChanged { get; set; }

    [Parameter]
    public CanvasMaintenanceProgressDto? MaintenanceProgress { get; set; }

    [Inject]
    private MyApiClient ApiClient { get; set; } = default!;

    [Inject]
    private NotificationService NotificationService { get; set; } = default!;

    private bool _isMobileExpanded = true;
    private static readonly Logger _nlog = LogManager.GetCurrentClassLogger();
    private List<ColorDto>? _colors;
    private List<HistoryResponseItem>? _pixelChangeHistory;
    private Guid? PrevPixelId;
    private (int X, int Y)? _prevClickedPixel;
    private Guid? _skipNextHistoryRefreshPixelId;
    private int? _selectedColorId;
    private int _textForegroundColorId;
    private string _textBackgroundColorValue = string.Empty;
    private string _textContent = string.Empty;
    private DrawingTool _activeTool = DrawingTool.Pixel;
    private int _selectedTextFontSize = DefaultTextFontSize;
    private bool _isTextForegroundMenuOpen;
    private bool _isTextBackgroundMenuOpen;
    private Guid? _currentUserId;
    private bool _isManagingCanvas;
    private CanvasManagementAction _pendingCanvasAction;
    private string _economyBidText = string.Empty;
    private (int X, int Y)? _economyBidPixel;
    private NormalModeQuotaDto? _normalModeQuota;
    private Guid? _normalModeQuotaCanvasId;
    private TextCaretPreviewState _lastTextCaretPreviewState = TextCaretPreviewState.Hidden;
    private bool _lastTextSelectionPersistenceEnabled;

    private ColorDto? SelectedColor => _colors?.FirstOrDefault(c => c.Id == _selectedColorId);
    private ColorDto? SelectedTextForegroundColor => _colors?.FirstOrDefault(c => c.Id == _textForegroundColorId);
    private int? SelectedTextBackgroundColorId => int.TryParse(_textBackgroundColorValue, out var colorId) ? colorId : null;
    private ColorDto? SelectedTextBackgroundColor => SelectedTextBackgroundColorId.HasValue
        ? _colors?.FirstOrDefault(c => c.Id == SelectedTextBackgroundColorId.Value)
        : null;
    private bool IsEconomyCanvas => Canvas?.CanvasMode == CanvasMode.Economy;
    private bool IsNormalCanvas => Canvas?.CanvasMode == CanvasMode.Normal;
    private bool IsFreeDrawCanvas => Canvas?.CanvasMode == CanvasMode.FreeDraw;
    private bool IsCreator => Canvas != null && _currentUserId.HasValue && Canvas.CreatorId == _currentUserId.Value;
    private bool CanEraseCanvas => Canvas != null && (IsCreator || IsFreeDrawCanvas);
    private bool CanDeleteCanvas => IsCreator;
    private bool IsTextToolActive => IsFreeDrawCanvas && _activeTool == DrawingTool.Text;
    private bool BrushToggleDisabled => Canvas == null || IsTextToolActive || !IsFreeDrawCanvas || SelectedColor == null;
    private bool EraserToggleDisabled => Canvas == null || !IsFreeDrawCanvas || IsTextToolActive;
    private long CurrentPixelPrice => IsEconomyCanvas ? ClickedPixelData?.Price ?? 0 : 0;
    private long MinimumBid => CurrentPixelPrice + 1;
    private string DrawingSectionTitle => IsTextToolActive ? "Text Drawing" : "Pixel Drawing";
    private string SelectedPixelSummary => ClickedPixel.HasValue
        ? $"Selected pixel: {ClickedPixel.Value.X}, {ClickedPixel.Value.Y}"
        : "No pixel selected";
    private string BrushToggleTitle => BrushToggleDisabled
        ? IsEconomyCanvas
            ? "Brush is unavailable on economy canvases."
            : IsNormalCanvas
                ? "Brush is available only on FreeDraw canvases."
                : IsTextToolActive
                    ? "Brush is unavailable while the text tool is active."
                    : "Select a color to paint."
        : "Hold the left mouse button on the canvas to paint while brush mode is active. Use the middle mouse button at any time to pan the canvas.";
    private string EraserToggleTitle => EraserToggleDisabled
        ? !IsFreeDrawCanvas
            ? "Eraser is available only on FreeDraw canvases."
            : "Eraser is unavailable while the text tool is active."
        : $"Hold the left mouse button on the canvas to erase with the {SelectedEraserSize} x {SelectedEraserSize} eraser. Use the middle mouse button at any time to pan the canvas.";
    private int NormalModeRemainingToday => _normalModeQuota?.RemainingToday ?? 0;
    private static readonly int[] EraserSizes = [1, 3, 7];
    private bool HasPendingCanvasAction => _pendingCanvasAction != CanvasManagementAction.None;
    private bool HasCanvasMaintenanceProgress => Canvas != null && MaintenanceProgress != null && string.Equals(Canvas.Name, MaintenanceProgress.CanvasName, StringComparison.OrdinalIgnoreCase);
    private bool HasActiveCanvasMaintenance => HasCanvasMaintenanceProgress && !IsMaintenanceTerminalStatus(MaintenanceProgress?.Status);
    private string PendingCanvasActionTitle => _pendingCanvasAction == CanvasManagementAction.Delete
        ? "Delete this canvas?"
        : "Erase this canvas?";
    private string PendingCanvasActionMessage => Canvas == null
        ? string.Empty
        : _pendingCanvasAction == CanvasManagementAction.Delete
            ? $"This permanently deletes '{Canvas.Name}', its pixels, history, and subscriptions."
            : $"This clears every pixel on '{Canvas.Name}' but keeps the canvas itself.";
    private string PendingCanvasActionButtonText => _pendingCanvasAction == CanvasManagementAction.Delete
        ? "Delete canvas"
        : "Erase canvas";
    private string PendingCanvasActionButtonClass => _pendingCanvasAction == CanvasManagementAction.Delete
        ? "pm-creator-delete-btn"
        : "pm-creator-erase-btn";
    private string MaintenanceStatusLabel => MaintenanceProgress?.Status ?? string.Empty;
    private string MaintenanceOperationLabel => string.IsNullOrWhiteSpace(MaintenanceProgress?.Operation)
        ? "Canvas maintenance"
        : $"{MaintenanceProgress!.Operation} canvas";
    private string MaintenanceMessage => MaintenanceProgress?.Message ?? string.Empty;
    private string MaintenanceUpdatedLabel => MaintenanceProgress?.UpdatedAtUtc.ToLocalTime().ToString("T") ?? string.Empty;
    private string MaintenanceBadgeClass => string.Equals(MaintenanceProgress?.Status, "Failed", StringComparison.OrdinalIgnoreCase)
        ? "pm-maintenance-badge-failed"
        : string.Equals(MaintenanceProgress?.Status, "Completed", StringComparison.OrdinalIgnoreCase)
            ? "pm-maintenance-badge-completed"
            : string.Equals(MaintenanceProgress?.Status, "Running", StringComparison.OrdinalIgnoreCase)
                ? "pm-maintenance-badge-running"
                : "pm-maintenance-badge-queued";
    private bool HasValidEconomyBid => !IsEconomyCanvas || (ClickedPixel.HasValue && TryGetEconomyBid(out var bid) && bid >= MinimumBid && bid <= Gold);
    private bool HasRemainingNormalQuota => !IsNormalCanvas || _normalModeQuota == null || _normalModeQuota.RemainingToday > 0;
    private bool PaintDisabled => SelectedColor == null || !ClickedPixel.HasValue || !HasValidEconomyBid || !HasRemainingNormalQuota;
    private bool PaintTextDisabled => SelectedTextForegroundColor == null || !ClickedPixel.HasValue || string.IsNullOrWhiteSpace(_textContent);
    private string PaintButtonText => IsEconomyCanvas ? "Place Bid" : "Paint";
    private const int DefaultTextFontSize = 16;
    private static readonly int[] TextFontSizes = [16, 24];

    private string? EconomyValidationMessage
    {
        get
        {
            if (!IsEconomyCanvas || !ClickedPixel.HasValue)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(_economyBidText) || !TryGetEconomyBid(out var bid))
            {
                return "Enter a valid whole-number bid.";
            }

            if (bid < MinimumBid)
            {
                return $"Bid must be at least {MinimumBid} gold.";
            }

            if (bid > Gold)
            {
                return "Not enough gold for this bid.";
            }

            return null;
        }
    }

    private void ToggleMobilePanel() => _isMobileExpanded = !_isMobileExpanded;

    protected override async Task OnInitializedAsync()
    {
        _currentUserId = await ApiClient.GetCurrentUserIdAsync();
        var colors = await ApiClient.GetColorsAsync();
        if (colors == null)
        {
            return;
        }

        _colors = ColorPaletteOrdering.SortByHue(colors);
        InitializeTextDefaults();
    }

    protected override async Task OnParametersSetAsync()
    {
        if (!_currentUserId.HasValue)
        {
            _currentUserId = await ApiClient.GetCurrentUserIdAsync();
        }

        if (Canvas == null
            || (_pendingCanvasAction == CanvasManagementAction.Erase && !CanEraseCanvas)
            || (_pendingCanvasAction == CanvasManagementAction.Delete && !CanDeleteCanvas))
        {
            _pendingCanvasAction = CanvasManagementAction.None;
        }

        if (!IsFreeDrawCanvas && _activeTool == DrawingTool.Text)
        {
            _activeTool = DrawingTool.Pixel;
            CloseTextColorMenus();
            await NotifyTextCaretPreviewChangedAsync();
        }

        await RefreshNormalModeQuotaAsync();

        if (IsBrushEnabled && BrushToggleDisabled)
        {
            await SetBrushEnabledAsync(false);
        }

        if (IsEraserBrushEnabled && EraserToggleDisabled)
        {
            await SetEraserBrushEnabledAsync(false);
        }

        SyncEconomyBid();
        await NotifyTextSelectionPersistenceChangedAsync();
        await NotifyTextCaretPreviewChangedAsync();

        if (ClickedPixelData == null || !ClickedPixel.HasValue)
        {
            _skipNextHistoryRefreshPixelId = null;
            PrevPixelId = null;
            _prevClickedPixel = null;
            _pixelChangeHistory = null;
            return;
        }

        if (ClickedPixelData.Id == null)
        {
            _skipNextHistoryRefreshPixelId = null;
            PrevPixelId = null;
            _prevClickedPixel = ClickedPixel;
            _pixelChangeHistory = null;
            return;
        }

        if (_skipNextHistoryRefreshPixelId.HasValue && _skipNextHistoryRefreshPixelId.Value != ClickedPixelData.Id.Value)
        {
            _skipNextHistoryRefreshPixelId = null;
        }

        var samePixel = _prevClickedPixel.HasValue
            && _prevClickedPixel.Value.X == ClickedPixel.Value.X
            && _prevClickedPixel.Value.Y == ClickedPixel.Value.Y;

        if (samePixel && PrevPixelId.HasValue && PrevPixelId.Value == ClickedPixelData.Id.Value)
        {
            return;
        }

        _prevClickedPixel = ClickedPixel;
        PrevPixelId = ClickedPixelData.Id.Value;
        _pixelChangeHistory = await ApiClient.GetHistoryAsync(ClickedPixelData.Id.Value, useCache: true);
    }

    private async Task RefreshNormalModeQuotaAsync(bool force = false)
    {
        if (!IsNormalCanvas || Canvas == null)
        {
            _normalModeQuota = null;
            _normalModeQuotaCanvasId = null;
            return;
        }

        if (!force && _normalModeQuotaCanvasId == Canvas.Id && _normalModeQuota != null)
        {
            return;
        }

        try
        {
            _normalModeQuota = await ApiClient.GetNormalModeQuotaAsync(Canvas.Name);
            _normalModeQuotaCanvasId = Canvas.Id;
        }
        catch (Exception ex)
        {
            _normalModeQuota = null;
            _normalModeQuotaCanvasId = Canvas.Id;
            _nlog.Warn(ex, "Error loading Normal mode quota for canvas {CanvasName}", Canvas.Name);
        }
    }

    private void SyncEconomyBid()
    {
        if (!IsEconomyCanvas || !ClickedPixel.HasValue)
        {
            _economyBidText = string.Empty;
            _economyBidPixel = null;
            return;
        }

        var selectedPixelChanged = !_economyBidPixel.HasValue
            || _economyBidPixel.Value.X != ClickedPixel.Value.X
            || _economyBidPixel.Value.Y != ClickedPixel.Value.Y;

        if (selectedPixelChanged)
        {
            SetEconomyBidToMinimum();
            _economyBidPixel = ClickedPixel;
        }
    }

    public async Task UpdatePixelHistory()
    {
        if (ClickedPixelData?.Id == null)
        {
            return;
        }

        if (_skipNextHistoryRefreshPixelId.HasValue && _skipNextHistoryRefreshPixelId.Value == ClickedPixelData.Id.Value)
        {
            _skipNextHistoryRefreshPixelId = null;
            return;
        }

        _pixelChangeHistory = await ApiClient.GetHistoryAsync(ClickedPixelData.Id.Value, useCache: false);
        await InvokeAsync(StateHasChanged);
    }

    private void InitializeTextDefaults()
    {
        if (_colors == null || _colors.Count == 0)
        {
            return;
        }

        if (_textForegroundColorId == 0 || !_colors.Any(color => color.Id == _textForegroundColorId))
        {
            _textForegroundColorId = _colors.FirstOrDefault(color => string.Equals(color.Name, "Black", StringComparison.OrdinalIgnoreCase))?.Id
                ?? _colors[0].Id;
        }
    }

    private void SetEconomyBidToMinimum()
    {
        _economyBidText = MinimumBid.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private bool TryGetEconomyBid(out long bid)
    {
        return long.TryParse(_economyBidText, out bid);
    }

    private Task NotifySelectedBrushColorChangedAsync()
    {
        if (!SelectedBrushColorHexChanged.HasDelegate)
        {
            return Task.CompletedTask;
        }

        return SelectedBrushColorHexChanged.InvokeAsync(IsFreeDrawCanvas && _activeTool == DrawingTool.Pixel ? SelectedColor?.HexValue : null);
    }

    private Task NotifyTextCaretPreviewChangedAsync()
    {
        var previewState = CreateTextCaretPreviewState();
        if (previewState == _lastTextCaretPreviewState)
        {
            return Task.CompletedTask;
        }

        _lastTextCaretPreviewState = previewState;

        if (!OnTextCaretPreviewChanged.HasDelegate)
        {
            return Task.CompletedTask;
        }

        return OnTextCaretPreviewChanged.InvokeAsync(previewState);
    }

    private Task NotifyTextSelectionPersistenceChangedAsync()
    {
        var isSelectionPersistenceEnabled = IsTextToolActive;
        if (isSelectionPersistenceEnabled == _lastTextSelectionPersistenceEnabled)
        {
            return Task.CompletedTask;
        }

        _lastTextSelectionPersistenceEnabled = isSelectionPersistenceEnabled;

        if (!OnTextSelectionPersistenceChanged.HasDelegate)
        {
            return Task.CompletedTask;
        }

        return OnTextSelectionPersistenceChanged.InvokeAsync(isSelectionPersistenceEnabled);
    }

    private TextCaretPreviewState CreateTextCaretPreviewState()
    {
        if (!IsTextToolActive || !ClickedPixel.HasValue)
        {
            return TextCaretPreviewState.Hidden;
        }

        var metrics = TextConverter.GetPreviewMetrics(_selectedTextFontSize.ToString(CultureInfo.InvariantCulture));

        return new TextCaretPreviewState(
            true,
            (int)Math.Round(metrics.PixelFontSize),
            metrics.Margin,
            metrics.LineHeight,
            SelectedTextForegroundColor?.HexValue);
    }

    private static string GetColorDisplayName(ColorDto? color, string fallback)
    {
        return color?.Name ?? color?.HexValue ?? fallback;
    }

    private string GetTextBackgroundLabel() => SelectedTextBackgroundColor == null
        ? "Transparent"
        : GetColorDisplayName(SelectedTextBackgroundColor, "Transparent");

    private static string GetColorSwatchStyle(string? hexValue) => $"background:{hexValue ?? "transparent"};";

    private async Task NotifyAsync(CustomNotification note)
    {
        try
        {
            await NotificationService.NotifyAsync(note);
        }
        catch (Exception ex)
        {
            _nlog.Warn(ex, "NotificationService.Writer.WriteAsync failed in PixelManager");
        }
    }

    private enum CanvasManagementAction
    {
        None,
        Erase,
        Delete,
    }

    private enum DrawingTool
    {
        Pixel,
        Text,
    }

    private static bool IsMaintenanceTerminalStatus(string? status) =>
        string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase);
}

