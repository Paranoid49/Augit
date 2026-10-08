// 交付文档内部"数字自洽"的检查器。
//
// 背景：docs/ui-compliance.md 里有一批**由表格与正文互相引用**的数字（条文分母、用例行、
// 逐条行、像素行、§10 的"15 条全部有断言"）。它们以前靠手写维护，已经漂移过两次
// （§1.4 的行为断言行停在旧的 191/54.4%；`diff-boundary` 的 B 线列停在"未进列表"）。
// §1.4 现在由 tools/audit/gen-coverage-table.cjs 生成；本检查器管的是**其余几处**：
//
//   1. §2.0 表格：各节条文数之和 = 合计；各节用例行之和 = 合计；
//   2. §2.0 "当前事实"：括号里的逐条行加数之和 = 正文写的"有逐条行"条数，
//      且每个加数 ≤ 该节条文数，缺口之和 = 条文总数 − 有逐条行条数；
//   3. §2.8 表：行数与 §2.0 里 §10 的条文数一致，且每行状态列都在允许集合内；
//      若 §2.0 声称"15 条全部有断言"，则 §2.8 不允许出现 部分/未覆盖；
//   4. §1.1 的像素行数 = §1.4 里写的 A 线分母与分子；
//   5. §2.3 的"已验证快照"必须仍与工作区相符（受登记文件 md5 全一致）时，才把它的断言数当登记值：
//      §0／§0.1 的"当前结果"行必须等于它、任何声明不得更大；另核对 §0 说明里的 `check()` 调用点数、
//      `csCheck` 条数与"实际断言数"，以及 §0.1 的 PowerShell 脚本个数。
//
// 它**不**验证必须真跑套件/真机才能得到的数字（真机全场景巡检 PASS 数、打包体积）——
// 那些由各自的套件产出、写进 §0 时都带日期；本检查器只打印它们，明确标注"未机械核对"，
// 不假装核对过。`live-shell` 断言数则是例外：它挂在 §2.3 的已验证快照上，一旦
// 受登记文件漂移就只打印"快照过期"而不据此报错，因此不需要为本检查器重跑套件。
//
// 用法：node tools/audit/check-doc-claims.cjs
// 退出码：0 = 全部自洽；1 = 有数字互相矛盾。
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');

const root = path.resolve(__dirname, '../..');
const docPath = path.join(root, 'docs/ui-compliance.md');
const doc = fs.readFileSync(docPath, 'utf8');

const problems = [];
const notes = [];
// 按**行首标题**取小节：不能用 `doc.indexOf('### 2.3 ')` —— 第 47 行的目录/清单里也写着 `### 2.3 `，
// 那会把"小节"截成前 1 KB 的表单行，于是 §2.3 的断言数与哈希一个都读不到（实测：七个受登记文件
// 全被报成"漂移"、断言数解析不到）。这里锚定 `^### <标题>` 并允许标题后带说明文字。
const sectionOf = (heading) => {
  const escaped = heading.trim().replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const match = new RegExp('^' + escaped + '.*$', 'm').exec(doc);
  if (!match) return null;
  const start = match.index;
  const next = doc.indexOf('\n### ', start + match[0].length);
  return doc.slice(start, next < 0 ? doc.length : next);
};

// ---- 1) §2.0 表格：分节之和 = 合计 ----
const facts = sectionOf('### 2.0 ');
if (!facts) {
  console.error('DOC_CLAIMS_FAILED 找不到 §2.0');
  process.exit(1);
}
const sectionRows = [];
for (const line of facts.split('\n')) {
  const match = /^\| (§\d+[^|]*) \| (\d+) \| \*{0,2}(\d+)/.exec(line.trim());
  if (match) sectionRows.push({ section: match[1].trim(), clauses: Number(match[2]), rows: Number(match[3]) });
}
const totalRow = /\| \*\*合计\*\* \| \*\*(\d+)\*\* \| \*\*(\d+)\*\* \|/.exec(facts);
if (!totalRow) problems.push('§2.0 找不到合计行');
if (sectionRows.length === 0) problems.push('§2.0 没有解析到分节行');
if (totalRow && sectionRows.length > 0) {
  const clauseSum = sectionRows.reduce((sum, row) => sum + row.clauses, 0);
  const rowSum = sectionRows.reduce((sum, row) => sum + row.rows, 0);
  if (clauseSum !== Number(totalRow[1])) {
    problems.push(`§2.0 分节条文之和 ${clauseSum} ≠ 合计 ${totalRow[1]}`);
  }
  if (rowSum !== Number(totalRow[2])) {
    problems.push(`§2.0 分节用例行之和 ${rowSum} ≠ 合计 ${totalRow[2]}`);
  }
  notes.push(`§2.0 表格：分节 ${sectionRows.length} 节，条文之和 ${clauseSum}，用例行之和 ${rowSum}`);
}

