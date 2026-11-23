'use strict';
// Apply the saved palette before styles are loaded to avoid a flash of the default theme.
(() => {
  const bases = {
    light: { accent: '#087f80', background: '#f5f7fb', surface: '#ffffff', sidebar: '#ffffff', header: '#fcfdff', ink: '#192637' },
    dark: { accent: '#168b85', background: '#111925', surface: '#192433', sidebar: '#151f2d', header: '#141e2b', ink: '#d9e3f0' }
  };
  const fields = ['accent', 'background', 'surface'];
  const mode = value => ['system', 'light', 'dark'].includes(value) ? value : 'system';
  const valid = value => value != null && fields.every(key =>
    typeof value[key] === 'string' && /^#[\da-f]{6}$/i.test(value[key])) ? value : null;
  const channels = hex => [1, 3, 5].map(index => parseInt(hex.slice(index, index + 2), 16));
  function mix(base, tint, amount) {
    const destination = channels(tint);
    return '#' + channels(base).map((source, index) =>
      Math.round((source * (100 - amount) + destination[index] * amount) / 100).toString(16).padStart(2, '0')).join('');
  }
  function luminance(color) {
    const [r, g, b] = channels(color).map(channel => {
      const value = channel / 255;
      return value <= .04045 ? value / 12.92 : ((value + .055) / 1.055) ** 2.4;
    });
    return .2126 * r + .7152 * g + .0722 * b;
  }
  function contrast(first, second) {
    const a = luminance(first), b = luminance(second);
    return (Math.max(a, b) + .05) / (Math.min(a, b) + .05);
  }
  const defaults = dark => bases[dark ? 'dark' : 'light'];
  const isDark = value => mode(value) === 'dark' || mode(value) === 'system' && matchMedia('(prefers-color-scheme: dark)').matches;
  const imageId = value => typeof value === 'string' && /^[0-9a-f]{32}$/.test(value);
  function appearance(value) {
    const clamp = (number, minimum, fallback) => Number.isInteger(number) ? Math.max(minimum, Math.min(100, number)) : fallback;
    return { opacity: clamp(value?.opacity, 50, 100), image_opacity: clamp(value?.image_opacity, 0, 45),
      image: imageId(value?.image) ? value.image : null, frosted: value?.frosted === true };
  }

  function apply(value, custom, effects) {
    const dark = isDark(value);
    const root = document.documentElement;
    root.dataset.theme = dark ? 'dark' : 'light';
    root.style.colorScheme = dark ? 'dark' : 'light';
    const wallpaper = appearance(effects);
    root.toggleAttribute('data-wallpaper', wallpaper.image !== null);
    root.toggleAttribute('data-frosted', wallpaper.frosted);
    if (wallpaper.image) {
      root.style.setProperty('--ui-wallpaper', `url("https://wirelink-theme.example/${wallpaper.image}.png")`);
      root.style.setProperty('--ui-wallpaper-opacity', String(wallpaper.image_opacity / 100));
    }
    const colors = valid(custom);
    root.toggleAttribute('data-custom-theme', colors !== null);
    if (!colors) return dark;

    const base = defaults(dark);
    const surface = mix(base.surface, colors.surface, 14);
    const sidebar = mix(base.sidebar, colors.surface, 14);
    const selected = mix(sidebar, colors.accent, dark ? 18 : 10);
    let accentInk = colors.accent;
    for (let amount = 10; amount <= 100 && contrast(accentInk, selected) < 4.5; amount += 10)
      accentInk = mix(colors.accent, base.ink, amount);
    const buttonInk = luminance(colors.accent) > .179 ? '#000000' : '#ffffff';
    const properties = {
      'ui-accent': colors.accent,
      'ui-accent-ink': accentInk,
      'ui-button-ink': buttonInk,
      'ui-accent-hover': mix(colors.accent, buttonInk === '#000000' ? '#ffffff' : '#000000', 14),
      'ui-workspace': mix(base.background, colors.background, 18),
      'ui-surface': surface,
      'ui-sidebar': sidebar,
      'ui-header': mix(base.header, colors.surface, 14),
      'ui-selection': selected
    };
    for (const [name, color] of Object.entries(properties)) root.style.setProperty(`--${name}`, color);
    return dark;
  }

  window.WireLinkTheme = { apply, appearance, defaults, fields, isDark, mode, valid };
  const settings = window.__wirelinkSettings;
  let preference = settings?.theme;
  if (!preference) { try { preference = localStorage.getItem('wirelink-theme'); } catch { } }
  apply(preference, settings?.custom_theme, settings?.theme_appearance);
})();
