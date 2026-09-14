// 验证 Changes 到工作区 Diff 的连续交互；只使用本地视觉稿，不启动服务器或执行 Git。
const assert = require("node:assert/strict");
const fs = require("node:fs/promises");
const path = require("node:path");
const { pathToFileURL } = require("node:url");

async function verifyHover(browser, outputPath) {
  let passed = 0;
  for (const theme of ["light", "dark"]) {
    for (const dpi of [96, 120, 144]) {
      const context = await browser.newContext({ viewport: { width: 1180, height: 760 }, deviceScaleFactor: dpi / 96 });
      try {
        const page = await context.newPage();
        // 验证只消费仓库内资源，避免外部 CDN 的状态影响结果或收尾。
        await page.route(/^https?:\/\//, route => route.abort());
        const url = pathToFileURL(path.resolve(__dirname, "../docs/ux-mockups/commit-changes.html"));
        url.searchParams.set("theme", theme);
        await page.goto(url.href);
        await page.waitForSelector("body[data-typography-preview='ready']");
        const rows = page.locator(".change-file-row");
        const first = rows.nth(0), second = rows.nth(1);
        await first.click();
        await page.locator(".message-field").fill("fix: 悬停保留草稿");
        const snapshot = () => page.evaluate(() => ({
          selected: document.querySelector(".changes-list .selected")?.id,
          checks: [...document.querySelectorAll(".changes-list .fake-check")].map(check => check.getAttribute("aria-checked")),
          draft: document.querySelector(".message-field").value,
          focus: document.activeElement?.outerHTML,
          scroll: document.querySelector(".changes-list").scrollTop,
          tabs: document.querySelector(".editor-tabs").innerHTML,
          content: document.querySelector(".editor-content").innerHTML,
        }));
        const before = await snapshot();
        const color = row => row.evaluate(element => getComputedStyle(element).backgroundColor);
        const hover = theme === "dark" ? "rgb(45, 47, 51)" : "rgb(241, 242, 244)";
        const selectedColor = await color(first);
        await second.hover();
        assert.equal(await color(second), hover);
        assert.equal(await color(first), selectedColor);
        assert.deepEqual(await snapshot(), before, "悬停不改选择、勾选、草稿、焦点、滚动或正文。");
        await first.hover();
        assert.equal(await color(first), selectedColor, "悬停不能覆盖失焦选中态。");
        assert.notEqual(await color(second), hover, "移出后恢复原背景。");
        await page.locator(".changes-list").focus();
        const focusedColor = await color(first);
        await second.hover();
        await first.hover();
        assert.equal(await color(first), focusedColor, "悬停不能覆盖焦点选中态。");
        const group = page.locator(".check-group-row").first();
        await group.hover();
        assert.equal(await color(group), hover, "分组行也提供悬停反馈。");
        await second.hover();
        if (dpi === 96) await page.screenshot({ path: path.join(outputPath, `changes-hover-${theme}.png`) });
        passed++;
      } finally { await context.close(); }
    }
  }
  return passed;
}

async function main() {
  const [modulePath, browserPath, outputPath] = process.argv.slice(2);
  assert.ok(modulePath && browserPath && outputPath, "请提供现有依赖、浏览器和证据目录。");
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.launch({ executablePath: path.resolve(browserPath), headless: true });
  const page = await browser.newPage({ viewport: { width: 1645, height: 900 }, deviceScaleFactor: 1 });
  const url = pathToFileURL(path.resolve(__dirname, "../docs/ux-mockups/commit-changes.html"));
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  try {
    await fs.mkdir(outputPath, { recursive: true });
    const hoverCases = await verifyHover(browser, outputPath);
    // 视觉稿引用的图标脚本来自外部 CDN；验证不依赖它，避免网络不可用时阻塞 DOMContentLoaded。
    await page.goto(url.href, { waitUntil: "commit", timeout: 10000 });
    await page.waitForSelector("body[data-typography-preview='ready']");

    const rows = page.locator(".change-file-row");
    assert.equal(await rows.count(), 42, "Changes 与 Unversioned Files 应共用文件行结构。");
    const initialUrl = page.url();
    const first = rows.nth(0);
    const second = rows.nth(1);
    const third = rows.nth(2);

    await first.click();
    assert.equal(page.url(), initialUrl, "单击文件行不能跳转页面。");
    assert.equal(await first.evaluate(row => row.classList.contains("selected")), true);
    assert.equal(await page.locator("[data-workspace-diff-tab]").count(), 0, "单击不能创建 Diff 标签。");

    await second.click();
    assert.equal(await second.evaluate(row => row.classList.contains("selected")), true);
    assert.equal(await page.locator("[data-workspace-diff-tab]").count(), 0, "改选不能创建 Diff 标签。");

    const secondCheck = second.locator(".fake-check");
    await secondCheck.click();
    assert.equal(await secondCheck.getAttribute("aria-checked"), "false", "复选框应独立切换提交集合。");
    assert.equal(await second.evaluate(row => row.classList.contains("selected")), true, "勾选不能改变文件行选择。");

    await page.keyboard.press("Enter");
    await page.waitForSelector("[data-workspace-diff-tab]");
    assert.equal(page.url(), initialUrl, "Enter 打开 Diff 不应离开当前视觉稿页面。");
    assert.equal(await page.locator(".diff-layout").count(), 1);
    const secondName = await second.getAttribute("data-file");
    assert.match(await page.locator(".reference-filebar").getAttribute("title"), new RegExp(secondName.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")));

    await third.click();
    assert.equal(await page.locator("[data-workspace-diff-tab]").count(), 1, "连续改选只能保留一个工作区比较标签。");
    const thirdName = await third.getAttribute("data-file");
    assert.match(await page.locator(".reference-filebar").getAttribute("title"), new RegExp(thirdName.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")));

    await first.dblclick();
    assert.equal(await page.locator("[data-workspace-diff-tab]").count(), 1, "双击不能创建第二个工作区比较标签。");
    assert.match(await page.locator(".reference-filebar").getAttribute("title"), /app\.manifest/);

    await page.locator("[data-workspace-diff-tab] .tab-close").click();
    assert.equal(await page.locator("[data-workspace-diff-tab]").count(), 0, "关闭比较应只移除比较视图。");
    assert.equal(await page.locator(".editor-content > .markdown-document").isVisible(), true, "关闭比较后应恢复原普通文档。");
    assert.equal(await first.evaluate(row => row.classList.contains("selected")), true, "关闭比较不能清空 Changes 选择。");

    const firstCheck = first.locator(".fake-check");
    const selectedBeforeCheck = await first.evaluate(row => row.classList.contains("selected"));
    await firstCheck.click();
    assert.equal(await first.evaluate(row => row.classList.contains("selected")), selectedBeforeCheck, "复选不能改变行选择。");
    assert.equal(await page.locator(".changes-list").count(), 1);
    assert.deepEqual(errors, []);
    await page.screenshot({ path: path.join(outputPath, "commit-workflow-final.png"), fullPage: false });
    await fs.writeFile(path.join(outputPath, "commit-workflow.json"), JSON.stringify({ passed: true, steps: 8, hoverCases }, null, 2) + "\n");
    process.stdout.write(`Changes → Diff 连续交互及悬停 ${hoverCases}/6 检查通过。\n`);
  } finally {
    await page.close();
    await browser.close();
  }
}

main().catch(error => { process.stderr.write(`${error.stack}\n`); process.exitCode = 1; });
