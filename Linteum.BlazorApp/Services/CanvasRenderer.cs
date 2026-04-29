using System.Collections.Concurrent;
using Linteum.Shared.DTO;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Linteum.BlazorApp.Services;

public class CanvasRenderer : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private ElementReference _canvasElement;
    private ElementReference _overlayElement;
    private readonly ConcurrentQueue<PixelUpdate> _queue = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _renderSignal = new(0, 1);
    private Task? _renderLoopTask;
    private bool _initialized;
    private int _disposeState;
    private const int MaxQueuedUpdatesPerFrame = 4096;

    private bool IsDisposed => Volatile.Read(ref _disposeState) != 0;

    private bool IsShuttingDown(Exception ex) => IsDisposed || _cts.IsCancellationRequested || ex is ObjectDisposedException or JSDisconnectedException or TaskCanceledException;

    public CanvasRenderer(IJSRuntime js)
    {
        _js = js;
    }

    public async Task InitializeAsync(ElementReference canvasElement, ElementReference overlayElement)
    {
        if (IsDisposed)
        {
            return;
        }

        _canvasElement = canvasElement;
        _overlayElement = overlayElement;
        try
        {
            await _js.InvokeVoidAsync("canvasRenderer.init", _canvasElement, _overlayElement);
        }
        catch (Exception ex) when (IsShuttingDown(ex))
        {
            return;
        }

        if (IsDisposed)
        {
            return;
        }

        _initialized = true;

        _renderLoopTask ??= RenderLoop();
    }

    public async Task LoadImageAsync(byte[] imageBytes)
    {
        if (!_initialized || IsDisposed) return;

        try
        {
            await _js.InvokeVoidAsync("canvasRenderer.loadImage", imageBytes);
        }
        catch (Exception ex) when (IsShuttingDown(ex))
        {
        }
    }

    public async Task<List<CoordinateDto>> FilterNonWhiteCoordinatesAsync(IReadOnlyCollection<CoordinateDto> coordinates)
    {
        if (!_initialized || coordinates.Count == 0)
        {
            return coordinates.ToList();
        }

        try
        {
            return await _js.InvokeAsync<List<CoordinateDto>>("canvasRenderer.filterNonWhitePixels", coordinates);
        }
        catch
        {
            return coordinates.ToList();
        }
    }

    public void EnqueuePixel(int x, int y, string color, bool suppressRipple = false)
    {
        if (!_initialized || IsDisposed)
        {
            return;
        }

        _queue.Enqueue(new PixelUpdate(x, y, color, suppressRipple, false));
        SignalRender();
    }

    public void EnqueuePixelClear(int x, int y)
    {
        if (!_initialized || IsDisposed)
        {
            return;
        }

        _queue.Enqueue(new PixelUpdate(x, y, null, true, true));
        SignalRender();
    }

    private void SignalRender()
    {
        if (IsDisposed)
        {
            return;
        }

        if (_renderSignal.CurrentCount > 0)
        {
            return;
        }

        try
        {
            _renderSignal.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    private async Task RenderLoop()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                await _renderSignal.WaitAsync(_cts.Token);

                while (!_cts.IsCancellationRequested)
                {
                    var batch = DequeueBatch();
                    if (batch.Count == 0)
                    {
                        break;
                    }

                    try
                    {
                        await _js.InvokeVoidAsync("canvasRenderer.renderBatch", batch);
                    }
                    catch (Exception ex) when (IsShuttingDown(ex))
                    {
                        return;
                    }
                    catch (Exception)
                    {
                    }

                    if (_queue.IsEmpty)
                    {
                        break;
                    }

                    await Task.Yield();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private List<PixelUpdate> DequeueBatch()
    {
        var latestUpdates = new Dictionary<(int X, int Y), PixelUpdate>();
        var dequeuedCount = 0;

        while (dequeuedCount < MaxQueuedUpdatesPerFrame && _queue.TryDequeue(out var pixel))
        {
            latestUpdates[(pixel.X, pixel.Y)] = pixel;
            dequeuedCount++;
        }

        return latestUpdates.Values.ToList();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _cts.Cancel();
        if (_renderLoopTask != null)
        {
            try
            {
                await _renderLoopTask;
            }
            catch
            {
                
            }
        }
        _cts.Dispose();
        _renderSignal.Dispose();
    }
}

public record struct PixelUpdate(int X, int Y, string? Color, bool SuppressRipple, bool Clear);
