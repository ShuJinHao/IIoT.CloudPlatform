<template>
  <div class="process-list">
    <!-- 页头 -->
    <div class="page-header">
      <div>
        <h1 class="page-title">工序管理</h1>
        <p class="page-sub">定义与维护车间制造工序，作为设备、配方、员工权限的核心锚点</p>
      </div>
      <button class="btn btn-primary" v-permission="'Process.Create'" @click="openCreateModal">
        <svg viewBox="0 0 16 16" fill="none"><path d="M8 2v12M2 8h12" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/></svg>
        新建工序
      </button>
    </div>

    <!-- 搜索栏 -->
    <div class="toolbar">
      <div class="search-wrap">
        <svg viewBox="0 0 16 16" fill="none"><circle cx="6.5" cy="6.5" r="4.5" stroke="currentColor" stroke-width="1.3"/><path d="M10 10l3 3" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/></svg>
        <input v-model="keyword" placeholder="搜索工序编码或名称..." @keyup.enter="fetchList" @input="onSearchInput" />
        <button v-if="keyword" class="clear-btn" @click="keyword=''; fetchList()">✕</button>
      </div>
      <span class="total-badge">共 {{ metaData.totalCount }} 条</span>
    </div>

    <!-- 表格 -->
    <div class="table-wrap">
      <div v-if="loading" class="skeleton-rows">
        <div v-for="i in 5" :key="i" class="skeleton-row">
          <div class="skel skel-md"></div><div class="skel skel-lg"></div><div class="skel skel-sm"></div>
        </div>
      </div>
      <table v-else class="data-table">
        <thead>
          <tr>
            <th>工序编码</th><th>工序名称</th><th>工序 ID</th>
            <th style="text-align:right">操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-if="processes.length === 0">
            <td colspan="4" class="empty-cell">
              <div class="empty-state">
                <svg viewBox="0 0 48 48" fill="none"><rect x="6" y="10" width="36" height="28" rx="3" stroke="currentColor" stroke-width="1.5" opacity="0.3"/><path d="M14 22h20M14 30h12" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" opacity="0.3"/></svg>
                <p>暂无工序数据</p>
              </div>
            </td>
          </tr>
          <tr v-for="p in processes" :key="p.id" class="table-row">
            <td><span class="process-code">{{ p.processCode }}</span></td>
            <td><span class="process-name">{{ p.processName }}</span></td>
            <td><span class="id-chip">{{ p.id.substring(0, 8) }}…</span></td>
            <td class="action-cell">
              <button class="icon-btn edit" title="编辑工序" v-permission="'Process.Update'" @click="openEditModal(p)">
                <svg viewBox="0 0 16 16" fill="none"><path d="M11.5 2.5l2 2-8 8H3.5v-2l8-8z" stroke="currentColor" stroke-width="1.2" stroke-linejoin="round"/></svg>
              </button>
              <button class="icon-btn deactivate" title="删除工序" v-permission="'Process.Delete'" @click="handleDelete(p)">
                <svg viewBox="0 0 16 16" fill="none"><path d="M3 3l10 10M13 3L3 13" stroke="currentColor" stroke-width="1.2" stroke-linecap="round"/></svg>
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
      <button v-for="pg in pageNumbers" :key="pg" class="page-btn" :class="{active:pg===currentPage}" @click="goPage(pg)">{{ pg }}</button>
      <button class="page-btn" :disabled="currentPage===metaData.totalPages" @click="goPage(currentPage+1)">
        <svg viewBox="0 0 12 12" fill="none"><path d="M4 2l4 4-4 4" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/></svg>
      </button>
    </div>

    <!-- 新建工序弹窗 -->
    <Teleport to="body">
      <div v-if="showCreateModal" class="modal-overlay" @click.self="showCreateModal=false">
        <div class="modal">
          <div class="modal-header">
            <span class="modal-title">新建制造工序</span>
            <button class="modal-close" @click="showCreateModal=false">✕</button>
          </div>
          <div class="modal-body">
            <div class="form-field">
              <label class="form-label">工序编码 <span class="required">*</span></label>
              <input class="form-input mono-input" v-model="createForm.processCode" placeholder="如：Stacking、Injection" />
              <p class="form-hint">编码全局唯一，建议使用英文标识符</p>
            </div>
            <div class="form-field">
              <label class="form-label">工序名称 <span class="required">*</span></label>
              <input class="form-input" v-model="createForm.processName" placeholder="如：叠片工序、注液工序" />
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn btn-ghost" @click="showCreateModal=false">取消</button>
            <button class="btn btn-primary" :disabled="submitting" @click="submitCreate">
              {{ submitting ? '创建中...' : '确认创建' }}
            </button>
          </div>
        </div>
      </div>
    </Teleport>

    <!-- 编辑工序弹窗 -->
    <Teleport to="body">
      <div v-if="showEditModal" class="modal-overlay" @click.self="showEditModal=false">
        <div class="modal">
          <div class="modal-header">
            <span class="modal-title">编辑工序</span>
            <button class="modal-close" @click="showEditModal=false">✕</button>
          </div>
          <div class="modal-body">
            <div class="form-field">
              <label class="form-label">工序编码 <span class="required">*</span></label>
              <input class="form-input mono-input" v-model="editForm.processCode" />
            </div>
            <div class="form-field">
              <label class="form-label">工序名称 <span class="required">*</span></label>
              <input class="form-input" v-model="editForm.processName" />
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn btn-ghost" @click="showEditModal=false">取消</button>
            <button class="btn btn-primary" :disabled="submitting" @click="submitEdit">
              {{ submitting ? '保存中...' : '保存修改' }}
            </button>
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
  getProcessPagedListApi, createProcessApi, updateProcessApi, deleteProcessApi,
  type ProcessListItemDto, type PagedMetaData,
} from '../../api/masterData/processes';

