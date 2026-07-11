<template>
  <aside class="login-capability min-h-0 min-w-0 rounded-r-[28px] bg-[var(--bg-2)] p-7 max-[1120px]:hidden">
    <div class="login-capability__surface flex h-full min-h-0 flex-col overflow-hidden rounded-[24px] bg-white p-7 shadow-[var(--shadow-sm)]">
      <div>
        <p class="mb-2 text-[var(--fs-sm)] font-[var(--fw-bold)] uppercase text-[var(--text-2)]">
          {{ t('login.capabilityEyebrow') }}
        </p>
        <h2 class="text-[30px] font-[var(--fw-strong)] leading-tight">
          {{ t('login.capabilityTitle') }}
        </h2>
        <p class="mt-3 max-w-[560px] text-[var(--fs-base)] font-[var(--fw-semibold)] leading-6 text-[var(--text-2)]">
          {{ t('login.capabilityDesc') }}
        </p>
      </div>

      <div class="login-capability__grid my-8 grid flex-1 grid-cols-2 content-center gap-5">
        <article
          v-for="item in capabilities"
          :key="item.title"
          class="login-capability__card rounded-[var(--radius-xl)] border border-[var(--border)] bg-[var(--bg-2)] p-5"
        >
          <span class="login-capability__icon mb-7 grid size-11 place-items-center rounded-[var(--radius-md)] bg-white text-[var(--text-0)] shadow-[var(--shadow-sm)]">
            <component :is="item.icon" :size="20" :stroke-width="2.2" />
          </span>
          <h3 class="text-[var(--fs-lg)] font-[var(--fw-strong)] text-[var(--text-0)]">{{ item.title }}</h3>
          <p class="mt-2 text-[var(--fs-sm)] font-[var(--fw-semibold)] leading-5 text-[var(--text-2)]">{{ item.description }}</p>
        </article>
      </div>

      <div class="flex items-center gap-3 rounded-[var(--radius-lg)] bg-[var(--accent)] px-5 py-4 text-white">
        <ShieldCheck class="shrink-0" :size="20" />
        <p class="text-[var(--fs-sm)] font-[var(--fw-semibold)] leading-5">{{ t('login.capabilityFoot') }}</p>
      </div>
    </div>
  </aside>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import {
  Activity,
  ClipboardCheck,
  Factory,
  ShieldCheck,
} from 'lucide-vue-next';

const { t } = useI18n();
const capabilities = computed(() => [
  { icon: Factory, title: t('login.capabilityDevices'), description: t('login.capabilityDevicesDesc') },
  { icon: Activity, title: t('login.capabilityCapacity'), description: t('login.capabilityCapacityDesc') },
  { icon: ClipboardCheck, title: t('login.capabilityTrace'), description: t('login.capabilityTraceDesc') },
  { icon: ShieldCheck, title: t('login.capabilityAccess'), description: t('login.capabilityAccessDesc') },
]);
</script>

<style scoped>
@media (min-width: 1121px) and (max-height: 820px) {
  .login-capability {
    padding: 20px;
  }

  .login-capability__surface {
    padding: 20px;
  }

  .login-capability__grid {
    gap: 12px;
    margin-block: 18px;
  }

  .login-capability__card {
    padding: 16px;
  }

  .login-capability__icon {
    margin-bottom: 16px;
  }
}
</style>
