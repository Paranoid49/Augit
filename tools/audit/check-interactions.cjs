// 交互基线的核对器：保证「基线」与「断言」不会各自漂移。
//
// 设计说明（为什么不在这里开浏览器）：
// 真正的**重放**由 `tools/audit/live-shell.spec.cjs` 在真实数据路径上执行（它是本仓库唯一
// 会启动 WebView2 宿主桩、用真实鼠标键盘驱动并比对 DOM/像素的套件）。本检查器做的是
// **可核对的连接检查**——对 JSON 里每条声称 `pass` 的记录，验证它引用的断言名真的存在于
// 套件里；对 `gap/diff` 的记录，验证它已被写进交付文档。这样任何一方改名、删除或
// 「声明通过但没有断言」都会在这里立刻暴露。
//
// 用法：node tools/audit/check-interactions.cjs
// 退出码：0 = 全部可核对；1 = 有记录与断言/文档脱节。
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../..');
const baselinePath = path.join(root, 'docs/baselines/pycharm-interactions.json');
const specPath = path.join(root, 'tools/audit/live-shell.spec.cjs');
const docs = [
  path.join(root, 'docs/ui-compliance.md'),
  path.join(root, 'docs/baselines/mockup-gap-inventory.md'),
  path.join(root, 'docs/ui-refactor-baseline.md'),
];

const baseline = JSON.parse(fs.readFileSync(baselinePath, 'utf8'));
const spec = fs.readFileSync(specPath, 'utf8');
const docText = docs.map((file) => fs.readFileSync(file, 'utf8')).join('\n');

const problems = [];
const stats = { checked: 0, pass: 0, gap: 0, diff: 0, other: 0 };

const refsOf = (entry) => {
  const augit = entry.augit || {};
  const refs = [];
  for (const value of [augit.ref, entry.ref]) {
    if (typeof value !== 'string') continue;
    for (const piece of value.split('|')) {
      const label = piece.split(':').slice(1).join(':').trim();
      if (label.length > 0) refs.push(label);
    }
  }
  return refs;
};

const checkEntry = (kind, entry) => {
  stats.checked += 1;
  const augit = entry.augit || {};
  const where = `${kind}:${entry.id}`;
  if (augit.status === 'pass') {
    stats.pass += 1;
    const refs = refsOf(entry);
    if (refs.length === 0) {
      problems.push(`${where} 声称 pass 但没有引用任何断言（augit.ref 为空）`);
      return;
    }
    for (const label of refs) {
      if (!spec.includes(label)) {
        problems.push(`${where} 引用的断言在 live-shell.spec.cjs 里不存在：${label}`);
      }
    }
    return;
  }
  if (augit.status === 'gap') {
    stats.gap += 1;
    // 记为缺口必须**明确指出写在哪份文档里**（docsRef），而不是靠关键词猜测：
    // 猜关键词会把"同一件事换了措辞"误判成缺文档，也会漏掉真的没写的情况。
    const keyword = String(augit.docsRef || '');
    if (keyword.length === 0) {
      problems.push(`${where} 记为缺口但没有 augit.docsRef（无法核对文档是否记录了它）`);
    } else if (!docText.includes(keyword)) {
      problems.push(`${where} 的 docsRef 在交付文档里找不到：${keyword}`);
    }
    return;
  }
  if (augit.status === 'diff') {
    stats.diff += 1;
    if (!augit.note) problems.push(`${where} 记为差异但没有写明确原因（augit.note）`);
    return;
  }
  stats.other += 1;
};

for (const entry of baseline.jumps || []) checkEntry('jump', entry);
for (const entry of baseline.feedback || []) checkEntry('feedback', entry);
for (const entry of baseline.sequences || []) checkEntry('sequence', entry);

// 每条 gap 必须同时出现在 JSON 与三方覆盖清单里
const gapIds = new Set();
for (const gap of baseline.gaps || []) {
  if (!gap.id || !gap.what || !gap.augit || !gap.plan || !gap.docsRef) {
    problems.push(`gap ${gap.id || '<无 id>'} 缺少 what/augit/plan/docsRef 字段`);
    continue;
  }
  if (!docText.includes(gap.docsRef)) {
    problems.push(`gap ${gap.id} 的 docsRef 在交付文档里找不到：${gap.docsRef}`);
  }
  gapIds.add(gap.id);
}

const assertionCount = (spec.match(/check\(/g) || []).length;
console.log(`BASELINE surfaces=${(baseline.surfaces || []).length} jumps=${(baseline.jumps || []).length} `
  + `feedback=${(baseline.feedback || []).length} sequences=${(baseline.sequences || []).length} `
  + `gaps=${gapIds.size}`);
console.log(`CHECKED ${stats.checked} (pass=${stats.pass} diff=${stats.diff} gap=${stats.gap} other=${stats.other}) `
  + `against ${assertionCount} check() call sites`);
if (problems.length > 0) {
  for (const problem of problems) console.log('PROBLEM ' + problem);
  console.log(`INTERACTIONS_BASELINE_FAILED problems=${problems.length}`);
  process.exit(1);
}
console.log('INTERACTIONS_BASELINE_OK');
