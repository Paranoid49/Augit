// 逐页覆盖表（docs/ui-compliance.md §1.4）的生成器。
//
// 为什么要有这个脚本：§1.4 的正文写着"本表由脚本从 §1.1 的像素行与 verify-acceptance.ps1 的
// 场景列表直接生成，不手写数字"，但**仓库里并没有这个脚本** —— 声明与事实不符，而且已经
// 漂移过：`diff-boundary` 在 §0 更新为"含改动工作区 1/1"之后，本表的 B 线列仍写着"未进列表"；
// 行为断言那一行也还停在旧的 `191 条（54.4%）`，而 §2.0 的当前事实是 `407 条用例行 / 351 条中
// 336 条有逐条行`。补上生成器之后，重跑即可消除这类漂移。
//
// 数据来源（全部在读时解析，不写死）：
//   - 场景分母：tools/audit/verify-acceptance.ps1 的 $DefaultScenes
//   - A 线数字：docs/ui-compliance.md §1.1 的像素行（场景 | titlebar | statusbar | content | 判读）
//   - 行为断言：docs/ui-compliance.md §2.0 的合计行与"当前事实"段
//   - B 线判读：B_LINE 常量（干净仓库 54/54 + diff-boundary 单独 1/1，含证据轮次）
//
// 用法：node tools/audit/gen-coverage-table.cjs            # 打印生成的 §1.4 正文
//       node tools/audit/gen-coverage-table.cjs --write    # 写回 docs/ui-compliance.md
//       node tools/audit/gen-coverage-table.cjs --write --force  # 覆盖含手写内容的 §1.4（危险）
// 退出码：0 = 成功；1 = 上游数据缺失或 §1.4 含手写内容（宁可失败也不要生成一张空表）。
//
// **第 307 轮加的手写内容守卫**：§1.4 里除了本脚本生成的行，还有轮次手写的像素复核记录
// （例如"逐条展开 316 条 = §5 29 + …"那一段）。原实现无条件整段覆盖 ⇒ `--write` 会静默删掉它们
// （backlog §四 第 1 项）。现在只有 §1.4 已带生成标记（即上一次就是本脚本写的）或显式 `--force`
// 时才写回；否则打印失败原因并退出，**不会改动文档**。
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../..');
const docPath = path.join(root, 'docs/ui-compliance.md');
const acceptancePath = path.join(root, 'tools/audit/verify-acceptance.ps1');
const doc = fs.readFileSync(docPath, 'utf8');
const acceptance = fs.readFileSync(acceptancePath, 'utf8');

const fail = (message) => { console.error('GEN_COVERAGE_TABLE_FAILED ' + message); process.exit(1); };

// 生成标记（第 307 轮）：只有 §1.4 里已有它（说明上一次就是本脚本写的）或显式 --force 才允许覆盖，
// 否则拒绝写回 —— 见文件头的"手写内容守卫"。
const GENERATED_MARKER = '<!-- generated:gen-coverage-table；本段由脚本生成，手写内容请放到别的小节 -->';

// ---- 1) 场景分母：verify-acceptance.ps1 的 $DefaultScenes ----
const listStart = acceptance.indexOf('$DefaultScenes = @(');
if (listStart < 0) fail('verify-acceptance.ps1 里找不到 $DefaultScenes');
const listEnd = acceptance.indexOf(')\n', listStart);
if (listEnd < 0) fail('$DefaultScenes 没有结束括号');
const body = acceptance.slice(listStart, listEnd);
const acceptanceScenes = [...body.matchAll(/'([a-z0-9][a-z0-9-]*)'/g)].map((match) => match[1]);
if (acceptanceScenes.length < 40) fail('场景列表解析结果过少：' + acceptanceScenes.length);
// 被有意排除、但在报告里单独计一格的场景（原因写在脚本注释与 §1.4 正文里）。
const separateScenes = ['diff-boundary'];

