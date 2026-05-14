import './styles/tokens.css';
import './styles/global.css';
import { createApp } from 'vue';
import { createPinia } from 'pinia';
import App from './App.vue';
import router from './router';
import permissionDirective from './directives/permission';
import { useAuthStore } from './stores/auth';
import { i18n } from './i18n';

const app = createApp(App);
const pinia = createPinia();

app.use(pinia);
app.use(router);
app.use(i18n);
app.directive('permission', permissionDirective);

const authStore = useAuthStore();

async function bootstrap() {
  await authStore.restoreFromStorage();
  app.mount('#app');
}

void bootstrap();
