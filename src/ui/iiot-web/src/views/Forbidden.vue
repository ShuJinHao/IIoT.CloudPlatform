<template>
  <div class="forbidden">
    <CardSurface class="forbidden__card" variant="elevated">
      <div class="forbidden__code">403</div>
      <div class="forbidden__title">权限不足</div>
      <p class="forbidden__desc">
        当前账号没有访问此页面的权限，请联系管理员调整角色。
      </p>
      <router-link to="/">
        <UiButton type="primary" size="large">
          返回首页
        </UiButton>
      </router-link>
    </CardSurface>
  </div>
</template>

<script setup lang="ts">
import CardSurface from '../components/layout/CardSurface.vue';
import UiButton from '../components/ui/UiButton.vue';
</script>

<style scoped>
.forbidden {
  display: flex;
  min-height: calc(100vh - 120px);
  align-items: center;
  justify-content: center;
  padding: var(--space-8);
  font-family: var(--font-sans);
  text-align: center;
}

.forbidden__card {
  display: flex;
  width: 100%;
  max-width: 520px;
  flex-direction: column;
  align-items: center;
  overflow: hidden;
  padding: var(--space-16) var(--space-12);
  background: var(--bg-1);
}

.forbidden__code {
  margin-bottom: var(--space-4);
  color: #111827;
  font-family: var(--font-mono);
  font-size: 100px;
  font-weight: var(--fw-strong);
  letter-spacing: 0;
  line-height: 1;
  opacity: 0.12;
}

.forbidden__title {
  margin-bottom: var(--space-3);
  color: var(--text-0);
  font-size: var(--fs-3xl);
  font-weight: var(--fw-strong);
  letter-spacing: 0;
}

.forbidden__desc {
  margin: 0 0 var(--space-8);
  color: var(--text-1);
  font-size: var(--fs-md);
  line-height: 1.7;
}
</style>
