<template>
  <div class="recipe-list">
    <!-- 页头 -->
    <div class="page-header">
      <div>
        <h1 class="page-title">配方管理</h1>
        <p class="page-sub">管理生产工序的通用配方与机台专属特调配方</p>
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
            <th>配方名称</th><th>版本</th><th>类型</th><th>状态</th><th>工序 ID</th>
            <th style="text-align:right">操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-if="recipes.length === 0">
            <td colspan="6" class="empty-cell">
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
                <span v-if="recipe.deviceId" class="special-badge">特调</span>
              </div>
            </td>
            <td><span class="version-tag">{{ recipe.version }}</span></td>
            <td>
              <span class="type-tag" :class="recipe.deviceId ? 'special' : 'universal'">
                {{ recipe.deviceId ? '机台专属' : '工序通用' }}
              </span>
            </td>
            <td>
              <span class="status-tag" :class="recipe.isActive ? 'active' : 'inactive'">
                <span class="status-dot"></span>{{ recipe.isActive ? '启用' : '已停用' }}
              </span>
            </td>
            <td><span class="id-chip">{{ recipe.processId.substring(0, 8) }}…</span></td>
            <td class="action-cell" @click.stop>
              <button class="icon-btn edit" title="编辑参数" v-permission="'Recipe.Create'" @click="openEditModal(recipe)">
                <svg viewBox="0 0 16 16" fill="none"><path d="M11.5 2.5l2 2-8 8H3.5v-2l8-8z" stroke="currentColor" stroke-width="1.2" stroke-linejoin="round"/></svg>
              </button>
              <button v-if="recipe.isActive" class="icon-btn deactivate" title="停用配方" v-permission="'Recipe.Create'" @click="handleDeactivate(recipe)">
                <svg viewBox="0 0 16 16" fill="none"><circle cx="8" cy="8" r="5.5" stroke="currentColor" stroke-width="1.2"/><path d="M5.5 8h5" stroke="currentColor" stroke-width="1.2" stroke-linecap="round"/></svg>
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

    <!-- 新建配方弹窗 -->
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
                <input class="form-input" v-model="createForm.RecipeName" placeholder="如：A型号冬季配方" />
              </div>
              <div class="form-field">
                <label class="form-label">归属工序 ID <span class="required">*</span></label>
                <input class="form-input mono-input" v-model="createForm.ProcessId" placeholder="工序 UUID" />
              </div>
            </div>
            <div class="form-field">
              <label class="form-label">专属机台 ID <span class="optional">（不填 = 工序通用配方）</span></label>
              <input class="form-input mono-input" v-model="createForm.DeviceId" placeholder="留空则为通用配方，填入机台 UUID 则为特调配方" />
            </div>
            <div class="form-field">
              <label class="form-label">
                工艺参数 JSONB <span class="required">*</span>
                <span class="json-status" :class="jsonValid ? 'ok' : 'err'">
                  {{ jsonValid ? '✓ JSON 合法' : '✗ JSON 格式错误' }}
                </span>
              </label>
              <div class="json-editor-wrap">
                <textarea
                  class="json-editor"
                  v-model="createForm.ParametersJsonb"
                  @input="validateJson"
                  spellcheck="false"
                  placeholder='{"temperature": 85, "pressure": 1.2, "speed": 300}'
                  rows="10"
                ></textarea>
                <button class="format-btn" @click="formatJson('create')" title="格式化 JSON">
                  <svg viewBox="0 0 16 16" fill="none"><path d="M2 4h12M4 8h8M6 12h4" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/></svg>
                  格式化
                </button>
              </div>
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn btn-ghost" @click="showCreateModal=false">取消</button>
            <button class="btn btn-primary" :disabled="submitting || !jsonValid" @click="submitCreate">
              {{ submitting ? '创建中...' : '创建配方' }}
            </button>
          </div>
        </div>
      </div>
    </Teleport>

    <!-- 编辑参数弹窗（含版本升级） -->
    <Teleport to="body">
      <div v-if="showEditModal" class="modal-overlay" @click.self="showEditModal=false">
        <div class="modal modal-lg">
          <div class="modal-header">
            <div>
              <span class="modal-title">更新工艺参数</span>
              <span class="modal-subtitle">{{ editTarget?.recipeName }} · 当前版本 {{ editTarget?.version }}</span>
            </div>
            <button class="modal-close" @click="showEditModal=false">✕</button>
          </div>
          <div class="modal-body">
            <div class="form-row">
              <div class="form-field">
                <label class="form-label">新版本号 <span class="required">*</span></label>
                <input class="form-input mono-input" v-model="editForm.Version" placeholder="如：V2.0" />
                <p class="form-hint">版本号必须与同名配方中现有版本不重复</p>
              </div>
              <div class="form-field">
                <label class="form-label">配方类型</label>
                <div class="readonly-field">
                  <span class="type-tag" :class="editTarget?.deviceId ? 'special' : 'universal'">
                    {{ editTarget?.deviceId ? '机台专属特调' : '工序通用配方' }}
                  </span>
                </div>
              </div>
            </div>
            <div class="form-field">
              <label class="form-label">
                工艺参数 JSONB <span class="required">*</span>
                <span class="json-status" :class="editJsonValid ? 'ok' : 'err'">
                  {{ editJsonValid ? '✓ JSON 合法' : '✗ JSON 格式错误' }}
                </span>
              </label>
              <div class="json-editor-wrap">
                <textarea
                  class="json-editor"
                  v-model="editForm.ParametersJsonb"
                  @input="validateEditJson"
                  spellcheck="false"
                  rows="12"
                ></textarea>
                <button class="format-btn" @click="formatJson('edit')" title="格式化 JSON">
                  <svg viewBox="0 0 16 16" fill="none"><path d="M2 4h12M4 8h8M6 12h4" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/></svg>
                  格式化
                </button>
              </div>
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn btn-ghost" @click="showEditModal=false">取消</button>
            <button class="btn btn-primary" :disabled="submitting || !editJsonValid" @click="submitEdit">
              {{ submitting ? '保存中...' : '保存并升版' }}
            </button>
          </div>
        </div>
      </div>
    </Teleport>

    <!-- 详情侧边栏（含 JSONB 预览） -->
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
            <div class="detail-status-banner" :class="detailData.isActive ? 'active' : 'inactive'">
              <span class="status-dot"></span>
              {{ detailData.isActive ? '配方启用中' : '配方已停用' }}
              <span class="detail-type-badge" :class="detailData.deviceId ? 'special' : 'universal'">
                {{ detailData.deviceId ? '特调' : '通用' }}
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
                <span class="detail-label">归属工序 ID</span>
                <span class="detail-value mono small">{{ detailData.processId }}</span>
              </div>
              <div v-if="detailData.deviceId" class="detail-row">
                <span class="detail-label">专属机台 ID</span>
                <span class="detail-value mono small">{{ detailData.deviceId }}</span>
              </div>
              <div class="detail-row">
                <span class="detail-label">工艺参数 JSONB</span>
                <div class="json-preview-wrap">
                  <pre class="json-preview">{{ prettyJson(detailData.parametersJsonb) }}</pre>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </Teleport>

    <!-- 确认停用对话框 -->
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
  updateRecipeParametersApi, deactivateRecipeApi,
  type RecipeListItemDto, type RecipeDetailDto, type PagedMetaData,
} from '../../api/recipe';

