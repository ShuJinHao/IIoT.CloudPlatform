<template>
  <div class="space-y-7">
    <section class="flex items-start justify-between gap-6">
      <div>
        <p class="mb-2 text-[12px] font-bold uppercase text-[var(--muted-foreground)]">{{ todayLabel }}</p>
        <h2 class="text-[32px] font-extrabold leading-tight tracking-[0] text-[#111827] dark:text-[#f5f5f4]">{{ t('dashboard.title') }}</h2>
        <p class="mt-2 text-[14px] font-semibold text-[var(--muted-foreground)]">
          {{ authStore.employeeNo || t('layout.userFallback') }} · {{ displayRole }}
        </p>
      </div>
    </section>

    <section class="grid grid-cols-[minmax(0,1fr)_260px] gap-7 max-[1180px]:grid-cols-1">
      <div class="min-w-0 space-y-7">
        <div class="grid grid-cols-3 gap-6 max-[1060px]:grid-cols-2 max-[820px]:grid-cols-1">
          <article
            v-for="card in dashboardCards"
            :key="card.label"
            class="min-h-[132px] rounded-[18px] p-5 text-[#111827]"
            :style="{ background: card.background }"
          >
            <component :is="card.icon" class="mb-8" :size="17" :stroke-width="2.4" />
            <div class="text-[27px] font-extrabold leading-none tracking-[0] tabular-nums">{{ card.value }}</div>
            <div class="mt-2 text-[12px] font-bold text-[#4d5868]">{{ card.label }}</div>
            <div class="mt-1 text-[11px] font-semibold text-[#697386]">{{ card.helper }}</div>
          </article>
        </div>

        <div class="grid grid-cols-[minmax(0,1.45fr)_minmax(280px,0.85fr)] gap-7 max-[1180px]:grid-cols-1">
          <section class="rounded-[24px] bg-white p-6 shadow-[var(--shadow-sm)] dark:bg-[#18181b]">
            <div class="mb-8 flex items-start justify-between gap-4">
              <div>
                <h3 class="text-[20px] font-extrabold text-[#111827] dark:text-[#f5f5f4]">{{ t('dashboard.productionTrend') }}</h3>
                <p class="mt-1 text-[13px] font-semibold text-[var(--muted-foreground)]">{{ t('dashboard.trendSubtitle') }}</p>
              </div>
              <span class="rounded-[12px] bg-[#111827] px-3 py-2 text-[12px] font-extrabold text-white">{{ t('common.live') }}</span>
            </div>

            <div class="dashboard-bars" :aria-label="t('dashboard.productionTrend')">
              <div
                v-for="(bar, index) in trendBars"
                :key="`${bar.label}-${index}`"
                class="dashboard-bars__group"
              >
                <span class="dashboard-bars__bar" :style="{ height: bar.height, background: bar.color }"></span>
                <small>{{ bar.label }}</small>
              </div>
            </div>
          </section>

          <section class="rounded-[24px] bg-white p-6 shadow-[var(--shadow-sm)] dark:bg-[#18181b]">
            <div class="mb-7 flex items-center justify-between">
              <div>
                <h3 class="text-[20px] font-extrabold text-[#111827] dark:text-[#f5f5f4]">{{ t('dashboard.analysis') }}</h3>
                <p class="mt-1 text-[13px] font-semibold text-[var(--muted-foreground)]">{{ t('dashboard.analysisSub') }}</p>
              </div>
            </div>

            <div class="space-y-3">
              <router-link
                v-for="item in analysisLinks"
                :key="item.label"
                :to="item.to"
                class="flex h-14 items-center justify-between rounded-[16px] bg-[#f6f9f8] px-4 text-[13px] font-extrabold text-[#111827] transition-colors hover:bg-[#edf3f2] dark:bg-[#202024] dark:text-[#f5f5f4]"
              >
                <span class="flex items-center gap-3">
                  <component :is="item.icon" :size="16" />
                  {{ item.label }}
                </span>
                <ChevronRight :size="16" />
              </router-link>
            </div>

            <div class="mt-7 flex items-center gap-2 text-[12px] font-semibold text-[var(--muted-foreground)]">
              {{ t('dashboard.analysisCreatedBy') }}
              <span class="grid size-7 place-items-center rounded-full bg-[var(--accent-chip)] text-[#111827]">
                <Sparkles :size="14" />
              </span>
            </div>
          </section>
        </div>

        <section class="rounded-[24px] bg-white p-6 shadow-[var(--shadow-sm)] dark:bg-[#18181b]">
          <div class="mb-5 flex items-center justify-between">
            <h3 class="text-[20px] font-extrabold text-[#111827] dark:text-[#f5f5f4]">{{ t('dashboard.recentAlerts') }}</h3>
            <span class="rounded-[12px] bg-[#f0f4f3] px-3 py-2 text-[12px] font-extrabold text-[#596273]">{{ t('common.latest') }}</span>
          </div>

          <div class="overflow-hidden rounded-[18px] border border-[var(--border)]">
            <div class="grid grid-cols-[120px_minmax(0,1fr)_140px_92px] bg-[#f4f7f8] px-4 py-3 text-[12px] font-bold text-[var(--muted-foreground)] dark:bg-[#202024]">
              <span>{{ t('dashboard.time') }}</span>
              <span>{{ t('dashboard.message') }}</span>
              <span>{{ t('dashboard.device') }}</span>
              <span>{{ t('dashboard.status') }}</span>
            </div>
            <div v-if="events.length" class="divide-y divide-[var(--border)]">
              <div
                v-for="event in events.slice(0, 5)"
                :key="`${event.time}-${event.message}`"
                class="grid grid-cols-[120px_minmax(0,1fr)_140px_92px] items-center px-4 py-4 text-[13px] font-semibold text-[#111827] dark:text-[#f5f5f4]"
              >
                <span class="font-mono text-[12px] text-[var(--muted-foreground)]">{{ event.time }}</span>
                <span class="truncate">{{ event.message }}</span>
                <span class="truncate text-[var(--muted-foreground)]">{{ event.deviceCode }}</span>
                <span class="w-fit rounded-full px-3 py-1 text-[11px] font-extrabold" :class="event.severity === 'error' ? 'bg-[rgba(239,68,68,0.12)] text-[#ef4444]' : 'bg-[rgba(224,138,0,0.14)] text-[#b46600]'">
                  {{ event.label }}
                </span>
              </div>
            </div>
            <div v-else class="px-4 py-10 text-center text-[13px] font-semibold text-[var(--muted-foreground)]">
              {{ loadingDashboard ? t('common.loading') : t('dashboard.noAlerts') }}
            </div>
          </div>
        </section>
      </div>

      <aside class="space-y-6">
        <section class="rounded-[22px] bg-[var(--accent)] p-6 text-white shadow-[var(--shadow-sm)]">
          <div class="mb-5 text-[17px] font-extrabold">{{ t('dashboard.productionCenter') }}</div>
          <div class="mb-1 text-[30px] font-extrabold tabular-nums">{{ productionDisplay }}</div>
          <div class="mb-7 text-[12px] font-semibold text-white/80">{{ t('dashboard.todayOutputCount') }}</div>
          <router-link class="inline-flex h-10 items-center rounded-[12px] bg-[var(--primary)] px-4 text-[12px] font-extrabold text-[#111827]" to="/capacity">
            {{ t('dashboard.viewCapacity') }}
          </router-link>
        </section>

        <section class="rounded-[22px] bg-white p-5 shadow-[var(--shadow-sm)] dark:bg-[#18181b]">
          <h3 class="mb-5 text-[18px] font-extrabold text-[#111827] dark:text-[#f5f5f4]">{{ t('dashboard.deviceStatus') }}</h3>
          <div class="space-y-3">
            <div v-for="row in statusRows" :key="row.label" class="flex items-center justify-between rounded-[14px] bg-[#f6f9fa] px-3 py-3 dark:bg-[#202024]">
              <span class="flex items-center gap-3 text-[13px] font-bold text-[#111827] dark:text-[#f5f5f4]">
                <i class="size-2.5 rounded-full" :style="{ background: row.color }"></i>
                {{ row.label }}
              </span>
              <span class="font-mono text-[13px] font-extrabold text-[#111827] dark:text-[#f5f5f4]">{{ row.value }}</span>
            </div>
          </div>
        </section>

        <section class="rounded-[22px] bg-white p-5 shadow-[var(--shadow-sm)] dark:bg-[#18181b]">
          <h3 class="mb-5 text-[18px] font-extrabold text-[#111827] dark:text-[#f5f5f4]">{{ t('dashboard.shiftDuty') }}</h3>
          <div class="space-y-2">
            <div v-for="member in teamMembers" :key="member.name" class="flex items-center justify-between rounded-[14px] bg-[#f6f9fa] px-3 py-3 dark:bg-[#202024]">
              <span class="flex min-w-0 items-center gap-3">
                <span class="grid size-8 shrink-0 place-items-center rounded-full text-[12px] font-extrabold" :style="{ background: member.color }">{{ member.initial }}</span>
                <span class="min-w-0">
                  <strong class="block truncate text-[12px] font-extrabold text-[#111827] dark:text-[#f5f5f4]">{{ member.name }}</strong>
                  <small class="block truncate text-[10px] font-semibold text-[var(--muted-foreground)]">{{ member.role }}</small>
                </span>
              </span>
              <ChevronRight :size="15" class="text-[var(--muted-foreground)]" />
            </div>
          </div>
        </section>
      </aside>
    </section>

    <div v-if="dashboardError" class="rounded-[18px] border border-[rgba(239,68,68,0.20)] bg-[rgba(239,68,68,0.10)] px-5 py-4 text-[13px] font-semibold text-[#ef4444]">
      {{ dashboardError }}
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, type Component } from 'vue';
import { useI18n } from 'vue-i18n';
import { useAuthStore } from '../stores/auth';
import {
  Activity,
  BarChart3,
  ChevronRight,
  Factory,
  Gauge,
  Route,
  ScrollText,
  Sparkles,
} from 'lucide-vue-next';
import { getDeviceStatusSummaryApi } from '../api/device';
import { getHourlyAggregateApi } from '../api/capacity';
import {
  getRecentAlertCountApi,
  getRecentDeviceLogsApi,
  type DeviceLogListItemDto,
} from '../api/deviceLog';
import type { AppLocale } from '../i18n';

