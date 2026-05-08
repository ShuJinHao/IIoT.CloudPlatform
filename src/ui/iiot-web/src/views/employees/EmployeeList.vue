<template>
  <div class="employee-page">
    <PageHeader
      title="员工花名册"
      subtitle="管理车间操作人员档案与设备双维管辖权"
    >
      <template #actions>
        <n-button
          type="primary"
          v-permission="'Employee.Onboard'"
          @click="openOnboardModal"
        >
          <template #icon>
            <svg viewBox="0 0 16 16" fill="none">
              <path d="M8 2v12M2 8h12" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/>
            </svg>
          </template>
          员工入职
        </n-button>
      </template>
    </PageHeader>

    <CardSurface class="employee-page__filter-card">
      <div class="filter-row">
        <n-input
          v-model:value="keyword"
          placeholder="搜索工号或姓名..."
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
        </n-input>
        <n-tag round :bordered="false" size="small">共 {{ metaData.totalCount }} 人</n-tag>
      </div>
    </CardSurface>

    <CardSurface class="employee-page__table-card" no-padding>
      <n-data-table
        class="employee-page__table"
        :columns="columns"
        :data="employees"
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
          :page-size="10"
          show-quick-jumper
          @update:page="onPageChange"
        />
      </div>
    </CardSurface>

    <!-- 入职 modal -->
    <n-modal
      v-model:show="showOnboardModal"
      preset="card"
      title="员工入职建档"
      style="width: 640px;"
      :mask-closable="false"
    >
      <div class="form-stack">
        <div class="form-section-label">基础信息</div>
        <div class="form-grid form-grid--2">
          <div class="form-field">
            <label class="form-label">工号 <span class="required">*</span></label>
            <n-input v-model:value="onboardForm.EmployeeNo" placeholder="如：A10086" />
          </div>
          <div class="form-field">
            <label class="form-label">姓名 <span class="required">*</span></label>
            <n-input v-model:value="onboardForm.RealName" placeholder="真实姓名" />
          </div>
          <div class="form-field">
            <label class="form-label">初始密码 <span class="required">*</span></label>
            <n-input
              v-model:value="onboardForm.Password"
              type="password"
              show-password-on="click"
              placeholder="至少 8 位，含大小写和数字"
            />
          </div>
          <div class="form-field">
            <label class="form-label">系统角色</label>
            <n-select
              v-model:value="onboardForm.RoleName"
              :options="roleOptions"
              placeholder="不分配角色"
              clearable
            />
          </div>
        </div>
        <div class="form-section-label">设备授权说明</div>
        <p class="form-hint">
          员工入职只创建账号、员工档案和初始角色。设备授权请在入职完成后，通过"管辖权配置"单独维护。
        </p>
      </div>
      <template #footer>
        <div class="modal-actions">
          <n-button @click="showOnboardModal = false">取消</n-button>
          <n-button
            type="primary"
            :loading="submitting"
            @click="submitOnboard"
          >
            确认入职
          </n-button>
        </div>
      </template>
    </n-modal>

    <!-- 编辑档案 modal -->
    <n-modal
      v-model:show="showEditModal"
      preset="card"
      title="编辑员工档案"
      style="width: 480px;"
      :mask-closable="false"
    >
      <div class="form-stack">
        <div class="form-field">
          <label class="form-label">工号</label>
          <n-input :value="editTarget?.employeeNo" disabled />
        </div>
        <div class="form-field">
          <label class="form-label">姓名 <span class="required">*</span></label>
          <n-input v-model:value="editForm.RealName" />
        </div>
        <div class="form-field">
          <label class="form-label">账号状态</label>
          <div class="toggle-row">
            <n-switch v-model:value="editForm.IsActive" />
            <span class="toggle-label">{{ editForm.IsActive ? '启用' : '停用' }}</span>
          </div>
        </div>
      </div>
      <template #footer>
        <div class="modal-actions">
          <n-button @click="showEditModal = false">取消</n-button>
          <n-button
            type="primary"
            :loading="submitting"
            @click="submitEdit"
          >
            保存修改
          </n-button>
        </div>
      </template>
    </n-modal>

    <!-- 管辖权 modal -->
    <n-modal
      v-model:show="showAccessModal"
      preset="card"
      title="配置设备管辖权"
      style="width: 580px;"
      :mask-closable="false"
    >
      <div class="form-stack">
        <LoadingState v-if="accessLoading" :rows="4" />
        <div v-else>
          <div class="access-header">
            <span class="access-header__title">可访问设备</span>
            <span class="access-header__hint">
              当前已分配 {{ accessForm.DeviceIds.length }} 台
            </span>
          </div>
          <div v-if="allDevices.length > 0" class="access-list">
            <n-checkbox
              v-for="d in allDevices"
              :key="d.id"
              :checked="accessForm.DeviceIds.includes(d.id)"
              @update:checked="(checked: boolean) => toggleDeviceAccess(d.id, checked)"
            >
              {{ d.deviceName }}
            </n-checkbox>
          </div>
          <EmptyState v-else title="暂无设备数据" />
        </div>
      </div>
      <template #footer>
        <div class="modal-actions">
          <n-button @click="showAccessModal = false">关闭</n-button>
          <n-button
            type="primary"
            :loading="submitting"
            @click="submitAccess"
          >
            保存管辖权
          </n-button>
        </div>
      </template>
    </n-modal>

    <!-- 详情 modal -->
    <n-modal
      v-model:show="showDetailModal"
      preset="card"
      title="员工档案详情"
      style="width: 560px;"
      :mask-closable="true"
    >
      <div v-if="detailData" class="detail-grid">
        <div class="detail-row">
          <span class="detail-row__label">工号</span>
          <span class="detail-row__value detail-row__value--mono detail-row__value--brand">
            {{ detailData.employeeNo }}
          </span>
        </div>
        <div class="detail-row">
          <span class="detail-row__label">姓名</span>
          <span class="detail-row__value">{{ detailData.realName }}</span>
        </div>
        <div class="detail-row">
          <span class="detail-row__label">状态</span>
          <n-tag
            size="small"
            :bordered="false"
            :type="detailData.isActive ? 'success' : 'default'"
          >
            {{ detailData.isActive ? '在职' : '停用' }}
          </n-tag>
        </div>
        <div class="detail-row">
          <span class="detail-row__label">系统 ID</span>
          <span class="detail-row__value detail-row__value--mono detail-row__value--small">
            {{ detailData.id }}
          </span>
        </div>
        <div class="detail-row detail-row--full">
          <span class="detail-row__label">机台管辖</span>
          <div v-if="detailData.deviceIds.length" class="detail-chips">
            <n-tag
              v-for="id in detailData.deviceIds"
              :key="id"
              size="small"
              :bordered="false"
              type="info"
            >
              {{ deviceNameMap[id] || id.substring(0, 8) + '…' }}
            </n-tag>
          </div>
          <span v-else class="detail-row__value detail-row__value--muted">未分配</span>
        </div>
      </div>
      <template #footer>
        <div class="modal-actions">
          <n-button @click="showDetailModal = false">关闭</n-button>
        </div>
      </template>
    </n-modal>

    <!-- 重置密码 modal -->
    <n-modal
      v-model:show="showResetPwdModal"
      preset="card"
      title="重置员工密码"
      style="width: 440px;"
      :mask-closable="false"
    >
      <div class="form-stack">
        <div class="reset-target">
          <span class="reset-target__label">目标员工</span>
          <span class="reset-target__value">
            {{ resetPwdTarget?.realName }}（{{ resetPwdTarget?.employeeNo }}）
          </span>
        </div>
        <div class="form-field">
          <label class="form-label">新密码 <span class="required">*</span></label>
          <n-input
            v-model:value="resetPwdForm.newPwd"
            type="password"
            show-password-on="click"
            placeholder="至少 8 位，含大小写和数字"
          />
        </div>
        <div class="form-field">
          <label class="form-label">确认新密码 <span class="required">*</span></label>
          <n-input
            v-model:value="resetPwdForm.confirm"
            type="password"
            show-password-on="click"
            placeholder="再次输入新密码"
          />
        </div>
      </div>
      <template #footer>
        <div class="modal-actions">
          <n-button @click="showResetPwdModal = false">取消</n-button>
          <n-button
            type="primary"
            :loading="submitting"
            @click="submitResetPwd"
          >
            确认重置
          </n-button>
        </div>
      </template>
    </n-modal>

    <!-- 特批权限 modal -->
    <n-modal
      v-model:show="showPersonalPermModal"
      preset="card"
      style="width: 720px;"
      :mask-closable="false"
    >
      <template #header>
        <div class="modal-header-stack">
          <div class="modal-header-title">个人特批权限</div>
          <div class="modal-header-sub">
            {{ personalPermTarget?.realName }}（{{ personalPermTarget?.employeeNo }}）· 与角色权限是并集关系
          </div>
        </div>
      </template>
      <LoadingState v-if="personalPermLoading" :rows="6" />
      <div v-else class="perm-selector">
        <div
          v-for="group in permissionGroups"
          :key="group.groupName"
          class="perm-group"
        >
          <div class="perm-group__title">{{ group.groupName }}</div>
          <div class="perm-group__items">
            <n-checkbox
              v-for="perm in group.permissions"
              :key="perm"
              :checked="personalPermForm.includes(perm)"
              @update:checked="(checked: boolean) => togglePersonalPerm(perm, checked)"
            >
              {{ perm }}
            </n-checkbox>
          </div>
        </div>
      </div>
      <template #footer>
        <div class="modal-actions">
          <n-button @click="showPersonalPermModal = false">取消</n-button>
          <n-button
            type="primary"
            :loading="submitting"
            @click="submitPersonalPerm"
          >
            保存特批权限
          </n-button>
        </div>
      </template>
    </n-modal>

    <!-- 通用确认 modal -->
    <n-modal
      v-model:show="confirmDialog.show"
      preset="card"
      :title="confirmDialog.title"
      style="width: 480px;"
      :mask-closable="false"
    >
      <p class="confirm-desc">{{ confirmDialog.desc }}</p>
      <template #footer>
        <div class="modal-actions">
          <n-button @click="confirmDialog.show = false">取消</n-button>
          <n-button
            type="error"
            :loading="submitting"
            @click="confirmDialog.onConfirm()"
          >
            {{ confirmDialog.confirmText }}
          </n-button>
        </div>
      </template>
    </n-modal>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, h, onMounted } from 'vue';
