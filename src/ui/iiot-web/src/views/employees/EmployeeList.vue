<template>
  <div class="employee-list">
    <!-- 页头 -->
    <div class="page-header">
      <div>
        <h1 class="page-title">员工花名册</h1>
        <p class="page-sub">管理车间操作人员档案与双维数据管辖权</p>
      </div>
      <button class="btn btn-primary" v-permission="'Employee.Onboard'" @click="openOnboardModal">
        <svg viewBox="0 0 16 16" fill="none"><path d="M8 2v12M2 8h12" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/></svg>
        员工入职
      </button>
    </div>

    <!-- 搜索栏 -->
    <div class="toolbar">
      <div class="search-wrap">
        <svg viewBox="0 0 16 16" fill="none"><circle cx="6.5" cy="6.5" r="4.5" stroke="currentColor" stroke-width="1.3"/><path d="M10 10l3 3" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/></svg>
        <input v-model="keyword" placeholder="搜索工号或姓名..." @keyup.enter="fetchList" @input="onSearchInput" />
        <button v-if="keyword" class="clear-btn" @click="keyword=''; fetchList()">✕</button>
      </div>
      <span class="total-badge">共 {{ metaData.totalCount }} 人</span>
    </div>

    <!-- 表格 -->
    <div class="table-wrap">
      <div v-if="loading" class="skeleton-rows">
        <div v-for="i in 5" :key="i" class="skeleton-row">
          <div class="skel skel-sm"></div><div class="skel skel-md"></div>
          <div class="skel skel-md"></div><div class="skel skel-sm"></div><div class="skel skel-lg"></div>
        </div>
      </div>
      <table v-else class="data-table">
        <thead>
          <tr>
            <th>工号</th><th>姓名</th><th>状态</th><th>工序管辖</th><th>机台管辖</th>
            <th style="text-align:right">操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-if="employees.length === 0">
            <td colspan="6" class="empty-cell">
              <div class="empty-state">
                <svg viewBox="0 0 48 48" fill="none"><rect x="8" y="12" width="32" height="28" rx="3" stroke="currentColor" stroke-width="1.5" opacity="0.3"/><path d="M16 20h16M16 28h10" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" opacity="0.3"/></svg>
                <p>暂无员工档案</p>
              </div>
            </td>
          </tr>
          <tr v-for="emp in employees" :key="emp.id" class="table-row" @click="openDetailModal(emp.id)">
            <td><span class="employee-no">{{ emp.employeeNo }}</span></td>
            <td><span class="real-name">{{ emp.realName }}</span></td>
            <td>
              <span class="status-tag" :class="emp.isActive ? 'active' : 'inactive'">
                <span class="status-dot"></span>{{ emp.isActive ? '在职' : '停用' }}
              </span>
            </td>
            <td><span class="access-badge">{{ emp.processCount }} 项</span></td>
            <td><span class="access-badge device">{{ emp.deviceCount }} 台</span></td>
            <td class="action-cell" @click.stop>
              <button class="icon-btn edit" title="编辑档案" v-permission="'Employee.Update'" @click="openEditModal(emp)">
                <svg viewBox="0 0 16 16" fill="none"><path d="M11.5 2.5l2 2-8 8H3.5v-2l8-8z" stroke="currentColor" stroke-width="1.2" stroke-linejoin="round"/></svg>
              </button>
              <button class="icon-btn access" title="管辖权配置" v-permission="'Employee.Update'" @click="openAccessModal(emp.id)">
                <svg viewBox="0 0 16 16" fill="none"><circle cx="8" cy="5" r="2.5" stroke="currentColor" stroke-width="1.2"/><path d="M3 13c0-2.761 2.239-5 5-5s5 2.239 5 5" stroke="currentColor" stroke-width="1.2" stroke-linecap="round"/><path d="M12 9l1.5 1.5L16 8" stroke="currentColor" stroke-width="1.2" stroke-linecap="round" stroke-linejoin="round"/></svg>
              </button>
              <button v-if="emp.isActive" class="icon-btn deactivate" title="停用" v-permission="'Employee.Deactivate'" @click="handleDeactivate(emp)">
                <svg viewBox="0 0 16 16" fill="none"><circle cx="8" cy="8" r="5.5" stroke="currentColor" stroke-width="1.2"/><path d="M5.5 8h5" stroke="currentColor" stroke-width="1.2" stroke-linecap="round"/></svg>
              </button>
              <button class="icon-btn terminate" title="离职销户" v-permission="'Employee.Terminate'" @click="handleTerminate(emp)">
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
      <button v-for="p in pageNumbers" :key="p" class="page-btn" :class="{active:p===currentPage}" @click="goPage(p)">{{ p }}</button>
      <button class="page-btn" :disabled="currentPage===metaData.totalPages" @click="goPage(currentPage+1)">
        <svg viewBox="0 0 12 12" fill="none"><path d="M4 2l4 4-4 4" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/></svg>
      </button>
    </div>

    <!-- 入职弹窗 -->
    <Teleport to="body">
      <div v-if="showOnboardModal" class="modal-overlay" @click.self="showOnboardModal=false">
        <div class="modal">
          <div class="modal-header">
            <span class="modal-title">员工入职建档</span>
            <button class="modal-close" @click="showOnboardModal=false">✕</button>
          </div>
          <div class="modal-body">
            <div class="form-section-label">基础信息</div>
            <div class="form-row">
              <div class="form-field">
                <label>工号 <span class="required">*</span></label>
                <input v-model="onboardForm.EmployeeNo" placeholder="如：A10086" />
              </div>
              <div class="form-field">
                <label>姓名 <span class="required">*</span></label>
                <input v-model="onboardForm.RealName" placeholder="真实姓名" />
              </div>
            </div>
            <div class="form-row">
              <div class="form-field">
                <label>初始密码 <span class="required">*</span></label>
                <input v-model="onboardForm.Password" type="password" placeholder="至少8位，含大小写和数字" />
              </div>
              <div class="form-field">
                <label>系统角色</label>
                <select v-model="onboardForm.RoleName">
                  <option value="">不分配角色</option>
                  <option v-for="role in availableRoles" :key="role" :value="role">{{ role }}</option>
                </select>
              </div>
            </div>
            <div class="form-section-label" style="margin-top:20px">
              双维管辖权 <span class="section-hint">（可选，入职后也可单独配置）</span>
            </div>
            <div class="dual-access-grid">
              <div class="access-panel">
                <div class="access-panel-title">工序管辖（粗颗粒）</div>
                <div class="access-hint">选中工序后，员工可访问该工序下所有通用配方</div>
                <div class="access-placeholder"><span class="coming-soon">📋 工序选择器待后续迭代完善</span></div>
              </div>
              <div class="access-panel">
                <div class="access-panel-title">机台管辖（精细颗粒）</div>
                <div class="access-hint">选中具体机台后，员工可访问该机台的专属配方</div>
                <div class="access-placeholder"><span class="coming-soon">🔧 设备列表将在第5步联动</span></div>
              </div>
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn btn-ghost" @click="showOnboardModal=false">取消</button>
            <button class="btn btn-primary" :disabled="submitting" @click="submitOnboard">{{ submitting?'建档中...':'确认入职' }}</button>
          </div>
        </div>
      </div>
    </Teleport>

    <!-- 编辑弹窗 -->
    <Teleport to="body">
      <div v-if="showEditModal" class="modal-overlay" @click.self="showEditModal=false">
        <div class="modal modal-sm">
          <div class="modal-header">
            <span class="modal-title">编辑员工档案</span>
            <button class="modal-close" @click="showEditModal=false">✕</button>
          </div>
          <div class="modal-body">
            <div class="form-field"><label>工号</label><input :value="editTarget?.employeeNo" disabled class="disabled-input" /></div>
            <div class="form-field"><label>姓名 <span class="required">*</span></label><input v-model="editForm.RealName" /></div>
            <div class="form-field">
              <label>账号状态</label>
              <div class="toggle-row">
                <label class="toggle"><input type="checkbox" v-model="editForm.IsActive" /><span class="toggle-slider"></span></label>
                <span class="toggle-label">{{ editForm.IsActive?'启用':'停用' }}</span>
              </div>
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn btn-ghost" @click="showEditModal=false">取消</button>
            <button class="btn btn-primary" :disabled="submitting" @click="submitEdit">{{ submitting?'保存中...':'保存修改' }}</button>
          </div>
        </div>
      </div>
    </Teleport>

    <!-- 管辖权弹窗 -->
    <Teleport to="body">
      <div v-if="showAccessModal" class="modal-overlay" @click.self="showAccessModal=false">
        <div class="modal">
          <div class="modal-header">
            <span class="modal-title">配置双维管辖权</span>
            <button class="modal-close" @click="showAccessModal=false">✕</button>
          </div>
          <div class="modal-body">
            <div class="access-loading" v-if="accessLoading">加载数据中...</div>
            <div v-else class="dual-access-grid">
              <div class="access-panel">
                <div class="access-panel-title">工序管辖（粗颗粒）</div>
                <div class="access-hint">当前已分配 {{ accessForm.ProcessIds.length }} 个工序</div>
                <div class="access-placeholder"><span class="coming-soon">📋 工序选择器待后续迭代完善</span></div>
              </div>
              <div class="access-panel">
                <div class="access-panel-title">机台管辖（精细颗粒）</div>
                <div class="access-hint">当前已分配 {{ accessForm.DeviceIds.length }} 台机台</div>
                <div class="access-placeholder"><span class="coming-soon">🔧 设备列表将在第5步联动</span></div>
              </div>
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn btn-ghost" @click="showAccessModal=false">关闭</button>
            <button class="btn btn-primary" :disabled="submitting" @click="submitAccess">{{ submitting?'保存中...':'保存管辖权' }}</button>
          </div>
        </div>
      </div>
    </Teleport>

    <!-- 详情弹窗 -->
    <Teleport to="body">
      <div v-if="showDetailModal" class="modal-overlay" @click.self="showDetailModal=false">
        <div class="modal modal-sm">
          <div class="modal-header">
            <span class="modal-title">员工档案详情</span>
            <button class="modal-close" @click="showDetailModal=false">✕</button>
          </div>
          <div class="modal-body" v-if="detailData">
            <div class="detail-grid">
              <div class="detail-item"><span class="detail-label">工号</span><span class="detail-value emp-no">{{ detailData.employeeNo }}</span></div>
              <div class="detail-item"><span class="detail-label">姓名</span><span class="detail-value">{{ detailData.realName }}</span></div>
              <div class="detail-item"><span class="detail-label">状态</span>
                <span class="status-tag" :class="detailData.isActive?'active':'inactive'">
                  <span class="status-dot"></span>{{ detailData.isActive?'在职':'停用' }}
                </span>
              </div>
              <div class="detail-item"><span class="detail-label">系统ID</span><span class="detail-value id-text">{{ detailData.id }}</span></div>
              <div class="detail-item full"><span class="detail-label">工序管辖</span>
                <div class="id-chips" v-if="detailData.processIds.length">
                  <span v-for="id in detailData.processIds" :key="id" class="id-chip">{{ id.substring(0,8) }}…</span>
                </div>
                <span v-else class="detail-value muted">未分配</span>
              </div>
              <div class="detail-item full"><span class="detail-label">机台管辖</span>
                <div class="id-chips" v-if="detailData.deviceIds.length">
                  <span v-for="id in detailData.deviceIds" :key="id" class="id-chip device">{{ id.substring(0,8) }}…</span>
                </div>
                <span v-else class="detail-value muted">未分配</span>
              </div>
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn btn-ghost" @click="showDetailModal=false">关闭</button>
          </div>
        </div>
      </div>
    </Teleport>

    <!-- 确认对话框 -->
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
              {{ submitting?'处理中...':confirmDialog.confirmText }}
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
  getEmployeePagedListApi, getEmployeeDetailApi, getEmployeeAccessApi,
  onboardEmployeeApi, updateEmployeeProfileApi, updateEmployeeAccessApi,
  deactivateEmployeeApi, terminateEmployeeApi, getAllRolesApi,
  type EmployeeListItemDto, type EmployeeDetailDto, type PagedMetaData,
} from '../../api/employee';

