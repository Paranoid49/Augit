// 项目树根节点右键回归：根节点以空 data-tree-path 表示工作区根，右键必须回落到 live.root；
// 普通文件/目录节点仍用自己的相对路径，空路径的非根节点不得凭空生成菜单。
import fs from 'node:fs';

const source = fs.readFileSync('D:/github/Augit/web/src/live-data.js', 'utf8');
const start = source.indexOf('function treeContextPath');
if (start < 0) throw new Error('treeContextPath 不存在');
let depth = 0;
let end = -1;
for (let index = source.indexOf('{', start); index < source.length; index += 1) {
  if (source[index] === '{') depth += 1;
  else if (source[index] === '}') {
    depth -= 1;
    if (depth === 0) { end = index + 1; break; }
  }
}
const fn = new Function(source.slice(start, end) + '; return treeContextPath;')();
const row = (path, root = false) => ({
  dataset: { treePath: path },
  classList: { contains: (name) => root && name === 'root-row' },
});
const checks = [
  ['文件节点使用自身路径', fn(row('notes.txt'), { root: 'D:/w' }) === 'notes.txt'],
  ['根节点空路径回落到 live.root', fn(row('', true), { root: 'D:/w' }) === 'D:/w'],
  ['根节点无 root 时返回空', fn(row('', true), {}) === ''],
  ['非根空路径不生成菜单', fn(row(''), { root: 'D:/w' }) === ''],
  ['空行安全', fn(null, { root: 'D:/w' }) === ''],
  ['打开菜单使用 treeContextPath', source.includes('const path = treeContextPath(row, live);')],
];
for (const [name, ok] of checks) console.log((ok ? 'PASS ' : 'FAIL ') + name);
const failed = checks.filter(([, ok]) => !ok).length;
console.log(`RESULT ${checks.length - failed}/${checks.length} PASS`);
process.exitCode = failed ? 1 : 0;
