<template>
  <div class="device-log-page">
    <PageHeader
      title="设备日志"
      subtitle="按设备、级别、关键字和时间范围检索设备运行日志"
    />

    <!-- 设备选择卡 -->
    <CardSurface class="device-log-page__device-card">
      <div class="device-row">
        <span class="device-row__label">设备</span>
        <n-select
          v-model:value="selectedDeviceId"
          :options="deviceOptions"
          placeholder="请先选择设备"
          clearable
          filterable
          size="small"
          style="width: 320px;"
          @update:value="onDeviceChange"
        />
      </div>
    </CardSurface>

    <!-- 查询模式 + 筛选条件 -->
    <CardSurface
      v-if="selectedDeviceId"
      class="device-log-page__filter-card"
    >
      <div class="filter-stack">
        <div class="filter-field">
          <span class="filter-field__label">查询粒度</span>
          <n-radio-group
            v-model:value="currentMode"
            size="small"
            @update:value="switchMode"
          >
            <n-radio-button
              v-for="mode in queryModes"
              :key="mode.key"
              :value="mode.key"
            >
              {{ mode.label }}
            </n-radio-button>
          </n-radio-group>
        </div>

        <div class="filter-row">
          <template v-if="currentMode === 'level'">
            <div class="filter-field">
              <span class="filter-field__label">日志级别</span>
              <n-select
                v-model:value="filters.level"
                :options="levelOptions"
                placeholder="全部级别"
                clearable
                size="small"
                style="width: 200px;"
              />
            </div>
          </template>

          <template v-if="currentMode === 'keyword'">
            <div class="filter-field filter-field--wide">
              <span class="filter-field__label">关键字</span>
              <n-input
                v-model:value="filters.keyword"
                placeholder="搜索日志内容"
                size="small"
                style="width: 320px;"
                @keyup.enter="doSearch"
              />
            </div>
          </template>

          <template v-if="currentMode === 'date'">
            <div class="filter-field">
              <span class="filter-field__label">日期</span>
              <n-date-picker
                v-model:formatted-value="filters.date"
                value-format="yyyy-MM-dd"
                type="date"
                size="small"
                style="width: 180px;"
              />
            </div>
          </template>

          <template v-if="currentMode === 'time-range'">
            <div class="filter-field">
              <span class="filter-field__label">开始时间</span>
              <n-date-picker
                v-model:formatted-value="filters.startTime"
                value-format="yyyy-MM-dd'T'HH:mm"
                type="datetime"
                size="small"
                style="width: 220px;"
              />
            </div>
            <div class="filter-field">
              <span class="filter-field__label">结束时间</span>
              <n-date-picker
                v-model:formatted-value="filters.endTime"
                value-format="yyyy-MM-dd'T'HH:mm"
                type="datetime"
                size="small"
                style="width: 220px;"
              />
            </div>
          </template>

          <template v-if="currentMode === 'date-keyword'">
            <div class="filter-field">
              <span class="filter-field__label">日期</span>
              <n-date-picker
                v-model:formatted-value="filters.date"
                value-format="yyyy-MM-dd"
                type="date"
                size="small"
                style="width: 180px;"
              />
            </div>
            <div class="filter-field filter-field--wide">
              <span class="filter-field__label">关键字</span>
              <n-input
                v-model:value="filters.keyword"
                placeholder="搜索日志内容"
                size="small"
                style="width: 280px;"
                @keyup.enter="doSearch"
              />
            </div>
          </template>

          <n-button type="primary" size="small" @click="doSearch">
            <template #icon>
              <svg viewBox="0 0 16 16" width="14" height="14" fill="none">
                <circle cx="6.5" cy="6.5" r="4.5" stroke="currentColor" stroke-width="1.3"/>
                <path d="M10 10l3 3" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/>
              </svg>
            </template>
            查询
          </n-button>
        </div>
      </div>
    </CardSurface>

    <!-- 引导：尚未选设备 -->
    <CardSurface v-if="!selectedDeviceId">
      <EmptyState
        title="请先选择一台设备"
        description="设备日志需要先指定查询目标，再选择查询模式与条件。"
      />
    </CardSurface>

    <!-- 表格 -->
    <CardSurface
      v-if="selectedDeviceId"
      class="device-log-page__table-card"
      no-padding
    >
      <div v-if="!searched && !loading" class="hint-empty">
        <EmptyState
          title="设置条件后点击查询"
          description="未查询前不显示数据，避免误展示无关日志。"
        />
      </div>

      <n-data-table
        v-else
        class="device-log-page__table"
        :columns="columns"
        :data="records"
        :loading="loading"
        :bordered="false"
        :single-line="false"
        :row-key="rowKey"
        size="small"
      />

      <div v-if="metaData.totalPages > 1" class="pagination-wrap">
        <n-pagination
          :page="currentPage"
          :page-count="metaData.totalPages"
          :item-count="metaData.totalCount"
          :page-size="20"
          show-quick-jumper
          @update:page="onPageChange"
        />
      </div>
    </CardSurface>
  </div>
