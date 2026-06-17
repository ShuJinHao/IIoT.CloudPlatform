<template>
  <main class="min-h-screen bg-[var(--bg-2)] px-6 py-6 text-[var(--text-0)] dark:bg-[var(--bg-2)] dark:text-[var(--text-0)]">
    <section class="mx-auto flex max-w-[1180px] items-center justify-between gap-4 pb-6">
      <div class="flex items-center gap-3">
        <div class="grid size-11 place-items-center rounded-[15px] bg-[var(--bg-1)] text-[var(--primary)]">
          <CloudDownload :size="22" :stroke-width="2.4" />
        </div>
        <div>
          <h1 class="text-[25px] font-[var(--fw-strong)] leading-tight tracking-[0]">客户端版本中心</h1>
          <p class="mt-1 text-[var(--fs-base)] font-[var(--fw-semibold)] text-[var(--text-2)] dark:text-[var(--text-2)]">公开页只展示宿主与插件版本，安装包必须登录云平台后按设备绑定生成。</p>
        </div>
      </div>
      <router-link class="rounded-[13px] bg-white px-4 py-2 text-[var(--fs-base)] font-[var(--fw-strong)] text-[var(--text-0)] shadow-[var(--shadow-sm)] transition-colors hover:bg-[var(--bg-2)]  dark:text-[var(--text-0)] dark:hover:bg-[var(--bg-3)]" to="/login">
        进入云平台
      </router-link>
    </section>

    <section class="mx-auto grid max-w-[1180px] grid-cols-[minmax(0,1fr)_320px] gap-6 max-[960px]:grid-cols-1">
      <div class="min-w-0 space-y-6">
        <section class="rounded-[var(--radius-xl)] bg-white p-6 shadow-[var(--shadow-sm)] ">
          <div class="mb-5 flex flex-wrap items-end justify-between gap-4">
            <div>
              <p class="text-[var(--fs-sm)] font-[var(--fw-strong)] uppercase text-[var(--text-2)]">Windows 通用宿主</p>
              <h2 class="mt-2 text-[28px] font-[var(--fw-strong)] leading-tight tracking-[0]">EdgeClient 版本</h2>
            </div>
            <div class="flex flex-wrap gap-3">
              <label class="grid gap-1 text-[var(--fs-sm)] font-[var(--fw-bold)] text-[var(--text-1)]">
                Channel
                <input v-model.trim="channel" class="h-10 w-[132px] rounded-[var(--radius-sm)] border border-[var(--border)] bg-[var(--bg-2)] px-3 text-[var(--fs-base)] font-[var(--fw-strong)] text-[var(--text-0)] outline-none focus:border-[var(--text-0)] dark:bg-[var(--bg-2)] dark:text-[var(--text-0)]" />
              </label>
              <label class="grid gap-1 text-[var(--fs-sm)] font-[var(--fw-bold)] text-[var(--text-1)]">
                Runtime
                <input v-model.trim="targetRuntime" class="h-10 w-[132px] rounded-[var(--radius-sm)] border border-[var(--border)] bg-[var(--bg-2)] px-3 text-[var(--fs-base)] font-[var(--fw-strong)] text-[var(--text-0)] outline-none focus:border-[var(--text-0)] dark:bg-[var(--bg-2)] dark:text-[var(--text-0)]" />
              </label>
              <button class="mt-5 inline-flex h-10 items-center gap-2 rounded-[var(--radius-sm)] bg-[var(--bg-1)] px-4 text-[var(--fs-base)] font-[var(--fw-strong)] text-white transition-colors hover:bg-[var(--bg-3)] disabled:cursor-not-allowed disabled:opacity-60" type="button" :disabled="loading" @click="loadCatalog">
                <RefreshCcw :size="16" :class="loading ? 'animate-spin' : ''" />
                刷新
              </button>
            </div>
          </div>

          <div v-if="loading" class="grid min-h-[260px] place-items-center rounded-[var(--radius-lg)] border border-dashed border-[var(--border)] bg-[var(--bg-2)] text-[var(--fs-base)] font-[var(--fw-bold)] text-[var(--text-2)] dark:bg-[var(--bg-2)]">
            正在读取发布目录...
          </div>

          <div v-else-if="errorMessage" class="rounded-[var(--radius-lg)] border border-[var(--error)] bg-[var(--bg-2)] p-5 text-[var(--error)]">
            <div class="mb-2 flex items-center gap-2 text-[var(--fs-md)] font-[var(--fw-strong)]">
              <AlertTriangle :size="18" />
              读取失败
            </div>
            <p class="text-[var(--fs-base)] font-[var(--fw-semibold)]">{{ errorMessage }}</p>
          </div>

          <div v-else-if="selectedHostPackage" class="grid grid-cols-[minmax(0,1fr)_220px] gap-5 rounded-[20px] border border-[var(--border)] bg-[var(--bg-2)] p-5 dark:bg-[var(--bg-2)] max-[760px]:grid-cols-1">
            <div class="min-w-0">
              <div class="mb-5 flex flex-wrap items-center gap-2">
                <span class="rounded-full bg-[var(--primary)] px-3 py-1 text-[var(--fs-sm)] font-[var(--fw-strong)] text-[var(--text-0)]">v{{ selectedHostPackage.version }}</span>
                <span class="rounded-full bg-white px-3 py-1 text-[var(--fs-sm)] font-[var(--fw-bold)] text-[var(--text-2)]  dark:text-[var(--text-2)]">{{ selectedHostPackage.channel }} / {{ selectedHostPackage.targetRuntime }}</span>
                <span class="rounded-full bg-white px-3 py-1 text-[var(--fs-sm)] font-[var(--fw-bold)] text-[var(--text-2)]  dark:text-[var(--text-2)]">Host API {{ selectedHostPackage.hostApiVersion }}</span>
              </div>

              <dl class="grid grid-cols-2 gap-4 text-[var(--fs-base)] max-[680px]:grid-cols-1">
                <div>
                  <dt class="mb-1 font-[var(--fw-bold)] text-[var(--text-2)]">发布时间</dt>
                  <dd class="font-[var(--fw-strong)]">{{ formatDate(selectedHostPackage.publishedAtUtc) }}</dd>
                </div>
                <div>
                  <dt class="mb-1 font-[var(--fw-bold)] text-[var(--text-2)]">包大小</dt>
                  <dd class="font-[var(--fw-strong)]">{{ formatBytes(selectedHostPackage.packageSize) }}</dd>
                </div>
                <div class="col-span-2 min-w-0 max-[680px]:col-span-1">
                  <dt class="mb-1 font-[var(--fw-bold)] text-[var(--text-2)]">SHA256</dt>
                  <dd class="break-all font-mono text-[var(--fs-sm)] font-[var(--fw-bold)] text-[var(--text-1)] dark:text-[var(--text-2)]">{{ selectedHostPackage.sha256 }}</dd>
                </div>
              </dl>

              <p v-if="selectedHostPackage.releaseNotes" class="mt-5 rounded-[var(--radius-md)] bg-white p-4 text-[var(--fs-base)] font-[var(--fw-semibold)] leading-6 text-[var(--text-2)]  dark:text-[var(--text-2)]">
                {{ selectedHostPackage.releaseNotes }}
              </p>
            </div>

            <div class="flex flex-col justify-between gap-4 rounded-[var(--radius-lg)] bg-white p-4 ">
              <div>
                <div class="mb-2 grid size-11 place-items-center rounded-[var(--radius-md)] bg-[var(--bg-1)] text-[var(--primary)]">
                  <Package :size="22" />
                </div>
                <p class="text-[var(--fs-base)] font-[var(--fw-bold)] text-[var(--text-1)]">这里只显示当前版本信息。首装包登录云平台后按设备身份生成，后续更新由现场 Launcher 自动检测。</p>
              </div>
              <div class="rounded-[13px] bg-[var(--primary)] px-4 py-3 text-center text-[var(--fs-sm)] font-[var(--fw-strong)] text-[var(--text-0)]">
                网页不提供客户端更新下载
              </div>
            </div>
          </div>

          <div v-else class="grid min-h-[260px] place-items-center rounded-[var(--radius-lg)] border border-dashed border-[var(--border)] bg-[var(--bg-2)] p-8 text-center dark:bg-[var(--bg-2)]">
            <div>
              <PackageX class="mx-auto mb-3 text-[var(--text-2)]" :size="34" />
              <h3 class="text-[17px] font-[var(--fw-strong)]">暂无已发布宿主包</h3>
              <p class="mt-2 text-[var(--fs-base)] font-[var(--fw-semibold)] text-[var(--text-2)] dark:text-[var(--text-2)]">请先在后台登记 {{ channel || 'stable' }} / {{ targetRuntime || 'win-x64' }} 的通用宿主版本。</p>
            </div>
          </div>
        </section>

        <section class="space-y-4">
          <div class="flex items-center justify-between gap-4">
            <div>
              <h2 class="text-[var(--fs-2xl)] font-[var(--fw-strong)] tracking-[0]">插件 catalog</h2>
              <p class="mt-1 text-[var(--fs-base)] font-[var(--fw-semibold)] text-[var(--text-2)] dark:text-[var(--text-2)]">这里只展示版本与兼容窗口，实际插件下载仍由设备 Launcher 完成。</p>
            </div>
            <span class="rounded-full bg-white px-3 py-2 text-[var(--fs-sm)] font-[var(--fw-strong)] text-[var(--text-2)] shadow-[var(--shadow-sm)]  dark:text-[var(--text-2)]">{{ catalog?.plugins.length ?? 0 }} 个插件</span>
          </div>

          <div v-if="pluginCards.length" class="grid grid-cols-2 gap-4 max-[760px]:grid-cols-1">
            <article v-for="plugin in pluginCards" :key="plugin.moduleId" class="rounded-[20px] bg-white p-5 shadow-[var(--shadow-sm)] ">
              <div class="mb-4 flex items-start justify-between gap-3">
                <div class="min-w-0">
                  <h3 class="truncate text-[17px] font-[var(--fw-strong)]">{{ plugin.displayName || plugin.moduleId }}</h3>
                  <p class="mt-1 truncate text-[var(--fs-sm)] font-[var(--fw-bold)] text-[var(--text-2)]">{{ plugin.moduleId }}</p>
                </div>
                <span class="shrink-0 rounded-full bg-[var(--bg-2)] px-3 py-1 text-[var(--fs-sm)] font-[var(--fw-strong)] text-[var(--text-0)] dark:bg-[var(--bg-2)] dark:text-[var(--text-0)]">v{{ plugin.version.version }}</span>
              </div>

              <div class="grid gap-3 text-[var(--fs-sm)] font-[var(--fw-bold)] text-[var(--text-2)]">
                <div class="flex items-center justify-between gap-3">
                  <span>Host API</span>
                  <strong class="text-[var(--text-0)] dark:text-[var(--text-0)]">{{ plugin.version.hostApiVersion }}</strong>
                </div>
                <div class="flex items-center justify-between gap-3">
                  <span>兼容宿主</span>
                  <strong class="text-right text-[var(--text-0)] dark:text-[var(--text-0)]">{{ plugin.version.minHostVersion }} - {{ plugin.version.maxHostVersion }}</strong>
                </div>
                <div class="flex items-center justify-between gap-3">
                  <span>包大小</span>
                  <strong class="text-[var(--text-0)] dark:text-[var(--text-0)]">{{ formatBytes(plugin.version.packageSize) }}</strong>
                </div>
                <div class="flex items-center justify-between gap-3">
                  <span>发布时间</span>
                  <strong class="text-[var(--text-0)] dark:text-[var(--text-0)]">{{ formatDate(plugin.version.publishedAtUtc) }}</strong>
                </div>
              </div>

              <p v-if="plugin.description || plugin.version.releaseNotes" class="mt-4 line-clamp-3 text-[var(--fs-base)] font-[var(--fw-semibold)] leading-6 text-[var(--text-1)] dark:text-[var(--text-2)]">
                {{ plugin.description || plugin.version.releaseNotes }}
              </p>
            </article>
          </div>

          <div v-else class="rounded-[20px] border border-dashed border-[var(--border)] bg-white p-8 text-center text-[var(--fs-base)] font-[var(--fw-semibold)] text-[var(--text-2)]  dark:text-[var(--text-2)]">
            当前 channel/runtime 暂无已发布插件。
          </div>
        </section>
      </div>

      <aside class="space-y-4">
        <section class="rounded-[22px] bg-white p-5 shadow-[var(--shadow-sm)] ">
          <div class="mb-4 flex items-center gap-2 text-[var(--fs-md)] font-[var(--fw-strong)]">
            <ShieldCheck :size="18" />
            安装边界
          </div>
          <ul class="space-y-3 text-[var(--fs-base)] font-[var(--fw-semibold)] leading-6 text-[var(--text-1)] dark:text-[var(--text-2)]">
            <li>公开页面只展示版本，不提供安装包下载。</li>
            <li>插件安装需要设备身份，仍走 ClientCode → bootstrap → DeviceId。</li>
            <li>启动密钥、设备 token、内部发布状态不会在这里展示。</li>
          </ul>
        </section>

        <section class="rounded-[22px] bg-[var(--bg-1)] p-5 text-white shadow-[var(--shadow-sm)]">
          <div class="mb-3 text-[var(--fs-md)] font-[var(--fw-strong)]">当前目录</div>
          <div class="space-y-2 text-[var(--fs-base)] font-[var(--fw-bold)] text-[var(--text-2)]">
            <div class="flex items-center justify-between gap-3">
              <span>Channel</span>
              <strong class="text-white">{{ catalog?.channel ?? channel }}</strong>
            </div>
            <div class="flex items-center justify-between gap-3">
              <span>Runtime</span>
              <strong class="text-white">{{ catalog?.targetRuntime ?? targetRuntime }}</strong>
            </div>
            <div class="flex items-center justify-between gap-3">
              <span>生成时间</span>
              <strong class="text-right text-white">{{ formatDate(catalog?.generatedAtUtc) }}</strong>
            </div>
          </div>
        </section>
      </aside>
    </section>
  </main>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import {
  AlertTriangle,
  CloudDownload,
  Package,
  PackageX,
  RefreshCcw,
  ShieldCheck,
} from 'lucide-vue-next';
import {
  getPublicClientDownloadsApi,
  type PublicClientDownloadCatalogDto,
} from '../api/publicDownloads';

