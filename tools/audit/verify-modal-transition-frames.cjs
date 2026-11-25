// 真实外壳逐帧验收：打开/关闭原生模态时主界面不得出现黑帧、淡出或重建。
// 采样 Format JSON（Ctrl+Shift+J）的完整开关周期；窗口只用于本次实例，结束后关闭并清理。
// 依赖：Playwright（显式传入模块路径）、ffmpeg（PATH 或 --ffmpeg）。
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const net = require('node:net');
const crypto = require('node:crypto');
const { spawn, spawnSync } = require('node:child_process');
const {
  frameStats, stableRuns, changedFraction, classifyStates, mergeStates, assessModalTimeline,
} = require('./modal-transition-analysis.cjs');

const ANALYSIS_WIDTH = 590;
const ANALYSIS_HEIGHT = 380;
const SHADOW_MARGIN_PHYSICAL = 48;

function fail(message) {
  throw new Error(message);
}

function sha256File(file) {
  return crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex').toUpperCase();
}

function parseArguments(argv) {
  const positional = [];
  const options = { theme: 'Light', cycles: 2, dpi: null, exe: null, workspace: null, ffmpeg: null };
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    if (argument === '--theme') options.theme = argv[++index];
    else if (argument === '--cycles') options.cycles = Number(argv[++index]);
    else if (argument === '--dpi') options.dpi = Number(argv[++index]);
    else if (argument === '--exe') options.exe = argv[++index];
    else if (argument === '--workspace') options.workspace = argv[++index];
    else if (argument === '--ffmpeg') options.ffmpeg = argv[++index];
    else if (argument.startsWith('--')) fail(`未知参数：${argument}`);
    else positional.push(argument);
  }
  if (positional.length !== 2) fail('用法：verify-modal-transition-frames.cjs <playwright 模块路径> <输出目录> [--exe 路径] [--workspace 路径] [--theme Light|Dark] [--dpi 96..480] [--cycles 1..3]');
  const [playwrightModule, output] = positional;
  if (!['Light', 'Dark'].includes(options.theme)) fail('--theme 只能是 Light 或 Dark');
  if (!Number.isSafeInteger(options.cycles) || options.cycles < 1 || options.cycles > 3) fail('--cycles 只能是 1..3');
  if (options.dpi !== null && (!Number.isSafeInteger(options.dpi) || options.dpi < 96 || options.dpi > 480)) fail('--dpi 无效');
  const repo = path.resolve(__dirname, '..', '..');
  return {
    repo,
    playwrightModule: path.resolve(playwrightModule),
    output: path.resolve(output),
    exe: path.resolve(options.exe || path.join(repo, 'src/Augit.Shell/bin/Debug/net10.0-windows/win-x64/Augit.exe')),
    workspace: path.resolve(options.workspace || repo),
    ffmpeg: options.ffmpeg || process.env.FFMPEG || 'ffmpeg',
    theme: options.theme,
    cycles: options.cycles,
    dpi: options.dpi,
  };
}

function allocatePort() {
  return new Promise((resolve, reject) => {
    const server = net.createServer();
    server.listen(0, '127.0.0.1', () => {
      const { port } = server.address();
      server.close(() => resolve(port));
    });
    server.on('error', reject);
  });
}

function runPowerShell(script) {
  const result = spawnSync('pwsh', ['-NoProfile', '-Command', script], { encoding: 'utf8' });
  if (result.status !== 0) fail(`PowerShell 失败：${result.stderr || result.stdout}`);
  return result.stdout.trim();
}

