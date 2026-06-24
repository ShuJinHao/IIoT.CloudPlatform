import type { RouteRecordRaw } from 'vue-router';
import { Permissions } from '../../types/permissions';

export const capacityRoutes: RouteRecordRaw[] = [
  {
    path: 'capacity',
    name: 'Capacity',
    component: () => import('./CapacityDashboardPage.vue'),
    meta: { requiresAuth: true, requiredPermission: Permissions.Device.Read, title: '产能看板' },
  },
  {
    path: 'capacity/detail',
    name: 'CapacityDetail',
    component: () => import('./CapacityDetailPage.vue'),
    meta: { requiresAuth: true, requiredPermission: Permissions.Device.Read, title: '产能详情' },
  },
];
