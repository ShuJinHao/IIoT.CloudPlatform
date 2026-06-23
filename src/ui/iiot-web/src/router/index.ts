import { createRouter, createWebHistory } from 'vue-router';
import type { RouteRecordRaw } from 'vue-router';
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
      {
        path: '',
        name: 'Dashboard',
        component: () => import('../views/Dashboard.vue'),
        meta: { requiresAuth: true, title: '系统概览' }
      },
      {
        path: 'employees',
        name: 'Employees',
        component: () => import('../views/employees/EmployeeList.vue'),
        meta: { requiresAuth: true, requiredPermission: Permissions.Employee.Read, title: '员工花名册' }
      },
      {
        path: 'master-data/processes',
        name: 'MasterDataProcesses',
        component: () => import('../views/masterData/ProcessList.vue'),
        meta: { requiresAuth: true, requiredPermission: Permissions.Process.Read, title: '工序管理' }
      },
      {
        path: 'processes',
        redirect: { name: 'MasterDataProcesses' }
      },
      {
        path: 'devices',
        name: 'Devices',
        component: () => import('../views/devices/DeviceList.vue'),
        meta: { requiresAuth: true, requiredPermission: Permissions.Device.Read, title: '设备台账' }
      },
      {
        path: 'recipes',
        name: 'Recipes',
        component: () => import('../views/recipes/RecipeList.vue'),
        meta: { requiresAuth: true, requiredPermission: Permissions.Recipe.Read, title: '配方管理' }
      },
      {
        path: 'pass-station',
        name: 'PassStation',
        component: () => import('../views/passstation/PassStationList.vue'),
        meta: { requiresAuth: true, requiredPermission: Permissions.Device.Read, title: '过站追溯' }
      },
      {
        path: 'capacity',
        name: 'Capacity',
        component: () => import('../views/capacity/CapacityDashboard.vue'),
        meta: { requiresAuth: true, requiredPermission: Permissions.Device.Read, title: '产能看板' }
      },
      {
        path: 'capacity/detail',
        name: 'CapacityDetail',
        component: () => import('../views/capacity/CapacityDetail.vue'),
        meta: { requiresAuth: true, requiredPermission: Permissions.Device.Read, title: '产能详情' }
      },
      {
        path: 'device-logs',
        name: 'DeviceLogs',
        component: () => import('../views/devicelogs/DeviceLogList.vue'),
        meta: { requiresAuth: true, requiredPermission: Permissions.Device.Read, title: '设备日志' }
      },
      {
        path: 'client-releases',
        name: 'ClientReleases',
        component: () => import('../views/clientReleases/ClientReleaseCenter.vue'),
        meta: { requiresAuth: true, requiredPermission: Permissions.ClientRelease.Read, title: '客户端首装生成' }
      },
      {
        path: 'client-releases/publish',
        name: 'ClientReleasePublish',
        component: () => import('../views/clientReleases/ClientReleaseCenter.vue'),
        meta: { requiresAuth: true, requiredPermission: Permissions.ClientRelease.Manage, title: '客户端发布管理' }
      },
      {
        path: 'roles',
        name: 'Roles',
        component: () => import('../views/roles/RoleList.vue'),
        meta: { requiresAuth: true, requiredPermission: Permissions.Role.Define, title: '角色与权限' }
      },
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
