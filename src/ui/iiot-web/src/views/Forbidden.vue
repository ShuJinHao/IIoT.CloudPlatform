<template>
  <div class="forbidden">
    <div class="code">403</div>
    <div class="title">无权访问</div>
    <div class="desc">您的账号当前缺少访问该页面所需的权限点，请联系系统管理员。</div>
    <router-link to="/" class="back-btn">返回首页</router-link>
  </div>
</template>

<style scoped>
.forbidden {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  min-height: 60vh;
  font-family: 'Noto Sans SC', sans-serif;
  text-align: center;
}
.code {
  font-family: 'Rajdhani', sans-serif;
  font-size: 96px;
  font-weight: 700;
  color: rgba(0,229,255,0.15);
  line-height: 1;
  margin-bottom: 16px;
  letter-spacing: 8px;
}
.title { font-size: 22px; font-weight: 500; color: rgba(255,255,255,0.7); margin-bottom: 12px; }
.desc { font-size: 13px; color: rgba(255,255,255,0.3); max-width: 340px; line-height: 1.7; margin-bottom: 32px; }
.back-btn {
  padding: 10px 28px;
  background: rgba(0,229,255,0.1);
  border: 1px solid rgba(0,229,255,0.3);
  border-radius: 3px;
  color: #00e5ff;
  text-decoration: none;
  font-size: 13px;
  transition: all 0.2s;
}
.back-btn:hover { background: rgba(0,229,255,0.18); }
</style>
