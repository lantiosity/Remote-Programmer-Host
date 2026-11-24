'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const { runInNewContext } = require('node:vm');

const app = readFileSync(join(__dirname, '../WireLink.App/wwwroot/app.js'), 'utf8');
const theme = readFileSync(join(__dirname, '../WireLink.App/wwwroot/theme-init.js'), 'utf8');

function fixture(selection, showModal = function () { this.open = true; }) {
  const input = { value: selection };
  const options = [];
  const list = { replaceChildren() {}, append(option) { options.push(option.value); } };
  const element = { addEventListener() {}, append() {}, classList: { remove() {}, toggle() {} } };
  const dialog = { ...element, dataset: {}, open: false, showModal, close() { this.open = false; } };
  const handlers = {};
  const accept = { ...element, addEventListener(name, handler) { handlers[name] = handler; } };
  const sent = [];
  const matchMedia = () => ({ addEventListener() {}, matches: false });
  const context = {
    document: {
      documentElement: { dataset: {}, style: { setProperty() {} }, toggleAttribute() {} },
      querySelector: selector => ({
        '[data-setting="usb_id"]': input, '#confirm-dialog': dialog, '#confirm-accept': accept
      })[selector] || element,
      querySelectorAll: () => [], getElementById: () => list, createElement: () => ({})
    },
    window: {
      WireLinkRequests: { create: () => ({ request: (action, extra) => {
        sent.push({ action, ...extra });
        return Promise.resolve();
      } }) }, WireLinkPickers: { create: () => ({ close() {} }) },
      matchMedia, addEventListener() {}
    },
    TextDecoder, matchMedia, setTimeout() {}, clearTimeout() {}, setInterval() {}
  };
  runInNewContext(theme, context);
  runInNewContext(app, context);
  return { context, input, options, dialog, handlers, sent };
}

function scan(selection, ids) {
  const { context, input, options } = fixture(selection);
  context.data('probes', ids.map(unique_id => ({ unique_id })));
  return { selected: input.value, options };
}

test('USB scan keeps the selected probe only while it is present', () => {
  assert.deepEqual(scan('A', ['B', 'A']), { selected: 'A', options: ['B', 'A'] });
  assert.deepEqual(scan('A', ['B']), { selected: '', options: ['B'] });
  assert.deepEqual(scan('', ['B']), { selected: '', options: ['B'] });
});

const prompt = { title: 'Erase', message: 'Clear flash?', confirmLabel: 'Erase', destructive: true };

test('confirmation dialog errors explicitly reject the matching host prompt', () => {
  const { context, dialog, sent } = fixture('', () => { throw new Error('dialog unavailable'); });
  context.showConfirmation({ promptId: 'failed', prompt });
  assert.deepEqual(sent, [{ action: 'confirm_result', promptId: 'failed', accepted: false }]);
  assert.equal(dialog.dataset.promptId, undefined);
});

test('host dismissal expires one prompt and leaves the next confirmation usable', () => {
  const { context, dialog, handlers, sent } = fixture('');
  context.handleMessage({ type: 'confirm', promptId: 'expired', prompt });
  context.handleMessage({ type: 'confirm_dismissed', promptId: 'expired' });
  handlers.click();
  assert.equal(dialog.open, false);
  assert.deepEqual(sent, []);
  context.handleMessage({ type: 'confirm', promptId: 'next', prompt });
  context.handleMessage({ type: 'confirm_dismissed', promptId: 'expired' });
  assert.equal(dialog.open, true);
  handlers.click();
  assert.deepEqual(sent, [{ action: 'confirm_result', promptId: 'next', accepted: true }]);
});
