<template>
  <div class="pass-station">
    <div class="page-header">
      <div>
        <h1 class="page-title">过站数据追溯</h1>
        <p class="page-sub">按条码、设备和时间范围查询注液过站记录，并查看单条详情。</p>
      </div>
    </div>

    <div class="query-modes">
      <button
        v-for="mode in queryModes"
        :key="mode.key"
        class="mode-btn"
        :class="{ active: currentMode === mode.key }"
        @click="switchMode(mode.key)"
      >
        <span class="mode-icon" v-html="mode.icon"></span>
        {{ mode.label }}
      </button>
    </div>

    <div class="filter-bar">
      <template v-if="currentMode === 'barcode-process'">
        <div class="filter-field">
          <label>工序</label>
          <select v-model="filters.processId" class="filter-input">
            <option value="">请选择工序</option>
            <option v-for="process in allProcesses" :key="process.id" :value="process.id">
              {{ process.processCode }} · {{ process.processName }}
            </option>
          </select>
        </div>
        <div class="filter-field filter-field--wide">
          <label>电芯条码</label>
          <input
            v-model="filters.barcode"
            class="filter-input"
            placeholder="输入电芯条码"
            @keyup.enter="doSearch"
          />
        </div>
      </template>

      <template v-if="currentMode === 'time-process'">
        <div class="filter-field">
          <label>工序</label>
          <select v-model="filters.processId" class="filter-input">
            <option value="">请选择工序</option>
            <option v-for="process in allProcesses" :key="process.id" :value="process.id">
              {{ process.processCode }} · {{ process.processName }}
            </option>
          </select>
        </div>
        <div class="filter-field filter-field--time">
          <label>开始时间</label>
          <input type="datetime-local" v-model="filters.startTime" class="filter-input" />
        </div>
        <div class="filter-field filter-field--time">
          <label>结束时间</label>
          <input type="datetime-local" v-model="filters.endTime" class="filter-input" />
        </div>
      </template>

      <template v-if="currentMode === 'device-barcode'">
        <div class="filter-field">
          <label>设备</label>
          <select v-model="filters.deviceId" class="filter-input">
            <option value="">请选择设备</option>
            <option v-for="device in allDevices" :key="device.id" :value="device.id">
              {{ device.deviceName }}
            </option>
          </select>
        </div>
        <div class="filter-field filter-field--wide">
          <label>电芯条码</label>
          <input
            v-model="filters.barcode"
            class="filter-input"
            placeholder="输入电芯条码"
            @keyup.enter="doSearch"
          />
        </div>
      </template>

      <template v-if="currentMode === 'device-time'">
        <div class="filter-field">
          <label>设备</label>
          <select v-model="filters.deviceId" class="filter-input">
            <option value="">请选择设备</option>
            <option v-for="device in allDevices" :key="device.id" :value="device.id">
              {{ device.deviceName }}
            </option>
          </select>
        </div>
        <div class="filter-field filter-field--time">
          <label>开始时间</label>
          <input type="datetime-local" v-model="filters.startTime" class="filter-input" />
        </div>
        <div class="filter-field filter-field--time">
          <label>结束时间</label>
          <input type="datetime-local" v-model="filters.endTime" class="filter-input" />
        </div>
      </template>

      <template v-if="currentMode === 'device-latest'">
        <div class="filter-field">
          <label>设备</label>
          <select v-model="filters.deviceId" class="filter-input">
            <option value="">请选择设备</option>
            <option v-for="device in allDevices" :key="device.id" :value="device.id">
              {{ device.deviceName }}
            </option>
          </select>
        </div>
        <div class="mode-hint">直接读取该设备最近 200 条过站记录，无需再输入时间。</div>
      </template>

      <button class="btn btn-primary search-btn" @click="doSearch">
        <svg viewBox="0 0 16 16" fill="none">
          <circle cx="6.5" cy="6.5" r="4.5" stroke="currentColor" stroke-width="1.3" />
          <path d="M10 10l3 3" stroke="currentColor" stroke-width="1.3" stroke-linecap="round" />
        </svg>
        查询
      </button>
    </div>

    <div class="table-wrap">
      <div v-if="loading" class="skeleton-rows">
        <div v-for="index in 5" :key="index" class="skeleton-row">
          <div class="skel skel-md"></div>
          <div class="skel skel-sm"></div>
          <div class="skel skel-lg"></div>
          <div class="skel skel-sm"></div>
          <div class="skel skel-md"></div>
        </div>
      </div>

      <div v-else-if="!searched" class="empty-cell">
        <div class="empty-state">
          <svg viewBox="0 0 48 48" fill="none">
            <circle cx="20" cy="20" r="14" stroke="currentColor" stroke-width="1.5" opacity="0.25" />
            <path d="M30 30l10 10" stroke="currentColor" stroke-width="2" stroke-linecap="round" opacity="0.25" />
          </svg>
          <p>设置条件后点击查询。</p>
        </div>
      </div>

      <table v-else-if="records.length > 0" class="data-table">
        <thead>
          <tr>
            <th>电芯条码</th>
            <th>检测结果</th>
            <th>注液量</th>
            <th>注液前重</th>
            <th>注液后重</th>
            <th>完成时间</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="record in records" :key="record.id" class="table-row" @click="openDetail(record.id)">
            <td><span class="barcode-chip">{{ record.barcode }}</span></td>
            <td>
              <span class="result-tag" :class="record.cellResult === 'OK' ? 'ok' : 'ng'">
                {{ record.cellResult }}
              </span>
            </td>
            <td class="mono">{{ record.injectionVolume }} ml</td>
            <td class="mono">{{ record.preInjectionWeight }} g</td>
            <td class="mono">{{ record.postInjectionWeight }} g</td>
            <td class="time-cell">{{ formatTime(record.completedTime) }}</td>
          </tr>
        </tbody>
      </table>

      <div v-else class="empty-cell">
        <div class="empty-state">
          <svg viewBox="0 0 48 48" fill="none">
            <rect x="8" y="6" width="28" height="36" rx="2" stroke="currentColor" stroke-width="1.5" opacity="0.25" />
            <path d="M16 20h12M16 26h8" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" opacity="0.25" />
          </svg>
          <p>未查询到匹配的过站数据。</p>
        </div>
      </div>
    </div>

    <div class="pagination" v-if="metaData.totalPages > 1">
      <button class="page-btn" :disabled="currentPage === 1" @click="goPage(currentPage - 1)">
        <svg viewBox="0 0 12 12" fill="none">
          <path d="M8 2L4 6l4 4" stroke="currentColor" stroke-width="1.3" stroke-linecap="round" />
        </svg>
      </button>
      <button
        v-for="page in pageNumbers"
        :key="page"
        class="page-btn"
        :class="{ active: page === currentPage }"
        @click="goPage(page)"
      >
        {{ page }}
      </button>
      <button class="page-btn" :disabled="currentPage === metaData.totalPages" @click="goPage(currentPage + 1)">
        <svg viewBox="0 0 12 12" fill="none">
          <path d="M4 2l4 4-4 4" stroke="currentColor" stroke-width="1.3" stroke-linecap="round" />
        </svg>
      </button>
      <span class="total-badge">共 {{ metaData.totalCount }} 条</span>
    </div>

    <Teleport to="body">
      <div v-if="showDetail" class="detail-overlay" @click.self="showDetail = false">
        <div class="detail-panel">
          <div class="detail-header">
            <span class="detail-title">过站详情</span>
            <button class="modal-close" @click="showDetail = false">✕</button>
          </div>

          <div class="detail-body" v-if="detailLoading">
            <div class="detail-loading">
              <div class="loading-ring"></div>
              <span>加载中...</span>
            </div>
          </div>

          <div class="detail-body" v-else-if="detailData">
            <div class="detail-result-banner" :class="detailData.cellResult === 'OK' ? 'ok' : 'ng'">
              <span class="result-icon">{{ detailData.cellResult === 'OK' ? '✓' : '✕' }}</span>
              检测结果：{{ detailData.cellResult }}
            </div>

            <div class="detail-section">
              <div class="detail-row">
                <span class="detail-label">电芯条码</span>
                <span class="detail-value mono-val">{{ detailData.barcode }}</span>
              </div>
              <div class="detail-row">
                <span class="detail-label">设备 ID</span>
                <span class="detail-value mono-val small">{{ detailData.deviceId }}</span>
              </div>

              <div class="detail-divider"></div>

              <div class="detail-row">
                <span class="detail-label">注液前时间</span>
                <span class="detail-value">{{ formatTime(detailData.preInjectionTime) }}</span>
              </div>
              <div class="detail-row">
                <span class="detail-label">注液前称重</span>
                <span class="detail-value mono-val">{{ detailData.preInjectionWeight }} g</span>
              </div>
              <div class="detail-row">
                <span class="detail-label">注液后时间</span>
                <span class="detail-value">{{ formatTime(detailData.postInjectionTime) }}</span>
              </div>
              <div class="detail-row">
                <span class="detail-label">注液后称重</span>
                <span class="detail-value mono-val">{{ detailData.postInjectionWeight }} g</span>
              </div>
              <div class="detail-row">
                <span class="detail-label">注液量</span>
                <span class="detail-value mono-val highlight">{{ detailData.injectionVolume }} ml</span>
              </div>

              <div class="detail-divider"></div>

              <div class="detail-row">
                <span class="detail-label">完成时间</span>
                <span class="detail-value">{{ formatTime(detailData.completedTime) }}</span>
              </div>
              <div class="detail-row">
                <span class="detail-label">云端接收时间</span>
                <span class="detail-value small">{{ formatTime(detailData.receivedAt) }}</span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </Teleport>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue';
