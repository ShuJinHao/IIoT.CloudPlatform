<template>
  <div class="device-list">
    <div class="page-header">
      <div>
        <h1 class="page-title">设备台账</h1>
        <p class="page-sub">管理云端导入的设备档案、工序归属与现场使用的设备 Code。</p>
      </div>
      <button class="btn btn-primary" v-permission="'Device.Create'" @click="openRegisterModal">
        <svg viewBox="0 0 16 16" fill="none"><path d="M8 2v12M2 8h12" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/></svg>
        新建设备
      </button>
    </div>

    <div class="toolbar">
      <div class="search-wrap">
        <svg viewBox="0 0 16 16" fill="none"><circle cx="6.5" cy="6.5" r="4.5" stroke="currentColor" stroke-width="1.3"/><path d="M10 10l3 3" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/></svg>
        <input
          v-model="keyword"
          placeholder="搜索设备名称或 Code..."
          @keyup.enter="fetchList"
          @input="onSearchInput"
        />
        <button v-if="keyword" class="clear-btn" @click="keyword=''; fetchList()">×</button>
      </div>
      <span class="total-badge">共 {{ metaData.totalCount }} 台</span>
    </div>

    <div class="table-wrap">
      <div v-if="loading" class="skeleton-rows">
        <div v-for="i in 5" :key="i" class="skeleton-row">
          <div class="skel skel-md"></div>
          <div class="skel skel-lg"></div>
          <div class="skel skel-sm"></div>
          <div class="skel skel-lg"></div>
        </div>
      </div>
      <table v-else class="data-table">
        <thead>
          <tr>
            <th>设备名称</th>
            <th>Code</th>
            <th>状态</th>
            <th>所属工序</th>
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
            <td>
              <div class="code-cell">
                <span class="device-code">{{ device.code }}</span>
                <button class="mini-copy-btn" title="复制 Code" @click.stop="copyCode(device.code)">复制</button>
              </div>
            </td>
            <td><span class="status-tag active"><span class="status-dot"></span>已启用</span></td>
            <td><span class="process-name-chip">{{ processNameMap[device.processId] || `${device.processId.slice(0, 8)}…` }}</span></td>
            <td class="action-cell" @click.stop>
              <button class="icon-btn edit" title="编辑设备" v-permission="'Device.Update'" @click="openEditModal(device)">
                <svg viewBox="0 0 16 16" fill="none"><path d="M11.5 2.5l2 2-8 8H3.5v-2l8-8z" stroke="currentColor" stroke-width="1.2" stroke-linejoin="round"/></svg>
              </button>
              <button class="icon-btn secret" title="轮换启动密钥" v-permission="'Device.Update'" @click="handleRotateBootstrapSecret(device)">
                <svg viewBox="0 0 16 16" fill="none"><circle cx="5.5" cy="8" r="2.5" stroke="currentColor" stroke-width="1.2"/><path d="M8 8h5M11 8v2M13 8v2" stroke="currentColor" stroke-width="1.2" stroke-linecap="round"/></svg>
              </button>
              <button class="icon-btn deactivate" title="删除设备" v-permission="'Device.Delete'" @click="handleDelete(device)">
                <svg viewBox="0 0 16 16" fill="none"><path d="M4.5 5.5h7M6 5.5V4.3c0-.44.36-.8.8-.8h1.4c.44 0 .8.36.8.8v1.2M5.2 5.5l.5 6.1c.03.39.36.69.75.69h3.1c.39 0 .72-.3.75-.69l.5-6.1" stroke="currentColor" stroke-width="1.2" stroke-linecap="round" stroke-linejoin="round"/></svg>
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <div class="pagination" v-if="metaData.totalPages > 1">
      <button class="page-btn" :disabled="currentPage === 1" @click="goPage(currentPage - 1)">
        <svg viewBox="0 0 12 12" fill="none"><path d="M8 2L4 6l4 4" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/></svg>
      </button>
      <button v-for="page in pageNumbers" :key="page" class="page-btn" :class="{ active: page === currentPage }" @click="goPage(page)">
        {{ page }}
      </button>
      <button class="page-btn" :disabled="currentPage === metaData.totalPages" @click="goPage(currentPage + 1)">
        <svg viewBox="0 0 12 12" fill="none"><path d="M4 2l4 4-4 4" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/></svg>
      </button>
    </div>

    <Teleport to="body">
      <div v-if="showRegisterModal" class="modal-overlay" @click.self="showRegisterModal = false">
        <div class="modal">
          <div class="modal-header">
            <span class="modal-title">新建设备</span>
            <button class="modal-close" @click="showRegisterModal = false">×</button>
          </div>
          <div class="modal-body">
            <div class="form-field">
              <label class="form-label">设备名称 <span class="required">*</span></label>
              <input class="form-input" v-model="registerForm.deviceName" placeholder="如：1号注液机" />
            </div>
            <div class="form-field">
              <label class="form-label">所属工序 <span class="required">*</span></label>
              <select class="form-input" v-model="registerForm.processId">
                <option value="">请选择工序</option>
                <option v-for="process in allProcesses" :key="process.id" :value="process.id">
                  {{ process.processCode }} · {{ process.processName }}
                </option>
              </select>
            </div>
              <div class="hint-card">
                <div class="hint-title">设备 Code 由云端自动生成</div>
                <div class="hint-desc">保存后会返回唯一 Code 和启动密钥，可直接复制给现场客户端配置使用。</div>
              </div>
            </div>
          <div class="modal-footer">
            <button class="btn btn-ghost" @click="showRegisterModal = false">取消</button>
            <button class="btn btn-primary" :disabled="submitting" @click="submitRegister">
              {{ submitting ? '创建中...' : '确认创建' }}
            </button>
          </div>
        </div>
      </div>
    </Teleport>

    <Teleport to="body">
      <div v-if="showEditModal" class="modal-overlay" @click.self="showEditModal = false">
        <div class="modal">
          <div class="modal-header">
            <span class="modal-title">编辑设备名称</span>
            <button class="modal-close" @click="showEditModal = false">×</button>
          </div>
          <div class="modal-body">
            <div class="form-field">
              <label class="form-label">设备名称 <span class="required">*</span></label>
              <input class="form-input" v-model="editForm.deviceName" />
            </div>
            <div class="hint-card subtle">
              <div class="hint-title">设备 Code 不可修改</div>
              <div class="hint-desc">如误创建设备，仅可在无依赖数据时删除后重新创建。</div>
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn btn-ghost" @click="showEditModal = false">取消</button>
            <button class="btn btn-primary" :disabled="submitting" @click="submitEdit">
              {{ submitting ? '保存中...' : '保存修改' }}
            </button>
          </div>
        </div>
      </div>
    </Teleport>

    <Teleport to="body">
      <div v-if="showDetailPanel" class="detail-overlay" @click.self="showDetailPanel = false">
        <div class="detail-panel">
          <div class="detail-header">
            <span class="detail-title">设备详情</span>
            <button class="modal-close" @click="showDetailPanel = false">×</button>
          </div>
          <div v-if="selectedDevice" class="detail-body">
            <div class="detail-status-banner active"><span class="status-dot"></span>设备已启用</div>
            <div class="detail-section">
              <div class="detail-row">
                <span class="detail-label">设备名称</span>
                <span class="detail-value">{{ selectedDevice.deviceName }}</span>
              </div>
              <div class="detail-row">
                <span class="detail-label">设备 Code</span>
                <div class="detail-code-row">
                  <span class="detail-value mono">{{ selectedDevice.code }}</span>
                  <button class="mini-copy-btn detail-copy" @click="copyCode(selectedDevice.code)">复制</button>
                </div>
              </div>
              <div class="detail-row">
                <span class="detail-label">设备 ID</span>
                <span class="detail-value mono small">{{ selectedDevice.id }}</span>
              </div>
              <div class="detail-row">
                <span class="detail-label">所属工序</span>
                <span class="detail-value">{{ processNameMap[selectedDevice.processId] || selectedDevice.processId }}</span>
              </div>
              <button class="btn btn-ghost detail-action" v-permission="'Device.Update'" @click="handleRotateBootstrapSecret(selectedDevice)">
                轮换启动密钥
              </button>
            </div>
          </div>
        </div>
      </div>
    </Teleport>

    <Teleport to="body">
      <div v-if="bootstrapSecretDialog.show" class="modal-overlay" @click.self="bootstrapSecretDialog.show = false">
        <div class="modal secret-modal">
          <div class="modal-header">
            <span class="modal-title">{{ bootstrapSecretDialog.title }}</span>
            <button class="modal-close" @click="bootstrapSecretDialog.show = false">×</button>
          </div>
          <div class="modal-body">
            <div class="secret-warning">
              启动密钥只显示一次，请立即保存到边缘端配置。
            </div>
            <div class="form-field">
              <label class="form-label">设备 Code</label>
              <div class="secret-copy-row">
                <span class="secret-value mono">{{ bootstrapSecretDialog.code }}</span>
                <button class="mini-copy-btn" @click="copyText(bootstrapSecretDialog.code, 'Code 已复制。')">复制</button>
              </div>
            </div>
            <div class="form-field">
              <label class="form-label">启动密钥</label>
              <div class="secret-copy-row">
                <span class="secret-value mono">{{ bootstrapSecretDialog.secret }}</span>
                <button class="mini-copy-btn" @click="copyText(bootstrapSecretDialog.secret, '启动密钥已复制。')">复制</button>
              </div>
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn btn-primary" @click="bootstrapSecretDialog.show = false">我已保存</button>
          </div>
        </div>
      </div>
    </Teleport>

    <Teleport to="body">
      <div v-if="confirmDialog.show" class="modal-overlay">
        <div class="confirm-box">
          <div class="confirm-icon danger">
            <svg viewBox="0 0 20 20" fill="none"><path d="M10 6v5M10 13.5v.5" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/><circle cx="10" cy="10" r="8" stroke="currentColor" stroke-width="1.3"/></svg>
          </div>
          <div class="confirm-title">{{ confirmDialog.title }}</div>
          <div class="confirm-desc">{{ confirmDialog.desc }}</div>
          <div class="confirm-actions">
            <button class="btn btn-ghost" @click="confirmDialog.show = false">取消</button>
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
import { computed, onMounted, reactive, ref } from 'vue';
import {
  getDevicePagedListApi,
  registerDeviceApi,
  updateDeviceProfileApi,
  rotateDeviceBootstrapSecretApi,
  deleteDeviceApi,
  type DeviceListItemDto,
  type PagedMetaData,
} from '../../api/device';
import { getAllProcessesApi, type ProcessSelectDto } from '../../api/masterData/processes';

