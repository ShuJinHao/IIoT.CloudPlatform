import { describe, expect, it } from 'vitest';
import { Permissions } from '../../types/permissions';
import { clientReleaseRoutes } from './routes';
import {
  getReleaseMetadataValidationMessage,
  isValidDownloadUrl,
  validateReleaseMetadata,
} from './types';

const validMetadata = {
  downloadUrl: '/edge-updates/host/IIoT.EdgeClient.exe',
  sha256: 'a'.repeat(64),
  packageSize: '1024',
  status: 'Draft',
  releaseNotes: '',
};

describe('client release feature guards', () => {
  it('keeps installer and publish route permissions separated', () => {
    expect(clientReleaseRoutes[0]!.meta?.requiredPermission).toBe(Permissions.ClientRelease.Read);
    expect(clientReleaseRoutes[1]!.meta?.requiredPermission).toBe(Permissions.ClientRelease.Manage);
  });

  it('accepts only http/https or edge release download paths', () => {
    expect(isValidDownloadUrl('/edge-updates/plugin.zip')).toBe(true);
    expect(isValidDownloadUrl('https://example.test/plugin.zip')).toBe(true);
    expect(isValidDownloadUrl('file:///tmp/plugin.zip')).toBe(false);
  });

  it('rejects placeholder release metadata', () => {
    expect(validateReleaseMetadata({ ...validMetadata, sha256: '0'.repeat(64) })).toBeNull();
    expect(validateReleaseMetadata({ ...validMetadata, packageSize: '0' })).toBeNull();
    expect(validateReleaseMetadata({ ...validMetadata, status: 'Published', releaseNotes: '' })).toBeNull();
  });

  it('returns concrete validation messages and parsed package size', () => {
    expect(getReleaseMetadataValidationMessage(validMetadata)).toBeNull();
    expect(validateReleaseMetadata(validMetadata)).toBe(1024);
    expect(getReleaseMetadataValidationMessage({ ...validMetadata, downloadUrl: 'ftp://example.test/a.zip' })).toContain('下载地址');
  });
});