interface DashboardCard {
  label: string;
  value: string | number;
  helper: string;
  background: string;
  icon: Component;
}

interface AnalysisLink {
  label: string;
  to: string;
  icon: Component;
}

export interface DashboardEvent {
  time: string;
  message: string;
  deviceCode: string;
  severity: 'info' | 'warn' | 'error';
  label: string;
}

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
const browserLocale = computed(() => (currentLocale.value === 'zh-CN' ? 'zh-CN' : 'en-US'));
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
const onlineDevicesDisplay = computed(() => (dataReady.value ? onlineDevices.value : '--'));
const totalDevicesDisplay = computed(() => (dataReady.value ? `/ ${totalDevices.value}` : ''));
const productionDisplay = computed(() => (dataReady.value ? formattedProduction.value : '--'));
const alertCountDisplay = computed(() => (dataReady.value ? alertCount.value : '--'));
const passRateDisplay = computed(() => (dataReady.value ? passRate.value.toFixed(1) : '--'));

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
    value: onlineDevicesDisplay.value,
    helper: t('dashboard.onlineDevicesHelper', { count: totalDevicesDisplay.value }),
    background: 'var(--chart-2)',
    icon: Activity,
  },
  {
    label: t('dashboard.passRate'),
    value: passRateDisplay.value === '--' ? '--' : `${passRateDisplay.value}%`,
    helper: t('dashboard.passRateHelper', { count: alertCountDisplay.value }),
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
  { label: t('dashboard.online'), value: onlineDevices.value, color: '#10a37f' },
  { label: t('dashboard.warning'), value: warningDevices.value, color: '#f4b63f' },
  { label: t('dashboard.error'), value: errorDevices.value, color: '#ef4444' },
  { label: t('dashboard.offline'), value: offlineDevices.value, color: '#9aa3af' },
]);

