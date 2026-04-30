<template>
  <div class="pass-station">
    <div class="page-header">
      <div>
        <h1 class="page-title">{{ currentSchema?.title ?? '过站追溯' }}</h1>
        <p class="page-sub">
          {{ currentSchema?.subtitle ?? '请选择已接入追溯能力的工序，查询过站记录。' }}
        </p>
      </div>
    </div>

    <div class="filter-bar context-bar">
      <div class="filter-field">
        <label>当前工序</label>
        <select v-model="currentProcessId" class="filter-input">
          <option value="">请选择工序</option>
          <option v-for="process in supportedProcesses" :key="process.id" :value="process.id">
            {{ process.processCode }} - {{ process.processName }}
          </option>
        </select>
      </div>
      <div class="mode-hint" v-if="currentProcess">
        工序编码：<span class="mono">{{ currentProcess.processCode }}</span>
      </div>
    </div>

    <div v-if="currentSchema" class="query-modes">
      <button
        v-for="mode in activeQueryModes"
        :key="mode.key"
        class="mode-btn"
        :class="{ active: currentMode === mode.key }"
        @click="switchMode(mode.key)"
      >
        <span class="mode-icon" v-html="mode.icon"></span>
        {{ mode.label }}
      </button>
    </div>

    <div v-if="currentSchema" class="filter-bar">
      <template v-if="currentMode === 'barcode-process'">
        <div class="filter-field filter-field--wide">
          <label>条码</label>
          <input
            v-model="filters.barcode"
            class="filter-input"
            placeholder="请输入条码"
            @keyup.enter="doSearch"
          />
        </div>
      </template>

      <template v-if="currentMode === 'time-process'">
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
            <option v-for="device in filteredDevices" :key="device.id" :value="device.id">
              {{ device.deviceName }}
            </option>
          </select>
        </div>
        <div class="filter-field filter-field--wide">
          <label>条码</label>
          <input
            v-model="filters.barcode"
            class="filter-input"
            placeholder="请输入条码"
            @keyup.enter="doSearch"
          />
        </div>
      </template>

      <template v-if="currentMode === 'device-time'">
        <div class="filter-field">
          <label>设备</label>
          <select v-model="filters.deviceId" class="filter-input">
            <option value="">请选择设备</option>
            <option v-for="device in filteredDevices" :key="device.id" :value="device.id">
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
            <option v-for="device in filteredDevices" :key="device.id" :value="device.id">
              {{ device.deviceName }}
            </option>
          </select>
        </div>
        <div class="mode-hint">
          读取所选设备最新 200 条过站记录。
        </div>
      </template>

      <button class="btn btn-primary search-btn" @click="doSearch">
        <svg viewBox="0 0 16 16" fill="none">
          <circle cx="6.5" cy="6.5" r="4.5" stroke="currentColor" stroke-width="1.3" />
          <path d="M10 10l3 3" stroke="currentColor" stroke-width="1.3" stroke-linecap="round" />
        </svg>
        查询
      </button>
    </div>

    <div v-else class="table-wrap">
      <div class="empty-cell">
        <div class="empty-state">
          <svg viewBox="0 0 48 48" fill="none">
            <rect x="8" y="6" width="28" height="36" rx="2" stroke="currentColor" stroke-width="1.5" opacity="0.25" />
            <path d="M16 20h12M16 26h8" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" opacity="0.25" />
          </svg>
          <p>请选择已支持追溯的工序后继续。</p>
        </div>
      </div>
    </div>

    <div v-if="currentSchema" class="table-wrap">
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
          <p>请填写查询条件后执行查询。</p>
        </div>
      </div>

      <table v-else-if="records.length > 0" class="data-table">
        <thead>
          <tr>
            <th v-for="column in currentSchema.columns" :key="column.key">{{ column.label }}</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="record in records" :key="record.id" class="table-row" @click="openDetail(record.id)">
            <td
              v-for="column in currentSchema.columns"
              :key="column.key"
              :class="column.className"
            >
              <span v-if="column.variant === 'barcode'" class="barcode-chip">
                {{ formatResultText(column.render(record)) }}
              </span>
              <span
                v-else-if="column.variant === 'result'"
                class="result-tag"
                :class="(record.cellResult ?? '').toUpperCase() === 'OK' ? 'ok' : 'ng'"
              >
                {{ column.render(record) }}
              </span>
              <span v-else>
                {{ formatDisplayValue(column.render(record)) }}
              </span>
            </td>
          </tr>
        </tbody>
      </table>

      <div v-else class="empty-cell">
        <div class="empty-state">
          <svg viewBox="0 0 48 48" fill="none">
            <rect x="8" y="6" width="28" height="36" rx="2" stroke="currentColor" stroke-width="1.5" opacity="0.25" />
            <path d="M16 20h12M16 26h8" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" opacity="0.25" />
          </svg>
          <p>当前查询条件下没有匹配到过站记录。</p>
        </div>
      </div>
    </div>

    <div class="pagination" v-if="currentSchema && metaData.totalPages > 1">
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
            <button class="modal-close" @click="showDetail = false">x</button>
          </div>

          <div class="detail-body" v-if="detailLoading">
            <div class="detail-loading">
              <div class="loading-ring"></div>
              <span>加载中...</span>
            </div>
          </div>

          <div class="detail-body" v-else-if="detailData && currentSchema">
            <div class="detail-result-banner" :class="(detailData.cellResult ?? '').toUpperCase() === 'OK' ? 'ok' : 'ng'">
              <span class="result-icon">{{ formatResultText(detailData.cellResult || '-') }}</span>
              结果：{{ formatResultText(detailData.cellResult || '-') }}
            </div>

            <div
              v-for="section in currentSchema.detailSections"
              :key="section.title"
              class="detail-section"
            >
              <div class="detail-section-title">{{ section.title }}</div>
              <div v-for="field in section.fields" :key="field.key" class="detail-row">
                <span class="detail-label">{{ field.label }}</span>
                <span class="detail-value" :class="field.className">
                  {{ formatDisplayValue(field.render(detailData)) }}
                </span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </Teleport>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue';
