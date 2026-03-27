<template>
  <div class="capacity-page">
    <!-- 页头 -->
    <div class="page-header">
      <div>
        <h1 class="page-title">产能看板</h1>
        <p class="page-sub">查看所有机台每日产能汇总，支持按设备查看趋势</p>
      </div>
    </div>

    <!-- 筛选栏 -->
    <div class="filter-bar">
      <div class="filter-field">
        <label>筛选设备</label>
        <select v-model="filterDeviceId" class="filter-input" @change="onFilterChange">
          <option value="">全部设备</option>
          <option v-for="d in allDevices" :key="d.id" :value="d.id">{{ d.deviceCode }} · {{ d.deviceName }}</option>
        </select>
      </div>
      <div class="filter-field">
        <label>筛选日期</label>
        <input type="date" v-model="filterDate" class="filter-input" @change="onFilterChange" />
      </div>
      <button class="btn btn-ghost" @click="clearFilters">清空筛选</button>
    </div>

    <!-- 产能表格 -->
    <div class="table-wrap">
      <div v-if="loading" class="skeleton-rows">
        <div v-for="i in 5" :key="i" class="skeleton-row">
          <div class="skel skel-md"></div><div class="skel skel-sm"></div>
          <div class="skel skel-sm"></div><div class="skel skel-sm"></div><div class="skel skel-md"></div>
        </div>
      </div>
      <table v-else-if="records.length > 0" class="data-table">
        <thead>
          <tr>
            <th>设备</th>
            <th>日期</th>
            <th>班次</th>
            <th>总产出</th>
            <th>良品</th>
            <th>不良品</th>
            <th>良率</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="r in records" :key="r.id" class="table-row">
            <td>
              <div class="device-cell">
                <span class="device-code">{{ r.device_code }}</span>
                <span class="device-name">{{ r.device_name }}</span>
              </div>
            </td>
            <td>{{ r.date }}</td>
            <td><span class="shift-tag">{{ r.shift_code }}</span></td>
            <td class="mono">{{ r.total_count }}</td>
            <td class="mono ok-num">{{ r.ok_count }}</td>
            <td class="mono ng-num">{{ r.ng_count }}</td>
            <td>
              <div class="rate-bar-wrap">
                <div class="rate-bar" :style="{ width: r.ok_rate + '%' }" :class="rateClass(r.ok_rate)"></div>
                <span class="rate-text">{{ r.ok_rate }}%</span>
              </div>
            </td>
            <td>
              <button class="icon-btn trend" title="查看趋势" @click="loadTrend(r.device_id, r.device_code, r.device_name)">
                <svg viewBox="0 0 16 16" fill="none"><path d="M2 12l4-4 3 2 5-6" stroke="currentColor" stroke-width="1.3" stroke-linecap="round" stroke-linejoin="round"/></svg>
              </button>
            </td>
          </tr>
        </tbody>
      </table>
      <div v-else class="empty-cell">
        <div class="empty-state">
          <svg viewBox="0 0 48 48" fill="none"><rect x="6" y="20" width="8" height="18" rx="1" stroke="currentColor" stroke-width="1.5" opacity="0.25"/><rect x="18" y="12" width="8" height="26" rx="1" stroke="currentColor" stroke-width="1.5" opacity="0.25"/><rect x="30" y="6" width="8" height="32" rx="1" stroke="currentColor" stroke-width="1.5" opacity="0.25"/></svg>
          <p>暂无产能数据</p>
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

    <!-- 趋势面板 -->
    <Teleport to="body">
      <div v-if="showTrend" class="detail-overlay" @click.self="showTrend=false">
        <div class="trend-panel">
          <div class="trend-header">
            <div>
              <span class="trend-title">产能趋势</span>
              <span class="trend-subtitle">{{ trendDeviceCode }} · {{ trendDeviceName }} · 最近7天</span>
            </div>
            <button class="modal-close" @click="showTrend=false">✕</button>
          </div>
          <div class="trend-body" v-if="trendLoading">
            <div class="detail-loading"><div class="loading-ring"></div><span>加载中...</span></div>
          </div>
          <div class="trend-body" v-else-if="trendData.length > 0">
            <!-- 简易柱状图 -->
            <div class="chart-area">
              <div class="chart-bars">
                <div class="chart-col" v-for="item in trendData" :key="item.date">
                  <div class="bar-stack">
                    <div class="bar ok" :style="{ height: barHeight(item.ok_count) + 'px' }" :title="'良品: ' + item.ok_count"></div>
                    <div class="bar ng" :style="{ height: barHeight(item.ng_count) + 'px' }" :title="'不良: ' + item.ng_count"></div>
                  </div>
                  <span class="bar-label">{{ formatDate(item.date) }}</span>
                  <span class="bar-total">{{ item.total_count }}</span>
                </div>
              </div>
              <div class="chart-legend">
                <span class="legend-item"><span class="legend-dot ok"></span>良品</span>
                <span class="legend-item"><span class="legend-dot ng"></span>不良品</span>
              </div>
            </div>
            <!-- 趋势数据表 -->
            <div class="trend-table">
              <div class="trend-table-header">
                <span>日期</span><span>班次</span><span>总产出</span><span>良品</span><span>不良</span><span>良率</span>
              </div>
              <div class="trend-table-row" v-for="item in trendData" :key="item.date + item.shift_code">
                <span>{{ item.date }}</span>
                <span><span class="shift-tag sm">{{ item.shift_code }}</span></span>
                <span class="mono">{{ item.total_count }}</span>
                <span class="mono ok-num">{{ item.ok_count }}</span>
                <span class="mono ng-num">{{ item.ng_count }}</span>
                <span class="mono" :class="rateClass(item.ok_rate)">{{ item.ok_rate }}%</span>
              </div>
            </div>
          </div>
          <div class="trend-body" v-else>
            <div class="empty-state" style="padding-top: 40px;">
              <p>该设备最近7天暂无产能数据</p>
            </div>
          </div>
        </div>
      </div>
    </Teleport>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { getDailyCapacityPagedApi, getDeviceCapacitySummaryApi } from '../../api/capacity';
