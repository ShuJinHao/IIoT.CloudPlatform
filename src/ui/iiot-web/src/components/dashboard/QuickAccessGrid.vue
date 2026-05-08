<template>
  <CardSurface title="快速操作" subtitle="常用动作 · 状态一览">
    <div class="quick">
      <router-link
        v-for="item in visibleItems"
        :key="item.name"
        :to="item.path"
        class="quick__item"
        :class="{ 'quick__item--alert': item.urgent }"
      >
        <div class="quick__head">
          <div class="quick__icon" v-html="item.icon"></div>
          <div v-if="item.badge" class="quick__badge" :class="`quick__badge--${item.badgeTone}`">
            {{ item.badge }}
          </div>
        </div>
        <div class="quick__name">{{ item.label }}</div>
        <div class="quick__hint">{{ item.hint }}</div>
        <svg class="quick__arrow" viewBox="0 0 16 16" fill="none">
          <path d="M5 4l4 4-4 4" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" />
        </svg>
      </router-link>
    </div>
  </CardSurface>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import CardSurface from '../layout/CardSurface.vue';
import { usePermission } from '../../composables/usePermission';
import { Permissions } from '../../types/permissions';

interface QuickItemDef {
  name: string;
  path: string;
  label: string;
  hint: string;
  permission: string | null;
  icon: string;
  badge?: string;
  badgeTone?: 'brand' | 'warn' | 'error' | 'success' | 'info';
  urgent?: boolean;
}

const props = withDefaults(
  defineProps<{
    alertCount?: number;
    onlineCount?: number;
    totalCount?: number;
    todayProduction?: number;
  }>(),
  {
    alertCount: 0,
    onlineCount: 0,
    totalCount: 0,
    todayProduction: 0,
  },
);

const { has } = usePermission();

const items = computed<QuickItemDef[]>(() => {
  const formattedProd = props.todayProduction.toLocaleString('zh-CN');
  const onlineRatio =
    props.totalCount > 0 ? `${props.onlineCount}/${props.totalCount}` : '';

  return [
    {
      name: 'AlertTriage',
      path: '/device-logs',
      label: '查看告警',
      hint: '近24小时日志事件',
      permission: Permissions.Device.Read,
      icon: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M12 9v4M12 17h0M10.3 3.7l-7.5 13a2 2 0 0 0 1.7 3h15a2 2 0 0 0 1.7-3l-7.5-13a2 2 0 0 0-3.4 0z"/></svg>`,
      badge: props.alertCount > 0 ? `${props.alertCount} 条` : undefined,
      badgeTone: props.alertCount > 0 ? 'warn' : undefined,
      urgent: props.alertCount > 0,
    },
    {
      name: 'DeviceRegister',
      path: '/devices',
      label: '设备台账',
      hint: '查看设备与 ClientCode',
      permission: Permissions.Device.Read,
      icon: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><rect x="3" y="3" width="18" height="18" rx="2"/><path d="M12 8v8M8 12h8"/></svg>`,
      badge: onlineRatio ? `${onlineRatio} 在线` : undefined,
      badgeTone: 'brand',
    },
    {
      name: 'RecipePush',
      path: '/recipes',
      label: '配方管理',
      hint: '版本化 · 设备工序绑定',
      permission: Permissions.Recipe.Read,
      icon: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/><path d="M9 14l2 2 4-4"/></svg>`,
    },
    {
      name: 'CapacityReport',
      path: '/capacity',
      label: '查看产能',
      hint: '日 · 周 · 月趋势',
      permission: Permissions.Device.Read,
      icon: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M3 3v18h18"/><path d="M18 17V9"/><path d="M13 17V5"/><path d="M8 17v-3"/></svg>`,
      badge: props.todayProduction > 0 ? `${formattedProd} 件` : undefined,
      badgeTone: 'success',
    },
    {
      name: 'PassStationTrace',
      path: '/pass-station',
      label: '追溯过站',
      hint: '按条码 / 时间 / 设备查询',
      permission: Permissions.Device.Read,
      icon: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><circle cx="12" cy="12" r="9"/><path d="M3 12h18"/><circle cx="12" cy="12" r="3" fill="currentColor"/></svg>`,
    },
    {
      name: 'EmployeeOnboard',
      path: '/employees',
      label: '人员入职',
      hint: '建账 · 角色 · 权限授予',
      permission: Permissions.Employee.Read,
      icon: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><circle cx="9" cy="8" r="4"/><path d="M3 21v-2a4 4 0 0 1 4-4h4a4 4 0 0 1 4 4v2"/><path d="M16 11h6M19 8v6"/></svg>`,
    },
  ];
});

const visibleItems = computed(() =>
  items.value.filter((it) => !it.permission || has(it.permission)),
);
</script>

<style scoped>
.quick {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
  gap: var(--space-3);
}
.quick__item {
  position: relative;
  display: block;
  padding: var(--space-4);
  background: var(--bg-1);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  text-decoration: none;
  color: var(--text-0);
  transition: all var(--motion-base);
  overflow: hidden;
}
.quick__item::before {
  content: '';
  position: absolute;
  top: -50%;
  left: -50%;
  width: 200%;
  height: 200%;
  background: radial-gradient(circle, rgba(8, 145, 178, 0.06), transparent 60%);
  opacity: 0;
  transition: opacity var(--motion-base);
  pointer-events: none;
}
.quick__item:hover {
  border-color: var(--border-brand);
  transform: translateY(-2px);
  box-shadow: var(--shadow-card-hover);
}
.quick__item:hover::before { opacity: 1; }

/* 紧急（有告警时）状态：边框带橙色微提示 */
.quick__item--alert {
  border-color: rgba(217, 119, 6, 0.28);
}
.quick__item--alert::before {
  background: radial-gradient(circle, rgba(217, 119, 6, 0.08), transparent 60%);
}
.quick__item--alert:hover {
  border-color: var(--warn);
}

.quick__head {
  position: relative;
  z-index: 1;
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--space-2);
  margin-bottom: var(--space-3);
}
.quick__icon {
  width: 36px;
  height: 36px;
  border-radius: var(--radius-md);
  background: linear-gradient(135deg, var(--brand-soft), var(--info-soft));
  color: var(--brand);
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}
.quick__icon :deep(svg) { width: 18px; height: 18px; }

.quick__badge {
  display: inline-flex;
  align-items: center;
  padding: 3px 8px;
  font-size: var(--fs-xs);
  font-weight: var(--fw-semibold);
  font-family: var(--font-mono);
  border-radius: var(--radius-sm);
  white-space: nowrap;
  letter-spacing: 0;
}
.quick__badge--brand   { color: var(--brand);   background: var(--brand-soft); }
.quick__badge--warn    { color: var(--warn);    background: var(--warn-soft); }
.quick__badge--error   { color: var(--error);   background: var(--error-soft); }
.quick__badge--success { color: var(--success); background: var(--success-soft); }
.quick__badge--info    { color: var(--info);    background: var(--info-soft); }

.quick__name {
  position: relative;
  z-index: 1;
  font-size: var(--fs-md);
  font-weight: var(--fw-semibold);
  margin-bottom: var(--space-1);
}
.quick__hint {
  position: relative;
  z-index: 1;
  font-size: var(--fs-sm);
  color: var(--text-2);
  line-height: 1.5;
}
.quick__arrow {
  position: absolute;
  bottom: var(--space-4);
  right: var(--space-4);
  width: 14px;
  height: 14px;
  color: var(--text-2);
  transition: all var(--motion-base);
  z-index: 1;
}
.quick__item:hover .quick__arrow {
  color: var(--brand);
  transform: translateX(2px);
}
</style>