import {
  getInjectionByBarcodeAndProcessApi,
  getInjectionByDeviceAndBarcodeApi,
  getInjectionByDeviceAndTimeApi,
  getInjectionByTimeAndProcessApi,
  getInjectionDetailApi,
  getInjectionLatest200ByDeviceApi,
  type InjectionPassDetailDto,
  type InjectionPassListItemDto,
} from '../../api/passStation';
import type { PagedMetaData } from '../../api/employee';
import { getAllProcessesApi, type ProcessSelectDto } from '../../api/masterData/processes';
import { getAllActiveDevicesApi, type DeviceSelectDto } from '../../api/device';

type QueryMode =
  | 'barcode-process'
  | 'time-process'
  | 'device-barcode'
  | 'device-time'
  | 'device-latest';

interface QueryModeOption {
  key: QueryMode;
  label: string;
  icon: string;
}

const PAGE_SIZE = 10;

const queryModes: QueryModeOption[] = [
  {
    key: 'barcode-process',
    label: '条码 + 工序',
    icon: '<svg viewBox="0 0 16 16" fill="none"><rect x="2" y="3" width="12" height="2" rx="0.5" stroke="currentColor" stroke-width="1.1"/><rect x="2" y="7" width="8" height="2" rx="0.5" stroke="currentColor" stroke-width="1.1"/><rect x="2" y="11" width="10" height="2" rx="0.5" stroke="currentColor" stroke-width="1.1"/></svg>',
  },
  {
    key: 'time-process',
    label: '时间 + 工序',
    icon: '<svg viewBox="0 0 16 16" fill="none"><circle cx="8" cy="8" r="6" stroke="currentColor" stroke-width="1.1"/><path d="M8 5v3.5l2.5 1.5" stroke="currentColor" stroke-width="1.1" stroke-linecap="round"/></svg>',
  },
  {
    key: 'device-barcode',
    label: '设备 + 条码',
    icon: '<svg viewBox="0 0 16 16" fill="none"><rect x="2" y="4" width="12" height="8" rx="1.5" stroke="currentColor" stroke-width="1.1"/><circle cx="8" cy="8" r="2" stroke="currentColor" stroke-width="1.1"/></svg>',
  },
  {
    key: 'device-time',
    label: '设备 + 时间',
    icon: '<svg viewBox="0 0 16 16" fill="none"><rect x="2" y="4" width="8" height="8" rx="1.5" stroke="currentColor" stroke-width="1.1"/><path d="M12 6v4l1.5 1" stroke="currentColor" stroke-width="1.1" stroke-linecap="round"/></svg>',
  },
  {
    key: 'device-latest',
    label: '设备最近 200 条',
    icon: '<svg viewBox="0 0 16 16" fill="none"><path d="M3 4h10M3 8h10M3 12h6" stroke="currentColor" stroke-width="1.1" stroke-linecap="round"/><circle cx="13" cy="12" r="2" stroke="currentColor" stroke-width="1.1"/></svg>',
  },
];

