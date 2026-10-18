// 仅计算取证窗口位置；背景、阴影、系统DPI与原始PNG保持不变。
const assert = require('node:assert/strict');
function placement(reference, body, radius) {
  const scale = reference.scale;
  assert.ok(Number.isFinite(scale) && scale > 0);
  assert.ok(Number.isInteger(radius) && radius > 0);
  assert.ok(reference.frameRectPhysical?.length === 4 && reference.frameRectPhysical.every(Number.isInteger));
  assert.ok([body.x, body.y, body.width, body.height].every(Number.isFinite));
  const [left, top, right, bottom] = reference.frameRectPhysical;
  const width = right-left, height = bottom-top;
  assert.ok(width > 0 && height > 0);
  const x0 = Math.ceil(body.x*scale)+radius+2;
  const y0 = Math.ceil(body.y*scale)+radius+2;
  const x1 = Math.floor((body.x+body.width)*scale)-radius-2;
  const y1 = Math.floor((body.y+body.height)*scale)-radius-2;
  assert.ok(x1-x0 >= width && y1-y0 >= height, '完整采样带超出正文区域；拒绝裁切或压缩窗口');
  return {x:Math.floor((x0+x1-width)/2)/scale,y:Math.floor((y0+y1-height)/2)/scale,width:width/scale,height:height/scale,radius};
}
module.exports = {placement};
