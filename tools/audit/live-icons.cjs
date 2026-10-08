// 以指定 IntelliJ checkout 的 SVG 为几何与颜色基准，在浏览器内比较栅格结果。
const fs = require('node:fs');
const path = require('node:path');
module.exports = async function verifyIcons({ browser, port, stubHost, stubData, data, check }) {
  const referenceRoot = process.env.AUGIT_INTELLIJ_ROOT || 'D:/github/intellij-community';
  const entries = [
    ['yaml', 'fileTypes/yaml.svg', 'file', 'sample.yaml'],
    ['json', 'fileTypes/json.svg', 'file', 'sample.json'],
    ['markdown', 'fileTypes/markdown.svg', 'file', 'sample.md'],
    ['folder', 'nodes/folder.svg', 'folder', ''],
    ['copy', 'general/copy.svg', 'icon', 'copy'],
    ['search', 'general/search.svg', 'icon', 'search'],
    ['searchHistory', 'inline/searchHistory.svg', 'icon', 'history-search'],
    ['zoomIn', 'image/zoomIn.svg', 'icon', 'zoom-in'],
    ['zoomOut', 'image/zoomOut.svg', 'icon', 'zoom-out'],
    ['fitContent', 'image/fitContent.svg', 'icon', 'image-fit'],
    ['stash', 'vcs/shelve.svg', 'icon', 'stash'],
  ];
  const context = await browser.newContext();
  try {
    await context.addInitScript(stubHost); await context.addInitScript(stubData, data);
    const page = await context.newPage();
    for (const theme of ['light', 'dark']) {
      await page.goto(`http://127.0.0.1:${port}/index.html?scene=main-project&theme=${theme}`);
      await page.waitForFunction(() => window.__augitReady);
      for (const [name, resource, kind, value] of entries) {
        const base = path.join(referenceRoot, 'platform/icons/src/expui', resource);
        const dark = base.replace(/\.svg$/, '_dark.svg');
        const reference = fs.readFileSync(theme === 'dark' && fs.existsSync(dark) ? dark : base, 'utf8');
        const result = await page.evaluate(async ({ reference, kind, value, name }) => {
          const container = document.createElement('div');
          container.style.cssText = 'position:fixed;left:-1000px;top:0;color:var(--augit-icon)';
          const markup = kind === 'file' ? fileTypeIcon(value) : kind === 'folder' ? treeFolderIcon() : icon(value);
          container.innerHTML = markup + reference;
          document.body.append(container);
          try {
            const [actual, expected] = container.querySelectorAll('svg');
            const raster = async (svg) => {
              const clone = svg.cloneNode(true);
              const originals = [svg, ...svg.querySelectorAll('*')];
              [clone, ...clone.querySelectorAll('*')].forEach((element, index) => {
                const style = getComputedStyle(originals[index]);
                for (const key of ['fill', 'stroke', 'stroke-width', 'stroke-linecap', 'stroke-linejoin', 'fill-rule', 'clip-rule', 'opacity']) {
                  element.style.setProperty(key, style.getPropertyValue(key));
                }
                if (element.tagName === 'rect') for (const key of ['width', 'height', 'rx']) {
                  element.style.setProperty(key, style.getPropertyValue(key));
                }
                element.removeAttribute('class');
              });
              clone.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
              clone.setAttribute('width', '128'); clone.setAttribute('height', '128');
              const image = new Image();
              image.src = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(new XMLSerializer().serializeToString(clone));
              await image.decode();
              const canvas = document.createElement('canvas'); canvas.width = canvas.height = 128;
              const ctx = canvas.getContext('2d'); ctx.drawImage(image, 0, 0);
              return ctx.getImageData(0, 0, 128, 128).data;
            };
            const distance = (a, b) => {
              let pixels = 0;
              for (let index = 0; index < a.length; index += 4) {
                if ([0, 1, 2, 3].some((offset) => Math.abs(a[index + offset] - b[index + offset]) > 12)) pixels++;
              }
              return pixels;
            };
            const a = await raster(actual), b = await raster(expected);
            const result = { viewBox: actual.getAttribute('viewBox'), pixels: distance(a, b) };
            // 负向变体直接缩短复制横线，能区分几何相近但错误的实现。
            if (name === 'copy') {
              actual.querySelector('rect[x="5"]').setAttribute('width', '3');
              result.negativePixels = distance(await raster(actual), b);
            }
            return result;
          } finally { container.remove(); }
        }, { reference, kind, value, name });
        check(`${theme}/${name} 绘图网格为 16×16`, result.viewBox === '0 0 16 16');
        console.log(`INFO 图标 ${theme}/${name}: ${JSON.stringify(result)}`);
        check(`${theme}/${name} 几何与主题色符合本地权威（差异像素 ${result.pixels}/16384）`, result.pixels <= 32);
        if (name === 'copy') check(`${theme}/copy 负向短横线被同一判定拒绝`, result.negativePixels > 32);
      }
    }
  } finally { await context.close(); }
};
