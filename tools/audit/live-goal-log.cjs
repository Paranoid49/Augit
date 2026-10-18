// 本批次验证 Git 日志列表的键盘回归：ArrowDown 的局部刷新一致性、
// PageDown／PageUp 按日志视口行数分页、失焦后的 inactive 语义，以及一次
// 日志重绘后键盘选择仍在；页面由 finally 关闭。
//
// 本模块只承载断言，不修改实现。实测（2026-10-11，本机 Windows 11 x64 + 无头 Chromium）：
// `web/src/mockup.js` 的 `.commit-list` keydown 已同时处理 ArrowUp／ArrowDown／Home／End／
// PageUp／PageDown（分页步长 = `floor(list.clientHeight / 行高)`），失焦 inactive 由
// `bindSelectionFocus()` 写入，因此本模块 7 条断言全部通过。PageDown／PageUp 的行数口径是
// 与被测实现独立算出的：本模块自己读 `list.clientHeight / 行高` 得到期望步长，不复用实现变量。
const assert = require('node:assert/strict');

// 初始化脚本会被 `addInitScript` 序列化后在页面里重建：凡是进页面执行的函数
// 都不能引用本模块作用域的变量（下面用内联数值 + `new Function` 解决）。
const FIXTURE = {
  // 至少 20 个可见提交才谈得上"可测分页"：25 个足够铺满视口并留出多页边界，
  // 又小于分页大小 100（桩的 page slice），因此一次渲染就全部可见、不触发加载下一页。
  total: 25,
  pageSize: 100,
};

/** 造一组确定性的提交：短哈希、不同主题与作者、父子链按列表顺序串联。 */
function buildHistoryFixture(total) {
  const commits = Array.from({ length: total }, (_, index) => {
    const shortHash = 'goal' + String(index).padStart(3, '0');
    const fullHash = 'goal-' + String(index).padStart(3, '0') + '-full';
    return {
      hash: shortHash,
      fullHash,
      subject: 'goal-log: 桩提交 ' + String(index + 1).padStart(2, '0'),
      author: 'goal-author-' + (index % 3),
      authorEmail: 'goal' + (index % 3) + '@example.invalid',
      committerName: 'goal-author-' + (index % 3),
      committerEmail: 'goal' + (index % 3) + '@example.invalid',
      date: '2026/9/' + String(25 - index).padStart(2, '0') + ' 10:00',
      references: index === 0 ? ['HEAD', 'dsh'] : [],
      parents: index + 1 < total ? ['goal' + String(index + 1).padStart(3, '0')] : [],
    };
  });
  return { commits };
}

/**
 * 造一个 init hook：在页面里把 git/history（分页）与 git/commit（详情）
 * 扩到夹具规模，并暴露 `window.__goalLogFixture` 供断言读取。
 *
 * 返回的要交给 `openScene(query, initialize)`。**必须完全自包含**：调用方的
 * `openScene` 只做 `page.addInitScript(initialize)`，既不会转发第三个参数，
 * 也不会让你的模块作用域进入页面 —— `addInitScript` 把函数序列化后在页面里重建，
 * 引用闭包变量只会在页面里抛 `ReferenceError`，日志静默退回空列表（本模块实测踩过）。
 * 因此这里用 `new Function` 把两个数字**内联进函数体**，函数对外部零依赖。
 */
