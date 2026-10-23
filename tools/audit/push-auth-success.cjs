// Push 认证成功路径：带 Basic 凭据的 URL + 本地 smart-HTTP（git http-backend）远端。
//
// 真实外壳走分支 chip → Push → 确认；Git 子进程从 URL 读取 user:pass 并发送 Authorization，
// 服务端校验后由 git http-backend 完成 smart HTTP 传输。Augit 不存储凭据。
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const net = require('node:net');
const http = require('node:http');
const { spawn, execFile } = require('node:child_process');
const execute = require('node:util').promisify(execFile);
const { chromium } = require(process.argv[2]);
const output = path.resolve(process.argv[3] || 'artifacts/push-auth-success-2026-10-24');
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

async function waitWindow(pid){const s=`$d=(Get-Date).AddSeconds(20); while((Get-Date)-lt $d){$h=(Get-Process -Id ${pid} -ErrorAction SilentlyContinue).MainWindowHandle; if($h -ne 0){Write-Output $h; exit 0}; Start-Sleep -Milliseconds 100}; exit 2`;return Number((await execute('powershell.exe',['-NoProfile','-Command',s],{timeout:30000})).stdout.trim())}
async function topmost(hwnd){const s=`Add-Type @"
using System; using System.Runtime.InteropServices;
public static class PT { [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c); [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int c); [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h); [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int cx,int cy,uint f); [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); }
"@
try{[void][PT]::SetProcessDpiAwarenessContext([IntPtr](-4))}catch{}
$h=[IntPtr]${hwnd}; [void][PT]::ShowWindow($h,5); [void][PT]::BringWindowToTop($h); [void][PT]::SetWindowPos($h,[IntPtr](-1),0,0,0,0,0x0003); [void][PT]::SetForegroundWindow($h); Start-Sleep -Milliseconds 300`;await execute('powershell.exe',['-NoProfile','-Command',s],{timeout:30000})}
async function clickRect(hwnd,rect,label){const r=await execute('powershell.exe',['-NoProfile','-ExecutionPolicy','Bypass','-File',path.resolve('tools/audit/click-window.ps1'),'-X',String(rect.x+rect.width/2),'-Y',String(rect.y+rect.height/2),'-WindowHandle',String(hwnd),'-AfterMs','100','-Clicks','1','-NoForeground'],{timeout:30000});return{label,rect,clickUtc:/click_utc=([^\s]+)/.exec(r.stdout)?.[1]||null}}
async function closeApp(pid){const s=`$p=Get-Process -Id ${pid} -ErrorAction Stop; $p.CloseMainWindow()|Out-Null; $d=(Get-Date).AddSeconds(20); while(-not $p.HasExited -and (Get-Date)-lt $d){Start-Sleep -Milliseconds 100}; if($p.HasExited){exit 0}; exit 2`;try{await execute('powershell.exe',['-NoProfile','-Command',s],{timeout:30000});return true}catch{return false}}
async function connect(port){for(let i=0;i<160;i++){try{return await chromium.connectOverCDP('http://127.0.0.1:'+port)}catch{await delay(250)}}throw new Error('CDP fail')}
async function pageOf(browser){for(let i=0;i<120;i++){const p=browser.contexts().flatMap(c=>c.pages()).find(x=>x.url().includes('index.html'));if(p)return p;await delay(250)}throw new Error('page fail')}
async function launch(workspace,settings,port){const lit=v=>"'"+String(v).replaceAll("'","''")+"'";const cmd='$ErrorActionPreference="Stop"; $p=& '+lit(path.resolve('tools/start-augit.ps1'))+' -Workspace '+lit(workspace)+' -SettingsFile '+lit(settings)+' -SkipBuild; if(-not $p.Id){throw "no pid"}; Write-Output ("AUGIT_PID="+$p.Id)';const r=await execute('powershell.exe',['-NoProfile','-ExecutionPolicy','Bypass','-Command',cmd],{env:{...process.env,WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS:'--remote-debugging-port='+port},timeout:120000});return Number(/AUGIT_PID=(\d+)/.exec(r.stdout)?.[1])}
async function git(args,cwd){return (await execute('git',args,{cwd,windowsHide:true,timeout:120000})).stdout}

