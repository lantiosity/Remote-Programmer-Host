'use strict';
const $ = (selector) => document.querySelector(selector);
const $$ = (selector) => [...document.querySelectorAll(selector)];
const state = { settings: {}, busy: false, action: '', serial: 'closed', rx: 0, tx: 0, started: 0, logCount: 0 };
const requests = window.WireLinkRequests.create({ send: message => window.chrome.webview.postMessage(message) });
let checkingHost = false;
let saveTimer, toastTimer;
let decoder = new TextDecoder('utf-8');
let terminalText = '';
let offlineItems = [], offlinePending = false;
const systemTheme = window.matchMedia('(prefers-color-scheme: dark)');

function applyTheme(theme, notify = true) {
  theme = ['system', 'light', 'dark'].includes(theme) ? theme : 'system';
  const dark = theme === 'dark' || theme === 'system' && systemTheme.matches;
  document.documentElement.dataset.theme = dark ? 'dark' : 'light';
  document.documentElement.style.colorScheme = dark ? 'dark' : 'light';
  $('#theme-icon').textContent = dark ? '☾' : '☀';
  try { localStorage.setItem('wirelink-theme', theme); } catch { }
  if (notify && window.chrome?.webview) run('theme', { dark });
}
systemTheme.addEventListener('change', () => {
  if (state.settings.theme === 'system') applyTheme('system');
});
try { applyTheme(window.__wirelinkSettings?.theme || localStorage.getItem('wirelink-theme') || 'system', false); }
catch { applyTheme('system', false); }

function snapshot() {
  $$('[data-setting]').forEach(input => {
    state.settings[input.dataset.setting] = input.type === 'checkbox' ? input.checked : input.value.trim();
  });
  return { ...state.settings };
}

function request(action, extra = {}) {
  if (!window.chrome?.webview) {
    toast('请从 WireLink Windows 程序启动此界面。');
    return Promise.reject(new Error('WebView2 host unavailable'));
  }
  return requests.request(action, { settings: snapshot(), ...extra });
}

function run(action, extra) { return request(action, extra).catch(error => toast(error.message)); }
function toast(text) {
  const element = $('#toast');
  element.textContent = text;
  element.classList.remove('hidden');
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => element.classList.add('hidden'), 6500);
}

function populate(settings) {
  state.settings = { ...settings };
  $$('[data-setting]').forEach(input => {
    if (!(input.dataset.setting in settings)) return;
    if (input.type === 'checkbox') input.checked = !!settings[input.dataset.setting];
    else input.value = settings[input.dataset.setting] ?? '';
  });
  showTransport(settings.transport || 'USB');
  applyTheme(settings.theme || 'system');
  showFilename();
  pickers.sync();
}

function setSetting(key, value) {
  state.settings[key] = value;
  const input = $(`[data-setting="${key}"]`);
  if (input) { if (input.type === 'checkbox') input.checked = value; else input.value = value; }
  if (key === 'transport') showTransport(value);
  if (key === 'image') showFilename();
  if (key === 'target') pickers.sync();
  saveSoon();
}

function saveSoon() { clearTimeout(saveTimer); saveTimer = setTimeout(() => run('save'), 350); }

function showTransport(transport) {
  state.settings.transport = transport;
  $$('[data-transport]').forEach(button => button.classList.toggle('selected', button.dataset.transport === transport));
  $$('[data-link]').forEach(panel => panel.classList.toggle('hidden', panel.dataset.link !== transport));
}

function showFilename() {
  const path = $('[data-setting="image"]').value;
  $('#firmware-name').textContent = path ? path.split(/[\\/]/).pop() : '选择要烧录的目标固件';
  $('#firmware-description').textContent = path ? '已选择固件 · 烧录前请核对芯片与起始地址' : 'BIN 使用起始地址，HEX / ELF 使用文件内地址';
}

function lockControls() {
  if (state.hostLost) {
    $$('button,input,select').forEach(element => element.disabled = true);
    $('#phase').textContent = '宿主未响应，任务状态未知，正在等待恢复连接…';
    return;
  }
  $$('#page-wifi input, #page-wifi select, #wifi-refresh-ports').forEach(input => input.disabled = state.busy);
  $('#wifi-use-ip').disabled = state.busy || !$('#wifi-use-ip').dataset.address;
  pickers.setBusy(state.busy && state.action !== 'targets');
  $$('[data-action]').forEach(button => {
    const conflict = state.serial !== 'closed' && (button.dataset.action.startsWith('ota_') || state.settings.transport === 'BLE');
    button.disabled = state.busy || conflict;
  });
  $$('[data-transport], .link-settings input').forEach(element => element.disabled = state.busy || state.serial !== 'closed');
  $$('[data-browse]').forEach(button => button.disabled = state.busy);
  $('#cancel').disabled = !state.busy;
  $('#serial-toggle').disabled = state.busy || state.serial === 'opening';
  $('#serial-toggle').textContent = state.serial === 'connected' ? '关闭连接' : state.serial === 'opening' ? '正在连接…' : '打开连接';
  $('#send').disabled = state.serial !== 'connected';
  lockOfflineControls();
}

