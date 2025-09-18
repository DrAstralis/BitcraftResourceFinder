/*! ImageCrop512: fixed 512×512 export with file/URL/clipboard + zoom/pan
    Vanilla JS. No dependencies. Auto-detects host <form> or use data-form / formSelector.
*/
class ImageCrop512 {
    constructor(container, opts) {
        this.el = container;
        this.opts = Object.assign({
            formSelector: null,   // optional; will fall back to container.closest('form')
            fieldName: "file",    // "file" (Admin Edit) or "image" (New)
            proxyUrl: null,       // e.g. "/image-proxy?url="
            previewSize: 128,
            cropCssSize: null
        }, opts || {});
        this._initDOM();
        this._bind();
        this._resetState();
    }

    /* ---------- DOM ---------- */
    _initDOM() {
        this.el.classList.add("ic512");
        this.el.innerHTML = `
      <div class="ic512-toolbar">
        <div class="ic512-row">
          <label class="ic512-btn">
            <input type="file" accept="image/*" class="ic512-file" hidden>
            Upload from file
          </label>
          <div class="ic512-url">
            <input type="text" class="ic512-url-input" placeholder="Paste image URL…">
            <button type="button" class="ic512-btn ic512-load-url">Load URL</button>
          </div>
          <button type="button" class="ic512-btn ic512-paste-help" title="Press Ctrl/Cmd+V to paste from clipboard">
            📋 Paste from clipboard
          </button>
          <div class="ic512-spacer"></div>
          <button type="button" class="ic512-btn ic512-reset" disabled>Reset</button>
        </div>
        <div class="ic512-row ic512-hint">
          <span>Tip: Use mouse wheel / pinch to <strong>zoom</strong>. Hold <kbd>Space</kbd> to <strong>pan</strong>.</span>
          <span class="ic512-error" hidden></span>
        </div>
      </div>
      <div class="ic512-stage">
        <canvas class="ic512-canvas" aria-label="Image crop canvas"></canvas>
        <div class="ic512-crop-overlay" aria-label="Fixed 512×512 crop box" role="region" tabindex="0"></div>
        <canvas class="ic512-preview" width="${this.opts.previewSize}" height="${this.opts.previewSize}" aria-label="Live preview"></canvas>
      </div>
      <div class="ic512-actions">
        <button type="button" class="ic512-btn ic512-use" disabled>Use 512×512 PNG</button>
      </div>
    `;
        this.$file = this.el.querySelector(".ic512-file");
        this.$url = this.el.querySelector(".ic512-url-input");
        this.$loadUrl = this.el.querySelector(".ic512-load-url");
        this.$reset = this.el.querySelector(".ic512-reset");
        this.$use = this.el.querySelector(".ic512-use");
        this.$error = this.el.querySelector(".ic512-error");
        this.$canvas = this.el.querySelector(".ic512-canvas");
        this.$stage = this.el.querySelector(".ic512-stage");
        this.$preview = this.el.querySelector(".ic512-preview");
        this.$overlay = this.el.querySelector(".ic512-crop-overlay");
        this.ctx = this.$canvas.getContext("2d", { alpha: true, desynchronized: true });
        this.pctx = this.$preview.getContext("2d", { alpha: true, desynchronized: true });
        this.cropCssSize = this.opts.cropCssSize;

        // Find host form:
        // 1) explicit selector -> document.querySelector
        // 2) else nearest ancestor form -> container.closest('form')
        // If not found, don't throw; we just won't intercept submit.
        this.$form =
            (this.opts.formSelector && document.querySelector(this.opts.formSelector)) ||
            this.el.closest("form") ||
            null;

        if (!this.$form) {
            console.warn(
                "ImageCrop512: host <form> not found. Place the widget inside a form or pass data-form/formSelector. " +
                "Submit interception is disabled; 'Use 512×512 PNG' will still export."
            );
        } else if (!this.opts.fieldName) {
            // fieldName from data-field if possible
            const df = this.el.dataset.field;
            this.opts.fieldName = df || "file";
        }
    }

    /* ---------- State ---------- */
    _resetState() {
        this.img = null;
        this.imgW = 0; this.imgH = 0;
        this.scale = 1; this.tx = 0; this.ty = 0;
        this.cropX = 0; this.cropY = 0;
        this.cropSize = this.cropCssSize || 300;
        this.dragging = null;
        this.spaceDown = false;
        this.pinch = null;
        this.dpr = Math.max(1, window.devicePixelRatio || 1);
        this._drawEmpty();
        this._setEnabled(false);
    }

