// 校验精简入口仍加载共享实现，普通入口保留已有终端与查看器能力。
const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm');
test('设置入口只有共享样式与数据模块，不加载未使用的查看器资源',()=>{
 const html=fs.readFileSync('web/settings-surface.html','utf8');
 const allSources=[...html.matchAll(/(?:src|href)="([^"]+)"/g)].map(m=>m[1]);
 const sources=allSources.filter(source=>!source.startsWith('data:'));
 assert.deepEqual(sources,['src/mockup.css','src/live-data.js']);
 assert.deepEqual(allSources.filter(source=>source.startsWith('data:')),['data:,'],'入口需用空 data: 图标避免 favicon.ico 请求失败');
 assert.equal((html.match(/id="app"/g)||[]).length,1);
 for(const file of sources)assert.ok(fs.existsSync('web/'+file));
 const main=fs.readFileSync('web/index.html','utf8');
 for(const file of ['vendor/xterm/xterm.js','vendor/xterm/addon-fit.js','src/current-find.js','src/image-preview.js'])assert.ok(main.includes(file));
});
test('精简入口在数据模块运行前落实实际主题',()=>{
 const html=fs.readFileSync('web/settings-surface.html','utf8');
 const script=html.split('<script>')[1].split('</script>')[0];
 for(const [search,theme] of [['?surface=settings&theme=dark','dark'],['?surface=settings&theme=light','light'],['','light']]){
  const document={documentElement:{dataset:{}}};
  vm.runInNewContext(script,{window:{location:{search}},document,URLSearchParams});
  assert.equal(document.documentElement.dataset.theme,theme);
 }
});
