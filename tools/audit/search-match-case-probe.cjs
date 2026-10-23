// 诊断：搜索浮层的 Match case 按钮为何在部分正常入口点击中不切换。
// 只读 DOM + 三种输入路径（CDP click / DOM click / DPI-aware OS click），不改产品。
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const net = require('node:net');
const { execFile } = require('node:child_process');
const execute = require('node:util').promisify(execFile);
const { chromium } = require(process.argv[2]);
const output = path.resolve(process.argv[3] || 'artifacts/search-match-case-probe-2026-10-24');
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

async function launch(workspace, settings, port) {
  const literal = (value) => "'" + String(value).replaceAll("'", "''") + "'";
  const cmd = '$ErrorActionPreference="Stop"; $p = & ' + literal(path.resolve('tools/start-augit.ps1'))
    + ' -Workspace ' + literal(workspace) + ' -SettingsFile ' + literal(settings) + ' -SkipBuild; if(-not $p.Id){throw "no pid"}; Write-Output ("AUGIT_PID="+$p.Id)';
  const r = await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', cmd], {
    env: { ...process.env, WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: '--remote-debugging-port=' + port }, timeout: 120000,
  });
  return Number(/AUGIT_PID=(\d+)/.exec(r.stdout)?.[1]);
}
async function waitWindow(pid) {
  const script = `$d=(Get-Date).AddSeconds(20); while((Get-Date)-lt $d){$h=(Get-Process -Id ${pid} -ErrorAction SilentlyContinue).MainWindowHandle; if($h -ne 0){Write-Output $h; exit 0}; Start-Sleep -Milliseconds 100}; exit 2`;
  return Number((await execute('powershell.exe', ['-NoProfile', '-Command', script], { timeout: 30000 })).stdout.trim());
}
async function topmost(hwnd) {
  const script = `Add-Type @"
using System; using System.Runtime.InteropServices;
public static class MT { [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c); [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int c); [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h); [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int cx,int cy,uint f); [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); }
"@
try{[void][MT]::SetProcessDpiAwarenessContext([IntPtr](-4))}catch{}
$h=[IntPtr]${hwnd}; [void][MT]::ShowWindow($h,5); [void][MT]::BringWindowToTop($h); [void][MT]::SetWindowPos($h,[IntPtr](-1),0,0,0,0,0x0003); [void][MT]::SetForegroundWindow($h); Start-Sleep -Milliseconds 300`;
  await execute('powershell.exe', ['-NoProfile', '-Command', script], { timeout: 30000 });
}
async function osClick(hwnd, cx, cy) {
  const script = `Add-Type @"
using System; using System.Runtime.InteropServices;
public static class MC { [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c); [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h,out Rect r); [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h,ref Pt p); [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h); [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y); [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e); [StructLayout(LayoutKind.Sequential)] public struct Rect{public int L,T,R,B;} [StructLayout(LayoutKind.Sequential)] public struct Pt{public int X,Y;} }
"@
try{[void][MC]::SetProcessDpiAwarenessContext([IntPtr](-4))}catch{}
$r=New-Object MC+Rect; $o=New-Object MC+Pt; [void][MC]::GetClientRect([IntPtr]${hwnd},[ref]$r); [void][MC]::ClientToScreen([IntPtr]${hwnd},[ref]$o); $s=[MC]::GetDpiForWindow([IntPtr]${hwnd})/96.0
$x=[int][Math]::Round($o.X+${cx}*$s); $y=[int][Math]::Round($o.Y+${cy}*$s)
[void][MC]::SetCursorPos($x,$y); [MC]::mouse_event(2,0,0,0,[UIntPtr]::Zero); [MC]::mouse_event(4,0,0,0,[UIntPtr]::Zero); Write-Output "$x,$y"`;
  return (await execute('powershell.exe', ['-NoProfile', '-Command', script], { timeout: 30000 })).stdout.trim();
}
async function closeApp(pid) {
  const script = `$p=Get-Process -Id ${pid} -ErrorAction Stop; $p.CloseMainWindow()|Out-Null; $d=(Get-Date).AddSeconds(20); while(-not $p.HasExited -and (Get-Date)-lt $d){Start-Sleep -Milliseconds 100}; if($p.HasExited){exit 0}; exit 2`;
  try { await execute('powershell.exe', ['-NoProfile', '-Command', script], { timeout: 30000 }); return true; } catch { return false; }
}
async function connect(port) {
  for (let i=0;i<160;i++){try{return await chromium.connectOverCDP('http://127.0.0.1:'+port)}catch{await delay(250)}}
  throw new Error('CDP fail');
}
(async()=>{
  await fs.mkdir(output,{recursive:true});
  const temp=await fs.mkdtemp(path.join(os.tmpdir(),'Augit-search-case-'));
  const workspace=path.join(temp,'workspace'); const settings=path.join(temp,'settings.json');
  let browser=null,pid=0,closed=false; const report={};
  try{
    await fs.mkdir(workspace); await fs.writeFile(path.join(workspace,'a.txt'),'alpha\n');
    await execute('git',['init','-b','main'],{cwd:workspace,windowsHide:true});
    await execute('git',['config','user.name','Case Probe'],{cwd:workspace,windowsHide:true});
    await execute('git',['config','user.email','case@example.invalid'],{cwd:workspace,windowsHide:true});
    await execute('git',['add','a.txt'],{cwd:workspace,windowsHide:true});
    await execute('git',['commit','-m','test: case probe'],{cwd:workspace,windowsHide:true});
    const source=JSON.parse(await fs.readFile(path.join(process.env.LOCALAPPDATA,'Augit/settings.json'),'utf8'));
    source.lastWorkspace=workspace; source.openFiles=[]; source.activeFile=null;
    await fs.writeFile(settings,JSON.stringify(source,null,2)+'\n');
    const listener=net.createServer(); await new Promise(r=>listener.listen(0,'127.0.0.1',r)); const port=listener.address().port; await new Promise(r=>listener.close(r));
    pid=await launch(workspace,settings,port); const hwnd=await waitWindow(pid); report.pid=pid; report.hwnd=hwnd; await topmost(hwnd);
    browser=await connect(port); const page=browser.contexts().flatMap(c=>c.pages()).find(x=>x.url().includes('index.html'));
    await page.waitForFunction('window.__augitReady===true && !!window.__augitLive',null,{timeout:30000});
    await page.keyboard.press('Control+Shift+F');
    await page.waitForSelector('.search-overlay .search-field',{timeout:15000});
    await delay(800);
    const geom=await page.evaluate(()=>{
      const b=document.querySelector('.search-option[aria-label="Match case"]');
      const r=b.getBoundingClientRect();
      const cx=r.x+r.width/2, cy=r.y+r.height/2;
      const hit=document.elementFromPoint(cx,cy);
      return {rect:{x:r.x,y:r.y,width:r.width,height:r.height},center:{x:cx,y:cy},hit:hit?{tag:hit.tagName,cls:hit.className,label:hit.getAttribute('aria-label')}:null,pointer:getComputedStyle(b).pointerEvents,pressed:b.getAttribute('aria-pressed')};
    });
    report.geom=geom;
    report.options=await page.evaluate(()=>[...document.querySelectorAll('.search-option,.search-ignored')].map(n=>{const r=n.getBoundingClientRect();const cx=r.x+r.width/2,cy=r.y+r.height/2;const hit=document.elementFromPoint(cx,cy);return{tag:n.tagName,cls:n.className,label:n.getAttribute('aria-label')||n.innerText.trim(),rect:{x:r.x,y:r.y,w:r.width,h:r.height},center:{x:cx,y:cy},hit:hit?{tag:hit.tagName,label:hit.getAttribute('aria-label'),cls:hit.className}:null}}));
    const read=async()=>page.evaluate(()=>({pressed:document.querySelector('.search-option[aria-label="Match case"]')?.getAttribute('aria-pressed'),words:document.querySelector('.search-option[aria-label="Words"]')?.getAttribute('aria-pressed'),focus:document.activeElement?.getAttribute('aria-label')||document.activeElement?.tagName}));
    // 1 DOM click
    await page.evaluate(()=>document.querySelector('.search-option[aria-label="Match case"]').click());
    await delay(300); report.afterDom=await read();
    // 2 OS DPI-aware click
    const osScreen=await osClick(hwnd,geom.center.x,geom.center.y);
    await delay(500); report.afterOs={screen:osScreen,...await read()};
    await page.screenshot({path:path.join(output,'after-os.png')});
    await browser.close(); browser=null; report.closedNormally=await closeApp(pid); closed=report.closedNormally; await delay(500); report.pidAlive=(()=>{try{process.kill(pid,0);return true}catch{return false}})();
  }catch(error){report.error=String(error&&error.stack||error);process.exitCode=1}
  finally{if(browser)await browser.close().catch(()=>{});if(pid&&!closed)await closeApp(pid).catch(()=>{});await fs.writeFile(path.join(output,'search-match-case-probe.json'),JSON.stringify(report,null,2)+'\n');await fs.rm(temp,{recursive:true,force:true}).catch(()=>{})}
  console.log(JSON.stringify(report,null,2));
})();
