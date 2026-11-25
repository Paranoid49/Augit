const { test } = require('node:test');
const assert = require('node:assert/strict');
const {
  countChangedPixels, frameStats, stableRuns, changedFraction, classifyStates, mergeStates, assessModalTimeline,
} = require('./modal-transition-analysis.cjs');

const W = 8;
const H = 4;
const PIXELS = W * H;
const RECT = { left: 1, top: 1, right: 6, bottom: 3 };
const OUTSIDE = PIXELS - (RECT.right - RECT.left) * (RECT.bottom - RECT.top);

function frame(fill = 200) {
  const buffer = Buffer.alloc(W * H * 3);
  for (let index = 0; index < PIXELS; index += 1) {
    buffer[index * 3] = fill;
    buffer[index * 3 + 1] = fill;
    buffer[index * 3 + 2] = fill;
  }
  return buffer;
}

function paint(buffer, x, y, value) {
  const offset = (y * W + x) * 3;
  buffer[offset] = value;
  buffer[offset + 1] = value;
  buffer[offset + 2] = value;
}

function dialogFrame() {
  const buffer = frame(200);
  // 对话框覆盖框内约 30%（8 个像素），远高于 5% 分类阈值。
  for (const [x, y] of [[1, 1], [2, 1], [3, 1], [4, 1], [1, 2], [2, 2], [3, 2], [4, 2]]) paint(buffer, x, y, 60);
  return buffer;
}

function halfBlend() {
  return frame(170);
}

test('区域内与带阴影余量的区域外变化分别计数', () => {
  const before = frame(200);
  const after = frame(200);
  paint(after, 1, 1, 0);
  paint(after, 7, 3, 0);
  assert.equal(countChangedPixels(before, after, W, H, { rect: RECT }), 1);
  assert.equal(countChangedPixels(before, after, W, H, { rect: RECT, outside: true }), 1);
  const neighbour = frame(200);
  paint(neighbour, 6, 1, 0); // 正好在框右边界外，属于阴影余量带
  assert.equal(countChangedPixels(before, neighbour, W, H, { rect: RECT, outside: true, margin: 2 }), 0);
  assert.equal(countChangedPixels(before, neighbour, W, H, { rect: RECT, outside: true, margin: 0 }), 1);
});

test('容差内的像素差异不算变化', () => {
  const before = frame(200);
  const after = frame(202);
  assert.equal(countChangedPixels(before, after, W, H), 0);
  paint(after, 0, 0, 203);
  assert.equal(countChangedPixels(before, after, W, H), 1);
  assert.equal(changedFraction(before, after, W, H), 1 / PIXELS);
});

test('单帧摘要给出变化像素、平均亮度和近黑比例', () => {
  const before = frame(200);
  const after = frame(200);
  paint(after, 0, 0, 0);
  const stats = frameStats(before, after, W, H);
  assert.equal(stats.changed, 1);
  assert.equal(stats.mean, Math.round(((200 * (PIXELS - 1)) / PIXELS) * 1000) / 1000);
  assert.equal(stats.darkFraction, Math.round((1 / PIXELS) * 1000000) / 1000000);
  assert.equal(frameStats(null, after, W, H).changed, null);
});

test('稳定段按连续无变化帧切分并过滤短段', () => {
  const stats = [
    { changed: 5, mean: 200 }, { changed: 0, mean: 200 }, { changed: 0, mean: 200 }, { changed: 0, mean: 200 },
    { changed: 9, mean: 30 }, { changed: 0, mean: 30 }, { changed: 0, mean: 30 },
    { changed: 7, mean: 200 }, { changed: 0, mean: 200 }, { changed: 0, mean: 200 }, { changed: 0, mean: 200 }, { changed: 0, mean: 200 },
  ];
  assert.deepEqual(stableRuns(stats).map((run) => [run.start, run.end]), [[1, 3], [5, 6], [8, 11]]);
  assert.deepEqual(stableRuns(stats, 3).map((run) => [run.start, run.end]), [[1, 3], [8, 11]]);
});

