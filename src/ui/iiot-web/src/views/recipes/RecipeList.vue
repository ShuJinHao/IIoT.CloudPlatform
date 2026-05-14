<template>
  <NiondDataPage
    class="recipe-page"
    page-key="recipes"
    title="配方管理"
      subtitle="管理生产设备配方和版本历史，支持按设备查看、升级和删除"
  >
      <template #actions>
        <UiButton
          type="primary"
          v-permission="'Recipe.Create'"
          @click="openCreateModal"
        >
          <template #icon>
            <svg viewBox="0 0 16 16" fill="none">
              <path d="M8 2v12M2 8h12" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/>
            </svg>
          </template>
          新建配方
        </UiButton>
      </template>

    <template #toolbar>
      <NiondToolbar>
        <div class="filter-row">
          <UiInput
            v-model:value="keyword"
            placeholder="搜索配方名称..."
            clearable
            size="small"
            style="max-width: 320px;"
            @input="onSearchInput"
            @keyup.enter="fetchList"
            @clear="onClearKeyword"
          >
            <template #prefix>
              <svg viewBox="0 0 16 16" width="14" height="14" fill="none">
                <circle cx="6.5" cy="6.5" r="4.5" stroke="currentColor" stroke-width="1.3"/>
                <path d="M10 10l3 3" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/>
              </svg>
            </template>
          </UiInput>
          <UiTag round :bordered="false" size="small">共 {{ metaData.totalCount }} 条</UiTag>
        </div>
      </NiondToolbar>
    </template>

    <NiondTableCard class="recipe-page__table-card">
      <UiDataTable
        class="recipe-page__table"
        :columns="columns"
        :data="recipes"
        :loading="loading"
        :bordered="false"
        :single-line="false"
        :row-key="rowKey"
        size="small"
      />
      <div v-if="metaData.totalPages > 1" class="pagination-wrap">
        <UiPagination
          :page="currentPage"
          :page-count="metaData.totalPages"
          :item-count="metaData.totalCount"
          :page-size="10"
          show-quick-jumper
          @update:page="onPageChange"
        />
      </div>
    </NiondTableCard>

    <!-- 新建配方 modal -->
    <UiModal
      v-model:show="showCreateModal"
      preset="card"
      title="新建生产配方"
      style="width: 720px;"
      :mask-closable="false"
    >
      <div class="form-stack">
        <div class="form-grid form-grid--2">
          <div class="form-field">
            <label class="form-label">配方名称 <span class="required">*</span></label>
            <UiInput v-model:value="createForm.recipeName" placeholder="如：A型号冬季配方" />
          </div>
          <div class="form-field">
            <label class="form-label">归属工序 <span class="required">*</span></label>
            <UiSelect
              v-model:value="createForm.processId"
              :options="processOptions"
              placeholder="请选择工序"
              filterable
            />
          </div>
        </div>
        <div class="form-field">
          <label class="form-label">归属设备 <span class="required">*</span></label>
          <UiSelect
            v-model:value="createForm.deviceId"
            :options="deviceOptions"
            placeholder="请选择设备"
            filterable
          />
        </div>
        <ParamsEditor v-model:params="createParams" />
      </div>
      <template #footer>
        <div class="modal-actions">
          <UiButton @click="showCreateModal = false">取消</UiButton>
          <UiButton
            type="primary"
            :loading="submitting"
            :disabled="createParams.length === 0"
            @click="submitCreate"
          >
            创建配方
          </UiButton>
        </div>
      </template>
    </UiModal>

    <!-- 升级版本 modal -->
    <UiModal
      v-model:show="showUpgradeModal"
      preset="card"
      style="width: 720px;"
      :mask-closable="false"
    >
      <template #header>
        <div class="modal-header-stack">
          <div class="modal-header-title">升级配方版本</div>
          <div class="modal-header-sub">
            {{ upgradeTarget?.recipeName }} · 当前版本 {{ upgradeTarget?.version }}
          </div>
        </div>
      </template>
      <div class="form-stack">
        <div class="form-grid form-grid--2">
          <div class="form-field">
            <label class="form-label">新版本号 <span class="required">*</span></label>
            <UiInput
              v-model:value="upgradeForm.newVersion"
              placeholder="如：V2.0"
              class="mono-input"
            />
            <p class="form-hint">版本号不能与已有版本重复</p>
          </div>
          <div class="form-field">
            <label class="form-label">配方类型</label>
            <div class="readonly-badge">
              <UiTag :type="isDeviceBoundRecipe(upgradeTarget?.deviceId) ? 'warning' : 'info'" :bordered="false" size="small">
                {{ isDeviceBoundRecipe(upgradeTarget?.deviceId) ? '设备配方' : '未绑定设备' }}
              </UiTag>
            </div>
          </div>
        </div>
        <ParamsEditor v-model:params="upgradeParams" />
      </div>
      <template #footer>
        <div class="modal-actions">
          <UiButton @click="showUpgradeModal = false">取消</UiButton>
          <UiButton
            type="primary"
            :loading="submitting"
            :disabled="upgradeParams.length === 0"
            @click="submitUpgrade"
          >
            保存并升版
          </UiButton>
        </div>
      </template>
    </UiModal>

    <!-- 详情侧滑 -->
    <UiDrawer
      v-model:show="showDetailPanel"
      :width="420"
      placement="right"
    >
      <UiDrawerContent title="配方详情" closable>
        <LoadingState v-if="detailLoading" :rows="6" />
        <div v-else-if="detailData" class="detail-stack">
          <div
            class="detail-status-banner"
            :class="detailData.status === 'Active' ? 'is-active' : 'is-archived'"
          >
            <span class="detail-status-banner__dot"></span>
            {{ detailData.status === 'Active' ? '配方启用中' : '配方已归档' }}
            <UiTag
              size="small"
              :bordered="false"
              :type="isDeviceBoundRecipe(detailData.deviceId) ? 'warning' : 'info'"
              style="margin-left: auto;"
            >
              {{ isDeviceBoundRecipe(detailData.deviceId) ? '设备配方' : '未绑定设备' }}
            </UiTag>
          </div>

          <div class="detail-row">
            <span class="detail-row__label">配方名称</span>
            <span class="detail-row__value">{{ detailData.recipeName }}</span>
          </div>
          <div class="detail-row">
            <span class="detail-row__label">当前版本</span>
            <span class="detail-row__value">
              <UiTag size="small" :bordered="false" type="info" class="mono-tag">{{ detailData.version }}</UiTag>
            </span>
          </div>
          <div class="detail-row">
            <span class="detail-row__label">归属工序</span>
            <span class="detail-row__value">{{ processNameMap[detailData.processId] || detailData.processId }}</span>
          </div>
          <div v-if="isDeviceBoundRecipe(detailData.deviceId)" class="detail-row">
            <span class="detail-row__label">归属设备</span>
            <span class="detail-row__value">{{ deviceNameMap[detailData.deviceId] || detailData.deviceId }}</span>
          </div>

          <div class="detail-row">
            <span class="detail-row__label">工艺参数</span>
            <div v-if="detailParams.length > 0" class="detail-params">
              <div v-for="p in detailParams" :key="p.id" class="detail-params__item">
                <span class="detail-params__name">{{ p.name }}</span>
                <span class="detail-params__range">{{ p.min }} ~ {{ p.max }} {{ p.unit }}</span>
              </div>
            </div>
            <pre v-else class="detail-json">{{ prettyJson(detailData.parametersJsonb) }}</pre>
          </div>
        </div>
      </UiDrawerContent>
    </UiDrawer>

    <!-- 删除确认 -->
    <UiModal
      v-model:show="confirmDialog.show"
      preset="card"
      :title="confirmDialog.title"
      style="width: 420px;"
      :mask-closable="false"
    >
      <p class="confirm-desc">{{ confirmDialog.desc }}</p>
      <template #footer>
        <div class="modal-actions">
          <UiButton @click="confirmDialog.show = false">取消</UiButton>
          <UiButton
            type="error"
            :loading="submitting"
            @click="confirmDialog.onConfirm()"
          >
            {{ confirmDialog.confirmText }}
          </UiButton>
        </div>
      </template>
    </UiModal>
  </NiondDataPage>