const employees = ref<EmployeeListItemDto[]>([]);
const loading = ref(false);
const keyword = ref('');
const currentPage = ref(1);
const metaData = ref<PagedMetaData>({ totalCount: 0, pageSize: 10, currentPage: 1, totalPages: 1 });
const availableRoles = ref<string[]>([]);
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
    const raw = await getEmployeePagedListApi({
      PaginationParams: { PageNumber: currentPage.value, PageSize: 10 },
      Keyword: keyword.value || undefined,
    }) as unknown as Record<string, unknown>;

    if (raw && raw.metaData) {
      metaData.value = raw.metaData as PagedMetaData;
      // PagedList 继承 List，序列化出的数字key就是数组元素
      const items: EmployeeListItemDto[] = [];
      for (const k of Object.keys(raw)) {
        if (!isNaN(Number(k))) items.push(raw[k] as EmployeeListItemDto);
      }
      employees.value = items;
    } else if (Array.isArray(raw)) {
      employees.value = raw as EmployeeListItemDto[];
    }
  } catch {
    employees.value = [];
  } finally {
    loading.value = false;
  }
};

const goPage = (page: number) => { currentPage.value = page; fetchList(); };

// 入职弹窗
const showOnboardModal = ref(false);
const onboardForm = reactive({ EmployeeNo: '', RealName: '', Password: '', RoleName: '' });

