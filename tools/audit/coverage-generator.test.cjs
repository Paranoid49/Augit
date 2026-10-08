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

// ---- §3.7：17 页的"分档 → 五类最终归类"必须逐页闭合，且不得把未取证写成已取证 ----
// 这段守卫的口径全部来自文档自己：
//   a) 五类归类的名字取自 `ui-classification.md` §0.1 的表（不在这里另抄一份字面量）；
//   b) §3.7 的最终归类表必须覆盖 §1.6 里"未对照"的那 17 页，逐页一一对应、不重不漏；
//   c) 每一行都必须写明"未完成真实前台取证"（含原因），且不得出现"已完成/已取证/已对照"式的伪结论——
//      否则"按本地权威源码与自动化证据归类"会被读成"PyCharm 面级取证已完成"；
//   d) 分档小计的 9/2/5/1 必须与表内逐行一致（§1.6 的机械核对已在上面）。
const classificationDoc = fs.readFileSync(path.join(root, 'docs/ui-classification.md'), 'utf8');
const classificationSection = sectionOf(classificationDoc, '### 0.1 ');
assert.ok(classificationSection, '应能找到 `ui-classification.md` §0.1 五个归类');
const categories = [...classificationSection.matchAll(/\| \*\*([^|*]+)\*\* \|/g)]
  .map((match) => match[1].trim());
assert.equal(categories.length, 5,
  '§0.1 应恰好定义五个归类（已按 New UI 对齐／有意产品差异／不适用／无法取证／待处理）');
assert.ok(categories.includes('已按 New UI 对齐') && categories.includes('待处理'),
  `§0.1 的归类名集合异常：${categories.join('、')}`);

const section37 = sectionOf(doc, '### 3.7 ');
assert.ok(section37, '应能找到 §3.7 的 17 页分档与最终归类');
const uncoveredScenes = sectionOf(doc, '### 1.6 ').split('\n')
  .map((line) => (/^\| `([^`]+)` \| 未对照（([A-Z_]+)） \|/.exec(line.trim()) || null))
  .filter(Boolean)
  .map((match) => ({ scene: match[1], bucket: match[2] }));
assert.equal(uncoveredScenes.length, 17, '§1.6 的"未对照"页应为 17 页');

// 最终归类表：5 列——分档 / 页面 / 真实前台取证 / 最终归类 / 结论依据。
// §3.7 里另有一张 4 列的"分档小计"表（`| 分档 | 页数 | 页面 | 含义 |`），靠列数区分并把两者都钉住。
const rows37 = section37.split('\n')
  .map((line) => line.trim())
  .filter((line) => /^\| `[A-Z_]+` \|/.test(line))
  .map((line) => line.split('|').map((cell) => cell.trim())
    .filter((cell, index, cells) => index > 0 && index < cells.length - 1));
const bucketRows = rows37.filter((cells) => cells.length === 4);
const finalRows = rows37.filter((cells) => cells.length === 5);
assert.equal(bucketRows.length, 4, '§3.7 的分档小计表应有 4 行');
assert.deepEqual(bucketRows.map((cells) => Number(cells[1].replace(/[^0-9]/g, ''))), [9, 2, 5, 1],
  '§3.7 分档小计表的页数应为 9／2／5／1');
assert.equal(finalRows.length, 6,
  '§3.7 的最终归类表应把 17 页分成 6 行（MEDIUM_BLOCKED 8 页 + MEDIUM_BLOCKED 1 页 + RECOVERABLE 2 行 + PRECONDITION 1 行 + AUGIT_ONLY 1 行）');
const listedScenes = [];
for (const cells of finalRows) {
  assert.equal(cells.length, 5, `§3.7 最终归类行的列数应为 5：${cells.join(' / ')}`);
  const [bucket, pages, forensics, categoryRaw, reason] = cells;
  const category = categoryRaw.replace(/[*`]/g, '').trim();
  assert.ok(categories.includes(category),
    `§3.7 的最终归类"${category}"不在 §0.1 的五类内（不得把 MEDIUM_BLOCKED 等分档标签当归类）`);
  assert.match(forensics, /未完成真实前台取证/,
    `§3.7 的 "${pages}" 行必须写明"未完成真实前台取证"`);
  assert.ok(!/已完成真实前台取证|已取证|已对照/.test(forensics),
    `§3.7 的 "${pages}" 行不得把未取证写成已取证：${forensics}`);
  assert.ok(reason.length > 20, `§3.7 的 "${pages}" 行缺少可核对的结论依据`);
  for (const page of pages.split('、').map((name) => name.replace(/`/g, '').trim()).filter(Boolean)) {
    listedScenes.push({ scene: page, bucket: bucket.replace(/[`*]/g, '').trim() });
  }
}
assert.equal(listedScenes.length, 17, '§3.7 的最终归类表应覆盖 17 页');
const byBucketThenScene = (a, b) => (a.bucket === b.bucket ? a.scene.localeCompare(b.scene) : a.bucket.localeCompare(b.bucket));
assert.deepEqual([...listedScenes].sort(byBucketThenScene), [...uncoveredScenes].sort(byBucketThenScene),
  '§3.7 的页名与分档必须与 §1.6 的 17 个"未对照"页一一对应');
