// 主框架按钮与终端标题的静态状态、布局验证；使用已有浏览器，不启动 Shell 或服务器。
// 用法：node tools/verify-ux-frame-buttons.cjs <Playwright 路径> <浏览器路径> [截图目录]
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
    for (const state of ['ready', 'loading']) for (const scale of [1, 1.25, 1.5]) for (const theme of ['light', 'dark']) for (const size of [13, 40]) {
      const context = await browser.newContext({ viewport: { width: 1180, height: 760 }, deviceScaleFactor: scale });
      try {
        const page = await context.newPage();
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/terminal.html'));
        url.search = new URLSearchParams({ theme, 'ui-size': String(size), 'terminal-state': state });
        await page.goto(url.href);
        await page.waitForSelector('body[data-typography-preview="ready"]');
        assert.equal(await page.locator('.terminal-view').getAttribute('aria-busy'), String(state === 'loading'));
        // 用户 2026-10-18 裁决：可见标签固定 Local，当前 Shell 只在 title/aria；加载中追加 Starting…。
        assert.equal(await page.locator('.terminal-session').textContent(),
          `Local${state === 'loading' ? ' · Starting…' : ''}`);
        if (state === 'loading') assert.equal(await page.locator('.terminal-view').textContent(), '');
        // 悬停色按 权威 `ActionButton.hoverBackground`（浅 #00000012／深 #FFFFFF16）（第 116 轮更新；原 rgb(241,242,244)/rgb(45,47,51) 属已删除的 --augit-blue-hover，无权威依据）。
        const hover = theme === 'dark' ? 'rgba(255, 255, 255, 0.09)' /* 权威深色 hover = #FFFFFF17；旧 #FFFFFF16 是过期值 */ : 'rgba(0, 0, 0, 0.07)';
        const pushed = theme === 'dark' ? 'rgba(255, 255, 255, 0.16)' : 'rgba(0, 0, 0, 0.125)';
        const accent = theme === 'dark' ? 'rgb(53, 116, 240)' /* 权威 accent-brand-bg 深色 = Blue6 #3574F0（第 116 轮订正；原 Blue8 无依据） */ : 'rgb(56, 113, 225)';
        // 深色 `--augit-panel` 是 #191A1C（design-system §6.2 的 panel）；#1E1F22 是 editor-bg，
        // 旧期望把两者混用，激活工具轨边框因此假失败。
        const panel = theme === 'dark' ? 'rgb(25, 26, 28)' : 'rgb(255, 255, 255)';
        // 禁用态底取 `--augit-panel-muted`：浅 = Islands `*.disabledBackground` = `dialog-bg` = `gray-160`
        // `#F7F8F9`（第 131 轮订正；原 `#F7F8FA` = **未被采用**的 expUI_light `Gray13`，更早的 #F5F8FE 无依据）；
        // 深 = expUI_dark `*.disabledBackground` = `Gray2` `#2B2D30`（原 #25262A 无依据）。
        // 深色 `--augit-panel-muted` 是 #212326；#2B2D30 是 popup-bg，不是禁用底。
        const mutedPanel = theme === 'dark' ? 'rgb(33, 35, 38)' : 'rgb(247, 248, 249)';
        const faint = theme === 'dark' ? 'rgb(76, 79, 86)' /* 权威深色 faint = #4C4F56；旧 #5A5D63 是另一个灰阶 */ : 'rgb(159, 162, 168)';
        const controls = page.locator('.rail-button, .side-tool:has(.side-content.tree) > .tool-header .icon-button, .terminal-header .icon-button');
        assert.equal(await controls.count(), 12);
        const bounds = await controls.evaluateAll(elements => elements.map(element => element.getBoundingClientRect().toJSON()));
        const read = (button, pseudo = null) => button.evaluate((element, pseudo) => {
          const style = getComputedStyle(element, pseudo);
          return { color: style.color, background: style.backgroundColor, border: style.borderTopColor, content: style.content };
        }, pseudo);
        // 指针是否停在该控件上。取"未悬停档"前必须**逐个实测**，不能假设把指针挪到某个固定坐标
        // 就一定离开了控件（控件会随 `.hover()` 自动滚入视口）。
        const isHovered = (button) => button.evaluate(element => element.matches(':hover'));
        const restingReads = [];
        for (let index = 0; index < await controls.count(); index++) {
          const button = controls.nth(index);
          const active = await button.evaluate(element => element.classList.contains('active'));
          const visible = await button.evaluate(element => element.classList.contains('visible'));
          const close = await button.evaluate(element => element.classList.contains('terminal-session-close'));
          const rail = await button.evaluate(element => element.classList.contains('rail-button'));
          // 未悬停档：把指针移到**由该控件矩形算出的死区**（视口中心，落在控件外时用它），
          // 并实测确认指针确实不在控件上，然后才取样。
          // 不能固定写 `page.mouse.move(0, 0)`：控件会随 `.hover()` 滚入视口，角落坐标未必在控件外，
          // 那样这一档会静默退化成"悬停档"，负向验证也就测不出回归（第 314 轮实测踩到过）。
          await page.evaluate(() => {
            if (document.activeElement && typeof document.activeElement.blur === 'function') document.activeElement.blur();
          });
          const away = await button.evaluate((element) => {
            const rect = element.getBoundingClientRect();
            const inside = (x, y) => x >= rect.left && x <= rect.right && y >= rect.top && y <= rect.bottom;
            const centre = { x: Math.round(window.innerWidth / 2), y: Math.round(window.innerHeight / 2) };
            if (!inside(centre.x, centre.y)) return centre;
            const below = { x: centre.x, y: Math.min(window.innerHeight - 1, Math.round(rect.bottom) + 40) };
            if (!inside(below.x, below.y)) return below;
            return { x: Math.max(1, Math.round(rect.left) - 40), y: below.y };
          });
          await page.mouse.move(away.x, away.y);
          const hovering = await isHovered(button);
          // 工具轨与终端关闭段的底色都画在 ::before 上；标题动作按钮的底色在元素自身。
          const resting = await read(button, (close || rail) ? '::before' : null);
          // 权威 `SquareStripeButtonLook.getState()/getBackgroundColor()`：
          // `SquareStripeButton.isFocused()` 的实现是 `toolWindow.isActive`（`SquareStripeButton.kt:120`；
          // `ToolWindowImpl.isActive()` = 窗口可见、有装饰器且 `activeToolWindowId == id`，`ToolWindowImpl.kt:470-472`），
          // 即"该工具窗口当前是激活窗口"，与按钮自身的 DOM 键盘焦点无关。
          // 因此窗口激活的入口在任何指针状态下都取 `ToolWindow.Button.selectedBackground`（白字 + accent），
          // 可见但未激活的入口取 `PUSHED`，悬停只改变仍未激活且不可见的入口。
          // 终端会话关闭按钮的 `::before` 是它自己的提示底（恒定不透明），沿用下面的既有期望，不参与本档判定。
          if (rail && active && !hovering) {
            assert.equal(resting.background, accent,
              `窗口激活入口在未悬停时应为 accent（${theme}/${size}/scale ${scale}，index ${index}）`);
            assert.equal(resting.color, 'rgb(255, 255, 255)');
          }
          if (!close && !hovering) {
            assert.equal(resting.background, rail ? (active ? accent : visible ? pushed : 'rgba(0, 0, 0, 0)') : 'rgba(0, 0, 0, 0)',
              `未悬停档底色（${theme}/${size}/scale ${scale}，index ${index}）`);
          }
          restingReads.push({ index, rail, active, hovering, background: resting.background });
          await button.hover();
          // 悬停档：激活入口的底色**不换**（`getState()` 在 `isFocused()` 处提前返回 `SELECTED`，`popState` 不参与）；
          // 非激活入口才给 `--augit-rail-hover`。第 314 轮按权威订正。
          assert.equal((await read(button, (close || rail) ? '::before' : null)).background,
            rail ? (active ? accent : visible ? pushed : hover) : hover);
          await button.focus();
          assert.equal((await read(button, '::after')).border, accent);
          if (active) assert.equal((await read(button, '::before')).border, panel);
          await button.evaluate((element, rail) => { if (rail) element.setAttribute('aria-disabled', 'true'); else element.disabled = true; }, rail);
          assert.equal((await read(button)).color, faint);
          assert.equal((await read(button, (close || rail) ? '::before' : null)).background, close ? mutedPanel : 'rgba(0, 0, 0, 0)');
          assert.equal((await read(button, '::after')).content, 'none');
          await button.evaluate(element => { element.removeAttribute('aria-disabled'); element.disabled = false; element.blur(); });
        }
        // 断言不能是空的：必须至少有一个"激活且指针不在其上"的轨道入口被真正判过，
        // 否则"未悬停档"会因为取样条件从未成立而静默通过（负向验证也测不出回归）。
        assert.ok(restingReads.some(entry => entry.rail && entry.active && !entry.hovering),
          `未悬停档一个"激活轨道入口"样本都没取到：${JSON.stringify(restingReads)}`);
        // `hover()` 会把目标滚入视口；这里比较的是布局是否移动，不是滚动位置。
        // 不先复位滚动会拿"hover 后的滚动态"去比"初始态"，得到整页 x/y 平移的假失败。
        await page.evaluate(() => {
          window.scrollTo(0, 0);
          for (const element of document.querySelectorAll('*')) {
            if (element.scrollLeft) element.scrollLeft = 0;
            if (element.scrollTop) element.scrollTop = 0;
          }
        });
        assert.deepEqual(await controls.evaluateAll(elements => elements.map(element => element.getBoundingClientRect().toJSON())), bounds);
        // 界面英文审计后终端头部 aria 标签为英文；多标签后还包含新建/Shell 菜单入口。
        assert.deepEqual(await page.locator('.terminal-header button').evaluateAll(elements => elements.map(e => e.getAttribute('aria-label'))),
          ['Local terminal: Windows PowerShell', 'Close Terminal', 'New Terminal', 'Terminal Shells', 'More Actions', 'Hide Terminal']);
        for (const width of [1024, 1180]) {
          await page.setViewportSize({ width, height: 760 });
          await page.evaluate(() => measureTerminalHeaders());
          const layout = await page.locator('.terminal-header').evaluate(header => {
            const rect = element => { const r = element.getBoundingClientRect(); return { left: r.left, top: r.top, right: r.right, bottom: r.bottom, height: r.height }; };
            const style = getComputedStyle(header);
            const iconHit = Number.parseFloat(style.getPropertyValue('--augit-icon-hit')) || 28;
            // 大字号命中区 91px 时，Local 标签按文字高度取 49px；普通字号回落到命中区。
            const sessionHeight = Number.parseFloat(style.getPropertyValue('--terminal-session-height')) || iconHit;
            return {
              header: rect(header),
              iconHit,
              sessionHeight,
              session: rect(header.querySelector('.terminal-tab .terminal-session')),
              close: rect(header.querySelector('.terminal-tab .terminal-session-close')),
              controls: [...header.querySelectorAll('button')].map(rect),
            };
          });
          for (const control of layout.controls) {
            assert.ok(control.left >= layout.header.left - .01 && control.right <= layout.header.right + .01,
              `终端控件超出标题栏：${theme}/${size}/${width}`);
            assert.ok(control.bottom <= layout.header.bottom + .01,
              `终端控件超出标题栏高度：${theme}/${size}/${width}`);
          }
          // Local 标签与关闭段使用标签高度；新建/Shell 菜单/更多/隐藏在 20–91px 命中区内。
          assert.equal(layout.session.height, layout.sessionHeight, `终端 Local 标签高度：${theme}/${size}`);
          assert.equal(layout.close.height, layout.sessionHeight, `终端关闭段高度：${theme}/${size}`);
        }
        if (output && scale === 1) {
          await page.getByRole('link', { name: 'Terminal', exact: true }).focus();
          await page.screenshot({ path: path.join(output, `terminal-${state}-${theme}-${size}.png`) });
        }
        assert.deepEqual(errors, []);
        passed++;
      } finally { await context.close(); }
    }
    console.log(`主框架按钮与终端标题就绪/加载矩阵 ${passed}/24 通过。`);
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
