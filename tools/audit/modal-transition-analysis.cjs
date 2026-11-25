// 原生模态开关的逐帧判据：主界面不能被黑帧/淡出替换，对话框外像素必须与打开前一致。
// 只做纯计算，真实采样由 verify-modal-transition-frames.cjs 提供，便于单元测试直接构造帧。
const assert = require('node:assert/strict');

const DEFAULT_TOLERANCE = 2;
const DIALOG_STATE_FRACTION = 0.05;

function assertFrame(buffer, width, height, name = '帧') {
  assert.ok(buffer && typeof buffer.length === 'number', `${name}必须是像素缓冲`);
  assert.equal(buffer.length, width * height * 3, `${name}长度必须等于 width*height*3`);
}

function clampRect(width, height, rect) {
  const left = Math.max(0, Math.min(width, Math.floor(rect.left)));
  const top = Math.max(0, Math.min(height, Math.floor(rect.top)));
  const right = Math.max(left, Math.min(width, Math.ceil(rect.right)));
  const bottom = Math.max(top, Math.min(height, Math.ceil(rect.bottom)));
  return { left, top, right, bottom };
}

// 统计两帧之间变化像素数。传入 rect 只统计框内；传 outside 统计框外（可带 margin 排除阴影带）。
function countChangedPixels(previous, current, width, height, options = {}) {
  assertFrame(previous, width, height, '前帧');
  assertFrame(current, width, height, '后帧');
  const tolerance = options.tolerance ?? DEFAULT_TOLERANCE;
  const margin = Math.max(0, Math.floor(options.margin ?? 0));
  const rect = options.rect ? clampRect(width, height, options.rect) : { left: 0, top: 0, right: width, bottom: height };
  const expanded = {
    left: Math.max(0, rect.left - margin), top: Math.max(0, rect.top - margin),
    right: Math.min(width, rect.right + margin), bottom: Math.min(height, rect.bottom + margin),
  };
  const outside = options.outside === true;
  let changed = 0;
  for (let y = 0; y < height; y += 1) {
    for (let x = 0; x < width; x += 1) {
      const inside = x >= rect.left && x < rect.right && y >= rect.top && y < rect.bottom;
      const inMarginBand = x >= expanded.left && x < expanded.right && y >= expanded.top && y < expanded.bottom;
      if (outside ? inMarginBand : !inside) continue;
      const offset = (y * width + x) * 3;
      if (Math.max(
        Math.abs(current[offset] - previous[offset]),
        Math.abs(current[offset + 1] - previous[offset + 1]),
        Math.abs(current[offset + 2] - previous[offset + 2]),
      ) > tolerance) changed += 1;
    }
  }
  return changed;
}

// 单帧摘要：相对前帧的变化像素、平均亮度、近黑像素比例。首帧 previous 传 null。
function frameStats(previous, current, width, height, tolerance = DEFAULT_TOLERANCE) {
  assertFrame(current, width, height, '当前帧');
  let changed = 0;
  let sum = 0;
  let dark = 0;
  const total = width * height;
  for (let index = 0; index < total; index += 1) {
    const offset = index * 3;
    const red = current[offset], green = current[offset + 1], blue = current[offset + 2];
    sum += red + green + blue;
    if (Math.max(red, green, blue) < 24) dark += 1;
    if (previous && (
      Math.abs(red - previous[offset]) > tolerance
      || Math.abs(green - previous[offset + 1]) > tolerance
      || Math.abs(blue - previous[offset + 2]) > tolerance)) changed += 1;
  }
  return {
    changed: previous ? changed : null,
    mean: Math.round((sum / (total * 3)) * 1000) / 1000,
    darkFraction: Math.round((dark / total) * 1000000) / 1000000,
  };
}

// 稳定段：连续 changed===0 的帧（至少 minFrames 帧），代表画面静止的一段时间。
function stableRuns(stats, minFrames = 2) {
  assert.ok(Array.isArray(stats) && stats.length > 0, '需要帧摘要');
  assert.ok(Number.isSafeInteger(minFrames) && minFrames >= 1, '稳定帧下限无效');
  const runs = [];
  let start = null;
  for (let index = 0; index < stats.length; index += 1) {
    const stable = stats[index].changed === 0;
    if (stable && start === null) start = index;
    if ((!stable || index === stats.length - 1) && start !== null) {
      const end = stable ? index : index - 1;
      if (end - start + 1 >= minFrames) runs.push({ start, end, length: end - start + 1, mean: stats[start].mean, darkFraction: stats[start].darkFraction });
      start = null;
    }
  }
  return runs;
}

function changedFraction(previous, current, width, height, tolerance = DEFAULT_TOLERANCE) {
  return countChangedPixels(previous, current, width, height, { tolerance }) / (width * height);
}

