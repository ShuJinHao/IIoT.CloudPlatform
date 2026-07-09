import type { RouteRecordRaw } from 'vue-router';
import { Permissions } from '../../types/permissions';

export const edgeHostRoutes: RouteRecordRaw[] = [
  {
    path: 'edge-hosts',
    name: 'EdgeHosts',
    component: () => import('./EdgeHostListPage.vue'),
    meta: {
      requiresAuth: true,
      requiredPermission: Permissions.EdgeHost.Read,
      title: '上位机 PLC 状态',
    },
  },
];
