// Define Remote 自动 Fetch 失败时管理器内联反馈的正常入口证据（Augit 侧）。
//
// 真实外壳：无远端仓库 → 分支 chip → Push… → Define remote → Add → 填写无效 URL →
// OK；观察管理器仍打开、inline notice 含 Added + fetch 失败，主窗 toast 不冒充模态反馈。
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const net = require('node:net');
const { execFile } = require('node:child_process');
const execute = require('node:util').promisify(execFile);
const { chromium } = require(process.argv[2]);
const output = path.resolve(process.argv[3] || 'artifacts/define-remote-autofetch-2026-10-24');
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

async function waitWindow(pid){const s=`$d=(Get-Date).AddSeconds(20); while((Get-Date)-lt $d){$h=(Get-Process -Id ${pid} -ErrorAction SilentlyContinue).MainWindowHandle; if($h -ne 0){Write-Output $h; exit 0}; Start-Sleep -Milliseconds 100}; exit 2`;return Number((await execute('powershell.exe',['-NoProfile','-Command',s],{timeout:30000})).stdout.trim())}
async function topmost(hwnd){const s=`Add-Type @"
using System; using System.Runtime.InteropServices;
public static class DT { [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c); [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int c); [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h); [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int cx,int cy,uint f); [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); }
"@
try{[void][DT]::SetProcessDpiAwarenessContext([IntPtr](-4))}catch{}
$h=[IntPtr]${hwnd}; [void][DT]::ShowWindow($h,5); [void][DT]::BringWindowToTop($h); [void][DT]::SetWindowPos($h,[IntPtr](-1),0,0,0,0,0x0003); [void][DT]::SetForegroundWindow($h); Start-Sleep -Milliseconds 300`;await execute('powershell.exe',['-NoProfile','-Command',s],{timeout:30000})}
async function clickCss(hwnd,rect){const s=`Add-Type @"
using System; using System.Runtime.InteropServices;
public static class DC { [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c); [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h,out Rect r); [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h,ref Pt p); [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h); [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y); [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e); [StructLayout(LayoutKind.Sequential)] public struct Rect{public int L,T,R,B;} [StructLayout(LayoutKind.Sequential)] public struct Pt{public int X,Y;} }
"@
try{[void][DC]::SetProcessDpiAwarenessContext([IntPtr](-4))}catch{}
$r=New-Object DC+Rect; $o=New-Object DC+Pt; [void][DC]::GetClientRect([IntPtr]${hwnd},[ref]$r); [void][DC]::ClientToScreen([IntPtr]${hwnd},[ref]$o); $s=[DC]::GetDpiForWindow([IntPtr]${hwnd})/96.0
$x=[int][Math]::Round($o.X+${rect.x+rect.width/2}*$s); $y=[int][Math]::Round($o.Y+${rect.y+rect.height/2}*$s)
[void][DC]::SetCursorPos($x,$y); [DC]::mouse_event(2,0,0,0,[UIntPtr]::Zero); [DC]::mouse_event(4,0,0,0,[UIntPtr]::Zero)`;await execute('powershell.exe',['-NoProfile','-Command',s],{timeout:30000})}
async function pasteText(text){const lit=String(text).replaceAll("'","''");const s=`Add-Type @"
using System; using System.Runtime.InteropServices;
public static class DK { [DllImport("user32.dll")] public static extern void keybd_event(byte vk,byte scan,uint f,UIntPtr e); }
"@
$old=Get-Clipboard -Raw -ErrorAction SilentlyContinue; Set-Clipboard -Value '${lit}'; [DK]::keybd_event(0x11,0,0,[UIntPtr]::Zero); [DK]::keybd_event(0x56,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 40; [DK]::keybd_event(0x56,0,2,[UIntPtr]::Zero); [DK]::keybd_event(0x11,0,2,[UIntPtr]::Zero); if($null -ne $old){Set-Clipboard -Value $old}`;await execute('powershell.exe',['-NoProfile','-Command',s],{timeout:30000})}
async function selectAll(){const s=`Add-Type @"
using System; using System.Runtime.InteropServices;
public static class DA { [DllImport("user32.dll")] public static extern void keybd_event(byte vk,byte scan,uint f,UIntPtr e); }
"@
[DA]::keybd_event(0x11,0,0,[UIntPtr]::Zero); [DA]::keybd_event(0x41,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 30; [DA]::keybd_event(0x41,0,2,[UIntPtr]::Zero); [DA]::keybd_event(0x11,0,2,[UIntPtr]::Zero)`;await execute('powershell.exe',['-NoProfile','-Command',s],{timeout:30000})}
async function closeApp(pid){const s=`$p=Get-Process -Id ${pid} -ErrorAction Stop; $p.CloseMainWindow()|Out-Null; $d=(Get-Date).AddSeconds(20); while(-not $p.HasExited -and (Get-Date)-lt $d){Start-Sleep -Milliseconds 100}; if($p.HasExited){exit 0}; exit 2`;try{await execute('powershell.exe',['-NoProfile','-Command',s],{timeout:30000});return true}catch{return false}}
async function connect(port){for(let i=0;i<160;i++){try{return await chromium.connectOverCDP('http://127.0.0.1:'+port)}catch{await delay(250)}}throw new Error('CDP fail')}
async function launch(workspace,settings,port){const lit=v=>"'"+String(v).replaceAll("'","''")+"'";const cmd='$ErrorActionPreference="Stop"; $p=& '+lit(path.resolve('tools/start-augit.ps1'))+' -Workspace '+lit(workspace)+' -SettingsFile '+lit(settings)+' -SkipBuild; if(-not $p.Id){throw "no pid"}; Write-Output ("AUGIT_PID="+$p.Id)';const r=await execute('powershell.exe',['-NoProfile','-ExecutionPolicy','Bypass','-Command',cmd],{env:{...process.env,WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS:'--remote-debugging-port='+port},timeout:120000});return Number(/AUGIT_PID=(\d+)/.exec(r.stdout)?.[1])}

