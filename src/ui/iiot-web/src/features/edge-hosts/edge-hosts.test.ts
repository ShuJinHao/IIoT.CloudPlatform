import { describe, expect, it } from 'vitest';
import { Permissions } from '../../types/permissions';
import { createEdgeHostColumns, createPlcRuntimeStateColumns } from './columns';
import { edgeHostRoutes } from './routes';
import { emptyEdgeHostMetaData } from './types';

describe('edge host feature', () => {
  it('guards edge host page with dedicated read permission', () => {
    expect(edgeHostRoutes).toHaveLength(1);
    const route = edgeHostRoutes[0];
    expect(route).toBeDefined();
    expect(route!.path).toBe('edge-hosts');
    expect(route!.meta?.requiredPermission).toBe(Permissions.EdgeHost.Read);
  });

  it('only exposes read permission for edge host plc states', () => {
    expect(Permissions.EdgeHost.Read).toBe('EdgeHost.Read');
    expect('Manage' in Permissions.EdgeHost).toBe(false);
  });

  it('defines host status columns without cloud write actions', () => {
    const columns = createEdgeHostColumns({
      onOpenPlcState: () => {},
    });
    expect(columns.map((column) => column.key)).toEqual([
      'hostName',
      'primaryIpAddress',
      'softwareStatus',
      'currentVersion',
      'lastRuntimeHeartbeatAtUtc',
      'plcCount',
      'lastPlcSeenAtUtc',
      'issue',
      'actions',
    ]);
  });

  it('defines runtime state columns without binding configuration fields', () => {
    const columns = createPlcRuntimeStateColumns();
    expect(columns.map((column) => column.key)).toEqual([
      'plcCode',
      'runtimeStatus',
      'runtimeAddress',
      'runtimeStationCode',
      'lastError',
      'lastSeenAtUtc',
    ]);
  });

  it('keeps pagination defaults stable', () => {
    expect(emptyEdgeHostMetaData()).toMatchObject({
      totalCount: 0,
      pageSize: 10,
      currentPage: 1,
      totalPages: 1,
    });
  });
});
