import type { Component, VNodeChild } from 'vue';

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
  formatter?: (
    value: unknown,
    row: T,
    rowIndex: number,
  ) => string | number | null | undefined;
  component?: Component;
  componentProps?: (
    context: UiDataTableCellContext<T>,
  ) => Record<string, unknown>;
  slot?: string;
}

export interface UiDataTableCellContext<T = Record<string, unknown>> {
  row: T;
  rowIndex: number;
  value: unknown;
  column: UiDataTableColumn<T>;
}
