<template>
  <div class="layout-root">
    <header class="topbar">
      <router-link to="/" class="topbar__brand">
        <span class="topbar__brand-mark">
          <svg viewBox="0 0 40 40" fill="none">
            <rect x="2" y="2" width="16" height="16" rx="3" fill="#0891b2" opacity="0.92"/>
            <rect x="22" y="2" width="16" height="16" rx="3" fill="#6366f1" opacity="0.78"/>
            <rect x="2" y="22" width="16" height="16" rx="3" fill="#059669" opacity="0.72"/>
            <rect x="22" y="22" width="16" height="16" rx="3" fill="#0891b2" opacity="0.85"/>
          </svg>
        </span>
        <span class="topbar__brand-name">IIoT 云平台</span>
      </router-link>

      <nav class="topbar__nav">
        <router-link
          v-for="item in visibleNavItems"
          :key="item.name"
          :to="item.path"
          class="topbar__nav-item"
          :class="{ 'is-active': isActive(item.path) }"
        >
          {{ item.label }}
        </router-link>
      </nav>

      <div class="topbar__right">
        <button
          class="topbar__ai-entry"
          type="button"
          :disabled="!isAicopilotEntryConfigured"
          :title="aicopilotEntryTitle"
          @click="openAicopilot"
        >
          <svg viewBox="0 0 20 20" fill="none" aria-hidden="true">
            <path d="M10 3l1.4 3.6L15 8l-3.6 1.4L10 13l-1.4-3.6L5 8l3.6-1.4L10 3z" stroke="currentColor" stroke-width="1.4" stroke-linejoin="round"/>
            <path d="M15 12l.7 1.8 1.8.7-1.8.7L15 17l-.7-1.8-1.8-.7 1.8-.7L15 12z" stroke="currentColor" stroke-width="1.2" stroke-linejoin="round"/>
          </svg>
          AI 助手
        </button>

        <span class="topbar__status">
          <StatusLed status="success" />
          <span class="topbar__status-text">已登录</span>
        </span>

        <n-popover
          trigger="click"
          placement="bottom-end"
          :show-arrow="false"
          raw
        >
          <template #trigger>
            <button class="topbar__avatar" type="button" :title="authStore.employeeNo">
              {{ avatarChar }}
            </button>
          </template>
          <div class="profile-menu">
            <div class="profile-menu__head">
              <div class="profile-menu__avatar">{{ avatarChar }}</div>
              <div class="profile-menu__info">
                <div class="profile-menu__name">{{ authStore.employeeNo || '用户' }}</div>
                <div class="profile-menu__role">{{ authStore.role || '未分配角色' }}</div>
              </div>
            </div>
            <div class="profile-menu__divider"></div>
            <button class="profile-menu__item" @click="openPasswordModal">
              <svg viewBox="0 0 20 20" fill="none">
                <rect x="3" y="9" width="14" height="9" rx="2" stroke="currentColor" stroke-width="1.5"/>
                <path d="M7 9V6a3 3 0 016 0v3" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
              </svg>
              修改密码
            </button>
            <button
              class="profile-menu__item profile-menu__item--logout"
              @click="handleLogout"
            >
              <svg viewBox="0 0 20 20" fill="none">
                <path d="M7 3H4a1 1 0 00-1 1v12a1 1 0 001 1h3M13 14l4-4-4-4M17 10H8" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
              </svg>
              退出登录
            </button>
          </div>
        </n-popover>
      </div>
    </header>

    <main
      class="page-content"
      :class="isLightView ? 'page-content--light' : 'page-content--legacy'"
    >
      <router-view v-slot="{ Component }">
        <transition name="page-fade" mode="out-in">
          <component :is="Component" />
        </transition>
      </router-view>
    </main>

    <Teleport to="body">
      <div
        v-if="showPasswordModal"
        class="modal-overlay"
        @click.self="showPasswordModal = false"
      >
        <div class="modal modal-sm">
          <div class="modal-header">
            <span class="modal-title">修改密码</span>
            <button class="modal-close" @click="showPasswordModal = false">✕</button>
          </div>
          <div class="modal-body">
            <div class="form-field">
              <label>当前密码 <span class="required">*</span></label>
              <input type="password" v-model="passwordForm.current" placeholder="输入当前密码" />
            </div>
            <div class="form-field">
              <label>新密码 <span class="required">*</span></label>
              <input type="password" v-model="passwordForm.newPwd" placeholder="至少 8 位，含大小写和数字" />
            </div>
            <div class="form-field">
              <label>确认新密码 <span class="required">*</span></label>
              <input type="password" v-model="passwordForm.confirm" placeholder="再次输入新密码" />
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn btn-ghost" @click="showPasswordModal = false">取消</button>
            <button
              class="btn btn-primary"
              :disabled="pwdSubmitting"
              @click="submitPassword"
            >
              {{ pwdSubmitting ? '修改中...' : '确认修改' }}
            </button>
          </div>
        </div>
      </div>
    </Teleport>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed } from 'vue';