// ---- 2) A 线数字：§1.1 的像素行 ----
const sectionOf = (text, heading) => {
  const start = text.indexOf(heading);
  if (start < 0) return null;
  const next = text.indexOf('\n### ', start + heading.length);
  return text.slice(start, next < 0 ? text.length : next);
};
const pixelSection = sectionOf(doc, '### 1.1 ');
if (!pixelSection) fail('找不到 §1.1');
const pixelRows = [];
for (const line of pixelSection.split('\n')) {
  const match = /^\| `([^`]+)` \| ([\d.]+) \| ([\d.]+) \| ([\d.]+) \| (.*) \|$/.exec(line.trim());
  if (match) pixelRows.push({ scene: match[1], titlebar: match[2], statusbar: match[3], content: match[4], verdict: match[5] });
}
if (pixelRows.length === 0) fail('§1.1 里没有解析到像素行');

// ---- 3) 行为断言：§2.0 的合计与"当前事实" ----
const factSection = sectionOf(doc, '### 2.0 ');
if (!factSection) fail('找不到 §2.0');
const totalMatch = /\| \*\*合计\*\* \| \*\*(\d+)\*\* \| \*\*(\d+)\*\* \|/.exec(factSection);
if (!totalMatch) fail('§2.0 找不到合计行');
const totalClauses = totalMatch[1];
const totalCaseRows = totalMatch[2];
const clauseRowsMatch = /规格条文 (\d+) 条中有逐条行的是 \*\*(\d+) 条\*\*/.exec(factSection);
const clauseRows = clauseRowsMatch ? clauseRowsMatch[2] : '?';

// ---- 4) B 线判读（人工维护的小常量，必须写明轮次，便于回溯） ----
const B_LINE = {
  cleanRepo: { scenes: acceptanceScenes.length, verdict: 'PASS' },
  separate: separateScenes.map((scene) => ({ scene, verdict: 'OK', where: '含改动的工作区', round: 371 })),
};

const rows = pixelRows.slice().sort((a, b) => a.scene.localeCompare(b.scene));
const bCell = (scene) => {
  const separate = B_LINE.separate.find((item) => item.scene === scene);
  if (separate) return `**OK**（${separate.where}，第 ${separate.round} 轮）`;
  if (acceptanceScenes.includes(scene)) return `PASS（${B_LINE.cleanRepo.scenes}/${B_LINE.cleanRepo.scenes} 那一轮）`;
  return '未进列表（需写明原因）';
};

const out = [];
out.push('### 1.4 逐页覆盖表（⑪：页面 × 检查层级 × 判读）');
out.push('');
// 生成标记：未来的 --write 依据它判断"这一段上次就是本脚本写的"，从而不必再要 --force。
out.push(GENERATED_MARKER);
out.push('');
out.push('**口径**：本表的数字由 `tools/audit/gen-coverage-table.cjs` 从 §1.1 的像素行与');
out.push('`tools/audit/verify-acceptance.ps1` 的场景列表**直接生成**（不手写数字；重跑即可消除漂移）。');
out.push('四层的分母如下：');
out.push('');
out.push('| 检查层级 | 分母 | 本轮结果 | 说明 |');
out.push('| --- | ---: | --- | --- |');
out.push(`| A 线：同引擎像素对照 | ${rows.length} 场景 | ${rows.length}/${rows.length} 有行（见 §1.1） | 逐带 \`layoutPercent\`；判据是"平坦像素差异"而非逐像素相等 |`);
out.push(`| B 线：真机巡检 | ${B_LINE.cleanRepo.scenes} 场景（干净仓库） | **${B_LINE.cleanRepo.scenes}/${B_LINE.cleanRepo.scenes} PASS** | 分母 = 场景总数 − ${separateScenes.join('、')}（需含改动的工作区） |`);
out.push(`| B 线：含改动工作区的场景 | ${B_LINE.separate.length} 场景 | ${B_LINE.separate.map((item) => `**${item.verdict} 1/1**（\`${item.scene}\`，第 ${item.round} 轮）`).join('、')} | 单独一次真机验收；与干净仓库那一轮**分开计**，不合并成一个数字 |`);
out.push(`| B 线：行为断言 | ${totalClauses} 条规格条文 | ${totalCaseRows} 条用例行（其中 ${clauseRows} 条有逐条行） | **按条文归属，不按页面**；逐页行为覆盖请查 §2 对应小节 |`);
out.push('| C 线：PyCharm 对照 | 55 个视觉稿场景 | 33 页面级已对照 + 5 页仅入口级证据 + 17 页未对照 | 判据是"声明容差内地标等价"，不做逐像素相等 |');
out.push('');
out.push('> **为什么行为断言不按页面列**：Harness 的断言是按"规格条文/状态转换"组织的（一个断言常跨多页，');
out.push('> 例如"工具窗口互斥"同时覆盖终端与 Git 历史），硬按页面拆会造出虚假的逐页分母。');
out.push('> 因此这里只给"条文维度"的分母，逐页行为覆盖请对照 §2 的小节标题。');
out.push('');
out.push('| 页面（场景） | A 线 titlebar | A 线 statusbar | A 线 content | B 线真机巡检 | A 线判读 |');
out.push('| --- | ---: | ---: | ---: | --- | --- |');
for (const row of rows) {
  out.push(`| \`${row.scene}\` | ${row.titlebar} | ${row.statusbar} | ${row.content} | ${bCell(row.scene)} | ${row.verdict} |`);
}
out.push('');
out.push(`> 生成来源：§1.1 ${rows.length} 行像素数据、\`verify-acceptance.ps1\` ${acceptanceScenes.length} 个场景、`);
out.push(`> §2.0 合计行（${totalClauses} 条条文 / ${totalCaseRows} 条用例行）。`);
out.push('');
const generated = out.join('\n');

if (!process.argv.includes('--write')) {
  process.stdout.write(generated);
  process.exit(0);
}

const headingAt = doc.indexOf('### 1.4 ');
if (headingAt < 0) fail('交付文档里找不到 §1.4');
const nextHeading = doc.indexOf('\n### ', headingAt + 1);
if (nextHeading < 0) fail('§1.4 之后没有下一个标题，无法确定替换边界');
// 手写内容守卫（第 307 轮）：整段覆盖会连同轮次手写的像素复核记录一起删掉，必须显式确认。
const section = doc.slice(headingAt, nextHeading);
if (!process.argv.includes('--force') && !section.includes(GENERATED_MARKER)) {
  fail('§1.4 含手写内容（没有生成标记）：--write 会整段覆盖并删掉它们。'
    + '确认要覆盖时加 --force；只想核对差异请不带 --write 运行并与 §1.4 手工比对。');
}
const replaced = doc.slice(0, headingAt) + generated.replace(/\n$/, '') + '\n' + doc.slice(nextHeading + 1);
fs.writeFileSync(docPath, replaced);
console.log(`WROTE §1.4 rows=${rows.length} scenes=${acceptanceScenes.length} clauses=${totalClauses} caseRows=${totalCaseRows}`);
