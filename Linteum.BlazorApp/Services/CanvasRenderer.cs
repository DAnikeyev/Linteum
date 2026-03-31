using System.Collections.Concurrent;
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
        await _js.InvokeVoidAsync("canvasRenderer.init", _canvasElement, _overlayElement);
        _initialized = true;
        
        _renderLoopTask = RenderLoop();
    }

    public async Task LoadImageAsync(byte[] imageBytes)
    {
        if (!_initialized) return;
        await _js.InvokeVoidAsync("canvasRenderer.loadImage", imageBytes);
    }

    public void EnqueuePixel(int x, int y, string color)
    {
        _queue.Enqueue(new PixelUpdate(x, y, color));
    }

    private async Task RenderLoop()
    {
        // Run at approx 10 FPS
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        
        while (await timer.WaitForNextTickAsync(_cts.Token))
        {
            if (_queue.IsEmpty) continue;

            var batch = new List<PixelUpdate>();
            while (_queue.TryDequeue(out var pixel) && batch.Count < 1000)
            {
                batch.Add(pixel);
            }

            if (batch.Count > 0)
            {
                try
                {
                    await _js.InvokeVoidAsync("canvasRenderer.renderBatch", batch);
                }
                catch (Exception)
                {
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
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
    }
}

public record struct PixelUpdate(int X, int Y, string Color);
