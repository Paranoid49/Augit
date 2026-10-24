// 大文件只读预览的正常入口验收（真实外壳 + WebView2，CDP 只用于观测与输入）。
//
// 目的：在真实 start-augit.ps1 启动的实例里，用项目树的正常双击入口打开
// 2.96 MB / 80,000 行与 22.75 MiB 样本，记录：
//   - 警告横幅、首行、全文窗口层挂载、全文可访问文本的时序；
//   - 窗口行数、全文行数、实际行高；
//   - Find in File 的全文计数与定位（跳到未显示行）；
//   - Go to Line 跳到全文末尾。
//
// 这是行为/时序证据，不是前台像素或 UIA 全量枚举验收；CDP 只驱动 DOM 输入并读取状态，
// 不注入场景、不替换宿主返回值。实例与临时目录在 finally 中正常关闭/删除。
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const net = require('node:net');
const crypto = require('node:crypto');
const { execFile } = require('node:child_process');
const execute = require('node:util').promisify(execFile);
const { chromium } = require(process.argv[2]);
const output = path.resolve(process.argv[3] || 'artifacts/large-text-normal-entry-2026-10-23');
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

function bigText(lines, tokenLines, width = 96) {
  const tokens = new Map(Object.entries(tokenLines || {}).map(([line, token]) => [Number(line), token]));
  const out = [];
  for (let index = 1; index <= lines; index++) {
    const token = tokens.get(index);
    out.push(`line ${String(index).padStart(6, '0')} ${token ? token : '-'.repeat(width)}`);
  }
  return out.join('\n');
}

async function clipboardInfo() {
  const script = [
    '$text = Get-Clipboard -Raw',
    '$bytes = [Text.Encoding]::UTF8.GetBytes([string]$text)',
    '$hash = [Security.Cryptography.SHA256]::Create().ComputeHash($bytes)',
    '$hex = ($hash | ForEach-Object { $_.ToString("x2") }) -join ""',
    'Write-Output ($hex + "|" + $bytes.Length)',
  ].join('\n');
  const out = (await execute('powershell.exe', ['-NoProfile', '-Command', script], { timeout: 60000 })).stdout.trim();
  const [hash, length] = out.split('|');
  return { hash, length: Number(length) };
}

async function windowHandle(pid) {
  const script = `$d=(Get-Date).AddSeconds(20); while((Get-Date)-lt $d){$h=(Get-Process -Id ${pid} -ErrorAction SilentlyContinue).MainWindowHandle; if($h -ne 0){Write-Output $h; exit 0}; Start-Sleep -Milliseconds 100}; exit 2`;
  return Number((await execute('powershell.exe', ['-NoProfile', '-Command', script], { timeout: 30000 })).stdout.trim());
}

async function osDrag(hwnd, rect, holdMs) {
  const script = `Add-Type @"
using System; using System.Runtime.InteropServices; using System.Threading;
public static class LargeDrag {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int c);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int cx,int cy,uint f);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h,out Rect r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h,ref Pt p);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e);
  [StructLayout(LayoutKind.Sequential)] public struct Rect{public int L,T,R,B;}
  [StructLayout(LayoutKind.Sequential)] public struct Pt{public int X,Y;}
}
"@
try{[void][LargeDrag]::SetProcessDpiAwarenessContext([IntPtr](-4))}catch{}
$h=[IntPtr]${hwnd}; [void][LargeDrag]::ShowWindow($h,5); [void][LargeDrag]::BringWindowToTop($h); [void][LargeDrag]::SetWindowPos($h,[IntPtr](-1),0,0,0,0,0x0003); [void][LargeDrag]::SetForegroundWindow($h); Start-Sleep -Milliseconds 300
$r=New-Object LargeDrag+Rect; $o=New-Object LargeDrag+Pt; [void][LargeDrag]::GetClientRect($h,[ref]$r); [void][LargeDrag]::ClientToScreen($h,[ref]$o); $s=[LargeDrag]::GetDpiForWindow($h)/96.0
$sx=[int][Math]::Round($o.X+(${rect.x + 120})*$s); $sy=[int][Math]::Round($o.Y+(${rect.y + 12})*$s)
$ex=[int][Math]::Round($o.X+(${rect.x + 120})*$s); $ey=[int][Math]::Round($o.Y+(${rect.y + rect.height + 40})*$s)
[void][LargeDrag]::SetCursorPos($sx,$sy); Start-Sleep -Milliseconds 120
[LargeDrag]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
for($i=1;$i -le 6;$i++){ $y=[int]($sy+($ey-$sy)*$i/6); [void][LargeDrag]::SetCursorPos($ex,$y); Start-Sleep -Milliseconds 60 }
Start-Sleep -Milliseconds ${holdMs}
[LargeDrag]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
Write-Output "$sx,$sy -> $ex,$ey"`;
  return (await execute('powershell.exe', ['-NoProfile', '-Command', script], { timeout: 30000 })).stdout.trim();
}

