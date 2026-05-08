<template>
  <div class="dashboard">
    <HeroBanner
      :name="authStore.employeeNo"
      :role="authStore.role"
      :alert-count="alertCount"
      :data-state="dashboardState"
    />

    <div class="dashboard__grid">
      <StatCard
        class="dashboard__kpi"
        label="在线设备"
        :value="onlineDevicesDisplay"
        :unit="totalDevicesDisplay"
        accent="brand"
      />
      <StatCard
        class="dashboard__kpi"
        label="今日产量"
        :value="productionDisplay"
        unit="件"
        accent="success"
      />
      <StatCard
        class="dashboard__kpi"
        label="近24小时告警"
        :value="alertCountDisplay"
        unit="条"
        accent="warn"
      />
      <StatCard
        class="dashboard__kpi"
        label="合格率"
        :value="passRateDisplay"
        unit="%"
        accent="info"
      />

      <ProductionTrendChart
        class="dashboard__chart"
        :hours="hourly"
        :loading="loadingHourly"
        :is-demo="isHourlyDemo"
        :subtitle="hourlySubtitle"
        :show-fresh-status="dataReady"
      />
      <DeviceStatusDonut
        class="dashboard__donut"
        :segments="deviceSegments"
        :loading="loadingDashboard"
        :load-failed="loadFailed"
      />

      <EventStream
        class="dashboard__events"
        :events="events"
        :loading="loadingDashboard"
        :show-fresh-status="dataReady"
      />
      <QuickAccessGrid
        class="dashboard__quick"
        :alert-count="alertCount"
        :online-count="onlineDevices"
        :total-count="totalDevices"
        :today-production="todayProduction"
      />
    </div>

    <div v-if="dashboardError" class="dashboard__error">
      {{ dashboardError }}
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { useAuthStore } from '../stores/auth';
import HeroBanner from '../components/dashboard/HeroBanner.vue';
import ProductionTrendChart from '../components/dashboard/ProductionTrendChart.vue';
import DeviceStatusDonut from '../components/dashboard/DeviceStatusDonut.vue';
import EventStream, { type DashboardEvent } from '../components/dashboard/EventStream.vue';
import QuickAccessGrid from '../components/dashboard/QuickAccessGrid.vue';
import StatCard from '../components/data/StatCard.vue';
import { getDeviceStatusSummaryApi } from '../api/device';
import { getHourlyAggregateApi } from '../api/capacity';
import {
  getRecentAlertCountApi,
  getRecentDeviceLogsApi,
  type DeviceLogListItemDto,
} from '../api/deviceLog';

const authStore = useAuthStore();

const totalDevices = ref(0);
const onlineDevices = ref(0);
const warningDevices = ref(0);
const errorDevices = ref(0);
const offlineDevices = ref(0);
const todayProduction = ref(0);
const todayOkProduction = ref(0);
const alertCount = ref(0);
const loadingDashboard = ref(true);
const dashboardError = ref('');
const dataReady = ref(false);
const loadFailed = computed(() => !!dashboardError.value && !loadingDashboard.value);
const dashboardState = computed<'loading' | 'ready' | 'error'>(() => {
  if (loadingDashboard.value) return 'loading';
  if (loadFailed.value) return 'error';
  return dataReady.value ? 'ready' : 'loading';
});

const formattedProduction = computed(() =>
  todayProduction.value.toLocaleString('zh-CN'),
);

const passRate = computed(() =>
  todayProduction.value > 0
    ? (todayOkProduction.value / todayProduction.value) * 100
    : 0,
);
const onlineDevicesDisplay = computed(() => (dataReady.value ? onlineDevices.value : '--'));
const totalDevicesDisplay = computed(() => (dataReady.value ? `/ ${totalDevices.value}` : ''));
const productionDisplay = computed(() => (dataReady.value ? formattedProduction.value : '--'));
const alertCountDisplay = computed(() => (dataReady.value ? alertCount.value : '--'));
const passRateDisplay = computed(() => (dataReady.value ? passRate.value.toFixed(1) : '--'));

const hourly = ref<{ label: string; value: number }[]>([]);
const loadingHourly = ref(true);
const isHourlyDemo = ref(false);
const hourlySubtitle = ref('今日授权设备聚合产能');

const deviceSegments = computed(() => [
  { label: '在线', value: onlineDevices.value, color: '#0891b2' },
  { label: '警告', value: warningDevices.value, color: '#d97706' },
  { label: '故障', value: errorDevices.value, color: '#dc2626' },
  { label: '离线', value: offlineDevices.value, color: '#9ba3b4' },
]);

