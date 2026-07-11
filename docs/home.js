(function () {
  'use strict';

  var STORAGE_KEY = 'onge-theme';

  var THEMES = {
    mocha:     { label: 'Mocha',     capSuffix: 'Mocha',     swatch: '#cba6f7' },
    macchiato: { label: 'Macchiato', capSuffix: 'Macchiato', swatch: '#c6a0f6' },
    frappe:    { label: 'Frappé',    capSuffix: 'Frappe',    swatch: '#ca9ee6' },
    latte:     { label: 'Latte',     capSuffix: 'Latte',     swatch: '#8839ef' }
  };

  var ORDER = ['mocha', 'macchiato', 'frappe', 'latte'];

  function osDefault() {
    return window.matchMedia('(prefers-color-scheme: light)').matches ? 'latte' : 'mocha';
  }

  function resolveTheme() {
    var saved = localStorage.getItem(STORAGE_KEY);
    return saved && THEMES[saved] ? saved : osDefault();
  }

  function capSrc(cap, suffix) {
    return 'caps/' + cap + '_' + suffix + '.png';
  }

  function setTheme(id, persist) {
    if (!THEMES[id]) return;
    document.documentElement.setAttribute('data-theme', id);

    var suffix = THEMES[id].capSuffix;
    document.querySelectorAll('.theme-cap').forEach(function (img) {
      var cap = img.getAttribute('data-cap');
      if (!cap) return;
      var next = capSrc(cap, suffix);
      if (img.getAttribute('src') === next) return;
      img.classList.add('fading');
      img.addEventListener('load', function onLoad() {
        img.classList.remove('fading');
        img.removeEventListener('load', onLoad);
      });
      img.setAttribute('src', next);
    });

    var btn = document.getElementById('theme-btn-label');
    if (btn) btn.textContent = THEMES[id].label;

    document.querySelectorAll('.theme-option').forEach(function (opt) {
      var active = opt.getAttribute('data-theme-id') === id;
      opt.setAttribute('aria-current', active ? 'true' : 'false');
    });

    if (persist !== false) {
      localStorage.setItem(STORAGE_KEY, id);
    }
  }

  function initSwitcher() {
    var root = document.getElementById('theme-switcher');
    var btn = document.getElementById('theme-btn');
    var menu = document.getElementById('theme-menu');
    if (!root || !btn || !menu) return;

    btn.addEventListener('click', function (e) {
      e.stopPropagation();
      var open = root.classList.toggle('open');
      btn.setAttribute('aria-expanded', open ? 'true' : 'false');
    });

    menu.querySelectorAll('.theme-option').forEach(function (opt) {
      opt.addEventListener('click', function () {
        setTheme(opt.getAttribute('data-theme-id'));
        root.classList.remove('open');
        btn.setAttribute('aria-expanded', 'false');
      });
    });

    document.addEventListener('click', function () {
      root.classList.remove('open');
      btn.setAttribute('aria-expanded', 'false');
    });

    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape') {
        root.classList.remove('open');
        btn.setAttribute('aria-expanded', 'false');
      }
    });
  }

  setTheme(resolveTheme(), false);
  initSwitcher();
})();
