/* FAZ 5d — Geri bildirim / anket müşteri istemcisi (anonim, token'lı).
   Akış: yıldız seç (1-5) + opsiyonel yorum → POST /api/public/feedback?t=token.
   HUNİ: API yüksek puanda { redirect } döndürürse müşteri Google yorum sayfasına yönlendirilir; düşük puan
   yalnız içeride kaydedilir → sayfa içi teşekkür gösterilir. Native alert/confirm YOK (custom sayfa içi durum). */
(function () {
  "use strict";
  var TOKEN = window.FEEDBACK_TOKEN || "";
  var app = document.getElementById("fb-app");

  var RATING_LABELS = { 1: "Çok kötü", 2: "Kötü", 3: "Orta", 4: "İyi", 5: "Mükemmel" };
  var state = { rating: 0, branch: "", busy: false };

  function esc(s) {
    return String(s == null ? "" : s).replace(/[&<>"']/g, function (c) {
      return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c];
    });
  }

  function invalid() {
    app.innerHTML =
      '<div class="fb-wrap"><div class="fb-card">' +
      '<div class="fb-logo">🔗</div>' +
      '<h1 class="fb-title">Bağlantı geçersiz</h1>' +
      '<p class="fb-sub">Bu değerlendirme bağlantısı geçersiz ya da süresi dolmuş.</p>' +
      "</div></div>";
  }

  function renderForm() {
    var stars = "";
    for (var i = 1; i <= 5; i++) {
      stars += '<button type="button" class="fb-star" data-v="' + i + '" aria-label="' + i + ' yıldız">★</button>';
    }
    app.innerHTML =
      '<div class="fb-wrap"><div class="fb-card">' +
      '<div class="fb-logo">🍽️</div>' +
      '<h1 class="fb-title">Deneyiminiz nasıldı?</h1>' +
      '<p class="fb-sub">' + (state.branch ? esc(state.branch) : "Görüşleriniz bizim için değerli") + "</p>" +
      '<div class="fb-stars" id="fb-stars">' + stars + "</div>" +
      '<div class="fb-rating-label" id="fb-label"></div>' +
      '<textarea class="fb-field" id="fb-comment" placeholder="Eklemek istedikleriniz (opsiyonel)"></textarea>' +
      '<input type="tel" class="fb-field" id="fb-phone" placeholder="Telefon (opsiyonel)" inputmode="tel" />' +
      '<button type="button" class="fb-btn" id="fb-send" disabled>Gönder</button>' +
      '<div class="fb-error" id="fb-error"></div>' +
      '<div class="fb-hint">Yıldız seçerek değerlendirmenizi tamamlayın.</div>' +
      "</div></div>";

    var starsEl = document.getElementById("fb-stars");
    starsEl.addEventListener("click", function (e) {
      var b = e.target.closest(".fb-star");
      if (!b) return;
      setRating(parseInt(b.getAttribute("data-v"), 10));
    });
    document.getElementById("fb-send").addEventListener("click", submit);
  }

  function setRating(v) {
    state.rating = v;
    var stars = document.querySelectorAll(".fb-star");
    for (var i = 0; i < stars.length; i++) {
      stars[i].classList.toggle("on", i < v);
    }
    document.getElementById("fb-label").textContent = RATING_LABELS[v] || "";
    document.getElementById("fb-send").disabled = v < 1 || state.busy;
  }

  function submit() {
    if (state.rating < 1 || state.busy) return;
    state.busy = true;
    var btn = document.getElementById("fb-send");
    var errEl = document.getElementById("fb-error");
    btn.disabled = true;
    btn.textContent = "Gönderiliyor…";
    errEl.textContent = "";

    var body = {
      rating: state.rating,
      comment: (document.getElementById("fb-comment").value || "").trim(),
      phone: (document.getElementById("fb-phone").value || "").trim()
    };

    fetch("/api/public/feedback?t=" + encodeURIComponent(TOKEN), {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body)
    })
      .then(function (r) {
        return r.json().then(function (j) { return { ok: r.ok, data: j }; });
      })
      .then(function (res) {
        if (!res.ok) {
          throw new Error((res.data && res.data.message) || "Gönderilemedi.");
        }
        // HUNİ: yüksek puanda API Google URL'i döndürür → yönlendir.
        if (res.data && res.data.redirect) {
          done(true, res.data.redirect);
          // Kısa teşekkürden sonra Google yorum sayfasına yönlendir.
          setTimeout(function () { window.location.href = res.data.redirect; }, 1400);
        } else {
          done(false, null);
        }
      })
      .catch(function (e) {
        state.busy = false;
        btn.disabled = false;
        btn.textContent = "Gönder";
        errEl.textContent = e && e.message ? e.message : "Bir hata oluştu, tekrar deneyin.";
      });
  }

  function done(redirecting, url) {
    var extra = redirecting
      ? '<div class="fb-redirect">Google\'da da paylaşmak ister misiniz? ' +
        '<a href="' + esc(url) + '">Buraya dokunun</a></div>'
      : "";
    app.innerHTML =
      '<div class="fb-wrap"><div class="fb-card fb-done">' +
      '<div class="fb-logo">🙏</div>' +
      '<h1 class="fb-title">Teşekkür ederiz!</h1>' +
      '<p class="fb-sub">Değerlendirmeniz bize ulaştı.</p>' +
      extra +
      "</div></div>";
  }

  function boot() {
    if (!TOKEN) { invalid(); return; }
    renderForm();
    // Şube adını (başlık) getir — başarısız olursa sessizce genel başlıkta kal.
    fetch("/api/public/feedback/info?t=" + encodeURIComponent(TOKEN))
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (j) {
        if (j && j.branch) {
          state.branch = j.branch;
          var sub = document.querySelector(".fb-sub");
          if (sub) sub.textContent = j.branch;
        }
      })
      .catch(function () { /* başlık opsiyonel */ });
  }

  boot();
})();
