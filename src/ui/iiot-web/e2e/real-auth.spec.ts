import { readFileSync } from 'node:fs';
import { expect, test } from '@playwright/test';

interface RealEnvironmentState {
  schemaVersion: number;
  webUrl: string;
  gatewayUrl: string;
  employeeNo: string;
  password: string;
  dependencyChain: string[];
}

const statePath = process.env.CLOUD_WEB_E2E_STATE;
if (!statePath) {
  throw new Error('CLOUD_WEB_E2E_STATE is required; run through npm run test:e2e-real.');
}
const state = JSON.parse(readFileSync(statePath, 'utf8')) as RealEnvironmentState;

test('logs in through real Gateway, HttpApi, auth middleware and PostgreSQL', async ({ page }) => {
  expect(state.schemaVersion).toBe(1);
  expect(state.dependencyChain).toEqual([
    'Browser', 'Vite', 'Gateway', 'HttpApi', 'AuthMiddleware', 'PostgreSQL',
  ]);

  await page.goto(`${state.webUrl}/login`);
  const employeeNo = page.getByRole('textbox', { name: '工号' });
  const password = page.locator('input[type="password"]');
  const submit = page.getByRole('button', { name: '登录' });

  await employeeNo.fill(state.employeeNo);
  await password.fill(`${state.password}-invalid`);
  const rejectedResponse = page.waitForResponse((response) =>
    response.url().endsWith('/api/v1/human/identity/login')
    && response.status() >= 400
    && response.status() < 500);
  await submit.click();
  expect([400, 401]).toContain((await rejectedResponse).status());
  await expect.poll(() => page.evaluate(() => localStorage.getItem('token'))).toBeNull();
  await expect(page).toHaveURL(/\/login$/);

  await password.fill(state.password);
  const acceptedResponse = page.waitForResponse((response) =>
    response.url().endsWith('/api/v1/human/identity/login') && response.status() === 200);
  await submit.click();
  const response = await acceptedResponse;
  expect(response.headers()['x-iiot-refresh-token']).toBeTruthy();

  await expect.poll(() => page.evaluate(() => localStorage.getItem('token'))).not.toBeNull();
  const token = await page.evaluate(() => localStorage.getItem('token'));
  const [encodedHeader] = (token ?? '').split('.');
  const header = JSON.parse(Buffer.from(encodedHeader, 'base64url').toString('utf8')) as { alg?: string };
  expect(header.alg).toBeTruthy();
  expect(header.alg).not.toBe('none');
  await expect(page).not.toHaveURL(/\/login(?:\?|$)/);
});
