// 验证真实随包字形、各正文角色及异步字体切换代际，系统已安装字体不能充当资源加载证据。
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const output = path.resolve('artifacts/pixel-font-followup-2026-10-13');

module.exports = async ({ openScene, check }) => {
  const records = [];
  const cases = [
    ['text-viewer', '&open=docs/notes.txt', '.code-line > span:last-child'],
    ['commit-diff', '&diff=src%2FApp.cs', '.diff-code-line'],
    ['conflict-resolver', '', '.conflict-line'],
    ['markdown-preview', '&open=docs/product-spec.md', '.markdown-preview code'],
    ['terminal', '', '.terminal-view .xterm'],
  ];
  for (const [name, query, selector] of cases) {
    const { page, errors } = await openScene(`scene=${name}&theme=light&ui-font-size=12${query}`, () => {
      window.__settingsOverride = { monospaceFontFamily: 'JetBrains Mono' };
      window.__terminalBootOutput = 'Ag 0123456789\r\n';
      const original = window.__hostStub;
      window.__hostStub = async (method, params) => {
        const result = await original(method, params);
        if (method === 'document/read') return { ...result, text: params.path.endsWith('.md') ? '# Reference\n\nBody `value`\n\n```\nAg 0123456789\n```\n' : 'Ag 0123456789\nFirst line\nSecond line\n' };
        if (method === 'git/conflict/read') return { ...result, yoursText: 'Ag left\n', theirsText: 'Ag right\n', resultText: 'Ag result\n', blocks: [] };
        return result;
      };
    });
    let client;
    try {
      await page.waitForFunction(() => window.__augitSettingsReady);
      await page.waitForSelector(selector);
      await page.evaluate(async () => {
        for (const style of ['normal 400', 'normal 700', 'italic 400', 'italic 700']) {
          const faces = await document.fonts.load(`${style} 13px "JetBrains Mono"`, 'Ag 0123456789');
          if (!faces.length || faces.some(face => face.status !== 'loaded')) throw Error('随包字形未加载：' + style);
        }
        await document.fonts.ready;
      });
      const role = await page.locator(selector).first().evaluate(node => ({ family: getComputedStyle(node).fontFamily, size: getComputedStyle(node).fontSize, ligatures: getComputedStyle(node).fontVariantLigatures }));
      assert.ok(role.family.includes('JetBrains Mono'), `${name}: ${JSON.stringify(role)}`);
      assert.equal(role.size, '13px');
      if (name !== 'terminal') assert.equal(role.ligatures, 'none', '参考默认关闭连字');
      const faces = await page.evaluate(() => [...document.fonts].filter(f => f.family.replaceAll('"', '') === 'JetBrains Mono').map(f => ({ family: f.family, style: f.style, weight: f.weight, status: f.status })));
      assert.equal(faces.length, 4); assert.ok(faces.every(face => face.status === 'loaded'));
      const widths = await page.evaluate(() => {
        const ctx = document.createElement('canvas').getContext('2d'); ctx.font = '13px "JetBrains Mono"';
        return [...'iWM0'].map(text => ctx.measureText(text).width);
      });
      assert.ok(Math.max(...widths) - Math.min(...widths) < .01, '等宽字形宽度相等');
      // 独立JBR实测及EditorView公式：度量分别取整后乘1.2，不能复制浏览器当前值作期望。
      const jbrMetrics = JSON.parse(await fs.readFile(path.join(__dirname, 'jetbrains-mono-reference-metrics.json'), 'utf8'));
      const referenceHeights = jbrMetrics.map(metric => metric.lineHeight);
      const measuredHeights = await page.evaluate(() => Array.from({length:32},(_,index)=>index+9).map(size => codeLineHeight('"JetBrains Mono"', size)));
      assert.deepEqual(measuredHeights, referenceHeights, '随包字体行高必须与独立JBR度量一致');
      if (name === 'text-viewer') {
        const rows = await page.locator('.code-line').evaluateAll(nodes => nodes.slice(0, 3).map(node => node.getBoundingClientRect().top));
        assert.ok(Math.abs(rows[1] - rows[0] - 22) < .1);
        assert.ok(Math.abs(rows[2] - rows[1] - 22) < .1);
        // 临时恢复旧包围盒公式，负向对照必须能检出原20.4px行高。
        const previous = await page.evaluate(() => { const ctx = document.createElement('canvas').getContext('2d');ctx.font='13px "JetBrains Mono"';const m=ctx.measureText('Ag');return Math.ceil(Math.ceil((m.fontBoundingBoxAscent+m.fontBoundingBoxDescent)*devicePixelRatio)*1.2)/devicePixelRatio; });
        assert.notEqual(previous, 22, '旧浏览器公式不得误通过参考行高');
        check('JetBrains Mono行高匹配独立JBR参考且旧公式负向对照失败', true);
        const baselines=[];
        for (const metric of jbrMetrics) {
          await page.evaluate(size => applyTypography({codeSize:size,monospaceFamily:'JetBrains Mono'}),metric.size);
          const measured=await page.locator('.code-line').first().evaluate(row=>{const probe=document.createElement('i');probe.style.cssText='display:inline-block;width:0;height:0;vertical-align:baseline';row.lastElementChild.append(probe);const value=probe.getBoundingClientRect().top-row.getBoundingClientRect().top;probe.remove();return {baseline:value,height:row.getBoundingClientRect().height};});
          assert.equal(measured.height,metric.lineHeight,`字号${metric.size}的实际行盒`);
          assert.equal(measured.baseline,metric.baseline,`字号${metric.size}的实际正文基线`);
          baselines.push({size:metric.size,...measured});
        }
        await page.evaluate(()=>applyTypography({codeSize:13,monospaceFamily:'JetBrains Mono'}));
        await fs.writeFile(path.join(output,'text-baselines.json'),JSON.stringify(baselines,null,2)+'\n');
        check('32个等宽字号的实际正文行盒与基线匹配独立JBR参考',true);
      }
      if(name==='commit-diff') {
        const actual=await page.locator('.diff-code-line').first().evaluate(row=>{const glyph=row.querySelector('.code-glyph');const probe=document.createElement('i');probe.style.cssText='display:inline-block;width:0;height:0;vertical-align:baseline';glyph.append(probe);const baseline=probe.getBoundingClientRect().top-row.getBoundingClientRect().top;probe.remove();return {baseline,height:row.getBoundingClientRect().height};});
        assert.equal(actual.height,22);assert.equal(actual.baseline,16,'Diff墨迹基线与普通正文一致');
        check('Diff正文保持22px行盒并使用16px参考基线',true);
      }
      if(name==='conflict-resolver') {
        const actual=await page.locator('.conflict-line').first().evaluate(row=>{const probe=document.createElement('i');probe.style.cssText='display:inline-block;width:0;height:0;vertical-align:baseline';row.append(probe);const baseline=probe.getBoundingClientRect().top-row.getBoundingClientRect().top;probe.remove();return {baseline,height:row.getBoundingClientRect().height};});
        assert.equal(actual.height,22);assert.equal(actual.baseline,16,'冲突正文墨迹与普通正文同基线');
        check('三栏冲突保留原生编辑结构并对齐参考基线',true);
      }
      client = await page.context().newCDPSession(page); await client.send('DOM.enable'); await client.send('CSS.enable');
      // 终端canvas没有可枚举DOM字形；同时检查其实际xterm配置与已加载字体，不虚构canvas平台字形证据。
      let fonts = null;
      if (name !== 'terminal') {
        const { root } = await client.send('DOM.getDocument');
        const { nodeId } = await client.send('DOM.querySelector', { nodeId: root.nodeId, selector });
        fonts = (await client.send('CSS.getPlatformFontsForNode', { nodeId })).fonts;
        assert.ok(fonts.some(font => font.isCustomFont && font.familyName === 'JetBrains Mono' && font.glyphCount > 0), `${name}: ${JSON.stringify(fonts)}`);
      } else {
        await page.waitForFunction(() => window.__augitTerminalReady);
        const terminal = await page.evaluate(() => window.__augitTerminalFont());
        assert.ok(terminal.family.includes('JetBrains Mono')); assert.equal(terminal.size, 13);
      }
      const record = { name, role, faces, widths, fonts, referenceHeights, measuredHeights, evidence: name === 'terminal' ? '实际xterm配置与随包资源加载，未等同canvas像素配对' : '实际CDP自定义字形' };
      records.push(record); check(`${name}实际加载随包JetBrains Mono及四种字形`, true);

      if (name === 'text-viewer') {
        const result = await page.evaluate(async () => {
          const originalLoad = document.fonts.load.bind(document.fonts); let release;
          const blocked = new Promise(resolve => { release = resolve; }); let first = true;
          document.fonts.load = async (...args) => { if (first) { first = false; await blocked; } return originalLoad(...args); };
          try {
            const old = applyTypography({ codeSize: 40, monospaceFamily: 'JetBrains Mono' });
            await applyTypography({ codeSize: 17, monospaceFamily: 'Consolas' });
            const before = getComputedStyle(document.documentElement).getPropertyValue('--code-line-height');
            release(); await old;
            return { before, after: getComputedStyle(document.documentElement).getPropertyValue('--code-line-height'), size: getComputedStyle(document.documentElement).getPropertyValue('--augit-code-size') };
          } finally { document.fonts.load = originalLoad; }
        });
        assert.equal(result.before, result.after); assert.equal(result.size, '17px');
        check('延迟旧字体加载不能覆盖最新字体字号的行高', true);
      }
      assert.deepEqual(errors, []);
    } finally { await client?.detach(); await page.close(); }
  }
  await fs.mkdir(output, { recursive: true });
  await fs.writeFile(path.join(output, 'font-roles.json'), JSON.stringify(records, null, 2) + '\n');
};
