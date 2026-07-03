import { createRouter, createWebHistory } from 'vue-router';
import type { RouteRecordRaw } from 'vue-router';
import { capacityRoutes } from '../features/capacity/routes';
import { clientReleaseRoutes } from '../features/client-releases/routes';
import { dashboardRoutes } from '../features/dashboard/routes';
import { deviceLogRoutes } from '../features/device-logs/routes';
import { deviceRoutes } from '../features/devices/routes';
import { edgeHostRoutes } from '../features/edge-hosts/routes';
import { employeeRoutes } from '../features/employees/routes';
import { passStationRoutes } from '../features/pass-station/routes';
import { processRoutes } from '../features/processes/routes';
import { recipeRoutes } from '../features/recipes/routes';
import { roleRoutes } from '../features/roles/routes';
import { useAuthStore } from '../stores/auth';
import { Permissions } from '../types/permissions';

const routes: Array<RouteRecordRaw> = [
  {
    path: '/login',
    name: 'Login',
    component: () => import('../views/Login.vue'),
    meta: { requiresAuth: false }
  },
  {
    path: '/downloads',
    name: 'PublicDownloads',
    component: () => import('../views/PublicDownloadCenter.vue'),
    meta: { requiresAuth: false }
  },
  {
    path: '/',
    component: () => import('../layout/MainLayout.vue'),
    meta: { requiresAuth: true },
    children: [
      ...dashboardRoutes,
      ...employeeRoutes,
      ...processRoutes,
      {
        path: 'processes',
        redirect: { name: 'MasterDataProcesses' }
      },
      ...deviceRoutes,
      ...edgeHostRoutes,
      ...recipeRoutes,
      ...passStationRoutes,
      ...capacityRoutes,
      ...deviceLogRoutes,
      ...clientReleaseRoutes,
      ...roleRoutes,
      {
        path: 'forbidden',
        name: 'Forbidden',
        component: () => import('../views/Forbidden.vue'),
        meta: { requiresAuth: true, title: '无权访问' }
      }
    ]
  },
  { path: '/:pathMatch(.*)*', redirect: '/' }
];

const router = createRouter({
  history: createWebHistory(),
  routes
});

const isSafeLocalReturnUrl = (value: unknown): value is string =>
  typeof value === 'string'
  && value.startsWith('/')
  && !value.startsWith('//')
  && !value.includes('\\');

router.beforeEach((to) => {
  const authStore = useAuthStore();
  const returnUrl = to.query.returnUrl;
  const hasOidcReturnUrl =
    to.name === 'Login'
    && isSafeLocalReturnUrl(returnUrl)
    && returnUrl.startsWith('/connect/');

  if (to.meta.requiresAuth === false) {
    if (to.name === 'Login' && authStore.isAuthenticated && !hasOidcReturnUrl) {
      if (isSafeLocalReturnUrl(returnUrl) && returnUrl !== '/login') return returnUrl;
      return { name: 'Dashboard' };
    }
    return true;
  }

  if (!authStore.isAuthenticated) return { name: 'Login', query: { returnUrl: to.fullPath } };

  const requiredPermission = to.meta.requiredPermission as string | undefined;
  if (requiredPermission && !authStore.hasPermission(requiredPermission)) {
    return { name: 'Forbidden' };
  }

  return true;
});

export default router;
