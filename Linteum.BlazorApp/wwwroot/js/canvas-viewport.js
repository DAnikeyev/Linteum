/**
 * Canvas Viewport Controller
 *
 * Handles pan, zoom, hover, and click interactions entirely client-side
 * to avoid Blazor Server SignalR round-trips for high-frequency mouse events.
 *
 * Performance optimizations:
 *  - Lightweight translate3d panning with rAF-batched renderer resizing for zoom
 *  - requestAnimationFrame batching for all visual updates
 *  - Cached viewport rect (invalidated on scroll/resize/zoom)
 *  - Document-level mousemove/mouseup during drag so panning never "sticks"
 *  - Pointer events (with mouse fallback) + passive touch for mobile pinch-zoom
 */

window.canvasViewport = {
    _instance: null,

    init: function (dotNetRef, viewportEl, rendererEl, coordsEl, canvasWidth, canvasHeight, vpWidth, vpHeight) {
        if (this._instance) {
            this._instance.destroy();
        }
        this._instance = new CanvasViewportController(dotNetRef, viewportEl, rendererEl, coordsEl, canvasWidth, canvasHeight, vpWidth, vpHeight);
    },

    fitCanvas: function (vpWidth, vpHeight) {
        if (this._instance) this._instance.fit(vpWidth, vpHeight);
    },

    setBrushEnabled: function (enabled) {
        if (this._instance) this._instance.setBrushEnabled(enabled);
    },

    setBrushPreview: function (eraserEnabled, colorHex, eraserSize) {
        if (this._instance) this._instance.setBrushPreview(eraserEnabled, colorHex, eraserSize);
    },

    setSelectionPersistence: function (enabled) {
        if (this._instance) this._instance.setSelectionPersistence(enabled);
    },

    dispose: function () {
        if (this._instance) {
            this._instance.destroy();
            this._instance = null;
        }
    }
};

