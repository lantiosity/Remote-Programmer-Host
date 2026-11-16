const { test } = require('node:test');
const assert = require('node:assert/strict');
const { normalizeTargets, buildTree, familyFor } = require('../WireLink.App/wwwroot/pickers.js');

test('HC32, MM32 and MX32 split into device families instead of one vendor-wide group', () => {
  for (const [name, expected] of [['hc32f460xc','HC32F460'],['hc32l110','HC32L110'],
    ['mm32f3277','MM32F327'],['mm32l073','MM32L073'],['mx32f103','MX32F103']])
    assert.equal(familyFor({name}), expected);
});

test('STM32 variants are grouped by series without rewriting pyOCD IDs', () => {
  const data = normalizeTargets([
    { name: 'stm32f103rc', vendor: 'STMicroelectronics', part_number: 'STM32F103RC' },
    { name: 'stm32f412xe', vendor: 'STMicroelectronics', part_number: 'STM32F412xE' },
    { name: 'stm32h743xx', vendor: 'STMicroelectronics', part_number: 'STM32H743xx' }
  ]);
  const tree = buildTree(data);
  assert.deepEqual([...tree.get('STMicroelectronics').keys()], ['STM32F1', 'STM32F4', 'STM32H7']);
  assert.equal(tree.get('STMicroelectronics').get('STM32F4')[0].name, 'stm32f412xe');
});

test('DFP family metadata is used for other vendors, with useful Nordic and generic groups', () => {
  assert.equal(familyFor({ name: 'foo123', part_families: ['Foo family', 'Foo123'] }), 'Foo family');
  assert.equal(familyFor({ name: 'nrf52832', part_number: 'nRF52832' }), 'nRF52');
  assert.equal(familyFor({ name: 'cortex_m' }), '通用 Cortex-M');
});

test('vendor, series and ID can be searched together; no unavailable model is fabricated', () => {
  const data = normalizeTargets([
    { name: 'stm32f412xe', vendor: 'STMicroelectronics', part_number: 'STM32F412xE' },
    { name: 'stm32h743xx', vendor: 'STMicroelectronics', part_number: 'STM32H743xx' },
    { name: 'lpc1768', vendor: 'NXP', part_number: 'LPC1768' }
  ]);
  const tree = buildTree(data, 'stmicro f4');
  assert.equal(tree.size, 1);
  assert.equal(tree.get('STMicroelectronics').size, 1);
  assert.equal(buildTree(data, 'stm32f401').size, 0);
});

test('malformed entries are ignored and duplicate names are merged', () => {
  const data = normalizeTargets([null, {}, { name: '' }, { name: 'device', vendor: 'A' }, { name: 'device', vendor: 'B' }]);
  assert.equal(data.length, 1);
  assert.equal(data[0].vendor, 'B');
  assert.equal(normalizeTargets([{ name: 'no_vendor' }])[0].vendor, '其他');
});

test('pack targets retain their exact names, part labels and origin', () => {
  const data = normalizeTargets([{ name: 'stm32f401rctx', vendor: 'STMicroelectronics',
    part_number: 'STM32F401RCTx', part_families: ['STM32F4 Series'], source: 'pack' }]);
  assert.equal(data[0].name, 'stm32f401rctx');
  assert.equal(data[0].label, 'STM32F401RCTx');
  assert.equal(data[0].source, 'pack');
  assert.equal(buildTree(data).get('STMicroelectronics').get('STM32F4').length, 1);
});
