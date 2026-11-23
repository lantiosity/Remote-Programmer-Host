'use strict';
((root) => {
  function create({ send, setTimer = setTimeout, clearTimer = clearTimeout }) {
    const pending = new Map(); let sequence = 0;
    function request(action, extra = {}) {
      const id = String(++sequence);
      const timeout = ['ota_update', 'flash', 'erase'].includes(action) ? 3600000 :
        ['browse', 'export', 'offline_import', 'offline_export', 'offline_remove', 'theme_pick_image'].includes(action) ? 600000 : action === 'wifi_scan' ? 120000 :
        action.startsWith('wifi_') || action === 'targetsBackground' || action === 'targets' || action === 'ota_info' ? 90000 : 30000;
      return new Promise((resolve, reject) => {
        const timer = setTimer(() => { pending.delete(id); reject(new Error('上位机响应超时，任务状态可能尚未更新，请检查连接。')); }, timeout);
        pending.set(id, { resolve, reject, timer });
        try { send({ id, action, ...extra }); }
        catch (error) { clearTimer(timer); pending.delete(id); reject(error); }
      });
    }
    function receive(message) {
      if (!message || typeof message.id !== 'string' || typeof message.ok !== 'boolean') return false;
      const entry = pending.get(message.id); if (!entry) return false;
      pending.delete(message.id); clearTimer(entry.timer);
      if (message.ok) entry.resolve(); else entry.reject(new Error(typeof message.error === 'string' ? message.error : '宿主请求失败'));
      return true;
    }
    function rejectAll(text) {
      for (const entry of pending.values()) { clearTimer(entry.timer); entry.reject(new Error(text)); }
      pending.clear();
    }
    return { request, receive, rejectAll, get size() { return pending.size; } };
  }
  function valid(message) {
    if (!message || typeof message !== 'object' || typeof message.type !== 'string') return false;
    switch (message.type) {
      case 'themeEditor': return true;
      case 'themeImage': return typeof message.image === 'string' && /^[0-9a-f]{32}$/.test(message.image);
      case 'response': return typeof message.id === 'string' && typeof message.ok === 'boolean';
      case 'rx': case 'tx': return typeof message.hex === 'string' && message.hex.length <= 1000000 && /^(?:[0-9a-fA-F]{2})*$/.test(message.hex);
      case 'progress': return Number.isFinite(message.value);
      case 'job': return typeof message.busy === 'boolean' && typeof message.action === 'string' &&
        (message.busy || Number.isFinite(message.elapsed) && ['success','error','cancelled'].includes(message.status));
      case 'serial': return ['opening','connected','closed'].includes(message.status) && typeof message.text === 'string';
      case 'hostState': return typeof message.busy === 'boolean' && ['opening','connected','closed'].includes(message.serial);
      case 'log': case 'phase': case 'error': return typeof message.text === 'string';
      case 'init': return message.settings && typeof message.settings === 'object' && typeof message.version === 'string';
      case 'file': return ['pack','image','ota_image'].includes(message.key) && typeof message.path === 'string';
      case 'data': {
        const value = message.value;
        if (['ports','wifi_ports'].includes(message.kind)) return Array.isArray(value) && value.every(x => typeof x === 'string');
        if (message.kind === 'targets') return Array.isArray(value) && value.every(x => x && typeof x.name === 'string');
        if (message.kind === 'probes') return Array.isArray(value) && value.every(x => x && typeof x.unique_id === 'string');
        if (message.kind === 'scan') return Array.isArray(value) && value.every(x => x && typeof x.name === 'string' && typeof x.address === 'string');
        if (message.kind === 'ota_info') return value && value.info && typeof value.info === 'object';
        if (message.kind === 'offline_library') return value && typeof value.selected === 'string' &&
          Array.isArray(value.items) && value.items.every(item => item && typeof item.name === 'string' &&
            Number.isInteger(item.size) && Number.isInteger(item.address) && typeof item.reset === 'boolean' &&
            (item.eraseEnd === null || Number.isInteger(item.eraseEnd)));
        if (message.kind === 'wifi_setup') return value && ['status','scan','save','clear'].includes(value.operation) &&
          (value.operation !== 'scan' || Array.isArray(value.networks) && value.networks.every(x => x && typeof x.ssid === 'string' && Number.isFinite(x.rssi)));
        return false;
      }
      default: return false;
    }
  }
  root.WireLinkRequests = { create, valid };
  if (typeof module !== 'undefined' && module.exports) module.exports = { create, valid };
})(typeof window !== 'undefined' ? window : globalThis);
