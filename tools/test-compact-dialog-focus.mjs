// 紧凑输入窗口 Tab 顺序回归：禁用的主按钮不能吞掉 Tab（焦点停在 Cancel 上）。
import assert from "node:assert/strict";
import { compactDialogFocusOrder, nextCompactDialogFocus } from "../web/src/compact-dialog-focus.js";

const node = (id, { disabled = false, ariaDisabled = false } = {}) => ({
  id,
  disabled,
  getAttribute: (name) => (name === "aria-disabled" && ariaDisabled ? "true" : null),
});

const field = node("field");
const cancel = node("cancel");
const confirmDisabled = node("confirm", { disabled: true });
const confirm = node("confirm");
const close = node("close");

// 空名称：主按钮禁用，Tab 顺序跳过它，不能停住。
const emptyOrder = compactDialogFocusOrder([field, cancel, confirmDisabled, close]);
assert.deepEqual(emptyOrder.map((item) => item.id), ["field", "cancel", "close"]);
assert.equal(nextCompactDialogFocus(emptyOrder, field).id, "cancel");
assert.equal(nextCompactDialogFocus(emptyOrder, cancel).id, "close");
assert.equal(nextCompactDialogFocus(emptyOrder, close).id, "field");
assert.equal(nextCompactDialogFocus(emptyOrder, field, true).id, "close");
// aria-disabled 与 disabled 同样跳过。
const ariaOrder = compactDialogFocusOrder([field, cancel, node("confirm", { ariaDisabled: true }), close]);
assert.deepEqual(ariaOrder.map((item) => item.id), ["field", "cancel", "close"]);
// 名称非空：主按钮加入循环，顺序回到 field → cancel → confirm → close。
const fullOrder = compactDialogFocusOrder([field, cancel, confirm, close]);
assert.deepEqual(fullOrder.map((item) => item.id), ["field", "cancel", "confirm", "close"]);
assert.equal(nextCompactDialogFocus(fullOrder, cancel).id, "confirm");
assert.equal(nextCompactDialogFocus(fullOrder, confirm).id, "close");
assert.equal(nextCompactDialogFocus(fullOrder, close).id, "field");
// 当前焦点不在顺序里：向前取首项，向后取末项；空顺序返回 null。
assert.equal(nextCompactDialogFocus(fullOrder, node("outside")).id, "field");
assert.equal(nextCompactDialogFocus(fullOrder, node("outside"), true).id, "close");
assert.equal(nextCompactDialogFocus([], field), null);
assert.equal(compactDialogFocusOrder(null).length, 0);

console.log("PASS: 紧凑输入窗口 Tab 顺序（禁用按钮跳过/循环/反向）。");
