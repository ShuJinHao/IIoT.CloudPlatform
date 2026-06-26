import type { RouteRecordRaw } from 'vue-router';
import { Permissions } from '../../types/permissions';

export const deviceLogRoutes: RouteRecordRaw[] = [
  {
    path: 'device-logs',
    name: 'DeviceLogs',
    component: () => import('./DeviceLogPage.vue'),
    meta: { requiresAuth: true, requiredPermission: Permissions.Device.Read, title: '设备日志' },
  },
];
