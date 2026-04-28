import type {
  PassStationDetailDto,
  PassStationListItemDto,
  PassStationQueryMode,
  PassStationFieldValue,
} from '../../api/passStation';

export interface PassStationColumnSchema {
  key: string;
  label: string;
  variant?: 'barcode' | 'result';
  className?: string;
  render: (record: PassStationListItemDto) => string;
}

export interface PassStationDetailFieldSchema {
  key: string;
  label: string;
  className?: string;
  render: (detail: PassStationDetailDto) => string;
}

export interface PassStationSchema {
  typeKey: string;
  title: string;
  subtitle: string;
  supportedModes: PassStationQueryMode[];
  columns: PassStationColumnSchema[];
  detailFields: PassStationDetailFieldSchema[];
  unsupportedMessage: string;
}

function readFieldAsString(
  value: PassStationFieldValue | undefined,
  suffix = '',
) {
  if (value === null || value === undefined || value === '') {
    return '-';
  }

  return `${value}${suffix}`;
}

export function normalizePassStationTypeKey(processCode: string) {
  return processCode.trim().toLowerCase();
}

export const passStationSchemas: Record<string, PassStationSchema> = {
  injection: {
    typeKey: 'injection',
    title: '注液过站追溯',
    subtitle: '按工序、设备、条码和时间范围查询注液过站记录。',
    supportedModes: [
      'barcode-process',
      'time-process',
      'device-barcode',
      'device-time',
      'device-latest',
    ],
    columns: [
      {
        key: 'barcode',
        label: '条码',
        variant: 'barcode',
        render: (record) => record.barcode || '-',
      },
      {
        key: 'cellResult',
        label: '结果',
        variant: 'result',
        render: (record) => record.cellResult || '-',
      },
      {
        key: 'injectionVolume',
        label: '注液量',
        className: 'mono',
        render: (record) => readFieldAsString(record.fields.injectionVolume, ' ml'),
      },
      {
        key: 'preInjectionWeight',
        label: '注液前重量',
        className: 'mono',
        render: (record) => readFieldAsString(record.fields.preInjectionWeight, ' g'),
      },
      {
        key: 'postInjectionWeight',
        label: '注液后重量',
        className: 'mono',
        render: (record) => readFieldAsString(record.fields.postInjectionWeight, ' g'),
      },
      {
        key: 'completedTime',
        label: '完成时间',
        className: 'time-cell',
        render: (record) => record.completedTime || '-',
      },
    ],
    detailFields: [
      {
        key: 'barcode',
        label: '条码',
        className: 'mono-val',
        render: (detail) => detail.barcode || '-',
      },
      {
        key: 'deviceId',
        label: '设备 ID',
        className: 'mono-val small',
        render: (detail) => detail.deviceId,
      },
      {
        key: 'preInjectionTime',
        label: '注液前时间',
        render: (detail) => readFieldAsString(detail.fields.preInjectionTime),
      },
      {
        key: 'preInjectionWeight',
        label: '注液前重量',
        className: 'mono-val',
        render: (detail) => readFieldAsString(detail.fields.preInjectionWeight, ' g'),
      },
      {
        key: 'postInjectionTime',
        label: '注液后时间',
        render: (detail) => readFieldAsString(detail.fields.postInjectionTime),
      },
      {
        key: 'postInjectionWeight',
        label: '注液后重量',
        className: 'mono-val',
        render: (detail) => readFieldAsString(detail.fields.postInjectionWeight, ' g'),
      },
      {
        key: 'injectionVolume',
        label: '注液量',
        className: 'mono-val highlight',
        render: (detail) => readFieldAsString(detail.fields.injectionVolume, ' ml'),
      },
      {
        key: 'completedTime',
        label: '完成时间',
        render: (detail) => detail.completedTime || '-',
      },
      {
        key: 'receivedAt',
        label: '接收时间',
        className: 'small',
        render: (detail) => detail.receivedAt || '-',
      },
    ],
    unsupportedMessage: '当前工序暂未开放注液过站追溯。',
  },
  stacking: {
    typeKey: 'stacking',
    title: '叠片过站追溯',
    subtitle: '在同一查询页内追溯叠片过站记录。',
    supportedModes: [
      'barcode-process',
      'time-process',
      'device-barcode',
      'device-time',
      'device-latest',
    ],
    columns: [
      {
        key: 'barcode',
        label: '条码',
        variant: 'barcode',
        render: (record) => record.barcode || '-',
      },
      {
        key: 'cellResult',
        label: '结果',
        variant: 'result',
        render: (record) => record.cellResult || '-',
      },
      {
        key: 'trayCode',
        label: '托盘码',
        className: 'mono',
        render: (record) => readFieldAsString(record.fields.trayCode),
      },
      {
        key: 'sequenceNo',
        label: '序号',
        className: 'mono',
        render: (record) => readFieldAsString(record.fields.sequenceNo),
      },
      {
        key: 'layerCount',
        label: '层数',
        className: 'mono',
        render: (record) => readFieldAsString(record.fields.layerCount),
      },
      {
        key: 'completedTime',
        label: '完成时间',
        className: 'time-cell',
        render: (record) => record.completedTime || '-',
      },
    ],
    detailFields: [
      {
        key: 'barcode',
        label: '条码',
        className: 'mono-val',
        render: (detail) => detail.barcode || '-',
      },
      {
        key: 'deviceId',
        label: '设备 ID',
        className: 'mono-val small',
        render: (detail) => detail.deviceId,
      },
      {
        key: 'trayCode',
        label: '托盘码',
        className: 'mono-val',
        render: (detail) => readFieldAsString(detail.fields.trayCode),
      },
      {
        key: 'sequenceNo',
        label: '序号',
        className: 'mono-val',
        render: (detail) => readFieldAsString(detail.fields.sequenceNo),
      },
      {
        key: 'layerCount',
        label: '层数',
        className: 'mono-val highlight',
        render: (detail) => readFieldAsString(detail.fields.layerCount),
      },
      {
        key: 'completedTime',
        label: '完成时间',
        render: (detail) => detail.completedTime || '-',
      },
      {
        key: 'receivedAt',
        label: '接收时间',
        className: 'small',
        render: (detail) => detail.receivedAt || '-',
      },
    ],
    unsupportedMessage: '当前工序暂未开放叠片过站追溯。',
  },
};

export function getPassStationSchema(typeKey: string) {
  return passStationSchemas[typeKey] || null;
}
