// 仅按父进程关系归属本实例；同名的其他应用WebView2不得计入。
const {execFile}=require('node:child_process');
const {promisify}=require('node:util');
const execute=promisify(execFile);
function ownedTree(processes,root) {
 const ids=new Set([root]);
 for(let previous=-1;previous!==ids.size;) {previous=ids.size;for(const process of processes)if(ids.has(process.parent))ids.add(process.pid);}
 const owned=processes.filter(process=>ids.has(process.pid));
 return {root,pids:owned.map(process=>process.pid),processCount:owned.length,workingSetBytes:owned.reduce((sum,process)=>sum+process.workingSet,0),privateBytes:owned.reduce((sum,process)=>sum+process.privateBytes,0)};
}
async function measure(root) {
 const command="$pixelRows = @(Get-CimInstance Win32_Process | ForEach-Object { $pixelProcess = Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue; if($pixelProcess){ [pscustomobject]@{pid=[int]$_.ProcessId;parent=[int]$_.ParentProcessId;workingSet=[long]$pixelProcess.WorkingSet64;privateBytes=[long]$pixelProcess.PrivateMemorySize64} } }); ConvertTo-Json -InputObject $pixelRows -Compress";
 const {stdout}=await execute('powershell.exe',['-NoProfile','-NonInteractive','-Command',command],{windowsHide:true,maxBuffer:4*1024*1024});
 const rows=JSON.parse(stdout),summary=ownedTree(rows,root);
 // 原始归属结果保留每个进程，避免总数掩盖宿主或渲染器的增长。
 return {...summary,processes:rows.filter(row=>summary.pids.includes(row.pid))};
}
module.exports={ownedTree,measure};
