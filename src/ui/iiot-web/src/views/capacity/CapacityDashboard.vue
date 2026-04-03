<template>
  <div class="capacity-page">
    <div class="page-header">
      <div>
        <h1 class="page-title">产能看板</h1>
        <p class="page-sub">查看所有机台每日产能汇总，点击设备查看详细报表</p>
      </div>
    </div>

    <div class="filter-bar">
      <div class="filter-field">
        <label>筛选设备</label>
        <select v-model="filterDeviceId" class="filter-input" @change="onFilterChange">
          <option value="">全部设备</option>
          <option v-for="d in allDevices" :key="d.id" :value="d.id">{{ d.deviceName }}</option>
        </select>
      </div>
      <div class="filter-field">
        <label>筛选日期</label>
        <input type="date" v-model="filterDate" class="filter-input" @change="onFilterChange" />
      </div>
      <button class="btn btn-ghost" @click="clearFilters">清空筛选</button>
    </div>

    <!-- 统计卡片 -->
    <div class="stat-cards">
      <div class="stat-card">
        <span class="stat-label">总产出</span>
        <span class="stat-value">{{ totalStats.total }}</span>
      </div>
      <div class="stat-card ok">
        <span class="stat-label">良品</span>
        <span class="stat-value">{{ totalStats.ok }}</span>
      </div>
      <div class="stat-card ng">
        <span class="stat-label">不良品</span>
        <span class="stat-value">{{ totalStats.ng }}</span>
      </div>
      <div class="stat-card rate">
        <span class="stat-label">综合良率</span>
        <span class="stat-value">{{ totalStats.rate }}</span>
      </div>
    </div>

    <div class="table-wrap">
      <div v-if="loading" class="skeleton-rows">
        <div v-for="i in 5" :key="i" class="skeleton-row">
          <div class="skel skel-md"></div>
          <div class="skel skel-sm"></div>
          <div class="skel skel-sm"></div>
          <div class="skel skel-sm"></div>
          <div class="skel skel-md"></div>
        </div>
      </div>
      <table v-else-if="records.length > 0" class="data-table">
        <thead>
          <tr>
            <th>设备</th>
            <th>日期</th>
            <th>总产出</th>
            <th>良品</th>
            <th>不良品</th>
            <th>良率</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="r in records" :key="r.device_id + r.date" class="table-row">
            <td><span class="device-name">{{ r.device_name }}</span></td>
            <td>{{ r.date }}</td>
            <td class="mono">{{ r.total_count }}</td>
            <td class="mono ok-num">{{ r.ok_count }}</td>
            <td class="mono ng-num">{{ r.ng_count }}</td>
            <td>
              <div class="rate-bar-wrap">
                <div class="rate-bar" :style="{ width: r.ok_rate + '%' }" :class="rateClass(r.ok_rate)"></div>
                <span class="rate-text" :class="rateClass(r.ok_rate)">{{ r.ok_rate }}%</span>
              </div>
            </td>
            <td>
              <button class="icon-btn detail" title="查看详细报表"
                @click="goDetail(r.device_id, r.device_name)">
                <svg viewBox="0 0 16 16" fill="none">
                  <path d="M2 12l4-4 3 2 5-6" stroke="currentColor" stroke-width="1.3"
                    stroke-linecap="round" stroke-linejoin="round"/>
                </svg>
              </button>
            </td>
          </tr>
        </tbody>
      </table>
      <div v-else class="empty-cell">
        <div class="empty-state">
          <svg viewBox="0 0 48 48" fill="none">
            <rect x="6"  y="20" width="8" height="18" rx="1" stroke="currentColor" stroke-width="1.5" opacity="0.25"/>
            <rect x="18" y="12" width="8" height="26" rx="1" stroke="currentColor" stroke-width="1.5" opacity="0.25"/>
            <rect x="30" y="6"  width="8" height="32" rx="1" stroke="currentColor" stroke-width="1.5" opacity="0.25"/>
          </svg>
          <p>暂无产能数据</p>
        </div>
      </div>
    </div>

    <div class="pagination" v-if="metaData.totalPages > 1">
      <button class="page-btn" :disabled="currentPage===1" @click="goPage(currentPage-1)">
        <svg viewBox="0 0 12 12" fill="none"><path d="M8 2L4 6l4 4" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/></svg>
      </button>
      <button v-for="p in pageNumbers" :key="p" class="page-btn"
        :class="{ active: p === currentPage }" @click="goPage(p)">{{ p }}</button>
      <button class="page-btn" :disabled="currentPage===metaData.totalPages" @click="goPage(currentPage+1)">
        <svg viewBox="0 0 12 12" fill="none"><path d="M4 2l4 4-4 4" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/></svg>
      </button>
      <span class="total-badge">共 {{ metaData.totalCount }} 条</span>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { getDailyPagedApi, type DailyCapacityItem } from '../../api/capacity';
