// 覆盖表生成器的定向回归检查，并守卫 §3.6 的环境事实断言；不写入仓库文档。
const assert = require('node:assert/strict');
const { spawnSync, execFileSync } = require('node:child_process');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

const root = path.resolve(__dirname, '../..');
const docPath = path.join(root, 'docs/ui-compliance.md');
const sectionOf = (text, heading) => {
  const start = text.indexOf(heading);
  if (start < 0) return null;
  const next = text.indexOf('\n### ', start + heading.length);
  return text.slice(start, next < 0 ? text.length : next);
};
const rowsOf = (section) => {
  const rows = new Map();
  for (const line of section.split('\n')) {
    const match = /^\| `([^`]+)` \| ([\d.]+) \| ([\d.]+) \| ([\d.]+) \|/.exec(line.trim());
    if (match) rows.set(match[1], match.slice(2, 5));
  }
  return rows;
};

const doc = fs.readFileSync(docPath, 'utf8');
const pixels = rowsOf(sectionOf(doc, '### 1.1 '));
const coverage = rowsOf(sectionOf(doc, '### 1.4 '));
assert.equal(pixels.size, 55, '§1.1 应包含 55 个场景');
assert.equal(coverage.size, pixels.size, '§1.4 的 A 线行数应与 §1.1 一致');
for (const [scene, expected] of pixels) {
  assert.deepEqual(coverage.get(scene), expected, `§1.4 的 ${scene} 像素值应与 §1.1 一致`);
}

const tableOutput = execFileSync(process.execPath,
  [path.join(__dirname, 'gen-coverage-table.cjs')], { cwd: root, encoding: 'utf8' });
const cLine = /\| C 线：PyCharm 对照 \| 55 个视觉稿场景 \| (?:\*\*)?33 页面级已对照 \+ 5 页仅入口级证据 \+ 17 页未对照(?:\*\*)?[^|]*\|/;
assert.match(tableOutput, cLine, '§1.4 生成器的 C 线模板应使用当前覆盖口径');
assert.match(sectionOf(doc, '### 1.4 '), cLine, '§1.4 文档 C 线应与生成器模板一致');

// 逐页行还要比数字列之外的部分：判读文本里一个空格级的漂移不会改变 §1.1/§1.4 的三列值，
// 但会让"重跑即可消除漂移"的生成器输出与文档不再逐字节相同（第 311 轮实测抓到 `diff-status` 一处）。
const tableRowsOf = (text) => text.split('\n')
  .filter((line) => /^\| `[^`]+` \| [\d.]+ \| [\d.]+ \| [\d.]+ \|/.test(line.trim()))
  .map((line) => line.trim());
const generatedRows = tableRowsOf(tableOutput);
const documentedRows = tableRowsOf(sectionOf(doc, '### 1.4 '));
assert.equal(generatedRows.length, pixels.size, '§1.4 生成表应有全部逐页行');
assert.deepEqual(documentedRows, generatedRows, '§1.4 逐页行应与生成器输出逐字节一致');

// 四类分档与 §0.2 摘要的机械核对：§0.2 此前自述这组数字"靠人工转录"，
// 这里从 §1.6 逐行提取"未对照（分档）"计数，并要求 §0.2 的摘要声明同一组数字。
const categoryCounts = (text) => {
  const section = sectionOf(text, '### 1.6 ');
  assert.ok(section, '应能找到 §1.6 PyCharm 对照表');
  const counts = new Map();
  for (const line of section.split('\n')) {
    const row = line.trim();
    const match = /^\| `[^`]+` \| 未对照（([A-Z_]+)） \|/.exec(row);
    if (match) counts.set(match[1], (counts.get(match[1]) || 0) + 1);
    else assert.ok(!/^\| `[^`]+` \| 未对照 \|/.test(row), `§1.6 出现无分档的"未对照"行：${row}`);
  }
  return counts;
};
assert.deepEqual([...categoryCounts(doc).entries()].sort(),
  [['AUGIT_ONLY', 1], ['MEDIUM_BLOCKED', 9], ['PRECONDITION', 5], ['RECOVERABLE', 2]],
  '§1.6 的四类分档应为 AUGIT_ONLY 1 / MEDIUM_BLOCKED 9 / PRECONDITION 5 / RECOVERABLE 2');
const summarySection = sectionOf(doc, '### 0.2 ');
assert.ok(summarySection, '应能找到 §0.2 交接摘要');
for (const claim of ['33/55 面级已对照', 'MEDIUM_BLOCKED 9 页', 'RECOVERABLE 2 页',
  'PRECONDITION 5 页', 'AUGIT_ONLY 1 页', '9 + 2 + 5 + 1 = 17']) {
  assert.ok(summarySection.includes(claim), `§0.2 摘要应声明 "${claim}"（与 §1.6 的表一致）`);
}

