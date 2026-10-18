// 宿主桥接：在 WebView2 外壳内通过 postMessage 调用 C# 能力。
// 在浏览器里打开 HTML 视觉稿时没有宿主，调用方必须回退到样例数据。

import { uiMessage, uiPayload } from "./ui-text.js";

const hostAvailable = typeof window !== "undefined" && !!window.chrome?.webview;

const pending = new Map();
let sequence = 0;

// Git 写入和网络操作只能由宿主结束或用户取消；网页超时不能说明本机 Git 已经停止。
// 在公共桥接处固定这一契约，避免各入口遗留的超时值提前解除忙状态。
const waitsForHost = new Set([
  "git/init", "git/commit-create", "git/push", "git/checkout", "git/checkout-smart",
  "git/branch", "git/tag", "git/fetch", "git/remote-write", "git/rollback", "git/reset",
  "git/stash", "git/stash-write", "git/worktree-write", "git/worktree-remove",
  "git/operation-action", "git/clone",
  "write/cancel",
]);

// 宿主事件订阅：事件消息不带 id，因此与请求应答天然区分。
const eventHandlers = new Map();

if (hostAvailable) {
  window.chrome.webview.addEventListener("message", (event) => {
    const message = typeof event.data === "string" ? safeParse(event.data) : event.data;
    if (!message) return;
    // 宿主推送的事件（例如文件系统变化）走这里，不参与请求应答匹配。
    if (message.event !== undefined) {
      const handler = eventHandlers.get(message.event);
      if (handler) handler(message.payload);
      return;
    }

    if (message.id === undefined) return;
    const entry = pending.get(message.id);
    if (!entry) return;
    pending.delete(message.id);
    if (message.error) entry.reject(new Error(uiMessage(message.error)));
    else entry.resolve(message.result);
  });
}

function safeParse(text) {
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

/** 订阅宿主事件；返回取消订阅函数。 */
export function subscribe(event, handler) {
  eventHandlers.set(event, handler);
  return () => eventHandlers.delete(event);
}

/** 是否有 C# 宿主可用。 */
export const hasHost = () => hostAvailable;

/** 调用宿主方法；Git 写入、网络与取消始终等待宿主结束，其余方法按 timeoutMs 超时。 */
export function invoke(method, params = {}, timeoutMs = 15000) {
  if (!hostAvailable) return Promise.reject(new Error("no-host"));
  const deadline = waitsForHost.has(method) ? 0 : timeoutMs;
  const id = ++sequence;
  return new Promise((resolve, reject) => {
    const timer = deadline > 0 ? setTimeout(() => {
      pending.delete(id);
      reject(new Error(`host-timeout:${method}`));
    }, deadline) : null;
    pending.set(id, {
      resolve: (value) => {
        if (timer !== null) clearTimeout(timer);
        resolve(uiPayload(value, method));
      },
      reject: (error) => {
        if (timer !== null) clearTimeout(timer);
        reject(error);
      },
    });
    try {
      window.chrome.webview.postMessage(JSON.stringify({ id, method, params }));
    } catch (error) {
      pending.delete(id);
      if (timer !== null) clearTimeout(timer);
      reject(error);
    }
  });
}
