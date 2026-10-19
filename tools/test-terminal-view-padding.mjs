// 参考终端首行在工具窗顶下约 11 物理px；11 CSS 顶距会多出约 15px。
// 守护 web 与视觉稿的 .terminal-view 顶部内距，防止回退到 11px。
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';

const files = ['web/src/mockup.css', 'docs/ux-mockups/mockup.css'];

for (const file of files) {
  test(`${file} 终端首行顶部内距`, async () => {
    const css = await fs.readFile(file, 'utf8');
    assert.match(css, /参考终端首行距工具窗顶约 11 物理px/);
    assert.match(css, /\.terminal-view\s*\{[^}]*padding:\s*2\.5px 12px 11px;/s);
    assert.doesNotMatch(css, /\.terminal-view\s*\{[^}]*padding:\s*11px 12px;/s);
  });
}
