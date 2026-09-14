(() => {
  'use strict';

  // 首帧与后续外观消息共用原生宿主配置，不先用另一套字体或颜色排版。
  const initialConfiguration = window.__augitTerminalConfiguration || {
    fontFamily: 'Cascadia Mono, Consolas, monospace',
    fontSize: 13,
    theme: {
      background: '#1e1f22',
      foreground: '#d8d8d8',
      cursor: '#d8d8d8',
      selectionBackground: '#355a7a',
      scrollbarThumb: '#5a5f68',
      scrollbarThumbHover: '#737a84'
    }
  };
  const initialTheme = initialConfiguration.theme;

  document.documentElement.style.background = initialTheme.background;
  document.documentElement.style.setProperty('--augit-scrollbar-thumb', initialTheme.scrollbarThumb);
  document.documentElement.style.setProperty('--augit-scrollbar-thumb-hover', initialTheme.scrollbarThumbHover);
  document.body.style.background = initialTheme.background;

  const terminal = new Terminal({
    allowProposedApi: false,
    convertEol: false,
    cursorBlink: true,
    fontFamily: initialConfiguration.fontFamily,
    fontSize: initialConfiguration.fontSize,
    // 与原生正文的 1.7 倍等宽行高契约一致；字号变化时仍由 xterm 重新度量。
    lineHeight: initialConfiguration.lineHeight || 1.7,
    scrollback: 2000,
    theme: initialTheme
  });
  const fitAddon = new FitAddon.FitAddon();
  terminal.loadAddon(fitAddon);
  terminal.open(document.getElementById('terminal'));

  let resizeFrame = 0;
  // 原生工具窗口完成布局之前，保留默认 80×24，不能使用隐藏宿主的临时尺寸启动 Shell。
  let layoutReady = false;
  const fit = () => {
    if (resizeFrame !== 0) {
      cancelAnimationFrame(resizeFrame);
    }

    resizeFrame = requestAnimationFrame(() => {
      resizeFrame = 0;
      if (layoutReady) {
        fitAddon.fit();
      }
    });
  };

  terminal.onData(data => {
    window.chrome.webview.postMessage({ type: 'input', data });
  });
  terminal.onResize(size => {
    window.chrome.webview.postMessage({ type: 'resize', columns: size.cols, rows: size.rows });
  });
  window.chrome.webview.addEventListener('message', event => {
    const message = event.data;
    if (!message || typeof message.type !== 'string') {
      return;
    }

    if (message.type === 'output' && typeof message.data === 'string') {
      terminal.write(message.data);
    } else if (message.type === 'layout') {
      layoutReady = message.visible === true;
      fit();
    } else if (message.type === 'configure') {
      terminal.options.fontFamily = message.fontFamily;
      terminal.options.fontSize = message.fontSize;
      terminal.options.lineHeight = message.lineHeight || 1.7;
      terminal.options.theme = message.theme;
      document.documentElement.style.background = message.theme.background;
      document.documentElement.style.setProperty('--augit-scrollbar-thumb', message.theme.scrollbarThumb);
      document.documentElement.style.setProperty('--augit-scrollbar-thumb-hover', message.theme.scrollbarThumbHover);
      document.body.style.background = message.theme.background;
      document.getElementById('terminal').style.background = 'transparent';
      fit();
    } else if (message.type === 'focus') {
      terminal.focus();
    }
  });

  new ResizeObserver(fit).observe(document.getElementById('terminal'));
  window.chrome.webview.postMessage({
    type: 'ready',
    columns: terminal.cols,
    rows: terminal.rows
  });
  terminal.focus();
})();