async function osCopy() {
  const script = `Add-Type @"
using System; using System.Runtime.InteropServices;
public static class LargeCopy { [DllImport("user32.dll")] public static extern void keybd_event(byte vk,byte scan,uint f,UIntPtr e); }
"@
[LargeCopy]::keybd_event(0x11,0,0,[UIntPtr]::Zero); [LargeCopy]::keybd_event(0x43,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 40; [LargeCopy]::keybd_event(0x43,0,2,[UIntPtr]::Zero); [LargeCopy]::keybd_event(0x11,0,2,[UIntPtr]::Zero)`;
  await execute('powershell.exe', ['-NoProfile', '-Command', script], { timeout: 30000 });
}

async function closeWindow(pid) {
  const script = [
    'Add-Type @\"',
    'using System; using System.Runtime.InteropServices;',
    'public static class CloseProbe { [DllImport(\"user32.dll\")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l); }',
    '\"@',
    `$p = Get-Process -Id ${pid} -ErrorAction Stop`,
    '[void][CloseProbe]::PostMessage($p.MainWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)',
    '$deadline = (Get-Date).AddSeconds(20)',
    'while (-not $p.HasExited -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 100 }',
    'if ($p.HasExited) { exit 0 }',
    'exit 2',
  ].join('\n');
  try {
    await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 });
    return true;
  } catch {
    return false;
  }
}

