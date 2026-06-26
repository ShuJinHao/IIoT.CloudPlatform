import type { RouteRecordRaw } from 'vue-router';
import { Permissions } from '../../types/permissions';

export const processRoutes: RouteRecordRaw[] = [
  {
    path: 'master-data/processes',
    name: 'MasterDataProcesses',
    component: () => import('./ProcessListPage.vue'),
    meta: { requiresAuth: true, requiredPermission: Permissions.Process.Read, title: '工序管理' },
  },
];
