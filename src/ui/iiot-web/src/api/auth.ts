import http from '../utils/http';

export interface LoginPayload {
  employeeNo: string;
  password?: string;
}

const basePath = '/human/identity';

export const loginApi = (data: LoginPayload) => {
  return http.post<string>(`${basePath}/login`, {
    employeeNo: data.employeeNo,
    password: data.password,
  });
};
