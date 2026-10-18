// 只追踪隔离审计实例；按实际页面标记角色，不把进程数量当作界面稳定证据。
const fs = require('node:fs/promises');
const path = require('node:path');
async function traceRefreshCompositor(browser, businessPage, output, action) {
  const client = await browser.newBrowserCDPSession();
  let started = false, stream;
  const complete = new Promise(resolve => client.once('Tracing.tracingComplete', resolve));
  const categories = ['cc', 'viz', 'gpu', 'blink.user_timing', 'disabled-by-default-devtools.timeline'];
  try {
    await client.send('Tracing.start', {categories: categories.join(','), transferMode: 'ReturnAsStream'});
    started = true;
    await businessPage.evaluate(() => performance.mark('augit-role-business'));
    const result = await action();
    const roles = [];
    const businessSize = await businessPage.evaluate(() => [innerWidth, innerHeight]);
    for (const page of browser.contexts().flatMap(context => context.pages())) {
      const role = page === businessPage ? 'business' : await page.evaluate(size =>
        document.querySelector('img[src^="https://snapshot.augit.local/image.png"]')
          ? (innerWidth === size[0] && innerHeight === size[1] ? 'background' : 'cover') : 'unknown', businessSize);
      if (role === 'unknown') continue;
      await page.evaluate(role => performance.mark('augit-role-' + role), role);
      roles.push({role, url: page.url()});
    }
    await client.send('Tracing.end'); started = false;
    stream = (await complete).stream;
    if (!stream) throw Error('合成追踪没有返回数据流。');
    const chunks = [];
    for (;;) {
      const chunk = await client.send('IO.read', {handle: stream});
      chunks.push(chunk.base64Encoded ? Buffer.from(chunk.data, 'base64') : Buffer.from(chunk.data));
      if (chunk.eof) break;
    }
    await fs.writeFile(path.join(output, 'refresh-compositor-trace.json'), Buffer.concat(chunks));
    await fs.writeFile(path.join(output, 'refresh-compositor-roles.json'), JSON.stringify({categories, roles}, null, 2) + String.fromCharCode(10));
    return result;
  } finally {
    try {
      if (started) { await client.send('Tracing.end'); stream = (await complete).stream; }
      if (stream) await client.send('IO.close', {handle: stream});
    } finally { await client.detach(); }
  }
}
module.exports = {traceRefreshCompositor};
