import { browser } from '$app/environment';

export type ThemeMode = 'dark' | 'light' | 'system';

function systemPref(): 'dark' | 'light' {
  if (!browser) return 'dark';
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

export function applyTheme(mode: ThemeMode) {
  if (!browser) return;
  const resolved = mode === 'system' ? systemPref() : mode;
  document.documentElement.dataset.theme = resolved;
}

export function loadTheme(): ThemeMode {
  if (!browser) return 'system';
  return (localStorage.getItem('mg-theme') as ThemeMode) || 'system';
}

export function saveTheme(mode: ThemeMode) {
  if (!browser) return;
  localStorage.setItem('mg-theme', mode);
  applyTheme(mode);
}