import {
  NButton,
  NInput,
  NSelect,
  NSwitch,
  NCheckbox,
  NDataTable,
  NPagination,
  NModal,
  NTag,
} from 'naive-ui';
import type { DataTableColumns } from 'naive-ui';
import {
  getEmployeePagedListApi,
  getEmployeeDetailApi,
  getEmployeeAccessApi,
  onboardEmployeeApi,
  updateEmployeeProfileApi,
  updateEmployeeAccessApi,
  deactivateEmployeeApi,
  terminateEmployeeApi,
  getAllRolesApi,
  type EmployeeListItemDto,
  type EmployeeDetailDto,
  type PagedMetaData,
} from '../../api/employee';
import { getAllActiveDevicesApi, type DeviceSelectDto } from '../../api/device';
import {
  resetPasswordApi,
  getUserPersonalPermissionsApi,
  updateUserPermissionsApi,
  getAllDefinedPermissionsApi,
  type PermissionGroupDto,
} from '../../api/identity';
import PageHeader from '../../components/layout/PageHeader.vue';
import CardSurface from '../../components/layout/CardSurface.vue';
import LoadingState from '../../components/states/LoadingState.vue';
import EmptyState from '../../components/states/EmptyState.vue';

const employees = ref<EmployeeListItemDto[]>([]);
const loading = ref(false);
const keyword = ref('');
const currentPage = ref(1);
const metaData = ref<PagedMetaData>({
  totalCount: 0,
  pageSize: 10,
  currentPage: 1,
  totalPages: 1,
});
const availableRoles = ref<string[]>([]);
const submitting = ref(false);

