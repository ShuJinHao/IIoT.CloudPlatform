<template>
  <div class="device-list">
    <!-- 页头 -->
    <div class="page-header">
      <div>
        <h1 class="page-title">设备台账</h1>
        <p class="page-sub">管理车间物理机台的注册档案与运行状态</p>
      </div>
      <button class="btn btn-primary" v-permission="'Device.Create'" @click="openRegisterModal">
        <svg viewBox="0 0 16 16" fill="none"><path d="M8 2v12M2 8h12" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/></svg>
        注册设备
      </button>
    </div>

    <!-- 搜索栏 -->
    <div class="toolbar">
      <div class="search-wrap">
        <svg viewBox="0 0 16 16" fill="none"><circle cx="6.5" cy="6.5" r="4.5" stroke="currentColor" stroke-width="1.3"/><path d="M10 10l3 3" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/></svg>
        <input v-model="keyword" placeholder="搜索设备名称或编号..." @keyup.enter="fetchList" @input="onSearchInput" />
        <button v-if="keyword" class="clear-btn" @click="keyword=''; fetchList()">✕</button>
      </div>
      <span class="total-badge">共 {{ metaData.totalCount }} 台</span>
    </div>

    <!-- 表格 -->
    <div class="table-wrap">
      <div v-if="loading" class="skeleton-rows">
        <div v-for="i in 5" :key="i" class="skeleton-row">
          <div class="skel skel-md"></div><div class="skel skel-sm"></div>
          <div class="skel skel-sm"></div><div class="skel skel-lg"></div><div class="skel skel-sm"></div>
        </div>
      </div>
      <table v-else class="data-table">
        <thead>
          <tr>
            <th>设备名称</th><th>设备编号</th><th>状态</th><th>所属工序 ID</th>
            <th style="text-align:right">操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-if="devices.length === 0">
            <td colspan="5" class="empty-cell">
              <div class="empty-state">
                <svg viewBox="0 0 48 48" fill="none"><rect x="4" y="10" width="40" height="28" rx="3" stroke="currentColor" stroke-width="1.5" opacity="0.3"/><path d="M14 24h20M24 18v12" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" opacity="0.3"/></svg>
                <p>暂无设备档案</p>
              </div>
            </td>
          </tr>
          <tr v-for="device in devices" :key="device.id" class="table-row" @click="openDetailPanel(device)">
            <td><span class="device-name">{{ device.deviceName }}</span></td>
            <td><span class="device-code">{{ device.deviceCode }}</span></td>
            <td>
              <span class="status-tag" :class="device.isActive ? 'active' : 'inactive'">
                <span class="status-dot"></span>{{ device.isActive ? '运行中' : '已停用' }}
              </span>
            </td>
            <td><span class="process-id-chip">{{ device.processId.substring(0, 8) }}…</span></td>
            <td class="action-cell" @click.stop>
              <button class="icon-btn edit" title="编辑档案" v-permission="'Device.Create'" @click="openEditModal(device)">
                <svg viewBox="0 0 16 16" fill="none"><path d="M11.5 2.5l2 2-8 8H3.5v-2l8-8z" stroke="currentColor" stroke-width="1.2" stroke-linejoin="round"/></svg>
              </button>
              <button v-if="device.isActive" class="icon-btn deactivate" title="停用设备" v-permission="'Device.Deactivate'" @click="handleDeactivate(device)">
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

    <!-- 注册设备弹窗 -->
    <Teleport to="body">
      <div v-if="showRegisterModal" class="modal-overlay" @click.self="showRegisterModal=false">
        <div class="modal">
          <div class="modal-header">
            <span class="modal-title">注册新设备</span>
            <button class="modal-close" @click="showRegisterModal=false">✕</button>
          </div>
          <div class="modal-body">
            <div class="form-row">
              <div class="form-field">
                <label class="form-label">设备名称 <span class="required">*</span></label>
                <input class="form-input" v-model="registerForm.DeviceName" placeholder="如：1号叠片机" />
              </div>
              <div class="form-field">
                <label class="form-label">设备编号 <span class="required">*</span></label>
                <input class="form-input" v-model="registerForm.DeviceCode" placeholder="如：Stacker-01" />
              </div>
            </div>
            <div class="form-field">
              <label class="form-label">MAC 地址 <span class="required">*</span></label>
              <input class="form-input" v-model="registerForm.MacAddress" placeholder="如：00-1A-2B-3C-4D-5E" />
            </div>
            <div class="form-field">
              <label class="form-label">归属工序 ID <span class="required">*</span></label>
              <input class="form-input mono-input" v-model="registerForm.ProcessId" placeholder="粘贴工序 UUID" />
              <p class="form-hint">请从工序管理模块获取对应工序的 UUID</p>
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn btn-ghost" @click="showRegisterModal=false">取消</button>
            <button class="btn btn-primary" :disabled="submitting" @click="submitRegister">
              {{ submitting ? '注册中...' : '确认注册' }}
            </button>
          </div>
        </div>
      </div>
    </Teleport>

    <!-- 编辑档案弹窗 -->
    <Teleport to="body">
      <div v-if="showEditModal" class="modal-overlay" @click.self="showEditModal=false">
        <div class="modal">
          <div class="modal-header">
            <span class="modal-title">编辑设备档案</span>
            <button class="modal-close" @click="showEditModal=false">✕</button>
          </div>
          <div class="modal-body">
            <div class="form-row">
              <div class="form-field">
                <label class="form-label">设备名称 <span class="required">*</span></label>
                <input class="form-input" v-model="editForm.DeviceName" />
              </div>
              <div class="form-field">
                <label class="form-label">设备编号 <span class="required">*</span></label>
                <input class="form-input" v-model="editForm.DeviceCode" />
              </div>
            </div>
            <div class="info-row">
              <span class="info-label">MAC 地址（不可修改）</span>
              <span class="info-value mono">{{ editTarget?.deviceCode }}</span>
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

    <!-- 详情侧边栏 -->
    <Teleport to="body">
      <div v-if="showDetailPanel" class="detail-overlay" @click.self="showDetailPanel=false">
        <div class="detail-panel">
          <div class="detail-header">
            <span class="detail-title">设备详情</span>
            <button class="modal-close" @click="showDetailPanel=false">✕</button>
          </div>
          <div class="detail-body" v-if="selectedDevice">
            <div class="detail-status-banner" :class="selectedDevice.isActive ? 'active' : 'inactive'">
              <span class="status-dot"></span>
              {{ selectedDevice.isActive ? '设备运行中' : '设备已停用' }}
            </div>
            <div class="detail-section">
              <div class="detail-row">
                <span class="detail-label">设备名称</span>
                <span class="detail-value">{{ selectedDevice.deviceName }}</span>
              </div>
              <div class="detail-row">
                <span class="detail-label">设备编号</span>
                <span class="detail-value mono">{{ selectedDevice.deviceCode }}</span>
              </div>
              <div class="detail-row">
                <span class="detail-label">设备 ID</span>
                <span class="detail-value mono small">{{ selectedDevice.id }}</span>
              </div>
              <div class="detail-row">
                <span class="detail-label">归属工序 ID</span>
                <span class="detail-value mono small">{{ selectedDevice.processId }}</span>
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
  getDevicePagedListApi, registerDeviceApi, updateDeviceProfileApi, deactivateDeviceApi,
  type DeviceListItemDto, type PagedMetaData,
} from '../../api/device';

