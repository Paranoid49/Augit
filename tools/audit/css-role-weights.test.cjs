const assert=require('node:assert/strict');
const {test}=require('node:test');
const {roleWeights}=require('./css-role-weights.cjs');
test('随包粗体描述符不冒充界面字重，普通及半粗角色保留',()=>{
 assert.deepEqual([...roleWeights('@font-face {font-family:"JetBrains Mono";font-weight:700;src:url("}font.woff2")} .body {font-weight:400} .title {font-weight:600}')],['400','600']);
});
test('嵌套条件中的字体声明排除，按钮第三档仍会被检查',()=>{
 const weights=roleWeights('@media screen {@font-face {font-weight:700} .button {font-weight:700}} .menu {font-weight:500}');
 assert.deepEqual([...weights],['700','500']);
});
test('注释和字符串内的声明不进入角色集合，关键字不漏检',()=>{
 assert.deepEqual([...roleWeights('/*font-weight:700*/ .x{content:"font-weight:500";font-weight: BOLD} .y{font-weight:normal}')],['bold','normal']);
});
