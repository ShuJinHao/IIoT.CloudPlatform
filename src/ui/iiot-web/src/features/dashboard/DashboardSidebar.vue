<template>
  <aside class="space-y-6">
    <section class="rounded-[22px] bg-[var(--accent)] p-6 text-white shadow-[var(--shadow-sm)]">
      <div class="mb-5 text-[17px] font-[var(--fw-strong)]">{{ t('dashboard.productionCenter') }}</div>
      <div class="mb-1 text-[30px] font-[var(--fw-strong)] tabular-nums">{{ productionDisplay }}</div>
      <div class="mb-7 text-[var(--fs-sm)] font-[var(--fw-semibold)] text-white/80">{{ t('dashboard.todayOutputCount') }}</div>
      <router-link class="inline-flex h-10 items-center rounded-[var(--radius-sm)] bg-[var(--primary)] px-4 text-[var(--fs-sm)] font-[var(--fw-strong)] text-[var(--text-0)]" to="/capacity">
        {{ t('dashboard.viewCapacity') }}
      </router-link>
    </section>

    <section class="rounded-[22px] bg-white p-5 shadow-[var(--shadow-sm)]">
      <h3 class="mb-5 text-[var(--fs-xl)] font-[var(--fw-strong)] text-[var(--text-0)]">{{ t('dashboard.deviceStatus') }}</h3>
      <div class="space-y-3">
        <div v-for="row in statusRows" :key="row.label" class="flex items-center justify-between rounded-[var(--radius-md)] bg-[var(--bg-2)] px-3 py-3">
          <span class="flex items-center gap-3 text-[var(--fs-base)] font-[var(--fw-bold)] text-[var(--text-0)]">
            <i class="size-2.5 rounded-full" :style="{ background: row.color }"></i>
            {{ row.label }}
          </span>
          <span class="font-mono text-[var(--fs-base)] font-[var(--fw-strong)] text-[var(--text-0)]">{{ row.value }}</span>
        </div>
      </div>
    </section>

    <section class="rounded-[22px] bg-white p-5 shadow-[var(--shadow-sm)]">
      <h3 class="mb-5 text-[var(--fs-xl)] font-[var(--fw-strong)] text-[var(--text-0)]">{{ t('dashboard.shiftDuty') }}</h3>
      <div class="space-y-2">
        <div v-for="member in teamMembers" :key="member.name" class="flex items-center justify-between rounded-[var(--radius-md)] bg-[var(--bg-2)] px-3 py-3">
          <span class="flex min-w-0 items-center gap-3">
            <span class="grid size-8 shrink-0 place-items-center rounded-full text-[var(--fs-sm)] font-[var(--fw-strong)]" :style="{ background: member.color }">{{ member.initial }}</span>
            <span class="min-w-0">
              <strong class="block truncate text-[var(--fs-sm)] font-[var(--fw-strong)] text-[var(--text-0)]">{{ member.name }}</strong>
              <small class="block truncate text-[10px] font-[var(--fw-semibold)] text-[var(--muted-foreground)]">{{ member.role }}</small>
            </span>
          </span>
          <ChevronRight :size="15" class="text-[var(--muted-foreground)]" />
        </div>
      </div>
    </section>
  </aside>
</template>

<script setup lang="ts">
import { ChevronRight } from 'lucide-vue-next';
import { useI18n } from 'vue-i18n';
import type { DashboardStatusRow, DashboardTeamMember } from './types';

defineProps<{
  productionDisplay: string;
  statusRows: DashboardStatusRow[];
  teamMembers: DashboardTeamMember[];
}>();

const { t } = useI18n();
</script>
