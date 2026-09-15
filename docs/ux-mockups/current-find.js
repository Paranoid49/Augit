// 当前文件查找的可交互视觉稿；只搜索页面内的固定样本，不读取或修改工作区文件。
function currentFindBar() {
  return `<div class="current-find"><input class="search-field" value="" aria-label="当前文件查找"><button class="icon-button" aria-label="区分大小写">${icon("case-sensitive")}</button><button class="icon-button" aria-label="全字匹配">${icon("whole-word")}</button><button class="icon-button" aria-label="正则表达式">${icon("regex")}</button><span class="find-status" title=""></span><button class="icon-button" aria-label="上一项">${icon("chevron-up")}</button><button class="icon-button" aria-label="下一项">${icon("chevron-down")}</button><button class="icon-button" aria-label="关闭查找">${icon("x")}</button></div>`;
}

function bindCurrentFind() {
  const view = document.querySelector('.document-view:has(.code-view):not(.blame-document)');
  if (!view || view.dataset.findBound) return;
  view.dataset.findBound = 'true';
  // 查找条把监听挂在 document/window 上，所有者是本区域节点；
  // 区域刷新会换掉节点，若不释放旧监听就会随刷新次数累积。
  const lifetime = new AbortController();
  const owner = view;
  if (typeof registerRegionDisposer === 'function') {
    registerRegionDisposer(() => { lifetime.abort(); owner.dataset.findBound = ''; });
  }
  let bar = view.querySelector('.current-find');
  const code = view.querySelector('.code-view');
  let rows = [], sourceLines = [], source = '';
  const readSource = () => {
    rows = [...code.querySelectorAll('.code-line')];
    sourceLines = rows.map(row => row.lastElementChild.textContent);
    source = sourceLines.join('\n');
  };
  readSource();
  const template = document.createElement('template');
  template.innerHTML = currentFindBar();
  const retainedBar = bar || template.content.firstElementChild;
  const bodyRegion = view.querySelector(':scope > .markdown-panes') || code;
  if (bar) view.insertBefore(bar, bodyRegion);
  const initialState = new URLSearchParams(location.search).get('find-state');
  let savedQuery = bar || initialState ? retainedBar.querySelector('input').value : '';
  let options = { case: false, whole: false, regex: ['invalid', 'timeout'].includes(initialState) };
  let matches = [], current = -1, queue = [], worker = null, workerUrl = null;
  let timeout = null, progress = null, generation = 0, busy = false, completed = false, statusText = '';
  let preset = ['loading', 'timeout'].includes(initialState) ? initialState : null;
  let composing = false, compositionStatus = '';
  let compositionEvents = null;

  const label = name => bar.querySelector(`[aria-label="${name}"]`);
  const status = text => {
    statusText = text;
    const output = bar?.querySelector('.find-status');
    if (!output) return;
    output.textContent = output.title = text;
    measureCurrentFind();
  };
  const stop = () => {
    generation++;
    clearTimeout(timeout); clearTimeout(progress);
    worker?.terminate(); worker = null;
    if (workerUrl) URL.revokeObjectURL(workerUrl);
    workerUrl = null; busy = false; queue = [];
  };
  const clearHighlight = () => {
    code.querySelectorAll('[data-find-selected]').forEach(row => {
      delete row.dataset.findSelected;
      row.lastElementChild.textContent = sourceLines[rows.indexOf(row)];
    });
  };
  const select = (backwards, preservePosition = false) => {
    if (!matches.length) return;
    current = current < 0 ? backwards ? matches.length - 1 : 0
      : (current + (backwards ? -1 : 1) + matches.length) % matches.length;
    clearHighlight();
    let offset = 0, matchIndex = 0, firstMark = null;
    for (let lineIndex = 0; lineIndex < rows.length; lineIndex++) {
      const row = rows[lineIndex], content = row.lastElementChild, line = sourceLines[lineIndex];
      const fragments = []; let cursor = 0, marked = false;
      while (matchIndex < matches.length && matches[matchIndex].start + matches[matchIndex].length < offset) matchIndex++;
      for (let i = matchIndex; i < matches.length && matches[i].start <= offset + line.length; i++) {
        const match = matches[i];
        if (match.start + match.length < offset || (match.length && match.start + match.length === offset)) continue;
        const start = Math.max(cursor, match.start - offset), end = Math.min(line.length, match.start + match.length - offset);
        fragments.push(document.createTextNode(line.slice(cursor, start)));
        const mark = document.createElement('mark');
        mark.className = i === current ? 'find-current' : 'find-match';
        mark.textContent = line.slice(start, end);
        mark.dataset.start = String(match.start); mark.dataset.length = String(match.length);
        fragments.push(mark); cursor = end; marked = true;
        if (i === current) firstMark ??= mark;
      }
      if (marked) { fragments.push(document.createTextNode(line.slice(cursor))); content.replaceChildren(...fragments); row.dataset.findSelected = 'true'; }
      offset += line.length + 1;
    }
    status(`${current + 1}/${matches.length}`);
    if (!firstMark || preservePosition) return;
    // 只滚动正文容器，不调用会移动外层页面或抢焦点的 scrollIntoView。
    const bounds = firstMark.getBoundingClientRect(), viewport = code.getBoundingClientRect();
    const coveredTop = bar && !bar.hidden ? Math.max(viewport.top, bar.getBoundingClientRect().bottom + 4) : viewport.top;
    if (bounds.top < coveredTop) code.scrollTop -= coveredTop - bounds.top;
    else if (bounds.bottom > viewport.bottom) code.scrollTop += bounds.bottom - viewport.bottom;
    if (bounds.right > viewport.right) code.scrollLeft += bounds.right - viewport.right + 8;
    else if (bounds.left < viewport.left) code.scrollLeft -= viewport.left - bounds.left + 8;
  };
  const navigate = backwards => { if (composing) return; if (busy) queue.push(backwards); else select(backwards); };
  const computeMatches = data => {
    const escape = text => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const pattern = data.regex ? data.query : escape(data.query);
    // 普通文本不采用正则 Unicode 折叠（例如 K/k）；视觉稿仅验证共享样本，不声明与 .NET 全语法等价。
    const expression = new RegExp(data.regex && data.whole ? `(?<![\\p{L}\\p{N}_])(?:${pattern})(?![\\p{L}\\p{N}_])` : pattern,
      `g${data.regex ? 'u' : ''}${data.case ? '' : 'i'}`);
    const word = character => character !== undefined && /[\p{L}\p{N}_]/u.test(character);
    const results = [];
    for (let match; (match = expression.exec(data.source)) !== null;) {
      if (!data.regex && data.whole && (word(data.source[match.index - 1]) || word(data.source[expression.lastIndex]))) {
        expression.lastIndex = match.index + 1;
        continue;
      }
      results.push({ start: match.index, length: match[0].length });
      if (!match[0].length) expression.lastIndex += data.source.codePointAt(expression.lastIndex) > 0xffff ? 2 : 1;
    }
    return results;
  };
  const search = (preservePosition = false) => {
    if (composing || !bar) return;
    stop(); completed = false; current = -1; matches = []; clearHighlight();
    savedQuery = label('当前文件查找').value;
    status('');
    if (!savedQuery) { completed = true; return; }
    const request = { source, query: savedQuery, ...options };
    if (preset) { completed = true; status(preset === 'loading' ? '正在搜索…' : '查找超时'); return; }
    const accept = result => {
      busy = false; completed = true; clearTimeout(timeout); clearTimeout(progress);
      matches = result; status(`0/${matches.length}`);
      if (matches.length) select(false, preservePosition);
      for (const backwards of queue.splice(0)) select(backwards);
    };
    if (!options.regex) { accept(computeMatches(request)); return; }
    // 正则可能长时间运行，专用 Worker 在超时、换查询或关闭时终止。
    busy = true;
    const version = generation;
    workerUrl = URL.createObjectURL(new Blob([
      `const compute=${computeMatches.toString()};onmessage=event=>{try{postMessage({matches:compute(event.data)})}catch{postMessage({error:true})}};postMessage({ready:true});`
    ], { type: 'text/javascript' }));
    worker = new Worker(workerUrl);
    worker.onmessage = event => {
      if (generation !== version) return;
      if (event.data.ready) {
        // 250ms 从 Worker 就绪后开始，避免把进程调度和启动时间误报成正则超时。
        clearTimeout(timeout);
        timeout = setTimeout(() => { if (generation === version) { stop(); completed = true; status('查找超时'); } }, 250);
        worker.postMessage(request);
        return;
      }
      const pending = queue;
      stop(); queue = pending;
      if (event.data.error) { queue = []; completed = true; status('正则表达式无效'); }
      else accept(event.data.matches);
    };
    worker.onerror = () => { if (generation === version) { stop(); completed = true; status('查找失败'); } };
    progress = setTimeout(() => { if (generation === version) status('正在搜索…'); }, 150);
    timeout = setTimeout(() => { if (generation === version) { stop(); completed = true; status('查找失败'); } }, 5000);
  };
  const close = () => {
    composing = false;
    compositionEvents?.abort(); compositionEvents = null;
    // 组词未确认即关闭时，原匹配只属于旧查询，不能用旧数量冒充新文字的结果。
    if (savedQuery !== label('当前文件查找').value) completed = false;
    savedQuery = label('当前文件查找').value;
    stop(); clearHighlight(); bar.remove(); bar = null;
    // 高亮只是查找状态；关闭后保留普通正文选区，不能继续涂亮所有匹配。
    if (current >= 0 && matches[current]) {
      const match = matches[current], range = document.createRange();
      const point = position => {
        let offset = 0;
        for (let index = 0; index < rows.length; index++) {
          if (position <= offset + sourceLines[index].length || index === rows.length - 1) {
            const content = rows[index].lastElementChild;
            if (!content.firstChild) content.append(document.createTextNode(''));
            return [content.firstChild, Math.min(sourceLines[index].length, Math.max(0, position - offset))];
          }
          offset += sourceLines[index].length + 1;
        }
      };
      range.setStart(...point(match.start)); range.setEnd(...point(match.start + match.length));
      const selection = window.getSelection(); selection.removeAllRanges(); selection.addRange(range);
    }
    code.focus({ preventScroll: true });
  };
  const wire = () => {
    const input = label('当前文件查找');
    compositionEvents?.abort();
    compositionEvents = new AbortController();
    const compositionOptions = { signal: compositionEvents.signal };
    input.value = savedQuery;
    bar.querySelectorAll('button[aria-label]').forEach(button => { button.title = button.getAttribute('aria-label'); });
    for (const [name, key] of [['区分大小写', 'case'], ['全字匹配', 'whole'], ['正则表达式', 'regex']]) {
      const button = label(name);
      button.setAttribute('aria-pressed', String(options[key]));
      button.onclick = () => { options[key] = !options[key]; button.setAttribute('aria-pressed', String(options[key])); preset = null; search(); };
    }
    label('上一项').onclick = () => navigate(true);
    label('下一项').onclick = () => navigate(false);
    label('关闭查找').onclick = close;
    input.addEventListener('compositionstart', () => {
      composing = true;
      compositionStatus = statusText;
      // 取消上一条未完成的查询与导航，不让 Worker 晚到结果移动正在组词的正文。
      stop();
    }, compositionOptions);
    const finishComposition = preservePosition => {
      if (!composing || !bar) return;
      composing = false;
      if (input.value === savedQuery && completed) status(compositionStatus);
      else { preset = null; search(preservePosition); }
    };
    input.oninput = event => {
      if (composing || event.isComposing) { status(''); return; }
      // compositionend 后的最终 input 通知可能重复送达，已完成的相同查询直接复用。
      if (input.value === savedQuery && (completed || busy)) return;
      preset = null; search();
    };
    input.addEventListener('compositionend', () => finishComposition(false), compositionOptions);
    input.addEventListener('blur', () => finishComposition(true), compositionOptions);
    bar.onkeydown = event => {
      if (composing || event.isComposing || event.keyCode === 229) return;
      if (event.key === 'Escape') { event.preventDefault(); close(); }
      else if (event.key === 'Enter' && event.target === input) { event.preventDefault(); navigate(event.shiftKey); }
      else if (event.key === 'Tab') {
        event.preventDefault();
        const controls = [...bar.querySelectorAll('input, button')];
        const index = controls.indexOf(document.activeElement);
        controls[(index + (event.shiftKey ? -1 : 1) + controls.length) % controls.length].focus();
      }
    };
    if (completed) {
      // 重新打开相同查询恢复原匹配，不将第一次导航提前消耗。
      if (matches.length) { current--; select(false); }
      else status(statusText);
    } else search();
    input.focus({ preventScroll: true });
  };
  const open = () => {
    if (view.dataset.markdownMode === 'preview') view.querySelector('[data-markdown-mode="source"]').click();
    readSource();
    if (!bar) { bar = retainedBar; view.insertBefore(bar, bodyRegion); wire(); }
    else label('当前文件查找').focus({ preventScroll: true });
  };
  const searchButton = view.querySelector('[aria-label="当前文件搜索"]');
  if (searchButton) searchButton.onclick = open;
  view.addEventListener('document-content-changed', () => {
    if (bar) savedQuery = label('当前文件查找').value;
    stop(); clearHighlight(); readSource(); matches = []; current = -1; completed = false;
    if (bar && view.dataset.markdownMode !== 'preview') { preset = null; search(); }
    else if (bar) { bar.remove(); bar = null; }
  });
  document.addEventListener('keydown', event => {
    if (!view.isConnected || view.closest('[hidden]') || !view.getClientRects().length
        || event.isComposing || event.keyCode === 229) return;
    if (event.ctrlKey && !event.shiftKey && !event.altKey && event.key.toLowerCase() === 'f') {
      event.preventDefault(); open();
    }
  });
  window.addEventListener('pagehide', stop, { signal: lifetime.signal });
  document.addEventListener('visibilitychange', () => {
    if (document.hidden) { composing = false; stop(); }
    else if (bar && (!completed || label('当前文件查找').value !== savedQuery)) search();
  }, { signal: lifetime.signal });
  if (bar) wire();
  else if (initialState) open();
}