</template>

<script setup lang="ts">
import { ref, reactive, computed, h, onMounted, defineComponent } from 'vue';
import {
  getRecipePagedListApi,
  getRecipeDetailApi,
  createRecipeApi,
  upgradeRecipeVersionApi,
  deleteRecipeApi,
  type RecipeListItemDto,
  type RecipeDetailDto,
  type RecipeParameter,
  type PagedMetaData,
} from '../../api/recipe';
import { getAllProcessesApi, type ProcessSelectDto } from '../../api/masterData/processes';
import { getAllActiveDevicesApi, type DeviceSelectDto } from '../../api/device';
import NiondDataPage from '../../components/layout/NiondDataPage.vue';
import NiondTableCard from '../../components/layout/NiondTableCard.vue';
import NiondToolbar from '../../components/layout/NiondToolbar.vue';
import LoadingState from '../../components/states/LoadingState.vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiDataTable from '../../components/ui/UiDataTable.vue';
import UiDrawer from '../../components/ui/UiDrawer.vue';
import UiDrawerContent from '../../components/ui/UiDrawerContent.vue';
import UiInput from '../../components/ui/UiInput.vue';
import UiModal from '../../components/ui/UiModal.vue';
import UiNumberInput from '../../components/ui/UiNumberInput.vue';
import UiPagination from '../../components/ui/UiPagination.vue';
import UiSelect from '../../components/ui/UiSelect.vue';
import UiTag from '../../components/ui/UiTag.vue';
import type { UiDataTableColumn } from '../../components/ui/types';

