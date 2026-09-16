/* MindAeonRest Web — tema geçişi (Koyu/Açık/Okyanus). Cookie ile kalıcı; sunucu <html data-theme> ile ilk render'da uygular (flaş yok). */
(function () {
  'use strict';
  var THEMES = ['dark', 'light', 'ocean'];

  function apply(t) {
    if (THEMES.indexOf(t) < 0) t = 'dark';
    document.documentElement.setAttribute('data-theme', t);
    document.cookie = 'mar.theme=' + t + ';path=/;max-age=31536000;samesite=lax';
    document.querySelectorAll('[data-theme-set]').forEach(function (b) {
      b.classList.toggle('on', b.getAttribute('data-theme-set') === t);
    });
  }

  window.MarTheme = { apply: apply };

  document.addEventListener('click', function (e) {
    var b = e.target.closest ? e.target.closest('[data-theme-set]') : null;
    if (b) apply(b.getAttribute('data-theme-set'));
  });

  // İlk yüklemede aktif butonu işaretle (data-theme sunucudan gelir)
  document.addEventListener('DOMContentLoaded', function () {
    var cur = document.documentElement.getAttribute('data-theme') || 'dark';
    document.querySelectorAll('[data-theme-set]').forEach(function (b) {
      b.classList.toggle('on', b.getAttribute('data-theme-set') === cur);
    });
  });
})();
