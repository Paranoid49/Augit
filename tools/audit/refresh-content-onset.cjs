// 正常入口「外部文件修改 → 编辑区首内容」重新采集（当前构建）。
//
// 方法沿用 docs/refresh-60fps-2026-10-23.md：
//   1. 项目树双击打开 refresh.txt，窗口最大化、前台；
//   2. ffmpeg gdigrab 以 60fps 采集编辑区左上裁切为 raw gray；
//   3. 采集开始后约 800ms 重写文件（改第 2 行），记录写入时刻；
//   4. 以首帧为基线，找连续 3 帧变化像素 > 200 的第一帧为首内容帧；
//   5. 首内容滞后 = 首内容帧 / 60 - (写入时刻 - 采集起点)。
//
// CDP 只用于读取项目树/编辑区矩形、确认文档已打开；双击由 Win32 mouse_event 完成，
// 窗口临时置 topmost 以避免被其他最大化窗口遮挡。录制与关闭不依赖 CDP。
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const net = require('node:net');
const { spawn, execFile } = require('node:child_process');
const execute = require('node:util').promisify(execFile);
const { chromium } = require(process.argv[2]);
const output = path.resolve(process.argv[3] || 'artifacts/refresh-content-onset-2026-10-24');
const traceRoot = process.env.AUGIT_REFRESH_TRACE_DIRECTORY
  ? path.resolve(process.env.AUGIT_REFRESH_TRACE_DIRECTORY)
  : null;
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

function documentText(generation) {
  return Array.from({ length: 40 }, (_, i) => `Line ${String(i + 1).padStart(2, '0')}  generation ${generation}  Ag 0123456789`).join('\n') + '\n';
}

function installStageTrace() {
  const bridge = chrome.webview;
  const original = bridge.postMessage;
  const pending = new Map();
  window.__onsetStage = { requests: [], cycle: null };
  bridge.addEventListener('message', (event) => {
    const response = typeof event.data === 'string' ? JSON.parse(event.data) : event.data;
    const item = pending.get(response.id);
    if (!item) return;
    item.end = performance.now();
    pending.delete(response.id);
  });
  bridge.postMessage = function (raw) {
    const request = typeof raw === 'string' ? JSON.parse(raw) : raw;
    if (['git/status', 'document/read', 'workspace/changes'].includes(request.method)) {
      const item = { id: request.id, method: request.method, start: performance.now() };
      pending.set(request.id, item);
      window.__onsetStage.requests.push(item);
    }
    return original.call(this, raw);
  };
  new MutationObserver(() => {
    const cycle = window.__onsetStage.cycle;
    if (!cycle || cycle.mutation) return;
    const code = document.querySelector('.editor-content .code-view');
    if (!code || !code.textContent.includes(`generation ${cycle.target}`)) return;
    cycle.mutation = performance.now();
    requestAnimationFrame(() => { cycle.paint = performance.now(); });
  }).observe(document.getElementById('app'), { childList: true, subtree: true, characterData: true });
}

async function waitWindow(pid) {
  const script = `$deadline = (Get-Date).AddSeconds(20)
while ((Get-Date) -lt $deadline) {
  $h = (Get-Process -Id ${pid} -ErrorAction SilentlyContinue).MainWindowHandle
  if ($h -ne 0) { Write-Output $h; exit 0 }
  Start-Sleep -Milliseconds 100
}
exit 2`;
  const result = await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 });
  return Number(result.stdout.trim());
}

async function setTopmost(pid, hwnd) {
  const script = `Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class TopWin {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
"@
try { [void][TopWin]::SetProcessDpiAwarenessContext([IntPtr](-4)) } catch { }
$h = [IntPtr]${hwnd}
[void][TopWin]::ShowWindow($h, 5)
$ok = [TopWin]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x0003)
Write-Output ("TOPMOST=" + $ok)`;
  const result = await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 });
  return result.stdout.trim();
}