const recipes = ref<RecipeListItemDto[]>([]);
const loading = ref(false);
const keyword = ref('');
const currentPage = ref(1);
const metaData = ref<PagedMetaData>({
  totalCount: 0,
  pageSize: 10,
  currentPage: 1,
  totalPages: 1,
});
const submitting = ref(false);

const allProcesses = ref<ProcessSelectDto[]>([]);
const allDevices = ref<DeviceSelectDto[]>([]);
const EMPTY_GUID = '00000000-0000-0000-0000-000000000000';

const isDeviceBoundRecipe = (deviceId?: string | null) =>
  Boolean(deviceId && deviceId !== EMPTY_GUID);

const isRecipeDetailDto = (value: unknown): value is RecipeDetailDto => {
  if (!value || typeof value !== 'object') return false;
  const candidate = value as Partial<RecipeDetailDto>;
  return typeof candidate.id === 'string' && typeof candidate.parametersJsonb === 'string';
};

const fetchSelectData = async () => {
  try {
    allProcesses.value = await getAllProcessesApi();
  } catch {
    allProcesses.value = [];
  }
  try {
    allDevices.value = await getAllActiveDevicesApi();
  } catch {
    allDevices.value = [];
  }
};

const processNameMap = computed(() => {
  const m: Record<string, string> = {};
  for (const p of allProcesses.value) {
    m[p.id] = `${p.processCode} · ${p.processName}`;
  }
  return m;
});
const deviceNameMap = computed(() => {
  const m: Record<string, string> = {};
  for (const d of allDevices.value) m[d.id] = d.deviceName;
  return m;
});
const processOptions = computed(() =>
  allProcesses.value.map((p) => ({
    label: `${p.processCode} · ${p.processName}`,
    value: p.id,
  })),
);
const deviceOptions = computed(() =>
  allDevices.value.map((d) => ({ label: d.deviceName, value: d.id })),
);

let searchTimer: ReturnType<typeof setTimeout> | null = null;
const onSearchInput = () => {
  if (searchTimer) clearTimeout(searchTimer);
  searchTimer = setTimeout(() => {
    currentPage.value = 1;
    fetchList();
  }, 400);
};

const onClearKeyword = () => {
  currentPage.value = 1;
  fetchList();
};

const fetchList = async () => {
  loading.value = true;
  try {
    const response = await getRecipePagedListApi({
      pagination: { PageNumber: currentPage.value, PageSize: 10 },
      keyword: keyword.value || undefined,
    });
    metaData.value = response.metaData;
    recipes.value = response.items;
  } catch {
    recipes.value = [];
  } finally {
    loading.value = false;
  }
};

const onPageChange = (p: number) => {
  currentPage.value = p;
  fetchList();
};

// === 参数工具 ===
const generateParamId = () =>
  crypto.randomUUID?.() || Math.random().toString(36).substring(2, 10);

const parseParams = (jsonb: string): RecipeParameter[] => {
  try {
    const arr = JSON.parse(jsonb);
    if (Array.isArray(arr))
      return arr.map((p: any) => ({
        id: p.id || generateParamId(),
        name: p.name || '',
        unit: p.unit || '',
        min: p.min ?? 0,
        max: p.max ?? 0,
      }));
  } catch {
    /* */
  }
  return [];
};

