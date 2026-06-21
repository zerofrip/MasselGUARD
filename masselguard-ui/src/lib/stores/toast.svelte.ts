export type ToastKind = 'success' | 'error' | 'info';

export type ToastItem = {
  id: number;
  kind: ToastKind;
  message: string;
};

let nextId = 1;

export const toastState = $state<{ items: ToastItem[] }>({ items: [] });

export function showToast(kind: ToastKind, message: string, durationMs = 4000) {
  const id = nextId++;
  toastState.items = [...toastState.items, { id, kind, message }];
  setTimeout(() => {
    toastState.items = toastState.items.filter((t) => t.id !== id);
  }, durationMs);
}

export function dismissToast(id: number) {
  toastState.items = toastState.items.filter((t) => t.id !== id);
}
