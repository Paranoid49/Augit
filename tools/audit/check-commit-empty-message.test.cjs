// 「空提交信息走确认、确认后照常提交」的自动化检查（第 135 轮新增）。
//
// 权威（采集见 docs/nui-behavior/12-commit-changes.md）：
//   platform/vcs-impl/src/com/intellij/vcs/commit/SingleChangeListCommitWorkflowHandler.kt:117-122
//     checkCommit = super.checkCommit(...) && (
//       getCommitMessage().isNotEmpty() || ui.confirmCommitWithEmptyMessage())
//   ⇒ 空提交信息**不是阻断**，而是弹确认；确认后照常提交。
//   文案：VcsBundle.properties:36-37（title `No Commit Message` / text `Add a summary of ...`）
//         与 :35 的 `action.commit.anyway.text`（"… Anyway"）。
//
// 覆盖三件事：
//   1. 视觉稿行为：点击「提交」（信息为空）→ 出现确认层，且**不再**出现旧的硬拦报错；
//   2. 两条出路：取消 → 关闭并把焦点退回信息栏；仍然提交 → 链接照常放行（本页 case 走到 href）；
//   3. 接线：`live-data.js` 的空信息分支不再"设错误后 return"，而是调用共享确认助手。
// 用法：node tools/audit/check-commit-empty-message.test.cjs [playwright路径]
const fs = require('node:fs');
const path = require('node:path');
const PW = process.argv[2] || '/root/.npm/_npx/e41f203b7505f1fb/node_modules/playwright';
const { chromium } = require(PW);

const ROOT = '/mnt/d/github/Augit';
const MOCKUP_JS = path.join(ROOT, 'web/src/mockup.js');
const LIVE_JS = path.join(ROOT, 'web/src/live-data.js');
const PAGE = 'file://' + path.join(ROOT, 'docs/ux-mockups/commit-changes.html');
const CHROME = process.env.AUGIT_CHROME || '/root/.cache/ms-playwright/chromium-1234/chrome-linux64/chrome';

let fail = 0;
const ok = (cond, msg) => { console.log((cond ? 'OK   ' : 'FAIL ') + msg); if (!cond) fail += 1; };
const sel = '[data-commit-empty-message]';

(async () => {
  const mockup = fs.readFileSync(MOCKUP_JS, 'utf8');
  const live = fs.readFileSync(LIVE_JS, 'utf8');

  // ---- 3. 接线：两处都不能再"硬拦" ----
  ok(/function confirmCommitWithEmptyMessage\(/.test(mockup),
    'mockup.js 提供共享的 confirmCommitWithEmptyMessage()');
  ok(!/showError\("提交信息不能为空。"\)/.test(mockup),
    'mockup.js 的共享绑定不再用"提交信息不能为空。"硬拦');
  ok(/confirmCommitWithEmptyMessage\(\s*\(\)\s*=>\s*\{\s*void commitSelectedChanges\(andPush, null, true\);/.test(live),
    'live-data.js 空信息分支改为弹确认并可在确认后继续');
  ok(!/next: "填写提交信息后重试。"/.test(live),
    'live-data.js 不再把空信息当作"重试"类错误');
  // 提交动作的可用性：权威 `enabled = hasDiffs() && !isExecuting()`，hasDiffs 数的是**已勾选**项
  // （CommitChangeListDialog.java:602-604,616-618）。原实现只做了一半（busy），
  // 把空闲禁用态寄存在 `dataset.idleDisabled` 里等别处设置，而别处没人设置。
  ok(/hasIncluded/.test(live) && /busy \|\| !hasIncluded/.test(live),
    'live-data.js 的提交动作可用性按"已勾选项 + 非进行中"计算');
  // 只看"有没有使用"：注释里提到这个旧变通是允许的（它记录了为什么删掉）。
  ok(!/dataset\.idleDisabled\s*(=|===|!==)/.test(live),
    'live-data.js 不再依赖"寄存空闲禁用态"的 idleDisabled 变通');

  const browser = await chromium.launch({ executablePath: CHROME, headless: true });
  try {
    const page = await browser.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.goto(PAGE);
    await page.waitForSelector('body[data-typography-preview="ready"]');

    // ---- 1. 空信息点击「提交」→ 出现确认层，且没有旧的硬拦报错 ----
    const submit = page.locator('.commit-actions > .primary-button').first();
    ok(await submit.count() === 1, '视觉稿里存在提交动作');
    ok((await page.locator('.commit-message-box textarea').inputValue()).trim() === '',
      '该场景的提交信息为空（前置条件）');
    await submit.click();
    await page.waitForSelector(sel, { timeout: 3000 });
    const title = (await page.locator(`${sel} .dialog-header span`).first().textContent() || '').trim();
    const body = (await page.locator(`${sel} .dialog-body`).textContent() || '').trim();
    ok(title === '无提交信息', `确认层标题为「无提交信息」（实际 ${title}）`);
    ok(/填写改动摘要/.test(body), `确认层正文与权威语义一致（实际 ${body}）`);
    ok(await page.locator(`${sel} [data-commit-empty-anyway]`).count() === 1, '确认层有"仍然提交"主按钮');
    ok(await page.locator(`${sel} [data-commit-empty-cancel]`).count() === 1, '确认层有"取消"次要按钮');
    ok(!/提交信息不能为空。/.test(await page.locator('.commit-feedback').textContent() || ''),
      '不再显示旧的硬拦报错');

    // ---- 2a. 取消 → 关闭并回到信息栏 ----
    await page.locator(`${sel} [data-commit-empty-cancel]`).click();
    await page.waitForSelector(sel, { state: 'detached' });
    ok(await page.locator(sel).count() === 0, '取消后确认层关闭');
    ok(await page.evaluate(() => document.activeElement
      && document.activeElement.classList.contains('message-field')), '取消后焦点回到提交信息栏');

    // ---- 2b. 仍然提交 → 放行（本页的提交动作是链接，应离开当前页面）----
    const href = await submit.getAttribute('href');
    await submit.click();
    await page.waitForSelector(sel, { timeout: 3000 });
    await Promise.all([
      page.waitForURL(/operation-result\.html/, { timeout: 5000 }).catch(() => null),
      page.locator(`${sel} [data-commit-empty-anyway]`).click(),
    ]);
    ok(/operation-result\.html$/.test(new URL(page.url()).pathname),
      `确认后提交照常放行（期望进入 ${href}，实际 ${page.url()}）`);

    ok(errors.length === 0, `页面无脚本错误（${errors.join('; ') || '无'}）`);
  } finally {
    await browser.close();
  }

  console.log(fail === 0 ? 'PASS: 空提交信息确认检查通过。' : `FAIL: ${fail} 项未通过。`);
  process.exitCode = fail === 0 ? 0 : 1;
})();