async function foreground(pid, hwnd) {
  const script = `Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class FgWin {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool f);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
}
"@
try { [void][FgWin]::SetProcessDpiAwarenessContext([IntPtr](-4)) } catch { }
$h = [IntPtr]${hwnd}
[void][FgWin]::ShowWindow($h, 5)
[void][FgWin]::BringWindowToTop($h)
$fg = [FgWin]::GetForegroundWindow()
$fgPid = 0; $fgThread = [FgWin]::GetWindowThreadProcessId($fg, [ref]$fgPid)
$targetPid = 0; $targetThread = [FgWin]::GetWindowThreadProcessId($h, [ref]$targetPid)
$self = [FgWin]::GetCurrentThreadId()
if ($fgThread -ne 0 -and $fgThread -ne $self) { [void][FgWin]::AttachThreadInput($self, $fgThread, $true) }
[void][FgWin]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 400
if ($fgThread -ne 0 -and $fgThread -ne $self) { [void][FgWin]::AttachThreadInput($self, $fgThread, $false) }
Start-Sleep -Milliseconds 500
Write-Output ("FOREGROUND=" + ([FgWin]::GetForegroundWindow() -eq $h))`;
  const result = await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 });
  return result.stdout.trim();
}

async function closeWindow(pid) {
  const script = `Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class CloseWin {
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
}
"@
$p = Get-Process -Id ${pid} -ErrorAction Stop
[void][CloseWin]::PostMessage($p.MainWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
$deadline = (Get-Date).AddSeconds(20)
while (-not $p.HasExited -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 100 }
if ($p.HasExited) { exit 0 }
exit 2`;
  try {
    await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 });
    return true;
  } catch {
    return false;
  }
}

async function launch(workspace, settings, port, traceDirectory) {
  const literal = (value) => "'" + String(value).replaceAll("'", "''") + "'";
  const launcher = path.resolve('tools/start-augit.ps1');
  const command = '$ErrorActionPreference="Stop"; $p = & ' + literal(launcher)
    + ' -Workspace ' + literal(workspace) + ' -SettingsFile ' + literal(settings)
    + ' -SkipBuild; if (-not $p.Id) { throw "no pid" }; Write-Output ("AUGIT_PID=" + $p.Id)';
  const env = { ...process.env, WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: '--remote-debugging-port=' + port };
  if (traceDirectory) env.AUGIT_REFRESH_TRACE_DIRECTORY = traceDirectory;
  const started = await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', command], {
    env,
    timeout: 120000,
  });
  const pid = Number(/AUGIT_PID=(\d+)/.exec(started.stdout)?.[1]);
  if (!Number.isSafeInteger(pid) || pid <= 0) throw new Error('启动器未返回 PID: ' + started.stdout);
  return pid;
}

async function connect(port) {
  for (let attempt = 0; attempt < 160; attempt++) {
    try {
      return await chromium.connectOverCDP('http://127.0.0.1:' + port);
    } catch {
      await delay(250);
    }
  }
  throw new Error('无法连接 WebView2 调试端口');
}

async function pageOf(browser) {
  for (let attempt = 0; attempt < 120; attempt++) {
    const page = browser.contexts().flatMap((context) => context.pages()).find((candidate) => candidate.url().includes('index.html'));
    if (page) return page;
    await delay(250);
  }
  throw new Error('找不到 index.html 页面');
}

async function analyze(rawPath, frameBytes, writeAt, t0) {
  const buffer = await fs.readFile(rawPath);
  const frames = Math.floor(buffer.length / frameBytes);
  if (frames < 5) throw new Error('录制帧不足: ' + frames);
  const base = buffer.subarray(0, frameBytes);
  const counts = [];
  for (let frame = 0; frame < frames; frame++) {
    const start = frame * frameBytes;
    let changed = 0;
    // 每 2 个灰度采样一次，降低 96M 次比较的耗时；文字变化仍远超阈值。
    for (let offset = 0; offset < frameBytes; offset += 2) {
      if (Math.abs(buffer[start + offset] - base[offset]) > 8) changed++;
    }
    counts.push(changed);
  }
  const relWrite = writeAt - t0;
  let onset = -1;
  for (let frame = 0; frame < frames - 2; frame++) {
    const at = frame / 60 * 1000;
    if (at < relWrite - 100) continue;
    if (counts[frame] > 200 && counts[frame + 1] > 200 && counts[frame + 2] > 200) {
      onset = frame;
      break;
    }
  }
  return {
    frames,
    frameBytes,
    countsNearWrite: counts.map((value, index) => ({ frame: index, atMs: Math.round(index / 60 * 1000), changed: value }))
      .filter((item) => item.atMs >= relWrite - 300 && item.atMs <= relWrite + 1200),
    onsetFrame: onset,
    onsetMs: onset < 0 ? null : Math.round(onset / 60 * 1000),
    lagMs: onset < 0 ? null : Math.round(onset / 60 * 1000 - relWrite),
    writeOffsetMs: Math.round(relWrite),
  };
}

