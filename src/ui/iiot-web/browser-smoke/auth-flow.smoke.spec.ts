import { expect, test } from '@playwright/test';

test('redirects an unauthenticated protected route to login with a local return URL', async ({ page }) => {
  await page.goto('/devices');

  await expect(page).toHaveURL(/\/login\?returnUrl=(?:%2F|\/)devices$/);
  await expect(page.getByRole('heading', { name: '操作员登录' })).toBeVisible();
});

test('submits the login request shape and remains unauthenticated on rejection', async ({ page }) => {
  let submittedPayload: unknown;
  await page.route('**/api/v1/human/identity/login', async (route) => {
    const request = route.request();
    expect(request.method()).toBe('POST');
    submittedPayload = request.postDataJSON();
    await route.fulfill({
      status: 401,
      contentType: 'application/problem+json',
      body: JSON.stringify({ title: 'Unauthorized', detail: 'Smoke rejection' }),
    });
  });

  await page.goto('/login');
  await page.getByRole('textbox', { name: '工号' }).fill('SMOKE-USER');
  await page.locator('input[type="password"]').fill('rejected-password');
  await page.getByRole('button', { name: '登录' }).click();

  await expect(page.getByText('Smoke rejection')).toBeVisible();
  expect(submittedPayload).toEqual({ employeeNo: 'SMOKE-USER', password: 'rejected-password' });
  await expect.poll(() => page.evaluate(() => localStorage.getItem('token'))).toBeNull();
  await expect(page).toHaveURL(/\/login$/);
});
