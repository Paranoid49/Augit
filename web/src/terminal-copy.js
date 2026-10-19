// 终端选区复制的快捷键判定。纯函数，便于 Node 侧回归测试；
// 不处理剪贴板本身（WebView2 需要走宿主 clipboard/write）。
export function isTerminalCopyShortcut(event) {
  if (!event || event.type !== 'keydown' || !event.ctrlKey) return false;
  if (event.altKey || event.metaKey) return false;
  if (event.shiftKey) {
    return event.code === 'KeyC' || event.key === 'C' || event.key === 'c';
  }
  return event.code === 'Insert';
}