// 以物理像素读取窗口矩形，并把测试实例置顶，避免其它前台窗口污染录像区域。
function measureWindow(pid) {
  const script = `
Add-Type -TypeDefinition 'using System;using System.Runtime.InteropServices;public class W { [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c); [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r); [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h); [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow(); [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid); [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int w, int t, uint f); [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h); public struct RECT { public int L,T,R,B; } }'
[void][W]::SetThreadDpiAwarenessContext([IntPtr](-4))
$p = Get-Process -Id ${pid} -ErrorAction Stop
$h = $p.MainWindowHandle
if ($h -eq 0) { throw '测试实例没有主窗口。' }
$wsh = New-Object -ComObject WScript.Shell
$null = $wsh.AppActivate(${pid})
Start-Sleep -Milliseconds 250
# 仅审计实例：置顶保证录像区域只包含被测窗口；实例关闭后置顶状态随 HWND 消失。
[void][W]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x0001 -bor 0x0002 -bor 0x0010)
Start-Sleep -Milliseconds 100
$r = New-Object W+RECT
[W]::GetWindowRect($h, [ref]$r) | Out-Null
$fg = [W]::GetForegroundWindow(); $fgPid = 0
[W]::GetWindowThreadProcessId($fg, [ref]$fgPid) | Out-Null
@{ handle=[long]$h; left=$r.L; top=$r.T; right=$r.R; bottom=$r.B; dpi=[W]::GetDpiForWindow($h); foregroundPid=$fgPid } | ConvertTo-Json -Compress`;
  const lines = runPowerShell(script).split('\n');
  const info = JSON.parse(lines[lines.length - 1]);
  if (!info || info.right <= info.left || info.bottom <= info.top) fail('窗口矩形无效');
  return info;
}

function foregroundProcessId() {
  try {
    const text = runPowerShell(`
Add-Type -TypeDefinition 'using System;using System.Runtime.InteropServices;public class F { [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow(); [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid); }'
$fg = [F]::GetForegroundWindow(); $pid2 = 0
[F]::GetWindowThreadProcessId($fg, [ref]$pid2) | Out-Null
$pid2`);
    return Number(text);
  } catch {
    return -1;
  }
}

async function readFrameStream(stream, frameBytes, onFrame) {
  let pending = Buffer.alloc(0);
  for await (const chunk of stream) {
    pending = pending.length === 0 ? chunk : Buffer.concat([pending, chunk]);
    while (pending.length >= frameBytes) {
      await onFrame(pending.subarray(0, frameBytes));
      pending = pending.subarray(frameBytes);
    }
  }
  if (pending.length !== 0) fail(`视频解码尾部有 ${pending.length} 字节残帧`);
}

// 第一遍：逐帧变化与亮度，定位稳定段。只保留上一帧，避免整段视频驻留内存。
async function scanFrames(ffmpeg, video) {
  const frameBytes = ANALYSIS_WIDTH * ANALYSIS_HEIGHT * 3;
  const stats = [];
  let previous = null;
  const process = spawn(ffmpeg, ['-v', 'error', '-i', video, '-vf', `scale=${ANALYSIS_WIDTH}:${ANALYSIS_HEIGHT},format=rgb24`,
    '-fps_mode', 'passthrough', '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-'], { stdio: ['ignore', 'pipe', 'pipe'] });
  let stderr = '';
  process.stderr.on('data', (chunk) => { stderr += chunk; });
  const closed = new Promise((resolve, reject) => {
    process.on('error', reject);
    process.on('close', (code) => (code === 0 ? resolve() : reject(new Error(`ffmpeg 解码失败(${code})：${stderr.slice(-2000)}`))));
  });
  try {
    await readFrameStream(process.stdout, frameBytes, (frame) => {
      const copy = Buffer.from(frame); // 只保留本帧；上一帧继续用于比较。
      stats.push(frameStats(previous, copy, ANALYSIS_WIDTH, ANALYSIS_HEIGHT));
      previous = copy;
    });
    await closed;
  } catch (error) {
    if (process.exitCode === null) process.kill();
    throw error;
  }
  return stats;
}