</template>

<script setup lang="ts">
import { computed, h, onMounted, reactive, ref } from 'vue';
import {
  NButton,
  NInput,
  NSelect,
  NRadioGroup,
  NRadioButton,
  NDatePicker,
  NDataTable,
  NPagination,
} from 'naive-ui';
import type { DataTableColumns } from 'naive-ui';
import {
  getLogsByDeviceAndDateApi,
  getLogsByDeviceAndKeywordApi,
  getLogsByDeviceAndLevelApi,
  getLogsByDeviceAndTimeRangeApi,
  getLogsByDeviceDateAndKeywordApi,
  type DeviceLogListItemDto,
} from '../../api/deviceLog';
import type { PagedMetaData } from '../../api/employee';
import { getAllActiveDevicesApi, type DeviceSelectDto } from '../../api/device';
import PageHeader from '../../components/layout/PageHeader.vue';
import CardSurface from '../../components/layout/CardSurface.vue';
import EmptyState from '../../components/states/EmptyState.vue';
import SeverityBadge from '../../components/feedback/SeverityBadge.vue';

type QueryMode = 'level' | 'keyword' | 'date' | 'time-range' | 'date-keyword';

const PAGE_SIZE = 20;

const queryModes: Array<{ key: QueryMode; label: string }> = [
  { key: 'level', label: '按级别' },
  { key: 'keyword', label: '按关键字' },
  { key: 'date', label: '按日期' },
  { key: 'time-range', label: '按时间范围' },
  { key: 'date-keyword', label: '日期 + 关键字' },
];

const levelOptions = [
  { label: 'INFO', value: 'INFO' },
  { label: 'WARN', value: 'WARN' },
  { label: 'ERROR', value: 'ERROR' },
];

const localDate = () => {
  const date = new Date();
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
};

const defaultStartTime = () => `${localDate()}T00:00`;
const defaultEndTime = () => `${localDate()}T23:59`;
const toUtcIso = (localTime: string) =>
  localTime ? new Date(localTime).toISOString() : '';

const currentMode = ref<QueryMode>('level');
const selectedDeviceId = ref<string | null>(null);
const loading = ref(false);
const searched = ref(false);
const currentPage = ref(1);
const records = ref<DeviceLogListItemDto[]>([]);
const metaData = ref<PagedMetaData>({
  totalCount: 0,
  pageSize: PAGE_SIZE,
  currentPage: 1,
  totalPages: 1,
});

const allDevices = ref<DeviceSelectDto[]>([]);
const deviceOptions = computed(() =>
  allDevices.value.map((d) => ({ label: d.deviceName, value: d.id })),
);

const filters = reactive({
  level: null as string | null,
  keyword: '',
  date: localDate(),
  startTime: defaultStartTime(),
  endTime: defaultEndTime(),
});

const resetDateTime = () => {
  filters.date = localDate();
  filters.startTime = defaultStartTime();
  filters.endTime = defaultEndTime();
};

const switchMode = (mode: QueryMode) => {
  currentMode.value = mode;
  currentPage.value = 1;
  searched.value = false;
  records.value = [];
  resetDateTime();
};

const onDeviceChange = () => {
  currentPage.value = 1;
  searched.value = false;
  records.value = [];
};