/**
 * 解析宿主导出的阶段文件。所有阶段内时间用 QPC 计算；跨进程只用 harness mark
 * 对齐（误差为打点往返的一半，单独报告）。屏幕首内容仍以采集帧为准。
 */
async function readHostTrace(directory, hostMark, writeEpoch) {
  const files = (await fs.readdir(directory)).filter((name) => /^refresh-.*\.json$/.test(name));
  if (files.length === 0) return { available: false, reason: 'no export file' };
  const data = JSON.parse(await fs.readFile(path.join(directory, files[0]), 'utf8'));
  const frequency = data.frequency;
  if (!Number.isFinite(frequency) || frequency <= 0) return { available: false, reason: 'bad frequency' };
  const samples = Array.isArray(data.samples) ? data.samples : [];
  const mark = samples.find((sample) => sample.stage === 'harness-mark');
  const toMs = (timestamp) => (timestamp / frequency) * 1000;
  const writeFromMark = hostMark && mark && Number.isFinite(hostMark.nodeEpoch)
    ? writeEpoch - hostMark.nodeEpoch
    : null;
  const timeline = samples.map((sample) => {
    const fromMark = mark ? toMs(sample.timestamp) - toMs(mark.timestamp) : null;
    return {
      stage: sample.stage,
      detail: sample.detail === undefined ? null : sample.detail,
      fromMarkMs: fromMark === null ? null : Math.round(fromMark * 10) / 10,
      fromWriteMs: fromMark === null || writeFromMark === null
        ? null
        : Math.round((fromMark - writeFromMark) * 10) / 10,
    };
  });
  return {
    available: true,
    file: files[0],
    frequency,
    processId: data.processId,
    samples: samples.length,
    markRecorded: !!mark,
    markNodeEpoch: hostMark ? hostMark.nodeEpoch : null,
    markPageSent: hostMark ? hostMark.sent : null,
    markRoundTripMs: hostMark ? hostMark.roundTripMs : null,
    writeFromMarkMs: writeFromMark === null ? null : Math.round(writeFromMark * 10) / 10,
    timeline,
  };
}

