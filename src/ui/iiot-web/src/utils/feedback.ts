import { reactive } from 'vue';

export type FeedbackTone = 'info' | 'success' | 'warning' | 'error';

export interface FeedbackDialogOptions {
  type?: FeedbackTone;
  title?: string;
  message: string;
  details?: string[];
  confirmText?: string;
  cancelText?: string;
}

type FeedbackDialogKind = 'message' | 'confirm';

export interface FeedbackDialogState extends FeedbackDialogOptions {
  id: number;
  kind: FeedbackDialogKind;
  type: FeedbackTone;
  title: string;
  confirmText: string;
  cancelText: string;
  resolve?: (value: boolean) => void;
}

const defaultTitle: Record<FeedbackTone, string> = {
  info: '提示',
  success: '操作成功',
  warning: '请确认',
  error: '操作失败',
};

const state = reactive<{
  dialog: FeedbackDialogState | null;
}>({
  dialog: null,
});

let nextId = 1;

function normalizeDetails(details?: string[]) {
  return (details ?? [])
    .map((item) => item.trim())
    .filter(Boolean);
}

function createDialog(
  kind: FeedbackDialogKind,
  options: FeedbackDialogOptions,
  resolve?: (value: boolean) => void,
): FeedbackDialogState {
  const type = options.type ?? (kind === 'confirm' ? 'warning' : 'info');
  return {
    id: nextId++,
    kind,
    type,
    title: options.title?.trim() || defaultTitle[type],
    message: options.message,
    details: normalizeDetails(options.details),
    confirmText: options.confirmText?.trim() || '确定',
    cancelText: options.cancelText?.trim() || '取消',
    resolve,
  };
}

export function useFeedbackState() {
  return state;
}

export function notify(options: FeedbackDialogOptions) {
  state.dialog = createDialog('message', options);
}

export function notifyInfo(message: string, options: Omit<FeedbackDialogOptions, 'message' | 'type'> = {}) {
  notify({ ...options, type: 'info', message });
}

export function notifySuccess(message: string, options: Omit<FeedbackDialogOptions, 'message' | 'type'> = {}) {
  notify({ ...options, type: 'success', message });
}

export function notifyWarning(message: string, options: Omit<FeedbackDialogOptions, 'message' | 'type'> = {}) {
  notify({ ...options, type: 'warning', message });
}

export function notifyError(message: string, options: Omit<FeedbackDialogOptions, 'message' | 'type'> = {}) {
  notify({ ...options, type: 'error', message });
}

export function requestConfirmation(options: FeedbackDialogOptions): Promise<boolean> {
  return new Promise((resolve) => {
    state.dialog = createDialog('confirm', { ...options, type: options.type ?? 'warning' }, resolve);
  });
}

export function resolveFeedback(value: boolean) {
  const current = state.dialog;
  if (!current) return;

  state.dialog = null;
  current.resolve?.(value);
}
