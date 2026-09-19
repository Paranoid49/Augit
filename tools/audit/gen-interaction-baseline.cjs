// 由 docs/baselines/pycharm-interactions.json 生成人类可读视图。
// 用法：node tools/audit/gen-interaction-baseline.cjs
// 零依赖；输出确定（按 id 排序），便于 diff 审查。
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../..');
const source = path.join(root, 'docs/baselines/pycharm-interactions.json');
const target = path.join(root, 'docs/baselines/pycharm-interactions.md');

const data = JSON.parse(fs.readFileSync(source, 'utf8'));

const esc = (value) => String(value === undefined || value === null ? '' : value)
  .replace(/\|/g, '\\|')
  .replace(/\n/g, ' ');
const compact = (value) => (value === undefined || value === null ? '' : JSON.stringify(value));
const status = (entry) => {
  const augit = entry.augit || {};
  if (augit.status === 'pass') return `✅ 一致（${esc(augit.ref || '')}）`;
  if (augit.status === 'diff') return `⚠️ 有差异（${esc(augit.note || augit.ref || '')}）`;
  if (augit.status === 'gap') return `❌ 缺口（${esc(augit.note || '')}）`;
  if (augit.status === 'todo') return `⏳ 待接线`;
  return `⏳ 未核对`;
};

const out = [];
out.push('# PyCharm 交互基线（由 JSON 生成，请勿手改）');
out.push('');
out.push('> 机器源：`docs/baselines/pycharm-interactions.json`。本文件由');
out.push('> `node tools/audit/gen-interaction-baseline.cjs` 生成；改动请改 JSON 再重新生成。');
out.push('> 目标 ⑯ 要求「补基线与核对共用同一份数据」——核对入口是');
out.push('> `node tools/audit/check-interactions.cjs`（校验每条 `augit.ref` 指向的断言真实存在）。');
out.push('');

const src = data.source || {};
out.push('## 采集环境');
out.push('');
out.push('| 项 | 值 |');
out.push('| --- | --- |');
for (const [key, value] of Object.entries(src)) {
  if (key === 'capture') continue;
  out.push(`| ${esc(key)} | ${esc(typeof value === 'object' ? JSON.stringify(value) : value)} |`);
}
out.push('');

if (data.decisions) {
  out.push('## 已确认的裁决');
  out.push('');
  for (const [key, value] of Object.entries(data.decisions)) {
    out.push(`- **${esc(key)}**：${esc(value.decision || '')}`);
    out.push(`  - 确认人/时间：${esc(value.confirmedBy || '')}${value.note ? `；备注：${esc(value.note)}` : ''}`);
    if (value.target) out.push(`  - 落地方案：\`${esc(compact(value.target))}\``);
  }
  out.push('');
}

if (data.surfaces && data.surfaces.length) {
  out.push('## 界面（surfaces）');
  out.push('');
  for (const surface of data.surfaces.slice().sort((a, b) => String(a.id).localeCompare(String(b.id)))) {
    out.push(`### ${esc(surface.id)}：${esc(surface.title || '')}`);
    out.push('');
    for (const [key, value] of Object.entries(surface)) {
      if (['id', 'title'].includes(key)) continue;
      out.push(`- **${esc(key)}**：\`${esc(typeof value === 'object' ? JSON.stringify(value) : value)}\``);
    }
    out.push('');
  }
}

const table = (title, rows, columns) => {
  out.push(`## ${title}`);
  out.push('');
  if (!rows || rows.length === 0) {
    out.push('（暂无）');
    out.push('');
    return;
  }
  out.push(`| ${columns.map((column) => column.label).join(' | ')} |`);
  out.push(`| ${columns.map(() => '---').join(' | ')} |`);
  for (const row of rows.slice().sort((a, b) => String(a.id).localeCompare(String(b.id)))) {
    out.push(`| ${columns.map((column) => esc(column.get(row))).join(' | ')} |`);
  }
  out.push('');
};

table('跳转关系（jumps）', data.jumps, [
  { label: 'id', get: (row) => row.id },
  { label: '触发', get: (row) => row.trigger },
  { label: '到达', get: (row) => row.to },
  { label: '类型', get: (row) => row.kind },
  { label: '规格出处', get: (row) => row.spec },
  { label: 'Augit', get: status },
]);

table('各态反馈（feedback）', data.feedback, [
  { label: 'id', get: (row) => row.id },
  { label: '目标', get: (row) => row.target },
  { label: 'PyCharm', get: (row) => compact(row.pycharm) },
  { label: 'Augit', get: (row) => compact(row.augit) },
  { label: '判读', get: (row) => row.verdict || row.ref || '' },
]);

if (data.sequences && data.sequences.length) {
  table('操作序列与状态转换（sequences）', data.sequences, [
    { label: 'id', get: (row) => row.id },
    { label: '步骤', get: (row) => compact(row.steps) },
    { label: '期望', get: (row) => compact(row.expect) },
    { label: '规格出处', get: (row) => row.spec },
    { label: 'Augit', get: status },
  ]);
}

const list = (title, rows, render) => {
  out.push(`## ${title}`);
  out.push('');
  if (!rows || rows.length === 0) {
    out.push('（暂无）');
    out.push('');
    return;
  }
  for (const row of rows) out.push(`- **${esc(row.id)}**：${render(row)}`);
  out.push('');
};

list('已知缺口（gaps）', data.gaps,
  (row) => `${esc(row.what)}｜证据：${esc(row.evidence)}｜Augit：${esc(row.augit)}｜计划：${esc(row.plan)}`);
list('阻塞（blockers）', data.blockers,
  (row) => `${esc(row.what)}｜原因：${esc(row.why)}｜证据：${esc(row.evidence)}｜解除：${esc(row.unblock)}`);
if (data.compareRecipe) {
  list('对照配方（compareRecipe）', [Object.assign({ id: 'compare-recipe' }, data.compareRecipe)],
    (row) => `${esc(row.goal)}｜主题：${esc(row.theme)}｜不覆盖：${compact(row.notCovered)}`);
}

fs.writeFileSync(target, `${out.join('\n')}\n`, 'utf8');
console.log(`WROTE ${path.relative(root, target)} (${out.length} lines)`);