const route = useRoute();
const router = useRouter();

const readQuery = (key: string, fallback: string) => {
  const value = route.query[key];
  return typeof value === 'string' && value.trim() ? value.trim() : fallback;
};

const channel = ref(readQuery('channel', 'stable'));
const targetRuntime = ref(readQuery('targetRuntime', 'win-x64'));
const catalog = ref<PublicClientDownloadCatalogDto | null>(null);
const loading = ref(false);
const errorMessage = ref('');
const selectedHostPackage = computed(() => {
  const versions = catalog.value?.host.versions ?? [];
  return versions.find((version) => version.status.toLowerCase() === 'published') ?? versions[0] ?? null;
});
const pluginCards = computed(() => {
  return (catalog.value?.plugins ?? [])
    .flatMap((plugin) => {
      const version =
        plugin.versions.find((entry) => entry.status.toLowerCase() === 'published') ?? plugin.versions[0];
      return version ? [{ ...plugin, version }] : [];
    });
});

const loadCatalog = async () => {
  loading.value = true;
  errorMessage.value = '';
  try {
    catalog.value = await getPublicClientDownloadsApi({
      channel: channel.value,
      targetRuntime: targetRuntime.value,
    });
    await router.replace({
      query: {
        channel: channel.value || 'stable',
        targetRuntime: targetRuntime.value || 'win-x64',
      },
    });
  } catch (error) {
    console.error(error);
    errorMessage.value = '无法读取客户端发布目录，请确认云端服务和发布数据。';
  } finally {
    loading.value = false;
  }
};

const formatBytes = (value?: number | null) => {
  if (!value || value <= 0) return '-';
  const units = ['B', 'KB', 'MB', 'GB'];
  let size = value;
  let unitIndex = 0;
  while (size >= 1024 && unitIndex < units.length - 1) {
    size /= 1024;
    unitIndex += 1;
  }
  return `${size.toFixed(unitIndex === 0 ? 0 : 1)} ${units[unitIndex]}`;
};

const formatDate = (value?: string | null) => {
  if (!value) return '-';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '-';
  return new Intl.DateTimeFormat('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  }).format(date);
};

onMounted(loadCatalog);
</script>
