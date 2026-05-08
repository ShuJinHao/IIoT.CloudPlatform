<template>
  <div class="login-root">
    <div class="bg-grid" aria-hidden="true"></div>
    <div class="bg-glow bg-glow--cyan" aria-hidden="true"></div>
    <div class="bg-glow bg-glow--violet" aria-hidden="true"></div>

    <div class="login-card">
      <div class="logo-block">
        <div class="logo-icon">
          <svg viewBox="0 0 40 40" fill="none" xmlns="http://www.w3.org/2000/svg">
            <rect x="2" y="2" width="16" height="16" rx="3" fill="#0891b2" opacity="0.92"/>
            <rect x="22" y="2" width="16" height="16" rx="3" fill="#6366f1" opacity="0.78"/>
            <rect x="2" y="22" width="16" height="16" rx="3" fill="#059669" opacity="0.72"/>
            <rect x="22" y="22" width="16" height="16" rx="3" fill="#0891b2" opacity="0.85"/>
          </svg>
        </div>
        <div class="logo-text">
          <span class="logo-main">IIoT 云平台</span>
          <span class="logo-sub">INDUSTRIAL IOT CLOUD</span>
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
            <circle cx="8" cy="8" r="6.5" stroke="currentColor" stroke-width="1.4"/>
            <path d="M8 5v3.5M8 10.5v.5" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
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
  password: '',
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
  background: var(--bg-0);
  display: flex;
  align-items: center;
  justify-content: center;
  position: relative;
  overflow: hidden;
  font-family: var(--font-sans);
}

/* === 背景装饰：极淡网格 + 两团径向光晕 === */
.bg-grid {
  position: absolute;
  inset: 0;
  background-image:
    linear-gradient(rgba(15, 23, 42, 0.04) 1px, transparent 1px),
    linear-gradient(90deg, rgba(15, 23, 42, 0.04) 1px, transparent 1px);
  background-size: 56px 56px;
  pointer-events: none;
  mask-image: radial-gradient(ellipse at center, black 30%, transparent 80%);
  -webkit-mask-image: radial-gradient(ellipse at center, black 30%, transparent 80%);
}
.bg-glow {
  position: absolute;
  pointer-events: none;
  border-radius: 50%;
  filter: blur(80px);
  opacity: 0.5;
}
.bg-glow--cyan {
  width: 480px;
  height: 480px;
  background: radial-gradient(circle, rgba(8, 145, 178, 0.18), transparent 60%);
  top: -120px;
  left: 10vw;
}
.bg-glow--violet {
  width: 420px;
  height: 420px;
  background: radial-gradient(circle, rgba(99, 102, 241, 0.12), transparent 60%);
  bottom: -100px;
  right: 8vw;
}

/* === 登录卡 === */
.login-card {
  position: relative;
  z-index: 1;
  width: 420px;
  padding: 44px 40px 36px;
  background: var(--bg-1);
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  box-shadow: var(--shadow-lg);
}

/* === Logo === */
.logo-block {
  display: flex;
  align-items: center;
  gap: 14px;
  margin-bottom: 28px;
}
.logo-icon svg {
  width: 36px;
  height: 36px;
}
.logo-text {
  display: flex;
  flex-direction: column;
  line-height: 1.1;
}
.logo-main {
  font-weight: 700;
  font-size: 20px;
  color: var(--text-0);
  letter-spacing: 0.3px;
}
.logo-sub {
  font-family: var(--font-mono);
  font-weight: 500;
  font-size: 10px;
  color: var(--text-2);
  letter-spacing: 2px;
  margin-top: 4px;
}

.divider {
  height: 1px;
  background: var(--border);
  margin-bottom: 28px;
}

/* === 标题 === */
.login-title {
  font-size: 20px;
  font-weight: 600;
  color: var(--text-0);
  margin: 0 0 6px;
  letter-spacing: -0.2px;
}
.login-desc {
  font-size: 13px;
  color: var(--text-1);
  margin: 0 0 28px;
}

/* === 表单字段 === */
.field-group {
  margin-bottom: 18px;
}
.field-label {
  display: block;
  font-size: 12px;
  font-weight: 500;
  color: var(--text-1);
  letter-spacing: 0.5px;
  margin-bottom: 8px;
  transition: color var(--motion-fast);
}
.field-group.field-focus .field-label {
  color: var(--brand);
}

.field-input-wrap {
  position: relative;
  display: flex;
  align-items: center;
  background: var(--bg-1);
  border: 1px solid var(--border-strong);
  border-radius: var(--radius-sm);
  transition: border-color var(--motion-fast), box-shadow var(--motion-fast);
}
.field-group.field-focus .field-input-wrap {
  border-color: var(--brand);
  box-shadow: 0 0 0 3px var(--brand-soft);
}

.field-icon {
  width: 16px;
  height: 16px;
  color: var(--text-2);
  margin-left: 14px;
  flex-shrink: 0;
  transition: color var(--motion-fast);
}
.field-group.field-focus .field-icon {
  color: var(--brand);
}

input {
  flex: 1;
  background: transparent;
  border: none;
  outline: none;
  padding: 12px;
  font-size: 14px;
  color: var(--text-0);
  font-family: var(--font-sans);
}
input::placeholder {
  color: var(--text-2);
}

.eye-btn {
  background: none;
  border: none;
  cursor: pointer;
  padding: 0 14px;
  color: var(--text-2);
  display: flex;
  align-items: center;
  transition: color var(--motion-fast);
}
.eye-btn:hover {
  color: var(--brand);
}
.eye-btn svg {
  width: 16px;
  height: 16px;
}

/* === 错误信息 === */
.error-msg {
  display: flex;
  align-items: center;
  gap: 7px;
  font-size: 13px;
  color: var(--error);
  margin-bottom: 16px;
  padding: 10px 12px;
  background: var(--error-soft);
  border: 1px solid rgba(220, 38, 38, 0.2);
  border-radius: var(--radius-sm);
}
.error-msg svg {
  width: 14px;
  height: 14px;
  flex-shrink: 0;
}

/* === 提交按钮 === */
.submit-btn {
  width: 100%;
  padding: 13px;
  background: var(--brand);
  border: none;
  border-radius: var(--radius-sm);
  color: #ffffff;
  font-size: 14px;
  font-weight: 600;
  font-family: var(--font-sans);
  letter-spacing: 1px;
  cursor: pointer;
  margin-top: 8px;
  transition: background-color var(--motion-fast), transform 0.1s, box-shadow var(--motion-fast);
}
.submit-btn:hover:not(:disabled) {
  background: var(--brand-hover);
  box-shadow: 0 4px 12px rgba(8, 145, 178, 0.25);
}
.submit-btn:active:not(:disabled) {
  transform: translateY(1px);
}
.submit-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

/* === 加载点点 === */
.loading-dots {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 5px;
}
.loading-dots span {
  width: 6px;
  height: 6px;
  background: #ffffff;
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

/* === 底部提示 === */
.footer-tip {
  text-align: center;
  font-size: 11px;
  color: var(--text-2);
  margin: 28px 0 0;
  letter-spacing: 0.5px;
  font-family: var(--font-mono);
}
</style>
