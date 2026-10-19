#!/usr/bin/env node
// 紧凑输入窗口：名称为空时确认按钮必须禁用（对齐参考 New Tag / New Branch）。
// 运行：node tools/test-compact-input-empty.mjs
import { readFileSync } from "node:fs";

const root = new URL("../", import.meta.url);
const read = (p) => readFileSync(new URL(p, root), "utf8");
const live = read("web/src/live-data.js");
const mockup = read("web/src/mockup.js");

let pass = 0;
let fail = 0;
const check = (name, ok) => {
  if (ok) {
    pass += 1;
    console.log(`PASS ${name}`);
  } else {
    fail += 1;
    console.log(`FAIL ${name}`);
  }
};

const showCompact = live.match(/function showCompactInput\([\s\S]*?\n\}/)?.[0] || "";
check("紧凑窗口确认按钮存在", /data-compact-action="confirm"/.test(mockup));
check("showCompactInput 找到确认按钮", /\[data-compact-action="confirm"\]/.test(showCompact));
check("空名称时禁用确认", /confirm\.disabled\s*=\s*field\.value\.trim\(\)\.length\s*===\s*0/.test(showCompact));
check("输入变化时同步禁用态", /field\.addEventListener\("input",\s*syncConfirm\)/.test(showCompact));
check("创建窗口时立即同步一次", /syncConfirm\(\);\s*\n\s*\}\s*\n\s*\}\s*\n\}/.test(showCompact));
check("New Tag 仍走紧凑输入", /showCompactInput\(`Create New Tag On/.test(live));
check("New Branch 仍走紧凑输入", /showCompactInput\(title, label, value/.test(live));

console.log(`\nRESULT ${pass}/${pass + fail} ${fail === 0 ? "PASS" : "FAIL"}`);
process.exit(fail === 0 ? 0 : 1);
