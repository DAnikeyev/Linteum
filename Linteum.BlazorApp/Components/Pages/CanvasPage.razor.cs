using System.Collections.Concurrent;
using System.Threading.Channels;
using Linteum.BlazorApp.Components.Layout;
using Linteum.BlazorApp.Components.Notification;
using Linteum.BlazorApp.LocalDTO;
using Linteum.BlazorApp.Services;
using Linteum.Shared;
using Linteum.Shared.DTO;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.JSInterop;
using NLog;

namespace Linteum.BlazorApp.Components.Pages;

public partial class CanvasPage : ComponentBase, IAsyncDisposable
{
    [Parameter]
    public string canvasName { get; set; } = default!;

    [CascadingParameter(Name = "SidebarMargin")]
    public string SidebarMargin { get; set; } = "0px";

    [CascadingParameter]
    public CanvasSidebar? Sidebar { get; set; }

    [Inject]
    private IConfiguration Configuration { get; set; } = default!;

    [Inject]
    private MyApiClient ApiClient { get; set; } = default!;

    [Inject]
    private NotificationService NotificationService { get; set; } = default!;

    [Inject]
    private IJSRuntime JSRuntime { get; set; } = default!;

    [Inject]
    private LocalStorageService LocalStorageService { get; set; } = default!;

    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private IHostEnvironment Environment { get; set; } = default!;

    [Inject]
    private Config DefaultConfig { get; set; } = default!;

    [Inject]
    private CanvasChatStateService CanvasChatState { get; set; } = default!;

    private static readonly Logger _nlog = LogManager.GetCurrentClassLogger();

    private CanvasDto? _canvas;
    private string? _loadedCanvasName;
    private (int X, int Y)? _clickedPixel;
    private PixelDto? _clickedPixelData;
    private long _gold;
    private Guid? _currentUserId;
    private string? _currentUserName;

    private HubConnection? _hubConnection;
    private string? _connectedGroup;
    private List<string> _onlineUsers = [];

    private ElementReference _canvasRef;
    private ElementReference _overlayRef;
    private CanvasRenderer? _renderer;

    private ElementReference _viewportHostRef;
    private ElementReference _viewportRef;
    private ElementReference _rendererRef;
    private ElementReference _coordsDisplayRef;

    private double ViewportWidth { get; set; } = 900;
    private double ViewportHeight { get; set; } = 600;