// ---- 1b) 用例行数：逐块计数后与 §2.0 表格各行相加核对 ----
// §2.7 里有**三个**"逐条展开"区块（§6 40 / §5 29 / §9 22 = 91 条编号行）；
// §5/§6 的概览分别在 §2.1–§2.4 与 §2.5，§7 的概览与逐条都在 §2.6，§10 在 §2.8，§4 在 §1.2。
const countRows = (body, pattern) => body.split('\n')
  .filter((line) => pattern.test(line.trim())).length;
const twoSeven = sectionOf('### 2.7 ');
const twoSix = sectionOf('### 2.6 ');
const oneTwo = sectionOf('### 1.2 ');
const twoEight = sectionOf('### 2.8 ');
const five = sectionOf('### 2.1 ');
const six = sectionOf('### 2.5 ');
if (!twoSeven || !twoSix || !oneTwo || !twoEight || !five || !six) {
  problems.push('用例行数核对所需的某些小节找不到');
} else {
  const expand = {};
  for (const match of twoSeven.matchAll(/\*\*§(\d+) 逐条展开（[^）]*?(\d+) 条）\*\*/g)) {
    expand[match[1]] = Number(match[2]);
  }
  const twoOneToFour = ['### 2.1 ', '### 2.2 ', '### 2.3 ', '### 2.4 ']
    .map((heading) => sectionOf(heading) || '')
    .reduce((total, body) => total + countRows(body, /^\| §5\.\d+/), 0);
  const counted = {
    '§4': countRows(oneTwo, /^\| §4(\.\d+)? \|/) + countRows(oneTwo, /^\| §4 /),
    '§5': twoOneToFour + (expand['5'] || 0),
    '§6': countRows(six, /^\| §6\.\d+/) + (expand['6'] || 0),
    '§7': countRows(twoSix, /^\| §7\.\d+/) + countRows(twoSix, /^\| \d+ \| §7/),
    '§9': countRows(twoSeven, /^\| §9\.\d+/) + (expand['9'] || 0),
    '§10': countRows(twoEight, /^\| \d+ \| §10\./),
  };
  for (const row of sectionRows) {
    const key = '§' + (row.section.match(/§(\d+)/) || [])[1];
    if (!(key in counted)) continue;
    if (counted[key] !== row.rows) {
      problems.push(`§2.0 ${key} 的用例行 ${row.rows} ≠ 逐块计数 ${counted[key]}`);
    }
  }
  try {
    // 第 105 轮：把"有多少条规格有逐条行"变成**脚本可核**的数字。
    // §5/§6/§7/§9/§10 是逐条展开（条数=应有的逐条行数）；§4 属于"主题式核销"（§1.2 按维度成行，不是逐条一一对应），
    // 因此不能把 §4 的行数当成"有逐条行的条文数" —— 之前那个手写的 336 就是这么来的（既算不出也不准）。
    const specCounts = { '§4': 35, '§5': 29, '§6': 40, '§7': 210, '§9': 22, '§10': 15 };
    const perClause = ['§5', '§6', '§7', '§9', '§10'].reduce((sum, key) => sum + specCounts[key], 0);
    // 第 107 轮：规格里"给了死值"的视觉条款也做成机械核对 —— 尺寸令牌的**兜底值**必须等于规格（§4.2 尺寸与密度），
  // 主题底色必须满足"浅色白面板 / 深色非纯黑"（§4.4 颜色）。运行时这些令牌会被 mockup.js 按实测字高覆盖，
  // 但兜底值本身也是规格的一部分（改错会让首帧或异常路径不符合规格）。
  try {
    const css = fs.readFileSync(path.join(root, 'web', 'src', 'mockup.css'), 'utf8');
    const tokenOf = (name) => {
      const m = css.match(new RegExp('--augit-' + name + ':\\s*([^;]+);'));
      return m ? m[1].trim() : null;
    };
    const wanted = {
      'title-height': '44px',
      'tab-height': '40px',
      'tree-height': '24px',
      'status-height': '28px',
      'project-header-height': '39px',
      'document-toolbar-height': '36px',
      'side-width': 'clamp(300px, 22vw, 360px)',
      'bottom-height': 'clamp(180px, 31vh, 305px)',
    };
    let checked = 0;
    for (const [name, value] of Object.entries(wanted)) {
      const actual = tokenOf(name);
      if (actual === value) { checked += 1; continue; }
      problems.push('mockup.css 的 --augit-' + name + ' 兜底值 ' + actual + ' ≠ 规格 ' + value);
    }
    const panels = [...css.matchAll(/--augit-panel:\s*(#[0-9a-fA-F]{6})/g)].map((m) => m[1].toLowerCase());
    if (!panels.includes('#ffffff')) problems.push('浅色主题 --augit-panel 不是 #ffffff（规格 §4.4）');
    if (panels.includes('#000000')) problems.push('深色主题 --augit-panel 用了纯黑 #000000（规格 §4.4 明确禁止）');
    if (checked === Object.keys(wanted).length && !panels.includes('#000000') && panels.includes('#ffffff')) {
      notes.push('mockup.css 规格固定值核对通过：尺寸令牌 ' + checked + ' 项、主题面板 ' + panels.join(' / '));
    }
  } catch (error) { /* 读不到就跳过，不影响其它判定 */ }
  // 第 106 轮：`design-system.md:135/144` 只允许普通(400)与半粗(600)两个主要字重，
  // 而且 135 行的角色表把"按钮"明确归到普通档。第 105 轮发现实现里有 4 处 `font-weight: 500`，
  // 已按角色改掉；这里加一条机械守卫，防止第三档字重再悄悄回来。
  try {
    const css = fs.readFileSync(path.join(root, 'web', 'src', 'mockup.css'), 'utf8');
    const weights = new Set();
    for (const m of css.matchAll(/font-weight:\s*([0-9]+|normal|bold|bolder|lighter)/g)) weights.add(m[1]);
    const allowed = new Set(['400', '600', 'normal']);
    const bad = [...weights].filter((w) => !allowed.has(w));
    if (bad.length) problems.push('mockup.css 出现规格未允许的字重：' + bad.join(', '));
    else notes.push('mockup.css 字重集合 = ' + [...weights].sort().join('/') + '（规格只允许普通 400 与半粗 600）');
  } catch (error) { /* 读不到就跳过，不影响其它判定 */ }
  notes.push('逐条展开合计（§5+§6+§7+§9+§10 规格条数）= ' + perClause
      + '（§4 的 ' + specCounts['§4'] + ' 条在 §1.2 按维度主题式核销，不是逐条一一对应）');
  } catch (error) { /* NOTE 失败不影响判定 */ }
  notes.push('用例行逐块计数：' + Object.entries(counted).map(([key, value]) => `${key}=${value}`).join(' '));
}

// ---- 2) §2.0 "当前事实"里的加数与总数 ----
const factLine = /规格条文 (\d+) 条中有逐条行的是 \*\*(\d+) 条\*\*/.exec(facts);
// 只在**这一句后面的那个括号里**解析加数：用全文匹配会把正文里其它「§N 数字」也算进来
// （第 374 轮实测：新加的说明段里"§10 15"被误当成加数，导致假报警）。
const addendGroup = /有逐条行的是 \*\*\d+ 条\*\*[^（]*（([^）]*)）/.exec(facts);
const addends = addendGroup ? [...addendGroup[1].matchAll(/§(\d+) (\d+)/g)] : [];
if (!factLine) problems.push('§2.0 找不到"当前事实"里的条文/逐条行句子');
if (addends.length > 0 && factLine) {
  const sum = addends.reduce((total, match) => total + Number(match[2]), 0);
  if (sum !== Number(factLine[2])) {
    problems.push(`§2.0 逐条行加数之和 ${sum} ≠ 正文写的 ${factLine[2]}`);
  }
  for (const match of addends) {
    const section = sectionRows.find((row) => row.section.startsWith('§' + match[1]));
    if (!section) {
      problems.push(`§2.0 加数里的 §${match[1]} 在表格里找不到`);
      continue;
    }
    if (Number(match[2]) > section.clauses) {
      problems.push(`§2.0 §${match[1]} 的逐条行 ${match[2]} > 该节条文数 ${section.clauses}`);
    }
  }
  const clauses = Number(factLine[1]);
  const withRows = Number(factLine[2]);
  const sumClauses = sectionRows.reduce((total, row) => total + row.clauses, 0);
  const gap = addends
    .map((match) => {
      const section = sectionRows.find((row) => row.section.startsWith('§' + match[1]));
      return section ? section.clauses - Number(match[2]) : 0;
    })
    .reduce((total, value) => total + value, 0);
  if (clauses !== sumClauses) problems.push(`§2.0 正文条文总数 ${clauses} ≠ 表格条文之和 ${sumClauses}`);
  if (gap !== clauses - withRows) {
    problems.push(`§2.0 缺口之和 ${gap} ≠ 条文总数 − 有逐条行 (${clauses} − ${withRows})`);
  }
  notes.push(`§2.0 当前事实：${clauses} 条中 ${withRows} 条有逐条行，缺口 ${gap} 条`);
}

// ---- 3) §2.8 表：行数、状态集合、"15 条全部有断言" ----
const ten = sectionOf('### 2.8');
if (!ten) {
  problems.push('找不到 §2.8');
} else {
  const rows = ten.split('\n').filter((line) => /^\| \d+ \| §10\./.test(line.trim()));
  const statuses = rows.map((line) => {
    const cells = line.split('|').map((cell) => cell.trim());
    return cells[4];
  });
  const allowed = new Set(['是', '部分', '未覆盖']);
  const unknown = statuses.filter((status) => !allowed.has(status));
  if (unknown.length > 0) problems.push(`§2.8 有不在允许集合里的状态列：${[...new Set(unknown)].join('、')}`);
  const tenSection = sectionRows.find((row) => row.section.startsWith('§10'));
  if (tenSection && tenSection.clauses !== rows.length) {
    problems.push(`§2.8 行数 ${rows.length} ≠ §2.0 里 §10 的条文数 ${tenSection.clauses}`);
  }
  const claimAll = /§10[^|]*\*\*15 条全部有断言\*\*/.test(facts) || /\*\*15 条全部有断言\*\*/.test(facts);
  const partial = statuses.filter((status) => status !== '是').length;
  if (claimAll && partial > 0) {
    problems.push(`§2.0 声称"15 条全部有断言"，但 §2.8 有 ${partial} 行不是"是"`);
  }
  notes.push(`§2.8：${rows.length} 行，是 ${statuses.filter((s) => s === '是').length} / 部分 ${statuses.filter((s) => s === '部分').length} / 未覆盖 ${statuses.filter((s) => s === '未覆盖').length}`);
}

// ---- 4) §1.1 像素行数 = §1.4 的 A 线分母/分子 ----
const pixels = sectionOf('### 1.1 ');
const coverage = sectionOf('### 1.4 ');
if (!pixels || !coverage) {
  problems.push('找不到 §1.1 或 §1.4');
} else {
  const pixelRows = pixels.split('\n').filter((line) => /^\| `[^`]+` \| [\d.]+ \| [\d.]+ \| [\d.]+ \|/.test(line.trim())).length;
  const aLine = /\| A 线：同引擎像素对照 \| (\d+) 场景 \| (\d+)\/(\d+) 有行/.exec(coverage);
  if (!aLine) {
    problems.push('§1.4 找不到 A 线那一行');
  } else {
    if (Number(aLine[1]) !== pixelRows) problems.push(`§1.4 A 线分母 ${aLine[1]} ≠ §1.1 像素行数 ${pixelRows}`);
    if (Number(aLine[3]) !== pixelRows) problems.push(`§1.4 A 线分母(后一个数) ${aLine[3]} ≠ §1.1 像素行数 ${pixelRows}`);
    if (aLine[2] !== aLine[3]) problems.push(`§1.4 A 线分子 ${aLine[2]} ≠ 分母 ${aLine[3]}`);
  }
  notes.push(`§1.1 像素行 ${pixelRows} 行`);
}

// ---- 5) 套件数字：与"已验证快照"逐字挂钩后再核对，不再是纯手写 ----
// 背景（第 311 轮收尾）：`live-shell` 的断言数与脚本数只出现在正文里、没有任何守卫，
// 于是 §0 的"实时外壳"行停在 1070/1070、§0.1 停在 1014/1014，而同一份 §2.3 已经登记
// `live-shell 通过 1459 项断言`（同样 2026-10-01）——三个数字互相矛盾，读者无法分辨哪个是当前值。
//
// 做法：把 §2.3 的"已验证快照"当作**可挂钩的登记值**——它给出断言数与每个受登记文件的 md5。
//   a) 本机重算这些 md5；全部一致 ⇒ 那一次全量跑过的结论（含断言数）仍然有效，不需要跑套件也能核对；
//   b) 任何不一致 ⇒ 只打印"快照过期"，不据此报错（只改文档时按 §2.3 的规程本来就不重跑套件）；
//   c) §0／§0.1 的"当前结果"行必须等于登记值（历史沿革段里的旧值按轮次递增、必然更小，
//      不当矛盾处理），且任何 `live-shell 通过 N 项断言` 的声明都**不得大于**登记值
//      （写大的值只能来自没跑过的推导，属于假通过）；
//   c2) 登记值不得低于规格里的 `check()` 调用点数 —— 调用点会循环执行，实际通过数不可能更低；
//   d) §0.1 的"脚本编码"个数必须等于 `tools/**/*.ps1`（排除 `ripgrep` 里第三方附带的那一个）
//      —— 该守卫口径与 `verify-script-encoding.ps1` 自己用的筛选一致。
const behaviorDocPath = path.join(root, 'docs/intellij-platform-ui-behavior.md');
const scriptEncodingScript = path.join(root, 'tools/audit/verify-script-encoding.ps1');
const behavior = fs.readFileSync(behaviorDocPath, 'utf8');
// `### 2.3 验证证据` 小节里的**历史沿革**段落也写着 `live-shell 通过 1312 项断言`（那是第 1xx 轮的旧值），
// 因此"当前登记值"必须从**已验证快照**块里读：它紧跟在 `#### 已验证快照` 标题之后，只有一段说明与哈希表。
// 直接在 §2.3 里取第一个 `live-shell 通过 N 项断言` 会读到沿革里的旧值（实测读到 1312）。
const snapshotBlock = (() => {
  const start = behavior.indexOf('#### 已验证快照');
  if (start < 0) return null;
  const next = behavior.indexOf('\n#### ', start + 1);
  return behavior.slice(start, next < 0 ? behavior.length : next);
})();
if (!snapshotBlock) {
  notes.push('行为文档里找不到 `#### 已验证快照`，跳过 live-shell 断言数核对');
}
const registeredSource = snapshotBlock || behavior;
const md5 = (filePath) => crypto.createHash('md5').update(fs.readFileSync(filePath)).digest('hex');
// 表里的文件名单元格可能带括号说明（`web/src/markdown.js（第 283 轮加入本表）`），
// 所以按"反引号里的路径"取整格，而不是让整格 equals 文件名。
const registeredRow = (label) => registeredSource.split('\n')
  .find((line) => {
    const cell = (/^\|\s*`([^`]+)`[^|]*\|/.exec(line.trim()) || [])[1];
    return cell === label;
  }) || '';
const registeredHash = (label) => (registeredRow(label).match(/`([0-9a-f]{32})`/) || [])[1] || null;
const registered = {
  count: Number((/live-shell 通过 (\d+) 项断言/.exec(registeredSource) || [])[1] || 0),
  specHash: registeredHash('live-shell.spec.cjs'),
  files: {
    'web/src/mockup.css': registeredHash('web/src/mockup.css'),
    'web/src/mockup.js': registeredHash('web/src/mockup.js'),
    'web/src/live-data.js': registeredHash('web/src/live-data.js'),
    'web/src/bridge.js': registeredHash('web/src/bridge.js'),
    'web/src/markdown.js': registeredHash('web/src/markdown.js'),
    'web/src/image-preview.js': registeredHash('web/src/image-preview.js'),
    'web/src/current-find.js': registeredHash('web/src/current-find.js'),
  },
};
const snapshotDrift = Object.entries(registered.files)
  .filter(([filePath, expected]) => !expected || md5(path.join(root, ...filePath.split('/'))) !== expected)
  .map(([filePath]) => filePath);
const specHash = md5(path.join(root, 'tools/audit/live-shell.spec.cjs'));
const suiteUnchanged = registered.count > 0 && registered.specHash === specHash && snapshotDrift.length === 0;

if (!suiteUnchanged) {
  notes.push('§2.3 已验证快照已过期（live-shell 断言数 ' + (registered.count || '解析不到')
    + '，规格 md5 ' + (registered.specHash === specHash ? '一致' : '不一致')
    + (snapshotDrift.length ? '，漂移文件 ' + snapshotDrift.join('、') : '')
    + '）⇒ 不把该断言数当作机械核对依据，需重跑套件后更新 §2.3');
} else {
  // c) §0／§0.1 的"当前结果"行必须携带登记值（历史沿革段里的旧值按轮次递增，必然更小，不算矛盾），
  //    且任何 `live-shell 通过 N 项断言` 的声明都不得**大于**登记值：写大只能来自没跑过的推导。
  const declared = [...new Set([...doc.matchAll(/live-shell 通过 (\d+) 项断言/g)].map((m) => Number(m[1])))];
  for (const value of declared) {
    if (value > registered.count) {
      problems.push('live-shell 断言数 ' + value + ' > §2.3 已验证快照的 ' + registered.count
        + '（更大的值来自未跑过的推导）');
    }
  }
  // §0 与 §0.1 末尾（"本轮验证汇总"）各有一行"当前结果"，必须与快照一致；旧行保留但要在标注之后。
  const currentRows = doc.split('\n')
    .filter((line) => /实时外壳/.test(line) && /\d+\s*\/\s*\d+/.test(line));
  const regressed = currentRows
    .map((line) => (/(\d+)\s*\/\s*\d+\s*（未执行/.exec(line) || [])[1])
    .filter(Boolean)
    .filter((value) => Number(value) < registered.count);
  if (currentRows.length === 0) problems.push('§0／§0.1 找不到"实时外壳"的当前结果行');
  if (regressed.length > 0) {
    problems.push('"实时外壳"结果行里的断言数 ' + regressed.join('、') + ' < §2.3 已验证快照的 '
      + registered.count + '（当前结果行必须等于登记值；历史轮次的数值要明确标成历史）');
  }
  const specText = fs.readFileSync(path.join(root, 'tools/audit/live-shell.spec.cjs'), 'utf8');
  const siteFloor = (specText.match(/check\(/g) || []).length;
  if (registered.count < siteFloor) {
    problems.push('§2.3 的断言数 ' + registered.count + ' < 规格里的 check() 调用点 ' + siteFloor
      + '（调用点会循环执行，实际通过数不可能低于调用点数）');
  }
  notes.push('§2.3 已验证快照仍有效：live-shell ' + registered.count + ' 项断言（≥ 规格 ' + siteFloor
    + ' 个 check() 调用点）、规格与受登记文件 md5 全部一致');
}

// d) §0.1 的脚本编码个数 = 仓库里的 PowerShell 脚本数（与 verify-script-encoding.ps1 同口径）
if (!fs.existsSync(scriptEncodingScript)) {
  notes.push('找不到 verify-script-encoding.ps1，跳过脚本计数核对');
} else {
  const countScript = (directory) => fs.readdirSync(directory, { withFileTypes: true })
    .filter((entry) => entry.name !== 'ripgrep')
    .map((entry) => {
      const entryPath = path.join(directory, entry.name);
      if (entry.isDirectory()) return countScript(entryPath);
      return entry.name.toLowerCase().endsWith('.ps1') ? 1 : 0;
    })
    .reduce((total, value) => total + value, 0);
  const scriptCount = countScript(path.join(root, 'tools'));
  // 取**当前基线**那一行（`| 审计脚本编码规则 | …verify-script-encoding.ps1… |`）。
  // 用"第一行含脚本名"会取到 §3.1 的历史行（`PASS（12 个脚本）`）——实测被它误判成"没有可核对的个数"。
  const scriptsRow = doc.split('\n')
    .find((line) => /审计脚本编码规则/.test(line) && /verify-script-encoding\.ps1/.test(line)) || '';
  // 文档里写成 `PASS（14 个脚本…）` 或 `PASS**（14 个 `.ps1` …）`（`.ps1` 带反引号），
  // 因此只锚定 `PASS` → 最多 12 个非数字字符 → `N 个`；换行/改写格式导致取不到数时报错，不静默跳过。
  const claimed = Number((/PASS\D{0,12}(\d+) 个/.exec(scriptsRow) || [])[1]);
  if (!claimed) {
    problems.push('§0.1 的脚本编码行没有可核对的脚本个数（写成"PASS（N 个脚本）"或"PASS（N 个 .ps1）"）');
  } else if (claimed !== scriptCount) {
    problems.push('§0.1 的脚本个数 ' + claimed + ' ≠ 仓库里的 PowerShell 脚本数 ' + scriptCount
      + '（与 verify-script-encoding.ps1 同口径，排除 ripgrep 里的第三方脚本）');
  } else {
    notes.push(`脚本编码：${claimed} 个 .ps1 与仓库计数一致`);
  }
}

// ---- 6) §0 的"说明"数字：调用点数与 `csCheck` 条数来自规格，实际断言数来自 §2.3 的登记值 ----
// 背景（第 312 轮收口）：这段说明一直写着"745 个 `check(...)` 调用点 / `csCheck` 2 条 /
// 实际断言数 942"，而同一份文档的 §2.3 登记值是 1459、规格里实际是 1232 个调用点与 8 条
// `csCheck` —— 三个手写数字互相矛盾，读者无法分辨哪个是当前值。这里把它们钉到各自的唯一来源：
// 调用点数与 `csCheck` 条数对规格实测（与 `check-interactions.cjs` 同一条正则口径），
// 实际断言数对 §2.3 已验证快照的登记值（仅在快照仍有效时才当依据，见上）。
const liveShellPath = path.join(root, 'tools/audit/live-shell.spec.cjs');
const liveShellSource = fs.readFileSync(liveShellPath, 'utf8');
const callSites = (liveShellSource.match(/check\(/g) || []).length;
const csCheckSites = (liveShellSource.match(/csCheck\(/g) || []).length;
const callSiteClaim = /它有 (\d+) 个 `check\(\.\.\.\)` 调用点/.exec(doc);
if (!callSiteClaim) {
  problems.push('§0 的说明没有可核对的 `check(...)` 调用点数');
} else if (Number(callSiteClaim[1]) !== callSites) {
  problems.push('§0 说明里的 `check(...)` 调用点 ' + callSiteClaim[1] + ' ≠ 规格实际 ' + callSites);
} else {
  notes.push(`§0 说明：${callSites} 个 check() 调用点与规格一致`);
}
const csCheckClaim = /`csCheck` 包裹的 (\d+) 条会话断言/.exec(doc);
if (!csCheckClaim) {
  problems.push('§0 的说明没有可核对的 `csCheck` 条数');
} else if (Number(csCheckClaim[1]) !== csCheckSites) {
  problems.push('§0 说明里的 `csCheck` 条数 ' + csCheckClaim[1] + ' ≠ 规格实际 ' + csCheckSites);
} else {
  notes.push(`§0 说明：${csCheckSites} 条 csCheck 会话断言与规格一致`);
}
// 说明里在这句后面跟了补充（"（1459，见 §2.3 的已验证快照）"），因此只要求数字到右括号之间没有别的括号，
// 也不跨行（避免括号缺失时把后文整段吞进来）。
const assertionClaim = /实际断言数（(\d+)[^）\n]{0,80}）/.exec(doc);
if (!suiteUnchanged) {
  notes.push('§2.3 快照过期，跳过 §0 说明里"实际断言数"的核对');
} else if (!assertionClaim) {
  problems.push('§0 的说明没有可核对的"实际断言数（N）"');
} else if (Number(assertionClaim[1]) !== registered.count) {
  problems.push('§0 说明里的实际断言数 ' + assertionClaim[1] + ' ≠ §2.3 登记的 ' + registered.count);
} else {
  notes.push(`§0 说明：实际断言数 ${registered.count} 与 §2.3 登记值一致`);
}

// ---- 7) 需要跑套件才能得到的数字：只打印，明确不机械核对 ----
const reported = doc.split('\n')
  .filter((line) => /live-shell|真机全场景巡检/.test(line) && /\*\*\d+\/\d+\*\*|\d+ PASS/.test(line))
  .map((line) => line.split('|')[0].trim() + ' → ' + (line.match(/\*\*\d+\/\d+\*\*|\d+\/\d+ PASS[^|]*/g) || []).join(' / '));

for (const note of notes) console.log('NOTE ' + note);
for (const line of reported) console.log('REPORTED(未机械核对) ' + line);
if (problems.length > 0) {
  for (const problem of problems) console.error('PROBLEM ' + problem);
  console.error('DOC_CLAIMS_FAILED ' + problems.length + ' problem(s)');
  process.exit(1);
}
console.log('DOC_CLAIMS_OK');