const recipes = ref<RecipeListItemDto[]>([]);
const loading = ref(false);
const keyword = ref('');
const currentPage = ref(1);
const metaData = ref<PagedMetaData>({ totalCount: 0, pageSize: 10, currentPage: 1, totalPages: 1 });
const submitting = ref(false);

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
    const raw = await getRecipePagedListApi({
      pagination: { PageNumber: currentPage.value, PageSize: 10 },
      keyword: keyword.value || undefined,
    }) as unknown as Record<string, unknown>;

    if (raw && raw.metaData) {
      metaData.value = raw.metaData as PagedMetaData;
      const items: RecipeListItemDto[] = [];
      for (const k of Object.keys(raw)) {
        if (!isNaN(Number(k))) items.push(raw[k] as RecipeListItemDto);
      }
      recipes.value = items;
    } else if (Array.isArray(raw)) {
      recipes.value = raw as RecipeListItemDto[];
    }
  } catch {
    recipes.value = [];
  } finally {
    loading.value = false;
  }
};

const goPage = (page: number) => { currentPage.value = page; fetchList(); };

// ── JSON 工具 ──
const isValidJson = (str: string): boolean => {
  if (!str.trim()) return false;
  try { JSON.parse(str); return true; } catch { return false; }
};

const prettyJson = (str: string): string => {
  try { return JSON.stringify(JSON.parse(str), null, 2); } catch { return str; }
};

const formatJson = (target: 'create' | 'edit') => {
  if (target === 'create') {
    try { createForm.ParametersJsonb = JSON.stringify(JSON.parse(createForm.ParametersJsonb), null, 2); jsonValid.value = true; } catch { }
  } else {
    try { editForm.ParametersJsonb = JSON.stringify(JSON.parse(editForm.ParametersJsonb), null, 2); editJsonValid.value = true; } catch { }
  }
};

// ── 新建配方 ──
const showCreateModal = ref(false);
const jsonValid = ref(true);
const createForm = reactive({
  RecipeName: '', ProcessId: '', DeviceId: '', ParametersJsonb: '{\n  \n}',
});

