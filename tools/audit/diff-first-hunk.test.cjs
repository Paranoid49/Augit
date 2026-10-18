// 文件起始片段不显示补丁头；中途和多个片段仍保留省略边界。
const {test}=require('node:test'),assert=require('node:assert/strict');
const fs=require('node:fs'),vm=require('node:vm'),path=require('node:path');
const source=fs.readFileSync(path.join(__dirname,'../../web/src/mockup.js'),'utf8');
const start=source.indexOf('function diffBodyParts(diff)');
const context=vm.createContext({escapeHtml:text=>String(text)});
vm.runInContext(source.slice(start,source.indexOf('\nfunction ',start+1)),context);
const header=text=>({kind:'HunkHeader',newText:text});
const row={kind:'Added',oldLine:null,newLine:1,newText:'new',oldText:null};
test('文件开头的普通/空侧范围头不占正文行且不改变输入',()=>{
 for(const range of ['@@ -1,4 +1,5 @@','@@ -0,0 +1 @@','@@ -1 +0,0 @@']) {
  const rows=[header(range),row],result=context.diffBodyParts({rows});
  assert.ok(!result.columns.includes('@@'));assert.ok(!result.unifiedTemplate.includes('@@'));
  assert.equal(rows.length,2);assert.equal(result.blockCount,1);
  assert.match(result.columns,/data-line="1">new/);
 }
});
test('文件中段首片段保留省略边界',()=>{
 assert.match(context.diffBodyParts({rows:[header('@@ -40,3 +40,4 @@'),row]}).columns,/@@ -40/);
});
test('多个片段仅移除文件起始头，分隔两块变更',()=>{
 const result=context.diffBodyParts({rows:[header('@@ -1 +1 @@'),row,header('@@ -20 +20 @@'),{...row,newLine:20}]});
 assert.ok(!result.columns.includes('@@ -1 '));assert.match(result.columns,/@@ -20/);assert.equal(result.blockCount,2);
});
test('新增和删除的缺失侧不能染满占位行，真实空行仍有高亮',()=>{
 for(const kind of ['Added','Removed']) {
  const added=kind==='Added';
  const result=context.diffBodyParts({rows:[{kind,oldLine:added?null:1,newLine:added?1:null,oldText:added?null:'',newText:added?'':null}]});
  const cells=[...result.columns.matchAll(/class="diff-code-line ([^"]*)" data-line="([^"]*)"/g)];
  assert.equal(cells.length,2);
  assert.equal(cells[added?0:1][1].trim(),added?'empty-added':'empty-removed');
  assert.equal(cells[added?1:0][1],added?'added':'removed');
 }
});
test('连续新增块空侧只画一条起点边界，连接尖端位于块顶部',()=>{
 const result=context.diffBodyParts({rows:[row,{...row,newLine:2,newText:'next'}]});
 assert.equal([...result.columns.matchAll(/empty-added/g)].length,1);
 assert.match(result.columns,/clip-path:polygon\(0 0%,100% 0%,100% 100%,0 0%\)/);
});
test('替换的两侧仍保留修改底色和真实行号',()=>{
 const result=context.diffBodyParts({rows:[{kind:'Modified',oldLine:2,newLine:2,oldText:'old',newText:'new'}]});
 assert.equal([...result.columns.matchAll(/diff-code-line changed" data-line="2"/g)].length,2);
});
test('差异摘要按变更块使用单复数，不把替换的两行计为两处',()=>{
 const cases=[
  {rows:[],count:0,label:'0 differences'},
  {rows:[row],count:1,label:'1 difference'},
  {rows:[{kind:'Removed',oldLine:1,newLine:null,oldText:'old'},row],count:1,label:'1 difference'},
  {rows:[row,{kind:'Context',oldLine:2,newLine:2,oldText:'same',newText:'same'},{...row,newLine:3}],count:2,label:'2 differences'},
 ];
 for(const item of cases) {
  const result=context.diffBodyParts({rows:item.rows});
  assert.equal(result.blockCount,item.count);
  assert.equal(result.summary,`<span class="diff-summary">${item.label}</span>`);
 }
});
