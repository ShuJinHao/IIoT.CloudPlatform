import http from '../utils/http';

export interface Pagination {
  PageNumber: number;
  PageSize: number;
}

export interface PagedMetaData {
  totalCount: number;
  pageSize: number;
  currentPage: number;
  totalPages: number;
}

export interface PagedList<T> {
  items: T[];
  metaData: PagedMetaData;
}

export interface EmployeeListItemDto {
  id: string;
  employeeNo: string;
  realName: string;
  isActive: boolean;
  deviceCount: number;
}

export interface EmployeeDetailDto {
  id: string;
  employeeNo: string;
  realName: string;
  isActive: boolean;
  deviceIds: string[];
}

export interface EmployeeAccessDto {
  deviceIds: string[];
}

export interface OnboardEmployeePayload {
  employeeNo: string;
  realName: string;
  password: string;
  roleName?: string;
}

export interface UpdateProfilePayload {
  employeeId: string;
  realName: string;
  isActive: boolean;
}

export interface UpdateAccessPayload {
  employeeId: string;
  deviceIds: string[];
}

const basePath = '/human/employees';

export const getEmployeePagedListApi = (params: {
  PaginationParams?: Pagination;
  Keyword?: string;
}) =>
  http.get<PagedList<EmployeeListItemDto>>(basePath, {
    params: {
      'PaginationParams.PageNumber': params.PaginationParams?.PageNumber ?? 1,
      'PaginationParams.PageSize': params.PaginationParams?.PageSize ?? 10,
      Keyword: params.Keyword || undefined,
    }
  });

export const getEmployeeDetailApi = (id: string) =>
  http.get<EmployeeDetailDto>(`${basePath}/${id}`);

export const getEmployeeAccessApi = (id: string) =>
  http.get<EmployeeAccessDto>(`${basePath}/${id}/access`);

export const onboardEmployeeApi = (payload: OnboardEmployeePayload) =>
  http.post<string>(basePath, payload);

export const updateEmployeeProfileApi = (id: string, payload: UpdateProfilePayload) =>
  http.put<boolean>(`${basePath}/${id}/profile`, payload);

export const updateEmployeeAccessApi = (id: string, payload: UpdateAccessPayload) =>
  http.put<boolean>(`${basePath}/${id}/access`, payload);

export const deactivateEmployeeApi = (id: string) =>
  http.put<boolean>(`${basePath}/${id}/deactivate`);

export const terminateEmployeeApi = (id: string) =>
  http.delete<boolean>(`${basePath}/${id}`);

export const getAllRolesApi = () =>
  http.get<string[]>('/human/identity/roles');
