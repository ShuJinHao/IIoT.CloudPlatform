<template>
  <div class="pass-station">
    <!-- 页头 -->
    <div class="page-header">
      <div>
        <h1 class="page-title">过站数据追溯</h1>
        <p class="page-sub">按条码、时间、设备等多维度追溯注液工序检测数据</p>
      </div>
    </div>

    <!-- 查询模式切换 -->
    <div class="query-modes">
      <button v-for="mode in queryModes" :key="mode.key" class="mode-btn" :class="{ active: currentMode === mode.key }" @click="switchMode(mode.key)">
        <span class="mode-icon" v-html="mode.icon"></span>
        {{ mode.label }}
      </button>
    </div>

    <!-- 查询条件区 -->
    <div class="filter-bar">
      <!-- 模式1: 条码 + 工序 -->
      <template v-if="currentMode === 'barcode-process'">
        <div class="filter-field">
          <label>工序</label>
          <select v-model="filters.processId" class="filter-input">
            <option value="">请选择工序</option>
            <option v-for="p in allProcesses" :key="p.id" :value="p.id">{{ p.processCode }} · {{ p.processName }}</option>
          </select>
        </div>
        <div class="filter-field">
          <label>电芯条码</label>
          <input v-model="filters.barcode" class="filter-input" placeholder="输入电芯条码" @keyup.enter="doSearch" />
        </div>
      </template>

      <!-- 模式2: 时间 + 工序 -->
      <template v-if="currentMode === 'time-process'">
        <div class="filter-field">
          <label>工序</label>
          <select v-model="filters.processId" class="filter-input">
            <option value="">请选择工序</option>
            <option v-for="p in allProcesses" :key="p.id" :value="p.id">{{ p.processCode }} · {{ p.processName }}</option>
          </select>
        </div>
        <div class="filter-field">
          <label>开始时间</label>
          <input type="datetime-local" v-model="filters.startTime" class="filter-input" />
        </div>
        <div class="filter-field">
          <label>结束时间</label>
          <input type="datetime-local" v-model="filters.endTime" class="filter-input" />
        </div>
      </template>

      <!-- 模式3: 设备 + 条码 -->
      <template v-if="currentMode === 'device-barcode'">
        <div class="filter-field">
          <label>设备</label>
          <select v-model="filters.deviceId" class="filter-input">
            <option value="">请选择设备</option>
            <option v-for="d in allDevices" :key="d.id" :value="d.id">{{ d.deviceCode }} · {{ d.deviceName }}</option>
          </select>
        </div>
        <div class="filter-field">
          <label>电芯条码</label>
          <input v-model="filters.barcode" class="filter-input" placeholder="输入电芯条码" @keyup.enter="doSearch" />
        </div>
      </template>

      <!-- 模式4: 设备 + 时间 -->
      <template v-if="currentMode === 'device-time'">
        <div class="filter-field">
          <label>设备</label>
          <select v-model="filters.deviceId" class="filter-input">
            <option value="">请选择设备</option>
            <option v-for="d in allDevices" :key="d.id" :value="d.id">{{ d.deviceCode }} · {{ d.deviceName }}</option>
          </select>
        </div>
        <div class="filter-field">
          <label>开始时间</label>
          <input type="datetime-local" v-model="filters.startTime" class="filter-input" />
        </div>
        <div class="filter-field">
          <label>结束时间</label>
          <input type="datetime-local" v-model="filters.endTime" class="filter-input" />
        </div>
      </template>

      <button class="btn btn-primary search-btn" @click="doSearch">
        <svg viewBox="0 0 16 16" fill="none"><circle cx="6.5" cy="6.5" r="4.5" stroke="currentColor" stroke-width="1.3"/><path d="M10 10l3 3" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/></svg>
        查询
      </button>
    </div>

    <!-- 结果表格 -->
    <div class="table-wrap">
      <div v-if="loading" class="skeleton-rows">
        <div v-for="i in 5" :key="i" class="skeleton-row">
          <div class="skel skel-md"></div><div class="skel skel-sm"></div>
          <div class="skel skel-lg"></div><div class="skel skel-sm"></div><div class="skel skel-md"></div>
        </div>
      </div>
      <div v-else-if="!searched" class="empty-cell">
        <div class="empty-state">
          <svg viewBox="0 0 48 48" fill="none"><circle cx="20" cy="20" r="14" stroke="currentColor" stroke-width="1.5" opacity="0.25"/><path d="M30 30l10 10" stroke="currentColor" stroke-width="2" stroke-linecap="round" opacity="0.25"/></svg>
          <p>选择查询条件后点击查询</p>
        </div>
      </div>
      <table v-else-if="records.length > 0" class="data-table">
        <thead>
          <tr>
            <th>电芯条码</th>
            <th>检测结果</th>
            <th>注液量 (ml)</th>
            <th>注液前重 (g)</th>
            <th>注液后重 (g)</th>
            <th>完成时间</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="r in records" :key="r.id" class="table-row" @click="openDetail(r.id)">
            <td><span class="barcode-chip">{{ r.barcode }}</span></td>
            <td>
              <span class="result-tag" :class="r.cell_result === 'OK' ? 'ok' : 'ng'">
                {{ r.cell_result }}
              </span>
            </td>
            <td class="mono">{{ r.injection_volume }}</td>
            <td class="mono">{{ r.pre_injection_weight }}</td>
            <td class="mono">{{ r.post_injection_weight }}</td>
            <td class="time-cell">{{ formatTime(r.completed_time) }}</td>
          </tr>
        </tbody>
      </table>
      <div v-else class="empty-cell">
        <div class="empty-state">
          <svg viewBox="0 0 48 48" fill="none"><rect x="8" y="6" width="28" height="36" rx="2" stroke="currentColor" stroke-width="1.5" opacity="0.25"/><path d="M16 20h12M16 26h8" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" opacity="0.25"/></svg>
          <p>未查询到匹配的过站数据</p>
        </div>
      </div>
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
      <span class="total-badge">共 {{ metaData.totalCount }} 条</span>
    </div>

    <!-- 详情侧边栏 -->
    <Teleport to="body">
      <div v-if="showDetail" class="detail-overlay" @click.self="showDetail=false">
        <div class="detail-panel">
          <div class="detail-header">
            <span class="detail-title">过站详情</span>
            <button class="modal-close" @click="showDetail=false">✕</button>
          </div>
          <div class="detail-body" v-if="detailLoading">
            <div class="detail-loading"><div class="loading-ring"></div><span>加载中...</span></div>
          </div>
          <div class="detail-body" v-else-if="detailData">
            <div class="detail-result-banner" :class="detailData.cell_result === 'OK' ? 'ok' : 'ng'">
              <span class="result-icon">{{ detailData.cell_result === 'OK' ? '✓' : '✗' }}</span>
              检测结果：{{ detailData.cell_result }}
            </div>
            <div class="detail-section">
              <div class="detail-row"><span class="detail-label">电芯条码</span><span class="detail-value mono-val">{{ detailData.barcode }}</span></div>
              <div class="detail-row"><span class="detail-label">设备 ID</span><span class="detail-value mono-val small">{{ detailData.device_id }}</span></div>
              <div class="detail-divider"></div>
              <div class="detail-row"><span class="detail-label">注液前时间</span><span class="detail-value">{{ formatTime(detailData.pre_injection_time) }}</span></div>
              <div class="detail-row"><span class="detail-label">注液前称重</span><span class="detail-value mono-val">{{ detailData.pre_injection_weight }} g</span></div>
              <div class="detail-row"><span class="detail-label">注液后时间</span><span class="detail-value">{{ formatTime(detailData.post_injection_time) }}</span></div>
              <div class="detail-row"><span class="detail-label">注液后称重</span><span class="detail-value mono-val">{{ detailData.post_injection_weight }} g</span></div>
              <div class="detail-row"><span class="detail-label">注液量</span><span class="detail-value mono-val highlight">{{ detailData.injection_volume }} ml</span></div>
              <div class="detail-divider"></div>
              <div class="detail-row"><span class="detail-label">完成时间</span><span class="detail-value">{{ formatTime(detailData.completed_time) }}</span></div>
              <div class="detail-row"><span class="detail-label">云端接收时间</span><span class="detail-value small">{{ formatTime(detailData.received_at) }}</span></div>
            </div>
          </div>
        </div>
      </div>
    </Teleport>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue';
