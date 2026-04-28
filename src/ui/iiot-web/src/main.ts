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
app.directive('permission', permissionDirective);

const authStore = useAuthStore();

async function bootstrap() {
  await authStore.restoreFromStorage();
  app.mount('#app');
}

void bootstrap();
