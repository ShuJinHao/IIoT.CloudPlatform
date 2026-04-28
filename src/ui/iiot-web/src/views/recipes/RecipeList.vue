<template>
  <div class="recipe-list">
    <!-- 页头 -->
    <div class="page-header">
      <div>
        <h1 class="page-title">配方管理</h1>
        <p class="page-sub">管理生产设备配方和版本历史，支持按设备查看、升级和删除。</p>
      </div>
      <button class="btn btn-primary" v-permission="'Recipe.Create'" @click="openCreateModal">
        <svg viewBox="0 0 16 16" fill="none"><path d="M8 2v12M2 8h12" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/></svg>
        新建配方
      </button>
    </div>

    <!-- 搜索栏 -->
    <div class="toolbar">
      <div class="search-wrap">
        <svg viewBox="0 0 16 16" fill="none"><circle cx="6.5" cy="6.5" r="4.5" stroke="currentColor" stroke-width="1.3"/><path d="M10 10l3 3" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/></svg>
        <input v-model="keyword" placeholder="搜索配方名称..." @keyup.enter="fetchList" @input="onSearchInput" />
        <button v-if="keyword" class="clear-btn" @click="keyword=''; fetchList()">✕</button>
      </div>
      <span class="total-badge">共 {{ metaData.totalCount }} 条</span>
    </div>

    <!-- 表格 -->
    <div class="table-wrap">
      <div v-if="loading" class="skeleton-rows">
        <div v-for="i in 5" :key="i" class="skeleton-row">
          <div class="skel skel-lg"></div><div class="skel skel-sm"></div>
          <div class="skel skel-sm"></div><div class="skel skel-md"></div><div class="skel skel-sm"></div>
        </div>
      </div>
      <table v-else class="data-table">
        <thead>
          <tr>
            <th>配方名称</th><th>版本</th><th>类型</th><th>状态</th><th>所属工序</th><th>所属机台</th>
            <th style="text-align:right">操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-if="recipes.length === 0">
            <td colspan="7" class="empty-cell">
              <div class="empty-state">
                <svg viewBox="0 0 48 48" fill="none"><rect x="8" y="6" width="28" height="36" rx="2" stroke="currentColor" stroke-width="1.5" opacity="0.3"/><path d="M14 16h16M14 22h12M14 28h8" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" opacity="0.3"/><circle cx="36" cy="36" r="8" fill="#0f1525" stroke="currentColor" stroke-width="1.5" opacity="0.5"/><path d="M33 36h6M36 33v6" stroke="currentColor" stroke-width="1.3" stroke-linecap="round" opacity="0.5"/></svg>
                <p>暂无配方档案</p>
              </div>
            </td>
          </tr>
          <tr v-for="recipe in recipes" :key="recipe.id" class="table-row" @click="openDetailPanel(recipe)">
            <td>
              <div class="recipe-name-cell">
                <span class="recipe-name">{{ recipe.recipeName }}</span>
                <span class="special-badge">设备</span>
              </div>
            </td>
            <td><span class="version-tag">{{ recipe.version }}</span></td>
            <td>
              <span class="type-tag" :class="isDeviceBoundRecipe(recipe.deviceId) ? 'special' : 'universal'">
                {{ isDeviceBoundRecipe(recipe.deviceId) ? '设备配方' : '未绑定设备' }}
              </span>
            </td>
            <td>
              <span class="status-tag" :class="recipe.status === 'Active' ? 'active' : 'archived'">
                <span class="status-dot"></span>{{ recipe.status === 'Active' ? '启用中' : '已归档' }}
              </span>
            </td>
            <td><span class="process-name-chip">{{ processNameMap[recipe.processId] || recipe.processId.substring(0, 8) + '…' }}</span></td>
            <td><span v-if="isDeviceBoundRecipe(recipe.deviceId)" class="device-name-chip">{{ deviceNameMap[recipe.deviceId] || recipe.deviceId.substring(0, 8) + '…' }}</span><span v-else class="no-device">—</span></td>
            <td class="action-cell" @click.stop>
              <button v-if="recipe.status === 'Active'" class="icon-btn edit" title="升级版本" v-permission="'Recipe.Update'" @click="openUpgradeModal(recipe)">
                <svg viewBox="0 0 16 16" fill="none"><path d="M8 12V4M5 7l3-3 3 3" stroke="currentColor" stroke-width="1.2" stroke-linecap="round" stroke-linejoin="round"/></svg>
              </button>
              <button class="icon-btn delete" title="删除配方" v-permission="'Recipe.Create'" @click="handleDelete(recipe)">
                <svg viewBox="0 0 16 16" fill="none"><path d="M4 5h8M5.5 5V4a1 1 0 011-1h3a1 1 0 011 1v1M6.5 7v4M9.5 7v4M5 5l.5 7.5a1 1 0 001 .5h3a1 1 0 001-.5L11 5" stroke="currentColor" stroke-width="1.1" stroke-linecap="round" stroke-linejoin="round"/></svg>
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- 分页 -->
    <div class="pagination" v-if="metaData.totalPages > 1">
      <button class="page-btn" :disabled="currentPage===1" @click="goPage(currentPage-1)">
        <svg viewBox="0 0 12 12" fill="none"><path d="M8 2L4 6l4 4" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/></svg>
      </button>
      <button v-for="p in pageNumbers" :key="p" class="page-btn" :class="{active:p===currentPage}" @click="goPage(p)">{{ p }}</button>
      <button class="page-btn" :disabled="currentPage===metaData.totalPages" @click="goPage(currentPage+1)">
        <svg viewBox="0 0 12 12" fill="none"><path d="M4 2l4 4-4 4" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/></svg>
      </button>
    </div>

    <!-- 新建配方弹窗（结构化参数表单） -->
    <Teleport to="body">
      <div v-if="showCreateModal" class="modal-overlay" @click.self="showCreateModal=false">
        <div class="modal modal-lg">
          <div class="modal-header">
            <span class="modal-title">新建生产配方</span>
            <button class="modal-close" @click="showCreateModal=false">✕</button>
          </div>
          <div class="modal-body">
            <div class="form-row">
              <div class="form-field">
                <label class="form-label">配方名称 <span class="required">*</span></label>
                <input class="form-input" v-model="createForm.recipeName" placeholder="如：A型号冬季配方" />
              </div>
              <div class="form-field">
                <label class="form-label">归属工序 <span class="required">*</span></label>
                <select class="form-input" v-model="createForm.processId">
                  <option value="">请选择工序</option>
                  <option v-for="p in allProcesses" :key="p.id" :value="p.id">{{ p.processCode }} · {{ p.processName }}</option>
                </select>
              </div>
            </div>
            <div class="form-field">
              <label class="form-label">归属设备 <span class="required">*</span></label>
              <select class="form-input" v-model="createForm.deviceId">
                <option value="">请选择设备</option>
                <option v-for="d in allDevices" :key="d.id" :value="d.id">{{ d.deviceName }}</option>
              </select>
            </div>
            <!-- 结构化参数表单 -->
            <div class="form-field">
              <label class="form-label">
                工艺参数 <span class="required">*</span>
                <button class="add-param-btn" @click="addCreateParam">+ 添加参数</button>
              </label>
              <div class="params-table" v-if="createParams.length > 0">
                <div class="params-header">
                  <span class="ph-name">参数名称</span>
                  <span class="ph-unit">单位</span>
                  <span class="ph-min">下限</span>
                  <span class="ph-max">上限</span>
                  <span class="ph-act"></span>
                </div>
                <div class="param-row" v-for="(param, idx) in createParams" :key="param.id">
                  <input class="param-input name" v-model="param.name" placeholder="如：温度" />
                  <input class="param-input unit" v-model="param.unit" placeholder="℃" />
                  <input class="param-input num" v-model.number="param.min" type="number" placeholder="0" />
                  <input class="param-input num" v-model.number="param.max" type="number" placeholder="100" />
                  <button class="param-del" @click="createParams.splice(idx, 1)" title="删除">✕</button>
                </div>
              </div>
              <div v-else class="params-empty">暂无参数，点击上方"添加参数"开始配置</div>
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn btn-ghost" @click="showCreateModal=false">取消</button>
            <button class="btn btn-primary" :disabled="submitting || createParams.length === 0" @click="submitCreate">
              {{ submitting ? '创建中...' : '创建配方' }}
            </button>
          </div>
        </div>
      </div>
    </Teleport>

    <!-- 升级版本弹窗（结构化参数表单） -->
    <Teleport to="body">
      <div v-if="showUpgradeModal" class="modal-overlay" @click.self="showUpgradeModal=false">
        <div class="modal modal-lg">
          <div class="modal-header">
            <div>
              <span class="modal-title">升级配方版本</span>
              <span class="modal-subtitle">{{ upgradeTarget?.recipeName }} · 当前版本 {{ upgradeTarget?.version }}</span>
            </div>
            <button class="modal-close" @click="showUpgradeModal=false">✕</button>
          </div>
          <div class="modal-body">
            <div class="form-row">
              <div class="form-field">
                <label class="form-label">新版本号 <span class="required">*</span></label>
                <input class="form-input mono-input" v-model="upgradeForm.newVersion" placeholder="如：V2.0" />
                <p class="form-hint">版本号不能与已有版本重复</p>
              </div>
              <div class="form-field">
                <label class="form-label">配方类型</label>
                <div class="readonly-field">
                  <span class="type-tag" :class="isDeviceBoundRecipe(upgradeTarget?.deviceId) ? 'special' : 'universal'">
                    {{ isDeviceBoundRecipe(upgradeTarget?.deviceId) ? '设备配方' : '未绑定设备' }}
                  </span>
                </div>
              </div>
            </div>
            <!-- 结构化参数表单 -->
            <div class="form-field">
              <label class="form-label">
                工艺参数 <span class="required">*</span>
                <button class="add-param-btn" @click="addUpgradeParam">+ 添加参数</button>
              </label>
              <div class="params-table" v-if="upgradeParams.length > 0">
                <div class="params-header">
                  <span class="ph-name">参数名称</span>
                  <span class="ph-unit">单位</span>
                  <span class="ph-min">下限</span>
                  <span class="ph-max">上限</span>
                  <span class="ph-act"></span>
                </div>
                <div class="param-row" v-for="(param, idx) in upgradeParams" :key="param.id">
                  <input class="param-input name" v-model="param.name" placeholder="如：温度" />
                  <input class="param-input unit" v-model="param.unit" placeholder="℃" />
                  <input class="param-input num" v-model.number="param.min" type="number" placeholder="0" />
                  <input class="param-input num" v-model.number="param.max" type="number" placeholder="100" />
                  <button class="param-del" @click="upgradeParams.splice(idx, 1)" title="删除">✕</button>
                </div>
              </div>
              <div v-else class="params-empty">暂无参数，点击上方"添加参数"开始配置</div>
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn btn-ghost" @click="showUpgradeModal=false">取消</button>
            <button class="btn btn-primary" :disabled="submitting || upgradeParams.length === 0" @click="submitUpgrade">
              {{ submitting ? '保存中...' : '保存并升版' }}
            </button>
          </div>
        </div>
      </div>
    </Teleport>

    <!-- 详情侧边栏 -->
    <Teleport to="body">
      <div v-if="showDetailPanel" class="detail-overlay" @click.self="showDetailPanel=false">
        <div class="detail-panel">
          <div class="detail-header">
            <span class="detail-title">配方详情</span>
            <button class="modal-close" @click="showDetailPanel=false">✕</button>
          </div>
          <div class="detail-body" v-if="detailLoading">
            <div class="detail-loading">
              <div class="loading-ring"></div>
              <span>加载中...</span>
            </div>
          </div>
          <div class="detail-body" v-else-if="detailData">
            <div class="detail-status-banner" :class="detailData.status === 'Active' ? 'active' : 'archived'">
              <span class="status-dot"></span>
              {{ detailData.status === 'Active' ? '配方启用中' : '配方已归档' }}
              <span class="detail-type-badge" :class="isDeviceBoundRecipe(detailData.deviceId) ? 'special' : 'universal'">
                {{ isDeviceBoundRecipe(detailData.deviceId) ? '设备配方' : '未绑定设备' }}
              </span>
            </div>
            <div class="detail-section">
              <div class="detail-row">
                <span class="detail-label">配方名称</span>
                <span class="detail-value">{{ detailData.recipeName }}</span>
              </div>
              <div class="detail-row">
                <span class="detail-label">当前版本</span>
                <span class="detail-value"><span class="version-tag">{{ detailData.version }}</span></span>
              </div>
              <div class="detail-row">
                <span class="detail-label">归属工序</span>
                <span class="detail-value">{{ processNameMap[detailData.processId] || detailData.processId }}</span>
              </div>
              <div v-if="isDeviceBoundRecipe(detailData.deviceId)" class="detail-row">
                <span class="detail-label">归属设备</span>
                <span class="detail-value">{{ deviceNameMap[detailData.deviceId] || detailData.deviceId }}</span>
              </div>
              <div class="detail-row">
                <span class="detail-label">工艺参数</span>
                <div class="detail-params-list" v-if="detailParams.length > 0">
                  <div class="detail-param-item" v-for="p in detailParams" :key="p.id">
                    <span class="dp-name">{{ p.name }}</span>
                    <span class="dp-range">{{ p.min }} ~ {{ p.max }} {{ p.unit }}</span>
                  </div>
                </div>
                <div v-else class="json-preview-wrap">
                  <pre class="json-preview">{{ prettyJson(detailData.parametersJsonb) }}</pre>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </Teleport>

    <!-- 确认删除对话框 -->
    <Teleport to="body">
      <div v-if="confirmDialog.show" class="modal-overlay">
        <div class="confirm-box">
          <div class="confirm-icon danger">
            <svg viewBox="0 0 20 20" fill="none"><path d="M10 6v5M10 13.5v.5" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/><circle cx="10" cy="10" r="8" stroke="currentColor" stroke-width="1.3"/></svg>
          </div>
          <div class="confirm-title">{{ confirmDialog.title }}</div>
          <div class="confirm-desc">{{ confirmDialog.desc }}</div>
          <div class="confirm-actions">
            <button class="btn btn-ghost" @click="confirmDialog.show=false">取消</button>
            <button class="btn btn-danger" :disabled="submitting" @click="confirmDialog.onConfirm()">
              {{ submitting ? '处理中...' : confirmDialog.confirmText }}
            </button>
          </div>
        </div>
      </div>
    </Teleport>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue';
