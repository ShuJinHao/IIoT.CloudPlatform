import type { RouteRecordRaw } from 'vue-router';

export const dashboardRoutes: RouteRecordRaw[] = [
  {
    path: '',
    name: 'Dashboard',
    component: () => import('./DashboardPage.vue'),
    meta: { requiresAuth: true, title: '系统概览' },
  },
];
