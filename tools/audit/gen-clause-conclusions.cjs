// §2 非"是"条文的**结论清单**生成器（⑨/⑩：覆盖率要写成有分母的事实）。
//
// 为什么需要它：§2 里 351 条条文中有 138 条是"部分/未覆盖"，散落在 §2.1–§2.8 各表里；
// 之前只能靠人翻。本生成器把它们集中成一张表，**逐行给出结论类别**（不是重写条文）：
//   A 实现已有、仅缺断言      —— 行内缺什么里明说"没有断言/未单独断言"
//   B 只有像素或间接证据      —— 行内提到像素基线/间接证据
//   C 未覆盖（无断言也无观察）—— 状态就是"未覆盖"
//   D 其它部分覆盖            —— 其余"部分"（多为"某个子条件没断言"）
// 用法：node tools/audit/gen-clause-conclusions.cjs [--write]
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const docPath = path.join(root, 'docs/ui-compliance.md');
const doc = fs.readFileSync(docPath, 'utf8');

const secs = [];
const re = /^### (2\.\d+) .*$/gm;
let m;
while ((m = re.exec(doc))) secs.push({ at: m.index, name: m[1] });
// 本生成器自己插入的 §2.10 也是 '### 2.x' 开头，必须排除，否则第二次运行会把自己的表当成条文行
// （第一次运行实测就是这样：再生成一次 rows 翻倍、幂等检查失败）。
const end = doc.indexOf('### 3.1');
secs.push({ at: end, name: 'end' });

const rows = [];
for (let i = 0; i < secs.length - 1; i++) {
  if (secs[i].name === '2.10') continue;
  const body = doc.slice(secs[i].at, secs[i + 1].at);
  for (const line of body.split('\n')) {
    const t = line.trim();
    const r = /^\| (?:\d+ \| )?(§[\d.]+) \| (.*?) \| (是|部分|未覆盖) \| (.*) \|$/.exec(t);
    if (!r || r[3] === '是') continue;
    const missing = r[4];
    let kind, next;
    if (r[3] === '未覆盖') { kind = 'C 未覆盖（无断言也无观察）'; next = '补断言或明确不做（需用户裁决）'; }
    else if (/没有断言|未单独断言|没有单独断言/.test(missing)) { kind = 'A 实现已有、仅缺断言'; next = '补 harness 断言 → 可转"是"'; }
    else if (/像素|间接/.test(missing)) { kind = 'B 只有像素或间接证据'; next = '把像素/间接证据升级为可复跑断言'; }
    else { kind = 'D 其它部分覆盖'; next = '定位子条件后补断言'; }
    rows.push({ section: secs[i].name, clause: r[1], status: r[3], text: r[2], kind, next, missing });
  }
}
const counts = rows.reduce((acc, r) => { acc[r.kind] = (acc[r.kind] || 0) + 1; return acc; }, {});
const out = [];
out.push('### 2.10 非"是"条文的结论清单（⑨/⑩，逐行；生成器 `tools/audit/gen-clause-conclusions.cjs`）');
out.push('');
out.push(`**分母**：§2.1–§2.8 里状态为**非"是"**的条文共 **${rows.length}** 行，分类如下：`);
out.push('');
out.push('| 结论类别 | 行数 | 含义与下一步 |');
out.push('| --- | ---: | --- |');
for (const kind of Object.keys(counts).sort()) {
  const first = rows.find((r) => r.kind === kind);
  out.push(`| ${kind} | ${counts[kind]} | ${first.next} |`);
}
out.push('');
out.push('| 小节 | 条文 | 状态 | 结论类别 | 缺什么（原文摘录） |');
out.push('| --- | --- | --- | --- | --- |');
const esc = (t) => String(t).replace(/\|/g, '\\|');
for (const r of rows) {
  out.push(`| §${r.section} | ${r.clause} | ${r.status} | ${r.kind} | ${esc(r.missing).slice(0, 120)} |`);
}
out.push('');
out.push('> **口径**：本表**只做归类**，不改写任何条文的状态；"实现已有、仅缺断言"这一类是下一步补断言的队列，');
out.push('> "未覆盖"那一类要么补断言、要么按纪律交用户裁决，**不写成通过**。');
out.push('');
const generated = out.join('\n');

if (!process.argv.includes('--write')) { process.stdout.write(generated); process.exit(0); }
const at = doc.indexOf('### 2.10 ');
const anchor = doc.indexOf('### 3.1 ');
if (anchor < 0) { console.error('GEN_CLAUSE_CONCLUSIONS_FAILED no 3.1'); process.exit(1); }
if (at >= 0) {
  const next = doc.indexOf('\n### ', at + 1);
  fs.writeFileSync(docPath, doc.slice(0, at) + generated.replace(/\n$/, '') + '\n' + (next < 0 ? '' : doc.slice(next + 1)));
} else {
  fs.writeFileSync(docPath, doc.slice(0, anchor) + generated + doc.slice(anchor));
}
console.log(`WROTE 2.10 rows=${rows.length} ` + Object.entries(counts).map(([k, v]) => `${k}=${v}`).join(' '));