import {
  getInjectionByBarcodeAndProcessApi,
  getInjectionByTimeAndProcessApi,
  getInjectionByDeviceAndBarcodeApi,
  getInjectionByDeviceAndTimeApi,
  getInjectionDetailApi,
} from '../../api/passStation';
import { getAllMfgProcessesApi, type MfgProcessSelectDto } from '../../api/mfgProcess';
import { getAllActiveDevicesApi, type DeviceSelectDto } from '../../api/device';
import type { PagedMetaData } from '../../api/employee';

type QueryMode = 'barcode-process' | 'time-process' | 'device-barcode' | 'device-time';

const queryModes = [
  { key: 'barcode-process' as QueryMode, label: '条码 + 工序', icon: '<svg viewBox="0 0 16 16" fill="none"><rect x="2" y="3" width="12" height="2" rx="0.5" stroke="currentColor" stroke-width="1.1"/><rect x="2" y="7" width="8" height="2" rx="0.5" stroke="currentColor" stroke-width="1.1"/><rect x="2" y="11" width="10" height="2" rx="0.5" stroke="currentColor" stroke-width="1.1"/></svg>' },
  { key: 'time-process' as QueryMode, label: '时间 + 工序', icon: '<svg viewBox="0 0 16 16" fill="none"><circle cx="8" cy="8" r="6" stroke="currentColor" stroke-width="1.1"/><path d="M8 5v3.5l2.5 1.5" stroke="currentColor" stroke-width="1.1" stroke-linecap="round"/></svg>' },
  { key: 'device-barcode' as QueryMode, label: '设备 + 条码', icon: '<svg viewBox="0 0 16 16" fill="none"><rect x="2" y="4" width="12" height="8" rx="1.5" stroke="currentColor" stroke-width="1.1"/><circle cx="8" cy="8" r="2" stroke="currentColor" stroke-width="1.1"/></svg>' },
  { key: 'device-time' as QueryMode, label: '设备 + 时间', icon: '<svg viewBox="0 0 16 16" fill="none"><rect x="2" y="4" width="8" height="8" rx="1.5" stroke="currentColor" stroke-width="1.1"/><path d="M12 6v4l1.5 1" stroke="currentColor" stroke-width="1.1" stroke-linecap="round"/></svg>' },
];