const devices = ref<DeviceListItemDto[]>([]);
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

// PagedList<T> 后端序列化：数字 key 是元素，metaData 是元信息
const fetchList = async () => {
  loading.value = true;
  try {
    const raw = await getDevicePagedListApi({
      PaginationParams: { PageNumber: currentPage.value, PageSize: 10 },
      Keyword: keyword.value || undefined,
    }) as unknown as Record<string, unknown>;

    if (raw && raw.metaData) {
      metaData.value = raw.metaData as PagedMetaData;
      const items: DeviceListItemDto[] = [];
      for (const k of Object.keys(raw)) {
        if (!isNaN(Number(k))) items.push(raw[k] as DeviceListItemDto);
      }
      devices.value = items;
    } else if (Array.isArray(raw)) {
      devices.value = raw as DeviceListItemDto[];
    }
  } catch {
    devices.value = [];
  } finally {
    loading.value = false;
  }
};

const goPage = (page: number) => { currentPage.value = page; fetchList(); };

// ── 注册弹窗 ──
const showRegisterModal = ref(false);
const registerForm = reactive({ DeviceName: '', DeviceCode: '', MacAddress: '', ProcessId: '' });

const openRegisterModal = () => {
  Object.assign(registerForm, { DeviceName: '', DeviceCode: '', MacAddress: '', ProcessId: '' });
  showRegisterModal.value = true;
};

