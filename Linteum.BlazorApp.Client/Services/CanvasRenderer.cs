using System.Collections.Concurrent;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Linteum.BlazorApp.Client.Services;

public class CanvasRenderer : IAsyncDisposable
{
    private static long _nextRendererSessionId;
    private readonly IJSRuntime _js;
    private readonly long _rendererSessionId = Interlocked.Increment(ref _nextRendererSessionId);
    private ElementReference _canvasElement;
    private ElementReference _overlayElement;
    private readonly ConcurrentQueue<PixelUpdate> _queue = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _renderLoopTask;
    private bool _initialized;

    public CanvasRenderer(IJSRuntime js)
    {
        _js = js;
    }

    public async Task InitializeAsync(ElementReference canvasElement, ElementReference overlayElement)
    {
        _canvasElement = canvasElement;
        _overlayElement = overlayElement;
        await _js.InvokeVoidAsync("canvasRenderer.init", _canvasElement, _overlayElement, _rendererSessionId);
        _initialized = true;
        _renderLoopTask = RenderLoop();
    }

    public async Task<bool> LoadImageAsync(byte[] imageBytes, long requestId)
    {
        if (!_initialized) return false;
        return await _js.InvokeAsync<bool>("canvasRenderer.loadImage", imageBytes, _rendererSessionId, requestId);
    }

    public void EnqueuePixel(int x, int y, string color)
    {
        _queue.Enqueue(new PixelUpdate(x, y, color));
    }

    private async Task RenderLoop()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        while (await timer.WaitForNextTickAsync(_cts.Token))
        {
            if (_queue.IsEmpty) continue;
            var batch = new List<PixelUpdate>();
            while (_queue.TryDequeue(out var pixel) && batch.Count < 1000)
                batch.Add(pixel);
            if (batch.Count > 0)
            {
                try { await _js.InvokeVoidAsync("canvasRenderer.renderBatch", batch); }
                catch { }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_initialized)
        {
            try { await _js.InvokeVoidAsync("canvasRenderer.dispose", _rendererSessionId); }
            catch { }
            _initialized = false;
        }
        if (_renderLoopTask != null)
        {
            try { await _renderLoopTask; }
            catch { }
        }
        _cts.Dispose();
    }
}

public record struct PixelUpdate(int X, int Y, string Color);

