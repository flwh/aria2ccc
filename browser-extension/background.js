// Aria 嗅探（MV3 service worker）：
// 以观察模式监听页面请求，收集媒体/下载直链（按扩展名或 Content-Type 判定），
// 按标签页维护列表（storage.session 持久，防 worker 休眠丢失），badge 显示数量。
// popup 通过消息读取/删除/清空该列表。

const MAX_PER_TAB = 300;

// 下载器可处理的直链后缀（不含 HLS 分片 .ts，避免淹没列表）
const EXT_RE = /\.(m3u8|mpd|mp4|m4v|webm|mkv|flv|mov|avi|mp3|m4a|aac|flac|wav|ogg|opus|zip|rar|7z|tar|gz|exe|msi|iso|apk|pdf|torrent)(\?|#|$)/i;
const TYPE_RE = /^(video\/|audio\/|application\/(vnd\.apple\.mpegurl|x-mpegurl|dash\+xml))/i;

const cache = new Map(); // tabId -> Set(url)
const loaded = new Set(); // 已从 storage.session 加载过的 tabId

async function ensure(tabId) {
  let s = cache.get(tabId);
  if (loaded.has(tabId) && s) return s;
  let arr = [];
  const key = 'tab_' + tabId;
  try {
    const o = await chrome.storage.session.get(key);
    arr = o[key] || [];
  } catch (e) {
  }
  s = new Set(arr);
  cache.set(tabId, s);
  loaded.add(tabId);
  return s;
}

// 合并节流写回（onHeadersReceived 高频，避免逐条落盘）
let saveTimer = null;
function scheduleSave() {
  if (saveTimer) return;
  saveTimer = setTimeout(async () => {
    saveTimer = null;
    for (const id of loaded) {
      const s = cache.get(id);
      if (!s) continue;
      try {
        await chrome.storage.session.set({ ['tab_' + id]: Array.from(s) });
      } catch (e) {
      }
    }
  }, 600);
}

function badge(tabId, n) {
  const p1 = chrome.action.setBadgeBackgroundColor({ tabId, color: '#2563EB' });
  const p2 = chrome.action.setBadgeText({ tabId, text: n ? String(n) : '' });
  if (p1 && p1.catch) p1.catch(() => {});
  if (p2 && p2.catch) p2.catch(() => {});
}

function isMediaUrl(url, headers) {
  if (EXT_RE.test(url)) return true;
  if (!headers) return false;
  for (let i = 0; i < headers.length; i++) {
    const h = headers[i];
    if (h.name && h.name.toLowerCase() === 'content-type' && TYPE_RE.test(h.value || '')) return true;
  }
  return false;
}

chrome.webRequest.onHeadersReceived.addListener(
  (d) => {
    if (d.tabId < 0) return;
    const url = d.url;
    if (!/^https?:/i.test(url)) return;
    if (!isMediaUrl(url, d.responseHeaders)) return;
    ensure(d.tabId)
      .then((set) => {
        if (set.has(url) || set.size >= MAX_PER_TAB) return;
        set.add(url);
        badge(d.tabId, set.size);
        scheduleSave();
      })
      .catch(() => {});
  },
  { urls: ['http://*/*', 'https://*/*'] },
  ['responseHeaders']
);

chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (!msg || !msg.type) return;
  const tabId = msg.tabId;
  if (msg.type === 'get') {
    ensure(tabId)
      .then((set) => sendResponse({ list: Array.from(set) }))
      .catch(() => sendResponse({ list: [] }));
    return true; // 异步 sendResponse 需要保持通道
  }
  if (msg.type === 'remove') {
    ensure(tabId)
      .then((set) => {
        set.delete(msg.url);
        badge(tabId, set.size);
        scheduleSave();
        sendResponse({ ok: true });
      })
      .catch(() => sendResponse({ ok: false }));
    return true;
  }
  if (msg.type === 'clear') {
    ensure(tabId)
      .then((set) => {
        set.clear();
        badge(tabId, 0);
        scheduleSave();
        sendResponse({ ok: true });
      })
      .catch(() => sendResponse({ ok: false }));
    return true;
  }
});

chrome.tabs.onRemoved.addListener((tabId) => {
  cache.delete(tabId);
  loaded.delete(tabId);
  try {
    chrome.storage.session.remove('tab_' + tabId).catch(() => {});
  } catch (e) {
  }
});
