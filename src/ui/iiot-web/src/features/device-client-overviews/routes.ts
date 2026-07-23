import type { RouteRecordRaw } from 'vue-router';
import { Permissions } from '../../types/permissions';

export const deviceClientOverviewRoutes: RouteRecordRaw[] = [
  {
    path: 'device-client-overviews',
    name: 'DeviceClientOverviews',
    component: () => import('./DeviceClientOverviewPage.vue'),
    meta: {
      requiresAuth: true,
      requiredPermission: Permissions.DeviceClientOverview.Read,
      title: '设备运行与版本',
    },
  },
];
