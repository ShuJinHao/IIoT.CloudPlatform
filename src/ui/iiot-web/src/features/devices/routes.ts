import type { RouteRecordRaw } from 'vue-router';
import { Permissions } from '../../types/permissions';

export const deviceRoutes: RouteRecordRaw[] = [
  {
    path: 'devices',
    name: 'Devices',
    component: () => import('./DeviceListPage.vue'),
    meta: { requiresAuth: true, requiredPermission: Permissions.Device.Read, title: '设备台账' },
  },
];
