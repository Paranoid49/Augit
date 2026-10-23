// 大文件只读预览的全文语义验收（规格 §3.2/§7.2）。
//
// 这个模块验证的是「全文逻辑层 + 视觉窗口层」分离后的**行为**，不是节点计数：
//   - 窗口里只保留可视行，但查找计数、拖动/全选/复制、跳转行都按全文偏移工作；
//   - 拖动到视口外会继续自动滚动并扩展选择；
//   - 80,000 行样本只建可视窗口的行节点，滚到末尾能渲染最后一行；
//   - 普通文档仍走原来的完整行渲染。
//
// 可单独运行：node tools/audit/live-shell.spec.cjs <playwright> --large-text-only
module.exports = async function liveLargeText({ openScene, check }) {
  // ---- 2,000 行正常入口：全文语义 -------------------------------------------
  const { page } = await openScene('scene=main-project&theme=dark');
  const fixture = await page.evaluate(() => {
    const count = 2000;
    const needles = {
      1: 'needle first',
      1000: 'alpha needle middle',
      1999: 'needle last but one',
      2000: 'needle last',
    };
    const lines = [];
    for (let index = 1; index <= count; index++) {
      lines.push(needles[index]
        ? `line ${index} ${needles[index]}`
        : `line ${index} ${'-'.repeat(160)}`);
    }
    const text = lines.join('\n');
    window.__largeText = { text, count };
    window.__limitDocs = {
      'big.txt': {
        path: 'big.txt', name: 'big.txt', fullPath: 'D:\\w\\big.txt', workspaceName: 'w',
        status: 'TextPreview', kind: 'Text', typeName: '文本文件',
        fileSize: 21 * 1024 * 1024, previewBytes: 2500 * 1024,
        text, encoding: 'UTF-8', lineEndings: 'LF',
        message: 'Large file (21.0 MB). Showing the first 2.4 MB as a read-only preview.',
      },
    };
    window.__largeTextStarted = performance.now();
    window.__augitOpenDocument('big.txt');
    return { count, length: text.length };
  });
  await page.waitForFunction(() => {
    const code = document.querySelector('.editor-content .code-view');
    return !!code && !!code.__augitVirtual && code.getAttribute('aria-busy') === 'false'
      && code.getAttribute('data-accessible-text') === 'ready';
  }, null, { timeout: 20000 });
  await page.waitForTimeout(100);

  const mounted = await page.evaluate(() => {
    const code = document.querySelector('.editor-content .code-view');
    const controller = code.__augitVirtual;
    return {
      mountedMs: Math.round(controller.mountedAt - window.__largeTextStarted),
      accessibleMs: Math.round(controller.accessibleLaidOutAt - controller.mountedAt),
      lineCount: controller.lineCount,
      rowHeight: controller.rowHeight,
      rows: code.querySelectorAll(':scope > .code-lines > .code-line').length,
      totalHeight: code.querySelector(':scope > .code-lines').getBoundingClientRect().height,
      clientHeight: code.clientHeight,
      a11yLength: code.querySelector('.virtual-text-accessible').textContent.length,
      a11yLabel: code.querySelector('.virtual-text-accessible').getAttribute('aria-label'),
      a11yRole: code.querySelector('.virtual-text-accessible').getAttribute('role'),
      codeLabel: code.getAttribute('aria-label'),
      linesAriaHidden: code.querySelector(':scope > .code-lines').getAttribute('aria-hidden'),
      linesRole: code.querySelector(':scope > .code-lines').getAttribute('role'),
      linesLabel: code.querySelector(':scope > .code-lines').getAttribute('aria-label'),
      firstRowRole: code.querySelector('.code-line[data-line="1"]')?.getAttribute('role') || null,
      firstRowPos: Number(code.querySelector('.code-line[data-line="1"]')?.getAttribute('aria-posinset')),
      firstRowSet: Number(code.querySelector('.code-line[data-line="1"]')?.getAttribute('aria-setsize')),
      ariaBusy: code.getAttribute('aria-busy'),
      firstLine: code.querySelector('.code-line[data-line="1"] .virtual-line-text')?.textContent || null,
      firstLineNumber: code.querySelector('.code-line[data-line="1"] .line-number')?.textContent || null,
    };
  });
  check('大文件预览的全文逻辑层覆盖全部 2,000 行: ' + JSON.stringify([mounted.lineCount, mounted.totalHeight, mounted.clientHeight, mounted.rowHeight]),
    mounted.lineCount === fixture.count
      && Math.abs(mounted.totalHeight - fixture.count * mounted.rowHeight) < 4);
  check('大文件预览的视觉窗口只建可视行而不是全文行节点: ' + JSON.stringify([mounted.rows, mounted.clientHeight]),
    mounted.rows > 0 && mounted.rows < 120 && mounted.rows < fixture.count / 4);
  check('大文件预览的加载状态结束且行号/正文对应: ' + JSON.stringify([mounted.ariaBusy, mounted.firstLineNumber, mounted.firstLine, mounted.mountedMs]),
    mounted.ariaBusy === 'false' && mounted.firstLineNumber === '1' && mounted.firstLine.startsWith('line 1 needle first'));
  check('大文件预览提供窗口层可访问文本与全文行数: ' + JSON.stringify([mounted.a11yLength, mounted.a11yLabel, mounted.codeLabel, mounted.a11yRole, mounted.linesAriaHidden, mounted.linesRole, mounted.linesLabel, mounted.firstRowRole, mounted.firstRowPos, mounted.firstRowSet, mounted.accessibleMs]),
    mounted.a11yLength === 0
      && mounted.a11yRole === 'document'
      && mounted.a11yLabel === 'Read-only content, 2,000 lines'
      && mounted.codeLabel === 'Read-only content, 2,000 lines'
      && mounted.linesAriaHidden === 'false'
      && mounted.linesRole === 'list'
      && mounted.linesLabel === 'Read-only lines, 2,000 total'
      && mounted.firstRowRole === 'listitem'
      && mounted.firstRowPos === 1
      && mounted.firstRowSet === 2000
      && mounted.accessibleMs < 1000);

  // ---- 滚动到未显示区域：最后一行可见 ---------------------------------------
  const scrolled = await page.evaluate(async () => {
    const code = document.querySelector('.editor-content .code-view');
    code.scrollTop = code.scrollHeight;
    await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
    const rows = [...code.querySelectorAll('.code-line')];
    // 绝对定位窗口节点的 DOM 顺序是追加顺序，不等于行号顺序。
    const lastLine = rows.length ? Math.max(...rows.map((row) => Number(row.dataset.line))) : null;
    const last = rows.find((row) => Number(row.dataset.line) === lastLine);
    return {
      rows: rows.length,
      lastLine,
      lastText: last ? last.querySelector('.virtual-line-text').textContent : null,
      scrollTop: code.scrollTop,
      scrollHeight: code.scrollHeight,
    };
  });
  check('大文件预览滚到全文末尾仍能渲染最后一行: ' + JSON.stringify([scrolled.lastLine, scrolled.scrollTop, scrolled.scrollHeight, scrolled.rows]),
    scrolled.lastLine === 2000 && scrolled.lastText.includes('needle last')
      && scrolled.scrollTop > scrolled.scrollHeight - 2000 && scrolled.rows < 120);

  // ---- 横向滚动时行号列固定 ------------------------------------------------
  const horizontal = await page.evaluate(async () => {
    const code = document.querySelector('.editor-content .code-view');
    code.scrollTop = 0;
    code.scrollLeft = 320;
    await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
    const number = code.querySelector('.code-line .line-number');
    return {
      scrollLeft: code.scrollLeft,
      scrollWidth: code.scrollWidth,
      numberLeft: number ? number.getBoundingClientRect().left - code.getBoundingClientRect().left : null,
    };
  });
  check('大文件预览横向滚动时行号列保持固定: ' + JSON.stringify(horizontal),
    horizontal.scrollLeft > 0 && horizontal.scrollWidth > horizontal.scrollLeft + 300
      && horizontal.numberLeft !== null && Math.abs(horizontal.numberLeft) < 2);

  // ---- 全选与全文复制 -------------------------------------------------------
  await page.locator('.editor-content .code-view').click({ position: { x: 300, y: 40 } });
  await page.keyboard.press('Control+a');
  const selectAll = await page.evaluate(() => {
    const code = document.querySelector('.editor-content .code-view');
    const controller = code.__augitVirtual;
    const selected = controller.selectedText();
    const transfer = new DataTransfer();
    code.dispatchEvent(new ClipboardEvent('copy', { clipboardData: transfer, bubbles: true, cancelable: true }));
    return {
      selection: controller.selection,
      length: selected.length,
      textLength: controller.text.length,
      copiedLength: transfer.getData('text/plain').length,
      copiedStart: transfer.getData('text/plain').slice(0, 20),
      renderedSelection: code.querySelectorAll('.virtual-selection').length,
    };
  });
  check('大文件预览的全选覆盖全文而不是当前窗口: ' + JSON.stringify([selectAll.selection, selectAll.length, selectAll.textLength]),
    selectAll.selection.start === 0 && selectAll.selection.end === selectAll.textLength
      && selectAll.length === selectAll.textLength);
  check('大文件预览复制按全文偏移生成文本: ' + JSON.stringify([selectAll.copiedLength, selectAll.copiedStart, selectAll.renderedSelection]),
    selectAll.copiedLength === selectAll.textLength && selectAll.copiedStart.startsWith('line 1 needle first')
      && selectAll.renderedSelection > 0);

  // ---- 拖动选择跨出可视窗口后继续扩展 --------------------------------------
  await page.evaluate(() => {
    const code = document.querySelector('.editor-content .code-view');
    code.scrollTop = 0;
    code.__augitVirtual.clearSelection();
  });
  const rect = await page.locator('.editor-content .code-view').boundingBox();
  const dragStartX = rect.x + 150;
  const dragStartY = rect.y + 22 * 2 + 10;
  await page.mouse.move(dragStartX, dragStartY);
  await page.mouse.down();
  // 从第 2 行拖到视口下边缘，保持按下让自动滚动把选择扩展到未显示区域。
  await page.mouse.move(dragStartX + 200, rect.y + rect.height - 2, { steps: 10 });
  await page.waitForTimeout(900);
  await page.mouse.up();
  await page.waitForTimeout(100);
  const dragged = await page.evaluate(() => {
    const code = document.querySelector('.editor-content .code-view');
    const controller = code.__augitVirtual;
    const selection = controller.selection;
    return {
      selection,
      length: controller.selectedText().length,
      lines: controller.lineIndexOf(selection.end) - controller.lineIndexOf(selection.start) + 1,
      renderedSelection: code.querySelectorAll('.virtual-selection').length,
      scrollTop: code.scrollTop,
      rows: code.querySelectorAll('.code-line').length,
    };
  });
  check('大文件预览拖动到视口外继续扩展全文选择: ' + JSON.stringify([dragged.selection, dragged.length, dragged.lines, dragged.scrollTop]),
    dragged.selection.active && dragged.length > 5000 && dragged.lines > 20
      && dragged.scrollTop > 0 && dragged.renderedSelection > 0 && dragged.rows < 120);

  // ---- Find in File：全文计数 + 定位未显示行 --------------------------------
  await page.locator('.editor-content .code-view').click({ position: { x: 300, y: 40 } });
  await page.keyboard.press('Control+f');
  await page.locator('.current-find input').fill('needle');
  await page.waitForFunction(() => {
    const status = document.querySelector('.current-find .find-status');
    return !!status && status.textContent === '1/4';
  }, null, { timeout: 8000 });
  const findFirst = await page.evaluate(() => {
    const current = document.querySelector('.find-current');
    const row = current ? current.closest('.code-line') : null;
    return {
      status: document.querySelector('.current-find .find-status').textContent,
      current: current ? current.textContent : null,
      line: row ? row.dataset.line : null,
      rowText: row ? row.querySelector('.virtual-line-text').textContent : null,
      rows: document.querySelectorAll('.editor-content .code-line').length,
    };
  });
  check('大文件预览的 Find in File 计数来自全文: ' + JSON.stringify([findFirst.status, findFirst.current, findFirst.line, findFirst.rows]),
    findFirst.status === '1/4' && findFirst.current === 'needle' && findFirst.line === '1'
      && (findFirst.rowText || '').includes('needle first') && findFirst.rows < 120);
  await page.keyboard.press('Enter');
  await page.keyboard.press('Enter');
  await page.keyboard.press('Enter');
  await page.waitForFunction(() => {
    const status = document.querySelector('.current-find .find-status');
    return !!status && status.textContent === '4/4';
  }, null, { timeout: 8000 });
  await page.waitForTimeout(150);
  const findLast = await page.evaluate(() => {
    const code = document.querySelector('.editor-content .code-view');
    const current = document.querySelector('.find-current');
    const row = current ? current.closest('.code-line') : null;
    return {
      status: document.querySelector('.current-find .find-status').textContent,
      line: row ? row.dataset.line : null,
      text: row ? row.querySelector('.virtual-line-text').textContent : null,
      scrollTop: code.scrollTop,
    };
  });
  check('大文件预览的 Find 能定位到未显示的末尾命中: ' + JSON.stringify(findLast),
    findLast.status === '4/4' && findLast.line === '2000' && (findLast.text || '').endsWith('needle last') && findLast.scrollTop > 30000);
  await page.keyboard.press('Escape');
  await page.waitForTimeout(150);
  const afterFindClose = await page.evaluate(() => {
    const controller = document.querySelector('.editor-content .code-view').__augitVirtual;
    return { selected: controller.selectedText(), selection: controller.selection };
  });
  check('关闭查找后把末尾命中保留为全文选区: ' + JSON.stringify(afterFindClose),
    afterFindClose.selected === 'needle' && afterFindClose.selection.active
      && afterFindClose.selection.end - afterFindClose.selection.start === 6
      && afterFindClose.selection.start > 300000);

  // ---- Go to Line：按全文行号跳转 ------------------------------------------
  await page.locator('.editor-content .code-view').click({ position: { x: 300, y: 40 } });
  await page.keyboard.press('Control+g');
  await page.waitForSelector('[data-compact-dialog] [data-compact-field]', { timeout: 5000 });
  await page.locator('[data-compact-dialog] [data-compact-field]').fill('1000');
  await page.keyboard.press('Enter');
  await page.waitForTimeout(200);
  const goto = await page.evaluate(() => {
    const code = document.querySelector('.editor-content .code-view');
    const controller = code.__augitVirtual;
    const row = code.querySelector('.code-line.active');
    return {
      dialog: !!document.querySelector('[data-compact-dialog]'),
      line: row ? row.dataset.line : null,
      text: row ? row.querySelector('.virtual-line-text').textContent : null,
      scrollTop: code.scrollTop,
      clientHeight: code.clientHeight,
      rowHeight: controller.rowHeight,
    };
  });
  const centered = Math.abs(goto.scrollTop - (999 * goto.rowHeight - (goto.clientHeight - goto.rowHeight) / 2)) < 80;
  check('大文件预览的 Go to Line 按全文行号跳转并居中: ' + JSON.stringify(goto),
    goto.dialog === false && goto.line === '1000' && (goto.text || '').includes('alpha needle middle') && centered);

  // ---- 普通文档不进入窗口层 ------------------------------------------------
  await page.evaluate(() => window.__augitOpenDocument('docs/notes.txt'));
  await page.waitForFunction(() => {
    const code = document.querySelector('.editor-content .code-view');
    return !!code && !code.__augitVirtual && !!code.querySelector('.code-line');
  }, null, { timeout: 8000 });
  const small = await page.evaluate(() => ({
    virtual: !!document.querySelector('.editor-content .code-view').__augitVirtual,
    rows: document.querySelectorAll('.editor-content .code-line').length,
  }));
  check('普通文本文档仍使用完整行渲染而不是窗口层: ' + JSON.stringify(small), small.virtual === false && small.rows > 0);

  // ---- 软换行回退与恢复 ----------------------------------------------------
  await page.evaluate(() => window.__augitOpenDocument('big.txt'));
  await page.waitForFunction(() => {
    const code = document.querySelector('.editor-content .code-view');
    return !!code && !!code.__augitVirtual;
  }, null, { timeout: 15000 });
  await page.locator('.document-toolbar [aria-label="Soft Wraps"]').click();
  await page.waitForFunction(() => {
    const code = document.querySelector('.editor-content .code-view');
    return !!code && !code.__augitVirtual && code.dataset.virtualWrap === '1'
      && code.querySelectorAll('.code-line').length === 2000;
  }, null, { timeout: 15000 });
  const wrapOn = await page.evaluate(() => {
    const code = document.querySelector('.editor-content .code-view');
    return {
      virtual: !!code.__augitVirtual,
      rows: code.querySelectorAll('.code-line').length,
      wrap: code.classList.contains('wrap'),
      virtualWrap: code.dataset.virtualWrap,
    };
  });
  check('打开软换行时退回完整行构建以保证换行语义: ' + JSON.stringify(wrapOn),
    wrapOn.virtual === false && wrapOn.wrap === true && wrapOn.virtualWrap === '1' && wrapOn.rows === 2000);
  await page.locator('.document-toolbar [aria-label="Soft Wraps"]').click();
  await page.waitForFunction(() => {
    const code = document.querySelector('.editor-content .code-view');
    return !!code && !!code.__augitVirtual;
  }, null, { timeout: 15000 });
  const wrapOff = await page.evaluate(() => {
    const code = document.querySelector('.editor-content .code-view');
    return {
      virtual: !!code.__augitVirtual,
      rows: code.querySelectorAll('.code-line').length,
      wrap: code.classList.contains('wrap'),
      virtualWrap: code.dataset.virtualWrap || null,
      lineCount: code.__augitVirtual ? code.__augitVirtual.lineCount : null,
    };
  });
  check('关闭软换行后恢复窗口层: ' + JSON.stringify(wrapOff),
    wrapOff.virtual === true && wrapOff.wrap === false && wrapOff.virtualWrap === null
      && wrapOff.lineCount === 2000 && wrapOff.rows < 120);

  // ---- 普通 TextReady 的大文本同样走窗口层（不是只有超限预览） --------------
  await page.evaluate(() => {
    const lines = [];
    for (let index = 1; index <= 80000; index++) lines.push(`normal line ${index} ${'z'.repeat(110)}`);
    window.__limitDocs = {
      'normal-big.txt': {
        path: 'normal-big.txt', name: 'normal-big.txt', fullPath: 'D:\\w\\normal-big.txt', workspaceName: 'w',
        status: 'TextReady', kind: 'Text', typeName: '纯文本',
        fileSize: lines.join('\n').length, text: lines.join('\n'), encoding: 'UTF-8', lineEndings: 'LF',
      },
    };
    window.__augitOpenDocument('normal-big.txt');
  });
  await page.waitForFunction(() => {
    const code = document.querySelector('.editor-content .code-view');
    return !!code && !!code.__augitVirtual && code.__augitVirtual.lineCount === 80000;
  }, null, { timeout: 20000 });
  const normalBig = await page.evaluate(() => {
    const code = document.querySelector('.editor-content .code-view');
    return {
      virtual: !!code.__augitVirtual,
      lineCount: code.__augitVirtual.lineCount,
      rows: code.querySelectorAll('.code-line').length,
      readOnlyPreview: !!window.__augitLive.document.readOnlyPreview,
      largePreview: code.getAttribute('data-large-preview'),
      largeText: code.getAttribute('data-large-text'),
      banner: !!document.querySelector('.editor-content [data-large-file-banner]'),
    };
  });
  check('超过 200,000 字符的普通文本也使用窗口层而非完整行节点: ' + JSON.stringify(normalBig),
    normalBig.virtual === true && normalBig.lineCount === 80000 && normalBig.rows < 120
      && normalBig.readOnlyPreview === false && normalBig.largePreview === null
      && normalBig.largeText === '1' && normalBig.banner === false);
  // 80,000 行普通文本上的查找也必须走全文层并定位到末尾。
  await page.locator('.editor-content .code-view').click({ position: { x: 200, y: 40 } });
  await page.keyboard.press('Control+f');
  await page.locator('.current-find input').fill('normal line 79999');
  await page.waitForFunction(() => {
    const status = document.querySelector('.current-find .find-status');
    return !!status && status.textContent === '1/1';
  }, null, { timeout: 20000 });
  const normalFind = await page.evaluate(() => {
    const code = document.querySelector('.editor-content .code-view');
    const current = document.querySelector('.find-current');
    return {
      status: document.querySelector('.current-find .find-status').textContent,
      line: current ? Number(current.closest('.code-line').dataset.line) : null,
      scrollTop: code.scrollTop,
      rows: code.querySelectorAll('.code-line').length,
    };
  });
  check('80,000 行普通文本的 Find 定位到末尾行: ' + JSON.stringify(normalFind),
    normalFind.status === '1/1' && normalFind.line === 79999 && normalFind.scrollTop > 100000 && normalFind.rows < 120);
  await page.keyboard.press('Escape');
  await page.close();

  // ---- 80,000 行：只建可视窗口，滚到末尾 -----------------------------------
  const { page: big } = await openScene('scene=main-project&theme=dark');
  const perf = await big.evaluate(async () => {
    const count = 80000;
    const lines = [];
    for (let index = 1; index <= count; index++) lines.push(`line ${index} ${'-'.repeat(24)}`);
    const text = lines.join('\n');
    const code = document.createElement('div');
    code.className = 'code-view';
    code.style.cssText = 'position:fixed;left:0;top:0;width:900px;height:600px;visibility:hidden';
    document.body.append(code);
    const started = performance.now();
    const controller = window.__augitLargeTextView.mount(code, text);
    const mountMs = performance.now() - started;
    // 强制一次布局，避免把惰性布局算成 0。
    void code.getBoundingClientRect();
    const layoutMs = performance.now() - started;
    const initialRows = code.querySelectorAll('.code-line').length;
    const scrollStarted = performance.now();
    code.scrollTop = code.scrollHeight;
    await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
    const scrollMs = performance.now() - scrollStarted;
    // 全文可访问文本推迟到窗口行之后写入；等它完成再读长度和耗时。
    const accessibleDeadline = performance.now() + 10000;
    while (!controller.accessibleReady && performance.now() < accessibleDeadline) {
      await new Promise((resolve) => setTimeout(resolve, 25));
    }
    const accessibleMs = Math.round(controller.accessibleLaidOutAt - started);
    const rows = [...code.querySelectorAll('.code-line')];
    const lastLine = rows.length ? Math.max(...rows.map((row) => Number(row.dataset.line))) : null;
    const last = rows.find((row) => Number(row.dataset.line) === lastLine);
    const result = {
      mountMs: Math.round(mountMs * 10) / 10,
      layoutMs: Math.round(layoutMs * 10) / 10,
      scrollMs: Math.round(scrollMs * 10) / 10,
      accessibleMs,
      initialRows,
      rows: rows.length,
      lineCount: controller.lineCount,
      lastLine,
      lastText: last ? last.querySelector('.virtual-line-text').textContent : null,
      scrollHeight: code.scrollHeight,
      rowHeight: controller.rowHeight,
      a11yLength: code.querySelector('.virtual-text-accessible').textContent.length,
      a11yLabel: code.querySelector('.virtual-text-accessible').getAttribute('aria-label'),
      textLength: text.length,
    };
    controller.destroy();
    code.remove();
    return result;
  });
  check('80,000 行预览只建可视窗口行节点: ' + JSON.stringify([perf.lineCount, perf.initialRows, perf.rows, perf.scrollHeight]),
    perf.lineCount === 80000 && perf.initialRows > 0 && perf.initialRows < 120
      && perf.rows > 0 && perf.rows < 120 && perf.scrollHeight > 80000 * (perf.rowHeight - 2));
  check('80,000 行预览滚到末尾渲染最后一行: ' + JSON.stringify([perf.lastLine, perf.lastText]),
    perf.lastLine === 80000 && (perf.lastText || '').startsWith('line 80000'));
  check('80,000 行预览的窗口层可访问文本与全文行数: ' + JSON.stringify([perf.a11yLength, perf.a11yLabel, perf.textLength, perf.accessibleMs]),
    perf.a11yLength === 0 && perf.a11yLabel === 'Read-only content, 80,000 lines' && perf.accessibleMs < 1000);
  check('80,000 行预览的挂载/滚动耗时可复验（原型门槛 1000ms/1000ms）: ' + JSON.stringify([perf.mountMs, perf.layoutMs, perf.scrollMs, perf.accessibleMs]),
    perf.mountMs < 1000 && perf.layoutMs < 1000 && perf.scrollMs < 1000);
  console.log('live-large-text metrics ' + JSON.stringify({
    mountedMs: mounted.mountedMs,
    accessibleMs2000: mounted.accessibleMs,
    rows2000: mounted.rows,
    rowHeight: mounted.rowHeight,
    mountMs80000: perf.mountMs,
    layoutMs80000: perf.layoutMs,
    scrollMs80000: perf.scrollMs,
    accessibleMs80000: perf.accessibleMs,
    rows80000: perf.rows,
    a11yChars: perf.a11yLength,
  }));
  await big.close();
};
