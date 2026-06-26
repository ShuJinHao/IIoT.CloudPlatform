import http from '../../core/http/httpClient';
import type { PagedList, Pagination } from '../../core/types/pagination';
import type {
  CreateRecipePayload,
  RecipeDetailDto,
  RecipeListItemDto,
  UpgradeRecipeVersionPayload,
} from './types';

const basePath = '/human/recipes';

export const getRecipePagedListApi = (params: {
  pagination?: Pagination;
  keyword?: string;
}) =>
  http.get<PagedList<RecipeListItemDto>>(basePath, {
    params: {
      PageNumber: params.pagination?.PageNumber ?? 1,
      PageSize: params.pagination?.PageSize ?? 10,
      keyword: params.keyword || undefined,
    },
  });

export const getRecipeDetailApi = (id: string) =>
  http.get<RecipeDetailDto>(`${basePath}/${id}`);

export const createRecipeApi = (payload: CreateRecipePayload) =>
  http.post<string>(basePath, payload);

export const upgradeRecipeVersionApi = (id: string, payload: UpgradeRecipeVersionPayload) =>
  http.post<string>(`${basePath}/${id}/upgrade`, payload);

export const deleteRecipeApi = (id: string) =>
  http.delete<boolean>(`${basePath}/${id}`);
