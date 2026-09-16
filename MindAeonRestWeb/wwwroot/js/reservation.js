/* FAZ 5c — Online rezervasyon + bekleme listesi müşteri istemcisi (anonim, token'lı).
   Akış: gün/saat/kişi + ad/telefon → müsaitlik canlı kontrol edilir → "Rezervasyon Yap".
   Uygun masa varsa rezervasyon açılır (onay SMS'i); doluysa müşteri BEKLEME LİSTESİNE alınır.
   Native alert/confirm YOK — durum/onay sayfa içi custom bileşenle gösterilir. Mobil-öncelikli, tr-TR. */
(function () {
  "use strict";
  var TOKEN = window.RESERVATION_TOKEN || "";
  var app = document.getElementById("rz-app");

  var state = {
    branch: "",
    waiting: 0,
    party: 2,
    availability: null,   // null=bilinmiyor, true=müsait, false=dolu
    checking: false,
    busy: false
  };

  function esc(s) {
    return String(s == null ? "" : s).replace(/[&<>"']/g, function (c) {
      return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c];
    });
  }
  function byId(id) { return document.getElementById(id); }
  function pad(n) { return (n < 10 ? "0" : "") + n; }
  function todayStr() { var d = new Date(); return d.getFullYear() + "-" + pad(d.getMonth() + 1) + "-" + pad(d.getDate()); }
  function nextHalfHour() {
    var d = new Date(); d.setMinutes(d.getMinutes() > 30 ? 60 : 30, 0, 0);
    return pad(d.getHours()) + ":" + pad(d.getMinutes());
  }

  function invalid() {
    app.innerHTML =
      '<div class="rz-wrap"><div class="rz-card">' +
      '<div class="rz-logo">🔗</div>' +
      '<h1 class="rz-title">Bağlantı geçersiz</h1>' +
      '<p class="rz-sub">Bu rezervasyon bağlantısı geçersiz ya da süresi dolmuş.</p>' +
      "</div></div>";
  }

  function renderForm() {
    app.innerHTML =
      '<div class="rz-wrap"><div class="rz-card">' +
      '<div class="rz-logo">🍽️</div>' +
      '<h1 class="rz-title">Rezervasyon</h1>' +
      '<p class="rz-sub" id="rz-branch">' + (state.branch ? esc(state.branch) : "Masanızı ayırtın") + "</p>" +

      '<label class="rz-lbl" for="rz-date">Tarih</label>' +
      '<input type="date" class="rz-field" id="rz-date" min="' + todayStr() + '" value="' + todayStr() + '" />' +

      '<label class="rz-lbl" for="rz-time">Saat</label>' +
      '<input type="time" class="rz-field" id="rz-time" value="' + nextHalfHour() + '" step="1800" />' +

      '<label class="rz-lbl">Kişi Sayısı</label>' +
      '<div class="rz-stepper">' +
      '<button type="button" class="rz-step" id="rz-minus" aria-label="Azalt">−</button>' +
      '<span class="rz-party" id="rz-party">' + state.party + "</span>" +
      '<button type="button" class="rz-step" id="rz-plus" aria-label="Arttır">+</button>' +
      "</div>" +

      '<div class="rz-avail" id="rz-avail"></div>' +

      '<label class="rz-lbl" for="rz-name">Ad Soyad</label>' +
      '<input type="text" class="rz-field" id="rz-name" placeholder="Adınız Soyadınız" autocomplete="name" />' +

      '<label class="rz-lbl" for="rz-phone">Telefon</label>' +
      '<input type="tel" class="rz-field" id="rz-phone" placeholder="05xx xxx xx xx" inputmode="tel" autocomplete="tel" />' +

      '<label class="rz-lbl" for="rz-note">Not (opsiyonel)</label>' +
      '<textarea class="rz-field" id="rz-note" placeholder="Özel bir talebiniz varsa yazın"></textarea>' +

      '<button type="button" class="rz-btn" id="rz-send">Rezervasyon Yap</button>' +
      '<div class="rz-error" id="rz-error"></div>' +
      '<div class="rz-hint" id="rz-hint">' +
      (state.waiting > 0 ? "Şu an " + state.waiting + " kişi bekleme listesinde." : "Uygun masa yoksa bekleme listesine alınırsınız.") +
      "</div>" +
      "</div></div>";

    byId("rz-minus").addEventListener("click", function () { setParty(state.party - 1); });
    byId("rz-plus").addEventListener("click", function () { setParty(state.party + 1); });
    byId("rz-date").addEventListener("change", checkAvailability);
    byId("rz-time").addEventListener("change", checkAvailability);
    byId("rz-send").addEventListener("click", submit);
    checkAvailability();
  }

  function setParty(v) {
    state.party = Math.max(1, Math.min(50, v | 0));
    byId("rz-party").textContent = state.party;
    checkAvailability();
  }

  var _availTimer = null;
  function checkAvailability() {
    var date = byId("rz-date").value, time = byId("rz-time").value;
    var av = byId("rz-avail");
    if (!date || !time) { av.className = "rz-avail"; av.textContent = ""; state.availability = null; updateSendLabel(); return; }
    av.className = "rz-avail checking"; av.textContent = "Müsaitlik kontrol ediliyor…";
    state.checking = true;
    clearTimeout(_availTimer);
    _availTimer = setTimeout(function () {
      var qs = "?t=" + encodeURIComponent(TOKEN) +
        "&date=" + encodeURIComponent(date) + "&time=" + encodeURIComponent(time) +
        "&party=" + state.party;
      fetch("/api/public/reservation/availability" + qs)
        .then(function (r) { return r.ok ? r.json() : null; })
        .then(function (j) {
          state.checking = false;
          if (!j) { av.className = "rz-avail"; av.textContent = ""; state.availability = null; updateSendLabel(); return; }
          state.availability = !!j.available;
          if (j.available) {
            av.className = "rz-avail ok"; av.textContent = "✓ Uygun masa var";
          } else {
            av.className = "rz-avail full"; av.textContent = "Bu saatte masa dolu — bekleme listesine katılabilirsiniz";
          }
          updateSendLabel();
        })
        .catch(function () { state.checking = false; av.className = "rz-avail"; av.textContent = ""; state.availability = null; updateSendLabel(); });
    }, 300);
  }

  function updateSendLabel() {
    var btn = byId("rz-send");
    if (!btn) return;
    btn.textContent = state.availability === false ? "Bekleme Listesine Katıl" : "Rezervasyon Yap";
  }

  function submit() {
    if (state.busy) return;
    var errEl = byId("rz-error");
    errEl.textContent = "";

    var name = (byId("rz-name").value || "").trim();
    var phone = (byId("rz-phone").value || "").trim();
    var date = byId("rz-date").value, time = byId("rz-time").value;
    var note = (byId("rz-note").value || "").trim();

    if (!name) { errEl.textContent = "Lütfen ad soyad girin."; return; }
    if (!phone) { errEl.textContent = "Lütfen telefon numaranızı girin."; return; }
    if (!date || !time) { errEl.textContent = "Lütfen tarih ve saat seçin."; return; }

    state.busy = true;
    var btn = byId("rz-send");
    btn.disabled = true;
    var oldLabel = btn.textContent;
    btn.textContent = "Gönderiliyor…";

    var body = { name: name, phone: phone, date: date, time: time, party: state.party, durationMin: 120, note: note };
    fetch("/api/public/reservation?t=" + encodeURIComponent(TOKEN), {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body)
    })
      .then(function (r) { return r.json().then(function (j) { return { ok: r.ok, data: j }; }); })
      .then(function (res) {
        if (!res.ok) throw new Error((res.data && res.data.message) || "İşlem tamamlanamadı.");
        done(res.data);
      })
      .catch(function (e) {
        state.busy = false;
        btn.disabled = false;
        btn.textContent = oldLabel;
        errEl.textContent = e && e.message ? e.message : "Bir hata oluştu, tekrar deneyin.";
      });
  }

  function done(data) {
    var isReservation = data && data.mode === "reservation";
    var logo = isReservation ? "✅" : "⏳";
    var title = isReservation ? "Rezervasyonunuz alındı!" : "Bekleme listesindesiniz";
    var msg = (data && data.message) ||
      (isReservation ? "Onay mesajı gönderildi." : "Yer açılınca sizi arayacağız.");
    var codeLine = isReservation && data.code
      ? '<div class="rz-code">Rezervasyon Kodu: <b>' + esc(data.code) + "</b></div>"
      : "";
    app.innerHTML =
      '<div class="rz-wrap"><div class="rz-card rz-done">' +
      '<div class="rz-logo">' + logo + "</div>" +
      '<h1 class="rz-title">' + esc(title) + "</h1>" +
      '<p class="rz-sub">' + esc(msg) + "</p>" +
      codeLine +
      '<button type="button" class="rz-btn rz-ghost" id="rz-again">Yeni Rezervasyon</button>' +
      "</div></div>";
    byId("rz-again").addEventListener("click", function () { state.busy = false; state.availability = null; renderForm(); });
  }

  function boot() {
    if (!TOKEN) { invalid(); return; }
    renderForm();
    fetch("/api/public/reservation/info?t=" + encodeURIComponent(TOKEN))
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (j) {
        if (!j) return;
        if (j.branch) {
          state.branch = j.branch;
          var el = byId("rz-branch"); if (el) el.textContent = j.branch;
        }
        state.waiting = j.waiting || 0;
        var hint = byId("rz-hint");
        if (hint && state.waiting > 0) hint.textContent = "Şu an " + state.waiting + " kişi bekleme listesinde.";
      })
      .catch(function () { /* başlık opsiyonel */ });
  }

  boot();
})();
