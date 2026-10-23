// 工具轨 tooltip 与窗口边缘的正常入口观察。
//
// 真实外壳打开后，把真实鼠标移到 Terminal 轨道图标上，等待原生 tooltip；用 Win32
// CopyFromScreen 截取整个窗口（含边框）并另存 CDP 客户区截图。只观察，不改产品。
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const net = require('node:net');
const { execFile } = require('node:child_process');
const execute = require('node:util').promisify(execFile);
const { chromium } = require(process.argv[2]);
const output = path.resolve(process.argv[3] || 'artifacts/tooltip-window-edge-2026-10-24');
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

async function waitWindow(pid){const s=`$d=(Get-Date).AddSeconds(20); while((Get-Date)-lt $d){$h=(Get-Process -Id ${pid} -ErrorAction SilentlyContinue).MainWindowHandle; if($h -ne 0){Write-Output $h; exit 0}; Start-Sleep -Milliseconds 100}; exit 2`;return Number((await execute('powershell.exe',['-NoProfile','-Command',s],{timeout:30000})).stdout.trim())}
async function topmost(hwnd){const s=`Add-Type @"
using System; using System.Runtime.InteropServices;
public static class TT { [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c); [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int c); [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h); [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int cx,int cy,uint f); [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); }
"@
try{[void][TT]::SetProcessDpiAwarenessContext([IntPtr](-4))}catch{}
$h=[IntPtr]${hwnd}; [void][TT]::ShowWindow($h,5); [void][TT]::BringWindowToTop($h); [void][TT]::SetWindowPos($h,[IntPtr](-1),0,0,0,0,0x0003); [void][TT]::SetForegroundWindow($h); Start-Sleep -Milliseconds 300`;await execute('powershell.exe',['-NoProfile','-Command',s],{timeout:30000})}
async function hoverCss(hwnd,rect){const s=`Add-Type @"
using System; using System.Runtime.InteropServices;
public static class TH { [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c); [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h,out Rect r); [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h,ref Pt p); [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h); [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y); [StructLayout(LayoutKind.Sequential)] public struct Rect{public int L,T,R,B;} [StructLayout(LayoutKind.Sequential)] public struct Pt{public int X,Y;} }
"@
try{[void][TH]::SetProcessDpiAwarenessContext([IntPtr](-4))}catch{}
$r=New-Object TH+Rect; $o=New-Object TH+Pt; [void][TH]::GetClientRect([IntPtr]${hwnd},[ref]$r); [void][TH]::ClientToScreen([IntPtr]${hwnd},[ref]$o); $s=[TH]::GetDpiForWindow([IntPtr]${hwnd})/96.0
$x=[int][Math]::Round($o.X+${rect.x+rect.width/2}*$s); $y=[int][Math]::Round($o.Y+${rect.y+rect.height/2}*$s); [void][TH]::SetCursorPos($x,$y); Write-Output "$x,$y"`;return(await execute('powershell.exe',['-NoProfile','-Command',s],{timeout:30000})).stdout.trim()}
async function captureWindow(hwnd,file){const s=`Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class TW { [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c); [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r); [StructLayout(LayoutKind.Sequential)] public struct Rect{public int L,T,R,B;} }
"@
try{[void][TW]::SetProcessDpiAwarenessContext([IntPtr](-4))}catch{}
$r=New-Object TW+Rect; [void][TW]::GetWindowRect([IntPtr]${hwnd},[ref]$r); $w=$r.R-$r.L; $h=$r.B-$r.T
$bmp=New-Object System.Drawing.Bitmap($w,$h); $g=[System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($r.L,$r.T,0,0,$bmp.Size); $bmp.Save('${file.replaceAll("'","''")}',[System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose(); Write-Output "$($r.L),$($r.T),$w,$h"`;return(await execute('powershell.exe',['-NoProfile','-Command',s],{timeout:30000})).stdout.trim()}
async function closeApp(pid){const s=`$p=Get-Process -Id ${pid} -ErrorAction Stop; $p.CloseMainWindow()|Out-Null; $d=(Get-Date).AddSeconds(20); while(-not $p.HasExited -and (Get-Date)-lt $d){Start-Sleep -Milliseconds 100}; if($p.HasExited){exit 0}; exit 2`;try{await execute('powershell.exe',['-NoProfile','-Command',s],{timeout:30000});return true}catch{return false}}
async function connect(port){for(let i=0;i<160;i++){try{return await chromium.connectOverCDP('http://127.0.0.1:'+port)}catch{await delay(250)}}throw new Error('CDP fail')}
async function pageOf(browser){for(let i=0;i<120;i++){const p=browser.contexts().flatMap(c=>c.pages()).find(x=>x.url().includes('index.html'));if(p)return p;await delay(250)}throw new Error('page fail')}
async function launch(workspace,settings,port){const lit=v=>"'"+String(v).replaceAll("'","''")+"'";const cmd='$ErrorActionPreference="Stop"; $p=& '+lit(path.resolve('tools/start-augit.ps1'))+' -Workspace '+lit(workspace)+' -SettingsFile '+lit(settings)+' -SkipBuild; if(-not $p.Id){throw "no pid"}; Write-Output ("AUGIT_PID="+$p.Id)';const r=await execute('powershell.exe',['-NoProfile','-ExecutionPolicy','Bypass','-Command',cmd],{env:{...process.env,WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS:'--remote-debugging-port='+port},timeout:120000});return Number(/AUGIT_PID=(\d+)/.exec(r.stdout)?.[1])}