// 以第一段主界面为基准，把稳定段按“是否出现对话框”分类。光标闪烁会把同一画面切成多段，
// 因此分类必须与基准帧比较，而不是只和上一段比较。
function classifyStates(reference, frames, width, height, options = {}) {
  assertFrame(reference, width, height, '状态基准帧');
  const threshold = options.dialogFraction ?? DIALOG_STATE_FRACTION;
  return frames.map((frame, index) => {
    assertFrame(frame, width, height, `状态 ${index} 代表帧`);
    return changedFraction(reference, frame, width, height) > threshold ? 'dialog' : 'main';
  });
}

// 把连续同类稳定段合并为逻辑状态；对话框内的光标闪烁不能变成“对话框→主界面→对话框”。
function mergeStates(runs, classifications) {
  assert.equal(runs.length, classifications.length, '稳定段与分类数量必须一致');
  const states = [];
  for (let index = 0; index < runs.length; index += 1) {
    const className = classifications[index];
    assert.ok(className === 'main' || className === 'dialog', `未知状态分类：${className}`);
    const previous = states[states.length - 1];
    if (previous && previous.className === className) {
      previous.end = runs[index].end;
      previous.runEnd = index;
      if (runs[index].length > runs[previous.runStart].length) previous.runStart = index;
      continue;
    }
    states.push({ className, start: runs[index].start, end: runs[index].end, runStart: index, runEnd: index });
  }
  return states;
}

const TRANSITION_MATCH_LIMIT = 0.02;
const OUTSIDE_LIMIT = 0.002;
const FLASH_DARK_FRACTION = 0.8;
const FLASH_MAX_MEAN = 24;

// states: [{className, frame}]；intermediate: [{index, frame, previous, next}]
// 判定：状态必须是主界面/对话框交替；每个过渡中间帧必须等价于某一端（不能是半透明混合或黑帧）；
// 对话框期间框外像素必须与打开前一致。
function assessModalTimeline({ states, intermediate, width, height, rect, margin = 0, outsideLimit = OUTSIDE_LIMIT }) {
  assert.ok(Array.isArray(states) && states.length > 0, '缺少稳定状态');
  assert.ok(Array.isArray(intermediate), '缺少过渡帧');
  const failures = [];
  const expected = states.map((_, index) => (index % 2 === 0 ? 'main' : 'dialog'));
  const classifications = states.map((state) => state.className);
  if (classifications.some((value, index) => value !== expected[index])) {
    failures.push({ kind: 'state-order', expected: expected.join(','), actual: classifications.join(',') });
  }
  const outsideArea = countArea(width, height, rect).outside;
  const outsideChangedFractions = [];
  for (let index = 1; index < states.length; index += 1) {
    if (states[index].className !== 'dialog') continue;
    // 背景快照取打开瞬间的画面；等待期间背景内容可能有一次合法刷新（如状态栏文字），
    // 因此与打开前后两段主界面比较并取较小差异，只有两段都对不上才判为背景不一致。
    const candidates = [countChangedPixels(states[index - 1].frame, states[index].frame, width, height, { rect, outside: true, margin })];
    if (index + 1 < states.length) candidates.push(countChangedPixels(states[index + 1].frame, states[index].frame, width, height, { rect, outside: true, margin }));
    const changed = Math.min(...candidates);
    const fraction = changed / Math.max(1, outsideArea);
    outsideChangedFractions.push({ index, changed, fraction: Math.round(fraction * 1000000) / 1000000 });
    if (fraction > outsideLimit) failures.push({ kind: 'outside-changed', state: index, changed, fraction });
  }
  const transitions = [];
  for (const item of intermediate) {
    const beforeFraction = changedFraction(item.previous, item.frame, width, height);
    const afterFraction = changedFraction(item.next, item.frame, width, height);
    const stats = frameStats(item.previous, item.frame, width, height);
    const record = {
      index: item.index,
      beforeFraction: Math.round(beforeFraction * 1000000) / 1000000,
      afterFraction: Math.round(afterFraction * 1000000) / 1000000,
      mean: stats.mean,
      darkFraction: stats.darkFraction,
    };
    transitions.push(record);
    const dark = stats.mean < FLASH_MAX_MEAN && stats.darkFraction > FLASH_DARK_FRACTION;
    if (dark) failures.push({ kind: 'dark-flash', ...record });
    else if (beforeFraction > TRANSITION_MATCH_LIMIT && afterFraction > TRANSITION_MATCH_LIMIT) {
      failures.push({ kind: 'blend-transition', ...record });
    }
  }
  return { passed: failures.length === 0, classifications, outsideChangedFractions, transitions, failures };
}

function countArea(width, height, rect) {
  const clamped = clampRect(width, height, rect);
  const inside = (clamped.right - clamped.left) * (clamped.bottom - clamped.top);
  return { inside, outside: width * height - inside };
}

module.exports = {
  DEFAULT_TOLERANCE,
  DIALOG_STATE_FRACTION,
  countChangedPixels,
  countArea,
  frameStats,
  stableRuns,
  changedFraction,
  classifyStates,
  mergeStates,
  assessModalTimeline,
};