    _setEnabled(enabled) {
        this.$use.disabled = !enabled;
        this.$reset.disabled = !enabled;
    }

    /* ---------- Bind events ---------- */
    _bind() {
        this.el.querySelector(".ic512-btn input.ic512-file")?.addEventListener("change", (e) => {
            const f = e.target.files && e.target.files[0];
            if (f) this._loadFromFile(f);
        });
        this.$loadUrl.addEventListener("click", () => this._loadFromUrl(this.$url.value.trim()));
        this.$url.addEventListener("keydown", (e) => { if (e.key === "Enter") this.$loadUrl.click(); });
        this.$reset.addEventListener("click", () => this._resetState());

        window.addEventListener("paste", (e) => this._onPaste(e));

        const ro = new ResizeObserver(() => this._onResize());
        ro.observe(this.$stage);
        this._ro = ro;

        this.$overlay.addEventListener("pointerdown", (e) => this._onPointerDown(e));
        window.addEventListener("pointermove", (e) => this._onPointerMove(e));
        window.addEventListener("pointerup", (e) => this._onPointerUp(e));
        this.$canvas.addEventListener("wheel", (e) => this._onWheel(e), { passive: false });

        this.$overlay.addEventListener("keydown", (e) => this._onKey(e));
        window.addEventListener("keydown", (e) => { if (e.code === "Space") { this.spaceDown = true; } });
        window.addEventListener("keyup", (e) => { if (e.code === "Space") { this.spaceDown = false; if (this.dragging === "pan") this.dragging = null; } });

        this.$canvas.addEventListener("touchstart", (e) => this._onTouchStart(e), { passive: false });
        this.$canvas.addEventListener("touchmove", (e) => this._onTouchMove(e), { passive: false });
        this.$canvas.addEventListener("touchend", (e) => this._onTouchEnd(e));
        this.$canvas.addEventListener("touchcancel", (e) => this._onTouchEnd(e));

        // Only bind submit interception if a host form exists
        if (this.$form) {
            this.$form.addEventListener("submit", (e) => this._onFormSubmit(e));
        }

        this.$use.addEventListener("click", () => this._exportClicked());
    }

    /* ---------- Loading ---------- */
    async _loadFromFile(file) {
        try {
            this._err();
            const bmp = await this._blobToImageBitmap(file);
            this._setImage(bmp, file.type);
        } catch (err) { this._err(err.message || String(err)); }
    }
    async _loadFromUrl(url) {
        try {
            this._err();
            if (!url) throw new Error("Enter an image URL");
            let resp;
            if (this.opts.proxyUrl) {
                const prox = this.opts.proxyUrl + encodeURIComponent(url);
                resp = await fetch(prox, { credentials: "same-origin" });
            } else {
                resp = await fetch(url, { mode: "cors" });
            }
            if (!resp.ok) throw new Error(`Fetch failed (${resp.status})`);
            const ct = resp.headers.get("Content-Type") || "";
            if (!ct.startsWith("image/")) throw new Error("URL is not an image");
            const blob = await resp.blob();
            const bmp = await this._blobToImageBitmap(blob);
            this._setImage(bmp, blob.type);
        } catch (err) { this._err(err.message || String(err)); }
    }
    async _onPaste(e) {
        if (!e.clipboardData) return;
        this._err();
        const items = Array.from(e.clipboardData.items || []);
        const imgItem = items.find(it => it.type && it.type.startsWith("image/"));
        if (imgItem) {
            e.preventDefault();
            const blob = imgItem.getAsFile();
            if (blob) {
                const bmp = await this._blobToImageBitmap(blob);
                this._setImage(bmp, blob.type);
            }
            return;
        }
        const text = e.clipboardData.getData("text/plain");
        if (text && /^https?:\/\//i.test(text.trim())) {
            e.preventDefault();
            this.$url.value = text.trim();
            this._loadFromUrl(text.trim());
        }
    }

    async _blobToImageBitmap(blob) {
        if ("createImageBitmap" in window) {
            try { return await createImageBitmap(blob, { premultiplyAlpha: "none" }); }
            catch { }
        }
        const url = URL.createObjectURL(blob);
        const img = new Image();
        img.crossOrigin = "anonymous";
        await new Promise((res, rej) => { img.onload = () => res(); img.onerror = () => rej(new Error("Image decode failed")); img.src = url; });
        const c = document.createElement("canvas");
        c.width = img.naturalWidth; c.height = img.naturalHeight;
        c.getContext("2d").drawImage(img, 0, 0);
        URL.revokeObjectURL(url);
        return c;
    }

