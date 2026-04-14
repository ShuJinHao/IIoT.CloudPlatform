import http from '../utils/http';
import type { Pagination, PagedMetaData } from './employee';

export type { Pagination, PagedMetaData };

export interface RecipeListItemDto {
  id: string;
  recipeName: string;
  version: string;
  processId: string;
  deviceId: string;
  status: string;
}

export interface RecipeDetailDto {
  id: string;
  recipeName: string;
  version: string;
  processId: string;
  deviceId: string;
  parametersJsonb: string;
  status: string;
}

export interface RecipeParameter {
  id: string;
  name: string;
  unit: string;
  min: number;
  max: number;
}

export interface CreateRecipePayload {
  recipeName: string;
  processId: string;
  deviceId: string;
  parametersJsonb: string;
}

export interface UpgradeRecipeVersionPayload {
  sourceRecipeId: string;
  newVersion: string;
  parametersJsonb: string;
}

export interface PagedList<T> {
  items: T[];
  metaData: PagedMetaData;
}

const basePath = '/human/recipes';

export const getRecipePagedListApi = (params: {
  pagination?: Pagination;
  keyword?: string;
}) => {
  return http.get<PagedList<RecipeListItemDto>>(basePath, {
    params: {
      PageNumber: params.pagination?.PageNumber ?? 1,
      PageSize: params.pagination?.PageSize ?? 10,
      keyword: params.keyword || undefined,
    },
  });
};

export const getRecipeDetailApi = (id: string) => {
  return http.get<RecipeDetailDto>(`${basePath}/${id}`);
};

export const createRecipeApi = (payload: CreateRecipePayload) => {
  return http.post<string>(basePath, payload);
};

export const upgradeRecipeVersionApi = (id: string, payload: UpgradeRecipeVersionPayload) => {
  return http.post<string>(`${basePath}/${id}/upgrade`, payload);
};

export const deleteRecipeApi = (id: string) => {
  return http.delete<boolean>(`${basePath}/${id}`);
};