const submitRegister = async () => {
  if (!registerForm.DeviceName.trim() || !registerForm.DeviceCode.trim() ||
      !registerForm.MacAddress.trim() || !registerForm.ProcessId.trim()) {
    alert('所有字段均为必填项'); return;
  }
  submitting.value = true;
  try {
    await registerDeviceApi({ ...registerForm });
    showRegisterModal.value = false;
    fetchList();
  } catch { } finally { submitting.value = false; }
};

// ── 编辑弹窗 ──
const showEditModal = ref(false);
const editTarget = ref<DeviceListItemDto | null>(null);
const editForm = reactive({ DeviceName: '', DeviceCode: '' });

const openEditModal = (device: DeviceListItemDto) => {
  editTarget.value = device;
  editForm.DeviceName = device.deviceName;
  editForm.DeviceCode = device.deviceCode;
  showEditModal.value = true;
};

const submitEdit = async () => {
  if (!editTarget.value || !editForm.DeviceName.trim() || !editForm.DeviceCode.trim()) {
    alert('名称和编号不能为空'); return;
  }
  submitting.value = true;
  try {
    await updateDeviceProfileApi(editTarget.value.id, {
      DeviceName: editForm.DeviceName,
      DeviceCode: editForm.DeviceCode,
    });
    showEditModal.value = false;
    fetchList();
  } catch { } finally { submitting.value = false; }
};

// ── 详情侧边栏 ──
const showDetailPanel = ref(false);
const selectedDevice = ref<DeviceListItemDto | null>(null);

const openDetailPanel = (device: DeviceListItemDto) => {
  selectedDevice.value = device;
  showDetailPanel.value = true;
};

// ── 停用确认 ──
const confirmDialog = reactive({
  show: false, title: '', desc: '', confirmText: '',
  onConfirm: () => {},
});