function historyStubInitializer(config) {
  const { total, pageSize } = config;
  // 本模块的存在意义就是"至少 20 个可见提交"，夹具不达标时直接报错，
  // 不要退回少数几行让分页断言平凡为真。
  if (!Number.isInteger(total) || total < 20) throw new Error('日志夹具提交数必须 ≥ 20，当前 ' + total);
  // 宿主桩已经存在（由 live-shell.spec.cjs 在 context 上注入），这里只在它外面
  // 包一层：不是重写宿主，而是让日志通道返回至少 20 条真实形状的提交。
  return new Function('return function extendHistoryStub() {'
    + 'const TOTAL = ' + total + ';'
    + 'const PAGE_SIZE = ' + pageSize + ';'
    + 'const original = window.__hostStub;'
    + 'const commits = Array.from({ length: TOTAL }, (_, index) => {'
    + 'const shortHash = "goal" + String(index).padStart(3, "0");'
    + 'const fullHash = "goal-" + String(index).padStart(3, "0") + "-full";'
    + 'return {'
    + 'hash: shortHash,'
    + 'fullHash,'
    + 'subject: "goal-log: 桩提交 " + String(index + 1).padStart(2, "0"),'
    + 'author: "goal-author-" + (index % 3),'
    + 'authorEmail: "goal" + (index % 3) + "@example.invalid",'
    + 'committerName: "goal-author-" + (index % 3),'
    + 'committerEmail: "goal" + (index % 3) + "@example.invalid",'
    + 'date: "2026/9/" + String(25 - index).padStart(2, "0") + " 10:00",'
    + 'references: index === 0 ? ["HEAD", "dsh"] : [],'
    + 'parents: index + 1 < TOTAL ? ["goal" + String(index + 1).padStart(3, "0")] : [],'
    + '};'
    + '});'
    + 'window.__goalLogFixture = {'
    + 'total: TOTAL,'
    + 'commits,'
    + 'byFull: Object.fromEntries(commits.map((commit) => [commit.fullHash, commit])),'
    + 'byShort: Object.fromEntries(commits.map((commit) => [commit.hash, commit])),'
    + 'historyCalls: 0,'
    + '};'
    + 'window.__hostStub = async function (method, params) {'
    + 'if (method === "git/history") {'
    + 'const fixture = window.__goalLogFixture;'
    + 'fixture.historyCalls += 1;'
    // 与宿主同构：第 0 页不送 page（`historyFilterParams()`），缺省当第 0 页，
    // 按 PAGE_SIZE 切片并回报 hasNextPage。
    + 'const page = Math.max(0, Number(params && params.page) || 0);'
    + 'const start = page * PAGE_SIZE;'
    + 'return { available: true, isRepository: true, head: null,'
    + 'commits: fixture.commits.slice(start, start + PAGE_SIZE),'
    + 'hasNextPage: (page + 1) * PAGE_SIZE < fixture.commits.length };'
    + '}'
    + 'if (method === "git/commit") {'
    + 'const commit = window.__goalLogFixture.byFull[params && params.revision];'
    + 'if (!commit) return { available: false, reason: "unknown" };'
    + 'return { available: true, hash: commit.hash, fullHash: commit.fullHash,'
    + 'subject: commit.subject, author: commit.author, date: commit.date,'
    + 'body: "goal-log 详情正文：" + commit.fullHash + "。",'
    + 'files: [{ path: "src/App.cs", name: "App.cs", directory: "src", kind: "Modified", original: null }] };'
    + '}'
    + 'return original(method, params);'
    + '};'
    + '}')();
}

// 页面侧的固定选择器：只读日志列表（不是文件历史列表）。
// 注意 `.log-detail-panel` 与 `.log-list-panel` 是**兄弟**（都在 `.git-log` 下），
// 不是嵌套关系：把详情写成 `.log-list-panel .commit-detail` 会选到空集合。
const ROW_SELECTOR = '.log-list-panel .commit-row';
const LIST_SELECTOR = '.log-list-panel .commit-list';
const DETAIL_SELECTOR = '.log-detail-panel .commit-detail';
const DETAIL_HEADING_SELECTOR = '.log-detail-panel .commit-detail h3';

/** 读一次"当前选中提交"的全貌：DOM 选中态、状态哈希、详情内容、视口几何。 */
async function readLogState(page, fixture) {
  return page.evaluate(({ rowSelector, listSelector, detailSelector, fixtureTotal }) => {
    const list = document.querySelector(listSelector);
    const rows = [...document.querySelectorAll(rowSelector)];
    const active = document.activeElement;
    const selected = rows.find((row) => row.classList.contains('selected')) || null;
    const detail = document.querySelector(detailSelector);
    const detailHeading = detail ? detail.querySelector('h3') : null;
    const live = window.__augitLive || {};
    const rowHeight = rows.length ? rows[0].getBoundingClientRect().height : 0;
    return {
      rowCount: rows.length,
      hash: selected ? selected.dataset.hash : null,
      fullHash: selected ? selected.dataset.fullHash : null,
      ariaSelected: selected ? selected.getAttribute('aria-selected') : null,
      selectedCount: rows.filter((row) => row.getAttribute('aria-selected') === 'true').length,
      subject: selected ? selected.querySelector('.commit-subject').textContent.trim() : null,
      selectedClassCount: rows.filter((row) => row.classList.contains('selected')).length,
      stateHash: (live.historySelectedHash || null),
      stateMatchesRow: !!selected && (live.historySelectedHash || null) === selected.dataset.hash,
      detailSubject: detailHeading ? detailHeading.textContent.trim() : null,
      detailMeta: detail ? detail.textContent : '',
      commitDetailsRevision: (live.commitDetails && live.commitDetails.revision) || null,
      inactive: selected ? selected.classList.contains('inactive') : null,
      listHeight: list ? list.clientHeight : 0,
      listScrollTop: list ? list.scrollTop : 0,
      rowHeight,
      viewportRows: list && rowHeight ? Math.floor(list.clientHeight / rowHeight) : 0,
      activeElement: active ? (active.className || active.tagName) : null,
      activeInsideList: !!(list && active && list.contains(active)),
      fixtureTotal,
    };
  }, { rowSelector: ROW_SELECTOR, listSelector: LIST_SELECTOR, detailSelector: DETAIL_SELECTOR, fixtureTotal: fixture.commits.length });
}

