window.canvasRenderer = {
    ctx: null,

    init: function (canvasElement) {
        this.ctx = canvasElement.getContext('2d');
        // Ensure pixels stay sharp when zooming in (pixel art style)
        this.ctx.imageSmoothingEnabled = false;
        // Force the browser to use nearest-neighbor interpolation for the canvas element
        canvasElement.style.imageRendering = "pixelated";
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

        // Batch is an array of objects: { x, y, color }
        // Optimization: minimizing context state changes if possible,
        // but for simple pixel updates, this loop is usually fast enough.
        for (let i = 0; i < batch.length; i++) {
            const pixel = batch[i];
            this.ctx.fillStyle = pixel.color;
            this.ctx.fillRect(pixel.x, pixel.y, 1, 1);
        }
    }
};