import { getAllActiveDevicesApi, type DeviceSelectDto } from '../../api/device';
import type { PagedMetaData } from '../../api/employee';

const router = useRouter();

const loading     = ref(false);
const records     = ref<DailyCapacityItem[]>([]);
const currentPage = ref(1);
const metaData    = ref<PagedMetaData>({ totalCount: 0, pageSize: 10, currentPage: 1, totalPages: 1 });
const allDevices  = ref<DeviceSelectDto[]>([]);
const filterDeviceId = ref('');

const todayLocal = () => {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`;
};
const filterDate = ref(todayLocal());

const pageNumbers = computed(() => {
  const total = metaData.value.totalPages;
  const cur   = currentPage.value;
  const range: number[] = [];
  for (let i = Math.max(1, cur-2); i <= Math.min(total, cur+2); i++) range.push(i);
  return range;
});

const totalStats = computed(() => {
  const total = records.value.reduce((s, r) => s + r.total_count, 0);
  const ok    = records.value.reduce((s, r) => s + r.ok_count,    0);
  const ng    = records.value.reduce((s, r) => s + r.ng_count,    0);
  const rate  = total > 0 ? (ok * 100 / total).toFixed(1) + '%' : '0%';
  return { total, ok, ng, rate };
});

const rateClass = (rate: number) => {
  if (rate >= 95) return 'rate-good';
  if (rate >= 85) return 'rate-warn';
  return 'rate-bad';
};

const onFilterChange = () => { currentPage.value = 1; fetchData(); };
const clearFilters   = () => {
  filterDeviceId.value = '';
  filterDate.value = todayLocal();
  currentPage.value = 1;
  fetchData();
};

const fetchData = async () => {
  loading.value = true;
  try {
    const raw = await getDailyPagedApi({
      PageNumber: currentPage.value,
      PageSize:   10,
      date:       filterDate.value || undefined,
      deviceId:   filterDeviceId.value || undefined,
    }) as any;
    if (raw?.metaData) {
      metaData.value = raw.metaData;
      records.value  = Array.isArray(raw.items) ? raw.items : [];
    } else {
      records.value = [];
    }
  } catch {
    records.value = [];
  } finally {
    loading.value = false;
  }
};

const goPage   = (page: number) => { currentPage.value = page; fetchData(); };
const goDetail = (deviceId: string, deviceName: string) => {
  router.push({ name: 'CapacityDetail', query: { deviceId, deviceName } });
};

onMounted(async () => {
  try { allDevices.value = await getAllActiveDevicesApi() as unknown as DeviceSelectDto[]; } catch { allDevices.value = []; }
  fetchData();
});
</script>

<style scoped>
* { box-sizing: border-box; }
.capacity-page { font-family: 'Noto Sans SC', sans-serif; color: #e0e4ef; }
.page-header { margin-bottom: 20px; }
.page-title { font-size: 22px; font-weight: 600; color: #fff; margin: 0 0 4px; }
.page-sub { font-size: 13px; color: rgba(255,255,255,0.35); margin: 0; }

.filter-bar { display: flex; align-items: flex-end; gap: 12px; margin-bottom: 16px; padding: 14px 16px; background: rgba(255,255,255,0.02); border: 1px solid rgba(255,255,255,0.06); border-radius: 4px; }
.filter-field { display: flex; flex-direction: column; gap: 5px; min-width: 180px; }
.filter-field label { font-size: 11px; color: rgba(255,255,255,0.35); font-weight: 500; }
.filter-input { background: rgba(255,255,255,0.04); border: 1px solid rgba(255,255,255,0.1); border-radius: 3px; padding: 8px 10px; color: rgba(255,255,255,0.8); font-size: 13px; outline: none; transition: border-color 0.2s; }
.filter-input:focus { border-color: rgba(0,229,255,0.4); }
select.filter-input { appearance: none; background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' viewBox='0 0 12 12'%3E%3Cpath d='M3 4.5l3 3 3-3' stroke='%2300e5ff' stroke-width='1.2' fill='none' stroke-linecap='round'/%3E%3C/svg%3E"); background-repeat: no-repeat; background-position: right 10px center; padding-right: 28px; cursor: pointer; }
select.filter-input option { background: #0f1525; color: #e0e4ef; }

.stat-cards { display: grid; grid-template-columns: repeat(4, 1fr); gap: 12px; margin-bottom: 16px; }
.stat-card { background: rgba(255,255,255,0.03); border: 1px solid rgba(255,255,255,0.06); border-radius: 4px; padding: 16px 18px; display: flex; flex-direction: column; gap: 6px; }
.stat-card.ok  { border-color: rgba(0,229,160,0.15); }
.stat-card.ng  { border-color: rgba(255,77,79,0.15); }
.stat-card.rate { border-color: rgba(0,229,255,0.15); }
.stat-label { font-size: 11px; color: rgba(255,255,255,0.35); }
.stat-value { font-size: 24px; font-weight: 600; color: #fff; font-family: 'Courier New', monospace; }
.stat-card.ok  .stat-value { color: #00e5a0; }
.stat-card.ng  .stat-value { color: #ff8888; }
.stat-card.rate .stat-value { color: #00e5ff; }

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
.device-name { font-size: 13px; font-weight: 500; color: rgba(255,255,255,0.85); }

.rate-bar-wrap { display: flex; align-items: center; gap: 8px; min-width: 110px; }
.rate-bar { height: 6px; border-radius: 3px; transition: width 0.3s; min-width: 2px; }
.rate-text { font-size: 12px; font-family: 'Courier New', monospace; white-space: nowrap; }
.rate-good .rate-bar { background: rgba(0,229,160,0.6); }
.rate-warn .rate-bar { background: rgba(255,179,0,0.5); }
.rate-bad  .rate-bar { background: rgba(255,77,79,0.5); }
.rate-good { color: #00e5a0; }
.rate-warn { color: #ffb300; }
.rate-bad  { color: #ff8888; }

.icon-btn { display: inline-flex; align-items: center; justify-content: center; width: 28px; height: 28px; border-radius: 3px; border: none; cursor: pointer; background: rgba(255,255,255,0.04); color: rgba(255,255,255,0.4); transition: all 0.15s; }
.icon-btn svg { width: 14px; height: 14px; }
.icon-btn.detail:hover { background: rgba(0,229,255,0.12); color: #00e5ff; }

.skeleton-rows { padding: 8px 0; }
.skeleton-row { display: flex; gap: 16px; padding: 14px 16px; border-bottom: 1px solid rgba(255,255,255,0.04); }
.skel { background: rgba(255,255,255,0.06); border-radius: 3px; height: 14px; animation: shimmer 1.5s infinite; }
.skel-sm { width: 70px; } .skel-md { width: 120px; }
@keyframes shimmer { 0%,100% { opacity:0.5; } 50% { opacity:1; } }

.empty-cell { text-align: center; padding: 56px 0; }
.empty-state { display: flex; flex-direction: column; align-items: center; gap: 12px; }
.empty-state svg { width: 52px; height: 52px; color: rgba(255,255,255,0.2); }
.empty-state p { font-size: 13px; color: rgba(255,255,255,0.25); margin: 0; }

.pagination { display: flex; justify-content: center; align-items: center; gap: 6px; margin-top: 20px; }
.page-btn { width: 32px; height: 32px; border-radius: 3px; border: 1px solid rgba(255,255,255,0.08); background: rgba(255,255,255,0.03); color: rgba(255,255,255,0.45); font-size: 13px; cursor: pointer; display: flex; align-items: center; justify-content: center; transition: all 0.15s; }
.page-btn:hover:not(:disabled) { border-color: rgba(0,229,255,0.3); color: #00e5ff; }
.page-btn.active { background: rgba(0,229,255,0.12); border-color: rgba(0,229,255,0.4); color: #00e5ff; }
.page-btn:disabled { opacity: 0.3; cursor: not-allowed; }
.page-btn svg { width: 12px; height: 12px; }
.total-badge { font-size: 12px; color: rgba(255,255,255,0.3); margin-left: 8px; }

.btn { display: inline-flex; align-items: center; gap: 6px; padding: 8px 14px; border-radius: 3px; border: none; font-size: 13px; font-family: 'Noto Sans SC', sans-serif; font-weight: 500; cursor: pointer; transition: all 0.18s; }
.btn-ghost { background: rgba(255,255,255,0.05); color: rgba(255,255,255,0.55); border: 1px solid rgba(255,255,255,0.1); }
.btn-ghost:hover { background: rgba(255,255,255,0.08); color: rgba(255,255,255,0.75); }
</style>