const handleDeactivate = (device: DeviceListItemDto) => {
  Object.assign(confirmDialog, {
    show: true,
    title: '确认停用设备',
    desc: `设备【${device.deviceName}】停用后将无法下发配方，请确认操作。`,
    confirmText: '停用',
    onConfirm: async () => {
      submitting.value = true;
      try {
        await deactivateDeviceApi(device.id);
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
.device-list { font-family: 'Noto Sans SC', sans-serif; color: #e0e4ef; }

.page-header { display: flex; align-items: flex-start; justify-content: space-between; margin-bottom: 24px; }
.page-title { font-size: 22px; font-weight: 600; color: #fff; margin: 0 0 4px; letter-spacing: 0.5px; }
.page-sub { font-size: 13px; color: rgba(255,255,255,0.35); margin: 0; }

.toolbar { display: flex; align-items: center; gap: 12px; margin-bottom: 16px; }
.search-wrap {
  position: relative; display: flex; align-items: center;
  background: rgba(255,255,255,0.04); border: 1px solid rgba(255,255,255,0.08);
  border-radius: 4px; padding: 0 12px; gap: 8px; transition: border-color 0.2s; flex: 0 0 300px;
}
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
.table-row { border-bottom: 1px solid rgba(255,255,255,0.04); cursor: pointer; transition: background 0.15s; }
.table-row:last-child { border-bottom: none; }
.table-row:hover { background: rgba(0,229,255,0.03); }
.data-table td { padding: 13px 16px; font-size: 13px; vertical-align: middle; }

.device-name { color: #e0e4ef; font-weight: 500; }
.device-code { font-family: 'Courier New', monospace; font-size: 12px; color: #00e5ff; background: rgba(0,229,255,0.08); padding: 2px 7px; border-radius: 3px; }
.process-id-chip { font-family: 'Courier New', monospace; font-size: 11px; color: rgba(255,255,255,0.35); background: rgba(255,255,255,0.04); padding: 2px 6px; border-radius: 3px; }

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

.form-row { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }
.form-field { display: flex; flex-direction: column; gap: 6px; }
.form-label { font-size: 12px; color: rgba(255,255,255,0.45); font-weight: 500; }
.required { color: #ff8888; }
.form-input { background: rgba(255,255,255,0.04); border: 1px solid rgba(255,255,255,0.1); border-radius: 3px; padding: 8px 12px; color: rgba(255,255,255,0.8); font-size: 13px; font-family: 'Noto Sans SC', sans-serif; outline: none; transition: border-color 0.2s; }
.form-input:focus { border-color: rgba(0,229,255,0.4); }
.form-input::placeholder { color: rgba(255,255,255,0.2); }
.mono-input { font-family: 'Courier New', monospace; font-size: 12px; }
.form-hint { font-size: 11px; color: rgba(255,255,255,0.2); margin: 0; }
.info-row { display: flex; flex-direction: column; gap: 4px; }
.info-label { font-size: 11px; color: rgba(255,255,255,0.25); }
.info-value { font-size: 12px; color: rgba(255,255,255,0.4); }
.info-value.mono { font-family: 'Courier New', monospace; }

.detail-overlay { position: fixed; inset: 0; z-index: 100; background: rgba(0,0,0,0.5); display: flex; align-items: stretch; justify-content: flex-end; }
.detail-panel { width: 360px; background: #0f1525; border-left: 1px solid rgba(255,255,255,0.08); display: flex; flex-direction: column; animation: slideIn 0.22s cubic-bezier(0.4,0,0.2,1); }
@keyframes slideIn { from { transform: translateX(100%); } to { transform: translateX(0); } }
.detail-header { display: flex; align-items: center; justify-content: space-between; padding: 18px 22px; border-bottom: 1px solid rgba(255,255,255,0.06); }
.detail-title { font-size: 15px; font-weight: 600; color: #fff; }
.detail-body { padding: 20px 22px; flex: 1; overflow-y: auto; }
.detail-status-banner { display: flex; align-items: center; gap: 8px; padding: 10px 14px; border-radius: 4px; font-size: 13px; font-weight: 500; margin-bottom: 20px; }
.detail-status-banner.active { background: rgba(0,229,160,0.1); color: #00e5a0; border: 1px solid rgba(0,229,160,0.2); }
.detail-status-banner.inactive { background: rgba(255,107,107,0.08); color: #ff8888; border: 1px solid rgba(255,107,107,0.2); }
.detail-status-banner .status-dot { width: 7px; height: 7px; border-radius: 50%; }
.detail-status-banner.active .status-dot { background: #00e5a0; box-shadow: 0 0 5px #00e5a0; }
.detail-status-banner.inactive .status-dot { background: #ff8888; }
.detail-section { display: flex; flex-direction: column; gap: 18px; }
.detail-row { display: flex; flex-direction: column; gap: 4px; }
.detail-label { font-size: 11px; color: rgba(255,255,255,0.3); text-transform: uppercase; letter-spacing: 0.8px; }
.detail-value { font-size: 13px; color: rgba(255,255,255,0.8); word-break: break-all; }
.detail-value.mono { font-family: 'Courier New', monospace; color: #00e5ff; font-size: 12px; }
.detail-value.small { font-size: 11px; color: rgba(255,255,255,0.45); }

.confirm-box { background: #0f1525; border: 1px solid rgba(255,255,255,0.08); border-radius: 6px; padding: 28px 28px 22px; width: 360px; max-width: 90vw; text-align: center; box-shadow: 0 24px 48px rgba(0,0,0,0.6); }
.confirm-icon { width: 44px; height: 44px; border-radius: 50%; display: flex; align-items: center; justify-content: center; margin: 0 auto 14px; }
.confirm-icon.danger { background: rgba(255,77,79,0.1); color: #ff8888; }
.confirm-icon svg { width: 22px; height: 22px; }
.confirm-title { font-size: 15px; font-weight: 600; color: #fff; margin-bottom: 8px; }
.confirm-desc { font-size: 13px; color: rgba(255,255,255,0.4); line-height: 1.6; margin-bottom: 22px; }
.confirm-actions { display: flex; gap: 10px; justify-content: center; }
</style>
