// Unified Diff 上下文行号回归守卫：实时统一视图必须同时渲染旧/新行号两列。
// 这次修复针对实际端只显示变更行 5/6、缺失上下文 1–4 行号的问题；
// 视觉稿与运行时共享文件必须字节一致，模板必须同时引用 oldCells/newCells 的行号。
import fs from 'node:fs';
import path from 'node:path';

const root = 'D:/github/Augit';
const web = fs.readFileSync(path.join(root, 'web/src/mockup.js'), 'utf8');
const mock = fs.readFileSync(path.join(root, 'docs/ux-mockups/mockup.js'), 'utf8');
const tests = [];
const check = (name, ok) => { tests.push({ name, ok }); };
check('web/src 与 docs/ux-mockups mockup.js 字节一致', web === mock);
check('live unified 模板使用 unified-diff-line', web.includes('class="diff-code-line unified-diff-line'));
check('live unified 同时渲染 old/new 两列行号', /<span class="unified-number">\$\{oldNumber\}<\/span><span class="unified-number">\$\{newNumber\}<\/span>/.test(web));
check('live unified 行号来自 oldCells/newCells', web.includes('const oldNumber = oldCells[index].lineNumber ?? ""') && web.includes('const newNumber = newCells[index].lineNumber ?? ""'));
for (const t of tests) console.log((t.ok ? 'PASS ' : 'FAIL ') + t.name);
const failed = tests.filter(t => !t.ok).length;
console.log(`RESULT ${tests.length - failed}/${tests.length} PASS`);
process.exitCode = failed ? 1 : 0;
