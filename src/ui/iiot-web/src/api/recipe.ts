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
  deviceId: string | null;  // null = 通用配方；有值 = 特调配方
  isActive: boolean;
}

/** 配方详情 DTO — 对齐 RecipeDetailDto（含完整 JSONB） */
export interface RecipeDetailDto {
  id: string;
  recipeName: string;
  version: string;
  processId: string;
  deviceId: string | null;
  parametersJsonb: string;  // 🌟 核心：完整 JSONB 工艺参数
  isActive: boolean;
}

/** 创建配方指令 — 对齐 CreateRecipeCommand */
export interface CreateRecipePayload {
  RecipeName: string;
  ProcessId: string;
  DeviceId?: string | null;   // 不传 = 通用配方
  ParametersJsonb: string;    // JSON 字符串
}

/** 更新配方参数指令 — 对齐 UpdateRecipeParametersCommand */
export interface UpdateRecipeParametersPayload {
  ParametersJsonb: string;
  Version: string;
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

/** 更新配方参数并升版 — PUT /api/v1/recipe/{id}/parameters */
export const updateRecipeParametersApi = (id: string, payload: UpdateRecipeParametersPayload) => {
  return http.put<boolean>(`/recipe/${id}/parameters`, payload);
};

/** 停用配方 — DELETE /api/v1/recipe/{id} */
export const deactivateRecipeApi = (id: string) => {
  return http.delete<boolean>(`/recipe/${id}`);
};
