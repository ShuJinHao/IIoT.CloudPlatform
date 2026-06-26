import { nextTick } from 'vue';
import { describe, expect, it, vi } from 'vitest';
import { useListPage } from './useListPage';

describe('useListPage', () => {
  it('fetches data and derives pagination state', async () => {
    const fetcher = vi.fn(async () => ({
      items: [{ id: 'device-1' }],
      total: 21,
    }));

    const page = useListPage<{ id: string }, { keyword: string }>({
      initialFilter: { keyword: '' },
      initialPageSize: 10,
      fetcher,
      immediate: false,
    });

    await page.refresh();

    expect(fetcher).toHaveBeenCalledWith({
      page: 1,
      pageSize: 10,
      filter: { keyword: '' },
    });
    expect(page.items.value).toEqual([{ id: 'device-1' }]);
    expect(page.total.value).toBe(21);
    expect(page.totalPages.value).toBe(3);
    expect(page.isEmpty.value).toBe(false);
  });

  it('clamps page navigation and resets filters', async () => {
    const page = useListPage<string, { keyword: string }>({
      initialFilter: { keyword: 'abc' },
      initialPageSize: 10,
      fetcher: async () => ({ items: [], total: 15 }),
      immediate: false,
    });

    await page.refresh();
    page.gotoPage(5);
    expect(page.page.value).toBe(2);

    page.filter.keyword = 'changed';
    page.resetFilter();
    await nextTick();

    expect(page.filter.keyword).toBe('abc');
    expect(page.page.value).toBe(1);
  });

  it('stores errors and clears stale rows when fetch fails', async () => {
    const page = useListPage<string, Record<string, unknown>>({
      fetcher: async () => {
        throw new Error('network failed');
      },
      immediate: false,
    });

    await page.refresh();

    expect(page.items.value).toEqual([]);
    expect(page.total.value).toBe(0);
    expect(page.error.value?.message).toBe('network failed');
    expect(page.isEmpty.value).toBe(true);
  });
});
