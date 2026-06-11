<template>
  <main class="grid min-h-screen place-items-center overflow-auto bg-[var(--bg-2)] p-8 text-[var(--text-0)]">
    <div class="grid h-[min(820px,calc(100vh-64px))] min-h-[640px] w-[min(1600px,calc(100vw-128px))] grid-cols-[250px_minmax(0,1fr)] overflow-hidden rounded-[32px] bg-white shadow-[0_34px_90px_rgba(30,38,48,0.18)] max-[1120px]:w-[min(960px,calc(100vw-64px))] max-[1120px]:grid-cols-1 max-[1120px]:min-h-[620px]">
      <aside class="flex min-h-0 flex-col bg-[var(--bg-2)] px-7 py-8 max-[1120px]:hidden">
        <div class="mb-10 flex items-center gap-3">
          <div class="grid size-10 place-items-center rounded-[var(--radius-md)] bg-[var(--bg-1)] text-[var(--primary)]">
            <Factory :size="21" :stroke-width="2.4" />
          </div>
          <div>
            <div class="text-[var(--fs-xl)] font-[var(--fw-strong)] leading-tight">{{ t('brand.name') }}</div>
            <div class="text-[var(--fs-xs)] font-[var(--fw-semibold)] uppercase text-[var(--text-2)]">{{ t('brand.subtitle') }}</div>
          </div>
        </div>

        <div class="grid gap-2">
          <div class="flex h-11 items-center gap-3 rounded-[var(--radius-sm)] bg-[var(--primary)] px-3 text-[var(--fs-base)] font-[var(--fw-bold)] text-[var(--text-0)]">
            <LayoutDashboard :size="18" :stroke-width="2.4" />
            {{ t('login.sectionOverview') }}
          </div>
          <div class="flex h-11 items-center gap-3 rounded-[var(--radius-sm)] px-3 text-[var(--fs-base)] font-[var(--fw-bold)] text-[var(--text-1)]">
            <Activity :size="18" :stroke-width="2.2" />
            {{ t('login.sectionDevices') }}
          </div>
          <div class="flex h-11 items-center gap-3 rounded-[var(--radius-sm)] px-3 text-[var(--fs-base)] font-[var(--fw-bold)] text-[var(--text-1)]">
            <ClipboardList :size="18" :stroke-width="2.2" />
            {{ t('login.sectionTrace') }}
          </div>
          <div class="flex h-11 items-center gap-3 rounded-[var(--radius-sm)] px-3 text-[var(--fs-base)] font-[var(--fw-bold)] text-[var(--text-1)]">
            <Users :size="18" :stroke-width="2.2" />
            {{ t('login.sectionTeam') }}
          </div>
        </div>

        <div class="mt-auto rounded-[var(--radius-xl)] bg-[var(--bg-2)] p-5">
          <div class="mb-2 text-[var(--fs-base)] font-[var(--fw-strong)]">{{ t('login.sideTitle') }}</div>
          <p class="text-[var(--fs-sm)] leading-5 text-[var(--text-2)]">{{ t('login.sideDesc') }}</p>
        </div>
      </aside>

      <section class="grid min-w-0 grid-cols-[410px_minmax(0,1fr)] bg-[var(--bg-2)] p-8 max-[1120px]:grid-cols-1 max-[1120px]:bg-white max-[1120px]:p-7">
        <div class="relative flex min-h-0 flex-col justify-center rounded-l-[28px] bg-white px-9 py-10 max-[1120px]:mx-auto max-[1120px]:w-full max-[1120px]:max-w-[460px] max-[1120px]:rounded-[28px] max-[1120px]:px-7">
          <button
            class="absolute right-7 top-7 inline-flex h-10 items-center gap-2 rounded-[13px] border border-[rgba(17,24,39,0.10)] bg-white px-3 text-[var(--fs-sm)] font-[var(--fw-strong)] text-[var(--text-0)] shadow-[0_8px_22px_rgba(17,24,39,0.05)] transition hover:bg-[var(--bg-2)]"
            type="button"
            :title="t('common.language')"
            @click="toggleLocale"
          >
            <Languages :size="17" />
            {{ currentLocale === 'zh-CN' ? 'EN' : '中' }}
          </button>

          <div class="mb-10 flex items-center gap-3 min-[1121px]:hidden">
            <div class="grid size-10 place-items-center rounded-[var(--radius-md)] bg-[var(--bg-1)] text-[var(--primary)]">
              <Factory :size="21" :stroke-width="2.4" />
            </div>
            <div>
              <div class="text-[var(--fs-xl)] font-[var(--fw-strong)] leading-tight">{{ t('brand.name') }}</div>
              <div class="text-[var(--fs-xs)] font-[var(--fw-semibold)] uppercase text-[var(--text-2)]">{{ t('brand.subtitle') }}</div>
            </div>
          </div>

          <p class="mb-3 text-[var(--fs-sm)] font-[var(--fw-bold)] uppercase text-[var(--text-2)]">{{ t('login.eyebrow') }}</p>
          <h1 class="mb-3 text-[var(--fs-4xl)] font-[var(--fw-strong)] leading-tight tracking-[0]">{{ t('login.title') }}</h1>
          <p class="mb-9 max-w-[330px] text-[var(--fs-md)] leading-6 text-[var(--text-2)]">{{ t('login.desc') }}</p>

          <div class="space-y-5">
            <label class="grid gap-2">
              <span class="text-[var(--fs-base)] font-[var(--fw-bold)] text-[var(--text-1)]">{{ t('login.employeeNo') }}</span>
              <span class="flex h-[54px] items-center rounded-[var(--radius-md)] border border-[rgba(17,24,39,0.10)] bg-[var(--bg-2)] px-4 transition focus-within:border-[rgba(17,24,39,0.26)] focus-within:bg-white focus-within:shadow-[0_10px_24px_rgba(17,24,39,0.06)]">
                <UserRound class="mr-3 shrink-0 text-[var(--text-2)]" :size="18" />
                <input
                  v-model="loginForm.employeeNo"
                  class="min-w-0 flex-1 bg-transparent text-[var(--fs-lg)] font-[var(--fw-semibold)] text-[var(--text-0)] outline-none placeholder:text-[var(--text-2)]"
                  type="text"
                  :placeholder="t('login.employeePlaceholder')"
                  autocomplete="username"
                />
              </span>
            </label>

            <label class="grid gap-2">
              <span class="text-[var(--fs-base)] font-[var(--fw-bold)] text-[var(--text-1)]">{{ t('login.password') }}</span>
              <span class="flex h-[54px] items-center rounded-[var(--radius-md)] border border-[rgba(17,24,39,0.10)] bg-[var(--bg-2)] px-4 transition focus-within:border-[rgba(17,24,39,0.26)] focus-within:bg-white focus-within:shadow-[0_10px_24px_rgba(17,24,39,0.06)]">
                <LockKeyhole class="mr-3 shrink-0 text-[var(--text-2)]" :size="18" />
                <input
                  v-model="loginForm.password"
                  class="min-w-0 flex-1 bg-transparent text-[var(--fs-lg)] font-[var(--fw-semibold)] text-[var(--text-0)] outline-none placeholder:text-[var(--text-2)]"
                  :type="showPw ? 'text' : 'password'"
                  :placeholder="t('login.passwordPlaceholder')"
                  autocomplete="current-password"
                  @keyup.enter="handleLogin"
                />
                <button class="grid size-9 shrink-0 place-items-center rounded-[var(--radius-sm)] text-[var(--text-2)] hover:bg-[var(--bg-2)] hover:text-[var(--text-0)]" type="button" tabindex="-1" @click="showPw = !showPw">
                  <EyeOff v-if="showPw" :size="18" />
                  <Eye v-else :size="18" />
                </button>
              </span>
            </label>

            <div v-if="errorMsg" class="rounded-[var(--radius-md)] bg-[rgba(239,68,68,0.10)] px-4 py-3 text-[var(--fs-base)] font-[var(--fw-semibold)] text-[var(--error)]">
              {{ errorMsg }}
            </div>

            <button
              class="mt-2 h-[54px] w-full rounded-[var(--radius-md)] bg-[var(--primary)] text-[var(--fs-lg)] font-[var(--fw-strong)] text-[var(--primary-foreground)] transition hover:brightness-95 disabled:cursor-not-allowed disabled:opacity-55"
              type="button"
              :disabled="loading"
              @click="handleLogin"
            >
              {{ loading ? t('login.submitting') : t('login.submit') }}
            </button>

            <router-link
              class="flex h-12 items-center justify-center rounded-[15px] border border-[rgba(17,24,39,0.10)] bg-white text-[var(--fs-base)] font-[var(--fw-strong)] text-[var(--text-0)] transition hover:bg-[var(--bg-2)]"
              to="/downloads"
            >
              客户端版本中心
            </router-link>
          </div>

          <p class="mt-10 text-center text-[var(--fs-sm)] font-[var(--fw-semibold)] text-[var(--text-2)]">{{ t('login.version') }}</p>
        </div>

        <aside class="min-w-0 rounded-r-[28px] bg-[var(--bg-2)] p-6 max-[1120px]:hidden">
          <div class="mb-6 flex items-start justify-between">
            <div>
              <p class="mb-2 text-[var(--fs-sm)] font-[var(--fw-bold)] uppercase text-[var(--text-2)]">{{ t('login.previewSubtitle') }}</p>
              <h2 class="text-[30px] font-[var(--fw-strong)] leading-tight">{{ t('login.previewDate') }}</h2>
            </div>
            <div class="rounded-full bg-[var(--success-soft)] px-4 py-3 text-[var(--fs-base)] font-[var(--fw-strong)] text-[var(--success)]">
              <span class="mr-2 inline-block size-2 rounded-full bg-[var(--success)]"></span>
              {{ t('login.workshopStatus') }}
            </div>
          </div>

          <div class="grid grid-cols-4 gap-4 max-[1320px]:grid-cols-2">
            <div class="rounded-[var(--radius-lg)] bg-[var(--chart-1)] p-4">
              <Factory class="mb-6" :size="16" />
              <div class="text-[28px] font-[var(--fw-strong)]">36</div>
              <div class="mt-1 text-[var(--fs-xs)] font-[var(--fw-semibold)] text-[var(--text-2)]">{{ t('login.onlineDevices') }}</div>
            </div>
            <div class="rounded-[var(--radius-lg)] bg-[var(--chart-2)] p-4">
              <Activity class="mb-6" :size="16" />
              <div class="text-[28px] font-[var(--fw-strong)]">932</div>
              <div class="mt-1 text-[var(--fs-xs)] font-[var(--fw-semibold)] text-[var(--text-2)]">{{ t('login.todayOutput') }}</div>
            </div>
            <div class="rounded-[var(--radius-lg)] bg-[var(--chart-3)] p-4">
              <Gauge class="mb-6" :size="16" />
              <div class="text-[28px] font-[var(--fw-strong)]">98.6%</div>
              <div class="mt-1 text-[var(--fs-xs)] font-[var(--fw-semibold)] text-[var(--text-2)]">{{ t('login.passRate') }}</div>
            </div>
            <div class="rounded-[var(--radius-lg)] bg-[var(--chart-5)] p-4">
              <AlertTriangle class="mb-6" :size="16" />
              <div class="text-[28px] font-[var(--fw-strong)]">2</div>
              <div class="mt-1 text-[var(--fs-xs)] font-[var(--fw-semibold)] text-[var(--text-2)]">{{ t('login.alerts') }}</div>
            </div>
          </div>

          <div class="mt-6 grid grid-cols-[minmax(0,1fr)_230px] gap-5">
            <div class="rounded-[22px] bg-white p-5">
              <div class="mb-4 flex items-center justify-between">
                <h3 class="text-[var(--fs-xl)] font-[var(--fw-strong)]">{{ t('login.trend') }}</h3>
                <span class="rounded-[var(--radius-sm)] bg-[var(--bg-1)] px-3 py-2 text-[var(--fs-sm)] font-[var(--fw-strong)] text-white">{{ t('common.live') }}</span>
              </div>
              <div class="login-chart">
                <i style="height: 52%; background: var(--chart-1)"></i>
                <i style="height: 44%; background: var(--chart-3)"></i>
                <i style="height: 68%; background: var(--chart-1)"></i>
                <i style="height: 56%; background: var(--chart-3)"></i>
                <i style="height: 82%; background: var(--chart-1)"></i>
                <i style="height: 62%; background: var(--chart-3)"></i>
                <i style="height: 50%; background: var(--chart-1)"></i>
                <i style="height: 46%; background: var(--chart-3)"></i>
                <i style="height: 70%; background: var(--chart-1)"></i>
                <i style="height: 54%; background: var(--chart-3)"></i>
              </div>
            </div>

            <div class="space-y-5">
              <div class="rounded-[22px] bg-[var(--accent)] p-6 text-white">
                <Wifi class="mb-5" :size="20" />
                <div class="mb-2 text-[17px] font-[var(--fw-strong)]">{{ t('login.syncStatus') }}</div>
                <div class="mb-6 text-[var(--fs-base)] font-[var(--fw-semibold)] text-white/80">{{ t('login.gateway') }}</div>
                <span class="rounded-[var(--radius-sm)] bg-white/90 px-3 py-2 text-[var(--fs-sm)] font-[var(--fw-strong)] text-[var(--text-0)]">{{ t('login.syncNormal') }}</span>
              </div>
              <div class="rounded-[22px] bg-white p-5 max-[1400px]:hidden">
                <div class="mb-4 flex items-center gap-3">
                  <span class="size-3 rounded-full bg-[var(--warn)]"></span>
                  <div>
                    <div class="text-[var(--fs-lg)] font-[var(--fw-strong)]">{{ t('login.alertHint') }}</div>
                    <div class="mt-1 text-[var(--fs-sm)] font-[var(--fw-semibold)] text-[var(--text-2)]">{{ t('login.lineHint') }}</div>
                  </div>
                </div>
                <div class="h-px bg-[var(--border)]"></div>
                <div class="mt-4 flex items-center gap-3">
                  <span class="size-3 rounded-full bg-[var(--success)]"></span>
                  <div>
                    <div class="text-[var(--fs-lg)] font-[var(--fw-strong)]">{{ t('login.syncNormal') }}</div>
                    <div class="mt-1 text-[var(--fs-sm)] font-[var(--fw-semibold)] text-[var(--text-2)]">{{ t('login.gateway') }}</div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </aside>
      </section>
    </div>
  </main>
