import { expect, test, type Page } from '@playwright/test';

const DEVICE_A = '1c2b3a4d-5555-4666-8777-888899990000';
const DEVICE_B = '9d8c7b6a-5555-4666-8777-888899990000';

async function installSession(page: Page, permissions: string[]) {
  await page.addInitScript(({ grantedPermissions }) => {
    const encode = (value: object) =>
      btoa(JSON.stringify(value))
        .replace(/\+/g, '-')
        .replace(/\//g, '_')
        .replace(/=+$/g, '');
    const expiresAt = new Date(Date.now() + 60 * 60 * 1000);
    const refreshExpiresAt = new Date(Date.now() + 2 * 60 * 60 * 1000);
    const accessToken = [
      encode({ alg: 'HS256', typ: 'JWT' }),
      encode({
        sub: 'device-overview-smoke-user',
        unique_name: 'OVERVIEW-SMOKE',
        exp: Math.floor(expiresAt.getTime() / 1000),
        Permission: grantedPermissions,
      }),
      'local-smoke-signature',
    ].join('.');

    localStorage.setItem('authStorageVersion', '2');
    localStorage.setItem('token', accessToken);
    localStorage.setItem('refreshToken', 'local-smoke-refresh-token');
    localStorage.setItem('accessTokenExpiresAt', expiresAt.toISOString());
    localStorage.setItem('refreshTokenExpiresAt', refreshExpiresAt.toISOString());
  }, { grantedPermissions: permissions });
}

async function mockOverview(page: Page, items: object[]) {
  await page.route(/\/api\/v1\/human\/device-client-overviews(?:\?.*)?$/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        items,
        metaData: {
          currentPage: 1,
          totalPages: 1,
          pageSize: 10,
          totalCount: items.length,
        },
      }),
    });
  });
}

function overviewItem(deviceId: string, deviceName: string, softwareStatus = 'Running') {
  return {
    deviceId,
    deviceName,
    primaryIpAddress: deviceId === DEVICE_A ? '10.0.0.11' : '10.0.0.12',
    softwareStatus,
    currentVersion: deviceId === DEVICE_A ? '2.4.0' : '3.0.0',
    issue: null,
  };
}

function plcState(deviceId: string, suffix: string) {
  return {
    id: `plc-state-${suffix}`,
    deviceId,
    clientCode: `DC-${suffix}`,
    plcCode: `PLC-${suffix}`,
    reportedPlcName: `PLC ${suffix}`,
    runtimeStationCode: `ST-${suffix}`,
    runtimeProtocol: 'S7',
    runtimeAddress: `192.168.1.${suffix === 'A' ? '10' : '20'}`,
    isConnected: true,
    runtimeStatus: 'Connected',
    lastError: null,
    lastSeenAtUtc: '2026-07-23T01:00:00Z',
    updatedAtUtc: '2026-07-23T01:00:00Z',
  };
}

function releaseDetails(deviceId: string, suffix: string, currentVersion: string) {
  return {
    deviceId,
    deviceName: `设备 ${suffix}`,
    clientCode: `DC-${suffix}`,
    primaryIp: suffix === 'A' ? '10.0.0.11' : '10.0.0.12',
    localIpAddresses: [],
    remoteIpAddress: null,
    channel: 'stable',
    hostVersion: currentVersion,
    hostApiVersion: '1.0.0',
    hostUpdateStatus: 'Latest',
    hostCompatibilityIssue: null,
    installStatus: 'Normal',
    softwareStatus: 'Running',
    currentVersion,
    issue: null,
    versionIssue: null,
    cloudIssue: null,
    lastRuntimeHeartbeatAtUtc: '2026-07-23T01:00:00Z',
    reportedAtUtc: '2026-07-23T00:30:00Z',
    receivedAtUtc: null,
    plugins: [],
  };
}