import {
  getRecipePagedListApi, getRecipeDetailApi, createRecipeApi,
  upgradeRecipeVersionApi, deleteRecipeApi,
  type RecipeListItemDto, type RecipeDetailDto, type RecipeParameter, type PagedMetaData,
} from '../../api/recipe';
import { getAllProcessesApi, type ProcessSelectDto } from '../../api/masterData/processes';
import { getAllActiveDevicesApi, type DeviceSelectDto } from '../../api/device';

const recipes = ref<RecipeListItemDto[]>([]);
const loading = ref(false);
const keyword = ref('');
const currentPage = ref(1);
const metaData = ref<PagedMetaData>({ totalCount: 0, pageSize: 10, currentPage: 1, totalPages: 1 });
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
  try { allProcesses.value = await getAllProcessesApi(); } catch { allProcesses.value = []; }
  try { allDevices.value = await getAllActiveDevicesApi(); } catch { allDevices.value = []; }
};

const processNameMap = computed(() => {
  const m: Record<string, string> = {};
  for (const p of allProcesses.value) m[p.id] = `${p.processCode} · ${p.processName}`;
  return m;
});
const deviceNameMap = computed(() => {
  const m: Record<string, string> = {};
  for (const d of allDevices.value) m[d.id] = d.deviceName;
  return m;
});

