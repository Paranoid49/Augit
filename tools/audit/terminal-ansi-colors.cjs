// 终端 ANSI 16 色的受控输出与逐行取样（浅色）。
//
// 真实外壳 Terminal，分两批输出 SGR 30–37 / 90–97 的 C30…C97 行；每批截图并记录
// `.xterm-rows` 行几何。像素分析由 tools/audit/analyze-terminal-ansi.py 完成，
// 将每行核心色与 terminal-palette.js 的浅色值逐项对照。
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const net = require('node:net');
const { execFile } = require('node:child_process');
const execute = require('node:util').promisify(execFile);
const { chromium } = require(process.argv[2]);
const output = path.resolve(process.argv[3] || 'artifacts/terminal-ansi-colors-2026-10-24');
const dark = process.argv.includes('--dark');
const extended = process.argv.includes('--extended');
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

async function waitWindow(pid) {
  const script = `$deadline=(Get-Date).AddSeconds(20); while((Get-Date)-lt $deadline){$h=(Get-Process -Id ${pid} -ErrorAction SilentlyContinue).MainWindowHandle; if($h -ne 0){Write-Output $h; exit 0}; Start-Sleep -Milliseconds 100}; exit 2`;
  return Number((await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 })).stdout.trim());
}
async function topmost(hwnd) {
  const script = `Add-Type @"
using System; using System.Runtime.InteropServices;
public static class AT { [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c); [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int c); [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h); [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int cx,int cy,uint f); [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); }
"@
try{[void][AT]::SetProcessDpiAwarenessContext([IntPtr](-4))}catch{}
$h=[IntPtr]${hwnd}; [void][AT]::ShowWindow($h,5); [void][AT]::BringWindowToTop($h); [void][AT]::SetWindowPos($h,[IntPtr](-1),0,0,0,0,0x0003); [void][AT]::SetForegroundWindow($h); Start-Sleep -Milliseconds 300`;
  await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 });
}
async function clickCss(hwnd, point) {
  const script = `Add-Type @"
using System; using System.Runtime.InteropServices;
public static class AC { [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c); [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h,out Rect r); [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h,ref Pt p); [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h); [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y); [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e); [StructLayout(LayoutKind.Sequential)] public struct Rect{public int L,T,R,B;} [StructLayout(LayoutKind.Sequential)] public struct Pt{public int X,Y;} }
"@
try{[void][AC]::SetProcessDpiAwarenessContext([IntPtr](-4))}catch{}
$r=New-Object AC+Rect; $o=New-Object AC+Pt; [void][AC]::GetClientRect([IntPtr]${hwnd},[ref]$r); [void][AC]::ClientToScreen([IntPtr]${hwnd},[ref]$o); $s=[AC]::GetDpiForWindow([IntPtr]${hwnd})/96.0
$x=[int][Math]::Round($o.X+${point.x}*$s); $y=[int][Math]::Round($o.Y+${point.y}*$s)
[void][AC]::SetCursorPos($x,$y); [AC]::mouse_event(2,0,0,0,[UIntPtr]::Zero); [AC]::mouse_event(4,0,0,0,[UIntPtr]::Zero)`;
  await execute('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { timeout: 30000 });
}
async function pasteAndEnter(text) {
  const literal = String(text).replaceAll("'", "''");
  const script = `Add-Type @"
using System; using System.Runtime.InteropServices;
public static class AK { [DllImport("user32.dll")] public static extern void keybd_event(byte vk,byte scan,uint f,UIntPtr e); }
"@
$old=Get-Clipboard -Raw -ErrorAction SilentlyContinue; Set-Clipboard -Value '${literal}'
[AK]::keybd_event(0x11,0,0,[UIntPtr]::Zero); [AK]::keybd_event(0x56,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 40; [AK]::keybd_event(0x56,0,2,[UIntPtr]::Zero); [AK]::keybd_event(0x11,0,2,[UIntPtr]::Zero); Start-Sleep -Milliseconds 120; [AK]::keybd_event(0x0D,0,0,[UIntPtr]::Zero); [AK]::keybd_event(0x0D,0,2,[UIntPtr]::Zero)
if($null -ne $old){Set-Clipboard -Value $old}`;
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
  const temp=await fs.mkdtemp(path.join(os.tmpdir(),'Augit-term-ansi-'));
  const workspace=path.join(temp,'workspace'); const settings=path.join(temp,'settings.json');
  let browser=null,pid=0,closed=false;
  const report={generatedAt:new Date().toISOString(),batches:[]};
  try{
    await fs.mkdir(workspace);
    await fs.writeFile(path.join(workspace,'README.md'),'# ansi colors\n');
    await execute('git',['init','-b','main'],{cwd:workspace,windowsHide:true});
    await execute('git',['config','user.name','ANSI Colors'],{cwd:workspace,windowsHide:true});
    await execute('git',['config','user.email','ansi@example.invalid'],{cwd:workspace,windowsHide:true});
    await execute('git',['add','README.md'],{cwd:workspace,windowsHide:true});
    await execute('git',['commit','-m','test: ansi fixture'],{cwd:workspace,windowsHide:true});
    const source=JSON.parse(await fs.readFile(path.join(process.env.LOCALAPPDATA,'Augit/settings.json'),'utf8'));
    source.lastWorkspace=workspace; source.openFiles=[]; source.activeFile=null; source.theme=dark?'Dark':'Light';
    await fs.writeFile(settings,JSON.stringify(source,null,2)+'\n');
    const listener=net.createServer(); await new Promise(r=>listener.listen(0,'127.0.0.1',r)); const port=listener.address().port; await new Promise(r=>listener.close(r));
    report.port=port; pid=await launch(workspace,settings,port); const hwnd=await waitWindow(pid); report.pid=pid; report.hwnd=hwnd; await topmost(hwnd);
    browser=await connect(port); const page=await pageOf(browser);
    await page.waitForFunction('window.__augitReady===true && !!window.__augitLive',null,{timeout:30000});
    const rail=await page.evaluate(()=>{const n=document.querySelector('.rail-button[data-tool="terminal"]');const r=n.getBoundingClientRect();return{x:r.x,y:r.y,width:r.width,height:r.height}});
    await clickCss(hwnd,{x:rail.x+rail.width/2,y:rail.y+rail.height/2});
    await page.waitForSelector('.terminal-tool .terminal-view',{timeout:20000}); await delay(2500);
    const view=await page.evaluate(()=>{const n=document.querySelector('.terminal-tool .terminal-view');const r=n.getBoundingClientRect();return{x:r.x,y:r.y,width:r.width,height:r.height}});
    await clickCss(hwnd,{x:view.x+30,y:view.y+20});
    const batches = extended ? [
      { name: 'extended', command: '$e=[char]27; $c=@("38;5;196","38;5;46","38;5;21","38;5;244","38;2;18;52;86","38;2;200;120;40"); $n=@("I196","I46","I21","I244","T1856","T20012040"); for($i=0;$i -lt $c.Count;$i++){ Write-Host ($e+"["+$c[$i]+"m"+$n[$i]) }' },
    ] : [
      {name:'normal',command:'$e=[char]27; 30..37 | ForEach-Object { Write-Host ($e+"["+$_+"mC"+$_) }'},
      {name:'bright',command:'$e=[char]27; 90..97 | ForEach-Object { Write-Host ($e+"["+$_+"mC"+$_) }'},
    ];
    for(const batch of batches){
      await pasteAndEnter(batch.command); await delay(1500);
      const state=await page.evaluate(()=>[...document.querySelectorAll('.xterm-rows > div')].map((n,i)=>{const r=n.getBoundingClientRect();return{index:i,text:n.textContent,x:r.x,y:r.y,width:r.width,height:r.height}}));
      const file=path.join(output,`ansi-${dark?'dark-':''}${batch.name}.png`); await page.screenshot({path:file});
      report.batches.push({name:batch.name,theme:dark?'dark':'light',file,rows:state.filter(r=>/^(C\d+|I\d+|T\d+)$/.test(r.text.trim()))});
      await pasteAndEnter('Clear-Host'); await delay(700);
    }
    report.closedNormally=await closeApp(pid); closed=report.closedNormally; await delay(500); report.pidAlive=(()=>{try{process.kill(pid,0);return true}catch{return false}})();
    await browser.close(); browser=null;
  }catch(error){report.error=String(error&&error.stack||error);process.exitCode=1}
  finally{if(browser)await browser.close().catch(()=>{});if(pid&&!closed)await closeApp(pid).catch(()=>{});await fs.writeFile(path.join(output,'terminal-ansi-colors.json'),JSON.stringify(report,null,2)+'\n');await fs.rm(temp,{recursive:true,force:true}).catch(()=>{})}
  console.log(JSON.stringify({pid:report.pid,closed:report.closedNormally,batches:report.batches.map(b=>({name:b.name,rows:b.rows.length,file:b.file}))},null,2));
})();