import { getAllActiveDevicesApi, type DeviceSelectDto } from '../../api/device';
import { getAllProcessesApi, type ProcessSelectDto } from '../../api/masterData/processes';
import {
  getPassStationDetailApi,
  getPassStationListApi,
  getPassStationTypesApi,
  type PassStationDetailDto,
  type PassStationListItemDto,
  type PassStationQueryMode,
} from '../../api/passStation';
import type { PagedMetaData } from '../../api/employee';
import {
  buildPassStationSchemaMap,
  getPassStationSchema,
  normalizePassStationTypeKey,
  type PassStationSchema,
} from './schema';

interface QueryModeOption {
  key: PassStationQueryMode;
  label: string;
  icon: string;
}

const PAGE_SIZE = 10;

const queryModeMap: Record<PassStationQueryMode, QueryModeOption> = {
  'barcode-process': {
    key: 'barcode-process',
    label: '条码 + 工序',
    icon: '<svg viewBox="0 0 16 16" fill="none"><rect x="2" y="3" width="12" height="2" rx="0.5" stroke="currentColor" stroke-width="1.1"/><rect x="2" y="7" width="8" height="2" rx="0.5" stroke="currentColor" stroke-width="1.1"/><rect x="2" y="11" width="10" height="2" rx="0.5" stroke="currentColor" stroke-width="1.1"/></svg>',
  },
  'time-process': {
    key: 'time-process',
    label: '时间 + 工序',
    icon: '<svg viewBox="0 0 16 16" fill="none"><circle cx="8" cy="8" r="6" stroke="currentColor" stroke-width="1.1"/><path d="M8 5v3.5l2.5 1.5" stroke="currentColor" stroke-width="1.1" stroke-linecap="round"/></svg>',
  },
  'device-barcode': {
    key: 'device-barcode',
    label: '设备 + 条码',
    icon: '<svg viewBox="0 0 16 16" fill="none"><rect x="2" y="4" width="12" height="8" rx="1.5" stroke="currentColor" stroke-width="1.1"/><circle cx="8" cy="8" r="2" stroke="currentColor" stroke-width="1.1"/></svg>',
  },
  'device-time': {
    key: 'device-time',
    label: '设备 + 时间',
    icon: '<svg viewBox="0 0 16 16" fill="none"><rect x="2" y="4" width="8" height="8" rx="1.5" stroke="currentColor" stroke-width="1.1"/><path d="M12 6v4l1.5 1" stroke="currentColor" stroke-width="1.1" stroke-linecap="round"/></svg>',
  },
  'device-latest': {
    key: 'device-latest',
    label: '设备最近 200 条',
    icon: '<svg viewBox="0 0 16 16" fill="none"><path d="M3 4h10M3 8h10M3 12h6" stroke="currentColor" stroke-width="1.1" stroke-linecap="round"/><circle cx="13" cy="12" r="2" stroke="currentColor" stroke-width="1.1"/></svg>',
  },
};

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

