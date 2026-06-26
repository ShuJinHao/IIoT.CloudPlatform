<template>
  <div class="ui-data-table" :class="$attrs.class">
    <div class="ui-data-table__scroll">
      <table>
        <thead>
          <tr>
            <th
              v-for="column in columns"
              :key="String(column.key)"
              :style="columnStyle(column)"
              :class="alignClass(column)"
            >
              {{ column.title }}
            </th>
          </tr>
        </thead>
        <tbody>
          <tr v-if="loading">
            <td :colspan="columns.length" class="ui-data-table__state">
              <slot name="loading">加载中...</slot>
            </td>
          </tr>
          <tr v-else-if="data.length === 0">
            <td :colspan="columns.length" class="ui-data-table__state">
              <slot name="empty">暂无数据</slot>
            </td>
          </tr>
          <tr v-for="(row, rowIndex) in data" v-else :key="rowIdentity(row, rowIndex)">
            <td
              v-for="column in columns"
              :key="String(column.key)"
              :style="columnStyle(column)"
              :class="alignClass(column)"
            >
              <slot
                v-if="column.slot && $slots[column.slot]"
                :name="column.slot"
                v-bind="cellContext(column, row, rowIndex)"
              />
              <component
                v-else-if="column.component"
                :is="column.component"
                v-bind="cellComponentProps(column, row, rowIndex)"
              />
              <component v-else :is="cellRenderer(column, row, rowIndex)" />
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<script setup lang="ts" generic="T extends object">
import { computed, h } from 'vue';
import type { UiDataTableCellContext, UiDataTableColumn } from './types';

const props = withDefaults(defineProps<{
  columns: UiDataTableColumn<T>[];
  data: T[];
  loading?: boolean;
  rowKey?: string | ((row: T) => string | number);
}>(), {
  data: () => [],
});

const data = computed(() => props.data ?? []);

function columnStyle(column: UiDataTableColumn<T>) {
  const style: Record<string, string> = {};
  if (column.width) style.width = typeof column.width === 'number' ? `${column.width}px` : column.width;
  if (column.minWidth) style.minWidth = typeof column.minWidth === 'number' ? `${column.minWidth}px` : column.minWidth;
  return style;
}

function alignClass(column: UiDataTableColumn<T>) {
  return column.align ? `is-${column.align}` : '';
}

function rowIdentity(row: T, rowIndex: number) {
  if (typeof props.rowKey === 'function') return props.rowKey(row);
  if (typeof props.rowKey === 'string') return String(readRowValue(row, props.rowKey) ?? rowIndex);
  return String(readRowValue(row, 'id') ?? rowIndex);
}

function readRowValue(row: T, key: string) {
  return (row as Record<string, unknown>)[key];
}

function cellValue(column: UiDataTableColumn<T>, row: T) {
  return readRowValue(row, column.key);
}

function cellContext(
  column: UiDataTableColumn<T>,
  row: T,
  rowIndex: number,
): UiDataTableCellContext<T> {
  return {
    row,
    rowIndex,
    value: cellValue(column, row),
    column,
  };
}

function cellComponentProps(
  column: UiDataTableColumn<T>,
  row: T,
  rowIndex: number,
) {
  const context = cellContext(column, row, rowIndex);
  return {
    ...context,
    ...(column.componentProps?.(context) ?? {}),
  };
}

function cellRenderer(
  column: UiDataTableColumn<T>,
  row: T,
  rowIndex: number,
) {
  return {
    render() {
      if (column.render) return column.render(row, rowIndex);
      const value = cellValue(column, row);
      if (column.formatter) {
        const formatted = column.formatter(value, row, rowIndex);
        return h('span', formatted == null ? '-' : String(formatted));
      }
      return h('span', value == null ? '-' : String(value));
    },
  };
}
</script>

<style scoped>
.ui-data-table {
  width: 100%;
}

.ui-data-table__scroll {
  width: 100%;
  overflow-x: auto;
}

table {
  width: 100%;
  border-collapse: separate;
  border-spacing: 0;
  color: #111827;
  font-size: 13px;
}

th {
  background: #f4f7f8;
  color: #596273;
  font-size: 12px;
  font-weight: 900;
  text-align: left;
  white-space: nowrap;
  padding: 13px 14px;
  border-bottom: 1px solid rgba(17, 24, 39, 0.07);
}

td {
  background: #fff;
  padding: 14px;
  border-bottom: 1px solid rgba(17, 24, 39, 0.07);
  vertical-align: middle;
}

tbody tr:hover td {
  background: #f8fafb;
}

th:first-child,
td:first-child {
  border-left: 0;
}

.is-center {
  text-align: center;
}

.is-right {
  text-align: right;
}

.ui-data-table__state {
  height: 120px;
  text-align: center;
  color: #9aa3af;
  font-weight: 800;
}

:deep(.row-actions) {
  display: inline-flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 8px;
}
</style>
