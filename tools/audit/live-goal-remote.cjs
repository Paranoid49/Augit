// 远端正向整改的实时回归（规格 §7.11／§7.12）。
//
// 覆盖：Name/URL 表格与唯一 OK、单击只选行、双击/Edit 打开独立 Define Remote、
// 新增与改名、删除确认的取消与确认、Git 失败保留输入、客户端空值/重名校验、
// Fetch remote 默认勾选加 OK 才取回（取消勾选不取回、取消/失败/no-change 不取回）、
// 独立取消的焦点归还、保存忙防重入、晚到结果不重开窗口或抢焦点、空表禁动作、
// 大字号与小视口的限高、控件可见，以及 Push 嵌套关闭后仍只有唯一 Push。
//
// 本模块只承载断言，不修改实现。页面由 finally 关闭。
// 运行入口：`live-shell.spec.cjs --goal-remote-only`。
const ROW = '.remote-window .remote-row[data-remote-index]';
const TABLE = '.remote-window .remote-table';
const DEFINE = '.remote-define-window';
const CONFIRM = '.remote-confirm-window';

/** 每个场景开始前把远端相关的宿主桩状态清空。 */
async function resetRemoteState(page) {
  await page.evaluate(() => {
    window.__remotesState = null;
    window.__remoteWrites = [];
    window.__remoteWriteFails = false;
    window.__remoteWriteDelayMs = 0;
    window.__fetchCalls = 0;
    window.__fetchParams = [];
    window.__unpushedCalls = 0;
  });
}

/**
 * 在页面里包一层 `window.chrome.webview.postMessage`，给 git/remote-write 注入延迟，
 * 并记录 git/unpushed 次数。**桥接口真名是 `window.chrome.webview.postMessage`**
 * （`web/src/bridge.js` 的 `invoke` 直接调用它；仓库里没有 `__augitInvokeHost`）。
 * 这里不改 runner 的宿桩，也不引入新的常驻测试钩子。
 */
async function installInvokeHooks(page) {
  await page.evaluate(() => {
    if (window.__remoteInvokeHooked) return;
    window.__remoteInvokeHooked = true;
    const webview = window.chrome && window.chrome.webview;
    if (!webview) return;
    const original = webview.postMessage.bind(webview);
    webview.postMessage = (raw) => {
      let request = null;
      try { request = JSON.parse(raw); } catch { request = null; }
      if (request && request.method === 'git/unpushed') {
        window.__unpushedCalls = (window.__unpushedCalls || 0) + 1;
      }
      if (request && ['git/remotes', 'git/unpushed', 'git/references'].includes(request.method) && window.__remoteReadDelayMs > 0) {
        setTimeout(() => original(raw), window.__remoteReadDelayMs);
        return;
      }
      if (request && request.method === 'git/remote-write' && window.__remoteWriteDelayMs > 0) {
        setTimeout(() => original(raw), window.__remoteWriteDelayMs);
        return;
      }
      original(raw);
    };
  });
}

async function openManager(page) {
  await page.evaluate(() => window.__augitOpenRemoteManager());
  await page.waitForSelector(TABLE, { timeout: 8000 });
}

async function openDefine(page, via) {
  if (via === 'double') {
    await page.locator(ROW).first().dblclick();
  } else {
    await page.locator(`.remote-window [data-remote-action="${via}"]`).click();
  }
  await page.waitForSelector(DEFINE, { timeout: 8000 });
}

async function readRows(page) {
  return page.evaluate((rowSelector) => [...document.querySelectorAll(rowSelector)].map((row) => ({
    name: row.dataset.remoteEntry,
    selected: row.classList.contains('selected'),
    ariaSelected: row.getAttribute('aria-selected'),
    url: row.querySelector('.remote-url') ? row.querySelector('.remote-url').textContent : null,
  })), ROW);
}

