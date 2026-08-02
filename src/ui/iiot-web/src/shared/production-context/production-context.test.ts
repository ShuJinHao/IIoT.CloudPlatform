import { describe, expect, it } from 'vitest';
import { resolveProductionContextState } from './useProductionContext';

describe('production context states', () => {
  it('keeps every authorization and selection empty state explicit', () => {
    expect(resolveProductionContextState({
      status: 'loading',
      authorizedDeviceCount: 0,
      hasSelectedProcess: false,
      processDeviceCount: 0,
      hasSelectedDevice: false,
    })).toBe('loading');
    expect(resolveProductionContextState({
      status: 'error',
      authorizedDeviceCount: 0,
      hasSelectedProcess: false,
      processDeviceCount: 0,
      hasSelectedDevice: false,
    })).toBe('error');
    expect(resolveProductionContextState({
      status: 'ready',
      authorizedDeviceCount: 0,
      hasSelectedProcess: false,
      processDeviceCount: 0,
      hasSelectedDevice: false,
    })).toBe('no-authorized-devices');
    expect(resolveProductionContextState({
      status: 'ready',
      authorizedDeviceCount: 2,
      hasSelectedProcess: false,
      processDeviceCount: 0,
      hasSelectedDevice: false,
    })).toBe('select-process');
    expect(resolveProductionContextState({
      status: 'ready',
      authorizedDeviceCount: 2,
      hasSelectedProcess: true,
      processDeviceCount: 0,
      hasSelectedDevice: false,
    })).toBe('no-process-devices');
    expect(resolveProductionContextState({
      status: 'ready',
      authorizedDeviceCount: 2,
      hasSelectedProcess: true,
      processDeviceCount: 1,
      hasSelectedDevice: false,
    })).toBe('select-device');
    expect(resolveProductionContextState({
      status: 'ready',
      authorizedDeviceCount: 2,
      hasSelectedProcess: true,
      processDeviceCount: 1,
      hasSelectedDevice: true,
    })).toBe('ready');
  });
});
