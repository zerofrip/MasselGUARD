const STORAGE_KEY = 'mg-event-last-seq';

export function loadLastSeq(): number | null {
  if (typeof localStorage === 'undefined') return null;
  const v = localStorage.getItem(STORAGE_KEY);
  if (!v) return null;
  const n = Number(v);
  return Number.isFinite(n) && n > 0 ? n : null;
}

export function saveLastSeq(seq: number | null) {
  if (typeof localStorage === 'undefined' || seq == null || seq <= 0) return;
  localStorage.setItem(STORAGE_KEY, String(seq));
}

export function clearLastSeq() {
  if (typeof localStorage === 'undefined') return;
  localStorage.removeItem(STORAGE_KEY);
}