const allDevices = ref<DeviceSelectDto[]>([]);

const deviceNameMap = computed(() => {
  const m: Record<string, string> = {};
  for (const d of allDevices.value) m[d.id] = d.deviceName;
  return m;
});
const roleOptions = computed(() =>
  availableRoles.value.map((r) => ({ label: r, value: r })),
);

const fetchSelectData = async () => {
  try {
    allDevices.value = await getAllActiveDevicesApi();
  } catch {
    allDevices.value = [];
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
    const response = await getEmployeePagedListApi({
      PaginationParams: { PageNumber: currentPage.value, PageSize: 10 },
      Keyword: keyword.value || undefined,
    });
    metaData.value = response.metaData;
    employees.value = response.items;
  } catch {
    employees.value = [];
  } finally {
    loading.value = false;
  }
};

const onPageChange = (p: number) => {
  currentPage.value = p;
  fetchList();
};

// === 表格列 ===
const columns: DataTableColumns<EmployeeListItemDto> = [
  {
    title: '工号',
    key: 'employeeNo',
    width: 140,
    render(row) {
      return h('span', { class: 'cell-emp-no' }, row.employeeNo);
    },
  },
  {
    title: '姓名',
    key: 'realName',
    minWidth: 140,
    render(row) {
      return h('span', { class: 'cell-name' }, row.realName);
    },
  },
  {
    title: '状态',
    key: 'isActive',
    width: 100,
    render(row) {
      return h(
        NTag,
        {
          size: 'small',
          bordered: false,
          type: row.isActive ? 'success' : 'default',
        },
        { default: () => (row.isActive ? '在职' : '停用') },
      );
    },
  },
  {
    title: '设备管辖',
    key: 'deviceCount',
    width: 110,
    render(row) {
      return h(
        'span',
        { class: 'cell-mono cell-count' },
        `${row.deviceCount} 台`,
      );
    },
  },
  {
    title: '操作',
    key: 'actions',
    width: 320,
    align: 'right',
    render(row) {
      return h('div', { class: 'row-actions' }, [
        h(
          NButton,
          {
            size: 'tiny',
            type: 'primary',
            secondary: true,
            onClick: () => openDetailModal(row.id),
          },
          { default: () => '详情' },
        ),
        h(
          NButton,
          {
            size: 'tiny',
            type: 'info',
            secondary: true,
            onClick: () => openEditModal(row),
          },
          { default: () => '编辑' },
        ),
        h(
          NButton,
          {
            size: 'tiny',
            type: 'warning',
            secondary: true,
            onClick: () => openResetPwdModal(row),
          },
          { default: () => '重置密码' },
        ),
        h(
          NButton,
          {
            size: 'tiny',
            type: 'primary',
            secondary: true,
            onClick: () => openAccessModal(row.id),
          },
          { default: () => '管辖权' },
        ),
        h(
          NButton,
          {
            size: 'tiny',
            type: 'info',
            secondary: true,
            onClick: () => openPersonalPermModal(row),
          },
          { default: () => '特批权限' },
        ),
        row.isActive
          ? h(
              NButton,
              {
                size: 'tiny',
                type: 'warning',
                secondary: true,
                onClick: () => handleDeactivate(row),
              },
              { default: () => '停用' },
            )
          : null,
        h(
          NButton,
          {
            size: 'tiny',
            type: 'error',
            secondary: true,
            onClick: () => handleTerminate(row),
          },
          { default: () => '离职' },
        ),
      ]);
    },
  },
];

