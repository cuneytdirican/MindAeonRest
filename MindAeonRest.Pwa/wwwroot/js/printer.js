/* ===== MindAeon Garson PWA — donanım yazıcı köprüsü (FAZ 4d) =====
   WebSerial (navigator.serial) ile seri/USB ESC-POS termal yazıcıya bağlanır ve RAW bayt yazar.
   Kullanıcı bir kez yetki verir (requestPort); yetki verilen port tarayıcıda hatırlanır ve açılışta
   getPorts() ile otomatik yeniden bağlanmaya çalışılır (localStorage'da "etkin" bayrağı tutulur).

   ÖNEMLİ — FALLBACK: WebSerial yalnız Chrome/Edge + HTTPS'te vardır. Desteklenmiyor ya da bağlı değilse
   Printer.isConnected() false döner ve app.js mevcut window.print (HTML iframe) yoluna düşer. Yani
   donanım yoksa HİÇBİR ŞEY bozulmaz — bu katman tamamen additive.

   window.Printer olarak dışa açılır. escpos.js'ten (window.EscPos) bağımsızdır; sadece Uint8Array yazar. */
(function (global) {
  'use strict';

  const LS_ENABLED = 'mag.printer.enabled';   // kullanıcı köprüyü etkinleştirdi → açılışta oto-bağlan dene
  const BAUD = 9600;                           // termal ESC/POS seri varsayılanı (çoğu cihaz 9600/19200)

  let _port = null;      // SerialPort
  let _writer = null;    // WritableStreamDefaultWriter
  const _listeners = new Set();

  function supported() { return typeof navigator !== 'undefined' && 'serial' in navigator; }
  function isConnected() { return !!_port && !!_writer; }

  function onChange(fn) { _listeners.add(fn); return () => _listeners.delete(fn); }
  function emit() { const c = isConnected(); _listeners.forEach(fn => { try { fn(c); } catch (e) {} }); }

  async function _openPort(port) {
    await port.open({ baudRate: BAUD });
    _port = port;
    _writer = port.writable.getWriter();
    // Fiziksel kopmayı yakala (kablo çekilirse) → durum sıfırla.
    if (navigator.serial && navigator.serial.addEventListener) {
      navigator.serial.addEventListener('disconnect', (e) => {
        if (e.target === _port) { _resetState(); emit(); }
      });
    }
    try { localStorage.setItem(LS_ENABLED, '1'); } catch (e) {}
    emit();
  }

  function _resetState() {
    try { if (_writer) _writer.releaseLock(); } catch (e) {}
    _writer = null; _port = null;
  }

  // Kullanıcı etkileşimiyle (buton) port seç + bağlan. Başarıda true.
  async function connect() {
    if (!supported()) throw new Error('Bu tarayıcı donanım yazıcı köprüsünü (WebSerial) desteklemiyor.');
    if (isConnected()) return true;
    const port = await navigator.serial.requestPort();  // kullanıcı seçim penceresi
    await _openPort(port);
    return true;
  }

  // Açılışta: daha önce yetki verilmiş portu sessizce yeniden aç (kullanıcı etkileşimi gerekmez).
  async function tryReconnect() {
    if (!supported()) return false;
    let enabled = false;
    try { enabled = localStorage.getItem(LS_ENABLED) === '1'; } catch (e) {}
    if (!enabled) return false;
    try {
      const ports = await navigator.serial.getPorts();
      if (ports && ports.length) { await _openPort(ports[0]); return true; }
    } catch (e) {}
    return false;
  }

  async function disconnect() {
    try { localStorage.removeItem(LS_ENABLED); } catch (e) {}
    try { if (_writer) { _writer.releaseLock(); _writer = null; } } catch (e) {}
    try { if (_port) await _port.close(); } catch (e) {}
    _port = null;
    emit();
  }

  // RAW bayt yaz (Uint8Array). Bağlı değilse hata fırlatır (çağıran fallback'e düşer).
  async function print(bytes) {
    if (!isConnected()) throw new Error('Yazıcı bağlı değil.');
    const data = bytes instanceof Uint8Array ? bytes : Uint8Array.from(bytes || []);
    await _writer.write(data);
  }

  // Sadece kasa çekmecesini aç (ESC p 0). escpos.js varsa onun baytını, yoksa yerel sabiti kullan.
  async function kickDrawer() {
    const kick = (global.EscPos && global.EscPos.drawerBytes)
      ? global.EscPos.drawerBytes()
      : Uint8Array.from([0x1B, 0x70, 0x00, 0x19, 0xFA]);
    await print(kick);
  }

  global.Printer = { supported, isConnected, connect, tryReconnect, disconnect, print, kickDrawer, onChange };
})(typeof window !== 'undefined' ? window : this);
