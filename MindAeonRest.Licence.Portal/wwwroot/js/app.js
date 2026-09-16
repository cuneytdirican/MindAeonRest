/* MindAeon Portal — küçük yardımcılar (tema, rail, toast, lucide). */
(function () {
  "use strict";

  // ---- Tema (cookie kalıcılığı) ----
  function getCookie(name) {
    return document.cookie.split("; ").find(r => r.startsWith(name + "="))?.split("=")[1];
  }
  function setCookie(name, value, days) {
    const d = new Date();
    d.setTime(d.getTime() + days * 864e5);
    document.cookie = `${name}=${value}; expires=${d.toUTCString()}; path=/; samesite=lax`;
  }
  function applyTheme(theme) {
    document.documentElement.setAttribute("data-theme", theme);
  }
  window.toggleTheme = function () {
    const next = document.documentElement.getAttribute("data-theme") === "dark" ? "light" : "dark";
    applyTheme(next);
    setCookie("theme", next, 365);
    if (window.lucide) lucide.createIcons();
  };
  // İlk yükleme: cookie -> sistem tercihi
  (function initTheme() {
    const saved = getCookie("theme");
    if (saved) applyTheme(saved);
    else if (window.matchMedia && matchMedia("(prefers-color-scheme: dark)").matches) applyTheme("dark");
  })();

  // ---- Sol rail daralt/genişlet ----
  window.toggleRail = function () {
    const shell = document.querySelector(".app-shell");
    if (!shell) return;
    shell.classList.toggle("rail-collapsed");
    setCookie("rail", shell.classList.contains("rail-collapsed") ? "1" : "0", 365);
  };
  (function initRail() {
    if (getCookie("rail") === "1") {
      document.addEventListener("DOMContentLoaded", () => {
        document.querySelector(".app-shell")?.classList.add("rail-collapsed");
      });
    }
  })();

  // ---- Toast ----
  window.toast = function (message, kind) {
    let host = document.querySelector(".toast-host");
    if (!host) {
      host = document.createElement("div");
      host.className = "toast-host";
      document.body.appendChild(host);
    }
    const el = document.createElement("div");
    el.className = "toast " + (kind || "info");
    el.textContent = message;
    host.appendChild(el);
    setTimeout(() => el.remove(), 4000);
  };

  // ---- Lucide ikonları ----
  document.addEventListener("DOMContentLoaded", function () {
    if (window.lucide) lucide.createIcons();
  });
  // htmx kısmi render sonrası ikonları tazele
  document.body.addEventListener("htmx:afterSwap", function () {
    if (window.lucide) lucide.createIcons();
  });
})();
