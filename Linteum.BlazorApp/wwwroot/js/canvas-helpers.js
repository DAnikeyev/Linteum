// Works only if loaded in .net when requested from Blazor

window.canvasHelpers = {
    fillWhite: function (canvas, w, h) {
        if (!canvas) return;
        // ensure internal drawing buffer matches desired size
        canvas.width = w;
        canvas.height = h;
        var ctx = canvas.getContext('2d');
        ctx.fillStyle = '#ffffff';
        ctx.fillRect(0, 0, w, h);
    },

    getCanvasPixelCoords: function (canvas, clientX, clientY) {
        if (!canvas) return [-1, -1];
        var rect = canvas.getBoundingClientRect();
        // account for CSS scaling between element size and internal buffer
        var scaleX = canvas.width / rect.width;
        var scaleY = canvas.height / rect.height;
        var x = Math.floor((clientX - rect.left) * scaleX);
        var y = Math.floor((clientY - rect.top) * scaleY);
        // clamp
        x = Math.max(0, Math.min(canvas.width - 1, x));
        y = Math.max(0, Math.min(canvas.height - 1, y));
        return [x, y];
    }
};
