import type {
  PassStationDetailDto,
  PassStationFieldDefinitionDto,
  PassStationFieldValue,
  PassStationListItemDto,
  PassStationQueryMode,
  PassStationTypeDefinitionDto,
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

export interface PassStationDetailSectionSchema {
  title: string;
  fields: PassStationDetailFieldSchema[];
}

export interface PassStationSchema {
  typeKey: string;
  title: string;
  subtitle: string;
  supportedModes: PassStationQueryMode[];
  columns: PassStationColumnSchema[];
  detailSections: PassStationDetailSectionSchema[];
  detailFields: PassStationDetailFieldSchema[];
  unsupportedMessage: string;
}

const commonFieldLabels: Record<string, string> = {
  barcode: '条码',
  deviceId: '设备 ID',
  cellResult: '结果',
  completedTime: '完成时间',
  receivedAt: '接收时间',
};

const commonFieldClassNames: Record<string, string> = {
  barcode: 'mono-val',
  deviceId: 'mono-val small',
  completedTime: 'small',
  receivedAt: 'small',
};

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

function getFieldDefinition(
  definition: PassStationTypeDefinitionDto,
  key: string,
) {
  return definition.fields.find((field) => field.key === key) ?? null;
}

function getFieldLabel(
  definition: PassStationTypeDefinitionDto,
  key: string,
) {
  return commonFieldLabels[key] ?? getFieldDefinition(definition, key)?.label ?? key;
}

function getListClassName(
  field: PassStationFieldDefinitionDto | null,
  key: string,
) {
  if (key === 'completedTime' || key === 'receivedAt') {
    return 'time-cell';
  }

  if (field?.type === 'number' || field?.type === 'integer') {
    return 'mono';
  }

  return undefined;
}

function getDetailClassName(
  field: PassStationFieldDefinitionDto | null,
  key: string,
) {
  if (commonFieldClassNames[key]) {
    return commonFieldClassNames[key];
  }

  if (field?.type === 'number' || field?.type === 'integer') {
    return 'mono-val';
  }

  return undefined;
}

function getRecordValue(
  record: PassStationListItemDto,
  key: string,
) {
  if (key === 'barcode') return record.barcode;
  if (key === 'deviceId') return record.deviceId;
  if (key === 'cellResult') return record.cellResult;
  if (key === 'completedTime') return record.completedTime;
  if (key === 'receivedAt') return record.receivedAt;
  return record.fields[key];
}

function getDetailValue(
  detail: PassStationDetailDto,
  key: string,
) {
  if (key === 'barcode') return detail.barcode;
  if (key === 'deviceId') return detail.deviceId;
  if (key === 'cellResult') return detail.cellResult;
  if (key === 'completedTime') return detail.completedTime;
  if (key === 'receivedAt') return detail.receivedAt;
  return detail.fields[key];
}

function buildColumn(
  definition: PassStationTypeDefinitionDto,
  key: string,
): PassStationColumnSchema {
  const field = getFieldDefinition(definition, key);

  return {
    key,
    label: getFieldLabel(definition, key),
    variant: key === 'barcode' ? 'barcode' : key === 'cellResult' ? 'result' : undefined,
    className: getListClassName(field, key),
    render: (record) => readFieldAsString(getRecordValue(record, key), field?.unit ? ` ${field.unit}` : ''),
  };
}

function buildDetailField(
  definition: PassStationTypeDefinitionDto,
  key: string,
): PassStationDetailFieldSchema {
  const field = getFieldDefinition(definition, key);

  return {
    key,
    label: getFieldLabel(definition, key),
    className: getDetailClassName(field, key),
    render: (detail) => readFieldAsString(getDetailValue(detail, key), field?.unit ? ` ${field.unit}` : ''),
  };
}

export function buildPassStationSchema(
  definition: PassStationTypeDefinitionDto,
): PassStationSchema {
  const detailSections = definition.detailSections.map((section) => ({
    title: section.title,
    fields: section.fields.map((fieldKey) => buildDetailField(definition, fieldKey)),
  }));

  return {
    typeKey: definition.typeKey,
    title: `${definition.displayName}过站追溯`,
    subtitle: definition.description,
    supportedModes: definition.supportedModes,
    columns: definition.listColumns.map((key) => buildColumn(definition, key)),
    detailSections,
    detailFields: detailSections.flatMap((section) => section.fields),
    unsupportedMessage: `当前工序暂未开放${definition.displayName}过站追溯。`,
  };
}

export function buildPassStationSchemaMap(
  definitions: PassStationTypeDefinitionDto[],
) {
  return definitions.reduce<Record<string, PassStationSchema>>((acc, definition) => {
    acc[normalizePassStationTypeKey(definition.typeKey)] = buildPassStationSchema(definition);
    return acc;
  }, {});
}

export function getPassStationSchema(
  schemas: Record<string, PassStationSchema>,
  typeKey: string,
) {
  return schemas[typeKey] || null;
}