(async () => {
  await fs.mkdir(output, { recursive: true });
  const temporary = await fs.mkdtemp(path.join(os.tmpdir(), 'Augit-large-entry-'));
  const workspace = path.join(temporary, 'workspace');
  const settings = path.join(temporary, 'settings.json');
  let listener;
  let pid = 0;
  let browser = null;
  const report = { generatedAt: new Date().toISOString(), launcher: 'tools/start-augit.ps1', cdp: true, samples: [] };
  try {
    await fs.mkdir(workspace);
    await fs.copyFile(path.join(process.env.LOCALAPPDATA, 'Augit/settings.json'), settings).catch(() => fs.writeFile(settings, '{}\n'));
    const small = bigText(2000, { 1: 'NEEDLE_FIRST', 1000: 'NEEDLE_MIDDLE', 2000: 'NEEDLE_LAST' }, 120);
    const huge = bigText(211100, { 1: 'NEEDLE_FIRST', 12000: 'NEEDLE_MIDDLE', 22000: 'NEEDLE_LAST' }, 100);
    const mid = bigText(80000, { 1: 'NEEDLE_FIRST', 40000: 'NEEDLE_MIDDLE', 80000: 'NEEDLE_LAST' }, 24);
    await fs.writeFile(path.join(workspace, 'small.txt'), small);
    await fs.writeFile(path.join(workspace, 'huge.txt'), huge);
    await fs.writeFile(path.join(workspace, 'mid.txt'), mid);
    // 非 Git 目录会弹「初始化仓库」模态，挡住正常项目树入口；建一个只含种子提交的仓库，
    // 大文件保留在磁盘但不进入索引，项目树仍可见、Git 状态也不会扫描 23 MB 内容。
    await fs.writeFile(path.join(workspace, '.gitignore'), 'small.txt\nhuge.txt\nmid.txt\n');
    await fs.writeFile(path.join(workspace, 'seed.txt'), 'seed\n');
    await execute('git', ['init', '-b', 'main'], { cwd: workspace, windowsHide: true });
    await execute('git', ['config', 'user.name', 'Augit Large Entry'], { cwd: workspace, windowsHide: true });
    await execute('git', ['config', 'user.email', 'large-entry@example.invalid'], { cwd: workspace, windowsHide: true });
    await execute('git', ['config', 'commit.gpgsign', 'false'], { cwd: workspace, windowsHide: true });
    await execute('git', ['add', '.gitignore', 'seed.txt'], { cwd: workspace, windowsHide: true });
    await execute('git', ['commit', '-m', 'test: large text fixture'], { cwd: workspace, windowsHide: true });
    report.fixtures = {
      small: { bytes: Buffer.byteLength(small), lines: 2000 },
      huge: { bytes: Buffer.byteLength(huge), lines: 211100 },
      mid: { bytes: Buffer.byteLength(mid), lines: 80000 },
      settingsSha256: crypto.createHash('sha256').update(await fs.readFile(settings)).digest('hex').toUpperCase(),
    };

    listener = net.createServer();
    await new Promise((resolve) => listener.listen(0, '127.0.0.1', resolve));
    const port = listener.address().port;
    await new Promise((resolve) => listener.close(resolve));
    report.port = port;
    const launch = require('./daily-launch-command.cjs')(process.cwd(), settings, port, workspace);
    const started = await execute(launch.file, launch.args, { env: launch.env, timeout: 240000 });
    pid = Number(/AUGIT_PID=(\d+)/.exec(started.stdout)?.[1]);
    if (!Number.isSafeInteger(pid) || pid <= 0) throw new Error('启动器未返回 PID: ' + started.stdout);
    report.pid = pid;
    report.launchCommand = launch.args;
    const hwnd = await windowHandle(pid);
    report.hwnd = hwnd;

    for (let attempt = 0; attempt < 160 && !browser; attempt++) {
      try {
        browser = await chromium.connectOverCDP(`http://127.0.0.1:${port}`);
      } catch {
        await delay(250);
      }
    }
    if (!browser) throw new Error('无法连接 WebView2 调试端口');
    let page = browser.contexts().flatMap((context) => context.pages()).find((candidate) => candidate.url().includes('index.html'));
    for (let attempt = 0; attempt < 120 && !page; attempt++) {
      await delay(250);
      page = browser.contexts().flatMap((context) => context.pages()).find((candidate) => candidate.url().includes('index.html'));
    }
    if (!page) throw new Error('找不到 WebView2 页面: ' + JSON.stringify(browser.contexts().flatMap((context) => context.pages()).map((candidate) => candidate.url())));
    try {
      await page.waitForFunction('window.__augitReady === true && !!window.__augitLive && !!document.querySelector(".side-content.tree")', null, { timeout: 60000 });
    } catch (error) {
      const state = await page.evaluate(() => ({
        url: location.href,
        ready: window.__augitReady,
        gitReady: window.__augitGitReady,
        historyReady: window.__augitHistoryReady,
        error: window.__augitError || null,
        hasLive: !!window.__augitLive,
        bodyText: (document.body && document.body.innerText || '').slice(0, 300),
      })).catch((probeError) => ({ probeError: String(probeError) }));
      throw new Error('首屏未就绪: ' + JSON.stringify(state) + ' / ' + String(error && error.message || error));
    }

    for (const sample of [
      { key: 'small', file: 'small.txt', lines: 2000, goto: 2000, findLine: 2000, preview: false },
      { key: 'mid', file: 'mid.txt', lines: 80000, goto: 80000, findLine: 80000, preview: false },
      // 22.75 MiB 超过 20 MB 内容加载上限，宿主只返回前 2.5 MB 的 TextPreview；
      // 预览行数由实际截断决定，不拿原文件 212,000 行当预期。
      { key: 'huge', file: 'huge.txt', lines: null, goto: 'last', findLine: 22000, preview: true },
    ]) {
      await page.evaluate(() => {
        if (window.__largeProbeTimer) cancelAnimationFrame(window.__largeProbeTimer);
        window.__largeProbe = { t0: 0, events: {}, textLength: 0, mountedAt: 0, accessibleAt: 0 };
      });
      const row = page.locator(`.side-content.tree .tree-row[data-tree-path="${sample.file}"]`);
      await row.waitFor({ state: 'visible', timeout: 15000 });
      const start = await page.evaluate(() => {
        const probe = window.__largeProbe;
        probe.t0 = performance.now();
        const codeOf = () => document.querySelector('.editor-content .code-view');
        const tick = () => {
          const now = performance.now();
          const code = codeOf();
          if (!probe.events.banner && document.querySelector('.editor-content [data-large-file-banner]')) probe.events.banner = now - probe.t0;
          if (!probe.events.line && code && code.querySelector('.code-line')) probe.events.line = now - probe.t0;
          if (!probe.events.virtual && code && code.__augitVirtual) {
            probe.events.virtual = now - probe.t0;
            probe.textLength = code.__augitVirtual.text.length;
            probe.mountedAt = code.__augitVirtual.mountedAt - probe.t0;
          }
          if (!probe.events.accessible && code && code.getAttribute('data-accessible-text') === 'ready') {
            probe.events.accessible = now - probe.t0;
            probe.accessibleAt = code.__augitVirtual ? code.__augitVirtual.accessibleLaidOutAt - probe.t0 : null;
          }
          if (now - probe.t0 < 30000) window.__largeProbeTimer = requestAnimationFrame(tick);
        };
        window.__largeProbeTimer = requestAnimationFrame(tick);
        return probe.t0;
      });
      await row.dblclick();
      await page.waitForFunction((file) => window.__augitLive && window.__augitLive.document
        && window.__augitLive.document.path === file, sample.file, { timeout: 30000 });
      try {
        await page.waitForFunction(({ lines, preview }) => {
          const code = document.querySelector('.editor-content .code-view');
          if (!code || !code.__augitVirtual || code.getAttribute('data-accessible-text') !== 'ready') return false;
          const count = code.__augitVirtual.lineCount;
          if (preview) {
            return !!window.__augitLive.document.readOnlyPreview && count > 20000 && count < 40000;
          }
          return count === lines;
        }, { lines: sample.lines, preview: sample.preview }, { timeout: 60000 });
      } catch (error) {
        const diagnostic = await page.evaluate(() => {
          const code = document.querySelector('.editor-content .code-view');
          const doc = window.__augitLive.document;
          return {
            editor: window.__augitLive.editor,
            documentPath: doc ? doc.path : null,
            status: doc ? doc.status : null,
            kind: doc ? doc.kind : null,
            readOnlyPreview: !!(doc && doc.readOnlyPreview),
            fileSize: doc ? doc.fileSize : null,
            previewBytes: doc ? doc.previewBytes : null,
            textLength: doc && typeof doc.text === 'string' ? doc.text.length : null,
            codeView: !!code,
            codeAttrs: code ? {
              largePreview: code.getAttribute('data-large-preview'),
              deferred: code.getAttribute('data-deferred-preview'),
              virtualLines: code.getAttribute('data-virtual-lines'),
              ariaBusy: code.getAttribute('aria-busy'),
              accessible: code.getAttribute('data-accessible-text'),
            } : null,
            rows: code ? code.querySelectorAll('.code-line').length : 0,
            hasModule: typeof window.__augitLargeTextView,
            wrap: !!(window.__augitLive.displayOptions && window.__augitLive.displayOptions.wrap),
            probe: window.__largeProbe,
            error: window.__augitError || null,
          };
        }).catch((probeError) => ({ probeError: String(probeError) }));
        throw new Error('窗口层未出现: ' + JSON.stringify(diagnostic) + ' / ' + String(error && error.message || error));
      }
      const opened = await page.evaluate(() => {
        const code = document.querySelector('.editor-content .code-view');
        const controller = code.__augitVirtual;
        const banner = document.querySelector('.editor-content [data-large-file-banner]');
        return {
          events: window.__largeProbe.events,
          lineCount: controller.lineCount,
          rowHeight: controller.rowHeight,
          visibleRows: code.querySelectorAll('.code-line').length,
          rowRole: code.querySelector('.code-line')?.getAttribute('role') || null,
          rowPos: Number(code.querySelector('.code-line')?.getAttribute('aria-posinset')),
          rowSet: Number(code.querySelector('.code-line')?.getAttribute('aria-setsize')),
          linesRole: code.querySelector('.code-lines')?.getAttribute('role') || null,
          linesLabel: code.querySelector('.code-lines')?.getAttribute('aria-label') || null,
          textLength: controller.text.length,
          a11yLength: code.querySelector('.virtual-text-accessible').textContent.length,
          a11yLabel: code.querySelector('.virtual-text-accessible').getAttribute('aria-label'),
          readOnlyPreview: !!window.__augitLive.document.readOnlyPreview,
          bannerVisible: !!banner,
          bannerText: banner ? banner.innerText.replace(/\s+/g, ' ').trim() : null,
          scrollHeight: code.scrollHeight,
          clientHeight: code.clientHeight,
          fontFamily: getComputedStyle(code).fontFamily,
          fontSize: getComputedStyle(code).fontSize,
          lineHeight: getComputedStyle(code).lineHeight,
        };
      });

      let keyboard = null;
      if (sample.key === 'small') {
        await page.locator('.editor-content .code-view').click({ position: { x: 200, y: 40 } });
        await page.keyboard.press('ArrowDown');
        await page.keyboard.press('ArrowDown');
        await page.keyboard.press('End');
        keyboard = await page.evaluate(() => {
          const code = document.querySelector('.editor-content .code-view');
          return {
            activeLine: code.__augitVirtual.activeLine,
            activeDescendant: code.getAttribute('aria-activedescendant'),
            activeRow: code.querySelector('.code-line[aria-current="true"]')?.id || null,
            scrollTop: code.scrollTop,
            endRowText: code.querySelector('.code-line[aria-current="true"] .virtual-line-text')?.textContent || null,
          };
        });
        await page.keyboard.press('Home');
        await page.keyboard.press('PageDown');
        keyboard.pageDown = await page.evaluate(() => {
          const code = document.querySelector('.editor-content .code-view');
          return {
            activeLine: code.__augitVirtual.activeLine,
            activeDescendant: code.getAttribute('aria-activedescendant'),
            scrollTop: code.scrollTop,
          };
        });
        await page.keyboard.press('Home');
        await page.keyboard.press('Shift+ArrowDown');
        await page.keyboard.press('Shift+ArrowDown');
        keyboard.shiftSelection = await page.evaluate(() => {
          const code = document.querySelector('.editor-content .code-view');
          const selection = code.__augitVirtual.selection;
          return {
            start: selection.start,
            end: selection.end,
            activeLine: code.__augitVirtual.activeLine,
            text: code.__augitVirtual.selectedText().slice(0, 120),
          };
        });
      }

      // 全选复制：Ctrl+A 必须选中全文模型，而不是当前可见窗口。mid 对磁盘文件做
      // 长度/SHA256；huge 是宿主 2.5 MB TextPreview，对 bridge 的 text 长度做校验。
      let selectAll = null;
      let drag = null;
      let osDragResult = null;
      let reverseDrag = null;
      if (sample.key === 'small' || sample.key === 'mid' || sample.key === 'huge') {
        await page.locator('.editor-content .code-view').click({ position: { x: 200, y: 40 } });
        await page.keyboard.press('Control+a');
        await delay(250);
        await page.keyboard.press('Control+c');
        await delay(400);
        const clip = await clipboardInfo();
        const expectedHash = sample.key === 'small'
          ? crypto.createHash('sha256').update(small).digest('hex')
          : sample.key === 'mid'
            ? crypto.createHash('sha256').update(mid).digest('hex')
            : null;
        const expectedLength = opened.textLength;
        selectAll = {
          length: clip.length,
          hash: clip.hash,
          expectedLength,
          expectedHash,
          lengthMatches: clip.length === expectedLength,
          hashMatches: expectedHash ? clip.hash === expectedHash : null,
        };
        // 窗口外拖动 + 自动滚动：把指针停在正文下方 1.2s，滚动应前进，复制内容
        // 应比可见窗口多，证明拖选不是只覆盖当前窗口。
        const rect = await page.evaluate(() => {
          const code = document.querySelector('.editor-content .code-view');
          code.scrollTop = 0;
          const r = code.getBoundingClientRect();
          return { x: r.x, y: r.y, width: r.width, height: r.height };
        });
        await page.mouse.move(rect.x + 120, rect.y + 12);
        await page.mouse.down();
        await page.mouse.move(rect.x + 120, rect.y + rect.height + 40, { steps: 6 });
        await delay(1200);
        const duringDrag = await page.evaluate(() => {
          const code = document.querySelector('.editor-content .code-view');
          return { scrollTop: code.scrollTop, rows: code.querySelectorAll('.code-line').length };
        });
        await page.mouse.up();
        await delay(200);
        await page.keyboard.press('Control+c');
        await delay(400);
        const dragClip = await clipboardInfo();
        drag = { duringDrag, clipboardLength: dragClip.length, copiedBeyondWindow: dragClip.length > 2000 };
        // 再用 Win32 mouse_event 做同一路径，避免只依赖 CDP 合成指针。
        const rect2 = await page.evaluate(() => {
          const code = document.querySelector('.editor-content .code-view');
          code.scrollTop = 0;
          const r = code.getBoundingClientRect();
          return { x: r.x, y: r.y, width: r.width, height: r.height };
        });
        const cursor = await osDrag(hwnd, rect2, 1200);
        const osDuring = await page.evaluate(() => {
          const code = document.querySelector('.editor-content .code-view');
          return { scrollTop: code.scrollTop, rows: code.querySelectorAll('.code-line').length };
        });
        await osCopy();
        await delay(400);
        const osClip = await clipboardInfo();
        osDragResult = { cursor, osDuring, clipboardLength: osClip.length, copiedBeyondWindow: osClip.length > 2000 };
        // 反向跨窗口拖动：先滚到末尾，再从底部向上拖，自动滚动应把选区向回扩展。
        if (sample.key === 'small' || sample.key === 'mid' || sample.key === 'huge') {
          const rect3 = await page.evaluate(() => {
            const code = document.querySelector('.editor-content .code-view');
            code.scrollTop = code.scrollHeight;
            const r = code.getBoundingClientRect();
            return { x: r.x, y: r.y, width: r.width, height: r.height, beforeTop: code.scrollTop };
          });
          await delay(250);
          await page.mouse.move(rect3.x + 120, rect3.y + rect3.height - 12);
          await page.mouse.down();
          await page.mouse.move(rect3.x + 120, rect3.y - 40, { steps: 6 });
          await delay(1200);
          const duringReverse = await page.evaluate(() => {
            const code = document.querySelector('.editor-content .code-view');
            return { scrollTop: code.scrollTop };
          });
          await page.mouse.up();
          await delay(200);
          await page.keyboard.press('Control+c');
          await delay(400);
          const reverseClip = await clipboardInfo();
          reverseDrag = { beforeTop: rect3.beforeTop, duringReverse, clipboardLength: reverseClip.length, scrolledBack: duringReverse.scrollTop < rect3.beforeTop };
        }
      }

      // Find in File：全文计数 3，Enter 两次定位到最后一个未显示行。
      await page.locator('.editor-content .code-view').click({ position: { x: 200, y: 40 } });
      await page.keyboard.press('Control+f');
      await page.locator('.current-find input').fill('NEEDLE');
      await page.waitForFunction(() => {
        const status = document.querySelector('.current-find .find-status');
        return !!status && status.textContent === '1/3';
      }, null, { timeout: 30000 });
      await page.keyboard.press('Enter');
      await page.keyboard.press('Enter');
      await page.waitForFunction(() => {
        const status = document.querySelector('.current-find .find-status');
        return !!status && status.textContent === '3/3';
      }, null, { timeout: 30000 });
      await page.waitForTimeout(150);
      const find = await page.evaluate((expectedLine) => {
        const code = document.querySelector('.editor-content .code-view');
        const current = document.querySelector('.find-current');
        const row = current ? current.closest('.code-line') : null;
        return {
          status: document.querySelector('.current-find .find-status').textContent,
          line: row ? Number(row.dataset.line) : null,
          scrollTop: code.scrollTop,
          visibleRows: code.querySelectorAll('.code-line').length,
          expectedLine,
        };
      }, sample.findLine);
      await page.keyboard.press('Escape');
      await page.waitForTimeout(100);
      const afterFind = await page.evaluate(() => {
        const controller = document.querySelector('.editor-content .code-view').__augitVirtual;
        return { selected: controller.selectedText(), selection: controller.selection };
      });

      // Go to Line：跳到全文最后一行并保持活动行可见。
      const gotoLine = await page.evaluate(() => document.querySelector('.editor-content .code-view').__augitVirtual.lineCount);
      await page.locator('.editor-content .code-view').click({ position: { x: 200, y: 40 } });
      await page.keyboard.press('Control+g');
      await page.locator('[data-compact-dialog] [data-compact-field]').waitFor({ state: 'visible', timeout: 10000 });
      await page.locator('[data-compact-dialog] [data-compact-field]').fill(String(gotoLine));
      await page.keyboard.press('Enter');
      await page.waitForTimeout(200);
      const goto = await page.evaluate((line) => {
        const code = document.querySelector('.editor-content .code-view');
        const active = code.querySelector('.code-line.active');
        return {
          dialog: !!document.querySelector('[data-compact-dialog]'),
          line: active ? Number(active.dataset.line) : null,
          text: active ? active.querySelector('.virtual-line-text').textContent : null,
          scrollTop: code.scrollTop,
          maxScroll: code.scrollHeight - code.clientHeight,
          expectedLine: line,
          visibleRows: code.querySelectorAll('.code-line').length,
        };
      }, gotoLine);

      // 滚到末尾确认最后一行渲染，不把「窗口外无节点」当成失败。
      const end = await page.evaluate(async (lines) => {
        const code = document.querySelector('.editor-content .code-view');
        code.scrollTop = code.scrollHeight;
        await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
        const rows = [...code.querySelectorAll('.code-line')];
        // 行节点是绝对定位的窗口节点，DOM 顺序是追加顺序，不等于行号顺序。
        const lastLine = rows.length ? Math.max(...rows.map((row) => Number(row.dataset.line))) : null;
        return { lastLine, rows: rows.length, expectedLine: lines, scrollTop: code.scrollTop };
      }, opened.lineCount);

      // 软换行回退：只在 80,000 行样本上量化全量 DOM 构建成本与操作边界。
      let wrap = null;
      if (sample.key === 'mid') {
        const wrapStarted = Date.now();
        await page.locator('.document-toolbar [aria-label="Soft Wraps"]').click();
        await page.waitForFunction((count) => {
          const code = document.querySelector('.editor-content .code-view');
          return !!code && !code.__augitVirtual && code.querySelectorAll('.code-line').length === count;
        }, opened.lineCount, { timeout: 120000 });
        wrap = await page.evaluate(async (count) => {
          const code = document.querySelector('.editor-content .code-view');
          code.scrollTop = code.scrollHeight;
          await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
          const rows = [...code.querySelectorAll('.code-line')];
          const lastLine = rows.length ? Math.max(...rows.map((row) => Number(row.dataset.line))) : null;
          const last = rows.find((row) => Number(row.dataset.line) === lastLine);
          return {
            virtual: !!code.__augitVirtual,
            rows: rows.length,
            wrap: code.classList.contains('wrap'),
            lastLine,
            lastText: last ? last.textContent.slice(0, 80) : null,
            expected: count,
          };
        }, opened.lineCount);
        wrap.buildMs = Date.now() - wrapStarted;
        await page.locator('.document-toolbar [aria-label="Soft Wraps"]').click();
        await page.waitForFunction(() => {
          const code = document.querySelector('.editor-content .code-view');
          return !!code && !!code.__augitVirtual;
        }, null, { timeout: 30000 });
        wrap.restored = await page.evaluate(() => {
          const code = document.querySelector('.editor-content .code-view');
          return { virtual: !!code.__augitVirtual, rows: code.querySelectorAll('.code-line').length };
        });
        report.wrap = wrap;
      }

      const failures = [];
      if (opened.readOnlyPreview !== sample.preview) failures.push('readOnlyPreview=' + opened.readOnlyPreview);
      if (!sample.preview && opened.lineCount !== sample.lines) failures.push('lineCount=' + opened.lineCount);
      if (!sample.preview && opened.bannerVisible) failures.push('普通文本不应出现大文件横幅');
      if (opened.visibleRows >= 200) failures.push('visibleRows=' + opened.visibleRows);
      if (opened.a11yLength !== 0
        || opened.a11yLabel !== `Read-only content, ${opened.lineCount.toLocaleString('en-US')} lines`) {
        failures.push('a11y=' + opened.a11yLength + '/' + opened.a11yLabel);
      }
      if (find.status !== '3/3' || find.line !== sample.findLine) failures.push('find=' + find.status + '@' + find.line);
      if (goto.dialog || goto.line !== gotoLine) failures.push('goto=' + goto.line + ' expected=' + gotoLine);
      if (sample.key === 'small' && (!keyboard || !keyboard.shiftSelection
        || keyboard.shiftSelection.end <= keyboard.shiftSelection.start
        || keyboard.shiftSelection.activeLine !== 2)) {
        failures.push('keyboard-selection=' + JSON.stringify(keyboard && keyboard.shiftSelection));
      }
      if (sample.key === 'small' && (!keyboard || !keyboard.pageDown
        || keyboard.pageDown.activeLine <= 0
        || keyboard.pageDown.activeDescendant !== `virtual-line-${keyboard.pageDown.activeLine + 1}`)) {
        failures.push('keyboard-page=' + JSON.stringify(keyboard && keyboard.pageDown));
      }
      if (end.lastLine !== opened.lineCount) failures.push('end=' + end.lastLine + ' expected=' + opened.lineCount);
      if (wrap && (wrap.virtual || wrap.rows !== opened.lineCount || !wrap.wrap || wrap.lastLine !== opened.lineCount)) {
        failures.push('wrap=' + JSON.stringify(wrap));
      }
      if (wrap && wrap.buildMs > 15000) failures.push('wrapBuildMs=' + wrap.buildMs);
      if (wrap && (!wrap.restored || !wrap.restored.virtual)) failures.push('wrapRestore=' + JSON.stringify(wrap.restored));
      report.samples.push({ key: sample.key, file: sample.file, opened, keyboard, selectAll, drag, osDragResult, reverseDrag, find, afterFind, goto, end, wrap, failures });
      if (failures.length) throw new Error(sample.key + ' 正常入口失败: ' + JSON.stringify(failures));
      await page.evaluate(() => window.__augitOpenDocument('sample.txt')).catch(() => {});
      await page.waitForTimeout(300);
    }
  } catch (error) {
    report.error = String(error && error.stack || error);
    process.exitCode = 1;
  } finally {
    if (browser) await browser.close().catch(() => {});
    if (pid) report.closedNormally = await closeWindow(pid);
    await delay(1000);
    report.pidAlive = pid ? (() => { try { process.kill(pid, 0); return true; } catch { return false; } })() : null;
    await fs.writeFile(path.join(output, 'large-text-normal-entry.json'), JSON.stringify(report, null, 2) + '\n');
    await fs.rm(temporary, { recursive: true, force: true }).catch(() => {});
  }
  console.log(JSON.stringify({
    pid: report.pid,
    fixtures: report.fixtures,
    samples: report.samples.map((sample) => ({
      key: sample.key,
      events: sample.opened.events,
      lineCount: sample.opened.lineCount,
      visibleRows: sample.opened.visibleRows,
      rowHeight: sample.opened.rowHeight,
      find: { status: sample.find.status, line: sample.find.line },
      goto: { line: sample.goto.line, dialog: sample.goto.dialog },
      end: sample.end,
    })),
    closedNormally: report.closedNormally,
    pidAlive: report.pidAlive,
    error: report.error || null,
  }, null, 2));
})();
