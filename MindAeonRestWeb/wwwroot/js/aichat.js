/* ===== Bağlam-farkında AI Chat widget'ı (istemci) =====
   - FAB → panel aç/kapa. Mesaj → fetch POST /BackOffice/AiAsk?handler=Ask (RequestVerificationToken header).
   - Bağlam: yalnız path/title + form ETİKET adları gönderilir (DEĞER YOK → KVKK/enumerasyon güvenli).
   - Ses (Web Speech API, tr-TR): 🔊 açıkken yanıtı seslendir; 🎤 ile sesli sor. Destek yoksa mic gizlenir (feature-detect).
   - Native alert/confirm/prompt YOK → tüm geri bildirim panel içi baloncuklarla.
   - Panel/ses durumu localStorage'da hatırlanır. Enter=gönder, Shift+Enter=yeni satır. */
(function () {
  "use strict";

  var fab = document.getElementById("ai-fab");
  var panel = document.getElementById("ai-panel");
  if (!fab || !panel) return;

  var msgs = panel.querySelector("#ai-msgs");
  var input = panel.querySelector("#ai-input");
  var sendBtn = panel.querySelector("#ai-send");
  var micBtn = panel.querySelector("#ai-mic");
  var voiceBtn = panel.querySelector("#ai-voice-toggle");
  var closeBtn = panel.querySelector("#ai-close");
  var tokenEl = panel.querySelector('input[name="__RequestVerificationToken"]');
  var token = tokenEl ? tokenEl.value : "";

  var LS_OPEN = "mar.ai.open";
  var LS_VOICE = "mar.ai.voice";
  var busy = false;

  /* ---------- Panel aç/kapa ---------- */
  function setOpen(open) {
    if (open) {
      panel.classList.add("open");
      panel.setAttribute("aria-hidden", "false");
      fab.classList.add("hidden");
      try { localStorage.setItem(LS_OPEN, "1"); } catch (e) {}
      setTimeout(function () { if (input) input.focus(); }, 180);
    } else {
      panel.classList.remove("open");
      panel.setAttribute("aria-hidden", "true");
      fab.classList.remove("hidden");
      try { localStorage.setItem(LS_OPEN, "0"); } catch (e) {}
      stopListening();
    }
  }

  fab.addEventListener("click", function () { setOpen(true); });
  if (closeBtn) closeBtn.addEventListener("click", function () { setOpen(false); });

  /* ---------- Sesli çıkış toggle ---------- */
  var voiceOn = false;
  try { voiceOn = localStorage.getItem(LS_VOICE) === "1"; } catch (e) {}
  function reflectVoice() {
    if (!voiceBtn) return;
    voiceBtn.classList.toggle("on", voiceOn);
    voiceBtn.setAttribute("aria-pressed", voiceOn ? "true" : "false");
  }
  if (voiceBtn) {
    reflectVoice();
    voiceBtn.addEventListener("click", function () {
      voiceOn = !voiceOn;
      try { localStorage.setItem(LS_VOICE, voiceOn ? "1" : "0"); } catch (e) {}
      reflectVoice();
      if (!voiceOn && window.speechSynthesis) { try { window.speechSynthesis.cancel(); } catch (e) {} }
    });
  }

  /* ---------- Baloncuk / render ---------- */
  function scrollDown() { msgs.scrollTop = msgs.scrollHeight; }

  function addBubble(text, kind) {
    var b = document.createElement("div");
    b.className = "ai-bubble " + kind;
    b.textContent = text;
    msgs.appendChild(b);
    scrollDown();
    return b;
  }

  function addTable(table) {
    if (!table || !table.columns || !table.rows || !table.rows.length) return;
    var wrap = document.createElement("div");
    wrap.className = "ai-table-wrap";
    var t = document.createElement("table");
    t.className = "ai-table";

    var thead = document.createElement("thead");
    var htr = document.createElement("tr");
    table.columns.forEach(function (c) {
      var th = document.createElement("th");
      if (c.numeric) th.className = "ar";
      th.textContent = c.header || "";
      htr.appendChild(th);
    });
    thead.appendChild(htr);
    t.appendChild(thead);

    var tbody = document.createElement("tbody");
    table.rows.forEach(function (row) {
      var tr = document.createElement("tr");
      table.columns.forEach(function (c, i) {
        var td = document.createElement("td");
        if (c.numeric) td.className = "ar";
        td.textContent = row[i] != null ? row[i] : "";
        tr.appendChild(td);
      });
      tbody.appendChild(tr);
    });
    t.appendChild(tbody);

    if (table.footer && table.footer.length) {
      var tfoot = document.createElement("tfoot");
      var ftr = document.createElement("tr");
      table.columns.forEach(function (c, i) {
        var td = document.createElement("td");
        if (c.numeric) td.className = "ar";
        td.textContent = table.footer[i] != null ? table.footer[i] : "";
        ftr.appendChild(td);
      });
      tfoot.appendChild(ftr);
      t.appendChild(tfoot);
    }

    wrap.appendChild(t);
    msgs.appendChild(wrap);
    scrollDown();
  }

  function addTyping() {
    var d = document.createElement("div");
    d.className = "ai-typing";
    d.innerHTML = "<span></span><span></span><span></span>";
    msgs.appendChild(d);
    scrollDown();
    return d;
  }

  /* ---------- Bağlam topla (yalnız etiket adları — DEĞER GÖNDERİLMEZ) ---------- */
  // KVKK: label.textContent iç <select>/<option> metnini de içerir (cari/banka/kullanıcı ADLARI sızabilir).
  // Bu yüzden YALNIZ etiketin kendi doğrudan metin (nodeType===3) düğümlerini oku; iç kontrol/option değerlerini atla.
  function labelTitle(l) {
    var out = "";
    try {
      var kids = l.childNodes || [];
      for (var i = 0; i < kids.length; i++) {
        if (kids[i].nodeType === 3) out += kids[i].nodeValue || "";
      }
    } catch (e) {}
    return out.replace(/\s+/g, " ").trim();
  }
  function collectContext() {
    var labels = [];
    try {
      labels = Array.prototype.slice
        .call(document.querySelectorAll("form label"))
        .map(labelTitle)
        .filter(Boolean)
        .slice(0, 40);
    } catch (e) {}
    return {
      path: location.pathname,
      title: document.title,
      fields: labels
    };
  }

  /* ---------- Gönder ---------- */
  function send(question) {
    var q = (question != null ? question : (input ? input.value : "")).trim();
    if (!q || busy) return;
    busy = true;
    addBubble(q, "user");
    if (input) { input.value = ""; autoGrow(); }

    var typing = addTyping();

    fetch("/BackOffice/AiAsk?handler=Ask", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "RequestVerificationToken": token
      },
      credentials: "same-origin",
      body: JSON.stringify({ question: q, context: collectContext() })
    })
      .then(function (r) {
        return r.json().catch(function () { return { error: "Sunucu yanıtı okunamadı." }; });
      })
      .then(function (data) {
        if (typing.parentNode) typing.parentNode.removeChild(typing);
        if (!data || data.error) {
          var msg = (data && data.error === "forbidden")
            ? "Bu özellik için yetkiniz yok."
            : (data && data.error ? data.error : "Bir hata oluştu.");
          addBubble(msg, "error");
        } else {
          var text = data.text || "";
          if (text) {
            addBubble(text, "bot");
            speak(text);
          } else if (!data.table) {
            addBubble("Yanıt bulunamadı.", "bot");
          }
          if (data.table) addTable(data.table);
        }
      })
      .catch(function () {
        if (typing.parentNode) typing.parentNode.removeChild(typing);
        addBubble("Bağlantı hatası. Lütfen tekrar deneyin.", "error");
      })
      .then(function () { busy = false; });
  }

  if (sendBtn) sendBtn.addEventListener("click", function () { send(); });

  /* ---------- Giriş: Enter=gönder, Shift+Enter=yeni satır + auto-grow ---------- */
  function autoGrow() {
    if (!input) return;
    input.style.height = "auto";
    input.style.height = Math.min(input.scrollHeight, 120) + "px";
  }
  if (input) {
    input.addEventListener("input", autoGrow);
    input.addEventListener("keydown", function (e) {
      if (e.key === "Enter" && !e.shiftKey) {
        e.preventDefault();
        send();
      }
    });
  }

  /* ---------- Sesli çıkış (TTS, tr-TR) ---------- */
  function pickTrVoice() {
    if (!window.speechSynthesis) return null;
    var voices = window.speechSynthesis.getVoices() || [];
    for (var i = 0; i < voices.length; i++) {
      if (voices[i].lang && voices[i].lang.toLowerCase().indexOf("tr") === 0) return voices[i];
    }
    return null;
  }
  function speak(text) {
    if (!voiceOn || !window.speechSynthesis || !window.SpeechSynthesisUtterance) return;
    try {
      window.speechSynthesis.cancel();
      var u = new SpeechSynthesisUtterance(text);
      u.lang = "tr-TR";
      var v = pickTrVoice();
      if (v) u.voice = v;
      window.speechSynthesis.speak(u);
    } catch (e) {}
  }

  /* ---------- Sesli giriş (STT, tr-TR) — feature-detect ---------- */
  var Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
  var recog = null;
  var listening = false;

  function stopListening() {
    if (recog && listening) { try { recog.stop(); } catch (e) {} }
  }

  if (Recognition && micBtn) {
    recog = new Recognition();
    recog.lang = "tr-TR";
    recog.interimResults = false;
    recog.maxAlternatives = 1;

    recog.onresult = function (ev) {
      var said = "";
      try { said = ev.results[0][0].transcript || ""; } catch (e) {}
      if (said && input) {
        input.value = said;
        autoGrow();
        send(said);
      }
    };
    recog.onend = function () {
      listening = false;
      micBtn.classList.remove("listening");
    };
    recog.onerror = function () {
      listening = false;
      micBtn.classList.remove("listening");
    };

    micBtn.addEventListener("click", function () {
      if (listening) { stopListening(); return; }
      try {
        recog.start();
        listening = true;
        micBtn.classList.add("listening");
      } catch (e) {
        listening = false;
        micBtn.classList.remove("listening");
      }
    });
  } else if (micBtn) {
    // Tarayıcı desteklemiyor → mic düğmesini gizle (hata verme).
    micBtn.classList.add("hidden");
  }

  /* ---------- Açılışta durum geri yükle ---------- */
  var startOpen = false;
  try { startOpen = localStorage.getItem(LS_OPEN) === "1"; } catch (e) {}
  if (startOpen) setOpen(true);
})();
