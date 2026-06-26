import type { RouteRecordRaw } from 'vue-router';
import { Permissions } from '../../types/permissions';

export const clientReleaseRoutes: RouteRecordRaw[] = [
  {
    path: 'client-releases',
    name: 'ClientReleases',
    component: () => import('./ClientReleasePage.vue'),
    meta: { requiresAuth: true, requiredPermission: Permissions.ClientRelease.Read, title: '客户端首装生成' },
  },
  {
    path: 'client-releases/publish',
    name: 'ClientReleasePublish',
    component: () => import('./ClientReleasePage.vue'),
    meta: { requiresAuth: true, requiredPermission: Permissions.ClientRelease.Manage, title: '客户端发布管理' },
  },
];
