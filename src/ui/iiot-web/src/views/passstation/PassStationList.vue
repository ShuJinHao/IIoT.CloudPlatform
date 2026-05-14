<template>
  <NiondDataPage
    class="passstation-page"
      :title="currentSchema?.title ?? '过站追溯'"
      :subtitle="currentSchema?.subtitle ?? '请选择已接入追溯能力的工序，查询过站记录。'"
  >

    <!-- 工序选择栏 -->
    <NiondToolbar class="passstation-page__context-card">
      <div class="context-row">
        <div class="filter-field">
          <span class="filter-field__label">当前工序</span>
          <UiSelect
            v-model:value="currentProcessId"
            :options="processOptions"
            placeholder="请选择工序"
            clearable
            filterable
            size="small"
            style="width: 280px;"
          />
        </div>
        <div v-if="currentProcess" class="context-hint">
          工序编码：<code class="context-hint__code">{{ currentProcess.processCode }}</code>
        </div>
      </div>
    </NiondToolbar>

    <!-- 查询模式 + 筛选条件 -->
    <CardSurface
      v-if="currentSchema"
      class="passstation-page__filter-card"
    >
      <div class="filter-stack">
        <div class="filter-field">
          <span class="filter-field__label">查询模式</span>
          <UiRadioGroup
            v-model:value="currentMode"
            size="small"
            @update:value="switchMode"
          >
            <UiRadioButton
              v-for="mode in activeQueryModes"
              :key="mode.key"
              :value="mode.key"
            >
              {{ mode.label }}
            </UiRadioButton>
          </UiRadioGroup>
        </div>

        <div class="filter-row">
          <template v-if="currentMode === 'barcode-process'">
            <div class="filter-field filter-field--wide">
              <span class="filter-field__label">条码</span>
              <UiInput
                v-model:value="filters.barcode"
                placeholder="请输入条码"
                size="small"
                style="width: 280px;"
                @keyup.enter="doSearch"
              />
            </div>
          </template>

          <template v-if="currentMode === 'time-process'">
            <div class="filter-field">
              <span class="filter-field__label">开始时间</span>
              <UiDatePicker
                v-model:formatted-value="filters.startTime"
                value-format="yyyy-MM-dd'T'HH:mm"
                type="datetime"
                size="small"
                style="width: 220px;"
              />
            </div>
            <div class="filter-field">
              <span class="filter-field__label">结束时间</span>
              <UiDatePicker
                v-model:formatted-value="filters.endTime"
                value-format="yyyy-MM-dd'T'HH:mm"
                type="datetime"
                size="small"
                style="width: 220px;"
              />
            </div>
          </template>

          <template v-if="currentMode === 'device-barcode'">
            <div class="filter-field">
              <span class="filter-field__label">设备</span>
              <UiSelect
                v-model:value="filters.deviceId"
                :options="deviceOptions"
                placeholder="请选择设备"
                clearable
                filterable
                size="small"
                style="width: 220px;"
              />
            </div>
            <div class="filter-field filter-field--wide">
              <span class="filter-field__label">条码</span>
              <UiInput
                v-model:value="filters.barcode"
                placeholder="请输入条码"
                size="small"
                style="width: 240px;"
                @keyup.enter="doSearch"
              />
            </div>
          </template>

          <template v-if="currentMode === 'device-time'">
            <div class="filter-field">
              <span class="filter-field__label">设备</span>
              <UiSelect
                v-model:value="filters.deviceId"
                :options="deviceOptions"
                placeholder="请选择设备"
                clearable
                filterable
                size="small"
                style="width: 220px;"
              />
            </div>
            <div class="filter-field">
              <span class="filter-field__label">开始时间</span>
              <UiDatePicker
                v-model:formatted-value="filters.startTime"
                value-format="yyyy-MM-dd'T'HH:mm"
                type="datetime"
                size="small"
                style="width: 220px;"
              />
            </div>
            <div class="filter-field">
              <span class="filter-field__label">结束时间</span>
              <UiDatePicker
                v-model:formatted-value="filters.endTime"
                value-format="yyyy-MM-dd'T'HH:mm"
                type="datetime"
                size="small"
                style="width: 220px;"
              />
            </div>
          </template>

          <template v-if="currentMode === 'device-latest'">
            <div class="filter-field">
              <span class="filter-field__label">设备</span>
              <UiSelect
                v-model:value="filters.deviceId"
                :options="deviceOptions"
                placeholder="请选择设备"
                clearable
                filterable
                size="small"
                style="width: 220px;"
              />
            </div>
            <div class="latest-hint">读取所选设备最新 200 条过站记录</div>
          </template>

          <UiButton type="primary" size="small" @click="doSearch">
            <template #icon>
              <svg viewBox="0 0 16 16" width="14" height="14" fill="none">
                <circle cx="6.5" cy="6.5" r="4.5" stroke="currentColor" stroke-width="1.3"/>
                <path d="M10 10l3 3" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/>
              </svg>
            </template>
            查询
          </UiButton>
        </div>
      </div>
    </CardSurface>

    <!-- 引导：未选工序 -->
    <CardSurface v-if="!currentSchema">
      <EmptyState
        title="请先选择支持追溯的工序"
        description="只有已接入追溯能力的工序才会出现在选择器里。"
      />
    </CardSurface>

    <!-- 表格 -->
    <NiondTableCard
      v-if="currentSchema"
      class="passstation-page__table-card"
    >
      <div v-if="!searched && !loading" class="hint-empty">
        <EmptyState
          title="请填写查询条件后执行查询"
          description="未查询前不显示数据，避免误展示无关记录。"
        />
      </div>

      <UiDataTable
        v-else
        class="passstation-page__table"
        :columns="columns"
        :data="records"
        :loading="loading"
        :bordered="false"
        :single-line="false"
        :row-key="rowKey"
        :row-props="rowProps"
        size="small"
      />

      <div v-if="metaData.totalPages > 1" class="pagination-wrap">
        <UiPagination
          :page="currentPage"
          :page-count="metaData.totalPages"
          :item-count="metaData.totalCount"
          :page-size="PAGE_SIZE"
          show-quick-jumper
          @update:page="onPageChange"
        />
      </div>
    </NiondTableCard>

    <!-- 详情抽屉 -->
    <UiDrawer
      v-model:show="showDetail"
      :width="460"
      placement="right"
    >
      <UiDrawerContent title="过站详情" closable>
        <LoadingState v-if="detailLoading" :rows="6" />
        <div v-else-if="detailData && currentSchema" class="detail-stack">
          <div
            class="detail-result-banner"
            :class="(detailData.cellResult ?? '').toUpperCase() === 'OK' ? 'is-ok' : 'is-ng'"
          >
            <span class="detail-result-banner__dot"></span>
            结果：{{ formatResultText(detailData.cellResult || '-') }}
          </div>

          <div
            v-for="section in currentSchema.detailSections"
            :key="section.title"
            class="detail-section"
          >
            <div class="detail-section__title">{{ section.title }}</div>
            <div
              v-for="field in section.fields"
              :key="field.key"
              class="detail-row"
            >
              <span class="detail-row__label">{{ field.label }}</span>
              <span
                class="detail-row__value"
                :class="{
                  'detail-row__value--mono':
                    field.className === 'mono-val' || field.className === 'mono-val small',
                  'detail-row__value--small':
                    field.className === 'small' || field.className === 'mono-val small',
                }"
              >
                {{ formatDisplayValue(field.render(detailData)) }}
              </span>
            </div>
          </div>
        </div>
      </UiDrawerContent>
    </UiDrawer>
  </NiondDataPage>
