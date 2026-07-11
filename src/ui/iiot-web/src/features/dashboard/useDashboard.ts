import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import {
  Activity,
  BarChart3,
  Factory,
  Gauge,
  Route,
} from 'lucide-vue-next';
import { getHourlyAggregateApi } from '../capacity/api';
import {
  getRecentAlertCountApi,
  getRecentDeviceLogsApi,
} from '../device-logs/api';
import { getDeviceStatusSummaryApi } from '../devices/api';
import type { AppLocale } from '../../i18n';
import { useAuthStore } from '../../stores/auth';
import {
  mapDashboardEvent,
  hasDashboardData,
  todayIsoDate,
  type AnalysisLink,
  type DashboardCard,
  type DashboardEvent,
  type DashboardNonReadyState,
  type DashboardViewState,
} from './types';

export function useDashboard() {
  const authStore = useAuthStore();
  const { t, locale } = useI18n();
  const totalDevices = ref(0);
  const onlineDevices = ref(0);
  const warningDevices = ref(0);
  const errorDevices = ref(0);
  const offlineDevices = ref(0);
  const todayProduction = ref(0);
  const todayOkProduction = ref(0);
  const alertCount = ref(0);
  const dashboardState = ref<DashboardViewState>('loading');
  const dashboardNonReadyState = computed<DashboardNonReadyState | null>(() =>
    dashboardState.value === 'ready' ? null : dashboardState.value,
  );
  const hourly = ref<{ label: string; value: number }[]>([]);
  const events = ref<DashboardEvent[]>([]);

  const currentLocale = computed(() => locale.value as AppLocale);
  const browserLocale = computed(() =>
    currentLocale.value === 'zh-CN' ? 'zh-CN' : 'en-US',
  );
  const displayRole = computed(() => {
    if (!authStore.role) return t('layout.roleFallback');
    if (currentLocale.value === 'zh-CN' && authStore.role === 'Admin') return '管理员';
    return authStore.role;
  });
  const formattedProduction = computed(() =>
    todayProduction.value.toLocaleString(browserLocale.value),
  );
  const hasHourlyData = computed(() => hourly.value.length > 0);
  const passRate = computed(() =>
    todayProduction.value > 0
      ? (todayOkProduction.value / todayProduction.value) * 100
      : 0,
  );
  const productionDisplay = computed(() =>
    hasHourlyData.value ? formattedProduction.value : '--',
  );
  const passRateDisplay = computed(() =>
    hasHourlyData.value ? `${passRate.value.toFixed(1)}%` : '--',
  );
  const todayLabel = computed(() =>
    new Date().toLocaleDateString(browserLocale.value, {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
    }),
  );
  const dashboardCards = computed<DashboardCard[]>(() => [
    {
      id: 'production',
      label: t('dashboard.totalOutput'),
      value: productionDisplay.value,
      helper: t('dashboard.totalOutputHelper'),
      background: 'var(--chart-1)',
      icon: Factory,
    },
    {
      id: 'online-devices',
      label: t('dashboard.onlineDevices'),
      value: onlineDevices.value,
      helper: t('dashboard.onlineDevicesHelper', {
        count: `/ ${totalDevices.value}`,
      }),
      background: 'var(--chart-2)',
      icon: Activity,
    },
    {
      id: 'pass-rate',
      label: t('dashboard.passRate'),
      value: passRateDisplay.value,
      helper: t('dashboard.passRateHelper', {
        count: alertCount.value,
      }),
      background: 'var(--chart-3)',
      icon: Gauge,
    },
  ]);
  const trendBars = computed(() => {
    const source = hourly.value.slice(-10);
    if (!source.length) return [];
    const max = Math.max(...source.map((item) => item.value), 1);
    return source.map((item, index) => ({
      label: item.label,
      value: item.value,
      height: item.value > 0
        ? `${Math.max(8, Math.round((item.value / max) * 100))}%`
        : '0%',
      color: index % 2 === 0 ? 'var(--chart-1)' : 'var(--chart-3)',
    }));
  });
  const analysisLinks = computed<AnalysisLink[]>(() => [
    { label: t('dashboard.storeSellRatio'), to: '/capacity', icon: BarChart3 },
    { label: t('dashboard.topItemSold'), to: '/devices', icon: Factory },
    { label: t('dashboard.passTraceReview'), to: '/pass-station', icon: Route },
  ]);
  const statusRows = computed(() => [
    { label: t('dashboard.online'), value: onlineDevices.value, color: 'var(--success)' },
    { label: t('dashboard.warning'), value: warningDevices.value, color: 'var(--warn)' },
    { label: t('dashboard.error'), value: errorDevices.value, color: 'var(--error)' },
    { label: t('dashboard.offline'), value: offlineDevices.value, color: 'var(--text-2)' },
  ]);
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
    dashboardState.value = 'loading';
    resetDashboardData();

    try {
      const [statusSummary, hourlyData, alertSummary, recentLogs] = await Promise.all([
        getDeviceStatusSummaryApi(),
        getHourlyAggregateApi({ date: todayIsoDate() }),
        getRecentAlertCountApi(),
        getRecentDeviceLogsApi({ limit: 20, minLevel: 'WARN' }),
      ]);
      totalDevices.value = statusSummary.total;
      onlineDevices.value = statusSummary.online;
      warningDevices.value = statusSummary.warning;
      errorDevices.value = statusSummary.error;
      offlineDevices.value = statusSummary.offline;
      alertCount.value = alertSummary.count ?? 0;
      todayProduction.value = hourlyData.reduce((sum, item) => sum + (item.totalCount ?? 0), 0);
      todayOkProduction.value = hourlyData.reduce((sum, item) => sum + (item.okCount ?? 0), 0);
      hourly.value = hourlyData.map((item) => ({
        label: item.timeLabel || `${String(item.hour).padStart(2, '0')}:${String(item.minute).padStart(2, '0')}`,
        value: item.totalCount,
      }));
      events.value = recentLogs.map((log) => mapDashboardEvent(log, browserLocale.value));
      dashboardState.value = hasDashboardData({
        totalDevices: totalDevices.value,
        hourlyCount: hourly.value.length,
        alertCount: alertCount.value,
        eventCount: events.value.length,
      }) ? 'ready' : 'empty';
    } catch {
      resetDashboardData();
      dashboardState.value = 'error';
    }
  }

  return {
    authStore,
    t,
    todayLabel,
    displayRole,
    dashboardCards,
    trendBars,
    analysisLinks,
    events,
    dashboardNonReadyState,
    productionDisplay,
    statusRows,
    loadDashboard,
  };
}
