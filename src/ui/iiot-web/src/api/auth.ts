// src/api/auth.ts
import http from '../utils/http';

export interface LoginPayload {
  employeeNo: string;
  password?: string;
}

export const loginApi = (data: LoginPayload) => {
  // 🌟 强制首字母大写，对齐 C# Record 字段名
  const csharpPayload = {
    EmployeeNo: data.employeeNo,
    Password: data.password
  };
  return http.post<string>('/identity/login', csharpPayload);
};
