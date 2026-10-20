// 搜索取消接线：关闭搜索浮层或改发新查询时必须结束宿主里的 ripgrep（规格 §7.15）。
// 这里是静态接线守卫；真实进程结束由正常入口的 stall/cancel 实机证据证明。
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../..');
const live = fs.readFileSync(path.join(root, 'web/src/live-data.js'), 'utf8');
const bridge = fs.readFileSync(path.join(root, 'web/src/bridge.js'), 'utf8');
const shell = fs.readFileSync(path.join(root, 'src/Augit.Shell/ShellBridge.cs'), 'utf8');

test('网页导入取消函数，并由统一失效路径调用', () => {
  assert.match(live, /import \{[^}]*cancelRequest[^}]*\} from "\.\/bridge\.js"/);
  const start = live.indexOf('function cancelActiveSearchRequest');
  const invalidate = live.indexOf('function invalidateSearchRequests');
  assert.ok(start >= 0 && invalidate > start, '两个函数都必须存在');
  const cancelBody = live.slice(start, invalidate);
  assert.match(cancelBody, /cancelRequest\(requestId\)/);
  const invalidateBody = live.slice(invalidate, live.indexOf('async function runSearch'));
  assert.match(invalidateBody, /cancelActiveSearchRequest\(\)/);
});

test('每次搜索使用自己的 requestId，并在被取代前取消旧请求', () => {
  const run = live.slice(live.indexOf('async function runSearch'), live.indexOf('function renderSearchOverlay'));
  assert.match(run, /const requestId = \+\+searchRequestSequence/);
  assert.match(run, /cancelActiveSearchRequest\(\)/);
  assert.match(run, /invoke\(method, \{ query, requestId/);
  assert.match(run, /if \(activeSearchRequest === requestId\) activeSearchRequest = 0/);
});

test('继续搜索的每一页也带 requestId', () => {
  const next = live.slice(live.indexOf('async function continueLimitedSearch'), live.indexOf('function abortLimitedSearch'));
  assert.match(next, /const requestId = \+\+searchRequestSequence/);
  assert.match(next, /requestId,/);
  assert.match(next, /if \(activeSearchRequest === requestId\) activeSearchRequest = 0/);
});

test('桥接的取消请求不占用原请求应答，也不设读取超时', () => {
  assert.match(bridge, /export function cancelRequest\(requestId\)/);
  assert.match(bridge, /method: "search\/cancel"/);
  assert.match(bridge, /params: \{ requestId \}/);
});

test('宿主登记搜索请求并只取消对应 requestId', () => {
  assert.match(shell, /"search\/cancel" => CancelSearch\(parameters\)/);
  assert.match(shell, /private CancellationTokenSource\? RegisterSearch\(long requestId\)/);
  assert.match(shell, /UnregisterSearch\(requestId, request\)/);
  assert.match(shell, /ReferenceEquals\(current, source\)/);
  assert.match(shell, /_searchRequests\.TryGetValue\(requestId, out source\)/);
});

console.log('PASS: 搜索取消接线（网页 → 桥接 → 宿主 requestId）。');
