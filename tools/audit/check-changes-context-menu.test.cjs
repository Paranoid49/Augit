// 「Changes 右键菜单」的项顺序自动化检查（第 138 轮新增）。
//
// 权威 `ChangesViewPopupMenu`（`platform/vcs-impl/resources/META-INF/VcsActions.xml:186-219`）的相对顺序：
//   CheckinFiles → ChangesView.Revert / .RevertFiles → Move → Diff.ShowDiff → EditSource →
//   CopyReferencePopupGroup → … → Refresh → VersionControlsGroup
// 其中 `VersionControlsGroup` 经 `VcsFileGroupPopup`（`VcsActions.xml:56-58`）收编各 VCS 的文件级动作；
// git 侧是 `Git.FileActions`（`plugins/git4idea/backend/resources/intellij.vcs.git.backend.xml:162-174`）：
//   CheckinFiles → Git.Add → **Annotate（Blame）** → Compare.* → **Vcs.ShowTabbedFileHistory（文件历史）** → …
// ⇒ 权威的相对顺序：**回滚在显示 Diff 之前**、**Blame 在文件历史之前**。
//
// 覆盖四件事：
//   1. 渲染顺序（`changes-context-menu.html` 里菜单项的实际先后）；
//   2. 分组（三组之间各一个分隔符，组内不插分隔符）；
//   3. 接线：实时侧复用 `mockup.js` 的 `changesContextMenu()`（改一处即两侧一致）；
//   4. 权威 id 在注释里留痕（便于回溯）。
// 用法：node tools/audit/check-changes-context-menu.test.cjs [playwright路径]
const fs = require('node:fs');
const path = require('node:path');
const PW = process.argv[2] || '/root/.npm/_npx/e41f203b7505f1fb/node_modules/playwright';
const { chromium } = require(PW);

const ROOT = '/mnt/d/github/Augit';
const MOCKUP_JS = path.join(ROOT, 'web/src/mockup.js');
const LIVE_JS = path.join(ROOT, 'web/src/live-data.js');
const PAGE = 'file://' + path.join(ROOT, 'docs/ux-mockups/changes-context-menu.html');
const CHROME = process.env.AUGIT_CHROME || '/root/.cache/ms-playwright/chromium-1234/chrome-linux64/chrome';

const EXPECTED = ['回滚…', '显示 Diff', 'Blame', '文件历史', '复制路径', '在资源管理器中定位'];

let fail = 0;
const ok = (cond, msg) => { console.log((cond ? 'OK   ' : 'FAIL ') + msg); if (!cond) fail += 1; };

(async () => {
  const mockup = fs.readFileSync(MOCKUP_JS, 'utf8');
  const live = fs.readFileSync(LIVE_JS, 'utf8');

  // ---- 3. 接线 ----
  ok(/showPointerContextMenu\(changesContextMenu\(\)/.test(live),
    '实时侧复用 mockup.js 的 changesContextMenu()（改一处即两侧一致）');
  // ---- 4. 权威留痕 ----
  for (const id of ['ChangesView.Revert', 'Diff.ShowDiff', 'Git.FileActions', 'Vcs.ShowTabbedFileHistory']) {
    ok(mockup.includes(id), `注释里保留了权威 id：${id}`);
  }

  const browser = await chromium.launch({ executablePath: CHROME, headless: true });
  try {
    const page = await browser.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.goto(PAGE);
    await page.waitForSelector('body[data-typography-preview="ready"]');

    // ---- 1. 渲染顺序 ----
    const items = await page.locator('.changes-context > .menu-item').allTextContents();
    const labels = items.map(text => text.replace(/\s+/g, ' ').trim());
    ok(labels.length === EXPECTED.length,
      `菜单项数量为 ${EXPECTED.length}（实际 ${labels.length}：${labels.join(' / ')}）`);
    for (let i = 0; i < EXPECTED.length; i += 1) {
      ok((labels[i] || '').includes(EXPECTED[i]), `第 ${i + 1} 项是「${EXPECTED[i]}」（实际「${labels[i] || '—'}」）`);
    }
    // ---- 2. 分组：三组之间各一个分隔符 ----
    const groups = await page.evaluate(() => {
      const menu = document.querySelector('.changes-context');
      const out = [[]];
      for (const node of menu.children) {
        if (node.classList.contains('menu-separator')) out.push([]);
        else if (node.classList.contains('menu-item')) out[out.length - 1].push(node.textContent.replace(/\s+/g, ' ').trim());
      }
      return out;
    });
    ok(groups.length === 3 && groups.every(group => group.length >= 1),
      `菜单分为 3 组（实际 ${groups.length} 组：${JSON.stringify(groups)}）`);
    ok(errors.length === 0, `页面无脚本错误（${errors.join('; ') || '无'}）`);
  } finally {
    await browser.close();
  }

  console.log(fail === 0 ? 'PASS: Changes 右键菜单顺序检查通过。' : `FAIL: ${fail} 项未通过。`);
  process.exitCode = fail === 0 ? 0 : 1;
})();
