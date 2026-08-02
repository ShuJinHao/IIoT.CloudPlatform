import { expect, test, type Page } from '@playwright/test';

const ROLE_CLAIM = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role';
const COMPONENT_ID = '2b8f1c0d-1111-4222-8333-444455556666';

async function installSession(page: Page, role: string, permissions: string[]) {
  await page.addInitScript(({ grantedRole, grantedPermissions, roleClaim }) => {
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
        sub: 'client-release-smoke-user',
        unique_name: 'RELEASE-SMOKE',
        exp: Math.floor(expiresAt.getTime() / 1000),
        Permission: grantedPermissions,
        [roleClaim]: grantedRole,
      }),
      'local-smoke-signature',
    ].join('.');

    localStorage.setItem('authStorageVersion', '2');
    localStorage.setItem('token', accessToken);
    localStorage.setItem('refreshToken', 'local-smoke-refresh-token');
    localStorage.setItem('accessTokenExpiresAt', expiresAt.toISOString());
    localStorage.setItem('refreshTokenExpiresAt', refreshExpiresAt.toISOString());
  }, { grantedRole: role, grantedPermissions: permissions, roleClaim: ROLE_CLAIM });
}

async function mockReleaseApis(page: Page) {
  await page.route(/\/api\/v1\/human\/client-releases\/catalog(?:\?.*)?$/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        catalogSchemaVersion: 2,
        channel: 'stable',
        targetRuntime: 'win-x64',
        host: {
          componentKind: 'Host',
          displayName: 'Edge Host',
          versions: [{
            id: 'host-version-id',
            componentId: 'host-component-id',
            channel: 'stable',
            version: '2.0.14',
            hostApiVersion: '2.0.0',
            targetRuntime: 'win-x64',
            targetFramework: 'net10.0',
            downloadUrl: '/edge-updates/installers/stable/2.0.14/installer-artifact.json',
            sha256: 'a'.repeat(64),
            packageSize: 4096,
            releaseNotes: 'B17 Host',
            status: 'Published',
            signature: 'host-signature',
            publisher: 'release-admin',
            createdAtUtc: '2026-08-02T01:00:00Z',
            publishedAtUtc: '2026-08-02T01:00:00Z',
          }],
        },
        plugins: [],
        generatedAtUtc: '2026-08-02T01:05:00Z',
      }),
    });
  });

  await page.route(/\/api\/v1\/human\/client-releases\/history(?:\?.*)?$/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        items: [{
          componentId: COMPONENT_ID,
          componentKind: 'Plugin',
          moduleId: 'AP',
          displayName: 'AP 工序插件',
          channel: 'stable',
          targetRuntime: 'win-x64',
          canHardDelete: true,
          versions: [{
            id: 'plugin-version-id',
            version: '2.0.18',
            status: 'Archived',
            createdAtUtc: '2026-07-01T00:00:00Z',
            publishedAtUtc: '2026-07-02T00:00:00Z',
            deletedAtUtc: null,
            deletionReason: null,
            deletionFailure: null,
            releaseNotes: '完整历史发布说明',
            sha256: 'b'.repeat(64),
            packageSize: 2048,
            publisher: 'release-admin',
            signature: 'plugin-signature',
            downloadUrl: '/edge-updates/plugins/stable/AP/2.0.18/plugin.zip',
            hostApiVersion: '2.0.0',
            targetFramework: 'net10.0',
            minHostVersion: '2.0.14',
            maxHostVersion: '2.0.14',
            dependencies: [{ moduleId: 'Base' }],
            artifacts: [{
              artifactKind: 'PackageFile',
              relativePath: 'plugins/stable/AP/2.0.18/plugin.zip',
              sha256: 'b'.repeat(64),
              size: 2048,
              filesPresent: true,
            }],
          }],
        }],
        metaData: {
          currentPage: 1,
          totalPages: 1,
          pageSize: 10,
          totalCount: 1,
        },
      }),
    });
  });

  await page.route(/\/api\/v1\/human\/client-releases\/component-deletions(?:\?.*)?$/, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' });
  });
}

test('1440 Admin 可读完整历史详情并看到受控删除入口', async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await installSession(page, 'Admin', []);
  await mockReleaseApis(page);

  await page.goto('/client-releases/publish');

  await expect(page.getByRole('heading', { name: '客户端发布管理' })).toBeVisible();
  await expect(page.getByRole('button', { name: '永久删除插件' })).toBeVisible();
  await expect(page.getByRole('button', { name: '删文件' })).toBeVisible();
  await page.locator('.history-component').getByRole('button', { name: '详情' }).click();

  const detail = page.locator('.release-detail-modal');
  await expect(detail).toContainText('完整历史发布说明');
  await expect(detail).toContainText('2.0.14 — 2.0.14');
  await expect(detail).toContainText('plugins/stable/AP/2.0.18/plugin.zip');
  await expect(detail).toContainText('文件存在');
  await page.screenshot({
    path: testInfo.outputPath('client-release-admin-1440.png'),
    fullPage: true,
  });
});

test('1024 非 Admin 发布管理员看不到任何删文件或整体删除入口', async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 1024, height: 900 });
  await installSession(page, 'ClientReleaseManager', [
    'ClientRelease.Read',
    'ClientRelease.Manage',
    'ClientRelease.HardDelete',
  ]);
  await mockReleaseApis(page);

  await page.goto('/client-releases/publish');

  await expect(page.getByRole('heading', { name: '客户端发布管理' })).toBeVisible();
  await expect(page.getByRole('button', { name: '删文件' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: '永久删除插件' })).toHaveCount(0);
  await expect(page.locator('.history-component').getByRole('button', { name: '详情' })).toBeVisible();
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({
    path: testInfo.outputPath('client-release-manager-1024.png'),
    fullPage: true,
  });
});
