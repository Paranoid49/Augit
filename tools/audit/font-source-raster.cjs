// 只读字体来源诊断：同一浏览器比较两种字体资源，不冒充真实终端截图。
const fs = require('node:fs/promises');
const path = require('node:path');
const crypto = require('node:crypto');
const assert = require('node:assert/strict');
const hash = bytes => crypto.createHash('sha256').update(bytes).digest('hex');

function compareRaster(left, right) {
  assert.equal(left.length, right.length, '两份像素长度必须相同');
  assert.ok(left.length > 0 && left.length % 4 === 0, '必须提供完整RGBA像素');
  let differentPixels = 0, maxDifference = 0;
  for (let index = 0; index < left.length; index += 4) {
    let difference = 0;
    for (let channel = 0; channel < 4; channel++) {
      difference = Math.max(difference, Math.abs(left[index + channel] - right[index + channel]));
    }
    if (difference) differentPixels++;
    maxDifference = Math.max(maxDifference, difference);
  }
  return { differentPixels, maxDifference, equal: differentPixels === 0 };
}

async function run(playwrightPath, executablePath, output, woffPath, ttfPath) {
  const resources = await Promise.all([woffPath, ttfPath].map(async filename => {
    const bytes = await fs.readFile(filename);
    return { path: path.resolve(filename), bytes, sha256: hash(bytes) };
  }));
  // 已有目录不能覆盖，失败也保留诊断证据；浏览器临时profile由close释放。
  await fs.mkdir(output);
  const { chromium } = require(playwrightPath);
  let browser;
  try {
    browser = await chromium.launch({ executablePath, headless: true });
    const context = await browser.newContext({ viewport: { width: 900, height: 300 }, deviceScaleFactor: 1.75 });
    const page = await context.newPage();
    const cases = await page.evaluate(async sources => {
      const families = ['AuditWoff', 'AuditTtf'];
      for (let index = 0; index < sources.length; index++) {
        const face = new FontFace(families[index], 'url(' + sources[index] + ')');
        await face.load();
        document.fonts.add(face);
        if (face.status !== 'loaded') throw Error('诊断字体加载失败');
      }
      const results = [];
      const texts = ['Ag 0123456789', Array.from({ length: 95 }, (_, index) => String.fromCharCode(index + 32)).join('')];
      for (const scale of [1, 1.25, 1.5, 1.75, 2]) {
        for (const theme of ['light', 'dark']) {
          const background = theme === 'dark' ? '#1e1f22' : '#ffffff';
          const foreground = theme === 'dark' ? '#bcbec4' : '#000000';
          const rasters = families.map(family => {
            const canvas = document.createElement('canvas');
            canvas.width = Math.ceil(800 * scale); canvas.height = Math.ceil(90 * scale);
            const ctx = canvas.getContext('2d', { alpha: false });
            ctx.fillStyle = background; ctx.fillRect(0, 0, canvas.width, canvas.height);
            ctx.scale(scale, scale); ctx.font = '13px ' + family; ctx.fillStyle = foreground;
            const metrics = texts.map((text, row) => {
              ctx.fillText(text, 10, 25 + row * 30);
              const measured = ctx.measureText(text);
              return { width: measured.width, ascent: measured.actualBoundingBoxAscent, descent: measured.actualBoundingBoxDescent };
            });
            const pixels = ctx.getImageData(0, 0, canvas.width, canvas.height).data;
            let inkPixels = 0;
            for (let index = 0; index < pixels.length; index += 4) {
              if (pixels[index] !== pixels[0] || pixels[index + 1] !== pixels[1] || pixels[index + 2] !== pixels[2]) inkPixels++;
            }
            // 分块编码，避免把大像素数组作为函数参数展开。
            let binary = '';
            for (let index = 0; index < pixels.length; index += 8192) binary += String.fromCharCode(...pixels.subarray(index, index + 8192));
            return { metrics, inkPixels, pixels: btoa(binary), png: canvas.toDataURL('image/png').split(',')[1] };
          });
          results.push({ scale, theme, rasters });
        }
      }
      return results;
    }, resources.map((resource, index) => 'data:font/' + (index ? 'ttf' : 'woff2') + ';base64,' + resource.bytes.toString('base64')));
    const reports = [];
    for (const item of cases) {
      const [left, right] = item.rasters;
      assert.ok(left.inkPixels > 100 && right.inkPixels > 100, '空白图不能作为字体相等证据');
      const comparison = compareRaster(Buffer.from(left.pixels, 'base64'), Buffer.from(right.pixels, 'base64'));
      const metricsEqual = JSON.stringify(left.metrics) === JSON.stringify(right.metrics);
      for (let index = 0; index < item.rasters.length; index++) {
        await fs.writeFile(path.join(output, `${item.theme}-${item.scale}-${index ? 'ttf' : 'woff2'}.png`), Buffer.from(item.rasters[index].png, 'base64'));
      }
      reports.push({ scale: item.scale, theme: item.theme, ...comparison, metricsEqual, inkPixels: [left.inkPixels, right.inkPixels], metrics: [left.metrics, right.metrics] });
    }
    const report = { browser: browser.version(), executablePath, sources: resources.map(({ path, bytes, sha256 }) => ({ path, bytes: bytes.length, sha256 })), reports, allEqual: reports.every(item => item.equal && item.metricsEqual), actualWindowPair: false, sfntTableComparison: false, productChanged: false };
    await fs.writeFile(path.join(output, 'report.json'), JSON.stringify(report, null, 2) + '\n');
    console.log(JSON.stringify({ output, cases: reports.length, allEqual: report.allEqual, differences: reports.map(item => item.differentPixels) }));
    return report;
  } finally {
    if (browser) await browser.close();
  }
}
module.exports = { compareRaster, run };
if (require.main === module) {
  const args = process.argv.slice(2);
  if (args.length !== 5) throw Error('参数：Playwright路径 浏览器路径 新证据目录 WOFF2路径 TTF路径');
  run(...args).catch(error => { console.error(error); process.exitCode = 1; });
}