import { useRouter, useRoute } from 'vue-router';
import { NPopover } from 'naive-ui';
import { useAuthStore } from '../stores/auth';
import { Permissions } from '../types/permissions';
import { changePasswordApi } from '../api/identity';
import StatusLed from '../components/feedback/StatusLed.vue';

interface NavItem {
  name: string;
  path: string;
  label: string;
  permission: string | null;
}

const router = useRouter();
const route = useRoute();
const authStore = useAuthStore();
const aicopilotChallengeUrl = (import.meta.env.VITE_AICOPILOT_CHALLENGE_URL as string | undefined)?.trim() || '';

const avatarChar = computed(() => {
  return authStore.employeeNo?.charAt(0)?.toUpperCase() || 'U';
});

const isAicopilotEntryConfigured = computed(() => aicopilotChallengeUrl.length > 0);
const aicopilotEntryTitle = computed(() =>
  isAicopilotEntryConfigured.value ? '进入 AI 助手' : 'AI 助手入口未配置',
);

const openAicopilot = () => {
  if (!isAicopilotEntryConfigured.value) {
    alert('AI 助手入口未配置');
    return;
  }

  window.location.assign(aicopilotChallengeUrl);
};

const navItems: NavItem[] = [
  { name: 'Dashboard', path: '/', label: '概览', permission: null },
  { name: 'Employees', path: '/employees', label: '人员', permission: Permissions.Employee.Read },
  { name: 'MasterDataProcesses', path: '/master-data/processes', label: '工序', permission: Permissions.Process.Read },
  { name: 'Devices', path: '/devices', label: '设备', permission: Permissions.Device.Read },
  { name: 'Recipes', path: '/recipes', label: '配方', permission: Permissions.Recipe.Read },
  { name: 'PassStation', path: '/pass-station', label: '过站', permission: Permissions.Device.Read },
  { name: 'Capacity', path: '/capacity', label: '产能', permission: Permissions.Device.Read },
  { name: 'DeviceLogs', path: '/device-logs', label: '日志', permission: Permissions.Device.Read },
  { name: 'Roles', path: '/roles', label: '权限', permission: Permissions.Role.Define },
];

const visibleNavItems = computed(() =>
  navItems.filter((item) => !item.permission || authStore.hasPermission(item.permission)),
);

const isActive = (path: string) => {
  if (path === '/') return route.path === '/';
  return route.path.startsWith(path);
};

// 已迁移到浅色风的路由名（其它路由 = 旧 view，仍 dark）
const lightRouteNames = new Set([
  'Dashboard',
  'Capacity',
  'CapacityDetail',
  'Devices',
  'Recipes',
  'Employees',
  'DeviceLogs',
  'MasterDataProcesses',
  'PassStation',
  'Roles',
  'Forbidden',
]);

const isLightView = computed(() => {
  const name = route.name as string | undefined;
  return name ? lightRouteNames.has(name) : false;
});

const handleLogout = () => {
  authStore.logout();
  router.push('/login');
};

// === 修改密码 ===
const showPasswordModal = ref(false);
const pwdSubmitting = ref(false);
const passwordForm = reactive({ current: '', newPwd: '', confirm: '' });

const openPasswordModal = () => {
  showPasswordModal.value = true;
};

const submitPassword = async () => {
  if (!passwordForm.current || !passwordForm.newPwd || !passwordForm.confirm) {
    alert('所有字段均为必填');
    return;
  }
  if (passwordForm.newPwd !== passwordForm.confirm) {
    alert('两次输入的新密码不一致');
    return;
  }
  pwdSubmitting.value = true;
  try {
    await changePasswordApi({
      userId: authStore.userId,
      currentPassword: passwordForm.current,
      newPassword: passwordForm.newPwd,
    });
    showPasswordModal.value = false;
    passwordForm.current = '';
    passwordForm.newPwd = '';
    passwordForm.confirm = '';
    alert('密码修改成功');
  } catch {
    /* http 拦截器已弹错误 */
  } finally {
    pwdSubmitting.value = false;
  }
};
</script>

