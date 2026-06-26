import { h } from 'vue';
import SeverityBadge from '../../components/feedback/SeverityBadge.vue';
import type { UiDataTableColumn } from '../../components/ui/types';
import type { DeviceLogListItemDto } from './api';
import { formatLogTime, levelToSeverity } from './types';

export function createDeviceLogColumns(): UiDataTableColumn<DeviceLogListItemDto>[] {
  return [
    {
      title: '级别',
      key: 'level',
      width: 90,
      render(row) {
        return h(SeverityBadge, {
          severity: levelToSeverity(row.level),
          label: row.level.toUpperCase(),
        });
      },
    },
    {
      title: '日志内容',
      key: 'message',
      minWidth: 320,
      render(row) {
        return h('span', { class: 'cell-msg' }, row.message);
      },
    },
    {
      title: '日志时间',
      key: 'logTime',
      width: 180,
      render(row) {
        return h('span', { class: 'cell-mono cell-time' }, formatLogTime(row.logTime));
      },
    },
    {
      title: '接收时间',
      key: 'receivedAt',
      width: 180,
      render(row) {
        return h('span', { class: 'cell-mono cell-time' }, formatLogTime(row.receivedAt));
      },
    },
  ];
}
