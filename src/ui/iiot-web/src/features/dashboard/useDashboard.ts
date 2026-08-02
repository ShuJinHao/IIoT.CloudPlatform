import { computed, reactive, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import {
  Activity,
  BarChart3,
  BellRing,
  Factory,
  Gauge,
  Route,
} from 'lucide-vue-next';
import { getHourlyByDeviceApi } from '../capacity/api';
import {
  getRecentAlertCountApi,
  getRecentDeviceLogsApi,
} from '../device-logs/api';
import {
  getDeviceStatusSummaryApi,
} from '../devices/api';
import type { AppLocale } from '../../i18n';
import { useProductionContext } from '../../shared/production-context';
import { useAuthStore } from '../../stores/auth';
import { resolveDashboardLoadError } from './errors';
import {
  mapDashboardEvent,
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
  const productionContext = useProductionContext();
  const {
    processOptions,
    deviceOptions,
    selectedProcessId,
    selectedDeviceId,
    selectedProcess,
    selectedDevice,
    context,
    status: contextStatus,
    error: contextError,
    state: contextState,
    selectionRevision,
    hasAuthorizedDevices,
    loadContext,
    selectProcess,
    selectDevice,
  } = productionContext;
  const onlineDevices = ref(0);
  const warningDevices = ref(0);
  const errorDevices = ref(0);
  const offlineDevices = ref(0);
  const clientSoftwareStatus = ref<string | null>(null);
  const clientStatusIssue = ref<string | null>(null);
  const todayProduction = ref(0);
  const reportingPlcCount = ref(0);
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
  const hasHourlyData = computed(() => hourly.value.length > 0);
  const productionDisplay = computed(() =>
    sourceStates.capacity.status === 'ready'
      ? formattedProduction.value
      : '--',
  );
  const reportingPlcDisplay = computed(() =>
    sourceStates.capacity.status === 'ready'
      ? reportingPlcCount.value
      : '--',
  );
  const clientStatusDisplay = computed(() => {
    if (sourceStates.deviceStatus.status !== 'ready') return '--';
    switch (clientSoftwareStatus.value) {
      case 'Running': return t('dashboard.statusRunning');
      case 'Starting': return t('dashboard.statusStarting');
      case 'Stopped': return t('dashboard.statusStopped');
      case 'RuntimeHeartbeatStale': return t('dashboard.statusHeartbeatStale');
      case 'MissingRuntimeHeartbeat': return t('dashboard.statusHeartbeatMissing');
      default: return t('dashboard.statusUnknown');
    }
  });
  const alertCountDisplay = computed(() =>
    sourceStates.alertCount.status === 'ready' ? alertCount.value : '--',
  );
  const todayLabel = computed(() =>
    new Date().toLocaleDateString(browserLocale.value, {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
      timeZone: 'Asia/Shanghai',
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
      value: clientStatusDisplay.value,
      helper: sourceHelper(
        'deviceStatus',
        clientStatusIssue.value || t('dashboard.activeClientsHelper', {
          device: selectedDevice.value?.deviceName ?? '--',
          code: selectedDevice.value?.deviceCode ?? '--',
        }),
      ),
      background: 'var(--chart-2)',
      icon: Activity,
      status: sourceStates.deviceStatus.status,
    },
    {
      id: 'reporting-plcs',
      label: t('dashboard.reportingPlcs'),
      value: reportingPlcDisplay.value,
      helper: sourceHelper('capacity', t('dashboard.reportingPlcsHelper')),
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
    { label: t('dashboard.running'), value: onlineDevices.value, color: 'var(--success)' },
    { label: t('dashboard.starting'), value: warningDevices.value, color: 'var(--warn)' },
    { label: t('dashboard.unknown'), value: errorDevices.value, color: 'var(--error)' },
    { label: t('dashboard.stoppedOrStale'), value: offlineDevices.value, color: 'var(--text-2)' },
  ]);
  const statusSummary = computed(() =>
    sourceHelper('deviceStatus', t('dashboard.clientStatusSummary', {
      device: selectedDevice.value?.deviceName ?? '--',
      status: clientStatusDisplay.value,
    })),
  );
  const trendSubtitle = computed(() => t('dashboard.trendSubtitle', {
    device: selectedDevice.value?.deviceName ?? '--',
  }));
  const dashboardNonReadyState = computed<DashboardNonReadyState | null>(() => {
    const statuses = dashboardSourceKeys.map((key) => sourceStates[key].status);
    if (statuses.every((status) => status === 'error')) return 'error';
    if (!statuses.includes('ready')) return 'loading';
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
    onlineDevices.value = 0;
    warningDevices.value = 0;
    errorDevices.value = 0;
    offlineDevices.value = 0;
    clientSoftwareStatus.value = null;
    clientStatusIssue.value = null;
    todayProduction.value = 0;
    reportingPlcCount.value = 0;
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

  async function loadDashboard(deviceId = selectedDeviceId.value) {
    if (!deviceId || selectedDevice.value?.deviceId !== deviceId) return;

    const generation = ++requestGeneration;
    resetDashboardData();
    resetSourceStates();

    await Promise.all([
      loadSource(
        'deviceStatus',
        generation,
        () => getDeviceStatusSummaryApi({ deviceId }),
        (statusSummary) => {
          onlineDevices.value = statusSummary.online;
          warningDevices.value = statusSummary.warning;
          errorDevices.value = statusSummary.error;
          offlineDevices.value = statusSummary.offline;
          clientSoftwareStatus.value = statusSummary.softwareStatus ?? null;
          clientStatusIssue.value = statusSummary.issue ?? null;
        },
      ),
      loadSource('capacity', generation, () =>
        getHourlyByDeviceApi({ deviceId }), (hourlyData) => {
          todayProduction.value = hourlyData.reduce(
            (sum, item) => sum + (item.totalCount ?? 0),
            0,
          );
          reportingPlcCount.value = new Set(
            hourlyData
              .map((item) => item.plcCode?.trim())
              .filter((plcCode): plcCode is string => Boolean(plcCode)),
          ).size;
          const buckets = new Map<string, { order: number; value: number }>();
          hourlyData.forEach((item) => {
            const label = item.timeLabel
              || `${String(item.hour).padStart(2, '0')}:${String(item.minute).padStart(2, '0')}`;
            const current = buckets.get(label);
            buckets.set(label, {
              order: item.hour * 60 + item.minute,
              value: (current?.value ?? 0) + (item.totalCount ?? 0),
            });
          });
          hourly.value = Array.from(buckets.entries())
            .sort((left, right) => left[1].order - right[1].order)
            .map(([label, bucket]) => ({ label, value: bucket.value }));
        }),
      loadSource(
        'alertCount',
        generation,
        () => getRecentAlertCountApi({ deviceId }),
        (alertSummary) => {
          alertCount.value = alertSummary.count ?? 0;
        },
      ),
      loadSource('recentLogs', generation, () =>
        getRecentDeviceLogsApi({
          limit: 20,
          minLevel: 'WARN',
          deviceId,
        }), (recentLogs) => {
          events.value = recentLogs.map((log) =>
            mapDashboardEvent(log, browserLocale.value));
        }),
    ]);
  }

  watch(selectionRevision, () => {
    requestGeneration++;
    resetDashboardData();
    resetSourceStates();
    if (context.value) void loadDashboard(context.value.deviceId);
  }, { flush: 'sync' });

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
    contextStatus,
    contextError,
    contextState,
    context,
    processOptions,
    deviceOptions,
    selectedProcessId,
    selectedDeviceId,
    selectedProcess,
    selectedDevice,
    hasAuthorizedDevices,
    productionDisplay,
    productionHelper,
    statusRows,
    statusSummary,
    trendSubtitle,
    loadContext,
    loadDashboard,
    selectProcess,
    selectDevice,
  };
}