function CanvasViewportController(dotNetRef, viewportEl, rendererEl, coordsEl, cw, ch, vpW, vpH) {
    var self = this;

    self.dotNet = dotNetRef;
    self.viewport = viewportEl;
    self.renderer = rendererEl;
    self.cw = cw;
    self.ch = ch;
    self.vpW = vpW;
    self.vpH = vpH;

    // State
    self.scale = 1;
    self.minScale = 0.5;
    self.maxScale = 80;
    self.ox = 0;
    self.oy = 0;
    self.dragging = false;
    self.dragMoved = false;
    self.brushEnabled = false;
    self.brushPreviewColor = null;
    self.eraserEnabled = false;
    self.eraserSize = 1;
    self.selectionPersistenceEnabled = false;
    self.brushing = false;
    self.lastBrushedKey = null;
    self.lastBrushedPixel = null;
    self.lastX = 0;
    self.lastY = 0;
    self.startX = 0;
    self.startY = 0;
    self.clickedPx = null;
    self.activeMouseButton = null;

    // Pending hover pixel for the next frame
    self._pendingHover = null;
    self._hoverVisible = false;

    // rAF batching
    self._rafId = 0;
    self._rafScheduled = false;

    // Cached bounding rects (invalidated on scroll/resize/transform)
    self._cachedViewportRect = null;
    self._cachedRendererRect = null;
    self._rectDirty = true;
    self._resizeObserver = null;
    self._transformDirty = false;
    // ── Renderer setup ──
    rendererEl.style.willChange = 'transform';
    rendererEl.style.transformOrigin = '0 0';
    rendererEl.style.position = 'absolute';
    rendererEl.style.left = '0';
    rendererEl.style.top = '0';
    // Start at native size; zoom updates renderer dimensions
    rendererEl.style.width = cw + 'px';
    rendererEl.style.height = ch + 'px';

    // ── Create overlay elements ──

    // Event layer
    self.eventLayer = document.createElement('div');
    self.eventLayer.style.cssText =
        'position:absolute;top:0;left:0;width:100%;height:100%;z-index:10;cursor:crosshair;touch-action:none;';
    viewportEl.appendChild(self.eventLayer);

    // Interaction overlay (screen-space canvas): faint pixel grid, prominent
    // canvas-edge frame, and crisp hover/selected pixel borders. Everything is
    // drawn in device pixels derived from the same renderer rect used for
    // hit-testing, so it stays perfectly aligned at every zoom on every browser
    // — there is no CSS layout or per-line sub-pixel snapping to drift (the
    // failure mode of a DOM/CSS grid in Chrome/Edge).
    self.gridCanvas = document.createElement('canvas');
    self.gridCanvas.className = 'canvas-interaction-overlay';
    self.gridCanvas.style.cssText =
        'position:absolute;top:0;left:0;width:100%;height:100%;pointer-events:none;z-index:6;';
    self.gridCtx = self.gridCanvas.getContext('2d');
    self._colors = null;
    viewportEl.appendChild(self.gridCanvas);

    // Coords display (Blazor-owned element, passed in)
    self.coordsEl = coordsEl;

    // ── Helper methods ──

    self._ensureTransformApplied = function () {
        if (self._transformDirty) {
            self.applyTransform();
        }
    };

    self._getRects = function () {
        self._ensureTransformApplied();

        if (self._rectDirty || !self._cachedViewportRect || !self._cachedRendererRect) {
            self._cachedViewportRect = self.viewport.getBoundingClientRect();
            self._cachedRendererRect = self.renderer.getBoundingClientRect();
            self._rectDirty = false;
        }

        return {
            viewport: self._cachedViewportRect,
            renderer: self._cachedRendererRect
        };
    };

    self._invalidateRects = function () {
        self._rectDirty = true;
    };

    self._resolveCssColor = function (cssColor) {
        if (!cssColor) {
            return cssColor;
        }

        var probe = document.createElement('div');
        probe.style.color = cssColor;
        probe.style.display = 'none';
        document.body.appendChild(probe);

        var resolved = getComputedStyle(probe).color;
        probe.remove();

        return resolved || cssColor;
    };

    self._snapOffset = function (value) {
        var dpr = window.devicePixelRatio || 1;
        return Math.round(value * dpr) / dpr;
    };

    // Resolve the app's accent tokens once (defined on :root in app.css). Hex
    // fallbacks keep working even if the stylesheet has not loaded yet.
    self._ensureColors = function () {
        if (self._colors) {
            return self._colors;
        }
        var resolve = function (name, fallback) {
            var value = getComputedStyle(document.documentElement).getPropertyValue(name);
            value = value ? value.trim() : '';
            return value || fallback;
        };
        self._colors = {
            hover: resolve('--accent-blue-600', '#2d72dd'),      // hovered pixel border
            selected: resolve('--accent-orange-500', '#ef7a2f'),  // selected pixel border
            edge: resolve('--ink-700', '#324c73'),                // canvas edge frame
            grid: 'rgba(90, 112, 148, 0.16)',                     // faint pixel grid (softer minor lines)
            gridStrong: 'rgba(40, 64, 100, 0.40)'                 // every 10th grid line (crisper for orientation)
        };
        return self._colors;
    };

    // Match the interaction canvas backing store to the viewport at the current
    // DPR. Returns true when it changed (assigning width/height also clears it).
    self._sizeGridCanvas = function (cssW, cssH, dpr) {
        var bw = Math.max(1, Math.round(cssW * dpr));
        var bh = Math.max(1, Math.round(cssH * dpr));
        if (self.gridCanvas.width !== bw || self.gridCanvas.height !== bh) {
            self.gridCanvas.width = bw;
            self.gridCanvas.height = bh;
            return true;
        }
        return false;
    };

    // Crisp hollow rectangle. Coordinates are viewport-local CSS px; thickness is
    // CSS px. Four device-pixel-aligned strips keep the border sharp and a
    // constant thickness at any zoom level and DPR.
    self._hollowRect = function (x0c, y0c, x1c, y1c, thicknessCss, color) {
        var ctx = self.gridCtx;
        var dpr = window.devicePixelRatio || 1;
        var t = Math.max(1, Math.round(thicknessCss * dpr));
        var x0 = Math.round(x0c * dpr);
        var y0 = Math.round(y0c * dpr);
        var x1 = Math.round(x1c * dpr);
        var y1 = Math.round(y1c * dpr);
        var w = x1 - x0;
        var h = y1 - y0;
        if (w <= 0 || h <= 0) {
            return;
        }
        ctx.fillStyle = color;
        ctx.fillRect(x0, y0, w, t);       // top
        ctx.fillRect(x0, y1 - t, w, t);   // bottom
        ctx.fillRect(x0, y0, t, h);       // left
        ctx.fillRect(x1 - t, y0, t, h);   // right
    };

    // Redraw the whole screen-space interaction layer: pixel grid, canvas edge
    // frame, hovered pixel border and selected pixel border. Runs on every
    // scheduled frame — hover/pan/zoom/click/resize are all rAF-batched through
    // _onFrame -> _updateOverlays.
    self._drawInteraction = function () {
        var ctx = self.gridCtx;
        if (!ctx || !self.gridCanvas.isConnected) {
            return;
        }

        var cssW = self.viewport.clientWidth;
        var cssH = self.viewport.clientHeight;
        if (cssW <= 0 || cssH <= 0) {
            return;
        }

        var dpr = window.devicePixelRatio || 1;
        if (!self._sizeGridCanvas(cssW, cssH, dpr)) {
            ctx.clearRect(0, 0, self.gridCanvas.width, self.gridCanvas.height);
        }

        var rects = self._getRects();
        var vpRect = rects.viewport;
        var r = rects.renderer;
        if (!r || r.width <= 0 || r.height <= 0 || self.cw <= 0 || self.ch <= 0) {
            return;
        }

        var colors = self._ensureColors();
        var originX = r.left - vpRect.left;
        var originY = r.top - vpRect.top;
        var cellW = r.width / self.cw;
        var cellH = r.height / self.ch;
        var canvasRight = originX + self.cw * cellW;
        var canvasBottom = originY + self.ch * cellH;

        var GRID_MIN_CELL = 8;   // hide the grid below this many CSS px per cell
        var GRID_MAJOR = 10;     // emphasise every Nth line for orientation

        // 1) Soft elevation shadow around the canvas edge so the white art
        //    sheet floats above the page background. The rect is filled to
        //    cast a full shadow, then its interior is cleared — leaving an
        //    outer-only halo that never tints the art. Drawn before the grid
        //    so the interior clear can't erase the grid lines.
        var shadowX = Math.round(originX * dpr);
        var shadowY = Math.round(originY * dpr);
        var shadowW = Math.round((canvasRight - originX) * dpr);
        var shadowH = Math.round((canvasBottom - originY) * dpr);
        if (shadowW > 0 && shadowH > 0) {
            ctx.save();
            ctx.shadowColor = 'rgba(20, 52, 108, 0.18)';
            ctx.shadowBlur = 18 * dpr;
            ctx.shadowOffsetX = 0;
            ctx.shadowOffsetY = 6 * dpr;
            ctx.fillStyle = '#000000';
            ctx.fillRect(shadowX, shadowY, shadowW, shadowH);
            ctx.restore();
            ctx.clearRect(shadowX, shadowY, shadowW, shadowH);
        }

        // 2) Faint pixel grid — only when cells are large enough to read.
        if (cellW >= GRID_MIN_CELL && cellH >= GRID_MIN_CELL) {
            var top = Math.round(originY * dpr);
            var bot = Math.round(canvasBottom * dpr);
            var left = Math.round(originX * dpr);
            var right = Math.round(canvasRight * dpr);

            var iMin = Math.max(0, Math.floor(-originX / cellW));
            var iMax = Math.min(self.cw, Math.ceil((cssW - originX) / cellW));
            for (var i = iMin; i <= iMax; i++) {
                var gx = Math.round((originX + i * cellW) * dpr);
                ctx.fillStyle = (i % GRID_MAJOR === 0) ? colors.gridStrong : colors.grid;
                ctx.fillRect(gx, top, 1, bot - top);
            }

            var jMin = Math.max(0, Math.floor(-originY / cellH));
            var jMax = Math.min(self.ch, Math.ceil((cssH - originY) / cellH));
            for (var j = jMin; j <= jMax; j++) {
                var gy = Math.round((originY + j * cellH) * dpr);
                ctx.fillStyle = (j % GRID_MAJOR === 0) ? colors.gridStrong : colors.grid;
                ctx.fillRect(left, gy, right - left, 1);
            }
        }

        // 3) Crisp canvas edge frame.
        self._hollowRect(originX, originY, canvasRight, canvasBottom, 2, colors.edge);

        // 4) Hovered pixel border (blue accent).
        var hover = self._pendingHover;
        if (hover && !self.brushing) {
            self._hollowRect(
                originX + hover.x * cellW, originY + hover.y * cellH,
                originX + (hover.x + 1) * cellW, originY + (hover.y + 1) * cellH,
                1.5, colors.hover);
        }

        // 5) Selected pixel border (orange accent) — static, no blink. Hidden in
        // brush mode, where clickedPx tracks the brush head (the solid colour
        // preview on the art-space overlay is the indicator there instead).
        var sel = (!self.brushEnabled) ? self.clickedPx : null;
        if (sel) {
            self._hollowRect(
                originX + sel.x * cellW, originY + sel.y * cellH,
                originX + (sel.x + 1) * cellW, originY + (sel.y + 1) * cellH,
                2, colors.selected);
        }
    };

    self._syncInteractionOverlay = function (hoverPixel, hoverStrokeWidth, clickStrokeWidth) {
        if (!window.canvasRenderer || typeof window.canvasRenderer.setInteractionState !== 'function') {
            return;
        }

        // The selected pixel is no longer rendered here (it used to blink on the
        // art-space overlay). It is now a static orange border on the screen-space
        // interaction canvas (_drawInteraction). In brush mode we only draw the
        // solid colour preview while an actual brush stroke is active, otherwise
        // a palette change would falsely recolour the last selected pixel locally.
        var renderedClickPixel = null;
        var clickPreviewMode = 'solid';
        var clickPreviewColor = null;
        if (self.brushEnabled && self.brushing && self.clickedPx) {
            renderedClickPixel = self.clickedPx;
            if (self.eraserEnabled) {
                clickPreviewColor = '#ffffff';
            } else if (self.brushPreviewColor) {
                clickPreviewColor = self.brushPreviewColor;
            }
        }

        var rects = self._getRects();
        var rendererRect = rects.renderer;
        var pxScaleX = rendererRect && self.cw > 0 ? rendererRect.width / self.cw : self.scale;
        var pxScaleY = rendererRect && self.ch > 0 ? rendererRect.height / self.ch : self.scale;
        var pxScale = Math.max(0.0001, Math.min(pxScaleX || self.scale, pxScaleY || self.scale));

        window.canvasRenderer.setInteractionState({
            hoverPixel: null,
            clickPixel: renderedClickPixel,
            hoverColor: 'rgba(55,140,255,1)',
            clickColor: 'rgba(55,140,255,1)',
            hoverLineWidth: hoverStrokeWidth / pxScale,
            clickLineWidth: clickStrokeWidth / pxScale,
            clickPreviewMode: clickPreviewMode,
            clickPreviewColor: clickPreviewColor,
            clickBlinkPeriodMs: 1500
        });
    };

    self._clearClickedPixel = function (notifyBlazor) {
        if (!self.clickedPx) {
            return;
        }

        self.clickedPx = null;
        self._scheduleFrame();

        if (notifyBlazor) {
            self.dotNet.invokeMethodAsync('OnPixelSelectionCleared');
        }
    };

    self.pixelAt = function (clientX, clientY) {
        var rects = self._getRects();
        var rendererRect = rects.renderer;
        if (!rendererRect || rendererRect.width <= 0 || rendererRect.height <= 0) {
            return null;
        }

        if (clientX < rendererRect.left || clientX >= rendererRect.right || clientY < rendererRect.top || clientY >= rendererRect.bottom) {
            return null;
        }

        var relativeX = (clientX - rendererRect.left) / rendererRect.width;
        var relativeY = (clientY - rendererRect.top) / rendererRect.height;
        var px = Math.min(self.cw - 1, Math.max(0, Math.floor(relativeX * self.cw)));
        var py = Math.min(self.ch - 1, Math.max(0, Math.floor(relativeY * self.ch)));
        return { x: px, y: py };
    };

    self.clamp = function () {
        var m = 20;
        var rw = self.cw * self.scale;
        var rh = self.ch * self.scale;
        self.ox = Math.max(m - rw, Math.min(self.vpW - m, self.ox));
        self.oy = Math.max(m - rh, Math.min(self.vpH - m, self.oy));
    };

    self.applyTransform = function () {
        var renderedWidth = self.cw * self.scale;
        var renderedHeight = self.ch * self.scale;
        var renderedOx = self._snapOffset(self.ox);
        var renderedOy = self._snapOffset(self.oy);
        self.renderer.style.width = renderedWidth + 'px';
        self.renderer.style.height = renderedHeight + 'px';
        self.renderer.style.transform =
            'translate3d(' + renderedOx + 'px,' + renderedOy + 'px,0)';
        self._transformDirty = false;
        self._invalidateRects();
    };

    self.showClick = function () {
        self._updateOverlays();
    };

    self._updateCursor = function () {
        self.eventLayer.style.cursor = self.dragging && !self.brushing ? 'grabbing' : 'crosshair';
    };

    self._onFrame = function () {
        self._rafScheduled = false;
        self.applyTransform();
        self._updateOverlays();
    };

    self._scheduleFrame = function () {
        if (!self._rafScheduled) {
            self._rafScheduled = true;
            self._rafId = requestAnimationFrame(self._onFrame);
        }
    };

    self._updateOverlays = function () {
        var overlayStrokeBase = window.devicePixelRatio || 1;
        var hoverStrokeWidth = 1.35 / overlayStrokeBase;
        var clickStrokeWidth = Math.max(2.35 / overlayStrokeBase, hoverStrokeWidth);

        // Hover indicator
        var p = self._pendingHover;
        if (p) {
            self.coordsEl.style.display = 'block';
            self.coordsEl.textContent = 'X: ' + p.x + ', Y: ' + p.y;
            self._hoverVisible = true;
        } else if (self._hoverVisible) {
            self.coordsEl.style.display = 'none';
            self._hoverVisible = false;
        }

        self._syncInteractionOverlay(p, hoverStrokeWidth, clickStrokeWidth);

        // Screen-space grid + canvas edge frame + hover/selected borders.
        self._drawInteraction();
    };

    self.setBrushEnabled = function (enabled) {
        self.brushEnabled = !!enabled;
        if (!self.brushEnabled && self.brushing) {
            self.brushing = false;
            self.lastBrushedKey = null;
            self.lastBrushedPixel = null;
            self.dotNet.invokeMethodAsync('OnBrushStrokeEnded');
        }
        self._updateCursor();
        self._scheduleFrame();
    };

    self.setBrushPreview = function (eraserEnabled, colorHex, eraserSize) {
        self.eraserEnabled = !!eraserEnabled;
        self.brushPreviewColor = typeof colorHex === 'string' && colorHex.length > 0 ? colorHex : null;
        self.eraserSize = Math.max(1, eraserSize || 1);
        self._scheduleFrame();
    };

    self.setSelectionPersistence = function (enabled) {
        self.selectionPersistenceEnabled = !!enabled;
    };

    self._renderBrushPreviewPixel = function (p) {
        if (!window.canvasRenderer) {
            return;
        }

        if (self.eraserEnabled) {
            var half = Math.floor(self.eraserSize / 2);
            var eraseBatch = [];
            for (var dx = -half; dx <= half; dx++) {
                var targetX = p.x + dx;
                if (targetX < 0 || targetX >= self.cw) {
                    continue;
                }

                for (var dy = -half; dy <= half; dy++) {
                    var targetY = p.y + dy;
                    if (targetY < 0 || targetY >= self.ch) {
                        continue;
                    }

                    eraseBatch.push({ x: targetX, y: targetY, color: null, suppressRipple: true, clear: true });
                }
            }

            if (eraseBatch.length > 0) {
                window.canvasRenderer.renderBatch(eraseBatch, false);
            }
            return;
        }

        if (!self.brushPreviewColor) {
            return;
        }

        window.canvasRenderer.renderBatch([{ x: p.x, y: p.y, color: self.brushPreviewColor, suppressRipple: true, clear: false }], false);
    };

    self._paintBrushPixel = function (p) {
        var key = p.x + ':' + p.y;
        if (self.lastBrushedKey === key) {
            return;
        }

        self.clickedPx = p;
        self.lastBrushedKey = key;
        self._renderBrushPreviewPixel(p);
        self.dotNet.invokeMethodAsync('OnBrushPixelPaintRequested', p.x, p.y);
    };

    self._paintBrushLine = function (fromPixel, toPixel) {
        if (!fromPixel) {
            self._paintBrushPixel(toPixel);
            return;
        }

        var x0 = fromPixel.x;
        var y0 = fromPixel.y;
        var x1 = toPixel.x;
        var y1 = toPixel.y;
        var dx = Math.abs(x1 - x0);
        var dy = Math.abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var err = dx - dy;

        while (true) {
            self._paintBrushPixel({ x: x0, y: y0 });
            if (x0 === x1 && y0 === y1) {
                break;
            }

            var e2 = err * 2;
            if (e2 > -dy) {
                err -= dy;
                x0 += sx;
            }
            if (e2 < dx) {
                err += dx;
                y0 += sy;
            }
        }
    };

    self._paintBrushAt = function (clientX, clientY) {
        var p = self.pixelAt(clientX, clientY);
        self._pendingHover = p;
        if (!p) {
            self.lastBrushedPixel = null;
            self._scheduleFrame();
            return;
        }

        self._paintBrushLine(self.lastBrushedPixel, p);
        self.lastBrushedPixel = p;
        self._scheduleFrame();
    };

    self._startBrushStroke = function (clientX, clientY) {
        var brushStartPixel = self.brushEnabled ? self.pixelAt(clientX, clientY) : null;
        if (!brushStartPixel) {
            return false;
        }

        self.dragging = true;
        self.dragMoved = true;
        self.brushing = true;
        self.activeMouseButton = 0;
        self.lastBrushedKey = null;
        self.lastBrushedPixel = null;
        self.lastX = clientX;
        self.lastY = clientY;
        self.startX = clientX;
        self.startY = clientY;
        self._updateCursor();

        self.dotNet.invokeMethodAsync('OnBrushStrokeStarted');
        self._paintBrushAt(clientX, clientY);
        return true;
    };

    self._endBrushStroke = function () {
        if (!self.brushing) {
            return;
        }

        self.brushing = false;
        self.dragging = false;
        self.dragMoved = false;
        self.activeMouseButton = null;
        self.lastBrushedKey = null;
        self.lastBrushedPixel = null;
        self.dotNet.invokeMethodAsync('OnBrushStrokeEnded');
        self._updateCursor();
        self._scheduleFrame();
    };

    self._startPanDrag = function (clientX, clientY, button) {
        self.dragging = true;
        self.dragMoved = false;
        self.activeMouseButton = button;
        self.lastX = clientX;
        self.lastY = clientY;
        self.startX = clientX;
        self.startY = clientY;
        self._updateCursor();
    };

    self._panWhileDragging = function (clientX, clientY) {
        self.ox += clientX - self.lastX;
        self.oy += clientY - self.lastY;
        self.clamp();
        self.lastX = clientX;
        self.lastY = clientY;
        self._transformDirty = true;
        self._pendingHover = self.pixelAt(clientX, clientY);
        self._scheduleFrame();
    };

    // ── Mouse event handlers ──

    self._onDown = function (e) {
        if (e.button !== 0 && e.button !== 1) return;
        e.preventDefault();

        if (e.button === 0 && self._startBrushStroke(e.clientX, e.clientY)) {
            document.addEventListener('mousemove', self._onDocMove, { passive: true });
            document.addEventListener('mouseup', self._onDocUp);
            return;
        }

        self._startPanDrag(e.clientX, e.clientY, e.button);

        // Listen on document so drag continues even outside viewport
        document.addEventListener('mousemove', self._onDocMove, { passive: true });
        document.addEventListener('mouseup', self._onDocUp);
    };

    self._onDocMove = function (e) {
        if (!self.dragging) return;

        if (self.brushing) {
            self._paintBrushAt(e.clientX, e.clientY);
            return;
        }

        self._panWhileDragging(e.clientX, e.clientY);
        if (!self.dragMoved && Math.abs(e.clientX - self.startX) + Math.abs(e.clientY - self.startY) > 4) {
            self.dragMoved = true;
        }
    };

    self._onDocUp = function (e) {
        if (self.activeMouseButton !== null && e.button !== self.activeMouseButton) {
            return;
        }

        document.removeEventListener('mousemove', self._onDocMove);
        document.removeEventListener('mouseup', self._onDocUp);

        if (self.brushing) {
            self._endBrushStroke();
            return;
        }

        if (self.activeMouseButton === 0 && self.dragging && !self.dragMoved) {
            // Click (not a drag) — notify Blazor server
            var p = self.pixelAt(e.clientX, e.clientY);
            if (p) {
                self.clickedPx = p;
                self._scheduleFrame();
                self.dotNet.invokeMethodAsync('OnPixelClicked', p.x, p.y);
            } else if (!self.brushEnabled && !self.selectionPersistenceEnabled) {
                self._clearClickedPixel(true);
            }
        }
        self.dragging = false;
        self.activeMouseButton = null;
        self._updateCursor();
    };

    self._onDocumentMouseDown = function (e) {
        if (e.button !== 0 || self.brushEnabled || self.dragging || !self.clickedPx || self.selectionPersistenceEnabled) {
            return;
        }

        if (e.target && typeof e.target.closest === 'function' && e.target.closest('.pixelmanager')) {
            return;
        }

        if (self.viewport && self.viewport.contains(e.target)) {
            return;
        }

        self._clearClickedPixel(true);
    };

    self._onAuxClick = function (e) {
        if (e.button === 1) {
            e.preventDefault();
        }
    };

    self._onMove = function (e) {
        // Hover-only (non-drag) moves on the event layer
        if (self.dragging) return; // drag is handled by _onDocMove
        self._pendingHover = self.pixelAt(e.clientX, e.clientY);
        self._scheduleFrame();
    };

    self._onOut = function () {
        if (!self.dragging) {
            self._pendingHover = null;
            self._scheduleFrame();
        }
    };

    self._onWheel = function (e) {
        e.preventDefault();
        var factor = e.deltaY < 0 ? 1.1 : 0.9;
        var ns = Math.max(self.minScale, Math.min(self.maxScale, self.scale * factor));

        self._transformDirty = true;
        var rects = self._getRects();
        var viewportRect = rects.viewport;
        var mx = e.clientX - viewportRect.left;
        var my = e.clientY - viewportRect.top;
        var wx = (mx - self.ox) / self.scale;
        var wy = (my - self.oy) / self.scale;

        self.scale = ns;
        self.ox = mx - wx * self.scale;
        self.oy = my - wy * self.scale;
        self.clamp();
        self._transformDirty = true;
        self._invalidateRects(); // zoom changes the visual transform, rect may shift

        self._pendingHover = self.pixelAt(e.clientX, e.clientY);
        self._scheduleFrame();
    };

    // ── Touch handlers (pinch-zoom + single-finger pan) ──

    self._activeTouches = {};
    self._lastPinchDist = 0;
    self._lastPinchMid = null;

    self._touchDist = function (a, b) {
        var dx = a.clientX - b.clientX;
        var dy = a.clientY - b.clientY;
        return Math.sqrt(dx * dx + dy * dy);
    };

    self._touchMid = function (a, b) {
        return { x: (a.clientX + b.clientX) / 2, y: (a.clientY + b.clientY) / 2 };
    };

    self._onTouchStart = function (e) {
        e.preventDefault();
        for (var i = 0; i < e.changedTouches.length; i++) {
            var t = e.changedTouches[i];
            self._activeTouches[t.identifier] = { clientX: t.clientX, clientY: t.clientY };
        }
        var keys = Object.keys(self._activeTouches);
        if (keys.length === 1) {
            var first = self._activeTouches[keys[0]];
            if (self._startBrushStroke(first.clientX, first.clientY)) {
                return;
            }

            self.dragging = true;
            self.dragMoved = false;
            self.lastX = first.clientX;
            self.lastY = first.clientY;
            self.startX = first.clientX;
            self.startY = first.clientY;
        } else if (keys.length >= 2) {
            self._endBrushStroke();
            var t0 = self._activeTouches[keys[0]];
            var t1 = self._activeTouches[keys[1]];
            self._lastPinchDist = self._touchDist(t0, t1);
            self._lastPinchMid = self._touchMid(t0, t1);
            self.dragging = false;
            self.dragMoved = true;
        }
    };

    self._onTouchMove = function (e) {
        e.preventDefault();
        for (var i = 0; i < e.changedTouches.length; i++) {
            var t = e.changedTouches[i];
            self._activeTouches[t.identifier] = { clientX: t.clientX, clientY: t.clientY };
        }
        var keys = Object.keys(self._activeTouches);
        if (keys.length === 1 && self.brushing) {
            var brushTouch = self._activeTouches[keys[0]];
            self._paintBrushAt(brushTouch.clientX, brushTouch.clientY);
        } else if (keys.length === 1 && self.dragging) {
            var cur = self._activeTouches[keys[0]];
            self._panWhileDragging(cur.clientX, cur.clientY);
            if (!self.dragMoved && Math.abs(cur.clientX - self.startX) + Math.abs(cur.clientY - self.startY) > 4) {
                self.dragMoved = true;
            }
        } else if (keys.length >= 2) {
            self._endBrushStroke();
            var t0 = self._activeTouches[keys[0]];
            var t1 = self._activeTouches[keys[1]];
            var dist = self._touchDist(t0, t1);
            var mid = self._touchMid(t0, t1);

            if (self._lastPinchDist > 0) {
                var factor = dist / self._lastPinchDist;
                var ns = Math.max(self.minScale, Math.min(self.maxScale, self.scale * factor));

                self._transformDirty = true;
                var rects = self._getRects();
                var viewportRect = rects.viewport;
                var mx = mid.x - viewportRect.left;
                var my = mid.y - viewportRect.top;
                var wx = (mx - self.ox) / self.scale;
                var wy = (my - self.oy) / self.scale;

                self.scale = ns;
                self.ox = mx - wx * self.scale;
                self.oy = my - wy * self.scale;

                // Also pan with midpoint movement
                if (self._lastPinchMid) {
                    self.ox += mid.x - self._lastPinchMid.x;
                    self.oy += mid.y - self._lastPinchMid.y;
                }
                self.clamp();
                self._transformDirty = true;
                self._invalidateRects();
                self._scheduleFrame();
            }
            self._lastPinchDist = dist;
            self._lastPinchMid = mid;
            self.dragMoved = true;
        }
    };

    self._onTouchEnd = function (e) {
        for (var i = 0; i < e.changedTouches.length; i++) {
            delete self._activeTouches[e.changedTouches[i].identifier];
        }
        var remaining = Object.keys(self._activeTouches).length;
        if (remaining === 0) {
            if (self.brushing) {
                self._endBrushStroke();
            } else if (self.dragging && !self.dragMoved) {
                var p = self.pixelAt(self.lastX, self.lastY);
                if (p) {
                    self.clickedPx = p;
                    self._scheduleFrame();
                    self.dotNet.invokeMethodAsync('OnPixelClicked', p.x, p.y);
                }
            }

            self.dragging = false;
            self.dragMoved = false;
            self._updateCursor();
            self.lastBrushedKey = null;
            self.lastBrushedPixel = null;
            self._lastPinchDist = 0;
            self._lastPinchMid = null;
            self._pendingHover = null;
            self._scheduleFrame();
        } else if (remaining === 1) {
            if (self.brushing) {
                self._endBrushStroke();
            }

            var key = Object.keys(self._activeTouches)[0];
            var t = self._activeTouches[key];

            self.dragging = !self.brushEnabled;
            self.dragMoved = true;
            self.lastX = t.clientX;
            self.lastY = t.clientY;
            self.startX = t.clientX;
            self.startY = t.clientY;
            self._lastPinchDist = 0;
            self._lastPinchMid = null;
            self._updateCursor();
        }
    };

    // ── Rect invalidation on scroll/resize ──
    self._onScrollOrResize = function () { self._invalidateRects(); };

    // ── Attach events ──
    self.eventLayer.addEventListener('mousedown', self._onDown);
    self.eventLayer.addEventListener('auxclick', self._onAuxClick);
    self.eventLayer.addEventListener('mousemove', self._onMove, { passive: true });
    self.eventLayer.addEventListener('mouseout', self._onOut);
    self.eventLayer.addEventListener('wheel', self._onWheel, { passive: false });

    self.eventLayer.addEventListener('touchstart', self._onTouchStart, { passive: false });
    self.eventLayer.addEventListener('touchmove', self._onTouchMove, { passive: false });
    self.eventLayer.addEventListener('touchend', self._onTouchEnd);
    self.eventLayer.addEventListener('touchcancel', self._onTouchEnd);

    document.addEventListener('mousedown', self._onDocumentMouseDown, true);
    window.addEventListener('scroll', self._onScrollOrResize, { passive: true });
    window.addEventListener('resize', self._onScrollOrResize, { passive: true });

    if (window.ResizeObserver) {
        self._resizeObserver = new ResizeObserver(function () {
            self._invalidateRects();
            self._scheduleFrame();
        });
        self._resizeObserver.observe(viewportEl);
    }

    // ── Initial fit ──
    self.fit(vpW, vpH);
}

