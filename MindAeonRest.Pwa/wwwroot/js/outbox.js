/* ===== MindAeon Garson PWA — offline outbox (IndexedDB) =====
   FAZ 1d: internet gidince POS sipariş/ödeme mutasyonları kaybolmasın.
   Bekleyen istekler (method+url+body+headers+idempotencyKey+timestamp) IndexedDB'de saklanır;
   bağlantı gelince Service Worker Background Sync (veya online-event fallback) sırayla POST eder.
   idempotencyKey backend'de çift-göndermeyi emer (aynı key = aynı sonuç).

   ÖNEMLİ: Bu dosya HEM sayfa (window) HEM Service Worker (worker) bağlamında çalışır.
   Bu yüzden 'self' kullanır (ikisinde de tanımlı) ve window'a özel API'lere dokunmaz.
   Sayfada <script src="js/outbox.js"> ile (api.js'ten ÖNCE), SW'de importScripts ile yüklenir. */
(function (global) {
  'use strict';

  const DB_NAME = 'mag-outbox';
  const DB_VERSION = 1;
  const STORE = 'requests';

  function openDb() {
    return new Promise((resolve, reject) => {
      let req;
      try { req = global.indexedDB.open(DB_NAME, DB_VERSION); }
      catch (e) { reject(e); return; }
      req.onupgradeneeded = () => {
        const db = req.result;
        if (!db.objectStoreNames.contains(STORE)) {
          const os = db.createObjectStore(STORE, { keyPath: 'id', autoIncrement: true });
          os.createIndex('ts', 'timestamp', { unique: false });
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

  const Outbox = {
    /** Bekleyen bir mutasyon isteğini kuyruğa ekler. rec: {method,url,body,headers,idempotencyKey,timestamp?}.
     *  Atanan kayıt id'sini döndürür. */
    async enqueue(rec) {
      const row = {
        method: rec.method,
        url: rec.url,
        body: rec.body != null ? rec.body : null,   // JSON string ya da null
        headers: rec.headers || {},
        idempotencyKey: rec.idempotencyKey || null,
        timestamp: rec.timestamp || Date.now(),
        tag: rec.tag || null                         // debug/UI için (örn. 'send','settle')
      };
      return tx('readwrite', store => reqP(store.add(row)));
    },

    /** Tüm bekleyen istekleri FIFO (eklenme) sırasında döndürür. */
    async all() {
      return tx('readonly', store => reqP(store.getAll())).then(rows =>
        (rows || []).sort((a, b) => (a.id || 0) - (b.id || 0)));
    },

    /** Verilen id'li kaydı siler (başarıyla gönderilince). */
    async remove(id) {
      return tx('readwrite', store => reqP(store.delete(id)));
    },

    /** En eski kaydı çıkarıp döndürür (FIFO pop). Kuyruk boşsa null. */
    async dequeue() {
      return tx('readwrite', async store => {
        const rows = await reqP(store.getAll());
        if (!rows || !rows.length) return null;
        rows.sort((a, b) => (a.id || 0) - (b.id || 0));
        const first = rows[0];
        await reqP(store.delete(first.id));
        return first;
      });
    },

    /** Kuyruktaki bekleyen istek sayısı. */
    async count() {
      return tx('readonly', store => reqP(store.count()));
    }
  };

  global.Outbox = Outbox;
})(typeof self !== 'undefined' ? self : this);