const levelToSeverity = (
  level: string,
): 'info' | 'warn' | 'error' | 'success' => {
  switch (level.toUpperCase()) {
    case 'WARN':
      return 'warn';
    case 'ERROR':
      return 'error';
    default:
      return 'info';
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

// === 表格列 ===
const columns: DataTableColumns<DeviceLogListItemDto> = [
  {
    title: '级别',
    key: 'level',
    width: 90,
    render(row) {
      return h(SeverityBadge, {
        severity: levelToSeverity(row.level),
        label: row.level.toUpperCase(),
      });
    },
  },
  {
    title: '日志内容',
    key: 'message',
    minWidth: 320,
    render(row) {
      return h('span', { class: 'cell-msg' }, row.message);
    },
  },
  {
    title: '日志时间',
    key: 'logTime',
    width: 180,
    render(row) {
      return h('span', { class: 'cell-mono cell-time' }, formatTime(row.logTime));
    },
  },
  {
    title: '接收时间',
    key: 'receivedAt',
    width: 180,
    render(row) {
      return h(
        'span',
        { class: 'cell-mono cell-time' },
        formatTime(row.receivedAt),
      );
    },
  },
];

const rowKey = (row: DeviceLogListItemDto) => row.id;

// === 数据加载 ===
const fetchData = async () => {
  if (!selectedDeviceId.value) {
    alert('请先选择设备。');
    return;
  }

  loading.value = true;
  searched.value = true;

  try {
    const pagination = { PageNumber: currentPage.value, PageSize: PAGE_SIZE };
    const deviceId = selectedDeviceId.value;
    let response;

    switch (currentMode.value) {
      case 'level':
        response = await getLogsByDeviceAndLevelApi({
          pagination,
          deviceId,
          level: filters.level || undefined,
        });
        break;

      case 'keyword':
        if (!filters.keyword.trim()) {
          alert('请输入关键字。');
          loading.value = false;
          return;
        }
        response = await getLogsByDeviceAndKeywordApi({
          pagination,
          deviceId,
          keyword: filters.keyword.trim(),
        });
        break;

      case 'date':
        if (!filters.date) {
          alert('请选择日期。');
          loading.value = false;
          return;
        }
        response = await getLogsByDeviceAndDateApi({
          pagination,
          deviceId,
          date: filters.date,
        });
        break;

      case 'time-range':
        if (!filters.startTime || !filters.endTime) {
          alert('请选择完整时间范围。');
          loading.value = false;
          return;
        }
        response = await getLogsByDeviceAndTimeRangeApi({
          pagination,
          deviceId,
          startTime: toUtcIso(filters.startTime),
          endTime: toUtcIso(filters.endTime),
        });
        break;

      case 'date-keyword':
        if (!filters.date || !filters.keyword.trim()) {
          alert('请选择日期并输入关键字。');
          loading.value = false;
          return;
        }
        response = await getLogsByDeviceDateAndKeywordApi({
          pagination,
          deviceId,
          date: filters.date,
          keyword: filters.keyword.trim(),
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

const onPageChange = async (page: number) => {
  currentPage.value = page;
  await fetchData();
};

onMounted(async () => {
  allDevices.value = await getAllActiveDevicesApi().catch(
    () => [] as DeviceSelectDto[],
  );
});
</script>

<style scoped>
.device-log-page {
  font-family: var(--font-sans);
  color: var(--text-0);
}

.device-log-page__device-card {
  margin-bottom: var(--space-4);
}
.device-row {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  flex-wrap: wrap;
}
.device-row__label {
  font-size: var(--fs-sm);
  font-weight: var(--fw-semibold);
  color: var(--text-1);
}

.device-log-page__filter-card {
  margin-bottom: var(--space-4);
}
.filter-stack {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}
.filter-row {
  display: flex;
  align-items: flex-end;
  gap: var(--space-4);
  flex-wrap: wrap;
}
.filter-field {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
}
.filter-field__label {
  font-size: var(--fs-xs);
  color: var(--text-2);
  font-weight: var(--fw-medium);
  letter-spacing: 0.5px;
}

.pagination-wrap {
  display: flex;
  justify-content: flex-end;
  padding: var(--space-4);
  border-top: 1px solid var(--border);
}

.hint-empty {
  padding: var(--space-4);
}

/* 表格单元 */
.device-log-page__table :deep(.cell-msg) {
  font-size: var(--fs-base);
  color: var(--text-0);
  line-height: 1.55;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
  text-overflow: ellipsis;
  word-break: break-word;
}
.device-log-page__table :deep(.cell-mono) {
  font-family: var(--font-mono);
  font-size: var(--fs-sm);
}
.device-log-page__table :deep(.cell-time) {
  color: var(--text-2);
  white-space: nowrap;
}
.device-log-page__table :deep(.n-data-table-thead) {
  background: var(--bg-3);
}
.device-log-page__table :deep(.n-data-table-th) {
  font-size: var(--fs-xs) !important;
  font-weight: var(--fw-semibold) !important;
  color: var(--text-2) !important;
  letter-spacing: 1px;
  text-transform: uppercase;
}
.device-log-page__table :deep(.n-data-table-tr:hover .n-data-table-td) {
  background-color: rgba(8, 145, 178, 0.04) !important;
}
</style>
