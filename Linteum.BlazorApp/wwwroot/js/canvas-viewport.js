        this.eventLayer.removeEventListener('mouseup', this._onUp);
    this.showClick();
    self.eventLayer.addEventListener('mousemove', self._onMove);
    self.eventLayer.addEventListener('mouseup', self._onUp);
 *
 * Performance optimizations:
 *  - GPU-accelerated CSS transform (translate3d + scale) instead of left/top/width/height
 *  - requestAnimationFrame batching for all visual updates
 *  - Cached viewport rect (invalidated on scroll/resize/zoom)
 *  - Document-level mousemove/mouseup during drag so panning never "sticks"
 *  - Pointer events (with mouse fallback) + passive touch for mobile pinch-zoom
        // Update hover position after zoom
        var p = self.pixelAt(e.clientX, e.clientY);
        if (p) {
            self.hoverEl.style.left = (self.ox + p.x * self.scale) + 'px';
            self.hoverEl.style.top = (self.oy + p.y * self.scale) + 'px';
            self.hoverEl.style.width = self.scale + 'px';
            self.hoverEl.style.height = self.scale + 'px';
            self.coordsEl.textContent = 'X: ' + p.x + ', Y: ' + p.y;
        self.applyTransform();
        self.showClick();
        var rect = self.eventLayer.getBoundingClientRect();
        self.dragging = false;
        self.hoverEl.style.display = 'none';
        self.coordsEl.style.display = 'none';
        self.eventLayer.style.cursor = 'crosshair';
                self.showClick();
            // Click (not a drag) — notify Blazor server
    self._onUp = function (e) {

        // Update hover indicator
        var p = self.pixelAt(e.clientX, e.clientY);
        if (p) {
            self.hoverEl.style.display = 'block';
            self.hoverEl.style.left = (self.ox + p.x * self.scale) + 'px';
            self.hoverEl.style.top = (self.oy + p.y * self.scale) + 'px';
            self.hoverEl.style.width = self.scale + 'px';
            self.hoverEl.style.height = self.scale + 'px';
            self.coordsEl.style.display = 'block';
            self.coordsEl.textContent = 'X: ' + p.x + ', Y: ' + p.y;
        } else {
            self.hoverEl.style.display = 'none';
            self.coordsEl.style.display = 'none';
        }
    self._onMove = function (e) {
        if (self.dragging) {
            self.ox += e.clientX - self.lastX;
            self.oy += e.clientY - self.lastY;
            self.clamp();
            self.lastX = e.clientX;
            self.lastY = e.clientY;
            if (Math.abs(e.clientX - self.startX) + Math.abs(e.clientY - self.startY) > 4) {
                self.dragMoved = true;
            }
            self.applyTransform();
            self.showClick();
        if (e.button === 0) {
            self.dragging = true;
            self.dragMoved = false;
    // rAF batching
    self._rafId = 0;
    self._rafScheduled = false;

    // Cached bounding rect (invalidated on scroll/resize)
    self._cachedRect = null;
    self._rectDirty = true;

    // ── GPU-accelerated renderer setup ──
    rendererEl.style.willChange = 'transform';
    rendererEl.style.transformOrigin = '0 0';
    rendererEl.style.position = 'absolute';
    rendererEl.style.left = '0';
    rendererEl.style.top = '0';
    // Set the native size once; scaling is handled by transform
    rendererEl.style.width = cw + 'px';
    rendererEl.style.height = ch + 'px';

            self.lastX = e.clientX;
            self.lastY = e.clientY;
            self.startX = e.clientX;
            self.startY = e.clientY;
            self.eventLayer.style.cursor = 'grabbing';
        'position:absolute;box-shadow:inset 0 0 0 1px rgba(31,76,145,0.6);pointer-events:none;z-index:5;display:none;will-change:transform;';
    // ── Event handlers ──
    self.showClick = function () {
        if (!self.clickedPx) { self.clickEl.style.display = 'none'; return; }
        self.clickEl.style.display = 'block';
        self.clickEl.style.left = (self.ox + self.clickedPx.x * self.scale) + 'px';
        'position:absolute;box-shadow:inset 0 0 0 2px var(--accent-3);pointer-events:none;z-index:6;display:none;will-change:transform;';
        self.clickEl.style.width = self.scale + 'px';
        self.clickEl.style.height = self.scale + 'px';
        var s = self.renderer.style;
        s.left = self.ox + 'px';
        s.top = self.oy + 'px';
        s.width = (self.cw * self.scale) + 'px';
        s.height = (self.ch * self.scale) + 'px';
        var rect = self.eventLayer.getBoundingClientRect();
        'position:absolute;top:0;left:0;width:100%;height:100%;z-index:10;cursor:crosshair;';
        'position:absolute;box-shadow:inset 0 0 0 2px var(--accent-3);pointer-events:none;z-index:6;display:none;';
        'position:absolute;box-shadow:inset 0 0 0 1px rgba(31,76,145,0.6);pointer-events:none;z-index:5;display:none;';
/**
 * Canvas Viewport Controller
        'position:absolute;top:0;left:0;width:100%;height:100%;z-index:10;cursor:crosshair;touch-action:none;';
 * to avoid Blazor Server SignalR round-trips for high-frequency mouse events.
window.canvasViewport = {
    _instance: null,

    self._getRect = function () {
        if (self._rectDirty || !self._cachedRect) {
            self._cachedRect = self.eventLayer.getBoundingClientRect();
            self._rectDirty = false;
        }
        return self._cachedRect;
    };

    self._invalidateRect = function () {
        self._rectDirty = true;
    };

    init: function (dotNetRef, viewportEl, rendererEl, canvasWidth, canvasHeight, vpWidth, vpHeight) {
        var rect = self._getRect();
            this._instance.destroy();
        }
        this._instance = new CanvasViewportController(dotNetRef, viewportEl, rendererEl, canvasWidth, canvasHeight, vpWidth, vpHeight);
    },

    fitCanvas: function (vpWidth, vpHeight) {
        if (this._instance) this._instance.fit(vpWidth, vpHeight);
    },

    dispose: function () {
        if (this._instance) {
            this._instance.destroy();
            this._instance = null;
        }
    }
};

function CanvasViewportController(dotNetRef, viewportEl, rendererEl, cw, ch, vpW, vpH) {
    // GPU-accelerated: use translate3d + scale instead of left/top/width/height
    var self = this;
        self.renderer.style.transform =
            'translate3d(' + self.ox + 'px,' + self.oy + 'px,0) scale(' + self.scale + ')';
    };

    self._scheduleFrame = function () {
        if (!self._rafScheduled) {
            self._rafScheduled = true;
            self._rafId = requestAnimationFrame(self._onFrame);
        }
    self.vpW = vpW;
    self.vpH = vpH;
    // Pending hover pixel for the next frame
    self._pendingHover = null;
    self._hoverVisible = false;

    self._onFrame = function () {
        self._rafScheduled = false;
        self.applyTransform();
        self._updateOverlays();
    };

    self._updateOverlays = function () {
        // Click indicator
        if (self.clickedPx) {
            self.clickEl.style.display = 'block';
            var cx = self.ox + self.clickedPx.x * self.scale;
            var cy = self.oy + self.clickedPx.y * self.scale;
            var cs = self.scale;
            self.clickEl.style.transform = 'translate3d(' + cx + 'px,' + cy + 'px,0)';
            self.clickEl.style.width = cs + 'px';
            self.clickEl.style.height = cs + 'px';
        } else {
            self.clickEl.style.display = 'none';
        }

        // Hover indicator
        var p = self._pendingHover;
        if (p) {
            self.hoverEl.style.display = 'block';
            var hx = self.ox + p.x * self.scale;
            var hy = self.oy + p.y * self.scale;
            var hs = self.scale;
            self.hoverEl.style.transform = 'translate3d(' + hx + 'px,' + hy + 'px,0)';
            self.hoverEl.style.width = hs + 'px';
            self.hoverEl.style.height = hs + 'px';
            self.coordsEl.style.display = 'block';
            self.coordsEl.textContent = 'X: ' + p.x + ', Y: ' + p.y;
            self._hoverVisible = true;
        } else if (self._hoverVisible) {
            self.hoverEl.style.display = 'none';
            self.coordsEl.style.display = 'none';
            self._hoverVisible = false;
        }
    self.dragging = false;
    self.dragMoved = false;
    // ── Mouse event handlers ──
    self.lastY = 0;
    self.startX = 0;
        if (e.button !== 0) return;
        e.preventDefault();
        self.dragging = true;
        self.dragMoved = false;
        self.lastX = e.clientX;
        self.lastY = e.clientY;
        self.startX = e.clientX;
        self.startY = e.clientY;
        self.eventLayer.style.cursor = 'grabbing';

        // Listen on document so drag continues even outside viewport
        document.addEventListener('mousemove', self._onDocMove, { passive: true });
        document.addEventListener('mouseup', self._onDocUp);
        'position:absolute;box-shadow:inset 0 0 0 1px rgba(31,76,145,0.6);pointer-events:none;z-index:5;display:none;';
    viewportEl.appendChild(self.hoverEl);
    self._onDocMove = function (e) {
        if (!self.dragging) return;
        self.ox += e.clientX - self.lastX;
        self.oy += e.clientY - self.lastY;
        self.clamp();
        self.lastX = e.clientX;
        self.lastY = e.clientY;
        if (!self.dragMoved && Math.abs(e.clientX - self.startX) + Math.abs(e.clientY - self.startY) > 4) {
            self.dragMoved = true;
        "border:1px solid rgba(45,114,221,0.26);box-shadow:0 10px 22px rgba(29,83,169,0.12);" +
        self._pendingHover = self.pixelAt(e.clientX, e.clientY);
        self._scheduleFrame();
        var ly = (my - self.oy) / self.scale;
        var px = Math.floor(lx);
    self._onDocUp = function (e) {
        document.removeEventListener('mousemove', self._onDocMove);
        document.removeEventListener('mouseup', self._onDocUp);

        if (px >= 0 && px < self.cw && py >= 0 && py < self.ch) return { x: px, y: py };
    };

    self.clamp = function () {
                self._scheduleFrame();
        var rw = self.cw * self.scale;
        var rh = self.ch * self.scale;
        self.ox = Math.max(m - rw, Math.min(self.vpW - m, self.ox));
        self.oy = Math.max(m - rh, Math.min(self.vpH - m, self.oy));
    };

    self.applyTransform = function () {
    self._onMove = function (e) {
        // Hover-only (non-drag) moves on the event layer
        if (self.dragging) return; // drag is handled by _onDocMove
        self._pendingHover = self.pixelAt(e.clientX, e.clientY);
        self._scheduleFrame();
    };

        var s = self.renderer.style;
        if (!self.dragging) {
            self._pendingHover = null;
            self._scheduleFrame();
        }
    };

    self.showClick = function () {
        if (!self.clickedPx) { self.clickEl.style.display = 'none'; return; }
        self.clickEl.style.display = 'block';
        self.clickEl.style.left = (self.ox + self.clickedPx.x * self.scale) + 'px';
        self.clickEl.style.top = (self.oy + self.clickedPx.y * self.scale) + 'px';
        var rect = self._getRect();
        self.clickEl.style.height = self.scale + 'px';
    };

    // ── Event handlers ──

    self._onDown = function (e) {
        if (e.button === 0) {
            self.dragging = true;
            self.dragMoved = false;
        self._invalidateRect(); // zoom changes the visual transform, rect may shift

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
            self.startX = e.clientX;
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
            self.dragging = true;
            self.dragMoved = false;
            self.lastX = first.clientX;
            self.lastY = first.clientY;
            self.startX = first.clientX;
            self.startY = first.clientY;
        } else if (keys.length >= 2) {
            var t0 = self._activeTouches[keys[0]];
            var t1 = self._activeTouches[keys[1]];
            self._lastPinchDist = self._touchDist(t0, t1);
            self._lastPinchMid = self._touchMid(t0, t1);
            self.dragging = false;
        }
    };

    self._onTouchMove = function (e) {
        e.preventDefault();
        for (var i = 0; i < e.changedTouches.length; i++) {
            var t = e.changedTouches[i];
            self._activeTouches[t.identifier] = { clientX: t.clientX, clientY: t.clientY };
        }
        var keys = Object.keys(self._activeTouches);
        if (keys.length === 1 && self.dragging) {
            var cur = self._activeTouches[keys[0]];
            self.ox += cur.clientX - self.lastX;
            self.oy += cur.clientY - self.lastY;
            self.clamp();
            self.lastX = cur.clientX;
            self.lastY = cur.clientY;
            if (!self.dragMoved && Math.abs(cur.clientX - self.startX) + Math.abs(cur.clientY - self.startY) > 4) {
                self.dragMoved = true;
            }
            self._pendingHover = self.pixelAt(cur.clientX, cur.clientY);
            self._scheduleFrame();
        } else if (keys.length >= 2) {
            var t0 = self._activeTouches[keys[0]];
            var t1 = self._activeTouches[keys[1]];
            var dist = self._touchDist(t0, t1);
            var mid = self._touchMid(t0, t1);

            if (self._lastPinchDist > 0) {
                var factor = dist / self._lastPinchDist;
                var ns = Math.max(self.minScale, Math.min(self.maxScale, self.scale * factor));

                var rect = self._getRect();
                var mx = mid.x - rect.left;
                var my = mid.y - rect.top;
                var wx = (mx - self.ox) / self.scale;
                var wy = (my - self.oy) / self.scale;

                self.scale = ns;
                self.ox = mx - wx * ns;
                self.oy = my - wy * ns;

                // Also pan with midpoint movement
                if (self._lastPinchMid) {
                    self.ox += mid.x - self._lastPinchMid.x;
                    self.oy += mid.y - self._lastPinchMid.y;
                }
                self.clamp();
                self._invalidateRect();
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
            if (self.dragging && !self.dragMoved) {
                var p = self.pixelAt(self.lastX, self.lastY);
                if (p) {
                    self.clickedPx = p;
                    self._scheduleFrame();
                    self.dotNet.invokeMethodAsync('OnPixelClicked', p.x, p.y);
                }
            }
            self.dragging = false;
            self._lastPinchDist = 0;
            self._lastPinchMid = null;
            self._pendingHover = null;
            self._scheduleFrame();
        } else if (remaining === 1) {
            // Switch back to single-finger pan
            var key = Object.keys(self._activeTouches)[0];
            var t = self._activeTouches[key];
            self.dragging = true;
            self.dragMoved = true; // already moved via pinch, don't fire click
            self.lastX = t.clientX;
            self.lastY = t.clientY;
            self._lastPinchDist = 0;
            self._lastPinchMid = null;
            self.oy += e.clientY - self.lastY;
            self.clamp();

    // ── Rect invalidation on scroll/resize ──
    self._onScrollOrResize = function () { self._invalidateRect(); };
            self.lastX = e.clientX;
            self.lastY = e.clientY;
            if (Math.abs(e.clientX - self.startX) + Math.abs(e.clientY - self.startY) > 4) {
    self.eventLayer.addEventListener('mousemove', self._onMove, { passive: true });
            self.applyTransform();
            self.showClick();
        }
    self.eventLayer.addEventListener('touchstart', self._onTouchStart, { passive: false });
    self.eventLayer.addEventListener('touchmove', self._onTouchMove, { passive: false });
    self.eventLayer.addEventListener('touchend', self._onTouchEnd);
    self.eventLayer.addEventListener('touchcancel', self._onTouchEnd);

    window.addEventListener('scroll', self._onScrollOrResize, { passive: true });
    window.addEventListener('resize', self._onScrollOrResize, { passive: true });


        // Update hover indicator
        var p = self.pixelAt(e.clientX, e.clientY);
        if (p) {
            self.hoverEl.style.display = 'block';
            self.hoverEl.style.left = (self.ox + p.x * self.scale) + 'px';
            self.hoverEl.style.top = (self.oy + p.y * self.scale) + 'px';
    this._invalidateRect();
            self.hoverEl.style.width = self.scale + 'px';
            self.hoverEl.style.height = self.scale + 'px';
            self.coordsEl.style.display = 'block';
            self.coordsEl.textContent = 'X: ' + p.x + ', Y: ' + p.y;
        } else {
            self.hoverEl.style.display = 'none';
            self.coordsEl.style.display = 'none';
        }
    };

    this._updateOverlays();
        if (self.dragging && !self.dragMoved) {
            // Click (not a drag) — notify Blazor server
            var p = self.pixelAt(e.clientX, e.clientY);
    if (this._rafId) cancelAnimationFrame(this._rafId);

    // Remove document-level listeners that may still be attached from a drag
    document.removeEventListener('mousemove', this._onDocMove);
    document.removeEventListener('mouseup', this._onDocUp);

    window.removeEventListener('scroll', this._onScrollOrResize);
    window.removeEventListener('resize', this._onScrollOrResize);

            if (p) {
                self.clickedPx = p;
                self.showClick();
            }
        }
        this.eventLayer.removeEventListener('touchstart', this._onTouchStart);
        this.eventLayer.removeEventListener('touchmove', this._onTouchMove);
        this.eventLayer.removeEventListener('touchend', this._onTouchEnd);
        this.eventLayer.removeEventListener('touchcancel', this._onTouchEnd);
        self.dragging = false;
        self.eventLayer.style.cursor = 'crosshair';
    };

    self._onOut = function () {
        self.dragging = false;
        self.hoverEl.style.display = 'none';
        self.coordsEl.style.display = 'none';
        self.eventLayer.style.cursor = 'crosshair';
    };

    self._onWheel = function (e) {
        e.preventDefault();
        var factor = e.deltaY < 0 ? 1.1 : 0.9;
        var ns = Math.max(self.minScale, Math.min(self.maxScale, self.scale * factor));

        var rect = self.eventLayer.getBoundingClientRect();
        var mx = e.clientX - rect.left;
        var my = e.clientY - rect.top;
        var wx = (mx - self.ox) / self.scale;
        var wy = (my - self.oy) / self.scale;

        self.scale = ns;
        self.ox = mx - wx * ns;
        self.oy = my - wy * ns;
        self.clamp();
        self.applyTransform();
        self.showClick();

        // Update hover position after zoom
        var p = self.pixelAt(e.clientX, e.clientY);
        if (p) {
            self.hoverEl.style.left = (self.ox + p.x * self.scale) + 'px';
            self.hoverEl.style.top = (self.oy + p.y * self.scale) + 'px';
            self.hoverEl.style.width = self.scale + 'px';
            self.hoverEl.style.height = self.scale + 'px';
            self.coordsEl.textContent = 'X: ' + p.x + ', Y: ' + p.y;
        }
    };

    // ── Attach events ──
    self.eventLayer.addEventListener('mousedown', self._onDown);
    self.eventLayer.addEventListener('mousemove', self._onMove);
    self.eventLayer.addEventListener('mouseup', self._onUp);
    self.eventLayer.addEventListener('mouseout', self._onOut);
    self.eventLayer.addEventListener('wheel', self._onWheel, { passive: false });

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
    this.applyTransform();
    this.showClick();
};

CanvasViewportController.prototype.destroy = function () {
    if (this.eventLayer) {
        this.eventLayer.removeEventListener('mousedown', this._onDown);
        this.eventLayer.removeEventListener('mousemove', this._onMove);
        this.eventLayer.removeEventListener('mouseup', this._onUp);
        this.eventLayer.removeEventListener('mouseout', this._onOut);
        this.eventLayer.removeEventListener('wheel', this._onWheel);
        this.eventLayer.remove();
    }
    if (this.hoverEl) this.hoverEl.remove();
    if (this.clickEl) this.clickEl.remove();
    if (this.coordsEl) this.coordsEl.remove();
};