const rowKey = (row: EmployeeListItemDto) => row.id;

// === 入职 ===
const showOnboardModal = ref(false);
const onboardForm = reactive({
  EmployeeNo: '',
  RealName: '',
  Password: '',
  RoleName: null as string | null,
});

const openOnboardModal = async () => {
  Object.assign(onboardForm, {
    EmployeeNo: '',
    RealName: '',
    Password: '',
    RoleName: null,
  });
  showOnboardModal.value = true;
  try {
    const roles = await getAllRolesApi();
    availableRoles.value = roles.filter((r) => r !== 'Admin');
  } catch {
    availableRoles.value = [];
  }
};

const submitOnboard = async () => {
  if (
    !onboardForm.EmployeeNo.trim() ||
    !onboardForm.RealName.trim() ||
    !onboardForm.Password.trim()
  ) {
    alert('工号、姓名和初始密码为必填项');
    return;
  }
  submitting.value = true;
  try {
    await onboardEmployeeApi({
      employeeNo: onboardForm.EmployeeNo,
      realName: onboardForm.RealName,
      password: onboardForm.Password,
      roleName: onboardForm.RoleName || undefined,
    });
    showOnboardModal.value = false;
    fetchList();
  } catch {
    /* */
  } finally {
    submitting.value = false;
  }
};

