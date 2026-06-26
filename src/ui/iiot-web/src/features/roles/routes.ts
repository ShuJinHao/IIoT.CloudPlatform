import type { RouteRecordRaw } from 'vue-router';
import { Permissions } from '../../types/permissions';

export const roleRoutes: RouteRecordRaw[] = [
  {
    path: 'roles',
    name: 'Roles',
    component: () => import('./RoleListPage.vue'),
    meta: { requiresAuth: true, requiredPermission: Permissions.Role.Define, title: '角色与权限' },
  },
];