    _setImage(bmp) {
        this.img = bmp;
        this.imgW = bmp.width;
        this.imgH = bmp.height;
        this._onResize(true);
        this._setEnabled(true);
        this._render();
    }

    /* ---------- Layout & render ---------- */
    _onResize(first = false) {
        const w = Math.max(320, Math.floor(this.$stage.clientWidth));
        const h = Math.max(260, Math.floor(this.$stage.clientHeight));
        this.dpr = Math.max(1, window.devicePixelRatio || 1);
        this.$canvas.width = Math.floor(w * this.dpr);
        this.$canvas.height = Math.floor(h * this.dpr);
        this.$canvas.style.width = w + "px";
        this.$canvas.style.height = h + "px";

        if (!this.cropCssSize) {
            const s = Math.min(w, h) * 0.6;
            this.cropSize = Math.max(160, Math.min(480, Math.floor(s)));
        } else {
            this.cropSize = this.cropCssSize;
        }

        if (first && this.img) {
            const fitScale = Math.min(w / this.imgW, h / this.imgH);
            const minScale = Math.max(this.cropSize / this.imgW, this.cropSize / this.imgH);
            this.scale = Math.max(fitScale, minScale);
            this.tx = (w - this.imgW * this.scale) / 2;
            this.ty = (h - this.imgH * this.scale) / 2;
            this.cropX = (w - this.cropSize) / 2;
            this.cropY = (h - this.cropSize) / 2;
            this._clampAll();
        }
        this._render();
    }

    _render() {
        const ctx = this.ctx;
        if (!ctx) return;
        const dpr = this.dpr;
        const cw = this.$canvas.width;
        const ch = this.$canvas.height;

        // clear
        ctx.setTransform(1, 0, 0, 1, 0, 0);
        ctx.clearRect(0, 0, cw, ch);

        if (this.img) {
            // draw image with scale+translate at device scale
            ctx.setTransform(this.scale * dpr, 0, 0, this.scale * dpr, this.tx * dpr, this.ty * dpr);
            ctx.imageSmoothingEnabled = true;
            ctx.imageSmoothingQuality = "high";
            ctx.drawImage(this.img, 0, 0);

            // dark overlay + crop hole + border
            ctx.setTransform(1, 0, 0, 1, 0, 0);
            ctx.fillStyle = "rgba(0,0,0,0.45)";
            ctx.fillRect(0, 0, cw, ch);
            ctx.clearRect(this.cropX * dpr, this.cropY * dpr, this.cropSize * dpr, this.cropSize * dpr);
            ctx.lineWidth = 2;
            ctx.strokeStyle = "#fff";
            ctx.strokeRect(this.cropX * dpr + 1, this.cropY * dpr + 1, this.cropSize * dpr - 2, this.cropSize * dpr - 2);

            this._renderPreview();
        } else {
            this._drawEmpty();
        }

        // overlay element for a11y/pointer focus
        this.$overlay.style.left = this.cropX + "px";
        this.$overlay.style.top = this.cropY + "px";
        this.$overlay.style.width = this.cropSize + "px";
        this.$overlay.style.height = this.cropSize + "px";
    }

    _renderPreview() {
        const s = this.opts.previewSize;
        const pctx = this.pctx;
        pctx.setTransform(1, 0, 0, 1, 0, 0);
        pctx.clearRect(0, 0, s, s);
        if (!this.img) return;
        const { x, y, w, h } = this._cropImageRect();
        pctx.imageSmoothingEnabled = true;
        pctx.imageSmoothingQuality = "high";
        pctx.drawImage(this.img, x, y, w, h, 0, 0, s, s);
        pctx.lineWidth = 2;
        pctx.strokeStyle = "rgba(255,255,255,0.8)";
        pctx.strokeRect(1, 1, s - 2, s - 2);
    }

    _drawEmpty() {
        const ctx = this.ctx;
        if (!ctx) return;
        const cw = this.$canvas.width, ch = this.$canvas.height;
        ctx.setTransform(1, 0, 0, 1, 0, 0);
        ctx.clearRect(0, 0, cw, ch);
        ctx.fillStyle = "#111";
        ctx.fillRect(0, 0, cw, ch);
        ctx.fillStyle = "#777";
        ctx.font = "14px system-ui, -apple-system, Segoe UI, Roboto, sans-serif";
        ctx.fillText("Load an image to start…", 16, 28);
    }