const pageNumbers = computed(() => {
  const total = metaData.value.totalPages;
  const cur = currentPage.value;
  const range: number[] = [];
  for (let i = Math.max(1, cur - 2); i <= Math.min(total, cur + 2); i++) range.push(i);
  return range;
});

let searchTimer: ReturnType<typeof setTimeout> | null = null;
const onSearchInput = () => {
  if (searchTimer) clearTimeout(searchTimer);
  searchTimer = setTimeout(() => { currentPage.value = 1; fetchList(); }, 400);
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

const goPage = (page: number) => { currentPage.value = page; fetchList(); };

// ── 参数工具函数 ──
const generateParamId = () => crypto.randomUUID?.() || Math.random().toString(36).substring(2, 10);

const parseParams = (jsonb: string): RecipeParameter[] => {
  try {
    const arr = JSON.parse(jsonb);
    if (Array.isArray(arr)) return arr.map((p: any) => ({ id: p.id || generateParamId(), name: p.name || '', unit: p.unit || '', min: p.min ?? 0, max: p.max ?? 0 }));
  } catch { }
  return [];
};

const paramsToJsonb = (params: RecipeParameter[]): string => {
  return JSON.stringify(params.map(p => ({ id: p.id, name: p.name, unit: p.unit, min: p.min, max: p.max })));
};

const validateParams = (params: RecipeParameter[]): string | null => {
  if (params.length === 0) {
    return '至少保留一个工艺参数';
  }

  for (let index = 0; index < params.length; index += 1) {
    const param = params[index]!;
    const rowLabel = `第 ${index + 1} 个参数`;

    if (!param.id?.trim()) {
      return `${rowLabel}缺少参数标识，请删除后重新添加`;
    }

    if (!param.name?.trim()) {
      return `${rowLabel}的参数名称不能为空`;
    }

    if (!param.unit?.trim()) {
      return `${rowLabel}的单位不能为空`;
    }

    if (typeof param.min !== 'number' || Number.isNaN(param.min)) {
      return `${rowLabel}的下限必须是数字`;
    }

    if (typeof param.max !== 'number' || Number.isNaN(param.max)) {
      return `${rowLabel}的上限必须是数字`;
    }

    if (param.min > param.max) {
      return `${rowLabel}的下限不能大于上限`;
    }
  }

  return null;
};

const prettyJson = (str: string): string => {
  try { return JSON.stringify(JSON.parse(str), null, 2); } catch { return str; }
};

// ── 新建配方 ──
const showCreateModal = ref(false);
const createParams = ref<RecipeParameter[]>([]);
const createForm = reactive({
  recipeName: '', processId: '', deviceId: '',
});

const addCreateParam = () => {
  createParams.value.push({ id: generateParamId(), name: '', unit: '', min: 0, max: 0 });
};

const openCreateModal = async () => {
  Object.assign(createForm, { recipeName: '', processId: '', deviceId: '' });
  createParams.value = [];
  showCreateModal.value = true;
  await fetchSelectData();
};

const submitCreate = async () => {
  if (!createForm.recipeName.trim() || !createForm.processId || !createForm.deviceId) {
    alert('配方名称、归属工序和归属设备为必填项');
    return;
  }
  const paramValidationError = validateParams(createParams.value);
  if (paramValidationError) { alert(paramValidationError); return; }
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
  } catch { } finally { submitting.value = false; }
};

