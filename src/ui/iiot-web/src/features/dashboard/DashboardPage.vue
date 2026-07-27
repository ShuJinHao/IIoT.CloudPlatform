<template>
  <div class="space-y-7">
    <section class="flex items-start justify-between gap-6">
      <div>
        <p class="mb-2 text-[var(--fs-sm)] font-[var(--fw-bold)] uppercase text-[var(--muted-foreground)]">{{ todayLabel }}</p>
        <h2 class="text-[var(--fs-4xl)] font-[var(--fw-strong)] leading-tight tracking-[0] text-[var(--text-0)]">{{ t('dashboard.title') }}</h2>
        <p class="mt-2 text-[var(--fs-md)] font-[var(--fw-semibold)] text-[var(--muted-foreground)]">
          {{ authStore.employeeNo || t('layout.userFallback') }} · {{ displayRole }}
        </p>
      </div>
    </section>

    <DashboardContextToolbar
      :process-id="selectedProcessId"
      :device-id="selectedDeviceId"
      :process-options="processOptions"
      :device-options="deviceOptions"
      :selected-process="selectedProcess"
      :selected-device="selectedDevice"
      :loading="contextStatus === 'loading'"
      :has-authorized-devices="hasAuthorizedDevices"
      @update:process-id="selectProcess"
      @update:device-id="selectDevice"
    />

    <section
      v-if="contextStatus === 'loading'"
      data-testid="dashboard-context-loading"
      class="min-h-[280px] rounded-[var(--radius-xl)] bg-white p-8 shadow-[var(--shadow-sm)]"
    >
      <LoadingState variant="card" :rows="5" />
    </section>

    <section
      v-else-if="contextStatus === 'error'"
      data-testid="dashboard-context-error"
      class="min-h-[280px] rounded-[var(--radius-xl)] bg-white p-8 shadow-[var(--shadow-sm)]"
    >
      <EmptyState
        :title="t('dashboard.contextErrorTitle')"
        :description="contextError"
      >
        <template #icon>
          <AlertTriangle :size="52" :stroke-width="1.6" />
        </template>
        <template #action>
          <UiButton type="primary" @click="loadContext">{{ t('dashboard.retry') }}</UiButton>
        </template>
      </EmptyState>
    </section>

    <section
      v-else-if="!hasAuthorizedDevices"
      data-testid="dashboard-no-devices"
      class="min-h-[280px] rounded-[var(--radius-xl)] bg-white p-8 shadow-[var(--shadow-sm)]"
    >
      <EmptyState
        :title="t('dashboard.noDevicesTitle')"
        :description="t('dashboard.noDevicesDesc')"
      >
        <template #icon>
          <Factory :size="52" :stroke-width="1.6" />
        </template>
      </EmptyState>
    </section>

    <section
      v-else-if="!selectedDevice"
      data-testid="dashboard-selection-required"
      class="min-h-[280px] rounded-[var(--radius-xl)] bg-white p-8 shadow-[var(--shadow-sm)]"
    >
      <EmptyState
        :title="t('dashboard.selectionTitle')"
        :description="t('dashboard.selectionDesc')"
      >
        <template #icon>
          <ListFilter :size="52" :stroke-width="1.6" />
        </template>
      </EmptyState>
    </section>

    <DashboardStatePanel
      v-else-if="dashboardNonReadyState"
      :state="dashboardNonReadyState"
      :error-description="dashboardErrorDescription"
      @retry="loadDashboard"
    />

    <section v-else data-testid="dashboard-ready" class="grid grid-cols-[minmax(0,1fr)_260px] gap-7 max-[1180px]:grid-cols-1">
      <div class="min-w-0 space-y-7">
        <DashboardMetricCards :cards="dashboardCards" />
        <div class="grid grid-cols-[minmax(0,1.45fr)_minmax(280px,0.85fr)] gap-7 max-[1180px]:grid-cols-1">
          <DashboardTrendPanel
            :trend-bars="trendBars"
            :source-state="sourceStates.capacity"
            :subtitle="trendSubtitle"
            @retry="loadDashboard"
          />
          <DashboardAnalysisPanel :links="analysisLinks" />
        </div>
        <DashboardRecentAlerts
          :events="events"
          :source-state="sourceStates.recentLogs"
          @retry="loadDashboard"
        />
      </div>

      <DashboardSidebar
        :production-display="productionDisplay"
        :production-helper="productionHelper"
        :capacity-state="sourceStates.capacity"
        :status-rows="statusRows"
        :status-state="sourceStates.deviceStatus"
        :status-summary="statusSummary"
        @retry="loadDashboard"
      />
    </section>
  </div>
</template>

<script setup lang="ts">
import { onMounted } from 'vue';
import { AlertTriangle, Factory, ListFilter } from 'lucide-vue-next';
import EmptyState from '../../components/states/EmptyState.vue';
import LoadingState from '../../components/states/LoadingState.vue';
import UiButton from '../../components/ui/UiButton.vue';
import DashboardAnalysisPanel from './DashboardAnalysisPanel.vue';
import DashboardContextToolbar from './DashboardContextToolbar.vue';
import DashboardMetricCards from './DashboardMetricCards.vue';
import DashboardRecentAlerts from './DashboardRecentAlerts.vue';
import DashboardSidebar from './DashboardSidebar.vue';
import DashboardStatePanel from './DashboardStatePanel.vue';
import DashboardTrendPanel from './DashboardTrendPanel.vue';
import { useDashboard } from './useDashboard';
import './dashboard-page.css';

const {
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
} = useDashboard();

onMounted(loadContext);
</script>
