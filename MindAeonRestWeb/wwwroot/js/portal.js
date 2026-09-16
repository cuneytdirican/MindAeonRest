/* FAZ 5a MÜŞTERİ SADAKAT PORTALI SPA (anonim). GİRİŞ token'ı URL'den (?t=) gelir; OTP doğrulanınca müşteri-scope
   OTURUM token'ı alınır ve bellekte tutulur. Puan/geçmiş/yeniden-sipariş PortalController'ın [AllowAnonymous]
   uçlarında token'la doğrulanır. Native alert/confirm YOK: inline toast + özel onay modalı. Mobil-öncelikli. */
(function () {
  'use strict';

  var ENTRY = window.PORTAL_TOKEN || new URLSearchParams(location.search).get('t') || '';
  var API = '/api/public/portal';
  var $app = document.getElementById('portal-app');
  var $toast = document.getElementById('portal-toast');
  var $modal = document.getElementById('portal-modal');

  var state = {
    phase: 'login',   // login | otp | home
    session: '',      // OTP sonrası müşteri oturum token'ı
    phone: '',
    name: '',
    points: null,     // {active, points, balanceTl}
    history: [],      // [{id,code,total,paid,date}]
    busy: false
  };

  // ---------- yardımcılar ----------
  function el(html) { var d = document.createElement('div'); d.innerHTML = html.trim(); return d.firstElementChild; }
  function esc(s) {
    return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }
  var _fmt = new Intl.NumberFormat('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  var _fmt0 = new Intl.NumberFormat('tr-TR', { maximumFractionDigits: 0 });
  function money(v) { return '₺' + _fmt.format(Number(v) || 0); }
  function points(v) { return _fmt0.format(Number(v) || 0); }
  function dateStr(iso) {
    try { return new Date(iso).toLocaleString('tr-TR', { dateStyle: 'medium', timeStyle: 'short' }); }
    catch (e) { return iso; }
  }
  var _t = null;
  function toast(msg, kind) {
    $toast.className = 'pt-toast on' + (kind ? ' ' + kind : '');
    $toast.textContent = msg;
    clearTimeout(_t); _t = setTimeout(function () { $toast.className = 'pt-toast'; }, 2400);
  }

  // Özel onay modalı (native confirm YASAK). Promise<boolean> döner.
  function uiConfirm(title, msg, okText) {
    return new Promise(function (resolve) {
      $modal.innerHTML = '';
      var box = el(
        '<div class="pt-modal-box" role="dialog" aria-modal="true">' +
        '<div class="pt-modal-title">' + esc(title) + '</div>' +
        '<div class="pt-modal-msg">' + esc(msg) + '</div>' +
        '<div class="pt-modal-actions">' +
        '<button type="button" class="pt-btn ghost" data-no>Vazgeç</button>' +
        '<button type="button" class="pt-btn" data-yes>' + esc(okText || 'Onayla') + '</button>' +
        '</div></div>'
      );
      $modal.appendChild(box);
      $modal.className = 'pt-modal on';
      $modal.setAttribute('aria-hidden', 'false');
      function close(v) {
        $modal.className = 'pt-modal';
        $modal.setAttribute('aria-hidden', 'true');
        $modal.innerHTML = '';
        resolve(v);
      }
      box.querySelector('[data-yes]').addEventListener('click', function () { close(true); });
      box.querySelector('[data-no]').addEventListener('click', function () { close(false); });
      $modal.addEventListener('click', function onBg(e) { if (e.target === $modal) { $modal.removeEventListener('click', onBg); close(false); } });
    });
  }

  function tkn() { return state.phase === 'home' ? state.session : ENTRY; }
  function tq(url, token) { return url + (url.indexOf('?') < 0 ? '?' : '&') + 't=' + encodeURIComponent(token); }

  async function apiGet(path) {
    var r = await fetch(tq(API + path, state.session), { headers: { 'Accept': 'application/json' } });
    if (!r.ok) { var e = new Error('http'); e.status = r.status; try { e.body = await r.json(); } catch (x) { } throw e; }
    return r.json();
  }
  async function apiPost(path, body, token) {
    var r = await fetch(tq(API + path, token), {
      method: 'POST', headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
      body: JSON.stringify(body)
    });
    if (!r.ok) { var e = new Error('http'); e.status = r.status; try { e.body = await r.json(); } catch (x) { } throw e; }
    return r.json();
  }
  function serverMsg(err, fallback) {
    return (err && err.body && err.body.message) ? err.body.message : fallback;
  }

  // ---------- kabuk ----------
  function head() {
    return '<div class="pt-head"><div class="pt-logo">🎁</div>' +
      '<div class="pt-titles"><b>Sadakat Portalı</b><small>' +
      (state.phase === 'home' && state.name ? esc(state.name) : 'MindAeon') + '</small></div>' +
      (state.phase === 'home' ? '<button type="button" class="pt-logout" data-logout>Çıkış</button>' : '') +
      '</div>';
  }

  function showInvalid() {
    $app.innerHTML = '';
    $app.appendChild(el(
      '<div class="pt"><div class="pt-state"><div class="ico">🔒</div>' +
      '<h2>Geçersiz bağlantı</h2><p>Bu sadakat portalı bağlantısı geçersiz ya da süresi dolmuş. ' +
      'Lütfen restoranın paylaştığı güncel bağlantıyı kullanın.</p></div></div>'
    ));
  }

  // ---------- giriş: telefon ----------
  function drawLogin() {
    $app.innerHTML = '';
    var wrap = el('<div class="pt">' + head() +
      '<div class="pt-body"><div class="pt-card pt-form">' +
      '<h2>Giriş</h2><p class="pt-mut">Telefon numaranıza doğrulama kodu gönderelim.</p>' +
      '<label class="pt-label">Telefon</label>' +
      '<input id="pt-phone" class="pt-input" type="tel" inputmode="tel" autocomplete="tel" placeholder="05xx xxx xx xx" />' +
      '<button type="button" class="pt-btn block" data-send>Kod Gönder</button>' +
      '</div></div></div>');
    $app.appendChild(wrap);
    var $phone = wrap.querySelector('#pt-phone');
    $phone.value = state.phone || '';
    wrap.querySelector('[data-send]').addEventListener('click', function () { sendOtp($phone.value); });
    $phone.addEventListener('keydown', function (e) { if (e.key === 'Enter') sendOtp($phone.value); });
    $phone.focus();
  }

  async function sendOtp(phone) {
    phone = (phone || '').trim();
    if (phone.replace(/\D/g, '').length < 7) { toast('Geçerli bir telefon numarası girin', 'err'); return; }
    if (state.busy) return; state.busy = true;
    try {
      var res = await apiPost('/otp-request', { phone: phone }, ENTRY);
      state.phone = phone; state.phase = 'otp';
      drawOtp(res && res.devCode ? res.devCode : null);
      toast('Kod gönderildi (numara kayıtlıysa)', 'ok');
    } catch (err) {
      toast(serverMsg(err, 'Kod gönderilemedi, tekrar deneyin'), 'err');
    } finally { state.busy = false; }
  }

  // ---------- giriş: OTP ----------
  function drawOtp(devCode) {
    $app.innerHTML = '';
    var wrap = el('<div class="pt">' + head() +
      '<div class="pt-body"><div class="pt-card pt-form">' +
      '<h2>Doğrulama Kodu</h2><p class="pt-mut">' + esc(state.phone) + ' numarasına gönderilen kodu girin.</p>' +
      (devCode ? '<p class="pt-dev">Geliştirme kodu: <b>' + esc(devCode) + '</b></p>' : '') +
      '<label class="pt-label">Kod</label>' +
      '<input id="pt-code" class="pt-input pt-code" type="text" inputmode="numeric" autocomplete="one-time-code" maxlength="6" placeholder="______" />' +
      '<button type="button" class="pt-btn block" data-verify>Doğrula</button>' +
      '<button type="button" class="pt-link" data-back>← Numarayı değiştir</button>' +
      '</div></div></div>');
    $app.appendChild(wrap);
    var $code = wrap.querySelector('#pt-code');
    wrap.querySelector('[data-verify]').addEventListener('click', function () { verifyOtp($code.value); });
    wrap.querySelector('[data-back]').addEventListener('click', function () { state.phase = 'login'; drawLogin(); });
    $code.addEventListener('keydown', function (e) { if (e.key === 'Enter') verifyOtp($code.value); });
    $code.focus();
  }

  async function verifyOtp(code) {
    code = (code || '').trim();
    if (code.length < 4) { toast('Kodu girin', 'err'); return; }
    if (state.busy) return; state.busy = true;
    try {
      var res = await apiPost('/otp-verify', { phone: state.phone, code: code }, ENTRY);
      state.session = res.token; state.name = res.name || ''; state.phase = 'home';
      await loadHome();
    } catch (err) {
      toast(serverMsg(err, 'Kod hatalı veya süresi dolmuş'), 'err');
    } finally { state.busy = false; }
  }

  // ---------- ana ekran ----------
  async function loadHome() {
    drawHome(true);
    try {
      var p = await apiGet('/points');
      state.points = p;
    } catch (err) { if (err.status === 401) return sessionExpired(); state.points = null; }
    try {
      var h = await apiGet('/history');
      state.history = (h && h.items) || [];
    } catch (err) { if (err.status === 401) return sessionExpired(); state.history = []; }
    drawHome(false);
  }

  function sessionExpired() {
    state.phase = 'login'; state.session = ''; state.points = null; state.history = [];
    toast('Oturum süresi doldu, tekrar giriş yapın', 'err');
    drawLogin();
  }

  function drawHome(loading) {
    $app.innerHTML = '';
    var pts = state.points;
    var pointsCard =
      '<div class="pt-card pt-points">' +
      (pts && pts.active === false
        ? '<div class="pt-points-off">Sadakat programı şu an aktif değil.</div>'
        : '<div class="pt-points-big">' + (pts ? points(pts.points) : '—') + ' <span>puan</span></div>' +
          '<div class="pt-points-tl">' + (pts ? money(pts.balanceTl) + ' değerinde' : '') + '</div>') +
      '</div>';

    var histHtml;
    if (loading) {
      histHtml = '<div class="pt-loading">Yükleniyor…</div>';
    } else if (!state.history.length) {
      histHtml = '<div class="pt-empty">Henüz sipariş geçmişiniz yok.</div>';
    } else {
      histHtml = state.history.map(function (h) {
        return '<div class="pt-hist">' +
          '<div class="pt-hist-main">' +
          '<div class="pt-hist-code">' + esc(h.code) + '</div>' +
          '<div class="pt-hist-date">' + esc(dateStr(h.date)) + '</div>' +
          '</div>' +
          '<div class="pt-hist-side">' +
          '<div class="pt-hist-total">' + money(h.total) + '</div>' +
          '<button type="button" class="pt-btn sm" data-reorder="' + h.id + '">Yeniden Sipariş</button>' +
          '</div></div>';
      }).join('');
    }

    var wrap = el('<div class="pt">' + head() +
      '<div class="pt-body">' + pointsCard +
      '<h3 class="pt-h3">Sipariş Geçmişi</h3>' +
      '<div class="pt-list">' + histHtml + '</div>' +
      '</div></div>');
    $app.appendChild(wrap);

    var lg = wrap.querySelector('[data-logout]');
    if (lg) lg.addEventListener('click', logout);
    Array.prototype.forEach.call(wrap.querySelectorAll('[data-reorder]'), function (b) {
      b.addEventListener('click', function () { reorder(parseInt(b.getAttribute('data-reorder'), 10)); });
    });
  }

  async function reorder(ficheId) {
    if (state.busy) return;
    var ok = await uiConfirm('Yeniden Sipariş', 'Bu adisyondaki ürünler güncel fiyatlarla yeni bir sipariş olarak oluşturulacak. Onaylıyor musunuz?', 'Sipariş Ver');
    if (!ok) return;
    state.busy = true;
    try {
      var res = await apiPost('/reorder', { clFicheId: ficheId }, state.session);
      toast((res && res.code ? res.code + ' · ' : '') + (res.added || 0) + ' ürün sipariş edildi', 'ok');
      // Puanı tazele (yeni sipariş henüz ödenmediğinden puan değişmeyebilir; yine de senkron kalsın).
      try { state.points = await apiGet('/points'); drawHome(false); } catch (e) { }
    } catch (err) {
      if (err.status === 401) return sessionExpired();
      toast(serverMsg(err, 'Sipariş oluşturulamadı'), 'err');
    } finally { state.busy = false; }
  }

  async function logout() {
    var ok = await uiConfirm('Çıkış', 'Oturumu kapatmak istiyor musunuz?', 'Çıkış Yap');
    if (!ok) return;
    state.phase = 'login'; state.session = ''; state.name = ''; state.points = null; state.history = [];
    drawLogin();
  }

  // ---------- başlat ----------
  if (!ENTRY) { showInvalid(); }
  else { drawLogin(); }
})();
