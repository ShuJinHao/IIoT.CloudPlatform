<template>
  <div class="layout-root">
    <aside class="sidebar" :class="{ collapsed: sidebarCollapsed }">
      <div class="sidebar-logo">
        <div class="logo-icon">
          <svg viewBox="0 0 40 40" fill="none">
            <rect x="2" y="2" width="16" height="16" rx="2" fill="#00e5ff" opacity="0.9"/>
            <rect x="22" y="2" width="16" height="16" rx="2" fill="#00e5ff" opacity="0.4"/>
            <rect x="2" y="22" width="16" height="16" rx="2" fill="#00e5ff" opacity="0.4"/>
            <rect x="22" y="22" width="16" height="16" rx="2" fill="#00e5ff" opacity="0.9"/>
          </svg>
        </div>
        <transition name="fade-slide">
          <span v-if="!sidebarCollapsed" class="logo-label">IIoT Platform</span>
        </transition>
      </div>

      <nav class="sidebar-nav">
        <div class="nav-section-label" v-if="!sidebarCollapsed">主导航</div>

        <router-link
          v-for="item in visibleNavItems"
          :key="item.name"
          :to="item.path"
          class="nav-item"
          :class="{ active: isActive(item.path) }"
          :title="sidebarCollapsed ? item.label : ''"
        >
          <span class="nav-icon" v-html="item.icon"></span>
          <transition name="fade-slide">
            <span v-if="!sidebarCollapsed" class="nav-label">{{ item.label }}</span>
          </transition>
          <span v-if="!sidebarCollapsed && item.badge" class="nav-badge">{{ item.badge }}</span>
        </router-link>
      </nav>

      <div class="sidebar-footer">
        <div class="user-info" v-if="!sidebarCollapsed">
          <div class="user-avatar">{{ avatarChar }}</div>
          <div class="user-detail">
            <div class="user-name">{{ authStore.employeeNo }}</div>
            <div class="user-name" style="font-size: 11px; color: rgba(0,229,255,0.6); margin-top: 1px;">{{ authStore.role }}</div>
          </div>
        </div>
        <div class="user-avatar-sm" v-else :title="authStore.employeeNo">{{ avatarChar }}</div>
        <button class="footer-btn" @click="showPasswordModal=true" :title="sidebarCollapsed ? '修改密码' : ''">
          <svg viewBox="0 0 20 20" fill="none">
            <rect x="3" y="9" width="14" height="9" rx="2" stroke="currentColor" stroke-width="1.5"/>
            <path d="M7 9V6a3 3 0 016 0v3" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
          </svg>
          <transition name="fade-slide">
            <span v-if="!sidebarCollapsed">修改密码</span>
          </transition>
        </button>
        <button class="footer-btn logout" @click="handleLogout" :title="sidebarCollapsed ? '退出登录' : ''">
          <svg viewBox="0 0 20 20" fill="none">
            <path d="M7 3H4a1 1 0 00-1 1v12a1 1 0 001 1h3M13 14l4-4-4-4M17 10H8" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
          </svg>
          <transition name="fade-slide">
            <span v-if="!sidebarCollapsed">退出登录</span>
          </transition>
        </button>
      </div>

      <button class="collapse-btn" @click="sidebarCollapsed = !sidebarCollapsed">
        <svg viewBox="0 0 16 16" fill="none" :style="{ transform: sidebarCollapsed ? 'rotate(180deg)' : 'none', transition: 'transform 0.3s' }">
          <path d="M10 3L5 8l5 5" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
        </svg>
      </button>
    </aside>

    <div class="main-area">
      <header class="topbar">
        <div class="breadcrumb">
          <span class="breadcrumb-root">系统</span>
          <svg viewBox="0 0 12 12" fill="none" width="12" height="12"><path d="M4 2l4 4-4 4" stroke="rgba(255,255,255,0.3)" stroke-width="1.2" stroke-linecap="round"/></svg>
          <span class="breadcrumb-current">{{ currentTitle }}</span>
        </div>
        <div class="topbar-right">
          <div class="status-dot"></div>
          <span class="status-text">系统运行正常</span>
        </div>
      </header>

      <main class="page-content">
        <router-view v-slot="{ Component }">
          <transition name="page-fade" mode="out-in">
            <component :is="Component" />
          </transition>
        </router-view>
      </main>
    </div>

    <Teleport to="body">
      <div v-if="showPasswordModal" class="modal-overlay" @click.self="showPasswordModal=false">
        <div class="modal modal-sm">
          <div class="modal-header">
            <span class="modal-title">修改密码</span>
            <button class="modal-close" @click="showPasswordModal=false">✕</button>
          </div>
          <div class="modal-body">
            <div class="form-field">
              <label>当前密码 <span class="required">*</span></label>
              <input type="password" v-model="passwordForm.current" placeholder="输入当前密码" />
            </div>
            <div class="form-field">
              <label>新密码 <span class="required">*</span></label>
              <input type="password" v-model="passwordForm.newPwd" placeholder="至少8位，含大小写和数字" />
            </div>
            <div class="form-field">
              <label>确认新密码 <span class="required">*</span></label>
              <input type="password" v-model="passwordForm.confirm" placeholder="再次输入新密码" />
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn btn-ghost" @click="showPasswordModal=false">取消</button>
            <button class="btn btn-primary" :disabled="pwdSubmitting" @click="submitPassword">
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
import { useAuthStore } from '../stores/auth';
import { Permissions } from '../types/permissions';
import { changePasswordApi } from '../api/identity';

