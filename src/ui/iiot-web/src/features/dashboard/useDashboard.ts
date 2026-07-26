import { computed, reactive, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import {
  Activity,
  BarChart3,
  BellRing,
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
import { resolveDashboardLoadError } from './errors';
import {
  mapDashboardEvent,
  hasDashboardData,
  todayIsoDate,
  type AnalysisLink,
  type DashboardCard,
  type DashboardEvent,
  type DashboardNonReadyState,
  type DashboardSourceKey,
  type DashboardSourceState,
} from './types';

const dashboardSourceKeys: DashboardSourceKey[] = [
  'deviceStatus',
  'capacity',
  'alertCount',
  'recentLogs',
];

function createSourceState(): DashboardSourceState {
  return { status: 'loading', error: '' };
}

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
  const hourly = ref<{ label: string; value: number }[]>([]);
  const events = ref<DashboardEvent[]>([]);
  const sourceStates = reactive<Record<DashboardSourceKey, DashboardSourceState>>({
    deviceStatus: createSourceState(),
    capacity: createSourceState(),
    alertCount: createSourceState(),
    recentLogs: createSourceState(),
  });
  let requestGeneration = 0;

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
  const activeClients = computed(() =>
    onlineDevices.value + warningDevices.value + errorDevices.value,
  );
  const hasHourlyData = computed(() => hourly.value.length > 0);
  const passRate = computed(() =>
    todayProduction.value > 0
      ? (todayOkProduction.value / todayProduction.value) * 100
      : 0,
  );
  const productionDisplay = computed(() =>
    sourceStates.capacity.status === 'ready' && hasHourlyData.value
      ? formattedProduction.value
      : '--',
  );
  const passRateDisplay = computed(() =>
    sourceStates.capacity.status === 'ready' && hasHourlyData.value
      ? `${passRate.value.toFixed(1)}%`
      : '--',
  );
  const activeClientsDisplay = computed(() =>
    sourceStates.deviceStatus.status === 'ready' ? activeClients.value : '--',
  );
  const alertCountDisplay = computed(() =>
    sourceStates.alertCount.status === 'ready' ? alertCount.value : '--',
  );
  const todayLabel = computed(() =>
    new Date().toLocaleDateString(browserLocale.value, {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
    }),
  );

  function sourceHelper(
    key: DashboardSourceKey,
    readyText: string,
    emptyText?: string,
  ): string {
    const state = sourceStates[key];
    if (state.status === 'loading') return t('dashboard.sourceLoading');
    if (state.status === 'error') return state.error;
    if (emptyText && key === 'capacity' && !hasHourlyData.value) return emptyText;
    return readyText;
  }

  const productionHelper = computed(() =>
    sourceHelper(
      'capacity',
      t('dashboard.totalOutputHelper'),
      t('dashboard.totalOutputEmpty'),
    ),
  );
  const dashboardCards = computed<DashboardCard[]>(() => [
    {
      id: 'production',
      label: t('dashboard.totalOutput'),
      value: productionDisplay.value,
      helper: productionHelper.value,
      background: 'var(--chart-1)',
      icon: Factory,
      status: sourceStates.capacity.status,
    },
    {
      id: 'active-clients',
      label: t('dashboard.activeClients'),
      value: activeClientsDisplay.value,
      helper: sourceHelper('deviceStatus', t('dashboard.activeClientsHelper', {
        active: activeClients.value,
        total: totalDevices.value,
      })),
      background: 'var(--chart-2)',
      icon: Activity,
      status: sourceStates.deviceStatus.status,
    },
    {
      id: 'pass-rate',
      label: t('dashboard.passRate'),
      value: passRateDisplay.value,
      helper: sourceHelper(
        'capacity',
        t('dashboard.passRateHelper'),
        t('dashboard.passRateEmpty'),
      ),
      background: 'var(--chart-3)',
      icon: Gauge,
      status: sourceStates.capacity.status,
    },
    {
      id: 'alert-records',
      label: t('dashboard.alertRecords'),
      value: alertCountDisplay.value,
      helper: sourceHelper('alertCount', t('dashboard.alertRecordsHelper')),
      background: 'var(--chart-4)',
      icon: BellRing,
      status: sourceStates.alertCount.status,
    },
  ]);
  const trendBars = computed(() => {
    const source = hourly.value;
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
  const statusSummary = computed(() =>
    sourceHelper('deviceStatus', t('dashboard.clientStatusSummary', {
      active: activeClients.value,
      total: totalDevices.value,
    })),
  );
  const dashboardHasData = computed(() => hasDashboardData({
    totalDevices: totalDevices.value,
    hourlyCount: hourly.value.length,
    alertCount: alertCount.value,
    eventCount: events.value.length,
  }));
  const dashboardNonReadyState = computed<DashboardNonReadyState | null>(() => {
    const statuses = dashboardSourceKeys.map((key) => sourceStates[key].status);
    if (statuses.every((status) => status === 'error')) return 'error';
    if (!statuses.includes('ready')) return 'loading';
    if (statuses.every((status) => status === 'ready') && !dashboardHasData.value) {
      return 'empty';
    }
    return null;
  });
  const dashboardErrorDescription = computed(() => {
    const groupedErrors = new Map<string, string[]>();
    dashboardSourceKeys.forEach((key) => {
      const state = sourceStates[key];
      if (state.status !== 'error' || !state.error) return;
      const labels = groupedErrors.get(state.error) ?? [];
      labels.push(t(`dashboard.source.${key}`));
      groupedErrors.set(state.error, labels);
    });
    return Array.from(groupedErrors.entries())
      .map(([message, labels]) => `${labels.join(' / ')}：${message}`)
      .join('；');
  });

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

  function resetSourceStates() {
    dashboardSourceKeys.forEach((key) => {
      sourceStates[key].status = 'loading';
      sourceStates[key].error = '';
    });
  }

  async function loadSource<T>(
    key: DashboardSourceKey,
    generation: number,
    request: () => Promise<T>,
    apply: (value: T) => void,
  ) {
    try {
      const value = await request();
      if (generation !== requestGeneration) return;
      apply(value);
      sourceStates[key].status = 'ready';
      sourceStates[key].error = '';
    } catch (error) {
      const message = await resolveDashboardLoadError(error);
      if (generation !== requestGeneration) return;
      sourceStates[key].status = 'error';
      sourceStates[key].error = message;
    }
  }

  async function loadDashboard() {
    const generation = ++requestGeneration;
    resetDashboardData();
    resetSourceStates();

    await Promise.all([
      loadSource('deviceStatus', generation, getDeviceStatusSummaryApi, (statusSummary) => {
        totalDevices.value = statusSummary.total;
        onlineDevices.value = statusSummary.online;
        warningDevices.value = statusSummary.warning;
        errorDevices.value = statusSummary.error;
        offlineDevices.value = statusSummary.offline;
      }),
      loadSource('capacity', generation, () =>
        getHourlyAggregateApi({ date: todayIsoDate() }), (hourlyData) => {
          todayProduction.value = hourlyData.reduce(
            (sum, item) => sum + (item.totalCount ?? 0),
            0,
          );
          todayOkProduction.value = hourlyData.reduce(
            (sum, item) => sum + (item.okCount ?? 0),
            0,
          );
          hourly.value = hourlyData.map((item) => ({
            label: item.timeLabel
              || `${String(item.hour).padStart(2, '0')}:${String(item.minute).padStart(2, '0')}`,
            value: item.totalCount,
          }));
        }),
      loadSource('alertCount', generation, getRecentAlertCountApi, (alertSummary) => {
        alertCount.value = alertSummary.count ?? 0;
      }),
      loadSource('recentLogs', generation, () =>
        getRecentDeviceLogsApi({ limit: 20, minLevel: 'WARN' }), (recentLogs) => {
          events.value = recentLogs.map((log) =>
            mapDashboardEvent(log, browserLocale.value));
        }),
    ]);
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
    dashboardErrorDescription,
    sourceStates,
    productionDisplay,
    productionHelper,
    statusRows,
    statusSummary,
    loadDashboard,
  };
}
