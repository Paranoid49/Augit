// 像素诊断必须识别不同通道，不能把空证据判为通过。
const test = require('node:test');
const assert = require('node:assert/strict');
const { compareRaster } = require('./font-source-raster.cjs');
test('相同像素通过，任何RGBA通道不同均计数', () => {
  const first = Buffer.from([30, 31, 34, 255, 188, 190, 196, 255]);
  assert.deepEqual(compareRaster(first, first), { differentPixels: 0, maxDifference: 0, equal: true });
  const second = Buffer.from(first); second[2] += 4; second[7] -= 1;
  assert.deepEqual(compareRaster(first, second), { differentPixels: 2, maxDifference: 4, equal: false });
});
test('拒绝空证据、残缺像素和不同尺寸', () => {
  assert.throws(() => compareRaster(Buffer.alloc(0), Buffer.alloc(0)));
  assert.throws(() => compareRaster(Buffer.alloc(3), Buffer.alloc(3)));
  assert.throws(() => compareRaster(Buffer.alloc(4), Buffer.alloc(8)));
});
