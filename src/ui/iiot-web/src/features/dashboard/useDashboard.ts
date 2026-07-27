import { computed, reactive, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute, useRouter, type LocationQueryRaw } from 'vue-router';
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
  getScopedDeviceSelectApi,
  type ScopedDeviceSelectDto,
} from '../devices/api';
import type { UiSelectOption } from '../../components/ui/types';
import type { AppLocale } from '../../i18n';
import { useAuthStore } from '../../stores/auth';
import { resolveDashboardLoadError } from './errors';
import {
  mapDashboardEvent,
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

interface DashboardProcessContext {
  id: string;
  code: string;
  name: string;
}

type DashboardContextStatus = 'loading' | 'ready' | 'error';

function queryValue(value: unknown): string | null {
  if (typeof value === 'string' && value.trim()) return value;
  if (Array.isArray(value) && typeof value[0] === 'string' && value[0].trim()) {
    return value[0];
  }
  return null;
}

export function useDashboard() {
  const authStore = useAuthStore();
  const { t, locale } = useI18n();
  const route = useRoute();
  const router = useRouter();
  const authorizedDevices = ref<ScopedDeviceSelectDto[]>([]);
  const selectedProcessId = ref<string | null>(null);
  const selectedDeviceId = ref<string | null>(null);
  const contextStatus = ref<DashboardContextStatus>('loading');
  const contextError = ref('');
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
  let contextRequestGeneration = 0;
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
  const processes = computed<DashboardProcessContext[]>(() => {
    const uniqueProcesses = new Map<string, DashboardProcessContext>();
    authorizedDevices.value.forEach((device) => {
      if (uniqueProcesses.has(device.processId)) return;
      uniqueProcesses.set(device.processId, {
        id: device.processId,
        code: device.processCode,
        name: device.processName,
      });
    });
    return Array.from(uniqueProcesses.values()).sort((left, right) =>
      left.code.localeCompare(right.code, currentLocale.value));
  });
  const processOptions = computed<UiSelectOption[]>(() =>
    processes.value.map((process) => ({
      label: `${process.name} · ${process.code}`,
      value: process.id,
    })));
  const selectedProcess = computed(() =>
    processes.value.find((process) => process.id === selectedProcessId.value) ?? null);
  const devicesForSelectedProcess = computed(() => {
    if (!selectedProcessId.value) return [];
    return authorizedDevices.value
      .filter((device) => device.processId === selectedProcessId.value)
      .sort((left, right) =>
        left.code.localeCompare(right.code, currentLocale.value)
        || left.deviceName.localeCompare(right.deviceName, currentLocale.value));
  });
  const deviceOptions = computed<UiSelectOption[]>(() =>
    devicesForSelectedProcess.value.map((device) => ({
      label: `${device.deviceName} · ${device.code}`,
      value: device.id,
    })));
  const selectedDevice = computed(() =>
    authorizedDevices.value.find((device) =>
      device.id === selectedDeviceId.value
      && device.processId === selectedProcessId.value) ?? null);
  const hasAuthorizedDevices = computed(() => authorizedDevices.value.length > 0);
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
    sourceStates.capacity.status === 'ready'
      ? formattedProduction.value
      : '--',
  );
  const passRateDisplay = computed(() =>
    sourceStates.capacity.status === 'ready'
      ? `${passRate.value.toFixed(1)}%`
      : '--',
  );
  const clientStatusDisplay = computed(() => {
    if (sourceStates.deviceStatus.status !== 'ready') return '--';
    if (errorDevices.value > 0) return t('dashboard.statusError');
    if (warningDevices.value > 0) return t('dashboard.statusWarning');
    if (onlineDevices.value > 0) return t('dashboard.statusNormal');
    return t('dashboard.statusOffline');
  });
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
      value: clientStatusDisplay.value,
      helper: sourceHelper('deviceStatus', t('dashboard.activeClientsHelper', {
        device: selectedDevice.value?.deviceName ?? '--',
        code: selectedDevice.value?.code ?? '--',
      })),
      background: 'var(--chart-2)',
      icon: Activity,
      status: sourceStates.deviceStatus.status,
    },
    {
      id: 'pass-rate',
      label: t('dashboard.passRate'),
      value: passRateDisplay.value,
      helper: sourceHelper('capacity', t('dashboard.passRateHelper')),
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

  function clearDashboardSelection(
    processId: string | null,
    deviceId: string | null,
  ) {
    requestGeneration++;
    selectedProcessId.value = processId;
    selectedDeviceId.value = deviceId;
    resetDashboardData();
    resetSourceStates();
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
    if (!deviceId || selectedDevice.value?.id !== deviceId) return;

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
        },
      ),
      loadSource('capacity', generation, () =>
        getHourlyByDeviceApi({ deviceId, date: todayIsoDate() }), (hourlyData) => {
          todayProduction.value = hourlyData.reduce(
            (sum, item) => sum + (item.totalCount ?? 0),
            0,
          );
          todayOkProduction.value = hourlyData.reduce(
            (sum, item) => sum + (item.okCount ?? 0),
            0,
          );
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

  function replaceContextQuery(processId: string | null, deviceId: string | null) {
    const query: LocationQueryRaw = { ...route.query };
    if (processId) query.processId = processId;
    else delete query.processId;
    if (deviceId) query.deviceId = deviceId;
    else delete query.deviceId;
    return router.replace({ query });
  }

  function applyRouteContext() {
    if (contextStatus.value !== 'ready') return;

    const processId = queryValue(route.query.processId);
    const deviceId = queryValue(route.query.deviceId);
    const process = processId
      ? processes.value.find((item) => item.id === processId)
      : null;
    const device = deviceId
      ? authorizedDevices.value.find((item) => item.id === deviceId)
      : null;
    const validProcessOnly = Boolean(process && !deviceId);
    const validPair = Boolean(
      process
      && device
      && device.processId === process.id,
    );

    if (validProcessOnly) {
      if (selectedProcessId.value !== processId || selectedDeviceId.value !== null) {
        clearDashboardSelection(processId, null);
      }
      return;
    }

    if (validPair) {
      if (selectedProcessId.value === processId && selectedDeviceId.value === deviceId) {
        return;
      }
      clearDashboardSelection(processId, deviceId);
      void loadDashboard(deviceId);
      return;
    }

    if (selectedProcessId.value !== null || selectedDeviceId.value !== null) {
      clearDashboardSelection(null, null);
    }
    if (processId || deviceId) {
      void replaceContextQuery(null, null);
    }
  }

  async function selectProcess(processId: string | null) {
    const normalizedProcessId = processId
      && processes.value.some((process) => process.id === processId)
      ? processId
      : null;
    await replaceContextQuery(normalizedProcessId, null);
  }

  async function selectDevice(deviceId: string | null) {
    const normalizedDeviceId = deviceId
      && devicesForSelectedProcess.value.some((device) => device.id === deviceId)
      ? deviceId
      : null;
    await replaceContextQuery(selectedProcessId.value, normalizedDeviceId);
  }

  async function loadContext() {
    const generation = ++contextRequestGeneration;
    contextStatus.value = 'loading';
    contextError.value = '';
    clearDashboardSelection(null, null);

    try {
      const devices = await getScopedDeviceSelectApi();
      if (generation !== contextRequestGeneration) return;
      authorizedDevices.value = devices;
      contextStatus.value = 'ready';
      applyRouteContext();
    } catch (error) {
      const message = await resolveDashboardLoadError(error);
      if (generation !== contextRequestGeneration) return;
      authorizedDevices.value = [];
      contextStatus.value = 'error';
      contextError.value = message;
    }
  }

  watch(
    () => [route.query.processId, route.query.deviceId],
    applyRouteContext,
  );

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
