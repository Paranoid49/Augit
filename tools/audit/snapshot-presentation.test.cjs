// 执行产品实际注入脚本，检查两次渲染回调前不得确认呈现。
const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const source=fs.readFileSync(path.join(__dirname,'../../src/Augit.Shell/ShellSnapshotPresentation.cs'),'utf8');
const script=source.match(/const string Script = """\s*([\s\S]*?)\s*""";/)[1];
const sizing=source.match(/const string ImageSizingScript = """\s*([\s\S]*?)\s*""";/)[1];
const loaded=source.match(/const string ImageLoadedScript = """\s*([\s\S]*?)\s*""";/)[1];

test('宿主背景更新登记等待后设置图片，收到加载消息后才应答',()=>{
 const host=fs.readFileSync(path.join(__dirname,'../../src/Augit.Shell/ShellModalHost.cs'),'utf8');
 const replacement=host.slice(host.indexOf('internal async Task<bool> ReplaceAsync('),host.indexOf('private async Task LoadImageAsync('));
 assert.match(replacement,/WaitForLoadedAsync\(revision\)[\s\S]*?await LoadImageAsync\(revision\)[\s\S]*?await loaded/);
 assert.match(replacement,/long revision = _images.Add\(image\);[\s\S]*?if \(revision != _images.LoadedRevision\)/);
 assert.match(replacement,/catch \{ _images.FailLoading\(\); throw; \}/);
 assert.match(host,/if \(message == "failed"\) \{ FailLoading\(\); return; \}/);
 assert.match(host,/_images.Loaded\(revision\);/);
});

test('实际换图脚本仅在当前图片加载并替换后回报版本',()=>{
 const host=fs.readFileSync(path.join(__dirname,'../../src/Augit.Shell/ShellModalHost.cs'),'utf8');
 const method=host.slice(host.indexOf('private async Task LoadImageAsync('),host.indexOf('internal async Task ImageLoadedAsync('));
 const template=method.match(/ExecuteScriptAsync\(\$"([^\r\n]+)"\);/)[1];
 for(const stale of [false,true]) {
  const calls=[],next={dataset:{},removeAttribute:()=>{}},previous={cloneNode:()=>next,replaceWith:node=>{assert.equal(node,next);calls.push('replaced');},removeAttribute:()=>calls.push('released')};
  const window={};
  const actual=template.replaceAll('{revision}','7').replaceAll('{{','{').replaceAll('}}','}');
  vm.runInNewContext(actual,{window,document:{querySelector:()=>previous},fitSnapshotImage:()=>calls.push('sized'),chrome:{webview:{postMessage:message=>calls.push(message)}}});
  assert.deepEqual(calls,[]);
  assert.equal(next.src,'https://snapshot.augit.local/image.png?v=7');
  if(stale)window.latestRevision=8;
  next.onload();
  assert.deepEqual(calls,stale?[]:['sized','replaced','released','loaded:7']);
 }
});

test('首图先定尺再等待两帧，显示确认仍独立等待',()=>{
 const calls=[],frames=[],image={dataset:{revision:'0'}};
 vm.runInNewContext('(function(){'+loaded+'}).call(image)',{
  image,requestAnimationFrame:callback=>frames.push(callback),fitSnapshotImage:node=>{assert.equal(node,image);calls.push('sized');},
  chrome:{webview:{postMessage:message=>calls.push(message)}}
 });
 assert.deepEqual(calls,['sized']);
 frames.shift()();assert.deepEqual(calls,['sized']);
 frames.shift()();assert.deepEqual(calls,['sized','loaded:0']);
 const host=fs.readFileSync(path.join(__dirname,'../../src/Augit.Shell/ShellModalHost.cs'),'utf8');
 assert.ok(host.includes('{ShellSnapshotPresentation.ImageLoadedScript}'));
 assert.match(host,/await cover\.ShowCoverAsync\(\);[\s\S]*?DwmFlush\(\)/);
});

test('首图在导航HTML中已定尺，加载回调不是唯一尺寸来源',()=>{
 const host=fs.readFileSync(path.join(__dirname,'../../src/Augit.Shell/ShellModalHost.cs'),'utf8');
 assert.ok(host.includes('ShellSnapshotPresentation.InitialImageSize(initialImage, captureScale)'));
 assert.ok(host.includes("style='position:absolute;{initialImageSize}left:-{offsetX}px;top:-{offsetY}px'"));
});

test('快照宽高按真实PNG还原物理像素，更新时重新定尺',()=>{
 for(const scale of [1,1.25,1.5,1.75,2]) {
  const window={snapshotCaptureScale:scale};
  vm.runInNewContext(sizing,{window});
  const image={naturalWidth:2452,naturalHeight:1630,style:{}};
  window.fitSnapshotImage(image);
  assert.ok(Math.abs(parseFloat(image.style.width)*scale-2452)<1e-9);
  assert.ok(Math.abs(parseFloat(image.style.height)*scale-1630)<1e-9);
  image.naturalWidth=1801; image.naturalHeight=901;
  window.fitSnapshotImage(image);
  assert.ok(Math.abs(parseFloat(image.style.width)*scale-1801)<1e-9);
  assert.ok(Math.abs(parseFloat(image.style.height)*scale-901)<1e-9);
 }
});
test('遮盖显示确认等待两次渲染回调且只确认一次',()=>{
 const frames=[],messages=[];
 vm.runInNewContext(script,{requestAnimationFrame:callback=>frames.push(callback),chrome:{webview:{postMessage:message=>messages.push(message)}}});
 assert.deepEqual(messages,[]);
 assert.equal(frames.length,1);
 frames.shift()();
 assert.deepEqual(messages,[]);
 assert.equal(frames.length,1);
 frames.shift()();
 assert.deepEqual(messages,['presented']);
 assert.equal(frames.length,0);
});
