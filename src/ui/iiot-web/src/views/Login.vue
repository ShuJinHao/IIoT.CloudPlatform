<template>
  <main class="grid min-h-screen place-items-center overflow-auto bg-[var(--bg-2)] p-8 text-[var(--text-0)]">
    <div class="grid h-[min(820px,calc(100vh-64px))] min-h-[640px] w-[min(1600px,calc(100vw-128px))] grid-cols-[250px_minmax(0,1fr)] overflow-hidden rounded-[32px] bg-white shadow-[0_34px_90px_rgba(30,38,48,0.18)] max-[1120px]:w-[min(960px,calc(100vw-64px))] max-[1120px]:grid-cols-1 max-[1120px]:min-h-[620px]">
      <aside class="flex min-h-0 flex-col bg-[var(--bg-2)] px-7 py-8 max-[1120px]:hidden">
        <div class="mb-10 flex items-center gap-3">
          <div class="grid size-10 place-items-center rounded-[var(--radius-md)] bg-[var(--bg-1)] text-[var(--primary)]">
            <Factory :size="21" :stroke-width="2.4" />
          </div>
          <div>
            <div class="text-[var(--fs-xl)] font-[var(--fw-strong)] leading-tight">{{ t('brand.name') }}</div>
            <div class="text-[var(--fs-xs)] font-[var(--fw-semibold)] uppercase text-[var(--text-2)]">{{ t('brand.subtitle') }}</div>
          </div>
        </div>

        <nav class="grid gap-2" :aria-label="t('login.capabilityNav')">
          <div class="flex h-11 items-center gap-3 rounded-[var(--radius-sm)] bg-[var(--primary)] px-3 text-[var(--fs-base)] font-[var(--fw-bold)] text-[var(--text-0)]">
            <LayoutDashboard :size="18" :stroke-width="2.4" />
            {{ t('login.sectionOverview') }}
          </div>
          <div class="flex h-11 items-center gap-3 rounded-[var(--radius-sm)] px-3 text-[var(--fs-base)] font-[var(--fw-bold)] text-[var(--text-1)]">
            <Activity :size="18" :stroke-width="2.2" />
            {{ t('login.sectionDevices') }}
          </div>
          <div class="flex h-11 items-center gap-3 rounded-[var(--radius-sm)] px-3 text-[var(--fs-base)] font-[var(--fw-bold)] text-[var(--text-1)]">
            <ClipboardList :size="18" :stroke-width="2.2" />
            {{ t('login.sectionTrace') }}
          </div>
          <div class="flex h-11 items-center gap-3 rounded-[var(--radius-sm)] px-3 text-[var(--fs-base)] font-[var(--fw-bold)] text-[var(--text-1)]">
            <Users :size="18" :stroke-width="2.2" />
            {{ t('login.sectionTeam') }}
          </div>
        </nav>

        <div class="mt-auto rounded-[var(--radius-xl)] bg-[var(--bg-1)] p-5">
          <div class="mb-2 text-[var(--fs-base)] font-[var(--fw-strong)]">{{ t('login.sideTitle') }}</div>
          <p class="text-[var(--fs-sm)] leading-5 text-[var(--text-2)]">{{ t('login.sideDesc') }}</p>
        </div>
      </aside>

      <section class="grid min-h-0 min-w-0 grid-cols-[410px_minmax(0,1fr)] bg-[var(--bg-2)] p-8 max-[1120px]:grid-cols-1 max-[1120px]:bg-white max-[1120px]:p-7">
        <LoginAccessPanel />
        <LoginCapabilityPanel />
      </section>
    </div>
  </main>
</template>

<script setup lang="ts">
import {
  Activity,
  ClipboardList,
  Factory,
  LayoutDashboard,
  Users,
} from 'lucide-vue-next';
import { useI18n } from 'vue-i18n';
import LoginAccessPanel from '../features/auth/LoginAccessPanel.vue';
import LoginCapabilityPanel from '../features/auth/LoginCapabilityPanel.vue';

const { t } = useI18n();
</script>
