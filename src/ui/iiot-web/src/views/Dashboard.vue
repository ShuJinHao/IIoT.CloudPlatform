<template>
  <div class="dashboard">
    <div class="page-header">
      <h1 class="page-title">系统概览</h1>
      <span class="page-date">{{ currentDate }}</span>
    </div>

    <!-- 欢迎横幅 -->
    <div class="welcome-banner">
      <div class="banner-left">
        <div class="banner-greeting">你好，<span class="highlight">{{ authStore.employeeNo }}</span></div>
        <div class="banner-role">当前角色：<span class="role-tag">{{ authStore.role || '未分配' }}</span></div>
      </div>
      <div class="banner-right">
        <div class="banner-perm-label">已获授权限点</div>
        <div class="banner-perm-count">{{ authStore.isAdmin ? '全部权限' : authStore.permissions.length + ' 个' }}</div>
      </div>
    </div>

    <!-- 权限点展示卡（方便调试） -->
    <div class="section-title">我的权限点</div>
    <div class="perm-grid" v-if="!authStore.isAdmin">
      <div
        v-for="perm in allPermissions"
        :key="perm"
        class="perm-chip"
        :class="{ owned: authStore.hasPermission(perm) }"
      >
        <span class="perm-dot"></span>
        {{ perm }}
      </div>
    </div>
    <div class="admin-badge" v-else>
      <svg viewBox="0 0 20 20" fill="none"><path d="M10 2l2.4 5h5.3l-4.3 3.1 1.7 5.2L10 12.2l-5.1 3.1 1.7-5.2L2.3 7h5.3L10 2z" fill="#00e5ff" opacity="0.8"/></svg>
      超级管理员 · 拥有系统全部权限
    </div>

    <!-- 快捷入口 -->
    <div class="section-title">快捷入口</div>
    <div class="quick-links">
      <router-link
        v-for="item in visibleQuickLinks"
        :key="item.name"
        :to="item.path"
        class="quick-card"
      >
        <div class="quick-icon" v-html="item.icon"></div>
        <div class="quick-label">{{ item.label }}</div>
        <div class="quick-desc">{{ item.desc }}</div>
        <svg class="quick-arrow" viewBox="0 0 16 16" fill="none"><path d="M4 8h8M9 5l3 3-3 3" stroke="currentColor" stroke-width="1.3" stroke-linecap="round" stroke-linejoin="round"/></svg>
      </router-link>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { useAuthStore } from '../stores/auth';
import { Permissions } from '../types/permissions';

const authStore = useAuthStore();

const currentDate = computed(() => {
  return new Date().toLocaleDateString('zh-CN', { year: 'numeric', month: 'long', day: 'numeric', weekday: 'long' });
});

// 全量权限点列表，用于展示当前用户有哪些
const allPermissions = Object.values(Permissions).flatMap(group => Object.values(group));

const quickLinks = [
  {
    name: 'Employees', path: '/employees', label: '员工花名册', desc: '查看与管理操作人员档案',
    permission: Permissions.Employee.Read,
    icon: `<svg viewBox="0 0 24 24" fill="none"><circle cx="9" cy="7" r="4" stroke="currentColor" stroke-width="1.5"/><path d="M3 21c0-4 2.7-7 6-7h6" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/><path d="M16 14l2 2 4-4" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/></svg>`
  },
  {
    name: 'Devices', path: '/devices', label: '设备台账', desc: '注册与追踪车间物理设备',
    permission: Permissions.Device.Read,
    icon: `<svg viewBox="0 0 24 24" fill="none"><rect x="3" y="6" width="18" height="13" rx="2" stroke="currentColor" stroke-width="1.5"/><path d="M8 6V5a1 1 0 011-1h6a1 1 0 011 1v1" stroke="currentColor" stroke-width="1.5"/><circle cx="12" cy="12.5" r="2.5" stroke="currentColor" stroke-width="1.5"/></svg>`
  },
  {
    name: 'Recipes', path: '/recipes', label: '配方管理', desc: '维护工艺参数与配方数据',
    permission: Permissions.Recipe.Read,
    icon: `<svg viewBox="0 0 24 24" fill="none"><path d="M4 6h16M4 10h10M4 14h12M4 18h8" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/><circle cx="18" cy="17" r="3" stroke="currentColor" stroke-width="1.5"/><path d="M18 15.5v1.5l1 1" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/></svg>`
  },
];

const visibleQuickLinks = computed(() =>
  quickLinks.filter(item => authStore.hasPermission(item.permission))
);
</script>

