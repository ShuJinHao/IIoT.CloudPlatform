<template>
  <div class="niond-shell grid h-screen grid-cols-[260px_minmax(0,1fr)] gap-0 overflow-hidden bg-[var(--background)] p-4 text-[var(--foreground)] max-[980px]:grid-cols-[224px_minmax(0,1fr)]">
    <aside class="flex min-h-0 flex-col rounded-l-[30px] border border-r-0 border-[var(--border)] bg-[var(--sidebar)] px-5 py-7 shadow-[var(--shadow-sm)]">
      <router-link to="/" class="mb-9 flex items-center gap-3 text-[var(--sidebar-foreground)]">
        <span class="grid size-10 place-items-center rounded-[14px] bg-[#111827] text-[var(--primary)]">
          <Factory :size="21" :stroke-width="2.4" />
        </span>
        <span class="min-w-0">
          <strong class="block truncate text-[18px] font-extrabold leading-tight">{{ t('brand.name') }}</strong>
          <small class="block truncate text-[11px] font-semibold uppercase text-[var(--sidebar-muted)]">{{ t('brand.shortSubtitle') }}</small>
        </span>
      </router-link>

      <nav class="grid gap-2">
        <router-link
          v-for="item in visibleNavItems"
          :key="item.name"
          :to="item.path"
          class="group flex h-11 items-center gap-3 rounded-[12px] px-3 text-[13px] font-bold text-[#2f3542] transition-colors hover:bg-[#f0f3f4] dark:text-[#e6e6e6] dark:hover:bg-[#202024]"
          :class="isActive(item.path) ? 'bg-[var(--sidebar-active)] text-[#111827] shadow-[0_10px_22px_rgba(198,244,82,0.16)] hover:bg-[var(--sidebar-active)] dark:text-[#111827]' : ''"
        >
          <component :is="item.icon" :size="18" :stroke-width="2.3" class="shrink-0" />
          <span class="truncate">{{ t(item.labelKey) }}</span>
        </router-link>
      </nav>

      <div class="mt-auto space-y-4">
        <div class="rounded-[22px] bg-[#f2f5f6] p-4 dark:bg-[#202024]">
          <div class="mb-3 flex items-center gap-2 text-[12px] font-bold text-[#111827] dark:text-[#f5f5f4]">
            <span class="grid size-7 place-items-center rounded-full bg-[var(--primary)] text-[#111827]">
              <Sparkles :size="15" />
            </span>
            {{ t('brand.aiTitle') }}
          </div>
          <p class="mb-4 text-[12px] leading-5 text-[var(--muted-foreground)]">{{ t('brand.aiDesc') }}</p>
          <button
            class="h-9 w-full rounded-[12px] bg-[#111827] text-[12px] font-bold text-white transition-colors hover:bg-[#262f3f] disabled:cursor-not-allowed disabled:opacity-50"
            type="button"
            :disabled="!isAicopilotEntryConfigured"
            :title="aicopilotEntryTitle"
            @click="openAicopilot"
          >
            {{ t('common.openAssistant') }}
          </button>
        </div>

        <div class="flex items-center gap-3 rounded-[18px] bg-white p-3 shadow-[var(--shadow-sm)] dark:bg-[#18181b]">
          <div class="grid size-9 place-items-center rounded-full bg-[#f0f4f3] text-[13px] font-extrabold text-[#111827] dark:bg-[#27272a] dark:text-[#f5f5f4]">
            {{ avatarChar }}
          </div>
          <div class="min-w-0 flex-1">
            <div class="truncate text-[13px] font-extrabold text-[#111827] dark:text-[#f5f5f4]">{{ authStore.employeeNo || t('layout.userFallback') }}</div>
            <div class="truncate text-[11px] font-semibold text-[var(--muted-foreground)]">{{ displayRole }}</div>
          </div>
        </div>
      </div>
    </aside>

    <section class="flex min-w-0 flex-col overflow-hidden rounded-r-[30px] border border-[var(--border)] bg-[var(--background)]">
      <header class="flex h-[76px] shrink-0 items-center justify-between px-8">
        <div class="hidden h-11 min-w-[320px] items-center gap-3 rounded-[16px] bg-white px-4 text-[13px] font-semibold text-[var(--muted-foreground)] shadow-[var(--shadow-sm)] dark:bg-[#18181b] min-[980px]:flex">
          <Search :size="17" />
          <span>{{ t('common.searchPlaceholder') }}</span>
        </div>

        <div class="flex items-center gap-3">
          <div class="hidden items-center gap-2 rounded-full bg-white px-4 py-2 text-[12px] font-bold text-[#596273] shadow-[var(--shadow-sm)] dark:bg-[#18181b] dark:text-[#c4c4ca] min-[1180px]:flex">
            <span class="size-2 rounded-full bg-[#10a37f]"></span>
            {{ t('common.dataSyncNormal') }}
          </div>

          <button
            class="inline-flex h-10 items-center gap-2 rounded-[13px] border border-[var(--border)] bg-white px-3 text-[12px] font-extrabold text-[#111827] transition-colors hover:bg-[#f4f7f8] dark:bg-[#18181b] dark:text-[#f5f5f4] dark:hover:bg-[#202024]"
            type="button"
            :title="t('common.language')"
            @click="toggleLocale"
          >
            <Languages :size="17" />
            {{ currentLocale === 'zh-CN' ? 'EN' : '中' }}
          </button>

          <button
            class="grid size-10 place-items-center rounded-[13px] border border-[var(--border)] bg-white text-[#111827] transition-colors hover:bg-[#f4f7f8] dark:bg-[#18181b] dark:text-[#f5f5f4] dark:hover:bg-[#202024]"
            type="button"
            :title="mode === 'dark' ? t('layout.switchToLight') : t('layout.switchToDark')"
            @click="toggleTheme"
          >
            <Sun v-if="mode === 'dark'" :size="18" />
            <Moon v-else :size="18" />
          </button>

          <DropdownMenuRoot>
            <DropdownMenuTrigger as-child>
              <button class="grid size-10 place-items-center rounded-[13px] border border-[var(--border)] bg-white text-[13px] font-extrabold text-[#111827] transition-colors hover:bg-[#f4f7f8] dark:bg-[#18181b] dark:text-[#f5f5f4] dark:hover:bg-[#202024]" type="button">
                {{ avatarChar }}
              </button>
            </DropdownMenuTrigger>
            <DropdownMenuPortal>
              <DropdownMenuContent
                class="z-[80] w-[248px] rounded-[20px] border border-[var(--border)] bg-white p-2 shadow-[var(--shadow-lg)] will-change-[transform,opacity] dark:bg-[#18181b]"
                align="end"
                :side-offset="10"
              >
                <div class="flex items-center gap-3 p-3">
                  <div class="grid size-10 place-items-center rounded-full bg-[#f0f4f3] text-[13px] font-extrabold text-[#111827] dark:bg-[#27272a] dark:text-[#f5f5f4]">{{ avatarChar }}</div>
                  <div class="min-w-0">
                    <div class="truncate text-[14px] font-extrabold text-[#111827] dark:text-[#f5f5f4]">{{ authStore.employeeNo || t('layout.userFallback') }}</div>
                    <div class="truncate text-[12px] text-[var(--muted-foreground)]">{{ displayRole }}</div>
                  </div>
                </div>
                <DropdownMenuSeparator class="my-1 h-px bg-[var(--border)]" />
                <DropdownMenuItem class="flex cursor-pointer items-center gap-2 rounded-[12px] px-3 py-2 text-[13px] font-bold text-[#111827] outline-none hover:bg-[#f4f7f8] dark:text-[#f5f5f4] dark:hover:bg-[#202024]" @select="openPasswordModal">
                  <LockKeyhole :size="16" />
                  {{ t('layout.changePassword') }}
                </DropdownMenuItem>
                <DropdownMenuItem class="flex cursor-pointer items-center gap-2 rounded-[12px] px-3 py-2 text-[13px] font-bold text-[#ef4444] outline-none hover:bg-[rgba(239,68,68,0.1)]" @select="handleLogout">
                  <LogOut :size="16" />
                  {{ t('layout.logout') }}
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenuPortal>
          </DropdownMenuRoot>
        </div>
      </header>

      <main class="min-h-0 flex-1 overflow-y-auto px-8 pb-8">
        <router-view v-slot="{ Component }">
          <transition name="page-fade" mode="out-in">
            <component :is="Component" />
          </transition>
        </router-view>
      </main>
    </section>

    <Teleport to="body">
      <div v-if="showPasswordModal" class="fixed inset-0 z-[100] grid place-items-center bg-[rgba(17,24,39,0.38)] p-5 backdrop-blur-[2px]" @click.self="showPasswordModal = false">
        <div class="w-full max-w-[430px] rounded-[24px] border border-[var(--border)] bg-white p-6 shadow-[var(--shadow-xl)] dark:bg-[#18181b]">
          <div class="mb-5 flex items-center justify-between">
            <h2 class="text-[18px] font-extrabold text-[#111827] dark:text-[#f5f5f4]">{{ t('layout.changePassword') }}</h2>
            <button class="grid size-9 place-items-center rounded-[12px] text-[var(--muted-foreground)] hover:bg-[#f4f7f8] dark:hover:bg-[#202024]" type="button" @click="showPasswordModal = false">
              <X :size="18" />
            </button>
          </div>
          <div class="space-y-4">
            <label class="grid gap-2 text-[13px] font-bold text-[#596273] dark:text-[#c4c4ca]">
              {{ t('layout.currentPassword') }}
              <input v-model="passwordForm.current" class="h-11 rounded-[12px] border border-[var(--input)] bg-[#f7fafb] px-3 text-[#111827] outline-none focus:border-[#111827] focus:ring-2 focus:ring-[rgba(17,24,39,0.08)] dark:bg-[#202024] dark:text-[#f5f5f4]" type="password" :placeholder="t('layout.currentPasswordPlaceholder')" />
            </label>
            <label class="grid gap-2 text-[13px] font-bold text-[#596273] dark:text-[#c4c4ca]">
              {{ t('layout.newPassword') }}
              <input v-model="passwordForm.newPwd" class="h-11 rounded-[12px] border border-[var(--input)] bg-[#f7fafb] px-3 text-[#111827] outline-none focus:border-[#111827] focus:ring-2 focus:ring-[rgba(17,24,39,0.08)] dark:bg-[#202024] dark:text-[#f5f5f4]" type="password" :placeholder="t('layout.newPasswordPlaceholder')" />
            </label>
            <label class="grid gap-2 text-[13px] font-bold text-[#596273] dark:text-[#c4c4ca]">
              {{ t('layout.confirmPassword') }}
              <input v-model="passwordForm.confirm" class="h-11 rounded-[12px] border border-[var(--input)] bg-[#f7fafb] px-3 text-[#111827] outline-none focus:border-[#111827] focus:ring-2 focus:ring-[rgba(17,24,39,0.08)] dark:bg-[#202024] dark:text-[#f5f5f4]" type="password" :placeholder="t('layout.confirmPasswordPlaceholder')" />
            </label>
          </div>
          <div class="mt-6 flex justify-end gap-3">
            <button class="h-10 rounded-[12px] px-4 text-[13px] font-bold text-[#596273] hover:bg-[#f4f7f8] dark:text-[#c4c4ca] dark:hover:bg-[#202024]" type="button" @click="showPasswordModal = false">{{ t('common.cancel') }}</button>
            <button class="h-10 rounded-[12px] bg-[#111827] px-5 text-[13px] font-bold text-white hover:bg-[#262f3f] disabled:cursor-not-allowed disabled:opacity-50" type="button" :disabled="pwdSubmitting" @click="submitPassword">
              {{ pwdSubmitting ? t('layout.changeSubmitting') : t('common.confirm') }}
            </button>
          </div>
        </div>
      </div>
    </Teleport>
  </div>