/** 行选择在"按下按键 → 本地状态 → 详情"之间必须一致。 */
function assertRowConsistent(state, commit, label) {
  assert.ok(commit, label + '：期望选中的提交在夹具里');
  assert.equal(state.hash, commit.hash, label + '：选中行短哈希');
  assert.equal(state.fullHash, commit.fullHash, label + '：选中行完整哈希');
  assert.equal(state.ariaSelected, 'true', label + '：选中行 aria-selected');
  assert.equal(state.selectedCount, 1, label + '：只有一个 aria-selected=true');
  assert.equal(state.selectedClassCount, 1, label + '：只有一个 .selected');
  assert.equal(state.stateHash, commit.hash, label + '：live.historySelectedHash 与选中行一致');
  assert.equal(state.subject, commit.subject, label + '：选中行主题');
  assert.equal(state.detailSubject, commit.subject, label + '：提交详情与选中提交一致');
  assert.equal(state.commitDetailsRevision, commit.fullHash, label + '：详情状态按完整哈希记录');
}

/** 真实按键：先把焦点放到日志列表上（与用户点击列表后按方向键同一路径）。 */
async function pressOnLog(page, key) {
  await page.locator(LIST_SELECTOR).focus();
  await page.keyboard.press(key);
}

/**
 * 按键后等选择落到指定提交。
 *
 * 这是"按键真的被处理了"的判据：实现缺 PageDown／PageUp 分支时这里会超时，
 * 超时消息带上期望的哈希，避免把失败读成"详情没刷新"。等到了再读详情契约，
 * 就不会把选择没动误记成详情迟到。
 */
async function pressKeyAndWaitForSelection(page, key, commit) {
  await pressOnLog(page, key);
  try {
    await page.waitForFunction(
      (fullHash) => {
        const row = document.querySelector(`.log-list-panel .commit-row[data-full-hash="${fullHash}"]`);
        return !!row && row.getAttribute('aria-selected') === 'true';
      },
      commit.fullHash,
      { timeout: 15000 },
    );
  } catch {
    throw new Error(`按 ${key} 后选择没有移动到 ${commit.hash}（${commit.subject}）`);
  }
}

/** 读"列表里第几行被选中"（按夹具下标）。 */
function selectedIndex(state, fixture) {
  return state.hash ? fixture.commits.findIndex((commit) => commit.hash === state.hash) : -1;
}

/** 用真实点击选中某一行，作为边界用例的确定起点。 */
async function clickRow(page, fixture, index) {
  const commit = fixture.commits[index];
  await page.locator(`.log-list-panel .commit-row[data-full-hash="${commit.fullHash}"]`).click();
  await page.waitForFunction(
    (fullHash) => {
      const row = document.querySelector(`.log-list-panel .commit-row[data-full-hash="${fullHash}"]`);
      return !!row && row.getAttribute('aria-selected') === 'true';
    },
    commit.fullHash,
    { timeout: 10000 },
  );
  // 与用户"点一下日志再按方向键"同一焦点状态：把焦点交给列表容器，
  // 这样"持有焦点 ⇒ 非 inactive"的起点是确定的，不依赖点击子节点时的浏览器焦点归属。
  await page.locator(LIST_SELECTOR).focus();
  return commit;
}

/** 等一次"真实局部刷新"：调用已有的区域刷新入口，随后等详情按状态重建。 */
async function refreshLogRegion(page) {
  await page.evaluate(() => {
    if (typeof window.__augitRenderRegions !== 'function') throw new Error('缺少 __augitRenderRegions 区域刷新入口');
    window.__augitRenderRegions('bottomTool');
  });
  await page.waitForSelector(ROW_SELECTOR, { timeout: 10000 });
  await page.waitForSelector(DETAIL_SELECTOR, { timeout: 10000 });
}

