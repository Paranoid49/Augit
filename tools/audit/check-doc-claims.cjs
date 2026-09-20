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
//   4. §1.1 的像素行数 = §1.4 里写的 A 线分母与分子。
//
// 它**不**验证需要跑浏览器/真机才能得到的数字（live-shell 断言数、验收 PASS 数）——
// 那些由各自的套件产出、写进 §0 时都带日期；本检查器只打印它们，明确标注"未机械核对"，
// 不假装核对过。
//
// 用法：node tools/audit/check-doc-claims.cjs
// 退出码：0 = 全部自洽；1 = 有数字互相矛盾。
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../..');
const docPath = path.join(root, 'docs/ui-compliance.md');
const doc = fs.readFileSync(docPath, 'utf8');

const problems = [];
const notes = [];
const sectionOf = (heading) => {
  const start = doc.indexOf(heading);
  if (start < 0) return null;
  const next = doc.indexOf('\n### ', start + heading.length);
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

// ---- 5) 需要跑套件才能得到的数字：只打印，明确不机械核对 ----
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