</template>

<script setup lang="ts">
import { reactive, ref, computed, type Component } from 'vue';
import { useRouter, useRoute } from 'vue-router';
import { useI18n } from 'vue-i18n';
import {
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuPortal,
  DropdownMenuRoot,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from 'reka-ui';
import {
  BarChart3,
  ClipboardList,
  Factory,
  Gauge,
  Languages,
  LayoutDashboard,
  LockKeyhole,
  LogOut,
  Moon,
  Route,
  Search,
  ScrollText,
  Settings2,
  Sparkles,
  Sun,
  Users,
  X,
} from 'lucide-vue-next';
import { useAuthStore } from '../stores/auth';
import { Permissions } from '../types/permissions';
import { changePasswordApi } from '../api/identity';
import { useTheme } from '../composables/useTheme';
import { setAppLocale, type AppLocale } from '../i18n';

interface NavItem {
  name: string;
  path: string;
  labelKey: string;
  icon: Component;
  permission: string | null;
}

const router = useRouter();
const route = useRoute();
const authStore = useAuthStore();
const { t, locale } = useI18n();
const { mode, toggle: toggleTheme } = useTheme();
const aicopilotChallengeUrl = (import.meta.env.VITE_AICOPILOT_CHALLENGE_URL as string | undefined)?.trim() || '';

const navItems: NavItem[] = [
  { name: 'Dashboard', path: '/', labelKey: 'nav.dashboard', icon: LayoutDashboard, permission: null },
  { name: 'Employees', path: '/employees', labelKey: 'nav.employees', icon: Users, permission: Permissions.Employee.Read },
  { name: 'MasterDataProcesses', path: '/master-data/processes', labelKey: 'nav.processes', icon: Settings2, permission: Permissions.Process.Read },
  { name: 'Devices', path: '/devices', labelKey: 'nav.devices', icon: Factory, permission: Permissions.Device.Read },
  { name: 'Recipes', path: '/recipes', labelKey: 'nav.recipes', icon: ClipboardList, permission: Permissions.Recipe.Read },
  { name: 'PassStation', path: '/pass-station', labelKey: 'nav.passStation', icon: Route, permission: Permissions.Device.Read },
  { name: 'Capacity', path: '/capacity', labelKey: 'nav.capacity', icon: BarChart3, permission: Permissions.Device.Read },
  { name: 'DeviceLogs', path: '/device-logs', labelKey: 'nav.logs', icon: ScrollText, permission: Permissions.Device.Read },
  { name: 'Roles', path: '/roles', labelKey: 'nav.access', icon: Gauge, permission: Permissions.Role.Define },
];

const visibleNavItems = computed(() =>
  navItems.filter((item) => !item.permission || authStore.hasPermission(item.permission)),
);

const currentLocale = computed(() => locale.value as AppLocale);
const avatarChar = computed(() => authStore.employeeNo?.charAt(0)?.toUpperCase() || 'U');
const displayRole = computed(() => {
  if (!authStore.role) return t('layout.roleFallback');
  if (currentLocale.value === 'zh-CN' && authStore.role === 'Admin') return '管理员';
  return authStore.role;
});
const isAicopilotEntryConfigured = computed(() => aicopilotChallengeUrl.length > 0);
const aicopilotEntryTitle = computed(() =>
  isAicopilotEntryConfigured.value ? t('common.openAssistant') : t('common.assistantUnavailable'),
);

const toggleLocale = () => {
  setAppLocale(currentLocale.value === 'zh-CN' ? 'en-US' : 'zh-CN');
};

const isActive = (path: string) => {
  if (path === '/') return route.path === '/';
  return route.path.startsWith(path);
};

const openAicopilot = () => {
  if (!isAicopilotEntryConfigured.value) {
    alert(t('common.assistantUnavailable'));
    return;
  }
  window.location.assign(aicopilotChallengeUrl);
};

const handleLogout = () => {
  authStore.logout();
  router.push('/login');
};

const showPasswordModal = ref(false);
const pwdSubmitting = ref(false);
const passwordForm = reactive({ current: '', newPwd: '', confirm: '' });

const openPasswordModal = () => {
  showPasswordModal.value = true;
};

const submitPassword = async () => {
  if (!passwordForm.current || !passwordForm.newPwd || !passwordForm.confirm) {
    alert(t('layout.passwordRequired'));
    return;
  }
  if (passwordForm.newPwd !== passwordForm.confirm) {
    alert(t('layout.passwordMismatch'));
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
    alert(t('layout.passwordSuccess'));
  } catch {
    // Global HTTP interceptor handles the concrete error message.
  } finally {
    pwdSubmitting.value = false;
  }
};
</script>

<style scoped>
.page-fade-enter-active,
.page-fade-leave-active {
  transition: opacity 180ms ease, transform 180ms ease;
}

.page-fade-enter-from {
  opacity: 0;
  transform: translateY(6px);
}

.page-fade-leave-to {
  opacity: 0;
}
</style>
