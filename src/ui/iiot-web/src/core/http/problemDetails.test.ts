import { describe, expect, it } from 'vitest';
import {
  normalizeErrors,
  readProblemDetails,
  resolveProblemNotification,
} from './problemDetails';

describe('problemDetails', () => {
  it('normalizes validation error arrays and records', () => {
    expect(normalizeErrors([' 名称必填 ', '', 'Code 无效'])).toEqual([
      '名称必填',
      'Code 无效',
    ]);
    expect(normalizeErrors({ name: ['名称必填'], code: 'Code 无效' })).toEqual([
      '名称必填',
      'Code 无效',
    ]);
  });

  it('uses detail before validation errors and title', () => {
    const notification = resolveProblemNotification(400, {
      title: '校验失败',
      detail: '设备名称不能为空',
      errors: { deviceName: ['名称必填'] },
    });

    expect(notification).toEqual({
      title: '请求处理失败',
      message: '设备名称不能为空',
      details: ['名称必填'],
    });
  });

  it('falls back to validation error then status message', () => {
    expect(resolveProblemNotification(400, {
      title: '校验失败',
      errors: { deviceName: ['名称必填'] },
    }).message).toBe('名称必填');

    expect(resolveProblemNotification(403, null).message).toBe('当前账号无权访问该功能。');
  });

  it('reads json and plain text blobs', async () => {
    const jsonBlob = new Blob([
      JSON.stringify({ detail: '发布说明不能为空' }),
    ], { type: 'application/json' });
    await expect(readProblemDetails(jsonBlob, 'application/json')).resolves.toEqual({
      detail: '发布说明不能为空',
    });

    const textBlob = new Blob(['服务暂不可用'], { type: 'text/plain' });
    await expect(readProblemDetails(textBlob, 'text/plain')).resolves.toEqual({
      detail: '服务暂不可用',
    });
  });
});
