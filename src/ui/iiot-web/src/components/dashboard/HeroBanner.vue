<template>
  <header class="hero">
    <div class="hero__bg" aria-hidden="true"></div>
    <div class="hero__main">
      <h1 class="hero__title">{{ greeting }}，{{ displayName }}</h1>
      <div class="hero__subtitle">
        <span class="hero__date">{{ dateStr }}</span>
        <span class="hero__sep">·</span>
        <span class="hero__role">{{ role || '未分配角色' }}</span>
        <span class="hero__sep">·</span>
        <span v-if="alertCount > 0" class="hero__alert">
          <SeverityBadge severity="warn" :label="`${alertCount} 条告警`" />
          <span class="hero__alert-text">近24小时事件</span>
        </span>
        <span v-else class="hero__ok">
          <StatusLed status="success" />
          <span>近24小时无告警事件</span>
        </span>
      </div>
    </div>
  </header>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import StatusLed from '../feedback/StatusLed.vue';
import SeverityBadge from '../feedback/SeverityBadge.vue';

const props = defineProps<{
  name: string;
  role?: string;
  alertCount: number;
}>();

const displayName = computed(() => props.name || '用户');

const greeting = computed(() => {
  const h = new Date().getHours();
  if (h < 6) return '凌晨好';
  if (h < 12) return '上午好';
  if (h < 14) return '中午好';
  if (h < 18) return '下午好';
  return '晚上好';
});

const dateStr = computed(() =>
  new Date().toLocaleDateString('zh-CN', {
    year: 'numeric',
    month: 'long',
    day: 'numeric',
    weekday: 'long',
  }),
);
</script>

<style scoped>
.hero {
  position: relative;
  padding: var(--space-5) 0 var(--space-6);
  margin-bottom: var(--space-6);
}
.hero__bg {
  position: absolute;
  inset: -40px -32px auto -32px;
  height: 240px;
  pointer-events: none;
  z-index: 0;
  background:
    radial-gradient(circle at 12% 30%, rgba(8, 145, 178, 0.08), transparent 50%),
    radial-gradient(circle at 78% 70%, rgba(99, 102, 241, 0.06), transparent 55%);
  mask-image: linear-gradient(to bottom, black, transparent);
  -webkit-mask-image: linear-gradient(to bottom, black, transparent);
}
.hero__main {
  position: relative;
  z-index: 1;
}
.hero__title {
  font-size: var(--fs-4xl);
  font-weight: var(--fw-bold);
  letter-spacing: 0;
  margin: 0;
  color: var(--text-0);
  line-height: 1.15;
}
.hero__subtitle {
  margin: var(--space-3) 0 0;
  font-size: var(--fs-md);
  color: var(--text-1);
  display: flex;
  align-items: center;
  gap: var(--space-2);
  flex-wrap: wrap;
}
.hero__sep { color: var(--text-2); }
.hero__date { color: var(--text-1); }
.hero__role { color: var(--text-1); }
.hero__alert,
.hero__ok {
  display: inline-flex;
  align-items: center;
  gap: var(--space-2);
}
.hero__ok { color: var(--text-1); }
.hero__alert-text { color: var(--text-1); }
</style>