CanvasViewportController.prototype.fit = function (vpW, vpH) {
    this.vpW = vpW;
    this.vpH = vpH;
    var margin = 1;
    if (this.cw > 0 && this.ch > 0) {
        var aw = Math.max(1, vpW - margin * 2);
        var ah = Math.max(1, vpH - margin * 2);
        var ts = Math.min(aw / this.cw, ah / this.ch);
        this.scale = Math.max(this.minScale, Math.min(this.maxScale, ts));
        this.ox = (vpW - this.cw * this.scale) / 2;
        this.oy = (vpH - this.ch * this.scale) / 2;
    }
    this._invalidateRects();
    this.applyTransform();
    this._updateOverlays();
};

CanvasViewportController.prototype.destroy = function () {
    if (this._rafId) cancelAnimationFrame(this._rafId);

    // Remove document-level listeners that may still be attached from a drag
    document.removeEventListener('mousemove', this._onDocMove);
    document.removeEventListener('mouseup', this._onDocUp);
    document.removeEventListener('mousedown', this._onDocumentMouseDown, true);

    window.removeEventListener('scroll', this._onScrollOrResize);
    window.removeEventListener('resize', this._onScrollOrResize);
    if (this._resizeObserver) {
        this._resizeObserver.disconnect();
        this._resizeObserver = null;
    }

    if (this.eventLayer) {
        this.eventLayer.removeEventListener('mousedown', this._onDown);
        this.eventLayer.removeEventListener('auxclick', this._onAuxClick);
        this.eventLayer.removeEventListener('mousemove', this._onMove);
        this.eventLayer.removeEventListener('mouseout', this._onOut);
        this.eventLayer.removeEventListener('wheel', this._onWheel);
        this.eventLayer.removeEventListener('touchstart', this._onTouchStart);
        this.eventLayer.removeEventListener('touchmove', this._onTouchMove);
        this.eventLayer.removeEventListener('touchend', this._onTouchEnd);
        this.eventLayer.removeEventListener('touchcancel', this._onTouchEnd);
        this.eventLayer.remove();
    }
    if (this.gridCanvas) {
        this.gridCanvas.remove();
        this.gridCanvas = null;
        this.gridCtx = null;
    }
    if (window.canvasRenderer && typeof window.canvasRenderer.setInteractionState === 'function') {
        window.canvasRenderer.setInteractionState({
            hoverPixel: null,
            clickPixel: null
        });
    }
    if (this.coordsEl) { this.coordsEl.style.display = 'none'; this.coordsEl.textContent = ''; }
};

