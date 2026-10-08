// 主网格、工具窗活动归属和分隔条矩阵；复用 live-shell 的宿主夹具与清理流程。
module.exports = async function verifyLayout({ browser, port, stubHost, stubData, data, check }) {
  const geometry = (page) => page.evaluate(() => {
    const box = (selector) => {
      const element = document.querySelector(selector);
      if (!element) return null;
      const rect = element.getBoundingClientRect();
      return { x: rect.x, y: rect.y, width: rect.width, height: rect.height, right: rect.right, bottom: rect.bottom };
    };
    return { main: box('.app-main'), rail: box('.tool-rail'), side: box('.side-tool'),
      editor: box('.workspace'), bottom: box('.bottom-tool'), status: box('.statusbar'),
      direct: document.querySelector('.bottom-tool')?.parentElement.matches('.app-main'),
      overflow: document.documentElement.scrollWidth > innerWidth || document.documentElement.scrollHeight > innerHeight };
  });
  const near = (a, b) => Math.abs(a - b) < 1;
  const active = (page) => page.locator('.rail-button.active').evaluateAll((buttons) => buttons.map((button) => button.getAttribute('aria-label')));
  const spanning = (frame) => frame.direct && frame.side && frame.bottom && near(frame.bottom.x, frame.side.x)
    && near(frame.bottom.right, frame.editor.right);
  if (!process.argv.includes('--layout-details-only')) for (const scale of [1, 1.25, 1.5]) for (const theme of ['light', 'dark']) for (const size of [13, 40]) {
    const context = await browser.newContext({ deviceScaleFactor: scale });
    try {
      await context.addInitScript(stubHost);
      await context.addInitScript(stubData, data);
      const page = await context.newPage();
      const errors = [];
      page.on('pageerror', (error) => errors.push(error.message));
      for (const viewport of [{ width: 1024, height: 640 }, { width: 1180, height: 760 }, { width: 1645, height: 900 }]) {
        const label = `${theme}/${size}/${scale}/${viewport.width}`;
        await page.setViewportSize(viewport);
        await page.goto(`http://127.0.0.1:${port}/index.html?scene=main-project&theme=${theme}&ui-size=${size}&open=docs/notes.txt`);
        await page.waitForFunction(() => window.__augitReady && window.__augitGitReady && window.__augitHistoryReady
          && document.body.dataset.typographyPreview === 'ready');
        let frame = await geometry(page);
        check(`${label} 底部工具窗直接属于主内容区`, frame.direct === true);
        check(`${label} 底部跨项目树和编辑器`, near(frame.bottom.x, frame.side.x) && near(frame.bottom.right, frame.editor.right));
        check(`${label} 项目树与编辑器共同位于上行`, near(frame.side.y, frame.editor.y) && near(frame.side.bottom, frame.editor.bottom)
          && near(frame.side.bottom + 4, frame.bottom.y));
        check(`${label} rail 跨两行且底部贴主内容区`, near(frame.rail.bottom, frame.main.bottom) && near(frame.bottom.bottom, frame.main.bottom)
          && near(frame.bottom.bottom, frame.status.y) && !frame.overflow);
        const visibleRail = await page.locator('.rail-button.visible:not(.active)').first().evaluate((element) => ({
          background: getComputedStyle(element).backgroundColor,
          shadow: getComputedStyle(element).boxShadow,
        }));
        check(`${label} 可见未激活工具入口使用 PUSHED 按下态`,
          visibleRail.background === (theme === 'dark' ? 'rgba(255, 255, 255, 0.15)' : 'rgba(0, 0, 0, 0.125)')
            && visibleRail.shadow.includes('0px 0px 0px'));
        const railSteps = await page.locator('.rail-button').evaluateAll((buttons) => {
          const tops = buttons.map((button) => Math.round(button.getBoundingClientRect().top)).sort((a, b) => a - b);
          return tops.slice(1).map((top, index) => top - tops[index]).filter((step) => step < 50);
        });
        check(`${label} 工具轨同组按钮按 32px 中心步长排列: ${JSON.stringify(railSteps)}`,
          railSteps.length > 0 && railSteps.every((step) => step === 32));

        // 按下时不跳动；向右变宽，向左变窄，操作不替换正文节点。
        await page.evaluate(() => window.__layoutEditor = document.querySelector('.editor-content'));
        const point = { x: frame.side.right + 2, y: frame.side.y + 70 };
        await page.mouse.move(point.x, point.y);
        await page.mouse.down();
        check(`${label} 侧栏按下不跳动`, near((await geometry(page)).side.width, frame.side.width));
        await page.mouse.move(point.x + 60, point.y, { steps: 4 });
        const wide = await geometry(page);
        check(`${label} 侧栏可以扩大到默认范围之外`, wide.side.width > 360 && wide.editor.width >= 319);
        await page.mouse.move(point.x - 20, point.y, { steps: 4 });
        const small = await geometry(page);
        check(`${label} 侧栏可以反向缩小`, small.side.width < wide.side.width && near(small.side.width, frame.side.width - 20));
        await page.mouse.up();
        check(`${label} 侧栏保存真实显示宽度`, await page.evaluate(() => Math.abs(window.__settingsWritten.projectPanelWidth
          - document.querySelector('.side-tool').getBoundingClientRect().width) < 1));

        // 从项目树下方的分隔条命中底部，证明它不局限于编辑器列。
        frame = await geometry(page);
        const bottomPoint = { x: frame.side.x + 40, y: frame.bottom.y - 2 };
        await page.mouse.move(bottomPoint.x, bottomPoint.y);
        await page.mouse.down();
        check(`${label} 底部按下不跳动`, near((await geometry(page)).bottom.height, frame.bottom.height));
        await page.mouse.move(bottomPoint.x, bottomPoint.y - 40, { steps: 4 });
        const tall = await geometry(page);
        check(`${label} 向上变高或停在受限边界且两块上方面板同步变化`, tall.bottom.height >= frame.bottom.height
          && (size === 13 ? tall.bottom.height > frame.bottom.height : true)
          && tall.side.height <= frame.side.height && near(tall.side.height, tall.editor.height));
        await page.mouse.move(bottomPoint.x, bottomPoint.y + 10, { steps: 4 });
        const short = await geometry(page);
        check(`${label} 底部到边界后可反向缩小或保持字号最小高度`, short.bottom.height <= tall.bottom.height
          && (size === 13 ? short.bottom.height < tall.bottom.height : true));
        await page.mouse.up();
        const savedHeight = await page.evaluate(() => window.__settingsWritten.bottomPanelHeight);
        check(`${label} 底部变化后保存真实显示高度，无变化不写回`, savedHeight === undefined
          ? near(frame.bottom.height, short.bottom.height) && near(frame.bottom.height, tall.bottom.height)
          : near(savedHeight, short.bottom.height));

        // 活动窗口按内容焦点判定：同屏可见的另一个工具窗保持 visible，失焦列表为灰色。
        await page.locator('.side-content.tree').focus();
        check(`${label} 项目获焦后只有项目 active`, (await active(page)).join(',') === '项目');
        await page.locator('.rail-button[aria-label="Git 历史"]').focus();
        check(`${label} rail 键盘焦点不激活工具窗`, (await active(page)).join(',') === '项目');
        const row = page.locator('.commit-row').first();
        await row.click();
        check(`${label} 日志列表获焦后只有历史 active`, (await active(page)).join(',') === 'Git 历史');
        const selection = await row.evaluate((element) => ({ hash: element.dataset.hash, color: getComputedStyle(element).backgroundColor }));
        await page.locator('.history-search input').first().focus();
        const inactive = await row.evaluate((element) => ({ selected: element.classList.contains('selected'), hash: element.dataset.hash,
          color: getComputedStyle(element).backgroundColor }));
        check(`${label} 同工具窗输入框获焦后保留选择并转灰`, inactive.selected && inactive.hash === selection.hash
          && inactive.color !== selection.color && inactive.color === (theme === 'dark' ? 'rgb(67, 69, 74)' : 'rgb(233, 234, 238)'));

        // 折叠/重显保留原正文节点；两个区域可以独立折叠与恢复。
        await page.locator('.rail-button[aria-label="Git 历史"]').click();
        check(`${label} 折叠历史保留正文节点`, await page.evaluate(() => !document.querySelector('.bottom-tool')
          && document.querySelector('.editor-content') === window.__layoutEditor));
        await page.locator('.side-content.tree').focus();
        await page.locator('.rail-button[aria-label="项目"]').click();
        check(`${label} 两个工具区可以同时折叠`, !(await geometry(page)).side && !(await geometry(page)).bottom);
        await page.locator('.rail-button[aria-label="提交"]').click();
        check(`${label} 折叠项目后可以直接打开提交`, (await page.locator('.side-tool .tool-header').innerText()).includes('提交')
          && !(await geometry(page)).bottom);
        await page.locator('.rail-button[aria-label="Git 历史"]').click();
        frame = await geometry(page);
        check(`${label} 重显后底部仍跨两列并恢复高度`, frame.side && frame.bottom && near(frame.bottom.height, short.bottom.height)
          && near(frame.bottom.x, frame.side.x));
        await page.locator('.rail-button[aria-label="终端"]').click();
        check(`${label} 终端与历史互斥且完整跨栏`, await page.locator('.bottom-tool').count() === 1
          && await page.locator('.bottom-tool.terminal-tool').count() === 1 && near((await geometry(page)).bottom.x, frame.side.x));
        check(`${label} 过程中没有页面异常`, errors.length === 0);
      }
    } finally { await context.close(); }
  }

  // 取消条件使用真实捕获状态；不允许以不存在的测试钩子回退成“已结束”。
  const context = await browser.newContext({ viewport: { width: 1180, height: 760 } });
  try {
    await context.addInitScript(stubHost);
    await context.addInitScript(stubData, data);
    const page = await context.newPage();
    for (const reason of ['Escape', 'pointercancel', 'lostpointercapture', 'hide', 'disabled', 'resize', 'switch']) {
      await page.goto(`http://127.0.0.1:${port}/index.html?scene=main-project`);
      await page.waitForFunction(() => window.__augitReady && document.body.dataset.typographyPreview === 'ready');
      const frame = await geometry(page);
      const x = frame.side.x + 40, y = frame.bottom.y - 2;
      await page.mouse.move(x, y);
      await page.mouse.down();
      await page.mouse.move(x, y - 10);
      check(`${reason} 前置条件确实正在拖动`, await page.evaluate(() => window.__augitPanelDragActive()));
      if (reason === 'Escape') await page.keyboard.press('Escape');
      else if (reason === 'pointercancel' || reason === 'lostpointercapture') {
        await page.evaluate((type) => document.body.dispatchEvent(new PointerEvent(type, { pointerId: 1, bubbles: true })), reason);
      } else if (reason === 'hide' || reason === 'disabled') {
        await page.evaluate((type) => {
          const panel = document.querySelector('.bottom-tool');
          if (type === 'hide') panel.hidden = true;
          else panel.setAttribute('aria-disabled', 'true');
        }, reason);
      } else if (reason === 'resize') await page.setViewportSize({ width: 1190, height: 770 });
      else await page.locator('.rail-button[aria-label="终端"]').evaluate((element) => element.click());
      await page.waitForFunction(() => !window.__augitPanelDragActive());
      check(`${reason} 释放拖动与样式`, await page.evaluate(() => !document.body.style.cursor && !document.body.style.userSelect));
      await page.mouse.up();
    }
  } finally { await context.close(); }

  // 边界外反向、松开最后一帧与重载恢复；用真实 DOM 和保存请求互相印证。
  const restored = await browser.newContext({ viewport: { width: 1645, height: 900 } });
  try {
    await restored.addInitScript(stubHost);
    await restored.addInitScript(stubData, data);
    const page = await restored.newPage();
    await page.goto(`http://127.0.0.1:${port}/index.html?scene=main-project&open=docs/notes.txt`);
    await page.waitForFunction(() => window.__augitReady && document.body.dataset.typographyPreview === 'ready');
    for (const kind of ['side', 'bottom']) {
      const frame = await geometry(page);
      const x = kind === 'side' ? frame.side.right + 2 : frame.side.x + 40;
      const y = kind === 'side' ? frame.side.y + 70 : frame.bottom.y - 2;
      const sizeOf = (value) => kind === 'side' ? value.side.width : value.bottom.height;
      await page.mouse.move(x, y); await page.mouse.down();
      await page.mouse.move(kind === 'side' ? x + 2000 : x, kind === 'side' ? y : y - 2000);
      const limit = sizeOf(await geometry(page));
      await page.mouse.move(kind === 'side' ? x + 1980 : x, kind === 'side' ? y : y - 1980);
      check(`${kind} 越过上限后反向立即移动`, near(sizeOf(await geometry(page)), limit - 20));
      // 不发送最后的 pointermove，只有 pointerup 的新坐标，模拟浏览器合并事件。
      await page.evaluate(({ kind, x, y }) => document.body.dispatchEvent(new PointerEvent('pointerup', {
        pointerId: 1, bubbles: true, clientX: kind === 'side' ? x + 1965 : x, clientY: kind === 'side' ? y : y - 1965,
      })), { kind, x, y });
      await page.mouse.up();
      check(`${kind} 松开最终坐标计入显示尺寸`, near(sizeOf(await geometry(page)), limit - 35));
    }
    const saved = await page.evaluate(() => ({ ...window.__settingsWritten }));
    await restored.addInitScript((settings) => { window.__settingsOverride = settings; }, saved);
    await page.reload();
    await page.waitForFunction(() => window.__augitReady && document.body.dataset.typographyPreview === 'ready');
    let frame = await geometry(page);
    check('重启数据路径恢复侧栏和底部尺寸', near(frame.side.width, saved.projectPanelWidth) && near(frame.bottom.height, saved.bottomPanelHeight));
    await page.evaluate(() => {
      window.__resizeRecords = [];
      addEventListener('resize', () => window.__resizeRecords.push({ main: document.querySelector('.app-main').clientWidth,
        side: document.documentElement.style.getPropertyValue('--augit-side-width') }));
    });
    await page.setViewportSize({ width: 1024, height: 640 });
    await page.waitForFunction(() => document.querySelector('.workspace').getBoundingClientRect().width >= 319, null, { timeout: 3000 }).catch(async (error) => {
      console.log('INFO 窄窗恢复=' + JSON.stringify({ frame: await geometry(page), records: await page.evaluate(() => window.__resizeRecords), settings: await page.evaluate(() => window.__augitLive.settings) }));
      throw error;
    });
    frame = await geometry(page);
    check('窗口变窄时映射保存尺寸而不覆盖首选值', frame.editor.width >= 319 && await page.evaluate((settings) =>
      window.__augitLive.settings.projectPanelWidth === settings.projectPanelWidth && window.__augitLive.settings.bottomPanelHeight === settings.bottomPanelHeight, saved));
    await page.setViewportSize({ width: 1645, height: 900 });
    await page.waitForFunction((settings) => Math.abs(document.querySelector('.side-tool').getBoundingClientRect().width - settings.projectPanelWidth) < 1, saved);
    frame = await geometry(page);
    check('窗口恢复后恢复用户首选尺寸', near(frame.side.width, saved.projectPanelWidth) && near(frame.bottom.height, saved.bottomPanelHeight));

    // 负向证据：同一几何判定能抓住只占编辑器列的错误布局。
    const badGrid = await page.addStyleTag({ content: '.app-main > .bottom-tool { grid-column: 3 !important; }' });
    check('负向：错误底部单列布局被跨栏判定拒绝', !spanning(await geometry(page)));
    await badGrid.evaluate((element) => element.remove());
    check('移除负向注入后正常跨栏判定通过', spanning(await geometry(page)));
    await page.locator('.commit-row').first().click();
    await page.locator('.history-search input').first().focus();
    const badSelection = await page.addStyleTag({ content: '.commit-row.selected.inactive { background: var(--augit-blue-soft) !important; }' });
    check('负向：失焦选中仍蓝色被灰色判定拒绝', await page.locator('.commit-row.selected').first().evaluate((element) =>
      getComputedStyle(element).backgroundColor !== 'rgb(67, 69, 74)'));
    await badSelection.evaluate((element) => element.remove());
    await page.locator('.rail-button[aria-label="项目"]').evaluate((element) => element.classList.add('active'));
    check('负向：同时两个 active 被单活动窗口判定拒绝', (await active(page)).length !== 1);
  } finally { await restored.close(); }

  // 负向拖动：只在网络应答内倒置增量，不修改工作区文件。
  const negative = await browser.newContext({ viewport: { width: 1180, height: 760 } });
  try {
    await negative.addInitScript(stubHost); await negative.addInitScript(stubData, data);
    await negative.route('**/src/live-data.js', async (route) => {
      const response = await route.fetch();
      const source = await response.text();
      const wrong = source.replace("const grown = panelDrag.kind === 'side' ? delta : -delta;", "const grown = panelDrag.kind === 'side' ? -delta : delta;");
      check('负向拖动确实注入错误方向', wrong !== source);
      await route.fulfill({ response, body: wrong });
    });
    const page = await negative.newPage();
    await page.goto(`http://127.0.0.1:${port}/index.html?scene=main-project`);
    await page.waitForFunction(() => window.__augitReady && document.body.dataset.typographyPreview === 'ready');
    const frame = await geometry(page);
    await page.mouse.move(frame.side.right + 2, frame.side.y + 70); await page.mouse.down();
    await page.mouse.move(frame.side.right + 22, frame.side.y + 70); await page.mouse.up();
    check('负向：侧栏反向实现被扩大判定拒绝', (await geometry(page)).side.width <= frame.side.width);
  } finally { await negative.close(); }
};
