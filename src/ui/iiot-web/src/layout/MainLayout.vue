<template>
  <div class="layout-root">
    <aside class="sidebar" :class="{ collapsed: sidebarCollapsed }">
      <div class="sidebar-logo">
        <div class="logo-icon">
          <svg viewBox="0 0 40 40" fill="none">
            <rect x="2" y="2" width="16" height="16" rx="2" fill="#5ab69f" opacity="0.95"/>
            <rect x="22" y="2" width="16" height="16" rx="2" fill="#d9b66d" opacity="0.72"/>
            <rect x="2" y="22" width="16" height="16" rx="2" fill="#87968e" opacity="0.62"/>
            <rect x="22" y="22" width="16" height="16" rx="2" fill="#5ab69f" opacity="0.82"/>
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
            <div class="user-role">{{ authStore.role }}</div>
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
    name: 'MasterDataProcesses', path: '/master-data/processes', label: '工序管理', permission: Permissions.Process.Read,
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
      userId: authStore.userId,
      currentPassword: passwordForm.current,
      newPassword: passwordForm.newPwd,
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
* { box-sizing: border-box; }
.layout-root { display: flex; min-height: 100vh; background: #171b19; font-family: 'Noto Sans SC', sans-serif; color: #edf1ee; }
.sidebar {
  position: relative; display: flex; width: 232px; min-height: 100vh; flex-shrink: 0; flex-direction: column;
  border-right: 1px solid rgba(255,255,255,0.08); background: #1f2421; transition: width 0.28s ease;
}
.sidebar.collapsed { width: 72px; }
.sidebar-logo { display: flex; align-items: center; gap: 12px; padding: 22px 16px 18px; border-bottom: 1px solid rgba(255,255,255,0.06); overflow: hidden; }
.logo-icon svg { width: 32px; height: 32px; flex-shrink: 0; filter: drop-shadow(0 8px 18px rgba(0,0,0,0.18)); }
.logo-label { font-size: 15px; font-weight: 600; color: #edf1ee; white-space: nowrap; }

.sidebar-nav { flex: 1; padding: 16px 10px; overflow: hidden; }
.nav-section-label { margin-bottom: 8px; padding: 0 8px; color: rgba(237,241,238,0.38); font-size: 11px; font-weight: 600; }
.nav-item {
  position: relative; display: flex; align-items: center; gap: 10px; margin-bottom: 4px; padding: 10px 10px;
  border-radius: 6px; color: rgba(237,241,238,0.68); font-size: 13px; text-decoration: none; transition: all 0.18s ease;
  overflow: hidden; white-space: nowrap;
}
.nav-item:hover { background: rgba(255,255,255,0.05); color: #f7faf7; }
.nav-item.active { background: rgba(90,182,159,0.16); color: #d8f1ea; }
.nav-item.active::before { content: ''; position: absolute; left: 0; top: 18%; bottom: 18%; width: 3px; border-radius: 3px; background: #5ab69f; }
.nav-icon { display: flex; width: 20px; height: 20px; align-items: center; justify-content: center; flex-shrink: 0; }
.nav-icon :deep(svg) { width: 18px; height: 18px; }
.nav-label { flex: 1; }
.nav-badge { padding: 2px 6px; border-radius: 6px; background: rgba(217,182,109,0.16); color: #f1d8a6; font-size: 10px; }

.sidebar-footer { padding: 12px 10px 50px; border-top: 1px solid rgba(255,255,255,0.06); overflow: hidden; }
.user-info { display: flex; align-items: center; gap: 10px; padding: 10px 2px; margin-bottom: 6px; }
.user-avatar,
.user-avatar-sm {
  display: flex; width: 34px; height: 34px; align-items: center; justify-content: center; border-radius: 50%;
  background: linear-gradient(135deg, #5ab69f, #8ca298); color: #122019; font-size: 14px; font-weight: 700;
}
.user-avatar-sm { margin: 0 auto 8px; cursor: default; }
.user-name { font-size: 13px; font-weight: 500; color: rgba(237,241,238,0.84); }
.user-role { margin-top: 2px; color: rgba(217,182,109,0.92); font-size: 11px; }

.footer-btn {
  display: flex; width: 100%; align-items: center; gap: 8px; padding: 9px 8px; border: none; border-radius: 6px;
  background: none; color: rgba(237,241,238,0.58); font: inherit; font-size: 13px; cursor: pointer; transition: all 0.18s ease;
  overflow: hidden; white-space: nowrap;
}
.footer-btn:hover { background: rgba(255,255,255,0.06); color: #f7faf7; }
.footer-btn.logout:hover { background: rgba(212, 96, 96, 0.12); color: #ffb4b4; }
.footer-btn svg { width: 16px; height: 16px; flex-shrink: 0; }

.collapse-btn {
  position: absolute; right: -12px; bottom: 18px; z-index: 10; display: flex; width: 24px; height: 24px;
  align-items: center; justify-content: center; border: 1px solid rgba(255,255,255,0.08); border-radius: 50%;
  background: #1f2421; color: rgba(237,241,238,0.72); cursor: pointer; transition: all 0.18s ease;
}
.collapse-btn:hover { background: #2b312d; color: #ffffff; }
.collapse-btn svg { width: 12px; height: 12px; }

.main-area { display: flex; min-width: 0; flex: 1; flex-direction: column; }
.topbar {
  display: flex; height: 56px; flex-shrink: 0; align-items: center; justify-content: space-between; padding: 0 28px;
  border-bottom: 1px solid rgba(255,255,255,0.06); background: rgba(23,27,25,0.9); backdrop-filter: blur(8px);
}
.breadcrumb { display: flex; align-items: center; gap: 8px; font-size: 13px; }
.breadcrumb-root { color: rgba(237,241,238,0.38); }
.breadcrumb-current { color: rgba(237,241,238,0.86); font-weight: 500; }
.topbar-right { display: flex; align-items: center; gap: 8px; }
.status-dot { width: 8px; height: 8px; border-radius: 50%; background: #66c39f; box-shadow: 0 0 0 4px rgba(102,195,159,0.16); }
.status-text { color: rgba(237,241,238,0.48); font-size: 12px; }
.page-content { flex: 1; overflow-y: auto; padding: 24px 28px 32px; background: #181d1a; }

.modal-overlay {
  position: fixed; inset: 0; z-index: 1000; display: flex; align-items: center; justify-content: center;
  background: rgba(0,0,0,0.56); backdrop-filter: blur(3px);
}
.modal {
  display: flex; max-width: 95vw; max-height: 90vh; flex-direction: column; border: 1px solid rgba(255,255,255,0.08);
  border-radius: 6px; background: #202622; box-shadow: 0 24px 64px rgba(0,0,0,0.36);
}
.modal-sm { width: 420px; }
.modal-header { display: flex; align-items: center; justify-content: space-between; padding: 20px 24px 16px; border-bottom: 1px solid rgba(255,255,255,0.06); }
.modal-title { color: #edf1ee; font-size: 15px; font-weight: 600; }
.modal-close {
  display: inline-flex; width: 26px; height: 26px; align-items: center; justify-content: center;
  border: none; border-radius: 6px; background: transparent; color: rgba(237,241,238,0.52); font-size: 14px; cursor: pointer;
}
.modal-close:hover { background: rgba(255,255,255,0.08); color: #ffffff; }
.modal-body { flex: 1; overflow-y: auto; padding: 20px 24px; }
.modal-footer { display: flex; justify-content: flex-end; gap: 10px; padding: 16px 24px; border-top: 1px solid rgba(255,255,255,0.06); }
.form-field { display: flex; flex-direction: column; gap: 7px; margin-bottom: 14px; }
.form-field label { color: rgba(237,241,238,0.56); font-size: 12px; }
.required { color: #ff9186; }
.form-field input {
  padding: 10px 12px; border: 1px solid rgba(255,255,255,0.1); border-radius: 6px; background: rgba(255,255,255,0.04);
  color: #edf1ee; font: inherit; font-size: 13px; outline: none; transition: border-color 0.18s ease;
}
.form-field input:focus { border-color: rgba(90,182,159,0.4); }
.form-field input::placeholder { color: rgba(237,241,238,0.3); }

.btn {
  display: inline-flex; align-items: center; gap: 7px; padding: 9px 18px; border: 1px solid transparent; border-radius: 6px;
  font: inherit; font-size: 13px; font-weight: 500; cursor: pointer; transition: all 0.18s ease;
}
.btn-primary { background: #5ab69f; color: #0f1d16; }
.btn-primary:hover:not(:disabled) { background: #69c4ad; }
.btn-primary:disabled { opacity: 0.5; cursor: not-allowed; }
.btn-ghost { border-color: rgba(255,255,255,0.1); background: rgba(255,255,255,0.04); color: rgba(237,241,238,0.72); }
.btn-ghost:hover { background: rgba(255,255,255,0.08); }

.fade-slide-enter-active, .fade-slide-leave-active { transition: all 0.2s; }
.fade-slide-enter-from { opacity: 0; transform: translateX(-6px); }
.fade-slide-leave-to { opacity: 0; transform: translateX(-6px); }
.page-fade-enter-active, .page-fade-leave-active { transition: opacity 0.2s, transform 0.2s; }
.page-fade-enter-from { opacity: 0; transform: translateY(6px); }
.page-fade-leave-to { opacity: 0; }
</style>