const currentMode = ref<QueryMode>('barcode-process');
const loading = ref(false);
const searched = ref(false);
const records = ref<any[]>([]);
const currentPage = ref(1);
const metaData = ref<PagedMetaData>({ totalCount: 0, pageSize: 10, currentPage: 1, totalPages: 1 });

const allProcesses = ref<MfgProcessSelectDto[]>([]);
const allDevices = ref<DeviceSelectDto[]>([]);

const filters = reactive({
  processId: '',
  deviceId: '',
  barcode: '',
  startTime: '',
  endTime: '',
});

const pageNumbers = computed(() => {
  const total = metaData.value.totalPages;
  const cur = currentPage.value;
  const range: number[] = [];
  for (let i = Math.max(1, cur - 2); i <= Math.min(total, cur + 2); i++) range.push(i);
  return range;
});

const switchMode = (mode: QueryMode) => {
  currentMode.value = mode;
  records.value = [];
  searched.value = false;
  currentPage.value = 1;
};

const formatTime = (t: string) => {
  if (!t) return '—';
  try {
    const d = new Date(t);
    return d.toLocaleString('zh-CN', { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit' });
  } catch { return t; }
};

const doSearch = async () => {
  currentPage.value = 1;
  await fetchData();
};

const fetchData = async () => {
  loading.value = true;
  searched.value = true;
  try {
    let raw: any;
    const pagination = { PageNumber: currentPage.value, PageSize: 10 };

    switch (currentMode.value) {
      case 'barcode-process':
        if (!filters.processId || !filters.barcode.trim()) { alert('请选择工序并输入条码'); loading.value = false; return; }
        raw = await getInjectionByBarcodeAndProcessApi({ pagination, processId: filters.processId, barcode: filters.barcode });
        break;
      case 'time-process':
        if (!filters.processId || !filters.startTime || !filters.endTime) { alert('请选择工序和时间范围'); loading.value = false; return; }
        raw = await getInjectionByTimeAndProcessApi({ pagination, processId: filters.processId, startTime: filters.startTime, endTime: filters.endTime });
        break;
      case 'device-barcode':
        if (!filters.deviceId || !filters.barcode.trim()) { alert('请选择设备并输入条码'); loading.value = false; return; }
        raw = await getInjectionByDeviceAndBarcodeApi({ pagination, deviceId: filters.deviceId, barcode: filters.barcode });
        break;
      case 'device-time':
        if (!filters.deviceId || !filters.startTime || !filters.endTime) { alert('请选择设备和时间范围'); loading.value = false; return; }
        raw = await getInjectionByDeviceAndTimeApi({ pagination, deviceId: filters.deviceId, startTime: filters.startTime, endTime: filters.endTime });
        break;
    }

    if (raw && raw.metaData) {
      metaData.value = raw.metaData as PagedMetaData;
      const items: any[] = [];
      for (const k of Object.keys(raw)) {
        if (!isNaN(Number(k))) items.push(raw[k]);
      }
      records.value = items;
    } else if (Array.isArray(raw)) {
      records.value = raw;
    } else {
      records.value = [];
    }
  } catch {
    records.value = [];
  } finally {
    loading.value = false;
  }
};

const goPage = (page: number) => { currentPage.value = page; fetchData(); };

// ── 详情侧边栏 ──
const showDetail = ref(false);
const detailData = ref<any>(null);
const detailLoading = ref(false);

const openDetail = async (id: string) => {
  showDetail.value = true;
  detailLoading.value = true;
  detailData.value = null;
  try {
    detailData.value = await getInjectionDetailApi(id);
  } catch {
    showDetail.value = false;
  } finally {
    detailLoading.value = false;
  }
};

const fetchSelectData = async () => {
  try { allProcesses.value = await getAllMfgProcessesApi() as unknown as MfgProcessSelectDto[]; } catch { allProcesses.value = []; }
  try { allDevices.value = await getAllActiveDevicesApi() as unknown as DeviceSelectDto[]; } catch { allDevices.value = []; }
};

onMounted(() => { fetchSelectData(); });
</script>

<style scoped>
* { box-sizing: border-box; }
.pass-station { font-family: 'Noto Sans SC', sans-serif; color: #e0e4ef; }

.page-header { display: flex; align-items: flex-start; justify-content: space-between; margin-bottom: 20px; }
.page-title { font-size: 22px; font-weight: 600; color: #fff; margin: 0 0 4px; }
.page-sub { font-size: 13px; color: rgba(255,255,255,0.35); margin: 0; }

/* 查询模式切换 */
.query-modes { display: flex; gap: 8px; margin-bottom: 16px; }
.mode-btn { display: flex; align-items: center; gap: 6px; padding: 8px 14px; border-radius: 4px; border: 1px solid rgba(255,255,255,0.08); background: rgba(255,255,255,0.03); color: rgba(255,255,255,0.45); font-size: 12px; font-family: 'Noto Sans SC', sans-serif; cursor: pointer; transition: all 0.18s; }
.mode-btn:hover { border-color: rgba(0,229,255,0.2); color: rgba(255,255,255,0.7); }
.mode-btn.active { background: rgba(0,229,255,0.1); border-color: rgba(0,229,255,0.35); color: #00e5ff; }
.mode-icon { width: 14px; height: 14px; display: flex; align-items: center; }
.mode-icon :deep(svg) { width: 14px; height: 14px; }

/* 查询条件栏 */
.filter-bar { display: flex; align-items: flex-end; gap: 12px; margin-bottom: 16px; padding: 16px; background: rgba(255,255,255,0.02); border: 1px solid rgba(255,255,255,0.06); border-radius: 4px; flex-wrap: wrap; }
.filter-field { display: flex; flex-direction: column; gap: 5px; min-width: 160px; flex: 1; }
.filter-field label { font-size: 11px; color: rgba(255,255,255,0.35); font-weight: 500; }
.filter-input { background: rgba(255,255,255,0.04); border: 1px solid rgba(255,255,255,0.1); border-radius: 3px; padding: 8px 10px; color: rgba(255,255,255,0.8); font-size: 13px; font-family: 'Noto Sans SC', sans-serif; outline: none; transition: border-color 0.2s; }
.filter-input:focus { border-color: rgba(0,229,255,0.4); }
.filter-input::placeholder { color: rgba(255,255,255,0.2); }
select.filter-input { appearance: none; background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' viewBox='0 0 12 12'%3E%3Cpath d='M3 4.5l3 3 3-3' stroke='%2300e5ff' stroke-width='1.2' fill='none' stroke-linecap='round'/%3E%3C/svg%3E"); background-repeat: no-repeat; background-position: right 10px center; padding-right: 28px; cursor: pointer; }
select.filter-input option { background: #0f1525; color: #e0e4ef; }
.search-btn { flex-shrink: 0; height: 36px; }
.search-btn svg { width: 14px; height: 14px; }

/* 表格 */
.table-wrap { background: rgba(255,255,255,0.02); border: 1px solid rgba(255,255,255,0.06); border-radius: 4px; overflow: hidden; }
.data-table { width: 100%; border-collapse: collapse; }
.data-table thead tr { background: rgba(255,255,255,0.03); border-bottom: 1px solid rgba(255,255,255,0.06); }
.data-table th { padding: 11px 16px; text-align: left; font-size: 11px; font-weight: 500; color: rgba(255,255,255,0.3); letter-spacing: 1px; text-transform: uppercase; white-space: nowrap; }
.table-row { border-bottom: 1px solid rgba(255,255,255,0.04); cursor: pointer; transition: background 0.15s; }
.table-row:last-child { border-bottom: none; }
.table-row:hover { background: rgba(0,229,255,0.03); }
.data-table td { padding: 12px 16px; font-size: 13px; vertical-align: middle; }
.mono { font-family: 'Courier New', monospace; font-size: 12px; color: rgba(255,255,255,0.7); }
.time-cell { font-size: 12px; color: rgba(255,255,255,0.5); }

.barcode-chip { font-family: 'Courier New', monospace; font-size: 12px; background: rgba(0,229,255,0.06); border: 1px solid rgba(0,229,255,0.12); padding: 2px 8px; border-radius: 3px; color: #00e5ff; }
.result-tag { font-size: 11px; font-weight: 600; padding: 3px 10px; border-radius: 20px; }
.result-tag.ok { background: rgba(0,229,160,0.12); color: #00e5a0; }
.result-tag.ng { background: rgba(255,77,79,0.12); color: #ff8888; }

.skeleton-rows { padding: 8px 0; }
.skeleton-row { display: flex; gap: 16px; padding: 14px 16px; border-bottom: 1px solid rgba(255,255,255,0.04); }
.skel { background: rgba(255,255,255,0.06); border-radius: 3px; height: 14px; animation: shimmer 1.5s infinite; }
.skel-sm { width: 70px; } .skel-md { width: 120px; } .skel-lg { width: 200px; }
@keyframes shimmer { 0%,100% { opacity:0.5; } 50% { opacity:1; } }

.empty-cell { text-align: center; padding: 56px 0; }
.empty-state { display: flex; flex-direction: column; align-items: center; gap: 12px; }
.empty-state svg { width: 52px; height: 52px; color: rgba(255,255,255,0.2); }
.empty-state p { font-size: 13px; color: rgba(255,255,255,0.25); margin: 0; }

/* 分页 */
.pagination { display: flex; justify-content: center; align-items: center; gap: 6px; margin-top: 20px; }
.page-btn { width: 32px; height: 32px; border-radius: 3px; border: 1px solid rgba(255,255,255,0.08); background: rgba(255,255,255,0.03); color: rgba(255,255,255,0.45); font-size: 13px; cursor: pointer; display: flex; align-items: center; justify-content: center; transition: all 0.15s; }
.page-btn:hover:not(:disabled) { border-color: rgba(0,229,255,0.3); color: #00e5ff; }
.page-btn.active { background: rgba(0,229,255,0.12); border-color: rgba(0,229,255,0.4); color: #00e5ff; }
.page-btn:disabled { opacity: 0.3; cursor: not-allowed; }
.page-btn svg { width: 12px; height: 12px; }
.total-badge { font-size: 12px; color: rgba(255,255,255,0.3); margin-left: 8px; }

/* 按钮 */
.btn { display: inline-flex; align-items: center; gap: 6px; padding: 8px 16px; border-radius: 3px; border: none; font-size: 13px; font-family: 'Noto Sans SC', sans-serif; font-weight: 500; cursor: pointer; transition: all 0.18s; }
.btn-primary { background: rgba(0,229,255,0.15); color: #00e5ff; border: 1px solid rgba(0,229,255,0.3); }
.btn-primary:hover { background: rgba(0,229,255,0.25); }

/* 详情侧边栏 */
.detail-overlay { position: fixed; inset: 0; z-index: 100; background: rgba(0,0,0,0.5); display: flex; align-items: stretch; justify-content: flex-end; }
.detail-panel { width: 440px; background: #0f1525; border-left: 1px solid rgba(255,255,255,0.08); display: flex; flex-direction: column; animation: slideIn 0.22s cubic-bezier(0.4,0,0.2,1); }
@keyframes slideIn { from { transform: translateX(100%); } to { transform: translateX(0); } }
.detail-header { display: flex; align-items: center; justify-content: space-between; padding: 18px 22px; border-bottom: 1px solid rgba(255,255,255,0.06); }
.detail-title { font-size: 15px; font-weight: 600; color: #fff; }
.modal-close { background: none; border: none; color: rgba(255,255,255,0.3); font-size: 16px; cursor: pointer; }
.modal-close:hover { color: rgba(255,255,255,0.7); }
.detail-body { padding: 20px 22px; flex: 1; overflow-y: auto; }
.detail-loading { display: flex; flex-direction: column; align-items: center; gap: 14px; padding-top: 60px; color: rgba(255,255,255,0.3); font-size: 13px; }
.loading-ring { width: 32px; height: 32px; border: 2px solid rgba(0,229,255,0.15); border-top-color: #00e5ff; border-radius: 50%; animation: spin 0.8s linear infinite; }
@keyframes spin { to { transform: rotate(360deg); } }

.detail-result-banner { display: flex; align-items: center; gap: 10px; padding: 12px 16px; border-radius: 4px; font-size: 14px; font-weight: 600; margin-bottom: 20px; }
.detail-result-banner.ok { background: rgba(0,229,160,0.1); color: #00e5a0; border: 1px solid rgba(0,229,160,0.2); }
.detail-result-banner.ng { background: rgba(255,77,79,0.1); color: #ff8888; border: 1px solid rgba(255,77,79,0.2); }
.result-icon { font-size: 18px; }

.detail-section { display: flex; flex-direction: column; gap: 14px; }
.detail-row { display: flex; align-items: center; justify-content: space-between; }
.detail-label { font-size: 12px; color: rgba(255,255,255,0.35); }
.detail-value { font-size: 13px; color: rgba(255,255,255,0.8); }
.mono-val { font-family: 'Courier New', monospace; }
.mono-val.small { font-size: 11px; color: rgba(255,255,255,0.4); }
.highlight { color: #00e5ff; font-weight: 600; }
.small { font-size: 11px; color: rgba(255,255,255,0.4); }
.detail-divider { height: 1px; background: rgba(255,255,255,0.06); margin: 4px 0; }
</style>
