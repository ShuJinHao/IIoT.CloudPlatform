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
  todayIsoDate,
  type AnalysisLink,
  type DashboardCard,
  type DashboardEvent,
  type DashboardTeamMember,
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
  const loadingDashboard = ref(true);
  const dashboardError = ref('');
  const dataReady = ref(false);
  const hourly = ref<{ label: string; value: number }[]>([]);
  const events = ref<DashboardEvent[]>([]);

  const currentLocale = computed(() => locale.value as AppLocale);
  const browserLocale = computed(() =>
    currentLocale.value === 'zh-CN' ? 'zh-CN' : 'en-US',
  );
  const displayRole = computed(() => {
    if (!authStore.role) return t('dashboard.subtitleFallback');
    if (currentLocale.value === 'zh-CN' && authStore.role === 'Admin') return '管理员';
    return authStore.role;
  });
  const formattedProduction = computed(() =>
    todayProduction.value.toLocaleString(browserLocale.value),
  );
  const passRate = computed(() =>
    todayProduction.value > 0
      ? (todayOkProduction.value / todayProduction.value) * 100
      : 0,
  );
  const productionDisplay = computed(() =>
    dataReady.value ? formattedProduction.value : '--',
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
      label: t('dashboard.totalOutput'),
      value: productionDisplay.value,
      helper: t('dashboard.totalOutputHelper'),
      background: 'var(--chart-1)',
      icon: Factory,
    },
    {
      label: t('dashboard.onlineDevices'),
      value: dataReady.value ? onlineDevices.value : '--',
      helper: t('dashboard.onlineDevicesHelper', {
        count: dataReady.value ? `/ ${totalDevices.value}` : '',
      }),
      background: 'var(--chart-2)',
      icon: Activity,
    },
    {
      label: t('dashboard.passRate'),
      value: dataReady.value ? `${passRate.value.toFixed(1)}%` : '--',
      helper: t('dashboard.passRateHelper', {
        count: dataReady.value ? alertCount.value : '--',
      }),
      background: 'var(--chart-3)',
      icon: Gauge,
    },
  ]);
  const defaultTrend = computed(() =>
    currentLocale.value === 'zh-CN'
      ? [
          { label: '周日', value: 32 },
          { label: '周一', value: 46 },
          { label: '周二', value: 58 },
          { label: '周三', value: 39 },
          { label: '周四', value: 52 },
          { label: '周五', value: 42 },
          { label: '周六', value: 56 },
        ]
      : [
          { label: 'Sun', value: 32 },
          { label: 'Mon', value: 46 },
          { label: 'Tue', value: 58 },
          { label: 'Wed', value: 39 },
          { label: 'Thu', value: 52 },
          { label: 'Fri', value: 42 },
          { label: 'Sat', value: 56 },
        ],
  );
  const trendBars = computed(() => {
    const source = hourly.value.length ? hourly.value.slice(-10) : defaultTrend.value;
    const max = Math.max(...source.map((item) => item.value), 1);
    return source.map((item, index) => ({
      label: item.label,
      height: `${Math.max(24, Math.round((item.value / max) * 100))}%`,
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
  const teamMembers = computed<DashboardTeamMember[]>(() => [
    { name: currentLocale.value === 'zh-CN' ? '甲班组' : 'Shift A', role: t('dashboard.shiftLead'), initial: 'A', color: 'var(--chart-1)' },
    { name: currentLocale.value === 'zh-CN' ? '质检岗' : 'Quality', role: t('dashboard.quality'), initial: 'Q', color: 'var(--chart-2)' },
    { name: currentLocale.value === 'zh-CN' ? '维修岗' : 'Maintenance', role: t('dashboard.maintenance'), initial: 'M', color: 'var(--chart-3)' },
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
    loadingDashboard.value = true;
    dashboardError.value = '';
    dataReady.value = false;

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
      dataReady.value = true;
    } catch {
      resetDashboardData();
      dataReady.value = false;
      dashboardError.value = t('dashboard.loadFailed');
    } finally {
      loadingDashboard.value = false;
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
    loadingDashboard,
    productionDisplay,
    statusRows,
    teamMembers,
    dashboardError,
    loadDashboard,
  };
}
