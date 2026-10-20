// 紧凑输入窗口（New Branch / Rename / New Tag / Go to Line）的 Tab 顺序。
// 纯函数，供 live-data 绑定键盘与 Node 回归测试共用：禁用的主按钮不能吃 Tab，
// 否则焦点会停在 Cancel 上无法前进。
export function compactDialogFocusOrder(candidates) {
  return (candidates || []).filter((node) => node
    && node.disabled !== true
    && !(typeof node.getAttribute === "function" && node.getAttribute("aria-disabled") === "true"));
}

/** 返回下一个焦点目标；当前焦点不在顺序里时：向前取首项，向后取末项。 */
export function nextCompactDialogFocus(order, active, backwards = false) {
  if (!Array.isArray(order) || order.length === 0) return null;
  const index = order.indexOf(active);
  const from = index >= 0 ? index : (backwards ? 0 : -1);
  const step = backwards ? -1 : 1;
  return order[(from + step + order.length) % order.length] || null;
}
