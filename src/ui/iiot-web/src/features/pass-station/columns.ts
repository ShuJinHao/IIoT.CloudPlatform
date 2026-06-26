import { h } from 'vue';
import UiTag from '../../components/ui/UiTag.vue';
import type { UiDataTableColumn } from '../../components/ui/types';
import type { PassStationListItemDto } from './api';
import type { PassStationSchema } from './schema';
import { formatDisplayValue } from './types';

export function createPassStationColumns(
  schema: PassStationSchema | null,
): UiDataTableColumn<PassStationListItemDto>[] {
  if (!schema) return [];

  return schema.columns.map((col) => ({
    title: col.label,
    key: col.key,
    minWidth: col.variant === 'barcode' ? 200 : 140,
    render(record) {
      const raw = col.render(record);
      if (col.variant === 'barcode') return h('code', { class: 'cell-barcode' }, raw);
      if (col.variant === 'result') {
        const isOk = (record.cellResult ?? '').toUpperCase() === 'OK';
        return h(UiTag, { size: 'small', bordered: false, type: isOk ? 'success' : 'error' }, { default: () => raw });
      }
      return h('span', {
        class: [
          col.className === 'mono' ? 'cell-mono' : '',
          col.className === 'time-cell' ? 'cell-time' : '',
        ].filter(Boolean),
      }, formatDisplayValue(raw));
    },
  }));
}