const events = ref<DashboardEvent[]>([]);

function todayIsoDate(): string {
  const d = new Date();
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

function toEventSeverity(level: string): DashboardEvent['severity'] {
  const normalized = level.trim().toUpperCase();
  if (normalized === 'ERROR' || normalized === 'ERR') return 'error';
  if (normalized === 'WARN' || normalized === 'WARNING') return 'warn';
  return 'info';
}

function toEventLabel(level: string): string {
  const normalized = level.trim().toUpperCase();
  if (normalized === 'ERROR') return 'ERR';
  if (normalized === 'WARNING') return 'WARN';
  if (normalized === 'INFORMATION') return 'INFO';
  return normalized || 'INFO';
}

function formatEventTime(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '--:--:--';
  return date.toLocaleTimeString('zh-CN', {
    hour12: false,
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  });
}

function mapEvent(log: DeviceLogListItemDto): DashboardEvent {
  return {
    time: formatEventTime(log.logTime),
    message: log.message,
    deviceCode: log.deviceName || log.deviceId.slice(0, 8),
    severity: toEventSeverity(log.level),
    label: toEventLabel(log.level),
  };
}

function resetDashboardData() {
  totalDevices.value = 0;
  onlineDevices.value = 0;
  warningDevices.value = 0;
  errorDevices.value = 0;
  offlineDevices.value = 0;
  todayProduction.value = 0;
  todayOkProduction.value = 0;
  alertCount.value = 0;
  hourly.value = [];
  events.value = [];
}

async function loadDashboard() {
  const today = todayIsoDate();
  loadingDashboard.value = true;
  loadingHourly.value = true;
  dashboardError.value = '';
  dataReady.value = false;

  try {
    const [statusSummary, hourlyData, alertSummary, recentLogs] = await Promise.all([
      getDeviceStatusSummaryApi(),
      getHourlyAggregateApi({ date: today }),
      getRecentAlertCountApi(),
      getRecentDeviceLogsApi({ limit: 20, minLevel: 'WARN' }),
    ]);

    totalDevices.value = statusSummary.total;
    onlineDevices.value = statusSummary.online;
    warningDevices.value = statusSummary.warning;
    errorDevices.value = statusSummary.error;
    offlineDevices.value = statusSummary.offline;
    alertCount.value = alertSummary.count;

    todayProduction.value = hourlyData.reduce((sum, item) => sum + (item.totalCount ?? 0), 0);
    todayOkProduction.value = hourlyData.reduce((sum, item) => sum + (item.okCount ?? 0), 0);
    hourly.value = hourlyData.map((item) => ({
      label: item.timeLabel || `${String(item.hour).padStart(2, '0')}:${String(item.minute).padStart(2, '0')}`,
      value: item.totalCount,
    }));
    events.value = recentLogs.map(mapEvent);
    isHourlyDemo.value = false;
    dataReady.value = true;
  } catch {
    resetDashboardData();
    dataReady.value = false;
    dashboardError.value = 'Dashboard 数据加载失败，请检查账号权限或后端服务状态。';
  } finally {
    loadingHourly.value = false;
    loadingDashboard.value = false;
  }
}

onMounted(loadDashboard);
</script>

<style scoped>
.dashboard {
  min-height: 100%;
}

.dashboard__grid {
  display: grid;
  grid-template-columns: repeat(12, 1fr);
  gap: var(--space-4);
}

.dashboard__error {
  margin-top: var(--space-4);
  padding: var(--space-3) var(--space-4);
  border: 1px solid var(--error-soft);
  border-radius: var(--radius-md);
  background: var(--error-soft);
  color: var(--error);
  font-size: var(--fs-sm);
}

.dashboard__kpi    { grid-column: span 3; }
.dashboard__chart  { grid-column: span 8; }
.dashboard__donut  { grid-column: span 4; }
.dashboard__events { grid-column: span 7; }
.dashboard__quick  { grid-column: span 5; }

@media (max-width: 1280px) {
  .dashboard__kpi    { grid-column: span 6; }
  .dashboard__chart  { grid-column: span 12; }
  .dashboard__donut  { grid-column: span 12; }
  .dashboard__events { grid-column: span 12; }
  .dashboard__quick  { grid-column: span 12; }
}

@media (max-width: 768px) {
  .dashboard__kpi { grid-column: span 12; }
}
</style>