const validateJson = () => { jsonValid.value = isValidJson(createForm.ParametersJsonb); };

const openCreateModal = () => {
  Object.assign(createForm, { RecipeName: '', ProcessId: '', DeviceId: '', ParametersJsonb: '{\n  \n}' });
  jsonValid.value = true;
  showCreateModal.value = true;
};

const submitCreate = async () => {
  if (!createForm.RecipeName.trim() || !createForm.ProcessId.trim()) { alert('配方名称和工序 ID 为必填项'); return; }
  if (!isValidJson(createForm.ParametersJsonb)) { alert('工艺参数 JSON 格式错误，请检查'); return; }
  submitting.value = true;
  try {
    await createRecipeApi({
      RecipeName: createForm.RecipeName,
      ProcessId: createForm.ProcessId,
      DeviceId: createForm.DeviceId.trim() || null,
      ParametersJsonb: createForm.ParametersJsonb,
    });
    showCreateModal.value = false;
    fetchList();
  } catch { } finally { submitting.value = false; }
};

// ── 编辑参数（升版） ──
const showEditModal = ref(false);
const editTarget = ref<RecipeListItemDto | null>(null);
const editJsonValid = ref(true);
const editForm = reactive({ ParametersJsonb: '', Version: '' });

const validateEditJson = () => { editJsonValid.value = isValidJson(editForm.ParametersJsonb); };

const openEditModal = async (recipe: RecipeListItemDto) => {
  editTarget.value = recipe;
  editForm.Version = '';
  editForm.ParametersJsonb = '';
  showEditModal.value = true;
  // 拉取含 JSONB 的完整详情
  try {
    const detail = await getRecipeDetailApi(recipe.id) as unknown as RecipeDetailDto;
    editForm.ParametersJsonb = prettyJson(detail.parametersJsonb);
    editJsonValid.value = true;
  } catch {
    editForm.ParametersJsonb = '{}';
  }
};

const submitEdit = async () => {
  if (!editTarget.value || !editForm.Version.trim()) { alert('版本号不能为空'); return; }
  if (!isValidJson(editForm.ParametersJsonb)) { alert('工艺参数 JSON 格式错误，请检查'); return; }
  submitting.value = true;
  try {
    await updateRecipeParametersApi(editTarget.value.id, {
      ParametersJsonb: editForm.ParametersJsonb,
      Version: editForm.Version,
    });
    showEditModal.value = false;
    fetchList();
  } catch { } finally { submitting.value = false; }
};

// ── 详情侧边栏 ──
const showDetailPanel = ref(false);
const detailData = ref<RecipeDetailDto | null>(null);
const detailLoading = ref(false);

const openDetailPanel = async (recipe: RecipeListItemDto) => {
  showDetailPanel.value = true;
  detailLoading.value = true;
  detailData.value = null;
  try {
    detailData.value = await getRecipeDetailApi(recipe.id) as unknown as RecipeDetailDto;
  } catch {
    showDetailPanel.value = false;
  } finally {
    detailLoading.value = false;
  }
};

// ── 停用确认 ──
const confirmDialog = reactive({
  show: false, title: '', desc: '', confirmText: '',
  onConfirm: () => {},
});

const handleDeactivate = (recipe: RecipeListItemDto) => {
  Object.assign(confirmDialog, {
    show: true,
    title: '确认停用配方',
    desc: `配方【${recipe.recipeName} · ${recipe.version}】停用后将从生产终端下发列表中移除，请确认。`,
    confirmText: '停用',
    onConfirm: async () => {
      submitting.value = true;
      try {
        await deactivateRecipeApi(recipe.id);
        confirmDialog.show = false;
        fetchList();
      } catch { } finally { submitting.value = false; }
    },
  });
};

onMounted(() => fetchList());
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
.id-chip { font-family: 'Courier New', monospace; font-size: 11px; color: rgba(255,255,255,0.3); background: rgba(255,255,255,0.04); padding: 2px 6px; border-radius: 3px; }

