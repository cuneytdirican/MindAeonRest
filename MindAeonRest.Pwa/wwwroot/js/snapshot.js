/* ===== MindAeon Garson PWA — katalog snapshot cache (IndexedDB) =====
   FAZ 2a: offline-first. Menü (kategori+ürün) ve masa/zone listesi ONLINE iken buraya yazılır;
   OFFLINE açılışta bu snapshot'tan servis edilir (garson menüyü görüp sipariş kuyruklayabilsin).
   Basit anahtar-değer deposu (key -> {value, ts}); api.js'teki cachedGet sarmalayıcısı kullanır.

   ÖNEMLİ: outbox.js gibi HEM sayfa (window) HEM Service Worker bağlamında güvenli olsun diye
   'self' üstünde çalışır ve window'a özel API'lere dokunmaz. api.js'ten ÖNCE yüklenir. */
(function (global) {
  'use strict';

  const DB_NAME = 'mag-snapshot';
  const DB_VERSION = 1;
  const STORE = 'kv';

  function openDb() {
    return new Promise((resolve, reject) => {
      let req;
      try { req = global.indexedDB.open(DB_NAME, DB_VERSION); }
      catch (e) { reject(e); return; }
      req.onupgradeneeded = () => {
        const db = req.result;
        if (!db.objectStoreNames.contains(STORE)) {
          db.createObjectStore(STORE, { keyPath: 'key' });
        }
      };
      req.onsuccess = () => resolve(req.result);
      req.onerror = () => reject(req.error);
    });
  }

  function tx(mode, fn) {
    return openDb().then(db => new Promise((resolve, reject) => {
      const t = db.transaction(STORE, mode);
      const store = t.objectStore(STORE);
      let out;
      Promise.resolve(fn(store)).then(v => { out = v; }).catch(reject);
      t.oncomplete = () => { resolve(out); db.close(); };
      t.onerror = () => { reject(t.error); db.close(); };
      t.onabort = () => { reject(t.error); db.close(); };
    }));
  }

  function reqP(request) {
    return new Promise((resolve, reject) => {
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
  }

  const Snapshot = {
    /** Anahtara değer yaz (online iken taze veriyle). value JSON-serileştirilebilir olmalı. */
    async set(key, value) {
      return tx('readwrite', store => reqP(store.put({ key: key, value: value, ts: Date.now() })));
    },
    /** Anahtardaki değeri döndür; yoksa null. */
    async get(key) {
      const row = await tx('readonly', store => reqP(store.get(key)));
      return row ? row.value : null;
    },
    /** Anahtarın yazıldığı zaman damgası (ms) ya da null (bayatlık göstergesi için). */
    async ts(key) {
      const row = await tx('readonly', store => reqP(store.get(key)));
      return row ? (row.ts || null) : null;
    }
  };

  global.Snapshot = Snapshot;
})(typeof self !== 'undefined' ? self : this);
