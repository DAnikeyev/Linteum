// Works only if loaded in .net when requested from Blazor

window.canvasHelpers = {
    fillWhite: function (canvas, w, h) {
        if (!canvas) return;
        canvas.width = w;
        canvas.height = h;
        var ctx = canvas.getContext('2d');
        ctx.fillStyle = '#ffffff';
        ctx.fillRect(0, 0, w, h);
    },

    getCanvasPixelCoords: function (canvas, clientX, clientY) {
        if (!canvas) return [-1, -1];
        var rect = canvas.getBoundingClientRect();
        var scaleX = canvas.width / rect.width;
        var scaleY = canvas.height / rect.height;
        var x = Math.floor((clientX - rect.left) * scaleX);
        var y = Math.floor((clientY - rect.top) * scaleY);
        x = Math.max(0, Math.min(canvas.width - 1, x));
        y = Math.max(0, Math.min(canvas.height - 1, y));
        return [x, y];
    },

    /** Returns all layout metrics in a single interop call (avoids multiple eval round-trips). */
    getLayoutMetrics: function () {
        var ww = window.innerWidth;
        var wh = window.visualViewport ? window.visualViewport.height : window.innerHeight;
        var mh = 0, mv = 0;
        var main = document.querySelector('main');
        if (main) {
            var s = getComputedStyle(main);
            mh = (parseFloat(s.paddingLeft) || 0) + (parseFloat(s.paddingRight) || 0);
            mv = (parseFloat(s.paddingTop) || 0) + (parseFloat(s.paddingBottom) || 0);
        }
        var pmEl = document.querySelector('.pixelmanager');
        var pmw = pmEl ? pmEl.offsetWidth : 300;
        return { windowWidth: ww, windowHeight: wh, mainHPad: mh, mainVPad: mv, pixelManagerWidth: pmw };
    },

    getElementSize: function (element) {
        if (!element) return { width: 0, height: 0 };
        var rect = element.getBoundingClientRect();
        var width = element.clientWidth || rect.width || 0;
        var height = element.clientHeight || rect.height || 0;

        // Clamp measured size to what is actually visible in the viewport.
        var vv = window.visualViewport;
        var viewportWidth = vv ? vv.width : window.innerWidth;
        var viewportHeight = vv ? vv.height : window.innerHeight;
        var viewportLeft = vv ? vv.offsetLeft : 0;
        var viewportTop = vv ? vv.offsetTop : 0;

        var visibleWidth = Math.max(0, Math.min(rect.right, viewportLeft + viewportWidth) - Math.max(rect.left, viewportLeft));
        var visibleHeight = Math.max(0, Math.min(rect.bottom, viewportTop + viewportHeight) - Math.max(rect.top, viewportTop));

        if (visibleWidth > 0) {
            width = Math.min(width || visibleWidth, visibleWidth);
        }
        if (visibleHeight > 0) {
            height = Math.min(height || visibleHeight, visibleHeight);
        }

        return { width: Math.max(0, width), height: Math.max(0, height) };
    },

    registerResizeListener: function (dotNetHelper) {
        var listener = function () {
            dotNetHelper.invokeMethodAsync('OnWindowResize');
        };
        window.addEventListener('resize', listener);
        return listener;
    },

    unregisterResizeListener: function (listener) {
        window.removeEventListener('resize', listener);
    }
};

window.sidebarHelpers = {
    setSidebarMargin: function (margin) {
        document.documentElement.style.setProperty('--sidebar-margin', margin);
    }
};

