import type { VNodeChild } from 'vue';

export interface UiSelectOption {
  label: string;
  value: string | number | boolean | null;
  disabled?: boolean;
}

export interface UiDataTableColumn<T = Record<string, unknown>> {
  title: string;
  key: string;
  width?: number | string;
  minWidth?: number | string;
  align?: 'left' | 'center' | 'right';
  render?: (row: T, rowIndex: number) => VNodeChild;
}