function log(text) {
  const box = $('#logs');
  const bottom = box.scrollHeight - box.scrollTop - box.clientHeight < 30;
  const line = document.createElement('span');
  line.className = 'log-line';
  if (/错误|失败|Error|Exception|Traceback/i.test(text)) line.classList.add('error');
  line.textContent = text;
  box.append(line);
  while (box.childElementCount > 2500) box.firstElementChild.remove();
  state.logCount++;
  $('#log-count').textContent = String(state.logCount);
  if (bottom) box.scrollTop = box.scrollHeight;
}

function catalog(id, values) {
  const list = document.getElementById(id);
  list.replaceChildren();
  values.forEach(value => {
    const option = document.createElement('option');
    option.value = value;
    list.append(option);
  });
}

function data(kind, value) {
  if (kind === 'offline_library') {
    const select = $('#offline-images');
    const selected = value.selected || select.value;
    offlineItems = value.items;
    select.replaceChildren(...offlineItems.map(item => new Option(item.name, item.name)));
    if (!offlineItems.length) select.add(new Option('暂无镜像', ''));
    if (offlineItems.some(item => item.name === selected)) select.value = selected;
    showOfflineImage();
    $('#offline-result').textContent = value.notice ?? '';
  } else if (kind === 'wifi_ports') {
    catalog('wifi-ports', value);
    toast(value.length ? '请选择烧录器的 CH343 控制台串口。' : '没有发现串口，请连接 CH343 控制台 USB。');
  } else if (kind === 'wifi_setup') {
    if (value.operation === 'status') {
      $('#wifi-current-ssid').textContent = value.ssid || '未配置';
      $('#wifi-connected').textContent = value.connected ? '已连接' : '未连接';
      $('#wifi-current-ip').textContent = value.connected ? value.ip : '热点 ' + value.ap_ip;
      $('#wifi-use-ip').disabled = !value.connected;
      $('#wifi-use-ip').dataset.address = value.connected ? value.ip : '';
      if (!$('#wifi-ssid').value) $('#wifi-ssid').value = value.ssid || '';
      $('#wifi-result').textContent = value.connected ? '烧录器已联网，可将设备地址用于 WiFi 连接。' : '尚未接入路由器。保存配置并重启后，请再次读取状态。';
    } else if (value.operation === 'scan') {
      const list = $('#wifi-networks'); list.replaceChildren();
      const networks = [], seen = new Set();
      [...value.networks].sort((a,b) => b.rssi-a.rssi).filter(n => n.ssid).forEach(network => {
        if (!seen.has(network.ssid_hex)) { seen.add(network.ssid_hex); networks.push(network); }
      });
      networks.forEach(network => {
        const button = document.createElement('button'); button.className = 'wifi-network'; button.type = 'button';
        const name = document.createElement('span'); name.textContent = network.ssid;
        const detail = document.createElement('small'); detail.textContent = `${network.auth ? '加密' : '开放'} · ${network.rssi} dBm`;
        button.append(name,detail);
        button.addEventListener('click', () => {
          if ($('#wifi-ssid').value !== network.ssid) $('#wifi-password').value = '';
          $('#wifi-ssid').value = network.ssid;
          $$('.wifi-network').forEach(item => item.classList.toggle('selected',item===button));
        }); list.append(button);
      });
      if (!networks.length) { const message=document.createElement('p');message.className='hint';message.textContent='未发现可见网络，可重试或手动填写隐藏网络名称。';list.append(message); }
      $('#wifi-result').textContent = `发现 ${networks.length} 个网络，选择后填写密码。支持手动输入隐藏网络名称。`;
    } else {
      $('#wifi-password').value = '';
      $('#wifi-use-ip').dataset.address = '';
      $('#wifi-connected').textContent = '等待重启'; $('#wifi-use-ip').disabled = true;
      $('#wifi-result').textContent = value.operation === 'clear' ? '已清除配置，烧录器正在重启。' : 'WiFi 已保存，烧录器正在重启；稍后点击读取状态确认联网结果。';
    }
  } else if (kind === 'probes') {
    const ids = value.map(item => item.unique_id);
    catalog('probes', ids);
    const selected = $('[data-setting="usb_id"]').value.trim();
    if (selected && !ids.includes(selected)) {
      setSetting('usb_id', '');
      toast(ids.length ? '原 USB 探针已不在扫描结果中，请重新选择。' : '原 USB 探针未找到，请重新连接后扫描。');
    }
    log(ids.length ? `USB 探针：${ids.join(', ')}` : '没有发现 USB 探针，请检查原生 USB 口和数据线。');
  } else if (kind === 'targets') {
    pickers.updateTargets(value);
  } else if (kind === 'ports') {
    catalog('ports', value);
    if (!value.includes(state.settings.com_port)) setSetting('com_port', value[0] || '');
    log('串口：' + (value.join(', ') || '未发现'));
  } else if (kind === 'scan') {
    if (!value.length) { toast('没有发现蓝牙设备，请检查电源及电脑蓝牙。'); return; }
    const list = $('#ble-devices');
    list.replaceChildren();
    value.forEach(device => {
      const button = document.createElement('button');
      button.className = 'ble-device';
      const copy = document.createElement('div');
      const name = document.createElement('strong'); name.textContent = device.name || device.address;
      const address = document.createElement('small'); address.textContent = device.address;
      const signal = document.createElement('span'); signal.textContent = `${device.rssi} dBm`;
      copy.append(name, address); button.append(copy, signal);
      button.addEventListener('click', () => {
        setSetting('ble_name', device.name); setSetting('ble_address', device.address); setSetting('transport', 'BLE');
        $('#ble-picker').close(); lockControls();
      });
      list.append(button);
    });
    $('#ble-picker').showModal();
  } else if (kind === 'ota_info') {
    const info = value.info;
    $('#ota-device').textContent = info.serial || '—';
    $('#ota-version').textContent = info.version || '—';
    $('#ota-slot').textContent = info.running || '—';
    $('#ota-image-state').textContent = info.image_state === 2 ? 'VALID · 已确认' : String(info.image_state ?? '—');
    $('#ota-window').textContent = value.status ? (value.status.flags & 1 ? '存在授权窗口 · 无线链路请长按 BOOT 确认' : '升级窗口已关闭') : '新固件启动已确认';
    if (state.settings.transport !== 'WiFi' && Number.isInteger(info.ota_port) && info.ota_port >= 1 && info.ota_port <= 65535)
      setSetting('ota_port', String(info.ota_port));
    if (info.serial && !state.settings.ota_serial) setSetting('ota_serial', info.serial);
    log(JSON.stringify(value, null, 2));
  }
}