const loading = ref(false);
const searched = ref(false);
const currentPage = ref(1);
const currentMode = ref<PassStationQueryMode>('barcode-process');
const currentProcessId = ref('');
const records = ref<PassStationListItemDto[]>([]);
const metaData = ref<PagedMetaData>({ totalCount: 0, pageSize: PAGE_SIZE, currentPage: 1, totalPages: 1 });

const allProcesses = ref<ProcessSelectDto[]>([]);
const allDevices = ref<DeviceSelectDto[]>([]);
const schemaMap = ref<Record<string, PassStationSchema>>({});

const filters = reactive({
  deviceId: '',
  barcode: '',
  startTime: defaultStartTime(),
  endTime: defaultEndTime(),
});

const currentProcess = computed(() => allProcesses.value.find((process) => process.id === currentProcessId.value) ?? null);
const currentTypeKey = computed(() => (currentProcess.value ? normalizePassStationTypeKey(currentProcess.value.processCode) : ''));
const currentSchema = computed(() => getPassStationSchema(schemaMap.value, currentTypeKey.value));

const supportedProcesses = computed(() =>
  allProcesses.value.filter((process) => Boolean(schemaMap.value[normalizePassStationTypeKey(process.processCode)])));

const filteredDevices = computed(() => {
  if (!currentProcessId.value) {
    return [] as DeviceSelectDto[];
  }

  return allDevices.value.filter((device) => device.processId === currentProcessId.value);
});

const activeQueryModes = computed(() => {
  if (!currentSchema.value) {
    return [] as QueryModeOption[];
  }

  return currentSchema.value.supportedModes.map((mode) => queryModeMap[mode]);
});

const pageNumbers = computed(() => {
  const pages: number[] = [];
  for (let page = Math.max(1, currentPage.value - 2); page <= Math.min(metaData.value.totalPages, currentPage.value + 2); page += 1) {
    pages.push(page);
  }
  return pages;
});

watch(currentSchema, (schema) => {
  if (!schema) {
    records.value = [];
    searched.value = false;
    filters.deviceId = '';
    return;
  }

  if (!schema.supportedModes.includes(currentMode.value)) {
    const nextMode = schema.supportedModes[0];
    if (nextMode) {
      currentMode.value = nextMode;
    }
  }
});

watch(currentProcessId, () => {
  currentPage.value = 1;
  searched.value = false;
  records.value = [];
  filters.deviceId = '';
});

const switchMode = (mode: PassStationQueryMode) => {
  currentMode.value = mode;
  currentPage.value = 1;
  searched.value = false;
  records.value = [];

  if (mode === 'time-process' || mode === 'device-time') {
    filters.startTime = defaultStartTime();
    filters.endTime = defaultEndTime();
  }
};

const showDetail = ref(false);
const detailLoading = ref(false);
const detailData = ref<PassStationDetailDto | null>(null);

function formatDisplayValue(value: string | null | undefined) {
  if (!value) {
    return '-';
  }

  const date = new Date(value);
  if (!Number.isNaN(date.getTime()) && value.includes('T')) {
    return date.toLocaleString('zh-CN', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    });
  }

  return value;
}

function formatResultText(value: string | null | undefined) {
  const normalized = (value ?? '').trim().toUpperCase();
  if (!normalized) {
    return '-';
  }

  if (normalized === 'OK') {
    return '合格';
  }

  if (normalized === 'NG') {
    return '不合格';
  }

  return value ?? '-';
}

