// 汇总 SettingsStageTrace 的冷/热打开阶段；只读 trace JSON，不启动应用。
// 用法：node tools/audit/settings-trace-summary.cjs <trace.json>
const fs = require('node:fs');
const path = require('node:path');

function summarize(trace) {
  if (!trace || !Array.isArray(trace.samples)) throw new Error('trace.samples 缺失');
  const frequency = Number(trace.frequency) || 10000000;
  const toMs = (ticks) => Math.round(ticks / (frequency / 1000) * 10) / 10;
  const samples = new Map();
  for (const sample of trace.samples) {
    if (sample && typeof sample.Stage === 'string' && Number.isFinite(sample.Timestamp)) {
      samples.set(sample.Stage, sample.Timestamp);
    }
  }
  const need = ['created', 'prepare-start', 'capture-issued', 'capture-end', 'presented'];
  for (const stage of need) {
    if (!samples.has(stage)) throw new Error('缺少阶段：' + stage);
  }
  const elapsed = (from, to) => toMs(samples.get(to) - samples.get(from));
  return {
    processId: trace.processId ?? null,
    utc: trace.utc ?? null,
    createdToPresentedMs: elapsed('created', 'presented'),
    prepareToPresentedMs: elapsed('prepare-start', 'presented'),
    captureMs: elapsed('capture-issued', 'capture-end'),
    captureTimedOut: samples.has('capture-timeout'),
    stages: [...samples.keys()],
  };
}

module.exports = { summarize };

if (require.main === module) {
  const file = process.argv[2];
  if (!file) {
    console.error('用法：node tools/audit/settings-trace-summary.cjs <trace.json>');
    process.exit(2);
  }
  try {
    const trace = JSON.parse(fs.readFileSync(path.resolve(file), 'utf8').replace(/^\uFEFF/, ''));
    console.log(JSON.stringify(summarize(trace), null, 2));
  } catch (error) {
    console.error('FAIL: ' + error.message);
    process.exit(1);
  }
}