function onMessage(message) {
  if (!window.WireLinkRequests.valid(message)) { log('已忽略格式无效的宿主消息。'); return; }
  try { handleMessage(message); } catch { toast('收到无效设备数据，已忽略；可重试读取。'); }
}
function handleMessage(message) {
  switch (message.type) {
    case 'init': populate(message.settings); lockControls();
      $$('[data-app-version]').forEach(element => element.textContent = message.version);
      if (!message.engineReady) { $('#footer-hint').textContent = 'pyOCD 尚未安装：请运行安装上位机依赖.cmd'; toast('缺少 pyOCD 引擎。串口和 BLE OTA 可独立使用。'); }
      presentWhenPainted();
      break;
    case 'response': requests.receive(message); break;
    case 'hostState': {
      if (state.hostLost) {
        state.hostLost = false;
        $$('button,input,select').forEach(element => element.disabled = false);
        state.busy = message.busy; state.serial = message.serial; state.action = message.action || '';
        $('#phase').textContent = state.busy ? '宿主连接已恢复，任务仍在执行' : '宿主连接已恢复';
        lockControls();
      }
      break;
    }
    case 'error': toast(message.text); break;
    case 'file':
      setSetting(message.key, message.path);
      if (message.key === 'pack' && message.path.startsWith('data/dfp/')) toast('DFP 已保存在 data/dfp。可点击「加载芯片列表」查看型号。');
      break;
    case 'log': log(message.text); break;
    case 'data': data(message.kind, message.value); break;
    case 'phase': $('#phase').textContent = message.text; break;
    case 'progress':
      if (!state.busy) break;
      $('#progress').classList.remove('indeterminate');
      $('#progress').style.width = Math.max(0, Math.min(100, message.value)) + '%';
      break;
    case 'job': {
      state.busy = message.busy;
      state.action = message.action;
      $('#task-dot').className = 'task-dot ' + (state.busy ? 'busy' : message.status);
      if (state.busy) {
        state.started = Date.now();
        $('#status').className = 'status-pill busy'; $('#status span').textContent = '任务进行中';
        $('#progress').style.width = ''; $('#progress').classList.add('indeterminate');
      } else {
        const success = message.status === 'success';
        const label = success ? '任务完成' : message.status === 'cancelled' ? '任务已取消' : '任务失败，请查看日志';
        $('#status').className = 'status-pill ' + (message.status === 'error' ? 'error' : '');
        $('#status span').textContent = label.split('，')[0];
        $('#phase').textContent = label;
        $('#progress').classList.remove('indeterminate'); $('#progress').style.width = success ? '100%' : '0';
        $('#elapsed').textContent = message.elapsed.toFixed(1) + ' s';
      }
      lockControls(); break;
    }
    case 'serial':
      state.serial = message.status;
      $('#serial-state').textContent = message.text;
      if (message.status === 'connected') decoder = new TextDecoder('utf-8');
      lockControls(); break;
    case 'rx': {
      const bytes = Uint8Array.from(message.hex.match(/.{1,2}/g) || [], value => parseInt(value, 16));
      state.rx += bytes.length;
      const decoded = decoder.decode(bytes, { stream: true });
      terminalText += state.settings.hex_receive ? (message.hex.match(/.{1,2}/g) || []).join(' ') + ' ' : decoded;
      if (terminalText.length > 250000) terminalText = terminalText.slice(-200000);
      const terminal = $('#terminal');
      const atBottom = terminal.scrollHeight - terminal.scrollTop - terminal.clientHeight < 30;
      terminal.textContent = terminalText;
      if (atBottom) terminal.scrollTop = terminal.scrollHeight;
      counts(); break;
    }
    case 'tx': state.tx += message.hex.length / 2; counts(); break;
  }
}