</template>

<script setup lang="ts">
import { ref, reactive, computed, h, onMounted, watch } from 'vue';
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
import CardSurface from '../../components/layout/CardSurface.vue';
import NiondDataPage from '../../components/layout/NiondDataPage.vue';
import NiondTableCard from '../../components/layout/NiondTableCard.vue';
import NiondToolbar from '../../components/layout/NiondToolbar.vue';
import LoadingState from '../../components/states/LoadingState.vue';
import EmptyState from '../../components/states/EmptyState.vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiDataTable from '../../components/ui/UiDataTable.vue';
import UiDatePicker from '../../components/ui/UiDatePicker.vue';
import UiDrawer from '../../components/ui/UiDrawer.vue';
import UiDrawerContent from '../../components/ui/UiDrawerContent.vue';
import UiInput from '../../components/ui/UiInput.vue';
import UiPagination from '../../components/ui/UiPagination.vue';
import UiRadioButton from '../../components/ui/UiRadioButton.vue';
import UiRadioGroup from '../../components/ui/UiRadioGroup.vue';
import UiSelect from '../../components/ui/UiSelect.vue';
import UiTag from '../../components/ui/UiTag.vue';
import type { UiDataTableColumn } from '../../components/ui/types';

const PAGE_SIZE = 10;