</template>

<script setup lang="ts">
import { computed, reactive, ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useI18n } from 'vue-i18n';
import {
  Activity,
  AlertTriangle,
  ClipboardList,
  Eye,
  EyeOff,
  Factory,
  Gauge,
  Languages,
  LayoutDashboard,
  LockKeyhole,
  UserRound,
  Users,
  Wifi,
} from 'lucide-vue-next';
import { loginApi } from '../api/auth';
import { useAuthStore } from '../stores/auth';
import type { LoginPayload } from '../api/auth';
import { setAppLocale, type AppLocale } from '../i18n';

const router = useRouter();
const route = useRoute();
const authStore = useAuthStore();
const { t, locale } = useI18n();

const loading = ref(false);
const showPw = ref(false);
const errorMsg = ref('');
const currentLocale = computed(() => locale.value as AppLocale);

const loginForm = reactive<LoginPayload>({
  employeeNo: '',
  password: '',
});

const toggleLocale = () => {
  setAppLocale(currentLocale.value === 'zh-CN' ? 'en-US' : 'zh-CN');
};

const getSafeReturnUrl = () => {
  const value = route.query.returnUrl;
  if (typeof value !== 'string') return null;
  const trimmed = value.trim();
  if (!trimmed.startsWith('/') || trimmed.startsWith('//') || trimmed.includes('\\')) return null;
  return trimmed;
};

