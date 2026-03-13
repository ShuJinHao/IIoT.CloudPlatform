// src/api/employee.ts
import http from '../utils/http';

// ==========================================
// DTO 类型定义（完全对齐后端 C# Record）
// ==========================================

/** 分页参数 */
export interface Pagination {
  PageNumber: number;
  PageSize: number;
}

/** 分页元数据 */
export interface PagedMetaData {
  totalCount: number;
  pageSize: number;
  currentPage: number;
  totalPages: number;
}

/** 分页列表返回结构（后端 PagedList<T> 序列化后是数组 + MetaData） */
export interface PagedList<T> {
  items: T[];
  metaData: PagedMetaData;
}

/** 员工列表项 DTO */
export interface EmployeeListItemDto {
  id: string;
  employeeNo: string;
  realName: string;
  isActive: boolean;
  processCount: number;  // 工序管辖数量（后端一次查完，无需额外请求）
  deviceCount: number;   // 机台管辖数量
}

/** 员工详情 DTO */
export interface EmployeeDetailDto {
  id: string;
  employeeNo: string;
  realName: string;
  isActive: boolean;
  processIds: string[];
  deviceIds: string[];
}

/** 员工双维管辖权 DTO */
export interface EmployeeAccessDto {
  processIds: string[];
  deviceIds: string[];
}

/** 入职指令（对齐 OnboardEmployeeCommand） */
export interface OnboardEmployeePayload {
  EmployeeNo: string;
  RealName: string;
  Password: string;
  RoleName?: string;
  ProcessIds?: string[];
  DeviceIds?: string[];
}

/** 更新档案指令（对齐 UpdateEmployeeProfileCommand） */
export interface UpdateProfilePayload {
  RealName: string;
  IsActive: boolean;
}

/** 更新管辖权指令（对齐 UpdateEmployeeAccessCommand） */
export interface UpdateAccessPayload {
  ProcessIds: string[];
  DeviceIds: string[];
}

// ==========================================
// API 调用函数
// ==========================================

/** 获取员工分页列表 */
export const getEmployeePagedListApi = (params: {
  PaginationParams?: Pagination;
  Keyword?: string;
}) => {
  return http.get<PagedList<EmployeeListItemDto>>('/employee', {
    params: {
      'PaginationParams.PageNumber': params.PaginationParams?.PageNumber ?? 1,
      'PaginationParams.PageSize': params.PaginationParams?.PageSize ?? 10,
      Keyword: params.Keyword || undefined,
    }
  });
};

/** 获取员工详情 */
export const getEmployeeDetailApi = (id: string) => {
  return http.get<EmployeeDetailDto>(`/employee/${id}`);
};

/** 获取员工双维管辖权 */
export const getEmployeeAccessApi = (id: string) => {
  return http.get<EmployeeAccessDto>(`/employee/${id}/access`);
};

/** 员工入职建档 */
export const onboardEmployeeApi = (payload: OnboardEmployeePayload) => {
  return http.post<string>('/employee', payload);
};

/** 更新员工基础档案 */
export const updateEmployeeProfileApi = (id: string, payload: UpdateProfilePayload) => {
  return http.put<boolean>(`/employee/${id}/profile`, payload);
};

/** 更新员工双维管辖权 */
export const updateEmployeeAccessApi = (id: string, payload: UpdateAccessPayload) => {
  return http.put<boolean>(`/employee/${id}/access`, payload);
};

/** 停用员工（软删除） */
export const deactivateEmployeeApi = (id: string) => {
  return http.put<boolean>(`/employee/${id}/deactivate`);
};

/** 离职员工（硬删除） */
export const terminateEmployeeApi = (id: string) => {
  return http.delete<boolean>(`/employee/${id}`);
};

// ==========================================
// Identity 相关（获取角色列表，入职时用）
// ==========================================
export const getAllRolesApi = () => {
  return http.get<string[]>('/identity/roles');
};
