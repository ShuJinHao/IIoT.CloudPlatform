import { createApp } from 'vue';
import { createPinia } from 'pinia';
import App from './App.vue';
import router from './router';
import permissionDirective from './directives/permission';
import { useAuthStore } from './stores/auth';

const app = createApp(App);
const pinia = createPinia();

app.use(pinia);
app.use(router);

// 注册全局 v-permission 指令
app.directive('permission', permissionDirective);

// 🌟 应用启动时：从 localStorage 恢复用户状态（处理刷新页面场景）
const authStore = useAuthStore();
authStore.restoreFromStorage();

app.mount('#app');