(async () => {
  await fs.mkdir(output, { recursive: true });
  const temporary = await fs.mkdtemp(path.join(os.tmpdir(), 'Augit-refresh-onset-'));
  const workspace = path.join(temporary, 'workspace');
  const settingsLight = path.join(temporary, 'settings-light.json');
  const settingsDark = path.join(temporary, 'settings-dark.json');
  const report = { generatedAt: new Date().toISOString(), launcher: 'tools/start-augit.ps1 -SkipBuild', method: 'ffmpeg gdigrab 60fps raw gray', runs: [] };
  let browser = null;
  let currentPid = 0;
  let currentClosed = false;
  try {
    await fs.mkdir(workspace);
    await fs.writeFile(path.join(workspace, 'refresh.txt'), documentText(0));
    await execute('git', ['init', '-b', 'main'], { cwd: workspace, windowsHide: true });
    await execute('git', ['config', 'user.name', 'Augit Onset'], { cwd: workspace, windowsHide: true });
    await execute('git', ['config', 'user.email', 'onset@example.invalid'], { cwd: workspace, windowsHide: true });
    await execute('git', ['config', 'commit.gpgsign', 'false'], { cwd: workspace, windowsHide: true });
    await execute('git', ['add', 'refresh.txt'], { cwd: workspace, windowsHide: true });
    await execute('git', ['commit', '-m', 'test: onset fixture'], { cwd: workspace, windowsHide: true });
    const original = path.join(process.env.LOCALAPPDATA, 'Augit/settings.json');
    await fs.copyFile(original, settingsLight);
    const dark = JSON.parse(await fs.readFile(original, 'utf8'));
    dark.theme = 'Dark';
    await fs.writeFile(settingsDark, JSON.stringify(dark, null, 2) + '\n');
    report.settings = {
      lightSha256: require('crypto').createHash('sha256').update(await fs.readFile(settingsLight)).digest('hex').toUpperCase(),
      darkTheme: dark.theme,
    };
    report.workspace = workspace;

    for (const run of [
      { key: 'light-1', settings: settingsLight },
      { key: 'light-2', settings: settingsLight },
      { key: 'light-3', settings: settingsLight },
      { key: 'light-4', settings: settingsLight },
      { key: 'dark-1', settings: settingsDark },
    ]) {
      // 每轮把文件恢复到 generation 0，避免上一轮残留。
      await fs.writeFile(path.join(workspace, 'refresh.txt'), documentText(0));
      const listener = net.createServer();
      await new Promise((resolve) => listener.listen(0, '127.0.0.1', resolve));
      const port = listener.address().port;
      await new Promise((resolve) => listener.close(resolve));
      report.port = port;
      const runTraceDir = traceRoot ? path.join(traceRoot, run.key) : null;
      if (runTraceDir) await fs.mkdir(runTraceDir, { recursive: true });
      const pid = await launch(workspace, run.settings, port, runTraceDir);
      const hwnd = await waitWindow(pid);
      currentPid = pid;
      currentClosed = false;
      report.pid = pid;
      browser = await connect(port);
      let page = await pageOf(browser);
      await page.waitForFunction('window.__augitReady === true && !!window.__augitLive', null, { timeout: 30000 });
      const rowRect = await page.evaluate(() => {
        const row = document.querySelector('.side-content.tree .tree-row[data-tree-path="refresh.txt"]');
        if (!row) return null;
        const rect = row.getBoundingClientRect();
        return { x: rect.x, y: rect.y, width: rect.width, height: rect.height };
      });
      if (!rowRect) throw new Error('项目树找不到 refresh.txt');
      await browser.close();
      browser = null;
      // Edge 等前台窗口常与 Augit 一样最大化，普通 SetForegroundWindow 会被前台锁拒绝。
      // 临时把本实例置为 topmost，再用真实鼠标双击项目树打开文件；窗口因此真实获得前台，
      // 录制结束随实例关闭恢复；只影响本批隔离实例。
      const topmost = await setTopmost(pid, hwnd);
      const click = await execute('powershell.exe', [
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', path.resolve('tools/audit/click-window.ps1'),
        '-X', String(rowRect.x + rowRect.width / 2), '-Y', String(rowRect.y + rowRect.height / 2),
        '-WindowHandle', String(hwnd), '-AfterMs', '150', '-Clicks', '2', '-GapMs', '80', '-NoForeground',
      ], { timeout: 30000 });
      const clickForeground = /foreground_match=(\w+)/.exec(click.stdout)?.[1] || null;

      browser = await connect(port);
      page = await pageOf(browser);
      await page.waitForFunction(() => window.__augitLive.document && window.__augitLive.document.path === 'refresh.txt', null, { timeout: 30000 });
      const geometry = await page.evaluate(() => {
        const editor = document.querySelector('.editor-content');
        const rect = editor.getBoundingClientRect();
        return {
          dpr: window.devicePixelRatio,
          editor: { x: rect.x, y: rect.y, width: rect.width, height: rect.height },
          document: window.__augitLive.document.path,
          codeLines: document.querySelectorAll('.editor-content .code-line').length,
          firstLine: document.querySelector('.editor-content .code-line .virtual-line-text, .editor-content .code-line span:last-child')?.textContent || null,
        };
      });
      const clickDpi = Number(/dpi=(\d+)/.exec(click.stdout)?.[1] || geometry.dpr * 96);
      const clickScale = clickDpi / 96;
      const screenPoint = /screen=\((\d+),(\d+)\)/.exec(click.stdout);
      const rowCenterX = rowRect.x + rowRect.width / 2;
      const rowCenterY = rowRect.y + rowRect.height / 2;
      const clientX = screenPoint ? Number(screenPoint[1]) - Math.round(rowCenterX * clickScale) : 0;
      const clientY = screenPoint ? Number(screenPoint[2]) - Math.round(rowCenterY * clickScale) : 0;
      const dpi = clickDpi;
      const scale = clickScale;
      const width = Math.min(800, Math.floor(geometry.editor.width * scale / 2) * 2);
      const height = Math.min(500, Math.floor(geometry.editor.height * scale / 2) * 2);
      const x = Math.round(clientX + geometry.editor.x * scale);
      const y = Math.round(clientY + geometry.editor.y * scale);
      const captureForeground = await foreground(pid, hwnd);
      if (!/FOREGROUND=True/.test(captureForeground)) {
        throw new Error('窗口未在前台，拒绝录制被遮挡的桌面: ' + captureForeground);
      }
      // 同轮记录 DOM 变化与桥接阶段；用于区分「产品慢」与「采集/合成慢」。
      await page.evaluate(installStageTrace);

      const rawPath = path.join(output, `${run.key}.raw`);
      const logsPath = path.join(output, `${run.key}.ffmpeg.log`);
      const desktopDuplication = process.argv.includes('--ddagrab');
      const captureArgs = desktopDuplication
        ? ['-f', 'lavfi', '-i',
          `ddagrab=output_idx=0:draw_mouse=0:framerate=60:output_fmt=bgra:offset_x=${x}:offset_y=${y}:video_size=${width}x${height},hwdownload,format=bgra`]
        : ['-f', 'gdigrab', '-framerate', '60', '-offset_x', String(x), '-offset_y', String(y),
          '-video_size', `${width}x${height}`, '-i', 'desktop'];
      const recorder = spawn('ffmpeg', [
        '-y', '-hide_banner', '-loglevel', 'warning',
        ...captureArgs,
        '-t', '4', '-f', 'rawvideo', '-pix_fmt', 'gray', rawPath,
      ], { windowsHide: true, stdio: ['ignore', 'ignore', 'pipe'] });
      let ffmpegLog = '';
      recorder.stderr.on('data', (chunk) => { ffmpegLog += chunk.toString(); });
      const frameBytes = width * height;
      const started = Date.now();
      while (Date.now() - started < 8000) {
        try {
          const stat = await fs.stat(rawPath);
          if (stat.size >= frameBytes) break;
        } catch { /* 文件尚未创建 */ }
        await delay(5);
      }
      const firstSize = await fs.stat(rawPath).then((stat) => stat.size).catch(() => 0);
      if (firstSize < frameBytes) throw new Error('ffmpeg 未写出首帧');
      const t0 = Date.now() - Math.floor(firstSize / frameBytes) / 60 * 1000;
      await delay(Math.max(0, 800 - (Date.now() - t0)));
      const generation = run.key === 'dark-1' ? 11 : 1;
      let hostMark = null;
      if (runTraceDir) {
        // 写入前经真实桥接打点：宿主在同一时刻记录 QPC，采集器记录墙钟与往返时间。
        const before = Date.now();
        const pageMark = await page.evaluate(() => new Promise((resolve) => {
          const id = 900000 + Math.floor(Math.random() * 100000);
          const sent = Date.now();
          const handler = (event) => {
            const response = typeof event.data === 'string' ? JSON.parse(event.data) : event.data;
            if (!response || response.id !== id) return;
            chrome.webview.removeEventListener('message', handler);
            resolve({
              recorded: !!(response.result && response.result.recorded),
              sent,
              received: Date.now(),
            });
          };
          chrome.webview.addEventListener('message', handler);
          chrome.webview.postMessage(JSON.stringify({
            id,
            method: 'audit/refresh-mark',
            params: { epochMs: String(Date.now()) },
          }));
          setTimeout(() => {
            chrome.webview.removeEventListener('message', handler);
            resolve({ recorded: false, sent, received: Date.now() });
          }, 2000);
        }));
        const after = Date.now();
        const oneWay = Math.max(0, (after - before) / 2);
        hostMark = {
          recorded: pageMark.recorded,
          sent: pageMark.sent,
          roundTripMs: pageMark.received - pageMark.sent,
          // 网页与采集器共用系统墙钟；用单程估计把打点对齐到宿主接收时刻。
          nodeEpoch: pageMark.sent + oneWay,
        };
      }
      const marker = await page.evaluate((target) => {
        window.__onsetStage.cycle = { target, start: performance.now(), mutation: null, paint: null };
        window.__onsetStage.cycle.requestStartIndex = window.__onsetStage.requests.length;
        return { start: window.__onsetStage.cycle.start, epoch: Date.now() };
      }, generation);
      const writeEpoch = Date.now();
      const writeAt = writeEpoch;
      await fs.writeFile(path.join(workspace, 'refresh.txt'), documentText(generation));
      await new Promise((resolve) => recorder.once('close', resolve));
      await fs.writeFile(logsPath, ffmpegLog);
      const analysis = await analyze(rawPath, frameBytes, writeAt, t0);
      const stageTrace = await page.evaluate(() => {
        const cycle = window.__onsetStage.cycle;
        const writeOffset = cycle ? cycle.start : 0;
        const requests = window.__onsetStage.requests.slice(cycle.requestStartIndex || 0)
          .filter((item) => item.start >= writeOffset - 5)
          .map((item) => ({
            method: item.method,
            start: Math.round(item.start - writeOffset),
            end: Number.isFinite(item.end) ? Math.round(item.end - writeOffset) : null,
            duration: Number.isFinite(item.end) ? Math.round(item.end - item.start) : null,
          }));
        return { mutation: cycle.mutation, paint: cycle.paint, requests };
      });
      const writePerformance = marker.start + (writeEpoch - marker.epoch);
      const stage = {
        mutationMs: stageTrace.mutation === null ? null : Math.round(stageTrace.mutation - writePerformance),
        paintMs: stageTrace.paint === null ? null : Math.round(stageTrace.paint - writePerformance),
        requests: stageTrace.requests,
      };
      const runReport = {
        key: run.key,
        pid,
        hwnd,
        port,
        foreground: { click: clickForeground, capture: captureForeground, clientX, clientY, dpi, topmost },
        crop: { x, y, width, height, dpi, scale },
        capture: desktopDuplication ? 'ffmpeg-ddagrab' : 'ffmpeg-gdigrab',
        geometry,
        stage,
        recorderPid: recorder.pid,
        t0,
        writeAt,
        writeOffsetMs: Math.round(writeAt - t0),
        analysis,
      };
      // 保留首帧与首内容帧 PNG，删除 100 MB 级 raw。
      const buffer = await fs.readFile(rawPath);
      if (analysis.onsetFrame >= 0) {
        const baselineRaw = path.join(output, `${run.key}-baseline.raw`);
        const onsetRaw = path.join(output, `${run.key}-onset.raw`);
        await fs.writeFile(baselineRaw, buffer.subarray(0, frameBytes));
        await fs.writeFile(onsetRaw, buffer.subarray(analysis.onsetFrame * frameBytes, (analysis.onsetFrame + 1) * frameBytes));
        for (const [source, target] of [[baselineRaw, `${run.key}-baseline.png`], [onsetRaw, `${run.key}-onset.png`]]) {
          await execute('ffmpeg', ['-y', '-hide_banner', '-loglevel', 'error', '-f', 'rawvideo', '-pixel_format', 'gray', '-video_size', `${width}x${height}`, '-i', source, '-frames:v', '1', path.join(output, target)], { timeout: 30000 });
          await fs.rm(source, { force: true });
        }
      }
      await fs.rm(rawPath, { force: true });
      await browser.close();
      browser = null;
      report.runs.push(runReport);
      runReport.closedNormally = await closeWindow(pid);
      currentClosed = runReport.closedNormally;
      await delay(500);
      runReport.pidAlive = (() => { try { process.kill(pid, 0); return true; } catch { return false; } })();
      if (runTraceDir) {
        runReport.hostMark = hostMark;
        runReport.hostTrace = await readHostTrace(runTraceDir, hostMark, writeAt);
      }
      report.closedNormally = runReport.closedNormally;
      report.pidAlive = runReport.pidAlive;
    }
  } catch (error) {
    report.error = String(error && error.stack || error);
    process.exitCode = 1;
  } finally {
    if (browser) await browser.close().catch(() => {});
    if (currentPid && !currentClosed) await closeWindow(currentPid).catch(() => {});
    await fs.writeFile(path.join(output, 'report.json'), JSON.stringify(report, null, 2) + '\n');
    await fs.rm(temporary, { recursive: true, force: true }).catch(() => {});
  }
  console.log(JSON.stringify({
    runs: report.runs.map((run) => ({
      key: run.key,
      crop: run.crop,
      writeOffsetMs: run.writeOffsetMs,
      onsetFrame: run.analysis.onsetFrame,
      lagMs: run.analysis.lagMs,
      stage: run.stage,
      counts: run.analysis.countsNearWrite,
    })),
    closedNormally: report.closedNormally,
    error: report.error || null,
  }, null, 2));
})();
