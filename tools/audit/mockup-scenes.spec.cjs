// 视觉稿场景渲染校验：对 docs/ux-mockups/ 下每个场景页做一次真实渲染，
// 断言没有页面脚本错误、且渲染出了内容。视觉稿既是设计基线也是运行时界面来源，
// 因此改动 mockup.js/mockup.css 后必须确认每个场景都还能渲染。
// 用法：node tools/audit/mockup-scenes.spec.cjs [playwright 模块路径] [主题]
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');

const MODULE = process.argv[2] || '/root/.npm/_npx/e41f203b7505f1fb/node_modules/playwright';
const THEME = process.argv[3] || 'dark';
const MIME = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.png': 'image/png',
  '.svg': 'image/svg+xml',
};

// 非场景页：索引页与资源页不算场景。
const SKIP = new Set(['index.html']);

function startStaticServer(root) {
  const server = http.createServer((request, response) => {
    const url = new URL(request.url, 'http://127.0.0.1');
    const relative = decodeURIComponent(url.pathname).replace(/^\//, '') || 'index.html';
    const target = path.join(root, relative);
    if (!target.startsWith(root) || !fs.existsSync(target) || fs.statSync(target).isDirectory()) {
      response.writeHead(404);
      response.end('not found');
      return;
    }
    response.writeHead(200, { 'Content-Type': MIME[path.extname(target)] || 'application/octet-stream' });
    fs.createReadStream(target).pipe(response);
  });
  return new Promise((resolve) => server.listen(0, '127.0.0.1', () => resolve(server)));
}

async function main() {
  const { chromium } = require(path.resolve(MODULE));
  const root = path.resolve(__dirname, '../../docs/ux-mockups');
  const scenes = fs.readdirSync(root).filter((name) => name.endsWith('.html') && !SKIP.has(name)).sort();
  if (scenes.length === 0) throw new Error('没有找到场景页。');

  const server = await startStaticServer(root);
  const port = server.address().port;
  const browser = await chromium.launch({ headless: true });
  const failures = [];
  let passed = 0;
  try {
    const context = await browser.newContext({ viewport: { width: 1180, height: 760 }, deviceScaleFactor: 1 });
    for (const scene of scenes) {
      const page = await context.newPage();
      const errors = [];
      page.on('pageerror', (error) => errors.push(error.message));
      page.on('console', (message) => {
        if (message.type() === 'error') errors.push('console: ' + message.text());
      });
      try {
        await page.goto(`http://127.0.0.1:${port}/${scene}?theme=${THEME}`, { waitUntil: 'load' });
        await page.waitForTimeout(120);
        const state = await page.evaluate(() => {
          const shell = document.querySelector('.augit-window, .window-shell, body > *');
          return {
            text: (document.body.innerText || '').replace(/\s+/g, '').length,
            shell: !!shell,
          };
        });
        if (errors.length > 0) {
          failures.push(`${scene}: 页面错误 ${JSON.stringify(errors.slice(0, 2))}`);
        } else if (!state.shell || state.text < 20) {
          failures.push(`${scene}: 渲染内容过少 text=${state.text} shell=${state.shell}`);
        } else {
          passed += 1;
        }
      } catch (error) {
        failures.push(`${scene}: ${error.message}`);
      } finally {
        await page.close();
      }
    }
  } finally {
    await browser.close();
    server.close();
  }

  if (failures.length > 0) {
    throw new Error(`场景渲染失败 ${failures.length}/${scenes.length}:\n` + failures.join('\n'));
  }
  console.log(`mockup-scenes 通过 ${passed}/${scenes.length} 个场景（主题=${THEME}）`);
}

main().catch((error) => {
  console.error('mockup-scenes 失败：' + error.message);
  process.exit(1);
});
