// 「Diff 行色两层」的自动化检查（第 223 轮，取代第 114 轮的 check-diff-current）。
// 覆盖四件事：
//   1. 令牌：三个 `--augit-diff-*`（整行底）与三个 `--augit-diff-inline-*`（行内层）在浅色与深色块里
//      都等于权威值（见 `08-diff-merge.md` §2.2）。
//   2. 推导：`Modified` 行的柔和整行底必须等于 `mix(DIFF_MODIFIED.BACKGROUND, 编辑器底, 0.6)`
//      （权威 `TextDiffTypeImpl.getIgnoredColor()`），且与行内层可分辨。
//   3. 行为：给三种变更行各插一个 `<mark>` 后，计算样式的背景应等于该行类型对应的行内令牌，
//      且文字颜色**继承行本身**（不能被 `<mark>` 的 UA 默认黑字覆盖）。
//   4. 回归守卫：无权威对应的 `.diff-current` 层（令牌／规则／JS 施加）已删除，不得回归。
// 用法：node tools/audit/check-diff-inline.test.cjs [playwright路径]
const fs = require('node:fs');
const path = require('node:path');
const PW = process.argv[2] || '/root/.npm/_npx/e41f203b7505f1fb/node_modules/playwright';
const { chromium } = require(PW);

const ROOT = '/mnt/d/github/Augit';
const CSS = path.join(ROOT, 'web/src/mockup.css');
const MOCKUP_CSS = path.join(ROOT, 'docs/ux-mockups/mockup.css');
const JS = path.join(ROOT, 'web/src/live-data.js');
const PAGE = 'file://' + path.join(ROOT, 'docs/ux-mockups/commit-diff.html');

let fail = 0;
const ok = (cond, msg) => { console.log((cond ? 'OK   ' : 'FAIL ') + msg); if (!cond) fail += 1; };

// 权威取值（见 `08-diff-merge.md` §2.2 与 `diff-impl` 的绘制路径）：
//   整行底：无行内差异 ⇒ 全强度 `DIFF_*.BACKGROUND`；有行内差异 ⇒ `getIgnoredColor()` = mix(…, 编辑器底, 0.6)。
//   纯新增/纯删除块没有行内差异，只有 `Modified` 行有 ⇒ 只有 modified 的整行底是柔和值。
const AUTH = {
  light: {
    line: { added: '#bee6be', deleted: '#d6d6d6', modified: '#e7effa' },
    inline: { added: '#bee6be', deleted: '#d6d6d6', modified: '#c2d8f2' },
    editorBg: '#ffffff',
  },
  dark: {
    line: { added: '#294436', deleted: '#484a4a', modified: '#283541' },
    inline: { added: '#294436', deleted: '#484a4a', modified: '#385570' },
    editorBg: '#1e1f22',
  },
};
// 行内类名 → 令牌后缀（`mockup.js` 的 `cssKind()`：Modified 渲染成 `changed`）。
const CLASS_OF = { added: 'added', deleted: 'removed', modified: 'changed' };
const rgb = (hex) => {
  const s = hex.replace('#', '');
  return `rgb(${parseInt(s.slice(0, 2), 16)}, ${parseInt(s.slice(2, 4), 16)}, ${parseInt(s.slice(4, 6), 16)})`;
};
// `TextDiffTypeImpl.getIgnoredColor()` → `ColorUtil.mix(c1, c2, 0.6)` → `MixedColorProducer.mix()`
// 的逐通道 `v0 + round(balance * (v1 - v0))`。
const mix = (from, to, balance) => {
  const parse = (hex) => [1, 3, 5].map((i) => parseInt(hex.replace('#', '').slice(i - 1, i + 1), 16));
  const [r1, g1, b1] = parse(from);
  const [r2, g2, b2] = parse(to);
  const channel = (a, b) => a + Math.round(balance * (b - a));
  return '#' + [channel(r1, r2), channel(g1, g2), channel(b1, b2)]
    .map((v) => v.toString(16).padStart(2, '0')).join('');
};