const fetchSelectData = async () => {
  const [processes, devices, schemas] = await Promise.all([
    getAllProcessesApi().catch(() => [] as ProcessSelectDto[]),
    getAllActiveDevicesApi().catch(() => [] as DeviceSelectDto[]),
    getPassStationTypesApi().catch(() => []),
  ]);

  allProcesses.value = processes;
  allDevices.value = devices;
  schemaMap.value = buildPassStationSchemaMap(schemas);

  const firstSupportedProcess = supportedProcesses.value[0];
  if (!currentProcessId.value && firstSupportedProcess) {
    currentProcessId.value = firstSupportedProcess.id;
  }
};

const fetchData = async () => {
  if (!currentSchema.value || !currentProcess.value) {
    alert('请先选择已支持追溯的工序。');
    return;
  }

  loading.value = true;
  searched.value = true;

  try {
    const pagination = { PageNumber: currentPage.value, PageSize: PAGE_SIZE };

    const response = await getPassStationListApi({
      typeKey: currentSchema.value.typeKey,
      mode: currentMode.value,
      pagination,
      processId:
        currentMode.value === 'barcode-process' || currentMode.value === 'time-process'
          ? currentProcess.value.id
          : undefined,
      deviceId:
        currentMode.value === 'device-barcode' ||
        currentMode.value === 'device-time' ||
        currentMode.value === 'device-latest'
          ? filters.deviceId
          : undefined,
      barcode:
        currentMode.value === 'barcode-process' || currentMode.value === 'device-barcode'
          ? filters.barcode.trim()
          : undefined,
      startTime:
        currentMode.value === 'time-process' || currentMode.value === 'device-time'
          ? toUtcIso(filters.startTime)
          : undefined,
      endTime:
        currentMode.value === 'time-process' || currentMode.value === 'device-time'
          ? toUtcIso(filters.endTime)
          : undefined,
    });

    metaData.value = response.metaData;
    records.value = response.items;
  } catch {
    records.value = [];
  } finally {
    loading.value = false;
  }
};

const doSearch = async () => {
  if (!currentSchema.value || !currentProcess.value) {
    alert('请先选择已支持追溯的工序。');
    return;
  }

  if ((currentMode.value === 'barcode-process' || currentMode.value === 'device-barcode') && !filters.barcode.trim()) {
    alert('当前查询模式必须填写条码。');
    return;
  }

  if (
    (currentMode.value === 'device-barcode' ||
      currentMode.value === 'device-time' ||
      currentMode.value === 'device-latest') &&
    !filters.deviceId
  ) {
    alert('请选择设备。');
    return;
  }

  if ((currentMode.value === 'time-process' || currentMode.value === 'device-time') && (!filters.startTime || !filters.endTime)) {
    alert('请同时填写开始时间和结束时间。');
    return;
  }

  currentPage.value = 1;
  await fetchData();
};

const goPage = async (page: number) => {
  currentPage.value = page;
  await fetchData();
};

const openDetail = async (id: string) => {
  if (!currentSchema.value) {
    return;
  }

  showDetail.value = true;
  detailLoading.value = true;
  detailData.value = null;

  try {
    detailData.value = await getPassStationDetailApi(currentSchema.value.typeKey, id);
  } catch {
    showDetail.value = false;
  } finally {
    detailLoading.value = false;
  }
};

onMounted(() => {
  void fetchSelectData();
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
.context-bar { align-items: center; }
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
.detail-section + .detail-section { margin-top: 22px; }
.detail-section-title {
  padding-bottom: 8px; border-bottom: 1px solid rgba(255, 255, 255, 0.06);
  color: var(--text-muted); font-size: 13px; font-weight: 600;
}
.detail-row { display: flex; align-items: center; justify-content: space-between; gap: 20px; }
.detail-label { color: var(--text-subtle); font-size: 12px; }
.detail-value { color: var(--text-main); font-size: 13px; text-align: right; }
.mono-val { font-family: 'Courier New', monospace; }
.small { font-size: 12px; color: var(--text-muted); }
.highlight { color: #cdeec6; font-weight: 600; }

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
