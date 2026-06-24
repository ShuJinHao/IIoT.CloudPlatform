import { describe, expect, it } from 'vitest';
import { Permissions } from '../../types/permissions';
import { createPassStationColumns } from './columns';
import { passStationRoutes } from './routes';
import { buildPassStationSchema, normalizePassStationTypeKey } from './schema';
import type { PassStationTypeDefinitionDto } from './api';

const definition: PassStationTypeDefinitionDto = {
  typeKey: 'formation',
  displayName: '化成',
  description: '化成追溯记录',
  supportedModes: ['barcode-process', 'device-latest'],
  fields: [{ key: 'voltage', label: '电压', type: 'number', required: false, unit: 'V' }],
  listColumns: ['barcode', 'cellResult', 'voltage'],
  detailSections: [{ title: '基础信息', fields: ['barcode', 'cellResult', 'voltage'] }],
};

describe('pass station feature schema', () => {
  it('keeps the route guarded by device read permission', () => {
    expect(passStationRoutes[0]!.meta?.requiredPermission).toBe(Permissions.Device.Read);
  });

  it('normalizes process code to schema type key', () => {
    expect(normalizePassStationTypeKey(' Formation ')).toBe('formation');
  });

  it('builds columns and detail sections from server schema', () => {
    const schema = buildPassStationSchema(definition);
    expect(schema.title).toBe('化成过站追溯');
    expect(schema.columns.map((column) => column.key)).toEqual(['barcode', 'cellResult', 'voltage']);
    expect(schema.detailSections[0]!.fields.map((field) => field.key)).toEqual(['barcode', 'cellResult', 'voltage']);
    expect(schema.columns[2]!.render({
      id: '1',
      deviceId: 'd1',
      barcode: 'B1',
      cellResult: 'OK',
      completedTime: null,
      receivedAt: null,
      fields: { voltage: 3.6 },
    })).toBe('3.6 V');
  });

  it('creates UI columns from schema without fixed process pages', () => {
    expect(createPassStationColumns(buildPassStationSchema(definition))).toHaveLength(3);
  });
});