for (const claim of ['MEDIUM_BLOCKED', 'RECOVERABLE', 'PRECONDITION', 'AUGIT_ONLY',
  '未完成真实前台取证', '不来自 PyCharm 面级取证']) {
  assert.ok(section37.includes(claim), `§3.7 应声明"${claim}"`);
}

// ---- 8) 收口口径守卫：交付文档的小节标题不得再写成"当前未完成/待核/未决" ----
// 背景（第 313 轮外审收口）：第 311／312 轮已把归类与数字收口，但只要标题或口径句重新写成
// "尚未完成／待实施／未决／未执行"，读者就会把它读成"目标未完成"。这里只钉住**小节标题**
// 这一处最容易被读成当前状态的表述（历史正文里的"当时／原／已结案"式记录仍允许保留）。
const headingLine = (text, heading) => (text.split('\n').find((line) => line.startsWith(heading)) || '');
const titleGuards = [
  ['docs/performance-report.md', '## 8. ', /未纳入验收的范围与最终归类/, /尚未完成/, '§8 已收口为"最终归类"口径'],
  ['docs/ui-classification.md', '## 7. ', /已全部关闭/, /必须清零/, '§7 已写明待处理清单全部关闭'],
  ['docs/intellij-platform-ui-behavior.md', '## 4. ', /每一项都已归类/, /待实施/, '§4 已写明逐项归类、不是待办队列'],
  ['docs/intellij-platform-ui-behavior.md', '### 9.4 ', /最终归类/, /未实施/, '§9.4 已写明剩余项最终归类'],
  ['docs/intellij-platform-ui-behavior.md', '### 11.5 ', /结论与边界/, /未决/, '§11.5 已写明结论与边界'],
  ['docs/ui-compliance.md', '### 3.1 ', /未执行的验证与真实边界/, /尚未执行/, '§3.1 已写明"未执行"是真实边界'],
  ['docs/ui-compliance.md', '### 0.1 ', /历史快照/, /未闭环/, '§0.1 已标明历史快照'],
  ['docs/nui-behavior/11-surface-audit.md', '## 3. ', /已全部收口/, /未覆盖区/, '§3 已标明历史审计产出、全部收口'],
];
for (const [file, heading, expected, forbidden, message] of titleGuards) {
  const text = fs.readFileSync(path.join(root, file), 'utf8');
  const line = headingLine(text, heading);
  assert.ok(line, `${file} 应能找到小节标题 ${heading}`);
  assert.match(line, expected, `${file} ${message}：${line}`);
  assert.ok(!forbidden.test(line), `${file} ${message}（标题里仍有 ${forbidden}）：${line}`);
}

console.log('PASS: 覆盖表生成器、PyCharm 分档摘要、§1.1/§1.4 像素值、§3.6 环境断言、§3.7 五类归类与收口口径标题一致。');
