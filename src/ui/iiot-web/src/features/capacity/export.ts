import type { CapacityDetailRow, CapacityQueryMode } from './types';

const escapeCsv = (value: string | number) => {
  const text = String(value);
  return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
};

const shiftText = (shift: string) =>
  shift === 'D' ? '白班' : shift === 'N' ? '夜班' : shift || '—';

export function buildCapacityCsv(rows: CapacityDetailRow[]): string {
  const header = [
    'PLC 名称',
    '时间范围',
    '班次',
    '完工弹夹数',
    '合格弹夹数',
    '不合格弹夹数',
    '良率',
  ];
  const body = rows.map((row) => [
    row.plcName,
    row.period,
    shiftText(row.shift),
    row.total,
    row.ok,
    row.ng,
    `${row.rate.toFixed(2)}%`,
  ]);
  return `\uFEFF${[header, ...body]
    .map((values) => values.map(escapeCsv).join(','))
    .join('\r\n')}`;
}

export function capacityExportFileName(
  deviceName: string,
  mode: CapacityQueryMode,
  scope: string,
  plcName: string,
): string {
  const modeText = mode === 'day' ? '日' : mode === 'month' ? '月' : '年';
  const safe = `${deviceName}-${scope}-${plcName}`
    .replace(/[\\/:*?"<>|]/g, '-')
    .replace(/\s+/g, '-');
  return `${safe}-${modeText}完工弹夹明细.csv`;
}

export function downloadCapacityCsv(fileName: string, rows: CapacityDetailRow[]) {
  const blob = new Blob([buildCapacityCsv(rows)], { type: 'text/csv;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  anchor.click();
  URL.revokeObjectURL(url);
}