const devices = ref<DeviceListItemDto[]>([]);
const loading = ref(false);
const keyword = ref('');
const currentPage = ref(1);
const metaData = ref<PagedMetaData>({ totalCount: 0, pageSize: 10, currentPage: 1, totalPages: 1 });
const submitting = ref(false);

const allProcesses = ref<ProcessSelectDto[]>([]);
const processNameMap = computed(() => {
  const map: Record<string, string> = {};
  for (const process of allProcesses.value) {
    map[process.id] = `${process.processCode} · ${process.processName}`;
  }
  return map;
});

const fetchProcesses = async () => {
  try {
    allProcesses.value = await getAllProcessesApi();
  } catch {
    allProcesses.value = [];
  }
};

const pageNumbers = computed(() => {
  const pages: number[] = [];
  for (let page = Math.max(1, currentPage.value - 2); page <= Math.min(metaData.value.totalPages, currentPage.value + 2); page++) {
    pages.push(page);
  }
  return pages;
});

let searchTimer: ReturnType<typeof setTimeout> | null = null;
const onSearchInput = () => {
  if (searchTimer) clearTimeout(searchTimer);
  searchTimer = setTimeout(() => {
    currentPage.value = 1;
    fetchList();
  }, 400);
};

const fetchList = async () => {
  loading.value = true;
  try {
    const response = await getDevicePagedListApi({
      PaginationParams: { PageNumber: currentPage.value, PageSize: 10 },
      Keyword: keyword.value || undefined,
    });
    devices.value = response.items;
    metaData.value = response.metaData;
  } catch {
    devices.value = [];
  } finally {
    loading.value = false;
  }
};