(async () => {
  const css = fs.readFileSync(CSS, 'utf8');
  const mockupCss = fs.readFileSync(MOCKUP_CSS, 'utf8');
  const js = fs.readFileSync(JS, 'utf8');

  ok(css === mockupCss, 'web/src/mockup.css 与 docs/ux-mockups/mockup.css 字节一致');

  const rootStart = css.indexOf(':root');
  const darkStart = css.indexOf('body[data-theme="dark"]');
  const lightBlock = css.slice(rootStart, darkStart);
  const darkBlock = css.slice(darkStart);
  const tokenRe = (name, value) => new RegExp(`\\n\\s*--augit-diff-${name}:\\s*${value};`);

  // ---- 1. 令牌两套主题都有，且等于权威值 ----
  for (const [theme, block] of [['light', lightBlock], ['dark', darkBlock]]) {
    for (const [name, value] of Object.entries(AUTH[theme].line)) {
      ok(tokenRe(name, value).test(block), `令牌 --augit-diff-${name} 在${theme}块中为权威值 ${value}（整行底）`);
    }
    for (const [name, value] of Object.entries(AUTH[theme].inline)) {
      ok(tokenRe(`inline-${name}`, value).test(block), `令牌 --augit-diff-inline-${name} 在${theme}块中为权威值 ${value}（行内层）`);
    }
  }

  // ---- 2. 推导：Modified 的柔和整行底 = mix(DIFF_MODIFIED.BACKGROUND, 编辑器底, 0.6)，且两档可分辨 ----
  for (const [theme, block] of [['light', lightBlock], ['dark', darkBlock]]) {
    const { line, inline, editorBg } = AUTH[theme];
    const derived = mix(inline.modified, editorBg, 0.6);
    const actual = (block.match(/--augit-diff-modified:\s*(#[0-9a-fA-F]{6,8});/) || [])[1] || '';
    ok(actual.toLowerCase() === derived,
      `${theme}：--augit-diff-modified = ${actual} 应等于 getIgnoredColor() 推出的 ${derived}`);
    ok(line.modified !== inline.modified,
      `${theme}：Modified 行的整行底(${line.modified})与行内层(${inline.modified})必须可分辨`);
  }

  // ---- 4. 回归守卫：`.diff-current` 层已删除 ----
  for (const token of ['--augit-diff-current-added', '--augit-diff-current-deleted', '--augit-diff-current-modified']) {
    ok(!css.includes(token), `已删除的令牌 ${token} 不再出现`);
  }
  ok(!/diff-current/.test(css), 'mockup.css 不再出现 diff-current');
  ok(!/classList\.(add|remove|toggle)\("diff-current"\)/.test(js),
    'live-data.js 不再施加 .diff-current（权威 DiffDrawUtil.PaintMode 无此层）');
  ok(/diff-code-line mark/.test(css), 'mockup.css 有 .diff-code-line mark 规则');

  // ---- 3. 行为：两种主题下三类行内高亮的计算样式 ----
  const b = await chromium.launch();
  const p = await b.newPage({ viewport: { width: 1300, height: 900 } });
  await p.goto(PAGE);
  for (const theme of ['light', 'dark']) {
    if (theme === 'dark') { await p.evaluate(() => { document.body.dataset.theme = 'dark'; }); await p.waitForTimeout(50); }
    const res = await p.evaluate((classOf) => {
      const host = document.querySelector('.diff-columns .diff-side') || document.querySelector('.diff-layout') || document.body;
      const out = {};
      for (const [name, cls] of Object.entries(classOf)) {
        const line = document.createElement('div');
        line.className = `diff-code-line ${cls}`;
        line.innerHTML = 'aa<mark>bb</mark>cc';
        host.appendChild(line);
        const mark = line.querySelector('mark');
        out[name] = {
          bg: getComputedStyle(mark).backgroundColor,
          color: getComputedStyle(mark).color,
          lineColor: getComputedStyle(line).color,
          lineBg: getComputedStyle(line).backgroundColor,
        };
        line.remove();
      }
      return out;
    }, CLASS_OF);
    for (const [name, expectHex] of Object.entries(AUTH[theme].inline)) {
      ok(res[name].bg === rgb(expectHex),
        `${theme}：.diff-code-line.${CLASS_OF[name]} mark 背景 = ${res[name].bg}（期望 ${rgb(expectHex)}）`);
      ok(res[name].color === res[name].lineColor,
        `${theme}：.diff-code-line.${CLASS_OF[name]} mark 文字色继承行本身（${res[name].color}）`);
      ok(res[name].lineBg === rgb(AUTH[theme].line[name]),
        `${theme}：.diff-code-line.${CLASS_OF[name]} 整行底 = ${res[name].lineBg}（期望 ${rgb(AUTH[theme].line[name])}）`);
    }
  }
  await b.close();
  console.log(fail ? `FAIL: ${fail} 项未通过` : 'PASS: Diff 行色两层检查通过。');
  process.exit(fail ? 1 : 0);
})();
