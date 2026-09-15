// 图片页使用原生审计的同一 PNG；比例以物理图片像素为基准。
function bindImagePreview() {
  const stage = document.querySelector('.image-stage');
  if (!stage) return;
  const picture = stage.querySelector('img');
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
  let fit = true, scale = 1, panX = 0, panY = 0, drag = null;
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
    if (!picture.naturalWidth) return;
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