/**
 * 等"选中提交 → 状态 → 详情"这条异步链落定。
 *
 * 提交详情是异步读取的（`loadCommitDetails` 先写 `live.commitDetails` 再填 DOM），
 * 只等 DOM 会读到半成品：详情栏可能还是上一行的正文。这里同时等三件事：
 * DOM 里被选中的是目标完整哈希、`live.commitDetails.revision` 是目标、详情 h3 是目标主题。
 */
async function waitForDetailContract(page, commit) {
  await page.waitForFunction(
    ({ fullHash, subject, headingSelector }) => {
      const row = document.querySelector(`.log-list-panel .commit-row[data-full-hash="${fullHash}"]`);
      const live = window.__augitLive || {};
      const heading = document.querySelector(headingSelector);
      return !!row
        && row.getAttribute('aria-selected') === 'true'
        && row.classList.contains('selected')
        && (live.historySelectedHash || null) === row.dataset.hash
        && !!live.commitDetails && live.commitDetails.revision === fullHash
        && !!heading && heading.textContent.trim() === subject;
    },
    { fullHash: commit.fullHash, subject: commit.subject, headingSelector: DETAIL_HEADING_SELECTOR },
    { timeout: 10000 },
  );
}

module.exports = async function verifyGoalLog({ openScene, check }) {
  const fixture = buildHistoryFixture(FIXTURE.total);
  const scene = await openScene('scene=git-history&theme=dark', historyStubInitializer(FIXTURE), FIXTURE);
  const { page } = scene;
  try {
    // ---- 0. 场景真实落地：日志列表至少 20 个可见提交 ----
    await page.waitForSelector(ROW_SELECTOR, { timeout: 15000 });
    const initial = await readLogState(page, fixture);
    assert.ok(initial.rowCount >= 20, `日志可见提交数 ${initial.rowCount} 必须 ≥ 20 才能覆盖分页`);
    assert.equal(initial.rowCount, Math.min(FIXTURE.total, FIXTURE.pageSize), '第一页提交全部渲染');
    assert.equal(initial.rowCount, initial.fixtureTotal, '渲染行数与夹具提交数一致');
    assert.ok(initial.viewportRows >= 2, `日志视口至少 2 行才可测分页，实测 ${initial.viewportRows}`);
    assert.ok(initial.listHeight > 0, '日志列表可滚动容器有真实高度');
    const fixtureState = await page.evaluate(() => ({
      calls: window.__goalLogFixture.historyCalls,
      total: window.__goalLogFixture.commits.length,
    }));
    assert.ok(fixtureState.calls >= 1, 'git/history 桩被真实调用');
    check(`扩展 git/history 桩到 ${FIXTURE.total} 个提交，日志列表渲染 ${initial.rowCount} 行（视口 ${initial.viewportRows} 行）`, true);

    // ---- 1. ArrowDown：局部刷新后选中 class / aria / 状态 / 详情都保留 ----
    await clickRow(page, fixture, 0);
    await waitForDetailContract(page, fixture.commits[0]);
    await pressOnLog(page, 'ArrowDown');
    const downCommit = fixture.commits[1];
    await waitForDetailContract(page, downCommit);
    const afterDown = await readLogState(page, fixture);
    assertRowConsistent(afterDown, downCommit, 'ArrowDown 后（刷新前）');
    assert.equal(selectedIndex(afterDown, fixture), 1, 'ArrowDown 只前进一行');

    // 真实重绘：走已有区域刷新入口（等价于一次日志局部刷新）。
    await refreshLogRegion(page);
    const refreshed = await readLogState(page, fixture);
    assertRowConsistent(refreshed, downCommit, 'ArrowDown 后局部刷新');
    assert.equal(selectedIndex(refreshed, fixture), 1, '局部刷新后选中仍在 ArrowDown 到达的行');
    assert.ok(refreshed.listScrollTop >= 0, '刷新后滚动位置是有效数值');
    check('ArrowDown 后局部刷新仍保留 selected/aria-selected/live.historySelectedHash 与详情', true);

    // ---- 2. 失焦：selected 语义变为 inactive，且选择不丢 ----
    await clickRow(page, fixture, 2);
    await waitForDetailContract(page, fixture.commits[2]);
    const beforeBlur = await readLogState(page, fixture);
    assert.equal(beforeBlur.inactive, false, '列表持有焦点时选中行不是 inactive');
    // 把焦点交给日志筛选栏的输入框（真实失焦路径，与用户点击输入框一致）。
    await page.locator('.log-list-panel .history-search input').focus();
    await page.waitForTimeout(50);
    const blurred = await readLogState(page, fixture);
    assert.equal(blurred.inactive, true, '失焦后选中行进入 inactive 语义');
    assert.equal(blurred.selectedCount, 1, '失焦不取消选择');
    assert.equal(blurred.selectedClassCount, 1, '失焦保留 .selected');
    assert.equal(blurred.hash, beforeBlur.hash, '失焦后选中的仍是同一行');
    assert.equal(blurred.stateHash, beforeBlur.stateHash, '失焦不清空 live.historySelectedHash');
    assert.equal(blurred.detailSubject, fixture.commits[2].subject, '失焦不丢失提交详情');
    check('失焦后 selected 进入 inactive 语义且选择与详情不丢失', true);

    // 失焦之后再来一次日志刷新/重绘：选中行、aria 与状态哈希都不能回到首行。
    // `inactive` 由 `bindSelectionFocus()` 按**刷新后**的焦点位置重新计算，而区域替换
    // 是否保留筛选输入框的焦点不由本模块规定，因此这里只断言"失焦语义（选中行不在
    // 持有焦点的列表里）时仍为 inactive"，避免把焦点归属的波动误判成选择丢失。
    await refreshLogRegion(page);
    await waitForDetailContract(page, fixture.commits[2]);
    const blurredRefreshed = await readLogState(page, fixture);
    assert.equal(blurredRefreshed.hash, beforeBlur.hash, '失焦重绘后仍选中同一行');
    assert.equal(blurredRefreshed.ariaSelected, 'true', '失焦重绘后 aria-selected 仍为 true');
    assert.equal(blurredRefreshed.selectedCount, 1, '失焦重绘后仍只有一个选中行');
    assert.equal(blurredRefreshed.stateHash, beforeBlur.stateHash, '失焦重绘后状态哈希不变');
    assert.equal(blurredRefreshed.detailSubject, fixture.commits[2].subject, '失焦重绘后详情仍是同一提交');
    if (!blurredRefreshed.activeInsideList) {
      assert.equal(blurredRefreshed.inactive, true, '失焦重绘后选中行仍是 inactive');
    }
    check('失焦后一次日志重绘仍保留选中行、aria-selected 与状态哈希', true);

    // ---- 3. 一次日志刷新/重绘后键盘选择仍保留（不依赖只数源码） ----
    await clickRow(page, fixture, 0);
    await waitForDetailContract(page, fixture.commits[0]);
    await pressOnLog(page, 'ArrowDown');
    await pressOnLog(page, 'ArrowDown');
    await pressOnLog(page, 'ArrowDown');
    const navigatedCommit = fixture.commits[3];
    await waitForDetailContract(page, navigatedCommit);
    const navigated = await readLogState(page, fixture);
    assertRowConsistent(navigated, navigatedCommit, '三次 ArrowDown 后');
    assert.equal(navigated.stateHash, navigatedCommit.hash, '键盘选择已写入 live.historySelectedHash');
    await refreshLogRegion(page);
    const afterRender = await readLogState(page, fixture);
    assertRowConsistent(afterRender, navigatedCommit, '日志重绘后');
    assert.equal(selectedIndex(afterRender, fixture), 3, '区域刷新重建列表后键盘选择不回到首行');
    check('日志区域刷新重建列表后键盘选择仍保留（选中行、aria、状态与详情一致）', true);

    // ---- 4. PageDown／PageUp：按日志视口行数跳转，首/中/末行不越界且保持选择 ----
    // 期望步长由本模块自己算（`list.clientHeight / 行高`），不改用页面里的实现变量：
    // 否则"实现改了步长口径"这件事会被同一个口径掩盖。
    const viewportRows = refreshed.viewportRows;
    const lastIndex = fixture.commits.length - 1;
    // 首行 → PageDown：从第 0 行跳到第 viewportRows 行（视口行数，不是固定值）。
    await clickRow(page, fixture, 0);
    await waitForDetailContract(page, fixture.commits[0]);
    await pressKeyAndWaitForSelection(page, 'PageDown', fixture.commits[viewportRows]);
    await waitForDetailContract(page, fixture.commits[viewportRows]);
    const pageDownTop = await readLogState(page, fixture);
    assertRowConsistent(pageDownTop, fixture.commits[viewportRows], 'PageDown 自首行');
    assert.equal(selectedIndex(pageDownTop, fixture), viewportRows, `PageDown 自首行应前进 ${viewportRows} 行`);

    // 同一行 → PageUp：回退到首行、不越界到 -1。
    await pressKeyAndWaitForSelection(page, 'PageUp', fixture.commits[0]);
    await waitForDetailContract(page, fixture.commits[0]);
    const pageUpTop = await readLogState(page, fixture);
    assertRowConsistent(pageUpTop, fixture.commits[0], 'PageUp 回退到首行');
    assert.equal(selectedIndex(pageUpTop, fixture), 0, 'PageUp 自中间行回到首行');

    // 中间行 → PageDown：前进一个视口但不越过末行。
    const middleStart = lastIndex - Math.max(2, viewportRows);
    const expectedMiddleForward = Math.min(lastIndex, middleStart + viewportRows);
    await clickRow(page, fixture, middleStart);
    await waitForDetailContract(page, fixture.commits[middleStart]);
    await pressKeyAndWaitForSelection(page, 'PageDown', fixture.commits[expectedMiddleForward]);
    await waitForDetailContract(page, fixture.commits[expectedMiddleForward]);
    const pageDownMiddle = await readLogState(page, fixture);
    assertRowConsistent(pageDownMiddle, fixture.commits[expectedMiddleForward], '中间行 PageDown');
    assert.equal(selectedIndex(pageDownMiddle, fixture), expectedMiddleForward, '中间行 PageDown 前进一个视口');

    // 末行 → PageDown：不越界，选择保持在最后一行。
    await clickRow(page, fixture, lastIndex);
    await waitForDetailContract(page, fixture.commits[lastIndex]);
    await pressKeyAndWaitForSelection(page, 'PageDown', fixture.commits[lastIndex]);
    const pageDownBottom = await readLogState(page, fixture);
    assertRowConsistent(pageDownBottom, fixture.commits[lastIndex], '末行 PageDown');
    assert.equal(selectedIndex(pageDownBottom, fixture), lastIndex, '末行 PageDown 不越界');

    // 末行 → PageUp：回退一个视口，仍在范围内。
    const expectedBottomBack = Math.max(0, lastIndex - viewportRows);
    await pressKeyAndWaitForSelection(page, 'PageUp', fixture.commits[expectedBottomBack]);
    await waitForDetailContract(page, fixture.commits[expectedBottomBack]);
    const pageUpBottom = await readLogState(page, fixture);
    assertRowConsistent(pageUpBottom, fixture.commits[expectedBottomBack], '末行 PageUp');
    assert.equal(selectedIndex(pageUpBottom, fixture), expectedBottomBack, '末行 PageUp 回退一个视口');
    check(`PageDown/PageUp 按视口 ${viewportRows} 行在首/中/末边界跳转并保持选择`, true);

    // 页面脚本错误：本批次新增的交互不得引入运行时异常。
    check('Git 日志键盘回归无页面脚本错误: ' + JSON.stringify(scene.errors.slice(0, 2)), scene.errors.length === 0);
  } finally {
    await page.close();
  }
};

