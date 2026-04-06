/**
 * Canvas Viewport Controller
 * Handles all pan, zoom, hover, and click interactions entirely client-side
 * to avoid Blazor Server SignalR round-trips for high-frequency mouse events.
 */
window.canvasViewport = {
    _instance: null,

    init: function (dotNetRef, viewportEl, rendererEl, canvasWidth, canvasHeight, vpWidth, vpHeight) {
        if (this._instance) {
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
    var self = this;
    self.dotNet = dotNetRef;
    self.viewport = viewportEl;
    self.renderer = rendererEl;
    self.cw = cw;
    self.ch = ch;
    self.vpW = vpW;
    self.vpH = vpH;

    self.scale = 1;
    self.ox = 0;
    self.oy = 0;
    self.minScale = 0.1;
    self.maxScale = 50;

    self.dragging = false;
    self.dragMoved = false;
    self.lastX = 0;
    self.lastY = 0;
    self.startX = 0;
    self.startY = 0;

    self.clickedPx = null;

    // ── Create overlay UI elements ──

    self.hoverEl = document.createElement('div');
    self.hoverEl.className = 'canvas-hover-indicator';
    self.hoverEl.style.cssText =
        'position:absolute;box-shadow:inset 0 0 0 1px rgba(31,76,145,0.6);pointer-events:none;z-index:5;display:none;';
    viewportEl.appendChild(self.hoverEl);

    self.clickEl = document.createElement('div');
    self.clickEl.className = 'canvas-click-indicator';
    self.clickEl.style.cssText =
        'position:absolute;box-shadow:inset 0 0 0 2px var(--accent-3);pointer-events:none;z-index:6;display:none;';
    viewportEl.appendChild(self.clickEl);

    self.coordsEl = document.createElement('div');
    self.coordsEl.className = 'canvas-coordinates';
    self.coordsEl.style.cssText =
        "position:absolute;top:10px;right:310px;z-index:10000;pointer-events:none;" +
        "background:rgba(255,255,255,0.72);color:#1a4f9e;padding:6px 12px;border-radius:11px;" +
        "border:1px solid rgba(45,114,221,0.26);box-shadow:0 10px 22px rgba(29,83,169,0.12);" +
        "font-weight:700;backdrop-filter:blur(8px);font-family:'Sora',sans-serif;display:none;";
    viewportEl.appendChild(self.coordsEl);

    self.eventLayer = document.createElement('div');
    self.eventLayer.style.cssText =
        'position:absolute;top:0;left:0;width:100%;height:100%;z-index:10;cursor:crosshair;';
    viewportEl.appendChild(self.eventLayer);

    // ── Helpers ──

    self.pixelAt = function (clientX, clientY) {
        var rect = self.eventLayer.getBoundingClientRect();
        var mx = clientX - rect.left;
        var my = clientY - rect.top;
        var lx = (mx - self.ox) / self.scale;
        var ly = (my - self.oy) / self.scale;
        var px = Math.floor(lx);
        var py = Math.floor(ly);
        if (px >= 0 && px < self.cw && py >= 0 && py < self.ch) return { x: px, y: py };
        return null;
    };

    self.clamp = function () {
        var m = 50;
        var rw = self.cw * self.scale;
        var rh = self.ch * self.scale;
        self.ox = Math.max(m - rw, Math.min(self.vpW - m, self.ox));
        self.oy = Math.max(m - rh, Math.min(self.vpH - m, self.oy));
    };

    self.applyTransform = function () {
        var s = self.renderer.style;
        s.left = self.ox + 'px';
        s.top = self.oy + 'px';
        s.width = (self.cw * self.scale) + 'px';
        s.height = (self.ch * self.scale) + 'px';
    };

    self.showClick = function () {
        if (!self.clickedPx) { self.clickEl.style.display = 'none'; return; }
        self.clickEl.style.display = 'block';
        self.clickEl.style.left = (self.ox + self.clickedPx.x * self.scale) + 'px';
        self.clickEl.style.top = (self.oy + self.clickedPx.y * self.scale) + 'px';
        self.clickEl.style.width = self.scale + 'px';
        self.clickEl.style.height = self.scale + 'px';
    };

    // ── Event handlers ──

    self._onDown = function (e) {
        if (e.button === 0) {
            self.dragging = true;
            self.dragMoved = false;
            self.lastX = e.clientX;
            self.lastY = e.clientY;
            self.startX = e.clientX;
            self.startY = e.clientY;
            self.eventLayer.style.cursor = 'grabbing';
        }
    };

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
        }

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
    };

    self._onUp = function (e) {
        if (self.dragging && !self.dragMoved) {
            // Click (not a drag) — notify Blazor server
            var p = self.pixelAt(e.clientX, e.clientY);
            if (p) {
                self.clickedPx = p;
                self.showClick();
                self.dotNet.invokeMethodAsync('OnPixelClicked', p.x, p.y);
            }
        }
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

