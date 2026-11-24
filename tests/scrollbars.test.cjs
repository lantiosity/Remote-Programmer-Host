const { test } = require('node:test');
const assert = require('node:assert/strict');
const { metrics, offsetForThumb } = require('../WireLink.App/wwwroot/scrollbars.js');

test('short content has no scroll range or draggable travel', () => {
  assert.deepEqual(metrics(200, 100, 0), { range: 0, size: 200, travel: 0, position: 0 });
  assert.equal(offsetForThumb(50, metrics(200, 100, 0)), 0);
});

test('thumb represents the visible fraction and reaches both ends of its inset track', () => {
  const first = metrics(200, 800, 0, 196);
  const last = metrics(200, 800, 600, 196);
  assert.equal(first.size, 49);
  assert.equal(first.position, 0);
  assert.equal(last.position + last.size, 196);
  assert.equal(offsetForThumb(last.position, last), 600);
});

test('very long logs keep a usable thumb and clamp drag overshoot', () => {
  const geometry = metrics(200, 100000, 50000, 196, 28);
  assert.equal(geometry.size, 28);
  assert.equal(offsetForThumb(-100, geometry), 0);
  assert.equal(offsetForThumb(500, geometry), 99800);
  assert.equal(metrics(200, 100000, -12, 196, 28).position, 0);
  assert.equal(metrics(200, 100000, 200000, 196, 28).position, geometry.travel);
});

test('scaled CSS geometry preserves thumb proportions and scroll position', () => {
  const base = metrics(200, 100000, 50000, 196, 28);
  for (const scale of [1.25, 1.5, 2, 3]) {
    const scaled = metrics(200 * scale, 100000 * scale, 50000 * scale, 196 * scale, 28 * scale);
    assert.ok(Math.abs(scaled.size - base.size * scale) < 1e-8);
    assert.ok(Math.abs(scaled.position - base.position * scale) < 1e-8);
    assert.ok(Math.abs(offsetForThumb(scaled.position, scaled) - 50000 * scale) < 1e-8);
  }
});

test('drag distance maps back to scroll offset for different viewport sizes', () => {
  for (const [viewport, extent, offset] of [[280, 429, 40], [736, 1800, 600], [96, 50000, 19345]]) {
    const geometry = metrics(viewport, extent, offset, viewport - 4);
    assert.ok(Math.abs(offsetForThumb(geometry.position, geometry) - offset) < 1e-8);
  }
});
