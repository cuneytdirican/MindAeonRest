/* ===== MindAeon Garson PWA — API layer + auth + storage ===== */
(function (global) {
  'use strict';

  const LS = {
    base: 'mag.apiBase',
    token: 'mag.token',
    session: 'mag.session',
    theme: 'mag.theme',
    creds: 'mag.lastCreds'
  };

  // Merkez (SaaS) API varsayılanı. Kullanıcı elle LAN adresi (mag.apiBase) girmediyse buna düşülür.
  // Aynı origin'den sunuluyorsa (self-host) origin tercih edilir; değilse gömülü merkez FQDN.
  const DEFAULT_BASE = (function () {
    if (location.protocol.indexOf('http') === 0 && location.origin) return location.origin;
    return 'https://mindaeonrestapi.mobilteg.com';
  })();

  const Store = {
    // Elle girilen LAN override varsa onu; yoksa merkez varsayılanı kullan.
    getBase() { return (localStorage.getItem(LS.base) || '').replace(/\/+$/, '') || DEFAULT_BASE; },
    getOverrideBase() { return localStorage.getItem(LS.base) || ''; },
    setBase(v) { localStorage.setItem(LS.base, (v || '').replace(/\/+$/, '')); },
    getToken() { return localStorage.getItem(LS.token) || ''; },
    setToken(v) { v ? localStorage.setItem(LS.token, v) : localStorage.removeItem(LS.token); },
    getSession() { try { return JSON.parse(localStorage.getItem(LS.session) || 'null'); } catch { return null; } },
    setSession(o) { o ? localStorage.setItem(LS.session, JSON.stringify(o)) : localStorage.removeItem(LS.session); },
    getTheme() { return localStorage.getItem(LS.theme) || 'okyanus'; },
    setTheme(v) { localStorage.setItem(LS.theme, v); },
    getCreds() { try { return JSON.parse(localStorage.getItem(LS.creds) || 'null'); } catch { return null; } },
    setCreds(o) { localStorage.setItem(LS.creds, JSON.stringify(o)); },
    clearAuth() { Store.setToken(null); Store.setSession(null); }
  };

  // event hub
  const listeners = {};
  function on(ev, fn) { (listeners[ev] = listeners[ev] || []).push(fn); }
  function emit(ev, data) { (listeners[ev] || []).forEach(fn => { try { fn(data); } catch (e) { console.error(e); } }); }

  class ApiError extends Error {
    constructor(message, status) { super(message); this.status = status; }
  }

  // Idempotency anahtarı üretici (çift sipariş/ödeme engeli — backend aynı key = aynı sonuç döner).
  function uuid() {
    try { if (global.crypto && crypto.randomUUID) return crypto.randomUUID(); } catch {}
    // Fallback (eski WebView'lar): RFC4122-benzeri.
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, c => {
      const r = (Math.random() * 16) | 0, v = c === 'x' ? r : (r & 0x3) | 0x8;
      return v.toString(16);
    });
  }

  const NET_TIMEOUT_MS = 12000;   // istek zaman aşımı (offline algısı için)

  // Sonuç dönmeyen ama kuyruğa alınan mutasyonlar bu işareti taşır (çağıran "kuyruğa alındı" diye anlar).
  function queuedResult(idempotencyKey) { return { __queued: true, offline: true, idempotencyKey }; }

  // Timeout'lu fetch (AbortController). Ağ hatası/timeout → throw (çağıran yakalar).
  async function fetchWithTimeout(url, init) {
    const ctl = (typeof AbortController !== 'undefined') ? new AbortController() : null;
    let timer = null;
    if (ctl) { init = Object.assign({}, init, { signal: ctl.signal }); timer = setTimeout(() => { try { ctl.abort(); } catch {} }, NET_TIMEOUT_MS); }
    try { return await fetch(url, init); }
    finally { if (timer) clearTimeout(timer); }
  }

  // Background Sync tetikle (varsa); yoksa online-event fallback devreye girer.
  function registerOutboxSync() {
    try {
      if (global.navigator && navigator.serviceWorker && navigator.serviceWorker.ready) {
        navigator.serviceWorker.ready.then(reg => {
          if (reg && reg.sync) reg.sync.register('outbox-sync').catch(() => {});
        }).catch(() => {});
      }
    } catch {}
  }

  async function request(method, path, body, opts) {
    opts = opts || {};
    const base = Store.getBase();
    if (!base) throw new ApiError('API adresi ayarlanmamış.', 0);
    const url = base + path;
    const headers = {};
    const token = Store.getToken();
    if (token && !opts.noAuth) headers['Authorization'] = 'Bearer ' + token;
    let payload;
    if (body !== undefined && body !== null) {
      headers['Content-Type'] = 'application/json';
      payload = JSON.stringify(body);
    }
    // MUTASYON isteklerine (POST/PATCH/DELETE) Idempotency-Key ekle: ağ hatasında yeniden gönderim
    // ya da çift-tık backend'de tek işleme indirgensin. Aynı istek için AYNI key kullanılır (retry/replay).
    const isMutation = method !== 'GET';
    let idemKey = opts.idempotencyKey || null;
    if (isMutation) { idemKey = idemKey || uuid(); headers['Idempotency-Key'] = idemKey; }

    let res;
    try {
      // Kısa retry: ağ blip'inde bir kez daha dene (Idempotency-Key sabit → tekrar güvenli).
      // GET için tek deneme; mutasyonda 2 deneme (idempotent olduğundan çift-işlem riski yok).
      const tries = isMutation ? 2 : 1;
      let lastErr;
      for (let i = 0; i < tries; i++) {
        try { res = await fetchWithTimeout(url, { method, headers, body: payload }); lastErr = null; break; }
        catch (e) { lastErr = e; if (i < tries - 1) await new Promise(r => setTimeout(r, 400)); }
      }
      if (lastErr) throw lastErr;
    } catch (e) {
      // Ağ hatası / timeout (offline). Kritik sipariş mutasyonu ise KUYRUĞA al, kaybolmasın.
      if (opts.queue && isMutation) {
        try {
          if (global.Outbox) {
            await Outbox.enqueue({ method, url, body: payload, headers, idempotencyKey: idemKey, tag: opts.tag || path });
            registerOutboxSync();
            return queuedResult(idemKey);
          }
        } catch (qe) { /* kuyruğa alınamadıysa aşağıda normal hata döneriz */ }
      }
      throw new ApiError('Sunucuya ulaşılamadı. Ağ/adres kontrol edin.', 0);
    }
    if (res.status === 401) {
      Store.clearAuth();
      emit('unauthorized');
      throw new ApiError('Oturum sona erdi. Tekrar giriş yapın.', 401);
    }
    if (!res.ok) {
      let msg = res.status === 403 ? 'Bu işlem için yetkiniz yok.' : 'İstek başarısız (' + res.status + ')';
      try {
        const j = await res.json();
        msg = j.message || j.title || msg;
      } catch { try { const t = await res.text(); if (t) msg = t; } catch {} }
      throw new ApiError(msg, res.status);
    }
    if (res.status === 204) return null;
    const ct = res.headers.get('content-type') || '';
    if (ct.indexOf('application/json') >= 0) return res.json();
    return res.text();
  }

  // Katalog snapshot sarmalayıcısı (FAZ 2a offline-first). ONLINE davranış AYNEN korunur:
  // önce gerçek GET denenir, başarılıysa snapshot fire-and-forget tazelenir ve sonuç OLDUĞU GİBİ döner.
  // OFFLINE (navigator.onLine === false) ise beklemeden snapshot döndürülür (12s timeout'a girmeden);
  // ağ hatasında (timeout/koptu) da snapshot'a düşülür. Snapshot yoksa gerçek hata fırlatılır.
  async function cachedGet(key, fetchFn) {
    const hasSnap = !!global.Snapshot;
    const offlineNow = !!(global.navigator) && global.navigator.onLine === false;
    if (offlineNow && hasSnap) {
      const snap = await Snapshot.get(key).catch(() => null);
      if (snap != null) return snap;
    }
    try {
      const data = await fetchFn();
      if (hasSnap) { Snapshot.set(key, data).catch(() => {}); }
      return data;
    } catch (e) {
      if (hasSnap) {
        const snap = await Snapshot.get(key).catch(() => null);
        if (snap != null) return snap;
      }
      throw e;
    }
  }

  const API = {
    get: (p, o) => request('GET', p, null, o),
    post: (p, b, o) => request('POST', p, b, o),
    patch: (p, b, o) => request('PATCH', p, b, o),
    del: (p, o) => request('DELETE', p, null, o),

    // ---- auth ----
    // Enumerasyon-güvenli tek-uç giriş. req = { companyCode, username, password, branchId? }
    // 401 → tek-tip hata; 200 { needBranch:true, branches:[...] } → şube seçtir, branchId ile tekrar çağır.
    login: (req) => API.post('/api/auth/login', req, { noAuth: true }),

    // ---- settings ----
    uiSettings: () => API.get('/api/settings/ui'),
    notePresets: () => API.get('/api/settings/note-presets'),

    // ---- tables ----  (offline-first: online çek+snapshot güncelle, offline snapshot döndür)
    zones: () => cachedGet('zones', () => API.get('/api/tables/zones')),
    board: (zoneId) => cachedGet('board:' + zoneId, () => API.get('/api/tables/board?zoneId=' + zoneId)),

    // ---- orders ----
    orderByTable: (tableId) => API.get('/api/orders/by-table/' + tableId),
    linesByOrder: (id) => API.get('/api/orders/' + id + '/lines'),
    unpaid: () => API.get('/api/orders/unpaid'),
    openOrder: (tableId, zoneId) => API.post('/api/orders/open', { tableId, zoneId }, { queue: true, tag: 'open' }),
    addLine: (id, dto) => API.post('/api/orders/' + id + '/lines', dto, { queue: true, tag: 'addLine' }),
    changeQty: (lineId, qty) => API.patch('/api/orders/lines/' + lineId + '/qty', { qty }, { queue: true, tag: 'changeQty' }),
    cancelLine: (lineId, reason, returnToStock) => API.del('/api/orders/lines/' + lineId +
      '?returnToStock=' + (returnToStock ? 'true' : 'false') + (reason ? '&reason=' + encodeURIComponent(reason) : ''),
      { queue: true, tag: 'cancelLine' }),
    lineNote: (lineId, note) => API.post('/api/orders/lines/' + lineId + '/note', { note }),
    treat: (lineId, isTreat) => API.post('/api/orders/lines/' + lineId + '/treat', { isTreat }),
    discount: (lineId, amount) => API.post('/api/orders/lines/' + lineId + '/discount', { amount }),
    send: (id) => API.post('/api/orders/' + id + '/send', null, { queue: true, tag: 'send' }),
    deleteIfEmpty: (id) => API.post('/api/orders/' + id + '/delete-if-empty'),
    cover: (id, count) => API.post('/api/orders/' + id + '/cover', { count }),
    orderNote: (id, note) => API.post('/api/orders/' + id + '/order-note', { note }),
    allergy: (id, note) => API.post('/api/orders/' + id + '/allergy', { note }),
    flags: (id, vip, priority) => API.post('/api/orders/' + id + '/flags', { vip, priority }),
    campaigns: (id) => API.get('/api/orders/' + id + '/campaigns'),
    applyCampaign: (id, cid) => API.post('/api/orders/' + id + '/campaigns/' + cid),
    removeCampaign: (id) => API.del('/api/orders/' + id + '/campaigns'),
    pay: (id, payments) => API.post('/api/orders/' + id + '/pay', { payments }),
    // opts.idempotencyKey verilirse settle DENEMESİ boyunca AYNI anahtar kullanılır (retry/kuyruk aynı key'i
    // taşır → sunucu Idempotent settle çift-tahsilatı emer). Verilmezse request() otomatik uuid üretir.
    settle: (id, body, opts) => API.post('/api/orders/' + id + '/settle', body,
      Object.assign({ queue: true, tag: 'settle' }, (opts && opts.idempotencyKey) ? { idempotencyKey: opts.idempotencyKey } : null)),
    // POS satır iadesi (ödenmiş satır, kısmi miktar) → negatif ClFiche. body: { refundQty, returnToStock, refundKind, instrumentId }
    refund: (lineId, body) => API.post('/api/orders/lines/' + lineId + '/refund', body),

    // ---- masa işlemleri (taşı / böl / birleştir / kişi-adisyon aktar) ----
    moveOrder: (id, targetTableId) => API.post('/api/orders/' + id + '/move', { targetTableId }),
    splitOrder: (id, targetTableId, lineIds) => API.post('/api/orders/' + id + '/split', { targetTableId, lineIds }),
    mergeOrder: (id, targetOrderId) => API.post('/api/orders/' + id + '/merge', { targetOrderId }),
    transferLines: (id, targetOrderId, lineIds) => API.post('/api/orders/' + id + '/transfer-lines', { targetOrderId, lineIds }),

    // ---- paket / gel-al / teslimat ----
    // type: 1=Paket, 2=GelAl. platform: 0=Telefon,1=GelAl,2=Trendyol,3=Getir,4=YemekSepeti,5=Migros,6=Diger.
    couriers: () => API.get('/api/couriers'),
    createPackage: (b) => API.post('/api/orders/package', b),
    packages: (delivered) => API.get('/api/orders/packages?delivered=' + (delivered ? 'true' : 'false')),
    // status: 0=Alindi,1=Hazirlaniyor,2=YolaCikti,3=TeslimEdildi,4=Iptal
    deliveryStatus: (id, status) => API.post('/api/orders/' + id + '/delivery-status', { status }),
    assignCourier: (id, courierId) => API.post('/api/orders/' + id + '/courier', { courierId }),
    // kapanmış (bugün) adisyonlar — iade tarayıcısında listelemek için
    closedOrders: (date) => API.get('/api/orders/closed' + (date ? ('?date=' + date) : '')),
    // yeniden yazdırma (satır / sipariş mutfak fişi)
    reprintLine: (lineId) => API.post('/api/orders/lines/' + lineId + '/reprint', {}),
    reprintOrder: (id) => API.post('/api/orders/' + id + '/reprint', {}),

    // ---- pos ----
    instruments: (kind) => API.get('/api/pos/instruments?kind=' + kind),

    // ---- cari / hediye / puan ----
    customers: (q) => API.get('/api/customers' + (q ? ('?q=' + encodeURIComponent(q)) : '')),
    giftcardByCode: (code) => API.get('/api/giftcards/by-code/' + encodeURIComponent(code)),
    loyaltyBalance: (customerId) => API.get('/api/loyalty/balance/' + customerId),

    // ---- menu ----  (offline-first: kategori+ürün meta snapshot'a alınır; görseller onerror ile emoji fallback'e düşer)
    categories: () => cachedGet('menu:categories', () => API.get('/api/menu/categories')),
    products: (priceListId) => cachedGet('menu:products' + (priceListId ? ':' + priceListId : ''),
      () => API.get('/api/menu/products' + (priceListId ? '?priceListId=' + priceListId : ''))),
    productOptions: (id) => API.get('/api/menu/products/' + id + '/options'),
    // <img> Authorization başlığı gönderemez → JWT'yi access_token query'sine ekle (sunucu bu uçlarda query'den okur).
    productImageUrl: (id) => Store.getBase() + '/api/menu/products/' + id + '/image?access_token=' + encodeURIComponent(Store.getToken()),
    categoryImageUrl: (id) => Store.getBase() + '/api/menu/categories/' + id + '/image?access_token=' + encodeURIComponent(Store.getToken()),

    // ---- kitchen ----
    tickets: (includeDone, reyonId) => API.get('/api/kitchen/tickets?reyonId=' + (reyonId || 0) + '&includeDone=' + (includeDone ? 'true' : 'false')),
    kdsStart: (lineId) => API.post('/api/kitchen/lines/' + lineId + '/start'),
    kdsReady: (lineId) => API.post('/api/kitchen/lines/' + lineId + '/ready'),
    kdsDelivered: (lineId) => API.post('/api/kitchen/lines/' + lineId + '/delivered'),

    // ---- reports (patron) ----
    dashboard: (branchId, date) => API.get('/api/reports/dashboard' +
      (branchId ? '?branchId=' + branchId : '?') + (date ? '&date=' + encodeURIComponent(date) : '')),
    tablesLive: (branchId, zoneId) => API.get('/api/reports/tables-live' +
      (branchId ? '?branchId=' + branchId : '?') + (zoneId ? '&zoneId=' + zoneId : '')),

    // ---- shifts (vardiya) — branch+kiosk JWT'den çözülür; istemci geçemez ----
    shiftCurrent: () => API.get('/api/shifts/current'),
    shiftOpen: (openingFloat) => API.post('/api/shifts/open', { openingFloat }),
    shiftClose: (id, countedCash, note) => API.post('/api/shifts/' + id + '/close', { countedCash, note }),
    shiftReport: (id) => API.get('/api/shifts/' + id + '/report'),
    shiftList: (from, to) => API.get('/api/shifts/list' +
      ((from || to) ? '?' + (from ? 'from=' + encodeURIComponent(from) : '') +
        (to ? (from ? '&' : '') + 'to=' + encodeURIComponent(to) : '') : '')),

    // ---- service requests ----
    serviceRequests: (type) => API.get('/api/service-requests' + (type != null ? '?type=' + type : '')),
    createServiceRequest: (dto) => API.post('/api/service-requests', dto),
    completeServiceRequest: (id) => API.post('/api/service-requests/' + id + '/complete'),

    // ---- offline outbox flush (Background Sync YOKSA online-event fallback bunu çağırır) ----
    // Kuyruktaki mutasyonları FIFO sırayla, saklanan header'larla (Idempotency-Key dahil) yeniden POST eder.
    // Ağ yeniden koparsa kalan kayıtlar bırakılır (bir sonraki tetiklemede tekrar denenir). Çift-gönderim
    // backend'de idempotency ile emilir. Döner: { sent, remaining }.
    async flushOutbox() {
      if (!global.Outbox) return { sent: 0, remaining: 0, failed: 0, authBlocked: false };
      const rows = await Outbox.all().catch(() => []);
      let sent = 0, failed = 0, authBlocked = false;
      for (const r of rows) {
        let res;
        try { res = await fetchWithTimeout(r.url, { method: r.method, headers: r.headers || {}, body: r.body != null ? r.body : undefined }); }
        catch (e) { break; }   // hâlâ offline / ağ hatası → kalanları koru, sonra dene
        // Yanıt sınıflandırması (kayıp önleme — Idempotency-Key sayesinde replay güvenli):
        //  401/403 → oturum/token expire. KURTARILABİLİR: kaydı SİLME, kuyrukta bırak ve flush'ı DURDUR.
        //            Yeni oturum açılınca (yeni token) tekrar denenir. Poison-pill DEĞİL, sahte başarı sayma.
        if (res.status === 401 || res.status === 403) { authBlocked = true; break; }
        //  5xx → geçici sunucu hatası: koru ve dur (sonraki tetiklemede tekrar dene).
        if (res.status >= 500) break;
        //  Gerçek 4xx (400/404/409/422 vb. — kalıcı istemci hatası) → poison-pill: sil ama BAŞARISIZ say
        //  (sessizce kaybetme; kullanıcıya ayrı bildirilir).
        if (res.status >= 400) { await Outbox.remove(r.id).catch(() => {}); failed++; continue; }
        //  2xx (ve nadir 3xx) → gerçek başarı: sil + sent++.
        await Outbox.remove(r.id).catch(() => {});
        sent++;
      }
      const remaining = await Outbox.count().catch(() => 0);
      if (sent > 0 || failed > 0 || authBlocked) emit('outbox:flushed', { sent, remaining, failed, authBlocked });
      return { sent, remaining, failed, authBlocked };
    }
  };

  // ===== Realtime (SignalR with graceful fallback) =====
  const Realtime = {
    conn: null,
    connected: false,
    joinedReyons: new Set(),   // JoinReyon ile katılınan reyon grupları — reconnect'te yeniden katılmak için
    // Hub metodu çağır (ör. JoinReyon). Bağlantı yoksa/kopuksa sessizce yutulur (fallback bozulmasın).
    async invoke(method, ...args) {
      if (method === 'JoinReyon' && args[0] != null) Realtime.joinedReyons.add(args[0]);
      const c = Realtime.conn;
      if (typeof signalR === 'undefined') return false;
      if (!c || c.state !== signalR.HubConnectionState.Connected) return false;
      try { await c.invoke(method, ...args); return true; } catch { return false; }
    },
    async start() {
      const token = Store.getToken();
      const base = Store.getBase();
      if (!token || !base) return false;
      if (typeof signalR === 'undefined') return false;
      // Idempotent: zaten bağlıysa yeni bağlantı kurma (mode switch'te sızıntı/mükerrer olay olmasın).
      if (Realtime.conn) {
        if (Realtime.conn.state === signalR.HubConnectionState.Connected) return true;
        // Bağlı değil (kapanmış/yarıda kalmış) — eskisini temizle, yeniden kur.
        try { await Realtime.conn.stop(); } catch {}
        Realtime.conn = null;
        Realtime.connected = false;
      }
      try {
        const conn = new signalR.HubConnectionBuilder()
          .withUrl(base + '/hubs/pos?access_token=' + encodeURIComponent(token), {
            transport: signalR.HttpTransportType.WebSockets | signalR.HttpTransportType.LongPolling
          })
          .withAutomaticReconnect()
          .build();
        // Tazele-olayları (NewTicket/TicketReady/TableChanged/ServiceChanged) + yeni hedefli olaylar
        // (KitchenNew → mutfak/reyon KDS ses, WaiterReady → garson ses). Hepsi emit('rt:'+ev) ile akar.
        ['NewTicket', 'TicketReady', 'TableChanged', 'ServiceChanged', 'KitchenNew', 'WaiterReady'].forEach(ev =>
          conn.on(ev, (p) => emit('rt:' + ev, p)));
        conn.onreconnected(() => {
          Realtime.connected = true;
          // Yeniden bağlanınca daha önce katıldığımız reyon gruplarına tekrar katıl (hedefli olaylar kesilmesin).
          Realtime.joinedReyons.forEach(id => { try { conn.invoke('JoinReyon', id); } catch {} });
          emit('rt:state', true);
        });
        conn.onclose(() => { Realtime.connected = false; emit('rt:state', false); });
        await conn.start();
        Realtime.conn = conn;
        Realtime.connected = true;
        emit('rt:state', true);
        return true;
      } catch (e) {
        console.warn('SignalR baglanamadi, polling kullanilacak.', e);
        Realtime.connected = false;
        return false;
      }
    },
    async stop() {
      if (Realtime.conn) { try { await Realtime.conn.stop(); } catch {} Realtime.conn = null; }
      Realtime.connected = false;
    }
  };

  global.MAG = { Store, API, ApiError, on, emit, Realtime, LS, Outbox: global.Outbox, Snapshot: global.Snapshot, registerOutboxSync, uuid };
})(window);
