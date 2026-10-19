#!/usr/bin/env node
// New Worktree 布局回归：新分支复选行必须跨满 form-grid，避免 Directory 标签错位。
// 运行：node tools/test-worktree-check-row.mjs
import { readFileSync } from "node:fs";

const root = new URL("../", import.meta.url);
const read = (p) => readFileSync(new URL(p, root), "utf8");
const cssRuntime = read("web/src/mockup.css");
const cssMockup = read("docs/ux-mockups/mockup.css");
const live = read("web/src/live-data.js");

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

check("两个共享 CSS 字节一致", cssRuntime === cssMockup);
check("New Worktree 标记含 worktree-new-branch-row",
  /class="worktree-new-branch-row"/.test(live));
check("新分支行跨满两列",
  /\.worktree-dialog \.form-grid > \.worktree-new-branch-row\s*\{[^}]*grid-column:\s*1 \/ -1/.test(cssRuntime));
check("新分支行为 flex 同行",
  /\.worktree-dialog \.form-grid > \.worktree-new-branch-row\s*\{[^}]*display:\s*flex[^}]*align-items:\s*center/.test(cssRuntime));
check("新分支名输入可伸缩", /\.worktree-new-branch-row \.text-field\s*\{[^}]*flex:\s*1 1 auto/.test(cssRuntime));
check("复选标签不再单独占行", /\.worktree-new-branch-row \.check-line\s*\{[^}]*flex:\s*0 0 auto/.test(cssRuntime));

console.log(`\nRESULT ${pass}/${pass + fail} ${fail === 0 ? "PASS" : "FAIL"}`);
process.exit(fail === 0 ? 0 : 1);
