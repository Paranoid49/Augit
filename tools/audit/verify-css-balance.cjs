#!/usr/bin/env node
/**
 * 校验 `web/src/mockup.css` 的括号配平。
 *
 * 为什么需要单独检查：现代 CSS 支持嵌套，漏写一个 `}` **不会**让浏览器报错，
 * 而是把它后面的所有规则静默当成嵌套规则——多数情况下因祖先选择器仍匹配而"看起来正常"，
 * 但精确选择器（例如 `.rail-button.active:focus`）会失效，极难排查。
 * 第 78 轮就是编辑时吃掉了一个 `}`，导致其后 481 条规则被嵌套。
 *
 * 用法：node tools/audit/verify-css-balance.cjs
 * 退出码：0 = 配平；1 = 不配平（并打印首个异常位置）。
 */
const fs = require("node:fs");
const path = require("node:path");

const targets = ["web/src/mockup.css"];

/** 去掉注释但保留换行，避免注释里的花括号影响计数，同时保留行号。 */
function stripComments(text) {
  return text.replace(/\/\*[\s\S]*?\*\//g, (match) => match.replace(/[^\n]/g, ""));
}

let failed = false;
for (const rel of targets) {
  const abs = path.resolve(__dirname, "..", "..", rel);
  const source = stripComments(fs.readFileSync(abs, "utf8"));
  let depth = 0;
  let lastZero = 0;
  let firstNegative = 0;
  const lines = source.split("\n");
  lines.forEach((line, index) => {
    for (const ch of line) {
      if (ch === "{") depth += 1;
      else if (ch === "}") depth -= 1;
    }
    if (depth < 0 && firstNegative === 0) firstNegative = index + 1;
    if (depth === 0) lastZero = index + 1;
  });

  if (depth !== 0 || firstNegative !== 0) {
    failed = true;
    console.error(`FAIL    ${rel}：括号不配平（最终深度 ${depth}${firstNegative ? `，首个负深度在第 ${firstNegative} 行` : ""}）`);
    if (depth > 0) {
      console.error(`        最后一次深度归零在第 ${lastZero} 行，其后 ${lines.length - lastZero} 行都会被当作嵌套规则`);
    }
  } else {
    console.log(`OK      ${rel}：括号配平（${lines.length} 行）`);
  }
}

if (failed) {
  console.error("FAIL: CSS 括号配平检查未通过。");
  process.exit(1);
}
console.log("PASS: CSS 括号配平。");