const goPage = (page: number) => {
  currentPage.value = page;
  fetchList();
};

const copyText = async (text: string, successMessage: string) => {
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(text);
    } else {
      const textarea = document.createElement('textarea');
      textarea.value = text;
      textarea.style.position = 'fixed';
      textarea.style.opacity = '0';
      document.body.appendChild(textarea);
      textarea.select();
      document.execCommand('copy');
      document.body.removeChild(textarea);
    }
    alert(successMessage);
  } catch {
    alert('复制失败，请手动复制。');
  }
};

const copyCode = async (code: string) => {
  await copyText(code, `Code 已复制：${code}`);
};

const showRegisterModal = ref(false);
const registerForm = reactive({ deviceName: '', processId: '' });
const bootstrapSecretDialog = reactive({
  show: false,
  title: '',
  code: '',
  secret: '',
});

const openRegisterModal = async () => {
  Object.assign(registerForm, { deviceName: '', processId: '' });
  await fetchProcesses();
  showRegisterModal.value = true;
};

const showDetailPanel = ref(false);
const selectedDevice = ref<DeviceListItemDto | null>(null);

const openDetailPanel = (device: DeviceListItemDto) => {
  selectedDevice.value = device;
  showDetailPanel.value = true;
};

const submitRegister = async () => {
  const deviceName = registerForm.deviceName.trim();
  if (!deviceName || !registerForm.processId) {
    alert('请填写设备名称并选择所属工序。');
    return;
  }

  submitting.value = true;
  try {
    const created = await registerDeviceApi({
      deviceName,
      processId: registerForm.processId,
    });

    const createdDevice: DeviceListItemDto = {
      id: created.id,
      code: created.code,
      deviceName,
      processId: registerForm.processId,
    };

    showRegisterModal.value = false;
    openDetailPanel(createdDevice);
    showBootstrapSecret('设备启动密钥', created.code, created.bootstrapSecret);
    await fetchList();
  } catch {
    // handled by the shared http wrapper
  } finally {
    submitting.value = false;
  }
};

