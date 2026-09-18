// 实时外壳 vs 视觉稿：逐元素比对布局签名。
//
// 用法：
//   node tools/audit/compare-dom.cjs --live <dump-live-dom.ps1 输出的 json> [--scene git-history]
//        [--top 40] [--json <报告输出路径>] [--all]
//
// 真机一侧由 tools/audit/dump-live-dom.ps1 经 CDP 抓取（两侧跑同一段
// tools/audit/dom-signature.js），本脚本负责在 Chromium 里渲染静态视觉稿页并比对。
//
// 输出分三类，便于逐条修：
//   LAYOUT 几何/排版/配色/间距不一致（真正要修的差异）
//   TEXT   只有文字不一致（实时外壳显示真实数据，通常是预期差异）
//   SHAPE  节点只在一边存在（结构差异）
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.PLAYWRIGHT || '/root/.npm/_npx/e41f203b7505f1fb/node_modules/playwright');

const REPO = path.resolve(__dirname, '..', '..');
const SIGNATURE = fs.readFileSync(path.join(__dirname, 'dom-signature.js'), 'utf8');

// 这些字段只反映内容，不反映设计；单独归类，避免淹没真正的布局差异。
const TEXT_FIELDS = new Set(['ownText', 'text', 'scroll']);
// 实时外壳里必然不同的字段：滚动位置与内容长度随真实数据变化。
const IGNORED = new Set(['scroll']);

function parseArgs(argv) {
  const args = { top: 40, selectors: null, all: false, theme: 'dark', width: 1180, height: 760 };
  for (let index = 0; index < argv.length; index++) {
    const name = argv[index];
    if (name === '--live') args.live = argv[++index];
    else if (name === '--scene') args.scene = argv[++index];
    else if (name === '--top') args.top = Number(argv[++index]);
    else if (name === '--json') args.json = argv[++index];
    else if (name === '--selectors') args.selectors = argv[++index].split(',').map((s) => s.trim()).filter(Boolean);
    else if (name === '--theme') args.theme = argv[++index];
    else if (name === '--all') args.all = true;
    else throw new Error('未知参数：' + name);
  }
  if (!args.live) throw new Error('缺少 --live <json>');
  return args;
}

