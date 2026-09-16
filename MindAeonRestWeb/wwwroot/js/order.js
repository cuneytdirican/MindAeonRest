/* QR masadan-sipariş müşteri SPA (anonim). Token URL'den alınır; menü/sipariş/görsel PublicController'ın
   [AllowAnonymous] uçlarında token'la doğrulanır. İstemci FİYAT GÖNDERMEZ — fiyat/KDV sunucuda (menü) çözülür.
   Native alert/confirm YOK: inline toast + alt-sayfa sepet modalı. Mobil-öncelikli, büyük dokunmatik. */
(function () {
  'use strict';

  var TOKEN = window.ORDER_TOKEN || new URLSearchParams(location.search).get('t') || '';
  var API = '/api/public';
  var $app = document.getElementById('order-app');
  var $toast = document.getElementById('order-toast');

  var state = {
    categories: [],   // [{id,name,hasImage}]
    products: [],     // [{id,name,description,price,taxRate,isSalesStopped,isMenu,hasImage,categoryIds}]
    activeCat: 0,     // 0 = Tümü
    cart: [],         // [{productId,name,price,qty,note}]
    busy: false,
    sheetOpen: false,
    orderKey: null,   // aktif sepet gönderimine sabit Idempotency-Key (retry AYNI key → çift sipariş yok)
    retried: false    // 429 için gönderim-başına TEK otomatik retry bayrağı (sonsuz döngü yok)
  };

  // ---------- sepet kalıcılığı (sessionStorage; token'a-özel anahtar) ----------
  // Sepet yalnız bellekte tutulunca pull-to-refresh / SW güncelleme / sekme değişimi "sepette 0 ürün"e yol açıyordu.
  // Anahtara token'ın kısa hash'i katılır → farklı masa/QR sepetleri KARIŞMAZ (token'da şube+masa gömülü).
  function shortHash(s) {
    var h = 5381;
    for (var i = 0; i < s.length; i++) { h = ((h << 5) + h) + s.charCodeAt(i); h |= 0; }
    return (h >>> 0).toString(36);
  }
  var CART_KEY = 'mar.cart.' + shortHash(TOKEN || '');
  function saveCart() { try { sessionStorage.setItem(CART_KEY, JSON.stringify(state.cart)); } catch (e) { } }
  function clearCartStore() { try { sessionStorage.removeItem(CART_KEY); } catch (e) { } }
  function loadCart() {
    try {
      var raw = sessionStorage.getItem(CART_KEY);
      if (!raw) return;
      var arr = JSON.parse(raw);
      if (Array.isArray(arr)) {
        state.cart = arr.filter(function (x) { return x && x.productId != null && x.qty > 0; })
          .map(function (x) { return { productId: x.productId, name: String(x.name || ''), price: Number(x.price) || 0, qty: x.qty | 0, note: String(x.note || '') }; });
      }
    } catch (e) { }
  }

  // ---------- yardımcılar ----------
  function el(html) { var d = document.createElement('div'); d.innerHTML = html.trim(); return d.firstElementChild; }
  function esc(s) {
    return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }
  var _fmt = new Intl.NumberFormat('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  function money(v) { return '₺' + _fmt.format(Number(v) || 0); }
  var _t = null;
  function toast(msg, kind) {
    $toast.className = 'ord-toast on' + (kind ? ' ' + kind : '');
    $toast.textContent = msg;
    clearTimeout(_t); _t = setTimeout(function () { $toast.className = 'ord-toast'; }, 2200);
  }
  function tq(url) { return url + (url.indexOf('?') < 0 ? '?' : '&') + 't=' + encodeURIComponent(TOKEN); }
  // İstemci-üretimli idempotency anahtarı (çift-tık / kaybolan yanıt emici katman için).
  function uuid() {
    if (window.crypto && crypto.randomUUID) return crypto.randomUUID();
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
      var r = Math.random() * 16 | 0, v = c === 'x' ? r : (r & 0x3 | 0x8); return v.toString(16);
    });
  }
  function productImg(id) { return tq(API + '/product-image/' + id); }
  function categoryImg(id) { return tq(API + '/category-image/' + id); }

  async function apiGet(path) {
    var r = await fetch(tq(API + path), { headers: { 'Accept': 'application/json' } });
    if (!r.ok) { var e = new Error('http'); e.status = r.status; try { e.body = await r.json(); } catch (x) { } throw e; }
    return r.json();
  }
  async function apiPost(path, body, idemKey) {
    var headers = { 'Content-Type': 'application/json', 'Accept': 'application/json' };
    if (idemKey) headers['Idempotency-Key'] = idemKey;   // aynı gönderim aynı key → sunucu ilk sonucu döner
    var r = await fetch(tq(API + path), {
      method: 'POST', headers: headers,
      body: JSON.stringify(body)
    });
    if (!r.ok) { var e = new Error('http'); e.status = r.status; try { e.body = await r.json(); } catch (x) { } throw e; }
    return r.json();
  }
  function serverMsg(err, fallback) {
    return (err && err.body && err.body.message) ? err.body.message : fallback;
  }

  // ---------- sepet ----------
  function cartFind(pid) { for (var i = 0; i < state.cart.length; i++) if (state.cart[i].productId === pid) return state.cart[i]; return null; }
  function cartCount() { return state.cart.reduce(function (s, it) { return s + it.qty; }, 0); }
  function cartTotal() { return state.cart.reduce(function (s, it) { return s + it.qty * it.price; }, 0); }
  function cartAdd(p) {
    var it = cartFind(p.id);
    if (it) it.qty += 1;
    else state.cart.push({ productId: p.id, name: p.name, price: Number(p.price) || 0, qty: 1, note: '' });
    saveCart();
    toast(p.name + ' eklendi', 'ok');
    syncBar(); drawGrid(); if (state.sheetOpen) drawSheet();
  }
  function cartSetQty(pid, delta) {
    var it = cartFind(pid); if (!it) return;
    it.qty += delta;
    if (it.qty <= 0) state.cart = state.cart.filter(function (x) { return x.productId !== pid; });
    saveCart();
    syncBar(); drawGrid(); if (state.sheetOpen) drawSheet();
    if (!state.cart.length) closeSheet();
  }

  // ---------- render: yükleniyor / hata ----------
  function showState(icon, title, msg, btnText, onBtn, cls) {
    $app.innerHTML = '';
    var s = el('<div class="ord ' + (cls || '') + '"><div class="ord-state">' +
      '<div class="ico">' + icon + '</div>' +
      '<h2>' + esc(title) + '</h2>' +
      (msg ? '<p>' + esc(msg) + '</p>' : '') +
      (btnText ? '<button class="big-btn" id="ord-state-btn">' + esc(btnText) + '</button>' : '') +
      '</div></div>');
    $app.appendChild(s);
    if (btnText && onBtn) s.querySelector('#ord-state-btn').addEventListener('click', onBtn);
  }
  function showLoading() {
    $app.innerHTML = '';
    $app.appendChild(el('<div class="ord"><div class="ord-state"><div class="ord-spin"></div>' +
      '<p>Menü yükleniyor…</p></div></div>'));
  }
  function showInvalid(msg) {
    showState('📵', 'Bağlantı geçersiz', msg || 'Bu QR kodu geçersiz veya süresi dolmuş. Lütfen garsonu çağırın.',
      'Tekrar dene', function () { boot(); });
  }

  // ---------- render: menü kabuğu ----------
  function renderShell() {
    $app.innerHTML = '';
    var shell = el('<div class="ord">' +
      '<header class="ord-head">' +
        '<span class="ord-logo">🍽️</span>' +
        '<div class="ord-titles"><b>MindAeon</b><small>Masanızdan sipariş verin</small></div>' +
        '<button class="ord-call" id="ord-call" type="button">🔔 Garson Çağır</button>' +
      '</header>' +
      '<nav class="ord-cats" id="ord-cats"></nav>' +
      '<main class="ord-body"><div class="ord-grid" id="ord-grid"></div></main>' +
      '<div class="ord-bar" id="ord-bar"></div>' +
    '</div>');
    $app.appendChild(shell);
    document.getElementById('ord-call')?.addEventListener('click', callWaiter);
    drawCats(); drawGrid(); syncBar();
  }

  // ---------- garson çağır ----------
  // Müşteri VERİ GÖNDERMEZ (boş {}); fiyat/masa sunucuda token'dan çözülür. Spam'e karşı düğme 4sn kilitli.
  async function callWaiter() {
    var btn = document.getElementById('ord-call');
    if (btn) {
      if (btn.disabled) return;
      btn.disabled = true;
      setTimeout(function () { var b = document.getElementById('ord-call'); if (b) b.disabled = false; }, 4000);
    }
    try {
      await apiPost('/call-waiter', {});   // apiPost token'ı zaten query'e ekler
      toast('Garson çağrıldı, birazdan masanıza gelecek.', 'ok');
    } catch (err) {
      if (err.status === 429) toast('Çok sık istek — birkaç saniye sonra deneyin.', 'err');
      else if (err.status === 401) showInvalid(serverMsg(err, null));
      else toast(serverMsg(err, 'Çağrı gönderilemedi.'), 'err');
    }
  }

  function drawCats() {
    var host = document.getElementById('ord-cats'); if (!host) return;
    host.innerHTML = '';
    var all = [{ id: 0, name: 'Tümü' }].concat(state.categories);
    all.forEach(function (c) {
      var img = (c.id && c.hasImage)
        ? '<img loading="lazy" src="' + esc(categoryImg(c.id)) + '" onerror="this.remove()" />' : '';
      var b = el('<button class="ord-cat' + (state.activeCat === c.id ? ' on' : '') + '">' +
        img + '<span>' + esc(c.name) + '</span></button>');
      b.addEventListener('click', function () { state.activeCat = c.id; drawCats(); drawGrid(); });
      host.appendChild(b);
    });
  }

  function drawGrid() {
    var grid = document.getElementById('ord-grid'); if (!grid) return;
    grid.innerHTML = '';
    var list = state.products;
    if (state.activeCat) list = list.filter(function (p) { return (p.categoryIds || []).indexOf(state.activeCat) >= 0; });
    if (!list.length) {
      grid.appendChild(el('<div class="ord-state" style="grid-column:1/-1"><div class="ico">🍽️</div><p>Bu kategoride ürün yok.</p></div>'));
      return;
    }
    list.forEach(function (p) { grid.appendChild(productCard(p)); });
  }

  function productCard(p) {
    var stopped = !!p.isSalesStopped;
    var it = cartFind(p.id);
    var imgInner = p.hasImage
      ? '<img loading="lazy" src="' + esc(productImg(p.id)) + '" onerror="this.parentElement.textContent=\'🍴\'" />'
      : (p.isMenu ? '🍱' : '🍴');
    var card = el('<div class="ord-card' + (stopped ? ' stopped' : '') + '">' +
      (it && it.qty ? '<span class="ord-qbadge">' + it.qty + '</span>' : '') +
      '<div class="ord-pimg">' + imgInner + '</div>' +
      '<div class="ord-pbody">' +
        '<div class="ord-pname">' + esc(p.name) + '</div>' +
        '<div class="ord-pdesc">' + esc(p.description || '') + '</div>' +
        '<div class="ord-prow"><span class="ord-price">' + money(p.price) + '</span>' +
          (stopped ? '<span class="ord-stop">Tükendi</span>' : '<span class="ord-add">＋ Ekle</span>') +
        '</div>' +
      '</div></div>');
    if (!stopped) card.addEventListener('click', function () { cartAdd(p); });
    return card;
  }

  // ---------- alt sepet çubuğu ----------
  function syncBar() {
    var host = document.getElementById('ord-bar'); if (!host) return;
    var n = cartCount();
    host.innerHTML = '<button class="ord-bar-btn" id="ord-open-cart"' + (n ? '' : ' disabled') + '>' +
      '<span>🛒 Sepeti Gör <span class="ord-bar-n">' + n + '</span></span>' +
      '<b>' + money(cartTotal()) + '</b></button>';
    var b = host.querySelector('#ord-open-cart');
    if (b) b.addEventListener('click', openSheet);
  }

  // ---------- sepet alt-sayfası ----------
  var $sheetBg = null;
  function openSheet() {
    if (!state.cart.length) return;
    state.sheetOpen = true;
    $sheetBg = el('<div class="ord-sheet-bg"><div class="ord-sheet">' +
      '<div class="ord-sheet-head"><b>🛒 Sepetim</b><button class="ord-x" id="ord-sheet-x">✕</button></div>' +
      '<div class="ord-sheet-list" id="ord-sheet-list"></div>' +
      '<div class="ord-sheet-foot" id="ord-sheet-foot"></div>' +
    '</div></div>');
    document.body.appendChild($sheetBg);
    $sheetBg.addEventListener('click', function (e) { if (e.target === $sheetBg) closeSheet(); });
    $sheetBg.querySelector('#ord-sheet-x').addEventListener('click', closeSheet);
    requestAnimationFrame(function () { if ($sheetBg) $sheetBg.classList.add('on'); });
    drawSheet();
  }
  function closeSheet() {
    state.sheetOpen = false;
    if (!$sheetBg) return;
    var node = $sheetBg; $sheetBg = null;
    node.classList.remove('on');
    setTimeout(function () { if (node && node.parentNode) node.parentNode.removeChild(node); }, 220);
  }

  function drawSheet() {
    if (!$sheetBg) return;
    var list = $sheetBg.querySelector('#ord-sheet-list');
    var foot = $sheetBg.querySelector('#ord-sheet-foot');
    if (!state.cart.length) { closeSheet(); return; }
    list.innerHTML = '';
    state.cart.forEach(function (it) {
      var row = el('<div class="ord-crow">' +
        '<div class="ord-cinfo">' +
          '<div class="ord-cname">' + esc(it.name) + '</div>' +
          '<div class="ord-cprice">' + money(it.price) + ' × ' + it.qty + '</div>' +
          '<input class="ord-note" maxlength="120" placeholder="Not (örn. az acılı)" value="' + esc(it.note) + '" />' +
        '</div>' +
        '<div style="display:flex;flex-direction:column;align-items:flex-end;gap:8px">' +
          '<div class="ord-step">' +
            '<button data-dec>−</button><span class="q">' + it.qty + '</span><button data-inc>＋</button>' +
          '</div>' +
          '<div class="ord-ctot">' + money(it.price * it.qty) + '</div>' +
        '</div>' +
      '</div>');
      row.querySelector('[data-inc]').addEventListener('click', function () { cartSetQty(it.productId, 1); });
      row.querySelector('[data-dec]').addEventListener('click', function () { cartSetQty(it.productId, -1); });
      row.querySelector('.ord-note').addEventListener('input', function (e) { it.note = e.target.value; saveCart(); });
      list.appendChild(row);
    });
    foot.innerHTML =
      '<div class="ord-total"><span>TOPLAM</span><b>' + money(cartTotal()) + '</b></div>' +
      '<button class="ord-place" id="ord-place"' + (state.busy ? ' disabled' : '') + '>🛒 Siparişi Ver</button>' +
      '<button class="ord-clear" id="ord-clear">Sepeti temizle</button>';
    foot.querySelector('#ord-place').addEventListener('click', placeOrder);
    foot.querySelector('#ord-clear').addEventListener('click', function () {
      state.cart = []; clearCartStore(); syncBar(); drawGrid(); closeSheet();
    });
  }

  // ---------- sipariş gönder ----------
  async function placeOrder() {
    if (state.busy || !state.cart.length) return;
    state.busy = true;
    // Bu sepet gönderimine sabit key: yalnız BAŞARIDA sıfırlanır → başarısız/yeniden-denemede AYNI key kalır.
    // Yeni gönderim (yeni key) başlarken 429 otomatik-retry bayrağı sıfırlanır → her gönderim TEK retry hakkı.
    if (!state.orderKey) { state.orderKey = uuid(); state.retried = false; }
    var btn = $sheetBg && $sheetBg.querySelector('#ord-place');
    if (btn) { btn.disabled = true; btn.textContent = 'Gönderiliyor…'; }
    try {
      var items = state.cart.map(function (it) {
        return { productId: it.productId, qty: it.qty, note: (it.note || '').trim() || null };
      });
      var res = await apiPost('/order', { items: items }, state.orderKey);   // istemci fiyat GÖNDERMEZ
      state.orderKey = null;   // başarılı → sonraki sipariş yeni key alır
      state.retried = false;
      state.cart = [];
      clearCartStore();        // başarılı sipariş → kalıcı sepet temizlenir
      closeSheet();
      showSuccess(res);
    } catch (err) {
      if (err.status === 401) { closeSheet(); showInvalid(serverMsg(err, null)); }
      else if (err.status === 429) {
        // Rate-limit: sepet/orderKey KORUNUR. Kullanıcıya bilgi ver + AYNI key ile bir kez otomatik yeniden dene.
        toast('Çok fazla istek — birkaç saniye sonra tekrar deneyin.', 'err');
        if (btn) { btn.disabled = false; btn.textContent = '🛒 Siparişi Ver'; }
        if (!state.retried) {
          state.retried = true;
          state.busy = false;   // finally da sıfırlar; retry re-entry'sine izin ver
          setTimeout(function () { placeOrder(); }, 1500);   // AYNI orderKey → sunucu çift sipariş üretmez
          return;
        }
      }
      else {
        toast(serverMsg(err, 'Sipariş gönderilemedi, tekrar deneyin.'), 'err');
        if (btn) { btn.disabled = false; btn.textContent = '🛒 Siparişi Ver'; }
      }
    } finally {
      state.busy = false;
    }
  }

  function showSuccess(res) {
    $app.innerHTML = '';
    var code = res && res.code ? res.code : (res && res.orderId ? '#' + res.orderId : '');
    var s = el('<div class="ord ord-success"><div class="ord-state">' +
      '<div class="ico">✅</div>' +
      '<h2>Siparişiniz mutfağa iletildi</h2>' +
      (code ? '<p style="font-weight:700;color:var(--txt)">Sipariş No: ' + esc(code) + '</p>' : '') +
      '<p>Siparişiniz hazırlanıp masanıza gelecek. Ödemeyi garsondan veya kasadan yapabilirsiniz.</p>' +
      '<button class="big-btn" id="ord-again">＋ Yeni Sipariş Ekle</button>' +
      '</div></div>');
    $app.appendChild(s);
    s.querySelector('#ord-again').addEventListener('click', function () { renderShell(); window.scrollTo(0, 0); });
  }

  // ---------- açılış ----------
  async function boot() {
    if (!TOKEN) { showInvalid('Bağlantıda masa bilgisi yok. Lütfen QR kodu tekrar okutun.'); return; }
    showLoading();
    try {
      var menu = await apiGet('/menu');
      state.categories = menu.categories || [];
      state.products = menu.products || [];
      loadCart();   // sayfa yenileme/SW güncelleme/sekme dönüşünde kalıcı sepeti geri yükle (menü geldikten sonra)
      renderShell();
    } catch (err) {
      if (err.status === 401) showInvalid(serverMsg(err, null));
      else showState('⚠️', 'Menü yüklenemedi', serverMsg(err, 'Bağlantı sorunu. Lütfen tekrar deneyin.'),
        'Tekrar dene', function () { boot(); });
    }
  }

  boot();
})();