import { getAllActiveDevicesApi, type DeviceSelectDto } from '../../api/device';
import type { PagedMetaData } from '../../api/employee';

const loading = ref(false);
const records = ref<any[]>([]);
const currentPage = ref(1);
const metaData = ref<PagedMetaData>({ totalCount: 0, pageSize: 10, currentPage: 1, totalPages: 1 });

const allDevices = ref<DeviceSelectDto[]>([]);
const filterDeviceId = ref('');
const filterDate = ref('');

const pageNumbers = computed(() => {
  const total = metaData.value.totalPages;
  const cur = currentPage.value;
  const range: number[] = [];
  for (let i = Math.max(1, cur - 2); i <= Math.min(total, cur + 2); i++) range.push(i);
  return range;
});

const rateClass = (rate: number) => {
  if (rate >= 95) return 'rate-good';
  if (rate >= 85) return 'rate-warn';
  return 'rate-bad';
};

const onFilterChange = () => { currentPage.value = 1; fetchData(); };
const clearFilters = () => { filterDeviceId.value = ''; filterDate.value = ''; currentPage.value = 1; fetchData(); };

const fetchData = async () => {
  loading.value = true;
  try {
    const raw = await getDailyCapacityPagedApi({
      pagination: { PageNumber: currentPage.value, PageSize: 10 },
      date: filterDate.value || undefined,
      deviceId: filterDeviceId.value || undefined,
    }) as any;

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

// ── 趋势面板 ──
const showTrend = ref(false);
const trendLoading = ref(false);
const trendData = ref<any[]>([]);
const trendDeviceCode = ref('');
const trendDeviceName = ref('');

const loadTrend = async (deviceId: string, deviceCode: string, deviceName: string) => {
  trendDeviceCode.value = deviceCode;
  trendDeviceName.value = deviceName;
  showTrend.value = true;
  trendLoading.value = true;
  trendData.value = [];

  const today = new Date();
  const endDate = today.toISOString().split('T')[0];
  const start = new Date(today);
  start.setDate(start.getDate() - 6);
  const startDate = start.toISOString().split('T')[0];

  try {
    const raw = await getDeviceCapacitySummaryApi({ deviceId, startDate, endDate });
    trendData.value = Array.isArray(raw) ? raw : [];
  } catch {
    trendData.value = [];
  } finally {
    trendLoading.value = false;
  }
};

const trendMaxCount = computed(() => {
  if (trendData.value.length === 0) return 1;
  return Math.max(...trendData.value.map((d: any) => d.total_count || 1), 1);
});

const barHeight = (count: number) => {
  const maxH = 140;
  return Math.max(2, Math.round((count / trendMaxCount.value) * maxH));
};

const formatDate = (d: string) => {
  if (!d) return '';
  const parts = d.split('-');
  return parts.length >= 3 ? `${parts[1]}/${parts[2]}` : d;
};

onMounted(async () => {
  try { allDevices.value = await getAllActiveDevicesApi() as unknown as DeviceSelectDto[]; } catch { allDevices.value = []; }
  fetchData();
});
</script>

<style scoped>
* { box-sizing: border-box; }
.capacity-page { font-family: 'Noto Sans SC', sans-serif; color: #e0e4ef; }

.page-header { display: flex; align-items: flex-start; justify-content: space-between; margin-bottom: 20px; }
.page-title { font-size: 22px; font-weight: 600; color: #fff; margin: 0 0 4px; }
.page-sub { font-size: 13px; color: rgba(255,255,255,0.35); margin: 0; }

/* 筛选栏 */
.filter-bar { display: flex; align-items: flex-end; gap: 12px; margin-bottom: 16px; padding: 14px 16px; background: rgba(255,255,255,0.02); border: 1px solid rgba(255,255,255,0.06); border-radius: 4px; }
.filter-field { display: flex; flex-direction: column; gap: 5px; min-width: 180px; }
.filter-field label { font-size: 11px; color: rgba(255,255,255,0.35); font-weight: 500; }
.filter-input { background: rgba(255,255,255,0.04); border: 1px solid rgba(255,255,255,0.1); border-radius: 3px; padding: 8px 10px; color: rgba(255,255,255,0.8); font-size: 13px; font-family: 'Noto Sans SC', sans-serif; outline: none; transition: border-color 0.2s; }
.filter-input:focus { border-color: rgba(0,229,255,0.4); }
select.filter-input { appearance: none; background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' viewBox='0 0 12 12'%3E%3Cpath d='M3 4.5l3 3 3-3' stroke='%2300e5ff' stroke-width='1.2' fill='none' stroke-linecap='round'/%3E%3C/svg%3E"); background-repeat: no-repeat; background-position: right 10px center; padding-right: 28px; cursor: pointer; }
select.filter-input option { background: #0f1525; color: #e0e4ef; }

/* 表格 */
.table-wrap { background: rgba(255,255,255,0.02); border: 1px solid rgba(255,255,255,0.06); border-radius: 4px; overflow: hidden; }
.data-table { width: 100%; border-collapse: collapse; }
.data-table thead tr { background: rgba(255,255,255,0.03); border-bottom: 1px solid rgba(255,255,255,0.06); }
.data-table th { padding: 11px 14px; text-align: left; font-size: 11px; font-weight: 500; color: rgba(255,255,255,0.3); letter-spacing: 1px; text-transform: uppercase; white-space: nowrap; }
.table-row { border-bottom: 1px solid rgba(255,255,255,0.04); transition: background 0.15s; }
.table-row:last-child { border-bottom: none; }
.table-row:hover { background: rgba(0,229,255,0.03); }
.data-table td { padding: 12px 14px; font-size: 13px; vertical-align: middle; }
.mono { font-family: 'Courier New', monospace; font-size: 12px; }
.ok-num { color: #00e5a0; }
.ng-num { color: #ff8888; }

.device-cell { display: flex; flex-direction: column; gap: 2px; }
.device-code { font-size: 13px; font-weight: 500; color: rgba(255,255,255,0.8); }
.device-name { font-size: 11px; color: rgba(255,255,255,0.35); }

.shift-tag { font-size: 10px; background: rgba(0,229,255,0.08); color: rgba(0,229,255,0.7); padding: 2px 8px; border-radius: 3px; border: 1px solid rgba(0,229,255,0.15); font-weight: 500; }
.shift-tag.sm { font-size: 10px; padding: 1px 6px; }

/* 良率条 */
.rate-bar-wrap { display: flex; align-items: center; gap: 8px; min-width: 100px; }
.rate-bar { height: 6px; border-radius: 3px; transition: width 0.3s; }
.rate-text { font-size: 12px; font-family: 'Courier New', monospace; white-space: nowrap; }
.rate-good, .rate-good .rate-bar { background: rgba(0,229,160,0.6); color: #00e5a0; }
.rate-warn, .rate-warn .rate-bar { background: rgba(255,179,0,0.5); color: #ffb300; }
.rate-bad, .rate-bad .rate-bar { background: rgba(255,77,79,0.5); color: #ff8888; }

.icon-btn { display: inline-flex; align-items: center; justify-content: center; width: 28px; height: 28px; border-radius: 3px; border: none; cursor: pointer; background: rgba(255,255,255,0.04); color: rgba(255,255,255,0.4); transition: all 0.15s; }
.icon-btn svg { width: 14px; height: 14px; }
.icon-btn.trend:hover { background: rgba(0,229,255,0.12); color: #00e5ff; }

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
.btn { display: inline-flex; align-items: center; gap: 6px; padding: 8px 14px; border-radius: 3px; border: none; font-size: 13px; font-family: 'Noto Sans SC', sans-serif; font-weight: 500; cursor: pointer; transition: all 0.18s; }
.btn-ghost { background: rgba(255,255,255,0.05); color: rgba(255,255,255,0.55); border: 1px solid rgba(255,255,255,0.1); }
.btn-ghost:hover { background: rgba(255,255,255,0.08); color: rgba(255,255,255,0.75); }

/* 趋势面板 */
.detail-overlay { position: fixed; inset: 0; z-index: 100; background: rgba(0,0,0,0.5); display: flex; align-items: stretch; justify-content: flex-end; }
.trend-panel { width: 520px; background: #0f1525; border-left: 1px solid rgba(255,255,255,0.08); display: flex; flex-direction: column; animation: slideIn 0.22s cubic-bezier(0.4,0,0.2,1); }
@keyframes slideIn { from { transform: translateX(100%); } to { transform: translateX(0); } }
.trend-header { display: flex; align-items: center; justify-content: space-between; padding: 18px 22px; border-bottom: 1px solid rgba(255,255,255,0.06); }
.trend-title { font-size: 15px; font-weight: 600; color: #fff; display: block; }
.trend-subtitle { font-size: 12px; color: rgba(255,255,255,0.35); display: block; margin-top: 2px; }
.modal-close { background: none; border: none; color: rgba(255,255,255,0.3); font-size: 16px; cursor: pointer; }
.modal-close:hover { color: rgba(255,255,255,0.7); }
.trend-body { padding: 20px 22px; flex: 1; overflow-y: auto; }
.detail-loading { display: flex; flex-direction: column; align-items: center; gap: 14px; padding-top: 60px; color: rgba(255,255,255,0.3); font-size: 13px; }
.loading-ring { width: 32px; height: 32px; border: 2px solid rgba(0,229,255,0.15); border-top-color: #00e5ff; border-radius: 50%; animation: spin 0.8s linear infinite; }
@keyframes spin { to { transform: rotate(360deg); } }

/* 柱状图 */
.chart-area { margin-bottom: 24px; }
.chart-bars { display: flex; align-items: flex-end; justify-content: space-around; height: 180px; padding: 0 8px; gap: 8px; border-bottom: 1px solid rgba(255,255,255,0.08); }
.chart-col { display: flex; flex-direction: column; align-items: center; gap: 6px; flex: 1; }
.bar-stack { display: flex; flex-direction: column-reverse; align-items: center; }
.bar { width: 28px; border-radius: 3px 3px 0 0; transition: height 0.4s ease; }
.bar.ok { background: rgba(0,229,160,0.6); }
.bar.ng { background: rgba(255,77,79,0.5); border-radius: 0; }
.bar-label { font-size: 10px; color: rgba(255,255,255,0.35); margin-top: 4px; }
.bar-total { font-size: 11px; font-family: 'Courier New', monospace; color: rgba(255,255,255,0.5); }
.chart-legend { display: flex; gap: 16px; justify-content: center; margin-top: 12px; }
.legend-item { display: flex; align-items: center; gap: 5px; font-size: 11px; color: rgba(255,255,255,0.4); }
.legend-dot { width: 8px; height: 8px; border-radius: 2px; }
.legend-dot.ok { background: rgba(0,229,160,0.6); }
.legend-dot.ng { background: rgba(255,77,79,0.5); }

/* 趋势数据表 */
.trend-table { border: 1px solid rgba(255,255,255,0.06); border-radius: 4px; overflow: hidden; }
.trend-table-header { display: grid; grid-template-columns: 1.2fr 0.8fr 1fr 0.8fr 0.8fr 0.8fr; padding: 9px 12px; background: rgba(255,255,255,0.03); border-bottom: 1px solid rgba(255,255,255,0.06); }
.trend-table-header span { font-size: 11px; color: rgba(255,255,255,0.3); font-weight: 500; }
.trend-table-row { display: grid; grid-template-columns: 1.2fr 0.8fr 1fr 0.8fr 0.8fr 0.8fr; padding: 8px 12px; border-bottom: 1px solid rgba(255,255,255,0.04); font-size: 12px; color: rgba(255,255,255,0.7); }
.trend-table-row:last-child { border-bottom: none; }
</style>