const queryModeLabels: Record<PassStationQueryMode, string> = {
  'barcode-process': '条码 + 工序',
  'time-process': '时间 + 工序',
  'device-barcode': '设备 + 条码',
  'device-time': '设备 + 时间',
  'device-latest': '设备最近 200 条',
};

const localDate = () => {
  const date = new Date();
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
};
const defaultStartTime = () => `${localDate()}T00:00`;
const defaultEndTime = () => `${localDate()}T23:59`;
const toUtcIso = (localTime: string) =>
  localTime ? new Date(localTime).toISOString() : '';

const loading = ref(false);
const searched = ref(false);
const currentPage = ref(1);
const currentMode = ref<PassStationQueryMode>('barcode-process');
const currentProcessId = ref<string | null>(null);
const records = ref<PassStationListItemDto[]>([]);
const metaData = ref<PagedMetaData>({
  totalCount: 0,
  pageSize: PAGE_SIZE,
  currentPage: 1,
  totalPages: 1,
});

const allProcesses = ref<ProcessSelectDto[]>([]);
const allDevices = ref<DeviceSelectDto[]>([]);
const schemaMap = ref<Record<string, PassStationSchema>>({});

const filters = reactive({
  deviceId: null as string | null,
  barcode: '',
  startTime: defaultStartTime(),
  endTime: defaultEndTime(),
});

const currentProcess = computed(
  () =>
    allProcesses.value.find((p) => p.id === currentProcessId.value) ?? null,
);
const currentTypeKey = computed(() =>
  currentProcess.value
    ? normalizePassStationTypeKey(currentProcess.value.processCode)
    : '',
);
const currentSchema = computed(() =>
  getPassStationSchema(schemaMap.value, currentTypeKey.value),
);

const supportedProcesses = computed(() =>
  allProcesses.value.filter((p) =>
    Boolean(schemaMap.value[normalizePassStationTypeKey(p.processCode)]),
  ),
);

const processOptions = computed(() =>
  supportedProcesses.value.map((p) => ({
    label: `${p.processCode} - ${p.processName}`,
    value: p.id,
  })),
);

const filteredDevices = computed(() => {
  if (!currentProcessId.value) return [] as DeviceSelectDto[];
  return allDevices.value.filter((d) => d.processId === currentProcessId.value);
});
const deviceOptions = computed(() =>
  filteredDevices.value.map((d) => ({ label: d.deviceName, value: d.id })),
);

const activeQueryModes = computed(() => {
  if (!currentSchema.value) return [];
  return currentSchema.value.supportedModes.map((m) => ({
    key: m,
    label: queryModeLabels[m],
  }));
});