test('overview-only permission shows Unknown in Chinese and hides the empty detail action', async ({ page }) => {
  await installSession(page, ['DeviceClientOverview.Read']);
  await mockOverview(page, [overviewItem(DEVICE_A, '设备 A', 'Unknown')]);

  await page.goto('/device-client-overviews');

  await expect(page.getByRole('heading', { name: '设备运行与版本' })).toBeVisible();
  await expect(page.locator('.overview-page__table').getByText('未知', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: '详情' })).toHaveCount(0);
  await expect(page.locator('.ui-drawer')).toHaveCount(0);
});

test('late device A detail responses cannot replace the currently open device B', async ({ page }) => {
  await installSession(page, [
    'DeviceClientOverview.Read',
    'EdgeHost.Read',
    'ClientRelease.Read',
  ]);
  await mockOverview(page, [
    overviewItem(DEVICE_A, '设备 A'),
    overviewItem(DEVICE_B, '设备 B'),
  ]);

  let releasePlcA!: () => void;
  let releaseVersionA!: () => void;
  let plcAStarted = false;
  let versionAStarted = false;
  const plcAGate = new Promise<void>((resolve) => {
    releasePlcA = resolve;
  });
  const versionAGate = new Promise<void>((resolve) => {
    releaseVersionA = resolve;
  });

  await page.route(/\/api\/v1\/human\/edge-hosts\/[^/]+\/plc-runtime-states(?:\?.*)?$/, async (route) => {
    const deviceId = new URL(route.request().url()).pathname.split('/').at(-2);
    if (deviceId === DEVICE_A) {
      plcAStarted = true;
      await plcAGate;
    }
    const suffix = deviceId === DEVICE_A ? 'A' : 'B';
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([plcState(deviceId!, suffix)]),
    });
  });
  await page.route(/\/api\/v1\/human\/device-client-overviews\/[^/]+\/release-details(?:\?.*)?$/, async (route) => {
    const deviceId = new URL(route.request().url()).pathname.split('/').at(-2);
    if (deviceId === DEVICE_A) {
      versionAStarted = true;
      await versionAGate;
    }
    const isDeviceA = deviceId === DEVICE_A;
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(releaseDetails(
        deviceId!,
        isDeviceA ? 'A' : 'B',
        isDeviceA ? '2.4.0' : '3.0.0',
      )),
    });
  });

  await page.goto('/device-client-overviews');
  const detailButtons = page.getByRole('button', { name: '详情' });
  await expect(detailButtons).toHaveCount(2);
  await detailButtons.nth(0).click();
  await expect.poll(() => plcAStarted && versionAStarted).toBe(true);

  await page.locator('.ui-drawer').getByRole('button', { name: '关闭' }).click();
  await detailButtons.nth(1).click();
  const drawer = page.locator('.ui-drawer');
  await expect(drawer.getByRole('heading', { name: '设备 B' })).toBeVisible();
  await expect(drawer.getByText('PLC-B', { exact: true })).toBeVisible();
  await expect(drawer.getByText('3.0.0', { exact: true }).first()).toBeVisible();

  const lateResponses = Promise.all([
    page.waitForResponse((response) =>
      response.url().includes(`/edge-hosts/${DEVICE_A}/plc-runtime-states`)),
    page.waitForResponse((response) =>
      response.url().includes(`/device-client-overviews/${DEVICE_A}/release-details`)),
  ]);
  releasePlcA();
  releaseVersionA();
  await lateResponses;
  await page.evaluate(() => new Promise<void>((resolve) => {
    requestAnimationFrame(() => resolve());
  }));

  await expect(drawer.getByRole('heading', { name: '设备 B' })).toBeVisible();
  await expect(drawer.getByText('PLC-B', { exact: true })).toBeVisible();
  await expect(drawer.getByText('3.0.0', { exact: true }).first()).toBeVisible();
  await expect(drawer).not.toContainText('PLC-A');
  await expect(drawer).not.toContainText('2.4.0');
});
