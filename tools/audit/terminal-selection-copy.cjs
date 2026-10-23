// 终端鼠标选区 → Ctrl+C 复制的正常入口证据。
//
// 真实外壳打开 Terminal，执行 echo COPY_TOKEN_12345；真实鼠标拖选/三击选行，
// Ctrl+C 后读取 Windows 剪贴板。CDP 只读行几何/选区节点与截图。
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const net = require('node:net');
const { execFile } = require('node:child_process');
const execute = require('node:util').promisify(execFile);
const { chromium } = require(process.argv[2]);
const output = path.resolve(process.argv[3] || 'artifacts/terminal-selection-copy-2026-10-24');
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

async function waitWindow(pid) {
  const script = `$deadline=(Get-Date).AddSeconds(20); while((Get-Date)-lt $deadline){$h=(Get-Process -Id ${pid} -ErrorAction SilentlyContinue).MainWindowHandle; if($h -ne 0){Write-Output $h; exit 0}; Start-Sleep -Milliseconds 100}; exit 2`;
  return Number((await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 })).stdout.trim());
}
async function topmost(hwnd) {
  const script = `Add-Type @"
using System; using System.Runtime.InteropServices;
public static class ST { [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c); [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int c); [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h); [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int cx,int cy,uint f); [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); }
"@
try{[void][ST]::SetProcessDpiAwarenessContext([IntPtr](-4))}catch{}
$h=[IntPtr]${hwnd}; [void][ST]::ShowWindow($h,5); [void][ST]::BringWindowToTop($h); [void][ST]::SetWindowPos($h,[IntPtr](-1),0,0,0,0,0x0003); [void][ST]::SetForegroundWindow($h); Start-Sleep -Milliseconds 300`;
  await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 });
}
async function clickCss(hwnd, point, label) {
  // 168 DPI: 显式计算客户区原点 + DPI，避免 DPI-unaware SetCursorPos 偏移。
  const script = `Add-Type @"
using System; using System.Runtime.InteropServices;
public static class SC { [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c); [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h,out Rect r); [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h,ref Pt p); [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h); [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y); [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e); [StructLayout(LayoutKind.Sequential)] public struct Rect{public int L,T,R,B;} [StructLayout(LayoutKind.Sequential)] public struct Pt{public int X,Y;} }
"@
try{[void][SC]::SetProcessDpiAwarenessContext([IntPtr](-4))}catch{}
$r=New-Object SC+Rect; $o=New-Object SC+Pt; [void][SC]::GetClientRect([IntPtr]${hwnd},[ref]$r); [void][SC]::ClientToScreen([IntPtr]${hwnd},[ref]$o); $s=[SC]::GetDpiForWindow([IntPtr]${hwnd})/96.0
$x=[int][Math]::Round($o.X+${point.x}*$s); $y=[int][Math]::Round($o.Y+${point.y}*$s)
[void][SC]::SetCursorPos($x,$y); [SC]::mouse_event(2,0,0,0,[UIntPtr]::Zero); [SC]::mouse_event(4,0,0,0,[UIntPtr]::Zero); Write-Output "$x,$y"`;
  const result = await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 });
  return { label, point, screen: result.stdout.trim() };
}
async function dragCss(hwnd, from, to, label) {
  const script = `Add-Type @"
using System; using System.Runtime.InteropServices;
public static class SD { [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c); [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h,out Rect r); [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h,ref Pt p); [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h); [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y); [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e); [StructLayout(LayoutKind.Sequential)] public struct Rect{public int L,T,R,B;} [StructLayout(LayoutKind.Sequential)] public struct Pt{public int X,Y;} }
"@
try{[void][SD]::SetProcessDpiAwarenessContext([IntPtr](-4))}catch{}
$r=New-Object SD+Rect; $o=New-Object SD+Pt; [void][SD]::GetClientRect([IntPtr]${hwnd},[ref]$r); [void][SD]::ClientToScreen([IntPtr]${hwnd},[ref]$o); $s=[SD]::GetDpiForWindow([IntPtr]${hwnd})/96.0
$sx=[int][Math]::Round($o.X+${from.x}*$s); $sy=[int][Math]::Round($o.Y+${from.y}*$s); $ex=[int][Math]::Round($o.X+${to.x}*$s); $ey=[int][Math]::Round($o.Y+${to.y}*$s)
[void][SD]::SetCursorPos($sx,$sy); [SD]::mouse_event(2,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 80
for($i=1;$i -le 8;$i++){ [void][SD]::SetCursorPos([int]($sx+($ex-$sx)*$i/8),[int]($sy+($ey-$sy)*$i/8)); Start-Sleep -Milliseconds 30 }
[SD]::mouse_event(4,0,0,0,[UIntPtr]::Zero); Write-Output "$sx,$sy -> $ex,$ey"`;
  const result = await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 });
  return { label, from, to, screen: result.stdout.trim() };
}
async function tripleClickCss(hwnd, point) {
  const script = `Add-Type @"
using System; using System.Runtime.InteropServices;
public static class ST3 { [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c); [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h,out Rect r); [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h,ref Pt p); [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h); [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y); [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e); [StructLayout(LayoutKind.Sequential)] public struct Rect{public int L,T,R,B;} [StructLayout(LayoutKind.Sequential)] public struct Pt{public int X,Y;} }
"@
try{[void][ST3]::SetProcessDpiAwarenessContext([IntPtr](-4))}catch{}
$r=New-Object ST3+Rect; $o=New-Object ST3+Pt; [void][ST3]::GetClientRect([IntPtr]${hwnd},[ref]$r); [void][ST3]::ClientToScreen([IntPtr]${hwnd},[ref]$o); $s=[ST3]::GetDpiForWindow([IntPtr]${hwnd})/96.0
$x=[int][Math]::Round($o.X+${point.x}*$s); $y=[int][Math]::Round($o.Y+${point.y}*$s)
[void][ST3]::SetCursorPos($x,$y)
1..3 | ForEach-Object { [ST3]::mouse_event(2,0,0,0,[UIntPtr]::Zero); [ST3]::mouse_event(4,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 60 }
Write-Output "$x,$y"`;
  const result = await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 });
  return { point, screen: result.stdout.trim() };
}
async function pasteAndEnter(text) {
  const literal = String(text).replaceAll("'", "''");
  const script = `Add-Type @"
using System; using System.Runtime.InteropServices;
public static class SK { [DllImport("user32.dll")] public static extern void keybd_event(byte vk,byte scan,uint f,UIntPtr e); }
"@
$old=Get-Clipboard -Raw -ErrorAction SilentlyContinue; Set-Clipboard -Value '${literal}'
[SK]::keybd_event(0x11,0,0,[UIntPtr]::Zero); [SK]::keybd_event(0x56,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 40; [SK]::keybd_event(0x56,0,2,[UIntPtr]::Zero); [SK]::keybd_event(0x11,0,2,[UIntPtr]::Zero); Start-Sleep -Milliseconds 120; [SK]::keybd_event(0x0D,0,0,[UIntPtr]::Zero); [SK]::keybd_event(0x0D,0,2,[UIntPtr]::Zero)
if($null -ne $old){Set-Clipboard -Value $old}`;
  await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 });
}
async function copyShortcut(hwnd) {
  const script = `Add-Type @"
using System; using System.Runtime.InteropServices;
public static class CK { [DllImport("user32.dll")] public static extern void keybd_event(byte vk,byte scan,uint f,UIntPtr e); [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); }
"@
[CK]::SetForegroundWindow([IntPtr]${hwnd}) | Out-Null
# xterm 的复制是 Ctrl+Shift+C（或 Ctrl+Insert），不是 Ctrl+C。
[CK]::keybd_event(0x11,0,0,[UIntPtr]::Zero); [CK]::keybd_event(0x10,0,0,[UIntPtr]::Zero); [CK]::keybd_event(0x43,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 40; [CK]::keybd_event(0x43,0,2,[UIntPtr]::Zero); [CK]::keybd_event(0x10,0,2,[UIntPtr]::Zero); [CK]::keybd_event(0x11,0,2,[UIntPtr]::Zero)`;
  await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 });
}
async function closeApp(pid) {
  const script = `$p=Get-Process -Id ${pid} -ErrorAction Stop; $p.CloseMainWindow()|Out-Null; $d=(Get-Date).AddSeconds(20); while(-not $p.HasExited -and (Get-Date)-lt $d){Start-Sleep -Milliseconds 100}; if($p.HasExited){exit 0}; exit 2`;
  try { await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 }); return true; } catch { return false; }
}
async function connect(port) {
  for (let i=0;i<160;i++){try{return await chromium.connectOverCDP('http://127.0.0.1:'+port)}catch{await delay(250)}}
  throw new Error('无法连接 CDP');
}
async function pageOf(browser) {
  for (let i=0;i<120;i++){const p=browser.contexts().flatMap(c=>c.pages()).find(x=>x.url().includes('index.html'));if(p)return p;await delay(250)}
  throw new Error('找不到页面');
}
async function launch(workspace, settings, port) {
  const literal=(v)=>"'"+String(v).replaceAll("'","''")+"'";
  const cmd='$ErrorActionPreference="Stop"; $p=& '+literal(path.resolve('tools/start-augit.ps1'))+' -Workspace '+literal(workspace)+' -SettingsFile '+literal(settings)+' -SkipBuild; if(-not $p.Id){throw "no pid"}; Write-Output ("AUGIT_PID="+$p.Id)';
  const r=await execute('powershell.exe',['-NoProfile','-ExecutionPolicy','Bypass','-Command',cmd],{env:{...process.env,WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS:'--remote-debugging-port='+port},timeout:120000});
  return Number(/AUGIT_PID=(\d+)/.exec(r.stdout)?.[1]);
}