const teamMembers = computed(() => [
  { name: currentLocale.value === 'zh-CN' ? '甲班组' : 'Shift A', role: t('dashboard.shiftLead'), initial: 'A', color: '#f7d7c8' },
  { name: currentLocale.value === 'zh-CN' ? '质检岗' : 'Quality', role: t('dashboard.quality'), initial: 'Q', color: '#d9e8ff' },
  { name: currentLocale.value === 'zh-CN' ? '维修岗' : 'Maintenance', role: t('dashboard.maintenance'), initial: 'M', color: '#e1d6ff' },
]);

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
  return date.toLocaleTimeString(browserLocale.value, {
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
    alertCount.value = alertSummary.count ?? 0;

    todayProduction.value = hourlyData.reduce((sum, item) => sum + (item.totalCount ?? 0), 0);
    todayOkProduction.value = hourlyData.reduce((sum, item) => sum + (item.okCount ?? 0), 0);
    hourly.value = hourlyData.map((item) => ({
      label: item.timeLabel || `${String(item.hour).padStart(2, '0')}:${String(item.minute).padStart(2, '0')}`,
      value: item.totalCount,
    }));
    events.value = recentLogs.map(mapEvent);
    dataReady.value = true;
  } catch {
    resetDashboardData();
    dataReady.value = false;
    dashboardError.value = t('dashboard.loadFailed');
  } finally {
    loadingDashboard.value = false;
  }
}

onMounted(loadDashboard);
</script>

<style scoped>
.dashboard-bars {
  display: grid;
  grid-template-columns: repeat(10, minmax(24px, 1fr));
  align-items: end;
  gap: 11px;
  height: 270px;
  padding: 18px 4px 0;
  background:
    linear-gradient(to bottom, rgba(17, 24, 39, 0.07) 1px, transparent 1px) 0 20% / 100% 25% repeat-y;
}

.dashboard-bars__group {
  display: grid;
  min-width: 0;
  height: 100%;
  grid-template-rows: minmax(0, 1fr) 24px;
  align-items: end;
  gap: 9px;
}

.dashboard-bars__bar {
  display: block;
  min-height: 32px;
  border-radius: 999px 999px 8px 8px;
}

.dashboard-bars__group small {
  overflow: hidden;
  color: var(--muted-foreground);
  font-size: 11px;
  font-weight: 700;
  text-align: center;
  text-overflow: ellipsis;
  white-space: nowrap;
}

@media (max-width: 820px) {
  .dashboard-bars {
    grid-template-columns: repeat(7, minmax(24px, 1fr));
    height: 220px;
  }
}
</style>
