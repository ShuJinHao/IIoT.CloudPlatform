import { describe, expect, it } from 'vitest';
import { Permissions } from '../../types/permissions';
import { processRoutes } from './routes';
import {
  emptyProcessMetaData,
  normalizeProcessPageResult,
  validateProcessForm,
} from './types';

describe('processes feature', () => {
  it('guards process management with process read permission', () => {
    expect(processRoutes[0]!.meta?.requiredPermission).toBe(Permissions.Process.Read);
  });

  it('normalizes the paged process response', () => {
    const item = { id: 'p1', processCode: 'AP', processName: '阳极' };
    expect(normalizeProcessPageResult({
      items: [item],
      metaData: { totalCount: 1, pageSize: 10, currentPage: 1, totalPages: 1 },
    }).items).toEqual([item]);
    expect(normalizeProcessPageResult(null)).toEqual({
      items: [],
      metaData: emptyProcessMetaData(),
    });
  });

  it('validates required process code and name', () => {
    expect(validateProcessForm({ processCode: '', processName: '阳极' })).toBe('编码和名称均为必填项');
    expect(validateProcessForm({ processCode: 'AP', processName: '' })).toBe('编码和名称均为必填项');
    expect(validateProcessForm({ processCode: 'AP', processName: '阳极' })).toBeNull();
  });
});
