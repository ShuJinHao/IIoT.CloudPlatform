import type { RouteRecordRaw } from 'vue-router';
import { Permissions } from '../../types/permissions';

export const employeeRoutes: RouteRecordRaw[] = [
  {
    path: 'employees',
    name: 'Employees',
    component: () => import('./EmployeeListPage.vue'),
    meta: { requiresAuth: true, requiredPermission: Permissions.Employee.Read, title: '员工花名册' },
  },
];