// 第二遍：按索引取出稳定段代表帧，用于区域级像素比较。
async function extractFrames(ffmpeg, video, indices) {
  const frameBytes = ANALYSIS_WIDTH * ANALYSIS_HEIGHT * 3;
  const filter = indices.map((index) => `eq(n\\,${index})`).join('+');
  const process = spawn(ffmpeg, ['-v', 'error', '-i', video, '-vf', `select=${filter},scale=${ANALYSIS_WIDTH}:${ANALYSIS_HEIGHT},format=rgb24`,
    '-fps_mode', 'passthrough', '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-'], { stdio: ['ignore', 'pipe', 'pipe'] });
  let stderr = '';
  process.stderr.on('data', (chunk) => { stderr += chunk; });
  const chunks = [];
  for await (const chunk of process.stdout) chunks.push(chunk);
  const all = Buffer.concat(chunks);
  await new Promise((resolve, reject) => {
    process.once('close', (code) => (code === 0 ? resolve() : reject(new Error(`ffmpeg 取帧失败(${code})：${stderr.slice(-2000)}`))));
    process.once('error', reject);
  });
  if (all.length !== frameBytes * indices.length) fail(`稳定段代表帧数量不符：${all.length / frameBytes} != ${indices.length}`);
  const frames = [];
  for (let index = 0; index < indices.length; index += 1) frames.push(all.subarray(index * frameBytes, (index + 1) * frameBytes));
  return frames;
}

function analysisRect(windowInfo, stateRect) {
  if (!stateRect) fail('没有取得原生模态窗口矩形');
  const scaleX = ANALYSIS_WIDTH / (windowInfo.right - windowInfo.left);
  const scaleY = ANALYSIS_HEIGHT / (windowInfo.bottom - windowInfo.top);
  const left = (stateRect.x - windowInfo.left) * scaleX;
  const top = (stateRect.y - windowInfo.top) * scaleY;
  const right = (stateRect.x + stateRect.width - windowInfo.left) * scaleX;
  const bottom = (stateRect.y + stateRect.height - windowInfo.top) * scaleY;
  if (right - left < ANALYSIS_WIDTH * 0.1 || bottom - top < ANALYSIS_HEIGHT * 0.1) fail('原生模态窗口与录像区域不匹配');
  return { left, top, right, bottom };
}