async function smartServer(root, report) {
  const expected = 'Basic ' + Buffer.from('user:pass').toString('base64');
  const server = http.createServer((request, response) => {
    const auth = request.headers.authorization || null;
    report.requests.push({method:request.method,url:request.url,authorization:!!auth});
    if (!auth) {
      response.writeHead(401, {'WWW-Authenticate':'Basic realm="Augit Auth Success Probe"','Content-Type':'text/plain'});
      response.end('authentication required\n');
      return;
    }
    if (auth !== expected) {
      response.writeHead(403, {'Content-Type':'text/plain'}); response.end('forbidden\n'); return;
    }
    const url = new URL(request.url, 'http://127.0.0.1');
    const env = {
      ...process.env,
      GIT_PROJECT_ROOT: root,
      GIT_HTTP_EXPORT_ALL: '1',
      PATH_INFO: url.pathname,
      QUERY_STRING: url.search.replace(/^\?/, ''),
      REQUEST_METHOD: request.method,
      CONTENT_TYPE: request.headers['content-type'] || '',
      CONTENT_LENGTH: request.headers['content-length'] || '',
      REMOTE_USER: 'user',
      REMOTE_ADDR: '127.0.0.1',
      SERVER_NAME: '127.0.0.1',
      SERVER_PORT: String(report.authPort),
      SERVER_PROTOCOL: 'HTTP/1.1',
      GATEWAY_INTERFACE: 'CGI/1.1',
    };
    for (const [key, value] of Object.entries(request.headers)) {
      if (key === 'content-length' || key === 'content-type') continue;
      env['HTTP_' + key.toUpperCase().replaceAll('-', '_')] = Array.isArray(value) ? value.join(',') : String(value);
    }
    const child = spawn('git', ['http-backend'], { env, windowsHide: true });
    let buffer = Buffer.alloc(0);
    let headersDone = false;
    child.stdout.on('data', (chunk) => {
      if (headersDone) { response.write(chunk); return; }
      buffer = Buffer.concat([buffer, chunk]);
      let split = buffer.indexOf('\r\n\r\n');
      let skip = 4;
      if (split < 0) { split = buffer.indexOf('\n\n'); skip = 2; }
      if (split < 0) return;
      const raw = buffer.slice(0, split).toString('utf8').split(/\r?\n/);
      let status = 200;
      const headers = {};
      for (const line of raw) {
        const i = line.indexOf(':');
        if (i < 0) continue;
        const name = line.slice(0, i).trim(), value = line.slice(i + 1).trim();
        if (name.toLowerCase() === 'status') status = Number((/\d{3}/.exec(value) || [200])[0]);
        else if (name.toLowerCase() !== 'content-length') headers[name] = value;
      }
      response.writeHead(status, headers);
      headersDone = true;
      const rest = buffer.slice(split + skip);
      if (rest.length) response.write(rest);
      buffer = Buffer.alloc(0);
    });
    child.on('error', () => { if (!headersDone) response.writeHead(500, {'Content-Type':'text/plain'}); response.end('cgi failed\n'); });
    child.on('close', () => { if (!headersDone) response.writeHead(500, {'Content-Type':'text/plain'}); response.end(); });
    request.pipe(child.stdin);
  });
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  return server;
}

