import type { Router } from 'vue-router';

export function getSafeLoginReturnUrl(value: unknown): string | null {
  if (typeof value !== 'string') return null;
  const trimmed = value.trim();
  if (!trimmed.startsWith('/') || trimmed.startsWith('//') || trimmed.includes('\\')) return null;
  return trimmed;
}

export async function navigateAfterLogin(
  router: Router,
  returnUrl: string | null,
  assign: (url: string) => void = (url) => window.location.assign(url),
): Promise<void> {
  if (returnUrl?.startsWith('/connect/')) {
    assign(returnUrl);
    return;
  }

  await router.push(returnUrl || '/');
}