const processes = ref<ProcessListItemDto[]>([]);
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
    const raw = await getProcessPagedListApi({
      pagination: { PageNumber: currentPage.value, PageSize: 10 },
      keyword: keyword.value || undefined,
    }) as unknown as Record<string, unknown>;

    if (raw && raw.metaData) {
      metaData.value = raw.metaData as PagedMetaData;
      processes.value = Array.isArray(raw.items) ? raw.items as ProcessListItemDto[] : [];
    } else if (Array.isArray(raw)) {
      processes.value = raw as ProcessListItemDto[];
    }
  } catch {
    processes.value = [];
  } finally {
    loading.value = false;
  }
};

const goPage = (page: number) => { currentPage.value = page; fetchList(); };

// ── 新建弹窗 ──
const showCreateModal = ref(false);
const createForm = reactive({ processCode: '', processName: '' });

const openCreateModal = () => {
  Object.assign(createForm, { processCode: '', processName: '' });
  showCreateModal.value = true;
};

const submitCreate = async () => {
  if (!createForm.processCode.trim() || !createForm.processName.trim()) {
    alert('编码和名称均为必填项'); return;
  }
  submitting.value = true;
  try {
    await createProcessApi({ ...createForm });
    showCreateModal.value = false;
    fetchList();
  } catch { } finally { submitting.value = false; }
};

// ── 编辑弹窗 ──
const showEditModal = ref(false);
const editTarget = ref<ProcessListItemDto | null>(null);
const editForm = reactive({ processCode: '', processName: '' });

const openEditModal = (p: ProcessListItemDto) => {
  editTarget.value = p;
  editForm.processCode = p.processCode;
  editForm.processName = p.processName;
  showEditModal.value = true;
};

const submitEdit = async () => {
  if (!editTarget.value || !editForm.processCode.trim() || !editForm.processName.trim()) {
    alert('编码和名称不能为空'); return;
  }
  submitting.value = true;
  try {
    await updateProcessApi(editTarget.value.id, {
      processId: editTarget.value.id,
      processCode: editForm.processCode,
      processName: editForm.processName,
    });
    showEditModal.value = false;
    fetchList();
  } catch { } finally { submitting.value = false; }
};

// ── 删除确认 ──
const confirmDialog = reactive({
  show: false, title: '', desc: '', confirmText: '',
  onConfirm: () => {},
});