test('按与首帧的差异分类并合并光标闪烁造成的同类分段', () => {
  const main = frame(200);
  const dialog = dialogFrame();
  const classifications = classifyStates(main, [main, dialog, dialog, main], W, H);
  assert.deepEqual(classifications, ['main', 'dialog', 'dialog', 'main']);
  const runs = [
    { start: 0, end: 30, length: 31 }, { start: 32, end: 40, length: 9 },
    { start: 42, end: 60, length: 19 }, { start: 62, end: 90, length: 29 },
  ];
  const states = mergeStates(runs, classifications);
  assert.deepEqual(states.map((state) => state.className), ['main', 'dialog', 'main']);
  assert.equal(states[1].start, 32);
  assert.equal(states[1].end, 60);
});

function timelineInput() {
  const main = frame(200);
  const dialog = dialogFrame();
  return {
    states: [{ className: 'main', frame: main }, { className: 'dialog', frame: dialog }, { className: 'main', frame: main }],
    intermediate: [
      { index: 31, frame: dialog, previous: main, next: dialog },
      { index: 61, frame: main, previous: dialog, next: main },
    ],
  };
}

test('“主界面→对话框→主界面”且过渡帧等同于某端时通过', () => {
  const result = assessModalTimeline({ ...timelineInput(), width: W, height: H, rect: RECT, margin: 0 });
  assert.equal(result.failures.length, 0);
  assert.deepEqual(result.classifications, ['main', 'dialog', 'main']);
});

test('半透明混合过渡帧被判定为闪烁', () => {
  const input = timelineInput();
  input.intermediate = [{ index: 31, frame: halfBlend(), previous: input.states[0].frame, next: input.states[1].frame }];
  const result = assessModalTimeline({ ...input, width: W, height: H, rect: RECT, margin: 0 });
  assert.ok(result.failures.some((failure) => failure.kind === 'blend-transition'));
});

test('整窗黑帧过渡被判定为闪烁', () => {
  const input = timelineInput();
  input.intermediate = [{ index: 31, frame: frame(5), previous: input.states[0].frame, next: input.states[1].frame }];
  const result = assessModalTimeline({ ...input, width: W, height: H, rect: RECT, margin: 0 });
  assert.ok(result.failures.some((failure) => failure.kind === 'dark-flash'));
});

test('对话框打开时框外像素变化被拒绝', () => {
  const input = timelineInput();
  const dialog = input.states[1].frame;
  for (let y = 0; y < H; y += 1) for (let x = 0; x < W; x += 1) if (x < RECT.left || x >= RECT.right) paint(dialog, x, y, 210);
  input.intermediate = [];
  const result = assessModalTimeline({ ...input, width: W, height: H, rect: RECT, margin: 0 });
  assert.ok(result.failures.some((failure) => failure.kind === 'outside-changed'));
});

test('背景与打开前或打开后的主界面之一一致即可通过', () => {
  const input = timelineInput();
  // 打开前的主界面有一处合法状态文字更新，背景快照取自更新后；与关闭后主界面一致。
  paint(input.states[0].frame, 0, 0, 120);
  const after = frame(200);
  input.states = [input.states[0], input.states[1], { className: 'main', frame: after }];
  input.intermediate = [];
  const result = assessModalTimeline({ ...input, width: W, height: H, rect: RECT, margin: 0 });
  assert.equal(result.failures.length, 0);
});

test('状态不交替时被拒绝', () => {
  const input = timelineInput();
  input.states = [input.states[0], input.states[0], input.states[2]];
  input.intermediate = [];
  const result = assessModalTimeline({ ...input, width: W, height: H, rect: RECT, margin: 0 });
  assert.ok(result.failures.some((failure) => failure.kind === 'state-order'));
});

test('分析矩形与区域面积按分析坐标系计算', () => {
  const input = timelineInput();
  const result = assessModalTimeline({ ...input, width: W, height: H, rect: RECT, margin: 1 });
  const expectedOutside = OUTSIDE - 6; // margin=1 时框外排除带扩大 6 像素
  assert.equal(result.outsideChangedFractions.length, 1);
  assert.ok(expectedOutside > 0);
});