// === 编辑 ===
const showEditModal = ref(false);
const editTarget = ref<EmployeeListItemDto | null>(null);
const editForm = reactive({ RealName: '', IsActive: true });

const openEditModal = (emp: EmployeeListItemDto) => {
  editTarget.value = emp;
  editForm.RealName = emp.realName;
  editForm.IsActive = emp.isActive;
  showEditModal.value = true;
};

const submitEdit = async () => {
  if (!editTarget.value || !editForm.RealName.trim()) {
    alert('姓名不能为空');
    return;
  }
  submitting.value = true;
  try {
    await updateEmployeeProfileApi(editTarget.value.id, {
      employeeId: editTarget.value.id,
      realName: editForm.RealName,
      isActive: editForm.IsActive,
    });
    showEditModal.value = false;
    fetchList();
  } catch {
    /* */
  } finally {
    submitting.value = false;
  }
};

// === 管辖权 ===
const showAccessModal = ref(false);
const accessLoading = ref(false);
const accessTargetId = ref('');
const accessForm = reactive({ DeviceIds: [] as string[] });

const openAccessModal = async (id: string) => {
  accessTargetId.value = id;
  accessLoading.value = true;
  showAccessModal.value = true;
  await fetchSelectData();
  try {
    const access = await getEmployeeAccessApi(id);
    accessForm.DeviceIds = [...access.deviceIds];
  } catch {
    accessForm.DeviceIds = [];
  } finally {
    accessLoading.value = false;
  }
};

const toggleDeviceAccess = (deviceId: string, checked: boolean) => {
  if (checked) {
    if (!accessForm.DeviceIds.includes(deviceId)) {
      accessForm.DeviceIds.push(deviceId);
    }
  } else {
    const idx = accessForm.DeviceIds.indexOf(deviceId);
    if (idx > -1) accessForm.DeviceIds.splice(idx, 1);
  }
};

const submitAccess = async () => {
  submitting.value = true;
  try {
    await updateEmployeeAccessApi(accessTargetId.value, {
      employeeId: accessTargetId.value,
      deviceIds: accessForm.DeviceIds,
    });
    showAccessModal.value = false;
    fetchList();
  } catch {
    /* */
  } finally {
    submitting.value = false;
  }
};

// === 详情 ===
const showDetailModal = ref(false);
const detailData = ref<EmployeeDetailDto | null>(null);

const openDetailModal = async (id: string) => {
  try {
    detailData.value = await getEmployeeDetailApi(id);
    showDetailModal.value = true;
  } catch {
    /* */
  }
};

// === 重置密码 ===
const showResetPwdModal = ref(false);
const resetPwdTarget = ref<EmployeeListItemDto | null>(null);
const resetPwdForm = reactive({ newPwd: '', confirm: '' });

const openResetPwdModal = (emp: EmployeeListItemDto) => {
  resetPwdTarget.value = emp;
  resetPwdForm.newPwd = '';
  resetPwdForm.confirm = '';
  showResetPwdModal.value = true;
};