const paramsToJsonb = (params: RecipeParameter[]) =>
  JSON.stringify(
    params.map((p) => ({
      id: p.id,
      name: p.name,
      unit: p.unit,
      min: p.min,
      max: p.max,
    })),
  );

const validateParams = (params: RecipeParameter[]): string | null => {
  if (params.length === 0) return '至少保留一个工艺参数';
  for (let i = 0; i < params.length; i += 1) {
    const param = params[i]!;
    const label = `第 ${i + 1} 个参数`;
    if (!param.id?.trim()) return `${label}缺少参数标识，请删除后重新添加`;
    if (!param.name?.trim()) return `${label}的参数名称不能为空`;
    if (!param.unit?.trim()) return `${label}的单位不能为空`;
    if (typeof param.min !== 'number' || Number.isNaN(param.min))
      return `${label}的下限必须是数字`;
    if (typeof param.max !== 'number' || Number.isNaN(param.max))
      return `${label}的上限必须是数字`;
    if (param.min > param.max) return `${label}的下限不能大于上限`;
  }
  return null;
};

const prettyJson = (str: string) => {
  try {
    return JSON.stringify(JSON.parse(str), null, 2);
  } catch {
    return str;
  }
};

// === 表格列 ===
const columns: UiDataTableColumn<RecipeListItemDto>[] = [
  {
    title: '配方名称',
    key: 'recipeName',
    minWidth: 180,
    render(row) {
      return h('span', { class: 'cell-name' }, row.recipeName);
    },
  },
  {
    title: '版本',
    key: 'version',
    width: 100,
    render(row) {
      return h(
        UiTag,
        { size: 'small', bordered: false, type: 'info' },
        { default: () => row.version },
      );
    },
  },
  {
    title: '类型',
    key: 'type',
    width: 110,
    render(row) {
      const bound = isDeviceBoundRecipe(row.deviceId);
      return h(
        UiTag,
        {
          size: 'small',
          bordered: false,
          type: bound ? 'warning' : 'info',
        },
        { default: () => (bound ? '设备配方' : '未绑定设备') },
      );
    },
  },
  {
    title: '状态',
    key: 'status',
    width: 110,
    render(row) {
      const active = row.status === 'Active';
      return h(
        UiTag,
        {
          size: 'small',
          bordered: false,
          type: active ? 'success' : 'warning',
        },
        { default: () => (active ? '启用中' : '已归档') },
      );
    },
  },
  {
    title: '所属工序',
    key: 'processId',
    minWidth: 160,
    render(row) {
      return h(
        'span',
        { class: 'cell-chip' },
        processNameMap.value[row.processId] || `${row.processId.substring(0, 8)}…`,
      );
    },
  },
  {
    title: '所属机台',
    key: 'deviceId',
    minWidth: 140,
    render(row) {
      if (!isDeviceBoundRecipe(row.deviceId)) {
        return h('span', { class: 'cell-muted' }, '—');
      }
      return h(
        'span',
        { class: 'cell-chip cell-chip--warn' },
        deviceNameMap.value[row.deviceId] || `${row.deviceId.substring(0, 8)}…`,
      );
    },
  },
  {
    title: '操作',
    key: 'actions',
    width: 200,
    align: 'right',
    render(row) {
      const buttons = [
        h(
          UiButton,
          {
            size: 'tiny',
            type: 'primary',
            secondary: true,
            onClick: () => openDetailPanel(row),
          },
          { default: () => '详情' },
        ),
      ];
      if (row.status === 'Active') {
        buttons.push(
          h(
            UiButton,
            {
              size: 'tiny',
              type: 'info',
              secondary: true,
              onClick: () => openUpgradeModal(row),
            },
            { default: () => '升版' },
          ),
        );
      }
      buttons.push(
        h(
          UiButton,
          {
            size: 'tiny',
            type: 'error',
            secondary: true,
            onClick: () => handleDelete(row),
          },
          { default: () => '删除' },
        ),
      );
      return h('div', { class: 'row-actions' }, buttons);
    },
  },
];

const rowKey = (row: RecipeListItemDto) => row.id;

