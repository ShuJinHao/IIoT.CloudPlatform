import { describe, expect, it } from 'vitest';
import { Permissions } from '../../types/permissions';
import { recipeRoutes } from './routes';
import { isDeviceBoundRecipe, paramsToJsonb, parseParams, validateParams } from './types';

describe('recipes feature guards', () => {
  it('requires recipe read permission on the route', () => {
    const route = recipeRoutes[0];
    expect(route).toBeDefined();
    expect(route!.path).toBe('recipes');
    expect(route!.meta?.requiredPermission).toBe(Permissions.Recipe.Read);
  });

  it('keeps unbound recipe device id semantics', () => {
    expect(isDeviceBoundRecipe('00000000-0000-0000-0000-000000000000')).toBe(false);
    expect(isDeviceBoundRecipe('device-1')).toBe(true);
  });

  it('validates recipe parameters before create or upgrade', () => {
    expect(validateParams([])).toBe('至少保留一个工艺参数');
    expect(validateParams([{ id: 'p1', name: '', unit: '℃', min: 0, max: 1 }])).toContain('参数名称不能为空');
    expect(validateParams([{ id: 'p1', name: '温度', unit: '℃', min: 2, max: 1 }])).toContain('下限不能大于上限');
    expect(validateParams([{ id: 'p1', name: '温度', unit: '℃', min: 1, max: 2 }])).toBeNull();
  });

  it('serializes and parses parameter json without losing ranges', () => {
    const json = paramsToJsonb([{ id: 'p1', name: '温度', unit: '℃', min: 10, max: 20 }]);
    expect(parseParams(json)).toEqual([{ id: 'p1', name: '温度', unit: '℃', min: 10, max: 20 }]);
  });
});
