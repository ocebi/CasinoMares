// Browser queries for Platform.cs (WebGL builds only).
mergeInto(LibraryManager.library, {
    // Height of the game canvas in CSS pixels, which tracks the phone's physical size better than its pixel count.
    CM_CanvasCssHeight: function () {
        var canvas = Module.canvas;
        return canvas ? canvas.getBoundingClientRect().height : window.innerHeight;
    },

    // True on phones and tablets: the main pointer is a finger. iPadOS Safari reports a desktop user agent, so the
    // user agent alone isn't enough.
    CM_PrefersTouch: function () {
        var coarse = window.matchMedia && window.matchMedia("(pointer: coarse)").matches;
        var noHover = window.matchMedia && window.matchMedia("(hover: none)").matches;
        return coarse || (navigator.maxTouchPoints > 0 && noHover) ? 1 : 0;
    },

    CM_IsPageHidden: function () {
        return document.hidden ? 1 : 0;
    },

    // iPhone Safari has no fullscreen API (iPad does).
    CM_CanFullscreen: function () {
        return document.fullscreenEnabled || document.webkitFullscreenEnabled ? 1 : 0;
    },

    // Opened from the home screen (no browser bars).
    CM_IsStandalone: function () {
        var standalone = window.matchMedia && (window.matchMedia("(display-mode: standalone)").matches ||
            window.matchMedia("(display-mode: fullscreen)").matches);
        return navigator.standalone || standalone ? 1 : 0;
    },
});
