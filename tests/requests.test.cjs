'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { create, valid } = require('../WireLink.App/wwwroot/requests.js');
function fixture(send = () => {}) {
  const timers = new Map(); let next = 0;
  const client = create({ send, setTimer(fn, delay) { timers.set(++next, { fn, delay }); return next; }, clearTimer(id) { timers.delete(id); } });
  return { client, timers };
}
test('response settles once and clears pending timer', async () => {
  let sent; const { client, timers } = fixture(message => sent = message);
  const result = client.request('scan');
  assert.equal(client.size, 1);
  assert.equal(client.receive({ id: sent.id, ok: true }), true);
  await result;
  assert.equal(client.size, 0); assert.equal(timers.size, 0);
  assert.equal(client.receive({ id: sent.id, ok: false }), false);
});
test('timeout rejects and ignores late response', async () => {
  const { client, timers } = fixture();
  const result = client.request('scan');
  const rejection = assert.rejects(result, /超时/);
  timers.get(1).fn(); await rejection;
  assert.equal(client.size, 0);
  assert.equal(client.receive({ id: '1', ok: true }), false);
});
test('bridge failure and page shutdown release requests', async () => {
  const broken = fixture(() => { throw new Error('bridge closed'); });
  await assert.rejects(broken.client.request('ready'), /bridge closed/);
  assert.equal(broken.client.size, 0); assert.equal(broken.timers.size, 0);
  const { client, timers } = fixture();
  const first = assert.rejects(client.request('scan'), /关闭/);
  const second = assert.rejects(client.request('targets'), /关闭/);
  client.rejectAll('页面已关闭'); await Promise.all([first, second]);
  assert.equal(client.size, 0); assert.equal(timers.size, 0);
});
test('hardware jobs allow their native deadline while heartbeat remains short', () => {
  const { client, timers } = fixture();
  client.request('flash').catch(() => {}); client.request('ping').catch(() => {});
  assert.equal(timers.get(1).delay, 3600000); assert.equal(timers.get(2).delay, 30000);
  client.rejectAll('end');
});
test('malformed device messages are rejected before rendering', () => {
  for (const message of [null, [], {}, { type: 'progress', value: NaN }, { type: 'rx', hex: '0xGG' },
    { type: 'job', busy: false, action: 'flash', elapsed: '1', status: 'success' },
    { type: 'data', kind: 'targets', value: [null] }, { type: 'data', kind: 'scan', value: [{}] },
    { type: 'data', kind: 'wifi_setup', value: { operation: 'scan', networks: [{ ssid: 'AP', rssi: 'bad' }] } }]) assert.ok(!valid(message));
  assert.ok(valid({ type: 'rx', hex: '00Aaff' }));
  assert.ok(valid({ type: 'job', busy: false, action: 'flash', elapsed: 1.2, status: 'success' }));
  assert.ok(valid({ type: 'hostState', busy: true, serial: 'closed' }));
});