// === 新建配方 ===
const showCreateModal = ref(false);
const createParams = ref<RecipeParameter[]>([]);
const createForm = reactive({
  recipeName: '',
  processId: null as string | null,
  deviceId: null as string | null,
});

const openCreateModal = async () => {
  createForm.recipeName = '';
  createForm.processId = null;
  createForm.deviceId = null;
  createParams.value = [];
  showCreateModal.value = true;
  await fetchSelectData();
};

const submitCreate = async () => {
  if (!createForm.recipeName.trim() || !createForm.processId || !createForm.deviceId) {
    alert('配方名称、归属工序和归属设备为必填项');
    return;
  }
  const err = validateParams(createParams.value);
  if (err) {
    alert(err);
    return;
  }
  submitting.value = true;
  try {
    await createRecipeApi({
      recipeName: createForm.recipeName,
      processId: createForm.processId,
      deviceId: createForm.deviceId,
      parametersJsonb: paramsToJsonb(createParams.value),
    });
    showCreateModal.value = false;
    fetchList();
  } catch {
    /* */
  } finally {
    submitting.value = false;
  }
};

// === 升级版本 ===
const showUpgradeModal = ref(false);
const upgradeTarget = ref<RecipeListItemDto | null>(null);
const upgradeParams = ref<RecipeParameter[]>([]);
const upgradeForm = reactive({ newVersion: '' });

const openUpgradeModal = async (recipe: RecipeListItemDto) => {
  upgradeTarget.value = recipe;
  upgradeForm.newVersion = '';
  upgradeParams.value = [];
  showUpgradeModal.value = true;
  try {
    const raw = await getRecipeDetailApi(recipe.id);
    upgradeParams.value = parseParams(raw.parametersJsonb || '');
  } catch (error: unknown) {
    if (isRecipeDetailDto(error)) {
      upgradeParams.value = parseParams(error.parametersJsonb);
    } else {
      upgradeParams.value = [];
    }
  }
};

const submitUpgrade = async () => {
  if (!upgradeTarget.value || !upgradeForm.newVersion.trim()) {
    alert('版本号不能为空');
    return;
  }
  const err = validateParams(upgradeParams.value);
  if (err) {
    alert(err);
    return;
  }
  submitting.value = true;
  try {
    await upgradeRecipeVersionApi(upgradeTarget.value.id, {
      sourceRecipeId: upgradeTarget.value.id,
      newVersion: upgradeForm.newVersion,
      parametersJsonb: paramsToJsonb(upgradeParams.value),
    });
    showUpgradeModal.value = false;
    fetchList();
  } catch {
    /* */
  } finally {
    submitting.value = false;
  }
};

// === 详情抽屉 ===
const showDetailPanel = ref(false);
const detailData = ref<RecipeDetailDto | null>(null);
const detailLoading = ref(false);
const detailParams = computed(() => {
  if (!detailData.value) return [];
  return parseParams(detailData.value.parametersJsonb);
});

const openDetailPanel = async (recipe: RecipeListItemDto) => {
  showDetailPanel.value = true;
  detailLoading.value = true;
  detailData.value = null;
  try {
    detailData.value = await getRecipeDetailApi(recipe.id);
  } catch (error: unknown) {
    if (isRecipeDetailDto(error)) {
      detailData.value = error;
    } else {
      showDetailPanel.value = false;
    }
  } finally {
    detailLoading.value = false;
  }
};

// === 物理删除确认 ===
const confirmDialog = reactive({
  show: false,
  title: '',
  desc: '',
  confirmText: '',
  onConfirm: () => Promise.resolve(),
});

const handleDelete = (recipe: RecipeListItemDto) => {
  Object.assign(confirmDialog, {
    show: true,
    title: '确认永久删除配方',
    desc: `配方【${recipe.recipeName} · ${recipe.version}】将被永久删除且无法恢复，确认要删除吗？`,
    confirmText: '永久删除',
    onConfirm: async () => {
      submitting.value = true;
      try {
        await deleteRecipeApi(recipe.id);
        confirmDialog.show = false;
        fetchList();
      } catch {
        /* */
      } finally {
        submitting.value = false;
      }
    },
  });
};

onMounted(() => {
  fetchList();
  fetchSelectData();
});