// 稳定段只代表“画面没变化”的时间片；光标闪烁会把同一状态切成多段，先按与首帧的差异
// 分类成主界面/对话框，再合并同类相邻段，最后只对逻辑状态之间的过渡帧做逐帧判定。
async function analyzeRecording(ffmpeg, video, rect, margin, cycles) {
  const stats = await scanFrames(ffmpeg, video);
  if (stats.length < 30) fail(`录像帧数不足：${stats.length}`);
  const runs = stableRuns(stats, 2);
  if (runs.length < 2) fail(`稳定段不足：${runs.length}`);
  const runIndices = runs.map((run) => run.start + Math.floor(run.length / 2));
  const runFrames = await extractFrames(ffmpeg, video, runIndices);
  const classifications = classifyStates(runFrames[0], runFrames, ANALYSIS_WIDTH, ANALYSIS_HEIGHT);
  const states = mergeStates(runs, classifications);
  // 黑帧/淡出会形成额外的稳定段或非主非对话框画面；先收集逐段证据，再检查状态数量。
  const contentFailures = [];
  for (let index = 0; index < runs.length; index += 1) {
    if (runs[index].mean < 24 && runs[index].darkFraction > 0.8) {
      contentFailures.push({ kind: 'dark-run', run: index, start: runs[index].start, end: runs[index].end, mean: runs[index].mean, darkFraction: runs[index].darkFraction });
    }
  }
  const dialogRuns = classifications.map((value, index) => (value === 'dialog' ? index : -1)).filter((index) => index >= 0);
  if (dialogRuns.length === 0) contentFailures.push({ kind: 'no-dialog' });
  else {
    const dialogReference = runFrames[dialogRuns[0]];
    for (let index = 0; index < runs.length; index += 1) {
      const fraction = changedFraction(runFrames[index], classifications[index] === 'main' ? runFrames[0] : dialogReference, ANALYSIS_WIDTH, ANALYSIS_HEIGHT);
      const limit = classifications[index] === 'main' ? 0.02 : 0.05;
      if (fraction > limit) contentFailures.push({ kind: 'state-content-mismatch', run: index, classification: classifications[index], fraction: Math.round(fraction * 1000000) / 1000000, limit });
    }
  }
  // 每个开关周期严格对应“主界面→对话框→主界面”；多出或缺少状态都说明中间出现了异常画面。
  if (states.length !== cycles * 2 + 1) {
    contentFailures.push({ kind: 'state-count', actual: states.length, expected: cycles * 2 + 1, classifications: classifications.join(',') });
    fail(`逻辑状态数量不符：${JSON.stringify(contentFailures)}`);
  }
  const stateRuns = states.map((state) => {
    let best = state.runStart;
    for (let index = state.runStart + 1; index <= state.runEnd; index += 1) if (runs[index].length > runs[best].length) best = index;
    return best;
  });
  const stateIndices = stateRuns.map((run) => runs[run].start + Math.floor(runs[run].length / 2));
  const intermediateIndices = [];
  for (let index = 0; index + 1 < states.length; index += 1) {
    for (let frame = states[index].end + 1; frame < states[index + 1].start; frame += 1) intermediateIndices.push(frame);
  }
  if (intermediateIndices.length > 600) fail(`过渡帧过多：${intermediateIndices.length}`);
  const requested = [...new Set([...stateIndices, ...intermediateIndices])].sort((left, right) => left - right);
  const extracted = await extractFrames(ffmpeg, video, requested);
  const frameByIndex = new Map(requested.map((index, position) => [index, extracted[position]]));
  const stateFrames = stateIndices.map((index) => frameByIndex.get(index));
  const intermediate = [];
  for (let index = 0; index + 1 < states.length; index += 1) {
    for (let frame = states[index].end + 1; frame < states[index + 1].start; frame += 1) {
      intermediate.push({ index: frame, frame: frameByIndex.get(frame), previous: stateFrames[index], next: stateFrames[index + 1] });
    }
  }
  const timeline = assessModalTimeline({
    states: stateFrames.map((frame, index) => ({ className: states[index].className, frame })),
    intermediate, width: ANALYSIS_WIDTH, height: ANALYSIS_HEIGHT, rect, margin,
  });
  timeline.failures.push(...contentFailures);
  const mainStateChanges = [];
  for (let index = 2; index < states.length; index += 2) {
    const fraction = changedFraction(stateFrames[0], stateFrames[index], ANALYSIS_WIDTH, ANALYSIS_HEIGHT);
    mainStateChanges.push({ state: index, fraction: Math.round(fraction * 1000000) / 1000000 });
    if (fraction > 0.02) timeline.failures.push({ kind: 'main-changed', state: index, fraction });
  }
  const restoredFraction = changedFraction(stateFrames[0], stateFrames[stateFrames.length - 1], ANALYSIS_WIDTH, ANALYSIS_HEIGHT);
  const restoredLimit = 0.005;
  if (restoredFraction > restoredLimit) timeline.failures.push({ kind: 'main-not-restored', fraction: Math.round(restoredFraction * 1000000) / 1000000, limit: restoredLimit });
  return {
    stats, runs, states, stateRuns, stateIndices, stateFrames, intermediate,
    timeline: { ...timeline, passed: timeline.failures.length === 0 },
    restoredFraction: Math.round(restoredFraction * 1000000) / 1000000, restoredLimit, mainStateChanges,
  };
}

