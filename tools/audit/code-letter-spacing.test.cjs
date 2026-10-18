// 独立JBR度量核对，不用产品公式生成期望值。
const assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm');
const {test}=require('node:test');
const source=fs.readFileSync('web/src/mockup.js','utf8');
const code=source.slice(source.indexOf('function codeLetterSpacing('),source.indexOf('function measureCodeViews('));
const records=JSON.parse(fs.readFileSync('tools/audit/jetbrains-mono-integer-widths.json','utf8'));
function load(fonts) {
 const context=vm.createContext({window:{devicePixelRatio:1.75},document:{fonts,createElement:()=>({getContext:()=>({font:'',measureText(){return{width:parseFloat(this.font)*.6};}})})}});
 vm.runInContext(code,context);return context.codeLetterSpacing;
}
test('32字号八种比例与独立JBR设备字宽一致',()=>{
 const spacing=load([{family:'JetBrains Mono',style:'normal',weight:'400',status:'loaded'}]);
 assert.equal(records.length,8);
 for(const sample of records) {
  assert.equal(sample.rows.length,32);
  for(const row of sample.rows)assert.ok(Math.abs(row.size*.6+spacing('JetBrains Mono, monospace',row.size,sample.scale)-row.spaceWidth)<1e-5);
 }
});
test('不同字体和未加载字体保留原始字距',()=>{
 const face={family:'JetBrains Mono',style:'normal',weight:'400',status:'loaded'};
 assert.equal(load([face])('Consolas',13),0);
 assert.equal(load([])('JetBrains Mono',13),0);
 assert.equal(load([{...face,status:'unloaded'}])('JetBrains Mono',13),0);
 assert.equal(load([{...face,weight:'700'}])('JetBrains Mono',13),0);
});