interface NavItem {
  name: string;
  path: string;
  label: string;
  icon: string;
  permission: string | null;
  badge?: string | number;
}

const router = useRouter();
const route = useRoute();
const authStore = useAuthStore();

const sidebarCollapsed = ref(false);

const currentTitle = computed(() => {
  return (route.meta.title as string) || '系统概览';
});

const avatarChar = computed(() => {
  return authStore.employeeNo?.charAt(0)?.toUpperCase() || 'U';
});

const navItems: NavItem[] = [
  {
    name: 'Dashboard', path: '/', label: '系统概览', permission: null,
    icon: `<svg viewBox="0 0 20 20" fill="none"><rect x="2" y="2" width="7" height="7" rx="1.5" stroke="currentColor" stroke-width="1.5"/><rect x="11" y="2" width="7" height="7" rx="1.5" stroke="currentColor" stroke-width="1.5"/><rect x="2" y="11" width="7" height="7" rx="1.5" stroke="currentColor" stroke-width="1.5"/><rect x="11" y="11" width="7" height="7" rx="1.5" stroke="currentColor" stroke-width="1.5"/></svg>`
  },
  {
    name: 'Employees', path: '/employees', label: '员工花名册', permission: Permissions.Employee.Read,
    icon: `<svg viewBox="0 0 20 20" fill="none"><circle cx="8" cy="6" r="3" stroke="currentColor" stroke-width="1.5"/><path d="M2 17c0-3.314 2.686-6 6-6s6 2.686 6 6" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/><path d="M14 8l2 2 3-3" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/></svg>`
  },
  {
    name: 'Processes', path: '/processes', label: '工序管理', permission: Permissions.Process.Read,
    icon: `<svg viewBox="0 0 20 20" fill="none"><path d="M3 5h14M3 10h14M3 15h14" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/><circle cx="7" cy="5" r="1.5" fill="currentColor"/><circle cx="13" cy="10" r="1.5" fill="currentColor"/><circle cx="9" cy="15" r="1.5" fill="currentColor"/></svg>`
  },
  {
    name: 'Devices', path: '/devices', label: '设备台账', permission: Permissions.Device.Read,
    icon: `<svg viewBox="0 0 20 20" fill="none"><rect x="2" y="5" width="16" height="11" rx="2" stroke="currentColor" stroke-width="1.5"/><path d="M6 5V4a1 1 0 011-1h6a1 1 0 011 1v1" stroke="currentColor" stroke-width="1.5"/><circle cx="10" cy="11" r="2" stroke="currentColor" stroke-width="1.5"/></svg>`
  },
  {
    name: 'Recipes', path: '/recipes', label: '配方管理', permission: Permissions.Recipe.Read,
    icon: `<svg viewBox="0 0 20 20" fill="none"><path d="M4 4h12v2H4zM4 9h8M4 13h10M4 17h6" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/><circle cx="15" cy="14" r="3" stroke="currentColor" stroke-width="1.5"/><path d="M15 13v1.5l1 1" stroke="currentColor" stroke-width="1.2" stroke-linecap="round"/></svg>`
  },
  {
    name: 'PassStation', path: '/pass-station', label: '过站追溯', permission: Permissions.Device.Read,
    icon: `<svg viewBox="0 0 20 20" fill="none"><path d="M3 10h14" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/><circle cx="6" cy="10" r="2.5" stroke="currentColor" stroke-width="1.3"/><circle cx="14" cy="10" r="2.5" stroke="currentColor" stroke-width="1.3"/><path d="M10 4v12" stroke="currentColor" stroke-width="1.2" stroke-linecap="round" stroke-dasharray="2 2"/></svg>`
  },
  {
    name: 'Capacity', path: '/capacity', label: '产能看板', permission: Permissions.Device.Read,
    icon: `<svg viewBox="0 0 20 20" fill="none"><rect x="3" y="11" width="3" height="6" rx="1" stroke="currentColor" stroke-width="1.2"/><rect x="8.5" y="7" width="3" height="10" rx="1" stroke="currentColor" stroke-width="1.2"/><rect x="14" y="3" width="3" height="14" rx="1" stroke="currentColor" stroke-width="1.2"/></svg>`
  },
  {
    name: 'DeviceLogs', path: '/device-logs', label: '设备日志', permission: Permissions.Device.Read,
    icon: `<svg viewBox="0 0 20 20" fill="none"><rect x="3" y="3" width="14" height="14" rx="2" stroke="currentColor" stroke-width="1.3"/><path d="M6 7h8M6 10h6M6 13h4" stroke="currentColor" stroke-width="1.2" stroke-linecap="round"/></svg>`
  },
  {
    name: 'Roles', path: '/roles', label: '角色与权限', permission: Permissions.Role.Define,
    icon: `<svg viewBox="0 0 20 20" fill="none"><path d="M10 2l1.5 3h3.3l-2.6 2 1 3.2L10 8.1 6.8 10.2l1-3.2-2.6-2h3.3L10 2z" stroke="currentColor" stroke-width="1.3" stroke-linejoin="round"/><path d="M4 14h12M6 17h8" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/></svg>`
  },
];

