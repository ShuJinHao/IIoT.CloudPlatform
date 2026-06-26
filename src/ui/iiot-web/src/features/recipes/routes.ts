import type { RouteRecordRaw } from 'vue-router';
import { Permissions } from '../../types/permissions';

export const recipeRoutes: RouteRecordRaw[] = [
  {
    path: 'recipes',
    name: 'Recipes',
    component: () => import('./RecipeListPage.vue'),
    meta: { requiresAuth: true, requiredPermission: Permissions.Recipe.Read, title: '配方管理' },
  },
];