const openOnboardModal = async () => {
  Object.assign(onboardForm, { EmployeeNo: '', RealName: '', Password: '', RoleName: '' });
  showOnboardModal.value = true;
  try {
    const roles = await getAllRolesApi() as unknown as string[];
    availableRoles.value = roles.filter(r => r !== 'Admin');
  } catch { availableRoles.value = []; }
};

const submitOnboard = async () => {
  if (!onboardForm.EmployeeNo.trim() || !onboardForm.RealName.trim() || !onboardForm.Password.trim()) {
    alert('工号、姓名和初始密码为必填项'); return;
  }
  submitting.value = true;
  try {
    await onboardEmployeeApi({ EmployeeNo: onboardForm.EmployeeNo, RealName: onboardForm.RealName, Password: onboardForm.Password, RoleName: onboardForm.RoleName || undefined });
    showOnboardModal.value = false; fetchList();
  } catch { } finally { submitting.value = false; }
};

// 编辑弹窗
const showEditModal = ref(false);
const editTarget = ref<EmployeeListItemDto | null>(null);
const editForm = reactive({ RealName: '', IsActive: true });

const openEditModal = (emp: EmployeeListItemDto) => {
  editTarget.value = emp; editForm.RealName = emp.realName; editForm.IsActive = emp.isActive; showEditModal.value = true;
};

