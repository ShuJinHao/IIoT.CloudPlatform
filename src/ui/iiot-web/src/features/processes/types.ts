import type { PagedMetaData } from '../../core/types/pagination';
import type { ProcessListItemDto } from './api';

export const PROCESS_PAGE_SIZE = 10;

export interface ProcessFormData {
  processCode: string;
  processName: string;
}

export interface ProcessConfirmDialog {
  show: boolean;
  title: string;
  desc: string;
  confirmText: string;
  onConfirm: () => Promise<void>;
}

export const emptyProcessMetaData = (): PagedMetaData => ({
  totalCount: 0,
  pageSize: PROCESS_PAGE_SIZE,
  currentPage: 1,
  totalPages: 1,
});

export function normalizeProcessPageResult(source: unknown) {
  if (source && typeof source === 'object' && 'metaData' in source) {
    const page = source as {
      items?: unknown;
      metaData?: PagedMetaData;
    };
    return {
      items: Array.isArray(page.items)
        ? page.items as ProcessListItemDto[]
        : [],
      metaData: page.metaData ?? emptyProcessMetaData(),
    };
  }

  if (Array.isArray(source)) {
    const items = source as ProcessListItemDto[];
    return {
      items,
      metaData: {
        totalCount: items.length,
        pageSize: PROCESS_PAGE_SIZE,
        currentPage: 1,
        totalPages: 1,
      },
    };
  }

  return { items: [], metaData: emptyProcessMetaData() };
}

export function validateProcessForm(formData: ProcessFormData) {
  if (!formData.processCode.trim() || !formData.processName.trim()) {
    return '编码和名称均为必填项';
  }
  return null;
}
