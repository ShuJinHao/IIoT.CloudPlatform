<template>
  <NiondDataPage
    class="device-page"
    page-key="devices"
    title="设备台账"
      subtitle="管理云端导入的设备档案、工序归属与现场使用的设备 Code"
  >
      <template #actions>
        <UiButton
          v-if="authStore.isAdmin"
          type="primary"
          @click="openRegisterModal"
        >
          <template #icon>
            <svg viewBox="0 0 16 16" fill="none">
              <path d="M8 2v12M2 8h12" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/>
            </svg>
          </template>
          新建设备
        </UiButton>
      </template>

    <template #toolbar>
      <NiondToolbar>
        <div class="filter-row">
          <UiInput
            v-model:value="keyword"
            placeholder="搜索设备名称或 Code..."
            clearable
            size="small"
            style="max-width: 360px;"
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
          <UiTag round :bordered="false" size="small">共 {{ metaData.totalCount }} 台</UiTag>
        </div>
      </NiondToolbar>
    </template>

    <NiondTableCard class="device-page__table-card">
      <UiDataTable
        class="device-page__table"
        :columns="columns"
        :data="devices"
        :loading="loading"
        :bordered="false"
        :single-line="false"
        :row-key="rowKey"
        size="small"
      >
        <template #empty>
          <EmptyState title="未找到设备" description="当前没有设备数据或未找到匹配的结果。" />
        </template>
      </UiDataTable>
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

    <!-- 新建设备 modal -->
    <UiModal
      v-model:show="showRegisterModal"
      preset="card"
      title="新建设备"
      style="width: 520px;"
      :mask-closable="false"
    >
      <div class="form-stack">
        <div class="form-field">
          <label class="form-label">设备名称 <span class="required">*</span></label>
          <UiInput v-model:value="registerForm.deviceName" placeholder="如：1号注液机" />
        </div>
        <div class="form-field">
          <label class="form-label">所属工序 <span class="required">*</span></label>
          <UiSelect
            v-model:value="registerForm.processId"
            :options="processOptions"
            placeholder="请选择工序"
            filterable
          />
        </div>
        <div class="hint-card">
          <div class="hint-card__title">设备 Code 由云端自动生成</div>
          <div class="hint-card__desc">
            保存后会返回唯一 Code 和启动密钥，可直接复制给现场客户端配置使用。
          </div>
        </div>
      </div>
      <template #footer>
        <div class="modal-actions">
          <UiButton @click="showRegisterModal = false">取消</UiButton>
          <UiButton
            type="primary"
            :loading="submitting"
            @click="submitRegister"
          >
            确认创建
          </UiButton>
        </div>
      </template>
    </UiModal>

    <!-- 编辑设备 modal -->
    <UiModal
      v-model:show="showEditModal"
      preset="card"
      title="编辑设备名称"
      style="width: 480px;"
      :mask-closable="false"
    >
      <div class="form-stack">
        <div class="form-field">
          <label class="form-label">设备名称 <span class="required">*</span></label>
          <UiInput v-model:value="editForm.deviceName" />
        </div>
        <div class="hint-card hint-card--subtle">
          <div class="hint-card__title">设备 Code 不可修改</div>
          <div class="hint-card__desc">
            如误创建设备，仅可在无依赖数据时删除后重新创建。
          </div>
        </div>
      </div>
      <template #footer>
        <div class="modal-actions">
          <UiButton @click="showEditModal = false">取消</UiButton>
          <UiButton
            type="primary"
            :loading="submitting"
            @click="submitEdit"
          >
            保存修改
          </UiButton>
        </div>
      </template>
    </UiModal>

    <!-- 详情抽屉 -->
    <UiDrawer
      v-model:show="showDetailPanel"
      :width="380"
      placement="right"
    >
      <UiDrawerContent title="设备详情" closable>
        <div v-if="selectedDevice" class="detail-stack">
          <div class="detail-status-banner is-active">
            <span class="detail-status-banner__dot"></span>
            设备已启用
          </div>
          <div class="detail-row">
            <span class="detail-row__label">设备名称</span>
            <span class="detail-row__value">{{ selectedDevice.deviceName }}</span>
          </div>
          <div class="detail-row">
            <span class="detail-row__label">设备 Code</span>
            <div class="detail-row__copy">
              <span class="detail-row__value detail-row__value--mono detail-row__value--brand">
                {{ selectedDevice.code }}
              </span>
              <UiButton
                size="tiny"
                quaternary
                type="primary"
                @click="copyCode(selectedDevice.code)"
              >
                复制
              </UiButton>
            </div>
          </div>
          <div class="detail-row">
            <span class="detail-row__label">设备 ID</span>
            <span class="detail-row__value detail-row__value--mono detail-row__value--small">
              {{ selectedDevice.id }}
            </span>
          </div>
          <div class="detail-row">
            <span class="detail-row__label">所属工序</span>
            <span class="detail-row__value">
              {{ processNameMap[selectedDevice.processId] || selectedDevice.processId }}
            </span>
          </div>
          <UiButton
            v-permission="'Device.Update'"
            secondary
            block
            @click="handleRotateBootstrapSecret(selectedDevice)"
          >
            轮换启动密钥
          </UiButton>
        </div>
      </UiDrawerContent>
    </UiDrawer>

    <!-- 启动密钥揭示 modal -->
    <UiModal
      v-model:show="bootstrapSecretDialog.show"
      preset="card"
      :title="bootstrapSecretDialog.title"
      style="width: 560px;"
      :mask-closable="false"
    >
      <div class="form-stack">
        <div class="warning-card">
          <svg viewBox="0 0 16 16" width="16" height="16" fill="none">
            <path d="M8 1.5L14 13H2L8 1.5z" stroke="currentColor" stroke-width="1.3" stroke-linejoin="round"/>
            <path d="M8 6v3M8 10.5v.5" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/>
          </svg>
          启动密钥只显示一次，请立即保存到边缘端配置。
        </div>
        <div class="form-field">
          <label class="form-label">设备 Code</label>
          <div class="secret-row">
            <code class="secret-row__value">{{ bootstrapSecretDialog.code }}</code>
            <UiButton
              size="small"
              secondary
              type="primary"
              @click="copyText(bootstrapSecretDialog.code, 'Code 已复制。')"
            >
              复制
            </UiButton>
          </div>
        </div>
        <div class="form-field">
          <label class="form-label">启动密钥</label>
          <div class="secret-row">
            <code class="secret-row__value">{{ bootstrapSecretDialog.secret }}</code>
            <UiButton
              size="small"
              secondary
              type="primary"
              @click="copyText(bootstrapSecretDialog.secret, '启动密钥已复制。')"
            >
              复制
            </UiButton>
          </div>
        </div>
      </div>
      <template #footer>
        <div class="modal-actions">
          <UiButton
            type="primary"
            @click="bootstrapSecretDialog.show = false"
          >
            我已保存
          </UiButton>
        </div>
      </template>
    </UiModal>

    <!-- 通用确认 modal -->
    <UiModal
      v-model:show="confirmDialog.show"
      preset="card"
      :title="confirmDialog.title"
      style="width: 440px;"
      :mask-closable="false"
    >
      <p class="confirm-desc">{{ confirmDialog.desc }}</p>
      <template #footer>
        <div class="modal-actions">
          <UiButton @click="confirmDialog.show = false">取消</UiButton>
          <UiButton
            :type="confirmDialog.danger ? 'error' : 'warning'"
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
import { ref, reactive, computed, h, onMounted } from 'vue';
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
import { useAuthStore } from '../../stores/auth';
import { Permissions } from '../../types/permissions';
import NiondDataPage from '../../components/layout/NiondDataPage.vue';
import NiondTableCard from '../../components/layout/NiondTableCard.vue';
import NiondToolbar from '../../components/layout/NiondToolbar.vue';
import EmptyState from '../../components/states/EmptyState.vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiDataTable from '../../components/ui/UiDataTable.vue';
import UiDrawer from '../../components/ui/UiDrawer.vue';
import UiDrawerContent from '../../components/ui/UiDrawerContent.vue';
import UiInput from '../../components/ui/UiInput.vue';
import UiModal from '../../components/ui/UiModal.vue';
import UiPagination from '../../components/ui/UiPagination.vue';
import UiSelect from '../../components/ui/UiSelect.vue';
import UiTag from '../../components/ui/UiTag.vue';
import type { UiDataTableColumn } from '../../components/ui/types';

