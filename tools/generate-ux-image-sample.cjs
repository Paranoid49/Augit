// 将自有 SVG 样图生成共享 PNG；原生审计与 HTML 使用完全相同的像素文件。
const fs = require('node:fs/promises');
const path = require('node:path');
async function main() {
  const [modulePath, executablePath] = process.argv.slice(2);
  const { chromium } = require(path.resolve(modulePath));
  const browser = await chromium.launch({ executablePath, headless: true });
  try {
    const page = await browser.newPage({ viewport: { width: 1920, height: 1200 }, deviceScaleFactor: 1 });
    const assets = path.resolve(__dirname, '../docs/ux-mockups/assets');
    await page.setContent(`<style>body{margin:0;background:transparent}svg{display:block}</style>${await fs.readFile(path.join(assets, 'image-sample.svg'), 'utf8')}`);
    await page.screenshot({ path: path.join(assets, 'image-sample.png'), omitBackground: true });
  } finally { await browser.close(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