watch(currentSchema, (schema) => {
  if (!schema) {
    records.value = [];
    searched.value = false;
    filters.deviceId = null;
    return;
  }
  if (!schema.supportedModes.includes(currentMode.value)) {
    const next = schema.supportedModes[0];
    if (next) currentMode.value = next;
  }
});

watch(currentProcessId, () => {
  currentPage.value = 1;
  searched.value = false;
  records.value = [];
  filters.deviceId = null;
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

// === 详情抽屉 ===
const showDetail = ref(false);
const detailLoading = ref(false);
const detailData = ref<PassStationDetailDto | null>(null);

function formatDisplayValue(value: string | null | undefined) {
  if (!value) return '-';
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
  if (!normalized) return '-';
  if (normalized === 'OK') return '合格';
  if (normalized === 'NG') return '不合格';
  return value ?? '-';
}

// === 数据 ===
const fetchSelectData = async () => {
  const [processes, devices, schemas] = await Promise.all([
    getAllProcessesApi().catch(() => [] as ProcessSelectDto[]),
    getAllActiveDevicesApi().catch(() => [] as DeviceSelectDto[]),
    getPassStationTypesApi().catch(() => []),
  ]);
  allProcesses.value = processes;
  allDevices.value = devices;
  schemaMap.value = buildPassStationSchemaMap(schemas);

  const firstSupported = supportedProcesses.value[0];
  if (!currentProcessId.value && firstSupported) {
    currentProcessId.value = firstSupported.id;
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
        currentMode.value === 'barcode-process' ||
        currentMode.value === 'time-process'
          ? currentProcess.value.id
          : undefined,
      deviceId:
        currentMode.value === 'device-barcode' ||
        currentMode.value === 'device-time' ||
        currentMode.value === 'device-latest'
          ? filters.deviceId || undefined
          : undefined,
      barcode:
        currentMode.value === 'barcode-process' ||
        currentMode.value === 'device-barcode'
          ? filters.barcode.trim()
          : undefined,
      startTime:
        currentMode.value === 'time-process' ||
        currentMode.value === 'device-time'
          ? toUtcIso(filters.startTime)
          : undefined,
      endTime:
        currentMode.value === 'time-process' ||
        currentMode.value === 'device-time'
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
  if (
    (currentMode.value === 'barcode-process' ||
      currentMode.value === 'device-barcode') &&
    !filters.barcode.trim()
  ) {
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
  if (
    (currentMode.value === 'time-process' ||
      currentMode.value === 'device-time') &&
    (!filters.startTime || !filters.endTime)
  ) {
    alert('请同时填写开始时间和结束时间。');
    return;
  }
  currentPage.value = 1;
  await fetchData();
};

const onPageChange = async (p: number) => {
  currentPage.value = p;
  await fetchData();
};

const openDetail = async (id: string) => {
  if (!currentSchema.value) return;
  showDetail.value = true;
  detailLoading.value = true;
  detailData.value = null;
  try {
    detailData.value = await getPassStationDetailApi(
      currentSchema.value.typeKey,
      id,
    );
  } catch {
    showDetail.value = false;
  } finally {
    detailLoading.value = false;
  }
};

// === 表格列（schema 驱动动态生成） ===
const columns = computed<UiDataTableColumn<PassStationListItemDto>[]>(() => {
  if (!currentSchema.value) return [];
  return currentSchema.value.columns.map((col) => ({
    title: col.label,
    key: col.key,
    minWidth: col.variant === 'barcode' ? 200 : 140,
    render(record) {
      const raw = col.render(record);
      if (col.variant === 'barcode') {
        return h('code', { class: 'cell-barcode' }, raw);
      }
      if (col.variant === 'result') {
        const isOk = (record.cellResult ?? '').toUpperCase() === 'OK';
        return h(
          UiTag,
          {
            size: 'small',
            bordered: false,
            type: isOk ? 'success' : 'error',
          },
          { default: () => raw },
        );
      }
      return h(
        'span',
        {
          class: [
            col.className === 'mono' ? 'cell-mono' : '',
            col.className === 'time-cell' ? 'cell-time' : '',
          ].filter(Boolean),
        },
        formatDisplayValue(raw),
      );
    },
  }));
});

const rowKey = (row: PassStationListItemDto) => row.id;
const rowProps = (row: PassStationListItemDto) => ({
  style: 'cursor: pointer;',
  onClick: () => openDetail(row.id),
});

onMounted(() => {
  void fetchSelectData();
});
</script>

<style scoped>
.passstation-page {
  font-family: var(--font-sans);
  color: var(--text-0);
}

.passstation-page__context-card {
  margin-bottom: var(--space-4);
}
.context-row {
  display: flex;
  align-items: flex-end;
  gap: var(--space-4);
  flex-wrap: wrap;
}
.context-hint {
  font-size: var(--fs-sm);
  color: var(--text-1);
  padding-bottom: 6px;
}
.context-hint__code {
  font-family: var(--font-mono);
  color: var(--brand);
  background: var(--brand-soft);
  padding: 2px 8px;
  border-radius: var(--radius-sm);
}

.passstation-page__filter-card {
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
  letter-spacing: 0;
}
.latest-hint {
  font-size: var(--fs-sm);
  color: var(--brand);
  background: var(--brand-soft);
  border: 1px dashed rgba(8, 145, 178, 0.32);
  padding: var(--space-2) var(--space-3);
  border-radius: var(--radius-sm);
  align-self: flex-end;
  height: 32px;
  display: flex;
  align-items: center;
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

/* 表格 */
.passstation-page__table :deep(.cell-barcode) {
  font-family: var(--font-mono);
  font-size: var(--fs-sm);
  color: var(--brand);
  background: var(--brand-soft);
  padding: 2px 8px;
  border-radius: var(--radius-sm);
}
.passstation-page__table :deep(.cell-mono) {
  font-family: var(--font-mono);
  font-size: var(--fs-sm);
}
.passstation-page__table :deep(.cell-time) {
  color: var(--text-2);
  white-space: nowrap;
}
.passstation-page__table :deep(.n-data-table-thead) {
  background: var(--bg-3);
}
.passstation-page__table :deep(.n-data-table-th) {
  font-size: var(--fs-xs) !important;
  font-weight: var(--fw-semibold) !important;
  color: var(--text-2) !important;
  letter-spacing: 0;
  text-transform: uppercase;
}
.passstation-page__table :deep(.n-data-table-tr:hover .n-data-table-td) {
  background-color: var(--bg-3) !important;
}

/* 详情抽屉 */
.detail-stack {
  display: flex;
  flex-direction: column;
  gap: var(--space-5);
}
.detail-result-banner {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  padding: var(--space-3) var(--space-4);
  border-radius: var(--radius-md);
  font-size: var(--fs-md);
  font-weight: var(--fw-semibold);
}
.detail-result-banner.is-ok {
  background: var(--success-soft);
  color: var(--success);
}
.detail-result-banner.is-ng {
  background: var(--error-soft);
  color: var(--error);
}
.detail-result-banner__dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: currentColor;
  box-shadow: 0 0 5px currentColor;
}

.detail-section {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}
.detail-section__title {
  font-size: var(--fs-xs);
  font-weight: var(--fw-semibold);
  color: var(--text-2);
  text-transform: uppercase;
  letter-spacing: 0;
  padding-bottom: var(--space-2);
  border-bottom: 1px solid var(--border);
}
.detail-row {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--space-4);
}
.detail-row__label {
  font-size: var(--fs-sm);
  color: var(--text-2);
  flex-shrink: 0;
}
.detail-row__value {
  font-size: var(--fs-base);
  color: var(--text-0);
  text-align: right;
  word-break: break-all;
}
.detail-row__value--mono {
  font-family: var(--font-mono);
}
.detail-row__value--small {
  font-size: var(--fs-sm);
  color: var(--text-1);
}
</style>