const submitResetPwd = async () => {
  if (!resetPwdTarget.value) return;
  if (!resetPwdForm.newPwd || !resetPwdForm.confirm) {
    alert('请输入新密码');
    return;
  }
  if (resetPwdForm.newPwd !== resetPwdForm.confirm) {
    alert('两次输入的密码不一致');
    return;
  }
  submitting.value = true;
  try {
    await resetPasswordApi({
      userId: resetPwdTarget.value.id,
      newPassword: resetPwdForm.newPwd,
    });
    showResetPwdModal.value = false;
    alert('密码重置成功');
  } catch {
    /* */
  } finally {
    submitting.value = false;
  }
};

// === 特批权限 ===
const showPersonalPermModal = ref(false);
const personalPermTarget = ref<EmployeeListItemDto | null>(null);
const personalPermLoading = ref(false);
const personalPermForm = ref<string[]>([]);
const permissionGroups = ref<PermissionGroupDto[]>([]);

const openPersonalPermModal = async (emp: EmployeeListItemDto) => {
  personalPermTarget.value = emp;
  personalPermLoading.value = true;
  personalPermForm.value = [];
  showPersonalPermModal.value = true;
  try {
    const [groups, currentPerms] = await Promise.all([
      getAllDefinedPermissionsApi() as unknown as Promise<PermissionGroupDto[]>,
      getUserPersonalPermissionsApi(emp.id) as unknown as Promise<string[]>,
    ]);
    permissionGroups.value = groups;
    personalPermForm.value = [...currentPerms];
  } catch {
    permissionGroups.value = [];
    personalPermForm.value = [];
  } finally {
    personalPermLoading.value = false;
  }
};

const togglePersonalPerm = (perm: string, checked: boolean) => {
  if (checked) {
    if (!personalPermForm.value.includes(perm)) {
      personalPermForm.value.push(perm);
    }
  } else {
    const idx = personalPermForm.value.indexOf(perm);
    if (idx > -1) personalPermForm.value.splice(idx, 1);
  }
};

const submitPersonalPerm = async () => {
  if (!personalPermTarget.value) return;
  submitting.value = true;
  try {
    await updateUserPermissionsApi(personalPermTarget.value.id, {
      userId: personalPermTarget.value.id,
      permissions: personalPermForm.value,
    });
    showPersonalPermModal.value = false;
    alert('特批权限保存成功，员工重新登录后生效');
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
  onConfirm: () => Promise.resolve(),
});

const handleDeactivate = (emp: EmployeeListItemDto) => {
  Object.assign(confirmDialog, {
    show: true,
    title: '停用员工',
    desc: `确定要停用「${emp.realName}（${emp.employeeNo}）」吗？停用后该员工将无法登录，档案数据保留。`,
    confirmText: '确认停用',
    onConfirm: async () => {
      submitting.value = true;
      try {
        await deactivateEmployeeApi(emp.id);
        confirmDialog.show = false;
        fetchList();
      } finally {
        submitting.value = false;
      }
    },
  });
};

