// 一次性收集某个检查器的**全部**断言不匹配（去重汇总）。
//
// 背景：这些 verify-ux-*.cjs 失败时会在**第一条**断言抛出，逐条修要跑十几遍。
// 本脚本作为 `--require` 预载，把 assert 的每个方法改成"记录并继续"，跑完在 exit 时输出去重汇总。
//
// 用法：
//   node --require tools/audit/collect-assert-failures.cjs tools/verify-ux-titlebar.cjs <playwright> <chrome> [证据目录]
//
// 注意：断言不再抛错后，控制流会继续往下走，可能出现**级联**记录；判断时以"同一组 actual/expected 出现次数多"为优先。
const Module = require('node:module');
const orig = Module._load;
const fails = [];
const patch = (fn, name) => function (...args) {
  try { fn.apply(this, args); }
  catch (e) {
    // 只给值不给位置时，多个断言长得一模一样，定不出是哪一条。
    // 设 AUGIT_ASSERT_SITES=1 即额外记录"调用点"（检查器里的行号），仍保持去重汇总。
    let site = null;
    if (process.env.AUGIT_ASSERT_SITES) {
      const line = (new Error().stack || '').split('\n')
        .find((l) => /verify-ux-[a-z-]+\.cjs/.test(l));
      site = line ? line.trim().replace(/^at\s+/, '').replace(process.cwd() + '/', '') : null;
    }
    fails.push({ kind: name, msg: String(e.message || '').split('\n')[0].slice(0, 90), actual: e.actual, expected: e.expected, site });
  }
};
Module._load = function (req) {
  const m = orig.apply(this, arguments);
  if (/^(node:)?assert(\/strict)?$/.test(req)) {
    return new Proxy(m, { get(t, k) { const v = t[k]; return typeof v === 'function' ? patch(v, k) : v; } });
  }
  return m;
};
process.on('exit', () => {
  const seen = new Map();
  for (const f of fails) { const key = JSON.stringify([f.kind, f.actual, f.expected]); if (!seen.has(key)) seen.set(key, { ...f, n: 0 }); seen.get(key).n += 1; }
  console.log('=== 不匹配汇总（去重）===');
  for (const v of seen.values()) console.log(`${String(v.n).padStart(3)}× ${v.kind}: ${JSON.stringify(v.actual)} vs ${JSON.stringify(v.expected)}${v.site ? '\n     @ ' + v.site : ''}`);
  console.log(`总计 ${fails.length} 次断言失败，去重后 ${seen.size} 组`);
});