(async()=>{
 await fs.mkdir(output,{recursive:true});
 const temp=await fs.mkdtemp(path.join(os.tmpdir(),'Augit-tooltip-'));
 const workspace=path.join(temp,'workspace'); const settings=path.join(temp,'settings.json');
 let browser=null,pid=0,closed=false; const report={};
 try{
  await fs.mkdir(workspace); await fs.writeFile(path.join(workspace,'README.md'),'# tooltip\n');
  await execute('git',['init','-b','main'],{cwd:workspace,windowsHide:true});
  await execute('git',['config','user.name','Tooltip Probe'],{cwd:workspace,windowsHide:true});
  await execute('git',['config','user.email','tooltip@example.invalid'],{cwd:workspace,windowsHide:true});
  await execute('git',['add','README.md'],{cwd:workspace,windowsHide:true});
  await execute('git',['commit','-m','test: tooltip fixture'],{cwd:workspace,windowsHide:true});
  const source=JSON.parse(await fs.readFile(path.join(process.env.LOCALAPPDATA,'Augit/settings.json'),'utf8'));
  source.lastWorkspace=workspace; source.openFiles=[]; source.activeFile=null;
  await fs.writeFile(settings,JSON.stringify(source,null,2)+'\n');
  const listener=net.createServer(); await new Promise(r=>listener.listen(0,'127.0.0.1',r)); const port=listener.address().port; await new Promise(r=>listener.close(r));
  pid=await launch(workspace,settings,port); const hwnd=await waitWindow(pid); report.pid=pid; report.hwnd=hwnd; report.port=port; await topmost(hwnd);
  browser=await connect(port); const page=await pageOf(browser);
  await page.waitForFunction('window.__augitReady===true',null,{timeout:30000});
  const controls=await page.evaluate(()=>[...document.querySelectorAll('.rail-button,.toolbar-button,.icon-button')].map(n=>({label:n.getAttribute('aria-label'),title:n.getAttribute('title'),cls:n.className})).filter(x=>x.label||x.title));
  const rail=await page.evaluate(()=>{const n=document.querySelector('.rail-button[data-tool="terminal"]');const r=n.getBoundingClientRect();return{x:r.x,y:r.y,width:r.width,height:r.height,label:n.getAttribute('aria-label'),title:n.getAttribute('title')}});
  report.rail=rail; report.controls=controls;
  const screen=await captureWindow(hwnd,path.join(output,'window-before.png')); report.windowRect=screen;
  report.cursor=await hoverCss(hwnd,rail);
  await delay(1600);
  const screen2=await captureWindow(hwnd,path.join(output,'window-tooltip.png')); report.windowRect2=screen2;
  await page.screenshot({path:path.join(output,'client.png')});
  await browser.close(); browser=null; report.closedNormally=await closeApp(pid); closed=report.closedNormally; await delay(500); report.pidAlive=(()=>{try{process.kill(pid,0);return true}catch{return false}})();
 }catch(error){report.error=String(error&&error.stack||error);process.exitCode=1}
 finally{if(browser)await browser.close().catch(()=>{});if(pid&&!closed)await closeApp(pid).catch(()=>{});await fs.writeFile(path.join(output,'tooltip-window-edge.json'),JSON.stringify(report,null,2)+'\n');await fs.rm(temp,{recursive:true,force:true}).catch(()=>{})}
 console.log(JSON.stringify({pid:report.pid,closed:report.closedNormally,rail:report.rail,windowRect:report.windowRect,error:report.error||null},null,2));
})();