module.exports = async function verifyGoalRemote({ openScene, check }) {
  // ---- 1. 主窗结构、唯一 OK、单击只选行、方向键单选 ----
  const main = await openScene('scene=main-project&theme=dark');
  const { page } = main;
  try {
    await page.waitForFunction('!!window.__augitLive.remotes', null, { timeout: 15000 });
    await installInvokeHooks(page);
    await resetRemoteState(page);
    await openManager(page);

    const shape = await page.evaluate(() => {
      const layer = document.querySelector('.remote-window');
      const dialog = layer.querySelector('.remote-dialog');
      const toolbar = [...dialog.querySelectorAll('.remote-toolbar [data-remote-action]')].map((node) => node.dataset.remoteAction);
      const footer = [...dialog.querySelectorAll('.dialog-footer [data-remote-action]')].map((node) => node.dataset.remoteAction);
      return {
        toolbar,
        toolbarIcons: dialog.querySelectorAll('.remote-toolbar [data-remote-action] svg').length,
        footer,
        head: [...dialog.querySelectorAll('.remote-table-head span')].map((node) => node.textContent),
        rows: dialog.querySelectorAll('.remote-row[data-remote-index]').length,
        title: dialog.querySelector('.dialog-header .remote-title').textContent,
        inlineFields: dialog.querySelectorAll('.management-detail, [data-remote-field]').length,
      };
    });
    check('远端主窗是 Name/URL 表格 + 顶端 Add/Remove/Edit + 唯一 OK: ' + JSON.stringify(shape),
      JSON.stringify(shape.toolbar) === JSON.stringify(['add', 'remove', 'edit'])
        && shape.toolbarIcons === 3
        && JSON.stringify(shape.footer) === JSON.stringify(['ok'])
        && JSON.stringify(shape.head) === JSON.stringify(['Name', 'URL'])
        && shape.rows === 1 && shape.inlineFields === 0 && shape.title === 'Git Remotes');

    const urlShown = await readRows(page);
    check('URL 列显示 Fetch URL: ' + JSON.stringify(urlShown),
      urlShown.length === 1 && urlShown[0].url === 'https://example.com/team/Augit.git'
        && urlShown[0].selected === false && urlShown[0].ariaSelected === 'false');

    // 单击只选行：不打开 Define，且不重建表格节点（否则双击与焦点都会丢）。
    const tableBefore = await page.evaluate(() => {
      const table = document.querySelector('.remote-window .remote-table');
      table.dataset.probe = 'kept';
      return !!table;
    });
    await page.locator(ROW).first().click();
    const singleClick = await page.evaluate(() => ({
      define: !!document.querySelector('.remote-define-window'),
      tableProbe: !!document.querySelector('.remote-window .remote-table[data-probe="kept"]'),
      selected: window.__augitLive.selectedRemoteIndex,
    }));
    check('单击只选行、不打开编辑、不重建表格: ' + JSON.stringify({ tableBefore, ...singleClick }),
      singleClick.define === false && singleClick.tableProbe === true && singleClick.selected === 0);

    // 空表禁用 Remove/Edit；先做，避免后续新增的行干扰。
    await page.evaluate(() => { window.__remotesState = { available: true, remotes: [] }; });
    await openManager(page);
    const empty = await page.evaluate(() => {
      const dialog = document.querySelector('.remote-window .remote-dialog');
      return {
        rows: dialog.querySelectorAll('.remote-row[data-remote-index]').length,
        add: dialog.querySelector('[data-remote-action="add"]').disabled,
        remove: dialog.querySelector('[data-remote-action="remove"]').disabled,
        edit: dialog.querySelector('[data-remote-action="edit"]').disabled,
      };
    });
    check('空表只有 Add 可用: ' + JSON.stringify(empty),
      empty.rows === 0 && empty.add === false && empty.remove === true && empty.edit === true);

    // 恢复夹具远端，开始写操作。
    await resetRemoteState(page);
    await openManager(page);

    // ---- 2. 双击打开 Define Remote；取消归还焦点，不改 Push/DOM ----
    const focusBefore = await page.evaluate(() => {
      const table = document.querySelector('.remote-window .remote-table');
      table.focus();
      return document.activeElement === table;
    });
    await page.locator(ROW).first().dblclick();
    await page.waitForSelector(DEFINE, { timeout: 8000 });
    const defineShape = await page.evaluate(() => {
      const layer = document.querySelector('.remote-define-window');
      const fields = [...layer.querySelectorAll('[data-remote-field]')].map((node) => node.dataset.remoteField);
      const fetch = layer.querySelector('[data-remote-field="fetch"]');
      return {
        title: layer.querySelector('.dialog-header .remote-title').textContent,
        fields,
        name: layer.querySelector('[data-remote-field="name"]').value,
        url: layer.querySelector('[data-remote-field="fetchUrl"]').value,
        fetchChecked: fetch ? fetch.checked : null,
        actions: [...layer.querySelectorAll('.dialog-footer button')].map(node => node.textContent.trim()),
        activeField: document.activeElement.dataset ? document.activeElement.dataset.remoteField : null,
      };
    });
    check('双击打开 Define Remote（Name/URL/Push URL + 默认勾选 Fetch，初始焦点按参考）: ' + JSON.stringify(defineShape),
      defineShape.title === 'Define Remote'
        && JSON.stringify(defineShape.fields) === JSON.stringify(['name', 'fetchUrl', 'pushUrl', 'fetch'])
        && defineShape.name === 'origin' && defineShape.url === 'https://example.com/team/Augit.git'
        && defineShape.fetchChecked === true && defineShape.activeField === 'fetchUrl'
        && defineShape.actions.join(',') === 'OK,Cancel');

    await page.locator(`${DEFINE} .dialog-footer [data-remote-define="cancel"]`).click();
    await page.waitForSelector(DEFINE, { state: 'detached', timeout: 5000 });
    const afterCancel = await page.evaluate(() => ({
      define: !!document.querySelector('.remote-define-window'),
      focusInTable: !!document.activeElement.closest && !!document.activeElement.closest('.remote-window .remote-table'),
      writes: (window.__remoteWrites || []).length,
    }));
    check('Define 取消不写且焦点回到表格: ' + JSON.stringify({ focusBefore, ...afterCancel }),
      afterCancel.define === false && afterCancel.focusInTable === true && afterCancel.writes === 0);

    // ---- 3. 客户端校验：空 Name/URL 与重名都不写 ----
    await openDefine(page, 'add');
    await page.locator(`${DEFINE} [data-remote-field="name"]`).fill('');
    await page.locator(`${DEFINE} .dialog-footer [data-remote-define="ok"]`).click();
    await page.waitForTimeout(100);
    const emptyName = await page.evaluate(() => ({
      open: !!document.querySelector('.remote-define-window'),
      notice: (document.querySelector('.remote-define-window .remote-notice') || {}).textContent || null,
      writes: (window.__remoteWrites || []).length,
    }));
    check('空 Name 是客户端错误且不调用 Git: ' + JSON.stringify(emptyName),
      emptyName.open === true && /cannot be empty/.test(emptyName.notice || '') && emptyName.writes === 0);

    await page.locator(`${DEFINE} [data-remote-field="name"]`).fill('origin');
    await page.locator(`${DEFINE} [data-remote-field="fetchUrl"]`).fill('https://example.com/dup.git');
    await page.locator(`${DEFINE} .dialog-footer [data-remote-define="ok"]`).click();
    await page.waitForTimeout(100);
    const duplicate = await page.evaluate(() => ({
      open: !!document.querySelector('.remote-define-window'),
      notice: (document.querySelector('.remote-define-window .remote-notice') || {}).textContent || null,
      writes: (window.__remoteWrites || []).length,
    }));
    check('重名是客户端错误且不调用 Git: ' + JSON.stringify(duplicate),
      duplicate.open === true && /already used/.test(duplicate.notice || '') && duplicate.writes === 0);
    for (const name of ['bad name', '.hidden', '-option', 'a..b', 'a/.hidden', 'a.lock', 'a@{b', 'bad"name', 'a\\b']) {
      await page.locator(`${DEFINE} [data-remote-field="name"]`).fill(name);
      await page.locator(`${DEFINE} .dialog-footer [data-remote-define="ok"]`).click();
      const invalid = await page.evaluate(() => ({
        writes: (window.__remoteWrites || []).length,
        notice: document.querySelector('.remote-define-window .remote-notice')?.textContent,
      }));
      check(`非法远端名称 ${JSON.stringify(name)} 前置拒绝并保留输入`, invalid.writes === 0 && /illegal characters/.test(invalid.notice || ''));
    }

    // ---- 4. 新增（默认勾选 Fetch → 保存成功后取回）与改名 ----
    await page.locator(`${DEFINE} [data-remote-field="name"]`).fill('backup');
    await page.locator(`${DEFINE} [data-remote-field="fetchUrl"]`).fill('https://example.com/backup.git');
    await page.locator(`${DEFINE} [data-remote-field="pushUrl"]`).fill('https://example.com/backup-push.git');
    await page.locator(`${DEFINE} .dialog-footer [data-remote-define="ok"]`).click();
    await page.waitForFunction('(window.__fetchCalls || 0) > 0', null, { timeout: 10000 });
    await page.waitForSelector(DEFINE, { state: 'detached', timeout: 5000 });
    const added = await page.evaluate(() => ({
      writes: window.__remoteWrites || [],
      fetchCalls: window.__fetchCalls || 0,
      fetchParams: window.__fetchParams || [],
      rows: [...document.querySelectorAll('.remote-window .remote-row[data-remote-index]')].map((row) => row.dataset.remoteEntry),
      notice: (document.querySelector('.remote-window .remote-notice') || {}).textContent || null,
    }));
    check('新增按 add 提交、成功且勾选 Fetch 时取回一次: ' + JSON.stringify({ writes: added.writes.length, fetchCalls: added.fetchCalls, rows: added.rows }),
      added.writes.length === 1 && added.writes[0].action === 'add' && added.writes[0].name === 'backup'
        && added.writes[0].fetchUrl === 'https://example.com/backup.git'
        && added.writes[0].pushUrl === 'https://example.com/backup-push.git'
        && added.fetchCalls === 1 && added.fetchParams[0].remote === 'backup'
        && added.rows.includes('backup') && /Added backup/.test(added.notice || ''));

    // 自动 Fetch 失败：参考 GitDefineRemoteDialog 保留 Define、URL 红框、错误留在 URL 附近；
    // 本轮回滚新增远端，不写管理器内联 notice，也不弹被遮挡的 toast。
    await openDefine(page, 'add');
    await page.locator(`${DEFINE} [data-remote-field="name"]`).fill('fetchfail');
    await page.locator(`${DEFINE} [data-remote-field="fetchUrl"]`).fill('https://example.com/fail.git');
    await page.evaluate(() => {
      window.__fetchCalls = 0;
      window.__fetchParams = [];
      window.__fetchFails = true;
    });
    await page.locator(`${DEFINE} .dialog-footer [data-remote-define="ok"]`).click();
    await page.waitForFunction('(window.__fetchCalls || 0) > 0', null, { timeout: 10000 });
    await page.waitForFunction(() => {
      const notice = document.querySelector('.remote-define-window .remote-notice');
      return !!notice && !notice.hidden && /No remote configured/.test(notice.textContent || '');
    }, null, { timeout: 10000 });
    const autoFetchFailed = await page.evaluate(() => {
      const notice = document.querySelector('.remote-define-window .remote-notice');
      return {
        writes: window.__remoteWrites || [],
        fetchCalls: window.__fetchCalls || 0,
        defineOpen: !!document.querySelector('.remote-define-window'),
        managerNotice: document.querySelector('.remote-window .remote-notice')?.textContent || null,
        notice: notice ? notice.textContent : null,
        noticeWhiteSpace: notice ? getComputedStyle(notice).whiteSpace : null,
        noticeOverflowWrap: notice ? getComputedStyle(notice).overflowWrap : null,
        noticeClipped: notice ? notice.scrollWidth > notice.clientWidth + 1 : null,
        urlInvalid: document.querySelector('.remote-define-window [data-remote-field="fetchUrl"]')?.getAttribute('aria-invalid') || null,
        toastTitle: (document.querySelector('.toast-title') || {}).textContent || null,
        rows: [...document.querySelectorAll('.remote-window .remote-row[data-remote-index]')].map((row) => row.dataset.remoteEntry),
      };
    });
    check('自动 Fetch 失败保留 Define、红框且回滚远端: ' + JSON.stringify(autoFetchFailed),
      autoFetchFailed.defineOpen
        && autoFetchFailed.writes.length >= 2
        && autoFetchFailed.writes[autoFetchFailed.writes.length - 2].action === 'add'
        && autoFetchFailed.writes[autoFetchFailed.writes.length - 1].action === 'delete'
        && autoFetchFailed.fetchCalls === 1
        && !autoFetchFailed.rows.includes('fetchfail')
        && !/fetchfail/.test(autoFetchFailed.managerNotice || '')
        && /No remote configured/.test(autoFetchFailed.notice || '')
        && autoFetchFailed.urlInvalid === 'true'
        && autoFetchFailed.noticeWhiteSpace === 'normal'
        && autoFetchFailed.noticeOverflowWrap === 'anywhere'
        && autoFetchFailed.noticeClipped === false
        && autoFetchFailed.toastTitle === null);
    await page.locator(`${DEFINE} .dialog-footer [data-remote-define="cancel"]`).click();
    await page.waitForSelector(DEFINE, { state: 'detached', timeout: 5000 });
    await page.evaluate(() => { window.__fetchFails = false; });

    // 改名：改 Name 走 update 并带 currentName。
    await page.evaluate(() => { window.__fetchCalls = 0; window.__fetchParams = []; });
    await page.locator('.remote-window .remote-row[data-remote-entry="origin"]').click();
    await openDefine(page, 'edit');
    await page.locator(`${DEFINE} [data-remote-field="name"]`).fill('upstream');
    await page.locator(`${DEFINE} .dialog-footer [data-remote-define="ok"]`).click();
    await page.waitForFunction('(window.__fetchCalls || 0) > 0', null, { timeout: 10000 });
    const renamed = await page.evaluate(() => ({
      writes: window.__remoteWrites || [],
      fetchParams: window.__fetchParams || [],
      rows: [...document.querySelectorAll('.remote-window .remote-row[data-remote-index]')].map((row) => row.dataset.remoteEntry),
    }));
    check('改名按 update 提交 currentName 并取回新名: ' + JSON.stringify({ writes: renamed.writes.map((write) => [write.action, write.currentName, write.name]), rows: renamed.rows }),
      renamed.writes.length >= 2 && renamed.writes[renamed.writes.length - 1].action === 'update'
        && renamed.writes[renamed.writes.length - 1].currentName === 'origin' && renamed.writes[renamed.writes.length - 1].name === 'upstream'
        && renamed.fetchParams[0].remote === 'upstream'
        && renamed.rows.includes('upstream') && !renamed.rows.includes('origin'));

    // ---- 5. no-change 编辑：直接关窗，不写、不取回 ----
    await page.evaluate(() => { window.__fetchCalls = 0; window.__fetchParams = []; });
    const writesBefore = await page.evaluate(() => (window.__remoteWrites || []).length);
    await openDefine(page, 'edit');
    await page.locator(`${DEFINE} .dialog-footer [data-remote-define="ok"]`).click();
    await page.waitForSelector(DEFINE, { state: 'detached', timeout: 5000 });
    await page.waitForTimeout(200);
    const noChange = await page.evaluate(() => ({
      writes: (window.__remoteWrites || []).length,
      fetchCalls: window.__fetchCalls || 0,
    }));
    check('no-change 编辑直接关窗、不写不取回: ' + JSON.stringify(noChange),
      noChange.writes === writesBefore && noChange.fetchCalls === 0);

    // ---- 6. 取消勾选 Fetch → 保存成功但绝不取回 ----
    await page.evaluate(() => { window.__fetchCalls = 0; window.__fetchParams = []; });
    await openDefine(page, 'add');
    await page.locator(`${DEFINE} [data-remote-field="name"]`).fill('nofetch');
    await page.locator(`${DEFINE} [data-remote-field="fetchUrl"]`).fill('https://example.com/nofetch.git');
    await page.locator(`${DEFINE} [data-remote-field="fetch"]`).uncheck();
    await page.locator(`${DEFINE} .dialog-footer [data-remote-define="ok"]`).click();
    await page.waitForSelector(DEFINE, { state: 'detached', timeout: 8000 });
    await page.waitForTimeout(250);
    const unchecked = await page.evaluate(() => ({
      fetchCalls: window.__fetchCalls || 0,
      rows: [...document.querySelectorAll('.remote-window .remote-row[data-remote-index]')].map((row) => row.dataset.remoteEntry),
    }));
    check('取消勾选 Fetch 时保存不取回: ' + JSON.stringify(unchecked),
      unchecked.fetchCalls === 0 && unchecked.rows.includes('nofetch'));

    // ---- 7. Git 失败保留窗口与输入，且不取回 ----
    await page.evaluate(() => { window.__remoteWriteFails = true; window.__fetchCalls = 0; });
    const writesBeforeFail = await page.evaluate(() => (window.__remoteWrites || []).length);
    await page.locator('.remote-window .remote-row[data-remote-entry="backup"]').click();
    await openDefine(page, 'edit');
    await page.locator(`${DEFINE} [data-remote-field="fetchUrl"]`).fill('https://example.com/backup-failed.git');
    await page.locator(`${DEFINE} .dialog-footer [data-remote-define="ok"]`).click();
    await page.waitForFunction('(document.querySelector(".remote-define-window .remote-notice") || {}).hidden === false', null, { timeout: 8000 });
    const failed = await page.evaluate(() => ({
      open: !!document.querySelector('.remote-define-window'),
      url: document.querySelector('.remote-define-window [data-remote-field="fetchUrl"]').value,
      writes: (window.__remoteWrites || []).length,
      fetchCalls: window.__fetchCalls || 0,
      rows: [...document.querySelectorAll('.remote-window .remote-row[data-remote-index]')].map((row) => row.dataset.remoteEntry),
    }));
    check('Git 失败保留 Define 与原输入、不取回: ' + JSON.stringify(failed),
      failed.open === true && failed.url === 'https://example.com/backup-failed.git'
        && failed.writes === writesBeforeFail + 1 && failed.fetchCalls === 0
        && failed.rows.includes('backup') && !failed.rows.includes('backup-failed'));
    await page.evaluate(() => { window.__remoteWriteFails = false; });
    await page.locator(`${DEFINE} .dialog-footer [data-remote-define="cancel"]`).click();
    await page.waitForSelector(DEFINE, { state: 'detached', timeout: 5000 });

    // ---- 8. Remove Remote 确认：取消不写；确认后按实际列表刷新 ----
    await page.locator('.remote-window .remote-row[data-remote-entry="nofetch"]').click();
    await page.locator('.remote-window [data-remote-action="remove"]').click();
    await page.waitForSelector(CONFIRM, { timeout: 8000 });
    const confirmShape = await page.evaluate(() => {
      const text = document.querySelector('.remote-confirm-window .remote-confirm-text').textContent;
      return { text, hasUrl: text.includes('https://example.com/nofetch.git'), title: document.querySelector('.remote-confirm-window .dialog-header .remote-title').textContent,
        actions: [...document.querySelectorAll('.remote-confirm-window .dialog-footer button')].map(node => node.textContent.trim()) };
    });
    check('Remove Remote 确认显示名称、URL 与影响: ' + JSON.stringify(confirmShape),
      confirmShape.title === 'Remove Remote' && confirmShape.hasUrl && /nofetch/.test(confirmShape.text) && confirmShape.actions.join(',') === 'Yes,No');

    const writesBeforeDelete = await page.evaluate(() => (window.__remoteWrites || []).length);
    await page.locator(`${CONFIRM} .dialog-footer [data-remote-confirm="cancel"]`).click();
    await page.waitForSelector(CONFIRM, { state: 'detached', timeout: 5000 });
    const cancelledDelete = await page.evaluate(() => ({
      writes: (window.__remoteWrites || []).length,
      rows: [...document.querySelectorAll('.remote-window .remote-row[data-remote-index]')].map((row) => row.dataset.remoteEntry),
    }));
    check('Remove 取消不写且列表不变: ' + JSON.stringify(cancelledDelete),
      cancelledDelete.writes === writesBeforeDelete && cancelledDelete.rows.includes('nofetch'));

    await page.locator('.remote-window .remote-row[data-remote-entry="nofetch"]').click();
    await page.locator('.remote-window [data-remote-action="remove"]').click();
    await page.waitForSelector(CONFIRM, { timeout: 8000 });
    await page.locator(`${CONFIRM} .dialog-footer [data-remote-confirm="remove"]`).click();
    await page.waitForSelector(CONFIRM, { state: 'detached', timeout: 8000 });
    await page.waitForFunction('!(window.__remotesState || { remotes: [] }).remotes.some((remote) => remote.name === "nofetch")', null, { timeout: 8000 });
    const deleted = await page.evaluate(() => ({
      writes: window.__remoteWrites || [],
      rows: [...document.querySelectorAll('.remote-window .remote-row[data-remote-index]')].map((row) => row.dataset.remoteEntry),
      notice: (document.querySelector('.remote-window .remote-notice') || {}).textContent || null,
    }));
    check('Remove 确认后按实际列表刷新: ' + JSON.stringify({ action: deleted.writes.at(-1) && deleted.writes.at(-1).action, rows: deleted.rows }),
      deleted.writes.at(-1).action === 'delete' && deleted.writes.at(-1).name === 'nofetch'
        && !deleted.rows.includes('nofetch') && /Deleted remote nofetch/.test(deleted.notice || ''));

    // ---- 9. 忙防重入与晚到结果不重开窗口 ----
    await page.evaluate(() => { window.__remoteWriteDelayMs = 500; });
    const writesBeforeBusy = await page.evaluate(() => (window.__remoteWrites || []).length);
    await page.locator('.remote-window .remote-row[data-remote-entry="backup"]').click();
    await openDefine(page, 'edit');
    await page.locator(`${DEFINE} [data-remote-field="fetchUrl"]`).fill('https://example.com/backup-busy.git');
    await page.locator(`${DEFINE} .dialog-footer [data-remote-define="ok"]`).click();
    const busy = await page.evaluate(() => ({
      okDisabled: document.querySelector('.remote-define-window [data-remote-define="ok"]').disabled,
      fieldDisabled: document.querySelector('.remote-define-window [data-remote-field="name"]').disabled,
    }));
    // 第二次提交必须在锁内被挡下。
    await page.evaluate(() => document.querySelector('.remote-define-window [data-remote-define="ok"]').click());
    await page.waitForTimeout(900);
    const busyResult = await page.evaluate(() => ({ writes: (window.__remoteWrites || []).length }));
    check('保存进行中冻结字段与 OK 并阻止重复提交: ' + JSON.stringify({ ...busy, writes: busyResult.writes - writesBeforeBusy }),
      busy.okDisabled === true && busy.fieldDisabled === true && busyResult.writes === writesBeforeBusy + 1);
    await page.waitForSelector(DEFINE, { state: 'detached', timeout: 8000 });

    // 晚到结果不重开窗口：在途保存时取消 Define，写入返回后不得再出现 Define 或抢焦点。
    await page.evaluate(() => { window.__remoteWriteDelayMs = 500; });
    await page.locator('.remote-window .remote-row[data-remote-entry="backup"]').click();
    await openDefine(page, 'edit');
    await page.locator(`${DEFINE} [data-remote-field="fetchUrl"]`).fill('https://example.com/backup-late.git');
    await page.locator(`${DEFINE} .dialog-footer [data-remote-define="ok"]`).click();
    await page.locator(`${DEFINE} .dialog-footer [data-remote-define="cancel"]`).click();
    await page.waitForTimeout(1000);
    const late = await page.evaluate(() => ({
      define: !!document.querySelector('.remote-define-window'),
      confirm: !!document.querySelector('.remote-confirm-window'),
      activeInsideRemote: !!(document.activeElement.closest && document.activeElement.closest('.remote-define-window')),
      remoteOpen: !!document.querySelector('.remote-window'),
    }));
    check('晚到保存结果不重开窗口、不抢焦点: ' + JSON.stringify(late),
      late.define === false && late.confirm === false && late.activeInsideRemote === false);
    await page.evaluate(() => { window.__remoteWriteDelayMs = 0; });

    // 旧写请求的返回不能清掉重新打开后新请求的忙状态。
    await page.locator('.remote-window .remote-row[data-remote-entry="backup"]').click();
    await openDefine(page, 'edit');
    await page.locator(`${DEFINE} [data-remote-field="fetchUrl"]`).fill('https://example.com/first-pending.git');
    await page.locator(`${DEFINE} [data-remote-field="fetch"]`).uncheck();
    await page.evaluate(() => { window.__remoteWriteDelayMs = 200; });
    await page.locator(`${DEFINE} .dialog-footer [data-remote-define="ok"]`).click();
    await page.locator(`${DEFINE} .dialog-footer [data-remote-define="cancel"]`).click();
    await openDefine(page, 'edit');
    await page.locator(`${DEFINE} [data-remote-field="fetchUrl"]`).fill('https://example.com/second-pending.git');
    await page.locator(`${DEFINE} [data-remote-field="fetch"]`).uncheck();
    await page.evaluate(() => { window.__remoteWriteDelayMs = 800; });
    await page.locator(`${DEFINE} .dialog-footer [data-remote-define="ok"]`).click();
    await page.waitForTimeout(350);
    check('旧写请求晚到不解除新编辑层忙状态', await page.locator(`${DEFINE} .dialog-footer [data-remote-define="ok"]`).isDisabled());
    await page.waitForSelector(DEFINE, { state: 'detached' });
    await page.evaluate(() => { window.__remoteWriteDelayMs = 0; });

    // 关闭期间的旧读取完成后，不能重建已重新打开的管理表格。
    await page.evaluate(() => { window.__remoteReadDelayMs = 200; });
    await page.locator('.remote-window .dialog-footer [data-remote-action="ok"]').click();
    await page.evaluate(() => { window.__remoteReadDelayMs = 0; });
    await openManager(page);
    await page.evaluate(() => { window.__reopenedRemoteTable = document.querySelector('.remote-window .remote-table'); });
    await page.waitForTimeout(400);
    check('管理窗关闭后的读取晚到不能重建新表格或打开 Push', await page.evaluate(() =>
      window.__reopenedRemoteTable === document.querySelector('.remote-window .remote-table')
        && !document.querySelector('.push-dialog')));

    // ---- 10. 大字号与小视口：限高只滚动正文，控件可见 ----
    await openManager(page);
    await page.evaluate(() => { document.querySelector('.remote-window .remote-table').focus(); });
    const layout = await page.evaluate(() => {
      const dialog = document.querySelector('.remote-window .remote-dialog');
      const style = getComputedStyle(dialog);
      const table = dialog.querySelector('.remote-table');
      const ok = dialog.querySelector('[data-remote-action="ok"]');
      ok.focus();
      const okRect = ok.getBoundingClientRect();
      const tableRect = table.getBoundingClientRect();
      return {
        row: style.getPropertyValue('--remote-row').trim(),
        nameWidth: style.getPropertyValue('--remote-name-width').trim(),
        tableScrolls: table.scrollHeight > table.clientHeight,
        okVisible: okRect.top >= tableRect.top - 1 && okRect.bottom <= innerHeight + 1,
        dialogFits: dialog.getBoundingClientRect().bottom <= innerHeight + 1,
      };
    });
    check('远端窗口量出 --remote-row/--remote-name-width 且 OK 可见: ' + JSON.stringify(layout),
      layout.row !== '' && layout.nameWidth !== '' && layout.okVisible && layout.dialogFits);

    const small = await openScene('scene=main-project&theme=dark&ui-size=20');
    try {
      await small.page.waitForFunction('!!window.__augitLive.remotes', null, { timeout: 15000 });
      await small.page.setViewportSize({ width: 900, height: 460 });
      await openManager(small.page);
      const clamped = await small.page.evaluate(() => {
        const dialog = document.querySelector('.remote-window .remote-dialog');
        const rect = dialog.getBoundingClientRect();
        const table = dialog.querySelector('.remote-table');
        return {
          fits: rect.bottom <= innerHeight + 1 && rect.top >= -1 && rect.left >= -1 && rect.right <= innerWidth + 1,
          row: getComputedStyle(dialog).getPropertyValue('--remote-row').trim(),
          bodyInside: !!(table.closest('.dialog-body')),
          toolbarFixed: !!dialog.querySelector('.remote-toolbar'),
        };
      });
      check('大字号 + 小视口远端窗口不越界且工具条固定: ' + JSON.stringify(clamped),
        clamped.fits && clamped.row !== '' && clamped.bodyInside && clamped.toolbarFixed);
      check('大字号场景无页面脚本错误: ' + JSON.stringify(small.errors.slice(0, 2)), small.errors.length === 0);
    } finally {
      await small.page.close();
    }

    // ---- 11. Push 嵌套：远端期间 Push 唯一；关闭后仍唯一并重读预览 ----
    const push = await openScene('scene=push&theme=dark', () => { window.__unpushedFails = true; });
    try {
      const { page: pushPage } = push;
      await installInvokeHooks(pushPage);
      await pushPage.evaluate(() => { window.__unpushedCalls = 0; });
      await pushPage.waitForSelector('.push-dialog', { timeout: 10000 });
      await pushPage.waitForSelector('.push-dialog .push-summary a[href$="remote.html"]', { timeout: 10000 });
      await pushPage.locator('.push-dialog .push-summary a[href$="remote.html"]').first().click();
      await pushPage.waitForSelector('.remote-window .remote-table', { timeout: 8000 });
      const nestedOpen = await pushPage.evaluate(() => {
        const push = document.querySelector('.live-overlay .dialog.push-dialog, .dialog.push-dialog');
        if (push) push.dataset.remoteProbe = 'kept';
        return {
          pushDialogs: document.querySelectorAll('.dialog.push-dialog').length,
          probe: !!(push && push.dataset.remoteProbe === 'kept'),
          remote: !!document.querySelector('.remote-window'),
        };
      });
      check('远端窗口期间 Push 保持唯一且本 DOM 未被重建: ' + JSON.stringify(nestedOpen),
        nestedOpen.pushDialogs === 1 && nestedOpen.probe === true && nestedOpen.remote === true);

      await pushPage.locator('.remote-window [data-remote-action="add"]').click();
      await pushPage.keyboard.press('Escape');
      check('嵌套 Define 的 Esc 只关闭 Define', await pushPage.locator(DEFINE).count() === 0
        && await pushPage.locator(TABLE).count() === 1 && await pushPage.locator('.push-dialog').count() === 1);
      await pushPage.keyboard.press('Escape');
      await pushPage.waitForSelector('.remote-window', { state: 'detached' });
      await pushPage.waitForFunction(() => document.activeElement.closest?.('.push-commits'));
      check('嵌套 Remotes 的 Esc 保留原 Push 节点并回到预览列表', await pushPage.evaluate(() =>
        document.querySelector('.push-dialog')?.dataset.remoteProbe === 'kept'));
      await pushPage.locator('.push-dialog .push-summary a[href$="remote.html"]').click();
      await pushPage.waitForSelector(TABLE);

      await pushPage.locator('.remote-window [data-remote-action="ok"]').click();
      await pushPage.waitForSelector('.remote-window', { state: 'detached', timeout: 8000 });
      await pushPage.waitForFunction('(window.__unpushedCalls || 0) > 0', null, { timeout: 8000 });
      const nestedClosed = await pushPage.evaluate(() => ({
        pushDialogs: document.querySelectorAll('.dialog.push-dialog').length,
        remote: !!document.querySelector('.remote-window'),
        define: !!document.querySelector('.remote-define-window'),
        unpushedCalls: window.__unpushedCalls || 0,
        focusInPush: !!(document.activeElement.closest && document.activeElement.closest('.push-commits')),
        identity: document.querySelector('.push-dialog')?.dataset.remoteProbe === 'kept',
      }));
      check('远端关闭只重读预览并原位保留唯一 Push: ' + JSON.stringify(nestedClosed),
        nestedClosed.pushDialogs === 1 && nestedClosed.remote === false
          && nestedClosed.define === false && nestedClosed.unpushedCalls >= 1 && nestedClosed.identity === true);
      check('Push 嵌套场景无页面脚本错误: ' + JSON.stringify(push.errors.slice(0, 2)), push.errors.length === 0);
    } finally {
      await push.page.close();
    }

    // ---- 12. scene=remote 静态运行时层复用同一套表格键盘逻辑 ----
    const scene = await openScene('scene=remote&theme=dark');
    try {
      await scene.page.waitForSelector('.remote-dialog .remote-table', { timeout: 8000 });
      const sceneRows = await scene.page.evaluate(() => document.querySelectorAll('.remote-dialog .remote-row[data-remote-index]').length);
      await scene.page.locator('.remote-dialog .remote-table').focus();
      await scene.page.keyboard.press('End');
      const sceneSelected = await scene.page.evaluate(() => {
        const rows = [...document.querySelectorAll('.remote-dialog .remote-row[data-remote-index]')];
        return {
          selected: rows.filter((row) => row.classList.contains('selected')).length,
          index: rows.findIndex((row) => row.classList.contains('selected')),
        };
      });
      await scene.page.keyboard.press('Home');
      const sceneHome = await scene.page.evaluate(() => {
        const rows = [...document.querySelectorAll('.remote-dialog .remote-row[data-remote-index]')];
        return rows.findIndex((row) => row.classList.contains('selected'));
      });
      check('scene=remote 复用表格键盘单选: ' + JSON.stringify({ sceneRows, ...sceneSelected, sceneHome }),
        sceneRows >= 1 && sceneSelected.selected === 1 && sceneSelected.index === sceneRows - 1 && sceneHome === 0);
      check('scene=remote 无页面脚本错误: ' + JSON.stringify(scene.errors.slice(0, 2)), scene.errors.length === 0);
      await scene.page.locator('.remote-dialog .dialog-header .icon-button').click();
      await scene.page.waitForSelector('.remote-dialog', { state: 'detached' });
      check('场景自带 Remotes 标题关闭移除整个层', await scene.page.locator('.remote-dialog').count() === 0);
    } finally {
      await scene.page.close();
    }

    check('远端正向整改场景无页面脚本错误: ' + JSON.stringify(main.errors.slice(0, 2)), main.errors.length === 0);
  } finally {
    await page.close();
  }
};

