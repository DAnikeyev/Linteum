window.canvasRenderer = {
    canvas: null,
    overlay: null,
    ctx: null,
    overlayCtx: null,
    width: 0,
    height: 0,
    committedImageData: null,
    ripples: [],
    hoverPixel: null,
    clickPixel: null,
    hoverStrokeStyle: 'rgba(55,140,255,1)',
    clickStrokeStyle: 'rgba(55,140,255,1)',
    hoverLineWidth: 0.08,
    clickLineWidth: 0.12,
    clickPreviewMode: 'blink-contrast',
    clickPreviewColor: null,
    clickBlinkPeriodMs: 1500,
    clickBlinkActiveFraction: 0.32,
    clickBlinkMaxAlpha: 0.92,
    clickContrastBrightnessThreshold: 145,
    clickPreviewStartedAt: 0,
    suppressedRipples: new Map(),
    animationFrameId: null,
    suppressedRippleTtlMs: 2000,

    getPixelKey: function (x, y, color) {
        return `${x}:${y}:${color}`;
    },

    rememberSuppressedRipple: function (pixel, now) {
        this.suppressedRipples.set(this.getPixelKey(pixel.x, pixel.y, pixel.color), now);
    },

    shouldSkipRipple: function (pixel, now) {
        const key = this.getPixelKey(pixel.x, pixel.y, pixel.color);
        const suppressedAt = this.suppressedRipples.get(key);
        if (suppressedAt === undefined) {
            return false;
        }

        if (now - suppressedAt > this.suppressedRippleTtlMs) {
            this.suppressedRipples.delete(key);
            return false;
        }

        this.suppressedRipples.delete(key);
        return true;
    },

    pruneSuppressedRipples: function (now) {
        for (const [key, suppressedAt] of this.suppressedRipples.entries()) {
            if (now - suppressedAt > this.suppressedRippleTtlMs) {
                this.suppressedRipples.delete(key);
            }
        }
    },

    arePixelsEqual: function (left, right) {
        if (left === right) {
            return true;
        }

        if (!left || !right) {
            return false;
        }

        return left.x === right.x && left.y === right.y;
    },

    disableImageSmoothing: function (context) {
        if (!context) {
            return;
        }

        context.imageSmoothingEnabled = false;

        if ('webkitImageSmoothingEnabled' in context) {
            context.webkitImageSmoothingEnabled = false;
        }

        if ('mozImageSmoothingEnabled' in context) {
            context.mozImageSmoothingEnabled = false;
        }

        if ('msImageSmoothingEnabled' in context) {
            context.msImageSmoothingEnabled = false;
        }

        if ('oImageSmoothingEnabled' in context) {
            context.oImageSmoothingEnabled = false;
        }

        if ('imageSmoothingQuality' in context) {
            context.imageSmoothingQuality = 'low';
        }
    },

    setInteractionState: function (state) {
        const nextHoverPixel = state && state.hoverPixel ? state.hoverPixel : null;
        const nextClickPixel = state && state.clickPixel ? state.clickPixel : null;
        const nextClickPreviewMode = state && state.clickPreviewMode ? state.clickPreviewMode : 'blink-contrast';
        const nextClickPreviewColor = state && state.clickPreviewColor ? state.clickPreviewColor : null;
        const nextClickBlinkPeriodMs = state && state.clickBlinkPeriodMs ? state.clickBlinkPeriodMs : 1500;

        if (!this.arePixelsEqual(this.clickPixel, nextClickPixel)
            || this.clickPreviewMode !== nextClickPreviewMode
            || this.clickPreviewColor !== nextClickPreviewColor) {
            this.clickPreviewStartedAt = performance.now();
        }

        this.hoverPixel = nextHoverPixel;
        this.clickPixel = nextClickPixel;
        this.hoverStrokeStyle = state && state.hoverColor ? state.hoverColor : 'rgba(55,140,255,1)';
        this.clickStrokeStyle = state && state.clickColor ? state.clickColor : 'rgba(55,140,255,1)';
        this.hoverLineWidth = state && state.hoverLineWidth ? state.hoverLineWidth : 0.08;
        this.clickLineWidth = state && state.clickLineWidth ? state.clickLineWidth : 0.12;
        this.clickPreviewMode = nextClickPreviewMode;
        this.clickPreviewColor = nextClickPreviewColor;
        this.clickBlinkPeriodMs = nextClickBlinkPeriodMs;

        if (!this.overlayCtx) {
            return;
        }

        this.renderOverlay(performance.now());

        if (this.shouldAnimateOverlay()) {
            this.ensureAnimation();
            return;
        }

        if (this.animationFrameId) {
            cancelAnimationFrame(this.animationFrameId);
            this.animationFrameId = null;
        }
    },

    normalizeHexColor: function (color) {
        if (typeof color !== 'string') {
            return null;
        }

        const normalizedColor = color.trim();
        if (!/^#?[0-9a-fA-F]{6}$/.test(normalizedColor)) {
            return null;
        }

        return normalizedColor.charAt(0) === '#' ? normalizedColor.toLowerCase() : `#${normalizedColor.toLowerCase()}`;
    },

    rgbToHex: function (red, green, blue) {
        return `#${red.toString(16).padStart(2, '0')}${green.toString(16).padStart(2, '0')}${blue.toString(16).padStart(2, '0')}`;
    },

    getColorBrightness: function (color) {
        const normalized = this.normalizeHexColor(color) || '#ffffff';
        const hex = normalized.substring(1);
        const red = parseInt(hex.substring(0, 2), 16);
        const green = parseInt(hex.substring(2, 4), 16);
        const blue = parseInt(hex.substring(4, 6), 16);
        return (0.299 * red) + (0.587 * green) + (0.114 * blue);
    },

    getNegativeHex: function (color) {
        const normalized = this.normalizeHexColor(color) || '#ffffff';
        const hex = normalized.substring(1);
        const red = 255 - parseInt(hex.substring(0, 2), 16);
        const green = 255 - parseInt(hex.substring(2, 4), 16);
        const blue = 255 - parseInt(hex.substring(4, 6), 16);
        return this.rgbToHex(red, green, blue);
    },

    getCommittedPixelHex: function (x, y) {
        if (!this.committedImageData || x < 0 || y < 0 || x >= this.width || y >= this.height) {
            return '#ffffff';
        }

        const imageData = this.getCommittedPixelData(x, y);
        if (imageData[3] === 0) {
            return '#ffffff';
        }

        return this.rgbToHex(imageData[0], imageData[1], imageData[2]);
    },

    shouldAnimateOverlay: function () {
        return this.ripples.length > 0 || (this.clickPixel !== null && this.clickPreviewMode !== 'solid');
    },

    ensureAnimation: function () {
        if (!this.animationFrameId) {
            this.animationFrameId = requestAnimationFrame(this.animate.bind(this));
        }
    },

    getContrastPreviewHex: function (color) {
        return this.getColorBrightness(color) < this.clickContrastBrightnessThreshold
            ? '#ffffff'
            : '#000000';
    },

    getClickBlinkAlpha: function (now) {
        const blinkPeriodMs = Math.max(250, this.clickBlinkPeriodMs || 1000);
        const activeFraction = Math.min(0.49, Math.max(0.1, this.clickBlinkActiveFraction || 0.32));
        const elapsed = Math.max(0, (typeof now === 'number' ? now : performance.now()) - this.clickPreviewStartedAt);
        const phase = (elapsed % blinkPeriodMs) / blinkPeriodMs;

        if (phase >= activeFraction) {
            return 0;
        }

        const localPhase = phase / activeFraction;
        const pulse = Math.sin(localPhase * Math.PI);
        return pulse * this.clickBlinkMaxAlpha;
    },

    resolveClickPreviewStyle: function (now) {
        if (!this.clickPixel) {
            return null;
        }

        if (this.clickPreviewMode === 'solid') {
            return {
                fillStyle: this.normalizeHexColor(this.clickPreviewColor) || '#ffffff',
                alpha: 1
            };
        }

        const currentColor = this.getCommittedPixelHex(this.clickPixel.x, this.clickPixel.y);
        return {
            fillStyle: this.getContrastPreviewHex(currentColor),
            alpha: this.getClickBlinkAlpha(now)
        };
    },

    drawSelectedPixel: function (pixel, previewStyle) {
        if (!this.overlayCtx
            || !pixel
            || !previewStyle
            || !previewStyle.fillStyle
            || previewStyle.alpha <= 0
            || pixel.x < 0
            || pixel.y < 0
            || pixel.x >= this.width
            || pixel.y >= this.height) {
            return;
        }

        this.overlayCtx.save();
        this.overlayCtx.globalAlpha = previewStyle.alpha;
        this.overlayCtx.fillStyle = previewStyle.fillStyle;
        this.overlayCtx.fillRect(pixel.x, pixel.y, 1, 1);
        this.overlayCtx.restore();
    },

    renderOverlay: function (now) {
        if (!this.overlayCtx) {
            return;
        }

        const timestamp = typeof now === 'number' ? now : performance.now();
        const duration = 600;
        const maxRadius = 8;
        const keep = [];

        this.overlayCtx.clearRect(0, 0, this.width, this.height);

        for (let i = 0; i < this.ripples.length; i++) {
            const ripple = this.ripples[i];
            const elapsed = timestamp - ripple.startTime;

            if (elapsed >= duration) {
                continue;
            }

            const progress = elapsed / duration;
            const radius = progress * maxRadius;
            const alpha = 1.0 - progress;

            this.overlayCtx.beginPath();
            this.overlayCtx.arc(ripple.x, ripple.y, radius, 0, 2 * Math.PI);
            this.overlayCtx.globalAlpha = alpha * 0.8;
            this.overlayCtx.strokeStyle = ripple.color;
            this.overlayCtx.lineWidth = 1;
            this.overlayCtx.stroke();

            keep.push(ripple);
        }

        this.overlayCtx.globalAlpha = 1.0;
        this.ripples = keep;

        this.drawSelectedPixel(this.clickPixel, this.resolveClickPreviewStyle(timestamp));
    },

    init: function (canvasElement, overlayElement) {
        this.canvas = canvasElement;
        this.overlay = overlayElement;
        this.ctx = canvasElement.getContext('2d');
        this.disableImageSmoothing(this.ctx);

        this.overlayCtx = overlayElement.getContext('2d');
        this.disableImageSmoothing(this.overlayCtx);

        this.width = canvasElement.width;
        this.height = canvasElement.height;
        this.committedImageData = null;
        this.ripples = [];
        this.hoverPixel = null;
        this.clickPixel = null;
        this.hoverStrokeStyle = 'rgba(55,140,255,1)';
        this.clickStrokeStyle = 'rgba(55,140,255,1)';
        this.hoverLineWidth = 0.08;
        this.clickLineWidth = 0.12;
        this.clickPreviewMode = 'blink-contrast';
        this.clickPreviewColor = null;
        this.clickBlinkPeriodMs = 1500;
        this.clickPreviewStartedAt = 0;
        this.suppressedRipples.clear();
        
        if (this.animationFrameId) {
            cancelAnimationFrame(this.animationFrameId);
            this.animationFrameId = null;
        }

        this.renderOverlay();
    },

    drawLoadedImage: function (img) {
        if (!this.ctx) {
            return;
        }

        this.disableImageSmoothing(this.ctx);
        this.ctx.clearRect(0, 0, this.width, this.height);
        if (img) {
            this.ctx.drawImage(img, 0, 0, this.width, this.height);
        }

        this.committedImageData = this.ctx.getImageData(0, 0, this.width, this.height);
    },

    loadImage: function (imageBytes) {
        if (!this.ctx) return Promise.resolve();

        const blob = new Blob([imageBytes]);
        const url = URL.createObjectURL(blob);
        const img = new Image();
        const renderer = this;

        return new Promise(function (resolve) {
            img.onload = function () {
                renderer.drawLoadedImage(img);
                URL.revokeObjectURL(url);
                resolve();
            };
            img.onerror = function () {
                renderer.drawLoadedImage(null);
                URL.revokeObjectURL(url);
                resolve();
            };
            img.src = url;
        });
    },

    loadImageFromUrl: function (imageUrl, sessionId) {
        if (!this.ctx || !imageUrl) return Promise.resolve();

        const headers = {};
        if (sessionId) {
            headers['Session-Id'] = sessionId;
        }

        const renderer = this;
        return fetch(imageUrl, {
            method: 'GET',
            headers: headers,
            credentials: 'include'
        })
            .then(function (response) {
                if (!response.ok) {
                    throw new Error(`Failed to fetch canvas image. Status ${response.status}`);
                }

                return response.blob();
            })
            .then(function (blob) {
                const url = URL.createObjectURL(blob);
                const img = new Image();

                return new Promise(function (resolve) {
                    img.onload = function () {
                        renderer.drawLoadedImage(img);
                        URL.revokeObjectURL(url);
                        resolve();
                    };

                    img.onerror = function () {
                        renderer.drawLoadedImage(null);
                        URL.revokeObjectURL(url);
                        resolve();
                    };

                    img.src = url;
                });
            });
    },

    filterNonWhitePixels: function (coordinates) {
        if (!this.ctx || !this.committedImageData || !Array.isArray(coordinates) || coordinates.length === 0) {
            return coordinates || [];
        }

        const keep = [];
        for (let i = 0; i < coordinates.length; i++) {
            const coordinate = coordinates[i];
            const imageData = this.getCommittedPixelData(coordinate.x, coordinate.y);
            const isTransparent = imageData[3] === 0;
            const isOpaqueWhite = imageData[0] === 255 && imageData[1] === 255 && imageData[2] === 255 && imageData[3] === 255;
            if (!isTransparent && !isOpaqueWhite) {
                keep.push(coordinate);
            }
        }

        return keep;
    },

    downloadImage: function (fileName, mimeType, quality) {
        if (!this.canvas) {
            return Promise.resolve(false);
        }

        var exportCanvas = document.createElement('canvas');
        exportCanvas.width = this.width || this.canvas.width;
        exportCanvas.height = this.height || this.canvas.height;

        var exportContext = exportCanvas.getContext('2d');
        if (!exportContext) {
            throw new Error('Could not create an export canvas context.');
        }

        exportContext.fillStyle = '#ffffff';
        exportContext.fillRect(0, 0, exportCanvas.width, exportCanvas.height);
        exportContext.drawImage(this.canvas, 0, 0);

        var downloadName = typeof fileName === 'string' && fileName.trim()
            ? fileName.trim()
            : 'canvas.jpg';
        var exportMimeType = typeof mimeType === 'string' && mimeType.trim()
            ? mimeType.trim()
            : 'image/jpeg';
        var exportQuality = typeof quality === 'number' && quality >= 0 && quality <= 1
            ? quality
            : 0.92;

        return new Promise(function (resolve, reject) {
            exportCanvas.toBlob(function (blob) {
                if (!blob) {
                    reject(new Error('Browser failed to generate the export image.'));
                    return;
                }

                var objectUrl = URL.createObjectURL(blob);
                var link = document.createElement('a');
                link.href = objectUrl;
                link.download = downloadName;
                link.style.display = 'none';
                document.body.appendChild(link);
                link.click();
                link.remove();
                setTimeout(function () {
                    URL.revokeObjectURL(objectUrl);
                }, 0);
                resolve(true);
            }, exportMimeType, exportQuality);
        });
    },

    getCommittedPixelData: function (x, y) {
        const index = (y * this.width + x) * 4;
        const data = this.committedImageData.data;
        return [data[index], data[index + 1], data[index + 2], data[index + 3]];
    },

    setCommittedPixelData: function (x, y, color, clear) {
        if (!this.committedImageData) {
            return;
        }

        const index = (y * this.width + x) * 4;
        const data = this.committedImageData.data;
        if (clear) {
            data[index] = 0;
            data[index + 1] = 0;
            data[index + 2] = 0;
            data[index + 3] = 0;
            return;
        }

        const normalizedColor = typeof color === 'string' ? color.trim() : '';
        if (!/^#?[0-9a-fA-F]{6}$/.test(normalizedColor)) {
            return;
        }

        const hex = normalizedColor.charAt(0) === '#' ? normalizedColor.substring(1) : normalizedColor;
        data[index] = parseInt(hex.substring(0, 2), 16);
        data[index + 1] = parseInt(hex.substring(2, 4), 16);
        data[index + 2] = parseInt(hex.substring(4, 6), 16);
        data[index + 3] = 255;
    },

    renderBatch: function (batch, commitState) {
        if (!this.ctx) return;

        const shouldCommitState = commitState !== false;

        const now = performance.now();
        this.pruneSuppressedRipples(now);
        
        // Batch is an array of objects: { x, y, color }
        // Optimization: minimizing context state changes if possible,
        // but for simple pixel updates, this loop is usually fast enough.
        for (let i = 0; i < batch.length; i++) {
            const pixel = batch[i];

            if (pixel.clear) {
                this.ctx.clearRect(pixel.x, pixel.y, 1, 1);
                if (shouldCommitState) {
                    this.setCommittedPixelData(pixel.x, pixel.y, pixel.color, true);
                }
                continue;
            }

            this.ctx.fillStyle = pixel.color;
            this.ctx.fillRect(pixel.x, pixel.y, 1, 1);
            if (shouldCommitState) {
                this.setCommittedPixelData(pixel.x, pixel.y, pixel.color, false);
            }

            if (pixel.suppressRipple) {
                this.rememberSuppressedRipple(pixel, now);
                continue;
            }

            if (this.shouldSkipRipple(pixel, now)) {
                continue;
            }

            this.ripples.push({
                x: pixel.x + 0.5,
                y: pixel.y + 0.5,
                color: pixel.color,
                startTime: now
            });
        }

        if (this.shouldAnimateOverlay()) {
            this.ensureAnimation();
        }
    },

    animate: function () {
        this.animationFrameId = null;
        this.renderOverlay(performance.now());

        if (!this.shouldAnimateOverlay()) {
            return;
        }

        this.ensureAnimation();
    }
};