let presentationRequested = false;
function presentWhenPainted() {
  if (presentationRequested) return;
  presentationRequested = true;
  let reported = false;
  function report(source) {
    if (reported) return;
    reported = true;
    run('painted', { source });
    setTimeout(() => pickers.preload(), 800);
  }
  requestAnimationFrame(() => requestAnimationFrame(() => report('animation-frame')));
  // A minimized/occluded WebView may throttle animation frames; don't trap startup.
  setTimeout(() => report('occluded-fallback'), 300);
}

function counts() { $('#byte-count').textContent = `RX ${state.rx.toLocaleString()} B / TX ${state.tx.toLocaleString()} B`; }

function offlineImage() { return offlineItems.find(item => item.name === $('#offline-images').value); }
function hexAddress(value) { return '0x' + value.toString(16).toUpperCase().padStart(8, '0'); }

function lockOfflineControls() {
  const locked = offlinePending || state.hostLost;
  const hasImage = !!offlineImage();
  $('#offline-images').disabled = locked || !hasImage;
  $('#offline-address').disabled = locked || !hasImage;
  $('#offline-reset').disabled = locked || !hasImage;
  $$('[data-offline]').forEach(button => {
    button.disabled = locked || (!hasImage && !['offline_import', 'offline_list'].includes(button.dataset.offline));
  });
}

function showOfflineImage() {
  const image = offlineImage();
  $('#offline-address').value = hexAddress(image?.address ?? 0x08000000);
  $('#offline-reset').checked = image?.reset ?? false;
  $('#offline-image-detail').textContent = image ? `${image.size.toLocaleString()} 字节 · data/offline` : '导入的镜像保存在 data/offline。';
  $('#offline-range').textContent = image?.eraseEnd == null ? '保存面板参数后可查看擦除范围。'
    : `预计擦除 ${hexAddress(image.address)}–${hexAddress(image.eraseEnd)}，包含镜像末尾所在的整个扇区。`;
  lockOfflineControls();
}

async function manageOffline(action) {
  if (offlinePending) return;
  offlinePending = true;
  lockOfflineControls();
  $('#offline-result').textContent = '';
  try {
    await request(action, { name: $('#offline-images').value, address: $('#offline-address').value,
      reset: $('#offline-reset').checked });
  } catch (error) {
    $('#offline-result').textContent = error.message;
  } finally {
    offlinePending = false;
    lockOfflineControls();
  }
}

$('#offline-images').addEventListener('change', showOfflineImage);
$('#offline-address').addEventListener('input', () => {
  $('#offline-range').textContent = '起始地址已编辑。保存面板参数可重新计算擦除范围。';
});
$$('[data-offline]').forEach(button => button.addEventListener('click', () => manageOffline(button.dataset.offline)));

