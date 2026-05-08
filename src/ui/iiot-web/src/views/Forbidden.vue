<template>
  <div class="forbidden">
    <div class="forbidden__icon">
      <svg viewBox="0 0 64 64" width="56" height="56" fill="none">
        <circle cx="32" cy="32" r="28" stroke="currentColor" stroke-width="2" opacity="0.2"/>
        <rect x="22" y="28" width="20" height="14" rx="2" stroke="currentColor" stroke-width="2"/>
        <path d="M27 28v-4a5 5 0 0110 0v4" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>
        <circle cx="32" cy="35" r="1.5" fill="currentColor"/>
      </svg>
    </div>
    <div class="forbidden__code">403</div>
    <div class="forbidden__title">无权访问</div>
    <p class="forbidden__desc">
      您的账号当前缺少访问该页面所需的权限点，请联系系统管理员申请授权。
    </p>
    <router-link to="/">
      <n-button type="primary" size="medium">
        <template #icon>
          <svg viewBox="0 0 16 16" width="14" height="14" fill="none">
            <path d="M3 8l5-5M3 8l5 5M3 8h10" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
          </svg>
        </template>
        返回首页
      </n-button>
    </router-link>
  </div>
</template>

<script setup lang="ts">
import { NButton } from 'naive-ui';
</script>

<style scoped>
.forbidden {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  min-height: 60vh;
  font-family: var(--font-sans);
  text-align: center;
  padding: var(--space-8);
}
.forbidden__icon {
  color: var(--brand);
  opacity: 0.6;
  margin-bottom: var(--space-4);
}
.forbidden__code {
  font-size: 96px;
  font-weight: var(--fw-bold);
  color: var(--brand);
  opacity: 0.18;
  line-height: 1;
  margin-bottom: var(--space-4);
  letter-spacing: 8px;
  font-family: var(--font-mono);
}
.forbidden__title {
  font-size: var(--fs-2xl);
  font-weight: var(--fw-semibold);
  color: var(--text-0);
  margin-bottom: var(--space-3);
}
.forbidden__desc {
  font-size: var(--fs-md);
  color: var(--text-1);
  max-width: 360px;
  line-height: 1.7;
  margin: 0 0 var(--space-8);
}
</style>