<style scoped>
.layout-root {
  display: flex;
  flex-direction: column;
  min-height: 100vh;
  background: var(--bg-0);
  color: var(--text-0);
  font-family: var(--font-sans);
}

/* === 顶栏（浅色 + 阴影分层） === */
.topbar {
  position: sticky;
  top: 0;
  z-index: 50;
  height: 64px;
  padding: 0 24px;
  display: flex;
  align-items: center;
  gap: 24px;
  background: rgba(255, 255, 255, 0.85);
  backdrop-filter: blur(14px);
  -webkit-backdrop-filter: blur(14px);
  border-bottom: 1px solid var(--border);
  box-shadow: var(--shadow-sm);
}

.topbar__brand {
  display: flex;
  align-items: center;
  gap: 10px;
  text-decoration: none;
  color: var(--text-0);
  flex-shrink: 0;
}
.topbar__brand-mark {
  width: 28px;
  height: 28px;
  display: flex;
}
.topbar__brand-mark svg {
  width: 100%;
  height: 100%;
}
.topbar__brand-name {
  font-size: 15px;
  font-weight: 700;
  letter-spacing: 0;
  color: var(--text-0);
  white-space: nowrap;
}

.topbar__nav {
  flex: 1;
  display: flex;
  gap: 4px;
  align-items: center;
  overflow-x: auto;
  scrollbar-width: none;
  -ms-overflow-style: none;
  min-width: 0;
}
.topbar__nav::-webkit-scrollbar {
  display: none;
}
.topbar__nav-item {
  padding: 8px 14px;
  color: var(--text-1);
  font-size: 14px;
  font-weight: 500;
  text-decoration: none;
  border-radius: var(--radius-md);
  transition: all 150ms;
  white-space: nowrap;
  flex-shrink: 0;
  border: 1px solid transparent;
}
.topbar__nav-item:hover {
  color: var(--text-0);
  background: var(--bg-3);
}
.topbar__nav-item.is-active {
  color: var(--brand);
  background: var(--brand-soft);
}

.topbar__right {
  display: flex;
  align-items: center;
  gap: 16px;
  flex-shrink: 0;
}
.topbar__status {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  font-size: 12px;
  color: var(--text-1);
  font-family: var(--font-mono);
}
.topbar__ai-entry {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 7px;
  height: 34px;
  padding: 0 12px;
  border-radius: var(--radius-md);
  border: 1px solid var(--border);
  background: var(--bg-1);
  color: var(--text-0);
  font: inherit;
  font-size: 13px;
  font-weight: 600;
  cursor: pointer;
  transition: all 150ms;
  white-space: nowrap;
}
.topbar__ai-entry svg {
  width: 16px;
  height: 16px;
  flex-shrink: 0;
  color: var(--brand);
}
.topbar__ai-entry:hover:not(:disabled) {
  border-color: var(--brand);
  background: var(--brand-soft);
  color: var(--brand);
}
.topbar__ai-entry:disabled {
  cursor: not-allowed;
  opacity: 0.55;
}
.topbar__avatar {
  width: 36px;
  height: 36px;
  border-radius: 50%;
  border: 1px solid var(--border);
  background: linear-gradient(135deg, var(--brand), var(--info));
  color: #ffffff;
  font-size: 13px;
  font-weight: 700;
  cursor: pointer;
  transition: all 150ms;
  display: flex;
  align-items: center;
  justify-content: center;
  font-family: var(--font-sans);
}
.topbar__avatar:hover {
  transform: scale(1.05);
  box-shadow: var(--brand-glow);
}

