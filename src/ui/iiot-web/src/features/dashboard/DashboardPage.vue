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

    <section class="grid grid-cols-[minmax(0,1fr)_260px] gap-7 max-[1180px]:grid-cols-1">
      <div class="min-w-0 space-y-7">
        <DashboardMetricCards :cards="dashboardCards" />
        <div class="grid grid-cols-[minmax(0,1.45fr)_minmax(280px,0.85fr)] gap-7 max-[1180px]:grid-cols-1">
          <DashboardTrendPanel :trend-bars="trendBars" />
          <DashboardAnalysisPanel :links="analysisLinks" />
        </div>
        <DashboardRecentAlerts :events="events" :loading="loadingDashboard" />
      </div>

      <DashboardSidebar
        :production-display="productionDisplay"
        :status-rows="statusRows"
        :team-members="teamMembers"
      />
    </section>

    <div v-if="dashboardError" class="rounded-[var(--radius-lg)] border border-[rgba(239,68,68,0.20)] bg-[rgba(239,68,68,0.10)] px-5 py-4 text-[var(--fs-base)] font-[var(--fw-semibold)] text-[var(--error)]">
      {{ dashboardError }}
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted } from 'vue';
import DashboardAnalysisPanel from './DashboardAnalysisPanel.vue';
import DashboardMetricCards from './DashboardMetricCards.vue';
import DashboardRecentAlerts from './DashboardRecentAlerts.vue';
import DashboardSidebar from './DashboardSidebar.vue';
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
  loadingDashboard,
  productionDisplay,
  statusRows,
  teamMembers,
  dashboardError,
  loadDashboard,
} = useDashboard();

onMounted(loadDashboard);
</script>
