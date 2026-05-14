<template>
  <header class="hero" :data-state="dataState">
    <div class="hero__content">
      <div class="hero__left">
        <h1 class="hero__title">欢迎，{{ displayName }}</h1>
        <div class="hero__subtitle">
          今日车间运行概览 · {{ dateStr }}
        </div>
      </div>

      <div class="hero__right">
        <div class="hero__kpi-group">
          <div class="hero__kpi-item">
            <span class="hero__kpi-value">{{ onlineCountDisplay }}</span>
            <span class="hero__kpi-label">在线设备</span>
          </div>
          <div class="hero__kpi-divider"></div>
          <div class="hero__kpi-item" :class="{ 'has-alert': alertCount > 0 }">
            <span class="hero__kpi-value">{{ alertCountDisplay }}</span>
            <span class="hero__kpi-label">告警数</span>
          </div>
        </div>
        <div class="hero__state-label">
          <span v-if="dataState === 'error'" class="state-error">
            <StatusLed status="idle" />数据加载失败
          </span>
          <span v-else-if="dataState === 'loading'" class="state-loading">
            <StatusLed status="idle" />数据加载中
          </span>
        </div>
      </div>
    </div>
  </header>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import StatusLed from '../feedback/StatusLed.vue';

const props = withDefaults(
  defineProps<{
    name: string;
    role?: string;
    alertCount: number;
    onlineCount?: string | number;
    dataState?: 'loading' | 'ready' | 'error';
  }>(),
  {
    dataState: 'ready',
    onlineCount: '--',
  },
);

const displayName = computed(() => props.name || '用户');

const dateStr = computed(() =>
  new Date().toLocaleDateString('zh-CN', {
    year: 'numeric',
    month: 'long',
    day: 'numeric',
    weekday: 'long',
  }),
);

const onlineCountDisplay = computed(() => props.dataState === 'ready' ? props.onlineCount : '--');
const alertCountDisplay = computed(() => props.dataState === 'ready' ? props.alertCount : '--');
</script>

<style scoped>
.hero {
  position: relative;
  height: 200px;
  border-radius: var(--radius-xl);
  overflow: hidden;
  background: var(--bg-1);
  border: 1px solid var(--border);
  color: var(--text-0);
  display: flex;
  align-items: center;
  padding: 0 40px;
  box-shadow: var(--shadow-md);
  transition: all var(--motion-base) ease;
}

.hero[data-state="error"] {
  border-color: var(--error);
  background: var(--error-soft);
}

.hero__content {
  position: relative;
  z-index: 1;
  display: flex;
  justify-content: space-between;
  align-items: center;
  width: 100%;
}

.hero__left {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.hero__title {
  font-size: 32px;
  font-weight: var(--fw-display);
  margin: 0;
  line-height: 1.2;
  letter-spacing: 0;
}

.hero__subtitle {
  font-size: 14px;
  color: var(--text-2);
  font-weight: 500;
}

.hero__right {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 12px;
}

.hero__kpi-group {
  display: flex;
  align-items: center;
  gap: 24px;
  background: var(--bg-3);
  padding: 16px 24px;
  border-radius: var(--radius-lg);
  border: 1px solid var(--border-strong);
}

.hero__kpi-item {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 4px;
}

.hero__kpi-value {
  font-size: var(--fs-display-2);
  font-weight: 800;
  line-height: 1;
  color: var(--text-0);
  font-family: var(--font-mono);
  font-feature-settings: 'tnum' on;
  letter-spacing: 0;
}

.hero__kpi-label {
  font-size: 13px;
  color: var(--text-1);
  font-weight: 600;
}

.hero__kpi-item.has-alert .hero__kpi-value {
  color: var(--error);
}

.hero__kpi-divider {
  width: 1px;
  height: 40px;
  background: var(--border-strong);
}

.hero__state-label {
  font-size: 12px;
  color: var(--text-2);
}

.state-error, .state-loading {
  display: flex;
  align-items: center;
  gap: 6px;
}

@media (max-width: 768px) {
  .hero {
    height: auto;
    min-height: 160px;
    padding: 24px;
  }
  .hero__content {
    flex-direction: column;
    align-items: flex-start;
    gap: 24px;
  }
  .hero__right {
    align-items: flex-start;
    width: 100%;
  }
  .hero__kpi-group {
    width: 100%;
    justify-content: space-around;
  }
  .hero__kpi-value {
    font-size: var(--fs-display-1);
  }
}
</style>
