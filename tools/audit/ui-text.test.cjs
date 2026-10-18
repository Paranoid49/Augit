// 英文说明转换不能改写用户数据，也不能让大型正文因无关转换被复制。
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

(async () => {
  const source = fs.readFileSync(path.resolve(__dirname, '../../web/src/ui-text.js'), 'utf8');
  const { uiMessage, uiPayload } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
  assert.equal(uiMessage('操作已取消。'), 'Operation cancelled.');
  assert.equal(uiMessage('你的本地改动会被覆盖。'), 'Your local changes would be overwritten.');
  assert.equal(uiMessage('document/read 需要 path 参数。'), 'document/read requires a path parameter.');
  assert.equal(uiMessage('当前分支 · 中文分支'), 'Current branch · 中文分支');
  assert.equal(uiMessage('fatal: 中文 hook 输出'), 'fatal: 中文 hook 输出');
  const content = { text: '操作已取消。', message: '操作已取消。', files: [{ path: '中文目录/文件.txt' }] };
  assert.equal(uiPayload(content, 'git/commit-details'), content);
  const failure = { reason: '操作已取消。', ...content };
  const translated = uiPayload(failure, 'git/commit-details');
  assert.equal(translated.reason, 'Operation cancelled.');
  assert.equal(translated.text, content.text);
  assert.equal(translated.message, content.message);
  assert.equal(translated.files, content.files);
  assert.equal(failure.reason, '操作已取消。');
  console.log('ui-text：英文说明与用户数据保留验证通过。');
})().catch((error) => { console.error(error); process.exitCode = 1; });