    /* ---------- Geometry ---------- */
    _cropImageRect() {
        const left = (this.cropX - this.tx) / this.scale;
        const top = (this.cropY - this.ty) / this.scale;
        const sizeIm = this.cropSize / this.scale;
        return { x: left, y: top, w: sizeIm, h: sizeIm };
    }

    _minScale() {
        return Math.max(this.cropSize / this.imgW, this.cropSize / this.imgH);
    }

    _clampAll() {
        if (!this.img) return;
        const imgRect = this._cropImageRect();
        let dx = 0, dy = 0;
        if (imgRect.x < 0) dx = -(imgRect.x) * this.scale;
        if (imgRect.y < 0) dy = -(imgRect.y) * this.scale;
        if (imgRect.x + imgRect.w > this.imgW) dx = (this.imgW - (imgRect.x + imgRect.w)) * this.scale;
        if (imgRect.y + imgRect.h > this.imgH) dy = (this.imgH - (imgRect.y + imgRect.h)) * this.scale;

        if (this.dragging === "crop") { this.cropX += dx; this.cropY += dy; }
        else { this.tx += -dx; this.ty += -dy; }

        if (this.scale < this._minScale()) this.scale = this._minScale();
    }

    /* ---------- Interactions ---------- */
    _inCrop(x, y) {
        return x >= this.cropX && x <= this.cropX + this.cropSize && y >= this.cropY && y <= this.cropY + this.cropSize;
    }

    _onPointerDown(e) {
        if (!this.img) return;
        this.$overlay.setPointerCapture?.(e.pointerId);
        const rect = this.$canvas.getBoundingClientRect();
        const x = e.clientX - rect.left, y = e.clientY - rect.top;
        this.last = { x, y };
        if (this.spaceDown || e.button === 1 || e.button === 2) {
            this.dragging = "pan";
        } else if (this._inCrop(x, y)) {
            this.dragging = "crop";
        } else {
            this.cropX = x - this.cropSize / 2;
            this.cropY = y - this.cropSize / 2;
            this.dragging = "crop";
            this._clampAll();
            this._render();
        }
    }
    _onPointerMove(e) {
        if (!this.img || !this.dragging) return;
        const rect = this.$canvas.getBoundingClientRect();
        const x = e.clientX - rect.left, y = e.clientY - rect.top;
        const dx = x - this.last.x, dy = y - this.last.y;
        this.last = { x, y };
        if (this.dragging === "crop") { this.cropX += dx; this.cropY += dy; }
        else if (this.dragging === "pan") { this.tx += dx; this.ty += dy; }
        this._clampAll();
        this._render();
    }
    _onPointerUp(e) {
        try { this.$overlay.releasePointerCapture?.(e.pointerId); } catch { }
        this.dragging = null;
    }

    _onWheel(e) {
        if (!this.img) return;
        e.preventDefault();
        const rect = this.$canvas.getBoundingClientRect();
        const cx = e.clientX - rect.left;
        const cy = e.clientY - rect.top;
        const delta = -Math.sign(e.deltaY) * 0.1;
        const newScale = Math.max(this._minScale(), this.scale * (1 + delta));
        const factor = newScale / this.scale;
        this.tx = this.tx + (1 - factor) * (cx - this.tx);
        this.ty = this.ty + (1 - factor) * (cy - this.ty);
        this.scale = newScale;
        this._clampAll();
        this._render();
    }

    _onKey(e) {
        if (!this.img) return;
        const step = e.shiftKey ? 10 : 1;
        let acted = true;
        switch (e.key) {
            case "ArrowLeft": this.cropX -= step; break;
            case "ArrowRight": this.cropX += step; break;
            case "ArrowUp": this.cropY -= step; break;
            case "ArrowDown": this.cropY += step; break;
            case "c": case "C":
                const rect = this.$canvas.getBoundingClientRect();
                this.cropX = Math.round((rect.width - this.cropSize) / 2);
                this.cropY = Math.round((rect.height - this.cropSize) / 2);
                break;
            default: acted = false;
        }
        if (acted) { e.preventDefault(); this._clampAll(); this._render(); }
    }

