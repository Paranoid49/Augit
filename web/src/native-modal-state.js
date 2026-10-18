// 只刷新变化区域所属的所有者快照，最上层表单没有自己的背景副本。
export function snapshotDepthForMutation(mutation, dialogs) {
  // 观察器交付前节点可能已移除；脱离文档的通知不能误归属到主窗口背景。
  // 实际删除仍由连接中的父节点childList通知处理。
  if (mutation.target.isConnected === false) return null;
  // setAttribute重复写入原值仍会通知观察器；这不代表背景像素改变。
  if (mutation.type === 'attributes' && mutation.attributeName
      && mutation.oldValue === mutation.target.getAttribute(mutation.attributeName)) return null;
  const target = mutation.target.nodeType === 1 ? mutation.target : mutation.target.parentElement;
  const dialog = target?.closest?.('.dialog');
  const index = dialogs.indexOf(dialog);
  if (index === dialogs.length - 1 && index >= 0) return null;
  // 增删模态由窗口拓扑同步处理，不再捕获整份主窗口背景。
  if (mutation.type === 'childList') {
    const changed = [...mutation.addedNodes, ...mutation.removedNodes];
    if (changed.length && changed.every(node => node.nodeType === 1 && node.matches?.('.dialog, [data-augit-overlay]'))) return null;
  }
  return index < 0 ? 0 : index + 1;
}