// === 内嵌组件：参数编辑器 ===
const ParamsEditor = defineComponent({
  name: 'ParamsEditor',
  props: {
    params: {
      type: Array as () => RecipeParameter[],
      required: true,
    },
  },
  emits: ['update:params'],
  setup(props, { emit }) {
    const addParam = () => {
      const next = [
        ...props.params,
        { id: generateParamId(), name: '', unit: '', min: 0, max: 0 },
      ];
      emit('update:params', next);
    };

    const removeParam = (idx: number) => {
      const next = [...props.params];
      next.splice(idx, 1);
      emit('update:params', next);
    };

    const updateParam = (
      idx: number,
      field: keyof RecipeParameter,
      value: string | number,
    ) => {
      const next = [...props.params];
      const param = { ...next[idx]! };
      (param as any)[field] = value;
      next[idx] = param;
      emit('update:params', next);
    };

    return () =>
      h('div', { class: 'params-editor' }, [
        h('div', { class: 'params-editor__head' }, [
          h(
            'label',
            { class: 'form-label' },
            [
              '工艺参数 ',
              h('span', { class: 'required' }, '*'),
            ],
          ),
          h(
            UiButton,
            {
              size: 'tiny',
              type: 'primary',
              secondary: true,
              onClick: addParam,
            },
            { default: () => '+ 添加参数' },
          ),
        ]),
        props.params.length === 0
          ? h(
              'div',
              { class: 'params-editor__empty' },
              '暂无参数，点击"添加参数"开始配置',
            )
          : h('div', { class: 'params-editor__table' }, [
              h('div', { class: 'params-editor__row params-editor__row--head' }, [
                h('span', '参数名称'),
                h('span', '单位'),
                h('span', '下限'),
                h('span', '上限'),
                h('span'),
              ]),
              ...props.params.map((p, idx) =>
                h(
                  'div',
                  { class: 'params-editor__row', key: p.id },
                  [
                    h(UiInput, {
                      value: p.name,
                      placeholder: '如：温度',
                      size: 'small',
                      'onUpdate:value': (v: string) =>
                        updateParam(idx, 'name', v),
                    }),
                    h(UiInput, {
                      value: p.unit,
                      placeholder: '℃',
                      size: 'small',
                      'onUpdate:value': (v: string) =>
                        updateParam(idx, 'unit', v),
                    }),
                    h(UiNumberInput, {
                      value: p.min,
                      placeholder: '0',
                      size: 'small',
                      showButton: false,
                      style: 'width: 100%;',
                      'onUpdate:value': (v: number | null) =>
                        updateParam(idx, 'min', v ?? 0),
                    }),
                    h(UiNumberInput, {
                      value: p.max,
                      placeholder: '100',
                      size: 'small',
                      showButton: false,
                      style: 'width: 100%;',
                      'onUpdate:value': (v: number | null) =>
                        updateParam(idx, 'max', v ?? 0),
                    }),
                    h(
                      UiButton,
                      {
                        size: 'tiny',
                        quaternary: true,
                        type: 'error',
                        onClick: () => removeParam(idx),
                      },
                      { default: () => '✕' },
                    ),
                  ],
                ),
              ),
            ]),
      ]);
  },
});
</script>

<style scoped>
.recipe-page {
  font-family: var(--font-sans);
  color: var(--text-0);
}

.recipe-page__filter-card {
  margin-bottom: var(--space-4);
}
.filter-row {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  flex-wrap: wrap;
}

.pagination-wrap {
  display: flex;
  justify-content: flex-end;
  padding: var(--space-4);
  border-top: 1px solid var(--border);
}

/* 表格单元 */
.recipe-page__table :deep(.cell-name) {
  font-size: var(--fs-base);
  font-weight: var(--fw-medium);
  color: var(--text-0);
}
.recipe-page__table :deep(.cell-chip) {
  display: inline-block;
  font-size: var(--fs-sm);
  color: var(--text-1);
  background: var(--bg-3);
  padding: 2px 8px;
  border-radius: var(--radius-sm);
}
.recipe-page__table :deep(.cell-chip--warn) {
  color: var(--warn);
  background: var(--warn-soft);
}
.recipe-page__table :deep(.cell-muted) {
  color: var(--text-2);
}
.recipe-page__table :deep(.row-actions) {
  display: flex;
  gap: var(--space-2);
  justify-content: flex-end;
}
.recipe-page__table :deep(.n-data-table-thead) {
  background: var(--bg-3);
}
.recipe-page__table :deep(.n-data-table-th) {
  font-size: var(--fs-xs) !important;
  font-weight: var(--fw-semibold) !important;
  color: var(--text-2) !important;
  letter-spacing: 0;
  text-transform: uppercase;
}
.recipe-page__table :deep(.n-data-table-tr:hover .n-data-table-td) {
  background-color: var(--bg-3) !important;
}