(async()=>{
 await fs.mkdir(output,{recursive:true});
 const temp=await fs.mkdtemp(path.join(os.tmpdir(),'Augit-define-remote-'));
 const workspace=path.join(temp,'workspace'); const settings=path.join(temp,'settings.json');
 let browser=null,pid=0,closed=false; const report={steps:[]};
 try{
  await fs.mkdir(workspace); await fs.writeFile(path.join(workspace,'README.md'),'# remote\n');
  await execute('git',['init','-b','main'],{cwd:workspace,windowsHide:true});
  await execute('git',['config','user.name','Remote Auto Fetch'],{cwd:workspace,windowsHide:true});
  await execute('git',['config','user.email','remote@example.invalid'],{cwd:workspace,windowsHide:true});
  await execute('git',['add','README.md'],{cwd:workspace,windowsHide:true});
  await execute('git',['commit','-m','test: define remote fixture'],{cwd:workspace,windowsHide:true});
  const source=JSON.parse(await fs.readFile(path.join(process.env.LOCALAPPDATA,'Augit/settings.json'),'utf8'));
  source.lastWorkspace=workspace; source.openFiles=[]; source.activeFile=null;
  await fs.writeFile(settings,JSON.stringify(source,null,2)+'\n');
  const listener=net.createServer(); await new Promise(r=>listener.listen(0,'127.0.0.1',r)); const port=listener.address().port; await new Promise(r=>listener.close(r));
  pid=await launch(workspace,settings,port); const hwnd=await waitWindow(pid); report.pid=pid; report.hwnd=hwnd; report.port=port; await topmost(hwnd);
  browser=await connect(port); const page=browser.contexts().flatMap(c=>c.pages()).find(x=>x.url().includes('index.html'));
  await page.waitForFunction('window.__augitReady===true && window.__augitGitReady===true',null,{timeout:30000});
  const rect=async sel=>page.evaluate(s=>{const n=document.querySelector(s);const r=n.getBoundingClientRect();return{x:r.x,y:r.y,width:r.width,height:r.height}},sel);
  const chip=await rect('.top-chip.branch-chip'); await clickCss(hwnd,chip);
  await page.waitForSelector('[data-popover-action="push"]',{timeout:10000});
  await clickCss(hwnd,await rect('[data-popover-action="push"]'));
  await page.waitForSelector('.push-dialog a[href$="remote.html"]',{timeout:15000});
  await clickCss(hwnd,await rect('.push-dialog a[href$="remote.html"]'));
  await page.waitForSelector('.remote-dialog [data-remote-action="add"]',{timeout:15000});
  await clickCss(hwnd,await rect('.remote-dialog [data-remote-action="add"]'));
  await page.waitForSelector('[data-remote-field="name"]',{timeout:15000});
  await clickCss(hwnd,await rect('[data-remote-field="name"]')); await selectAll(); await pasteText('badremote');
  await clickCss(hwnd,await rect('[data-remote-field="fetchUrl"]')); await selectAll(); await pasteText('http://127.0.0.1:1/repo.git');
  await clickCss(hwnd,await rect('[data-remote-define="ok"]'));
  await page.waitForFunction(()=>{const n=document.querySelector('.remote-define-dialog .remote-notice');const t=n?.textContent||'';return !n?.hidden && (t.includes('unable')||t.includes('502')||t.includes('Fetch failed'))},null,{timeout:30000});
  await delay(500);
  const result=await page.evaluate(()=>({defineOpen:!!document.querySelector('.remote-define-dialog'),urlInvalid:document.querySelector('[data-remote-field="fetchUrl"]')?.getAttribute('aria-invalid')||null,notice:document.querySelector('.remote-define-dialog .remote-notice')?.textContent||null,manager:!!document.querySelector('.remote-dialog'),managerNotice:document.querySelector('.remote-window .remote-notice')?.textContent||null,managerHasRow:!!document.querySelector('.remote-window .remote-row[data-remote-entry="badremote"]'),toast:!!document.querySelector('.toast'),toastText:document.querySelector('.toast')?.innerText||null,pushDialog:!!document.querySelector('.push-dialog'),overlayOrder:[...document.querySelectorAll('.live-overlay')].map(n=>n.className)}));
  result.remoteConfig=(await execute('git',['remote'],{cwd:workspace,windowsHide:true})).stdout.trim();
  await delay(1200);
  await page.screenshot({path:path.join(output,'define-remote-autofetch.png')});
  report.steps.push({result});
  // 先正常关闭两层对话框，再关主窗；否则 WM_CLOSE 会被仍打开的管理器/推送窗挡住。
  try {
    await clickCss(hwnd,await rect('[data-remote-define="cancel"]'));
    await page.waitForSelector('.remote-define-dialog',{state:'detached',timeout:10000});
    await clickCss(hwnd,await rect('[data-remote-action="ok"]'));
    await page.waitForSelector('.remote-dialog',{state:'detached',timeout:10000});
    await clickCss(hwnd,await rect('[data-push-action="cancel"]'));
    await page.waitForSelector('.push-dialog',{state:'detached',timeout:10000});
    await delay(300);
  } catch (closeError) { report.closeDialogsError=String(closeError&&closeError.message||closeError); }
  await browser.close(); browser=null; report.closedNormally=await closeApp(pid); closed=report.closedNormally; await delay(500); report.pidAlive=(()=>{try{process.kill(pid,0);return true}catch{return false}})();
 }catch(error){report.error=String(error&&error.stack||error);process.exitCode=1}
 finally{if(browser)await browser.close().catch(()=>{});if(pid&&!closed)await closeApp(pid).catch(()=>{});await fs.writeFile(path.join(output,'define-remote-autofetch.json'),JSON.stringify(report,null,2)+'\n');await fs.rm(temp,{recursive:true,force:true}).catch(()=>{})}
 console.log(JSON.stringify(report.steps[0]||{error:report.error||null},null,2));
})();
