// 大文件只读预览的「全文逻辑层 + 视觉窗口层」。
//
// 背景（产品规格 §3.2、UX 规范 §7.2）：大文件只读预览仍必须保留全文语义 ——
// 全文拖动选择与复制、Find in File 的全文计数与定位、Go to Line 跳到未显示区域、
// 原文换行与行号对应、可访问性和加载状态。此前把全部 `.code-line` 都建进 DOM，
// 再靠 `content-visibility` 跳过离屏布局；那只是布局优化，不是虚拟化：
// 80,000 行时 DOM 节点数、选择与查找仍然随全文规模线性增长。
//
// 本模块把两件事分开：
//   1. 全文逻辑层：`text` + `lineStarts`（每行起始 UTF-16 偏移），选择、复制、
//      查找计数和跳转行都只依赖这一层，与窗口里当前画了多少行无关。
//   2. 视觉窗口层：只创建可视区（含少量 overscan）的行节点；整段高度的占位由
//      `.virtual-code-lines` 的 height 承担，滚动条长度因此仍是全文长度。
//
// 选择不使用浏览器原生 DOM 选区（原生选区只能覆盖已建节点的窗口）。指针位置先由
// `caretRangeFromPoint()` 落到可见行内的字符，再换算成全文偏移；拖到视口外时自动
// 滚动并按新窗口继续扩展。复制时按全文偏移切片，不依赖当前窗口。
//
// 软换行（`.code-view.wrap`）下行高不再统一，当前版本在启用换行时退回既有的
// 分块全量 DOM 路径；这是已登记的边界，不把「窗口内选择」静默变成产品行为。
(function (global) {
  "use strict";

  const OVERSCAN_ROWS = 8;
  const AUTO_SCROLL_MAX = 48;
  const controllers = new Set();

  function normalizeText(text) {
    return String(text == null ? "" : text).replace(/\r\n?/g, "\n");
  }

  function buildModel(text) {
    const lineStarts = [0];
    for (let index = 0; index < text.length; index++) {
      if (text.charCodeAt(index) === 10) lineStarts.push(index + 1);
    }
    return { text, lineStarts, lineCount: lineStarts.length };
  }

  /** 第一个 >= value 的下标（`lineStarts` / 匹配起点都是升序）。 */
  function lowerBound(values, value) {
    let low = 0;
    let high = values.length;
    while (low < high) {
      const middle = (low + high) >> 1;
      if (values[middle] < value) low = middle + 1;
      else high = middle;
    }
    return low;
  }

  function clamp(value, min, max) {
    return value < min ? min : value > max ? max : value;
  }

  function lineEndOf(model, index) {
    return index + 1 < model.lineCount ? model.lineStarts[index + 1] - 1 : model.text.length;
  }

  function lineIndexOfOffset(model, offset) {
    const position = clamp(offset, 0, model.text.length);
    return Math.max(0, lowerBound(model.lineStarts, position + 1) - 1);
  }

  function createController(code, sourceText, options) {
    const model = buildModel(normalizeText(sourceText));
    const settings = options || {};
    const lifetime = new AbortController();
    const signal = lifetime.signal;

    const state = {
      anchor: -1,
      focus: -1,
      activeLine: -1,
      matches: [],
      currentMatch: -1,
      rowHeight: 22,
      rendered: new Map(),
      matchStarts: [],
      frame: 0,
      destroy: null,
      mountedAt: performance.now(),
    };

    // ---- 视觉窗口层的骨架 -------------------------------------------------
    code.classList.add("virtual-text");
    code.setAttribute("data-virtual-lines", String(model.lineCount));
    code.setAttribute("role", "document");
    code.setAttribute("aria-label", `Read-only content, ${model.lineCount.toLocaleString("en-US")} lines`);
    code.setAttribute("aria-busy", "true");
    if (!code.hasAttribute("tabindex")) code.tabIndex = 0;
    code.setAttribute("aria-activedescendant", "");
    code.style.setProperty("contain", "layout style");

    let lines = code.querySelector(":scope > .code-lines");
    if (!lines) {
      lines = document.createElement("div");
      lines.className = "code-lines";
      code.append(lines);
    }
    lines.classList.add("virtual-code-lines");
    lines.setAttribute("role", "list");
    lines.setAttribute("aria-label", `Read-only lines, ${model.lineCount.toLocaleString("en-US")} total`);
    lines.replaceChildren();
    // 视觉窗口里只有全文的一小段；可访问树暴露**当前窗口的行文本 + 全文行数**。
    // 曾经额外挂一个承载全文的 `pre[role=document]`：2.8 MB 文本节点在开启可访问树
    // （屏幕阅读器 / `--force-renderer-accessibility`）时首次布局实测约 100 s；
    // 分块 aria-label 也会在构建 625 个块时 16 s 未完成。全文仍由逻辑模型服务选择、
    // 复制、Find 与 Go to Line；可访问树不做全文单节点，避免把辅助技术锁死。
    // 行不再 aria-hidden：屏幕阅读器可读当前窗口；行号仍只作视觉标记。
    lines.setAttribute("aria-hidden", "false");

    // 摘要节点：只给全文行数，不承载全文文本（见上面的布局成本）。
    const accessible = document.createElement("pre");
    accessible.className = "virtual-text-accessible";
    accessible.setAttribute("role", "document");
    accessible.setAttribute("aria-label", `Read-only content, ${model.lineCount.toLocaleString("en-US")} lines`);
    code.append(accessible);
    accessible.setAttribute("aria-busy", "false");
    code.setAttribute("data-accessible-text", "ready");

    function measureRowHeight() {
      // 不能只读 `line-height`：`.code-line` 还有 `min-height: var(--code-line-height, 22px)`，
      // 两者取大才是真实行距（在 `measureCodeViews()` 尚未写入变量时会差 2px）。
      const probe = document.createElement("div");
      probe.className = "code-line";
      probe.style.cssText = "position:absolute;visibility:hidden;pointer-events:none;top:-10000px;left:0;width:1px";
      probe.innerHTML = '<span class="line-number">1</span><span> </span>';
      lines.append(probe);
      const height = probe.getBoundingClientRect().height;
      probe.remove();
      if (Number.isFinite(height) && height > 0) return height;
      const value = parseFloat(getComputedStyle(code).lineHeight);
      return Number.isFinite(value) && value > 0 ? value : 22;
    }

    function refreshMetrics() {
      const next = measureRowHeight();
      if (Math.abs(next - state.rowHeight) < 0.01) return;
      state.rowHeight = next;
      relayout();
    }

    function relayout() {
      lines.style.height = `${model.lineCount * state.rowHeight}px`;
      for (const [index, row] of state.rendered) {
        row.style.top = `${index * state.rowHeight}px`;
      }
      render(true);
    }

    // ---- 行内容：全文偏移 → 片段 -------------------------------------------
    function selectionRange() {
      if (state.anchor < 0 || state.focus < 0) return null;
      const start = Math.min(state.anchor, state.focus);
      const end = Math.max(state.anchor, state.focus);
      return end > start ? { start, end } : null;
    }

    function matchesForLine(index) {
      if (!state.matches.length) return [];
      const lineStart = model.lineStarts[index];
      const lineEnd = lineEndOf(model, index);
      let cursor = lowerBound(state.matchStarts, lineStart);
      while (cursor > 0
        && state.matches[cursor - 1].start + state.matches[cursor - 1].length >= lineStart) {
        cursor--;
      }
      const hits = [];
      for (; cursor < state.matches.length && state.matches[cursor].start <= lineEnd; cursor++) {
        const match = state.matches[cursor];
        const start = Math.max(match.start, lineStart);
        const end = Math.min(match.start + match.length, lineEnd);
        if (end <= start) continue;
        hits.push({
          start: start - lineStart,
          end: end - lineStart,
          label: cursor === state.currentMatch ? "find-current" : "find-match",
        });
      }
      return hits;
    }

    function segmentsForLine(index) {
      const lineStart = model.lineStarts[index];
      const lineEnd = lineEndOf(model, index);
      const length = lineEnd - lineStart;
      const marks = [];
      for (const hit of matchesForLine(index)) {
        marks.push({ start: hit.start, end: hit.end, label: hit.label });
      }
      const selection = selectionRange();
      if (selection) {
        const start = Math.max(selection.start, lineStart) - lineStart;
        const end = Math.min(selection.end, lineEnd) - lineStart;
        if (end > start) marks.push({ start, end, label: "virtual-selection" });
      }
      if (!marks.length) {
        return { signature: "", segments: [{ start: 0, end: length, labels: [] }] };
      }
      const points = new Set([0, length]);
      for (const mark of marks) {
        points.add(clamp(mark.start, 0, length));
        points.add(clamp(mark.end, 0, length));
      }
      const sorted = [...points].sort((left, right) => left - right);
      const segments = [];
      for (let cursor = 0; cursor + 1 < sorted.length; cursor++) {
        const start = sorted[cursor];
        const end = sorted[cursor + 1];
        if (end <= start) continue;
        const labels = marks
          .filter((mark) => mark.start <= start && mark.end >= end)
          .map((mark) => mark.label);
        if (!labels.length) continue;
        segments.push({ start, end, labels });
      }
      const signature = segments.map((segment) => `${segment.start}:${segment.end}:${segment.labels.join("+")}`).join("|");
      return { signature, segments };
    }

    function appendText(body, text) {
      if (!text) return;
      if (!code.classList.contains("show-whitespace")) {
        body.append(document.createTextNode(text));
        return;
      }
      for (const piece of text.split(/([ \t])/)) {
        if (!piece) continue;
        if (piece === " " || piece === "\t") {
          const mark = document.createElement("span");
          mark.className = "ws";
          mark.textContent = piece;
          body.append(mark);
        } else {
          body.append(document.createTextNode(piece));
        }
      }
    }

    function buildBody(index) {
      const lineStart = model.lineStarts[index];
      const lineText = model.text.slice(lineStart, lineEndOf(model, index));
      const { signature, segments } = segmentsForLine(index);
      const body = document.createElement("span");
      body.className = "virtual-line-text";
      let cursor = 0;
      for (const segment of segments) {
        if (segment.start > cursor) appendText(body, lineText.slice(cursor, segment.start));
        const labels = segment.labels;
        const node = labels.some((label) => label === "find-current" || label === "find-match")
          ? document.createElement("mark")
          : document.createElement("span");
        node.className = labels.join(" ");
        appendText(node, lineText.slice(segment.start, segment.end));
        body.append(node);
        cursor = segment.end;
      }
      if (cursor < lineText.length) appendText(body, lineText.slice(cursor));
      // 空行保留一个空格：与 `liveLineViews()` 的既有渲染一致，行高与选择不受影响。
      if (!lineText.length && !body.firstChild) appendText(body, " ");
      return { body, signature };
    }

    function ensureRow(index) {
      let row = state.rendered.get(index);
      const active = index === state.activeLine ? "1" : "0";
      const { body, signature } = buildBody(index);
      const fullSignature = `${active}|${signature}`;
      if (row && row.dataset.signature === fullSignature) return row;
      if (!row) {
        row = document.createElement("div");
        row.className = "code-line";
        row.dataset.line = String(index + 1);
        row.id = `virtual-line-${index + 1}`;
        row.setAttribute("role", "listitem");
        row.setAttribute("aria-setsize", String(model.lineCount));
        row.setAttribute("aria-posinset", String(index + 1));
        if (index === state.activeLine) row.setAttribute("aria-current", "true");
        row.style.top = `${index * state.rowHeight}px`;
        const number = document.createElement("span");
        number.className = "line-number";
        number.setAttribute("aria-hidden", "true");
        number.textContent = String(index + 1);
        row.append(number);
        state.rendered.set(index, row);
        lines.append(row);
      }
      row.classList.toggle("active", index === state.activeLine);
      if (index === state.activeLine) row.setAttribute("aria-current", "true");
      else row.removeAttribute("aria-current");
      row.dataset.signature = fullSignature;
      row.querySelector(".virtual-line-text")?.remove();
      row.append(body);
      return row;
    }

    function visibleRange() {
      const height = code.clientHeight || 1;
      const first = Math.max(0, Math.floor(code.scrollTop / state.rowHeight) - OVERSCAN_ROWS);
      const last = Math.min(
        model.lineCount - 1,
        Math.ceil((code.scrollTop + height) / state.rowHeight) + OVERSCAN_ROWS,
      );
      return { first, last };
    }

    function render(force) {
      if (state.frame && !force) return;
      const { first, last } = visibleRange();
      for (const [index, row] of [...state.rendered]) {
        if (index < first || index > last) {
          row.remove();
          state.rendered.delete(index);
        }
      }
      for (let index = first; index <= last; index++) ensureRow(index);
      state.lastRange = { first, last };
    }

    function scheduleRender() {
      if (state.frame) return;
      state.frame = global.requestAnimationFrame(() => {
        state.frame = 0;
        render(false);
      });
    }

    // ---- 指针选择 ----------------------------------------------------------
    function offsetFromPoint(clientX, clientY) {
      const rect = code.getBoundingClientRect();
      const x = clamp(clientX, rect.left + 1, rect.right - 1);
      const y = clamp(clientY, rect.top + 1, rect.bottom - 1);
      let range = null;
      if (typeof document.caretRangeFromPoint === "function") {
        range = document.caretRangeFromPoint(x, y);
      }
      if (range && range.startContainer) {
        const node = range.startContainer;
        const element = node.nodeType === 3 ? node.parentElement : node;
        const row = element && element.closest ? element.closest(".code-line") : null;
        if (row && code.contains(row) && row.dataset.line) {
          const index = Number(row.dataset.line) - 1;
          const body = row.querySelector(".virtual-line-text");
          if (body && body.contains(node)) {
            const prefix = document.createRange();
            prefix.selectNodeContents(body);
            try {
              prefix.setEnd(node, range.startOffset);
            } catch {
              return model.lineStarts[index];
            }
            return model.lineStarts[index] + prefix.toString().length;
          }
          const number = row.querySelector(".line-number");
          if (number && clientX <= number.getBoundingClientRect().right) return model.lineStarts[index];
          return lineEndOf(model, index);
        }
      }
      // 空行、行号列或行末空白处：按统一行高落到字符起点，避免拖选时选择跳行。
      const index = clamp(
        Math.floor((y - rect.top + code.scrollTop) / state.rowHeight),
        0,
        model.lineCount - 1,
      );
      return model.lineStarts[index];
    }

    const drag = { active: false, x: 0, y: 0, pointerId: null, frame: 0 };

    function updateDragFocus() {
      if (!drag.active) return;
      state.focus = offsetFromPoint(drag.x, drag.y);
      render(true);
    }

    function autoScrollStep() {
      drag.frame = 0;
      if (!drag.active) return;
      const rect = code.getBoundingClientRect();
      const speed = (distance) => Math.ceil(clamp(distance / 2, 4, AUTO_SCROLL_MAX));
      let deltaY = 0;
      let deltaX = 0;
      if (drag.y < rect.top + 4) deltaY = -speed(rect.top + 4 - drag.y);
      else if (drag.y > rect.bottom - 4) deltaY = speed(drag.y - (rect.bottom - 4));
      if (drag.x < rect.left + 4) deltaX = -speed(rect.left + 4 - drag.x);
      else if (drag.x > rect.right - 4) deltaX = speed(drag.x - (rect.right - 4));
      if (deltaY || deltaX) {
        const beforeTop = code.scrollTop;
        const beforeLeft = code.scrollLeft;
        code.scrollTop += deltaY;
        code.scrollLeft += deltaX;
        if (code.scrollTop !== beforeTop || code.scrollLeft !== beforeLeft) {
          updateDragFocus();
        }
      }
      drag.frame = global.requestAnimationFrame(autoScrollStep);
    }

    function beginDrag(event) {
      if (event.button !== 0 || event.isPrimary === false) return;
      const offset = offsetFromPoint(event.clientX, event.clientY);
      state.anchor = offset;
      state.focus = offset;
      drag.active = true;
      drag.x = event.clientX;
      drag.y = event.clientY;
      drag.pointerId = event.pointerId;
      try {
        code.setPointerCapture(event.pointerId);
      } catch {
        // 合成事件或已失去指针时忽略：后续 pointermove 仍按坐标工作。
      }
      code.focus({ preventScroll: true });
      event.preventDefault();
      render(true);
    }

    function moveDrag(event) {
      if (!drag.active) return;
      drag.x = event.clientX;
      drag.y = event.clientY;
      updateDragFocus();
      if (!drag.frame) drag.frame = global.requestAnimationFrame(autoScrollStep);
      event.preventDefault();
    }

    function endDrag() {
      if (!drag.active) return;
      drag.active = false;
      if (drag.frame) global.cancelAnimationFrame(drag.frame);
      drag.frame = 0;
      render(true);
    }

    function clearSelection() {
      if (state.anchor < 0 && state.focus < 0) return;
      state.anchor = -1;
      state.focus = -1;
      render(true);
    }

    function onPointerDownOutside(event) {
      if (!code.contains(event.target)) clearSelection();
    }

    // ---- 复制 / 全选 -------------------------------------------------------
    function selectedText() {
      const selection = selectionRange();
      return selection ? model.text.slice(selection.start, selection.end) : "";
    }

    function onCopy(event) {
      if (!event.clipboardData || !selectionRange()) return;
      event.clipboardData.setData("text/plain", selectedText());
      event.preventDefault();
    }

    function activateLine(index) {
      if (!Number.isInteger(index) || index < 0 || index >= model.lineCount) return;
      state.activeLine = index;
      const top = index * state.rowHeight;
      const height = code.clientHeight || 1;
      if (top < code.scrollTop) code.scrollTop = top;
      else if (top + state.rowHeight > code.scrollTop + height) code.scrollTop = top + state.rowHeight - height;
      code.setAttribute("aria-activedescendant", `virtual-line-${index + 1}`);
      render(true);
    }

    function onKeyDown(event) {
      if (event.defaultPrevented || event.isComposing || event.keyCode === 229) return;
      if (!event.ctrlKey && !event.metaKey && !event.altKey) {
        if (event.key === "ArrowDown" || event.key === "ArrowUp") {
          event.preventDefault();
          const current = state.activeLine >= 0 ? state.activeLine : Math.max(0, Math.floor(code.scrollTop / state.rowHeight));
          activateLine(clamp(current + (event.key === "ArrowDown" ? 1 : -1), 0, model.lineCount - 1));
          return;
        }
        if (event.key === "Home" || event.key === "End") {
          event.preventDefault();
          activateLine(event.key === "Home" ? 0 : model.lineCount - 1);
          return;
        }
      }
      if ((event.ctrlKey || event.metaKey) && !event.altKey && !event.shiftKey
        && event.key.toLowerCase() === "a") {
        event.preventDefault();
        state.anchor = 0;
        state.focus = model.text.length;
        render(true);
      }
    }

    function onScroll() {
      scheduleRender();
    }

    const resize = typeof ResizeObserver === "function"
      ? new ResizeObserver(() => scheduleRender())
      : null;
    resize?.observe(code);

    code.addEventListener("pointerdown", beginDrag, { signal });
    code.addEventListener("pointermove", moveDrag, { signal });
    code.addEventListener("pointerup", endDrag, { signal });
    code.addEventListener("pointercancel", endDrag, { signal });
    code.addEventListener("lostpointercapture", endDrag, { signal });
    code.addEventListener("copy", onCopy, { signal });
    code.addEventListener("keydown", onKeyDown, { signal });
    code.addEventListener("scroll", onScroll, { passive: true, signal });
    document.addEventListener("pointerdown", onPointerDownOutside, { capture: true, signal });

    // ---- 对外接口 ----------------------------------------------------------
    state.rowHeight = measureRowHeight();
    lines.style.height = `${model.lineCount * state.rowHeight}px`;
    render(true);
    code.setAttribute("aria-busy", "false");
    // 行号列宽按全文行数位数度量（`measureCodeViews()` 已按 `data-virtual-lines` 支持）。
    if (typeof global.measureCodeViews === "function") global.measureCodeViews();

    const controller = {
      text: model.text,
      lineCount: model.lineCount,
      lineStarts: model.lineStarts,
      get visibleRows() {
        return state.rendered.size;
      },
      get rowHeight() {
        return state.rowHeight;
      },
      get selection() {
        const selection = selectionRange();
        return selection ? { ...selection, active: true } : { start: -1, end: -1, active: false };
      },
      get activeLine() {
        return state.activeLine;
      },
      get accessibleReady() {
        return code.getAttribute("data-accessible-text") === "ready";
      },
      accessibleAt: 0,
      accessibleLaidOutAt: 0,
      setSelection(anchor, focus) {
        const limit = model.text.length;
        state.anchor = clamp(Number(anchor) || 0, 0, limit);
        state.focus = clamp(Number(focus) || 0, 0, limit);
        render(true);
      },
      clearSelection,
      selectedText,
      setFind(matches, current) {
        state.matches = Array.isArray(matches)
          ? matches.map((match) => ({ start: Number(match.start) || 0, length: Math.max(0, Number(match.length) || 0) }))
          : [];
        state.matchStarts = state.matches.map((match) => match.start);
        state.currentMatch = Number.isInteger(current) ? current : -1;
        render(true);
      },
      clearFind() {
        if (!state.matches.length && state.currentMatch < 0) return;
        state.matches = [];
        state.matchStarts = [];
        state.currentMatch = -1;
        render(true);
      },
      revealOffset(offset) {
        const index = lineIndexOfOffset(model, offset);
        const top = index * state.rowHeight;
        const bottom = top + state.rowHeight;
        const height = code.clientHeight || 1;
        if (top < code.scrollTop) code.scrollTop = top;
        else if (bottom > code.scrollTop + height) code.scrollTop = bottom - height;
        render(true);
        return index;
      },
      revealLine(line, opts) {
        const index = Number(line) - 1;
        if (!Number.isInteger(index) || index < 0 || index >= model.lineCount) return false;
        state.activeLine = opts && opts.active === false ? state.activeLine : index;
        const top = index * state.rowHeight;
        const height = code.clientHeight || 1;
        const center = !opts || opts.center !== false;
        code.scrollTop = center
          ? Math.max(0, top - (height - state.rowHeight) / 2)
          : Math.min(top, Math.max(0, code.scrollTop));
        code.setAttribute("aria-activedescendant", `virtual-line-${index + 1}`);
        render(true);
        return true;
      },
      lineText(index) {
        if (index < 0 || index >= model.lineCount) return "";
        return model.text.slice(model.lineStarts[index], lineEndOf(model, index));
      },
      lineIndexOf(offset) {
        return lineIndexOfOffset(model, offset);
      },
      rowForLine(line) {
        return state.rendered.get(Number(line) - 1) || null;
      },
      refreshMetrics,
      destroy() {
        lifetime.abort();
        resize?.disconnect();
        if (state.frame) global.cancelAnimationFrame(state.frame);
        if (drag.frame) global.cancelAnimationFrame(drag.frame);
        state.frame = 0;
        drag.frame = 0;
        controllers.delete(controller);
        if (code.__augitVirtual === controller) delete code.__augitVirtual;
        code.classList.remove("virtual-text");
        code.removeAttribute("data-virtual-lines");
        code.removeAttribute("data-accessible-text");
        code.removeAttribute("aria-busy");
        code.style.removeProperty("contain");
        lines.remove();
        accessible.remove();
        state.rendered.clear();
      },
      mountedAt: state.mountedAt,
    };

    code.__augitVirtual = controller;
    controller.__code = code;
    controller.accessibleAt = performance.now();
    controller.accessibleLaidOutAt = controller.accessibleAt;
    controllers.add(controller);
    return controller;
  }

  const api = {
    /**
     * 把一段全文挂到已有的 `.code-view` 上。调用方负责在替换区域后调用 `prune()`，
     * 让已离开文档的控制器释放挂在 document 上的监听。
     */
    mount(code, text, options) {
      if (!code) throw new Error("large-text-view: missing code view");
      if (code.__augitVirtual) code.__augitVirtual.destroy();
      return createController(code, text, options);
    },
    prune() {
      for (const controller of [...controllers]) {
        const view = controller.__code;
        if (!view || !view.isConnected) controller.destroy();
      }
    },
    supports(code) {
      return !!(code && code.__augitVirtual);
    },
    normalizeText,
  };

  global.__augitLargeTextView = api;
})(window);
