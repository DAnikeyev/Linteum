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

        var firstRenderAt = await jsRuntime.WaitForInvocationAsync("canvasRenderer.renderBatch");

        renderer.EnqueuePixel(3, 4, "#654321", suppressRipple: true);

        var secondRenderAt = await jsRuntime.WaitForInvocationAsync("canvasRenderer.renderBatch", skip: 1);

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

        await jsRuntime.WaitForInvocationAsync("canvasRenderer.renderBatch");

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
                var invocation = _invocations.Last(item => item.Identifier == "canvasRenderer.renderBatch");
                var rawBatch = AssertAndGetBatch(invocation.Arguments);
                return rawBatch
                    .Select(item => new RecordedPixelUpdate(
                        (int)(item.GetType().GetProperty("X")?.GetValue(item) ?? 0),
                        (int)(item.GetType().GetProperty("Y")?.GetValue(item) ?? 0),
                        item.GetType().GetProperty("Color")?.GetValue(item)?.ToString()))
                    .ToList();
            }
        }

        private static IEnumerable<object> AssertAndGetBatch(object?[]? args)
        {
            Assert.That(args, Is.Not.Null.And.Length.EqualTo(1));
            Assert.That(args![0], Is.InstanceOf<IEnumerable<PixelUpdate>>());
            return ((IEnumerable<PixelUpdate>)args[0]!).Cast<object>();
        }
    }

    private sealed record RecordedPixelUpdate(int X, int Y, string? Color);
}