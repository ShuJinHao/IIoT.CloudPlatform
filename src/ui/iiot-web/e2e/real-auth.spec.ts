import { readFileSync } from 'node:fs';
import { expect, test } from '@playwright/test';

interface RealEnvironmentState {
  schemaVersion: number;
  webUrl: string;
  gatewayUrl: string;
  employeeNo: string;
  password: string;
}

interface DeviceListPayload {
  items?: Array<{ deviceName?: string }>;
  metaData?: { totalCount?: number };
}

const isDeviceListResponse = (response: import('@playwright/test').Response) => {
  const url = new URL(response.url());
  return response.request().method() === 'GET'
    && url.pathname === '/api/v1/human/devices';
};

const statePath = process.env.CLOUD_WEB_E2E_STATE;
if (!statePath) {
  throw new Error('CLOUD_WEB_E2E_STATE is required; run through npm run test:e2e-real.');
}
const state = JSON.parse(readFileSync(statePath, 'utf8')) as RealEnvironmentState;

test('logs in through real Gateway, HttpApi, auth middleware and PostgreSQL', async ({ page }) => {
  expect(state.schemaVersion).toBe(1);

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

  const protectedPageResponse = page.waitForResponse((candidate) =>
    isDeviceListResponse(candidate) && candidate.status() === 200);
  await page.goto(`${state.webUrl}/devices`);
  const deviceListResponse = await protectedPageResponse;
  expect(deviceListResponse.request().resourceType()).toBe('xhr');
  expect(deviceListResponse.request().headers()['authorization']).toBe(`Bearer ${token}`);

  const envelope = await deviceListResponse.json() as DeviceListPayload | { value?: DeviceListPayload };
  const deviceList = 'value' in envelope && envelope.value ? envelope.value : envelope;
  await expect(page.getByRole('heading', { name: '设备台账' })).toBeVisible();
  if (typeof deviceList.metaData?.totalCount === 'number') {
    await expect(page.getByText(`共 ${deviceList.metaData.totalCount} 台`, { exact: true })).toBeVisible();
  }
  if (deviceList.items?.[0]?.deviceName) {
    await expect(page.getByText(deviceList.items[0].deviceName, { exact: true }).first()).toBeVisible();
  } else {
    await expect(page.getByText('未找到设备', { exact: true })).toBeVisible();
  }
  await expect(page.getByText(state.employeeNo).first()).toBeVisible();

  const forgedToken = await page.evaluate(() => {
    const accessToken = localStorage.getItem('token');
    if (!accessToken) throw new Error('Expected the real login token before forging its signature.');
    const [headerPart, payloadPart] = accessToken.split('.');
    const forged = `${headerPart}.${payloadPart}.invalid-real-e2e-signature`;
    localStorage.setItem('token', forged);
    return forged;
  });
  const rejectedPageResponse = page.waitForResponse((candidate) =>
    isDeviceListResponse(candidate) && [401, 403].includes(candidate.status()));
  await page.goto(`${state.webUrl}/devices`);
  const rejectedDeviceListResponse = await rejectedPageResponse;
  expect(rejectedDeviceListResponse.request().resourceType()).toBe('xhr');
  expect(rejectedDeviceListResponse.request().headers()['authorization']).toBe(`Bearer ${forgedToken}`);
  expect([401, 403]).toContain(rejectedDeviceListResponse.status());
  await expect(page).toHaveURL(/\/login(?:\?|$)/);
  await expect(page.getByRole('button', { name: '登录' })).toBeVisible();
});
