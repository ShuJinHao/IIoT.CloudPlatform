import { computed, reactive, ref, watch, type Reactive, type Ref } from 'vue';

export interface ListPageFetchParams<TFilter> {
  page: number;
  pageSize: number;
  filter: TFilter;
}

export interface ListPageFetchResult<TItem> {
  items: TItem[];
  total: number;
}

export interface UseListPageOptions<
  TItem,
  TFilter extends Record<string, unknown>,
> {
  initialFilter?: TFilter;
  initialPage?: number;
  initialPageSize?: number;
  fetcher: (params: ListPageFetchParams<TFilter>) => Promise<ListPageFetchResult<TItem>>;
  immediate?: boolean;
}

export interface UseListPageReturn<TItem, TFilter> {
  items: Ref<TItem[]>;
  total: Ref<number>;
  page: Ref<number>;
  pageSize: Ref<number>;
  loading: Ref<boolean>;
  error: Ref<Error | null>;
  filter: Reactive<TFilter>;
  totalPages: Ref<number>;
  isEmpty: Ref<boolean>;
  refresh: () => Promise<void>;
  gotoPage: (p: number) => void;
  resetFilter: () => void;
}

export function useListPage<
  TItem,
  TFilter extends Record<string, unknown> = Record<string, unknown>,
>(
  options: UseListPageOptions<TItem, TFilter>,
): UseListPageReturn<TItem, TFilter> {
  const items = ref([]) as Ref<TItem[]>;
  const total = ref(0);
  const page = ref(options.initialPage ?? 1);
  const pageSize = ref(options.initialPageSize ?? 20);
  const loading = ref(false);
  const error = ref<Error | null>(null);
  const filter = reactive(
    { ...(options.initialFilter ?? ({} as TFilter)) },
  ) as Reactive<TFilter>;

  const totalPages = computed(() =>
    Math.max(1, Math.ceil(total.value / pageSize.value)),
  );
  const isEmpty = computed(() => !loading.value && items.value.length === 0);

  async function refresh() {
    loading.value = true;
    error.value = null;
    try {
      const result = await options.fetcher({
        page: page.value,
        pageSize: pageSize.value,
        filter: { ...(filter as object) } as TFilter,
      });
      items.value = result.items;
      total.value = result.total;
    } catch (e) {
      error.value = e instanceof Error ? e : new Error(String(e));
      items.value = [];
      total.value = 0;
    } finally {
      loading.value = false;
    }
  }

  function gotoPage(p: number) {
    const clamped = Math.max(1, Math.min(totalPages.value, p));
    if (clamped !== page.value) {
      page.value = clamped;
    }
  }

  function resetFilter() {
    const initial = options.initialFilter ?? ({} as TFilter);
    for (const key of Object.keys(filter as object)) {
      delete (filter as Record<string, unknown>)[key];
    }
    Object.assign(filter as object, initial);
    page.value = 1;
  }

  watch([page, pageSize], () => {
    void refresh();
  });

  if (options.immediate !== false) {
    void refresh();
  }

  return {
    items,
    total,
    page,
    pageSize,
    loading,
    error,
    filter,
    totalPages,
    isEmpty,
    refresh,
    gotoPage,
    resetFilter,
  };
}