const authStore = useAuthStore();
const devices = ref<DeviceListItemDto[]>([]);
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
const canUpdateDevice = computed(() =>
  authStore.hasPermission(Permissions.Device.Update),
);
const canDeleteDevice = computed(() =>
  authStore.hasPermission(Permissions.Device.Delete),
);

const allProcesses = ref<ProcessSelectDto[]>([]);
const processNameMap = computed(() => {
  const map: Record<string, string> = {};
  for (const p of allProcesses.value) {
    map[p.id] = `${p.processCode} · ${p.processName}`;
  }
  return map;
});
const processOptions = computed(() =>
  allProcesses.value.map((p) => ({
    label: `${p.processCode} · ${p.processName}`,
    value: p.id,
  })),
);

const fetchProcesses = async () => {
  try {
    allProcesses.value = await getAllProcessesApi();
  } catch {
    allProcesses.value = [];
  }
};

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

const onPageChange = (p: number) => {
  currentPage.value = p;
  fetchList();
};

// === 复制工具 ===
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

// === 表格列 ===
const columns: UiDataTableColumn<DeviceListItemDto>[] = [
  {
    title: '设备名称',
    key: 'deviceName',
    minWidth: 180,
    render(row) {
      return h('span', { class: 'cell-name' }, row.deviceName);
    },
  },
  {
    title: 'Code',
    key: 'code',
    minWidth: 200,
    render(row) {
      return h('div', { class: 'cell-code-wrap' }, [
        h('code', { class: 'cell-code' }, row.code),
        h(
          UiButton,
          {
            size: 'tiny',
            quaternary: true,
            type: 'primary',
            onClick: (e: MouseEvent) => {
              e.stopPropagation();
              copyCode(row.code);
            },
          },
          { default: () => '复制' },
        ),
      ]);
    },
  },
  {
    title: '状态',
    key: 'status',
    width: 110,
    render() {
      return h(
        UiTag,
        { size: 'small', bordered: false, type: 'success' },
        { default: () => '已启用' },
      );
    },
  },
  {
    title: '所属工序',
    key: 'processId',
    minWidth: 180,
    render(row) {
      return h(
        'span',
        { class: 'cell-chip' },
        processNameMap.value[row.processId] || `${row.processId.slice(0, 8)}…`,
      );
    },
  },
  {
    title: '操作',
    key: 'actions',
    width: 240,
    align: 'right',
    render(row) {
      const actions = [
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

      if (canUpdateDevice.value) {
        actions.push(
          h(
            UiButton,
            {
              size: 'tiny',
              type: 'info',
              secondary: true,
              onClick: () => openEditModal(row),
            },
            { default: () => '编辑' },
          ),
          h(
            UiButton,
            {
              size: 'tiny',
              type: 'warning',
              secondary: true,
              onClick: () => handleRotateBootstrapSecret(row),
            },
            { default: () => '轮换密钥' },
          ),
        );
      }

      if (canDeleteDevice.value) {
        actions.push(
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
      }

      return h('div', { class: 'row-actions' }, actions);
    },
  },
];

const rowKey = (row: DeviceListItemDto) => row.id;

// === 新建设备 ===
const showRegisterModal = ref(false);
const registerForm = reactive({
  deviceName: '',
  processId: null as string | null,
});
const bootstrapSecretDialog = reactive({
  show: false,
  title: '',
  code: '',
  secret: '',
});

const openRegisterModal = async () => {
  registerForm.deviceName = '';
  registerForm.processId = null;
  await fetchProcesses();
  showRegisterModal.value = true;
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
    /* */
  } finally {
    submitting.value = false;
  }
};

const showBootstrapSecret = (title: string, code: string, secret: string) => {
  Object.assign(bootstrapSecretDialog, { show: true, title, code, secret });
};

// === 详情抽屉 ===
const showDetailPanel = ref(false);
const selectedDevice = ref<DeviceListItemDto | null>(null);

const openDetailPanel = (device: DeviceListItemDto) => {
  selectedDevice.value = device;
  showDetailPanel.value = true;
};

// === 编辑 ===
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
    /* */
  } finally {
    submitting.value = false;
  }
};

