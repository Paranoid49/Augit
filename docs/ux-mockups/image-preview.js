// 图片页使用原生审计的同一 PNG；比例以物理图片像素为基准。
function bindImagePreview() {
  const stage = document.querySelector('.image-stage');
  if (!stage) return;
  let picture = stage.querySelector('img');
  const label = document.querySelector('.image-zoom-label');
  const smaller = document.querySelector('[aria-label="缩小"]');
  const larger = document.querySelector('[aria-label="放大"]');
  const fitButton = document.querySelector('[aria-label="适应区域"]');
  const steps = [.1, .25, .5, .75, 1, 1.25, 1.5, 2, 3, 4, 6, 8];
  if (new URLSearchParams(location.search).get('image-state') === 'loading') {
    const status = document.createElement('div');
    status.className = 'image-loading';
    status.setAttribute('role', 'status');
    status.textContent = '正在读取文件…';
    stage.append(status);
  }
  // 视图状态（适应/比例/平移）要跨越**区域重绘**存活：外部更新会重建整个图片正文，
  // 而 `bindImagePreview()` 每次都从头绑定。实时外壳把它记在 `live.imageView` 上（按文档路径），
  // 视觉稿页面没有 `live`、也就没有这份记忆（静态页每次都按适应区域显示）。
  const viewKey = stage.dataset.documentPath || null;
  const storedView = () => {
    const live = window.__augitLive;
    const view = live && live.imageView;
    return viewKey && view && view.path === viewKey ? view : null;
  };
  const rememberView = () => {
    const live = window.__augitLive;
    if (!viewKey || !live) return;
    live.imageView = { path: viewKey, fit, scale, panX, panY };
  };
  const restored = storedView();
  let fit = restored ? restored.fit : true, scale = restored ? restored.scale : 1;
  let panX = restored ? restored.panX : 0, panY = restored ? restored.panY : 0, drag = null;
  // 图片异步解码：绑定时刻 naturalWidth 通常还是 0，render() 会直接返回，
  // 于是"适应区域"从未生效、图片停在原始尺寸并溢出画布（第 257 轮真机 1920×1200 vs 视觉稿 753×471）。
  // load 不冒泡但可捕获，因此监听挂在持久的 stage 上；节点被替换后新图片的 load 同样能捕获到。
  // 首次解码：`naturalWidth` 之前是 0，`render()` 会直接返回 ⇒ 这里补一次。
  // 外部更新复用同一张图时**保留手动缩放**（规格 §7.5「外部更新复用预览窗口、保留手动缩放与仍有效的位置」）：
  // 只有处于"适应区域"模式才清零平移并重新计算比例；手动缩放过就按原比例重画，位置由 `render()` 按新尺寸夹取。
  stage.addEventListener("load", () => {
    if (fit) { panX = panY = 0; }
    render();
  }, true);
  let wheelZoom = 0, wheelMode = '';
  function resetWheel() { wheelZoom = 0; wheelMode = ''; }
  function endDrag() {
    const previous = drag;
    drag = null;
    if (previous && stage.hasPointerCapture(previous.id)) stage.releasePointerCapture(previous.id);
  }
  function next(zoomIn, current = scale) {
    if (current < .1) return zoomIn ? .1 : current;
    return zoomIn ? steps.find(value => value > current + .0001) ?? 8 : steps.findLast(value => value < current - .0001) ?? .1;
  }
  function render() {
    // 每次渲染前重新解析当前 <img>：图片节点可能已被替换（实时外壳在 dataUrl 到达后重建正文）。
    picture = stage.querySelector('img') || picture;
    if (!picture || !picture.naturalWidth) return;
    const dpi = devicePixelRatio || 1;
    const width = Math.round(stage.clientWidth * dpi), height = Math.round(stage.clientHeight * dpi);
    if (fit) scale = Math.min(1, Math.max(1, width - Math.round(64 * dpi)) / picture.naturalWidth,
      Math.max(1, height - Math.round(64 * dpi)) / picture.naturalHeight);
    const w = Math.round(picture.naturalWidth * scale), h = Math.round(picture.naturalHeight * scale);
    const x = Math.trunc((width - w) / 2), y = Math.trunc((height - h) / 2);
    panX = w <= width ? 0 : Math.max(width - w - x, Math.min(-x, panX));
    panY = h <= height ? 0 : Math.max(height - h - y, Math.min(-y, panY));
    Object.assign(picture.style, { width: `${w / dpi}px`, height: `${h / dpi}px`, left: `${(x + panX) / dpi}px`, top: `${(y + panY) / dpi}px` });
    label.textContent = `${Math.max(1, Math.round(scale * 100))}%`;
    smaller.disabled = next(false) >= scale - .0001;
    larger.disabled = next(true) <= scale + .0001;
    stage.dataset.scale = String(scale);
    stage.dataset.fit = String(fit);
    stage.dataset.ready = 'true';
    rememberView();
  }
  function setScale(value) {
    if (Math.abs(value - scale) < .0001) return;
    endDrag();
    panX = Math.round(panX * value / scale); panY = Math.round(panY * value / scale);
    scale = value; fit = false; render();
  }
  smaller.addEventListener('click', () => { resetWheel(); setScale(next(false)); });
  larger.addEventListener('click', () => { resetWheel(); setScale(next(true)); });
  fitButton.addEventListener('click', () => { resetWheel(); endDrag(); fit = true; panX = panY = 0; render(); });
  stage.addEventListener('pointerdown', event => {
    if (event.button !== 0) return;
    resetWheel();
    stage.focus();
    if (picture.offsetWidth <= stage.clientWidth && picture.offsetHeight <= stage.clientHeight) return;
    drag = { id: event.pointerId, x: event.clientX, y: event.clientY, panX, panY };
    stage.setPointerCapture(event.pointerId);
  });
  stage.addEventListener('pointermove', event => { if (!drag) return; panX = drag.panX + (event.clientX - drag.x) * devicePixelRatio; panY = drag.panY + (event.clientY - drag.y) * devicePixelRatio; render(); });
  stage.addEventListener('pointerup', endDrag);
  stage.addEventListener('pointercancel', endDrag);
  stage.addEventListener('lostpointercapture', endDrag);
  stage.addEventListener('wheel', event => {
    if (document.hidden || stage.getClientRects().length === 0) { resetWheel(); return; }
    event.preventDefault();
    // DOM 的像素、行和页单位统一到一次常规滚轮约 40px，保留触控板细小增量。
    const unit = event.deltaMode === 1 ? 40 / 3 : event.deltaMode === 2 ? stage.clientHeight : 1;
    const dx = event.deltaX * unit, dy = event.deltaY * unit;
    if (!dx && !dy) return;
    const mode = event.ctrlKey ? 'zoom' : event.shiftKey || Math.abs(dx) > Math.abs(dy) ? 'horizontal' : 'vertical';
    if (mode !== wheelMode) { resetWheel(); wheelMode = mode; }
    if (mode === 'zoom') {
      wheelZoom -= dy;
      const count = Math.trunc((wheelZoom + Math.sign(wheelZoom) * 1e-8) / 40);
      wheelZoom -= count * 40;
      let value = scale;
      for (let i = 0; i < Math.abs(count); i++) value = next(count > 0, value);
      setScale(value);
    } else {
      panX -= (event.shiftKey && !dx ? dy : dx) * 48 / 40 * devicePixelRatio;
      if (!event.shiftKey) panY -= dy * 48 / 40 * devicePixelRatio;
      render();
    }
  }, { passive: false });
  stage.addEventListener('keydown', event => {
    resetWheel();
    const movement = { ArrowLeft: [32, 0], ArrowRight: [-32, 0], ArrowUp: [0, 32], ArrowDown: [0, -32] }[event.key];
    if (movement) { event.preventDefault(); panX += movement[0] * devicePixelRatio; panY += movement[1] * devicePixelRatio; render(); }
    if (event.key === 'Escape' && drag) { event.preventDefault(); endDrag(); }
  });
  document.addEventListener('visibilitychange', () => { if (document.hidden) { resetWheel(); endDrag(); } });
  picture.addEventListener('load', render);
  new ResizeObserver(render).observe(stage);
  render();
}