/* === 用户菜单弹窗 === */
.profile-menu {
  width: 260px;
  background: var(--bg-1);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  box-shadow: var(--shadow-lg);
  padding: 6px;
  font-family: var(--font-sans);
}
.profile-menu__head {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 12px;
}
.profile-menu__avatar {
  width: 40px;
  height: 40px;
  border-radius: 50%;
  background: linear-gradient(135deg, var(--brand), var(--info));
  color: #ffffff;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 14px;
  font-weight: 700;
  flex-shrink: 0;
}
.profile-menu__info {
  min-width: 0;
}
.profile-menu__name {
  font-size: 14px;
  font-weight: 600;
  color: var(--text-0);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.profile-menu__role {
  font-size: 12px;
  color: var(--text-1);
  margin-top: 2px;
}
.profile-menu__divider {
  height: 1px;
  background: var(--border);
  margin: 4px 0;
}
.profile-menu__item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 9px 12px;
  width: 100%;
  border: none;
  background: transparent;
  color: var(--text-1);
  font: inherit;
  font-size: 13px;
  cursor: pointer;
  border-radius: var(--radius-sm);
  text-align: left;
  transition: all 150ms;
}
.profile-menu__item svg {
  width: 14px;
  height: 14px;
  flex-shrink: 0;
}
.profile-menu__item:hover {
  background: var(--bg-3);
  color: var(--text-0);
}
.profile-menu__item--logout:hover {
  color: var(--error);
  background: var(--error-soft);
}

/* === 主体内容区（按路由切色） === */
.page-content {
  flex: 1;
  overflow-y: auto;
  padding: 24px 28px 32px;
  transition: background-color var(--motion-base);
}
.page-content--light {
  background: var(--bg-0);
  color: var(--text-0);
}
/* 旧 view 仍然沿用历史深色背景，避免视觉破坏 */
.page-content--legacy {
  background: var(--bg-legacy);
  color: var(--text-legacy);
}

/* === 路由切换动画 === */
.page-fade-enter-active,
.page-fade-leave-active {
  transition: opacity 0.2s, transform 0.2s;
}
.page-fade-enter-from {
  opacity: 0;
  transform: translateY(6px);
}
.page-fade-leave-to {
  opacity: 0;
}

/* === 修改密码模态框（浅色版） === */
.modal-overlay {
  position: fixed;
  inset: 0;
  z-index: 1000;
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgba(15, 23, 42, 0.32);
  backdrop-filter: blur(3px);
}
.modal {
  display: flex;
  max-width: 95vw;
  max-height: 90vh;
  flex-direction: column;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--bg-1);
  box-shadow: var(--shadow-lg);
}
.modal-sm {
  width: 420px;
}
.modal-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 20px 24px 16px;
  border-bottom: 1px solid var(--border);
}
.modal-title {
  color: var(--text-0);
  font-size: 15px;
  font-weight: 600;
}
.modal-close {
  display: inline-flex;
  width: 26px;
  height: 26px;
  align-items: center;
  justify-content: center;
  border: none;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text-2);
  font-size: 14px;
  cursor: pointer;
}
.modal-close:hover {
  background: var(--bg-3);
  color: var(--text-0);
}
.modal-body {
  flex: 1;
  overflow-y: auto;
  padding: 20px 24px;
}
.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  padding: 16px 24px;
  border-top: 1px solid var(--border);
}
.form-field {
  display: flex;
  flex-direction: column;
  gap: 7px;
  margin-bottom: 14px;
}
.form-field label {
  color: var(--text-1);
  font-size: 12px;
}
.required {
  color: var(--error);
}
.form-field input {
  padding: 10px 12px;
  border: 1px solid var(--border-strong);
  border-radius: var(--radius-sm);
  background: var(--bg-1);
  color: var(--text-0);
  font: inherit;
  font-size: 13px;
  outline: none;
  transition: border-color 0.18s ease, box-shadow 0.18s ease;
}
.form-field input:focus {
  border-color: var(--brand);
  box-shadow: 0 0 0 3px var(--brand-soft);
}
.form-field input::placeholder {
  color: var(--text-2);
}

.btn {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  padding: 9px 18px;
  border: 1px solid transparent;
  border-radius: var(--radius-sm);
  font: inherit;
  font-size: 13px;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.18s ease;
}
.btn-primary {
  background: var(--brand);
  color: #ffffff;
}
.btn-primary:hover:not(:disabled) {
  background: var(--brand-hover);
}
.btn-primary:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
.btn-ghost {
  border-color: var(--border-strong);
  background: var(--bg-1);
  color: var(--text-1);
}
.btn-ghost:hover {
  background: var(--bg-3);
  color: var(--text-0);
}
</style>