(async()=>{
 await fs.mkdir(output,{recursive:true});
 const temp=await fs.mkdtemp(path.join(os.tmpdir(),'Augit-push-auth-'));
 const root=path.join(temp,'server'); const remote=path.join(root,'remote.git'); const seed=path.join(temp,'seed'); const settings=path.join(temp,'settings.json');
 let server=null,browser=null,pid=0,closed=false; const report={generatedAt:new Date().toISOString(),requests:[],runs:[]};
 try{
  await fs.mkdir(root); await fs.mkdir(seed);
  await execute('git',['init','--bare',remote],{cwd:root,windowsHide:true});
  server=await smartServer(root,report); report.authPort=server.address().port;
  const url=`http://user:pass@127.0.0.1:${report.authPort}/remote.git`;
  await git(['init','-b','main'],seed);
  await git(['config','user.name','Push Auth Success'],seed);
  await git(['config','user.email','push-auth@example.invalid'],seed);
  await git(['config','commit.gpgsign','false'],seed);
  await fs.writeFile(path.join(seed,'base.txt'),'base\n');
  await git(['add','base.txt'],seed); await git(['commit','-m','test: auth seed'],seed);
  await git(['remote','add','origin',url],seed);
  await git(['push','-u','origin','main'],seed);
  await fs.writeFile(path.join(seed,'extra.txt'),'extra\n');
  await git(['add','extra.txt'],seed); await git(['commit','-m','test: auth push'],seed);
  const source=JSON.parse(await fs.readFile(path.join(process.env.LOCALAPPDATA,'Augit/settings.json'),'utf8'));
  source.lastWorkspace=seed; source.openFiles=[]; source.activeFile=null;
  await fs.writeFile(settings,JSON.stringify(source,null,2)+'\n');
  const listener=net.createServer(); await new Promise(r=>listener.listen(0,'127.0.0.1',r)); const port=listener.address().port; await new Promise(r=>listener.close(r));
  pid=await launch(seed,settings,port); const hwnd=await waitWindow(pid); report.pid=pid; report.hwnd=hwnd; report.port=port; await topmost(hwnd);
  browser=await connect(port); const page=await pageOf(browser);
  await page.waitForFunction('window.__augitReady===true && window.__augitGitReady===true',null,{timeout:30000});
  const rect=async sel=>page.evaluate(s=>{const n=document.querySelector(s);const r=n.getBoundingClientRect();return{x:r.x,y:r.y,width:r.width,height:r.height}},sel);
  await clickRect(hwnd,await rect('.top-chip.branch-chip'),'branch-chip');
  await page.waitForSelector('[data-popover-action="push"]',{timeout:10000});
  await clickRect(hwnd,await rect('[data-popover-action="push"]'),'push-menu');
  await page.waitForSelector('.push-dialog [data-push-action="confirm"]',{timeout:15000});
  await page.waitForFunction(()=>!document.querySelector('[data-push-action="confirm"]').disabled,null,{timeout:15000});
  await clickRect(hwnd,await rect('[data-push-action="confirm"]'),'push-confirm');
  await page.waitForSelector('.toast',{timeout:30000});
  await delay(500);
  const result=await page.evaluate(()=>({toast:document.querySelector('.toast')?.innerText||null,dialog:!!document.querySelector('.push-dialog'),confirmDisabled:document.querySelector('[data-push-action="confirm"]')?.disabled??null}));
  await page.screenshot({path:path.join(output,'push-auth-success.png')});
  const remoteLog=await git(['log','--oneline','main'],remote);
  report.runs.push({result,remoteLog:remoteLog.trim().split(/\r?\n/),authorizationRequests:report.requests.filter(r=>r.authorization).length,totalRequests:report.requests.length});
  await browser.close(); browser=null; report.closedNormally=await closeApp(pid); closed=report.closedNormally; await delay(500); report.pidAlive=(()=>{try{process.kill(pid,0);return true}catch{return false}})();
 }catch(error){report.error=String(error&&error.stack||error);process.exitCode=1}
 finally{if(browser)await browser.close().catch(()=>{});if(pid&&!closed)await closeApp(pid).catch(()=>{});if(server)await new Promise(r=>server.close(r));await fs.writeFile(path.join(output,'push-auth-success.json'),JSON.stringify(report,null,2)+'\n');await fs.rm(temp,{recursive:true,force:true}).catch(()=>{})}
 console.log(JSON.stringify(report.runs[0]||{error:report.error||null},null,2));
})();