const submitEdit = async () => {
  if (!editTarget.value || !editForm.RealName.trim()) { alert('姓名不能为空'); return; }
  submitting.value = true;
  try {
    await updateEmployeeProfileApi(editTarget.value.id, { RealName: editForm.RealName, IsActive: editForm.IsActive });
    showEditModal.value = false; fetchList();
  } catch { } finally { submitting.value = false; }
};

// 管辖权弹窗
const showAccessModal = ref(false);
const accessLoading = ref(false);
const accessTargetId = ref('');
const accessForm = reactive({ ProcessIds: [] as string[], DeviceIds: [] as string[] });

const openAccessModal = async (id: string) => {
  accessTargetId.value = id; accessLoading.value = true; showAccessModal.value = true;
  try {
    const access = await getEmployeeAccessApi(id) as unknown as { processIds: string[]; deviceIds: string[] };
    accessForm.ProcessIds = [...(access.processIds || [])]; accessForm.DeviceIds = [...(access.deviceIds || [])];
  } catch { accessForm.ProcessIds = []; accessForm.DeviceIds = []; } finally { accessLoading.value = false; }
};

const submitAccess = async () => {
  submitting.value = true;
  try {
    await updateEmployeeAccessApi(accessTargetId.value, { ProcessIds: accessForm.ProcessIds, DeviceIds: accessForm.DeviceIds });
    showAccessModal.value = false;
    // 管辖权变更后刷新列表，让后端重新计算 processCount/deviceCount
    fetchList();
  } catch { } finally { submitting.value = false; }
};

