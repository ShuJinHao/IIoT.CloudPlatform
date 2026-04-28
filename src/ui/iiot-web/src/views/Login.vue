<template>
  <div class="login-root">
    <div class="bg-grid"></div>
    <div class="accent-bar"></div>

    <div class="login-card">
      <div class="logo-block">
        <div class="logo-icon">
          <svg viewBox="0 0 40 40" fill="none" xmlns="http://www.w3.org/2000/svg">
            <rect x="2" y="2" width="16" height="16" rx="2" fill="#00e5ff" opacity="0.9"/>
            <rect x="22" y="2" width="16" height="16" rx="2" fill="#00e5ff" opacity="0.4"/>
            <rect x="2" y="22" width="16" height="16" rx="2" fill="#00e5ff" opacity="0.4"/>
            <rect x="22" y="22" width="16" height="16" rx="2" fill="#00e5ff" opacity="0.9"/>
          </svg>
        </div>
        <div class="logo-text">
          <span class="logo-main">IIoT</span>
          <span class="logo-sub">云平台</span>
        </div>
      </div>

      <div class="divider"></div>

      <h2 class="login-title">操作员登录</h2>
      <p class="login-desc">请输入工号和密码登录系统。</p>

      <div class="form">
        <div class="field-group" :class="{ 'field-focus': focusedField === 'no' }">
          <label class="field-label">工号</label>
          <div class="field-input-wrap">
            <svg class="field-icon" viewBox="0 0 20 20" fill="none">
              <circle cx="10" cy="7" r="3.5" stroke="currentColor" stroke-width="1.5"/>
              <path d="M3 17c0-3.314 3.134-6 7-6s7 2.686 7 6" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
            </svg>
            <input
              v-model="loginForm.employeeNo"
              type="text"
              placeholder="请输入工号"
              @focus="focusedField = 'no'"
              @blur="focusedField = ''"
              autocomplete="username"
            />
          </div>
        </div>

        <div class="field-group" :class="{ 'field-focus': focusedField === 'pw' }">
          <label class="field-label">密码</label>
          <div class="field-input-wrap">
            <svg class="field-icon" viewBox="0 0 20 20" fill="none">
              <rect x="4" y="9" width="12" height="9" rx="2" stroke="currentColor" stroke-width="1.5"/>
              <path d="M7 9V6.5a3 3 0 016 0V9" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
            </svg>
            <input
              v-model="loginForm.password"
              :type="showPw ? 'text' : 'password'"
              placeholder="请输入密码"
              @focus="focusedField = 'pw'"
              @blur="focusedField = ''"
              @keyup.enter="handleLogin"
              autocomplete="current-password"
            />
            <button class="eye-btn" @click="showPw = !showPw" type="button" tabindex="-1">
              <svg v-if="!showPw" viewBox="0 0 20 20" fill="none">
                <path d="M2 10s3-6 8-6 8 6 8 6-3 6-8 6-8-6-8-6z" stroke="currentColor" stroke-width="1.5"/>
                <circle cx="10" cy="10" r="2.5" stroke="currentColor" stroke-width="1.5"/>
              </svg>
              <svg v-else viewBox="0 0 20 20" fill="none">
                <path d="M3 3l14 14M8.46 8.52A2.5 2.5 0 0012.5 12.5M6 6.3C3.9 7.6 2 10 2 10s3 6 8 6c1.6 0 3-.5 4.2-1.3M10 4c4.4.3 8 6 8 6s-1 2-2.8 3.7" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
              </svg>
            </button>
          </div>
        </div>

        <div v-if="errorMsg" class="error-msg">
          <svg viewBox="0 0 16 16" fill="none">
            <circle cx="8" cy="8" r="6.5" stroke="#ff4d4f" stroke-width="1.2"/>
            <path d="M8 5v3.5M8 10.5v.5" stroke="#ff4d4f" stroke-width="1.5" stroke-linecap="round"/>
          </svg>
          {{ errorMsg }}
        </div>

        <button class="submit-btn" @click="handleLogin" :disabled="loading">
          <span v-if="!loading">登录</span>
          <span v-else class="loading-dots">
            <span></span><span></span><span></span>
          </span>
        </button>
      </div>

      <p class="footer-tip">工业物联网云平台 v1.0</p>
    </div>
  </div>
</template>

<script setup lang="ts">
import { reactive, ref } from 'vue';
import { useRouter } from 'vue-router';
import { loginApi } from '../api/auth';
import { useAuthStore } from '../stores/auth';
import type { LoginPayload } from '../api/auth';

const router = useRouter();
const authStore = useAuthStore();

const loading = ref(false);
const showPw = ref(false);
const focusedField = ref('');
const errorMsg = ref('');

const loginForm = reactive<LoginPayload>({
  employeeNo: '',
  password: ''
});

const handleLogin = async () => {
  if (!loginForm.employeeNo || !loginForm.password) {
    errorMsg.value = '请输入工号和密码。';
    return;
  }

  errorMsg.value = '';
  loading.value = true;

  try {
    const session = await loginApi(loginForm);
    authStore.setSession(session);
    router.push('/');
  } catch {
    errorMsg.value = '工号或密码不正确。';
  } finally {
    loading.value = false;
  }
};
</script>

<style scoped>
.login-root {
  min-height: 100vh;
  background: #0a0e1a;
  display: flex;
  align-items: center;
  justify-content: center;
  position: relative;
  overflow: hidden;
  font-family: 'Noto Sans SC', sans-serif;
}

.bg-grid {
  position: absolute;
  inset: 0;
  background-image:
    linear-gradient(rgba(0,229,255,0.04) 1px, transparent 1px),
    linear-gradient(90deg, rgba(0,229,255,0.04) 1px, transparent 1px);
  background-size: 48px 48px;
  pointer-events: none;
}