async function run(argv) {
  const config = parseArguments(argv);
  for (const [name, value] of [['exe', config.exe], ['playwright', config.playwrightModule], ['workspace', config.workspace]]) {
    if (!fs.existsSync(value)) fail(`${name} 不存在：${value}`);
  }
  const ffmpegVersion = spawnSync(config.ffmpeg, ['-version'], { encoding: 'utf8' });
  if (ffmpegVersion.status !== 0) fail('ffmpeg 不可用，请用 --ffmpeg 指定');
  fs.mkdirSync(config.output, { recursive: true });
  const recording = path.join(config.output, `modal-transition-${config.theme.toLowerCase()}${config.dpi ? `-${config.dpi}dpi` : ''}.mkv`);
  const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'Augit-modal-transition-'));
  const settings = path.join(temporary, 'settings.json');
  fs.writeFileSync(settings, '{}\n');
  const report = {
    verified: false, theme: config.theme, cycles: config.cycles, dpi: config.dpi,
    entry: 'isolated-explicit', exe: config.exe, workspace: config.workspace, recording, temporary,
    steps: [], limits: ['只覆盖 Format JSON 开关路径（所有普通模态共用 ShellModalHost）；未覆盖拖拽/缩放、背景刷新和系统睡眠恢复'],
  };
  // 记录实际执行身份，避免以后用旧产物或旧网页冒充本次证据。apphost(Augit.exe) 不随
  // 产品代码变化，必须同时记录实际加载的托管程序集。
  report.executable = { path: config.exe, sha256: sha256File(config.exe) };
  const appDirectory = path.dirname(config.exe);
  report.assemblies = Object.fromEntries(['Augit.dll', 'Augit.Core.dll', 'Augit.Infrastructure.dll']
    .map((name) => path.join(appDirectory, name))
    .filter((file) => fs.existsSync(file))
    .map((file) => [path.basename(file), sha256File(file)]));
  report.webAssets = Object.fromEntries(['web/src/live-data.js', 'web/src/mockup.js', 'web/src/mockup.css']
    .map((relative) => [relative, sha256File(path.join(config.repo, ...relative.split('/')))]));
  report.workspace = config.workspace;
  const port = await allocatePort();
  const userDataFolder = path.join(temporary, 'webview');
  const argumentsList = ['--web-root', path.join(config.repo, 'web'), '--workspace', config.workspace,
    '--settings-file', settings, '--user-data-folder', userDataFolder, '--theme', config.theme,
    '--width', '1180', '--height', '760', '--no-session-restore'];
  if (config.dpi !== null) argumentsList.push('--dpi', String(config.dpi));
  let app = null;
  let browser = null;
  let recorder = null;
  let recordingLog = '';
  let foregroundSamples = [];
  let page = null;
  const stopRecorder = async () => {
    if (!recorder || recorder.exitCode !== null) return;
    try { recorder.stdin.write('q'); } catch {}
    await new Promise((resolve) => { recorder.once('close', resolve); setTimeout(resolve, 5000); });
    if (recorder.exitCode === null) { try { recorder.kill(); } catch {} }
  };
  const cleanup = async () => {
    try { await stopRecorder(); } catch {}
    try { if (browser) await browser.close(); } catch {}
    if (app) {
      try {
        await new Promise((resolve) => {
          const closer = spawn('pwsh', ['-NoProfile', '-Command', `$p=Get-Process -Id ${app.pid} -ErrorAction Stop; if(-not $p.CloseMainWindow()){ throw 'close' }; if(-not $p.WaitForExit(8000)){ throw 'exit' }`], { windowsHide: true, stdio: 'ignore' });
          closer.once('close', resolve);
          setTimeout(() => { try { closer.kill(); } catch {} resolve(); }, 12000);
        });
        report.closeMethod = 'CloseMainWindow';
      } catch {
        try { spawnSync('taskkill', ['/PID', String(app.pid), '/T', '/F']); report.closeMethod = 'taskkill'; } catch {}
      }
    }
    // WebView2 子进程退出会稍晚于主窗，浏览器缓存文件可能仍被占用；有界重试后如实登记。
    for (let attempt = 0; attempt < 20; attempt += 1) {
      try { fs.rmSync(temporary, { recursive: true, force: true }); } catch {}
      if (!fs.existsSync(temporary)) break;
      await new Promise((resolve) => setTimeout(resolve, 400));
    }
    report.temporaryRemoved = !fs.existsSync(temporary);
    try {
      report.appExited = spawnSync('pwsh', ['-NoProfile', '-Command', `if (Get-Process -Id ${app?.pid ?? 0} -ErrorAction SilentlyContinue) { 1 } else { 0 }`], { encoding: 'utf8' }).stdout.trim() === '0';
    } catch { report.appExited = false; }
  };
  try {
    const env = { ...process.env, WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: `--remote-debugging-port=${port}` };
    app = spawn(config.exe, argumentsList, { cwd: config.repo, env, stdio: 'ignore' });
    report.pid = app.pid;
    report.port = port;
    let deadline = Date.now() + 40000;
    while (Date.now() < deadline) {
      try {
        const { chromium } = require(config.playwrightModule);
        browser = await chromium.connectOverCDP(`http://127.0.0.1:${port}`);
        break;
      } catch { await new Promise((resolve) => setTimeout(resolve, 250)); }
    }
    if (!browser) fail('CDP 在 40 秒内未就绪');
    for (let attempt = 0; attempt < 80 && !page; attempt += 1) {
      page = browser.contexts()[0].pages().find((candidate) => { try { return candidate.url().includes('index.html'); } catch { return false; } });
      if (!page) await new Promise((resolve) => setTimeout(resolve, 100));
    }
    if (!page) fail('找不到主页面');
    await page.waitForSelector('.augit-window', { timeout: 30000 });
    await page.waitForFunction(() => window.__augitLive && window.__augitNativeModalAvailable === true, { timeout: 30000 });
    await new Promise((resolve) => setTimeout(resolve, 1200));
    const windowInfo = measureWindow(app.pid);
    report.window = { ...windowInfo, viewport: await page.evaluate(() => [innerWidth, innerHeight, devicePixelRatio]) };
    report.window.topmostForCapture = true;
    const duration = config.cycles * 16 + 4;
    const recorderArguments = ['-hide_banner', '-y', '-f', 'lavfi', '-i',
      `ddagrab=output_idx=0:draw_mouse=0:framerate=60:dup_frames=1:output_fmt=bgra:offset_x=${windowInfo.left}:offset_y=${windowInfo.top}:video_size=${windowInfo.right - windowInfo.left}x${windowInfo.bottom - windowInfo.top},hwdownload,format=bgra`,
      '-t', String(duration), '-c:v', 'ffv1', '-level', '3', '-pix_fmt', 'bgr0', '-enc_time_base', '1:1000000', '-fps_mode', 'passthrough', recording];
    recorder = spawn(config.ffmpeg, recorderArguments, { stdio: ['pipe', 'ignore', 'pipe'] });
    recorder.stderr.on('data', (chunk) => { recordingLog += chunk; });
    await new Promise((resolve) => setTimeout(resolve, 1200));
    const begin = Date.now();
    const events = [];
    let modalRect = null;
    let errors = [];
    const sample = async (label) => {
      const state = await page.evaluate(() => ({
        dialogs: document.querySelectorAll('.dialog').length,
        nativeActive: window.__augitNativeModalActive === true,
        nativeWindow: document.querySelector('.json-format-dialog')?.dataset.nativeWindow === 'true',
        last: window.__augitNativeModalLastState || null,
        errors: (window.__augitErrors || []).slice(),
      }));
      events.push({ label, t: Date.now() - begin, state });
      if (state.last?.rect) modalRect = state.last.rect;
      errors = state.errors;
      return state;
    };
    const waitFor = async (label, predicate, timeoutMs) => {
      const stop = Date.now() + timeoutMs;
      while (Date.now() < stop) {
        const state = await sample(label);
        if (predicate(state)) return state;
        await new Promise((resolve) => setTimeout(resolve, 120));
      }
      fail(`${label} 等待超时`);
    };
    await waitFor('initial', (state) => !state.nativeActive && state.dialogs === 0 && state.errors.length === 0, 20000);
    for (let cycle = 0; cycle < config.cycles; cycle += 1) {
      await sample(`cycle${cycle}-before-open`);
      await page.keyboard.press('Control+Shift+J');
      await waitFor(`cycle${cycle}-opened`, (state) => state.nativeActive && state.nativeWindow, 20000);
      await new Promise((resolve) => setTimeout(resolve, 900));
      await sample(`cycle${cycle}-open-stable`);
      await page.keyboard.press('Escape');
      await waitFor(`cycle${cycle}-closed`, (state) => !state.nativeActive && state.dialogs === 0, 20000);
      await new Promise((resolve) => setTimeout(resolve, 900));
      await sample(`cycle${cycle}-closed-stable`);
      foregroundSamples.push({ cycle, foregroundPid: foregroundProcessId() });
    }
    await sample('final');
    await stopRecorder();
    report.events = events;
    report.modalRectPhysical = modalRect;
    report.pageErrors = errors;
    if (errors.length > 0) fail(`页面错误：${errors.join(';')}`);
    // 测试窗口在 measureWindow 时已置顶，采样区域由窗口自身保证；前台窗口只记录不门禁：
    // 模态切换期间系统可能把前台交给其它应用，而置顶的测试窗仍不会被其覆盖。
    report.foregroundSamples = foregroundSamples;
    const scale = ANALYSIS_WIDTH / (windowInfo.right - windowInfo.left);
    const rect = analysisRect(windowInfo, modalRect);
    const margin = Math.max(0, Math.round(SHADOW_MARGIN_PHYSICAL * scale));
    let analysis;
    try {
      analysis = await analyzeRecording(config.ffmpeg, recording, rect, margin, config.cycles);
    } catch (error) {
      // 失败原因要落在 result.json 里；控制台输出只用于即时定位。
      report.analysisError = String((error && error.message) || error);
      throw error;
    }
    report.analysis = {
      frameCount: analysis.stats.length, analysisWidth: ANALYSIS_WIDTH, analysisHeight: ANALYSIS_HEIGHT,
      modalRectAnalysis: rect, shadowMarginAnalysis: margin,
      runs: analysis.runs,
      states: analysis.states.map((state, index) => ({
        className: state.className, start: state.start, end: state.end,
        representative: analysis.stateIndices[index], runStart: state.runStart, runEnd: state.runEnd,
      })),
      stateFrames: analysis.stateIndices,
      intermediateFrames: analysis.intermediate.map((item) => item.index),
      intermediary: analysis.timeline.transitions,
      outsideChanged: analysis.timeline.outsideChangedFractions,
      classifications: analysis.timeline.classifications,
      mainStateChanges: analysis.mainStateChanges,
      restoredFraction: analysis.restoredFraction, restoredLimit: analysis.restoredLimit,
      framesMin: Math.min(...analysis.stats.map((item) => item.mean)),
      framesMax: Math.max(...analysis.stats.map((item) => item.mean)),
      failures: analysis.timeline.failures,
    };
    if (!analysis.timeline.passed) fail(`模态开关逐帧判据失败：${JSON.stringify(analysis.timeline.failures)}`);
    report.verified = true;
    report.limitation = '只验证本机本 DPI 下 Format JSON 的打开/关闭逐帧表现；不代表其它模态、背景刷新或跨 DPI 场景。';
  } finally {
    try { await cleanup(); } catch {}
    report.recordingLog = recordingLog.slice(-2000);
    fs.writeFileSync(path.join(config.output, 'result.json'), JSON.stringify(report, null, 2) + '\n');
  }
  console.log(`PASS 开关 ${config.cycles} 个周期，分析帧 ${report.analysis?.frameCount}`);
  return report;
}

if (require.main === module) {
  run(process.argv.slice(2)).catch((error) => {
    console.error(error.message || error);
    process.exitCode = 1;
  });
}

module.exports = { parseArguments, analysisRect };
