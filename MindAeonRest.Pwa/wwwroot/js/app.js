/* ===== MindAeon Garson PWA — router + screens ===== */
(function () {
  'use strict';
  const { Store, API, ApiError, on, Realtime, uuid } = window.MAG;
  const $app = document.getElementById('app');

  // ---------- helpers ----------
  const THEMES = ['okyanus', 'koyu', 'acik'];
  const THEME_LABELS = { okyanus: 'Okyanus', koyu: 'Koyu', acik: 'Açık' };
  function applyTheme(t) {
    document.documentElement.setAttribute('data-theme', t);
    Store.setTheme(t);
    const meta = document.querySelector('meta[name=theme-color]');
    if (meta) meta.setAttribute('content', t === 'acik' ? '#0e9c6c' : '#10b981');
  }
  function cycleTheme() {
    const cur = Store.getTheme();
    const next = THEMES[(THEMES.indexOf(cur) + 1) % THEMES.length];
    applyTheme(next);
    toast('Tema: ' + THEME_LABELS[next], 'ok');
    if (App.rerenderShell) App.rerenderShell();
  }
  applyTheme(Store.getTheme());

  function el(html) { const t = document.createElement('template'); t.innerHTML = html.trim(); return t.content.firstElementChild; }
  function esc(s) { return (s == null ? '' : String(s)).replace(/[&<>"']/g, c =>
    ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }
  function money(n) { const v = Number(n || 0); return '₺' + v.toLocaleString('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 }); }
  function num(n) { return Number(n || 0); }

  function toast(msg, kind) {
    const host = document.getElementById('toast-host');
    const t = el('<div class="toast ' + (kind || '') + '">' + esc(msg) + '</div>');
    host.appendChild(t);
    setTimeout(() => { t.style.opacity = '0'; t.style.transition = 'opacity .3s'; setTimeout(() => t.remove(), 300); }, 2600);
  }
  function errToast(e) { toast((e && e.message) ? e.message : 'Bir hata oluştu', 'err'); }

  // Offline kuyruk: api.js kritik mutasyonu (send/settle/addLine/open...) ağ yokken outbox'a alınca
  // throw yerine { __queued:true } döner. Çağıran bunu kullanıcıya bildirir (Faz 2 offline-first UI'a temel).
  function isQueued(r) { return !!(r && r.__queued); }
  function queuedToast() { toast('Çevrimdışı: işlem kuyruğa alındı, bağlantı gelince gönderilecek.', 'ok'); updateConnBadge(); }

  // ===== ÇEVRİMDIŞI ROZETİ + KUYRUK DURUMU (FAZ 2a offline-first) =====
  // Köşede sabit bir pil: navigator.onLine + online/offline event'ine göre "Çevrimdışı" gösterir ve
  // Outbox.count() ile bekleyen kuyruk sayısını yansıtır. Online + kuyruk boşken gizlenir (normal akışı kirletmez).
  let _connBadgeEl = null;
  function isOnline() { return (typeof navigator === 'undefined') ? true : navigator.onLine !== false; }
  async function outboxCount() {
    try { if (window.Outbox) return await Outbox.count(); } catch {}
    return 0;
  }
  async function updateConnBadge() {
    if (!_connBadgeEl) return;
    const online = isOnline();
    const n = await outboxCount();
    const show = !online || n > 0;
    _connBadgeEl.style.display = show ? 'inline-flex' : 'none';
    if (!show) return;
    let html;
    if (!online) {
      html = '<span class="cb-dot"></span><span>Çevrimdışı</span>' +
        (n > 0 ? '<span class="cb-n">' + (n > 99 ? '99+' : n) + ' bekliyor</span>' : '');
    } else {
      // online ama kuyrukta iş var → gönderiliyor
      html = '<span class="cb-dot sync"></span><span>Senkronize…</span><span class="cb-n">' + (n > 99 ? '99+' : n) + '</span>';
    }
    _connBadgeEl.className = 'conn-badge' + (online ? ' syncing' : ' offline');
    _connBadgeEl.innerHTML = html;
  }
  function initConnBadge() {
    if (_connBadgeEl) return;
    _connBadgeEl = el('<div class="conn-badge offline" id="conn-badge" style="display:none"></div>');
    document.body.appendChild(_connBadgeEl);
    window.addEventListener('online', updateConnBadge);
    window.addEventListener('offline', updateConnBadge);
    // Background Sync (SW) kuyruğu arka planda boşaltınca sayfa 'outbox:flushed' almayabilir; hafif periyodik tazele.
    setInterval(() => { updateConnBadge(); }, 12000);
    updateConnBadge();
  }

  function modal(title, bodyEl, opts) {
    opts = opts || {};
    const host = document.getElementById('modal-host');
    const overlay = el('<div class="modal-overlay"></div>');
    const m = el('<div class="modal"><div class="modal-head"><h3>' + esc(title) +
      '</h3><button class="modal-x">&times;</button></div><div class="modal-body"></div></div>');
    m.querySelector('.modal-body').appendChild(bodyEl);
    overlay.appendChild(m);
    function close() { overlay.remove(); if (opts.onClose) opts.onClose(); }
    overlay.addEventListener('click', e => { if (e.target === overlay) close(); });
    m.querySelector('.modal-x').addEventListener('click', close);
    host.appendChild(overlay);
    return { close, el: m };
  }

  // ===== CUSTOM POPUP'LAR — web'de native alert/confirm/prompt KULLANILMAZ =====
  function uiConfirm(message, opts) {
    opts = opts || {};
    return new Promise(resolve => {
      const body = el('<div class="ui-dlg"><p class="ui-dlg-msg">' + esc(message) + '</p>' +
        '<div class="ui-dlg-actions">' +
        '<button class="btn btn-ghost" data-no>' + esc(opts.cancelText || 'Vazgeç') + '</button>' +
        '<button class="btn ' + (opts.danger ? 'btn-danger' : 'btn-primary') + '" data-yes>' + esc(opts.okText || 'Evet') + '</button>' +
        '</div></div>');
      let done = false;
      const m = modal(opts.title || 'Onay', body, { onClose: () => { if (!done) { done = true; resolve(false); } } });
      body.querySelector('[data-yes]').addEventListener('click', () => { done = true; m.close(); resolve(true); });
      body.querySelector('[data-no]').addEventListener('click', () => { done = true; m.close(); resolve(false); });
    });
  }
  function uiPrompt(message, opts) {
    opts = opts || {};
    return new Promise(resolve => {
      const body = el('<div class="ui-dlg"><p class="ui-dlg-msg">' + esc(message) + '</p>' +
        '<input class="field ui-dlg-inp" type="' + (opts.type || 'text') + '"' +
        (opts.inputmode ? ' inputmode="' + opts.inputmode + '"' : '') +
        ' placeholder="' + esc(opts.placeholder || '') + '" value="' + esc(opts.value || '') + '" />' +
        '<div class="ui-dlg-actions">' +
        '<button class="btn btn-ghost" data-no>Vazgeç</button>' +
        '<button class="btn btn-primary" data-yes>' + esc(opts.okText || 'Tamam') + '</button>' +
        '</div></div>');
      const inp = body.querySelector('.ui-dlg-inp');
      let done = false;
      const m = modal(opts.title || 'Giriş', body, { onClose: () => { if (!done) { done = true; resolve(null); } } });
      function ok() { done = true; const v = inp.value; m.close(); resolve(v); }
      body.querySelector('[data-yes]').addEventListener('click', ok);
      body.querySelector('[data-no]').addEventListener('click', () => { done = true; m.close(); resolve(null); });
      inp.addEventListener('keydown', e => { if (e.key === 'Enter') { e.preventDefault(); ok(); } });
      setTimeout(() => inp.focus(), 30);
    });
  }
  // İptal/işlem sebebi: hazır sebeplerden SEÇ ya da serbest YAZ. resolve(null)=vazgeç, resolve(string)=onay (boş olabilir).
  const REASON_PRESETS = ['Müşteri vazgeçti', 'Yanlış ürün', 'Stok yok', 'Mutfak hatası', 'Fiyat/ikram düzeltme', 'Servis gecikmesi'];
  function uiReason(opts) {
    opts = opts || {};
    return new Promise(resolve => {
      const chips = REASON_PRESETS.map(r => '<button class="ui-chip" data-r="' + esc(r) + '">' + esc(r) + '</button>').join('');
      const body = el('<div class="ui-dlg">' +
        (opts.message ? '<p class="ui-dlg-msg">' + esc(opts.message) + '</p>' : '') +
        '<div class="ui-chips">' + chips + '</div>' +
        '<input class="field ui-dlg-inp" placeholder="' + esc(opts.placeholder || 'Sebep (yazın ya da yukarıdan seçin)') + '" value="" />' +
        '<div class="ui-dlg-actions">' +
        '<button class="btn btn-ghost" data-no>Vazgeç</button>' +
        '<button class="btn btn-danger" data-yes>' + esc(opts.okText || 'Onayla') + '</button>' +
        '</div></div>');
      const inp = body.querySelector('.ui-dlg-inp');
      let done = false;
      const m = modal(opts.title || 'İptal Sebebi', body, { onClose: () => { if (!done) { done = true; resolve(null); } } });
      body.querySelectorAll('[data-r]').forEach(b => b.addEventListener('click', () => { inp.value = b.getAttribute('data-r'); inp.focus(); }));
      body.querySelector('[data-yes]').addEventListener('click', () => { done = true; const v = (inp.value || '').trim(); m.close(); resolve(v); });
      body.querySelector('[data-no]').addEventListener('click', () => { done = true; m.close(); resolve(null); });
      setTimeout(() => inp.focus(), 30);
    });
  }

  function loading() { return el('<div class="spin"></div>'); }
  function emptyState(icon, msg) { return el('<div class="empty"><span class="ico">' + icon + '</span>' + esc(msg) + '</div>'); }

  async function busy(btn, fn) {
    if (btn) btn.disabled = true;
    try { return await fn(); }
    catch (e) { if (e instanceof ApiError && e.status === 401) return; errToast(e); throw e; }
    // btn.dataset.locked set edilmişse KALICI devre dışı bırak (ör. çevrimdışı settle kuyruğa alındı → çift-submit engeli).
    finally { if (btn && !btn.dataset.locked) btn.disabled = false; }
  }

  // payment kinds — 12 yöntem tam paritesi (/settle ile).
  // Tüm yöntemler /settle uçundan geçer: kart-türevleri + Yemek + Havale enstrüman seçer,
  // Cari/Puan overrideCustomerId taşır, Hediye Ç. kart id'sini instrumentId olarak gönderir.
  const PAY_KINDS = [
    { k: 0, n: 'Nakit', i: '💵' }, { k: 1, n: 'Kredi K.', i: '💳' }, { k: 2, n: 'Yemek Ç.', i: '🍽️' },
    { k: 3, n: 'Cari', i: '🧾' },
    { k: 4, n: 'Banka K.', i: '🏧' }, { k: 5, n: 'Temassız', i: '📶' },
    { k: 6, n: 'QR Ödeme', i: '🔳' }, { k: 7, n: 'Mobil', i: '📱' }, { k: 8, n: 'Havale', i: '🏦' },
    { k: 9, n: 'Online', i: '🌐' },
    { k: 10, n: 'Hediye Ç.', i: '🎁' }, { k: 11, n: 'Puan', i: '⭐' }
  ];

  // Hangi yöntemler enstrüman listesinden seçim ister (kind → instruments(kind)):
  // kart-türevleri (1,4,5,6,7,9), Yemek(2), Havale(8). Nakit(0) ayrı (cashInstrument).
  const INSTRUMENT_KINDS = [1, 2, 4, 5, 6, 7, 8, 9];

  // ================= APP STATE =================
  const App = {
    session: null,
    ui: null,
    presets: { general: [], allergy: [] },
    zones: [],
    activeTab: 'tables',
    counts: { tables: 0, deliver: 0, drinks: 0, service: 0 },
    poll: null,
    rerenderShell: null,
    mode: null,
    rtBound: false
  };

  function stopPolling() {
    if (App.poll) { clearInterval(App.poll); App.poll = null; }
    if (App.kdsClock) { clearInterval(App.kdsClock); App.kdsClock = null; }
  }

  // ================= ROUTER =================
  on('unauthorized', () => { stopPolling(); Realtime.stop(); renderLogin(); });

  // ---- JWT yardımcıları (SSO devri için; API'nin ürettiği token birebir aynı format) ----
  const ROLE_CLAIM = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role';
  function decodeJwt(token) {
    try {
      const part = String(token).split('.')[1];
      if (!part) return null;
      const b64 = part.replace(/-/g, '+').replace(/_/g, '/');
      const pad = b64 + '==='.slice((b64.length + 3) % 4);
      const json = decodeURIComponent(atob(pad).split('').map(
        c => '%' + ('00' + c.charCodeAt(0).toString(16)).slice(-2)).join(''));
      return JSON.parse(json);
    } catch { return null; }
  }
  function jwtExpired(claims) {
    return !!(claims && claims.exp && (claims.exp * 1000) <= Date.now());
  }
  // JWT claim'lerinden login akışıyla uyumlu bir session üretir (landing yok → mode paramı yönlendirir).
  function sessionFromClaims(c) {
    if (!c) return null;
    let roles = c[ROLE_CLAIM];
    roles = roles == null ? [] : (Array.isArray(roles) ? roles : [roles]);
    return {
      token: Store.getToken(),
      userId: c.sub || c.nameid || null,
      fullName: c.name || '',
      companyCode: c.company_code || '',
      companyName: '',
      branchId: c.branch ? Number(c.branch) : null,
      branchName: '',
      roles: roles,
      role: roles[0] || '',
      landing: null
    };
  }

  // ---- SSO token devri (web yönetim panelinden POS'a geçiş; çift login'i bitirir) ----
  // Açılışta, oturum yüklenmeden ÖNCE URL fragment/query'sinden #token=<JWT>&mode=... okunur.
  const FORCE_MODE_KEY = 'mag.forceMode';
  function consumeSsoHandoff() {
    let token = null, mode = null;
    try {
      const hash = (location.hash || '').replace(/^#/, '');
      const fromHash = new URLSearchParams(hash);
      const fromQuery = new URLSearchParams(location.search || '');
      token = fromHash.get('token') || fromQuery.get('token');
      mode = fromHash.get('mode') || fromQuery.get('mode');
    } catch { /* eski tarayıcı: yok say */ }
    // mode paramı token OLMADAN da forced mode olur — Müşteri Ekranı (#mode=display&table=..)
    // aynı origin/token'ı paylaşan yeni sekmede token'sız (#mode=..) açılır; hash temizlenmez ki table okunabilsin.
    if (mode) { try { sessionStorage.setItem(FORCE_MODE_KEY, mode); } catch {} }
    if (!token) return false;
    Store.setToken(token);
    // Token adres çubuğunda/geçmişte kalmasın.
    try { history.replaceState(null, '', location.pathname + (location.search || '')); } catch {}
    return true;
  }
  function takeForceMode() {
    let m = null;
    try { m = sessionStorage.getItem(FORCE_MODE_KEY); if (m) sessionStorage.removeItem(FORCE_MODE_KEY); } catch {}
    return m || null;
  }

  function start() {
    if (!Store.getBase()) return renderConfig();
    // SSO devri: URL'de token varsa kaydet + fragment'ı temizle (session/token yüklemeden ÖNCE).
    const handedOff = consumeSsoHandoff();
    const token = Store.getToken();
    if (!token) return renderLogin();
    // Yeni SSO token'ı geldiyse eski (olası başka kullanıcıya ait) oturumu at, token'dan yeniden kur.
    if (handedOff) Store.setSession(null);
    // Oturum yoksa (ör. SSO'dan yeni gelen token) JWT claim'lerinden kur.
    let session = Store.getSession();
    if (!session) {
      const claims = decodeJwt(token);
      if (!claims || jwtExpired(claims)) { Store.clearAuth(); return renderLogin(); }
      session = sessionFromClaims(claims);
      if (!session) { Store.clearAuth(); return renderLogin(); }
      Store.setSession(session);
    }
    App.session = session;
    routeByLanding();
  }

  // ================= MODE ROUTING (rol-bazlı ekran) =================
  const MODES = {
    waiter:  { label: 'Garson Terminal', icon: '🍽️' },
    kitchen: { label: 'Mutfak (KDS)',    icon: '🍳' },
    cashier: { label: 'Kasiyer Ödeme',   icon: '💳' },
    patron:  { label: 'Patron Panel',    icon: '📊' },
    // Self-Servis KIOSK: herkese açık müşteri terminali. availableModes'ta YER ALMAZ (rol menüsünde çıkmaz);
    // yalnızca dashboard'dan gelen forced mode=kiosk ile açılır.
    kiosk:   { label: 'Self-Servis',     icon: '🖥' },
    // Müşteri Ekranı (Customer Display): POS ikinci ekranı — salt-okunur adisyon/toplam gösterimi.
    // availableModes'ta YER ALMAZ; yalnız forced mode=display (POS içinden yeni sekme) ile açılır.
    display: { label: 'Müşteri Ekranı',  icon: '🖥' }
  };

  function hasRole(r) {
    const roles = (App.session && App.session.roles || []).map(x => String(x).toLocaleLowerCase('tr'));
    return roles.includes(String(r).toLocaleLowerCase('tr'));
  }
  function isManager() {
    return ['sysadmin', 'admin', 'patron', 'yönetici', 'yonetici', 'müdür', 'mudur', 'manager', 'sahip']
      .some(hasRole);
  }
  // Ödeme yetkisi: kasiyer rolü veya yönetici (isManager admin/sysadmin/patron'u kapsar).
  // İkram/İskonto butonları yalnız bu yetkidekilere gösterilir; garson göremez.
  function canPay() { return isManager() || hasRole('kasiyer'); }
  // İade yetkisi (backend /refund: SysAdmin/Patron/İade/Kasiyer). isManager sysadmin/patron'u kapsar.
  function canRefund() { return isManager() || hasRole('kasiyer') || hasRole('İade'); }
  function availableModes() {
    if (isManager()) return ['waiter', 'kitchen', 'cashier', 'patron'];
    const s = App.session || {};
    if (s.landing === 'kitchen' || hasRole('mutfak')) return ['kitchen'];
    return ['waiter', 'cashier'];   // garson/kasiyer aynı cihazda
  }
  function defaultMode() {
    const s = App.session || {};
    if (s.landing === 'patron') return 'patron';
    if (s.landing === 'kitchen') return 'kitchen';
    if (s.landing === 'cashier') return 'cashier';
    return 'waiter';
  }
  function routeByLanding() {
    // SSO devrinde gelen mode paramı (izinliyse) açılış ekranını belirler; yoksa varsayılan landing.
    const forced = takeForceMode();
    // KIOSK: herkese açık self-servis terminal — rol geçidini (availableModes) ATLA; forced mode=kiosk
    // gelince doğrudan kiosk aç. Boşta-çıkış sayacı KURULMAZ (paylaşımlı terminal açık kalmalı).
    if (forced === 'kiosk') {
      App.mode = 'kiosk';
      renderActiveMode();
      return;
    }
    // MÜŞTERİ EKRANI: POS ikinci ekranı — salt-okunur. Rol geçidini ATLA; boşta-çıkış sayacı KURULMAZ
    // (müşteriye dönük ekran sürekli açık kalmalı). Masa bağlamı location.hash'ten okunur.
    if (forced === 'display') {
      App.mode = 'display';
      renderActiveMode();
      return;
    }
    const avail = availableModes();
    const def = (forced && avail.includes(forced)) ? forced : defaultMode();
    App.mode = avail.includes(def) ? def : avail[0];
    renderActiveMode();
    armIdle();   // yetkili ekrana girildi → boşta zaman aşımı sayacını kur
  }
  function renderActiveMode() {
    stopPolling();
    if (App.mode === 'display') return renderCustomerDisplay();
    if (App.mode === 'kiosk') return renderKiosk();
    if (App.mode === 'kitchen') return renderKds();
    if (App.mode === 'cashier') return renderCashier();
    if (App.mode === 'patron') return renderPatron();
    return renderShell();
  }
  function switchMode(m) {
    if (m === App.mode) return;
    App.mode = m;
    renderActiveMode();
  }
  function openModeMenu() {
    const avail = availableModes();
    if (avail.length <= 1) return;
    const body = el('<div class="mode-list"></div>');
    avail.forEach(m => {
      const md = MODES[m];
      const b = el('<button class="mode-item' + (m === App.mode ? ' on' : '') + '">' +
        '<span class="mi">' + md.icon + '</span><span>' + esc(md.label) + '</span>' +
        (m === App.mode ? '<span class="chk">✓</span>' : '') + '</button>');
      b.addEventListener('click', () => { mm.close(); switchMode(m); });
      body.appendChild(b);
    });
    const mm = modal('Ekran Değiştir', body);
  }

  // ortak üst-bar buton grubu (mode + tema + çıkış)
  function headerButtons() {
    const multi = availableModes().length > 1;
    // Donanım yazıcı butonu yalnız WebSerial destekleyen tarayıcıda (Chrome/Edge + HTTPS) görünür;
    // desteklenmeyen tarayıcıda gizli → yalnız window.print fallback kullanılır.
    const printerSupported = !!(window.Printer && Printer.supported && Printer.supported());
    const wrap = el('<div class="hdr-actions">' +
      (multi ? '<button class="hdr-btn" id="h-mode" title="Ekran Değiştir">🔀</button>' : '') +
      (printerSupported ? '<button class="hdr-btn" id="h-printer" title="Yazıcı Bağla">🖨</button>' : '') +
      '<button class="hdr-btn" id="h-mute" title="Bildirim Sesi">🔔</button>' +
      '<button class="hdr-btn" id="h-theme" title="Tema">🎨</button>' +
      '<button class="hdr-btn" id="h-logout" title="Çıkış">⏻</button></div>');
    if (multi) wrap.querySelector('#h-mode').addEventListener('click', openModeMenu);
    const mb = wrap.querySelector('#h-mute');
    syncMuteBtn(mb);
    mb.addEventListener('click', () => {
      setNotifMuted(!isNotifMuted());
      syncMuteBtn(mb);
      if (!isNotifMuted()) notify('online');   // açıldığında kısa onay bipi (tıklama = ses izni jesti)
    });
    if (printerSupported) {
      const pb = wrap.querySelector('#h-printer');
      syncPrinterBtn(pb);
      pb.addEventListener('click', () => onPrinterBtn(pb));
    }
    wrap.querySelector('#h-theme').addEventListener('click', cycleTheme);
    wrap.querySelector('#h-logout').addEventListener('click', doLogout);
    return wrap;
  }
  // Butonun görünümünü bağlantı durumuna göre güncelle (bağlıysa vurgulu).
  function syncPrinterBtn(btn) {
    if (!btn || !window.Printer) return;
    const on = Printer.isConnected();
    btn.classList.toggle('on', on);
    btn.title = on ? 'Yazıcı bağlı — kesmek için dokun' : 'Yazıcı Bağla';
    btn.setAttribute('aria-pressed', on ? 'true' : 'false');
  }
  async function onPrinterBtn(btn) {
    if (!window.Printer) return;
    try {
      if (Printer.isConnected()) {
        if (await uiConfirm('Donanım yazıcı bağlantısı kesilsin mi? (Fişler yazıcı diyaloğuna döner.)',
          { danger: true, okText: 'Bağlantıyı Kes' })) {
          await Printer.disconnect();
          toast('Yazıcı bağlantısı kesildi', 'ok');
        }
      } else {
        await Printer.connect();
        toast('Yazıcı bağlandı 🖨', 'ok');
      }
    } catch (e) {
      // Kullanıcı port seçimini iptal etti → sessiz; gerçek hatada bildir.
      if (!(e && (e.name === 'NotFoundError' || e.name === 'AbortError'))) errToast(e);
    }
    syncPrinterBtn(btn);
  }
  async function doLogout() {
    if (await uiConfirm('Çıkış yapılsın mı?', { danger: true, okText: 'Çıkış' })) { stopPolling(); Realtime.stop(); Store.clearAuth(); renderLogin(); }
  }

  // ================= CONFIG SCREEN (opsiyonel / gelişmiş — LAN override) =================
  // Varsayılan merkez adres api.js'te gömülü; bu ekran yalnız "Sunucu adresini değiştir" ile açılır.
  // Boş bırakılırsa LAN override temizlenir ve merkez varsayılana dönülür.
  function renderConfig() {
    stopPolling();
    const cur = Store.getOverrideBase();   // yalnız elle girilen LAN override; boşsa merkez varsayılan aktif
    $app.innerHTML = '';
    const card = el(
      '<div class="auth-wrap"><div class="auth-card">' +
      '<div class="auth-logo"><span class="badge">🍽️</span><h1>MindAeon Garson</h1><p>Sunucu adresi (gelişmiş)</p></div>' +
      '<div class="field"><label>API Adresi</label><input id="cfg-base" type="url" placeholder="' + esc(Store.getBase()) + '" value="' + esc(cur) + '" /></div>' +
      '<div class="auth-err" id="cfg-err"></div>' +
      '<button class="btn btn-primary btn-block" id="cfg-save">Kaydet ve Devam Et</button>' +
      '<button class="auth-back" id="cfg-back" style="margin-top:8px">← Giriş ekranına dön</button>' +
      '<p class="muted" style="font-size:12px;text-align:center;margin-top:14px">Yerel ağ (LAN) kurulumu için doldurun. Boş bırakılırsa merkez sunucu kullanılır.</p>' +
      '</div></div>');
    $app.appendChild(card);
    card.querySelector('#cfg-back').addEventListener('click', renderLogin);
    card.querySelector('#cfg-save').addEventListener('click', async () => {
      const v = card.querySelector('#cfg-base').value.trim();
      Store.setBase(v);   // boşsa merkez varsayılana düşer (getBase override boşken DEFAULT_BASE döner)
      try {
        await API.get('/api/ping', { noAuth: true });
      } catch (e) {
        card.querySelector('#cfg-err').textContent = 'Bağlanılamadı: ' + e.message;
        return;
      }
      renderLogin();
    });
  }

  // ================= LOGIN (enumerasyon-güvenli KOD-LOGIN) =================
  // Tek ekran: Şirket Kodu + Kullanıcı Adı + Parola (dropdown/şirket-şube listeleme YOK).
  // API.login → 401 tek-tip hata; { needBranch, branches } → şube seçtir + branchId ile tekrar; başarı → session.
  function renderLogin() {
    stopPolling();
    const state = { companyCode: '', username: '', password: '', branches: null, branch: null };

    $app.innerHTML = '';
    const wrap = el('<div class="auth-wrap"><div class="auth-card">' +
      '<div class="auth-logo"><span class="badge">🍽️</span><h1>MindAeon Garson</h1><p id="lg-sub">Giriş</p></div>' +
      '<div id="lg-body"></div>' +
      '<div class="auth-err" id="lg-err"></div>' +
      '<button class="auth-back" id="lg-cfg" style="margin-top:8px">⚙ Sunucu adresini değiştir</button>' +
      '</div></div>');
    $app.appendChild(wrap);
    wrap.querySelector('#lg-cfg').addEventListener('click', renderConfig);
    const body = wrap.querySelector('#lg-body');
    const sub = wrap.querySelector('#lg-sub');
    const errEl = wrap.querySelector('#lg-err');

    function setErr(m) { errEl.textContent = m || ''; }

    function applyLogin(resp) {
      Store.setToken(resp.token);
      const session = {
        token: resp.token, userId: resp.userId, fullName: resp.fullName,
        companyCode: resp.companyCode, companyName: resp.companyName,
        branchId: resp.branchId, branchName: resp.branchName,
        roles: resp.roles || [], role: (resp.roles && resp.roles[0]) || '', landing: resp.landingScreen
      };
      Store.setSession(session);
      Store.setCreds({ companyCode: state.companyCode, username: state.username });
      App.session = session;
      toast('Hoş geldiniz, ' + (resp.fullName || ''), 'ok');
      routeByLanding();
    }

    // Ortak giriş çağrısı — branch seçilmişse branchId gönderir. needBranch → şube seçici.
    async function attemptLogin(btn) {
      setErr('');
      await busy(btn, async () => {
        const req = { companyCode: state.companyCode, username: state.username, password: state.password };
        if (state.branch) req.branchId = state.branch.id;
        const resp = await API.login(req);
        if (resp && resp.needBranch) {
          state.branches = resp.branches || [];
          showBranchPick();
          return;
        }
        applyLogin(resp);
      }).catch(e => { setErr(e.message); });
    }

    // Adım 1: kimlik girişi (tek ekran, dropdown yok).
    function showCredentials() {
      sub.textContent = 'Giriş'; setErr('');
      state.branches = null; state.branch = null;
      const last = Store.getCreds();
      const f = el('<div>' +
        '<div class="field"><label>Şirket Kodu</label>' +
          '<input id="lg-cc" type="text" autocomplete="organization" autocapitalize="characters" spellcheck="false" placeholder="Şirket kodu" /></div>' +
        '<div class="field"><label>Kullanıcı Adı</label>' +
          '<input id="lg-un" type="text" autocomplete="username" autocapitalize="none" spellcheck="false" placeholder="Kullanıcı adı" /></div>' +
        '<div class="field"><label>Parola</label>' +
          '<input id="lg-pwd" type="password" autocomplete="current-password" placeholder="••••••" /></div>' +
        '<button class="btn btn-primary btn-block" id="lg-go">Giriş</button></div>');
      body.innerHTML = '';
      body.appendChild(f);
      const cc = f.querySelector('#lg-cc');
      const un = f.querySelector('#lg-un');
      const pwd = f.querySelector('#lg-pwd');
      if (last && last.companyCode) cc.value = last.companyCode;
      if (last && last.username) un.value = last.username;
      (cc.value ? (un.value ? pwd : un) : cc).focus();
      const go = f.querySelector('#lg-go');
      function submit() {
        state.companyCode = cc.value.trim();
        state.username = un.value.trim();
        state.password = pwd.value;
        if (!state.companyCode || !state.username || !state.password) { setErr('Şirket kodu, kullanıcı adı ve parola gerekli.'); return; }
        attemptLogin(go);
      }
      go.addEventListener('click', submit);
      [cc, un, pwd].forEach(inp => inp.addEventListener('keydown', e => { if (e.key === 'Enter') submit(); }));
    }

    // Adım 2 (yalnız needBranch): şube seçtir; seçince branchId ekleyerek login'i tekrar çağır.
    function showBranchPick() {
      sub.textContent = 'Şube seçin'; setErr('');
      body.innerHTML = '';
      const back = el('<button class="auth-back">← Geri</button>');
      back.addEventListener('click', () => { state.branch = null; showCredentials(); });
      body.appendChild(back);
      if (!state.branches.length) { body.appendChild(emptyState('🏢', 'Şube bulunamadı')); return; }
      const grid = el('<div class="pick-grid"></div>');
      state.branches.forEach(b => {
        const p = el('<button class="pick">' + esc(b.name) + '</button>');
        p.addEventListener('click', () => { state.branch = b; attemptLogin(p); });
        grid.appendChild(p);
      });
      body.appendChild(grid);
    }

    showCredentials();
  }

  // ================= SHELL =================
  async function renderShell() {
    stopPolling();
    $app.innerHTML = '';
    const s = App.session;
    const shell = el('<div class="shell">' +
      '<header class="app-header">' +
        '<div class="brand"><div class="logo">🍽️</div><div class="titles">' +
          '<b>' + esc(s.companyName || 'MindAeon') + '</b>' +
          '<div class="meta-sub">' + esc(s.branchName || '') + '</div>' +
        '</div></div>' +
        '<div class="sp"></div>' +
        '<div class="titles" style="text-align:right"><b style="font-size:13px">' + esc(s.fullName || '') + '</b>' +
          '<div class="meta-sub">' + esc(s.role || (s.roles[0] || '')) + '</div></div>' +
        '<button class="hdr-btn" id="h-pkg" title="Paket / Gel-Al" style="margin-left:8px">📦</button>' +
      '</header>' +
      '<div class="svc-alert" id="svc-alert" title="Garson çağrıları"></div>' +
      '<main class="content" id="tab-content"></main>' +
      '<nav class="tabbar" id="tabbar"></nav>' +
    '</div>');
    $app.appendChild(shell);
    shell.querySelector('.app-header').appendChild(headerButtons());
    shell.querySelector('#h-pkg').addEventListener('click', () => renderPackages());
    // Çağrı şeridine tıklayınca "Yeni Servis" sekmesine geç.
    shell.querySelector('#svc-alert').addEventListener('click', () => selectTab('service'));
    armAudioOnce();
    App.rerenderShell = null; // shell uses CSS vars, no rerender needed for theme

    // load reference data
    try { App.zones = await API.zones(); } catch (e) { App.zones = []; }
    try { App.ui = await API.uiSettings(); } catch {}
    try { App.presets = await API.notePresets(); } catch {}

    buildTabbar();
    selectTab(App.activeTab || 'tables');

    // realtime + polling
    await Realtime.start();
    bindRealtimeOnce();
    startPolling();
    refreshCounts();
  }

  // Realtime olayları TEK SEFER bağlanır; aktif ekrana göre dağıtılır (mode switch'te çoğalmaz).
  function bindRealtimeOnce() {
    if (App.rtBound) return;
    App.rtBound = true;
    on('rt:NewTicket', () => {
      if (App.mode === 'waiter') refreshCounts();
      if (App.mode === 'kitchen') reloadKds();
      if (App.mode === 'display') reloadCustomerDisplay();
    });
    on('rt:TicketReady', () => {
      if (App.mode === 'waiter') { refreshCounts(); if (['deliver', 'drinks'].includes(App.activeTab)) reloadActiveTab(); }
      if (App.mode === 'kitchen') reloadKds();
      if (App.mode === 'display') reloadCustomerDisplay();
    });
    on('rt:TableChanged', () => {
      if (App.mode === 'waiter') { refreshCounts(); if (App.activeTab === 'tables') reloadActiveTab(); }
      if (App.mode === 'cashier') reloadCashier();
      if (App.mode === 'patron') reloadPatron();
      if (App.mode === 'display') reloadCustomerDisplay();
    });
    on('rt:ServiceChanged', () => {
      // waiter: rozet+banner (refreshCounts içinde) + servis sekmesi; cashier: banner; kiosk: çağrı sayacı/overlay.
      if (App.mode === 'waiter') { refreshCounts(); if (App.activeTab === 'service') reloadActiveTab(); }
      else if (App.mode === 'cashier') { pollServiceAlert(); }
      else if (App.mode === 'kiosk') { refreshKioskCalls(); }
    });
    // Hedefli olay — mutfağa yeni sipariş: veri tazeleme zaten rt:NewTicket'ta yapılır (çift-reload YOK),
    // burada YALNIZ ses. Reyon filtresi/tüm-reyon modu ile eşleşiyorsa çalar.
    on('rt:KitchenNew', (p) => {
      if (App.mode !== 'kitchen') return;
      if (kdsKitchenNewMatches(p)) notify('newTicket');
    });
    // Hedefli olay — garsona "hazır": veri tazeleme zaten rt:TicketReady'de yapılır (çift-reload YOK),
    // burada YALNIZ ses + kısa toast. Kendi masası (ownerUserId === session.userId) daha belirgin ton.
    on('rt:WaiterReady', (p) => {
      if (App.mode !== 'waiter') return;
      const uid = App.session && App.session.userId;
      const mine = p && p.ownerUserId != null && uid != null && String(p.ownerUserId) === String(uid);
      notify(mine ? 'readyMine' : 'ready', { mine });
      const tbl = esc((p && p.tableCode) || '');
      const prod = esc((p && p.productName) || '');
      toast('🔔 ' + tbl + ' hazır: ' + prod, 'ok');
    });
  }

  // KitchenNew hedefli ses filtresi: tüm-reyon modunda (seçim yok) veya payload.reyonIds seçili reyonlarla
  // kesişiyorsa çal. Reyon bilgisi gelmediyse (boş) çal — hedefleme sunucuda zaten yapılmış sayılır.
  function kdsKitchenNewMatches(p) {
    if (!kdsState.reyons || kdsState.reyons.size === 0) return true;
    const ids = (p && p.reyonIds) || [];
    if (!ids.length) return true;
    return ids.some(id => kdsState.reyons.has(num(id)));
  }

  function startPolling() {
    stopPolling();
    App.poll = setInterval(() => {
      refreshCounts();
      if (!Realtime.connected) reloadActiveTab();
    }, Realtime.connected ? 20000 : 8000);
  }

  const TABS = [
    { id: 'tables', label: 'Masalar', icon: '🍽️' },
    { id: 'deliver', label: 'Sipariş Teslim', icon: '🛎️' },
    { id: 'drinks', label: 'Meşrubatlar', icon: '🥤' },
    { id: 'service', label: 'Yeni Servis', icon: '🔔' }
  ];

  function buildTabbar() {
    const bar = document.getElementById('tabbar');
    bar.innerHTML = '';
    TABS.forEach(t => {
      const b = el('<button data-tab="' + t.id + '"><span class="ti">' + t.icon + '</span><span>' + t.label +
        '</span><span class="badge-n" id="badge-' + t.id + '" style="display:none">0</span></button>');
      b.addEventListener('click', () => selectTab(t.id));
      bar.appendChild(b);
    });
  }

  function selectTab(id) {
    App.activeTab = id;
    document.querySelectorAll('#tabbar button').forEach(b => b.classList.toggle('on', b.dataset.tab === id));
    reloadActiveTab();
  }

  let _tabToken = 0;
  function reloadActiveTab() {
    const content = document.getElementById('tab-content');
    if (!content) return;
    const token = ++_tabToken;
    const render = { tables: renderTablesTab, deliver: renderDeliverTab, drinks: renderDrinksTab, service: renderServiceTab }[App.activeTab];
    if (render) render(content, token);
  }
  function tabAlive(token) { return token === _tabToken && !!document.getElementById('tab-content'); }

  async function refreshCounts() {
    try {
      const [tickets, svc, unpaid] = await Promise.all([
        API.tickets(false).catch(() => []),
        API.serviceRequests().catch(() => []),
        API.unpaid().catch(() => [])
      ]);
      const ready = tickets.filter(t => num(t.lineStatus) === 2);
      // Rozet, kartlardaki gruplamayla (masa+sipariş) tutarlı olsun — satır değil grup say.
      const groupCount = (st) => {
        const keys = new Set();
        ready.filter(t => num(t.stationType) === st)
          .forEach(t => keys.add((t.orderId || 0) + '|' + (t.tableCodes || '')));
        return keys.size;
      };
      App.counts.deliver = groupCount(0);
      App.counts.drinks = groupCount(1);
      App.counts.service = svc.length;
      App.counts.tables = unpaid.length;
      ['deliver', 'drinks', 'service', 'tables'].forEach(k => {
        const b = document.getElementById('badge-' + k);
        if (!b) return;
        const n = App.counts[k];
        if (n > 0) { b.style.display = 'flex'; b.textContent = n > 99 ? '99+' : n; }
        else b.style.display = 'none';
      });
      // Belirgin garson-çağrısı şeridi (type 0) — mevcut svc listesinden türetilir (ek istek yok).
      renderServiceAlert(svc);
    } catch {}
  }

  // ===== GARSON ÇAĞRISI: belirgin banner + ses/titreşim (ServiceRequest type 0 = CagriGarson) =====
  // Altyapı yeniden kullanılır (API.serviceRequests / completeServiceRequest). Banner garson VE kasiyer
  // kabuğunda header ile main arasındaki #svc-alert şeridinde gösterilir; çağrı yoksa gizlenir.
  let _svcCallCount = 0;        // önceki bekleyen çağrı sayısı — yalnız ARTIŞTA ses/titreşim için
  let _audioCtx = null;         // WebAudio bağlamı (asset yok; oscillator ile bip)
  let _audioArmed = false;      // ilk kullanıcı dokunuşunda resume (autoplay guard)

  function armAudioOnce() {
    if (_audioArmed) return;
    _audioArmed = true;
    const resume = () => {
      try {
        const AC = window.AudioContext || window.webkitAudioContext;
        if (!AC) return;
        if (!_audioCtx) _audioCtx = new AC();
        if (_audioCtx.state === 'suspended') _audioCtx.resume().catch(() => {});
      } catch {}
    };
    ['pointerdown', 'touchstart', 'click', 'keydown'].forEach(ev =>
      document.addEventListener(ev, resume, { once: true, passive: true }));
  }

  // ===== SES MOTORU (asset yok; WebAudio oscillator) =====
  // tone(): tek bip çekirdeği — mevcut oscillator/gain zarfı korunur, parametrik freq/süre/titreşim.
  // Autoplay engelli / desteklenmeyen tarayıcıda sessizce yutulur (try/catch).
  function tone(freqStart, freqEnd, durMs, vibPattern) {
    try { if (vibPattern && navigator.vibrate) navigator.vibrate(vibPattern); } catch {}
    try {
      const AC = window.AudioContext || window.webkitAudioContext;
      if (!AC) return;
      if (!_audioCtx) _audioCtx = new AC();
      if (_audioCtx.state === 'suspended') _audioCtx.resume().catch(() => {});
      const ctx = _audioCtx;
      const t0 = ctx.currentTime;
      const dur = (durMs || 400) / 1000;
      const o = ctx.createOscillator();
      const g = ctx.createGain();
      o.type = 'sine';
      o.frequency.setValueAtTime(freqStart, t0);
      o.frequency.setValueAtTime(freqEnd, t0 + dur * 0.45);
      g.gain.setValueAtTime(0.0001, t0);
      g.gain.exponentialRampToValueAtTime(0.28, t0 + 0.02);
      g.gain.exponentialRampToValueAtTime(0.0001, t0 + dur * 0.95);
      o.connect(g); g.connect(ctx.destination);
      o.start(t0); o.stop(t0 + dur);
    } catch {}
  }

  // Ton tablosu: her bildirim türü için freq başlangıç/bitiş, süre, titreşim deseni ve (varsa) tekrar sayısı.
  const TONES = {
    service:   { f0: 880, f1: 660,  dur: 400, vib: [200, 100, 200] },          // garson çağrısı (mevcut)
    newTicket: { f0: 660, f1: 990,  dur: 130, vib: [120, 60, 120], repeat: 2 }, // mutfak: çift-bip
    ready:     { f0: 700, f1: 1050, dur: 340, vib: [300] },                     // garson: yükselen
    readyMine: { f0: 700, f1: 1050, dur: 300, vib: [300, 120, 300], repeat: 2 },// kendi masam: daha belirgin/çift
    online:    { f0: 520, f1: 780,  dur: 160, vib: null }                       // ses açıldı onayı
  };

  // Cihaz bildirim sesi ayarı (localStorage) — varsayılan AÇIK (sesli). '1' = kapalı (muted).
  const NOTIF_MUTE_KEY = 'mar.notif.muted';
  function isNotifMuted() { try { return localStorage.getItem(NOTIF_MUTE_KEY) === '1'; } catch { return false; } }
  function setNotifMuted(m) { try { localStorage.setItem(NOTIF_MUTE_KEY, m ? '1' : '0'); } catch {} }

  // notify(): tür bazlı ses+titreşim. Cihaz sesi kapalıysa sessiz. repeat>1 ise kısa aralıkla tekrar (çift-bip).
  function notify(type, opts) {
    if (isNotifMuted()) return;
    const t = TONES[type];
    if (!t) return;
    tone(t.f0, t.f1, t.dur, t.vib);
    const rep = t.repeat || 1;
    for (let i = 1; i < rep; i++) setTimeout(() => tone(t.f0, t.f1, t.dur, null), i * (t.dur + 80));
  }

  // Geriye dönük uyum: eski çağrılar garson-çağrısı tonunu (mute'a saygılı) çalar.
  function alertPulse() { notify('service'); }

  // Header'daki 🔔/🔕 düğmesini mevcut duruma göre günceller.
  function syncMuteBtn(btn) {
    if (!btn) return;
    const m = isNotifMuted();
    btn.textContent = m ? '🔕' : '🔔';
    btn.classList.toggle('muted', m);
    btn.title = m ? 'Bildirim sesi kapalı — açmak için dokun' : 'Bildirim sesi açık';
    btn.setAttribute('aria-pressed', m ? 'true' : 'false');
  }

  // svc: tüm servis istekleri (type filtresizse buradan type 0 süzülür). Banner'ı günceller + artışta uyarır.
  function renderServiceAlert(svc) {
    const host = document.getElementById('svc-alert');
    if (!host) return;
    const calls = (svc || []).filter(r => num(r.type) === 0);
    const n = calls.length;
    if (n <= 0) {
      host.className = 'svc-alert';
      host.innerHTML = '';
      _svcCallCount = 0;
      return;
    }
    let label;
    if (n === 1) label = 'Masa ' + esc(calls[0].tableCode || '') + ' garson çağırıyor';
    else label = n + ' masa garson çağırıyor';
    host.className = 'svc-alert show';
    host.innerHTML = '<span class="sa-ico">🔔</span><b>' + label + '</b>';
    if (n > _svcCallCount) alertPulse();   // yalnız ARTIŞTA (her poll'de değil)
    _svcCallCount = n;
  }

  // Kasiyer/kiosk için bağımsız çağrı yenileme (bu kabuklar refreshCounts çalıştırmaz).
  async function pollServiceAlert() {
    try { renderServiceAlert(await API.serviceRequests(0)); } catch {}
  }

  // ================= TAB 1: TABLES =================
  // tickets/serviceRequests: kart üstündeki "bekleyen işler" rozetleri için (mevcut polled veri).
  // detailCache: dolu masaların order/lines özetini kart açılışında lazy tutar (sipariş özeti + tümünü gör).
  // expanded: hangi masaların inline genişlediği (tableId set).
  const tablesState = { filter: 'all', boards: [], unpaid: [], tickets: [], service: [] };
  const tableDetailCache = new Map();   // tableId -> { order, lines } | 'loading'
  const tableExpanded = new Set();      // tableId

  async function renderTablesTab(content, token) {
    if (!content.dataset.tab || content.dataset.tab !== 'tables') {
      content.dataset.tab = 'tables';
      content.innerHTML = '<div class="chips" id="tbl-chips"></div><div id="tbl-grid"></div>';
      content.querySelector('#tbl-grid').appendChild(loading());
    }
    try {
      const [zoneBoards, unpaid, tickets, service] = await Promise.all([
        Promise.all(App.zones.map(z =>
          API.board(z.id).then(b => ({ zone: z, items: b })).catch(() => ({ zone: z, items: [] })))),
        API.unpaid().catch(() => []),
        API.tickets(false).catch(() => []),
        API.serviceRequests().catch(() => [])
      ]);
      if (!tabAlive(token)) return;
      tablesState.boards = zoneBoards;
      tablesState.unpaid = unpaid;
      tablesState.tickets = tickets || [];
      tablesState.service = service || [];
      // Her tazelemede özet önbelleğini geçersiz kıl (kart açıkken yeniden çekilir).
      tableDetailCache.clear();
      drawTables(content);
    } catch (e) {
      if (!tabAlive(token)) return;
      content.querySelector('#tbl-grid').innerHTML = '';
      content.querySelector('#tbl-grid').appendChild(emptyState('⚠️', e.message));
    }
  }

  function flatTables() {
    const out = [];
    tablesState.boards.forEach(zb => zb.items.forEach(it => out.push({ ...it, zoneName: zb.zone.name, zoneId: zb.zone.id })));
    return out;
  }
  function isLate(t) {
    if (num(t.status) !== 1 || !t.openedAt) return false;
    const mins = (Date.now() - new Date(t.openedAt).getTime()) / 60000;
    return mins >= 90;
  }
  function unpaidFor(orderId) { return tablesState.unpaid.find(u => u.orderId === orderId); }

  function drawTables(content) {
    const all = flatTables();
    // chips
    const chipsHost = content.querySelector('#tbl-chips');
    chipsHost.innerHTML = '';
    const filters = [{ id: 'all', label: 'Tümü' }];
    App.zones.forEach(z => filters.push({ id: 'z' + z.id, label: z.name }));
    filters.push({ id: 'occupied', label: 'Dolu' }, { id: 'waiting', label: 'Hesap Bekleyen' },
      { id: 'reserved', label: 'Rezerve' }, { id: 'empty', label: 'Boş' }, { id: 'late', label: 'Geciken' });
    filters.forEach(f => {
      const cnt = countFilter(all, f.id);
      const c = el('<button class="chip' + (tablesState.filter === f.id ? ' on' : '') + '">' + esc(f.label) +
        '<span class="cnt">' + cnt + '</span></button>');
      c.addEventListener('click', () => { tablesState.filter = f.id; drawTables(content); });
      chipsHost.appendChild(c);
    });
    // grid
    const grid = content.querySelector('#tbl-grid');
    grid.innerHTML = '';
    const list = all.filter(t => matchFilter(t, tablesState.filter));
    if (!list.length) { grid.appendChild(emptyState('🍽️', 'Bu filtrede masa yok')); return; }
    const g = el('<div class="tgrid"></div>');
    list.forEach(t => g.appendChild(tableCard(t)));
    grid.appendChild(g);
    // Dolu masaların sipariş özeti/notunu lazy çek (görünür olanlar) ve kartı yerinde güncelle.
    loadOccupiedDetails(list);
  }

  // ---- bekleyen işler (zamanlı) — mevcut tickets/serviceRequests/unpaid'ten türet ----
  // Dönen liste: { cls: 'drink'|'kitchen'|'call'|'bill', text, mins }
  function pendingWorks(t) {
    const out = [];
    const orderId = t.orderId;
    const tickets = tablesState.tickets || [];
    // Bar hazır, henüz teslim edilmemiş (stationType=1, lineStatus=2) → "Meşrubat Bekliyor"
    const drinks = tickets.filter(x => x.orderId === orderId && num(x.stationType) === 1 && num(x.lineStatus) === 2);
    if (drinks.length) {
      const oldest = drinks.reduce((a, b) => kmins(a.sentAt) > kmins(b.sentAt) ? a : b);
      out.push({ cls: 'drink', text: 'Meşrubat Bekliyor', mins: Math.floor(kmins(oldest.sentAt)) });
    }
    // Mutfak bekleyen/hazırlanan (lineStatus 1 veya 5) → "Sipariş Bekliyor"
    const kitchen = tickets.filter(x => x.orderId === orderId && num(x.stationType) === 0 && [1, 5].includes(num(x.lineStatus)));
    if (kitchen.length) {
      const oldest = kitchen.reduce((a, b) => kmins(a.sentAt) > kmins(b.sentAt) ? a : b);
      out.push({ cls: 'kitchen', text: 'Sipariş Bekliyor', mins: Math.floor(kmins(oldest.sentAt)) });
    }
    // Bu masaya servis isteği (çağrı / hesap) — serviceRequests tableId ile eşleşir
    (tablesState.service || []).filter(r => r.tableId === t.tableId).forEach(r => {
      const type = num(r.type);
      const cls = type === 2 ? 'bill' : 'call';
      const text = type === 2 ? 'Hesap İstiyor' : type === 1 ? 'Servis (Temizlik)' : 'Masaya Çağırıyor';
      out.push({ cls, text, mins: minsFromElapsed(r.elapsedText) });
    });
    return out;
  }
  // "12 dk" / "1 sa 5 dk" metninden dakikayı çıkar (servis isteğinde sunucu metni gelir).
  function minsFromElapsed(txt) {
    if (!txt) return null;
    let total = 0; const h = /(\d+)\s*sa/.exec(txt); const m = /(\d+)\s*dk/.exec(txt);
    if (h) total += Number(h[1]) * 60; if (m) total += Number(m[1]);
    return total || null;
  }

  function firstLineSummary(lines) {
    const vis = (lines || []).filter(l => !l.isDeleted && num(l.lineStatus) !== 4 && !l.parentLineId);
    if (!vis.length) return null;
    const first = vis[0];
    const q = num(first.qty).toLocaleString('tr-TR');
    const more = vis.length - 1;
    return q + 'x ' + (first.productName || '') + (more > 0 ? ' · +' + more + ' ürün' : '');
  }
  function lastOrderTime(lines) {
    // OrderLine'da SentAt yok; en güncel zaman alanını kullan (sentAt yoksa updatedAt/createdAt).
    let max = null;
    (lines || []).filter(l => !l.isDeleted).forEach(l => {
      const iso = l.sentAt || l.updatedAt || l.createdAt;
      if (!iso) return;
      const t = new Date(iso).getTime();
      if (!isNaN(t) && (!max || t > max)) max = t;
    });
    return max;
  }

  // Dolu masaların order/lines özetini paralel çek; her gelende ilgili kartı yerinde güncelle.
  async function loadOccupiedDetails(list) {
    const occ = list.filter(t => num(t.status) === 1 && t.orderId);
    occ.forEach(t => {
      if (tableDetailCache.has(t.tableId)) {
        // önbellekte hazırsa kartı hemen güncelle
        const d = tableDetailCache.get(t.tableId);
        if (d && d !== 'loading') updateCardDetail(t);
        return;
      }
      tableDetailCache.set(t.tableId, 'loading');
      API.orderByTable(t.tableId).then(r => {
        tableDetailCache.set(t.tableId, r || { order: null, lines: [] });
        updateCardDetail(t);
      }).catch(() => { tableDetailCache.set(t.tableId, { order: null, lines: [] }); });
    });
  }

  // Karttaki özet/not bölgesini (ve açıksa expand listesini) DOM'da yeniden çiz.
  function updateCardDetail(t) {
    const card = document.querySelector('.tcard[data-tid="' + t.tableId + '"]');
    if (!card) return;
    const det = card.querySelector('.tdetail');
    if (det) det.innerHTML = cardDetailInner(t);
    bindExpandToggle(card, t);
    if (tableExpanded.has(t.tableId)) renderExpandBody(card, t);
  }

  function countFilter(all, f) { return all.filter(t => matchFilter(t, f)).length; }
  function matchFilter(t, f) {
    const st = num(t.status);
    switch (f) {
      case 'all': return true;
      case 'occupied': return st === 1;
      case 'reserved': return st === 2;
      case 'empty': return st === 0;
      case 'late': return isLate(t);
      case 'waiting': { const u = unpaidFor(t.orderId); return st === 1 && u && u.hasBillRequest; }
      default: if (f[0] === 'z') return t.zoneId === Number(f.slice(1)); return true;
    }
  }

  function tableCard(t) {
    const st = num(t.status);
    const late = isLate(t);
    let cls = st === 1 ? 's-occupied' : st === 2 ? 's-reserved' : 's-empty';
    if (late) cls = 's-late';
    const statLabel = st === 1 ? 'Dolu' : st === 2 ? 'Rezerve' : 'Boş';
    const statCls = late ? 'st-late' : st === 1 ? 'st-occupied' : st === 2 ? 'st-reserved' : 'st-empty';
    const u = unpaidFor(t.orderId);
    const occupied = st === 1;
    const card = el('<div class="tcard ' + cls + (occupied ? ' has-detail' : '') + '" data-tid="' + t.tableId + '">' +
      '<div class="accent-bar"></div>' +
      '<div class="trow"><div><div class="tname">' + esc(t.display || t.code) + '</div>' +
        '<div class="tzone">' + esc(t.zoneName || '') + '</div></div>' +
        '<span class="tstat ' + statCls + '">' + (late ? 'Geciken' : statLabel) + '</span></div>' +
      '<div class="tmeta">' +
        (t.seats ? '<span>👥 <b>' + (u ? u.coverCount || t.seats : t.seats) + '</b></span>' : '') +
        (st === 1 && t.openedAt ? '<span>⏱ ' + esc(elapsed(t.openedAt)) + '</span>' : '') +
      '</div>' +
      (t.waiterName ? '<div class="waiter">👤 ' + esc(t.waiterName) + '</div>' : '') +
      (occupied ? '<div class="tdetail">' + cardDetailInner(t) + '</div>' : '') +
      (u && u.isPartial ? '<div class="tnote">💰 Kısmi ödendi</div>' : '') +
      (occupied ? '<div class="texpand" data-exp hidden></div>' : '') +
      '<div class="tamt"><span class="amt">' + (num(t.amount) > 0 ? money(t.amount) : '') + '</span>' +
        (occupied ? '<span class="see see-toggle" data-toggle>Tümünü Gör →</span>'
                  : '<span class="see">Aç →</span>') + '</div>' +
      (occupied ? '<div class="tactions">' +
          '<button class="btn tact-add" data-add>＋ Ürün Ekle</button>' +
          (canPay() ? '<button class="btn btn-primary tact-pay" data-pay>🧾 Hesap Al</button>' : '') +
        '</div>' : '') +
    '</div>');

    if (occupied) {
      // Kart gövdesine tıklayınca sipariş ekranı; aksiyon/toggle butonları kendi işlerini yapar.
      card.addEventListener('click', (e) => {
        if (e.target.closest('[data-toggle],[data-add],[data-pay],.texpand')) return;
        openOrderScreen(t);
      });
      bindExpandToggle(card, t);
      card.querySelector('[data-add]').addEventListener('click', (e) => { e.stopPropagation(); openOrderScreen(t); });
      const payBtn = card.querySelector('[data-pay]');
      if (payBtn) payBtn.addEventListener('click', (e) => { e.stopPropagation(); openTablePayment(t); });
      if (tableExpanded.has(t.tableId)) { card.classList.add('expanded'); renderExpandBody(card, t); }
    } else {
      card.addEventListener('click', () => openOrderScreen(t));
    }
    return card;
  }

  // Kart orta bölgesi: sipariş özeti + bekleyen işler + masa notu (özet önbelleğine bağlı).
  function cardDetailInner(t) {
    const d = tableDetailCache.get(t.tableId);
    const u = unpaidFor(t.orderId);
    let html = '';

    // 1) Sipariş özeti
    if (d && d !== 'loading') {
      const sum = firstLineSummary(d.lines);
      if (sum) html += '<div class="tsummary">🍽️ ' + esc(sum) + '</div>';
    } else {
      html += '<div class="tsummary skel">Yükleniyor…</div>';
    }

    // 2) Bekleyen işler (zamanlı renkli noktalar)
    const works = pendingWorks(t);
    if (u && u.hasBillRequest && !works.some(w => w.cls === 'bill')) {
      works.push({ cls: 'bill', text: 'Hesap İstiyor', mins: null });
    }
    if (works.length) {
      html += '<div class="tworks">' + works.map(w =>
        '<div class="twork w-' + w.cls + '"><span class="dot"></span><span class="wt">' + esc(w.text) + '</span>' +
        (w.mins != null ? '<span class="wm">' + w.mins + ' dk</span>' : '') + '</div>').join('') + '</div>';
    }

    // 3) Masa notu + alerji
    if (d && d !== 'loading' && d.order) {
      if (d.order.note) html += '<div class="tnotebox">📝 ' + esc(d.order.note) + '</div>';
      if (d.order.allergyNote) html += '<div class="tnotebox allergy">⚠ ' + esc(d.order.allergyNote) + '</div>';
    }
    return html;
  }

  function bindExpandToggle(card, t) {
    const tog = card.querySelector('[data-toggle]');
    if (!tog || tog._bound) return;
    tog._bound = true;
    tog.addEventListener('click', (e) => {
      e.stopPropagation();
      const open = tableExpanded.has(t.tableId);
      if (open) {
        tableExpanded.delete(t.tableId);
        card.classList.remove('expanded');
        const body = card.querySelector('.texpand'); if (body) { body.hidden = true; body.innerHTML = ''; }
        tog.textContent = 'Tümünü Gör →';
      } else {
        tableExpanded.add(t.tableId);
        card.classList.add('expanded');
        tog.textContent = 'Kapat ↑';
        renderExpandBody(card, t);
      }
    });
  }

  // Inline expand: tüm satırlar + son sipariş saati (ekrandan ayrılmadan).
  function renderExpandBody(card, t) {
    const body = card.querySelector('.texpand');
    if (!body) return;
    body.hidden = false;
    const tog = card.querySelector('[data-toggle]'); if (tog) tog.textContent = 'Kapat ↑';
    const d = tableDetailCache.get(t.tableId);
    if (!d || d === 'loading') { body.innerHTML = '<div class="texp-empty">Yükleniyor…</div>'; return; }
    const vis = (d.lines || []).filter(l => !l.isDeleted && num(l.lineStatus) !== 4 && !l.parentLineId);
    let html = '';
    if (!vis.length) html = '<div class="texp-empty">Satır yok</div>';
    else html = vis.map(l => {
      const tot = num(l.qty) * num(l.unitPrice) - num(l.discountAmount);
      return '<div class="texp-line"><span class="q">' + num(l.qty).toLocaleString('tr-TR') + 'x</span>' +
        '<span class="nm">' + esc(l.productName || '') +
          (l.seatNo > 0 ? ' <small>' + l.seatNo + '. Kişi</small>' : '') + '</span>' +
        '<span class="ls">' + lineStatusBadge(l.lineStatus) + '</span>' +
        '<span class="pr">' + money(l.isTreat ? 0 : tot) + '</span></div>';
    }).join('');
    const last = lastOrderTime(d.lines);
    if (last) html += '<div class="texp-foot">Son sipariş: ' + esc(elapsed(new Date(last).toISOString())) + ' önce</div>';
    body.innerHTML = html;
  }

  // Masa kartından doğrudan ödeme (orderState'i bozmadan; kasiyer akışıyla aynı motor).
  async function openTablePayment(t) {
    let d = tableDetailCache.get(t.tableId);
    if (!d || d === 'loading') {
      try { d = await API.orderByTable(t.tableId); tableDetailCache.set(t.tableId, d); }
      catch (e) { return errToast(e); }
    }
    if (!d.order) { toast('Açık adisyon bulunamadı'); return; }
    const total = (d.lines || []).filter(l => !l.isDeleted && num(l.lineStatus) !== 4 && !l.settledClFicheId)
      .reduce((s, l) => s + (l.parentLineId || l.isTreat ? 0 : num(l.qty) * num(l.unitPrice) - num(l.discountAmount)), 0);
    if (total <= 0) { toast('Ödenecek tutar yok'); return; }
    const ctx = {};
    const top = el('<div class="csh-pay">' +
      (d.order.allergyNote ? '<div class="alergi-banner">⚠ ALERJİ: ' + esc(d.order.allergyNote) + '</div>' : '') +
      '<button class="btn btn-block" id="tp-bill" style="margin-bottom:8px">🖨 Hesap Fişi</button>' +
    '</div>');
    top.querySelector('#tp-bill').addEventListener('click', () => printBillReceipt(d.lines, {
      tableName: t.display || t.code, waiterName: t.waiterName, coverCount: d.order.coverCount
    }));
    const payBody = buildPaymentBody({
      orderId: d.order.id, total, coverCount: d.order.coverCount, lines: d.lines,
      onPaid: () => { if (ctx.close) ctx.close(); toast('Ödeme alındı, masa kapandı', 'ok'); reloadActiveTab(); refreshCounts(); }
    });
    top.appendChild(payBody);
    const m = modal((t.display || t.code) + ' — ' + money(total), top);
    ctx.close = m.close;
  }

  function elapsed(iso) {
    if (!iso) return '';
    let ms = Date.now() - new Date(iso).getTime();
    if (ms < 0) ms = 0;
    const min = Math.floor(ms / 60000);
    if (min >= 60) return Math.floor(min / 60) + 'sa ' + (min % 60) + 'dk';
    return min + 'dk';
  }

  // ================= FİŞ YAZDIRMA (tarayıcı) =================
  // Termal-dostu (80mm) basit fiş; gizli iframe'e monospace HTML yazıp window.print() ile bastırır.
  // Bağımsız yardımcı — mevcut sipariş/ödeme akışını etkilemez. printReceipt(model):
  //   { heading, dateStr?, company?, branch?, meta:[[k,v]...], items:[{qty,name,amount|amountText}],
  //     totals:[{label,value|valueText,strong?}], footer? }
  function receiptHtml(model) {
    const S = App.session || {};
    const company = model.company || S.companyName || 'MindAeon';
    const branch = model.branch || S.branchName || '';
    const dateStr = model.dateStr || new Date().toLocaleString('tr-TR');
    const metaRows = (model.meta || []).filter(m => m && m[1] != null && m[1] !== '')
      .map(m => '<div class="r"><span>' + esc(m[0]) + '</span><span>' + esc(String(m[1])) + '</span></div>').join('');
    let itemsHtml = '';
    if (model.items && model.items.length) {
      itemsHtml = '<table class="it"><tbody>' + model.items.map(it =>
        '<tr><td class="q">' + (it.qty ? esc(String(it.qty)) + '×' : '') + '</td>' +
        '<td class="n">' + esc(it.name || '') + '</td>' +
        '<td class="a">' + esc(it.amountText != null ? it.amountText : money(it.amount)) + '</td></tr>'
      ).join('') + '</tbody></table>';
    }
    const totalsHtml = (model.totals || []).filter(Boolean).map(t =>
      '<div class="r' + (t.strong ? ' strong' : '') + '"><span>' + esc(t.label) + '</span><span>' +
      esc(t.valueText != null ? t.valueText : money(t.value)) + '</span></div>').join('');
    return '<!doctype html><html lang="tr"><head><meta charset="utf-8"><title>Fiş</title><style>' +
      '*{margin:0;padding:0;box-sizing:border-box}' +
      'body{font-family:"Courier New",Consolas,monospace;font-size:12px;color:#000;background:#fff;width:80mm;padding:4mm 3mm;line-height:1.35}' +
      'hr{border:0;border-top:1px dashed #000;margin:5px 0}' +
      '.head{text-align:center;margin-bottom:4px}.head .co{font-size:15px;font-weight:bold}.head .br{font-size:12px}' +
      '.heading{text-align:center;font-weight:bold;font-size:13px;margin:4px 0}' +
      '.r{display:flex;justify-content:space-between;gap:8px}.r.strong{font-weight:bold;font-size:14px;margin-top:2px}' +
      'table.it{width:100%;border-collapse:collapse;margin:2px 0}table.it td{vertical-align:top;padding:1px 0}' +
      'td.q{white-space:nowrap;padding-right:4px}td.n{width:100%}td.a{white-space:nowrap;text-align:right;padding-left:4px}' +
      '.foot{text-align:center;margin-top:6px;font-size:11px}' +
      '@media print{body{width:auto}}' +
      '</style></head><body>' +
      '<div class="head"><div class="co">' + esc(company) + '</div>' +
        (branch ? '<div class="br">' + esc(branch) + '</div>' : '') + '</div>' +
      (model.heading ? '<div class="heading">' + esc(model.heading) + '</div>' : '') +
      '<div class="r"><span>Tarih</span><span>' + esc(dateStr) + '</span></div>' +
      metaRows +
      '<hr>' + itemsHtml + (itemsHtml ? '<hr>' : '') +
      totalsHtml +
      (model.footer ? '<hr><div class="foot">' + esc(model.footer) + '</div>' : '') +
      '</body></html>';
  }

  // Donanım köprüsü (ESC/POS RAW) bağlıysa fişi termal yazıcıya gönder; değilse aşağıdaki
  // window.print (HTML iframe) yoluna düş. Additive — WebSerial yoksa hiçbir şey değişmez.
  async function tryHardwarePrint(model) {
    try {
      if (window.Printer && Printer.isConnected && Printer.isConnected() && window.EscPos) {
        const bytes = EscPos.fromModel(model, { money });
        await Printer.print(bytes);   // çekmece kick (nakit) EscPos byte akışına dahil (model.kickDrawer)
        return true;
      }
    } catch (e) { errToast(e); }   // donanım hatası → HTML fallback'e düş
    return false;
  }

  async function printReceipt(model) {
    if (await tryHardwarePrint(model)) return;   // termal yazıcıya gitti → bitti
    try {
      const html = receiptHtml(model);
      const ifr = document.createElement('iframe');
      ifr.setAttribute('aria-hidden', 'true');
      ifr.style.cssText = 'position:fixed;right:0;bottom:0;width:0;height:0;border:0;visibility:hidden';
      document.body.appendChild(ifr);
      const cleanup = () => { setTimeout(() => { try { ifr.remove(); } catch (e) {} }, 1000); };
      const go = () => {
        try {
          const w = ifr.contentWindow;
          w.focus();
          w.onafterprint = cleanup;   // dialog kapanınca temizle
          w.print();
          cleanup();                  // onafterprint desteklenmeyen tarayıcılar için yedek
        } catch (e) { errToast(e); cleanup(); }
      };
      const doc = ifr.contentWindow.document;
      doc.open(); doc.write(html); doc.close();
      if (doc.readyState === 'complete') setTimeout(go, 80);
      else ifr.onload = () => setTimeout(go, 80);
    } catch (e) { errToast(e); }
  }

  // Adisyon satırlarından fiş kalemleri + genel toplam (orderTotal ile tutarlı: silinmiş/iptal/alt-satır/
  // ödenmiş hariç; ikram satırı 0 tutarla "(İKRAM)" olarak gösterilir).
  function receiptItemsFromLines(lines) {
    const vis = (lines || []).filter(l => !l.isDeleted && num(l.lineStatus) !== 4 && !l.parentLineId && !l.settledClFicheId);
    let total = 0;
    const items = vis.map(l => {
      const treat = !!l.isTreat;
      const amt = treat ? 0 : num(l.qty) * num(l.unitPrice) - num(l.discountAmount);
      total += amt;
      return { qty: num(l.qty).toLocaleString('tr-TR'), name: (l.productName || '') + (treat ? ' (İKRAM)' : ''), amount: amt };
    });
    return { items, total };
  }

  // Hesap/Adisyon fişi (ödeme ÖNCESİ) — ürün+adet+tutar dökümü + GENEL TOPLAM.
  function printBillReceipt(lines, opts) {
    opts = opts || {};
    const { items, total } = receiptItemsFromLines(lines);
    if (!items.length) { toast('Yazdırılacak açık satır yok'); return; }
    printReceipt({
      heading: 'HESAP FİŞİ',
      meta: [['Masa', opts.tableName], ['Garson', opts.waiterName],
        [num(opts.coverCount) > 0 ? 'Kişi' : '', num(opts.coverCount) > 0 ? opts.coverCount : '']],
      items,
      totals: [{ label: 'GENEL TOPLAM', value: total, strong: true }],
      footer: 'Bu bir mali belge değildir.'
    });
  }

  // Adisyon + Ödeme fişi (settle SONRASI) — ÜRÜN satırları (linesSnapshot) + ödeme dökümü + toplam + ödenen + para üstü.
  // linesSnapshot: settle ÖNCESİ yakalanan adisyon satırları (yoksa yalnız ödeme dökümü basılır — regresyon yok).
  function printPaymentReceipt(fiche, payments, total, linesSnapshot) {
    fiche = fiche || {};
    // Ürün kalemleri (hesap fişindekiyle aynı biçim: adet × ad, ikram "(İKRAM)", iskonto düşülmüş).
    const prod = receiptItemsFromLines(linesSnapshot || []);
    const paidSum = (payments || []).reduce((s, p) => s + num(p.amount), 0);
    const grand = fiche.total != null ? num(fiche.total) : num(total);
    const change = Math.max(0, paidSum - grand);
    // Ödeme yöntemi dökümü toplamlar bloğuna girer (TOPLAM'dan sonra, her yöntem ayrı satır).
    const payItems = (payments || []).map(p => {
      const pk = PAY_KINDS.find(x => x.k === p.kind) || { n: 'Ödeme', i: '' };
      return { label: (pk.i ? pk.i + ' ' : '') + pk.n, value: num(p.amount) };
    });
    const dateStr = fiche.ficheDate ? new Date(fiche.ficheDate).toLocaleString('tr-TR') : new Date().toLocaleString('tr-TR');
    const hasCash = (payments || []).some(p => num(p.kind) === 0);   // nakit ödeme varsa çekmece kick
    printReceipt({
      heading: 'ADİSYON / ÖDEME FİŞİ',
      dateStr,
      meta: [['Fiş No', fiche.code], ['Müşteri', fiche.customerName]],
      items: prod.items,   // ÜRÜN bloğu (boşsa şablon bloğu atlar)
      totals: [
        { label: 'TOPLAM', value: grand, strong: true },
        ...payItems,       // ödeme yöntemi başına döküm
        { label: 'Ödenen', value: paidSum },
        change > 0.001 ? { label: 'Para Üstü', value: change } : null
      ],
      footer: 'Teşekkür ederiz',
      kickDrawer: hasCash   // donanım yazıcı bağlıysa nakit ödemede çekmece açılır (fallback'te etkisiz)
    });
  }

  // Ödeme başarılıysa küçük bir "Fiş Yazdır" seçeneği sun (settle sonrası).
  function offerPaymentPrint(fiche, payments, total, linesSnapshot) {
    const body = el('<div style="text-align:center">' +
      '<div style="font-size:40px;margin:2px 0 6px">✅</div>' +
      '<p style="margin-bottom:14px">Ödeme alındı.' +
        (fiche && fiche.code ? '<br><small style="opacity:.7">Fiş No: ' + esc(fiche.code) + '</small>' : '') + '</p>' +
      '<button class="btn btn-primary btn-block" id="po-print">🖨 Fiş Yazdır</button>' +
      '<button class="btn btn-block mt8" id="po-close">Kapat</button>' +
    '</div>');
    const m = modal('Ödeme Tamamlandı', body);
    body.querySelector('#po-print').addEventListener('click', () => printPaymentReceipt(fiche, payments, total, linesSnapshot));
    body.querySelector('#po-close').addEventListener('click', () => m.close());
  }

  // ================= TAB 2/3: DELIVER (kitchen) / DRINKS (bar) =================
  function renderDeliverTab(content, token) { renderReadyTab(content, token, 0, 'deliver', '🛎️', 'Teslim edilecek hazır ürün yok'); }
  function renderDrinksTab(content, token) { renderReadyTab(content, token, 1, 'drinks', '🥤', 'Hazır içecek yok'); }

  async function renderReadyTab(content, token, stationType, tab, icon, emptyMsg) {
    if (content.dataset.tab !== tab) {
      content.dataset.tab = tab;
      content.innerHTML = '<div id="rdy-list"></div>';
      content.querySelector('#rdy-list').appendChild(loading());
    }
    try {
      const tickets = await API.tickets(false);
      if (!tabAlive(token)) return;
      const ready = tickets.filter(t => num(t.lineStatus) === 2 && num(t.stationType) === stationType);
      const list = content.querySelector('#rdy-list');
      list.innerHTML = '';
      if (!ready.length) { list.appendChild(emptyState(icon, emptyMsg)); return; }
      // group by table+order
      const groups = {};
      ready.forEach(t => {
        const key = (t.orderId || 0) + '|' + (t.tableCodes || '');
        (groups[key] = groups[key] || { meta: t, items: [] }).items.push(t);
      });
      Object.values(groups).forEach(g => list.appendChild(readyGroupCard(g, icon)));
    } catch (e) {
      if (!tabAlive(token)) return;
      content.querySelector('#rdy-list').innerHTML = '';
      content.querySelector('#rdy-list').appendChild(emptyState('⚠️', e.message));
    }
  }

  function readyGroupCard(g, icon) {
    const m = g.meta;
    const card = el('<div class="rcard">' +
      '<div class="rhead"><div><b>' + esc(m.tableCodes || m.orderCode || '-') + '</b>' +
        '<div class="sub">' + esc(m.zoneName || '') + (m.waiterName ? ' · ' + esc(m.waiterName) : '') + '</div></div>' +
        '<button class="btn btn-primary" data-all>Tümünü Teslim Et</button></div>' +
      '<div class="ritems"></div></div>');
    const itemsHost = card.querySelector('.ritems');
    g.items.forEach(it => {
      const row = el('<div class="ritem"><span class="q">' + esc(num(it.qty).toLocaleString('tr-TR')) + 'x</span>' +
        '<span class="nm">' + esc(it.productName || '') +
          (it.seatNo > 0 ? '<small>' + it.seatNo + '. Kişi</small>' : '') +
          (it.kitchenNote ? '<small>📝 ' + esc(it.kitchenNote) + '</small>' : '') + '</span>' +
        (it.isVip ? '<span class="pill vip">VIP</span>' : '') +
        (it.allergyNote ? '<span class="pill allergy">ALERJİ</span>' : '') +
        '<span class="pill ready">Hazır ' + esc(it.elapsedText || '') + '</span>' +
        '<button class="btn btn-primary" data-line="' + it.lineId + '" style="padding:8px 12px">Teslim</button></div>');
      row.querySelector('[data-line]').addEventListener('click', (e) => deliverLine(e.target, it.lineId));
      itemsHost.appendChild(row);
    });
    card.querySelector('[data-all]').addEventListener('click', async (e) => {
      await busy(e.target, async () => {
        for (const it of g.items) await API.kdsDelivered(it.lineId);
        toast('Teslim edildi', 'ok');
        refreshCounts(); reloadActiveTab();
      });
    });
    return card;
  }

  async function deliverLine(btn, lineId) {
    await busy(btn, async () => {
      await API.kdsDelivered(lineId);
      toast('Teslim edildi', 'ok');
      refreshCounts(); reloadActiveTab();
    });
  }

  // ================= TAB 4: SERVICE =================
  async function renderServiceTab(content, token) {
    if (content.dataset.tab !== 'service') {
      content.dataset.tab = 'service';
      content.innerHTML = '<div id="svc-list"></div>';
      content.querySelector('#svc-list').appendChild(loading());
    }
    try {
      const reqs = await API.serviceRequests();
      if (!tabAlive(token)) return;
      const list = content.querySelector('#svc-list');
      list.innerHTML = '';
      if (!reqs.length) { list.appendChild(emptyState('🔔', 'Bekleyen servis isteği yok')); return; }
      reqs.forEach(r => list.appendChild(serviceCard(r)));
    } catch (e) {
      if (!tabAlive(token)) return;
      content.querySelector('#svc-list').innerHTML = '';
      content.querySelector('#svc-list').appendChild(emptyState('⚠️', e.message));
    }
  }

  function serviceCard(r) {
    const typeIcon = num(r.type) === 2 ? '🧾' : num(r.type) === 1 ? '🧹' : '🛎️';
    const card = el('<div class="rcard"><div class="rhead">' +
      '<div><b>' + typeIcon + ' ' + esc(r.typeText || ('Servis')) + '</b>' +
        '<div class="sub">' + esc(r.tableCode || '') + (r.zoneName ? ' · ' + esc(r.zoneName) : '') +
        (r.elapsedText ? ' · ⏱ ' + esc(r.elapsedText) : '') + '</div>' +
        (r.note ? '<div class="sub">📝 ' + esc(r.note) + '</div>' : '') + '</div>' +
      '<button class="btn btn-primary" data-done>Tamamlandı</button></div></div>');
    card.querySelector('[data-done]').addEventListener('click', async (e) => {
      await busy(e.target, async () => {
        await API.completeServiceRequest(r.id);
        toast('İstek tamamlandı', 'ok');
        refreshCounts(); reloadActiveTab();
      });
    });
    return card;
  }

  // ================= ORDER / MENU / ADISYON SCREEN =================
  const orderState = {};

  // Adisyon satırlarını tazele — masa siparişinde orderByTable, paket/gel-al siparişinde linesByOrder.
  // Mevcut tüm çağrı yerleri bunu kullanır (masa akışı birebir korunur; paket akışı da aynı motoru kullanır).
  function reloadLines() {
    const O = orderState;
    if (O.isPackage && O.order) return API.linesByOrder(O.order.id).then(r => r || []);
    return API.orderByTable(O.table.tableId).then(r => (r && r.lines) || []);
  }

  // Çevrimdışı optimistic satır: kuyruğa alınan addLine için yerel geçici satır (server id'siz).
  // __pending işaretiyle salt-okunur çizilir; online replay sonrası reloadLines gerçek satırla değiştirir.
  function makePendingLine(p, unitPrice, note) {
    return {
      id: null, __pending: true, __localId: 'pending-' + Date.now() + '-' + Math.random().toString(16).slice(2),
      productId: p.id, productName: p.name, qty: 1, unitPrice: num(unitPrice), taxRate: num(p.taxRate),
      discountAmount: 0, isTreat: false, lineStatus: 0, isDeleted: false, parentLineId: null,
      kitchenNote: note || null, seatNo: 0
    };
  }

  async function openOrderScreen(table) {
    stopPolling();
    const O = orderState;
    O.table = table;
    O.isPackage = false;
    O.zoneId = table.zoneId;
    O.view = 'menu';
    O.search = '';
    O.activeCat = 0;
    O.order = null;
    O.lines = [];
    O.categories = [];
    O.products = [];
    O.campaigns = [];

    $app.innerHTML = '';
    const screen = el('<div class="shell">' +
      '<div class="menu-top">' +
        '<button class="hdr-btn" id="o-back">←</button>' +
        '<div class="info"><b>' + esc(table.display || table.code) + '</b><small>' + esc(table.zoneName || '') + '</small></div>' +
        '<div class="cover-step"><button id="o-cov-m">−</button><span id="o-cov">' + (table.seats || 1) + '</span><button id="o-cov-p">+</button></div>' +
        '<button class="hdr-btn" id="o-ops" title="Masa İşlemleri">⇄</button>' +
        '<button class="hdr-btn" id="o-cust" title="Müşteri Ekranı">🖥</button>' +
        '<button class="hdr-btn" id="o-note">📝</button>' +
      '</div>' +
      '<div class="seg"><button data-v="menu" class="on">Menü</button><button data-v="adisyon">Adisyon <span class="n" id="o-lc"></span></button></div>' +
      '<div class="content" id="o-content" style="padding:0"></div>' +
    '</div>');
    $app.appendChild(screen);

    screen.querySelector('#o-back').addEventListener('click', () => { backToShell(); });
    screen.querySelector('#o-ops').addEventListener('click', openTableOpsMenu);
    screen.querySelector('#o-cust').addEventListener('click', () => openCustomerDisplayFor(table.tableId));
    screen.querySelector('#o-note').addEventListener('click', openNoteModal);
    screen.querySelectorAll('.seg button').forEach(b => b.addEventListener('click', () => {
      O.view = b.dataset.v;
      screen.querySelectorAll('.seg button').forEach(x => x.classList.toggle('on', x === b));
      drawOrderContent();
    }));
    screen.querySelector('#o-cov-m').addEventListener('click', () => changeCover(-1));
    screen.querySelector('#o-cov-p').addEventListener('click', () => changeCover(1));

    const content = screen.querySelector('#o-content');
    content.appendChild(loading());

    try {
      // ensure order exists if occupied; otherwise lazy-open on first add.
      // Çevrimdışıyken order özeti (GET, snapshot'sız) alınamayabilir — bu ekranı ENGELLEMESİN:
      // hata olursa order'sız devam (menü snapshot'tan gelir, ürün ekleme kuyruğa alınır).
      const existing = await API.orderByTable(table.tableId).catch(() => null);
      if (existing && existing.order) { O.order = existing.order; O.lines = existing.lines || []; }
      // load menu data (offline: snapshot fallback)
      const [cats, prods] = await Promise.all([API.categories(), API.products()]);
      O.categories = cats; O.products = prods;
      if (O.order) { O.campaigns = await API.campaigns(O.order.id).catch(() => []); }
      updateCover();
      drawOrderContent();
    } catch (e) {
      content.innerHTML = '';
      content.appendChild(emptyState('⚠️', e.message));
    }
  }

  function backToShell() {
    // Masaya girip ürün eklemeden (yalnız kişi/not vb.) çıkıldıysa boş adisyonu sil → masa "Dolu" kalmasın.
    // Boş değilse/kapandıysa sunucu no-op yapar (idempotent).
    const o = orderState.order;
    if (o && o.id) { try { API.deleteIfEmpty(o.id).catch(() => {}); } catch (e) {} }
    orderState.order = null; orderState.lines = [];
    renderShell();
  }

  function updateCover() {
    const cov = document.getElementById('o-cov');
    if (!cov) return;
    const c = orderState.order ? (orderState.order.coverCount || orderState.table.seats || 1) : (orderState.table.seats || 1);
    cov.textContent = c || 1;
  }

  async function ensureOrder() {
    const O = orderState;
    if (O.order) return O.order;
    // İn-flight guard: hızlı çift işlemde tek openOrder çağrısı olsun; ikinci çağıran aynı promise'i bekler.
    if (O._ensuring) return O._ensuring;
    O._ensuring = (async () => {
      const opened = await API.openOrder(O.table.tableId, O.zoneId);
      // Çevrimdışı: masa açma kuyruğa alındı — sunucu id'si olmadan adisyon kurulamaz (offline-first UI Faz 2).
      if (isQueued(opened)) { queuedToast(); throw new ApiError('Çevrimdışı: masa açma kuyruğa alındı.', 0); }
      O.order = opened;
      O.lines = await reloadLines().catch(() => O.lines);
      O.campaigns = await API.campaigns(O.order.id).catch(() => []);
      updateCover();
      return O.order;
    })();
    try { return await O._ensuring; }
    finally { O._ensuring = null; }
  }

  async function changeCover(delta) {
    const O = orderState;
    // Çift-tık kaybını önle: bir kişi güncellemesi sürerken yenisini alma.
    if (O._coverBusy) return;
    O._coverBusy = true;
    const cur = O.order ? (O.order.coverCount || O.table.seats || 1) : (O.table.seats || 1);
    const next = Math.max(1, cur + delta);
    document.getElementById('o-cov').textContent = next;
    try {
      await ensureOrder();
      await API.cover(O.order.id, next);
      O.order.coverCount = next;
    } catch (e) { errToast(e); updateCover(); }
    finally { O._coverBusy = false; }
  }

  function lineCount() { return orderState.lines.filter(l => !l.isDeleted && num(l.lineStatus) !== 4 && !l.parentLineId).length; }
  function orderTotal() {
    return orderState.lines.filter(l => !l.isDeleted && num(l.lineStatus) !== 4 && !l.settledClFicheId)
      .reduce((s, l) => s + (l.parentLineId || l.isTreat ? 0 : num(l.qty) * num(l.unitPrice) - num(l.discountAmount)), 0);
  }

  function refreshLineCountBadge() {
    const lc = document.getElementById('o-lc');
    if (lc) { const n = lineCount(); lc.textContent = n ? '(' + n + ')' : ''; }
  }

  function drawOrderContent() {
    const content = document.getElementById('o-content');
    if (!content) return;
    refreshLineCountBadge();
    content.innerHTML = '';
    if (orderState.view === 'menu') drawMenu(content);
    else drawAdisyon(content);
  }

  // ---- MENU view ----
  function drawMenu(content) {
    const O = orderState;
    // İki sütunlu yerleşim: solda menü (arama/kampanya/kategori/ürünler), sağda kalıcı canlı adisyon.
    // Geniş ekranda (tablet) panel yanda durur; dar ekranda alt çubuğun üstünde açılır şerit olur.
    const layout = el('<div class="menu-layout">' +
      '<div class="menu-main">' +
        '<div style="height:10px"></div>' +
        '<div class="searchbar"><span class="si">🔍</span><input id="m-search" placeholder="Ürün ara..." value="' + esc(O.search) + '" /></div>' +
        (O.campaigns && O.campaigns.length ? '<div class="camp-row" id="m-camps"></div>' : '') +
        '<div class="cats" id="m-cats"></div>' +
        '<div class="pgrid" id="m-grid"></div>' +
      '</div>' +
      '<aside class="live-cart" id="live-cart"></aside>' +
    '</div>');
    content.appendChild(layout);
    const wrap = layout.querySelector('.menu-main');

    // campaigns — daha belirgin kart stili + "KULLANILABİLİR" rozeti
    if (O.campaigns && O.campaigns.length) {
      drawCampaigns(layout.querySelector('#m-camps'));
    }

    // category tabs
    const catsHost = wrap.querySelector('#m-cats');
    const allCats = [{ id: 0, name: 'Tümü' }].concat(O.categories);
    allCats.forEach(c => {
      // hasImage → küçük resim (ürün kartındaki onerror fallback deseni); yoksa mevcut metin görünümü.
      // "Tümü" (id=0) ve hasImage olmayanlar resimsiz kalır. Stil, yeni CSS'e bağımlı olmasın diye inline.
      const hasImg = !!(c.id && c.hasImage);
      const img = hasImg
        ? '<img loading="lazy" src="' + esc(API.categoryImageUrl(c.id)) +
          '" onerror="this.remove()" style="width:22px;height:22px;border-radius:50%;object-fit:cover;flex-shrink:0" />'
        : '';
      const b = el('<button class="chip' + (O.activeCat === c.id ? ' on' : '') + '"' +
        (hasImg ? ' style="display:inline-flex;align-items:center;gap:6px"' : '') + '>' +
        img + '<span>' + esc(c.name) + '</span></button>');
      b.addEventListener('click', () => { O.activeCat = c.id; drawMenuGrid(wrap); updateCatChips(catsHost); });
      catsHost.appendChild(b);
    });

    const search = wrap.querySelector('#m-search');
    search.addEventListener('input', () => { O.search = search.value; drawMenuGrid(wrap); });

    drawMenuGrid(wrap);
    renderLiveCart();
    content.appendChild(menuBottomBar());
  }

  // Kampanya çipleri — uygun+uygulanmamış olanlar parlayan vurgu + "KULLANILABİLİR" rozeti alır.
  function drawCampaigns(host) {
    if (!host) return;
    host.innerHTML = '';
    (orderState.campaigns || []).forEach(c => {
      let cls = 'camp-chip';
      if (c.applied) cls += ' applied';
      else if (c.eligible) cls += ' eligible';
      else cls += ' locked';
      const b = el('<button class="' + cls + '" data-cid="' + c.id + '">' +
        '<span class="ci">🎯</span>' +
        '<span class="cn">' + esc(c.name) + '</span>' +
        (c.applied ? '<span class="cb ok">✓ UYGULANDI</span>'
          : c.eligible ? '<span class="cb go">' + (c.mandatory ? '🔒 OTOMATİK' : 'KULLANILABİLİR') + '</span>'
          : '<span class="cb">—</span>') +
      '</button>');
      if (!c.applied && c.eligible) b.addEventListener('click', (e) => applyCampaign(e.currentTarget, c));
      host.appendChild(b);
    });
    // Uygulanmış kampanya varsa "Tümünü Kaldır" (WPF CampaignApplyWindow RemoveCampaign) — hepsini kaldırır.
    if ((orderState.campaigns || []).some(c => c.applied)) {
      const rm = el('<button class="camp-chip camp-remove"><span class="ci">✖</span>' +
        '<span class="cn">Kampanyayı Kaldır</span><span class="cb">TÜMÜNÜ KALDIR</span></button>');
      rm.addEventListener('click', (e) => removeCampaigns(e.currentTarget));
      host.appendChild(rm);
    }
  }

  // Uygulanan tüm kampanyaları kaldır → kampanya listesini/toplamı tazele.
  async function removeCampaigns(btn) {
    if (!orderState.order) return;
    if (!(await uiConfirm('Uygulanan kampanya(lar) kaldırılsın mı?', { danger: true, okText: 'Kaldır' }))) return;
    await busy(btn, async () => {
      await API.removeCampaign(orderState.order.id);
      orderState.campaigns = await API.campaigns(orderState.order.id).catch(() => orderState.campaigns);
      orderState.lines = await reloadLines().catch(() => orderState.lines);
      toast('Kampanya kaldırıldı', 'ok');
      drawOrderContent();
    });
  }

  function updateCatChips(host) {
    [...host.children].forEach((b, i) => {
      const id = i === 0 ? 0 : orderState.categories[i - 1].id;
      b.classList.toggle('on', orderState.activeCat === id);
    });
  }

  function drawMenuGrid(wrap) {
    const O = orderState;
    const grid = wrap.querySelector('#m-grid');
    grid.innerHTML = '';
    const q = O.search.trim().toLocaleLowerCase('tr');
    let list = O.products;
    if (O.activeCat) list = list.filter(p => (p.categoryIds || []).includes(O.activeCat));
    if (q) list = list.filter(p => (p.name || '').toLocaleLowerCase('tr').includes(q) || (p.code || '').toLocaleLowerCase('tr').includes(q));
    if (!list.length) { grid.appendChild(emptyState('🔍', 'Ürün bulunamadı')); return; }
    list.forEach(p => grid.appendChild(productCard(p)));
  }

  function productCard(p) {
    const stopped = !!p.isSalesStopped;
    const card = el('<div class="pcard' + (stopped ? ' stopped' : '') + '">' +
      '<div class="pimg">' + (p.hasImage ? '<img loading="lazy" src="' + esc(API.productImageUrl(p.id)) + '" onerror="this.parentElement.textContent=\'🍴\'" />' : (p.isMenu ? '🍱' : '🍴')) + '</div>' +
      '<div class="pbody"><div class="pname">' + esc(p.name) + '</div>' +
        (p.description ? '<div class="pdesc">' + esc(p.description) + '</div>' : '<div class="pdesc"></div>') +
        '<div class="prow"><span class="pprice">' + money(p.price) + '</span>' +
          (stopped ? '<span class="stopbadge">Durdu</span>' : '<button class="add" title="Ekle">+</button>') +
        '</div></div></div>');
    if (!stopped) card.querySelector('.add').addEventListener('click', (e) => { e.stopPropagation(); addProduct(e.target, p); });
    return card;
  }

  async function addProduct(btn, p) {
    // ÇİFT-TIK KORUMASI: senkron in-flight kilidi. API.productOptions await'i uçarken ikinci hızlı tık
    // aynı ürünü ikinci kez eklemesin. busy(btn) async guard opsiyon await'inden SONRA devreye girdiği için
    // tek başına yetersizdi. Kilit fonksiyon sonunda (finally) bırakılır.
    if (btn && btn.dataset.busy) return;
    if (btn) btn.dataset.busy = '1';
    try {
      // Ürün opsiyonları varsa önce grup-bazlı tek-seçim seçtir (WPF OptionPickerWindow). Yoksa doğrudan ekle.
      let options = null;
      try { options = await API.productOptions(p.id); } catch { options = null; }
      if (options && options.length) {
        openOptionPicker(p, options, (selected) => addProductResolved(btn, p, selected));
        return;
      }
      await addProductResolved(btn, p, null);
    } finally {
      if (btn) delete btn.dataset.busy;
    }
  }

  // Seçilen opsiyonlarla (veya opsiyonsuz) satırı ekler.
  // NOT: OrdersController.AddLine fiyatı/KDV'yi SUNUCUDAN (menüden) çözer; istemci unitPrice'ı yok sayılır.
  // Yine de temel+priceDelta'yı gönderiyoruz (istenildiği gibi) + opsiyon özetini satır notuna yazıyoruz
  // ki seçim kalıcı görünsün (📝 not olarak).
  async function addProductResolved(btn, p, selected) {
    await busy(btn, async () => {
      await ensureOrder();
      const O = orderState;
      let unitPrice = num(p.price);
      let note = null;
      if (selected && selected.length) {
        unitPrice += selected.reduce((s, o) => s + num(o.priceDelta), 0);
        note = selected.map(o => o.name).join(', ');
      }
      const line = await API.addLine(O.order.id, {
        productId: p.id, qty: 1, unitPrice, taxRate: num(p.taxRate), note, seatNo: 0,
        // Seçilen opsiyon ID'lerini SUNUCUYA gönder: istemci unitPrice'ı yok sayıldığından ücretli
        // opsiyon delta'sını sunucu bu ID'lerden uygular (faturaya yansıması için). Opsiyonsuz → boş dizi.
        optionIds: (selected || []).map(o => o.id)
      });
      // Çevrimdışı: satır ekleme kuyruğa alındı. OPTIMISTIC UI — satırı yerel state'e "⏳ bekliyor"
      // işaretiyle ekle ki garson eklediğini görsün; online replay + reloadLines gerçek satırla değiştirir.
      if (isQueued(line)) {
        O.lines = O.lines.concat(makePendingLine(p, unitPrice, note));
        queuedToast();
        if (btn) { btn.classList.add('added'); setTimeout(() => btn.classList.remove('added'), 600); }
        refreshLineCountBadge();
        updateBottomBarTotal();
        renderLiveCart(true);
        updateConnBadge();
        return;
      }
      // refresh lines
      O.lines = await reloadLines().catch(() => O.lines.concat(line));
      // Anlık geri bildirim: yeni satırı canlı adisyonda vurgula, badge/toplam ANINDA güncellenir.
      orderState._flashLineId = (line && line.id) || null;
      toast(p.name + ' eklendi ✓', 'ok');
      if (btn) { btn.classList.add('added'); setTimeout(() => btn.classList.remove('added'), 600); }
      refreshLineCountBadge();
      updateBottomBarTotal();
      renderLiveCart(true);
      // ZORUNLU kampanyalar: satır değişince sunucu OTOMATİK uygular (WPF RefreshCampaigns paritesi) → iskonto anında yansısın.
      await syncCampaignsAfterChange();
    });
  }

  // Satır değişiminden sonra kampanyaları tazele: /campaigns GET sunucuda ZORUNLU+uygun kampanyaları OTOMATİK uygular
  // (WPF RefreshCampaigns paritesi); dönüşte iskontolu satırları yeniden yükle, toplam/kart/çipleri tazele.
  async function syncCampaignsAfterChange() {
    const O = orderState;
    if (!O.order || !(O.campaigns && O.campaigns.length)) return;
    // ESKİ durumu sakla → yeni durumla karşılaştırıp "kampanya kazanıldı" geçişini yakala.
    const prev = new Map((O.campaigns || []).map(c => [c.id, { applied: !!c.applied, eligible: !!c.eligible }]));
    O.campaigns = await API.campaigns(O.order.id).catch(() => O.campaigns);
    O.lines = await reloadLines().catch(() => O.lines);
    // KAZANILDI: applied false→true (mecburi oto-uygulama anı) VEYA eligible false→true (yeni hak).
    // Önceden bilinmeyen (yeni gelen) kampanya kaydı için kutlama tetiklenmez (spam önlenir).
    const won = (O.campaigns || []).filter(c => {
      const p = prev.get(c.id);
      return p && ((!p.applied && !!c.applied) || (!p.eligible && !!c.eligible));
    });
    const host = document.getElementById('m-camps');
    if (host) drawCampaigns(host);
    won.forEach(c => celebrateCampaign(c, host));
    refreshLineCountBadge();
    updateBottomBarTotal();
    renderLiveCart(true);
  }

  // Kampanya kazanıldı kutlaması: kısa toast + (chip host varsa) tek-seferlik pulse/glow (WPF dönen kırmızı/altın glow eşdeğeri).
  // Ödeme akışında chip host yoksa yalnız toast gösterilir.
  function celebrateCampaign(c, host) {
    if (!c) return;
    const nm = c.name || 'Kampanya';
    toast('🎉 ' + nm + (c.applied ? ' kampanyası uygulandı!' : ' kampanyası kazanıldı!'), 'ok');
    if (!host) return;
    const chip = host.querySelector('.camp-chip[data-cid="' + c.id + '"]');
    if (chip) { chip.classList.add('camp-won'); setTimeout(() => { chip.classList.remove('camp-won'); }, 1600); }
  }

  // Opsiyon seçici — options: [{id, groupName, name, priceDelta, isDefault}]. Her grup için tek seçim; varsayılan işaretli.
  function openOptionPicker(p, options, onConfirm) {
    const groups = [];
    const byName = new Map();
    options.forEach(o => {
      const key = o.groupName || 'Seçenek';
      let g = byName.get(key);
      if (!g) { g = { name: key, opts: [] }; byName.set(key, g); groups.push(g); }
      g.opts.push(o);
    });
    const selection = new Map();   // groupName -> option
    groups.forEach(g => selection.set(g.name, g.opts.find(o => o.isDefault) || g.opts[0]));

    const body = el('<div class="opt-pick"></div>');
    groups.forEach(g => {
      const gEl = el('<div class="opt-grp"><h4>' + esc(g.name) + '</h4><div class="opt-list"></div></div>');
      const list = gEl.querySelector('.opt-list');
      g.opts.forEach(o => {
        const on = selection.get(g.name) === o;
        const delta = num(o.priceDelta);
        const b = el('<button class="opt-item' + (on ? ' on' : '') + '" data-oid="' + o.id + '">' +
          '<span class="opt-ck">' + (on ? '●' : '○') + '</span>' +
          '<span class="opt-nm">' + esc(o.name) + '</span>' +
          (delta ? '<span class="opt-dl">' + (delta > 0 ? '+' : '') + money(delta) + '</span>' : '') +
        '</button>');
        b.addEventListener('click', () => {
          selection.set(g.name, o);
          [...list.children].forEach(ch => {
            const sel = Number(ch.dataset.oid) === o.id;
            ch.classList.toggle('on', sel);
            ch.querySelector('.opt-ck').textContent = sel ? '●' : '○';
          });
        });
        list.appendChild(b);
      });
      body.appendChild(gEl);
    });
    const foot = el('<button class="btn btn-primary btn-block mt8" id="opt-ok">Onayla ve Ekle</button>');
    body.appendChild(foot);
    const m = modal('⚙ ' + (p.name || 'Ürün') + ' — Seçenekler', body);
    foot.addEventListener('click', () => {
      const selected = groups.map(g => selection.get(g.name)).filter(Boolean);
      m.close();
      onConfirm(selected);
    });
  }

  // ---- canlı adisyon paneli (menü görünümü) ----
  function renderLiveCart(animate) {
    const host = document.getElementById('live-cart');
    if (!host) return;
    const O = orderState;
    const visible = O.lines.filter(l => !l.isDeleted && num(l.lineStatus) !== 4 && !l.parentLineId);
    const count = visible.length;
    const flashId = O._flashLineId;

    let rows;
    if (!count) {
      rows = '<div class="lc-empty"><span class="ico">🧾</span>Henüz ürün eklenmedi.<br><small>Menüden ＋ ile ekleyin.</small></div>';
    } else {
      // Son eklenen en üstte görünsün (ters sıra).
      rows = '<div class="lc-list">' + visible.slice().reverse().map(l => {
        const tot = num(l.qty) * num(l.unitPrice) - num(l.discountAmount);
        const isNew = flashId && l.id === flashId;
        const sent = num(l.lineStatus) !== 0;
        // Çevrimdışı kuyrukta bekleyen (optimistic) satır: "⏳ bekliyor" işareti.
        const tag = l.__pending ? '<small class="lc-pending">⏳ bekliyor</small>'
          : (sent ? '<small class="lc-sent">' + (num(l.lineStatus) === 2 ? 'hazır' : num(l.lineStatus) === 3 ? 'teslim' : 'mutfakta') + '</small>'
                  : '<small class="lc-new">yeni</small>');
        return '<div class="lc-row' + (isNew ? ' flash' : '') + (l.__pending ? ' pending' : '') + '" data-lid="' + (l.id || '') + '">' +
          '<span class="lc-q">' + num(l.qty).toLocaleString('tr-TR') + '×</span>' +
          '<span class="lc-nm">' + esc(l.productName || '') + tag + '</span>' +
          '<span class="lc-pr">' + money(l.isTreat ? 0 : tot) + '</span></div>';
      }).join('') + '</div>';
    }

    host.classList.toggle('empty', !count);
    host.innerHTML =
      '<div class="lc-head"><span>🛒 Canlı Adisyon</span><span class="lc-badge">' + count + '</span></div>' +
      rows +
      '<div class="lc-foot"><span>TOPLAM</span><b id="lc-tot">' + money(orderTotal()) + '</b></div>';

    // Yeni satır vurgusu otomatik sönsün; bir kerelik kullanım.
    O._flashLineId = null;
    if (animate) {
      const f = host.querySelector('.lc-row.flash');
      if (f) { f.scrollIntoView({ block: 'nearest' }); setTimeout(() => f.classList.remove('flash'), 1200); }
    }
  }

  function menuBottomBar() {
    const bar = el('<div class="bottombar">' +
      '<div class="tot"><small>Adisyon Toplamı</small><b id="bb-tot">' + money(orderTotal()) + '</b></div>' +
      '<button class="btn" id="bb-sum">Özet</button>' +
      '<button class="btn btn-primary" id="bb-send">Mutfağa Gönder</button>' +
    '</div>');
    bar.querySelector('#bb-sum').addEventListener('click', () => {
      orderState.view = 'adisyon';
      document.querySelectorAll('.seg button').forEach(x => x.classList.toggle('on', x.dataset.v === 'adisyon'));
      drawOrderContent();
    });
    bar.querySelector('#bb-send').addEventListener('click', (e) => sendToKitchen(e.target));
    return bar;
  }
  function updateBottomBarTotal() {
    const tot = money(orderTotal());
    const t = document.getElementById('bb-tot'); if (t) t.textContent = tot;
    const lc = document.getElementById('lc-tot'); if (lc) lc.textContent = tot;
  }

  async function sendToKitchen(btn) {
    const O = orderState;
    if (!O.order) { toast('Önce ürün ekleyin'); return; }
    const pending = O.lines.filter(l => !l.isDeleted && num(l.lineStatus) === 0);
    if (!pending.length) { toast('Gönderilecek bekleyen ürün yok'); return; }
    await busy(btn, async () => {
      const r = await API.send(O.order.id);
      // Çevrimdışı: mutfağa gönderim kuyruğa alındı — bağlantı gelince Background Sync ile iletilir.
      if (isQueued(r)) { queuedToast(); return; }
      O.lines = await reloadLines().catch(() => O.lines);
      // ZORUNLU kampanyaları sunucu OTOMATİK uygular (WPF paritesi) → durumu tazele, sonra çiz.
      await syncCampaignsAfterChange();
      toast('Mutfağa gönderildi (' + pending.length + ')', 'ok');
      drawOrderContent();
    });
  }

  // ---- ADISYON view ----
  function drawAdisyon(content) {
    const O = orderState;
    const wrap = el('<div class="lines" id="ad-lines"></div>');
    content.appendChild(wrap);
    const visible = O.lines.filter(l => !l.isDeleted && num(l.lineStatus) !== 4 && !l.parentLineId);
    if (!visible.length) { wrap.appendChild(emptyState('🧾', 'Adisyon boş. Menüden ürün ekleyin.')); }
    else visible.forEach(l => wrap.appendChild(lineCard(l)));
    content.appendChild(adisyonBottomBar());
  }

  function lineStatusBadge(s) {
    s = num(s);
    const map = { 0: ['Bekliyor', 'st-empty'], 1: ['Mutfakta', 'st-reserved'], 5: ['Hazırlanıyor', 'st-reserved'],
      2: ['Hazır', 'st-occupied'], 3: ['Teslim', 'st-empty'] };
    const m = map[s] || ['', 'st-empty'];
    return '<span class="lstatus ' + m[1] + '">' + m[0] + '</span>';
  }

  function lineCard(l) {
    // Optimistic (çevrimdışı kuyrukta bekleyen) satır: server id'si yok → salt-okunur çiz (aksiyon bağlama yok).
    if (l.__pending) return pendingLineCard(l);
    const O = orderState;
    const children = O.lines.filter(c => c.parentLineId === l.id && !c.isDeleted);
    const total = num(l.qty) * num(l.unitPrice) - num(l.discountAmount);
    const sent = num(l.lineStatus) !== 0; // already sent to kitchen
    const canDiscount = canPay(); // İkram/İskonto yalnız ödeme yetkili rolde
    // Satır-bazlı kampanya atfı: campaignId doluysa rozet kampanya adıyla belirginleşir (elle iskonto/ikram → jenerik rozet).
    const campName = l.campaignId ? (((O.campaigns || []).find(c => c.id === l.campaignId) || {}).name || 'KAMPANYA') : null;
    const card = el('<div class="lcard">' +
      '<div class="lname"><b>' + esc(l.productName || '') + '</b>' +
        (l.seatNo > 0 ? '<small>' + l.seatNo + '. Kişi</small>' : '') +
        (l.kitchenNote ? '<small>📝 ' + esc(l.kitchenNote) + '</small>' : '') +
        (children.length ? '<small>' + children.map(c => '• ' + esc(c.productName)).join(' ') + '</small>' : '') +
        '<div style="margin-top:3px">' + lineStatusBadge(l.lineStatus) +
          (l.isTreat
            ? (campName ? '<span class="lbadge camp">🎁 ' + esc(campName) + ' İKRAM</span>' : '<span class="lbadge treat">İKRAM</span>')
            : '') +
          (num(l.discountAmount) > 0 && !l.isTreat
            ? (campName ? '<span class="lbadge camp">🎯 ' + esc(campName) + ' -' + money(l.discountAmount) + '</span>' : '<span class="lbadge disc">İSK. -' + money(l.discountAmount) + '</span>')
            : '') +
        '</div></div>' +
      '<div class="qstep"><button data-m ' + (sent ? 'style="opacity:.4"' : '') + '>−</button>' +
        '<span class="qv">' + num(l.qty).toLocaleString('tr-TR') + '</span><button data-p>+</button></div>' +
      '<div class="lprice">' + money(l.isTreat ? 0 : total) + '</div>' +
      (canDiscount ? '<button class="ltreat' + (l.isTreat ? ' on' : '') + '" data-treat title="İkram">🎁</button>' : '') +
      (canDiscount ? '<button class="ldisc' + (num(l.discountAmount) > 0 ? ' on' : '') + '" data-disc title="İskonto">%</button>' : '') +
      '<button class="lnote' + (l.kitchenNote ? ' has' : '') + '" data-note title="Açıklama">📝</button>' +
      (sent ? '<button class="lnote" data-reprint title="Satırı Yeniden Yazdır">🖨</button>' : '') +
      '<button class="ldel" data-del>🗑</button>' +
    '</div>');
    const rpb = card.querySelector('[data-reprint]');
    if (rpb) rpb.addEventListener('click', (e) => busy(e.currentTarget, async () => {
      await API.reprintLine(l.id);
      toast('Satır yeniden yazdırıldı', 'ok');
    }));
    card.querySelector('[data-p]').addEventListener('click', (e) => changeLineQty(e.currentTarget, l, num(l.qty) + 1));
    card.querySelector('[data-m]').addEventListener('click', (e) => {
      if (num(l.qty) <= 1) return; changeLineQty(e.currentTarget, l, num(l.qty) - 1);
    });
    card.querySelector('[data-note]').addEventListener('click', (e) => openLineNoteModal(l));
    card.querySelector('[data-del]').addEventListener('click', (e) => cancelLine(e.currentTarget, l));
    const tb = card.querySelector('[data-treat]');
    if (tb) tb.addEventListener('click', (e) => treatLine(e.currentTarget, l));
    const db = card.querySelector('[data-disc]');
    if (db) db.addEventListener('click', (e) => discountLine(e.currentTarget, l));
    return card;
  }

  // Çevrimdışı bekleyen satır kartı — salt-okunur (miktar/ikram/iskonto/sil YOK; online replay ile gerçekleşir).
  function pendingLineCard(l) {
    const total = num(l.qty) * num(l.unitPrice);
    return el('<div class="lcard pending">' +
      '<div class="lname"><b>' + esc(l.productName || '') + '</b>' +
        (l.kitchenNote ? '<small>📝 ' + esc(l.kitchenNote) + '</small>' : '') +
        '<div style="margin-top:3px"><span class="lstatus st-empty">⏳ Bekliyor (çevrimdışı)</span></div></div>' +
      '<div class="qstep"><span class="qv">' + num(l.qty).toLocaleString('tr-TR') + '×</span></div>' +
      '<div class="lprice">' + money(total) + '</div>' +
    '</div>');
  }

  async function changeLineQty(btn, line, qty) {
    await busy(btn, async () => {
      await API.changeQty(line.id, qty);
      orderState.lines = await reloadLines();
      drawOrderContent();
    });
  }

  // ---- SATIR NOTU (AÇIKLAMA) MODALI ----
  // Adisyon satırında 📝 ile açılır; textarea + öneri çipleri (masa notu deseni).
  // Kaydet → API.lineNote(line.id, value) → adisyonu yenile.
  function openLineNoteModal(line) {
    const body = el('<div>' +
      '<div class="preset-wrap"><h4>' + esc(line.productName || 'Ürün') + ' — Açıklama</h4>' +
        '<textarea class="note-ta" id="ln-note" placeholder="Örn: az pişmiş, soğansız...">' +
          esc(line.kitchenNote || '') + '</textarea>' +
        '<div class="preset-chips mt8" id="ln-gp"></div></div>' +
      '<button class="btn btn-primary btn-block" id="ln-save">Kaydet</button>' +
    '</div>');

    const gp = body.querySelector('#ln-gp');
    (App.presets.general || []).forEach(p => {
      const c = el('<button class="chip">' + esc(p) + '</button>');
      c.addEventListener('click', () => appendNote(body.querySelector('#ln-note'), p));
      gp.appendChild(c);
    });

    const m = modal('📝 Satır Açıklaması', body);
    body.querySelector('#ln-save').addEventListener('click', async (e) => {
      await busy(e.target, async () => {
        const value = body.querySelector('#ln-note').value || null;
        await API.lineNote(line.id, value);
        orderState.lines = await reloadLines();
        toast('Açıklama kaydedildi', 'ok');
        m.close();
        drawOrderContent();
      });
    });
  }

  async function cancelLine(btn, line) {
    const sent = num(line.lineStatus) !== 0;
    let returnToStock = false;
    if (sent) {
      // Mutfağa gitmiş: önce Stoğa İade mi / Fire mi (Vazgeç ile çıkış), sonra sebep.
      const choice = await askStockDecision(line);
      if (choice === null) return; // vazgeç
      returnToStock = choice;
    } else {
      if (!(await uiConfirm(line.productName + ' silinsin mi?', { danger: true, okText: 'Sil' }))) return;
    }
    // İptal sebebi HER ZAMAN sorulur (hazır sebeplerden seç ya da yaz; boş bırakılabilir).
    const reason = await uiReason({ title: 'İptal Sebebi', message: '"' + line.productName + '" iptal ediliyor' });
    if (reason === null) return; // diyalogdan vazgeç
    await busy(btn, async () => {
      await API.cancelLine(line.id, reason || null, returnToStock);
      orderState.lines = await reloadLines();
      toast('İptal edildi', 'ok');
      drawOrderContent();
    });
  }

  // Mutfağa gitmiş ürün iptalinde: Stoğa İade (true) / Fire (false) / Vazgeç (null).
  function askStockDecision(line) {
    return new Promise(resolve => {
      let done = false;
      const body = el('<div class="stock-decide">' +
        '<p class="sd-q">"' + esc(line.productName || 'Ürün') + '" mutfağa gönderilmiş.<br>Malzemeler ne olsun?</p>' +
        '<button class="btn btn-block sd-return" id="sd-ret">↩️ Stoğa İade</button>' +
        '<button class="btn btn-block sd-waste" id="sd-fire">🔥 Fire (Stoktan düş)</button>' +
        '<button class="btn btn-block" id="sd-cancel">Vazgeç</button>' +
      '</div>');
      const m = modal('Ürün İptali', body, { onClose: () => { if (!done) { done = true; resolve(null); } } });
      const pick = (v) => { done = true; m.close(); resolve(v); };
      body.querySelector('#sd-ret').addEventListener('click', () => pick(true));
      body.querySelector('#sd-fire').addEventListener('click', () => pick(false));
      body.querySelector('#sd-cancel').addEventListener('click', () => pick(null));
    });
  }

  // ---- İKRAM (treat) — toggle, ödeme yetkili rolde ----
  async function treatLine(btn, line) {
    if (line.campaignId) { toast('Kampanya kalemi elle değiştirilemez; önce kampanyayı kaldırın.'); return; }
    const next = !line.isTreat;
    if (!(await uiConfirm('"' + line.productName + '" ' + (next ? 'ikram edilsin mi?' : 'ikramdan çıkarılsın mı?'), { okText: next ? 'İkram Et' : 'Kaldır' }))) return;
    await busy(btn, async () => {
      await API.treat(line.id, next);
      orderState.lines = await reloadLines();
      toast(next ? 'İkram uygulandı' : 'İkram kaldırıldı', 'ok');
      drawOrderContent();
    });
  }

  // ---- İSKONTO (discount) — satır tutarıyla sınırlı tutar, ödeme yetkili rolde ----
  async function discountLine(btn, line) {
    if (line.campaignId) { toast('Kampanya kalemi elle değiştirilemez; önce kampanyayı kaldırın.'); return; }
    if (line.isTreat) { toast('İkram satırına iskonto uygulanmaz'); return; }
    const maxAmount = num(line.qty) * num(line.unitPrice);
    const raw = await uiPrompt('İskonto tutarı (₺, en çok ' + money(maxAmount) + '):', {
      title: 'İskonto', inputmode: 'decimal', okText: 'Uygula',
      value: num(line.discountAmount) > 0 ? String(num(line.discountAmount)) : ''
    });
    if (raw === null) return; // vazgeç
    const amount = Number(String(raw).replace(',', '.'));
    if (isNaN(amount) || amount < 0) { toast('Geçersiz tutar'); return; }
    if (amount > maxAmount) { toast('İskonto satır tutarını aşamaz (en çok ' + money(maxAmount) + ')'); return; }
    await busy(btn, async () => {
      await API.discount(line.id, amount);
      orderState.lines = await reloadLines();
      toast(amount > 0 ? 'İskonto uygulandı' : 'İskonto kaldırıldı', 'ok');
      drawOrderContent();
    });
  }

  function adisyonBottomBar() {
    const bar = el('<div class="bottombar">' +
      '<div class="tot"><small>Adisyon Toplamı</small><b>' + money(orderTotal()) + '</b></div>' +
      '<button class="btn" id="ab-bill" title="Hesap Fişi Yazdır">🖨 Hesap Fişi</button>' +
      (orderState.order ? '<button class="btn" id="ab-kfiche" title="Mutfak Fişi Yeniden Yazdır">🖨 Mutfak</button>' : '') +
      '<button class="btn" id="ab-send">Mutfağa Gönder</button>' +
      (canPay() ? '<button class="btn btn-primary" id="ab-pay">Ödeme Al</button>' : '') +
    '</div>');
    bar.querySelector('#ab-bill').addEventListener('click', () => {
      const O = orderState;
      printBillReceipt(O.lines, {
        tableName: O.table ? (O.table.display || O.table.code)
          : (O.order && (O.order.deliveryName || O.order.platformText || 'Paket')),
        waiterName: O.table && O.table.waiterName,
        coverCount: O.order && O.order.coverCount
      });
    });
    const kfb = bar.querySelector('#ab-kfiche');
    if (kfb) kfb.addEventListener('click', (e) => busy(e.currentTarget, async () => {
      await API.reprintOrder(orderState.order.id);
      toast('Mutfak fişi yeniden yazdırıldı', 'ok');
    }));
    bar.querySelector('#ab-send').addEventListener('click', (e) => sendToKitchen(e.target));
    const payBtn = bar.querySelector('#ab-pay');
    if (payBtn) payBtn.addEventListener('click', () => {
      if (!orderState.order || orderTotal() <= 0) { toast('Ödenecek tutar yok'); return; }
      openPayment();
    });
    return bar;
  }

  async function applyCampaign(btn, c) {
    await busy(btn, async () => {
      await ensureOrder();
      await API.applyCampaign(orderState.order.id, c.id);
      orderState.campaigns = await API.campaigns(orderState.order.id).catch(() => orderState.campaigns);
      orderState.lines = await reloadLines();
      toast('Kampanya uygulandı', 'ok');
      drawOrderContent();
    });
  }

  // ---- NOTE MODAL ----
  function openNoteModal() {
    const O = orderState;
    const body = el('<div>' +
      '<div class="preset-wrap"><h4>Masa Notu</h4><textarea class="note-ta" id="nm-note" placeholder="Genel not...">' +
        esc(O.order ? (O.order.note || '') : '') + '</textarea>' +
        '<div class="preset-chips mt8" id="nm-gp"></div></div>' +
      '<div class="preset-wrap"><h4>Alerji Uyarısı</h4><textarea class="note-ta" id="nm-allergy" placeholder="Alerji notu...">' +
        esc(O.order ? (O.order.allergyNote || '') : '') + '</textarea>' +
        '<div class="preset-chips mt8" id="nm-ap"></div></div>' +
      '<div class="toggle-row"><button class="tg' + (O.order && O.order.isVip ? ' on' : '') + '" id="nm-vip">⭐ VIP</button>' +
        '<button class="tg' + (O.order && O.order.priority ? ' on' : '') + '" id="nm-pri">⚡ Öncelik</button></div>' +
      '<button class="btn btn-primary btn-block" id="nm-save">Kaydet</button>' +
    '</div>');

    const gp = body.querySelector('#nm-gp');
    (App.presets.general || []).forEach(p => {
      const c = el('<button class="chip">' + esc(p) + '</button>');
      c.addEventListener('click', () => appendNote(body.querySelector('#nm-note'), p));
      gp.appendChild(c);
    });
    const ap = body.querySelector('#nm-ap');
    (App.presets.allergy || []).forEach(p => {
      const c = el('<button class="chip">' + esc(p) + '</button>');
      c.addEventListener('click', () => appendNote(body.querySelector('#nm-allergy'), p));
      ap.appendChild(c);
    });

    let vip = O.order ? !!O.order.isVip : false;
    let pri = O.order ? !!O.order.priority : false;
    body.querySelector('#nm-vip').addEventListener('click', (e) => { vip = !vip; e.currentTarget.classList.toggle('on', vip); });
    body.querySelector('#nm-pri').addEventListener('click', (e) => { pri = !pri; e.currentTarget.classList.toggle('on', pri); });

    const m = modal('Masa Notu', body);
    body.querySelector('#nm-save').addEventListener('click', async (e) => {
      await busy(e.target, async () => {
        await ensureOrder();
        const id = O.order.id;
        await API.orderNote(id, body.querySelector('#nm-note').value || null);
        await API.allergy(id, body.querySelector('#nm-allergy').value || null);
        await API.flags(id, vip, pri);
        O.order.note = body.querySelector('#nm-note').value;
        O.order.allergyNote = body.querySelector('#nm-allergy').value;
        O.order.isVip = vip; O.order.priority = pri;
        toast('Not kaydedildi', 'ok');
        m.close();
      });
    });
  }
  function appendNote(ta, text) { ta.value = ta.value ? (ta.value.replace(/\s+$/, '') + ', ' + text) : text; }

  // ================= MASA İŞLEMLERİ (Taşı / Böl / Birleştir / Kişi Aktar) =================
  // Sipariş ekranından açılır; backend uçları (move/split/merge/transfer-lines) ile çalışır.
  // Hedef masa seçimi mevcut masa verisinden (tüm bölgelerin board'ı) türetilir.

  // Tüm masaları (her bölge) tek listede topla — sipariş ekranında App.zones doludur.
  async function fetchAllTables() {
    const lists = await Promise.all(App.zones.map(z =>
      API.board(z.id).then(b => (b || []).map(it => ({ ...it, zoneName: z.name, zoneId: z.id }))).catch(() => [])));
    return [].concat.apply([], lists);
  }

  // Adisyonun gönderilmemiş+gönderilmiş tüm görünür üst satırlarını döndür (parentLineId == null).
  function opsTopLines() {
    return orderState.lines.filter(l => !l.isDeleted && num(l.lineStatus) !== 4 && !l.parentLineId);
  }

  function openTableOpsMenu() {
    const O = orderState;
    if (!O.order) { toast('Bu masada açık adisyon yok'); return; }
    const body = el('<div class="ops-menu"></div>');
    const items = [
      { k: 'move',     i: '➡️', t: 'Taşı',          d: 'Adisyonu boş bir masaya taşı' },
      { k: 'split',    i: '✂️', t: 'Böl',           d: 'Seçili kalemleri boş masaya ayır' },
      { k: 'merge',    i: '🔗', t: 'Birleştir',     d: 'Bu adisyonu dolu bir masayla birleştir' },
      { k: 'transfer', i: '👥', t: 'Kişi/Adisyon Aktar', d: 'Seçili kalemleri başka adisyona aktar' }
    ];
    items.forEach(it => {
      const b = el('<button class="ops-item"><span class="oi">' + it.i + '</span>' +
        '<span class="ot"><b>' + esc(it.t) + '</b><small>' + esc(it.d) + '</small></span></button>');
      b.addEventListener('click', () => { m.close(); opsAction(it.k); });
      body.appendChild(b);
    });
    const m = modal('⇄ Masa İşlemleri — ' + esc(O.table.display || O.table.code), body);
  }

  function opsAction(kind) {
    if (kind === 'move') return opsMove();
    if (kind === 'split') return opsSplitOrTransfer('split');
    if (kind === 'transfer') return opsSplitOrTransfer('transfer');
    if (kind === 'merge') return opsMerge();
  }

  // Hedef masa seçimi (yalnız boş masa) — Taşı / Böl için.
  function pickEmptyTable(title, onPick) {
    const body = el('<div class="ops-body"><div class="muted" style="padding:6px">Yükleniyor…</div></div>');
    const m = modal(title, body);
    fetchAllTables().then(all => {
      const empties = all.filter(t => num(t.status) === 0 && t.tableId !== orderState.table.tableId);
      body.innerHTML = '';
      if (!empties.length) { body.appendChild(emptyState('🍽️', 'Boş masa yok')); return; }
      const grid = el('<div class="pick-grid"></div>');
      empties.forEach(t => {
        const p = el('<button class="pick">' + esc(t.display || t.code) + '<small>' + esc(t.zoneName || '') + '</small></button>');
        p.addEventListener('click', () => { m.close(); onPick(t); });
        grid.appendChild(p);
      });
      body.appendChild(grid);
    }).catch(e => { body.innerHTML = ''; body.appendChild(emptyState('⚠️', e.message)); });
  }

  // Hedef masa seçimi (yalnız dolu/açık adisyonu olan başka masa) — Birleştir / Aktar için.
  function pickOccupiedTable(title, onPick) {
    const body = el('<div class="ops-body"><div class="muted" style="padding:6px">Yükleniyor…</div></div>');
    const m = modal(title, body);
    fetchAllTables().then(all => {
      const occ = all.filter(t => num(t.status) === 1 && t.orderId && t.tableId !== orderState.table.tableId);
      body.innerHTML = '';
      if (!occ.length) { body.appendChild(emptyState('🍽️', 'Hedef olabilecek dolu masa yok')); return; }
      const grid = el('<div class="pick-grid"></div>');
      occ.forEach(t => {
        const p = el('<button class="pick">' + esc(t.display || t.code) + '<small>' + esc(t.zoneName || '') +
          (num(t.amount) > 0 ? ' · ' + money(t.amount) : '') + '</small></button>');
        p.addEventListener('click', () => { m.close(); onPick(t); });
        grid.appendChild(p);
      });
      body.appendChild(grid);
    }).catch(e => { body.innerHTML = ''; body.appendChild(emptyState('⚠️', e.message)); });
  }

  // Kalem seçim modalı — üst satırlar (parentLineId==null) çoklu seçilir; "kişi/koltuk" hızlı seçimi de var.
  // onConfirm(lineIds[]) ile döner. min=1 zorunlu.
  function pickLines(title, confirmLabel, onConfirm) {
    // Ödenmiş satırlar taşınamaz/bölünemez → seçilebilir listeden, koltuk/kişi sayımından HARİÇ.
    const lines = opsTopLines().filter(l => !l.settledClFicheId);
    if (!lines.length) { toast('Taşınacak (ödenmemiş) kalem yok', 'warn'); return; }
    const selected = new Set();

    // koltuk(seat) bazlı hızlı seçim için koltuk listesi
    const seats = [...new Set(lines.map(l => num(l.seatNo)).filter(s => s > 0))].sort((a, b) => a - b);

    const body = el('<div class="ops-lines"></div>');

    if (seats.length) {
      const sh = el('<div class="ops-seats"><small>Kişiye göre seç:</small><div class="ops-seat-chips"></div></div>');
      const chipHost = sh.querySelector('.ops-seat-chips');
      seats.forEach(s => {
        const c = el('<button class="chip">' + s + '. Kişi</button>');
        c.addEventListener('click', () => {
          // o koltuğun satırlarını topluca seç/kaldır (toggle)
          const ids = lines.filter(l => num(l.seatNo) === s).map(l => l.id);
          const allOn = ids.every(id => selected.has(id));
          ids.forEach(id => { allOn ? selected.delete(id) : selected.add(id); });
          c.classList.toggle('on', !allOn);
          syncRows();
        });
        chipHost.appendChild(c);
      });
      body.appendChild(sh);
    }

    const list = el('<div class="ops-line-list"></div>');
    lines.forEach(l => {
      const tot = num(l.qty) * num(l.unitPrice) - num(l.discountAmount);
      const row = el('<button class="ops-line" data-lid="' + l.id + '">' +
        '<span class="ck">☐</span>' +
        '<span class="q">' + num(l.qty).toLocaleString('tr-TR') + '×</span>' +
        '<span class="nm">' + esc(l.productName || '') +
          (num(l.seatNo) > 0 ? ' <small>' + l.seatNo + '. Kişi</small>' : '') + '</span>' +
        '<span class="pr">' + money(l.isTreat ? 0 : tot) + '</span></button>');
      row.addEventListener('click', () => {
        if (selected.has(l.id)) selected.delete(l.id); else selected.add(l.id);
        syncRows();
      });
      list.appendChild(row);
    });
    body.appendChild(list);

    const foot = el('<div class="ops-foot">' +
      '<button class="btn" id="ops-all">Tümünü Seç</button>' +
      '<button class="btn btn-primary" id="ops-go" disabled>' + esc(confirmLabel) + '</button></div>');
    body.appendChild(foot);

    function syncRows() {
      [...list.children].forEach(row => {
        const on = selected.has(Number(row.dataset.lid));
        row.classList.toggle('sel', on);
        row.querySelector('.ck').textContent = on ? '☑' : '☐';
      });
      foot.querySelector('#ops-go').disabled = selected.size === 0;
    }

    foot.querySelector('#ops-all').addEventListener('click', () => {
      const allOn = lines.every(l => selected.has(l.id));
      lines.forEach(l => allOn ? selected.delete(l.id) : selected.add(l.id));
      // koltuk çiplerini de güncelle
      body.querySelectorAll('.ops-seat-chips .chip').forEach(c => c.classList.toggle('on', !allOn));
      syncRows();
    });
    foot.querySelector('#ops-go').addEventListener('click', (e) =>
      onConfirm([...selected], e.currentTarget, () => m.close()));

    const m = modal(title, body);
  }

  async function opsMove() {
    pickEmptyTable('➡️ Taşınacak masa', (target) => {
      busy(null, async () => {
        await API.moveOrder(orderState.order.id, target.tableId);
        toast((target.display || target.code) + ' masasına taşındı', 'ok');
        backToShell();
      });
    });
  }

  function opsSplitOrTransfer(mode) {
    const isSplit = mode === 'split';
    const title = isSplit ? '✂️ Bölünecek kalemleri seçin' : '👥 Aktarılacak kalemleri seçin';
    pickLines(title, 'Devam →', (lineIds, btn, close) => {
      close();
      // kalemler seçildi → hedef masa
      if (isSplit) {
        pickEmptyTable('✂️ Hangi boş masaya bölünsün?', (target) => {
          busy(null, async () => {
            await API.splitOrder(orderState.order.id, target.tableId, lineIds);
            toast('Adisyon bölündü → ' + (target.display || target.code), 'ok');
            backToShell();
          });
        });
      } else {
        pickOccupiedTable('👥 Hangi adisyona aktarılsın?', (target) => {
          busy(null, async () => {
            await API.transferLines(orderState.order.id, target.orderId, lineIds);
            toast('Kalemler aktarıldı → ' + (target.display || target.code), 'ok');
            backToShell();
          });
        });
      }
    });
  }

  function opsMerge() {
    pickOccupiedTable('🔗 Hangi masayla birleştirilsin?', (target) => {
      busy(null, async () => {
        await API.mergeOrder(orderState.order.id, target.orderId);
        toast((target.display || target.code) + ' ile birleştirildi', 'ok');
        backToShell();
      });
    });
  }

  // ================= PAYMENT (paylaşılan motor) =================
  // Hem garson (sipariş modalı) hem kasiyer aynı ödeme gövdesini kullanır.
  function buildPaymentBody(opts) {
    // opts: { orderId, total, coverCount, onPaid(fiche) }
    const total = num(opts.total);
    // sel: o an seçili yöntemin çözüm durumu (enstrüman / cari / hediye / puan).
    // overrideCustomerId: tüm settle gövdesi için tek cari (Cari/Puan ortak kullanır).
    const payState = {
      method: 0, entered: '', payments: [], cashInstrument: null,
      overrideCustomerId: null,
      // Bu ödeme seti (modal) için SABİT idempotency anahtarı — ilk settle denemesinde bir kez üretilir,
      // retry/çevrimdışı kuyruk AYNI anahtarı taşır → sunucu Idempotent settle çift-tahsilatı önler.
      settleKey: null,
      sel: { instrumentId: null, instruments: null, customer: null, giftcard: null, loyalty: null }
    };

    const body = el('<div>' +
      '<div class="pay-methods" id="p-methods"></div>' +
      '<div class="pay-config" id="p-config"></div>' +
      '<div class="pay-amt"><small>Bu Ödeme</small><div class="big" id="p-amt">' + money(0) + '</div></div>' +
      '<div class="split-row" id="p-split"></div>' +
      '<div class="numpad" id="p-pad"></div>' +
      '<div id="p-list"></div>' +
      '<div class="pay-sum"><span>Adisyon</span><span>' + money(total) + '</span></div>' +
      '<div class="pay-sum tot"><span>Kalan</span><span class="rem" id="p-rem">' + money(total) + '</span></div>' +
      '<button class="btn btn-primary btn-block mt8" id="p-pay">Ödemeyi Al</button>' +
    '</div>');

    const mh = body.querySelector('#p-methods');
    PAY_KINDS.forEach(pk => {
      const b = el('<button class="pay-m' + (pk.k === payState.method ? ' on' : '') + '"><span class="mi">' + pk.i + '</span>' + esc(pk.n) + '</button>');
      b.addEventListener('click', () => {
        payState.method = pk.k;
        payState.sel = { instrumentId: null, instruments: null, customer: null, giftcard: null, loyalty: null };
        [...mh.children].forEach((x, i) => x.classList.toggle('on', PAY_KINDS[i].k === pk.k));
        renderConfig();
      });
      mh.appendChild(b);
    });

    function paid() { return payState.payments.reduce((s, p) => s + p.amount, 0); }
    function remaining() { return Math.max(0, total - paid()); }
    function curAmt() { return payState.entered === '' ? remaining() : (Number(payState.entered) || 0); }

    // ---- yönteme özel konfig paneli (enstrüman / cari / hediye / puan) ----
    const cfg = body.querySelector('#p-config');
    function renderConfig() {
      const m = payState.method;
      cfg.innerHTML = '';
      if (INSTRUMENT_KINDS.includes(m)) { renderInstrumentPicker(m); }
      else if (m === 3) { renderCustomerPicker('cari'); }
      else if (m === 10) { renderGiftcard(); }
      else if (m === 11) { renderCustomerPicker('puan'); }
      // Nakit(0): konfig yok.
    }

    function renderInstrumentPicker(kind) {
      const wrap = el('<div class="pcfg"><label class="pcfg-l">Hesap / Enstrüman</label>' +
        '<select class="pcfg-sel" id="p-instr"><option value="">Yükleniyor...</option></select></div>');
      cfg.appendChild(wrap);
      const sel = wrap.querySelector('#p-instr');
      API.instruments(kind).then(list => {
        list = list || [];
        payState.sel.instruments = list;
        sel.innerHTML = '';
        if (!list.length) { sel.innerHTML = '<option value="">Tanımlı enstrüman yok</option>'; payState.sel.instrumentId = null; return; }
        list.forEach((it, i) => {
          const o = el('<option value="' + it.id + '">' + esc(it.name || it.title || ('#' + it.id)) + '</option>');
          if (i === 0) o.selected = true;
          sel.appendChild(o);
        });
        payState.sel.instrumentId = list[0].id;
      }).catch(e => { sel.innerHTML = '<option value="">' + esc(e.message) + '</option>'; payState.sel.instrumentId = null; });
      sel.addEventListener('change', () => { payState.sel.instrumentId = sel.value ? Number(sel.value) : null; });
    }

    // Cari müşteri: binlerce kayıtta combobox yerine ARAMA-TABANLI custom lookup (sunucuda ad/kod/telefon arar).
    function renderCustomerPicker(mode) {
      const wrap = el('<div class="pcfg"><label class="pcfg-l">Cari Müşteri</label>' +
        '<div class="cust-look">' +
        '<input class="pcfg-inp" id="p-cust-q" placeholder="Ad / kod / telefon ara…" autocomplete="off" />' +
        '<div class="cust-res" id="p-cust-res" hidden></div>' +
        '<div class="cust-sel" id="p-cust-sel" hidden></div>' +
        '</div><div class="pcfg-info" id="p-cust-info"></div></div>');
      cfg.appendChild(wrap);
      const q = wrap.querySelector('#p-cust-q');
      const res = wrap.querySelector('#p-cust-res');
      const selBox = wrap.querySelector('#p-cust-sel');
      const info = wrap.querySelector('#p-cust-info');

      async function loadLoyalty(id) {
        info.textContent = 'Puan sorgulanıyor...';
        try {
          const b = await API.loyaltyBalance(id);
          payState.sel.loyalty = b;
          if (!b || !b.isActive) info.innerHTML = '<span class="pcfg-err">Puan programı pasif</span>';
          else {
            const tl = num(b.points) * (num(b.redeemRate) || 0);
            info.innerHTML = '<b>' + num(b.points).toLocaleString('tr-TR') + '</b> puan · max <b>' + money(tl) + '</b>';
          }
        } catch (e) { info.innerHTML = '<span class="pcfg-err">' + esc(e.message) + '</span>'; }
      }
      function setCustomer(c) {
        payState.sel.customer = c ? c.id : null;
        payState.sel.loyalty = null;
        info.innerHTML = '';
        res.hidden = true; res.innerHTML = '';
        if (!c) { selBox.hidden = true; selBox.innerHTML = ''; q.hidden = false; q.value = ''; setTimeout(() => q.focus(), 20); return; }
        q.hidden = true;
        selBox.hidden = false;
        selBox.innerHTML = '<span class="cust-chip">' + esc((c.code ? c.code + ' · ' : '') + (c.name || '')) +
          ' <button class="cust-x" title="Değiştir">✕</button></span>';
        selBox.querySelector('.cust-x').addEventListener('click', () => setCustomer(null));
        if (mode === 'puan') loadLoyalty(c.id);
      }

      let t = null, seq = 0;
      async function search() {
        const term = (q.value || '').trim();
        if (term.length < 1) { res.hidden = true; res.innerHTML = ''; return; }
        const my = ++seq;
        try {
          const list = (await API.customers(term)) || [];
          if (my !== seq) return;   // yarış: eski sonucu at
          res.innerHTML = '';
          if (!list.length) { res.innerHTML = '<div class="cust-empty">Sonuç yok</div>'; res.hidden = false; return; }
          list.forEach(c => {
            const row = el('<button class="cust-item">' + esc((c.code ? c.code + ' · ' : '') + (c.name || '')) + '</button>');
            row.addEventListener('click', () => setCustomer(c));
            res.appendChild(row);
          });
          res.hidden = false;
        } catch (e) { res.innerHTML = '<div class="cust-empty">' + esc(e.message) + '</div>'; res.hidden = false; }
      }
      q.addEventListener('input', () => { if (t) clearTimeout(t); t = setTimeout(search, 250); });
      q.addEventListener('focus', () => { if (q.value.trim()) search(); });
      setTimeout(() => q.focus(), 30);
    }

    function renderGiftcard() {
      const wrap = el('<div class="pcfg"><label class="pcfg-l">Hediye Çeki Kodu</label>' +
        '<div class="pcfg-row"><input class="pcfg-inp" id="p-gc" placeholder="Kod girin..." autocomplete="off" />' +
        '<button class="btn btn-sm" id="p-gc-q">Sorgula</button></div>' +
        '<div class="pcfg-info" id="p-gc-info"></div></div>');
      cfg.appendChild(wrap);
      const inp = wrap.querySelector('#p-gc');
      const info = wrap.querySelector('#p-gc-info');
      async function query(btn) {
        const code = (inp.value || '').trim();
        payState.sel.giftcard = null; payState.sel.instrumentId = null;
        info.innerHTML = '';
        if (!code) { toast('Kod girin'); return; }
        await busy(btn, async () => {
          try {
            const card = await API.giftcardByCode(code);
            if (num(card.status) !== 0) { info.innerHTML = '<span class="pcfg-err">Kart aktif değil</span>'; return; }
            payState.sel.giftcard = card;
            payState.sel.instrumentId = card.id;
            info.innerHTML = 'Bakiye: <b>' + (card.balanceText || money(card.balance)) + '</b>';
          } catch (e) { info.innerHTML = '<span class="pcfg-err">' + esc(e.message) + '</span>'; }
        });
      }
      wrap.querySelector('#p-gc-q').addEventListener('click', (e) => query(e.currentTarget));
      inp.addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); query(wrap.querySelector('#p-gc-q')); } });
    }

    function refresh() {
      body.querySelector('#p-amt').textContent = money(curAmt());
      const rem = remaining();
      const remEl = body.querySelector('#p-rem');
      remEl.textContent = money(rem);
      remEl.classList.toggle('done', rem <= 0.001);
      const list = body.querySelector('#p-list');
      list.innerHTML = '';
      payState.payments.forEach((p, i) => {
        const pk = PAY_KINDS.find(x => x.k === p.kind);
        const row = el('<div class="pay-sum"><span>' + pk.i + ' ' + esc(pk.n) + '</span><span>' + money(p.amount) +
          ' <button class="ldel" style="font-size:14px" data-rm="' + i + '">✕</button></span></div>');
        row.querySelector('[data-rm]').addEventListener('click', () => {
          const rm = payState.payments.splice(i, 1)[0];
          // Cari/Puan kaldırıldıysa ve başka cari ödeme kalmadıysa override'ı temizle.
          if (rm && (rm.kind === 3 || rm.kind === 11) && !payState.payments.some(x => x.kind === 3 || x.kind === 11)) {
            payState.overrideCustomerId = null;
          }
          refresh();
        });
        list.appendChild(row);
      });
      body.querySelector('#p-pay').textContent = rem <= 0.001 ? 'Ödemeyi Tamamla' : 'Bu Tutarı Ekle';
    }

    const sp = body.querySelector('#p-split');
    [['Tamamı', 1], ['1/2', 2], ['1/3', 3], ['1/4', 4]].forEach(([lbl, n]) => {
      const c = el('<button class="chip">' + lbl + '</button>');
      c.addEventListener('click', () => { payState.entered = (total / n).toFixed(2); refresh(); });
      sp.appendChild(c);
    });
    if (num(opts.coverCount) > 1) {
      const cc = num(opts.coverCount);
      const c = el('<button class="chip">Kişi Başı (' + cc + ')</button>');
      c.addEventListener('click', () => { payState.entered = (total / cc).toFixed(2); refresh(); });
      sp.appendChild(c);
    }

    const pad = body.querySelector('#p-pad');
    ['7','8','9','4','5','6','1','2','3','.','0','⌫'].forEach(k => {
      const b = el('<button>' + k + '</button>');
      b.addEventListener('click', () => {
        if (k === '⌫') payState.entered = payState.entered.slice(0, -1);
        else if (k === '.') { if (!payState.entered.includes('.')) payState.entered = (payState.entered || '0') + '.'; }
        else payState.entered = (payState.entered === '0' ? '' : payState.entered) + k;
        refresh();
      });
      pad.appendChild(b);
    });

    API.instruments(0).then(list => { payState.cashInstrument = (list && list[0]) ? list[0].id : null; }).catch(() => {});

    // Seçili yöntem için ödeme satırı hazırla; doğrulamalar (cap/aktiflik/bakiye) burada.
    // Dönen değer { ok, instrumentId, override?, cap } veya null (hata, mesaj gösterildi).
    async function resolveSelection(amt) {
      const m = payState.method;
      let instrumentId = null;
      if (m === 0) {
        if (payState.cashInstrument == null) { try { const l = await API.instruments(0); payState.cashInstrument = l[0] && l[0].id; } catch {} }
        instrumentId = payState.cashInstrument;
        return { instrumentId, cap: Infinity }; // nakit: para üstü serbest
      }
      if (INSTRUMENT_KINDS.includes(m)) {
        if (payState.sel.instrumentId == null) { toast('Enstrüman/hesap seçin'); return null; }
        return { instrumentId: payState.sel.instrumentId, cap: remaining() };
      }
      if (m === 3) { // Cari
        if (!payState.sel.customer) { toast('Cari müşteri seçin'); return null; }
        return { instrumentId: null, override: payState.sel.customer, cap: remaining() };
      }
      if (m === 10) { // Hediye Ç.
        const gc = payState.sel.giftcard;
        if (!gc) { toast('Hediye çekini sorgulayın'); return null; }
        if (num(gc.status) !== 0) { toast('Kart aktif değil'); return null; }
        if (amt > num(gc.balance) + 0.001) { toast('Bakiye yetersiz: ' + (gc.balanceText || money(gc.balance))); return null; }
        return { instrumentId: gc.id, cap: Math.min(remaining(), num(gc.balance)) };
      }
      if (m === 11) { // Puan
        const lo = payState.sel.loyalty;
        if (!payState.sel.customer) { toast('Müşteri seçin'); return null; }
        if (!lo || !lo.isActive) { toast('Puan programı pasif'); return null; }
        const maxTl = num(lo.points) * (num(lo.redeemRate) || 0);
        if (amt > maxTl + 0.001) { toast('Puan bakiyesi yetersiz: ' + money(maxTl)); return null; }
        return { instrumentId: null, override: payState.sel.customer, cap: Math.min(remaining(), maxTl) };
      }
      return { instrumentId: null, cap: remaining() };
    }

    body.querySelector('#p-pay').addEventListener('click', async (e) => {
      const amt = curAmt();
      if (remaining() > 0.001) {
        if (amt <= 0) { toast('Tutar girin'); return; }
        const r = await resolveSelection(amt);
        if (!r) return;
        // Cari/Puan farklı müşteriye karışmasın: tek override.
        if (r.override != null) {
          if (payState.overrideCustomerId != null && payState.overrideCustomerId !== r.override) {
            toast('Bir adisyonda tek cari müşteri kullanılabilir'); return;
          }
          payState.overrideCustomerId = r.override;
        }
        // Fazla-ödeme koruması: nakit hariç tutarı kalan ile sınırla; nakitte para üstü göster.
        const isCash = payState.method === 0;
        const cap = remaining();
        const applied = isCash ? amt : Math.min(amt, cap);
        payState.payments.push({ kind: payState.method, amount: applied, instrumentId: r.instrumentId, currencyId: 1 });
        const change = isCash ? Math.max(0, amt - cap) : 0;
        payState.entered = '';
        // konfig seçimini bir sonraki ödeme için sıfırla (Hediye kod tekrar sorulsun vb.)
        payState.sel = { instrumentId: null, instruments: null, customer: null, giftcard: null, loyalty: null };
        renderConfig();
        refresh();
        if (change > 0.001) toast('Para üstü: ' + money(change), 'ok');
        if (remaining() > 0.001) { toast('Eklendi. Kalan: ' + money(remaining())); return; }
      }
      await busy(e.target, async () => {
        if (!payState.payments.length) {
          // Hiç ödeme eklenmeden tamamla: seçili yöntemi tüm tutara uygula.
          const r = await resolveSelection(total);
          if (!r) return;
          if (r.override != null) payState.overrideCustomerId = r.override;
          payState.payments.push({ kind: payState.method, amount: total, instrumentId: r.instrumentId, currencyId: 1 });
        }
        // Ödeme dökümünü + adisyon satırlarını fiş için sabitle (settle sonrası UI/satırlar sıfırlanabilir).
        const paymentsSnapshot = payState.payments.slice();
        const linesSnapshot = (opts.lines || []).slice();
        // Bu settle denemesi için sabit idempotency anahtarını bir kez üret (retry + kuyruk aynı key'i taşır).
        payState.settleKey = payState.settleKey || uuid();
        const fiche = await API.settle(opts.orderId, {
          topLineIds: null,
          payments: payState.payments,
          overrideCustomerId: payState.overrideCustomerId
        }, { idempotencyKey: payState.settleKey });
        // Çevrimdışı: ödeme kuyruğa alındı — bağlantı gelince tek sefer işlenir (idempotency çift-tahsilatı önler).
        // onPaid çağrılmaz: adisyon "ödendi" görünmesin (henüz sunucuya işlenmedi); modal açık kalır.
        // ÇİFT-SUBMIT ENGELİ: kuyruğa alındıktan sonra "Ödemeyi Al" butonu KALICI devre dışı kalsın
        // (busy() finally'si normalde geri açardı). Aynı ödeme ikinci kez kuyruğa alınmasın.
        if (isQueued(fiche)) {
          queuedToast();
          const payBtn = body.querySelector('#p-pay');
          if (payBtn) { payBtn.disabled = true; payBtn.dataset.locked = '1'; payBtn.textContent = 'Kuyruğa alındı ⏳'; }
          return;
        }
        if (opts.onPaid) opts.onPaid(fiche);
        // Başarılı toast'a ek: fiş yazdırma seçeneği (para üstü dahil döküm).
        offerPaymentPrint(fiche, paymentsSnapshot, total, linesSnapshot);
      });
    });

    renderConfig();
    refresh();
    return body;
  }

  async function openPayment() {
    const O = orderState;
    const total = orderTotal();
    if (total <= 0) { toast('Ödenecek tutar yok'); return; }   // 0 TL settle + boş fiş engeli
    const ctx = {};
    const body = buildPaymentBody({
      orderId: O.order.id, total, coverCount: O.order && O.order.coverCount, lines: O.lines,
      onPaid: () => { if (ctx.close) ctx.close(); toast('Ödeme alındı', 'ok'); if (O.isPackage) backFromPackage(); else backToShell(); }
    });
    const m = modal('Ödeme — ' + money(total), body);
    ctx.close = m.close;
  }

  // ================= ortak: KDS rozetleri =================
  function kdsBadges(it) {
    let s = '';
    if (it.allergyNote) s += '<span class="pill allergy">⚠ ALERJİ</span>';
    if (it.isVip) s += '<span class="pill vip">VIP</span>';
    if (it.priority) s += '<span class="pill pri">ÖNCELİK</span>';
    if (it.isSpicy) s += '<span class="pill spicy">ACI</span>';
    return s;
  }
  function kmins(iso) { if (!iso) return 0; let m = (Date.now() - new Date(iso).getTime()) / 60000; return m < 0 ? 0 : m; }

  // ================= MUTFAK / KDS =================
  // station: -1 Tümü / 0 Mutfak / 1 Bar.  flt: ek hızlı filtre ('all' | 'package' | 'vip' | 'late').  rush: hızlı polling.
  // reyons: seçili reyonId'ler (çok-seçim). Boş = tüm reyonlar. İstasyon (Mutfak/Bar) filtresiyle AND'lenir.
  const kdsState = { station: -1, flt: 'all', rush: false, lines: [], reyons: new Set() };
  const KDS_RUSH_MS = 7000, KDS_NORMAL_MS = 15000, KDS_OFFLINE_MS = 7000;
  // status->aksiyon eşlemesi (satır bazlı kısmi ilerletme için ortak kaynak).
  const KDS_STEP = {
    1: { lbl: 'Başlat', fn: (id) => API.kdsStart(id) },
    5: { lbl: 'Hazır', fn: (id) => API.kdsReady(id) },
    2: { lbl: 'Teslim Et', fn: (id) => API.kdsDelivered(id) }
  };
  const KDS_COLS = [
    { key: 'pending', label: 'BEKLEYEN', status: 1, tone: 'col-pending', act: KDS_STEP[1] },
    { key: 'prep', label: 'HAZIRLANIYOR', status: 5, tone: 'col-prep', act: KDS_STEP[5] },
    { key: 'ready', label: 'HAZIR', status: 2, tone: 'col-ready', act: KDS_STEP[2] },
    { key: 'done', label: 'TAMAMLANDI', status: 3, tone: 'col-done', act: null }
  ];

  // Bir "kart" = sipariş. İçindeki satırların durumlarından kartın bulunduğu kolon statüsü.
  // Kural: herhangi 1 (Bekleyen) varsa → 1; değilse 5 (Hazırlanıyor) varsa → 5; değilse 2 (Hazır) varsa → 2; hepsi 3 → 3.
  function kdsCardStatus(rows) {
    const have = (st) => rows.some(r => num(r.lineStatus) === st);
    if (have(1)) return 1;
    if (have(5)) return 5;
    if (have(2)) return 2;
    return 3;
  }
  // Backend isCritical (≥20dk) yoksa süreden türet (≥20dk aktif satır).
  function kdsIsCritical(r) {
    if (r.isCritical != null) return !!r.isCritical;
    return [1, 5].includes(num(r.lineStatus)) && kmins(r.sentAt) >= 20;
  }
  function kdsIsCooling(r) {
    if (r.isCooling != null) return !!r.isCooling;
    return num(r.lineStatus) === 2 && kmins(r.sentAt) >= 10;
  }
  // Siparişleri orderId (yoksa orderCode/tableCodes) ile grupla; sırayı koru.
  function kdsGroup(lines) {
    const map = new Map();
    lines.forEach(r => {
      const key = (r.orderId != null ? 'o' + r.orderId : 't' + (r.orderCode || r.tableCodes || r.lineId));
      if (!map.has(key)) map.set(key, []);
      map.get(key).push(r);
    });
    return [...map.values()];
  }

  async function renderKds() {
    stopPolling();
    $app.innerHTML = '';
    const s = App.session;
    const shell = el('<div class="shell kds-shell">' +
      '<header class="app-header kds-header">' +
        '<div class="brand"><div class="logo">🍳</div><div class="titles"><b>KITCHEN DISPLAY</b>' +
          '<div class="meta-sub">' + esc(s.branchName || s.companyName || '') + '</div></div></div>' +
        '<span class="kds-clock" id="kds-clock"></span>' +
        '<div class="sp"></div>' +
        '<div class="kds-stats" id="kds-stats"></div>' +
        '<button class="kds-rush' + (kdsState.rush ? ' on' : '') + '" id="kds-rush">🔥 RUSH</button>' +
      '</header>' +
      '<div class="kds-crit" id="kds-crit"></div>' +
      '<div class="kds-filter" id="kds-filter"></div>' +
      '<main class="kds-board" id="kds-board"></main>' +
    '</div>');
    $app.appendChild(shell);
    shell.querySelector('.app-header').appendChild(headerButtons());
    armAudioOnce();   // KDS'de de ilk dokunuşta ses bağlamını aç (KitchenNew bipleri için)

    // canlı saat (HH:mm) — WPF KdsWindowV2 başlığındaki saat gibi
    const tickClock = () => {
      const c = document.getElementById('kds-clock');
      if (c) c.textContent = new Date().toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' });
    };
    tickClock();
    App.kdsClock = setInterval(tickClock, 1000);

    shell.querySelector('#kds-rush').addEventListener('click', (e) => {
      kdsState.rush = !kdsState.rush;
      e.currentTarget.classList.toggle('on', kdsState.rush);
      kdsApplyPolling();
      reloadKds();
    });

    shell.querySelector('#kds-board').appendChild(loading());
    await Realtime.start();
    bindRealtimeOnce();
    kdsApplyPolling();
    await reloadKds();
  }

  function kdsApplyPolling() {
    stopPolling();
    const base = Realtime.connected ? KDS_NORMAL_MS : KDS_OFFLINE_MS;
    App.poll = setInterval(reloadKds, kdsState.rush ? KDS_RUSH_MS : base);
  }

  async function reloadKds() {
    if (App.mode !== 'kitchen') return;
    const board = document.getElementById('kds-board');
    if (!board) return;
    try {
      kdsState.lines = await API.tickets(true) || [];
      drawKds();
    } catch (e) {
      board.innerHTML = ''; board.appendChild(emptyState('⚠️', e.message));
    }
  }

  // Ek hızlı filtre (Paket/VIP/Gecikenler) — satır bazında uygulanır.
  function kdsFltMatch(l) {
    switch (kdsState.flt) {
      case 'package': return !!l.isNonTable;
      case 'vip': return !!l.isVip;
      case 'late': return kdsIsCritical(l);
      default: return true;
    }
  }

  function drawKds() {
    const board = document.getElementById('kds-board');
    if (!board) return;
    const all = kdsState.lines;
    // İstasyon filtresi (Mutfak/Bar) — reyon çip sayaçları bunun üzerinden hesaplanır.
    const byStation = kdsState.station >= 0 ? all.filter(l => num(l.stationType) === kdsState.station) : all;
    // Reyon (dinamik çok-seçim) filtresi istasyon filtresiyle AND'lenir; boş seçim = tüm reyonlar.
    // Tüm sayaç/istatistik/kartlar reyon seçimini yansıtır.
    let lines = kdsState.reyons.size ? byStation.filter(l => kdsState.reyons.has(num(l.reyonId))) : byStation;

    // ---- üst: kritik gecikme şeridi ----
    const critHost = document.getElementById('kds-crit');
    if (critHost) {
      const crits = lines.filter(kdsIsCritical).sort((a, b) => kmins(b.sentAt) - kmins(a.sentAt));
      if (!crits.length) { critHost.className = 'kds-crit'; critHost.innerHTML = ''; }
      else {
        const parts = crits.slice(0, 4).map(c =>
          esc(c.tableCodes || c.orderCode || '-') + ' · ' + (c.elapsedText || (Math.floor(kmins(c.sentAt)) + 'dk')));
        const more = crits.length > 4 ? ' +' + (crits.length - 4) + ' daha' : '';
        critHost.className = 'kds-crit show';
        critHost.innerHTML = '<span class="kc-ico">⚠</span><b>' + crits.length + ' KRİTİK GECİKME</b>' +
          '<span class="kc-list">' + parts.join('  ·  ') + more + '</span>';
      }
    }

    // ---- istatistik bandı ----
    const actives = lines.filter(l => [1, 5].includes(num(l.lineStatus)));
    const active = actives.length;
    const late = lines.filter(kdsIsCritical).length;
    const ready = lines.filter(l => num(l.lineStatus) === 2).length;
    const cooling = lines.filter(kdsIsCooling).length;
    const avgPrep = active ? Math.round(actives.reduce((s, l) => s + (l.elapsedMinutes != null ? num(l.elapsedMinutes) : kmins(l.sentAt)), 0) / active) : 0;
    const statsHost = document.getElementById('kds-stats');
    if (statsHost) statsHost.innerHTML =
      '<span class="kstat"><b>' + active + '</b>AKTİF</span>' +
      '<span class="kstat ' + (late ? 'warn' : '') + '"><b>' + late + '</b>GECİKEN</span>' +
      '<span class="kstat ok"><b>' + ready + '</b>HAZIR</span>' +
      '<span class="kstat"><b>' + avgPrep + '</b>ORT.dk</span>' +
      '<span class="kstat ' + (cooling ? 'warn' : '') + '"><b>' + cooling + '</b>SOĞUYOR</span>';

    // ---- filtre çipleri (sayaçlı) ----
    const fh = document.getElementById('kds-filter');
    if (fh) {
      fh.innerHTML = '';
      // İstasyon çipleri (sayaç = ilgili istasyondaki satır sayısı; -1 için toplam).
      const stCount = (st) => st < 0 ? all.length : all.filter(l => num(l.stationType) === st).length;
      [['Tümü', -1], ['🍳 Mutfak', 0], ['🥤 Bar', 1]].forEach(([lbl, st]) => {
        const c = el('<button class="chip' + (kdsState.station === st ? ' on' : '') + '">' +
          lbl + ' <span class="cnt">' + stCount(st) + '</span></button>');
        c.addEventListener('click', () => { kdsState.station = st; drawKds(); });
        fh.appendChild(c);
      });
      fh.appendChild(el('<span class="kds-sep"></span>'));
      // Ek çipler — istasyon filtresine göre sayılır (lines).
      const fcount = (f) => f === 'package' ? lines.filter(l => l.isNonTable).length
        : f === 'vip' ? lines.filter(l => l.isVip).length
        : f === 'late' ? late : lines.length;
      [['📦 Paket', 'package'], ['👑 VIP', 'vip'], ['⏰ Gecikenler', 'late']].forEach(([lbl, f]) => {
        const c = el('<button class="chip kds-fchip' + (kdsState.flt === f ? ' on' : '') + '">' +
          lbl + ' <span class="cnt">' + fcount(f) + '</span></button>');
        c.addEventListener('click', () => { kdsState.flt = (kdsState.flt === f ? 'all' : f); drawKds(); });
        fh.appendChild(c);
      });

      // ---- dinamik reyon çipleri (çok-seçim) ----
      // Çekilen ticket'lardaki distinct reyonName/reyonId'den üretilir; birden çok reyon aynı anda seçilebilir.
      // "Tüm Reyonlar" seçimi sıfırlar. Sayaçlar istasyon-filtreli sete göredir.
      const reyonMap = new Map();
      all.forEach(l => {
        if (l.reyonId == null) return;
        const id = num(l.reyonId);
        if (!reyonMap.has(id)) reyonMap.set(id, l.reyonName || ('Reyon ' + id));
      });
      if (reyonMap.size > 1) {
        fh.appendChild(el('<span class="kds-sep"></span>'));
        const allOn = kdsState.reyons.size === 0;
        const allChip = el('<button class="chip kds-rchip' + (allOn ? ' on' : '') + '">🍽️ Tüm Reyonlar</button>');
        allChip.addEventListener('click', () => { kdsState.reyons.clear(); drawKds(); });
        fh.appendChild(allChip);
        [...reyonMap.entries()].forEach(([id, name]) => {
          const cnt = byStation.filter(l => num(l.reyonId) === id).length;
          const on = kdsState.reyons.has(id);
          const c = el('<button class="chip kds-rchip' + (on ? ' on' : '') + '">' +
            esc(name) + ' <span class="cnt">' + cnt + '</span></button>');
          c.addEventListener('click', () => {
            if (kdsState.reyons.has(id)) kdsState.reyons.delete(id);
            else { kdsState.reyons.add(id); Realtime.invoke('JoinReyon', id); }   // hedefli KitchenNew için gruba katıl
            drawKds();
          });
          fh.appendChild(c);
        });
      }
    }

    // ---- kart (sipariş) gruplama + ek çip filtresi ----
    let cards = kdsGroup(lines);
    if (kdsState.flt !== 'all') cards = cards.filter(rows => rows.some(kdsFltMatch));

    board.innerHTML = '';
    KDS_COLS.forEach(col => {
      const colCards = cards.filter(rows => kdsCardStatus(rows) === col.status);
      // Kolon ortalama süresi = kolondaki kartların yaşlarının ortalaması (dk) — WPF KdsWindowV2 ColXAvg karşılığı.
      const colAvg = colCards.length
        ? Math.round(colCards.reduce((s, rows) => s + kdsCardAge(rows), 0) / colCards.length) : 0;
      const column = el('<div class="kds-col ' + col.tone + '">' +
        '<div class="kds-col-accent"></div>' +
        '<div class="kds-col-h"><span class="kch-name">' + col.label + '</span>' +
          '<span class="kch-avg">⏱ ' + colAvg + ' dk</span>' +
          '<span class="kcount">' + colCards.length + '</span></div>' +
        '<div class="kds-col-body"></div></div>');
      const cbody = column.querySelector('.kds-col-body');
      if (!colCards.length) cbody.appendChild(el('<div class="kempty">—</div>'));
      else colCards
        .sort((a, b) => kdsCardAge(b) - kdsCardAge(a))
        .forEach(rows => cbody.appendChild(kdsCard(rows, col)));
      board.appendChild(column);
    });
  }

  // Kartın yaşı = en eski aktif (yoksa herhangi) satırın süresi — sıralama için.
  function kdsCardAge(rows) {
    const act = rows.filter(r => [1, 5].includes(num(r.lineStatus)));
    const pool = act.length ? act : rows;
    return Math.max(0, ...pool.map(r => kmins(r.sentAt)));
  }

  const KDS_TYPE_CLS = { 'MASA': 'masa', 'GEL-AL': 'gelal', 'PAKET': 'paket', 'ONLINE': 'online', 'KIOSK': 'kiosk' };

  // rows: aynı siparişe ait KdsLine[]. col: kartın bulunduğu kolon (toplu buton aksiyonu için).
  function kdsCard(rows, col) {
    const head = rows[0];
    const age = Math.floor(kdsCardAge(rows));
    const cardCrit = rows.some(kdsIsCritical);
    const cardLate = !cardCrit && rows.some(r => [1, 5].includes(num(r.lineStatus)) && kmins(r.sentAt) >= 15);
    const cooling = rows.filter(kdsIsCooling);

    const typeLbl = head.typeLabel || (head.isNonTable ? 'PAKET' : 'MASA');
    const typeCls = KDS_TYPE_CLS[typeLbl] || 'masa';

    const card = el('<div class="kcard' + (cardCrit ? ' crit' : cardLate ? ' late' : '') + '">' +
      '<div class="kcard-top">' +
        '<b>' + esc(head.tableCodes || head.orderCode || '-') + '</b>' +
        '<span class="ktype kt-' + typeCls + '">' + esc(typeLbl) + '</span>' +
        '<span class="ktimer">⏱ ' + age + 'dk</span>' +
      '</div>' +
      (head.zoneName || head.waiterName ? '<div class="ksub">' + esc(head.zoneName || '') +
        (head.waiterName ? ' · ' + esc(head.waiterName) : '') + '</div>' : '') +
      '<div class="klines"></div>' +
      (cooling.length ? '<div class="kcool">🔔 Serviste ' +
        esc(cooling[0].elapsedText || (Math.floor(kmins(cooling[0].sentAt)) + 'dk')) + ' — alın, soğuyor</div>' : '') +
    '</div>');

    const host = card.querySelector('.klines');
    rows.forEach(it => host.appendChild(kdsLineRow(it)));

    // Kart-altı toplu buton: kartın bulunduğu kolon aşamasındaki TÜM satırları ilerlet (mevcut davranış).
    if (col.act) {
      const targets = rows.filter(r => num(r.lineStatus) === col.status);
      const btn = el('<button class="btn btn-primary kact">' + col.act.lbl +
        (targets.length > 1 ? ' (' + targets.length + ')' : '') + '</button>');
      btn.addEventListener('click', (e) => busy(e.target, async () => {
        for (const t of targets) await col.act.fn(t.lineId);
        await reloadKds();
      }));
      card.appendChild(btn);
    }
    return card;
  }

  // Tek satır: tıklanınca o satırı bir aşama ilerletir (lineStatus -> KDS_STEP).
  function kdsLineRow(it) {
    const st = num(it.lineStatus);
    const step = KDS_STEP[st];
    const lineCrit = kdsIsCritical(it);
    const lineCool = kdsIsCooling(it);
    const stCls = st === 1 ? 'ls-pending' : st === 5 ? 'ls-prep' : st === 2 ? 'ls-ready' : 'ls-done';
    const children = (it.children || []).map(c => '<div class="kchild">• ' + esc(c.productName) +
      (num(c.qty) > 1 ? ' x' + num(c.qty) : '') + '</div>').join('');
    const row = el('<div class="kline ' + stCls + (step ? ' tappable' : '') +
      (lineCrit ? ' lc' : '') + '"' + (step ? ' title="Bir aşama ilerlet"' : '') + '>' +
      '<div class="kline-main">' +
        '<div class="kprod"><span class="kq">' + num(it.qty).toLocaleString('tr-TR') + '×</span> ' +
          esc(it.productName || '') +
          (it.seatNo > 0 ? ' <small>' + it.seatNo + '. Kişi</small>' : '') + '</div>' +
        children +
        (kdsBadges(it) ? '<div class="kbadges">' + kdsBadges(it) + '</div>' : '') +
        (it.kitchenNote ? '<div class="knote">📝 ' + esc(it.kitchenNote) + '</div>' : '') +
        (lineCool ? '<div class="kcool inline">🔔 soğuyor</div>' : '') +
      '</div>' +
      (step ? '<button class="kline-act" title="' + esc(step.lbl) + '">' + esc(step.lbl) + ' ›</button>' :
        '<span class="kline-done">✓</span>') +
    '</div>');
    if (step) {
      const run = (e) => { e.stopPropagation(); busy(e.currentTarget.tagName === 'BUTTON' ? e.currentTarget : null,
        async () => { await step.fn(it.lineId); await reloadKds(); }); };
      const btn = row.querySelector('.kline-act');
      if (btn) btn.addEventListener('click', run);
      // Satıra (butona değil) tıklayınca da ilerlet.
      row.addEventListener('click', (e) => { if (e.target.closest('.kline-act')) return; run(e); });
    }
    return row;
  }

  // ================= KASİYER =================
  const cashierState = { unpaid: [], filter: 'all', search: '' };

  async function renderCashier() {
    stopPolling();
    $app.innerHTML = '';
    const s = App.session;
    const shell = el('<div class="shell">' +
      '<header class="app-header">' +
        '<div class="brand"><div class="logo">💳</div><div class="titles"><b>Kasiyer — Ödeme</b>' +
          '<div class="meta-sub">' + esc(s.branchName || s.companyName || '') + '</div></div></div>' +
        '<div class="sp"></div>' +
        (canRefund() ? '<button class="hdr-btn" id="csh-refund" title="İade">↩</button>' : '') +
        (canPay() || isManager() ? '<button class="hdr-btn" id="csh-shift" title="Vardiya Kapanışı">🧾</button>' : '') +
      '</header>' +
      '<div class="svc-alert" id="svc-alert" title="Garson çağrıları"></div>' +
      '<div class="csh-search"><span class="si">🔍</span><input id="csh-q" placeholder="Masa / garson ara..." /></div>' +
      '<div class="chips" id="csh-chips"></div>' +
      '<main class="content" id="csh-list"></main>' +
    '</div>');
    $app.appendChild(shell);
    shell.querySelector('.app-header').appendChild(headerButtons());
    const cshShiftBtn = shell.querySelector('#csh-shift');
    if (cshShiftBtn) cshShiftBtn.addEventListener('click', openShiftScreen);
    const cshRefundBtn = shell.querySelector('#csh-refund');
    if (cshRefundBtn) cshRefundBtn.addEventListener('click', openRefundBrowser);
    const q = shell.querySelector('#csh-q');
    q.value = cashierState.search;
    q.addEventListener('input', () => { cashierState.search = q.value; drawCashier(); });

    shell.querySelector('#csh-list').appendChild(loading());
    armAudioOnce();
    await Realtime.start();
    bindRealtimeOnce();
    App.poll = setInterval(() => { reloadCashier(); pollServiceAlert(); }, Realtime.connected ? 15000 : 8000);
    await reloadCashier();
    pollServiceAlert();
  }

  async function reloadCashier() {
    if (App.mode !== 'cashier') return;
    const list = document.getElementById('csh-list');
    if (!list) return;
    try {
      cashierState.unpaid = await API.unpaid() || [];
      drawCashier();
    } catch (e) { list.innerHTML = ''; list.appendChild(emptyState('⚠️', e.message)); }
  }

  function cashierMatch(u) {
    const f = cashierState.filter;
    if (f === 'bill' && !u.hasBillRequest) return false;
    if (f === 'partial' && !u.isPartial) return false;
    if (f === 'nontable' && !u.isNonTable) return false;
    const q = cashierState.search.trim().toLocaleLowerCase('tr');
    if (q) {
      const hay = ((u.tableCodes || '') + ' ' + (u.deliveryName || '') + ' ' + (u.orderCode || '') + ' ' +
        (u.channelText || '') + ' ' + (u.waiterName || '') + ' ' + (u.zoneName || '')).toLocaleLowerCase('tr');
      if (!hay.includes(q)) return false;
    }
    return true;
  }

  function drawCashier() {
    const chips = document.getElementById('csh-chips');
    const list = document.getElementById('csh-list');
    if (!chips || !list) return;
    const all = cashierState.unpaid;
    const defs = [
      { id: 'all', label: 'Tümü', n: all.length },
      { id: 'nontable', label: '📦 Paket/Self-Servis', n: all.filter(u => u.isNonTable).length },
      { id: 'bill', label: 'Hesap İstiyor', n: all.filter(u => u.hasBillRequest).length },
      { id: 'partial', label: 'Kısmi Ödendi', n: all.filter(u => u.isPartial).length }
    ];
    chips.innerHTML = '';
    defs.forEach(d => {
      const c = el('<button class="chip' + (cashierState.filter === d.id ? ' on' : '') + '">' + d.label +
        '<span class="cnt">' + d.n + '</span></button>');
      c.addEventListener('click', () => { cashierState.filter = d.id; drawCashier(); });
      chips.appendChild(c);
    });

    const items = all.filter(cashierMatch);
    list.innerHTML = '';
    if (!items.length) { list.appendChild(emptyState('💳', 'Ödeme bekleyen masa yok')); return; }
    const grid = el('<div class="csh-grid"></div>');
    items.forEach(u => grid.appendChild(cashierCard(u)));
    list.appendChild(grid);
  }

  function cashierCard(u) {
    const rem = num(u.remaining != null ? u.remaining : (num(u.total) - num(u.paid)));
    const nonTable = !!u.isNonTable;
    const label = u.displayLabel || u.tableCodes || u.deliveryName || u.orderCode || '-';
    const card = el('<div class="csh-card' + (u.hasBillRequest ? ' bill' : '') + (nonTable ? ' nontable' : '') + '">' +
      '<div class="csh-top"><div><b>' + esc(label) + '</b>' +
        (nonTable ? ' <span class="csh-chan">📦 ' + esc(u.channelText || 'Paket') + '</span>' : '') +
        '<div class="sub">' + esc(nonTable ? ('Adisyon ' + (u.orderCode || '')) : (u.zoneName || '')) +
          (u.waiterName ? ' · ' + esc(u.waiterName) : '') + '</div></div>' +
        '<span class="csh-stat">' + esc(u.statusText || '') + '</span></div>' +
      '<div class="csh-meta">' +
        (u.coverCount ? '<span>👥 ' + u.coverCount + '</span>' : '') +
        (u.openedAt ? '<span>⏱ ' + esc(elapsed(u.openedAt)) + '</span>' : '') +
        (u.hasBillRequest ? '<span class="bilr">🧾 Hesap</span>' : '') + '</div>' +
      '<div class="csh-amt"><div><small>Toplam</small><b>' + money(u.total) + '</b></div>' +
        (num(u.paid) > 0 ? '<div><small>Ödenen</small><b>' + money(u.paid) + '</b></div>' : '') +
        '<div class="rem"><small>Kalan</small><b>' + money(rem) + '</b></div></div>' +
      '<button class="btn btn-primary btn-block">Ödeme Al</button>' +
    '</div>');
    card.querySelector('button').addEventListener('click', () => openCashierPayment(u, rem));
    return card;
  }

  async function openCashierPayment(u, rem) {
    if (num(rem) <= 0) { toast('Ödenecek tutar yok'); return; }   // 0 TL settle + boş fiş engeli
    const ctx = {};
    const top = el('<div class="csh-pay">' +
      ((u.allergyNote) ? '<div class="alergi-banner">⚠ ALERJİ: ' + esc(u.allergyNote) + '</div>' : '') +
      '<button class="btn btn-block" id="csh-bill" style="margin-bottom:8px" disabled>🖨 Hesap Fişi</button>' +
      '<div class="csh-adisyon" id="csh-ad"><div class="spin"></div></div>' +
    '</div>');
    // SABİT referanslı dizi: async yüklenen satırlar buraya push edilir. Hem fiş butonu hem ödeme fişi
    // (buildPaymentBody opts.lines) AYNI diziyi görür → referans kopmaz, ödeme fişine ürünler girer.
    const billLines = [];   // adisyon satırları yüklenince fiş butonu aktifleşir
    const label = u.displayLabel || u.tableCodes || u.deliveryName || u.orderCode || 'Adisyon';
    top.querySelector('#csh-bill').addEventListener('click', () => printBillReceipt(billLines, {
      tableName: label, waiterName: u.waiterName, coverCount: u.coverCount
    }));
    const payBody = buildPaymentBody({
      orderId: u.orderId, total: rem, coverCount: u.coverCount, lines: billLines,
      onPaid: () => { if (ctx.close) ctx.close(); toast('Ödeme alındı, adisyon kapandı', 'ok'); reloadCashier(); }
    });
    top.appendChild(payBody);
    const m = modal(label + ' — ' + money(rem), top);
    ctx.close = m.close;

    // adisyon satırlarını yükle (görsel; ödeme yine remaining üzerinden)
    try {
      const lines = await API.linesByOrder(u.orderId) || [];
      billLines.push(...lines);
      const billBtn = top.querySelector('#csh-bill'); if (billBtn) billBtn.disabled = false;
      const vis = lines.filter(l => !l.isDeleted && num(l.lineStatus) !== 4 && !l.parentLineId);
      const host = top.querySelector('#csh-ad');
      host.innerHTML = '';
      if (!vis.length) { host.appendChild(el('<div class="muted" style="padding:6px">Satır bulunamadı</div>')); }
      else vis.forEach(l => {
        const tot = num(l.qty) * num(l.unitPrice) - num(l.discountAmount);
        // Not: OrderLine'da satır-bazlı isVip alanı yok; VIP satır pill'i kaldırıldı (ölü UI).
        host.appendChild(el('<div class="csh-line"><span class="q">' + num(l.qty).toLocaleString('tr-TR') + '×</span>' +
          '<span class="nm">' + esc(l.productName || '') + '</span>' +
          '<span class="pr">' + money(l.isTreat ? 0 : tot) + '</span></div>'));
      });
    } catch { const host = top.querySelector('#csh-ad'); if (host) host.innerHTML = ''; }
  }

  // ================= İADE (POS satır iadesi) =================
  // WPF IadeWindow + RefundDialog karşılığı. Giriş: Kasiyer ekranı ↩ butonu (canRefund gated).
  // NOTLAR:
  //  - Süpervizör step-up onayı ŞİMDİLİK ATLANDI — /refund ucu zaten rol-korumalı (SysAdmin/Patron/İade/Kasiyer).
  //  - İade kapsamı iki kaynaktan gelir: (a) hâlâ açık/kısmi ödenmiş adisyonların settledClFicheId dolu satırları
  //    (unpaid), (b) BUGÜN tam ödenip KAPANAN adisyonlar — artık `API.closedOrders()` ucuyla listelenir.
  async function openRefundBrowser() {
    if (!canRefund()) { toast('Bu işlem için yetkiniz yok.', 'err'); return; }
    const body = el('<div class="refund-browser"><div class="spin"></div></div>');
    const m = modal('↩ İade — Adisyon Seç', body);
    let openList, closedList;
    try {
      const [ul, cl] = await Promise.all([API.unpaid().catch(() => []), API.closedOrders().catch(() => [])]);
      openList = ul || []; closedList = cl || [];
    } catch (e) { body.innerHTML = ''; body.appendChild(emptyState('⚠️', e.message)); return; }
    // Açık/kısmi: settle olmuş satır barındırabilecekler (paid>0 / kısmi). Kapanmış: bugün tam ödenip kapananlar.
    const openEntries = openList.filter(u => num(u.paid) > 0 || u.isPartial).map(u => ({
      orderId: u.orderId, label: u.tableCodes || u.orderCode || '-',
      sub: (u.zoneName || '') + (num(u.paid) > 0 ? ' · Ödenen ' + money(u.paid) : '')
    }));
    const closedEntries = (closedList || []).map(o => ({
      orderId: o.orderId || o.id, label: o.tableCodes || o.orderCode || o.code || ('#' + (o.orderId || o.id)),
      sub: 'Kapandı' + (o.total != null ? ' · ' + money(o.total) : '')
    }));
    body.innerHTML = '';
    if (!openEntries.length && !closedEntries.length) {
      body.appendChild(emptyState('↩', 'İade edilebilir (ödenmiş/kapanmış) adisyon bulunamadı')); return;
    }
    function section(title, entries) {
      if (!entries.length) return;
      body.appendChild(el('<div class="rfnd-head" style="margin:6px 0 8px"><b>' + esc(title) + '</b></div>'));
      const grid = el('<div class="pick-grid"></div>');
      entries.forEach(e => {
        const p = el('<button class="pick">' + esc(e.label) + '<small>' + esc(e.sub) + '</small></button>');
        p.addEventListener('click', () => { m.close(); openRefundForOrder({ orderId: e.orderId, tableCodes: e.label }); });
        grid.appendChild(p);
      });
      body.appendChild(grid);
    }
    section('Açık / Kısmi Ödenmiş', openEntries);
    section('Bugün Kapanan', closedEntries);
  }

  // Seçili adisyonun ödenmiş (settledClFicheId dolu) satırlarını listele → satır seç.
  async function openRefundForOrder(u) {
    const body = el('<div class="refund-lines"><div class="spin"></div></div>');
    const m = modal('↩ İade — ' + esc(u.tableCodes || u.orderCode || 'Adisyon'), body);
    let lines;
    try { lines = await API.linesByOrder(u.orderId) || []; }
    catch (e) { body.innerHTML = ''; body.appendChild(emptyState('⚠️', e.message)); return; }
    const paidLines = lines.filter(l => !l.isDeleted && l.settledClFicheId && num(l.lineStatus) !== 4 && !l.parentLineId);
    body.innerHTML = '';
    if (!paidLines.length) { body.appendChild(emptyState('↩', 'Bu adisyonda ödenmiş (iade edilebilir) satır yok')); return; }
    const listEl = el('<div class="rfnd-list"></div>');
    paidLines.forEach(l => {
      const tot = num(l.qty) * num(l.unitPrice) - num(l.discountAmount);
      const row = el('<div class="rfnd-row"><span class="q">' + num(l.qty).toLocaleString('tr-TR') + '×</span>' +
        '<span class="nm">' + esc(l.productName || '') +
          (l.seatNo > 0 ? ' <small>' + l.seatNo + '. Kişi</small>' : '') + '</span>' +
        '<span class="pr">' + money(l.isTreat ? 0 : tot) + '</span>' +
        '<button class="btn btn-sm" data-rf>↩ İade</button></div>');
      row.querySelector('[data-rf]').addEventListener('click', () => { m.close(); openRefundDialog(l); });
      listEl.appendChild(row);
    });
    body.appendChild(listEl);
  }

  // RefundDialog: iade miktarı (max=satır adedi) + Stoğa İade/Fire + iade ödeme türü (+enstrüman).
  function openRefundDialog(line) {
    // Kalan iade edilebilir miktar = satır adedi − daha önce iade edilen. Kısmen iade edilmiş satırda
    // varsayılan/max artık kalan miktardır (aşımı reddetmesin, tam satır adedi göstermesin).
    const maxQty = Math.max(0, num(line.qty) - num(line.refundedQty));
    const unit = num(line.unitPrice);
    const disc = num(line.discountAmount);
    const state = { returnToStock: true, kind: 0, instrumentId: null };
    // PaymentKind: Nakit=0, KrediKarti=1, YemekCeki=2, HediyeCeki=10.
    const REFUND_KINDS = [
      { k: 0, n: 'Nakit', i: '💵' }, { k: 1, n: 'Kart', i: '💳' },
      { k: 2, n: 'Yemek Ç.', i: '🍽️' }, { k: 10, n: 'Hediye Ç.', i: '🎁' }
    ];

    const body = el('<div class="refund-dialog">' +
      '<div class="rfnd-head"><b>' + esc(line.productName || 'Ürün') + '</b>' +
        '<small>Kalan iade edilebilir: ' + maxQty.toLocaleString('tr-TR') + ' · Birim ' + money(unit) + '</small></div>' +
      '<div class="field"><label>İade Miktarı (en çok ' + maxQty.toLocaleString('tr-TR') + ')</label>' +
        '<input id="rf-qty" type="number" inputmode="decimal" step="1" min="0" max="' + maxQty + '" value="' + maxQty + '" /></div>' +
      '<div class="field"><label>Stok</label><div class="toggle-row">' +
        '<button class="tg on" id="rf-stock">↩️ Stoğa İade</button>' +
        '<button class="tg" id="rf-waste">🔥 Fire</button></div></div>' +
      '<div class="field"><label>İade Ödeme Türü</label><div class="pay-methods rfnd-kinds" id="rf-kinds"></div></div>' +
      '<div class="pcfg" id="rf-instr-wrap" style="display:none;margin-bottom:10px"><label class="pcfg-l">Hesap / Enstrüman</label>' +
        '<select class="pcfg-sel" id="rf-instr"></select></div>' +
      '<div class="pay-sum tot"><span>İade Tutarı</span><span class="rem" id="rf-amt">' + money(maxQty * unit - disc) + '</span></div>' +
      '<button class="btn btn-primary btn-block mt8" id="rf-go">İadeyi Onayla</button>' +
    '</div>');
    const m = modal('↩ İade', body);
    const qtyInp = body.querySelector('#rf-qty');
    const amtEl = body.querySelector('#rf-amt');
    // Oransal iade tutarı: miktar oranınca birim+indirim.
    function refAmt() {
      const q = Math.min(Math.max(0, num(qtyInp.value)), maxQty);
      const ratio = maxQty > 0 ? q / maxQty : 0;
      return q * unit - disc * ratio;
    }
    function refreshAmt() { amtEl.textContent = money(refAmt()); }
    qtyInp.addEventListener('input', refreshAmt);

    const stockBtn = body.querySelector('#rf-stock'), wasteBtn = body.querySelector('#rf-waste');
    stockBtn.addEventListener('click', () => { state.returnToStock = true; stockBtn.classList.add('on'); wasteBtn.classList.remove('on'); });
    wasteBtn.addEventListener('click', () => { state.returnToStock = false; wasteBtn.classList.add('on'); stockBtn.classList.remove('on'); });

    const kindsHost = body.querySelector('#rf-kinds');
    const instrWrap = body.querySelector('#rf-instr-wrap');
    const instrSel = body.querySelector('#rf-instr');
    // Seçili türe göre enstrüman/hesap yükle (Nakit=kasa, Kart/Yemek/Hediye=ilgili hesap). Boşsa gizle → instrumentId null.
    function loadInstruments(kind) {
      state.instrumentId = null;
      instrSel.innerHTML = '<option value="">Yükleniyor…</option>';
      instrWrap.style.display = '';
      API.instruments(kind).then(l => {
        l = l || [];
        if (!l.length) { instrWrap.style.display = 'none'; state.instrumentId = null; return; }
        instrSel.innerHTML = '';
        l.forEach((it, i) => {
          const o = el('<option value="' + it.id + '">' + esc(it.name || it.title || ('#' + it.id)) + '</option>');
          if (i === 0) o.selected = true;
          instrSel.appendChild(o);
        });
        state.instrumentId = l[0].id;
      }).catch(() => { instrWrap.style.display = 'none'; state.instrumentId = null; });
    }
    instrSel.addEventListener('change', () => { state.instrumentId = instrSel.value ? Number(instrSel.value) : null; });
    REFUND_KINDS.forEach(rk => {
      const b = el('<button class="pay-m' + (rk.k === state.kind ? ' on' : '') + '"><span class="mi">' + rk.i + '</span>' + esc(rk.n) + '</button>');
      b.addEventListener('click', () => {
        state.kind = rk.k;
        [...kindsHost.children].forEach((x, i) => x.classList.toggle('on', REFUND_KINDS[i].k === rk.k));
        loadInstruments(rk.k);
      });
      kindsHost.appendChild(b);
    });
    loadInstruments(0);   // Nakit varsayılan — kasa enstrümanı

    body.querySelector('#rf-go').addEventListener('click', (e) => busy(e.currentTarget, async () => {
      const qty = num(qtyInp.value);
      if (qty <= 0) { toast('İade miktarı girin'); return; }
      if (qty > maxQty + 0.0001) { toast('İade miktarı satır adedini aşamaz'); return; }
      // Süpervizör step-up onayı ATLANDI — /refund ucu zaten rol-korumalı.
      const amount = refAmt();
      const fiche = await API.refund(line.id, {
        refundQty: qty, returnToStock: state.returnToStock, refundKind: state.kind, instrumentId: state.instrumentId
      });
      toast('İade tamamlandı', 'ok');
      m.close();
      if (App.mode === 'cashier') reloadCashier();
      offerRefundPrint(fiche, line, qty, state.kind, amount);
    }));
  }

  // İade başarılıysa "İade Fişi Yazdır" seçeneği (printReceipt ile negatif tutar dökümü).
  function offerRefundPrint(fiche, line, qty, kind, amount) {
    fiche = fiche || {};
    const rk = PAY_KINDS.find(x => x.k === kind) || { n: 'İade', i: '' };
    const grand = fiche.total != null ? Math.abs(num(fiche.total)) : num(amount);
    const body = el('<div style="text-align:center">' +
      '<div style="font-size:40px;margin:2px 0 6px">↩️</div>' +
      '<p style="margin-bottom:14px">İade tamamlandı.' +
        (fiche.code ? '<br><small style="opacity:.7">Fiş No: ' + esc(fiche.code) + '</small>' : '') + '</p>' +
      '<button class="btn btn-primary btn-block" id="rp-print">🖨 İade Fişi Yazdır</button>' +
      '<button class="btn btn-block mt8" id="rp-close">Kapat</button>' +
    '</div>');
    const m = modal('İade Tamamlandı', body);
    body.querySelector('#rp-print').addEventListener('click', () => printReceipt({
      heading: 'İADE FİŞİ',
      dateStr: fiche.ficheDate ? new Date(fiche.ficheDate).toLocaleString('tr-TR') : new Date().toLocaleString('tr-TR'),
      meta: [['Fiş No', fiche.code], ['İade Türü', (rk.i ? rk.i + ' ' : '') + rk.n]],
      items: [{ qty: num(qty).toLocaleString('tr-TR'), name: line.productName || '', amountText: '-' + money(grand) }],
      totals: [{ label: 'İADE TOPLAM', valueText: '-' + money(grand), strong: true }],
      footer: 'İade belgesi'
    }));
    body.querySelector('#rp-close').addEventListener('click', () => m.close());
  }

  // ================= PAKET / GEL-AL (WPF PaketWindow + PaketNewWindow karşılığı) =================
  // Garson kabuğundaki 📦 butonundan açılır. Monitör (Gelen/Giden) + yeni paket + durum/kurye + ödeme.
  const PKG_PLATFORMS = [
    { v: 0, n: 'Telefon' }, { v: 1, n: 'Gel-Al' }, { v: 2, n: 'Trendyol' },
    { v: 3, n: 'Getir' }, { v: 4, n: 'Yemeksepeti' }, { v: 5, n: 'Migros' }, { v: 6, n: 'Diğer' }
  ];
  // deliveryStatus: 0 Alındı · 1 Hazırlanıyor · 2 Yola Çıktı · 3 Teslim · 4 İptal
  const PKG_STATUS = [
    { s: 0, n: 'Alındı', c: 'ps-new' }, { s: 1, n: 'Hazırlanıyor', c: 'ps-prep' },
    { s: 2, n: 'Yola Çıktı', c: 'ps-road' }, { s: 3, n: 'Teslim', c: 'ps-done' }, { s: 4, n: 'İptal', c: 'ps-cancel' }
  ];
  function pkgPlatformName(v) { const p = PKG_PLATFORMS.find(x => x.v === num(v)); return p ? p.n : ''; }
  function pkgStatusDef(s) { return PKG_STATUS.find(x => x.s === num(s)) || PKG_STATUS[0]; }
  function pkgTypeName(t) { return num(t) === 2 ? 'Gel-Al' : 'Paket'; }

  const pkgState = { tab: 'incoming', items: [] };

  async function renderPackages() {
    stopPolling();
    $app.innerHTML = '';
    const s = App.session;
    const shell = el('<div class="shell">' +
      '<header class="app-header">' +
        '<button class="hdr-btn" id="pkg-back" title="Geri">←</button>' +
        '<div class="brand"><div class="logo">📦</div><div class="titles"><b>Paket / Gel-Al</b>' +
          '<div class="meta-sub">' + esc(s.branchName || s.companyName || '') + '</div></div></div>' +
        '<div class="sp"></div>' +
        '<button class="hdr-btn" id="pkg-new" title="Yeni Paket / Gel-Al">➕</button>' +
      '</header>' +
      '<div class="seg"><button data-pt="incoming" class="on">Gelen</button>' +
        '<button data-pt="delivered">Giden</button></div>' +
      '<main class="content" id="pkg-list"></main>' +
    '</div>');
    $app.appendChild(shell);
    shell.querySelector('.app-header').appendChild(headerButtons());
    shell.querySelector('#pkg-back').addEventListener('click', () => renderShell());
    shell.querySelector('#pkg-new').addEventListener('click', openNewPackageModal);
    shell.querySelectorAll('.seg button').forEach(b => b.addEventListener('click', () => {
      pkgState.tab = b.dataset.pt;
      shell.querySelectorAll('.seg button').forEach(x => x.classList.toggle('on', x === b));
      reloadPackages();
    }));
    shell.querySelector('#pkg-list').appendChild(loading());
    App.poll = setInterval(reloadPackages, 15000);
    await reloadPackages();
  }

  async function reloadPackages() {
    const list = document.getElementById('pkg-list');
    if (!list) return;
    try {
      pkgState.items = await API.packages(pkgState.tab === 'delivered') || [];
      drawPackages();
    } catch (e) { list.innerHTML = ''; list.appendChild(emptyState('⚠️', e.message)); }
  }

  function drawPackages() {
    const list = document.getElementById('pkg-list');
    if (!list) return;
    list.innerHTML = '';
    const items = pkgState.items || [];
    if (!items.length) {
      list.appendChild(emptyState('📦', pkgState.tab === 'delivered' ? 'Teslim edilmiş paket yok' : 'Bekleyen paket yok'));
      return;
    }
    const grid = el('<div class="csh-grid"></div>');
    items.forEach(p => grid.appendChild(packageCard(p)));
    list.appendChild(grid);
  }

  function packageCard(p) {
    const stt = pkgStatusDef(p.deliveryStatus);
    const total = p.total != null ? money(p.total) : '';
    const card = el('<div class="csh-card">' +
      '<div class="csh-top"><div><b>' + esc(p.code || p.orderCode || ('#' + p.id)) + '</b>' +
        '<div class="sub">' + esc(pkgTypeName(p.orderType)) +
          (pkgPlatformName(p.platform) ? ' · ' + esc(pkgPlatformName(p.platform)) : '') + '</div></div>' +
        '<span class="pkg-badge ' + stt.c + '">' + esc(p.deliveryStatusText || stt.n) + '</span></div>' +
      '<div class="csh-meta" style="flex-direction:column;gap:3px">' +
        (p.deliveryName ? '<span>👤 ' + esc(p.deliveryName) + '</span>' : '') +
        (p.deliveryPhone ? '<span>📞 ' + esc(p.deliveryPhone) + '</span>' : '') +
        (p.deliveryAddress ? '<span>📍 ' + esc(p.deliveryAddress) + '</span>' : '') +
        (p.courierName ? '<span>🛵 ' + esc(p.courierName) + '</span>' : '') + '</div>' +
      (total ? '<div class="csh-amt"><div class="rem"><small>Tutar</small><b>' + total + '</b></div></div>' : '') +
      '<div class="pkg-actions">' +
        '<button class="btn tact-add" data-open>🍽️ Adisyon</button>' +
        '<button class="btn" data-actions>⚙ İşlemler</button></div>' +
    '</div>');
    card.querySelector('[data-open]').addEventListener('click', () => openPackageOrderScreen(p));
    card.querySelector('[data-actions]').addEventListener('click', () => openPackageActions(p));
    return card;
  }

  // Durum / kurye / ödeme / iptal aksiyonları.
  function openPackageActions(p) {
    const stt = pkgStatusDef(p.deliveryStatus);
    const body = el('<div>' +
      '<div class="rfnd-head"><b>' + esc(p.code || ('#' + p.id)) + '</b>' +
        '<small>' + esc(pkgTypeName(p.orderType)) + ' · ' + esc(p.deliveryStatusText || stt.n) +
        (p.courierName ? ' · 🛵 ' + esc(p.courierName) : '') + '</small></div>' +
    '</div>');

    // durum ilerlet (Hazırlanıyor / Yola Çıktı / Teslim)
    const statusRow = el('<div class="toggle-row" style="flex-wrap:wrap"></div>');
    [[1, 'Hazırlanıyor'], [2, 'Yola Çıktı'], [3, 'Teslim']].forEach(([s, lbl]) => {
      const b = el('<button class="tg' + (num(p.deliveryStatus) === s ? ' on' : '') + '">' + lbl + '</button>');
      b.addEventListener('click', () => busy(b, async () => {
        await API.deliveryStatus(p.id, s);
        toast('Durum güncellendi', 'ok'); m.close(); reloadPackages();
      }));
      statusRow.appendChild(b);
    });
    body.appendChild(statusRow);

    const courierBtn = el('<button class="btn btn-block mt8">🛵 Kurye Ata</button>');
    courierBtn.addEventListener('click', () => { m.close(); openCourierPick(p); });
    body.appendChild(courierBtn);

    if (canPay()) {
      const payBtn = el('<button class="btn btn-primary btn-block mt8">💳 Öde / Hesap Fişi</button>');
      payBtn.addEventListener('click', () => { m.close(); openPackagePayment(p); });
      body.appendChild(payBtn);
    }

    const cancelBtn = el('<button class="btn btn-danger btn-block mt8">✖ İptal Et</button>');
    cancelBtn.addEventListener('click', async () => {
      if (!(await uiConfirm('Paket iptal edilsin mi?', { danger: true, okText: 'İptal Et' }))) return;
      busy(cancelBtn, async () => {
        await API.deliveryStatus(p.id, 4);
        toast('Paket iptal edildi', 'ok'); m.close(); reloadPackages();
      });
    });
    body.appendChild(cancelBtn);

    const m = modal('📦 Paket İşlemleri', body);
  }

  // Aktif kurye seçimi → assignCourier (durum sunucuda otomatik "Yola Çıktı" olur).
  async function openCourierPick(p) {
    const body = el('<div class="ops-body"><div class="spin"></div></div>');
    const m = modal('🛵 Kurye Seç', body);
    let couriers;
    try { couriers = await API.couriers() || []; }
    catch (e) { body.innerHTML = ''; body.appendChild(emptyState('⚠️', e.message)); return; }
    body.innerHTML = '';
    if (!couriers.length) { body.appendChild(emptyState('🛵', 'Aktif kurye yok')); return; }
    const grid = el('<div class="pick-grid"></div>');
    couriers.forEach(c => {
      const b = el('<button class="pick">' + esc(c.name || c.code || ('#' + c.id)) +
        (c.phone ? '<small>' + esc(c.phone) + '</small>' : '') + '</button>');
      b.addEventListener('click', () => busy(b, async () => {
        await API.assignCourier(p.id, c.id);
        toast('Kurye atandı (Yola Çıktı)', 'ok'); m.close(); reloadPackages();
      }));
      grid.appendChild(b);
    });
    body.appendChild(grid);
  }

  // Paket ödemesi — açık satırlardan tutar hesaplanır; ortak buildPaymentBody motoru + hesap fişi.
  async function openPackagePayment(p) {
    let lines;
    try { lines = await API.linesByOrder(p.id) || []; }
    catch (e) { return errToast(e); }
    const { total } = receiptItemsFromLines(lines);
    if (total <= 0) { toast('Ödenecek tutar yok'); return; }
    const ctx = {};
    const top = el('<div class="csh-pay">' +
      '<button class="btn btn-block" id="pkg-bill" style="margin-bottom:8px">🖨 Hesap Fişi</button>' +
    '</div>');
    top.querySelector('#pkg-bill').addEventListener('click', () => printBillReceipt(lines, {
      tableName: p.deliveryName || p.platformText || pkgTypeName(p.orderType)
    }));
    const payBody = buildPaymentBody({
      orderId: p.id, total, lines,
      onPaid: () => { if (ctx.close) ctx.close(); toast('Ödeme alındı', 'ok'); reloadPackages(); }
    });
    top.appendChild(payBody);
    const m = modal((p.code || 'Paket') + ' — ' + money(total), top);
    ctx.close = m.close;
  }

  // Yeni paket/gel-al — tür + platform + ad/telefon/adres → createPackage → adisyon ekranını aç.
  function openNewPackageModal() {
    const state = { type: 1, platform: 0 };
    const body = el('<div class="pkg-new">' +
      '<div class="field"><label>Tür</label><div class="toggle-row">' +
        '<button class="tg on" data-type="1">📦 Paket Servis</button>' +
        '<button class="tg" data-type="2">🛍️ Gel-Al</button></div></div>' +
      '<div class="field"><label>Platform</label><select class="pcfg-sel" id="pkg-platform"></select></div>' +
      '<div class="field"><label>Ad / Müşteri</label><input id="pkg-name" type="text" placeholder="Müşteri adı" /></div>' +
      '<div class="field"><label>Telefon</label><input id="pkg-phone" type="tel" placeholder="Telefon" /></div>' +
      '<div class="field" id="pkg-addr-field"><label>Adres <span id="pkg-addr-req" style="color:var(--danger)">*</span></label>' +
        '<textarea class="note-ta" id="pkg-address" placeholder="Teslimat adresi"></textarea></div>' +
      '<div class="auth-err" id="pkg-err"></div>' +
      '<button class="btn btn-primary btn-block" id="pkg-create">Oluştur ve Adisyon Aç</button>' +
    '</div>');

    const platSel = body.querySelector('#pkg-platform');
    PKG_PLATFORMS.forEach(pl => platSel.appendChild(el('<option value="' + pl.v + '">' + esc(pl.n) + '</option>')));
    platSel.addEventListener('change', () => { state.platform = num(platSel.value); });

    const addrReq = body.querySelector('#pkg-addr-req');
    function syncType() { addrReq.style.display = state.type === 1 ? '' : 'none'; }  // Paket'te adres zorunlu
    body.querySelectorAll('[data-type]').forEach(b => b.addEventListener('click', () => {
      state.type = num(b.dataset.type);
      body.querySelectorAll('[data-type]').forEach(x => x.classList.toggle('on', x === b));
      syncType();
    }));
    syncType();

    const m = modal('➕ Yeni Paket / Gel-Al', body);
    body.querySelector('#pkg-create').addEventListener('click', (e) => busy(e.currentTarget, async () => {
      const name = body.querySelector('#pkg-name').value.trim();
      const phone = body.querySelector('#pkg-phone').value.trim();
      const address = body.querySelector('#pkg-address').value.trim();
      const err = body.querySelector('#pkg-err');
      err.textContent = '';
      if (state.type === 1 && !address) { err.textContent = 'Paket servis için adres zorunludur.'; return; }
      const order = await API.createPackage({
        type: state.type, platform: state.platform,
        name: name || null, phone: phone || null, address: address || null
      });
      m.close();
      openPackageOrderScreen(order);
    }));
  }

  // Paket sipariş/adisyon ekranı — mevcut menü/adisyon motorunu (masa yerine paket order) yeniden kullanır.
  async function openPackageOrderScreen(order) {
    stopPolling();
    const O = orderState;
    O.table = null;
    O.isPackage = true;
    O.zoneId = null;
    O.view = 'menu';
    O.search = '';
    O.activeCat = 0;
    O.order = order;
    O.lines = [];
    O.categories = [];
    O.products = [];
    O.campaigns = [];

    $app.innerHTML = '';
    const title = order.code || order.orderCode || 'Paket';
    const sub = [pkgTypeName(order.orderType), pkgPlatformName(order.platform), order.deliveryName].filter(Boolean).join(' · ');
    const screen = el('<div class="shell">' +
      '<div class="menu-top">' +
        '<button class="hdr-btn" id="o-back">←</button>' +
        '<div class="info"><b>' + esc(title) + '</b><small>' + esc(sub) + '</small></div>' +
        '<button class="hdr-btn" id="o-note">📝</button>' +
      '</div>' +
      '<div class="seg"><button data-v="menu" class="on">Menü</button>' +
        '<button data-v="adisyon">Adisyon <span class="n" id="o-lc"></span></button></div>' +
      '<div class="content" id="o-content" style="padding:0"></div>' +
    '</div>');
    $app.appendChild(screen);

    screen.querySelector('#o-back').addEventListener('click', () => backFromPackage());
    screen.querySelector('#o-note').addEventListener('click', openNoteModal);
    screen.querySelectorAll('.seg button').forEach(b => b.addEventListener('click', () => {
      O.view = b.dataset.v;
      screen.querySelectorAll('.seg button').forEach(x => x.classList.toggle('on', x === b));
      drawOrderContent();
    }));

    const content = screen.querySelector('#o-content');
    content.appendChild(loading());
    try {
      O.lines = await reloadLines();
      const [cats, prods] = await Promise.all([API.categories(), API.products()]);
      O.categories = cats; O.products = prods;
      O.campaigns = await API.campaigns(O.order.id).catch(() => []);
      drawOrderContent();
    } catch (e) {
      content.innerHTML = '';
      content.appendChild(emptyState('⚠️', e.message));
    }
  }

  function backFromPackage() {
    const o = orderState.order;
    if (o && o.id) { try { API.deleteIfEmpty(o.id).catch(() => {}); } catch (e) {} }
    orderState.order = null; orderState.lines = []; orderState.isPackage = false;
    renderPackages();
  }

  // ================= MÜŞTERİ EKRANI (Customer Display) =================
  // POS ikinci ekranı: müşteriye o an açık adisyonun kalemlerini + toplamı gösteren SALT-OKUNUR,
  // büyük, müşteri-dostu ekran. Hiçbir işlem/ödeme YOK. WPF CustomerDisplayWindow karşılığı.
  // Masa/adisyon bağlamı location.hash'ten okunur (#mode=display&table=<id>  ya da  &order=<id>).
  const displayState = { tableId: 0, orderId: 0, timer: null };

  // POS içinden (masa adisyon ekranı) yeni sekmede müşteri ekranı aç — aynı origin/token paylaşılır.
  function openCustomerDisplayFor(tableId) {
    if (!tableId) return;
    const url = location.pathname + '#mode=display&table=' + encodeURIComponent(tableId);
    window.open(url, '_blank', 'noopener');
  }

  function readHashParams() {
    const out = {};
    try {
      new URLSearchParams((location.hash || '').replace(/^#/, '')).forEach((v, k) => { out[k] = v; });
      new URLSearchParams(location.search || '').forEach((v, k) => { if (!(k in out)) out[k] = v; });
    } catch { /* eski tarayıcı */ }
    return out;
  }

  async function renderCustomerDisplay() {
    stopPolling();
    if (displayState.timer) { clearInterval(displayState.timer); displayState.timer = null; }
    const p = readHashParams();
    displayState.tableId = num(p.table);
    displayState.orderId = num(p.order);
    const s = App.session || {};
    const company = esc(s.companyName || 'MindAeon');
    $app.innerHTML = '';
    const screen = el('<div class="cust-display">' +
      '<section class="cd-order">' +
        '<div class="cd-head"><span class="cd-hlogo">🍽️</span><span>Sipariş Özeti</span></div>' +
        '<div class="cd-list" id="cd-list" style="display:none">' +
          '<div class="cd-lines" id="cd-lines"></div>' +
          '<div class="cd-foot"><span class="cd-tlabel">TOPLAM</span><span class="cd-total" id="cd-total">' + money(0) + '</span></div>' +
        '</div>' +
        '<div class="cd-empty" id="cd-empty">' +
          '<div class="cd-eico">🍽️</div>' +
          '<div class="cd-etxt">Hoş geldiniz</div>' +
          '<div class="cd-esub">Siparişiniz hazırlandıkça burada görünecek</div>' +
        '</div>' +
      '</section>' +
      '<aside class="cd-brand">' +
        '<div class="cd-logo">🍽️</div>' +
        '<div class="cd-welcome">Hoş geldiniz</div>' +
        '<div class="cd-company">' + company + (s.branchName ? ' · ' + esc(s.branchName) : '') + '</div>' +
      '</aside>' +
    '</div>');
    $app.appendChild(screen);

    await reloadCustomerDisplay();

    // Canlı güncelleme: SignalR (TableChanged / kalem olayları) — mevcut Realtime deseni.
    // Bağlantı yoksa/kesikse kısa poll ile fallback.
    try { await Realtime.start(); bindRealtimeOnce(); } catch {}
    displayState.timer = setInterval(reloadCustomerDisplay, Realtime.connected ? 5000 : 3000);
  }

  // İlgili masanın/adisyonun güncel kalemlerini çek ve yeniden çiz (salt-okunur).
  async function reloadCustomerDisplay() {
    if (App.mode !== 'display') return;
    if (!document.getElementById('cd-list')) return;
    try {
      let data = null;
      if (displayState.tableId) {
        data = await API.orderByTable(displayState.tableId);
      } else if (displayState.orderId) {
        const lines = await API.linesByOrder(displayState.orderId);
        data = { order: { id: displayState.orderId }, lines: lines || [] };
      }
      drawCustomerDisplay(data);
    } catch { /* geçici ağ hatası: mevcut görünüm korunur */ }
  }

  function drawCustomerDisplay(data) {
    const listEl = document.getElementById('cd-list');
    const emptyEl = document.getElementById('cd-empty');
    const linesEl = document.getElementById('cd-lines');
    const totalEl = document.getElementById('cd-total');
    if (!listEl || !emptyEl || !linesEl || !totalEl) return;
    const order = data && data.order;
    // Görünür kalemler: iptal (lineStatus 4) ve alt-satırlar (parentLineId) hariç — adisyon görünümüyle birebir.
    const vis = ((data && data.lines) || []).filter(l => !l.isDeleted && num(l.lineStatus) !== 4 && !l.parentLineId);
    if (!order || vis.length === 0) {
      listEl.style.display = 'none';
      emptyEl.style.display = '';
      totalEl.textContent = money(0);
      return;
    }
    emptyEl.style.display = 'none';
    listEl.style.display = '';
    let total = 0;
    linesEl.innerHTML = vis.map(l => {
      const treat = !!l.isTreat;
      const tot = treat ? 0 : num(l.qty) * num(l.unitPrice) - num(l.discountAmount);
      total += tot;
      return '<div class="cd-line">' +
        '<span class="cd-q">' + num(l.qty).toLocaleString('tr-TR') + '×</span>' +
        '<span class="cd-nm">' + esc(l.productName || '') + (treat ? ' <em>(İKRAM)</em>' : '') + '</span>' +
        '<span class="cd-pr">' + money(tot) + '</span>' +
      '</div>';
    }).join('');
    totalEl.textContent = money(total);
  }

  // ================= KIOSK (Self-Servis) =================
  // Herkese açık müşteri terminali: müşteri menüden seçip sepete ekler, "Siparişi Ver" ile mevcut
  // Gel-Al (TakeAway) akışını YENİDEN KULLANARAK sipariş oluşturur → createPackage(type:2,platform:1)
  // → her kalem için addLine → send (mutfağa). Ödeme KASADA yapılır; kiosk ödeme almaz ve yönetime erişmez.
  // İş mantığı kopyalanmaz; hepsi mevcut API/helper'lar üzerinden gider. Fiyat SUNUCUDAN çözülür.
  const kioskState = { cart: [], categories: [], products: [], activeCat: 0, search: '', busy: false, calls: [] };

  async function renderKiosk() {
    stopPolling();
    kioskState.cart = [];
    kioskState.activeCat = 0;
    kioskState.search = '';
    kioskState.busy = false;
    $app.innerHTML = '';
    const s = App.session || {};
    const screen = el('<div class="kiosk">' +
      '<header class="kiosk-head">' +
        '<div class="kiosk-brand" id="k-logo" title="Menü">' +
          '<span class="kiosk-logo">🍽️</span>' +
          '<div class="kiosk-titles"><b>' + esc(s.companyName || 'MindAeon') + '</b>' +
            '<small>' + esc(s.branchName || '') + (s.branchName ? ' · ' : '') + 'Self-Servis Sipariş</small></div>' +
        '</div>' +
        '<div class="kiosk-head-sp"></div>' +
        '<button class="kiosk-call-btn" id="k-calls" title="Garson çağrıları"><span class="kcb-ico">🔔</span>' +
          '<span class="kcb-lbl">Çağrılar</span><span class="kcb-n">0</span></button>' +
        '<button class="kiosk-cart-pill" id="k-cart-pill"><span class="kcp-ico">🛒</span>' +
          '<span class="kcp-n" id="k-cart-n">0</span></button>' +
      '</header>' +
      '<div class="kiosk-body">' +
        '<main class="kiosk-menu">' +
          '<div class="kiosk-search"><span class="si">🔍</span><input id="k-search" placeholder="Ürün ara..." /></div>' +
          '<div class="kiosk-cats" id="k-cats"></div>' +
          '<div class="kiosk-grid" id="k-grid"></div>' +
        '</main>' +
        '<aside class="kiosk-cart" id="k-cart"></aside>' +
      '</div>' +
    '</div>');
    $app.appendChild(screen);
    armAudioOnce();   // kiosk shell'inde de ses bağlamını ilk dokunuşta aç

    // Gizli yönetici çıkışı: logoya 3 sn basılı tut → uiConfirm ile oturumu kapat (müşteri erişemez).
    bindKioskExit(screen.querySelector('#k-logo'));

    const search = screen.querySelector('#k-search');
    search.addEventListener('input', () => { kioskState.search = search.value; drawKioskGrid(); });
    screen.querySelector('#k-cart-pill').addEventListener('click', () => {
      const c = document.getElementById('k-cart'); if (c) c.scrollIntoView({ behavior: 'smooth', block: 'start' });
    });
    screen.querySelector('#k-calls').addEventListener('click', openKioskCalls);

    const grid = screen.querySelector('#k-grid');
    grid.appendChild(loading());
    try {
      const [cats, prods] = await Promise.all([API.categories(), API.products()]);
      kioskState.categories = cats || [];
      kioskState.products = prods || [];
      drawKioskCats();
      drawKioskGrid();
      renderKioskCart();
    } catch (e) {
      grid.innerHTML = '';
      grid.appendChild(emptyState('⚠️', e.message));
    }

    // Realtime + polling: kiosk şu ana dek başlatmıyordu. SignalR varsa rt:ServiceChanged,
    // yoksa polling çağrı sayacını/overlay'i güncel tutar.
    await Realtime.start();
    bindRealtimeOnce();
    refreshKioskCalls();
    App.poll = setInterval(refreshKioskCalls, Realtime.connected ? 20000 : 8000);
  }

  // ---- KIOSK: garson çağrılarını (type 0) listele + "Kapat" (completeServiceRequest) ----
  async function refreshKioskCalls() {
    try { kioskState.calls = (await API.serviceRequests(0) || []).filter(r => num(r.type) === 0); }
    catch { }
    const btn = document.getElementById('k-calls');
    if (btn) {
      const n = (kioskState.calls || []).length;
      const nEl = btn.querySelector('.kcb-n');
      if (nEl) nEl.textContent = n;
      btn.classList.toggle('has', n > 0);
    }
    drawKioskCallsList();   // overlay açıksa yenile
  }

  function openKioskCalls() {
    const body = el('<div class="k-calls-list" id="k-calls-ov"></div>');
    modal('🔔 Garson Çağrıları', body);
    drawKioskCallsList();
    refreshKioskCalls();
  }

  function drawKioskCallsList() {
    const host = document.getElementById('k-calls-ov');
    if (!host) return;
    host.innerHTML = '';
    const calls = kioskState.calls || [];
    if (!calls.length) { host.appendChild(emptyState('🔕', 'Bekleyen garson çağrısı yok')); return; }
    calls.forEach(r => {
      const row = el('<div class="k-call-row"><div class="kcr-info">' +
        '<b>' + esc(r.tableCode || r.typeText || 'Masa') + '</b>' +
        (r.elapsedText ? '<span class="kcr-t">⏱ ' + esc(r.elapsedText) + '</span>' : '') +
        (r.note ? '<div class="kcr-note">📝 ' + esc(r.note) + '</div>' : '') +
        '</div><button class="btn btn-primary" data-close-call>Kapat</button></div>');
      row.querySelector('[data-close-call]').addEventListener('click', (e) => busy(e.target, async () => {
        await API.completeServiceRequest(r.id);
        toast('Çağrı kapatıldı', 'ok');
        await refreshKioskCalls();
      }));
      host.appendChild(row);
    });
  }

  // Logoya basılı tutunca (3 sn) yönetici çıkışı — kısa dokunuşlar yok sayılır.
  function bindKioskExit(logoEl) {
    if (!logoEl) return;
    let timer = null;
    const start = () => {
      clearTimeout(timer);
      timer = setTimeout(async () => {
        if (await uiConfirm('Self-servis kiosk kapatılıp yönetici çıkışı yapılsın mı?',
          { danger: true, okText: 'Çıkış', title: 'Yönetici Çıkışı' })) {
          stopPolling(); Realtime.stop(); Store.clearAuth(); App.session = null; renderLogin();
        }
      }, 3000);
    };
    const cancel = () => { clearTimeout(timer); };
    ['mousedown', 'touchstart', 'pointerdown'].forEach(ev => logoEl.addEventListener(ev, start, { passive: true }));
    ['mouseup', 'mouseleave', 'touchend', 'touchcancel', 'pointerup', 'pointerleave'].forEach(ev => logoEl.addEventListener(ev, cancel, { passive: true }));
  }

  function drawKioskCats() {
    const host = document.getElementById('k-cats');
    if (!host) return;
    host.innerHTML = '';
    const allCats = [{ id: 0, name: 'Tümü' }].concat(kioskState.categories);
    allCats.forEach(c => {
      const hasImg = !!(c.id && c.hasImage);
      const img = hasImg
        ? '<img loading="lazy" src="' + esc(API.categoryImageUrl(c.id)) + '" onerror="this.remove()" />'
        : '';
      const b = el('<button class="kiosk-cat' + (kioskState.activeCat === c.id ? ' on' : '') + '">' +
        img + '<span>' + esc(c.name) + '</span></button>');
      b.addEventListener('click', () => { kioskState.activeCat = c.id; drawKioskCats(); drawKioskGrid(); });
      host.appendChild(b);
    });
  }

  function drawKioskGrid() {
    const grid = document.getElementById('k-grid');
    if (!grid) return;
    grid.innerHTML = '';
    const q = (kioskState.search || '').trim().toLocaleLowerCase('tr');
    let list = kioskState.products || [];
    if (kioskState.activeCat) list = list.filter(p => (p.categoryIds || []).includes(kioskState.activeCat));
    if (q) list = list.filter(p => (p.name || '').toLocaleLowerCase('tr').includes(q) || (p.code || '').toLocaleLowerCase('tr').includes(q));
    if (!list.length) { grid.appendChild(emptyState('🔍', 'Ürün bulunamadı')); return; }
    list.forEach(p => grid.appendChild(kioskProductCard(p)));
  }

  function kioskProductCard(p) {
    const stopped = !!p.isSalesStopped;
    const card = el('<div class="kiosk-pcard' + (stopped ? ' stopped' : '') + '">' +
      '<div class="kiosk-pimg">' +
        (p.hasImage
          ? '<img loading="lazy" src="' + esc(API.productImageUrl(p.id)) + '" onerror="this.parentElement.textContent=\'🍴\'" />'
          : (p.isMenu ? '🍱' : '🍴')) + '</div>' +
      '<div class="kiosk-pname">' + esc(p.name) + '</div>' +
      (p.description ? '<div class="kiosk-pdesc">' + esc(p.description) + '</div>' : '<div class="kiosk-pdesc"></div>') +
      '<div class="kiosk-prow"><span class="kiosk-pprice">' + money(p.price) + '</span>' +
        (stopped ? '<span class="kiosk-stop">Tükendi</span>' : '<span class="kiosk-add">＋ Ekle</span>') +
      '</div></div>');
    if (!stopped) card.addEventListener('click', () => kioskAddProduct(p));
    return card;
  }

  // Ürün opsiyonları varsa mevcut opsiyon seçiciyi (openOptionPicker) kullan; yoksa doğrudan sepete ekle.
  async function kioskAddProduct(p) {
    let options = null;
    try { options = await API.productOptions(p.id); } catch { options = null; }
    if (options && options.length) {
      openOptionPicker(p, options, (selected) => kioskCartAdd(p, selected));
      return;
    }
    kioskCartAdd(p, null);
  }

  function kioskCartAdd(p, selected) {
    let unitPrice = num(p.price);
    let note = null;
    if (selected && selected.length) {
      unitPrice += selected.reduce((s, o) => s + num(o.priceDelta), 0);
      note = selected.map(o => o.name).join(', ');
    }
    const key = p.id + '|' + (note || '');
    const existing = kioskState.cart.find(x => x.key === key);
    if (existing) existing.qty += 1;
    else kioskState.cart.push({ key, productId: p.id, name: p.name, unitPrice, taxRate: num(p.taxRate), qty: 1, note });
    toast(p.name + ' eklendi ✓', 'ok');
    renderKioskCart();
  }

  function kioskCartTotal() { return kioskState.cart.reduce((s, it) => s + num(it.qty) * num(it.unitPrice), 0); }
  function kioskCartCount() { return kioskState.cart.reduce((s, it) => s + num(it.qty), 0); }

  function renderKioskCart() {
    const host = document.getElementById('k-cart');
    if (!host) return;
    const cart = kioskState.cart;
    const n = kioskCartCount();
    const pill = document.getElementById('k-cart-n'); if (pill) pill.textContent = n;
    let rows;
    if (!cart.length) {
      rows = '<div class="kiosk-cart-empty"><span class="ico">🛒</span>Sepetiniz boş<br><small>Menüden ürün seçin.</small></div>';
    } else {
      rows = '<div class="kiosk-cart-list">' + cart.map((it, i) => {
        const tot = num(it.qty) * num(it.unitPrice);
        return '<div class="kiosk-cart-row" data-i="' + i + '">' +
          '<div class="kcr-info"><div class="kcr-name">' + esc(it.name) + '</div>' +
            (it.note ? '<div class="kcr-note">' + esc(it.note) + '</div>' : '') +
            '<div class="kcr-price">' + money(it.unitPrice) + '</div></div>' +
          '<div class="kcr-step"><button class="kcr-btn" data-dec="' + i + '">−</button>' +
            '<span class="kcr-q">' + num(it.qty).toLocaleString('tr-TR') + '</span>' +
            '<button class="kcr-btn" data-inc="' + i + '">+</button></div>' +
          '<div class="kcr-tot">' + money(tot) + '</div>' +
        '</div>';
      }).join('') + '</div>';
    }
    host.classList.toggle('empty', !cart.length);
    host.innerHTML =
      '<div class="kiosk-cart-head"><span>🛒 Sepetim</span><span class="kiosk-cart-badge">' + n + '</span></div>' +
      rows +
      '<div class="kiosk-cart-foot">' +
        '<div class="kiosk-cart-total"><span>TOPLAM</span><b>' + money(kioskCartTotal()) + '</b></div>' +
        '<button class="btn btn-primary kiosk-order-btn" id="k-order"' + (cart.length ? '' : ' disabled') + '>🛒 Siparişi Ver</button>' +
        '<button class="btn kiosk-clear-btn" id="k-clear"' + (cart.length ? '' : ' disabled') + '>Vazgeç / Temizle</button>' +
      '</div>';
    host.querySelectorAll('[data-inc]').forEach(b => b.addEventListener('click', () => {
      const i = num(b.dataset.inc); if (kioskState.cart[i]) { kioskState.cart[i].qty += 1; renderKioskCart(); }
    }));
    host.querySelectorAll('[data-dec]').forEach(b => b.addEventListener('click', () => {
      const i = num(b.dataset.dec); if (!kioskState.cart[i]) return;
      kioskState.cart[i].qty -= 1;
      if (kioskState.cart[i].qty <= 0) kioskState.cart.splice(i, 1);
      renderKioskCart();
    }));
    const ob = host.querySelector('#k-order'); if (ob) ob.addEventListener('click', (e) => kioskPlaceOrder(e.currentTarget));
    const cb = host.querySelector('#k-clear'); if (cb) cb.addEventListener('click', kioskClearCart);
  }

  async function kioskClearCart() {
    if (!kioskState.cart.length) return;
    if (!(await uiConfirm('Sepet temizlensin mi?', { danger: true, okText: 'Temizle' }))) return;
    kioskState.cart = [];
    renderKioskCart();
  }

  // Siparişi ver — mevcut Gel-Al (TakeAway) akışını yeniden kullan: createPackage → addLine (her kalem) → send.
  async function kioskPlaceOrder(btn) {
    if (kioskState.busy) return;
    if (!kioskState.cart.length) { toast('Sepetiniz boş'); return; }
    kioskState.busy = true;
    try {
      await busy(btn, async () => {
        // name='Self-Servis' → kasiyer ödeme ekranında masasız sipariş bu etiketle ayırt edilir (DeliveryName).
        const order = await API.createPackage({ type: 2, platform: 1, name: 'Self-Servis', phone: null, address: null });
        for (const it of kioskState.cart) {
          await API.addLine(order.id, {
            productId: it.productId, qty: it.qty, unitPrice: it.unitPrice, taxRate: it.taxRate, note: it.note, seatNo: 0
          });
        }
        await API.send(order.id);
        // Onay ekranı + fiş için sunucudan çözülmüş satırları çek (fiyat sunucudan gelir).
        let lines = [];
        try { lines = await API.linesByOrder(order.id) || []; } catch {}
        kioskState.cart = [];
        kioskShowConfirmation(order, lines);
      });
    } catch (e) { /* busy() zaten hata mesajını gösterdi */ }
    finally { kioskState.busy = false; }
  }

  // Büyük onay ekranı — sipariş kodu + "kasadan ödeyip teslim alın" + fiş (opsiyonel) + yeni sipariş.
  function kioskShowConfirmation(order, lines) {
    const code = order.code || order.orderCode || ('#' + order.id);
    const { items, total } = receiptItemsFromLines(lines || []);
    const scr = el('<div class="kiosk-confirm">' +
      '<div class="kiosk-confirm-card">' +
        '<div class="kc-check">✅</div>' +
        '<h2>Siparişiniz alındı</h2>' +
        '<div class="kc-code">No: ' + esc(code) + '</div>' +
        '<p class="kc-msg">Lütfen <b>kasadan ödeyip</b> siparişinizi teslim alın.</p>' +
        (items.length ? '<div class="kc-total">Toplam: <b>' + money(total) + '</b></div>' : '') +
        '<div class="kc-actions">' +
          (items.length ? '<button class="btn kiosk-print-btn" id="kc-print">🖨 Fiş Yazdır</button>' : '') +
          '<button class="btn btn-primary kiosk-new-btn" id="kc-new">＋ Yeni Sipariş</button>' +
        '</div>' +
      '</div>' +
    '</div>');
    $app.appendChild(scr);
    const pb = scr.querySelector('#kc-print');
    if (pb) pb.addEventListener('click', () => printReceipt({
      heading: 'SİPARİŞ FİŞİ',
      meta: [['Sipariş No', code], ['Tür', 'Self-Servis / Gel-Al']],
      items,
      totals: [{ label: 'TOPLAM', value: total, strong: true }],
      footer: 'Ödemenizi kasadan yapınız. Bu bir mali belge değildir.'
    }));
    scr.querySelector('#kc-new').addEventListener('click', () => { scr.remove(); renderKiosk(); });
  }

  // ================= PATRON =================
  const patronState = { date: null, data: null, tables: [], unpaid: [] };

  function todayStr() {
    const d = new Date();
    return d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0');
  }

  async function renderPatron() {
    stopPolling();
    if (!patronState.date) patronState.date = todayStr();
    $app.innerHTML = '';
    const s = App.session;
    const shell = el('<div class="shell">' +
      '<header class="app-header">' +
        '<div class="brand"><div class="logo">📊</div><div class="titles"><b>Patron — Özet</b>' +
          '<div class="meta-sub">' + esc(s.companyName || '') + (s.branchName ? ' · ' + esc(s.branchName) : '') + '</div></div></div>' +
        '<div class="sp"></div>' +
        (canPay() || isManager() ? '<button class="hdr-btn" id="pat-shift" title="Vardiya Kapanışı">🧾</button>' : '') +
        '<input type="date" id="pat-date" class="pat-date" value="' + esc(patronState.date) + '" />' +
      '</header>' +
      '<main class="content" id="pat-body"></main>' +
    '</div>');
    $app.appendChild(shell);
    shell.querySelector('.app-header').appendChild(headerButtons());
    const shiftBtn = shell.querySelector('#pat-shift');
    if (shiftBtn) shiftBtn.addEventListener('click', openShiftScreen);
    shell.querySelector('#pat-date').addEventListener('change', (e) => { patronState.date = e.target.value; reloadPatron(); });

    shell.querySelector('#pat-body').appendChild(loading());
    await Realtime.start();
    bindRealtimeOnce();
    App.poll = setInterval(reloadPatron, 30000);
    await reloadPatron();
  }

  async function reloadPatron() {
    if (App.mode !== 'patron') return;
    const body = document.getElementById('pat-body');
    if (!body) return;
    const branchId = App.session && App.session.branchId;
    try {
      const [data, tables, unpaid] = await Promise.all([
        API.dashboard(branchId, patronState.date),
        API.tablesLive(branchId).catch(() => []),
        API.unpaid().catch(() => [])
      ]);
      patronState.data = data; patronState.tables = tables || []; patronState.unpaid = unpaid || [];
      drawPatron();
    } catch (e) { body.innerHTML = ''; body.appendChild(emptyState('⚠️', e.message)); }
  }

  function kpi(label, value, sub) {
    return '<div class="kpi"><small>' + esc(label) + '</small><b>' + value + '</b>' +
      (sub ? '<span class="kpi-sub">' + esc(sub) + '</span>' : '') + '</div>';
  }

  function drawPatron() {
    const body = document.getElementById('pat-body');
    if (!body) return;
    const d = patronState.data || {};
    const openAmount = patronState.unpaid.reduce((s, u) => s + num(u.remaining != null ? u.remaining : num(u.total) - num(u.paid)), 0);
    const openTables = patronState.unpaid.length;

    const wrap = el('<div class="patron"></div>');

    // KPI grid
    const grid = el('<div class="kpi-grid">' +
      kpi('CİRO', money(d.salesTotal), patronState.date) +
      kpi('FİŞ ADEDİ', num(d.adisyonCount).toLocaleString('tr-TR')) +
      kpi('ORT. FİŞ', money(d.average)) +
      kpi('KASA', money(d.kasaTotal)) +
      kpi('AÇIK MASA', openTables) +
      kpi('AÇIK TUTAR', money(openAmount)) +
    '</div>');
    wrap.appendChild(grid);

    // payment distribution
    const payTot = num(d.nakit) + num(d.kart) + num(d.diger);
    const payBars = [['Nakit', d.nakit, '#17b26a'], ['Kart', d.kart, '#3b82f6'], ['Diğer', d.diger, '#f59e0b']];
    const psec = el('<div class="psec"><h4>Ödeme Dağılımı</h4><div class="bars" id="pat-pay"></div></div>');
    const ph = psec.querySelector('#pat-pay');
    payBars.forEach(([lbl, val, col]) => {
      const pct = payTot > 0 ? Math.round(num(val) / payTot * 100) : 0;
      ph.appendChild(el('<div class="bar-row"><span class="bl">' + lbl + '</span>' +
        '<div class="bt"><div class="bf" style="width:' + pct + '%;background:' + col + '"></div></div>' +
        '<span class="bv">' + money(val) + ' · %' + pct + '</span></div>'));
    });
    wrap.appendChild(psec);

    // top products
    const tops = d.topProducts || [];
    if (tops.length) {
      const tmax = Math.max.apply(null, tops.map(t => num(t.amount)).concat([1]));
      const tsec = el('<div class="psec"><h4>En Çok Satan Ürünler</h4><div class="bars" id="pat-top"></div></div>');
      const th = tsec.querySelector('#pat-top');
      tops.slice(0, 8).forEach(t => {
        const pct = Math.round(num(t.amount) / tmax * 100);
        th.appendChild(el('<div class="bar-row"><span class="bl">' + esc(t.name) + '</span>' +
          '<div class="bt"><div class="bf" style="width:' + pct + '%;background:' + (t.color || '#10b981') + '"></div></div>' +
          '<span class="bv">' + money(t.amount) + '</span></div>'));
      });
      wrap.appendChild(tsec);
    }

    // ERP mini (varsa)
    if (num(d.receivable) || num(d.payable) || num(d.bankTotal) || num(d.todayExpense)) {
      wrap.appendChild(el('<div class="kpi-grid erp">' +
        kpi('ALACAK', money(d.receivable)) + kpi('BORÇ', money(d.payable)) +
        kpi('BANKA', money(d.bankTotal)) + kpi('BUGÜN GİDER', money(d.todayExpense)) + '</div>'));
    }

    // Yükümlülükler (parasal açık takibi — dolaşımdaki hediye çeki + puan)
    wrap.appendChild(el('<div class="kpi-grid liab">' +
      kpi('HEDİYE ÇEKİ YÜKÜMLÜLÜĞÜ', money(d.giftCardLiability), d.giftCardLiabilityText || '') +
      kpi('PUAN YÜKÜMLÜLÜĞÜ', money(d.loyaltyLiability), d.loyaltyLiabilityText || '') +
    '</div>'));

    // canlı masa doluluğu (tablesLive — bölge bazlı dolu/toplam özet)
    const tl = patronState.tables || [];
    if (tl.length) {
      const occ = tl.filter(t => (t.state || t.statusText) === 'Dolu' || num(t.status) === 1);
      const byZone = {};
      tl.forEach(t => {
        const z = t.zoneName || t.zone || 'Genel';
        (byZone[z] = byZone[z] || { total: 0, occ: 0 }).total++;
        if ((t.state || t.statusText) === 'Dolu' || num(t.status) === 1) byZone[z].occ++;
      });
      const lsec = el('<div class="psec"><h4>Canlı Masa Doluluğu (' + occ.length + '/' + tl.length + ')</h4>' +
        '<div class="pat-live" id="pat-live"></div></div>');
      const lh = lsec.querySelector('#pat-live');
      Object.keys(byZone).forEach(z => {
        const v = byZone[z];
        lh.appendChild(el('<div class="pl-cell"><span class="pl-z">' + esc(z) + '</span>' +
          '<span class="pl-v">' + v.occ + '<small>/' + v.total + ' dolu</small></span></div>'));
      });
      wrap.appendChild(lsec);
    }

    // open tables list
    if (openTables) {
      const osec = el('<div class="psec"><h4>Açık Masalar (' + openTables + ')</h4><div id="pat-open"></div></div>');
      const oh = osec.querySelector('#pat-open');
      patronState.unpaid.forEach(u => {
        const rem = num(u.remaining != null ? u.remaining : num(u.total) - num(u.paid));
        oh.appendChild(el('<div class="csh-line"><span class="nm">' + esc(u.tableCodes || u.orderCode || '-') +
          (u.waiterName ? ' <small>' + esc(u.waiterName) + '</small>' : '') + '</span>' +
          '<span class="pr">' + money(rem) + '</span></div>'));
      });
      wrap.appendChild(osec);
    }

    body.innerHTML = '';
    body.appendChild(wrap);
  }

  // ================= VARDİYA KAPANIŞI (shift) =================
  // Kasiyer/yönetici kapılı (canPay() || isManager()). branch+kiosk JWT'den çözülür.
  function fmtDt(iso) {
    if (!iso) return '';
    const d = new Date(iso);
    if (isNaN(d.getTime())) return '';
    return d.toLocaleString('tr-TR', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' });
  }

  async function openShiftScreen() {
    if (!(canPay() || isManager())) { toast('Bu işlem için yetkiniz yok.', 'err'); return; }
    const body = el('<div class="shift-view"></div>');
    body.appendChild(loading());
    const m = modal('Vardiya Kapanışı', body);
    await loadShiftView(body, m);
  }

  async function loadShiftView(body, m) {
    body.innerHTML = '';
    body.appendChild(loading());
    let current;
    try {
      current = await API.shiftCurrent();
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) { m.close(); return; }
      body.innerHTML = '';
      const er = emptyState('⚠️', e.message);
      const rb = el('<button class="btn btn-block" style="margin-top:10px">Tekrar Dene</button>');
      rb.addEventListener('click', () => loadShiftView(body, m));
      body.appendChild(er); body.appendChild(rb);
      return;
    }
    body.innerHTML = '';
    if (!current || !current.id) { drawShiftOpen(body, m); return; }
    await drawShiftClose(body, m, current);
  }

  // --- Açık vardiya yok: aç formu ---
  function drawShiftOpen(body, m) {
    body.innerHTML = '';
    const wrap = el('<div class="shift-open">' +
      '<div class="empty"><span class="ico">🟢</span>Bu terminalde açık vardiya yok</div>' +
      '<div class="field"><label>Açılış Kasası (₺)</label>' +
      '<input id="sh-float" type="number" inputmode="decimal" step="0.01" min="0" placeholder="0,00" value="0" /></div>' +
      '<button class="btn btn-primary btn-block" id="sh-open">Vardiya Aç</button>' +
    '</div>');
    body.appendChild(wrap);
    const inp = wrap.querySelector('#sh-float');
    const btn = wrap.querySelector('#sh-open');
    inp.focus();
    btn.addEventListener('click', () => busy(btn, async () => {
      const v = num(inp.value);
      if (v < 0) { toast('Açılış kasası negatif olamaz.', 'err'); return; }
      await API.shiftOpen(v);
      toast('Vardiya açıldı', 'ok');
      await loadShiftView(body, m);
    }));
  }

  // --- Açık vardiya var: rapor + kapatma ---
  async function drawShiftClose(body, m, shift) {
    body.innerHTML = '';
    body.appendChild(loading());
    let rep;
    try {
      rep = await API.shiftReport(shift.id);
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) { m.close(); return; }
      body.innerHTML = '';
      body.appendChild(emptyState('⚠️', e.message));
      return;
    }
    body.innerHTML = '';
    const opening = num(shift.openingFloat);
    const expected = num(rep.expectedCash);
    const cashColl = num(rep.cashCollected);

    const wrap = el('<div class="shift-rep"></div>');

    // başlık / özet
    wrap.appendChild(el('<div class="shift-head">' +
      '<div><small>Vardiya</small><b>#' + esc(shift.id) + '</b></div>' +
      '<div><small>Açılış</small><b>' + esc(fmtDt(shift.openedAt)) + '</b></div>' +
    '</div>'));

    wrap.appendChild(el('<div class="kpi-grid">' +
      kpi('AÇILIŞ KASASI', money(opening)) +
      kpi('NAKİT TAHSİLAT', money(cashColl)) +
      kpi('TOPLAM CİRO', money(rep.total)) +
    '</div>'));

    // ödeme yöntemi kırılımı
    const pays = rep.payments || [];
    const psec = el('<div class="psec"><h4>Ödeme Yöntemi Kırılımı</h4><div id="sh-pays"></div></div>');
    const ph = psec.querySelector('#sh-pays');
    if (!pays.length) ph.appendChild(el('<div class="muted" style="padding:6px">Tahsilat yok</div>'));
    else pays.forEach(p => {
      ph.appendChild(el('<div class="csh-line"><span class="nm">' +
        esc(p.methodText || p.method || '-') + '</span>' +
        (p.count != null ? '<span class="q">' + num(p.count) + '×</span>' : '') +
        '<span class="pr">' + money(p.amount) + '</span></div>'));
    });
    wrap.appendChild(psec);

    // tahsil eden kullanıcı kırılımı
    const byUser = rep.byUser || [];
    if (byUser.length) {
      const usec = el('<div class="psec"><h4>Tahsil Eden Kullanıcı</h4><div id="sh-users"></div></div>');
      const uh = usec.querySelector('#sh-users');
      byUser.forEach(u => {
        uh.appendChild(el('<div class="csh-line"><span class="nm">' +
          esc(u.userName || u.fullName || u.user || '-') + '</span>' +
          (u.count != null ? '<span class="q">' + num(u.count) + '×</span>' : '') +
          '<span class="pr">' + money(u.amount) + '</span></div>'));
      });
      wrap.appendChild(usec);
    }

    // kapatma: beklenen nakit + sayılan + fark
    const close = el('<div class="psec shift-close"><h4>Vardiya Kapat</h4>' +
      '<div class="csh-line"><span class="nm">Beklenen Nakit</span>' +
        '<span class="pr">' + money(expected) + '</span></div>' +
      '<div class="field"><label>Sayılan Nakit (₺)</label>' +
        '<input id="sh-counted" type="number" inputmode="decimal" step="0.01" min="0" placeholder="0,00" value="' + expected.toFixed(2) + '" /></div>' +
      '<div class="shift-diff" id="sh-diff"></div>' +
      '<div class="field"><label>Not (opsiyonel)</label>' +
        '<input id="sh-note" type="text" maxlength="240" placeholder="Mutabakat notu..." /></div>' +
      '<button class="btn btn-primary btn-block" id="sh-close">Vardiya Kapat</button>' +
    '</div>');
    wrap.appendChild(close);
    body.appendChild(wrap);

    const counted = close.querySelector('#sh-counted');
    const diffEl = close.querySelector('#sh-diff');
    const note = close.querySelector('#sh-note');
    const closeBtn = close.querySelector('#sh-close');

    function renderDiff() {
      const d = num(counted.value) - expected;
      const r = Math.round(d * 100) / 100;
      let cls = 'ok', txt = 'Kasa tam';
      if (r > 0) { cls = 'plus'; txt = 'Fazla ' + money(r); }
      else if (r < 0) { cls = 'minus'; txt = 'Eksik ' + money(Math.abs(r)); }
      diffEl.className = 'shift-diff ' + cls;
      diffEl.textContent = 'Fark: ' + txt;
    }
    counted.addEventListener('input', renderDiff);
    renderDiff();

    closeBtn.addEventListener('click', () => busy(closeBtn, async () => {
      const cv = num(counted.value);
      if (cv < 0) { toast('Sayılan nakit negatif olamaz.', 'err'); return; }
      if (!(await uiConfirm('Vardiya kapatılsın mı?', { danger: true, okText: 'Kapat' }))) return;
      await API.shiftClose(shift.id, cv, note.value.trim() || null);
      toast('Vardiya kapatıldı', 'ok');
      await loadShiftView(body, m);
    }));
  }

  // ===== ÇOKLU-SEKME KİMLİK SENKRONU (#5) + BOŞTA ZAMAN AŞIMI (#6) =====
  // Aynı origin sekmeler localStorage'ı paylaşır: başka sekmede yeni SSO devri / login / logout token'ı değiştirdiğinde
  // bu sekme ESKİ kimlikle çalışmaya devam etmesin → token değişince yeniden yükle (doğru kimliği kurar / login'e düşer).
  window.addEventListener('storage', (e) => {
    if (e.key !== 'mag.token') return;
    const cur = (App.session && App.session.token) || Store.getToken();
    if (e.newValue !== cur) { try { location.reload(); } catch { location.href = location.pathname; } }
  });

  // Paylaşılan terminalde uzaklaşınca başka kullanıcının adına iş yapılmasın → boşta kalınca otomatik çıkış.
  const IDLE_MS = 20 * 60 * 1000;   // 20 dk
  let idleTimer = null;
  function armIdle() {
    if (idleTimer) clearTimeout(idleTimer);
    if (App.session) idleTimer = setTimeout(idleLogout, IDLE_MS);
  }
  function idleLogout() {
    if (!App.session) return;
    // Self-servis kiosk paylaşımlı bir terminaldir; boşta kalınca oturumu DÜŞÜRME (müşteri login ekranıyla karşılaşmasın).
    if (App.mode === 'kiosk') { armIdle(); return; }
    stopPolling(); Realtime.stop(); Store.clearAuth(); App.session = null;
    renderLogin();
    try { toast('Güvenlik için oturum zaman aşımına uğradı.', 'err'); } catch { }
  }
  ['click', 'keydown', 'touchstart', 'pointerdown'].forEach(ev =>
    document.addEventListener(ev, armIdle, { passive: true }));

  // ===== OFFLINE KUYRUK BOŞALTMA (online olunca) =====
  // Bağlantı gelince outbox'taki bekleyen sipariş/ödeme mutasyonlarını gönder. Öncelik Background Sync'te
  // (SW arka planda, uygulama kapalıyken bile dener); desteklenmiyorsa manuel flush (API.flushOutbox) fallback.
  async function triggerOutboxSync() {
    // Kuyrukta iş varsa "senkronize ediliyor…" görsel geri-bildirim (online olunca).
    const n = await outboxCount();
    if (n > 0) toast('Bağlantı geldi, senkronize ediliyor… (' + n + ')', 'ok');
    updateConnBadge();
    let bgSync = false;
    try {
      if ('serviceWorker' in navigator && 'SyncManager' in window) {
        bgSync = true;
        navigator.serviceWorker.ready.then(reg => reg.sync.register('outbox-sync').catch(() => manualFlush()))
          .catch(() => manualFlush());
      }
    } catch { bgSync = false; }
    if (!bgSync) manualFlush();
  }
  function manualFlush() {
    try { if (window.MAG && MAG.API && MAG.API.flushOutbox) MAG.API.flushOutbox().catch(() => {}); } catch {}
  }
  // Outbox flush sonucu bildirimi (api.js emit + service-worker postMessage aynı payload'ı kullanır).
  // SAHTE BAŞARI OLMASIN: yalnız gerçekten gönderilen (2xx) kayıtlar "gönderildi" sayılır;
  // kalıcı başarısızlıklar (4xx) ayrı uyarılır, oturum blokajı (401/403) ayrı uyarılır.
  let _lastOutboxMsg = 0;
  function onOutboxFlushed(d) {
    if (!d) { updateConnBadge(); return; }
    if (d.sent) toast(d.sent + ' işlem gönderildi ✓', 'ok');
    if (d.failed) toast(d.failed + ' işlem gönderilemedi (kalıcı hata).', 'err');
    if (d.authBlocked) {
      const n = (typeof d.remaining === 'number') ? d.remaining : null;
      toast('Oturum yenilenmeli' + (n ? (', ' + n + ' bekleyen işlem var') : '') +
        '. Lütfen yeniden giriş yapın.', 'err');
    }
    // RECONCILE: açık adisyon ekranı varsa (O.order mevcut + #o-content DOM'da) ve gerçekten kayıt gönderildiyse,
    // optimistik "⏳ Bekliyor" satırlarını sunucudaki gerçek satırlarla değiştir (reloadLines + yeniden çiz).
    if (d.sent > 0 && orderState && orderState.order && document.getElementById('o-content')) {
      reloadLines().then(lines => {
        orderState.lines = lines || [];
        refreshLineCountBadge();
        updateBottomBarTotal();
        drawOrderContent();
        renderLiveCart(true);
      }).catch(() => {});
    }
    updateConnBadge();
  }
  on('outbox:flushed', onOutboxFlushed);
  // Background Sync yolu (Service Worker) aynı olayı postMessage ile iletir.
  if ('serviceWorker' in navigator) {
    try {
      navigator.serviceWorker.addEventListener('message', (e) => {
        const d = e && e.data;
        if (d && d.type === 'outbox:flushed') {
          // SW payload'ında remaining yoksa canlı sayaçtan tamamla.
          if (typeof d.remaining !== 'number' && d.authBlocked) {
            outboxCount().then(n => onOutboxFlushed(Object.assign({}, d, { remaining: n })));
          } else {
            onOutboxFlushed(d);
          }
        }
      });
    } catch (e) {}
  }
  window.addEventListener('online', triggerOutboxSync);

  // ================= DONANIM YAZICI (FAZ 4d) =================
  // Açılışta: daha önce yetki verilmiş ESC/POS portunu sessizce yeniden bağla (kullanıcı etkileşimi
  // gerektirmez). Bağlantı durumu değişince açık header butonunu güncelle. Desteklenmeyen tarayıcıda no-op.
  if (window.Printer) {
    try {
      Printer.onChange(() => { const b = document.getElementById('h-printer'); if (b) syncPrinterBtn(b); });
      Printer.tryReconnect().catch(() => {});
    } catch (e) {}
  }

  // ================= BOOT =================
  initConnBadge();   // çevrimdışı rozeti + kuyruk sayacı (boot'ta, offline dahil her ekranda aktif)
  start();           // token varsa offline'da da POS açılır (JWT localStorage; veri uçları snapshot'tan)
  armIdle();
  // Açılışta çevrimiçiysek kalan kuyruğu (varsa) boşaltmayı dene.
  if (navigator.onLine) triggerOutboxSync();
})();