const handleTerminate = (emp: EmployeeListItemDto) => {
  Object.assign(confirmDialog, {
    show: true,
    title: '⚠ 员工离职销户（不可撤销）',
    desc: `即将永久删除「${emp.realName}（${emp.employeeNo}）」的所有档案，含身份账号与权限数据，此操作不可撤销！`,
    confirmText: '确认离职销户',
    onConfirm: async () => {
      submitting.value = true;
      try {
        await terminateEmployeeApi(emp.id);
        confirmDialog.show = false;
        fetchList();
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
</script>

<style scoped>
.employee-page {
  font-family: var(--font-sans);
  color: var(--text-0);
}

.employee-page__filter-card {
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
.employee-page__table :deep(.cell-emp-no) {
  font-family: var(--font-mono);
  font-size: var(--fs-base);
  color: var(--brand);
  background: var(--brand-soft);
  padding: 2px 8px;
  border-radius: var(--radius-sm);
  font-weight: var(--fw-semibold);
}
.employee-page__table :deep(.cell-name) {
  font-size: var(--fs-base);
  font-weight: var(--fw-medium);
  color: var(--text-0);
}
.employee-page__table :deep(.cell-mono) {
  font-family: var(--font-mono);
}
.employee-page__table :deep(.cell-count) {
  color: var(--text-1);
  font-size: var(--fs-sm);
}
.employee-page__table :deep(.row-actions) {
  display: flex;
  gap: var(--space-2);
  justify-content: flex-end;
  flex-wrap: wrap;
}
.employee-page__table :deep(.n-data-table-thead) {
  background: var(--bg-3);
}
.employee-page__table :deep(.n-data-table-th) {
  font-size: var(--fs-xs) !important;
  font-weight: var(--fw-semibold) !important;
  color: var(--text-2) !important;
  letter-spacing: 1px;
  text-transform: uppercase;
}
.employee-page__table :deep(.n-data-table-tr:hover .n-data-table-td) {
  background-color: rgba(8, 145, 178, 0.04) !important;
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
  font-size: var(--fs-sm);
  font-weight: var(--fw-medium);
  color: var(--text-1);
}
.form-section-label {
  font-size: var(--fs-xs);
  font-weight: var(--fw-semibold);
  color: var(--text-2);
  text-transform: uppercase;
  letter-spacing: 1px;
}
.form-hint {
  font-size: var(--fs-sm);
  color: var(--text-2);
  margin: 0;
  line-height: 1.6;
}
.required {
  color: var(--error);
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

/* Toggle */
.toggle-row {
  display: flex;
  align-items: center;
  gap: var(--space-3);
}
.toggle-label {
  font-size: var(--fs-sm);
  color: var(--text-1);
}

/* 管辖权 */
.access-header {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  margin-bottom: var(--space-3);
}
.access-header__title {
  font-size: var(--fs-md);
  font-weight: var(--fw-semibold);
  color: var(--text-0);
}
.access-header__hint {
  font-size: var(--fs-xs);
  color: var(--text-2);
}
.access-list {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
  gap: var(--space-2) var(--space-4);
  max-height: 360px;
  overflow-y: auto;
  padding: var(--space-2);
  background: var(--bg-3);
  border-radius: var(--radius-sm);
}

/* 重置密码目标信息 */
.reset-target {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
  background: var(--warn-soft);
  border: 1px solid rgba(217, 119, 6, 0.18);
  border-radius: var(--radius-sm);
  padding: var(--space-3);
}
.reset-target__label {
  font-size: var(--fs-xs);
  color: var(--text-2);
  letter-spacing: 0.5px;
}
.reset-target__value {
  font-size: var(--fs-base);
  color: var(--text-0);
  font-weight: var(--fw-medium);
}

/* 详情 */
.detail-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: var(--space-4) var(--space-5);
}
.detail-row {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
}
.detail-row--full {
  grid-column: 1 / -1;
}
.detail-row__label {
  font-size: var(--fs-xs);
  color: var(--text-2);
  text-transform: uppercase;
  letter-spacing: 0.8px;
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
  font-weight: var(--fw-semibold);
}
.detail-row__value--small {
  font-size: var(--fs-xs);
  color: var(--text-2);
}
.detail-row__value--muted {
  color: var(--text-2);
}
.detail-chips {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-2);
}

/* 特批权限 */
.perm-selector {
  display: flex;
  flex-direction: column;
  gap: var(--space-5);
  max-height: 400px;
  overflow-y: auto;
}
.perm-group__title {
  font-size: var(--fs-xs);
  font-weight: var(--fw-semibold);
  color: var(--text-2);
  text-transform: uppercase;
  letter-spacing: 1px;
  margin-bottom: var(--space-2);
  padding-bottom: var(--space-2);
  border-bottom: 1px solid var(--border);
}
.perm-group__items {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
  gap: var(--space-2);
}

.confirm-desc {
  font-size: var(--fs-base);
  color: var(--text-1);
  line-height: 1.6;
  margin: 0;
}
</style>
