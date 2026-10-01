// 验证提交图及列表局部交互，使用已有浏览器，不启动服务器。
// 用法：node tools/verify-ux-commit-graph.cjs <Playwright 路径> <浏览器路径> <证据目录>
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs/promises');
const { pathToFileURL } = require('node:url');

async function main() {
  const [modulePath, executablePath, outputPath] = process.argv.slice(2);
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.launch({ executablePath, headless: true });
  let passed = 0;
  try {
    await fs.mkdir(outputPath, { recursive: true });
    const page = await browser.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    for (const theme of ['light', 'dark']) {
      for (const width of [1024, 1645]) {
        for (const variant of ['', 'wide', 'filtered', 'page']) {
          const label = `${theme}-${width}-${variant || 'merge'}`;
          await page.setViewportSize({ width, height: 760 });
          const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/git-history-graph.html'));
          url.searchParams.set('theme', theme);
          url.searchParams.set('graph', variant);
          await page.goto(url.href);
          await page.evaluate(async () => { await document.fonts.ready; await new Promise(requestAnimationFrame); });
          const layout = await page.evaluate(() => {
            const list = document.querySelector('.commit-list');
            const rows = [...list.querySelectorAll('.commit-row')];
            window.graphEditorBefore = document.querySelector('.editor-area');
            window.graphListBefore = list;
            const bounds = element => { const r = element.getBoundingClientRect(); return { x: r.x, right: r.right, width: r.width }; };
            return { viewport: list.clientWidth, extent: list.scrollWidth, graph: rows[0].querySelector('svg').viewBox.baseVal.width,
              bodyOverflow: document.documentElement.scrollWidth > innerWidth,
              rows: rows.map(row => ({ row: bounds(row), graph: bounds(row.querySelector('svg')),
                subject: bounds(row.querySelector('.commit-subject')), author: bounds(row.querySelector('.commit-author')),
                date: bounds(row.querySelector('.commit-date')) })) };
          });
          assert.equal(layout.bodyOverflow, false, `${label}：整个窗口不应横向滚动。`);
          for (const row of layout.rows) {
            assert.ok(row.subject.x >= row.graph.right - 0.1, `${label}：提交图遮盖标题。`);
            assert.ok(row.subject.width >= 119.9, `${label}：标题可读宽度不足。`);
            assert.ok(row.author.width >= 12 && row.date.width >= 28, `${label}：元数据不可辨认。`);
            assert.ok(row.date.right <= row.row.right, `${label}：日期越界。`);
          }
          if (variant === 'wide') {
            // 多轨宽度按权威 `GraphCommitCellUtil.getGraphWidth` 复算：
            // `floor(列数 × 16 × 行高 ÷ 22) + floor(2 × 行高 ÷ 22)`
            // （`PaintParameters.scaleWithRowHeight`：基准行高 22，轨宽 16、图形文字间距 2）。
            // 不再钉住旧实现的常量 205px（那是"按固定 16px 轨距"的产物）。
            const expectedWide = await page.evaluate(() => {
              const rowHeight = Number.parseFloat(
                getComputedStyle(document.documentElement).getPropertyValue('--augit-history-row-height')) || 26;
              return Math.floor(12 * 16 * rowHeight / 22) + Math.floor(2 * rowHeight / 22);
            });
            assert.equal(layout.graph, expectedWide, `${label}：多轨图形区宽度不符合权威缩放公式。`);
            assert.ok(layout.graph > 205, `${label}：行高 26 下多轨宽度应比固定轨距的 205px 更宽。`);
            if (width === 1024) assert.ok(layout.extent > layout.viewport, `${label}：缺少横向滚动。`);
          }
          if (variant === 'filtered' || variant === 'page') {
            assert.ok(await page.evaluate(() => {
              const graph = buildCommitGraph([{ hash: 'merge', parents: ['main', 'hidden'] }, { hash: 'main', parents: [] }]);
              const solid = graph.rows[0].segments.find(segment => !segment[5]);
              const missing = graph.rows[0].segments.find(segment => segment[5]);
              return missing && missing[2] !== solid[2] && missing[3] < 1;
            }), `${label}：缺失父关系不能被可见父关系遮挡。`);
          }
          await page.locator('.commit-row').first().click();
          const firstTitle = await page.locator('.commit-row .commit-subject').first().textContent();
          assert.equal(await page.locator('.commit-detail h3').textContent(), firstTitle);
          const list = page.locator('.commit-list');
          await list.press('ArrowDown');
          const secondTitle = await page.locator('.commit-row .commit-subject').nth(1).textContent();
          assert.equal(await page.locator('.commit-detail h3').textContent(), secondTitle);
          assert.match(await page.locator('.changed-files').textContent(), /0 个文件/);
          const detailIdentity = await page.evaluate(() => {
            window.graphDetailBefore = document.querySelector('.commit-detail h3');
            return true;
          });
          assert.ok(detailIdentity);
          await page.locator('.commit-row').nth(1).click();
          assert.ok(await page.evaluate(() => graphDetailBefore === document.querySelector('.commit-detail h3')),
            `${label}：相同选择重复创建详情。`);
          if (!variant) {
            await list.press('End');
            assert.match(await page.locator('.changed-files').textContent(), /35 个文件/);
            await list.press('Home');
            assert.match(await page.locator('.changed-files').textContent(), /0 个文件/);
          }
          await page.evaluate(() => { const list = document.querySelector('.commit-list'); list.scrollLeft = list.scrollWidth; });
          const offset = await list.evaluate(element => element.scrollLeft);
          await list.press('ArrowUp');
          assert.equal(await list.evaluate(element => element.scrollLeft), offset, `${label}：选择提交重置横向偏移。`);
          assert.equal(page.url(), url.href, `${label}：单击提交跳转整页。`);
          assert.ok(await page.evaluate(() => graphEditorBefore === document.querySelector('.editor-area')
            && graphListBefore === document.querySelector('.commit-list')), `${label}：选择重建了周边区域。`);
          await list.evaluate(element => { element.scrollLeft = 0; });
          if (width === 1024) await page.screenshot({ path: path.join(outputPath, `${label}.png`) });
          assert.deepEqual(errors, []);
          passed++;
        }
      }
    }

    // ---- 提交图几何：按实际行高等比缩放，并在设备空间对齐到奇数 ----
    // 权威：`PaintParameters.java:9-15,17-43`（基准行高 22；节点半径 4、轨宽 16、普通线宽 1.5、
    // 图形文字间距 2，全部按 `实际行高 ÷ 22` 缩放）、`SimpleGraphCellPainter.kt:79-99`
    // （`PaintUtil.alignToInt(…, FLOOR, ODD)` 在设备空间取整到奇数）、`HeadNodePainter.kt:22-29`
    // （HEAD 是三个同心实心圆）、`GraphCommitCellUtil.kt:32`（图形区宽度公式）。
    // 期望值在这里**独立复算**（不调用页面里的 `commitGraphSvg` 一族函数），否则只是断言实现自己。
    const expectedGeometry = (rowHeight, dpr, columns) => {
      const ratio = rowHeight / 22;
      const align = (value, odd) => {
        let device = Math.floor(value * dpr);
        if (odd && device % 2 === 0) device -= 1;
        return device / dpr;
      };
      const nodeDiameter = align(8 * ratio, true);
      const headOuterDiameter = align(12 * ratio, true);
      return {
        elementWidth: align(16 * ratio, true),
        elementCenter: align(8 * ratio, false),
        rowCenter: align(rowHeight / 2, true),
        nodeRadius: nodeDiameter / 2,
        // 权威用**取整后的半径**定位、用**取整后的奇数直径**作尺寸（`SimpleGraphCellPainter.kt:172-178`）。
        nodeOffset: align(nodeDiameter / 2, false) - nodeDiameter / 2,
        headRadius: headOuterDiameter / 2,
        headOffset: align(headOuterDiameter / 2, false) - headOuterDiameter / 2,
        headDelta: align(2 * ratio, false),
        lineThickness: Math.max(align(1.5 * ratio, true), 1 / dpr),
        width: Math.floor(columns * 16 * ratio) + Math.floor(2 * ratio),
      };
    };
    const near = (a, b) => Math.abs(Number(a) - Number(b)) < 0.011;
    const readGeometry = () => page.evaluate(() => {
      const dpr = window.devicePixelRatio || 1;
      const rowHeight = Number.parseFloat(
        getComputedStyle(document.documentElement).getPropertyValue('--augit-history-row-height')) || 26;
      const list = document.querySelector('.commit-list.commit-list-graph');
      const svgs = [...document.querySelectorAll('.commit-graph-svg')];
      const normal = svgs.find(svg => svg.querySelectorAll('circle').length === 1);
      const head = svgs.find(svg => svg.querySelectorAll('circle').length === 3);
      const circle = normal.querySelector('circle');
      const line = normal.querySelector('.graph-line');
      const headCircles = head ? [...head.querySelectorAll('circle')] : null;
      return {
        dpr,
        rowHeight,
        columns: Number(normal.dataset.graphColumns),
        viewBoxWidth: normal.viewBox.baseVal.width,
        viewBoxHeight: normal.viewBox.baseVal.height,
        boxWidth: normal.getBoundingClientRect().width,
        cssWidth: Number.parseFloat(getComputedStyle(list).getPropertyValue('--augit-graph-width')),
        nodeRadius: Number(circle.getAttribute('r')),
        nodeCx: Number(circle.getAttribute('cx')),
        nodeCy: Number(circle.getAttribute('cy')),
        strokeWidth: Number.parseFloat(getComputedStyle(line).strokeWidth),
        vectorEffect: getComputedStyle(line).vectorEffect,
        headRadii: headCircles ? headCircles.map(node => Number(node.getAttribute('r'))) : null,
        headCenter: headCircles ? [Number(headCircles[0].getAttribute('cx')), Number(headCircles[0].getAttribute('cy'))] : null,
        headMiddleFill: headCircles ? headCircles[1].getAttribute('fill') : null,
        headOuterFill: headCircles ? headCircles[0].getAttribute('fill') : null,
      };
    });
    const graphUrl = pathToFileURL(path.resolve(__dirname, '../docs/ux-mockups/git-history-graph.html'));
    const measured = [];
    for (const uiSize of [null, 20]) {
      const url = new URL(graphUrl.href);
      url.searchParams.set('theme', 'dark');
      if (uiSize) url.searchParams.set('ui-size', String(uiSize));
      await page.setViewportSize({ width: 1645, height: 760 });
      await page.goto(url.href);
      await page.evaluate(async () => { await document.fonts.ready; await new Promise(requestAnimationFrame); });
      const actual = await readGeometry();
      const expected = expectedGeometry(actual.rowHeight, actual.dpr, actual.columns);
      measured.push({ uiSize, actual, expected });
      const label = `提交图几何 字号${uiSize || 13}`;
      assert.ok(near(actual.viewBoxWidth, expected.width)
        && near(actual.cssWidth, expected.width)
        && near(actual.boxWidth, expected.width),
      `${label}：图形区宽度不是权威公式值（实测 ${actual.viewBoxWidth}/${actual.cssWidth}/${actual.boxWidth}，期望 ${expected.width}）。`);
      assert.ok(near(actual.nodeCx, expected.elementCenter + expected.nodeOffset)
        && near(actual.nodeCy, expected.rowCenter + expected.nodeOffset)
        && near(actual.nodeRadius, expected.nodeRadius),
      `${label}：普通节点几何不符（实测 ${JSON.stringify([actual.nodeCx, actual.nodeCy, actual.nodeRadius])}）。`);
      assert.ok(near(actual.strokeWidth, expected.lineThickness),
        `${label}：连线线宽不是按行高缩放并在设备空间对齐的值（实测 ${actual.strokeWidth}，期望 ${expected.lineThickness}）。`);
      // `vector-effect: non-scaling-stroke` 会把线宽钉死在视口坐标系，与"设备空间对齐再换算回用户空间"相反。
      assert.equal(actual.vectorEffect, 'none', `${label}：连线仍带 non-scaling-stroke。`);
      assert.ok(actual.headRadii && actual.headRadii.length === 3,
        `${label}：HEAD 节点不是一个外圆 + 中圆 + 内圆的三同心实心圆。`);
      assert.ok(near(actual.headRadii[0], expected.headRadius)
        && near(actual.headRadii[1], expected.headRadius - expected.headDelta)
        && near(actual.headRadii[2], expected.headRadius - 2 * expected.headDelta),
      `${label}：HEAD 三同心圆半径不符（实测 ${JSON.stringify(actual.headRadii)}）。`);
      assert.ok(near(actual.headCenter[0], expected.elementCenter + expected.headOffset)
        && near(actual.headCenter[1], expected.rowCenter + expected.headOffset),
      `${label}：HEAD 圆心与轨中心关系不符（实测 ${JSON.stringify(actual.headCenter)}）。`);
      // 中圆必须取所在行的实际背景（`HeadNodePainter.kt:47-54` 用 `commitStyle.background` 填中圆）。
      assert.match(actual.headMiddleFill, /augit-row-background/, `${label}：HEAD 中圆没有取所在行背景。`);
      assert.ok(actual.headOuterFill !== actual.headMiddleFill, `${label}：HEAD 外圆与中圆同色，画不出环。`);
      passed++;
    }
    // 缩放：字号 13 → 20 时行高变大，轨距、节点半径与线宽都必须跟着变大（不能停在一组固定值上）。
    const [base, larger] = measured;
    assert.ok(larger.actual.rowHeight > base.actual.rowHeight,
      `字号 20 的行高没有变大（${base.actual.rowHeight} → ${larger.actual.rowHeight}）。`);
    assert.ok(larger.expected.elementWidth > base.expected.elementWidth
      && larger.actual.nodeRadius > base.actual.nodeRadius
      && larger.actual.strokeWidth >= base.actual.strokeWidth,
    `提交图几何未随行高等比缩放（${JSON.stringify([base.actual, larger.actual])}）。`);
    // 设备空间对齐：对齐后的值乘以设备像素比必须是**奇数**（`PaintUtil.alignToInt(…, FLOOR, ODD)`），
    // 且对齐后仍是设备整数（不能是 16.5 个设备像素这样的半像素）。
    for (const item of measured) {
      for (const [label, userValue] of [['轨距', item.expected.elementWidth], ['节点直径', item.expected.nodeRadius * 2]]) {
        const device = userValue * item.actual.dpr;
        assert.ok(Math.abs(device - Math.round(device)) < 1e-6,
          `${label}不是设备整数：${device}`);
        assert.equal(Math.round(device) % 2, 1, `${label}的设备值不是奇数：${device}`);
      }
    }
    console.log(`INFO 提交图几何=${JSON.stringify(measured)}`);

    console.log(`通过 ${passed} 组提交图、窄栏和连续选择场景。`);
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
