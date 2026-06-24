import type { RouteRecordRaw } from 'vue-router';
import { Permissions } from '../../types/permissions';

export const passStationRoutes: RouteRecordRaw[] = [
  {
    path: 'pass-station',
    name: 'PassStation',
    component: () => import('./PassStationPage.vue'),
    meta: { requiresAuth: true, requiredPermission: Permissions.Device.Read, title: '过站追溯' },
  },
];
