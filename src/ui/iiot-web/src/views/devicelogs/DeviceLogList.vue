<template>
  <div class="device-log-page">
    <!-- 页头 -->
    <div class="page-header">
      <div>
        <h1 class="page-title">设备日志</h1>
        <p class="page-sub">远程查看设备运行日志，支持按级别、关键字、时间等多维度筛选</p>
      </div>
    </div>

    <!-- 设备选择（必选） -->
    <div class="device-select-bar">
      <label>选择设备</label>
      <select v-model="selectedDeviceId" class="filter-input device-select" @change="onDeviceChange">
        <option value="">请先选择设备</option>
        <option v-for="d in allDevices" :key="d.id" :value="d.id">{{ d.deviceCode }} · {{ d.deviceName }}</option>
      </select>
    </div>

    <!-- 查询模式切换 -->
    <div class="query-modes" v-if="selectedDeviceId">
      <button v-for="mode in queryModes" :key="mode.key" class="mode-btn" :class="{ active: currentMode === mode.key }" @click="switchMode(mode.key)">
        {{ mode.label }}
      </button>
    </div>

    <!-- 筛选条件 -->
    <div class="filter-bar" v-if="selectedDeviceId">
      <!-- 模式1: 级别 -->
      <template v-if="currentMode === 'level'">
        <div class="filter-field">
          <label>日志级别</label>
          <select v-model="filters.level" class="filter-input">
            <option value="">全部级别</option>
            <option value="INFO">INFO</option>
            <option value="WARN">WARN</option>
            <option value="ERROR">ERROR</option>
          </select>
        </div>
      </template>

      <!-- 模式2: 关键字 -->
      <template v-if="currentMode === 'keyword'">
        <div class="filter-field">
          <label>关键字</label>
          <input v-model="filters.keyword" class="filter-input" placeholder="搜索日志内容..." @keyup.enter="doSearch" />
        </div>
      </template>

      <!-- 模式3: 日期 -->
      <template v-if="currentMode === 'date'">
        <div class="filter-field">
          <label>日期</label>
          <input type="date" v-model="filters.date" class="filter-input" />
        </div>
      </template>

      <!-- 模式4: 时间范围 -->
      <template v-if="currentMode === 'time-range'">
        <div class="filter-field">
          <label>开始时间</label>
          <input type="datetime-local" v-model="filters.startTime" class="filter-input" />
        </div>
        <div class="filter-field">
          <label>结束时间</label>
          <input type="datetime-local" v-model="filters.endTime" class="filter-input" />
        </div>
      </template>

      <!-- 模式5: 日期 + 关键字 -->
      <template v-if="currentMode === 'date-keyword'">
        <div class="filter-field">
          <label>日期</label>
          <input type="date" v-model="filters.date" class="filter-input" />
        </div>
        <div class="filter-field">
          <label>关键字</label>
          <input v-model="filters.keyword" class="filter-input" placeholder="搜索日志内容..." @keyup.enter="doSearch" />
        </div>
      </template>

      <button class="btn btn-primary search-btn" @click="doSearch">
        <svg viewBox="0 0 16 16" fill="none"><circle cx="6.5" cy="6.5" r="4.5" stroke="currentColor" stroke-width="1.3"/><path d="M10 10l3 3" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/></svg>
        查询
      </button>
    </div>

    <!-- 未选设备提示 -->
    <div v-if="!selectedDeviceId" class="empty-cell">
      <div class="empty-state">
        <svg viewBox="0 0 48 48" fill="none"><rect x="8" y="8" width="32" height="32" rx="4" stroke="currentColor" stroke-width="1.5" opacity="0.25"/><path d="M18 20h12M18 26h8M18 32h10" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" opacity="0.25"/></svg>
        <p>请先在上方选择一台设备</p>
      </div>
    </div>

    <!-- 结果表格 -->
    <div class="table-wrap" v-if="selectedDeviceId">
      <div v-if="loading" class="skeleton-rows">
        <div v-for="i in 5" :key="i" class="skeleton-row">
          <div class="skel skel-sm"></div><div class="skel skel-lg"></div><div class="skel skel-md"></div>
        </div>
      </div>
      <div v-else-if="!searched" class="empty-cell">
        <div class="empty-state">
          <svg viewBox="0 0 48 48" fill="none"><circle cx="20" cy="20" r="14" stroke="currentColor" stroke-width="1.5" opacity="0.25"/><path d="M30 30l10 10" stroke="currentColor" stroke-width="2" stroke-linecap="round" opacity="0.25"/></svg>
          <p>设置筛选条件后点击查询</p>
        </div>
      </div>
      <table v-else-if="records.length > 0" class="data-table">
        <thead>
          <tr>
            <th style="width:70px">级别</th>
            <th>日志内容</th>
            <th style="width:160px">日志时间</th>
            <th style="width:160px">接收时间</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="r in records" :key="r.id" class="table-row" @click="expandedId = expandedId === r.id ? null : r.id">
            <td>
              <span class="level-tag" :class="levelClass(r.level)">{{ r.level }}</span>
            </td>
            <td>
              <div class="msg-cell" :class="{ expanded: expandedId === r.id }">{{ r.message }}</div>
            </td>
            <td class="time-cell">{{ formatTime(r.log_time) }}</td>
            <td class="time-cell">{{ formatTime(r.received_at) }}</td>
          </tr>
        </tbody>
      </table>
      <div v-else class="empty-cell">
        <div class="empty-state">
          <svg viewBox="0 0 48 48" fill="none"><rect x="8" y="6" width="28" height="36" rx="2" stroke="currentColor" stroke-width="1.5" opacity="0.25"/><path d="M16 20h12M16 26h8" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" opacity="0.25"/></svg>
          <p>未查询到匹配的日志记录</p>
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
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue';
import {
  getLogsByDeviceAndLevelApi,
  getLogsByDeviceAndKeywordApi,
  getLogsByDeviceAndDateApi,
  getLogsByDeviceAndTimeRangeApi,
  getLogsByDeviceDateAndKeywordApi,
} from '../../api/deviceLog';
import { getAllActiveDevicesApi, type DeviceSelectDto } from '../../api/device';
import type { PagedMetaData } from '../../api/employee';

