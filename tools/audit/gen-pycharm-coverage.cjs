// ⑦⑧ 逐页对照的"有分母"表：对 §1.1 的每个场景给出 PyCharm 侧对照状态。
// 为什么按"面"而不是硬凑页面：PyCharm 并没有 Augit 的 diff/冲突/stash/远端等页面形态，
// 强行"逐页对照"会造出不存在的一一对应。本表因此如实分三类，并指明"已对照"的那些页
// 用的是 §1.3 里哪些实测面（chrome/编辑器标签/状态栏/设置对话框）。
// 用法：node tools/audit/gen-pycharm-coverage.cjs [--write]
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const docPath = path.join(root, 'docs/ui-compliance.md');
const doc = fs.readFileSync(docPath, 'utf8');

const start = doc.indexOf('### 1.1 ');
const end = doc.indexOf('\n### ', start + 1);
if (start < 0 || end < 0) { console.error('GEN_PYCHARM_COVERAGE_FAILED no 1.1'); process.exit(1); }
const scenes = [];
for (const line of doc.slice(start, end).split('\n')) {
  const m = /^\| `([^`]+)` \| ([\d.]+) \| ([\d.]+) \| ([\d.]+) \|/.exec(line.trim());
  if (m) scenes.push(m[1]);
}
if (scenes.length < 40) { console.error('GEN_PYCHARM_COVERAGE_FAILED too few scenes ' + scenes.length); process.exit(1); }

// 已实测的 PyCharm 面（见 §1.3 的逐面表）：
//  A = 主窗口 chrome（菜单栏/工具栏/自绘标题栏、状态栏上边缘与高度、状态栏底色）
//  B = 编辑器标签行与树行高
//  C = 设置对话框（左栏/搜索框/主按钮/树行高/选中行底色/禁用态/三页字体正文）
const CHROME_EDITOR = ['main-project', 'text-viewer', 'markdown-preview', 'json-preview', 'image-preview', 'file-limit', 'go-to-line'];
const SETTINGS = ['settings', 'settings-dirty', 'settings-save-failure'];
//  D = Git Log 工具窗（已采集：tabs Git|Log|Console、分支/文本过滤、提交列表含图标记号、Commit details 面板）
//  E = Commit 工具窗（已采集：底窗高 172 CSS、文件列表 + 提交信息区）
const GIT_LOG = ['git-history', 'git-history-graph', 'git-history-empty', 'git-history-menu'];
const COMMIT_TW = ['commit-changes', 'commit-empty'];
//  F = Terminal 工具窗（已采集：底部 tabs Terminal|Local + 真实 PowerShell 会话）
const TERMINAL_TW = ['terminal', 'terminal-close'];
//  G = Diff 视图（已采集两态：'1 difference' 与 'Contents are identical'；见 §1.3）
const DIFF_VIEW = ['commit-diff', 'diff-loading', 'diff-status', 'diff-boundary', 'git-compare',
                   'git-compare-empty', 'history-diff-loading', 'history-diff-failure', 'history-diff-cancelled'];
//  H = Search Everywhere 浮层（两态：空查询 + 有结果；tabs All/Classes/Files/Symbols/Actions/Text）
const SEARCH_EVERYWHERE = ['quick-open', 'quick-open-empty', 'search-limited'];
//  I = 文件/编辑器右键菜单（Refactor This…/Rename…/Move File…/Copy File…/Safe Delete…）
const CONTEXT_MENU = ['project-context-menu'];
//  **入口级证据**（第 70 轮新增，单独计数、不计入「面级已对照」）：PyCharm 只采到
//  「操作入口/命名/快捷键」或「确认框模式」，对话框本体仍未采集。
const ENTRY_LEVEL = new Map();
for (const s of ['branches', 'smart-checkout', 'push', 'push-no-remote', 'rollback', 'stash', 'worktrees']) {
  ENTRY_LEVEL.set(s, 'J 入口级（VCS Operations 弹出菜单：命名 + 快捷键）');
}
for (const s of ['terminal-close']) {
  ENTRY_LEVEL.set(s, 'K 确认框级（Confirm Exit：danger 默认 + 次按钮 + 不再询问）');
}
const covered = new Map();
for (const s of CHROME_EDITOR) covered.set(s, 'A+B（主窗口 chrome / 编辑器标签行）');
for (const s of SETTINGS) covered.set(s, 'C（设置对话框）');
for (const s of GIT_LOG) covered.set(s, 'D（Git Log 工具窗）');
for (const s of COMMIT_TW) covered.set(s, 'E（Commit 工具窗）');
for (const s of TERMINAL_TW) covered.set(s, 'F（Terminal 工具窗）');
for (const s of DIFF_VIEW) covered.set(s, 'G（Diff 视图）');
for (const s of SEARCH_EVERYWHERE) covered.set(s, 'H（Search Everywhere）');
for (const s of CONTEXT_MENU) covered.set(s, 'I（文件/编辑器右键菜单）');

const reason = (scene) => {
  if (/^(commit-diff|diff-|history-diff|git-compare)/.test(scene)) return 'PyCharm Diff 视图未采集';
  if (/^(git-history|file-history|blame|branches|smart-checkout|reset|rollback|push|clone|repository-init)/.test(scene)) return 'PyCharm Git 工具窗/Dialog 未采集';
  if (/^stash/.test(scene)) return 'PyCharm Shelf/Stash 面不同且未采集';
  if (/^(worktrees|remote)/.test(scene)) return 'PyCharm 对应面（Git 分支/远端）形态不同且未采集';
  if (/^terminal/.test(scene)) return 'PyCharm 终端工具窗未采集（Alt+F12 未打开，见日志）';
  if (/^(conflict|operation-|project-context-menu|changes-context-menu|git-history-menu|quick-open|search-|commit-empty|repository-search|workspace-open|worktrees)/.test(scene)) return 'PyCharm 同类弹层/状态未采集';
  return '未采集';
};

const out = [];
out.push('### 1.6 逐页 × PyCharm 对照状态（⑦⑧，55 行有分母；生成器 `tools/audit/gen-pycharm-coverage.cjs`）');
out.push('');
out.push('**口径**：PyCharm 没有 Augit 的 diff/冲突/stash/远端等页面形态，**按面**对照（§1.3 的逐面表）才是可核对的；');
out.push('本表因此给出**每个 Augit 页面**的 PyCharm 侧状态，而不是硬凑一一对应。');
out.push('`A` = 主窗口 chrome、`B` = 编辑器标签行/树行高、`C` = 设置对话框、`D` = Git Log 工具窗、`E` = Commit 工具窗、`F` = Terminal 工具窗、`G` = Diff 视图、`H` = Search Everywhere、`I` = 文件/编辑器右键菜单（九面均已在 §1.3 有实测值/证据图）。');
out.push('');
out.push('| 场景（§1.1 的 55 行） | PyCharm 侧 | 说明 |');
out.push('| --- | --- | --- |');
let coveredCount = 0;
let entryCount = 0;
for (const scene of scenes) {
  const hit = covered.get(scene);
  if (hit) { coveredCount++; out.push(`| \`${scene}\` | **面级已对照** | ${hit}——见 §1.3 逐面表的实测值与判读 |`); }
  else if (ENTRY_LEVEL.has(scene)) { entryCount++; out.push(`| \`${scene}\` | 仅入口级证据 | ${ENTRY_LEVEL.get(scene)}；**对话框本体未采集，不计入面级已对照** |`); }
  else { out.push(`| \`${scene}\` | 未对照 | ${reason(scene)} |`); }
}
out.push('');
out.push(`> 本轮读数：**${coveredCount}/${scenes.length} 个页面**落在已实测的面上；`
  + `另有 **${entryCount} 页**只有**入口级/确认框级**证据（不计入面级已对照）；`
  + `其余 ${scenes.length - coveredCount - entryCount} 个页面`);
out.push('> 的 PyCharm 同类面**尚未采集**（原因逐行写明）。**不把"未采集"写成"已通过"，也不把它算进对照完成率。**');
out.push('');
const generated = out.join('\n');

if (!process.argv.includes('--write')) { process.stdout.write(generated); process.exit(0); }
const at = doc.indexOf('### 1.6 ');
if (at >= 0) {
  const next = doc.indexOf('\n### ', at + 1);
  fs.writeFileSync(docPath, doc.slice(0, at) + generated.replace(/\n$/, '') + '\n' + (next < 0 ? '' : doc.slice(next + 1)));
} else {
  const anchor = '### 2.0 ';
  const pos = doc.indexOf(anchor);
  if (pos < 0) { console.error('GEN_PYCHARM_COVERAGE_FAILED no anchor'); process.exit(1); }
  fs.writeFileSync(docPath, doc.slice(0, pos) + generated + doc.slice(pos));
}
console.log(`WROTE 1.6 scenes=${scenes.length} covered=${coveredCount} uncovered=${scenes.length - coveredCount}`);