const showBootstrapSecret = (title: string, code: string, secret: string) => {
  Object.assign(bootstrapSecretDialog, {
    show: true,
    title,
    code,
    secret,
  });
};

const showEditModal = ref(false);
const editTarget = ref<DeviceListItemDto | null>(null);
const editForm = reactive({ deviceName: '' });

const openEditModal = (device: DeviceListItemDto) => {
  editTarget.value = device;
  editForm.deviceName = device.deviceName;
  showEditModal.value = true;
};

const submitEdit = async () => {
  const deviceName = editForm.deviceName.trim();
  if (!editTarget.value || !deviceName) {
    alert('设备名称不能为空。');
    return;
  }

  submitting.value = true;
  try {
    await updateDeviceProfileApi(editTarget.value.id, { deviceName });
    if (selectedDevice.value?.id === editTarget.value.id) {
      selectedDevice.value = { ...selectedDevice.value, deviceName };
    }
    showEditModal.value = false;
    await fetchList();
  } catch {
    // handled by the shared http wrapper
  } finally {
    submitting.value = false;
  }
};

const confirmDialog = reactive({
  show: false,
  title: '',
  desc: '',
  confirmText: '',
  onConfirm: () => Promise.resolve(),
});

const handleDelete = (device: DeviceListItemDto) => {
  Object.assign(confirmDialog, {
    show: true,
    title: '确认删除设备',
    desc: `设备【${device.deviceName}】删除后，现场保存的 Code 将无法继续寻址。若设备已有配方、产能、日志或过站数据，删除会被拒绝。`,
    confirmText: '删除',
    onConfirm: async () => {
      submitting.value = true;
      try {
        await deleteDeviceApi(device.id);
        if (selectedDevice.value?.id === device.id) {
          showDetailPanel.value = false;
          selectedDevice.value = null;
        }
        confirmDialog.show = false;
        await fetchList();
      } catch {
        // handled by the shared http wrapper
      } finally {
        submitting.value = false;
      }
    },
  });
};

const handleRotateBootstrapSecret = (device: DeviceListItemDto) => {
  Object.assign(confirmDialog, {
    show: true,
    title: '确认轮换启动密钥',
    desc: `轮换后，设备【${device.deviceName}】旧启动密钥会立即失效。`,
    confirmText: '轮换',
    onConfirm: async () => {
      submitting.value = true;
      try {
        const rotated = await rotateDeviceBootstrapSecretApi(device.id);
        confirmDialog.show = false;
        showBootstrapSecret('启动密钥已轮换', rotated.code, rotated.bootstrapSecret);
      } catch {
        // handled by the shared http wrapper
      } finally {
        submitting.value = false;
      }
    },
  });
};

onMounted(async () => {
  await Promise.all([fetchList(), fetchProcesses()]);
});
</script>