// ── 升级版本 ──
const showUpgradeModal = ref(false);
const upgradeTarget = ref<RecipeListItemDto | null>(null);
const upgradeParams = ref<RecipeParameter[]>([]);
const upgradeForm = reactive({ newVersion: '' });

const addUpgradeParam = () => {
  upgradeParams.value.push({ id: generateParamId(), name: '', unit: '', min: 0, max: 0 });
};

const openUpgradeModal = async (recipe: RecipeListItemDto) => {
  upgradeTarget.value = recipe;
  upgradeForm.newVersion = '';
  upgradeParams.value = [];
  showUpgradeModal.value = true;
  try {
    const raw = await getRecipeDetailApi(recipe.id);
    const jsonb = raw.parametersJsonb || '';
    upgradeParams.value = parseParams(jsonb);
  } catch (error: unknown) {
    if (isRecipeDetailDto(error)) {
      upgradeParams.value = parseParams(error.parametersJsonb);
    } else {
      upgradeParams.value = [];
    }
  }
};

const submitUpgrade = async () => {
  if (!upgradeTarget.value || !upgradeForm.newVersion.trim()) { alert('版本号不能为空'); return; }
  const paramValidationError = validateParams(upgradeParams.value);
  if (paramValidationError) { alert(paramValidationError); return; }
  submitting.value = true;
  try {
    await upgradeRecipeVersionApi(upgradeTarget.value.id, {
      sourceRecipeId: upgradeTarget.value.id,
      newVersion: upgradeForm.newVersion,
      parametersJsonb: paramsToJsonb(upgradeParams.value),
    });
    showUpgradeModal.value = false;
    fetchList();
  } catch { } finally { submitting.value = false; }
};