    private bool _hasRendered;
    private bool _canvasReady;
    private string _lastSidebarMargin = "0px";
    private bool _isMobileLayout;
    private List<ColorDto>? _colors;
    private TextCaretPreviewState _textCaretPreview = TextCaretPreviewState.Hidden;
    private DotNetObjectReference<CanvasPage>? _objRef;
    private string? _resizeListenerId;
    private int _canvasLoadVersion;
    private bool _jsViewportInitialized;
    private bool _pendingViewportRefresh = true;
    private bool _rendererInitializationInProgress;
    private int _disposeState;
    private bool _isBrushEnabled;
    private bool _isEraserBrushEnabled;
    private bool _isTextSelectionPersistenceEnabled;
    private int _selectedEraserSize = 1;
    private string? _selectedBrushColorHex;
    private CanvasMaintenanceProgressDto? _maintenanceProgress;
    private bool _suppressNextEraseNotification;
    private bool _suppressNextDeleteNotification;
    private bool _isHandlingCanvasErase;
    private bool _isHandlingCanvasDelete;
    private readonly HashSet<(int X, int Y)> _brushStrokePixels = [];
    private readonly List<(int X, int Y, int ColorId, string ColorHex)> _pendingBrushPixels = [];
    private readonly HashSet<(int X, int Y)> _pendingErasePixels = [];
    private readonly HashSet<(int X, int Y)> _clearedErasePixels = [];
    private readonly List<CoordinateDto> _pendingEraseCoordinates = [];
    private readonly object _brushStrokeLock = new();
    private readonly SemaphoreSlim _brushFlushSemaphore = new(1, 1);
    private readonly SemaphoreSlim _eraseFlushSemaphore = new(1, 1);
    private readonly CancellationTokenSource _brushFlushLoopCts = new();
    private readonly CancellationTokenSource _eraseFlushLoopCts = new();
    private readonly CancellationTokenSource _confirmedPlaybackLoopCts = new();
    private readonly Channel<ConfirmedPlaybackWorkItem> _confirmedPlaybackChannel = Channel.CreateUnbounded<ConfirmedPlaybackWorkItem>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });
    private readonly ConcurrentDictionary<string, byte> _pendingLocalPlaybackOperationIds = new(StringComparer.Ordinal);
    private bool _brushStrokeActive;
    private int _brushSelectionVersion;
    private Guid? _activeStrokeId;
    private int _strokeChunkSequence;
    private DateTime _strokeChunkStartedAtUtc;
    private int _pendingConfirmedPlaybackCount;
    private const int BrushBatchSize = 500;
    private const string ReceiveCanvasChatMessageEventName = "ReceiveCanvasChatMessage";
    private const string SessionExpiredEventName = "SessionExpired";
    private const string CanvasMaintenanceProgressEventName = "CanvasMaintenanceProgress";
    private const string ReceiveConfirmedPixelPlaybackBatchEventName = "ReceiveConfirmedPixelPlaybackBatch";
    private const string ReceiveConfirmedPixelDeletionPlaybackBatchEventName = "ReceiveConfirmedPixelDeletionPlaybackBatch";
    private static readonly TimeSpan BrushFlushInterval = TimeSpan.FromMilliseconds(75);
    private static readonly TimeSpan EraseFlushInterval = TimeSpan.FromMilliseconds(200);
    private Task? _brushFlushLoopTask;
    private Task? _eraseFlushLoopTask;
    private Task? _confirmedPlaybackLoopTask;

    private PixelManager? _pixelManager;
    private bool IsDragToolEnabled => _isBrushEnabled || _isEraserBrushEnabled;
    private bool ShowTextCaret => _canvas?.CanvasMode == CanvasMode.FreeDraw && _clickedPixel.HasValue && _textCaretPreview.IsVisible;
    private CanvasChatLobbyState CurrentChatState => CanvasChatState.GetState(_canvas?.Name ?? canvasName);
    private bool IsDisposed => Volatile.Read(ref _disposeState) != 0;

    private bool IsCurrentCanvasLoad(int loadVersion, CanvasDto? canvasSnapshot = null)
    {
        if (IsDisposed || loadVersion != Volatile.Read(ref _canvasLoadVersion))
        {
            return false;
        }

        return canvasSnapshot == null || ReferenceEquals(canvasSnapshot, _canvas);
    }

    private static bool IsExpectedUiShutdownException(Exception ex)
    {
        return ex is ObjectDisposedException or JSDisconnectedException or TaskCanceledException;
    }

    private async Task RequestRenderAsync()
    {
        if (IsDisposed)
        {
            return;
        }

        try
        {
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex) when (IsExpectedUiShutdownException(ex) || IsDisposed)
        {
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (IsDisposed)
        {
            return;
        }

        if (firstRender)
        {
            _hasRendered = true;
            _lastSidebarMargin = SidebarMargin;
            _pendingViewportRefresh = true;

            _objRef = DotNetObjectReference.Create(this);
            try
            {
                _resizeListenerId = await JSRuntime.InvokeAsync<string>("canvasHelpers.registerResizeListener", _objRef);
            }
            catch (Exception ex) when (IsExpectedUiShutdownException(ex))
            {
                return;
            }
        }

        var loadVersion = Volatile.Read(ref _canvasLoadVersion);
        var canvasSnapshot = _canvas;

        if (canvasSnapshot != null && _renderer == null && !_rendererInitializationInProgress)
        {
            _rendererInitializationInProgress = true;

            try
            {
                await CalculateViewportDimensions();
                if (!IsCurrentCanvasLoad(loadVersion, canvasSnapshot) || _renderer != null)
                {
                    return;
                }

                _pendingViewportRefresh = false;
                await RequestRenderAsync();
                if (!IsCurrentCanvasLoad(loadVersion, canvasSnapshot) || _renderer != null)
                {
                    return;
                }

                var renderer = new CanvasRenderer(JSRuntime);
                _renderer = renderer;
                await renderer.InitializeAsync(_canvasRef, _overlayRef);
                if (!IsCurrentCanvasLoad(loadVersion, canvasSnapshot) || !ReferenceEquals(_renderer, renderer))
                {
                    await renderer.DisposeAsync();
                    if (ReferenceEquals(_renderer, renderer))
                    {
                        _renderer = null;
                    }

                    return;
                }

                await LoadCanvasImage(loadVersion);
                if (!IsCurrentCanvasLoad(loadVersion, canvasSnapshot) || !ReferenceEquals(_renderer, renderer))
                {
                    return;
                }

                await InitJsViewport(loadVersion);
                if (!IsCurrentCanvasLoad(loadVersion, canvasSnapshot) || !ReferenceEquals(_renderer, renderer))
                {
                    return;
                }

                _pendingViewportRefresh = true;
                _canvasReady = true;
                await RequestRenderAsync();
                return;
            }
            catch (Exception ex) when (IsExpectedUiShutdownException(ex))
            {
                return;
            }
            finally
            {
                _rendererInitializationInProgress = false;
            }
        }

        if (_pendingViewportRefresh && canvasSnapshot != null && _jsViewportInitialized)
        {
            await CalculateViewportDimensions();
            if (!IsCurrentCanvasLoad(loadVersion, canvasSnapshot) || !_jsViewportInitialized)
            {
                return;
            }

            _pendingViewportRefresh = false;
            try
            {
                await JSRuntime.InvokeVoidAsync("canvasViewport.fitCanvas", ViewportWidth, ViewportHeight);
            }
            catch (Exception ex) when (IsExpectedUiShutdownException(ex))
            {
                return;
            }

            await RequestRenderAsync();
        }
    }

    private async Task InitJsViewport(int loadVersion)
    {
        var canvasSnapshot = _canvas;
        var objRefSnapshot = _objRef;
        if (canvasSnapshot == null || objRefSnapshot == null || !IsCurrentCanvasLoad(loadVersion, canvasSnapshot))
        {
            return;
        }

        await JSRuntime.InvokeVoidAsync(
            "canvasViewport.init",
            objRefSnapshot,
            _viewportRef,
            _rendererRef,
            _coordsDisplayRef,
            canvasSnapshot.Width,
            canvasSnapshot.Height,
            ViewportWidth,
            ViewportHeight);

        if (!IsCurrentCanvasLoad(loadVersion, canvasSnapshot))
        {
            return;
        }

        _jsViewportInitialized = true;
        await SyncBrushModeAsync();
        await SyncTextModeStateAsync();
    }

    [JSInvokable]
    public async Task OnWindowResize()
    {
        if (IsDisposed)
        {
            return;
        }

        await CalculateViewportDimensions();
        if (IsDisposed)
        {
            return;
        }

        if (_jsViewportInitialized)
        {
            try
            {
                await JSRuntime.InvokeVoidAsync("canvasViewport.fitCanvas", ViewportWidth, ViewportHeight);
            }
            catch (Exception ex) when (IsExpectedUiShutdownException(ex))
            {
                return;
            }
        }

        await RequestRenderAsync();
    }

    private async Task CalculateViewportDimensions()
    {
        try
        {
            var layoutMetrics = await JSRuntime.InvokeAsync<LayoutMetrics>("canvasHelpers.getLayoutMetrics");
            _isMobileLayout = layoutMetrics.WindowWidth <= 768;

            var hostSize = await JSRuntime.InvokeAsync<ElementSize>("canvasHelpers.getElementSize", _viewportHostRef);
            if (hostSize.Width > 0 && hostSize.Height > 0)
            {
                ViewportWidth = Math.Max(100, hostSize.Width);
                ViewportHeight = Math.Max(100, hostSize.Height);
                return;
            }

            const double safetyPadding = 2;
            if (_isMobileLayout)
            {
                const double mobileBottomSheetHeader = 56;
                ViewportWidth = Math.Max(100, layoutMetrics.WindowWidth - layoutMetrics.MainHPad - safetyPadding);
                ViewportHeight = Math.Max(100, layoutMetrics.WindowHeight - layoutMetrics.MainVPad - mobileBottomSheetHeader - safetyPadding);
            }
            else
            {
                ViewportWidth = Math.Max(100, layoutMetrics.WindowWidth - layoutMetrics.PixelManagerWidth - layoutMetrics.MainHPad - safetyPadding);
                ViewportHeight = Math.Max(100, layoutMetrics.WindowHeight - layoutMetrics.MainVPad - safetyPadding);
            }
        }
        catch (Exception ex) when (IsExpectedUiShutdownException(ex))
        {
        }
        catch (Exception ex)
        {
            _nlog.Warn(ex, "Failed to calculate viewport dimensions");
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        var requestedCanvasName = canvasName;
        var loadVersion = Interlocked.Increment(ref _canvasLoadVersion);

        lock (_brushStrokeLock)
        {
            _brushStrokeActive = false;
            _brushStrokePixels.Clear();
            _pendingBrushPixels.Clear();
            _pendingErasePixels.Clear();
            _clearedErasePixels.Clear();
            _pendingEraseCoordinates.Clear();
            _activeStrokeId = null;
            _strokeChunkSequence = 0;
            _strokeChunkStartedAtUtc = default;
        }

        _pendingLocalPlaybackOperationIds.Clear();

        if (_hasRendered && _lastSidebarMargin != SidebarMargin)
        {
            _lastSidebarMargin = SidebarMargin;
            _pendingViewportRefresh = true;
        }

        if (_hubConnection?.State == HubConnectionState.Connected && _connectedGroup != requestedCanvasName)
        {
            if (!string.IsNullOrEmpty(_connectedGroup))
            {
                await _hubConnection.InvokeAsync("LeaveCanvasGroup", _connectedGroup);
            }

            await _hubConnection.InvokeAsync("JoinCanvasGroup", requestedCanvasName);
            _connectedGroup = requestedCanvasName;
        }

        if (_loadedCanvasName == requestedCanvasName && _canvas != null)
        {
            return;
        }

        _canvasReady = false;
        _canvas = null;
        _loadedCanvasName = requestedCanvasName;
        _pendingViewportRefresh = true;
        _rendererInitializationInProgress = false;
        _maintenanceProgress = null;
        _textCaretPreview = TextCaretPreviewState.Hidden;

        if (_renderer != null)
        {
            await _renderer.DisposeAsync();
            _renderer = null;
        }

        _jsViewportInitialized = false;

        try
        {
            _colors ??= await ApiClient.GetColorsAsync();
            if (loadVersion != _canvasLoadVersion || requestedCanvasName != canvasName)
            {
                return;
            }

            var loadedCanvas = await ApiClient.GetCanvas(requestedCanvasName);
            if (loadVersion != _canvasLoadVersion || requestedCanvasName != canvasName)
            {
                return;
            }

            _canvas = loadedCanvas;
            if (_canvas.CanvasMode != CanvasMode.FreeDraw)
            {
                _isBrushEnabled = false;
                _isEraserBrushEnabled = false;
            }

            await RefreshGoldAsync();
            await LoadCanvasImage(loadVersion);

            if (_hubConnection?.State == HubConnectionState.Connected && _connectedGroup != requestedCanvasName)
            {
                if (!string.IsNullOrEmpty(_connectedGroup))
                {
                    await _hubConnection.InvokeAsync("LeaveCanvasGroup", _connectedGroup);
                }

                await _hubConnection.InvokeAsync("JoinCanvasGroup", requestedCanvasName);
                _connectedGroup = requestedCanvasName;
            }

            _clickedPixel = null;
            _clickedPixelData = null;

            if (_hasRendered && _renderer != null)
            {
                await InitJsViewport(loadVersion);
            }
        }
        catch (Exception ex)
        {
            _nlog.Warn(ex, "Failed to load canvas {CanvasName}", canvasName);
        }
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
            var imageBytes = await ApiClient.GetCanvasImage(canvasSnapshot);
            if (!IsCurrentCanvasLoad(versionSnapshot, canvasSnapshot) || _renderer == null)
            {
                return;
            }

            await _renderer.LoadImageAsync(imageBytes);
        }
        catch (Exception ex) when (IsExpectedUiShutdownException(ex) || !IsCurrentCanvasLoad(versionSnapshot, canvasSnapshot))
        {
        }
        catch (Exception ex)
        {
            _nlog.Warn(ex, "Failed to load image for canvas {CanvasName}", canvasName);
        }
    }

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

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _canvasReady = false;
        _rendererInitializationInProgress = false;
        Interlocked.Increment(ref _canvasLoadVersion);
        _brushFlushLoopCts.Cancel();
        _eraseFlushLoopCts.Cancel();
        _confirmedPlaybackLoopCts.Cancel();
        _pendingLocalPlaybackOperationIds.Clear();

        try
        {
            await JSRuntime.InvokeVoidAsync("canvasViewport.dispose");
        }
        catch (Exception ex) when (IsExpectedUiShutdownException(ex))
        {
        }

        if (_resizeListenerId != null)
        {
            try
            {
                await JSRuntime.InvokeVoidAsync("canvasHelpers.unregisterResizeListener", _resizeListenerId);
            }
            catch (Exception ex) when (IsExpectedUiShutdownException(ex))
            {
            }

            _resizeListenerId = null;
        }

        _objRef?.Dispose();
        _objRef = null;

        if (_renderer != null)
        {
            await _renderer.DisposeAsync();
            _renderer = null;
        }

        if (_eraseFlushLoopTask != null)
        {
            try
            {
                await _eraseFlushLoopTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_brushFlushLoopTask != null)
        {
            try
            {
                await _brushFlushLoopTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_confirmedPlaybackLoopTask != null)
        {
            try
            {
                await _confirmedPlaybackLoopTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _brushFlushLoopCts.Dispose();
        _eraseFlushLoopCts.Dispose();
        _confirmedPlaybackLoopCts.Dispose();
        _brushFlushSemaphore.Dispose();
        _eraseFlushSemaphore.Dispose();

        if (_hubConnection != null)
        {
            await _hubConnection.DisposeAsync();
        }
    }

    private sealed class ConfirmedPlaybackWorkItem
    {
        public List<PixelDto> Pixels { get; init; } = [];
        public List<CoordinateDto> Coordinates { get; init; } = [];
        public bool IsDeletion { get; init; }
        public int DurationMs { get; init; }

        public static ConfirmedPlaybackWorkItem FromPixels(ConfirmedPixelPlaybackBatchDto playbackBatch) => new()
        {
            Pixels = playbackBatch.Pixels,
            DurationMs = playbackBatch.DurationMs,
        };

        public static ConfirmedPlaybackWorkItem FromDeletes(ConfirmedPixelDeletionPlaybackBatchDto playbackBatch) => new()
        {
            Coordinates = playbackBatch.Coordinates,
            IsDeletion = true,
            DurationMs = playbackBatch.DurationMs,
        };
    }

    private sealed record ElementSize(double Width, double Height);
    private sealed record LayoutMetrics(double WindowWidth, double WindowHeight, double MainHPad, double MainVPad, double PixelManagerWidth);
}