/** 渲染静态视觉稿页（设计基线）并抽取同一份签名。 */
async function captureMockup(browser, scene, selectors, theme, width, height, prune, typography) {
  const html = path.join(REPO, 'docs', 'ux-mockups', scene + '.html');
  if (!fs.existsSync(html)) throw new Error('视觉稿页面不存在：' + html);
  const context = await browser.newContext({ viewport: { width, height }, deviceScaleFactor: 1 });
  const page = await context.newPage();
  // 字体与字号必须和实时侧一致：它们由 dump 记录（-FontFamily/-FontSize），
  // 视觉稿页面通过 ui-family/ui-size 查询参数应用（与实时外壳同一套排版预览管线）。
  // 只改一侧会让"字体"本身变成差异 —— 第 243 轮就是这样把 16 项差异放大到 52 项。
  const query = ['theme=' + encodeURIComponent(theme)];
  if (typography && typography.fontFamily) query.push('ui-family=' + encodeURIComponent(String(typography.fontFamily).replace(/["']/g, '')));
  if (typography && typography.fontSize) query.push('ui-size=' + encodeURIComponent(String(typography.fontSize).replace('px', '')));
  await page.goto('file://' + html + '?' + query.join('&'));
  await page.waitForTimeout(500);
  const regions = {};
  for (const selector of selectors) {
    const expression = '(' + SIGNATURE + ')(' + JSON.stringify(selector) + ',{prune:' + JSON.stringify(prune || '') + '})';
    regions[selector] = await page.evaluate(expression);
  }
  await context.close();
  return regions;
}

function compareNode(mockNode, liveNode) {
  const changes = [];
  for (const field of Object.keys(mockNode)) {
    if (field === 'depth' || field === 'path') continue;
    const mockValue = JSON.stringify(mockNode[field]);
    const liveValue = JSON.stringify(liveNode[field]);
    if (mockValue === liveValue) continue;
    if (IGNORED.has(field)) continue;
    changes.push({ field, kind: TEXT_FIELDS.has(field) ? 'TEXT' : 'LAYOUT', mock: mockNode[field], live: liveNode[field] });
  }
  return changes;
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  const dump = JSON.parse(fs.readFileSync(args.live, 'utf8'));
  const scene = args.scene || dump.scene;
  const selectors = args.selectors || Object.keys(dump.regions);
  const browser = await chromium.launch();
  const mockRegions = await captureMockup(browser, scene, selectors, args.theme, args.width, args.height, dump.prune, dump);
  await browser.close();

  const report = { scene, theme: args.theme, layout: [], text: [], shape: [] };
  for (const selector of selectors) {
    const mock = mockRegions[selector] || [];
    const live = (dump.regions[selector] || []).filter(Boolean);
    const liveByPath = new Map(live.map((node) => [node.path, node]));
    const mockByPath = new Map(mock.map((node) => [node.path, node]));
    for (const mockNode of mock) {
      const liveNode = liveByPath.get(mockNode.path);
      if (!liveNode) { report.shape.push({ kind: 'MISSING-IN-LIVE', selector, path: mockNode.path, cls: mockNode.cls, text: mockNode.ownText }); continue; }
      const changes = compareNode(mockNode, liveNode);
      const layout = changes.filter((c) => c.kind === 'LAYOUT');
      const text = changes.filter((c) => c.kind === 'TEXT');
      if (layout.length) report.layout.push({ selector, path: mockNode.path, cls: mockNode.cls, tag: mockNode.tag, changes: layout });
      if (text.length) report.text.push({ selector, path: mockNode.path, cls: mockNode.cls, changes: text });
    }
    for (const liveNode of live) {
      if (!mockByPath.has(liveNode.path)) report.shape.push({ kind: 'EXTRA-IN-LIVE', selector, path: liveNode.path, cls: liveNode.cls, text: liveNode.ownText });
    }
  }

  // 空区域不能当成"没有差异"：选择器写错时两侧都是 0 个节点，逐项比对自然 0 差异，
  // 看起来像"完全一致"（本轮 .overlay-layer 在 quick-open 场景里根本不存在，就是这样）。
  // 因此任一选择器在两侧都取不到节点时直接失败，只在一侧取不到时明确告警。
  const emptyRegions = selectors.filter((selector) =>
    (mockRegions[selector] || []).length === 0 && (dump.regions[selector] || []).length === 0);
  const oneSidedRegions = selectors.filter((selector) =>
    ((mockRegions[selector] || []).length === 0) !== ((dump.regions[selector] || []).length === 0));
  if (emptyRegions.length > 0) {
    console.error('EMPTY_REGION 选择器在视觉稿与实时两侧都没有节点，对照无意义：' + JSON.stringify(emptyRegions));
    process.exit(3);
  }
  if (oneSidedRegions.length > 0) {
    console.error('WARN_ONE_SIDED_REGION 只在一侧存在节点：' + JSON.stringify(oneSidedRegions));
  }

  const show = (list) => (args.all ? list : list.slice(0, args.top));
  console.log('scene=' + scene + ' 节点数 视觉稿=' + selectors.reduce((sum, s) => sum + (mockRegions[s] || []).length, 0)
    + ' 实时=' + selectors.reduce((sum, s) => sum + (dump.regions[s] || []).length, 0));
  console.log('LAYOUT 差异 ' + report.layout.length + ' 项：');
  for (const item of show(report.layout)) {
    console.log('  ' + item.path + ' <' + item.tag + ' class="' + item.cls + '">');
    for (const change of item.changes) console.log('      ' + change.field + ': 视觉稿=' + JSON.stringify(change.mock) + ' 实时=' + JSON.stringify(change.live));
  }
  console.log('SHAPE 差异 ' + report.shape.length + ' 项：');
  for (const item of show(report.shape)) console.log('  ' + item.kind + ' ' + item.path + ' <' + item.cls + '> "' + item.text + '"');
  console.log('TEXT 差异 ' + report.text.length + ' 项：');
  for (const item of show(report.text)) console.log('  ' + item.path + ' <' + item.cls + '> ' + item.changes.map((c) => c.field + ' 视觉稿=' + JSON.stringify(c.mock) + ' 实时=' + JSON.stringify(c.live)).join(' | '));
  if (args.json) {
    fs.writeFileSync(args.json, JSON.stringify(report, null, 2));
    console.log('报告写入 ' + args.json);
  }
  process.exitCode = report.layout.length > 0 ? 1 : 0;
}

main().catch((error) => { console.error('ERROR ' + error.stack); process.exit(2); });
