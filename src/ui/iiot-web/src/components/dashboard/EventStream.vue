<template>
  <CardSurface title="最新事件">
    <template #header>
      <div class="stream__head-right">
        <span v-if="isDemo" class="stream__demo-tag">演示数据</span>
        <span class="stream__live">
          <StatusLed status="success" />
          <span>最新</span>
        </span>
      </div>
    </template>
    <div class="stream__body">
      <LoadingState v-if="loading" :rows="5" />
      <EmptyState
        v-else-if="!events || events.length === 0"
        title="暂无事件"
        description="当前权限范围内暂无近期待关注日志。"
      />
      <div v-else class="stream__list">
        <div v-for="(ev, i) in events" :key="i" class="stream__row">
          <span class="stream__time">{{ ev.time }}</span>
          <span class="stream__msg">{{ ev.message }}</span>
          <span class="stream__device">{{ ev.deviceCode }}</span>
          <SeverityBadge :severity="ev.severity" :label="ev.label" />
        </div>
      </div>
    </div>
  </CardSurface>
</template>

<script setup lang="ts">
import StatusLed from '../feedback/StatusLed.vue';
import SeverityBadge from '../feedback/SeverityBadge.vue';
import CardSurface from '../layout/CardSurface.vue';
import LoadingState from '../states/LoadingState.vue';
import EmptyState from '../states/EmptyState.vue';

export interface DashboardEvent {
  time: string;
  message: string;
  deviceCode: string;
  severity: 'error' | 'warn' | 'info' | 'success';
  label: string;
}

defineProps<{
  events: DashboardEvent[];
  loading?: boolean;
  isDemo?: boolean;
}>();
</script>

<style scoped>
.stream__head-right {
  display: flex;
  align-items: center;
  gap: var(--space-3);
}
.stream__demo-tag {
  font-family: var(--font-mono);
  font-size: var(--fs-xs);
  color: var(--warn);
  background: var(--warn-soft);
  padding: 3px 8px;
  border-radius: var(--radius-sm);
  letter-spacing: 0;
}
.stream__live {
  display: inline-flex;
  align-items: center;
  gap: var(--space-2);
  font-size: var(--fs-xs);
  color: var(--success);
  font-family: var(--font-mono);
  letter-spacing: 0;
  padding: 4px 8px;
  background: var(--success-soft);
  border-radius: var(--radius-sm);
}
.stream__list {
  display: flex;
  flex-direction: column;
}
.stream__row {
  display: grid;
  grid-template-columns: 70px 1fr 110px 64px;
  gap: var(--space-3);
  padding: var(--space-3) 0;
  border-bottom: 1px solid var(--border);
  align-items: center;
  font-size: var(--fs-sm);
}
.stream__row:last-child {
  border-bottom: none;
  padding-bottom: 0;
}
.stream__row:first-child {
  padding-top: 0;
}
.stream__time {
  color: var(--text-2);
  font-family: var(--font-mono);
}
.stream__msg {
  color: var(--text-0);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.stream__device {
  color: var(--brand);
  font-family: var(--font-mono);
  font-size: var(--fs-xs);
  text-align: right;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
