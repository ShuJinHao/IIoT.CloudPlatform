<template>
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
          <button
            class="grid size-9 shrink-0 place-items-center rounded-[var(--radius-sm)] text-[var(--text-2)] hover:bg-[var(--bg-2)] hover:text-[var(--text-0)]"
            type="button"
            :aria-label="showPw ? t('login.hidePassword') : t('login.showPassword')"
            @click="showPw = !showPw"
          >
            <EyeOff v-if="showPw" :size="18" />
            <Eye v-else :size="18" />
          </button>
        </span>
      </label>

      <div v-if="errorMsg" class="rounded-[var(--radius-md)] bg-[rgba(239,68,68,0.10)] px-4 py-3 text-[var(--fs-base)] font-[var(--fw-semibold)] text-[var(--error)]" role="alert">
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
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, reactive, ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useI18n } from 'vue-i18n';
import {
  Eye,
  EyeOff,
  Factory,
  Languages,
  LockKeyhole,
  UserRound,
} from 'lucide-vue-next';
import { AuthRequestError, loginApi } from '../../api/auth';
import type { LoginPayload } from '../../api/auth';
import { setAppLocale, type AppLocale } from '../../i18n';
import { useAuthStore } from '../../stores/auth';
import { getSafeLoginReturnUrl, navigateAfterLogin } from './loginNavigation';

const router = useRouter();
const route = useRoute();
const authStore = useAuthStore();
const { t, locale } = useI18n();
const loading = ref(false);
const showPw = ref(false);
const errorMsg = ref('');
const currentLocale = computed(() => locale.value as AppLocale);
const loginForm = reactive<LoginPayload>({ employeeNo: '', password: '' });

const toggleLocale = () => {
  setAppLocale(currentLocale.value === 'zh-CN' ? 'en-US' : 'zh-CN');
};

const resolveLoginErrorMessage = (error: unknown) => {
  if (!(error instanceof AuthRequestError)) return t('login.unknownFailed');

  switch (error.kind) {
    case 'invalid-credentials': return error.detail || t('login.failed');
    case 'network': return t('login.networkFailed');
    case 'timeout': return t('login.timeout');
    case 'rate-limited': return error.detail || t('login.rateLimited');
    case 'server': return t('login.serverUnavailable');
    case 'invalid-response': return t('login.invalidResponse');
    default: return error.detail || t('login.unknownFailed');
  }
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
    const returnUrl = getSafeLoginReturnUrl(route.query.returnUrl);
    await navigateAfterLogin(router, returnUrl);
  } catch (error) {
    errorMsg.value = resolveLoginErrorMessage(error);
  } finally {
    loading.value = false;
  }
};
</script>