type QueryMode = 'level' | 'keyword' | 'date' | 'time-range' | 'date-keyword';

const queryModes = [
  { key: 'level' as QueryMode, label: '按级别' },
  { key: 'keyword' as QueryMode, label: '按关键字' },
  { key: 'date' as QueryMode, label: '按日期' },
  { key: 'time-range' as QueryMode, label: '按时间范围' },
  { key: 'date-keyword' as QueryMode, label: '日期 + 关键字' },
];

const currentMode = ref<QueryMode>('level');
const loading = ref(false);
const searched = ref(false);
const records = ref<any[]>([]);
const currentPage = ref(1);
const metaData = ref<PagedMetaData>({ totalCount: 0, pageSize: 10, currentPage: 1, totalPages: 1 });
const expandedId = ref<string | null>(null);

const allDevices = ref<DeviceSelectDto[]>([]);
const selectedDeviceId = ref('');

const filters = reactive({
  level: '',
  keyword: '',
  date: '',
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

const levelClass = (level: string) => {
  switch (level?.toUpperCase()) {
    case 'INFO': return 'info';
    case 'WARN': return 'warn';
    case 'ERROR': return 'error';
    default: return 'info';
  }
};

const formatTime = (t: string) => {
  if (!t) return '—';
  try {
    const d = new Date(t);
    return d.toLocaleString('zh-CN', { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit' });
  } catch { return t; }
};

const switchMode = (mode: QueryMode) => {
  currentMode.value = mode;
  records.value = [];
  searched.value = false;
  currentPage.value = 1;
};

const onDeviceChange = () => {
  records.value = [];
  searched.value = false;
  currentPage.value = 1;
};

const doSearch = async () => {
  currentPage.value = 1;
  await fetchData();
};

const fetchData = async () => {
  if (!selectedDeviceId.value) { alert('请先选择设备'); return; }
  loading.value = true;
  searched.value = true;
  try {
    let raw: any;
    const pagination = { PageNumber: currentPage.value, PageSize: 10 };
    const deviceId = selectedDeviceId.value;

    switch (currentMode.value) {
      case 'level':
        raw = await getLogsByDeviceAndLevelApi({ pagination, deviceId, level: filters.level || undefined });
        break;
      case 'keyword':
        if (!filters.keyword.trim()) { alert('请输入关键字'); loading.value = false; return; }
        raw = await getLogsByDeviceAndKeywordApi({ pagination, deviceId, keyword: filters.keyword });
        break;
      case 'date':
        if (!filters.date) { alert('请选择日期'); loading.value = false; return; }
        raw = await getLogsByDeviceAndDateApi({ pagination, deviceId, date: filters.date });
        break;
      case 'time-range':
        if (!filters.startTime || !filters.endTime) { alert('请选择完整时间范围'); loading.value = false; return; }
        raw = await getLogsByDeviceAndTimeRangeApi({ pagination, deviceId, startTime: filters.startTime, endTime: filters.endTime });
        break;
      case 'date-keyword':
        if (!filters.date || !filters.keyword.trim()) { alert('请选择日期并输入关键字'); loading.value = false; return; }
        raw = await getLogsByDeviceDateAndKeywordApi({ pagination, deviceId, date: filters.date, keyword: filters.keyword });
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

onMounted(async () => {
  try { allDevices.value = await getAllActiveDevicesApi() as unknown as DeviceSelectDto[]; } catch { allDevices.value = []; }
});
</script>

<style scoped>
* { box-sizing: border-box; }
.device-log-page { font-family: 'Noto Sans SC', sans-serif; color: #e0e4ef; }

.page-header { display: flex; align-items: flex-start; justify-content: space-between; margin-bottom: 20px; }
.page-title { font-size: 22px; font-weight: 600; color: #fff; margin: 0 0 4px; }
.page-sub { font-size: 13px; color: rgba(255,255,255,0.35); margin: 0; }

/* 设备选择栏 */
.device-select-bar { display: flex; align-items: center; gap: 12px; margin-bottom: 14px; padding: 12px 16px; background: rgba(0,229,255,0.04); border: 1px solid rgba(0,229,255,0.12); border-radius: 4px; }
.device-select-bar label { font-size: 13px; color: rgba(0,229,255,0.7); font-weight: 500; white-space: nowrap; }
.device-select { flex: 1; max-width: 400px; }

/* 查询模式切换 */
.query-modes { display: flex; gap: 6px; margin-bottom: 14px; }
.mode-btn { padding: 6px 14px; border-radius: 4px; border: 1px solid rgba(255,255,255,0.08); background: rgba(255,255,255,0.03); color: rgba(255,255,255,0.45); font-size: 12px; font-family: 'Noto Sans SC', sans-serif; cursor: pointer; transition: all 0.18s; }
.mode-btn:hover { border-color: rgba(0,229,255,0.2); color: rgba(255,255,255,0.7); }
.mode-btn.active { background: rgba(0,229,255,0.1); border-color: rgba(0,229,255,0.35); color: #00e5ff; }

/* 筛选条件 */
.filter-bar { display: flex; align-items: flex-end; gap: 12px; margin-bottom: 16px; padding: 14px 16px; background: rgba(255,255,255,0.02); border: 1px solid rgba(255,255,255,0.06); border-radius: 4px; flex-wrap: wrap; }
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
.data-table th { padding: 11px 14px; text-align: left; font-size: 11px; font-weight: 500; color: rgba(255,255,255,0.3); letter-spacing: 1px; text-transform: uppercase; white-space: nowrap; }
.table-row { border-bottom: 1px solid rgba(255,255,255,0.04); cursor: pointer; transition: background 0.15s; }
.table-row:last-child { border-bottom: none; }
.table-row:hover { background: rgba(0,229,255,0.03); }
.data-table td { padding: 10px 14px; font-size: 13px; vertical-align: top; }
.time-cell { font-size: 12px; color: rgba(255,255,255,0.45); white-space: nowrap; }

/* 日志级别标签 */
.level-tag { font-size: 10px; font-weight: 600; padding: 3px 8px; border-radius: 3px; letter-spacing: 0.5px; display: inline-block; text-align: center; min-width: 48px; }
.level-tag.info { background: rgba(0,149,255,0.12); color: #4dabf7; border: 1px solid rgba(0,149,255,0.2); }
.level-tag.warn { background: rgba(255,179,0,0.12); color: #ffb300; border: 1px solid rgba(255,179,0,0.2); }
.level-tag.error { background: rgba(255,77,79,0.12); color: #ff8888; border: 1px solid rgba(255,77,79,0.2); }

/* 日志消息（点击展开） */
.msg-cell { font-size: 12px; color: rgba(255,255,255,0.65); line-height: 1.5; max-height: 40px; overflow: hidden; text-overflow: ellipsis; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; transition: max-height 0.3s; word-break: break-all; }
.msg-cell.expanded { max-height: 400px; -webkit-line-clamp: unset; }

.skeleton-rows { padding: 8px 0; }
.skeleton-row { display: flex; gap: 16px; padding: 14px 16px; border-bottom: 1px solid rgba(255,255,255,0.04); }
.skel { background: rgba(255,255,255,0.06); border-radius: 3px; height: 14px; animation: shimmer 1.5s infinite; }
.skel-sm { width: 70px; } .skel-md { width: 120px; } .skel-lg { width: 280px; }
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
</style>