$$('[data-page]').forEach(button => button.addEventListener('click', () => {
  pickers.close();
  $$('[data-page]').forEach(item => item.classList.toggle('active', item === button));
  $$('.page').forEach(page => page.classList.toggle('hidden', page.id !== 'page-' + button.dataset.page));
  $('#breadcrumb').textContent = { flash: '目标芯片烧录', serial: '串口监视器', ota: '烧录器固件升级', wifi: 'WiFi 配置', offline: '离线烧录' }[button.dataset.page];
  if (button.dataset.page === 'offline') manageOffline('offline_list');
}));
$$('[data-transport]').forEach(button => button.addEventListener('click', () => { showTransport(button.dataset.transport); saveSoon(); lockControls(); }));
$$('[data-setting]').forEach(input => input.addEventListener('change', () => {
  if (input.dataset.setting === 'pack') {
    clearTimeout(saveTimer);
    run('pack_import');
    return;
  }
  snapshot(); showFilename();
  if (input.dataset.setting === 'theme') applyTheme(input.value);
  saveSoon();
}));
$$('[data-action]').forEach(button => button.addEventListener('click', () => {
  if (button.dataset.action === 'targets') pickers.reload();
  else if (button.dataset.action.startsWith('wifi_')) run(button.dataset.action, { wifiLink: $('#wifi-link').value,
    ssid: $('#wifi-ssid').value, password: button.dataset.action === 'wifi_save' ? $('#wifi-password').value : '' });
  else run(button.dataset.action);
}));
$('#wifi-link').addEventListener('change', () => {
  const usb = $('#wifi-link').value === 'UART';
  $('#wifi-console').classList.toggle('hidden', !usb);
  $('#wifi-link-hint').textContent = usb ? '连接烧录器 CH343 控制台 USB，选择对应串口；目标 UART 和原生 CDC 串口不可用于配网。' : $('#wifi-link').value === 'USB' ? '连接烧录器原生 USB 接口，使用左侧 USB 探针序列号定位设备，无需选择串口。' : '使用左侧 BLE 设备名称和蓝牙地址。其他调试、升级或串口任务需先停止。';
});
$('#wifi-refresh-ports').addEventListener('click', () => run('wifi_ports'));
$('#wifi-show-password').addEventListener('change', event => $('#wifi-password').type = event.target.checked ? 'text' : 'password');
$('#wifi-use-ip').addEventListener('click', event => {
  const address = event.currentTarget.dataset.address;
  if (address) { setSetting('host', address); setSetting('transport', 'WiFi'); toast('已将设备 IP 用于 WiFi 连接。'); }
});
$$('[data-browse]').forEach(button => button.addEventListener('click', () => run('browse', { key: button.dataset.browse })));
$('#cancel').addEventListener('click', () => { $('#phase').textContent = '正在取消任务并释放连接…'; run('cancel'); });
$('#serial-toggle').addEventListener('click', () => run(state.serial === 'closed' ? 'serialOpen' : 'serialClose'));
$('#send').addEventListener('click', () => run('serialSend', { text: $('#send-text').value }));
$('#send-text').addEventListener('keydown', event => { if (event.key === 'Enter' && state.serial === 'connected') run('serialSend', { text: event.target.value }); });
$('#clear-terminal').addEventListener('click', () => { terminalText = ''; $('#terminal').textContent = ''; });
$('#clear-log').addEventListener('click', () => { $('#logs').replaceChildren(); state.logCount = 0; $('#log-count').textContent = '0'; run('clearLog'); });
$('#export').addEventListener('click', () => run('export'));
$('#close-picker').addEventListener('click', () => $('#ble-picker').close());
setInterval(() => { if (state.busy) $('#elapsed').textContent = ((Date.now() - state.started) / 1000).toFixed(1) + ' s'; }, 100);
const pickers = window.WireLinkPickers.create({ getSettings: () => state.settings, setSetting,
  loadTargets: silent => request(silent ? 'targetsBackground' : 'targets'), onError: toast });
if (window.chrome?.webview) {
  window.chrome.webview.addEventListener('message', event => onMessage(event.data)); run('ready');
  setInterval(async () => {
    if (checkingHost || !state.busy && state.serial !== 'opening' && !state.hostLost) return;
    checkingHost = true;
    try { await request('ping'); }
    catch {
      requests.rejectAll('宿主连接中断，任务状态未知。');
      state.hostLost = true; lockControls();
    } finally { checkingHost = false; }
  }, 5000);
  window.addEventListener('pagehide', () => requests.rejectAll('页面已关闭'));
}
else toast('请从 WireLink Windows 程序启动此界面。');
