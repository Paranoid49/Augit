// 宿主桥接：在 WebView2 外壳内通过 postMessage 调用 C# 能力。
// 在浏览器里打开 HTML 视觉稿时没有宿主，调用方必须回退到样例数据。

const hostAvailable = typeof window !== "undefined" && !!window.chrome?.webview;

const pending = new Map();
let sequence = 0;

if (hostAvailable) {
  window.chrome.webview.addEventListener("message", (event) => {
    const message = typeof event.data === "string" ? safeParse(event.data) : event.data;
    if (!message || message.id === undefined) return;
    const entry = pending.get(message.id);
    if (!entry) return;
    pending.delete(message.id);
    if (message.error) entry.reject(new Error(message.error));
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

/** 是否有 C# 宿主可用。 */
export const hasHost = () => hostAvailable;

/** 调用宿主方法；无宿主时抛出，调用方负责回退。 */
export function invoke(method, params = {}, timeoutMs = 15000) {
  if (!hostAvailable) return Promise.reject(new Error("no-host"));
  const id = ++sequence;
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      pending.delete(id);
      reject(new Error(`host-timeout:${method}`));
    }, timeoutMs);
    pending.set(id, {
      resolve: (value) => {
        clearTimeout(timer);
        resolve(value);
      },
      reject: (error) => {
        clearTimeout(timer);
        reject(error);
      },
    });
    window.chrome.webview.postMessage(JSON.stringify({ id, method, params }));
  });
}
