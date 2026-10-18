// 仅供人工确认取证前景；限时等待，任何退出路径都清理本次信号文件。
const fs = require('node:fs/promises');
async function waitForCheckpoint(file, { attempts = 240, pause = ms => new Promise(resolve => setTimeout(resolve, ms)), access = fs.access, remove = path => fs.rm(path, { force: true }) } = {}) {
  if (!Number.isSafeInteger(attempts) || attempts < 1) throw new Error('等待次数无效');
  try {
    for (let index = 0; index < attempts; index++) {
      try { await access(file); return; }
      catch (error) { if (error.code !== 'ENOENT') throw error; }
      await pause(500);
    }
    throw new Error('等待取证前景确认超时');
  } finally { await remove(file); }
}
module.exports = { waitForCheckpoint };
