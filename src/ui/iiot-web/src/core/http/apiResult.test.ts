import { describe, expect, it } from 'vitest';
import { ResultStatus } from '../types/api';
import { resolveApiResultNotification } from './apiResult';

describe('apiResult', () => {
  it('keeps backend business errors visible', () => {
    expect(resolveApiResultNotification({
      isSuccess: false,
      status: ResultStatus.Invalid,
      errors: ['设备名称不能为空', '所属工序不能为空'],
    })).toEqual({
      title: '请求处理失败',
      message: '设备名称不能为空\n所属工序不能为空',
    });
  });

  it('uses a permission-specific title for forbidden results', () => {
    expect(resolveApiResultNotification({
      isSuccess: false,
      status: ResultStatus.Forbidden,
      errors: ['缺少 Device.Delete 权限'],
    })).toEqual({
      title: '禁止访问',
      message: '缺少 Device.Delete 权限',
    });
  });
});