<style scoped>
* { box-sizing: border-box; }
.device-list { font-family: 'Noto Sans SC', sans-serif; color: #e0e4ef; }
.page-header { display: flex; align-items: flex-start; justify-content: space-between; margin-bottom: 24px; gap: 16px; }
.page-title { font-size: 22px; font-weight: 600; color: #fff; margin: 0 0 4px; letter-spacing: 0.5px; }
.page-sub { font-size: 13px; color: rgba(255,255,255,0.35); margin: 0; max-width: 620px; line-height: 1.6; }
.toolbar { display: flex; align-items: center; gap: 12px; margin-bottom: 16px; }
.search-wrap { position: relative; display: flex; align-items: center; background: rgba(255,255,255,0.04); border: 1px solid rgba(255,255,255,0.08); border-radius: 4px; padding: 0 12px; gap: 8px; transition: border-color 0.2s; flex: 0 0 320px; }
.search-wrap:focus-within { border-color: rgba(0,229,255,0.3); }
.search-wrap svg { width: 14px; height: 14px; color: rgba(255,255,255,0.25); flex-shrink: 0; }
.search-wrap input { flex: 1; background: none; border: none; outline: none; color: rgba(255,255,255,0.75); font-size: 13px; font-family: 'Noto Sans SC', sans-serif; padding: 9px 0; }
.search-wrap input::placeholder { color: rgba(255,255,255,0.2); }
.clear-btn { background: none; border: none; color: rgba(255,255,255,0.3); cursor: pointer; font-size: 16px; line-height: 1; padding: 0; }
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
.code-cell { display: flex; align-items: center; gap: 8px; }
.device-code { font-family: 'Courier New', monospace; font-size: 12px; color: #00e5ff; background: rgba(0,229,255,0.08); padding: 2px 7px; border-radius: 3px; }
.mini-copy-btn { border: 1px solid rgba(255,255,255,0.1); background: rgba(255,255,255,0.04); color: rgba(255,255,255,0.55); border-radius: 3px; padding: 4px 8px; font-size: 11px; cursor: pointer; transition: all 0.15s; }
.mini-copy-btn:hover { color: #00e5ff; border-color: rgba(0,229,255,0.3); background: rgba(0,229,255,0.12); }
.process-name-chip { font-size: 12px; color: rgba(255,255,255,0.5); background: rgba(255,255,255,0.04); padding: 2px 8px; border-radius: 3px; }
.status-tag { display: inline-flex; align-items: center; gap: 5px; font-size: 11px; font-weight: 500; padding: 3px 9px; border-radius: 20px; }
.status-tag.active { background: rgba(0,229,160,0.12); color: #00e5a0; }
.status-dot { width: 5px; height: 5px; border-radius: 50%; }
.status-tag.active .status-dot { background: #00e5a0; box-shadow: 0 0 4px #00e5a0; }
.action-cell { text-align: right; white-space: nowrap; }
.icon-btn { display: inline-flex; align-items: center; justify-content: center; width: 28px; height: 28px; border-radius: 3px; border: none; cursor: pointer; background: rgba(255,255,255,0.04); color: rgba(255,255,255,0.4); transition: all 0.15s; margin-left: 4px; }
.icon-btn svg { width: 13px; height: 13px; }
.icon-btn.edit:hover { background: rgba(0,229,255,0.12); color: #00e5ff; }
.icon-btn.secret:hover { background: rgba(255,193,7,0.12); color: #ffd166; }
.icon-btn.deactivate:hover { background: rgba(255,107,107,0.12); color: #ff8888; }
.skeleton-rows { padding: 8px 0; }
.skeleton-row { display: flex; gap: 16px; padding: 14px 16px; border-bottom: 1px solid rgba(255,255,255,0.04); align-items: center; }
.skel { background: rgba(255,255,255,0.06); border-radius: 3px; height: 14px; animation: shimmer 1.5s infinite; }
.skel-sm { width: 80px; }
.skel-md { width: 140px; }
.skel-lg { width: 220px; }
@keyframes shimmer { 0%,100% { opacity: 0.5; } 50% { opacity: 1; } }
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
.btn-ghost:hover { background: rgba(255,255,255,0.08); }
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
select.form-input { appearance: none; background-image: url(\"data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' viewBox='0 0 12 12'%3E%3Cpath d='M3 4.5l3 3 3-3' stroke='%2300e5ff' stroke-width='1.2' fill='none' stroke-linecap='round'/%3E%3C/svg%3E\"); background-repeat: no-repeat; background-position: right 12px center; padding-right: 32px; cursor: pointer; }
select.form-input option { background: #0f1525; color: #e0e4ef; }
.hint-card { background: rgba(0,229,255,0.06); border: 1px solid rgba(0,229,255,0.14); border-radius: 4px; padding: 12px 14px; }
.hint-card.subtle { background: rgba(255,255,255,0.04); border-color: rgba(255,255,255,0.08); }
.hint-title { color: #dffbff; font-size: 12px; font-weight: 600; margin-bottom: 4px; }
.hint-desc { color: rgba(255,255,255,0.55); font-size: 12px; line-height: 1.6; }
.detail-overlay { position: fixed; inset: 0; z-index: 100; background: rgba(0,0,0,0.5); display: flex; align-items: stretch; justify-content: flex-end; }
.detail-panel { width: 360px; background: #0f1525; border-left: 1px solid rgba(255,255,255,0.08); display: flex; flex-direction: column; animation: slideIn 0.22s cubic-bezier(0.4,0,0.2,1); }
@keyframes slideIn { from { transform: translateX(100%); } to { transform: translateX(0); } }
.detail-header { display: flex; align-items: center; justify-content: space-between; padding: 18px 22px; border-bottom: 1px solid rgba(255,255,255,0.06); }
.detail-title { font-size: 15px; font-weight: 600; color: #fff; }
.detail-body { padding: 20px 22px; flex: 1; overflow-y: auto; }
.detail-status-banner { display: flex; align-items: center; gap: 8px; padding: 10px 14px; border-radius: 4px; font-size: 13px; font-weight: 500; margin-bottom: 20px; }
.detail-status-banner.active { background: rgba(0,229,160,0.1); color: #00e5a0; border: 1px solid rgba(0,229,160,0.2); }
.detail-section { display: flex; flex-direction: column; gap: 18px; }
.detail-row { display: flex; flex-direction: column; gap: 4px; }
.detail-label { font-size: 11px; color: rgba(255,255,255,0.3); text-transform: uppercase; letter-spacing: 0.8px; }
.detail-value { font-size: 13px; color: rgba(255,255,255,0.8); word-break: break-all; }
.detail-value.mono { font-family: 'Courier New', monospace; color: #00e5ff; font-size: 12px; }
.detail-value.small { font-size: 11px; color: rgba(255,255,255,0.45); }
.detail-code-row { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
.detail-copy { width: fit-content; }
.detail-action { width: fit-content; margin-top: 4px; }
.secret-modal { width: 560px; }
.secret-warning { background: rgba(255,193,7,0.1); border: 1px solid rgba(255,193,7,0.22); border-radius: 4px; padding: 10px 12px; color: #ffd166; font-size: 12px; line-height: 1.6; }
.secret-copy-row { display: flex; align-items: center; gap: 8px; min-width: 0; }
.secret-value { flex: 1; min-width: 0; color: #00e5ff; background: rgba(0,229,255,0.08); border: 1px solid rgba(0,229,255,0.14); border-radius: 3px; padding: 8px 10px; word-break: break-all; }
.mono { font-family: 'Courier New', monospace; }
.confirm-box { background: #0f1525; border: 1px solid rgba(255,255,255,0.08); border-radius: 6px; padding: 28px 28px 22px; width: 360px; max-width: 90vw; text-align: center; box-shadow: 0 24px 48px rgba(0,0,0,0.6); }
.confirm-icon { width: 44px; height: 44px; border-radius: 50%; display: flex; align-items: center; justify-content: center; margin: 0 auto 14px; }
.confirm-icon.danger { background: rgba(255,77,79,0.1); color: #ff8888; }
.confirm-icon svg { width: 22px; height: 22px; }
.confirm-title { font-size: 15px; font-weight: 600; color: #fff; margin-bottom: 8px; }
.confirm-desc { font-size: 13px; color: rgba(255,255,255,0.4); line-height: 1.6; margin-bottom: 22px; }
.confirm-actions { display: flex; gap: 10px; justify-content: center; }

@media (max-width: 960px) {
  .page-header { flex-direction: column; align-items: stretch; }
  .toolbar { flex-direction: column; align-items: stretch; }
  .search-wrap { flex: 1 1 auto; width: 100%; }
  .table-wrap { overflow-x: auto; }
  .data-table { min-width: 760px; }
}
</style>
