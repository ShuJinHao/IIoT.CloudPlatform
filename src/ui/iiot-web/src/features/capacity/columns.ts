import { h } from 'vue';
import UiButton from '../../components/ui/UiButton.vue';
import type { UiDataTableColumn } from '../../components/ui/types';
import type { DailyCapacityItem } from './api';
import type { CapacityDetailRow, CapacityQueryMode } from './types';
import { formatInt, rateAccent } from './types';

interface DashboardColumnOptions {
  onDetail: (deviceId: string, deviceName: string) => void;
}

export function renderRateBar(rate: number) {
  const tone = rateAccent(rate);
  return h('div', { class: 'rate-cell' }, [
    h('div', { class: 'rate-cell__track' }, [
      h('div', {
        class: ['rate-cell__bar', `rate-cell__bar--${tone}`],
        style: { width: `${Math.min(100, rate)}%` },
      }),
    ]),
    h(
      'span',
      { class: ['rate-cell__text', `rate-cell__text--${tone}`] },
      `${rate.toFixed(1)}%`,
    ),
  ]);
}

export function createCapacityDashboardColumns(
  options: DashboardColumnOptions,
): UiDataTableColumn<DailyCapacityItem>[] {
  return [
    {
      title: '设备',
      key: 'deviceName',
      minWidth: 180,
      render(row) {
        return h('span', { class: 'cell-device' }, row.deviceName);
      },
    },
    {
      title: '日期',
      key: 'date',
      width: 130,
      render(row) {
        return h('span', { class: 'cell-mono' }, row.date);
      },
    },
    {
      title: '总产出',
      key: 'totalCount',
      align: 'right',
      width: 120,
      render(row) {
        return h('span', { class: 'cell-mono' }, formatInt(row.totalCount));
      },
    },
    {
      title: '良品',
      key: 'okCount',
      align: 'right',
      width: 110,
      render(row) {
        return h('span', { class: 'cell-mono cell-num--ok' }, formatInt(row.okCount));
      },
    },
    {
      title: '不良品',
      key: 'ngCount',
      align: 'right',
      width: 110,
      render(row) {
        return h('span', { class: 'cell-mono cell-num--ng' }, formatInt(row.ngCount));
      },
    },
    {
      title: '良率',
      key: 'okRate',
      minWidth: 180,
      render(row) {
        return renderRateBar(row.okRate);
      },
    },
    {
      title: '操作',
      key: 'actions',
      width: 110,
      align: 'center',
      render(row) {
        return h(
          UiButton,
          {
            size: 'tiny',
            type: 'primary',
            secondary: true,
            disabled: !row.deviceId,
            onClick: () => options.onDetail(row.deviceId, row.deviceName),
          },
          { default: () => '查看详情' },
        );
      },
    },
  ];
}

function renderShiftTag(shift: string) {
  if (!shift) return null;
  return h(
    'span',
    { class: 'shift-tag' },
    shift === 'D' ? '白班' : shift === 'N' ? '夜班' : shift,
  );
}

export function createCapacityDetailColumns(
  queryMode: () => CapacityQueryMode,
): UiDataTableColumn<CapacityDetailRow>[] {
  const columns: UiDataTableColumn<CapacityDetailRow>[] = [
    {
      title: queryMode() === 'day'
        ? '时间段'
        : queryMode() === 'month'
          ? '日期'
          : '月份',
      key: 'label',
      minWidth: 140,
      render(row) {
        return h('span', { class: 'cell-mono' }, row.label);
      },
    },
  ];

  if (queryMode() === 'day') {
    columns.push({
      title: '班次',
      key: 'shift',
      width: 100,
      render(row) {
        return renderShiftTag(row.shift);
      },
    });
  }

  columns.push(
    {
      title: '总产出',
      key: 'total',
      align: 'right',
      width: 110,
      render(row) {
        return h('span', { class: 'cell-mono' }, formatInt(row.total));
      },
    },
    {
      title: '良品',
      key: 'ok',
      align: 'right',
      width: 110,
      render(row) {
        return h('span', { class: 'cell-mono cell-num--ok' }, formatInt(row.ok));
      },
    },
    {
      title: '不良品',
      key: 'ng',
      align: 'right',
      width: 110,
      render(row) {
        return h('span', { class: 'cell-mono cell-num--ng' }, formatInt(row.ng));
      },
    },
    {
      title: '良率',
      key: 'rate',
      width: 100,
      align: 'right',
      render(row) {
        const tone = rateAccent(row.rate);
        const toneClass = tone === 'success' ? 'ok' : tone === 'warn' ? 'warn' : 'ng';
        return h(
          'span',
          { class: ['cell-mono', `cell-num--${toneClass}`] },
          `${row.rate.toFixed(1)}%`,
        );
      },
    },
  );

  return columns;
}
