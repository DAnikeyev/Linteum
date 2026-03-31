window.canvasRenderer = {
    ctx: null,
    overlayCtx: null,
    width: 0,
    height: 0,
    ripples: [],
    animationFrameId: null,

    init: function (canvasElement, overlayElement) {
        this.ctx = canvasElement.getContext('2d');
        // Ensure pixels stay sharp when zooming in (pixel art style)
        this.ctx.imageSmoothingEnabled = false;
        // Force the browser to use nearest-neighbor interpolation for the canvas element
        canvasElement.style.imageRendering = "pixelated";

        this.overlayCtx = overlayElement.getContext('2d');
        this.overlayCtx.imageSmoothingEnabled = false;
        overlayElement.style.imageRendering = "pixelated";

        this.width = canvasElement.width;
        this.height = canvasElement.height;
        this.ripples = [];
        
        if (this.animationFrameId) {
            cancelAnimationFrame(this.animationFrameId);
            this.animationFrameId = null;
        }
    },

    loadImage: function (imageBytes) {
        if (!this.ctx) return;

        // Create a Blob from the byte array (assumed to be PNG/JPG data)
        const blob = new Blob([imageBytes]);
        const url = URL.createObjectURL(blob);
        const img = new Image();

        img.onload = () => {
            // Draw the image onto the canvas at 0,0
            this.ctx.drawImage(img, 0, 0);
            // Cleanup memory
            URL.revokeObjectURL(url);
        };

        img.src = url;
    },

    renderBatch: function (batch) {
        if (!this.ctx) return;

        const now = performance.now();
        
        // Batch is an array of objects: { x, y, color }
        // Optimization: minimizing context state changes if possible,
        // but for simple pixel updates, this loop is usually fast enough.
        for (let i = 0; i < batch.length; i++) {
            const pixel = batch[i];
            this.ctx.fillStyle = pixel.color;
            this.ctx.fillRect(pixel.x, pixel.y, 1, 1);

            // Add ripple
            this.ripples.push({
                x: pixel.x + 0.5,
                y: pixel.y + 0.5,
                color: pixel.color,
                startTime: now
            });
        }

        if (!this.animationFrameId && this.ripples.length > 0) {
            this.animate();
        }
    },

    animate: function () {
        if (this.ripples.length === 0) {
            this.animationFrameId = null;
            if (this.overlayCtx) {
                this.overlayCtx.clearRect(0, 0, this.width, this.height);
            }
            return;
        }

        this.overlayCtx.clearRect(0, 0, this.width, this.height);
        
        const now = performance.now();
        const duration = 600; // Duration in ms
        const maxRadius = 8; // Max radius in pixels
        const keep = [];

        for (let i = 0; i < this.ripples.length; i++) {
            const ripple = this.ripples[i];
            const elapsed = now - ripple.startTime;

            if (elapsed < duration) {
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
        }

        this.overlayCtx.globalAlpha = 1.0;
        this.ripples = keep;

        this.animationFrameId = requestAnimationFrame(this.animate.bind(this));
    }
};