// 详情弹窗
const showDetailModal = ref(false);
const detailData = ref<EmployeeDetailDto | null>(null);

const openDetailModal = async (id: string) => {
  try {
    detailData.value = await getEmployeeDetailApi(id) as unknown as EmployeeDetailDto;
    showDetailModal.value = true;
  } catch { }
};

// 确认对话框
const confirmDialog = reactive({ show: false, type: 'danger', title: '', desc: '', confirmText: '', onConfirm: () => {} });

const handleDeactivate = (emp: EmployeeListItemDto) => {
  Object.assign(confirmDialog, {
    show: true, type: 'danger', title: '停用员工',
    desc: `确定要停用「${emp.realName}（${emp.employeeNo}）」吗？停用后该员工将无法登录，档案数据保留。`,
    confirmText: '确认停用',
    onConfirm: async () => {
      submitting.value = true;
      try { await deactivateEmployeeApi(emp.id); confirmDialog.show = false; fetchList(); }
      finally { submitting.value = false; }
    }
  });
};

const handleTerminate = (emp: EmployeeListItemDto) => {
  Object.assign(confirmDialog, {
    show: true, type: 'danger', title: '⚠️ 员工离职（不可撤销）',
    desc: `即将永久删除「${emp.realName}（${emp.employeeNo}）」的所有档案，含身份账号与权限数据，此操作不可撤销！`,
    confirmText: '确认离职销户',
    onConfirm: async () => {
      submitting.value = true;
      try { await terminateEmployeeApi(emp.id); confirmDialog.show = false; fetchList(); }
      finally { submitting.value = false; }
    }
  });
};

onMounted(() => fetchList());
</script>

