// popup 逻辑：读取当前标签页嗅探列表 → 渲染 → 单条/全部发送到 aria-gui（127.0.0.1:6866）。
const PORT = 6866;
const BASE = 'http://127.0.0.1:' + PORT;

let tabId = -1;

const $dot = document.getElementById('dot');
const $state = document.getElementById('state');
const $hint = document.getElementById('hint');
const $list = document.getElementById('list');
const $sendAll = document.getElementById('sendAll');
const $clear = document.getElementById('clear');
const $tip = document.getElementById('tip');

async function fetchTimeout(url, opts, ms) {
  const ctl = new AbortController();
  const t = setTimeout(() => ctl.abort(), ms);
  try {
    return await fetch(url, Object.assign({ cache: 'no-store', signal: ctl.signal }, opts || {}));
  } finally {
    clearTimeout(t);
  }
}

// 检测 aria-gui 是否在线
async function checkConn() {
  try {
    const r = await fetchTimeout(BASE + '/ping', null, 1500);
    return r.ok;
  } catch (e) {
    return false;
  }
}

function setConn(ok) {
  $dot.className = ok ? 'dot on' : 'dot off';
  $state.textContent = ok ? '已连接' : '未连接';
  $hint.style.display = ok ? 'none' : 'block';
}

function setTip(text, isErr) {
  $tip.textContent = text || '';
  $tip.className = isErr ? 'err' : '';
}

function refreshButtons() {
  const n = $list.querySelectorAll('.item').length;
  $sendAll.disabled = n === 0;
  $sendAll.textContent = n > 0 ? '全部发送到 Aria（' + n + '）' : '全部发送到 Aria';
}

function render(urls) {
  $list.innerHTML = '';
  if (!urls.length) {
    const d = document.createElement('div');
    d.className = 'empty';
    d.textContent = '当前页面暂未嗅探到可下载资源';
    $list.appendChild(d);
    refreshButtons();
    return;
  }
  for (const url of urls) {
    const row = document.createElement('div');
    row.className = 'item';
    row.dataset.url = url;

    const a = document.createElement('div');
    a.className = 'url';
    a.title = url;
    try { a.textContent = decodeURIComponent(url.split('/').pop() || url); } catch (e) { a.textContent = url; }
    if (!a.textContent) a.textContent = url;

    const send = document.createElement('button');
    send.className = 'send';
    send.textContent = '发送';
    send.addEventListener('click', () => sendOne(url));

    const del = document.createElement('button');
    del.className = 'del';
    del.textContent = '✕';
    del.title = '从列表移除';
    del.addEventListener('click', () => removeLocal(url));

    row.appendChild(a);
    row.appendChild(send);
    row.appendChild(del);
    $list.appendChild(row);
  }
  refreshButtons();
}

async function post(urls) {
  const r = await fetchTimeout(
    BASE + '/add',
    { method: 'POST', headers: { 'Content-Type': 'text/plain;charset=UTF-8' }, body: urls.join('\n') },
    8000
  );
  if (!r.ok) throw new Error('HTTP ' + r.status);
  return r.json();
}

async function sendOne(url) {
  setTip('');
  try {
    const j = await post([url]);
    await chrome.runtime.sendMessage({ type: 'remove', tabId, url });
    const row = $list.querySelector('.item[data-url="' + CSS.escape(url) + '"]');
    if (row) row.remove();
    refreshButtons();
    setTip('已发送到 Aria（' + (j.count || 1) + ' 条）');
  } catch (e) {
    setTip('发送失败：' + ((await checkConn()) ? 'aria-gui 未响应' : 'aria-gui 未运行'), true);
    setConn(await checkConn());
  }
}

async function sendAll() {
  const rows = Array.from($list.querySelectorAll('.item'));
  const urls = rows.map((el) => el.dataset.url);
  if (!urls.length) return;
  setTip('');
  $sendAll.disabled = true;
  try {
    const j = await post(urls);
    await chrome.runtime.sendMessage({ type: 'clear', tabId });
    render([]);
    setTip('已发送到 Aria（' + (j.count || urls.length) + ' 条）');
  } catch (e) {
    setTip('发送失败：' + ((await checkConn()) ? 'aria-gui 未响应' : 'aria-gui 未运行'), true);
    setConn(await checkConn());
    refreshButtons();
  }
}

async function removeLocal(url) {
  await chrome.runtime.sendMessage({ type: 'remove', tabId, url });
  const row = $list.querySelector('.item[data-url="' + CSS.escape(url) + '"]');
  if (row) row.remove();
  refreshButtons();
}

async function clearAll() {
  await chrome.runtime.sendMessage({ type: 'clear', tabId });
  render([]);
  setTip('');
}

async function init() {
  const tabs = await chrome.tabs.query({ active: true, currentWindow: true });
  if (tabs.length) tabId = tabs[0].id;
  const ok = await checkConn();
  setConn(ok);
  let list = [];
  if (tabId >= 0) {
    try {
      const r = await chrome.runtime.sendMessage({ type: 'get', tabId });
      list = (r && r.list) || [];
    } catch (e) {
    }
  }
  render(list);
}

$sendAll.addEventListener('click', sendAll);
$clear.addEventListener('click', clearAll);

init();
