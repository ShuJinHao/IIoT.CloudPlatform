export interface Pagination {
  PageNumber: number;
  PageSize: number;
}

export interface PagedMetaData {
  totalCount: number;
  pageSize: number;
  currentPage: number;
  totalPages: number;
}

export interface PagedList<T> {
  items: T[];
  metaData: PagedMetaData;
}
