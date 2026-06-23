using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Linteum.BlazorApp.Services;

namespace Linteum.Tests;

public class CanvasRendererTests
{
    [Test]
    public async Task RenderLoop_RendersQueuedPixelsWithoutFixedFrameDelay()
    {
        var jsRuntime = new RecordingJsRuntime();
        await using var renderer = new CanvasRenderer(jsRuntime);

        await renderer.InitializeAsync(default(ElementReference), default(ElementReference));

        renderer.EnqueuePixel(1, 2, "#123456", suppressRipple: true);

        var firstRenderAt = await jsRuntime.WaitForInvocationAsync("canvasRenderer.renderBatchTyped");

        renderer.EnqueuePixel(3, 4, "#654321", suppressRipple: true);

        var secondRenderAt = await jsRuntime.WaitForInvocationAsync("canvasRenderer.renderBatchTyped", skip: 1);

        Assert.That(secondRenderAt - firstRenderAt, Is.LessThan(TimeSpan.FromMilliseconds(25)));
    }

    [Test]
    public async Task RenderLoop_KeepsLatestUpdatePerPixelInBatch()
    {
        var jsRuntime = new RecordingJsRuntime();
        await using var renderer = new CanvasRenderer(jsRuntime);

        await renderer.InitializeAsync(default(ElementReference), default(ElementReference));

        renderer.EnqueuePixel(5, 6, "#111111", suppressRipple: true);
        renderer.EnqueuePixel(5, 6, "#222222", suppressRipple: true);

        await jsRuntime.WaitForInvocationAsync("canvasRenderer.renderBatchTyped");

        var batch = jsRuntime.GetLastRenderBatch();

        Assert.That(batch, Has.Count.EqualTo(1));
        Assert.That(batch[0].X, Is.EqualTo(5));
        Assert.That(batch[0].Y, Is.EqualTo(6));
        Assert.That(batch[0].Color, Is.EqualTo("#222222"));
    }

    private sealed class RecordingJsRuntime : IJSRuntime
    {
        private readonly List<(string Identifier, object?[]? Arguments, DateTimeOffset Timestamp)> _invocations = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            lock (_invocations)
            {
                _invocations.Add((identifier, args, DateTimeOffset.UtcNow));
                Monitor.PulseAll(_invocations);
            }

            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);

        public async Task<DateTimeOffset> WaitForInvocationAsync(string identifier, int skip = 0, int timeoutMs = 1000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            while (true)
            {
                lock (_invocations)
                {
                    var matches = _invocations.Where(invocation => invocation.Identifier == identifier).ToList();
                    if (matches.Count > skip)
                    {
                        return matches[skip].Timestamp;
                    }

                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                    {
                        throw new TimeoutException($"Timed out waiting for JS invocation '{identifier}'.");
                    }

                    Monitor.Wait(_invocations, remaining);
                }

                await Task.Yield();
            }
        }

        public IReadOnlyList<RecordedPixelUpdate> GetLastRenderBatch()
        {
            lock (_invocations)
            {
                var invocation = _invocations.Last(item => item.Identifier == "canvasRenderer.renderBatchTyped");
                var args = invocation.Arguments;
                Assert.That(args, Is.Not.Null.And.Length.EqualTo(4));

                var xs = (int[])args![0];
                var ys = (int[])args![1];
                var rgbs = (int[])args![2];
                var flags = (byte[])args![3];

                var result = new List<RecordedPixelUpdate>(xs.Length);
                for (var i = 0; i < xs.Length; i++)
                {
                    // Packed RGB -> "#RRGGBB"; clear-flagged pixels carry no color (P-PERF-07).
                    var color = (flags[i] & 1) != 0 ? null : "#" + rgbs[i].ToString("X6");
                    result.Add(new RecordedPixelUpdate(xs[i], ys[i], color));
                }

                return result;
            }
        }
    }

    private sealed record RecordedPixelUpdate(int X, int Y, string? Color);
}