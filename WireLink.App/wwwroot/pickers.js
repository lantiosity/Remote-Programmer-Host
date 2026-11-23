'use strict';
((root) => {
  function familyFor(item) {
    const name = String(item.part_number || item.name || '').toUpperCase();
    const stm = name.match(/^STM32([A-Z]\d)/);
    if (stm) return 'STM32' + stm[1];
    const nordic = name.match(/^NRF(51|52|53|54|91|92)/);
    if (nordic) return 'nRF' + nordic[1];
    const hc = name.match(/^HC32([A-Z]\d{3})/);
    if (hc) return 'HC32' + hc[1];
    const mm = name.match(/^(MM32|MX32)([A-Z]?\d{3})/);
    if (mm) return mm[1] + mm[2];
    const declared = (Array.isArray(item.part_families) ? item.part_families : []).find(x => typeof x === 'string' && x.trim());
    if (declared) return declared.trim();
    if (item.name === 'cortex_m') return '通用 Cortex-M';
    const lpc = name.match(/^LPC(\d{2})/);
    if (lpc) return 'LPC' + lpc[1] + 'xx';
    const imx = name.match(/^(?:MIMXRT|IMXRT)(\d{2})/);
    if (imx) return 'i.MX RT' + imx[1] + 'xx';
    return name.match(/^[A-Z]+\d{0,2}/)?.[0] || '其他';
  }

  function normalizeTargets(values) {
    const unique = new Map();
    for (const item of values || []) {
      if (!item || typeof item.name !== 'string' || !item.name.trim()) continue;
      const name = item.name.trim();
      const vendor = String(item.vendor || '其他').trim();
      unique.set(name, { ...item, name, vendor, family: familyFor(item), label: String(item.part_number || name) });
    }
    return [...unique.values()].sort((a, b) => a.name.localeCompare(b.name));
  }

  function buildTree(targets, query = '') {
    const words = query.trim().toLowerCase().split(/\s+/).filter(Boolean);
    const tree = new Map();
    for (const item of targets) {
      const text = `${item.vendor} ${item.family} ${item.label} ${item.name}`.toLowerCase();
      if (!words.every(word => text.includes(word))) continue;
      if (!tree.has(item.vendor)) tree.set(item.vendor, new Map());
      const families = tree.get(item.vendor);
      if (!families.has(item.family)) families.set(item.family, []);
      families.get(item.family).push(item);
    }
    return tree;
  }

  function create({ getSettings, setSetting, loadTargets, onError }) {
    const doc = root.document;
    const byId = id => doc.getElementById(id);
    const targetInput = byId('target-input'), frequencyInput = byId('frequency-input');
    const targetMenu = byId('target-menu'), frequencyMenu = byId('frequency-menu');
    const choiceMenu = byId('choice-menu'), choiceOptions = byId('choice-options');
    const targetField = existingField(targetInput), frequencyField = existingField(frequencyInput);
    const search = byId('target-search');
    let targets = [], attempted = false, loading = false, loadPromise;
    let vendor = '', family = '', popup = null, activeField = null, choiceFilter = '', busy = false, loadError = '';
    const presets = [10, 50, 100, 250, 500, 1000, 2000, 4000, 8000, 10000, 20000, 40000, 50000];

    function existingField(input) {
      return { trigger: input, control: input.parentElement, toggle: input.nextElementSibling };
    }

    function expand(field, value) {
      field.trigger.setAttribute('aria-expanded', String(value));
      field.control.classList.toggle('open', value);
      field.toggle?.setAttribute('aria-expanded', String(value));
    }

    function close(focus = false) {
      if (!popup) return;
      const previous = activeField;
      popup.classList.add('hidden');
      expand(previous, false);
      popup = activeField = null;
      if (focus) previous.trigger.focus({ preventScroll: true });
    }

    function open(menu, field) {
      close();
      popup = menu;
      activeField = field;
      menu.classList.remove('hidden');
      expand(field, true);
    }

    function position() {
      if (!popup) return;
      const rect = activeField.control.getBoundingClientRect();
      const width = Math.min(popup === targetMenu ? 680 : Math.max(210, rect.width), root.innerWidth - 24);
      popup.style.width = width + 'px';
      popup.style.left = Math.max(8, Math.min(rect.left, root.innerWidth - width - 8)) + 'px';
      const height = popup.offsetHeight;
      const bottomSpace = root.innerHeight - rect.bottom - 14;
      const top = bottomSpace < height && rect.top > height + 8 ? rect.top - height - 7 : rect.bottom + 7;
      popup.style.top = Math.max(8, Math.min(top, root.innerHeight - height - 8)) + 'px';
    }

    function empty(container, text) {
      const message = doc.createElement('div'); message.className = 'picker-empty'; message.textContent = text; container.append(message);
    }

    function option(container, text, selected, handler, count, leaf = false) {
      const button = doc.createElement('button'); button.type = 'button'; button.className = 'picker-option' + (leaf ? ' model-option' : '');
      button.setAttribute('role', 'option'); button.setAttribute('aria-selected', String(selected));
      const label = doc.createElement('span'); label.textContent = text; button.append(label);
      if (count != null) {
        const number = doc.createElement('span'); number.className = 'option-count'; number.textContent = count; button.append(number);
        const arrow = doc.createElement('span'); arrow.className = 'option-arrow'; arrow.textContent = '›'; button.append(arrow);
      } else if (selected) {
        const mark = doc.createElement('span'); mark.className = 'option-check'; mark.textContent = '✓'; button.append(mark);
      }
      button.addEventListener('click', handler); container.append(button); return button;
    }

    function renderTargets() {
      const tree = buildTree(targets, search.value);
      const vendors = [...tree.keys()].sort((a, b) => a.localeCompare(b));
      if (!tree.has(vendor)) vendor = vendors[0] || '';
      const families = tree.get(vendor) || new Map();
      const familyNames = [...families.keys()].sort((a, b) => a.localeCompare(b, undefined, { numeric: true }));
      if (!families.has(family)) family = familyNames[0] || '';
      const items = families.get(family) || [];
      const vendorBox = byId('target-vendors'), familyBox = byId('target-families'), modelBox = byId('target-models');
      [vendorBox, familyBox, modelBox].forEach(box => box.replaceChildren());
      vendors.forEach(name => option(vendorBox, name === 'Generic' ? '通用 / Generic' : name, vendor === name, () => {
        vendor = name; family = ''; renderTargets();
      }, [...tree.get(name).values()].reduce((sum, models) => sum + models.length, 0)));
      familyNames.forEach(name => option(familyBox, name, family === name, () => { family = name; renderTargets(); }, families.get(name).length));
      items.forEach(item => {
        const button = option(modelBox, item.label, item.name === getSettings().target, () => {
          setSetting('target', item.name); close(true);
        }, null, true);
        button.title = item.name + (item.source === 'pack' ? ' · DFP 支持包' : '');
      });
      if (!vendors.length) empty(vendorBox, loading ? '正在读取芯片目录…' : '没有匹配的芯片');
      if (!familyNames.length) empty(familyBox, '选择厂商');
      if (!items.length) empty(modelBox, '未找到型号？请加载对应 DFP .pack，再刷新列表。');
      byId('target-catalog-status').textContent = loading ? '正在读取当前可用芯片…' : loadError ||
        `共 ${targets.length} 个可用型号 · 厂商 → 系列 → 型号；缺少型号时请加载 DFP .pack。`;
      targetMenu.classList.toggle('loading', loading);
      byId('target-reload').disabled = loading || busy;
      position();
    }

    function sync() {
      const current = targets.find(item => item.name === getSettings().target);
      if (current) { vendor = current.vendor; family = current.family; }
      if (popup === targetMenu) renderTargets();
      choiceFields.forEach(syncChoice);
    }

    async function reload(silent = false) {
      if (loadPromise) return loadPromise;
      attempted = true;
      loading = true; loadError = ''; renderTargets();
      loadPromise = Promise.resolve().then(() => loadTargets(silent === true)).catch(error => {
        if (silent !== true) onError(error.message); loadError = '芯片列表加载失败：' + error.message;
      }).finally(() => { loading = false; loadPromise = null; renderTargets(); });
      return loadPromise;
    }

    function updateTargets(values) {
      targets = normalizeTargets(values);
      sync(); renderTargets();
    }

    function showTargets() {
      if (busy) return;
      if (popup === targetMenu) { close(); return; }
      search.value = ''; sync();
      open(targetMenu, targetField); renderTargets(); search.focus();
      if (!attempted) reload(true);
    }

    function showFrequencies() {
      if (busy) return;
      if (popup === frequencyMenu) { close(); return; }
      const list = byId('frequency-options'); list.replaceChildren();
      presets.forEach(value => {
        const button = option(list, value >= 1000 ? value / 1000 + ' MHz' : value + ' kHz', String(value) === frequencyInput.value,
          () => { setSetting('frequency', String(value)); close(true); });
        button.setAttribute('aria-label', `${value} kHz${value >= 1000 ? ' · ' + value / 1000 + ' MHz' : ''}`);
        const raw = doc.createElement('small'); raw.textContent = value + ' kHz'; button.append(raw);
      });
      open(frequencyMenu, frequencyField); position();
      (list.querySelector('[aria-selected=true]') || list.querySelector('button'))?.focus();
    }

    function choiceEntries(field, filter) {
      const query = filter.trim().toLowerCase();
      return [...(field.list || field.source).options].filter(item =>
        item.label.toLowerCase().includes(query) || item.value.toLowerCase().includes(query));
    }

    function focusChoice(value) {
      const buttons = [...choiceOptions.querySelectorAll('button:not(:disabled)')];
      (buttons.find(button => button.dataset.value === value) ||
        buttons.find(button => button.getAttribute('aria-selected') === 'true') || buttons[0])?.focus();
    }

    function renderChoices(field, entries = choiceEntries(field, choiceFilter)) {
      const previousFocus = choiceOptions.contains(doc.activeElement) ? doc.activeElement.dataset.value : undefined;
      byId('choice-heading').textContent = field.label;
      choiceOptions.replaceChildren();
      choiceOptions.scrollTop = 0;
      entries.forEach(item => {
        const selected = field.list ? item.value === field.source.value : item.selected;
        const button = option(choiceOptions, item.label || item.value, selected, () => {
          if (field.source.disabled) return;
          const changed = field.source.value !== item.value;
          field.source.value = item.value;
          if (changed) {
            if (field.list) field.source.dispatchEvent(new root.Event('input', { bubbles: true }));
            field.source.dispatchEvent(new root.Event('change', { bubbles: true }));
          }
          close(true);
        }, null, !!field.list);
        button.disabled = item.disabled || !!item.closest('optgroup:disabled');
        button.tabIndex = -1;
        button.dataset.value = item.value;
      });
      if (!entries.length) empty(choiceOptions, field.list ? '暂无可选项，可手动输入。' : '暂无可选项。');
      position();
      if (previousFocus !== undefined) focusChoice(previousFocus);
    }

    function showChoices(field, focusOptions, filter = '', entries) {
      if (field.source.disabled) return;
      if (popup !== choiceMenu || activeField !== field) {
        open(choiceMenu, field);
        choiceFilter = filter;
        renderChoices(field, entries);
      }
      if (focusOptions) focusChoice();
    }

    function syncChoice(field) {
      if (field.list) field.toggle.disabled = field.source.disabled;
      else {
        field.trigger.disabled = field.source.disabled;
        field.caption.textContent = field.source.selectedOptions[0]?.label || '请选择';
      }
      if (popup === choiceMenu && activeField === field) {
        if (field.source.disabled) close();
        else renderChoices(field);
      }
    }

    function installChoice(source) {
      const list = source.list || null;
      const label = source.getAttribute('aria-label');
      const control = doc.createElement('span');
      control.className = 'picker-control';
      source.before(control);
      control.append(source);

      let trigger = source, toggle, caption;
      const button = doc.createElement('button');
      button.type = 'button';
      if (list) {
        source.removeAttribute('list');
        source.setAttribute('aria-autocomplete', 'list');
        toggle = button;
        toggle.className = 'picker-toggle';
        toggle.setAttribute('aria-label', '展开' + label);
        control.append(toggle);
      } else {
        source.hidden = true;
        trigger = button;
        trigger.className = 'picker-value';
        trigger.setAttribute('aria-label', label);
        caption = doc.createElement('span');
        caption.id = 'picker-value-' + (source.id || source.dataset.setting);
        trigger.setAttribute('aria-describedby', caption.id);
        trigger.append(caption);
        control.prepend(trigger);
      }
      for (const element of toggle ? [trigger, toggle] : [trigger]) {
        element.setAttribute('aria-controls', 'choice-options');
        element.setAttribute('aria-expanded', 'false');
        element.setAttribute('aria-haspopup', 'listbox');
      }
      trigger.setAttribute('role', 'combobox');
      const field = { source, list, label, control, trigger, toggle, caption };
      button.addEventListener('click', () => {
        if (popup === choiceMenu && activeField === field) close(!!list);
        else showChoices(field, true);
      });
      trigger.addEventListener('keydown', event => {
        if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp') return;
        event.preventDefault();
        showChoices(field, true);
      });
      if (list) {
        trigger.addEventListener('click', () => showChoices(field, false));
        trigger.addEventListener('input', () => {
          if (doc.activeElement !== trigger) return;
          const entries = choiceEntries(field, trigger.value);
          if (popup === choiceMenu && activeField === field) {
            if (!entries.length) { close(); return; }
            choiceFilter = trigger.value;
            renderChoices(field, entries);
          } else if (entries.length) showChoices(field, false, trigger.value, entries);
        });
      }
      source.addEventListener('change', () => syncChoice(field));
      const observer = new root.MutationObserver(() => syncChoice(field));
      observer.observe(source, { attributes: true, subtree: !list, childList: !list,
        attributeFilter: ['disabled', 'label', 'selected', 'value'] });
      if (list) observer.observe(list, { childList: true, subtree: true, attributes: true,
        attributeFilter: ['disabled', 'label', 'value'] });
      syncChoice(field);
      return field;
    }

    const choiceFields = [...doc.querySelectorAll('select, input[list]')].map(installChoice);

    byId('target-toggle').addEventListener('click', showTargets); targetInput.addEventListener('click', showTargets);
    byId('frequency-toggle').addEventListener('click', showFrequencies);
    byId('target-reload').addEventListener('click', reload);
    search.addEventListener('input', renderTargets);
    [targetInput, frequencyInput].forEach(input => input.addEventListener('keydown', event => {
      if (event.key === 'ArrowDown' || input === targetInput && (event.key === 'Enter' || event.key === ' ')) {
        event.preventDefault(); (input === targetInput ? showTargets : showFrequencies)();
      }
    }));
    doc.addEventListener('pointerdown', event => {
      if (popup && !popup.contains(event.target) && !activeField.control.contains(event.target)) close();
    });
    doc.addEventListener('keydown', event => {
      if (!popup) return;
      if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); close(true); return; }
      if (event.key === 'Tab' && popup === choiceMenu) { close(true); return; }
      const button = event.target.closest('.picker-option');
      if (!button || !popup.contains(button)) return;
      const buttons = [...button.parentElement.querySelectorAll('.picker-option:not(:disabled)')];
      const index = buttons.indexOf(button);
      if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
        event.preventDefault(); buttons[(index + (event.key === 'ArrowDown' ? 1 : -1) + buttons.length) % buttons.length]?.focus();
      } else if (event.key === 'Home' || event.key === 'End') {
        event.preventDefault(); (event.key === 'Home' ? buttons[0] : buttons.at(-1))?.focus();
      } else if (popup === targetMenu && event.key === 'ArrowRight') {
        event.preventDefault(); const column = button.closest('.target-column'); button.click();
        column.nextElementSibling?.querySelector('.picker-option')?.focus();
      } else if (popup === targetMenu && event.key === 'ArrowLeft') {
        event.preventDefault(); const column = button.closest('.target-column');
        column.previousElementSibling?.querySelector('[aria-selected=true]')?.focus();
      }
    });
    root.addEventListener('resize', position);
    doc.addEventListener('scroll', event => {
      if (!popup || popup.contains(event.target)) return;
      const control = activeField.control;
      const rect = control.getBoundingClientRect();
      const clip = control.closest('[data-scroll-area]')?.getBoundingClientRect();
      if (rect.bottom <= Math.max(0, clip?.top ?? 0) || rect.top >= Math.min(root.innerHeight, clip?.bottom ?? root.innerHeight)) close();
      else position(); // Layout/auto-scroll during catalogue loading must not dismiss the menu.
    }, true);
    return { sync, reload, updateTargets, close, preload() { if (!attempted) return reload(true); },
      setBusy(value) {
        busy = value;
        [targetInput, frequencyInput, byId('target-toggle'), byId('frequency-toggle')].forEach(input => input.disabled = value);
        byId('target-reload').disabled = value || loading;
        if (value) close();
      }
    };
  }

  root.WireLinkPickers = { normalizeTargets, buildTree, familyFor, create };
  if (typeof module !== 'undefined' && module.exports) module.exports = { normalizeTargets, buildTree, familyFor };
})(typeof window !== 'undefined' ? window : globalThis);
