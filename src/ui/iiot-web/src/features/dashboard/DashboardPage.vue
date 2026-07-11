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

    <DashboardStatePanel
      v-if="dashboardNonReadyState"
      :state="dashboardNonReadyState"
      @retry="loadDashboard"
    />

    <section v-else data-testid="dashboard-ready" class="grid grid-cols-[minmax(0,1fr)_260px] gap-7 max-[1180px]:grid-cols-1">
      <div class="min-w-0 space-y-7">
        <DashboardMetricCards :cards="dashboardCards" />
        <div class="grid grid-cols-[minmax(0,1.45fr)_minmax(280px,0.85fr)] gap-7 max-[1180px]:grid-cols-1">
          <DashboardTrendPanel :trend-bars="trendBars" />
          <DashboardAnalysisPanel :links="analysisLinks" />
        </div>
        <DashboardRecentAlerts :events="events" />
      </div>

      <DashboardSidebar
        :production-display="productionDisplay"
        :status-rows="statusRows"
      />
    </section>
  </div>
</template>

<script setup lang="ts">
import { onMounted } from 'vue';
import DashboardAnalysisPanel from './DashboardAnalysisPanel.vue';
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
  productionDisplay,
  statusRows,
  loadDashboard,
} = useDashboard();

onMounted(loadDashboard);
</script>
