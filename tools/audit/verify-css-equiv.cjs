// 新旧 CSS 等价性检查：把两份 CSS 分别作用在同一批页面，逐元素比对计算样式。
// 用法：node tools/audit/verify-css-equiv.cjs <旧CSS路径> [页面名...]
// 目的：批量删改 CSS（去重、合并规则等）后，证明"渲染结果未变"。
// 第 107 轮用它抓到 6 处"后一处在 @media 内"的不安全去重。
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('/root/.npm/_npx/e41f203b7505f1fb/node_modules/playwright');

const PROPS = ['backgroundColor','color','width','height','padding','margin','borderRadius',
  'borderTopWidth','borderTopColor','fontSize','display','gridTemplateColumns','gap','boxShadow',
  'outlineColor','opacity','backgroundImage','minHeight','maxHeight'];
const ROOT = '/mnt/d/github/Augit/docs/ux-mockups';
const DEFAULT_PAGES = ['main-project.html','reset.html','stash.html','push.html',
  'changes-context-menu.html','conflict-resolver.html'];

const sig = (p, cssText) => p.evaluate(({ PROPS, cssText }) => {
  if (cssText !== null) {
    for (const l of document.querySelectorAll('link[rel=stylesheet]')) l.disabled = true;
    for (const s of document.querySelectorAll('style[id=equiv-old]')) s.remove();
    const st = document.createElement('style');
    st.id = 'equiv-old'; st.textContent = cssText;
    document.head.appendChild(st);
  }
  const out = [];
  for (const el of document.querySelectorAll('*')) {
    if (el.tagName === 'STYLE' || el.tagName === 'LINK') continue;   // 否则元素索引会整体错位
    const cs = getComputedStyle(el);
    const row = [el.tagName + '#' + (el.id || '') + '.' + (el.className || '')];
    for (const k of PROPS) row.push(cs[k]);
    out.push(row.join('|'));
  }
  return out;
}, { PROPS, cssText });

(async () => {
  const oldPath = process.argv[2];
  if (!oldPath) { console.error('用法: node verify-css-equiv.cjs <旧CSS路径> [页面...]'); process.exit(2); }
  const oldCss = fs.readFileSync(oldPath, 'utf8');
  const pages = process.argv.slice(3).length ? process.argv.slice(3) : DEFAULT_PAGES;
  const b = await chromium.launch();
  const p = await b.newPage({ viewport: { width: 1300, height: 900 } });
  let bad = 0;
  for (const page of pages) {
    await p.goto('file://' + path.join(ROOT, page));
    const now = await sig(p, null);
    const before = await sig(p, oldCss);
    let d = 0;
    for (let i = 0; i < Math.max(now.length, before.length); i++) if (now[i] !== before[i]) d++;
    if (d) { bad++; console.log(`FAIL ${page}：${d} 处计算样式差异`); }
    else   { console.log(`OK   ${page}：${now.length} 个元素 × ${PROPS.length} 项属性一致`); }
  }
  await b.close();
  console.log(bad ? `FAIL: ${bad} 个页面存在差异` : 'PASS: 新旧 CSS 渲染等价。');
  process.exit(bad ? 1 : 0);
})();