.accent-bar {
  position: absolute;
  left: 0;
  top: 0;
  bottom: 0;
  width: 3px;
  background: linear-gradient(to bottom, transparent, #00e5ff 30%, #0077ff 70%, transparent);
}

.login-card {
  position: relative;
  z-index: 1;
  width: 400px;
  padding: 44px 40px 36px;
  background: rgba(14, 20, 38, 0.95);
  border: 1px solid rgba(0, 229, 255, 0.15);
  border-radius: 4px;
  box-shadow:
    0 0 0 1px rgba(0,229,255,0.05),
    0 24px 64px rgba(0,0,0,0.6),
    inset 0 1px 0 rgba(255,255,255,0.04);
}

.logo-block {
  display: flex;
  align-items: center;
  gap: 14px;
  margin-bottom: 28px;
}

.logo-icon svg {
  width: 36px;
  height: 36px;
  filter: drop-shadow(0 0 8px rgba(0,229,255,0.5));
}

.logo-text {
  display: flex;
  flex-direction: column;
  line-height: 1;
}

.logo-main {
  font-family: 'Rajdhani', sans-serif;
  font-weight: 700;
  font-size: 26px;
  color: #00e5ff;
  letter-spacing: 2px;
}

.logo-sub {
  font-family: 'Rajdhani', sans-serif;
  font-weight: 500;
  font-size: 10px;
  color: rgba(0,229,255,0.5);
  letter-spacing: 3px;
  margin-top: 3px;
}

.divider {
  height: 1px;
  background: linear-gradient(to right, rgba(0,229,255,0.3), transparent);
  margin-bottom: 28px;
}

.login-title {
  font-size: 18px;
  font-weight: 500;
  color: #e8eaf0;
  margin: 0 0 6px;
}

.login-desc {
  font-size: 12px;
  color: rgba(255,255,255,0.3);
  margin: 0 0 32px;
}

.field-group {
  margin-bottom: 20px;
  transition: all 0.2s;
}

.field-label {
  display: block;
  font-size: 11px;
  font-weight: 500;
  color: rgba(255,255,255,0.35);
  letter-spacing: 1.5px;
  margin-bottom: 8px;
  transition: color 0.2s;
}

.field-group.field-focus .field-label {
  color: #00e5ff;
}

.field-input-wrap {
  position: relative;
  display: flex;
  align-items: center;
  background: rgba(255,255,255,0.04);
  border: 1px solid rgba(255,255,255,0.1);
  border-radius: 3px;
  transition: border-color 0.2s, box-shadow 0.2s;
}

.field-group.field-focus .field-input-wrap {
  border-color: rgba(0,229,255,0.5);
  box-shadow: 0 0 0 3px rgba(0,229,255,0.08);
}

.field-icon {
  width: 16px;
  height: 16px;
  color: rgba(255,255,255,0.25);
  margin-left: 14px;
  flex-shrink: 0;
  transition: color 0.2s;
}

.field-group.field-focus .field-icon {
  color: #00e5ff;
}

input {
  flex: 1;
  background: transparent;
  border: none;
  outline: none;
  padding: 13px 12px;
  font-size: 14px;
  color: #e8eaf0;
  font-family: 'Noto Sans SC', sans-serif;
}

input::placeholder {
  color: rgba(255,255,255,0.18);
}

.eye-btn {
  background: none;
  border: none;
  cursor: pointer;
  padding: 0 14px;
  color: rgba(255,255,255,0.25);
  display: flex;
  align-items: center;
  transition: color 0.2s;
}

.eye-btn:hover {
  color: #00e5ff;
}

.eye-btn svg {
  width: 16px;
  height: 16px;
}

.error-msg {
  display: flex;
  align-items: center;
  gap: 7px;
  font-size: 12px;
  color: #ff6b6b;
  margin-bottom: 16px;
  padding: 10px 12px;
  background: rgba(255,77,79,0.08);
  border: 1px solid rgba(255,77,79,0.2);
  border-radius: 3px;
}

.error-msg svg {
  width: 14px;
  height: 14px;
  flex-shrink: 0;
}

.submit-btn {
  width: 100%;
  padding: 14px;
  background: linear-gradient(135deg, #0077ff, #00bcd4);
  border: none;
  border-radius: 3px;
  color: #fff;
  font-size: 15px;
  font-weight: 500;
  font-family: 'Noto Sans SC', sans-serif;
  letter-spacing: 4px;
  cursor: pointer;
  margin-top: 8px;
  transition: opacity 0.2s, transform 0.1s, box-shadow 0.2s;
  box-shadow: 0 4px 20px rgba(0,119,255,0.3);
}

.submit-btn:hover:not(:disabled) {
  opacity: 0.9;
  box-shadow: 0 6px 24px rgba(0,119,255,0.45);
}

.submit-btn:active:not(:disabled) {
  transform: translateY(1px);
}

.submit-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
  box-shadow: none;
}

.loading-dots {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 5px;
}

.loading-dots span {
  width: 6px;
  height: 6px;
  background: white;
  border-radius: 50%;
  animation: dot-bounce 1.2s infinite ease-in-out;
}

.loading-dots span:nth-child(2) {
  animation-delay: 0.2s;
}

.loading-dots span:nth-child(3) {
  animation-delay: 0.4s;
}

@keyframes dot-bounce {
  0%, 80%, 100% {
    transform: scale(0.6);
    opacity: 0.4;
  }

  40% {
    transform: scale(1);
    opacity: 1;
  }
}

.footer-tip {
  text-align: center;
  font-size: 11px;
  color: rgba(255,255,255,0.12);
  margin: 28px 0 0;
  letter-spacing: 0.5px;
}
</style>