const localDate = () => {
  const date = new Date();
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
};

const defaultStartTime = () => `${localDate()}T00:00`;
const defaultEndTime = () => `${localDate()}T23:59`;
const toUtcIso = (localTime: string) => (localTime ? new Date(localTime).toISOString() : '');

const currentMode = ref<QueryMode>('barcode-process');
const loading = ref(false);
const searched = ref(false);
const currentPage = ref(1);
const records = ref<InjectionPassListItemDto[]>([]);
const metaData = ref<PagedMetaData>({ totalCount: 0, pageSize: PAGE_SIZE, currentPage: 1, totalPages: 1 });

const allProcesses = ref<ProcessSelectDto[]>([]);
const allDevices = ref<DeviceSelectDto[]>([]);

const filters = reactive({
  processId: '',
  deviceId: '',
  barcode: '',
  startTime: defaultStartTime(),
  endTime: defaultEndTime(),
});

const pageNumbers = computed(() => {
  const pages: number[] = [];
  for (let page = Math.max(1, currentPage.value - 2); page <= Math.min(metaData.value.totalPages, currentPage.value + 2); page += 1) {
    pages.push(page);
  }
  return pages;
});

const resetTimeRange = () => {
  filters.startTime = defaultStartTime();
  filters.endTime = defaultEndTime();
};

