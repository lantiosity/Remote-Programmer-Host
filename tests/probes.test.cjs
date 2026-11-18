'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const { runInNewContext } = require('node:vm');

const app = readFileSync(join(__dirname, '../WireLink.App/wwwroot/app.js'), 'utf8');

function scan(selection, ids) {
  const input = { value: selection };
  const options = [];
  const list = { replaceChildren() {}, append(option) { options.push(option.value); } };
  const element = { addEventListener() {}, append() {}, classList: { remove() {} } };
  const context = {
    document: {
      documentElement: { dataset: {}, style: {} },
      querySelector: selector => selector === '[data-setting="usb_id"]' ? input : element,
      querySelectorAll: () => [], getElementById: () => list, createElement: () => ({})
    },
    window: {
      WireLinkRequests: { create() {} }, WireLinkPickers: { create() {} },
      matchMedia: () => ({ addEventListener() {} })
    },
    TextDecoder, setTimeout() {}, clearTimeout() {}, setInterval() {}
  };
  runInNewContext(app, context);
  context.data('probes', ids.map(unique_id => ({ unique_id })));
  return { selected: input.value, options };
}

test('USB scan keeps the selected probe only while it is present', () => {
  assert.deepEqual(scan('A', ['B', 'A']), { selected: 'A', options: ['B', 'A'] });
  assert.deepEqual(scan('A', ['B']), { selected: '', options: ['B'] });
  assert.deepEqual(scan('', ['B']), { selected: '', options: ['B'] });
});
