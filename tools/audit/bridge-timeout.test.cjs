// 可控时钟验证真实桥接超时、无自动超时及发送失败释放，不启动浏览器或服务器。
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.resolve(__dirname, '../../web/src/bridge.js'), 'utf8');
function bridge() {
  const timers = new Map(), sent = [], listeners = {};
  let token = 0, fail = false;
  const context = vm.createContext({
    window: { chrome: { webview: {
      addEventListener(type, handler) { listeners[type] = handler; },
      postMessage(value) { if (fail) throw new Error('send-failed'); sent.push(JSON.parse(value)); },
    } } },
    setTimeout(callback, ms) { const id = ++token; timers.set(id, { callback, ms }); return id; },
    clearTimeout(id) { timers.delete(id); },
    uiMessage: value => value, uiPayload: value => value,
  });
  vm.runInContext(source.replace(/^import .*;\s*$/m, '').replaceAll('export ', '')
    + '\nglobalThis.call = invoke;', context);
  return { call: context.call, timers, sent,
    reply(result, error) { listeners.message({ data: { id: sent.at(-1).id, result, error } }); },
    failSend() { fail = true; },
  };
}
(async () => {
  // 覆盖使用默认值与遗留显式超时的调用者；等待期间不能伪造失败或丢弃晚到结果。
  const operations = [
    'git/init', 'git/commit-create', 'git/push', 'git/checkout', 'git/checkout-smart',
    'git/branch', 'git/tag', 'git/fetch', 'git/remote-write', 'git/rollback', 'git/reset',
    'git/stash', 'git/stash-write', 'git/worktree-write', 'git/worktree-remove',
    'git/operation-action', 'git/clone', 'write/cancel',
  ];
  for (const method of operations) {
    for (const explicitTimeout of [undefined, 60000, 600000]) {
      const host = bridge();
      let finished = false;
      const operation = host.call(method, { marker: method }, explicitTimeout).then(value => {
        finished = true;
        return value;
      });
      assert.equal(host.timers.size, 0, method);
      await Promise.resolve();
      assert.equal(finished, false, method);
      host.reply({ completed: method });
      assert.deepEqual(await operation, { completed: method });
    }
  }

  const waiting = bridge();
  let settled = false;
  const fetch = waiting.call('git/fetch', {}, 0).then(value => { settled = true; return value; });
  assert.equal(waiting.timers.size, 0);
  await Promise.resolve(); assert.equal(settled, false);
  waiting.reply({ fetched: true });
  assert.deepEqual(await fetch, { fetched: true });
  assert.equal(waiting.timers.size, 0);

  const read = bridge();
  const timeout = read.call('workspace/list', {}, 30000);
  assert.equal([...read.timers.values()][0].ms, 30000);
  [...read.timers.values()][0].callback();
  await assert.rejects(timeout, /host-timeout:workspace\/list/);
  read.reply({ entries: [] });

  const success = bridge();
  const answer = success.call('workspace/info');
  assert.equal(success.timers.size, 1);
  success.reply('ok'); assert.equal(await answer, 'ok');
  assert.equal(success.timers.size, 0);

  const error = bridge();
  const rejected = error.call('git/remote-write', {}, 0);
  error.reply(null, 'git-failed'); await assert.rejects(rejected, /git-failed/);
  assert.equal(error.timers.size, 0);

  const send = bridge(); send.failSend();
  await assert.rejects(send.call('workspace/info'), /send-failed/);
  assert.equal(send.timers.size, 0);
  await assert.rejects(send.call('git/fetch', {}, 0), /send-failed/);
  assert.equal(send.timers.size, 0);
  console.log('PASS: 桥接无自动超时、读取超时、响应释放及发送失败场景。');
})().catch(error => { console.error(error); process.exitCode = 1; });