const switchMode = (mode: QueryMode) => {
  currentMode.value = mode;
  currentPage.value = 1;
  searched.value = false;
  records.value = [];

  if (mode === 'time-process' || mode === 'device-time') {
    resetTimeRange();
  }
};

const formatTime = (value?: string | null) => {
  if (!value) return '—';

  try {
    return new Date(value).toLocaleString('zh-CN', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    });
  } catch {
    return value;
  }
};

const fetchData = async () => {
  loading.value = true;
  searched.value = true;

  try {
    const pagination = { PageNumber: currentPage.value, PageSize: PAGE_SIZE };
    let response;

    switch (currentMode.value) {
      case 'barcode-process':
        if (!filters.processId || !filters.barcode.trim()) {
          alert('请选择工序并输入条码。');
          return;
        }
        response = await getInjectionByBarcodeAndProcessApi({
          pagination,
          processId: filters.processId,
          barcode: filters.barcode.trim(),
        });
        break;

      case 'time-process':
        if (!filters.processId || !filters.startTime || !filters.endTime) {
          alert('请选择工序并补全时间范围。');
          return;
        }
        response = await getInjectionByTimeAndProcessApi({
          pagination,
          processId: filters.processId,
          startTime: toUtcIso(filters.startTime),
          endTime: toUtcIso(filters.endTime),
        });
        break;

      case 'device-barcode':
        if (!filters.deviceId || !filters.barcode.trim()) {
          alert('请选择设备并输入条码。');
          return;
        }
        response = await getInjectionByDeviceAndBarcodeApi({
          pagination,
          deviceId: filters.deviceId,
          barcode: filters.barcode.trim(),
        });
        break;

      case 'device-time':
        if (!filters.deviceId || !filters.startTime || !filters.endTime) {
          alert('请选择设备并补全时间范围。');
          return;
        }
        response = await getInjectionByDeviceAndTimeApi({
          pagination,
          deviceId: filters.deviceId,
          startTime: toUtcIso(filters.startTime),
          endTime: toUtcIso(filters.endTime),
        });
        break;

      case 'device-latest':
        if (!filters.deviceId) {
          alert('请选择设备。');
          return;
        }
        response = await getInjectionLatest200ByDeviceApi({
          pagination,
          deviceId: filters.deviceId,
        });
        break;
    }

    metaData.value = response.metaData;
    records.value = response.items;
  } catch {
    records.value = [];
  } finally {
    loading.value = false;
  }
};