(async()=>{
  await fs.mkdir(output,{recursive:true});
  const temp=await fs.mkdtemp(path.join(os.tmpdir(),'Augit-term-select-'));
  const workspace=path.join(temp,'workspace'); const settings=path.join(temp,'settings.json');
  let browser=null,pid=0,closed=false;
  const report={generatedAt:new Date().toISOString()};
  try{
    await fs.mkdir(workspace);
    await fs.writeFile(path.join(workspace,'README.md'),'# terminal selection\n');
    await execute('git',['init','-b','main'],{cwd:workspace,windowsHide:true});
    await execute('git',['config','user.name','Terminal Selection'],{cwd:workspace,windowsHide:true});
    await execute('git',['config','user.email','term-select@example.invalid'],{cwd:workspace,windowsHide:true});
    await execute('git',['add','README.md'],{cwd:workspace,windowsHide:true});
    await execute('git',['commit','-m','test: terminal selection fixture'],{cwd:workspace,windowsHide:true});
    const source=JSON.parse(await fs.readFile(path.join(process.env.LOCALAPPDATA,'Augit/settings.json'),'utf8'));
    source.lastWorkspace=workspace; source.openFiles=[]; source.activeFile=null;
    await fs.writeFile(settings,JSON.stringify(source,null,2)+'\n');
    const listener=net.createServer(); await new Promise(r=>listener.listen(0,'127.0.0.1',r)); const port=listener.address().port; await new Promise(r=>listener.close(r));
    report.port=port; pid=await launch(workspace,settings,port); const hwnd=await waitWindow(pid); report.pid=pid; report.hwnd=hwnd; await topmost(hwnd);
    browser=await connect(port); const page=await pageOf(browser);
    await page.waitForFunction('window.__augitReady===true && !!window.__augitLive',null,{timeout:30000});
    const railRect=await page.evaluate(()=>{const n=document.querySelector('.rail-button[data-tool="terminal"]');const r=n.getBoundingClientRect();return{x:r.x,y:r.y,width:r.width,height:r.height}});
    await clickCss(hwnd,{x:railRect.x+railRect.width/2,y:railRect.y+railRect.height/2},'terminal-rail');
    await page.waitForSelector('.terminal-tool .terminal-view',{timeout:20000});
    await delay(2500);
    const viewRect=await page.evaluate(()=>{const n=document.querySelector('.terminal-tool .terminal-view');const r=n.getBoundingClientRect();return{x:r.x,y:r.y,width:r.width,height:r.height}});
    await clickCss(hwnd,{x:viewRect.x+30,y:viewRect.y+20},'terminal-focus');
    await pasteAndEnter('echo COPY_TOKEN_12345');
    await delay(1500);
    const rows=await page.evaluate(()=>[...document.querySelectorAll('.xterm-rows > div')].map((n,i)=>{const r=n.getBoundingClientRect();return{index:i,text:n.textContent,x:r.x,y:r.y,width:r.width,height:r.height}}));
    const outputRow=rows.find(r=>r.text.trim()==='COPY_TOKEN_12345')||null;
    const tokenRow=outputRow||rows.find(r=>r.text.includes('COPY_TOKEN'))||null;
    const point=tokenRow?{x:tokenRow.x+4,y:tokenRow.y+tokenRow.height/2}:{x:viewRect.x+30,y:viewRect.y+viewRect.height-40};
    const drag=await dragCss(hwnd,point,{x:point.x+140,y:point.y},'drag-select');
    await delay(300);
    const selection=await page.evaluate(()=>({
      nodes:document.querySelectorAll('.xterm-selection div').length,
      text:document.querySelector('.xterm-selection')?.textContent||null,
    }));
    await page.screenshot({path:path.join(output,'terminal-selection.png')});
    await copyShortcut(hwnd);
    await delay(300);
    const clipboard1=(await execute('powershell.exe',['-NoProfile','-Command','Get-Clipboard -Raw'],{timeout:30000})).stdout.trim();
    let triple=null,clipboard2=null,selection2=null;
    if(!clipboard1.includes('COPY_TOKEN_12345')){
      triple=await tripleClickCss(hwnd,{x:point.x+40,y:point.y});
      await delay(250);
      selection2=await page.evaluate(()=>({nodes:document.querySelectorAll('.xterm-selection div').length,text:document.querySelector('.xterm-selection')?.textContent||null}));
      await copyShortcut(hwnd); await delay(300);
      clipboard2=(await execute('powershell.exe',['-NoProfile','-Command','Get-Clipboard -Raw'],{timeout:30000})).stdout.trim();
      await page.screenshot({path:path.join(output,'terminal-selection-triple.png')});
    }
    report.result={rows,tokenRow,point,drag,selection,clipboard1,triple,selection2,clipboard2,ok:clipboard1.includes('COPY_TOKEN_12345')||(clipboard2||'').includes('COPY_TOKEN_12345')};
    await browser.close(); browser=null; closed=await closeApp(pid); report.closedNormally=closed; await delay(500); report.pidAlive=(()=>{try{process.kill(pid,0);return true}catch{return false}})();
  }catch(error){report.error=String(error&&error.stack||error);process.exitCode=1}
  finally{if(browser)await browser.close().catch(()=>{});if(pid&&!closed)await closeApp(pid).catch(()=>{});await fs.writeFile(path.join(output,'terminal-selection-copy.json'),JSON.stringify(report,null,2)+'\n');await fs.rm(temp,{recursive:true,force:true}).catch(()=>{})}
  console.log(JSON.stringify(report.result||{error:report.error||null},null,2));
})();
