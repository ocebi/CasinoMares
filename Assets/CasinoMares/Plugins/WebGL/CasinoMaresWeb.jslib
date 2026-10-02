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

    // The texture data file the page picked ("DXT" or "ASTC"), for the ?debug=1 overlay.
    CM_TextureFormat: function () {
        var format = window.cmTextureFormat || "?";
        var size = lengthBytesUTF8(format) + 1;
        var buffer = _malloc(size);
        stringToUTF8(format, buffer, size);
        return buffer;
    },

    // The mouse cursor over the game: 0 the paw, 1 the circling arrows, 2 the closed paw (classes in
    // TemplateData/style.css).
    CM_SetCursor: function (kind) {
        var canvas = Module.canvas;
        if (!canvas)
            return;
        canvas.classList.toggle("cursor-paw", kind === 0);
        canvas.classList.toggle("cursor-rotate", kind === 1);
        canvas.classList.toggle("cursor-paw-closed", kind === 2);
    },

    // Page text boxes laid over the game's text fields (WebTextInput.cs). They live in #game, so they stay on screen in
    // full screen, and each sits in a form, so the keyboard's Enter key submits it. Positions and sizes come in as
    // shares of the game's width and height.
    $CMText: {
        boxes: [],
        starts: {},

        place: function (box, rect) {
            var height = box.form.parentNode.clientHeight;
            var form = box.form.style;
            form.left = rect[0] * 100 + "%";
            form.top = rect[1] * 100 + "%";
            form.width = rect[2] * 100 + "%";
            form.height = rect[3] * 100 + "%";
            form.display = "block";
            var input = box.input.style;
            input.fontSize = box.font * height + "px";
            input.paddingLeft = box.padLeft * height + "px";
            input.paddingRight = box.padRight * height + "px";
        },

        hide: function (box) {
            box.pendingUntil = 0;
            box.wantFocus = false;
            CMText.release(box);
            box.form.style.display = "none";
        },

        // Hands the keyboard back to the game.
        release: function (box) {
            if (document.activeElement !== box.input)
                return;
            box.input.blur();
            Module.canvas.focus({ preventScroll: true });
        },

        point: function (p) {
            var r = Module.canvas.getBoundingClientRect();
            return [(p.clientX - r.left) / r.width, (p.clientY - r.top) / r.height];
        },

        inside: function (rect, p) {
            return rect && p[0] >= rect[0] && p[0] <= rect[0] + rect[2] && p[1] >= rect[1] && p[1] <= rect[1] + rect[3];
        },

        down: function (e) {
            var points = e.changedTouches || [e];
            for (var i = 0; i < points.length; i++)
                CMText.starts[points[i].identifier === undefined ? "mouse" : points[i].identifier] = CMText.point(points[i]);
        },

        // A tap on a button that opens a field (the Chat pill) focuses its box right here, while the browser still
        // counts it as the player's tap: phones only open the keyboard for that. The game opens the field itself a
        // frame or two later, so until then the box waits, focused but invisible.
        up: function (e) {
            var p = e.changedTouches ? e.changedTouches[0] : e;
            if (!p)
                return;
            var key = p.identifier === undefined ? "mouse" : p.identifier;
            var start = CMText.starts[key];
            delete CMText.starts[key];
            var end = CMText.point(p);
            for (var id = 0; id < CMText.boxes.length; id++) {
                var box = CMText.boxes[id];
                if (!box || !start || !CMText.inside(box.armed, start) || !CMText.inside(box.armed, end))
                    continue;
                if (box.form.style.display != "block") {
                    box.input.style.opacity = "0";
                    CMText.place(box, box.rect || box.armed);
                    // Long enough for a slow frame or two; a tap the game ignored lets the keyboard go again.
                    box.pendingUntil = performance.now() + 1500;
                    setTimeout(function () {
                        if (box.pendingUntil)
                            CMText.hide(box);
                    }, 1550);
                }
                box.input.focus({ preventScroll: true });
                // No mouse events after this tap: the canvas would take the focus back.
                if (e.type == "touchend" && e.cancelable)
                    e.preventDefault();
                return;
            }
        },
    },

    CM_TextInputCreate__deps: ["$CMText"],
    CM_TextInputCreate: function (id, numeric, maxLength, submits) {
        if (CMText.boxes[id])
            return;
        var form = document.createElement("form");
        form.className = "cm-text-form";
        form.noValidate = true;
        var input = document.createElement("input");
        input.className = "cm-text-input";
        input.type = "text";
        input.autocomplete = "off";
        input.spellcheck = false;
        input.setAttribute("enterkeyhint", submits ? "send" : "done");
        if (numeric) {
            input.inputMode = "numeric";
            input.pattern = "[0-9]*";
        }
        if (maxLength > 0)
            input.maxLength = maxLength;
        form.appendChild(input);
        Module.canvas.parentNode.appendChild(form);

        var box = { form: form, input: input, rect: null, armed: null, font: 0, padLeft: 0, padRight: 0,
            changed: false, submitted: false, cancelled: false, wantFocus: false, pendingUntil: 0 };
        input.addEventListener("input", function () { box.changed = true; });
        form.addEventListener("submit", function (e) {
            e.preventDefault();
            box.submitted = true;
            CMText.release(box);
        });
        // Esc, from the page's key guard (index.html): the game never sees keys typed here.
        input.addEventListener("cm-cancel", function () {
            box.cancelled = true;
            CMText.release(box);
        });
        CMText.boxes[id] = box;

        if (CMText.listening)
            return;
        CMText.listening = true;
        document.addEventListener("touchstart", CMText.down, true);
        document.addEventListener("touchend", CMText.up, { capture: true, passive: false });
        document.addEventListener("mousedown", CMText.down, true);
        document.addEventListener("mouseup", CMText.up, true);
        // The font size is in CSS pixels, so it follows the screen (a phone keyboard can shrink the window).
        window.addEventListener("resize", function () {
            CMText.boxes.forEach(function (b) {
                if (b && b.rect && b.form.style.display == "block" && !b.pendingUntil)
                    CMText.place(b, b.rect);
            });
        });
    },

    CM_TextInputPlace__deps: ["$CMText"],
    CM_TextInputPlace: function (id, x, y, width, height, font, padLeft, padRight) {
        var box = CMText.boxes[id];
        if (!box)
            return;
        box.rect = [x, y, width, height];
        box.font = font;
        box.padLeft = padLeft;
        box.padRight = padRight;
        box.pendingUntil = 0;
        box.input.style.opacity = "";
        CMText.place(box, box.rect);
        if (box.wantFocus) {
            box.wantFocus = false;
            box.input.focus({ preventScroll: true });
        }
    },

    CM_TextInputHide__deps: ["$CMText"],
    CM_TextInputHide: function (id) {
        var box = CMText.boxes[id];
        if (box && !(box.pendingUntil > performance.now()))
            CMText.hide(box);
    },

    // Where a tap opens this field (width 0: nowhere).
    CM_TextInputOpenOnTap__deps: ["$CMText"],
    CM_TextInputOpenOnTap: function (id, x, y, width, height) {
        var box = CMText.boxes[id];
        if (box)
            box.armed = width > 0 ? [x, y, width, height] : null;
    },

    CM_TextInputSetText__deps: ["$CMText"],
    CM_TextInputSetText: function (id, text) {
        var box = CMText.boxes[id];
        if (!box)
            return;
        box.input.value = UTF8ToString(text);
        box.changed = false;
    },

    // The text, if the player changed it since the last call (null otherwise).
    CM_TextInputTakeText__deps: ["$CMText"],
    CM_TextInputTakeText: function (id) {
        var box = CMText.boxes[id];
        if (!box || !box.changed)
            return 0;
        box.changed = false;
        var text = box.input.value;
        var size = lengthBytesUTF8(text) + 1;
        var buffer = _malloc(size);
        stringToUTF8(text, buffer, size);
        return buffer;
    },

    CM_TextInputTakeCancel__deps: ["$CMText"],
    CM_TextInputTakeCancel: function (id) {
        var box = CMText.boxes[id];
        if (!box || !box.cancelled)
            return 0;
        box.cancelled = false;
        return 1;
    },

    CM_TextInputTakeSubmit__deps: ["$CMText"],
    CM_TextInputTakeSubmit: function (id) {
        var box = CMText.boxes[id];
        if (!box || !box.submitted)
            return 0;
        box.submitted = false;
        return 1;
    },

    // Focuses the box, or does once it's on screen. Outside a tap a phone may not show its keyboard for this (iPhone
    // doesn't); a desktop browser always takes it.
    CM_TextInputFocus__deps: ["$CMText"],
    CM_TextInputFocus: function (id) {
        var box = CMText.boxes[id];
        if (!box)
            return;
        if (box.form.style.display == "block" && !box.pendingUntil)
            box.input.focus({ preventScroll: true });
        else
            box.wantFocus = true;
    },

    CM_TextInputBlur__deps: ["$CMText"],
    CM_TextInputBlur: function (id) {
        var box = CMText.boxes[id];
        if (!box)
            return;
        box.wantFocus = false;
        CMText.release(box);
    },
});