const doSearch = async () => {
  currentPage.value = 1;
  await fetchData();
};

const goPage = async (page: number) => {
  currentPage.value = page;
  await fetchData();
};

const showDetail = ref(false);
const detailLoading = ref(false);
const detailData = ref<InjectionPassDetailDto | null>(null);

const openDetail = async (id: string) => {
  showDetail.value = true;
  detailLoading.value = true;
  detailData.value = null;

  try {
    detailData.value = await getInjectionDetailApi(id);
  } catch {
    showDetail.value = false;
  } finally {
    detailLoading.value = false;
  }
};

const fetchSelectData = async () => {
  const [processes, devices] = await Promise.all([
    getAllProcessesApi().catch(() => [] as ProcessSelectDto[]),
    getAllActiveDevicesApi().catch(() => [] as DeviceSelectDto[]),
  ]);

  allProcesses.value = processes;
  allDevices.value = devices;
};

onMounted(() => {
  fetchSelectData();
});
</script>

<style scoped>
.pass-station {
  --accent: #4fb286;
  --accent-soft: rgba(79, 178, 134, 0.16);
  --surface: rgba(255, 255, 255, 0.04);
  --surface-strong: rgba(255, 255, 255, 0.06);
  --surface-hover: rgba(255, 255, 255, 0.08);
  --border: rgba(255, 255, 255, 0.09);
  --border-strong: rgba(255, 255, 255, 0.14);
  --text-main: #f0f3ef;
  --text-muted: rgba(240, 243, 239, 0.72);
  --text-subtle: rgba(240, 243, 239, 0.46);
  color: var(--text-main);
}

* { box-sizing: border-box; }
.page-header { display: flex; align-items: flex-start; justify-content: space-between; margin-bottom: 20px; }
.page-title { margin: 0 0 6px; font-size: 24px; font-weight: 600; color: var(--text-main); }
.page-sub { margin: 0; font-size: 13px; line-height: 1.6; color: var(--text-subtle); }