const visibleNavItems = computed(() =>
  navItems.filter(item => !item.permission || authStore.hasPermission(item.permission))
);

const isActive = (path: string) => {
  if (path === '/') return route.path === '/';
  return route.path.startsWith(path);
};

const handleLogout = () => {
  authStore.logout();
  router.push('/login');
};

// ── 修改密码 ──
const showPasswordModal = ref(false);
const pwdSubmitting = ref(false);
const passwordForm = reactive({ current: '', newPwd: '', confirm: '' });

const submitPassword = async () => {
  if (!passwordForm.current || !passwordForm.newPwd || !passwordForm.confirm) {
    alert('所有字段均为必填'); return;
  }
  if (passwordForm.newPwd !== passwordForm.confirm) {
    alert('两次输入的新密码不一致'); return;
  }
  pwdSubmitting.value = true;
  try {
    await changePasswordApi({
      UserId: authStore.userId,
      CurrentPassword: passwordForm.current,
      NewPassword: passwordForm.newPwd,
    });
    showPasswordModal.value = false;
    passwordForm.current = '';
    passwordForm.newPwd = '';
    passwordForm.confirm = '';
    alert('密码修改成功');
  } catch { } finally { pwdSubmitting.value = false; }
};
</script>

<style scoped>
@import url('https://fonts.googleapis.com/css2?family=Rajdhani:wght@500;600;700&family=Noto+Sans+SC:wght@300;400;500&display=swap');
* { box-sizing: border-box; }
.layout-root { display: flex; min-height: 100vh; background: #080c18; font-family: 'Noto Sans SC', sans-serif; color: #e0e4ef; }
.sidebar { position: relative; width: 220px; min-height: 100vh; background: #0a0e1a; border-right: 1px solid rgba(0,229,255,0.08); display: flex; flex-direction: column; transition: width 0.3s cubic-bezier(0.4,0,0.2,1); flex-shrink: 0; }
.sidebar.collapsed { width: 64px; }
.sidebar-logo { display: flex; align-items: center; gap: 12px; padding: 24px 16px 20px; border-bottom: 1px solid rgba(255,255,255,0.04); overflow: hidden; }
.logo-icon svg { width: 32px; height: 32px; flex-shrink: 0; filter: drop-shadow(0 0 6px rgba(0,229,255,0.4)); }
.logo-label { font-family: 'Rajdhani', sans-serif; font-weight: 600; font-size: 15px; color: #00e5ff; letter-spacing: 1px; white-space: nowrap; }
.sidebar-nav { flex: 1; padding: 16px 8px; overflow: hidden; }
.nav-section-label { font-size: 10px; font-weight: 500; color: rgba(255,255,255,0.2); letter-spacing: 2px; text-transform: uppercase; padding: 0 8px; margin-bottom: 8px; }
.nav-item { display: flex; align-items: center; gap: 10px; padding: 10px 10px; border-radius: 4px; text-decoration: none; color: rgba(255,255,255,0.45); font-size: 13px; font-weight: 400; margin-bottom: 2px; transition: all 0.18s; overflow: hidden; white-space: nowrap; position: relative; }
.nav-item:hover { background: rgba(0,229,255,0.06); color: rgba(255,255,255,0.8); }
.nav-item.active { background: rgba(0,229,255,0.1); color: #00e5ff; }
.nav-item.active::before { content: ''; position: absolute; left: 0; top: 20%; bottom: 20%; width: 2px; background: #00e5ff; border-radius: 2px; }
.nav-icon { width: 20px; height: 20px; flex-shrink: 0; display: flex; align-items: center; justify-content: center; }
.nav-icon :deep(svg) { width: 18px; height: 18px; }
.nav-label { flex: 1; }
.nav-badge { font-size: 10px; background: rgba(0,229,255,0.15); color: #00e5ff; padding: 1px 6px; border-radius: 8px; }
.sidebar-footer { padding: 12px 10px 48px; border-top: 1px solid rgba(255,255,255,0.04); overflow: hidden; }
.user-info { display: flex; align-items: center; gap: 10px; padding: 10px 2px; margin-bottom: 4px; }
.user-avatar { width: 32px; height: 32px; background: linear-gradient(135deg, #0077ff, #00bcd4); border-radius: 50%; display: flex; align-items: center; justify-content: center; font-family: 'Rajdhani', sans-serif; font-weight: 700; font-size: 14px; color: white; flex-shrink: 0; }
.user-avatar-sm { width: 32px; height: 32px; background: linear-gradient(135deg, #0077ff, #00bcd4); border-radius: 50%; display: flex; align-items: center; justify-content: center; font-family: 'Rajdhani', sans-serif; font-weight: 700; font-size: 14px; color: white; margin: 0 auto 8px; cursor: default; }
.user-name { font-size: 13px; font-weight: 500; color: rgba(255,255,255,0.75); }
.user-role { font-size: 11px; color: rgba(0,229,255,0.6); margin-top: 1px; }
.footer-btn { display: flex; align-items: center; gap: 8px; width: 100%; padding: 9px 8px; background: none; border: none; border-radius: 3px; color: rgba(255,255,255,0.3); font-size: 13px; font-family: 'Noto Sans SC', sans-serif; cursor: pointer; transition: all 0.18s; overflow: hidden; white-space: nowrap; }
.footer-btn:hover { background: rgba(255,255,255,0.06); color: rgba(255,255,255,0.6); }
.footer-btn.logout:hover { background: rgba(255,77,79,0.08); color: #ff6b6b; }
.footer-btn svg { width: 16px; height: 16px; flex-shrink: 0; }
.collapse-btn { position: absolute; bottom: 16px; right: -12px; width: 24px; height: 24px; background: #0a0e1a; border: 1px solid rgba(0,229,255,0.2); border-radius: 50%; display: flex; align-items: center; justify-content: center; cursor: pointer; color: rgba(0,229,255,0.6); z-index: 10; transition: all 0.2s; }
.collapse-btn:hover { background: rgba(0,229,255,0.1); color: #00e5ff; }
.collapse-btn svg { width: 12px; height: 12px; }
.main-area { flex: 1; display: flex; flex-direction: column; min-width: 0; }
.topbar { height: 52px; background: rgba(10,14,26,0.8); border-bottom: 1px solid rgba(255,255,255,0.05); display: flex; align-items: center; justify-content: space-between; padding: 0 28px; backdrop-filter: blur(8px); flex-shrink: 0; }
.breadcrumb { display: flex; align-items: center; gap: 8px; font-size: 13px; }
.breadcrumb-root { color: rgba(255,255,255,0.25); }
.breadcrumb-current { color: rgba(255,255,255,0.7); font-weight: 500; }
.topbar-right { display: flex; align-items: center; gap: 8px; }
.status-dot { width: 7px; height: 7px; background: #00e5a0; border-radius: 50%; box-shadow: 0 0 6px #00e5a0; animation: pulse 2s infinite; }
@keyframes pulse { 0%, 100% { opacity: 1; } 50% { opacity: 0.5; } }
.status-text { font-size: 12px; color: rgba(255,255,255,0.25); }
.page-content { flex: 1; padding: 28px; overflow-y: auto; }

/* 修改密码弹窗 */
.modal-overlay { position: fixed; inset: 0; z-index: 1000; background: rgba(0,0,0,0.7); backdrop-filter: blur(4px); display: flex; align-items: center; justify-content: center; }
.modal { background: #0e1526; border: 1px solid rgba(0,229,255,0.15); border-radius: 6px; max-width: 95vw; max-height: 90vh; display: flex; flex-direction: column; box-shadow: 0 24px 64px rgba(0,0,0,0.6); }
.modal-sm { width: 420px; }
.modal-header { display: flex; align-items: center; justify-content: space-between; padding: 20px 24px 16px; border-bottom: 1px solid rgba(255,255,255,0.06); }
.modal-title { font-size: 15px; font-weight: 500; color: #e8eaf0; }
.modal-close { background: none; border: none; cursor: pointer; color: rgba(255,255,255,0.3); font-size: 14px; width: 24px; height: 24px; display: flex; align-items: center; justify-content: center; border-radius: 3px; transition: all 0.15s; }
.modal-close:hover { background: rgba(255,255,255,0.08); color: rgba(255,255,255,0.7); }
.modal-body { padding: 20px 24px; overflow-y: auto; flex: 1; }
.modal-footer { display: flex; justify-content: flex-end; gap: 10px; padding: 16px 24px; border-top: 1px solid rgba(255,255,255,0.06); }
.form-field { display: flex; flex-direction: column; gap: 7px; margin-bottom: 14px; }
.form-field label { font-size: 12px; color: rgba(255,255,255,0.4); }
.required { color: #ff6b6b; }
.form-field input { background: rgba(255,255,255,0.05); border: 1px solid rgba(255,255,255,0.1); border-radius: 3px; padding: 10px 12px; font-size: 13px; color: #e0e4ef; font-family: 'Noto Sans SC', sans-serif; outline: none; transition: border-color 0.2s; }
.form-field input:focus { border-color: rgba(0,229,255,0.4); }
.form-field input::placeholder { color: rgba(255,255,255,0.2); }
.btn { display: inline-flex; align-items: center; gap: 7px; padding: 9px 18px; border-radius: 4px; font-size: 13px; font-weight: 500; cursor: pointer; font-family: 'Noto Sans SC', sans-serif; border: 1px solid transparent; transition: all 0.18s; }
.btn-primary { background: linear-gradient(135deg,#0077ff,#00bcd4); color: white; box-shadow: 0 3px 12px rgba(0,119,255,0.25); }
.btn-primary:hover:not(:disabled) { box-shadow: 0 4px 16px rgba(0,119,255,0.4); opacity: 0.92; }
.btn-primary:disabled { opacity: 0.5; cursor: not-allowed; }
.btn-ghost { background: rgba(255,255,255,0.05); border-color: rgba(255,255,255,0.1); color: rgba(255,255,255,0.6); }
.btn-ghost:hover { background: rgba(255,255,255,0.09); }

.fade-slide-enter-active, .fade-slide-leave-active { transition: all 0.2s; }
.fade-slide-enter-from { opacity: 0; transform: translateX(-6px); }
.fade-slide-leave-to { opacity: 0; transform: translateX(-6px); }
.page-fade-enter-active, .page-fade-leave-active { transition: opacity 0.2s, transform 0.2s; }
.page-fade-enter-from { opacity: 0; transform: translateY(6px); }
.page-fade-leave-to { opacity: 0; }
</style>