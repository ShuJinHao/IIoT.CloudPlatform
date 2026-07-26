import { describe, expect, it } from 'vitest';
import { Permissions } from '../../types/permissions';
import { createPassStationColumns } from './columns';
import { passStationRoutes } from './routes';
import { buildPassStationSchema, normalizePassStationTypeKey } from './schema';
import type { PassStationTypeDefinitionDto } from './api';

const definition: PassStationTypeDefinitionDto = {
  typeKey: 'cp',
  displayName: '正极模切',
  description: '正极模切追溯记录',
  supportedModes: ['barcode-process', 'device-latest'],
  fields: [
    { key: 'plcName', label: 'PLC 名称', type: 'string', required: true },
    { key: 'clipSlot', label: '弹夹位', type: 'enum', required: false, options: ['MG1', 'MG2'] },
  ],
  listColumns: ['plcName', 'clipSlot', 'barcode', 'cellResult'],
  detailSections: [{ title: '基础信息', fields: ['plcName', 'clipSlot', 'barcode', 'cellResult'] }],
};

describe('pass station feature schema', () => {
  it('keeps the route guarded by device read permission', () => {
    expect(passStationRoutes[0]!.meta?.requiredPermission).toBe(Permissions.Device.Read);
  });

  it('normalizes process code to schema type key', () => {
    expect(normalizePassStationTypeKey(' CP ')).toBe('cp');
  });

  it('builds columns and detail sections from server schema', () => {
    const schema = buildPassStationSchema(definition);
    expect(schema.title).toBe('正极模切过站追溯');
    expect(schema.columns.map((column) => column.key)).toEqual(['plcName', 'clipSlot', 'barcode', 'cellResult']);
    expect(schema.columns.map((column) => column.label)).toEqual(['PLC 名称', '弹夹位', '弹夹号', '结果']);
    expect(schema.detailSections[0]!.fields.map((field) => field.key)).toEqual(['plcName', 'clipSlot', 'barcode', 'cellResult']);
    const record = {
      id: '1',
      deviceId: 'd1',
      barcode: 'CP-CLIP-001',
      cellResult: 'OK',
      completedTime: null,
      receivedAt: null,
      fields: { plcName: '正极模切01', clipSlot: 'MG1' },
    };
    expect(schema.columns[1]!.render(record)).toBe('MG1');
    expect(schema.columns[2]!.render(record)).toBe('CP-CLIP-001');
    expect(schema.columns[1]!.render({ ...record, fields: { plcName: '正极模切01' } })).toBe('—');
  });

  it('creates UI columns from schema without fixed process pages', () => {
    expect(createPassStationColumns(buildPassStationSchema(definition))).toHaveLength(4);
  });
});
