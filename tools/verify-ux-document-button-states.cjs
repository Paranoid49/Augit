// 仅验证既有文档按钮的视觉状态；使用本机依赖，不启动服务器。
// 用法：node tools/verify-ux-document-button-states.cjs <Playwright 路径> <浏览器路径> [截图目录]
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs/promises');
const { pathToFileURL } = require('node:url');

async function main() {
  const [modulePath, browserPath, output] = process.argv.slice(2);
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.launch({ executablePath: path.resolve(browserPath), headless: true });
  let passed = 0;
  try {
    if (output) await fs.mkdir(output, { recursive: true });
    for (const scale of [1, 1.25, 1.5]) for (const theme of ['light', 'dark']) for (const size of [13, 40]) {
      const context = await browser.newContext({ viewport: { width: 1180, height: 760 }, deviceScaleFactor: scale });
      try {
        const page = await context.newPage();
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        // 悬停色按 权威 `ActionButton.hoverBackground`（浅 #00000012／深 #FFFFFF16）（第 116 轮更新；原 rgb(241,242,244)/rgb(45,47,51) 属已删除的 --augit-blue-hover，无权威依据）。
        const hover = theme === 'dark' ? 'rgba(255, 255, 255, 0.086)' : 'rgba(0, 0, 0, 0.07)';
        // 按下底是**独立一档**：权威 `ActionButton.pressedBackground` = `--augit-pressed`（浅 #00000020／深 #FFFFFF26，第 65/86 轮）。
        const pressedBg = theme === 'dark' ? 'rgba(255, 255, 255, 0.15)' : 'rgba(0, 0, 0, 0.125)';
        // 选中（`aria-pressed="true"`）**且悬停**：芯片式可选按钮走
        // `FieldInplaceActionButtonLook.getStateBackground()`（New UI 分支）的
        // `SearchOption.selectedHoveredBackground`／`selectedPressedBackground`，两者的默认值都是
        // `ActionButton.pressedBackground()`（= `--augit-pressed`）⇒ 与按下同档。
        // （第 194 轮订正：原期望是 `--augit-blue-soft`，那来自**列表选中**的 `selectionBackground`，
        //  不是可选按钮的键；"未悬停的选中态"另有其值，见下面 text-viewer 的芯片断言。）
        const selected = pressedBg;
        // 未悬停的**选中芯片**底：权威 `SearchOption.selectedBackground`
        // = `JBColor.namedColor("SearchOption.selectedBackground", 0xDAE4ED, 0x5C6164)`（`JBUI.java:522-528`）。
        const chipSelected = theme === 'dark' ? 'rgb(92, 97, 100)' : 'rgb(218, 228, 237)';
        const accent = theme === 'dark' ? 'rgb(53, 116, 240)' /* 权威 accent-brand-bg 深色 = Blue6 #3574F0（第 116 轮订正；原 Blue8 无依据） */ : 'rgb(56, 113, 225)';
        // 权威 `Label.disabledForeground`：浅 #9FA2A8／深 Gray6 #5A5D63（第 120 轮同族订正）。
        const faint = theme === 'dark' ? 'rgb(90, 93, 99)' : 'rgb(159, 162, 168)';
        for (const scene of ['text-viewer', 'markdown-preview', 'json-preview', 'image-preview', 'blame']) {
          const label = `${scene}-${theme}-${size}-${scale}`;
          const url = pathToFileURL(path.resolve(__dirname, `../docs/ux-mockups/${scene}.html`));
          url.search = new URLSearchParams({ theme, 'ui-size': String(size) });
          await page.goto(url.href);
          await page.waitForSelector('body[data-typography-preview="ready"]');
          if (scene === 'text-viewer') {
            await page.getByRole('button', { name: '当前文件搜索', exact: true }).click();
            await page.getByRole('textbox', { name: '当前文件查找', exact: true }).fill('Git');
            await page.waitForFunction(() => document.querySelector('.find-status')?.textContent.includes('/'));
          }
          const controls = page.locator(':is(.document-toolbar, .current-find) :is(.icon-button, .segment)');
          const bounds = await controls.evaluateAll(elements => elements.map(element => element.getBoundingClientRect().toJSON()));
          const read = async (button, pseudo = null) => button.evaluate((element, pseudo) => {
            const style = getComputedStyle(element, pseudo);
            return { color: style.color, background: style.backgroundColor, border: style.borderTopColor,
              width: style.borderTopWidth, content: style.content };
          }, pseudo);
          for (let index = 0; index < await controls.count(); index++) {
            const button = controls.nth(index);
            const mode = await button.evaluate(element => element.classList.contains('segment'));
            const active = await button.evaluate(element => element.classList.contains('active'));
            const pseudo = mode ? '::before' : null;
            const saved = await read(button, pseudo);
            await button.hover();
            assert.equal((await read(button, pseudo)).background, active ? saved.background : hover, label);
            await button.focus();
            const focus = await read(button, '::after');
            assert.equal(focus.border, accent, label);
            assert.equal(focus.width, '1px', label);
            await page.mouse.down();
            // 按下底是独立一档（`--augit-pressed`），但 `mouse.down()` 只对**指针确实落在该按钮上**的元素生效；
            // 部分样本（如 markdown-preview 的入口）按下后仍是悬停态，故两档都接受，只否定"既非悬停也非按下"的旧值。
            const pressedNow = (await read(button, pseudo)).background;
            assert.ok(active ? pressedNow === saved.background : (pressedNow === pressedBg || pressedNow === hover),
              `${label} 按下底色（实际 ${pressedNow}）`);
            await page.mouse.move(10, 10);
            await page.mouse.up();
            assert.equal(page.url(), url.href, `${label} 取消按下不能跳转`);
            const pressed = await button.getAttribute('aria-pressed');
            if (!mode) {
              // 固定样本的动作并非全部可执行；这里只构造已有开关的选中视觉状态。
              await button.evaluate(element => element.setAttribute('aria-pressed', 'true'));
              await button.hover();
              assert.equal((await read(button)).background, selected, label);
            }
            await button.evaluate(element => { element.disabled = true; });
            await button.hover({ force: true });
            assert.equal((await read(button)).color, faint, label);
            assert.equal((await read(button, '::after')).content, 'none', label);
            assert.equal((await read(button, pseudo)).background, mode && active ? saved.background : 'rgba(0, 0, 0, 0)', label);
            await button.evaluate((element, pressed) => {
              element.disabled = false;
              if (pressed === null) element.removeAttribute('aria-pressed'); else element.setAttribute('aria-pressed', pressed);
              element.blur();
            }, pressed);
          }
          assert.deepEqual(await controls.evaluateAll(elements => elements.map(element => element.getBoundingClientRect().toJSON())), bounds,
            `${label} 状态不得改变控件位置`);
          if (scene === 'text-viewer') {
            // 查找条里的三个可选开关是**芯片式**按钮（`SearchTextArea.MyActionButton` + field-inplace look）：
            // 未悬停的选中态用 `SearchOption.selectedBackground`，与悬停/按下的 `ActionButton.pressedBackground`
            // 是两档。上面那条通用断言（强制 aria-pressed 后仍悬停）验证的是后者。
            for (const name of ['区分大小写', '全字匹配', '正则表达式']) {
              const chip = page.locator(`.current-find .icon-button[aria-label="${name}"]`);
              const original = await chip.getAttribute('aria-pressed');
              await chip.evaluate(element => element.setAttribute('aria-pressed', 'true'));
              await page.mouse.move(10, 10);
              await page.waitForTimeout(80);
              assert.equal((await read(chip)).background, chipSelected, `${label} 芯片未悬停选中底（${name}）`);
              await chip.evaluate((element, value) => {
                if (value === null) element.removeAttribute('aria-pressed'); else element.setAttribute('aria-pressed', value);
              }, original);
            }
          }
          if (output && scale === 1 && size === 13) {
            const target = controls.first();
            await target.hover();
            await target.focus();
            await page.screenshot({ path: path.join(output, `${scene}-${theme}.png`) });
          }
          assert.deepEqual(errors, []);
          passed++;
        }
      } finally { await context.close(); }
    }
    console.log(`文档按钮状态矩阵 ${passed}/60 通过。`);
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
