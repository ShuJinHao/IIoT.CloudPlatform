import { describe, expect, it } from 'vitest';
import { Permissions } from '../../types/permissions';
import type { ClientHostVersionEntryDto } from './api';
import { clientReleaseRoutes } from './routes';
import { isDeletionRetryComplete, pickCurrentVersion } from './types';

function makeVersion(status: string, version = '1.0.0'): ClientHostVersionEntryDto {
  return {
    id: `id-${status}-${version}`,
    componentId: `component-${version}`,
    channel: 'stable',
    version,
    hostApiVersion: '1.0.0',
    targetRuntime: 'win-x64',
    targetFramework: 'net10.0',
    downloadUrl: '/edge-updates/host/IIoT.EdgeClient.exe',
    sha256: 'a'.repeat(64),
    packageSize: 1024,
    releaseNotes: '',
    status,
    createdAtUtc: '2026-07-01T00:00:00Z',
    publishedAtUtc: null,
    deletedAtUtc: null,
    deletionReason: null,
    deletionFailure: null,
  };
}

describe('client release feature guards', () => {
  it('keeps installer and publish route permissions separated', () => {
    expect(clientReleaseRoutes[0]!.meta?.requiredPermission).toBe(Permissions.ClientRelease.Read);
    expect(clientReleaseRoutes[1]!.meta?.requiredPermission).toBe(Permissions.ClientRelease.Manage);
  });

  it('exposes a dedicated HardDelete permission for permanent component deletion', () => {
    expect(Permissions.ClientRelease.HardDelete).toBe('ClientRelease.HardDelete');
  });
});

describe('pickCurrentVersion', () => {
  it('prefers Published, then Deprecated, then Draft', () => {
    expect(pickCurrentVersion([makeVersion('Draft'), makeVersion('Published', '2.0.0')])?.version).toBe('2.0.0');
    expect(pickCurrentVersion([makeVersion('Draft'), makeVersion('Deprecated', '1.5.0')])?.version).toBe('1.5.0');
    expect(pickCurrentVersion([makeVersion('Draft')])?.status).toBe('Draft');
  });

  it('never selects Archived, Deleted, DeleteRequested or DeleteFailed as current', () => {
    expect(pickCurrentVersion([makeVersion('Archived'), makeVersion('Deleted')]) ).toBeNull();
    expect(pickCurrentVersion([makeVersion('DeleteRequested'), makeVersion('DeleteFailed')])).toBeNull();
  });

  it('returns null when there is no active version instead of falling back to versions[0]', () => {
    const archived = makeVersion('Archived', '9.9.9');
    expect(pickCurrentVersion([archived])).toBeNull();
    expect(pickCurrentVersion([])).toBeNull();
  });

  it('picks an active version even when it is not the first entry', () => {
    const published = makeVersion('Published', '3.0.0');
    expect(pickCurrentVersion([makeVersion('Archived', '1.0.0'), published])?.version).toBe('3.0.0');
  });
});

describe('isDeletionRetryComplete', () => {
  it('only treats cleanup as done when both succeeded and auditConfirmed are true', () => {
    expect(isDeletionRetryComplete({ succeeded: true, auditConfirmed: true })).toBe(true);
    expect(isDeletionRetryComplete({ succeeded: true, auditConfirmed: false })).toBe(false);
    expect(isDeletionRetryComplete({ succeeded: false, auditConfirmed: true })).toBe(false);
    expect(isDeletionRetryComplete({ succeeded: false, auditConfirmed: false })).toBe(false);
  });
});