.status-tag { display: inline-flex; align-items: center; gap: 5px; font-size: 11px; font-weight: 500; padding: 3px 9px; border-radius: 20px; }
.status-tag.active { background: rgba(0,229,160,0.12); color: #00e5a0; }
.status-tag.inactive { background: rgba(255,107,107,0.1); color: #ff8888; }
.status-dot { width: 5px; height: 5px; border-radius: 50%; }
.status-tag.active .status-dot { background: #00e5a0; box-shadow: 0 0 4px #00e5a0; }
.status-tag.inactive .status-dot { background: #ff8888; }

.action-cell { text-align: right; white-space: nowrap; }
.icon-btn { display: inline-flex; align-items: center; justify-content: center; width: 28px; height: 28px; border-radius: 3px; border: none; cursor: pointer; background: rgba(255,255,255,0.04); color: rgba(255,255,255,0.4); transition: all 0.15s; margin-left: 4px; }
.icon-btn svg { width: 13px; height: 13px; }
.icon-btn.edit:hover { background: rgba(0,229,255,0.12); color: #00e5ff; }
.icon-btn.deactivate:hover { background: rgba(255,107,107,0.12); color: #ff8888; }

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
.modal-lg { width: 680px; }
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
.mono-input { font-family: 'Courier New', monospace; font-size: 12px; }
.form-hint { font-size: 11px; color: rgba(255,255,255,0.2); margin: 0; }
.readonly-field { display: flex; align-items: center; padding: 6px 0; }

/* JSONB 编辑器 */
.json-status { font-size: 11px; font-weight: 400; margin-left: auto; }
.json-status.ok { color: #00e5a0; }
.json-status.err { color: #ff8888; }
.json-editor-wrap { position: relative; }
.json-editor {
  width: 100%; background: #080c18;
  border: 1px solid rgba(0,229,255,0.15); border-radius: 4px;
  padding: 12px 14px; color: #a8d8ea;
  font-family: 'Courier New', Courier, monospace; font-size: 12px; line-height: 1.6;
  outline: none; resize: vertical; transition: border-color 0.2s;
  min-height: 120px;
}
.json-editor:focus { border-color: rgba(0,229,255,0.35); box-shadow: inset 0 0 0 1px rgba(0,229,255,0.08); }
.format-btn {
  position: absolute; bottom: 10px; right: 10px;
  display: inline-flex; align-items: center; gap: 4px;
  background: rgba(0,229,255,0.08); border: 1px solid rgba(0,229,255,0.2);
  color: rgba(0,229,255,0.7); font-size: 11px; padding: 3px 8px;
  border-radius: 3px; cursor: pointer; transition: all 0.15s;
  font-family: 'Noto Sans SC', sans-serif;
}
.format-btn:hover { background: rgba(0,229,255,0.15); color: #00e5ff; }
.format-btn svg { width: 12px; height: 12px; }

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
.detail-status-banner.inactive { background: rgba(255,107,107,0.08); color: #ff8888; border: 1px solid rgba(255,107,107,0.2); }
.detail-status-banner .status-dot { width: 7px; height: 7px; border-radius: 50%; }
.detail-status-banner.active .status-dot { background: #00e5a0; box-shadow: 0 0 5px #00e5a0; }
.detail-status-banner.inactive .status-dot { background: #ff8888; }
.detail-type-badge { margin-left: auto; font-size: 10px; padding: 2px 7px; border-radius: 3px; }
.detail-type-badge.universal { background: rgba(0,229,255,0.1); color: rgba(0,229,255,0.8); }
.detail-type-badge.special { background: rgba(255,179,0,0.12); color: #ffb300; }
.detail-section { display: flex; flex-direction: column; gap: 18px; }
.detail-row { display: flex; flex-direction: column; gap: 5px; }
.detail-label { font-size: 11px; color: rgba(255,255,255,0.3); text-transform: uppercase; letter-spacing: 0.8px; }
.detail-value { font-size: 13px; color: rgba(255,255,255,0.8); word-break: break-all; }
.detail-value.mono { font-family: 'Courier New', monospace; color: #00e5ff; font-size: 12px; }
.detail-value.small { font-size: 11px; color: rgba(255,255,255,0.45); }
.json-preview-wrap { background: #080c18; border: 1px solid rgba(0,229,255,0.12); border-radius: 4px; overflow: auto; max-height: 320px; }
.json-preview { margin: 0; padding: 12px 14px; font-family: 'Courier New', monospace; font-size: 11px; line-height: 1.6; color: #a8d8ea; white-space: pre; }

/* 确认框 */
.confirm-box { background: #0f1525; border: 1px solid rgba(255,255,255,0.08); border-radius: 6px; padding: 28px 28px 22px; width: 400px; max-width: 90vw; text-align: center; box-shadow: 0 24px 48px rgba(0,0,0,0.6); }
.confirm-icon { width: 44px; height: 44px; border-radius: 50%; display: flex; align-items: center; justify-content: center; margin: 0 auto 14px; }
.confirm-icon.danger { background: rgba(255,77,79,0.1); color: #ff8888; }
.confirm-icon svg { width: 22px; height: 22px; }
.confirm-title { font-size: 15px; font-weight: 600; color: #fff; margin-bottom: 8px; }
.confirm-desc { font-size: 13px; color: rgba(255,255,255,0.4); line-height: 1.6; margin-bottom: 22px; }
.confirm-actions { display: flex; gap: 10px; justify-content: center; }
</style>
