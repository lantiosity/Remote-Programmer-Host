'use strict';
// Runs synchronously before CSS. Native settings win over an older browser cache.
(() => {
  let theme = window.__wirelinkSettings?.theme;
  if (!theme) { try { theme = localStorage.getItem('wirelink-theme'); } catch { } }
  const dark = theme === 'dark' || theme !== 'light' && matchMedia('(prefers-color-scheme: dark)').matches;
  document.documentElement.dataset.theme = dark ? 'dark' : 'light';
  document.documentElement.style.colorScheme = dark ? 'dark' : 'light';
})();