<style scoped>
@import url('https://fonts.googleapis.com/css2?family=Rajdhani:wght@500;700&family=Noto+Sans+SC:wght@300;400;500&display=swap');
.employee-list { font-family: 'Noto Sans SC', sans-serif; }
.page-header { display:flex; align-items:flex-start; justify-content:space-between; margin-bottom:24px; }
.page-title { font-size:22px; font-weight:500; color:#e8eaf0; margin:0 0 4px; }
.page-sub { font-size:12px; color:rgba(255,255,255,0.3); margin:0; }
.toolbar { display:flex; align-items:center; justify-content:space-between; margin-bottom:16px; gap:12px; }
.search-wrap { display:flex; align-items:center; gap:10px; background:rgba(255,255,255,0.04); border:1px solid rgba(255,255,255,0.08); border-radius:4px; padding:0 14px; flex:1; max-width:360px; transition:border-color 0.2s; }
.search-wrap:focus-within { border-color:rgba(0,229,255,0.4); }
.search-wrap svg { width:14px; height:14px; color:rgba(255,255,255,0.3); flex-shrink:0; }
.search-wrap input { background:transparent; border:none; outline:none; font-size:13px; color:#e0e4ef; padding:10px 0; font-family:'Noto Sans SC',sans-serif; flex:1; }
.search-wrap input::placeholder { color:rgba(255,255,255,0.2); }
.clear-btn { background:none; border:none; cursor:pointer; color:rgba(255,255,255,0.3); font-size:11px; padding:0; }
.total-badge { font-size:12px; color:rgba(255,255,255,0.3); background:rgba(255,255,255,0.04); padding:5px 12px; border-radius:20px; border:1px solid rgba(255,255,255,0.06); white-space:nowrap; }
.table-wrap { background:rgba(255,255,255,0.02); border:1px solid rgba(255,255,255,0.06); border-radius:6px; overflow:hidden; margin-bottom:20px; }
.data-table { width:100%; border-collapse:collapse; }
.data-table thead tr { border-bottom:1px solid rgba(255,255,255,0.06); }
.data-table th { padding:12px 16px; text-align:left; font-size:11px; font-weight:500; color:rgba(255,255,255,0.3); letter-spacing:1px; text-transform:uppercase; background:rgba(255,255,255,0.02); }
.table-row { border-bottom:1px solid rgba(255,255,255,0.04); cursor:pointer; transition:background 0.15s; }
.table-row:hover { background:rgba(0,229,255,0.04); }
.table-row:last-child { border-bottom:none; }
.data-table td { padding:14px 16px; font-size:13px; color:rgba(255,255,255,0.7); }
.employee-no { font-family:'Rajdhani',sans-serif; font-weight:600; font-size:14px; color:#00e5ff; letter-spacing:0.5px; }
.real-name { color:rgba(255,255,255,0.85); font-weight:500; }
.status-tag { display:inline-flex; align-items:center; gap:5px; padding:3px 10px; border-radius:12px; font-size:12px; }
.status-tag.active { background:rgba(0,229,160,0.1); color:#00e5a0; border:1px solid rgba(0,229,160,0.2); }
.status-tag.inactive { background:rgba(255,255,255,0.05); color:rgba(255,255,255,0.3); border:1px solid rgba(255,255,255,0.08); }
.status-dot { width:5px; height:5px; border-radius:50%; background:currentColor; }
.status-tag.active .status-dot { box-shadow:0 0 4px currentColor; }
.access-badge { font-family:'Rajdhani',sans-serif; font-size:14px; font-weight:600; color:rgba(0,119,255,0.8); }
.access-badge.device { color:rgba(0,229,255,0.7); }
.action-cell { text-align:right; }
.icon-btn { display:inline-flex; align-items:center; justify-content:center; width:30px; height:30px; border-radius:4px; border:1px solid transparent; background:none; cursor:pointer; margin-left:4px; transition:all 0.15s; color:rgba(255,255,255,0.3); }
.icon-btn svg { width:14px; height:14px; }
.icon-btn.edit:hover { background:rgba(0,119,255,0.12); border-color:rgba(0,119,255,0.3); color:#4d9fff; }
.icon-btn.access:hover { background:rgba(0,229,255,0.1); border-color:rgba(0,229,255,0.3); color:#00e5ff; }
.icon-btn.deactivate:hover { background:rgba(255,165,0,0.1); border-color:rgba(255,165,0,0.3); color:#ffa500; }
.icon-btn.terminate:hover { background:rgba(255,77,79,0.1); border-color:rgba(255,77,79,0.3); color:#ff4d4f; }
.empty-cell { text-align:center; padding:48px 0 !important; }
.empty-state { display:flex; flex-direction:column; align-items:center; gap:12px; }
.empty-state svg { width:48px; height:48px; color:rgba(255,255,255,0.15); }
.empty-state p { font-size:13px; color:rgba(255,255,255,0.2); margin:0; }
.pagination { display:flex; align-items:center; justify-content:center; gap:6px; }
.page-btn { min-width:32px; height:32px; border-radius:4px; background:rgba(255,255,255,0.04); border:1px solid rgba(255,255,255,0.08); color:rgba(255,255,255,0.5); font-size:13px; cursor:pointer; display:flex; align-items:center; justify-content:center; transition:all 0.15s; padding:0 8px; font-family:'Noto Sans SC',sans-serif; }
.page-btn:hover:not(:disabled) { background:rgba(0,229,255,0.08); border-color:rgba(0,229,255,0.25); color:#00e5ff; }
.page-btn.active { background:rgba(0,119,255,0.2); border-color:rgba(0,119,255,0.5); color:#4d9fff; }
.page-btn:disabled { opacity:0.3; cursor:not-allowed; }
.page-btn svg { width:12px; height:12px; }
.skeleton-rows { padding:8px 0; }
.skeleton-row { display:flex; gap:24px; padding:14px 16px; border-bottom:1px solid rgba(255,255,255,0.04); }
.skel { height:14px; border-radius:4px; background:linear-gradient(90deg, rgba(255,255,255,0.05) 25%, rgba(255,255,255,0.08) 50%, rgba(255,255,255,0.05) 75%); background-size:200% 100%; animation:shimmer 1.5s infinite; }
.skel-sm{width:80px;} .skel-md{width:120px;} .skel-lg{width:160px;}
@keyframes shimmer { to { background-position:-200% 0; } }
.btn { display:inline-flex; align-items:center; gap:7px; padding:9px 18px; border-radius:4px; font-size:13px; font-weight:500; cursor:pointer; font-family:'Noto Sans SC',sans-serif; border:1px solid transparent; transition:all 0.18s; }
.btn svg { width:14px; height:14px; }
.btn-primary { background:linear-gradient(135deg,#0077ff,#00bcd4); color:white; box-shadow:0 3px 12px rgba(0,119,255,0.25); }
.btn-primary:hover:not(:disabled) { box-shadow:0 4px 16px rgba(0,119,255,0.4); opacity:0.92; }
.btn-primary:disabled { opacity:0.5; cursor:not-allowed; }
.btn-ghost { background:rgba(255,255,255,0.05); border-color:rgba(255,255,255,0.1); color:rgba(255,255,255,0.6); }
.btn-ghost:hover { background:rgba(255,255,255,0.09); }
.btn-danger { background:rgba(255,77,79,0.15); border-color:rgba(255,77,79,0.4); color:#ff6b6b; }
.btn-danger:hover:not(:disabled) { background:rgba(255,77,79,0.25); }
.btn-danger:disabled { opacity:0.5; cursor:not-allowed; }
.modal-overlay { position:fixed; inset:0; z-index:1000; background:rgba(0,0,0,0.7); backdrop-filter:blur(4px); display:flex; align-items:center; justify-content:center; }
.modal { background:#0e1526; border:1px solid rgba(0,229,255,0.15); border-radius:6px; width:640px; max-width:95vw; max-height:90vh; display:flex; flex-direction:column; box-shadow:0 24px 64px rgba(0,0,0,0.6); }
.modal-sm { width:440px; }
.modal-header { display:flex; align-items:center; justify-content:space-between; padding:20px 24px 16px; border-bottom:1px solid rgba(255,255,255,0.06); }
.modal-title { font-size:15px; font-weight:500; color:#e8eaf0; }
.modal-close { background:none; border:none; cursor:pointer; color:rgba(255,255,255,0.3); font-size:14px; width:24px; height:24px; display:flex; align-items:center; justify-content:center; border-radius:3px; transition:all 0.15s; }
.modal-close:hover { background:rgba(255,255,255,0.08); color:rgba(255,255,255,0.7); }
.modal-body { padding:20px 24px; overflow-y:auto; flex:1; }
.modal-footer { display:flex; justify-content:flex-end; gap:10px; padding:16px 24px; border-top:1px solid rgba(255,255,255,0.06); }
.form-section-label { font-size:11px; font-weight:500; color:rgba(255,255,255,0.3); letter-spacing:1.5px; text-transform:uppercase; margin-bottom:14px; }
.section-hint { font-size:11px; color:rgba(255,255,255,0.2); text-transform:none; letter-spacing:0; margin-left:8px; }
.form-row { display:grid; grid-template-columns:1fr 1fr; gap:14px; margin-bottom:14px; }
.form-field { display:flex; flex-direction:column; gap:7px; margin-bottom:14px; }
.form-field label { font-size:12px; color:rgba(255,255,255,0.4); }
.required { color:#ff6b6b; }
.form-field input,.form-field select { background:rgba(255,255,255,0.05); border:1px solid rgba(255,255,255,0.1); border-radius:3px; padding:10px 12px; font-size:13px; color:#e0e4ef; font-family:'Noto Sans SC',sans-serif; outline:none; transition:border-color 0.2s; }
.form-field input:focus,.form-field select:focus { border-color:rgba(0,229,255,0.4); }
.form-field select option { background:#0e1526; }
.disabled-input { opacity:0.4 !important; cursor:not-allowed; }
.toggle-row { display:flex; align-items:center; gap:10px; padding:6px 0; }
.toggle { position:relative; display:inline-block; width:40px; height:22px; cursor:pointer; }
.toggle input { display:none; }
.toggle-slider { position:absolute; inset:0; background:rgba(255,255,255,0.1); border-radius:11px; transition:all 0.2s; border:1px solid rgba(255,255,255,0.15); }
.toggle-slider::before { content:''; position:absolute; width:16px; height:16px; background:rgba(255,255,255,0.5); border-radius:50%; top:2px; left:2px; transition:all 0.2s; }
.toggle input:checked + .toggle-slider { background:rgba(0,229,160,0.2); border-color:rgba(0,229,160,0.4); }
.toggle input:checked + .toggle-slider::before { transform:translateX(18px); background:#00e5a0; }
.toggle-label { font-size:13px; color:rgba(255,255,255,0.6); }
.dual-access-grid { display:grid; grid-template-columns:1fr 1fr; gap:14px; }
.access-panel { background:rgba(255,255,255,0.03); border:1px solid rgba(255,255,255,0.07); border-radius:4px; padding:14px; }
.access-panel-title { font-size:13px; font-weight:500; color:rgba(255,255,255,0.7); margin-bottom:8px; }
.access-hint { font-size:11px; color:rgba(255,255,255,0.25); margin-bottom:12px; line-height:1.5; }
.access-placeholder { padding:12px; background:rgba(255,255,255,0.02); border-radius:3px; text-align:center; }
.coming-soon { font-size:12px; color:rgba(255,255,255,0.2); }
.access-loading { text-align:center; padding:24px; font-size:13px; color:rgba(255,255,255,0.3); }
.detail-grid { display:grid; grid-template-columns:1fr 1fr; gap:14px; }
.detail-item { display:flex; flex-direction:column; gap:5px; }
.detail-item.full { grid-column:1 / -1; }
.detail-label { font-size:11px; color:rgba(255,255,255,0.3); letter-spacing:0.5px; }
.detail-value { font-size:13px; color:rgba(255,255,255,0.75); }
.detail-value.emp-no { font-family:'Rajdhani',sans-serif; font-size:16px; font-weight:700; color:#00e5ff; }
.detail-value.id-text { font-size:11px; color:rgba(255,255,255,0.3); font-family:monospace; }
.detail-value.muted { color:rgba(255,255,255,0.2); }
.id-chips { display:flex; flex-wrap:wrap; gap:6px; }
.id-chip { padding:3px 8px; border-radius:3px; font-size:11px; font-family:monospace; background:rgba(0,119,255,0.1); border:1px solid rgba(0,119,255,0.2); color:rgba(0,119,255,0.8); }
.id-chip.device { background:rgba(0,229,255,0.08); border-color:rgba(0,229,255,0.2); color:rgba(0,229,255,0.7); }
.confirm-box { background:#0e1526; border:1px solid rgba(255,77,79,0.2); border-radius:6px; padding:28px; width:380px; max-width:95vw; text-align:center; box-shadow:0 24px 64px rgba(0,0,0,0.7); }
.confirm-icon { margin-bottom:14px; }
.confirm-icon.danger svg { width:32px; height:32px; color:#ff6b6b; }
.confirm-title { font-size:16px; font-weight:500; color:#e8eaf0; margin-bottom:10px; }
.confirm-desc { font-size:13px; color:rgba(255,255,255,0.4); line-height:1.7; margin-bottom:24px; }
.confirm-actions { display:flex; justify-content:center; gap:12px; }
</style>
