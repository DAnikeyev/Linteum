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
    private string? _currentSessionId;
    private Guid? _currentUserId;
    private string? _currentUserName;

    private HubConnection? _hubConnection;
    private string? _connectedGroup;
    private List<string> _onlineUsers = [];

    // Realtime connection / reconcile state (P-RT-02/03/04 + load-gap fix).
    private HubConnectionStatus _connectionStatus = HubConnectionStatus.Connecting;
    private long _lastReconciledSeq;
    private bool _reconcileInProgress;

    private bool _hasRendered;
    private bool _canvasReady;
    private string _lastSidebarMargin = "0px";
    private List<ColorDto>? _colors;
    private DotNetObjectReference<CanvasPage>? _objRef;
    private string? _resizeListenerId;
    private int _canvasLoadVersion;
    private int _canvasImageVersion;
    private int _disposeState;
    private CanvasMaintenanceProgressDto? _maintenanceProgress;
    private bool _suppressNextEraseNotification;
    private bool _suppressNextDeleteNotification;
    private bool _isHandlingCanvasErase;
    private bool _isHandlingCanvasDelete;

    /// <summary>
    /// True from the moment a local erase/delete is initiated until the corresponding
    /// SignalR broadcast arrives. While set, all incoming pixel-update and deletion
    /// events are dropped to prevent stale pre-erase pixels from flashing on the
    /// optimistically-cleared canvas.
    /// </summary>
    private bool _canvasMutationPendingConfirmation;

    // Brush / eraser flush plumbing — owned here because the lifecycle methods
    // (OnParametersSetAsync / OnInitializedAsync / DisposeAsync) set up and tear
    // it down. The brush/eraser *behavior* lives in CanvasPage.Brush.cs.
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
    private CanvasChatLobbyState CurrentChatState => CanvasChatState.GetState(_canvas?.Name ?? canvasName);
    private bool IsDisposed => Volatile.Read(ref _disposeState) != 0;

    private enum HubConnectionStatus { Connecting, Connected, Reconnecting, Disconnected }

    private bool IsCurrentCanvasByName(string canvasNameValue)
    {
        if (IsDisposed)
        {
            return false;
        }

        var current = _canvas?.Name ?? canvasName;
        return string.Equals(current, canvasNameValue, StringComparison.Ordinal);
    }

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
        return ex is ObjectDisposedException or JSDisconnectedException or TaskCanceledException
            || ex is InvalidOperationException invalidOperationException
                && invalidOperationException.Message.Contains("statically rendered", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCanvasNotFoundException(Exception ex)
    {
        return ex.Message.Contains("is not found", StringComparison.OrdinalIgnoreCase);
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

        if (!_interactiveReady && !_interactiveInitializationInProgress)
        {
            await EnsureInteractiveReadyAsync();
            if (IsDisposed)
            {
                return;
            }

            if (_pendingCanvasLoad)
            {
                await LoadRequestedCanvasAsync(canvasName, Volatile.Read(ref _canvasLoadVersion));
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
                _initTimedOut = false;
                CancelCanvasInitializationTimeout();
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

        if (_loadedCanvasName == requestedCanvasName && _canvas != null)
        {
            return;
        }

        RestartCanvasInitializationTimeout(requestedCanvasName, loadVersion);

        _canvasReady = false;
        _canvas = null;
        _loadedCanvasName = null;
        _initTimedOut = false;
        _pendingViewportRefresh = true;
        _pendingCanvasLoad = true;
        _rendererInitializationInProgress = false;
        _maintenanceProgress = null;
        _textCaretPreview = TextCaretPreviewState.Hidden;

        if (_renderer != null)
        {
            await _renderer.DisposeAsync();
            _renderer = null;
        }

        _jsViewportInitialized = false;

        if (!_interactiveReady)
        {
            return;
        }

        await LoadRequestedCanvasAsync(requestedCanvasName, loadVersion);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _canvasReady = false;
        _rendererInitializationInProgress = false;
        CancelCanvasInitializationTimeout();
        Interlocked.Increment(ref _canvasLoadVersion);
        _brushFlushLoopCts.Cancel();
        _eraseFlushLoopCts.Cancel();
        _confirmedPlaybackLoopCts.Cancel();
        _pendingLocalPlaybackOperationIds.Clear();

        if (_jsViewportInitialized)
        {
            try
            {
                await JSRuntime.InvokeVoidAsync("canvasViewport.dispose");
            }
            catch (Exception ex) when (IsExpectedUiShutdownException(ex))
            {
            }
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
}
