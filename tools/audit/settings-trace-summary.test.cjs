const assert = require('node:assert/strict');
const { summarize } = require('./settings-trace-summary.cjs');

const trace = {
  processId: 42,
  utc: '2026-10-22T00:00:00Z',
  frequency: 10000000,
  samples: [
    { Stage: 'created', Timestamp: 0 },
    { Stage: 'prepare-start', Timestamp: 1000000 },
    { Stage: 'capture-issued', Timestamp: 2000000 },
    { Stage: 'capture-end', Timestamp: 2500000 },
    { Stage: 'presented', Timestamp: 3000000 },
  ],
};

const result = summarize(trace);
assert.equal(result.createdToPresentedMs, 300);
assert.equal(result.prepareToPresentedMs, 200);
assert.equal(result.captureMs, 50);
assert.equal(result.captureTimedOut, false);

const timedOut = summarize({
  ...trace,
  samples: [...trace.samples, { Stage: 'capture-timeout', Timestamp: 2400000 }],
});
assert.equal(timedOut.captureTimedOut, true);

assert.throws(() => summarize({ frequency: 10000000, samples: [] }), /缺少阶段/);
assert.throws(() => summarize(null), /trace\.samples/);
console.log('settings-trace-summary 4/4 通过');
