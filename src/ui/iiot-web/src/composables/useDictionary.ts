import { ref, type Ref } from 'vue';

/**
 * 字典数据缓存 composable
 * 一次拉取、多组件共享、SPA 周期内常驻。
 *
 * 注意：同一个 key 只允许绑定一个 fetcher——首次注册的 fetcher 永久生效，
 *       后续传入相同 key 的 fetcher 会被忽略，避免误更换实现。
 *
 * 用法：
 *   const { data, loading, load } = useDictionary('processes', () => getAllProcessesApi());
 */

interface DictEntry {
  data: Ref<unknown[]>;
  loading: Ref<boolean>;
  error: Ref<Error | null>;
  loaded: Ref<boolean>;
  fetcher: () => Promise<unknown[]>;
}

const caches = new Map<string, DictEntry>();

export interface UseDictionaryOptions {
  /** 是否在创建时立刻拉取（默认 true） */
  auto?: boolean;
}

export interface UseDictionaryReturn<T> {
  data: Ref<T[]>;
  loading: Ref<boolean>;
  error: Ref<Error | null>;
  loaded: Ref<boolean>;
  load: (force?: boolean) => Promise<void>;
  clear: () => void;
}

export function useDictionary<T>(
  key: string,
  fetcher: () => Promise<T[]>,
  options: UseDictionaryOptions = {},
): UseDictionaryReturn<T> {
  let entry = caches.get(key);

  if (!entry) {
    entry = {
      data: ref([]) as Ref<unknown[]>,
      loading: ref(false),
      error: ref(null),
      loaded: ref(false),
      fetcher: fetcher as () => Promise<unknown[]>,
    };
    caches.set(key, entry);
  }
  // 同 key 第二次注册的 fetcher 忽略（防止误覆盖）

  const cached = entry;

  async function load(force = false) {
    if (cached.loading.value) return;
    if (cached.loaded.value && !force) return;
    cached.loading.value = true;
    cached.error.value = null;
    try {
      const result = await cached.fetcher();
      cached.data.value = result;
      cached.loaded.value = true;
    } catch (e) {
      cached.error.value = e instanceof Error ? e : new Error(String(e));
    } finally {
      cached.loading.value = false;
    }
  }

  function clear() {
    cached.data.value = [];
    cached.loaded.value = false;
    cached.error.value = null;
  }

  if (options.auto !== false && !cached.loaded.value && !cached.loading.value) {
    void load();
  }

  return {
    data: cached.data as Ref<T[]>,
    loading: cached.loading,
    error: cached.error,
    loaded: cached.loaded,
    load,
    clear,
  };
}

/** 退出登录或主动失效缓存时调用 */
export function clearAllDictionaries() {
  for (const entry of caches.values()) {
    entry.data.value = [];
    entry.loaded.value = false;
    entry.error.value = null;
  }
}