// ---------------------------------------------------------------------------
// 结论与未解决项（本模块只承载断言，不修改实现）：
//
// 1. 实测结果（2026-10-11，Windows 11 x64 + 无头 Chromium 1243）：7 条断言全部通过。
//    PageDown／PageUp 的实现位于 `web/src/mockup.js` 的 `.commit-list` keydown
//    （步长 = `floor(list.clientHeight / 行高)`，首/末行夹取）；本模块的期望步长是
//    独立算的，不在页面里复用实现变量，因此这不是"照抄实现的自证"。
// 2. 失焦的 inactive 语义落在 `bindSelectionFocus()`（mockup.js）里：焦点离开
//    `.commit-list`，或其内部控件命中 input/button 判断时加 `.inactive`。第 2 组断言
//    固定"selected 变成 inactive 且选择不丢"，实现若改用别的事件路径（例如把失焦写进
//    状态）需要同步调整这里的断言口径，不能放宽成"只要还有选中"。
// 3. 第 1／3 组断言是**回归保护**：选择由 `history-commit-selected` 事件写入
//    `live.historySelectedHash`，区域刷新按哈希重排选中态。若未来把键盘选择改成只写
//    DOM，这两组会立刻失败。
// 4. 当前通过 live-shell.spec.cjs 的 --goal-log-only 入口运行。
// 5. Windows 10 22H2 未实机验证：本机只有 Windows 11 x64 + 无头 Chromium。
// ---------------------------------------------------------------------------
