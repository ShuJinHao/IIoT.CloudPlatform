export interface RecipeListItemDto {
  id: string;
  recipeName: string;
  version: string;
  processId: string;
  deviceId: string;
  status: string;
}

export interface RecipeDetailDto {
  id: string;
  recipeName: string;
  version: string;
  processId: string;
  deviceId: string;
  parametersJsonb: string;
  status: string;
}

export interface RecipeParameter {
  id: string;
  name: string;
  unit: string;
  min: number;
  max: number;
}

export interface CreateRecipePayload {
  recipeName: string;
  processId: string;
  deviceId: string;
  parametersJsonb: string;
}

export interface UpgradeRecipeVersionPayload {
  sourceRecipeId: string;
  newVersion: string;
  parametersJsonb: string;
}

export interface RecipeCreateForm {
  recipeName: string;
  processId: string | null;
  deviceId: string | null;
}

export interface RecipeUpgradeForm {
  newVersion: string;
}

export interface RecipeConfirmDialogState {
  show: boolean;
  title: string;
  desc: string;
  confirmText: string;
  onConfirm: () => Promise<void>;
}

export const EMPTY_DEVICE_ID = '00000000-0000-0000-0000-000000000000';

export function isDeviceBoundRecipe(deviceId?: string | null): boolean {
  return Boolean(deviceId && deviceId !== EMPTY_DEVICE_ID);
}

export function generateParamId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }

  return Math.random().toString(36).substring(2, 10);
}

export function parseParams(jsonb: string): RecipeParameter[] {
  try {
    const parsed = JSON.parse(jsonb) as unknown;
    if (!Array.isArray(parsed)) return [];

    return parsed.map(normalizeParam);
  } catch {
    return [];
  }
}

export function paramsToJsonb(params: RecipeParameter[]): string {
  return JSON.stringify(
    params.map((param) => ({
      id: param.id,
      name: param.name,
      unit: param.unit,
      min: param.min,
      max: param.max,
    })),
  );
}

export function validateParams(params: RecipeParameter[]): string | null {
  if (params.length === 0) return '至少保留一个工艺参数';

  for (let i = 0; i < params.length; i += 1) {
    const param = params[i]!;
    const label = `第 ${i + 1} 个参数`;
    if (!param.id?.trim()) return `${label}缺少参数标识，请删除后重新添加`;
    if (!param.name?.trim()) return `${label}的参数名称不能为空`;
    if (!param.unit?.trim()) return `${label}的单位不能为空`;
    if (typeof param.min !== 'number' || Number.isNaN(param.min)) {
      return `${label}的下限必须是数字`;
    }
    if (typeof param.max !== 'number' || Number.isNaN(param.max)) {
      return `${label}的上限必须是数字`;
    }
    if (param.min > param.max) return `${label}的下限不能大于上限`;
  }

  return null;
}

export function prettyJson(value: string): string {
  try {
    return JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    return value;
  }
}

export function isRecipeDetailDto(value: unknown): value is RecipeDetailDto {
  if (!value || typeof value !== 'object') return false;
  const candidate = value as Partial<RecipeDetailDto>;
  return typeof candidate.id === 'string' && typeof candidate.parametersJsonb === 'string';
}

function normalizeParam(value: unknown): RecipeParameter {
  const record = value && typeof value === 'object'
    ? value as Record<string, unknown>
    : {};

  return {
    id: typeof record.id === 'string' && record.id.trim() ? record.id : generateParamId(),
    name: typeof record.name === 'string' ? record.name : '',
    unit: typeof record.unit === 'string' ? record.unit : '',
    min: toNumber(record.min),
    max: toNumber(record.max),
  };
}

function toNumber(value: unknown): number {
  if (typeof value === 'number') return value;
  if (typeof value === 'string' && value.trim() !== '') {
    const parsed = Number(value);
    return Number.isNaN(parsed) ? 0 : parsed;
  }

  return 0;
}
