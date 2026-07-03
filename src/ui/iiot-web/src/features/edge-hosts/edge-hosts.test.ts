import { describe, expect, it } from 'vitest';
import { Permissions } from '../../types/permissions';
import { createPlcCapacitySummaryColumns, createPlcRuntimeStateColumns } from './columns';
import { edgeHostRoutes } from './routes';
import {
  createEmptyPlcBindingForm,
  normalizeOptionalText,
  validateEdgeHostForm,
  validatePlcBindingForm,
} from './types';

describe('edge host feature', () => {
  it('guards edge host page with dedicated read permission', () => {
    expect(edgeHostRoutes).toHaveLength(1);
    const route = edgeHostRoutes[0];
    expect(route).toBeDefined();
    expect(route!.path).toBe('edge-hosts');
    expect(route!.meta?.requiredPermission).toBe(Permissions.EdgeHost.Read);
  });

  it('uses dedicated manage permission constants', () => {
    expect(Permissions.EdgeHost.Read).toBe('EdgeHost.Read');
    expect(Permissions.EdgeHost.Manage).toBe('EdgeHost.Manage');
  });

  it('validates edge host identity form without allowing empty names', () => {
    expect(validateEdgeHostForm({ deviceId: null, hostName: '上位机', remark: '' }, 'create'))
      .toBe('请选择要绑定的云端设备。');
    expect(validateEdgeHostForm({ deviceId: 'd1', hostName: '  ', remark: '' }, 'create'))
      .toBe('上位机名称不能为空。');
    expect(validateEdgeHostForm({ deviceId: 'd1', hostName: '模切上位机', remark: '' }, 'create'))
      .toBeNull();
    expect(validateEdgeHostForm({ deviceId: null, hostName: '模切上位机', remark: '' }, 'edit'))
      .toBeNull();
  });

  it('validates plc binding form and normalizes optional text', () => {
    const form = createEmptyPlcBindingForm();
    expect(validatePlcBindingForm(form, 'create')).toBe('PLC 编码不能为空。');

    form.plcCode = 'PLC-CUT-01';
    expect(validatePlcBindingForm(form, 'create')).toBe('PLC 名称不能为空。');

    form.plcName = '模切 PLC 01';
    form.displayOrder = 1.5;
    expect(validatePlcBindingForm(form, 'create')).toBe('排序必须是整数。');

    form.displayOrder = 1;
    expect(validatePlcBindingForm(form, 'create')).toBeNull();
    expect(normalizeOptionalText('  ModbusTcp  ')).toBe('ModbusTcp');
    expect(normalizeOptionalText('   ')).toBeNull();
  });

  it('defines runtime state columns without write actions', () => {
    const columns = createPlcRuntimeStateColumns({
      processLabel: (id) => id ?? '未关联',
      deviceLabel: (id) => id ?? '未关联',
    });
    expect(columns.map((column) => column.key)).toEqual([
      'plcCode',
      'isConfigured',
      'runtimeStatus',
      'processId',
      'configuredAddress',
      'runtimeAddress',
      'lastError',
      'lastSeenAtUtc',
    ]);
  });

  it('defines capacity summary columns without write actions', () => {
    const columns = createPlcCapacitySummaryColumns({
      deviceLabel: (id) => id ?? '未关联',
    });
    expect(columns.map((column) => column.key)).toEqual([
      'plcCode',
      'capacityStatus',
      'businessDeviceId',
      'totalCount',
      'okNg',
      'okRate',
    ]);
  });
});
