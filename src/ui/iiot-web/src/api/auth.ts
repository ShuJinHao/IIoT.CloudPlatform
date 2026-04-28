import axios, { type AxiosResponse } from 'axios';

export interface LoginPayload {
  employeeNo: string;
  password?: string;
}

export interface AuthSessionPayload {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
}

const authClient = axios.create({
  baseURL: '/api/v1',
  timeout: 15000,
  headers: {
    'Content-Type': 'application/json',
  },
});

const basePath = '/human/identity';
const refreshTokenHeader = 'x-iiot-refresh-token';
const refreshTokenExpiresAtHeader = 'x-iiot-refresh-token-expires-at';
const accessTokenExpiresAtHeader = 'x-iiot-access-token-expires-at';

function parseSession(response: AxiosResponse<string>): AuthSessionPayload {
  const accessToken = typeof response.data === 'string' ? response.data : '';
  const refreshToken = response.headers[refreshTokenHeader];
  const refreshTokenExpiresAt = response.headers[refreshTokenExpiresAtHeader];
  const accessTokenExpiresAt = response.headers[accessTokenExpiresAtHeader];

  if (!accessToken || !refreshToken || !refreshTokenExpiresAt || !accessTokenExpiresAt) {
    throw new Error('Missing authentication session headers.');
  }

  return {
    accessToken,
    refreshToken,
    refreshTokenExpiresAt,
    accessTokenExpiresAt,
  };
}

export const loginApi = async (data: LoginPayload): Promise<AuthSessionPayload> => {
  const response = await authClient.post<string>(`${basePath}/login`, {
    employeeNo: data.employeeNo,
    password: data.password,
  });

  return parseSession(response);
};

export const refreshHumanSessionApi = async (refreshToken: string): Promise<AuthSessionPayload> => {
  const response = await authClient.post<string>(
    `${basePath}/refresh`,
    null,
    {
      headers: {
        'X-IIoT-Refresh-Token': refreshToken,
      },
    },
  );

  return parseSession(response);
};