<style scoped>
@import url('https://fonts.googleapis.com/css2?family=Rajdhani:wght@500;700&family=Noto+Sans+SC:wght@300;400;500&display=swap');

.dashboard { font-family: 'Noto Sans SC', sans-serif; }

.page-header {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  margin-bottom: 24px;
}
.page-title {
  font-size: 22px;
  font-weight: 500;
  color: #e8eaf0;
  margin: 0;
}
.page-date { font-size: 12px; color: rgba(255,255,255,0.25); }

/* 欢迎横幅 */
.welcome-banner {
  display: flex;
  justify-content: space-between;
  align-items: center;
  background: linear-gradient(135deg, rgba(0,119,255,0.12), rgba(0,229,255,0.06));
  border: 1px solid rgba(0,229,255,0.15);
  border-radius: 6px;
  padding: 24px 28px;
  margin-bottom: 32px;
}
.banner-greeting {
  font-size: 18px;
  font-weight: 500;
  color: rgba(255,255,255,0.8);
  margin-bottom: 8px;
}
.banner-greeting .highlight {
  color: #00e5ff;
  font-family: 'Rajdhani', sans-serif;
  font-size: 20px;
  font-weight: 700;
}
.banner-role { font-size: 13px; color: rgba(255,255,255,0.4); }
.role-tag {
  background: rgba(0,229,255,0.12);
  color: #00e5ff;
  padding: 2px 8px;
  border-radius: 10px;
  font-size: 12px;
}
.banner-right { text-align: right; }
.banner-perm-label { font-size: 11px; color: rgba(255,255,255,0.3); margin-bottom: 6px; }
.banner-perm-count {
  font-family: 'Rajdhani', sans-serif;
  font-size: 28px;
  font-weight: 700;
  color: #00e5ff;
}

.section-title {
  font-size: 11px;
  font-weight: 500;
  color: rgba(255,255,255,0.25);
  letter-spacing: 2px;
  text-transform: uppercase;
  margin-bottom: 16px;
}

/* 权限点网格 */
.perm-grid {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-bottom: 32px;
}
.perm-chip {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 5px 12px;
  border-radius: 20px;
  font-size: 12px;
  background: rgba(255,255,255,0.04);
  border: 1px solid rgba(255,255,255,0.07);
  color: rgba(255,255,255,0.25);
  transition: all 0.2s;
}
.perm-chip.owned {
  background: rgba(0,229,255,0.08);
  border-color: rgba(0,229,255,0.25);
  color: rgba(0,229,255,0.8);
}
.perm-dot {
  width: 6px; height: 6px;
  border-radius: 50%;
  background: rgba(255,255,255,0.2);
  flex-shrink: 0;
}
.perm-chip.owned .perm-dot { background: #00e5ff; box-shadow: 0 0 4px #00e5ff; }

.admin-badge {
  display: inline-flex;
  align-items: center;
  gap: 10px;
  padding: 10px 20px;
  background: rgba(0,229,255,0.06);
  border: 1px solid rgba(0,229,255,0.2);
  border-radius: 4px;
  color: rgba(0,229,255,0.8);
  font-size: 14px;
  margin-bottom: 32px;
}
.admin-badge svg { width: 18px; height: 18px; }

/* 快捷入口卡片 */
.quick-links {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
  gap: 16px;
}
.quick-card {
  display: block;
  position: relative;
  padding: 22px;
  background: rgba(255,255,255,0.03);
  border: 1px solid rgba(255,255,255,0.07);
  border-radius: 6px;
  text-decoration: none;
  color: inherit;
  transition: all 0.2s;
  overflow: hidden;
}
.quick-card:hover {
  background: rgba(0,229,255,0.05);
  border-color: rgba(0,229,255,0.2);
  transform: translateY(-2px);
}
.quick-icon {
  width: 36px; height: 36px;
  color: #00e5ff;
  margin-bottom: 14px;
  opacity: 0.8;
}
.quick-icon :deep(svg) { width: 100%; height: 100%; }
.quick-label {
  font-size: 15px;
  font-weight: 500;
  color: rgba(255,255,255,0.8);
  margin-bottom: 6px;
}
.quick-desc {
  font-size: 12px;
  color: rgba(255,255,255,0.3);
  line-height: 1.5;
}
.quick-arrow {
  position: absolute;
  right: 18px; top: 50%;
  transform: translateY(-50%);
  width: 16px; height: 16px;
  color: rgba(255,255,255,0.15);
  transition: all 0.2s;
}
.quick-card:hover .quick-arrow {
  color: rgba(0,229,255,0.6);
  transform: translateY(-50%) translateX(3px);
}
</style>