// ── 详情侧边栏 ──
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

// ── 物理删除确认 ──
const confirmDialog = reactive({
  show: false, title: '', desc: '', confirmText: '',
  onConfirm: () => {},
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
      } catch { } finally { submitting.value = false; }
    },
  });
};

onMounted(() => { fetchList(); fetchSelectData(); });
</script>

<style scoped>
* { box-sizing: border-box; }
.recipe-list { font-family: 'Noto Sans SC', sans-serif; color: #e0e4ef; }

.page-header { display: flex; align-items: flex-start; justify-content: space-between; margin-bottom: 24px; }
.page-title { font-size: 22px; font-weight: 600; color: #fff; margin: 0 0 4px; letter-spacing: 0.5px; }
.page-sub { font-size: 13px; color: rgba(255,255,255,0.35); margin: 0; }

.toolbar { display: flex; align-items: center; gap: 12px; margin-bottom: 16px; }
.search-wrap { position: relative; display: flex; align-items: center; background: rgba(255,255,255,0.04); border: 1px solid rgba(255,255,255,0.08); border-radius: 4px; padding: 0 12px; gap: 8px; transition: border-color 0.2s; flex: 0 0 300px; }
.search-wrap:focus-within { border-color: rgba(0,229,255,0.3); }
.search-wrap svg { width: 14px; height: 14px; color: rgba(255,255,255,0.25); flex-shrink: 0; }
.search-wrap input { flex: 1; background: none; border: none; outline: none; color: rgba(255,255,255,0.75); font-size: 13px; font-family: 'Noto Sans SC', sans-serif; padding: 9px 0; }
.search-wrap input::placeholder { color: rgba(255,255,255,0.2); }
.clear-btn { background: none; border: none; color: rgba(255,255,255,0.3); cursor: pointer; font-size: 12px; padding: 0; }
.total-badge { font-size: 12px; color: rgba(255,255,255,0.3); background: rgba(255,255,255,0.04); border: 1px solid rgba(255,255,255,0.07); padding: 4px 12px; border-radius: 20px; white-space: nowrap; }

.table-wrap { background: rgba(255,255,255,0.02); border: 1px solid rgba(255,255,255,0.06); border-radius: 4px; overflow: hidden; }
.data-table { width: 100%; border-collapse: collapse; }
.data-table thead tr { background: rgba(255,255,255,0.03); border-bottom: 1px solid rgba(255,255,255,0.06); }
.data-table th { padding: 11px 16px; text-align: left; font-size: 11px; font-weight: 500; color: rgba(255,255,255,0.3); letter-spacing: 1px; text-transform: uppercase; white-space: nowrap; }
.table-row { border-bottom: 1px solid rgba(255,255,255,0.04); cursor: pointer; transition: background 0.15s; }
.table-row:last-child { border-bottom: none; }
.table-row:hover { background: rgba(0,229,255,0.03); }
.data-table td { padding: 13px 16px; font-size: 13px; vertical-align: middle; }

.recipe-name-cell { display: flex; align-items: center; gap: 8px; }
.recipe-name { color: #e0e4ef; font-weight: 500; }
.special-badge { font-size: 10px; background: rgba(255,179,0,0.12); color: #ffb300; border: 1px solid rgba(255,179,0,0.25); padding: 1px 6px; border-radius: 3px; font-weight: 500; }

.version-tag { font-family: 'Courier New', monospace; font-size: 11px; background: rgba(0,229,255,0.08); color: #00e5ff; padding: 2px 7px; border-radius: 3px; border: 1px solid rgba(0,229,255,0.15); }
.type-tag { font-size: 11px; padding: 3px 8px; border-radius: 3px; font-weight: 500; }
.type-tag.universal { background: rgba(0,229,255,0.08); color: rgba(0,229,255,0.7); border: 1px solid rgba(0,229,255,0.15); }
.type-tag.special { background: rgba(255,179,0,0.1); color: #ffb300; border: 1px solid rgba(255,179,0,0.2); }
.process-name-chip { font-size: 12px; color: rgba(255,255,255,0.6); background: rgba(0,229,255,0.06); border: 1px solid rgba(0,229,255,0.1); padding: 2px 8px; border-radius: 3px; }
.device-name-chip { font-size: 12px; color: rgba(255,179,0,0.8); background: rgba(255,179,0,0.06); border: 1px solid rgba(255,179,0,0.12); padding: 2px 8px; border-radius: 3px; }
.no-device { color: rgba(255,255,255,0.15); }

.status-tag { display: inline-flex; align-items: center; gap: 5px; font-size: 11px; font-weight: 500; padding: 3px 9px; border-radius: 20px; }
.status-tag.active { background: rgba(0,229,160,0.12); color: #00e5a0; }
.status-tag.archived { background: rgba(255,179,0,0.1); color: #ffb300; }
.status-dot { width: 5px; height: 5px; border-radius: 50%; }
.status-tag.active .status-dot { background: #00e5a0; box-shadow: 0 0 4px #00e5a0; }
.status-tag.archived .status-dot { background: #ffb300; }

.action-cell { text-align: right; white-space: nowrap; }
.icon-btn { display: inline-flex; align-items: center; justify-content: center; width: 28px; height: 28px; border-radius: 3px; border: none; cursor: pointer; background: rgba(255,255,255,0.04); color: rgba(255,255,255,0.4); transition: all 0.15s; margin-left: 4px; }
.icon-btn svg { width: 13px; height: 13px; }
.icon-btn.edit:hover { background: rgba(0,229,255,0.12); color: #00e5ff; }
.icon-btn.delete:hover { background: rgba(255,77,79,0.12); color: #ff8888; }

.skeleton-rows { padding: 8px 0; }
.skeleton-row { display: flex; gap: 16px; padding: 14px 16px; border-bottom: 1px solid rgba(255,255,255,0.04); align-items: center; }
.skel { background: rgba(255,255,255,0.06); border-radius: 3px; height: 14px; animation: shimmer 1.5s infinite; }
.skel-sm { width: 70px; } .skel-md { width: 120px; } .skel-lg { width: 200px; }
@keyframes shimmer { 0%,100% { opacity:0.5; } 50% { opacity:1; } }

.empty-cell { text-align: center; padding: 48px 0 !important; }
.empty-state { display: flex; flex-direction: column; align-items: center; gap: 12px; }
.empty-state svg { width: 52px; height: 52px; color: rgba(255,255,255,0.2); }
.empty-state p { font-size: 13px; color: rgba(255,255,255,0.25); margin: 0; }

.pagination { display: flex; justify-content: center; gap: 6px; margin-top: 20px; }
.page-btn { width: 32px; height: 32px; border-radius: 3px; border: 1px solid rgba(255,255,255,0.08); background: rgba(255,255,255,0.03); color: rgba(255,255,255,0.45); font-size: 13px; cursor: pointer; display: flex; align-items: center; justify-content: center; transition: all 0.15s; }
.page-btn:hover:not(:disabled) { border-color: rgba(0,229,255,0.3); color: #00e5ff; }
.page-btn.active { background: rgba(0,229,255,0.12); border-color: rgba(0,229,255,0.4); color: #00e5ff; }
.page-btn:disabled { opacity: 0.3; cursor: not-allowed; }
.page-btn svg { width: 12px; height: 12px; }

.btn { display: inline-flex; align-items: center; gap: 6px; padding: 8px 16px; border-radius: 3px; border: none; font-size: 13px; font-family: 'Noto Sans SC', sans-serif; font-weight: 500; cursor: pointer; transition: all 0.18s; white-space: nowrap; }
.btn-primary { background: rgba(0,229,255,0.15); color: #00e5ff; border: 1px solid rgba(0,229,255,0.3); }
.btn-primary:hover:not(:disabled) { background: rgba(0,229,255,0.25); }
.btn-primary:disabled { opacity: 0.4; cursor: not-allowed; }
.btn-primary svg { width: 14px; height: 14px; }
.btn-ghost { background: rgba(255,255,255,0.05); color: rgba(255,255,255,0.55); border: 1px solid rgba(255,255,255,0.1); }
.btn-ghost:hover { background: rgba(255,255,255,0.08); color: rgba(255,255,255,0.75); }
.btn-danger { background: rgba(255,77,79,0.15); color: #ff8888; border: 1px solid rgba(255,77,79,0.3); }
.btn-danger:hover:not(:disabled) { background: rgba(255,77,79,0.25); }
.btn-danger:disabled { opacity: 0.4; cursor: not-allowed; }

.modal-overlay { position: fixed; inset: 0; z-index: 100; background: rgba(0,0,0,0.7); backdrop-filter: blur(4px); display: flex; align-items: center; justify-content: center; }
.modal { background: #0f1525; border: 1px solid rgba(255,255,255,0.08); border-radius: 6px; width: 520px; max-width: 95vw; overflow: hidden; box-shadow: 0 24px 48px rgba(0,0,0,0.6); max-height: 90vh; display: flex; flex-direction: column; }
.modal-lg { width: 720px; }
.modal-header { display: flex; align-items: center; justify-content: space-between; padding: 18px 22px; border-bottom: 1px solid rgba(255,255,255,0.06); flex-shrink: 0; }
.modal-title { font-size: 15px; font-weight: 600; color: #fff; display: block; }
.modal-subtitle { font-size: 12px; color: rgba(255,255,255,0.35); margin-top: 2px; display: block; }
.modal-close { background: none; border: none; color: rgba(255,255,255,0.3); font-size: 16px; cursor: pointer; padding: 0; line-height: 1; align-self: flex-start; }
.modal-close:hover { color: rgba(255,255,255,0.7); }
.modal-body { padding: 22px; display: flex; flex-direction: column; gap: 16px; overflow-y: auto; flex: 1; }
.modal-footer { display: flex; justify-content: flex-end; gap: 10px; padding: 14px 22px; border-top: 1px solid rgba(255,255,255,0.06); flex-shrink: 0; }

.form-row { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }
.form-field { display: flex; flex-direction: column; gap: 6px; }
.form-label { font-size: 12px; color: rgba(255,255,255,0.45); font-weight: 500; display: flex; align-items: center; gap: 6px; }
.required { color: #ff8888; }
.optional { color: rgba(255,255,255,0.2); font-weight: 400; }
.form-input { background: rgba(255,255,255,0.04); border: 1px solid rgba(255,255,255,0.1); border-radius: 3px; padding: 8px 12px; color: rgba(255,255,255,0.8); font-size: 13px; font-family: 'Noto Sans SC', sans-serif; outline: none; transition: border-color 0.2s; }
.form-input:focus { border-color: rgba(0,229,255,0.4); }
.form-input::placeholder { color: rgba(255,255,255,0.2); }
select.form-input { appearance: none; background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' viewBox='0 0 12 12'%3E%3Cpath d='M3 4.5l3 3 3-3' stroke='%2300e5ff' stroke-width='1.2' fill='none' stroke-linecap='round'/%3E%3C/svg%3E"); background-repeat: no-repeat; background-position: right 12px center; padding-right: 32px; cursor: pointer; }
select.form-input option { background: #0f1525; color: #e0e4ef; }
.mono-input { font-family: 'Courier New', monospace; font-size: 12px; }
.form-hint { font-size: 11px; color: rgba(255,255,255,0.2); margin: 0; }
.readonly-field { display: flex; align-items: center; padding: 6px 0; }

/* 结构化参数表单 */
.add-param-btn { margin-left: auto; background: rgba(0,229,255,0.08); border: 1px solid rgba(0,229,255,0.2); color: rgba(0,229,255,0.7); font-size: 11px; padding: 2px 10px; border-radius: 3px; cursor: pointer; font-family: 'Noto Sans SC', sans-serif; transition: all 0.15s; }
.add-param-btn:hover { background: rgba(0,229,255,0.15); color: #00e5ff; }
.params-table { border: 1px solid rgba(255,255,255,0.08); border-radius: 4px; overflow: hidden; }
.params-header { display: grid; grid-template-columns: 2fr 1fr 1fr 1fr 32px; gap: 1px; background: rgba(255,255,255,0.03); padding: 8px 10px; border-bottom: 1px solid rgba(255,255,255,0.06); }
.params-header span { font-size: 11px; color: rgba(255,255,255,0.3); font-weight: 500; letter-spacing: 0.5px; }
.param-row { display: grid; grid-template-columns: 2fr 1fr 1fr 1fr 32px; gap: 6px; padding: 6px 10px; border-bottom: 1px solid rgba(255,255,255,0.04); align-items: center; }
.param-row:last-child { border-bottom: none; }
.param-input { background: rgba(255,255,255,0.04); border: 1px solid rgba(255,255,255,0.08); border-radius: 3px; padding: 6px 8px; color: rgba(255,255,255,0.8); font-size: 12px; font-family: 'Noto Sans SC', sans-serif; outline: none; transition: border-color 0.15s; }
.param-input:focus { border-color: rgba(0,229,255,0.3); }
.param-input::placeholder { color: rgba(255,255,255,0.15); }
.param-input.num { font-family: 'Courier New', monospace; text-align: center; }
.param-del { width: 24px; height: 24px; background: none; border: none; color: rgba(255,255,255,0.2); cursor: pointer; font-size: 12px; display: flex; align-items: center; justify-content: center; border-radius: 3px; transition: all 0.15s; }
.param-del:hover { background: rgba(255,77,79,0.12); color: #ff8888; }
.params-empty { padding: 24px 0; text-align: center; font-size: 12px; color: rgba(255,255,255,0.2); border: 1px dashed rgba(255,255,255,0.08); border-radius: 4px; }

/* 详情侧边栏 */
.detail-overlay { position: fixed; inset: 0; z-index: 100; background: rgba(0,0,0,0.5); display: flex; align-items: stretch; justify-content: flex-end; }
.detail-panel { width: 420px; background: #0f1525; border-left: 1px solid rgba(255,255,255,0.08); display: flex; flex-direction: column; animation: slideIn 0.22s cubic-bezier(0.4,0,0.2,1); }
@keyframes slideIn { from { transform: translateX(100%); } to { transform: translateX(0); } }
.detail-header { display: flex; align-items: center; justify-content: space-between; padding: 18px 22px; border-bottom: 1px solid rgba(255,255,255,0.06); flex-shrink: 0; }
.detail-title { font-size: 15px; font-weight: 600; color: #fff; }
.detail-body { padding: 20px 22px; flex: 1; overflow-y: auto; }
.detail-loading { display: flex; flex-direction: column; align-items: center; gap: 14px; padding-top: 60px; color: rgba(255,255,255,0.3); font-size: 13px; }
.loading-ring { width: 32px; height: 32px; border: 2px solid rgba(0,229,255,0.15); border-top-color: #00e5ff; border-radius: 50%; animation: spin 0.8s linear infinite; }
@keyframes spin { to { transform: rotate(360deg); } }
.detail-status-banner { display: flex; align-items: center; gap: 8px; padding: 10px 14px; border-radius: 4px; font-size: 13px; font-weight: 500; margin-bottom: 20px; }
.detail-status-banner.active { background: rgba(0,229,160,0.1); color: #00e5a0; border: 1px solid rgba(0,229,160,0.2); }
.detail-status-banner.archived { background: rgba(255,179,0,0.08); color: #ffb300; border: 1px solid rgba(255,179,0,0.2); }
.detail-status-banner .status-dot { width: 7px; height: 7px; border-radius: 50%; }
.detail-status-banner.active .status-dot { background: #00e5a0; box-shadow: 0 0 5px #00e5a0; }
.detail-status-banner.archived .status-dot { background: #ffb300; }
.detail-type-badge { margin-left: auto; font-size: 10px; padding: 2px 7px; border-radius: 3px; }
.detail-type-badge.universal { background: rgba(0,229,255,0.1); color: rgba(0,229,255,0.8); }
.detail-type-badge.special { background: rgba(255,179,0,0.12); color: #ffb300; }
.detail-section { display: flex; flex-direction: column; gap: 18px; }
.detail-row { display: flex; flex-direction: column; gap: 5px; }
.detail-label { font-size: 11px; color: rgba(255,255,255,0.3); text-transform: uppercase; letter-spacing: 0.8px; }
.detail-value { font-size: 13px; color: rgba(255,255,255,0.8); word-break: break-all; }
.json-preview-wrap { background: #080c18; border: 1px solid rgba(0,229,255,0.12); border-radius: 4px; overflow: auto; max-height: 320px; }
.json-preview { margin: 0; padding: 12px 14px; font-family: 'Courier New', monospace; font-size: 11px; line-height: 1.6; color: #a8d8ea; white-space: pre; }

/* 详情参数列表 */
.detail-params-list { display: flex; flex-direction: column; gap: 6px; }
.detail-param-item { display: flex; align-items: center; justify-content: space-between; padding: 8px 12px; background: rgba(255,255,255,0.03); border: 1px solid rgba(255,255,255,0.06); border-radius: 4px; }
.dp-name { font-size: 13px; color: rgba(255,255,255,0.8); font-weight: 500; }
.dp-range { font-size: 12px; color: rgba(0,229,255,0.7); font-family: 'Courier New', monospace; }

/* 确认框 */
.confirm-box { background: #0f1525; border: 1px solid rgba(255,255,255,0.08); border-radius: 6px; padding: 28px 28px 22px; width: 400px; max-width: 90vw; text-align: center; box-shadow: 0 24px 48px rgba(0,0,0,0.6); }
.confirm-icon { width: 44px; height: 44px; border-radius: 50%; display: flex; align-items: center; justify-content: center; margin: 0 auto 14px; }
.confirm-icon.danger { background: rgba(255,77,79,0.1); color: #ff8888; }
.confirm-icon svg { width: 22px; height: 22px; }
.confirm-title { font-size: 15px; font-weight: 600; color: #fff; margin-bottom: 8px; }
.confirm-desc { font-size: 13px; color: rgba(255,255,255,0.4); line-height: 1.6; margin-bottom: 22px; }
.confirm-actions { display: flex; gap: 10px; justify-content: center; }
</style>
