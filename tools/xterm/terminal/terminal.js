(() => {
  'use strict';

  const terminal = new Terminal({
    allowProposedApi: false,
    convertEol: false,
    cursorBlink: true,
    fontFamily: 'Cascadia Mono, Consolas, monospace',
    fontSize: 14,
    scrollback: 2000,
    theme: {
      background: '#1e1f22',
      foreground: '#d8d8d8',
      cursor: '#d8d8d8',
      selectionBackground: '#355a7a'
    }
  });
  const fitAddon = new FitAddon.FitAddon();
  terminal.loadAddon(fitAddon);
  terminal.open(document.getElementById('terminal'));

  let resizeFrame = 0;
  const fit = () => {
    if (resizeFrame !== 0) {
      cancelAnimationFrame(resizeFrame);
    }

    resizeFrame = requestAnimationFrame(() => {
      resizeFrame = 0;
      fitAddon.fit();
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
    } else if (message.type === 'configure') {
      terminal.options.fontFamily = message.fontFamily;
      terminal.options.fontSize = message.fontSize;
      terminal.options.theme = message.theme;
      document.documentElement.style.background = message.theme.background;
      document.body.style.background = message.theme.background;
      fit();
    } else if (message.type === 'focus') {
      terminal.focus();
    }
  });

  new ResizeObserver(fit).observe(document.getElementById('terminal'));
  fitAddon.fit();
  window.chrome.webview.postMessage({
    type: 'ready',
    columns: terminal.cols,
    rows: terminal.rows
  });
  terminal.focus();
})();