/* 表单 */
.form-stack {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}
.form-grid--2 {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: var(--space-4);
}
.form-field {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}
.form-label {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  font-size: var(--fs-sm);
  font-weight: var(--fw-medium);
  color: var(--text-1);
}
.required {
  color: var(--error);
}
.form-hint {
  font-size: var(--fs-xs);
  color: var(--text-2);
  margin: 0;
}
.mono-input :deep(.n-input__input-el) {
  font-family: var(--font-mono);
}
.readonly-badge {
  padding: 6px 0;
}
.modal-actions {
  display: flex;
  justify-content: flex-end;
  gap: var(--space-2);
}
.modal-header-stack {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
}
.modal-header-title {
  font-size: var(--fs-lg);
  font-weight: var(--fw-semibold);
  color: var(--text-0);
}
.modal-header-sub {
  font-size: var(--fs-sm);
  color: var(--text-1);
}

/* 参数编辑器 */
.params-editor {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}
.params-editor__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-2);
}
.params-editor__empty {
  padding: var(--space-6);
  border: 1px dashed var(--border-strong);
  border-radius: var(--radius-md);
  text-align: center;
  font-size: var(--fs-sm);
  color: var(--text-2);
}
.params-editor__table {
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  overflow: hidden;
}
.params-editor__row {
  display: grid;
  grid-template-columns: 2fr 1fr 1fr 1fr 36px;
  gap: var(--space-2);
  padding: var(--space-2) var(--space-3);
  align-items: center;
  border-bottom: 1px solid var(--border);
}
.params-editor__row:last-child {
  border-bottom: none;
}
.params-editor__row--head {
  background: var(--bg-3);
  font-size: var(--fs-xs);
  font-weight: var(--fw-semibold);
  color: var(--text-2);
  letter-spacing: 0;
  padding: var(--space-2) var(--space-3);
}
.params-editor__row--head span {
  text-transform: uppercase;
}

/* 详情抽屉 */
.detail-stack {
  display: flex;
  flex-direction: column;
  gap: var(--space-5);
}
.detail-status-banner {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  padding: var(--space-3) var(--space-4);
  border-radius: var(--radius-md);
  font-size: var(--fs-sm);
  font-weight: var(--fw-medium);
}
.detail-status-banner.is-active {
  background: var(--success-soft);
  color: var(--success);
}
.detail-status-banner.is-archived {
  background: var(--warn-soft);
  color: var(--warn);
}
.detail-status-banner__dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: currentColor;
  box-shadow: 0 0 5px currentColor;
}
.detail-row {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
}
.detail-row__label {
  font-size: var(--fs-xs);
  color: var(--text-2);
  text-transform: uppercase;
  letter-spacing: 0;
  font-weight: var(--fw-medium);
}
.detail-row__value {
  font-size: var(--fs-base);
  color: var(--text-0);
  word-break: break-all;
}
.mono-tag :deep(.n-tag__content) {
  font-family: var(--font-mono);
}

.detail-params {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}
.detail-params__item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: var(--space-2) var(--space-3);
  background: var(--bg-3);
  border-radius: var(--radius-sm);
}
.detail-params__name {
  font-size: var(--fs-base);
  color: var(--text-0);
  font-weight: var(--fw-medium);
}
.detail-params__range {
  font-family: var(--font-mono);
  font-size: var(--fs-sm);
  color: var(--brand);
}
.detail-json {
  margin: 0;
  padding: var(--space-3);
  background: var(--bg-3);
  border-radius: var(--radius-sm);
  font-family: var(--font-mono);
  font-size: var(--fs-xs);
  color: var(--text-1);
  overflow: auto;
  max-height: 320px;
  line-height: 1.6;
}

.confirm-desc {
  font-size: var(--fs-base);
  color: var(--text-1);
  line-height: 1.6;
  margin: 0;
}
</style>