    /* ---------- Touch pinch ---------- */
    _onTouchStart(e) {
        if (!this.img) return;
        if (e.touches.length === 2) {
            e.preventDefault();
            this.pinch = {
                dist: this._touchDist(e.touches[0], e.touches[1]),
                cx: (e.touches[0].clientX + e.touches[1].clientX) / 2 - this.$canvas.getBoundingClientRect().left,
                cy: (e.touches[0].clientY + e.touches[1].clientY) / 2 - this.$canvas.getBoundingClientRect().top
            };
        } else if (e.touches.length === 1) {
            const rect = this.$canvas.getBoundingClientRect();
            const x = e.touches[0].clientX - rect.left;
            const y = e.touches[0].clientY - rect.top;
            this.dragging = this._inCrop(x, y) ? "crop" : "pan";
            this.last = { x, y };
        }
    }
    _onTouchMove(e) {
        if (!this.img) return;
        if (e.touches.length === 2 && this.pinch) {
            e.preventDefault();
            const nd = this._touchDist(e.touches[0], e.touches[1]);
            const factor = nd / this.pinch.dist;
            const newScale = Math.max(this._minScale(), this.scale * factor);
            const f = newScale / this.scale;
            this.tx = this.tx + (1 - f) * (this.pinch.cx - this.tx);
            this.ty = this.ty + (1 - f) * (this.pinch.cy - this.ty);
            this.scale = newScale;
            this.pinch.dist = nd;
            this._clampAll();
            this._render();
        } else if (e.touches.length === 1 && this.dragging) {
            const rect = this.$canvas.getBoundingClientRect();
            const x = e.touches[0].clientX - rect.left;
            const y = e.touches[0].clientY - rect.top;
            const dx = x - this.last.x, dy = y - this.last.y;
            this.last = { x, y };
            if (this.dragging === "crop") { this.cropX += dx; this.cropY += dy; }
            else { this.tx += dx; this.ty += dy; }
            this._clampAll();
            this._render();
        }
    }
    _onTouchEnd(e) {
        if (e.touches.length < 2) this.pinch = null;
        if (e.touches.length === 0) this.dragging = null;
    }
    _touchDist(a, b) { const dx = a.clientX - b.clientX, dy = a.clientY - b.clientY; return Math.hypot(dx, dy); }

    /* ---------- Export & submit ---------- */
    async _exportPNGBlob() {
        if (!this.img) throw new Error("No image loaded");
        const { x, y, w, h } = this._cropImageRect();
        const out = document.createElement("canvas");
        out.width = 512; out.height = 512;
        const ox = out.getContext("2d");
        ox.imageSmoothingEnabled = true;
        ox.imageSmoothingQuality = "high";
        ox.drawImage(this.img, x, y, w, h, 0, 0, 512, 512);
        const blob = await new Promise(res => out.toBlob(res, "image/png"));
        if (!blob) throw new Error("PNG export failed");
        return blob;
    }

    async _exportClicked() {
        try {
            this._err();
            await this._exportPNGBlob();
            this.$use.classList.add("ic512-ok");
            setTimeout(() => this.$use.classList.remove("ic512-ok"), 600);
        } catch (err) { this._err(err.message || String(err)); }
    }

    async _onFormSubmit(e) {
        if (!this.img) return; // let normal submit if user never loaded an image
        e.preventDefault();
        try {
            const blob = await this._exportPNGBlob();
            const fd = new FormData(this.$form);
            fd.delete(this.opts.fieldName);
            fd.append(this.opts.fieldName, new File([blob], "crop-512.png", { type: "image/png" }));
            const method = (this.$form.getAttribute("method") || "POST").toUpperCase();
            const resp = await fetch(this.$form.action, { method, body: fd, credentials: "same-origin" });
            if (resp.redirected) { window.location.href = resp.url; return; }
            const ct = resp.headers.get("Content-Type") || "";
            if (ct.includes("application/json")) {
                const data = await resp.json();
                if (data.redirect) { window.location.href = data.redirect; return; }
            }
            if (resp.ok) window.location.reload();
            else this._err(`Upload failed (${resp.status})`);
        } catch (err) {
            this._err(err.message || String(err));
        }
    }

    _err(msg = null) {
        if (!msg) { this.$error.hidden = true; this.$error.textContent = ""; return; }
        this.$error.hidden = false; this.$error.textContent = msg;
    }
}

/* Auto-init helper that waits for DOMContentLoaded if called early */
window.initImageCrop512 = function initImageCrop512(selectorOrEl, opts) {
    const run = () => {
        const els = typeof selectorOrEl === "string" ? document.querySelectorAll(selectorOrEl) : [selectorOrEl];
        return Array.from(els).map(el => new ImageCrop512(el, Object.assign({
            formSelector: el?.dataset?.form || null,
            fieldName: el?.dataset?.field || "file",
            proxyUrl: el?.dataset?.proxyUrl || null
        }, opts || {})));
    };
    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", run, { once: true });
    } else {
        run();
    }
};