// --write 只在临时副本运行，验证写回摘要的三类计数且不触碰仓库文档。
const tempRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'augit-coverage-generator-'));
try {
  const tempDoc = path.join(tempRoot, 'docs', 'ui-compliance.md');
  const tempScript = path.join(tempRoot, 'tools', 'audit', 'gen-pycharm-coverage.cjs');
  fs.mkdirSync(path.dirname(tempDoc), { recursive: true });
  fs.mkdirSync(path.dirname(tempScript), { recursive: true });
  fs.copyFileSync(docPath, tempDoc);
  fs.copyFileSync(path.join(__dirname, 'gen-pycharm-coverage.cjs'), tempScript);

  const originalSection = sectionOf(doc, '### 1.6 ');
  const originalCore = originalSection.split('\n')
    .filter((line) => !line.startsWith('> **17 页的分档小计'))
    .join('\n').trimEnd();
  const result = spawnSync(process.execPath, [tempScript, '--write'], { encoding: 'utf8' });
  assert.equal(result.status, 0, result.stderr || result.error?.message || '生成器写回失败');
  assert.match(result.stdout,
    /WROTE 1\.6 scenes=55 covered=33 entry=5 uncovered=17/,
    '写回摘要应区分面级、入口级和未对照页面');

  const regeneratedDoc = fs.readFileSync(tempDoc, 'utf8');
  const regeneratedSection = sectionOf(regeneratedDoc, '### 1.6 ').trimEnd();
  assert.equal(originalCore, regeneratedSection, '§1.6 表体应与生成器一致，手写指针行除外');
  const generatedRows = regeneratedSection.split('\n').filter((line) => /^\| `[^`]+` \|/.test(line));
  assert.equal(generatedRows.length, 55, '§1.6 应覆盖全部 55 个场景');
  assert.equal(generatedRows.filter((line) => line.includes('| **面级已对照** |')).length, 33);
  assert.equal(generatedRows.filter((line) => line.includes('| 仅入口级证据 |')).length, 5);
  assert.equal(generatedRows.filter((line) => line.includes('| 未对照')).length, 17);
} finally {
  fs.rmSync(tempRoot, { recursive: true, force: true });
}

// 环境断言回归守卫：第 311 轮曾据一次只读探针把"本机当前没有 PyCharm 进程／没有任何 PyCharm 窗口"
// 写成当前事实，并把 PyCharm 的 `SunAwtFrame` 窗口误记为"Augit 另一个实例"；后续复查证明当时
// `pycharm64` 正在运行（窗口只是被最小化）。这里钉死这几句旧断言，并要求 §3.6 保留本轮复查事实，
// 防止把它们重新写成当前事实、也防止把"窗口在运行"误写成"已完成 17 个场景取证"。
const staleEnvPatterns = [
  /本机当前[^\n]{0,6}没有 PyCharm/,
  /没有任何 PyCharm 窗口/,
  /Augit 另一个实例/,
];
for (const pattern of staleEnvPatterns) {
  const found = pattern.exec(doc);
  assert.ok(!found,
    `文档不得再出现不可复现的环境断言"${found && found[0]}"（第 311 轮已订正，见 §3.6）`);
}
const envSection = sectionOf(doc, '### 3.6 ');
assert.ok(envSection, '应能找到 §3.6 环境观测段');
assert.match(envSection, /PID 20504/, '§3.6 应保留本轮复查的 PyCharm PID');
assert.match(envSection, /SunAwtFrame/, '§3.6 应保留本轮复查的 PyCharm 顶层窗口类名');
assert.match(envSection, /IsIconic=True/, '§3.6 应保留本轮复查的最小化状态');
assert.match(envSection, /不可复现/, '§3.6 应写明第 311 轮零命中不可复现');
assert.match(envSection, /一行都没有新证据/, '§3.6 应明确本轮仍未取得 17 个场景的取证');
assert.match(envSection, /JBRCustomTitleBarControls/, '§3.6 应保留只读 UIA 只能枚举到标题栏 Pane 的复核事实');
assert.match(envSection, /MENUBARS=0/, '§3.6 应写明 Swing 菜单栏没有暴露成 UIA 元素');
assert.match(envSection, /InvokePattern/, '§3.6 应写明不存在用 UIAutomation 激活条目的通道');

console.log('PASS: 覆盖表生成器、PyCharm 分档摘要、§1.1/§1.4 像素值及 §3.6 环境断言一致。');
