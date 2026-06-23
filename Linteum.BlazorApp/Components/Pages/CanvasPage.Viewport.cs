using Linteum.BlazorApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Linteum.BlazorApp.Components.Pages;

public partial class CanvasPage
{
    private ElementReference _canvasRef;
    private ElementReference _overlayRef;
    private CanvasRenderer? _renderer;

    private ElementReference _viewportHostRef;
    private ElementReference _viewportRef;
    private ElementReference _rendererRef;
    private ElementReference _coordsDisplayRef;

    private double ViewportWidth { get; set; } = 900;
    private double ViewportHeight { get; set; } = 600;

    private bool _isMobileLayout;
    private bool _jsViewportInitialized;
    private bool _pendingViewportRefresh = true;
    private bool _rendererInitializationInProgress;

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

    private sealed record ElementSize(double Width, double Height);
    private sealed record LayoutMetrics(double WindowWidth, double WindowHeight, double MainHPad, double MainVPad, double PixelManagerWidth);
}
