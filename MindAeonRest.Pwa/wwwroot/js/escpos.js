/* ===== MindAeon Garson PWA — ESC/POS bayt üretimi (FAZ 4d donanım köprüsü) =====
   Termal yazıcıya RAW gönderilecek ESC/POS komut dizisini üretir. Fiş modeli (app.js'teki
   receiptHtml/receiptItemsFromLines ile AYNI şekil) → Uint8Array.
     model = { heading, dateStr?, company?, branch?, meta:[[k,v]...],
               items:[{qty,name,amount|amountText}], totals:[{label,value|valueText,strong?}],
               footer?, kickDrawer? }
   Referans komutlar WPF Helpers/RawPrinterHelper.cs ile birebir aynı:
     Kağıt kes  = GS V B 0  (0x1D 0x56 0x42 0x00)
     Çekmece    = ESC p 0   (0x1B 0x70 0x00 0x19 0xFA)
   NOT: Bu dosya app.js'ten BAĞIMSIZ ve saf (DOM/ağ yok) — window.EscPos olarak dışa açılır. */
(function (global) {
  'use strict';

  // 80mm Font A = 48 sütun (58mm yazıcı için 32'ye düşürülebilir).
  const DEFAULT_WIDTH = 48;

  // ---- ESC/POS düşük seviye komutlar ----
  const ESC = 0x1B, GS = 0x1D;
  const CMD = {
    init:        [ESC, 0x40],                 // ESC @  — yazıcıyı sıfırla
    cut:         [GS, 0x56, 0x42, 0x00],      // GS V B 0 — tam kesme (besleme ile)
    drawerKick:  [ESC, 0x70, 0x00, 0x19, 0xFA], // ESC p 0 — kasa çekmecesi tetik
    codepage857: [ESC, 0x74, 13]              // ESC t 13 — PC857 (Türkçe) karakter tablosu
  };
  const alignCmd  = n => [ESC, 0x61, n & 3];              // ESC a n (0=sol,1=orta,2=sağ)
  const boldCmd   = on => [ESC, 0x45, on ? 1 : 0];        // ESC E n
  const sizeCmd   = n => [GS, 0x21, n & 0xFF];            // GS ! n (bit4=çift-boy, bit0=çift-genişlik)
  const feedCmd   = n => [ESC, 0x64, n & 0xFF];           // ESC d n — n satır besle

  // ---- CP857 (PC857 / Türkçe) eşlemesi: Türkçe'ye özel karakterler için bayt karşılıkları ----
  const CP857 = {
    'Ç': 0x80, 'ü': 0x81, 'é': 0x82, 'â': 0x83, 'ä': 0x84, 'à': 0x85, 'å': 0x86, 'ç': 0x87,
    'ê': 0x88, 'ë': 0x89, 'è': 0x8A, 'ï': 0x8B, 'î': 0x8C, 'ı': 0x8D, 'Ä': 0x8E, 'Å': 0x8F,
    'É': 0x90, 'ô': 0x93, 'ö': 0x94, 'ò': 0x95, 'û': 0x96, 'ù': 0x97, 'İ': 0x98, 'Ö': 0x99,
    'Ü': 0x9A, 'ø': 0x9B, '£': 0x9C, 'Ø': 0x9D, 'Ş': 0x9E, 'ş': 0x9F,
    'á': 0xA0, 'í': 0xA1, 'ó': 0xA2, 'ú': 0xA3, 'ñ': 0xA4, 'Ñ': 0xA5, 'Ğ': 0xA6, 'ğ': 0xA7,
    '₺': 0x54 /* çoğu termalde ₺ yoktur → 'T' */
  };
  // Türkçe harfleri ASCII'ye indirgeme (₺ / bilinmeyen karakter fallback için).
  const ASCII_FOLD = { 'ğ': 'g', 'Ğ': 'G', 'ş': 's', 'Ş': 'S', 'ı': 'i', 'İ': 'I',
    'ö': 'o', 'Ö': 'O', 'ü': 'u', 'Ü': 'U', 'ç': 'c', 'Ç': 'C', '₺': 'TL ' };

  function encodeChar(ch, out) {
    const code = ch.charCodeAt(0);
    if (code < 0x80) { out.push(code); return; }        // saf ASCII
    if (CP857[ch] != null) { out.push(CP857[ch]); return; } // CP857 tablosu
    const fold = ASCII_FOLD[ch];                         // eşleme yoksa ASCII'ye indirge
    if (fold != null) { for (const c of fold) out.push(c.charCodeAt(0) & 0x7F); return; }
    out.push(0x3F);                                      // '?' — bilinmeyen
  }
  function encodeText(str, out) { for (const ch of String(str == null ? '' : str)) encodeChar(ch, out); }

  // ---- Builder: bayt biriktirir, build() ile Uint8Array döner ----
  function Builder(width) {
    const bytes = [];
    const W = width || DEFAULT_WIDTH;
    const api = {
      raw(arr) { for (let i = 0; i < arr.length; i++) bytes.push(arr[i] & 0xFF); return api; },
      text(s) { encodeText(s, bytes); return api; },
      newline() { bytes.push(0x0A); return api; },
      line(s) { encodeText(s, bytes); bytes.push(0x0A); return api; },
      align(n) { return api.raw(alignCmd(n)); },
      bold(on) { return api.raw(boldCmd(on)); },
      size(n) { return api.raw(sizeCmd(n)); },
      feed(n) { return api.raw(feedCmd(n)); },
      // İki sütun: sol + boşluk + sağ, toplam W sütuna sığdır (taşarsa solu kırp).
      twoCol(left, right) {
        left = String(left == null ? '' : left); right = String(right == null ? '' : right);
        const rLen = [...right].length;
        const maxLeft = Math.max(0, W - rLen - 1);
        let l = [...left];
        if (l.length > maxLeft) l = l.slice(0, maxLeft);
        const pad = Math.max(1, W - l.length - rLen);
        return api.line(l.join('') + ' '.repeat(pad) + right);
      },
      // Ortalanmış tek satır.
      center(s) { api.align(1); api.line(s); api.align(0); return api; },
      // Tam genişlik ayraç (kesikli çizgi).
      rule(ch) { return api.line((ch || '-').repeat(W)); },
      build() { return Uint8Array.from(bytes); }
    };
    return api;
  }

  // Varsayılan para biçimlendirici (opts.money verilmezse). App.money ile aynı görünüm.
  function defMoney(n) {
    const v = Number(n || 0);
    return '₺' + v.toLocaleString('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  }

  // ---- Fiş modelini ESC/POS byte dizisine çevir ----
  function fromModel(model, opts) {
    model = model || {}; opts = opts || {};
    const money = opts.money || defMoney;
    const b = Builder(opts.width || DEFAULT_WIDTH);

    b.raw(CMD.init).raw(CMD.codepage857);

    // Başlık: firma (çift-boy kalın ortalı) + şube
    b.align(1);
    if (model.company) { b.size(0x11).bold(true).line(model.company).bold(false).size(0x00); }
    if (model.branch) b.line(model.branch);
    if (model.heading) { b.bold(true).line(model.heading).bold(false); }
    b.align(0);
    b.rule('-');

    // Tarih + meta satırları
    b.twoCol('Tarih', model.dateStr || new Date().toLocaleString('tr-TR'));
    (model.meta || []).forEach(m => {
      if (m && m[0] && m[1] != null && m[1] !== '') b.twoCol(m[0], String(m[1]));
    });

    // Kalemler
    if (model.items && model.items.length) {
      b.rule('-');
      model.items.forEach(it => {
        const qty = it.qty ? (String(it.qty) + 'x ') : '';
        const amt = it.amountText != null ? it.amountText : money(it.amount);
        b.twoCol(qty + (it.name || ''), amt);
      });
    }

    // Toplamlar
    b.rule('-');
    (model.totals || []).filter(Boolean).forEach(t => {
      const val = t.valueText != null ? t.valueText : money(t.value);
      // strong = kalın (çift-boy DEĞİL — 48 sütun twoCol hizası bozulmasın diye).
      if (t.strong) b.bold(true).twoCol(t.label, val).bold(false);
      else b.twoCol(t.label, val);
    });

    // Alt bilgi
    if (model.footer) { b.feed(1).center(model.footer); }

    // Besle → (nakit ise) çekmece → kes
    b.feed(3);
    if (model.kickDrawer) b.raw(CMD.drawerKick);
    b.raw(CMD.cut);
    return b.build();
  }

  // Bağımsız çekmece komutu (fiş basmadan sadece çekmece açmak için).
  function drawerBytes() { return Uint8Array.from(CMD.drawerKick); }

  global.EscPos = { fromModel, drawerBytes, encodeText: (s) => { const o = []; encodeText(s, o); return Uint8Array.from(o); }, CMD, DEFAULT_WIDTH };
})(typeof window !== 'undefined' ? window : this);