const handleDelete = (p: ProcessListItemDto) => {
  Object.assign(confirmDialog, {
    show: true,
    title: '确认删除工序',
    desc: `工序【${p.processName}（${p.processCode}）】删除后不可恢复。若该工序下仍有设备或配方挂载，删除将被拒绝。`,
    confirmText: '删除',
    onConfirm: async () => {
      submitting.value = true;
      try {
        await deleteProcessApi(p.id);
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
.process-list { font-family: 'Noto Sans SC', sans-serif; color: #e0e4ef; }
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
.clear-btn:hover { color: rgba(255,255,255,0.6); }
.total-badge { font-size: 12px; color: rgba(255,255,255,0.3); background: rgba(255,255,255,0.04); border: 1px solid rgba(255,255,255,0.07); padding: 4px 12px; border-radius: 20px; white-space: nowrap; }
.table-wrap { background: rgba(255,255,255,0.02); border: 1px solid rgba(255,255,255,0.06); border-radius: 4px; overflow: hidden; }
.data-table { width: 100%; border-collapse: collapse; }
.data-table thead tr { background: rgba(255,255,255,0.03); border-bottom: 1px solid rgba(255,255,255,0.06); }
.data-table th { padding: 11px 16px; text-align: left; font-size: 11px; font-weight: 500; color: rgba(255,255,255,0.3); letter-spacing: 1px; text-transform: uppercase; white-space: nowrap; }
.table-row { border-bottom: 1px solid rgba(255,255,255,0.04); transition: background 0.15s; }
.table-row:last-child { border-bottom: none; }
.table-row:hover { background: rgba(0,229,255,0.03); }
.data-table td { padding: 13px 16px; font-size: 13px; vertical-align: middle; }
.process-code { font-family: 'Courier New', monospace; font-size: 12px; color: #00e5ff; background: rgba(0,229,255,0.08); padding: 2px 7px; border-radius: 3px; }
.process-name { color: #e0e4ef; font-weight: 500; }
.id-chip { font-family: 'Courier New', monospace; font-size: 11px; color: rgba(255,255,255,0.35); background: rgba(255,255,255,0.04); padding: 2px 6px; border-radius: 3px; }
.action-cell { text-align: right; white-space: nowrap; }
.icon-btn { display: inline-flex; align-items: center; justify-content: center; width: 28px; height: 28px; border-radius: 3px; border: none; cursor: pointer; background: rgba(255,255,255,0.04); color: rgba(255,255,255,0.4); transition: all 0.15s; margin-left: 4px; }
.icon-btn svg { width: 13px; height: 13px; }
.icon-btn.edit:hover { background: rgba(0,229,255,0.12); color: #00e5ff; }
.icon-btn.deactivate:hover { background: rgba(255,107,107,0.12); color: #ff8888; }
.skeleton-rows { padding: 8px 0; }
.skeleton-row { display: flex; gap: 16px; padding: 14px 16px; border-bottom: 1px solid rgba(255,255,255,0.04); align-items: center; }
.skel { background: rgba(255,255,255,0.06); border-radius: 3px; height: 14px; animation: shimmer 1.5s infinite; }
.skel-sm { width: 80px; } .skel-md { width: 140px; } .skel-lg { width: 220px; }
@keyframes shimmer { 0%,100% { opacity:0.5; } 50% { opacity:1; } }
.empty-cell { text-align: center; padding: 48px 0 !important; }
.empty-state { display: flex; flex-direction: column; align-items: center; gap: 12px; }
.empty-state svg { width: 48px; height: 48px; color: rgba(255,255,255,0.2); }
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
.modal { background: #0f1525; border: 1px solid rgba(255,255,255,0.08); border-radius: 6px; width: 480px; max-width: 90vw; overflow: hidden; box-shadow: 0 24px 48px rgba(0,0,0,0.6); }
.modal-header { display: flex; align-items: center; justify-content: space-between; padding: 18px 22px; border-bottom: 1px solid rgba(255,255,255,0.06); }
.modal-title { font-size: 15px; font-weight: 600; color: #fff; }
.modal-close { background: none; border: none; color: rgba(255,255,255,0.3); font-size: 16px; cursor: pointer; padding: 0; line-height: 1; }
.modal-close:hover { color: rgba(255,255,255,0.7); }
.modal-body { padding: 22px; display: flex; flex-direction: column; gap: 16px; }
.modal-footer { display: flex; justify-content: flex-end; gap: 10px; padding: 14px 22px; border-top: 1px solid rgba(255,255,255,0.06); }
.form-field { display: flex; flex-direction: column; gap: 6px; }
.form-label { font-size: 12px; color: rgba(255,255,255,0.45); font-weight: 500; }
.required { color: #ff8888; }
.form-input { background: rgba(255,255,255,0.04); border: 1px solid rgba(255,255,255,0.1); border-radius: 3px; padding: 8px 12px; color: rgba(255,255,255,0.8); font-size: 13px; font-family: 'Noto Sans SC', sans-serif; outline: none; transition: border-color 0.2s; }
.form-input:focus { border-color: rgba(0,229,255,0.4); }
.form-input::placeholder { color: rgba(255,255,255,0.2); }
.mono-input { font-family: 'Courier New', monospace; font-size: 12px; }
.form-hint { font-size: 11px; color: rgba(255,255,255,0.2); margin: 0; }
.confirm-box { background: #0f1525; border: 1px solid rgba(255,255,255,0.08); border-radius: 6px; padding: 28px 28px 22px; width: 360px; max-width: 90vw; text-align: center; box-shadow: 0 24px 48px rgba(0,0,0,0.6); }
.confirm-icon { width: 44px; height: 44px; border-radius: 50%; display: flex; align-items: center; justify-content: center; margin: 0 auto 14px; }
.confirm-icon.danger { background: rgba(255,77,79,0.1); color: #ff8888; }
.confirm-icon svg { width: 22px; height: 22px; }
.confirm-title { font-size: 15px; font-weight: 600; color: #fff; margin-bottom: 8px; }
.confirm-desc { font-size: 13px; color: rgba(255,255,255,0.4); line-height: 1.6; margin-bottom: 22px; }
.confirm-actions { display: flex; gap: 10px; justify-content: center; }
</style>
