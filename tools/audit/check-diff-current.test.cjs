// 「当前差异块高亮」的自动化检查（第 114 轮新增）。
// 覆盖三件事：
//   1. 行为：给变更行加上 `.diff-current` 后，计算样式的背景应变成强色令牌（浅/深两套主题各验一次）。
//   2. 接线：`live-data.js` 的 `moveDiffChange` 必须施加并清除 `.diff-current`。
//   3. 令牌：三个 `--augit-diff-current-*` 在浅色与深色块里都有定义。
// 用法：node tools/audit/check-diff-current.test.cjs [playwright路径]
const fs = require('node:fs');
const path = require('node:path');
const PW = process.argv[2] || '/root/.npm/_npx/e41f203b7505f1fb/node_modules/playwright';
const { chromium } = require(PW);

const ROOT = '/mnt/d/github/Augit';
const CSS = path.join(ROOT, 'web/src/mockup.css');
const JS = path.join(ROOT, 'web/src/live-data.js');
const PAGE = 'file://' + path.join(ROOT, 'docs/ux-mockups/commit-diff.html');

let fail = 0;
const ok = (cond, msg) => { console.log((cond ? 'OK   ' : 'FAIL ') + msg); if (!cond) fail += 1; };

(async () => {
  const css = fs.readFileSync(CSS, 'utf8');
  const js = fs.readFileSync(JS, 'utf8');

  // ---- 3. 令牌两套主题都有 ----
  const rootStart = css.indexOf(':root');
  const darkStart = css.indexOf('body[data-theme="dark"]');
  for (const tok of ['--augit-diff-current-added', '--augit-diff-current-deleted', '--augit-diff-current-modified']) {
    const re = new RegExp('\\n\\s*' + tok.replace(/[-]/g, '\\-') + ':\\s*#[0-9a-fA-F]{6,8};');
    const light = re.test(css.slice(rootStart, darkStart));
    const dark = re.test(css.slice(darkStart));
    ok(light && dark, `令牌 ${tok} 在浅色与深色块中都有定义`);
  }

  // ---- 2. JS 接线 ----
  const move = js.slice(js.indexOf('function moveDiffChange'));
  ok(/classList\.remove\("diff-current"\)/.test(move), 'moveDiffChange 清除旧的 .diff-current');
  ok(/classList\.add\("diff-current"\)/.test(move), 'moveDiffChange 给当前块加 .diff-current');

  // ---- 1. 行为：两种主题下加类后背景变为强色 ----
  const b = await chromium.launch();
  const p = await b.newPage({ viewport: { width: 1300, height: 900 } });
  await p.goto(PAGE);
  const kindClass = { added: 'added', removed: 'removed', changed: 'changed' };
  for (const theme of ['light', 'dark']) {
    if (theme === 'dark') { await p.evaluate(() => { document.body.dataset.theme = 'dark'; }); await p.waitForTimeout(50); }
    const res = await p.evaluate((kindClass) => {
      const out = {};
      const token = (n) => getComputedStyle(document.body).getPropertyValue(n).trim();
      out.added = token('--augit-diff-current-added');
      out.removed = token('--augit-diff-current-deleted');
      out.changed = token('--augit-diff-current-modified');
      const line = document.querySelector('.diff-code-line.added, .diff-code-line.removed, .diff-code-line.changed');
      if (line) {
        line.classList.add('diff-current');
        out.applied = getComputedStyle(line).backgroundColor;
        line.classList.remove('diff-current');
        out.plain = getComputedStyle(line).backgroundColor;
      }
      return out;
    }, kindClass);
    const hexToRgb = (h) => { const s = h.replace('#', ''); return `rgb(${parseInt(s.slice(0,2),16)}, ${parseInt(s.slice(2,4),16)}, ${parseInt(s.slice(4,6),16)})`; };
    ok(res.applied && res.plain && res.applied !== res.plain,
       `${theme}：加 .diff-current 后背景改变（${res.plain} → ${res.applied}）`);
    const kind = null; // 逐类单独验：上面的元素是页面里第一个变更行
    if (res.applied) {
      const expect = Object.values({ a: res.added, r: res.removed, c: res.changed }).map(hexToRgb);
      ok(expect.includes(res.applied), `${theme}：高亮色取自 --augit-diff-current-*（${res.applied}）`);
    }
  }
  await b.close();
  console.log(fail ? `FAIL: ${fail} 项未通过` : 'PASS: 当前差异块高亮检查通过。');
  process.exit(fail ? 1 : 0);
})();
