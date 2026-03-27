// src/api/recipe.ts
import http from '../utils/http';
import type { Pagination, PagedMetaData } from './employee';

export type { Pagination, PagedMetaData };

// ==========================================
// DTO 类型定义（完全对齐后端 C# Record）
// ==========================================

/** 配方列表项 DTO — 对齐 RecipeListItemDto */
export interface RecipeListItemDto {
  id: string;
  recipeName: string;
  version: string;
  processId: string;
  deviceId: string | null;
  status: string;  // "Active" | "Archived"
}

/** 配方详情 DTO — 对齐 RecipeDetailDto（含完整 JSONB） */
export interface RecipeDetailDto {
  id: string;
  recipeName: string;
  version: string;
  processId: string;
  deviceId: string | null;
  parametersJsonb: string;
  status: string;
}

/** 配方参数结构 */
export interface RecipeParameter {
  id: string;
  name: string;
  unit: string;
  min: number;
  max: number;
}

/** 创建配方指令 — 对齐 CreateRecipeCommand */
export interface CreateRecipePayload {
  RecipeName: string;
  ProcessId: string;
  DeviceId?: string | null;
  ParametersJsonb: string;
}

/** 升级配方版本指令 — 对齐 UpgradeRecipeVersionCommand */
export interface UpgradeRecipeVersionPayload {
  NewVersion: string;
  ParametersJsonb: string;
}

/** 分页返回包装 */
export interface PagedList<T> {
  items: T[];
  metaData: PagedMetaData;
}

// ==========================================
// API 调用函数
// ==========================================

/** 获取我管辖范围内的配方分页列表 — GET /api/v1/recipe */
export const getRecipePagedListApi = (params: {
  pagination?: Pagination;
  keyword?: string;
}) => {
  return http.get<PagedList<RecipeListItemDto>>('/recipe', {
    params: {
      'pagination.PageNumber': params.pagination?.PageNumber ?? 1,
      'pagination.PageSize': params.pagination?.PageSize ?? 10,
      keyword: params.keyword || undefined,
    },
  });
};

/** 获取配方详情（含 JSONB）— GET /api/v1/recipe/{id} */
export const getRecipeDetailApi = (id: string) => {
  return http.get<RecipeDetailDto>(`/recipe/${id}`);
};

/** 创建新配方 — POST /api/v1/recipe */
export const createRecipeApi = (payload: CreateRecipePayload) => {
  return http.post<string>('/recipe', payload);
};

/** 升级配方版本 — POST /api/v1/recipe/{id}/upgrade */
export const upgradeRecipeVersionApi = (id: string, payload: UpgradeRecipeVersionPayload) => {
  return http.post<string>(`/recipe/${id}/upgrade`, payload);
};

/** 物理删除配方 — DELETE /api/v1/recipe/{id} */
export const deleteRecipeApi = (id: string) => {
  return http.delete<boolean>(`/recipe/${id}`);
};