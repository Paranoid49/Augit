// 首次差异请求没有内容时只呈现目标路径，不回落到视觉稿示例。
const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const vm=require('node:vm');
const source=fs.readFileSync(require('node:path').join(__dirname,'../../web/src/mockup.js'),'utf8');
const start=source.indexOf('function liveDiffView()');
const end=source.indexOf('\nfunction ',start+1);
for(const busy of [false,true])test(`首次差异加载：延迟提示${busy?'已触发':'未触发'}`,()=>{
 const calls=[];
 const context=vm.createContext({window:{__augitLive:{activeTabId:2,tabs:[{id:1,path:'old.py'},{id:2,path:'folder/current.py'}],diffLoading:busy}},location:{search:''},URLSearchParams,
  liveDiffToolbar:options=>{calls.push(options);return '<toolbar>';},
  liveConflictNotice:()=>'',
  diffFileHeader:(from,to,title,path)=>{assert.equal(path,'folder/current.py');return '<header>';},
  diffView:()=>{throw Error('禁止显示示例');},
 });
 const result=vm.runInContext(source.slice(start,end)+'\nliveDiffView()',context);
 assert.match(result,/aria-label="Diff Content"/);
 assert.doesNotMatch(result,/diff-code-line|app.manifest/);
 assert.equal(calls[0].arrowsDisabled,true);assert.equal(calls[0].busy,busy);
});