.query-modes { display: flex; flex-wrap: wrap; gap: 10px; margin-bottom: 16px; }
.mode-btn {
  display: inline-flex; align-items: center; gap: 8px; min-height: 38px; padding: 0 14px;
  border: 1px solid var(--border); border-radius: 6px; background: rgba(255, 255, 255, 0.02);
  color: var(--text-muted); font: inherit; font-size: 13px; cursor: pointer;
  transition: border-color 0.18s ease, background-color 0.18s ease, color 0.18s ease;
}
.mode-btn:hover { border-color: var(--border-strong); background: var(--surface-hover); color: var(--text-main); }
.mode-btn.active { border-color: rgba(79, 178, 134, 0.34); background: var(--accent-soft); color: #baf0d6; }
.mode-icon { display: flex; width: 14px; height: 14px; align-items: center; }
.mode-icon :deep(svg) { width: 14px; height: 14px; }

.filter-bar {
  display: flex; flex-wrap: wrap; gap: 12px; align-items: flex-end; margin-bottom: 18px; padding: 18px;
  border: 1px solid var(--border); border-radius: 6px; background: rgba(255, 255, 255, 0.025);
}
.filter-field { display: flex; min-width: 180px; flex: 1 1 220px; flex-direction: column; gap: 6px; }
.filter-field--wide { flex: 1.3 1 260px; }
.filter-field--time { max-width: 228px; min-width: 228px; flex: 0 1 228px; }
.filter-field label { font-size: 12px; font-weight: 500; color: var(--text-subtle); }

.filter-input {
  min-height: 40px; padding: 0 12px; border: 1px solid var(--border); border-radius: 6px;
  background: var(--surface); color: var(--text-main); font: inherit; font-size: 13px; outline: none;
  transition: border-color 0.18s ease, background-color 0.18s ease;
}
.filter-input:focus { border-color: rgba(79, 178, 134, 0.42); background: var(--surface-strong); }
.filter-input::placeholder { color: var(--text-subtle); }
select.filter-input { cursor: pointer; }
option { background: #1d231f; color: var(--text-main); }

.mode-hint {
  min-height: 40px; padding: 10px 12px; border: 1px dashed rgba(79, 178, 134, 0.2); border-radius: 6px;
  background: rgba(79, 178, 134, 0.05); color: var(--text-subtle); font-size: 12px; line-height: 1.5;
}

.btn {
  display: inline-flex; align-items: center; justify-content: center; gap: 8px; min-height: 40px; padding: 0 16px;
  border: 1px solid transparent; border-radius: 6px; font: inherit; font-size: 13px; font-weight: 500;
  cursor: pointer; transition: transform 0.16s ease, border-color 0.16s ease, background-color 0.16s ease;
}
.btn svg { width: 14px; height: 14px; }
.btn-primary { background: var(--accent); color: #102118; }
.btn-primary:hover { transform: translateY(-1px); background: #62c396; }
.search-btn { min-width: 112px; flex: 0 0 auto; }

.table-wrap { overflow: auto; border: 1px solid var(--border); border-radius: 6px; background: rgba(255, 255, 255, 0.025); }
.data-table { width: 100%; min-width: 860px; border-collapse: collapse; }
.data-table thead tr { background: rgba(255, 255, 255, 0.04); }
.data-table th { padding: 12px 16px; text-align: left; font-size: 12px; font-weight: 600; color: var(--text-subtle); white-space: nowrap; }
.data-table td { padding: 13px 16px; border-top: 1px solid rgba(255, 255, 255, 0.05); font-size: 13px; color: var(--text-muted); vertical-align: middle; }
.table-row { cursor: pointer; transition: background-color 0.16s ease; }
.table-row:hover { background: rgba(255, 255, 255, 0.04); }

.barcode-chip {
  display: inline-flex; padding: 4px 9px; border: 1px solid rgba(79, 178, 134, 0.22); border-radius: 6px;
  background: rgba(79, 178, 134, 0.08); color: #baf0d6; font-family: 'Courier New', monospace; font-size: 12px;
}
.result-tag { display: inline-flex; padding: 4px 10px; border-radius: 999px; font-size: 11px; font-weight: 600; }
.result-tag.ok { background: rgba(79, 178, 134, 0.16); color: #baf0d6; }
.result-tag.ng { background: rgba(227, 109, 90, 0.16); color: #ffb4a8; }
.mono { font-family: 'Courier New', monospace; color: var(--text-main); }
.time-cell { color: var(--text-subtle); white-space: nowrap; }

.skeleton-rows { padding: 8px 0; }
.skeleton-row { display: flex; gap: 16px; padding: 14px 16px; border-top: 1px solid rgba(255, 255, 255, 0.05); }
.skel { height: 14px; border-radius: 6px; background: rgba(255, 255, 255, 0.08); animation: shimmer 1.4s ease-in-out infinite; }
.skel-sm { width: 72px; }
.skel-md { width: 120px; }
.skel-lg { width: 220px; }
@keyframes shimmer { 0%, 100% { opacity: 0.45; } 50% { opacity: 1; } }

.empty-cell { padding: 56px 24px; text-align: center; }
.empty-state { display: flex; flex-direction: column; align-items: center; gap: 12px; }
.empty-state svg { width: 52px; height: 52px; color: rgba(255, 255, 255, 0.2); }
.empty-state p { margin: 0; font-size: 13px; color: var(--text-subtle); }

.pagination { display: flex; align-items: center; justify-content: center; gap: 8px; margin-top: 20px; }
.page-btn {
  display: inline-flex; width: 34px; height: 34px; align-items: center; justify-content: center;
  border: 1px solid var(--border); border-radius: 6px; background: rgba(255, 255, 255, 0.03);
  color: var(--text-muted); cursor: pointer; transition: border-color 0.16s ease, background-color 0.16s ease, color 0.16s ease;
}
.page-btn:hover:not(:disabled) { border-color: rgba(79, 178, 134, 0.28); color: #baf0d6; }
.page-btn.active { border-color: rgba(79, 178, 134, 0.35); background: var(--accent-soft); color: #baf0d6; }
.page-btn:disabled { opacity: 0.36; cursor: not-allowed; }
.page-btn svg { width: 12px; height: 12px; }
.total-badge { margin-left: 8px; font-size: 12px; color: var(--text-subtle); }

.detail-overlay {
  position: fixed; inset: 0; z-index: 100; display: flex; justify-content: flex-end;
  background: rgba(0, 0, 0, 0.48); backdrop-filter: blur(2px);
}
.detail-panel {
  display: flex; width: 440px; max-width: 100%; flex-direction: column; border-left: 1px solid var(--border);
  background: #1a201d; box-shadow: -12px 0 30px rgba(0, 0, 0, 0.25);
}
.detail-header { display: flex; align-items: center; justify-content: space-between; padding: 18px 22px; border-bottom: 1px solid rgba(255, 255, 255, 0.06); }
.detail-title { font-size: 15px; font-weight: 600; }
.modal-close {
  display: inline-flex; width: 28px; height: 28px; align-items: center; justify-content: center;
  border: none; border-radius: 6px; background: transparent; color: var(--text-subtle); font-size: 15px;
  cursor: pointer; transition: background-color 0.16s ease, color 0.16s ease;
}
.modal-close:hover { background: rgba(255, 255, 255, 0.08); color: var(--text-main); }
.detail-body { flex: 1; overflow-y: auto; padding: 20px 22px; }
.detail-loading { display: flex; flex-direction: column; align-items: center; gap: 12px; padding-top: 56px; color: var(--text-subtle); }
.loading-ring {
  width: 30px; height: 30px; border: 2px solid rgba(79, 178, 134, 0.18); border-top-color: var(--accent);
  border-radius: 50%; animation: spin 0.8s linear infinite;
}
@keyframes spin { to { transform: rotate(360deg); } }

.detail-result-banner {
  display: flex; align-items: center; gap: 10px; margin-bottom: 18px; padding: 12px 14px; border-radius: 6px;
  font-size: 14px; font-weight: 600;
}
.detail-result-banner.ok { border: 1px solid rgba(79, 178, 134, 0.2); background: rgba(79, 178, 134, 0.12); color: #baf0d6; }
.detail-result-banner.ng { border: 1px solid rgba(227, 109, 90, 0.2); background: rgba(227, 109, 90, 0.12); color: #ffb4a8; }
.result-icon { font-size: 16px; }

.detail-section { display: flex; flex-direction: column; gap: 14px; }
.detail-row { display: flex; align-items: center; justify-content: space-between; gap: 20px; }
.detail-label { color: var(--text-subtle); font-size: 12px; }
.detail-value { color: var(--text-main); font-size: 13px; text-align: right; }
.mono-val { font-family: 'Courier New', monospace; }
.small { font-size: 12px; color: var(--text-muted); }
.highlight { color: #cdeec6; font-weight: 600; }
.detail-divider { height: 1px; margin: 4px 0; background: rgba(255, 255, 255, 0.06); }

@media (max-width: 960px) {
  .filter-field--time { max-width: none; min-width: 180px; flex: 1 1 220px; }
  .search-btn { width: 100%; }
}

@media (max-width: 640px) {
  .page-title { font-size: 21px; }
  .page-sub { font-size: 12px; }
  .filter-bar { padding: 14px; }
  .filter-field, .filter-field--wide, .filter-field--time { min-width: 100%; max-width: none; flex-basis: 100%; }
  .detail-panel { width: 100%; }
}
</style>
