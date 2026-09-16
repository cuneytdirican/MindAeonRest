/* ===== MindAeon Garson PWA — service worker =====
   ÖNEMLİ: Her release'de CACHE sürümünü (v3 -> v4 ...) MUTLAKA artırın; aksi halde
   activate eski cache'i temizlemez ve istemciler bayat shell sunabilir.
   Not: JS/CSS dosyaları artık network-first servis edilir (aşağıya bakın), bu sayede
   deploy sonrası yeni app.js/api.js anında alınır; çevrimdışıyken cache'e düşülür. */
const CACHE = 'mag-shell-v6';
const SHELL = [
  './',
  './index.html',
  './css/app.css',
  './js/outbox.js',
  './js/snapshot.js',
  './js/api.js',
  './js/app.js',
  './manifest.webmanifest',
  './icons/icon.svg',
  './icons/icon-192.png',
  './icons/icon-512.png'
];

// Offline outbox (IndexedDB) — sayfa ile AYNI store'u paylaşır. SW bağlamına import edilir.
// Background Sync 'outbox-sync' olayında kuyruk boşaltılır (idempotency backend'de çift-göndermeyi emer).
try { importScripts('js/outbox.js'); } catch (e) { /* import başarısızsa sync no-op olur */ }

// Açık istemcilere (sayfalara) outbox durumunu bildir (401 oturum / kalıcı 4xx başarısızlık).
// Sayfa bu mesajı toast'a çevirir (sahte başarı OLMASIN; kullanıcı uyarılsın).
async function notifyClients(payload) {
  try {
    const cs = await self.clients.matchAll({ includeUncontrolled: true, type: 'window' });
    cs.forEach(c => { try { c.postMessage({ type: 'outbox:flushed', ...payload }); } catch (e) {} });
  } catch (e) {}
}

// Kuyruğu FIFO sırayla, saklanan header'larla (Idempotency-Key dahil) POST et.
// Yanıt sınıflandırması (kayıp önleme):
//  - Ağ hâlâ kopuksa (fetch throw) → reject et (tarayıcı sync'i sonra tekrar dener).
//  - 401/403 (oturum/token expire, KURTARILABİLİR) → kaydı SİLME, kuyrukta bırak ve DUR. throw ETME
//    (aynı expired token ile tekrar sync anlamsız); yeni oturumda sayfa flush'ı devralır.
//  - 5xx (geçici sunucu hatası) → kalanı koru ve reject et (retry).
//  - Gerçek 4xx (400/404/409/422 — kalıcı istemci hatası) → poison-pill: sil ama başarısız say + kullanıcıya bildir.
//  - 2xx → sil.
async function flushOutbox() {
  if (!self.Outbox) return;
  const rows = await self.Outbox.all().catch(() => []);
  let authBlocked = false, failed = 0;
  for (const r of rows) {
    let res;
    try {
      res = await fetch(r.url, { method: r.method, headers: r.headers || {}, body: r.body != null ? r.body : undefined });
    } catch (e) {
      throw e;   // hâlâ offline → waitUntil reject → sync yeniden planlanır
    }
    if (res.status === 401 || res.status === 403) { authBlocked = true; break; }      // oturum yenilenmeli → BIRAK, dur
    if (res.status >= 500) { await notifyFailed(failed, authBlocked); throw new Error('server ' + res.status); }   // geçici → tekrar dene
    if (res.status >= 400) { await self.Outbox.remove(r.id).catch(() => {}); failed++; continue; }   // kalıcı 4xx poison-pill
    await self.Outbox.remove(r.id).catch(() => {});   // 2xx
  }
  if (authBlocked || failed > 0) await notifyClients({ authBlocked, failed });
}
// 5xx'te reject etmeden önce o ana kadar biriken kalıcı başarısızlıkları da bildir (kaybolmasın).
async function notifyFailed(failed, authBlocked) {
  if (failed > 0 || authBlocked) await notifyClients({ authBlocked, failed });
}

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(CACHE).then((c) => c.addAll(SHELL)).then(() => self.skipWaiting())
  );
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys().then((keys) =>
      Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k)))
    ).then(() => self.clients.claim())
  );
});

// Background Sync: bağlantı gelince outbox'ı boşalt (offline'da kuyruğa alınan sipariş/ödeme mutasyonları).
self.addEventListener('sync', (event) => {
  if (event.tag === 'outbox-sync') {
    event.waitUntil(flushOutbox());
  }
});

// Manuel tetik (Background Sync desteklenmeyen tarayıcılarda app.js online-event fallback'i buradan çağırabilir).
self.addEventListener('message', (event) => {
  if (event.data && event.data.type === 'flush-outbox') {
    event.waitUntil(flushOutbox().catch(() => {}));
  }
});

self.addEventListener('fetch', (event) => {
  const req = event.request;
  if (req.method !== 'GET') return;

  const url = new URL(req.url);

  // Never cache API or hub/auth traffic — always go to network.
  if (url.pathname.indexOf('/api/') >= 0 || url.pathname.indexOf('/hubs/') >= 0) {
    event.respondWith(fetch(req).catch(() =>
      new Response(JSON.stringify({ message: 'Çevrimdışı: sunucuya ulaşılamadı.' }),
        { status: 503, headers: { 'Content-Type': 'application/json' } })));
    return;
  }

  // Versiyonsuz JS/CSS dosyaları için network-first: deploy sonrası bayat app.js/api.js
  // sunulmasın. Ağ başarılıysa cache'i tazele; çevrimdışıysa cache'e düş.
  const isCode = /\.(?:js|css)$/i.test(url.pathname);
  if (isCode) {
    event.respondWith(
      fetch(req).then((res) => {
        if (res && res.ok && (res.type === 'basic' || res.type === 'default')) {
          const copy = res.clone();
          caches.open(CACHE).then((c) => c.put(req, copy)).catch(() => {});
        }
        return res;
      }).catch(() => caches.match(req).then((c) => c || new Response('', { status: 504 })))
    );
    return;
  }

  // App shell (html/icons/manifest): cache-first with background refresh; SPA fallback to index.html.
  event.respondWith(
    caches.match(req).then((cached) => {
      const network = fetch(req).then((res) => {
        if (res && res.ok && (res.type === 'basic' || res.type === 'default')) {
          const copy = res.clone();
          caches.open(CACHE).then((c) => c.put(req, copy)).catch(() => {});
        }
        return res;
      }).catch(() => null);
      if (cached) return cached;
      return network.then((res) => {
        if (res) return res;
        if (req.mode === 'navigate') return caches.match('./index.html');
        return new Response('', { status: 504 });
      });
    })
  );
});