// === 通用确认 ===
const confirmDialog = reactive({
  show: false,
  title: '',
  desc: '',
  confirmText: '',
  danger: true,
  onConfirm: () => Promise.resolve(),
});

const handleDelete = (device: DeviceListItemDto) => {
  Object.assign(confirmDialog, {
    show: true,
    danger: true,
    title: '确认删除设备',
    desc: `设备【${device.deviceName}】删除后，现场保存的 Code 将无法继续寻址。若设备已有配方、产能、日志或过站数据，删除会被拒绝。`,
    confirmText: '确认删除',
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
        /* */
      } finally {
        submitting.value = false;
      }
    },
  });
};

const handleRotateBootstrapSecret = (device: DeviceListItemDto) => {
  Object.assign(confirmDialog, {
    show: true,
    danger: false,
    title: '确认轮换启动密钥',
    desc: `轮换后，设备【${device.deviceName}】旧启动密钥会立即失效。`,
    confirmText: '确认轮换',
    onConfirm: async () => {
      submitting.value = true;
      try {
        const rotated = await rotateDeviceBootstrapSecretApi(device.id);
        confirmDialog.show = false;
        showBootstrapSecret(
          '启动密钥已轮换',
          rotated.code,
          rotated.bootstrapSecret,
        );
      } catch {
        /* */
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
.device-page {
  font-family: var(--font-sans);
  color: var(--text-0);
}

.device-page__filter-card {
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
.device-page__table :deep(.cell-name) {
  font-size: var(--fs-base);
  font-weight: var(--fw-medium);
  color: var(--text-0);
}
.device-page__table :deep(.cell-code-wrap) {
  display: flex;
  align-items: center;
  gap: var(--space-2);
}
.device-page__table :deep(.cell-code) {
  font-family: var(--font-mono);
  font-size: var(--fs-sm);
  color: var(--brand);
  background: var(--brand-soft);
  padding: 2px 8px;
  border-radius: var(--radius-sm);
}
.device-page__table :deep(.cell-chip) {
  display: inline-block;
  font-size: var(--fs-sm);
  color: var(--text-1);
  background: var(--bg-3);
  padding: 2px 8px;
  border-radius: var(--radius-sm);
}
.device-page__table :deep(.row-actions) {
  display: flex;
  gap: var(--space-2);
  justify-content: flex-end;
  flex-wrap: nowrap;
}
.device-page__table :deep(.n-data-table-thead) {
  background: var(--bg-3);
}
.device-page__table :deep(.n-data-table-th) {
  font-size: var(--fs-xs) !important;
  font-weight: var(--fw-semibold) !important;
  color: var(--text-2) !important;
  letter-spacing: 0;
  text-transform: uppercase;
}
.device-page__table :deep(.n-data-table-tr:hover .n-data-table-td) {
  background-color: var(--bg-3) !important;
}

/* 表单 */
.form-stack {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}
.form-field {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}
.form-label {
  font-size: var(--fs-sm);
  font-weight: var(--fw-medium);
  color: var(--text-1);
}
.required {
  color: var(--error);
}
.modal-actions {
  display: flex;
  justify-content: flex-end;
  gap: var(--space-2);
}

/* hint card */
.hint-card {
  background: var(--brand-soft);
  border: 1px solid rgba(8, 145, 178, 0.2);
  border-radius: var(--radius-md);
  padding: var(--space-3) var(--space-4);
}
.hint-card--subtle {
  background: var(--bg-3);
  border-color: var(--border);
}
.hint-card__title {
  font-size: var(--fs-sm);
  font-weight: var(--fw-semibold);
  color: var(--text-0);
  margin-bottom: var(--space-1);
}
.hint-card__desc {
  font-size: var(--fs-sm);
  color: var(--text-1);
  line-height: 1.6;
}

/* 警告 */
.warning-card {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  background: var(--warn-soft);
  border: 1px solid rgba(217, 119, 6, 0.22);
  color: var(--warn);
  border-radius: var(--radius-md);
  padding: var(--space-3);
  font-size: var(--fs-sm);
  font-weight: var(--fw-medium);
}

/* 启动密钥行 */
.secret-row {
  display: flex;
  align-items: center;
  gap: var(--space-2);
}
.secret-row__value {
  flex: 1;
  font-family: var(--font-mono);
  font-size: var(--fs-sm);
  color: var(--brand);
  background: var(--brand-soft);
  border: 1px solid rgba(8, 145, 178, 0.18);
  padding: var(--space-2) var(--space-3);
  border-radius: var(--radius-sm);
  word-break: break-all;
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
.detail-row__value--mono {
  font-family: var(--font-mono);
}
.detail-row__value--brand {
  color: var(--brand);
}
.detail-row__value--small {
  font-size: var(--fs-xs);
  color: var(--text-2);
}
.detail-row__copy {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  flex-wrap: wrap;
}

.confirm-desc {
  font-size: var(--fs-base);
  color: var(--text-1);
  line-height: 1.6;
  margin: 0;
}
</style>