const handleLogin = async () => {
  if (!loginForm.employeeNo || !loginForm.password) {
    errorMsg.value = t('login.required');
    return;
  }

  errorMsg.value = '';
  loading.value = true;

  try {
    const session = await loginApi(loginForm);
    authStore.setSession(session);
    const returnUrl = getSafeReturnUrl();
    if (returnUrl?.startsWith('/connect/')) {
      window.location.assign(returnUrl);
      return;
    }
    if (returnUrl) {
      await router.push(returnUrl);
      return;
    }
    await router.push('/');
  } catch {
    errorMsg.value = t('login.failed');
  } finally {
    loading.value = false;
  }
};
</script>

<style scoped>
.login-chart {
  display: grid;
  grid-template-columns: repeat(10, minmax(0, 1fr));
  align-items: end;
  gap: 10px;
  height: 130px;
  padding-top: 18px;
  background:
    linear-gradient(to bottom, transparent 24%, rgba(17, 24, 39, 0.07) 24%, rgba(17, 24, 39, 0.07) calc(24% + 1px), transparent calc(24% + 1px)),
    linear-gradient(to bottom, transparent 49%, rgba(17, 24, 39, 0.07) 49%, rgba(17, 24, 39, 0.07) calc(49% + 1px), transparent calc(49% + 1px)),
    linear-gradient(to bottom, transparent 74%, rgba(17, 24, 39, 0.07) 74%, rgba(17, 24, 39, 0.07) calc(74% + 1px), transparent calc(74% + 1px));
}

.login-chart i {
  display: block;
  min-height: 32px;
  border-radius: 999px 999px 8px 8px;
}
</style>
