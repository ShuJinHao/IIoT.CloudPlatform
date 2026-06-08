<template>
  <NiondDataPage
    page-key="clientReleases"
    title="客户端下载中心"
    subtitle="维护通用宿主、工序插件 catalog，并查看设备当前版本与最新版本差异"
  >
    <template #actions>
      <UiButton v-if="canManage" type="primary" size="small" @click="openHostModal">
        <Plus :size="15" />
        登记宿主
      </UiButton>
      <UiButton v-if="canManage" type="info" size="small" secondary @click="openPluginModal">
        <PackagePlus :size="15" />
        登记插件
      </UiButton>
    </template>

    <template #toolbar>
      <NiondToolbar>
        <div class="release-toolbar">
          <div class="release-tabs">
            <button
              class="release-tab"
              :class="{ 'is-active': activeView === 'catalog' }"
              type="button"
              @click="activeView = 'catalog'"
            >
              <CloudDownload :size="16" />
              下载中心
            </button>
            <button
              class="release-tab"
              :class="{ 'is-active': activeView === 'inventory' }"
              type="button"
              @click="activeView = 'inventory'"
            >
              <MonitorCheck :size="16" />
              版本盘点
            </button>
          </div>
          <div class="release-filters">
            <UiInput v-model:value="channel" size="small" placeholder="channel" @keyup.enter="refresh" />
            <UiInput v-model:value="targetRuntime" size="small" placeholder="target runtime" @keyup.enter="refresh" />
            <UiInput
              v-if="activeView === 'inventory'"
              v-model:value="keyword"
              size="small"
              placeholder="搜索设备名称或 Code"
              clearable
              @keyup.enter="refresh"
              @clear="refresh"
            />
            <UiCheckbox v-if="activeView === 'catalog'" v-model:checked="onlyPublished">只看已发布</UiCheckbox>
            <UiButton size="small" secondary @click="refresh">
              <RefreshCw :size="15" />
              刷新
            </UiButton>
          </div>
        </div>
      </NiondToolbar>
    </template>

    <div class="release-metrics">
      <div class="metric-card">
        <span class="metric-card__label">最新宿主</span>
        <strong>{{ catalog?.latestHost?.version || '-' }}</strong>
        <small>{{ catalog?.latestHost?.hostApiVersion || 'hostApiVersion -' }}</small>
      </div>
      <div class="metric-card">
        <span class="metric-card__label">宿主版本</span>
        <strong>{{ catalog?.hostReleases.length ?? 0 }}</strong>
        <small>{{ channelDisplay }} / {{ runtimeDisplay }}</small>
      </div>
      <div class="metric-card">
        <span class="metric-card__label">插件版本</span>
        <strong>{{ catalog?.pluginReleases.length ?? 0 }}</strong>
        <small>按 moduleId 维护</small>
      </div>
      <div class="metric-card">
        <span class="metric-card__label">有更新设备</span>
        <strong>{{ updateDeviceCount }}</strong>
        <small>{{ inventory.length }} 台已纳入盘点</small>
      </div>
    </div>

    <div v-if="activeView === 'catalog'" class="release-stack">
      <NiondTableCard>
        <div class="table-heading">
          <div>
            <h2>通用宿主</h2>
            <p>Velopack 宿主包版本与下载地址</p>
          </div>
        </div>
        <UiDataTable
          :columns="hostColumns"
          :data="catalog?.hostReleases ?? []"
          :loading="loadingCatalog"
          :row-key="(row: ClientHostReleaseDto) => row.id"
        />
      </NiondTableCard>

      <NiondTableCard>
        <div class="table-heading">
          <div>
            <h2>工序插件</h2>
            <p>外部插件目录消费的云端 catalog 条目</p>
          </div>
        </div>
        <UiDataTable
          :columns="pluginColumns"
          :data="catalog?.pluginReleases ?? []"
          :loading="loadingCatalog"
          :row-key="(row: ClientPluginReleaseDto) => row.id"
        />
      </NiondTableCard>
    </div>

    <NiondTableCard v-else>
      <div class="table-heading">
        <div>
          <h2>设备版本盘点</h2>
          <p>最近一次客户端版本上报与当前 catalog 最新版本的差异</p>
        </div>
      </div>
      <UiDataTable
        :columns="inventoryColumns"
        :data="inventory"
        :loading="loadingInventory"
        :row-key="(row: DeviceClientVersionInventoryDto) => row.deviceId"
      />
    </NiondTableCard>

    <UiDrawer v-model:show="showPluginDrawer" :width="520" placement="right">
      <UiDrawerContent title="设备插件版本" closable>
        <div v-if="selectedInventory" class="drawer-stack">
          <div class="drawer-summary">
            <strong>{{ selectedInventory.deviceName }}</strong>
            <code>{{ selectedInventory.clientCode }}</code>
          </div>
          <div v-for="plugin in selectedInventory.plugins" :key="plugin.moduleId" class="plugin-version-card">
            <div>
              <strong>{{ plugin.displayName || plugin.moduleId }}</strong>
              <span>{{ plugin.moduleId }}</span>
            </div>
            <UiTag :type="statusTone(plugin.updateStatus)" size="small" :bordered="false">
              {{ statusText(plugin.updateStatus) }}
            </UiTag>
            <dl>
              <div><dt>当前</dt><dd>{{ plugin.version || '-' }}</dd></div>
              <div><dt>最新</dt><dd>{{ plugin.latestVersion || '-' }}</dd></div>
              <div><dt>启用</dt><dd>{{ plugin.enabled ? '是' : '否' }}</dd></div>
            </dl>
            <p v-if="plugin.compatibilityIssue">{{ plugin.compatibilityIssue }}</p>
          </div>
          <EmptyState v-if="selectedInventory.plugins.length === 0" title="暂无插件上报" description="该设备最近一次版本上报中没有插件明细。" />
        </div>
      </UiDrawerContent>
    </UiDrawer>

    <UiModal v-model:show="showHostModal" preset="card" title="登记宿主版本" style="width: 720px;" :mask-closable="false">
      <div class="release-form">
        <label>Channel<UiInput v-model:value="hostForm.channel" /></label>
        <label>Version<UiInput v-model:value="hostForm.version" /></label>
        <label>Host API<UiInput v-model:value="hostForm.hostApiVersion" /></label>
        <label>Runtime<UiInput v-model:value="hostForm.targetRuntime" /></label>
        <label>Framework<UiInput v-model:value="hostForm.targetFramework" /></label>
        <label>Status<UiSelect v-model:value="hostForm.status" :options="statusOptions" /></label>
        <label class="is-wide">Download URL<UiInput v-model:value="hostForm.downloadUrl" /></label>
        <label class="is-wide">SHA256<UiInput v-model:value="hostForm.sha256" /></label>
        <label>Package Size<UiInput v-model:value="hostForm.packageSize" /></label>
        <label>Publisher<UiInput v-model:value="hostForm.publisher" /></label>
        <label class="is-wide">Release Notes<UiInput v-model:value="hostForm.releaseNotes" /></label>
        <label class="is-wide">Signature<UiInput v-model:value="hostForm.signature" /></label>
      </div>
      <template #footer>
        <div class="modal-actions">
          <UiButton @click="showHostModal = false">取消</UiButton>
          <UiButton type="primary" :loading="submitting" @click="submitHostRelease">保存</UiButton>
        </div>
      </template>
    </UiModal>

    <UiModal v-model:show="showPluginModal" preset="card" title="登记插件版本" style="width: 760px;" :mask-closable="false">
      <div class="release-form">
        <label>Module ID<UiInput v-model:value="pluginForm.moduleId" /></label>
        <label>Display Name<UiInput v-model:value="pluginForm.displayName" /></label>
        <label>Channel<UiInput v-model:value="pluginForm.channel" /></label>
        <label>Version<UiInput v-model:value="pluginForm.version" /></label>
        <label>Host API<UiInput v-model:value="pluginForm.hostApiVersion" /></label>
        <label>Min Host<UiInput v-model:value="pluginForm.minHostVersion" /></label>
        <label>Max Host<UiInput v-model:value="pluginForm.maxHostVersion" /></label>
        <label>Runtime<UiInput v-model:value="pluginForm.targetRuntime" /></label>
        <label>Framework<UiInput v-model:value="pluginForm.targetFramework" /></label>
        <label>Status<UiSelect v-model:value="pluginForm.status" :options="statusOptions" /></label>
        <label>Package Size<UiInput v-model:value="pluginForm.packageSize" /></label>
        <label>Publisher<UiInput v-model:value="pluginForm.publisher" /></label>
        <label>Description<UiInput v-model:value="pluginForm.description" /></label>
        <label>Icon<UiInput v-model:value="pluginForm.iconKind" /></label>
        <label>Accent<UiInput v-model:value="pluginForm.accentColor" /></label>
        <label class="is-wide">Download URL<UiInput v-model:value="pluginForm.downloadUrl" /></label>
        <label class="is-wide">SHA256<UiInput v-model:value="pluginForm.sha256" /></label>
        <label class="is-wide">Dependencies JSON<UiInput v-model:value="pluginForm.dependenciesJson" /></label>
        <label class="is-wide">Release Notes<UiInput v-model:value="pluginForm.releaseNotes" /></label>
        <label class="is-wide">Signature<UiInput v-model:value="pluginForm.signature" /></label>
      </div>
      <template #footer>
        <div class="modal-actions">
          <UiButton @click="showPluginModal = false">取消</UiButton>
          <UiButton type="primary" :loading="submitting" @click="submitPluginRelease">保存</UiButton>
        </div>
      </template>
    </UiModal>
  </NiondDataPage>
</template>

<script setup lang="ts">
import { computed, h, onMounted, reactive, ref, watch } from 'vue';
import {
  CloudDownload,
  Copy,
  ExternalLink,
  MonitorCheck,
  PackagePlus,
  Plus,
  RefreshCw,
} from 'lucide-vue-next';
import {
  getClientReleaseCatalogApi,
  getDeviceClientVersionInventoryApi,
  upsertClientHostReleaseApi,
  upsertClientPluginReleaseApi,
  type ClientHostReleaseDto,
  type ClientPluginReleaseDto,
  type ClientReleaseCatalogDto,
  type DeviceClientVersionInventoryDto,
  type UpsertClientHostReleasePayload,
  type UpsertClientPluginReleasePayload,
} from '../../api/clientRelease';
import { useAuthStore } from '../../stores/auth';
import { Permissions } from '../../types/permissions';
import NiondDataPage from '../../components/layout/NiondDataPage.vue';
import NiondTableCard from '../../components/layout/NiondTableCard.vue';
import NiondToolbar from '../../components/layout/NiondToolbar.vue';
import EmptyState from '../../components/states/EmptyState.vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiCheckbox from '../../components/ui/UiCheckbox.vue';
import UiDataTable from '../../components/ui/UiDataTable.vue';
import UiDrawer from '../../components/ui/UiDrawer.vue';
import UiDrawerContent from '../../components/ui/UiDrawerContent.vue';
import UiInput from '../../components/ui/UiInput.vue';
import UiModal from '../../components/ui/UiModal.vue';
import UiSelect from '../../components/ui/UiSelect.vue';
import UiTag from '../../components/ui/UiTag.vue';
import type { UiDataTableColumn } from '../../components/ui/types';

type ViewMode = 'catalog' | 'inventory';
type TagTone = 'default' | 'primary' | 'info' | 'success' | 'warning' | 'error';
type HostReleaseForm = Omit<UpsertClientHostReleasePayload, 'packageSize'> & { packageSize: string };
type PluginReleaseForm = Omit<UpsertClientPluginReleasePayload, 'packageSize'> & { packageSize: string };

const authStore = useAuthStore();
const activeView = ref<ViewMode>('catalog');
const channel = ref('stable');
const targetRuntime = ref('win-x64');
const keyword = ref('');
const onlyPublished = ref(false);
const catalog = ref<ClientReleaseCatalogDto | null>(null);
const inventory = ref<DeviceClientVersionInventoryDto[]>([]);
const loadingCatalog = ref(false);
const loadingInventory = ref(false);
const submitting = ref(false);
const showHostModal = ref(false);
const showPluginModal = ref(false);
const showPluginDrawer = ref(false);
const selectedInventory = ref<DeviceClientVersionInventoryDto | null>(null);

const canManage = computed(() => authStore.isAdmin || authStore.hasPermission(Permissions.Device.Update));
const channelDisplay = computed(() => channel.value.trim() || 'stable');
const runtimeDisplay = computed(() => targetRuntime.value.trim() || '全部 Runtime');
const updateDeviceCount = computed(() =>
  inventory.value.filter((device) =>
    device.hostUpdateStatus === 'UpdateAvailable'
    || device.plugins.some((plugin) => plugin.updateStatus === 'UpdateAvailable'),
  ).length,
);

const statusOptions = [
  { label: 'Draft', value: 'Draft' },
  { label: 'Published', value: 'Published' },
  { label: 'Revoked', value: 'Revoked' },
];

const defaultSha = '0'.repeat(64);
const hostForm = reactive<HostReleaseForm>({
  channel: 'stable',
  version: '',
  hostApiVersion: '1.0.0',
  targetRuntime: 'win-x64',
  targetFramework: 'net10.0',
  downloadUrl: '',
  sha256: defaultSha,
  packageSize: '0',
  releaseNotes: '',
  status: 'Draft',
  signature: '',
  publisher: '',
});

const pluginForm = reactive<PluginReleaseForm>({
  moduleId: '',
  displayName: '',
  description: '',
  iconKind: '',
  accentColor: '',
  channel: 'stable',
  version: '',
  hostApiVersion: '1.0.0',
  minHostVersion: '1.0.0',
  maxHostVersion: '99.0.0',
  targetRuntime: 'win-x64',
  targetFramework: 'net10.0',
  downloadUrl: '',
  sha256: defaultSha,
  packageSize: '0',
  releaseNotes: '',
  dependenciesJson: '[]',
  status: 'Draft',
  signature: '',
  publisher: '',
});

const fetchCatalog = async () => {
  loadingCatalog.value = true;
  try {
    catalog.value = await getClientReleaseCatalogApi({
      channel: channel.value,
      targetRuntime: targetRuntime.value,
      onlyPublished: onlyPublished.value,
    });
  } catch {
    catalog.value = null;
  } finally {
    loadingCatalog.value = false;
  }
};

const fetchInventory = async () => {
  loadingInventory.value = true;
  try {
    inventory.value = await getDeviceClientVersionInventoryApi({
      channel: channel.value,
      targetRuntime: targetRuntime.value,
      keyword: keyword.value,
    });
  } catch {
    inventory.value = [];
  } finally {
    loadingInventory.value = false;
  }
};

const refresh = async () => {
  await Promise.all([fetchCatalog(), fetchInventory()]);
};

watch(activeView, () => {
  if (activeView.value === 'inventory' && inventory.value.length === 0) {
    fetchInventory();
  }
});

onMounted(refresh);

const openHostModal = () => {
  Object.assign(hostForm, {
    channel: channel.value || 'stable',
    version: '',
    hostApiVersion: catalog.value?.latestHost?.hostApiVersion || '1.0.0',
    targetRuntime: targetRuntime.value || 'win-x64',
    targetFramework: 'net10.0',
    downloadUrl: '',
    sha256: defaultSha,
    packageSize: '0',
    releaseNotes: '',
    status: 'Draft',
    signature: '',
    publisher: '',
  });
  showHostModal.value = true;
};

const openPluginModal = () => {
  Object.assign(pluginForm, {
    moduleId: '',
    displayName: '',
    description: '',
    iconKind: '',
    accentColor: '',
    channel: channel.value || 'stable',
    version: '',
    hostApiVersion: catalog.value?.latestHost?.hostApiVersion || '1.0.0',
    minHostVersion: catalog.value?.latestHost?.version || '1.0.0',
    maxHostVersion: '99.0.0',
    targetRuntime: targetRuntime.value || 'win-x64',
    targetFramework: 'net10.0',
    downloadUrl: '',
    sha256: defaultSha,
    packageSize: '0',
    releaseNotes: '',
    dependenciesJson: '[]',
    status: 'Draft',
    signature: '',
    publisher: '',
  });
  showPluginModal.value = true;
};

const submitHostRelease = async () => {
  if (!hostForm.version.trim() || !hostForm.downloadUrl.trim()) {
    alert('请填写宿主版本和下载地址。');
    return;
  }

  submitting.value = true;
  try {
    await upsertClientHostReleaseApi({
      ...hostForm,
      packageSize: Number(hostForm.packageSize) || 0,
    });
    showHostModal.value = false;
    await fetchCatalog();
  } finally {
    submitting.value = false;
  }
};

const submitPluginRelease = async () => {
  if (!pluginForm.moduleId.trim() || !pluginForm.displayName.trim() || !pluginForm.version.trim()) {
    alert('请填写插件模块、名称和版本。');
    return;
  }

  submitting.value = true;
  try {
    await upsertClientPluginReleaseApi({
      ...pluginForm,
      packageSize: Number(pluginForm.packageSize) || 0,
    });
    showPluginModal.value = false;
    await fetchCatalog();
  } finally {
    submitting.value = false;
  }
};

const openPluginDrawer = (row: DeviceClientVersionInventoryDto) => {
  selectedInventory.value = row;
  showPluginDrawer.value = true;
};

const copyText = async (text: string) => {
  if (!text) return;
  await navigator.clipboard?.writeText(text);
};

const openUrl = (url: string) => {
  if (!url) return;
  window.open(url, '_blank', 'noopener,noreferrer');
};

const statusTone = (status: string): TagTone => {
  const normalized = status.toLowerCase();
  if (normalized === 'published' || normalized === 'latest') return 'success';
  if (normalized === 'updateavailable') return 'warning';
  if (normalized === 'incompatible' || normalized === 'revoked') return 'error';
  if (normalized === 'missingreport' || normalized === 'norelease') return 'default';
  return 'info';
};

const statusText = (status: string) => ({
  Draft: '草稿',
  Published: '已发布',
  Revoked: '已撤回',
  Latest: '已最新',
  UpdateAvailable: '可更新',
  Incompatible: '不兼容',
  MissingReport: '未上报',
  NoRelease: '无发布',
}[status] || status);

const formatSize = (size: number) => {
  if (!Number.isFinite(size) || size <= 0) return '-';
  if (size >= 1024 * 1024) return `${(size / 1024 / 1024).toFixed(1)} MB`;
  if (size >= 1024) return `${(size / 1024).toFixed(1)} KB`;
  return `${size} B`;
};

const formatDate = (value?: string | null) => {
  if (!value) return '-';
  return new Date(value).toLocaleString();
};

const hostColumns: UiDataTableColumn<ClientHostReleaseDto>[] = [
  { title: '版本', key: 'version', minWidth: 120, render: (row) => h('strong', row.version) },
  { title: 'Host API', key: 'hostApiVersion', minWidth: 120 },
  { title: 'Runtime', key: 'targetRuntime', minWidth: 110 },
  {
    title: '状态',
    key: 'status',
    width: 110,
    render: (row) => h(UiTag, { type: statusTone(row.status), size: 'small', bordered: false }, () => statusText(row.status)),
  },
  { title: '大小', key: 'packageSize', width: 110, render: (row) => h('span', formatSize(row.packageSize)) },
  { title: '发布时间', key: 'publishedAtUtc', minWidth: 180, render: (row) => h('span', formatDate(row.publishedAtUtc)) },
  {
    title: '操作',
    key: 'actions',
    width: 160,
    align: 'right',
    render: (row) => h('div', { class: 'row-actions' }, [
      h(UiButton, { size: 'tiny', secondary: true, type: 'primary', onClick: () => openUrl(row.downloadUrl) }, () => [h(ExternalLink, { size: 13 }), '下载']),
      h(UiButton, { size: 'tiny', quaternary: true, type: 'info', onClick: () => copyText(row.sha256) }, () => [h(Copy, { size: 13 }), 'SHA']),
    ]),
  },
];

const pluginColumns: UiDataTableColumn<ClientPluginReleaseDto>[] = [
  { title: '插件', key: 'moduleId', minWidth: 190, render: (row) => h('div', { class: 'plugin-cell' }, [h('strong', row.displayName), h('span', row.moduleId)]) },
  { title: '版本', key: 'version', width: 110 },
  { title: 'Host API', key: 'hostApiVersion', width: 110 },
  { title: '宿主窗口', key: 'minHostVersion', minWidth: 160, render: (row) => h('span', `${row.minHostVersion} - ${row.maxHostVersion}`) },
  { title: 'Runtime', key: 'targetRuntime', width: 110 },
  {
    title: '状态',
    key: 'status',
    width: 110,
    render: (row) => h(UiTag, { type: statusTone(row.status), size: 'small', bordered: false }, () => statusText(row.status)),
  },
  { title: '大小', key: 'packageSize', width: 110, render: (row) => h('span', formatSize(row.packageSize)) },
  {
    title: '操作',
    key: 'actions',
    width: 160,
    align: 'right',
    render: (row) => h('div', { class: 'row-actions' }, [
      h(UiButton, { size: 'tiny', secondary: true, type: 'primary', onClick: () => openUrl(row.downloadUrl) }, () => [h(ExternalLink, { size: 13 }), '下载']),
      h(UiButton, { size: 'tiny', quaternary: true, type: 'info', onClick: () => copyText(row.sha256) }, () => [h(Copy, { size: 13 }), 'SHA']),
    ]),
  },
];

const inventoryColumns: UiDataTableColumn<DeviceClientVersionInventoryDto>[] = [
  { title: '设备', key: 'deviceName', minWidth: 190, render: (row) => h('div', { class: 'device-cell' }, [h('strong', row.deviceName), h('code', row.clientCode)]) },
  { title: 'Channel', key: 'channel', width: 120, render: (row) => h('span', row.channel || '-') },
  { title: '宿主当前', key: 'hostVersion', width: 120, render: (row) => h('span', row.hostVersion || '-') },
  { title: '宿主最新', key: 'latestHostVersion', width: 120, render: (row) => h('span', row.latestHostVersion || '-') },
  {
    title: '宿主状态',
    key: 'hostUpdateStatus',
    width: 120,
    render: (row) => h(UiTag, { type: statusTone(row.hostUpdateStatus), size: 'small', bordered: false }, () => statusText(row.hostUpdateStatus)),
  },
  { title: '插件', key: 'plugins', width: 110, render: (row) => h(UiButton, { size: 'tiny', secondary: true, type: 'info', onClick: () => openPluginDrawer(row) }, () => `${row.plugins.length} 个`) },
  { title: '上报时间', key: 'reportedAtUtc', minWidth: 180, render: (row) => h('span', formatDate(row.reportedAtUtc)) },
  { title: '问题', key: 'hostCompatibilityIssue', minWidth: 240, render: (row) => h('span', { class: row.hostCompatibilityIssue ? 'issue-text' : '' }, row.hostCompatibilityIssue || '-') },
];
</script>

<style scoped>
.release-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  flex-wrap: wrap;
}

.release-tabs,
.release-filters {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
}

.release-tab {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  height: 36px;
  border: 0;
  border-radius: 12px;
  padding: 0 12px;
  background: transparent;
  color: #596273;
  font-size: 13px;
  font-weight: 900;
  cursor: pointer;
}

.release-tab.is-active {
  background: #111827;
  color: #fff;
}

.release-filters :deep(.ui-input),
.release-filters :deep(input) {
  width: 190px;
}

.release-metrics {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 14px;
}

.metric-card {
  min-width: 0;
  border: 1px solid var(--border);
  border-radius: 18px;
  background: #fff;
  padding: 16px;
  box-shadow: var(--shadow-sm);
}

.metric-card__label,
.metric-card small {
  display: block;
  color: #7b8492;
  font-size: 12px;
  font-weight: 800;
}

.metric-card strong {
  display: block;
  margin: 6px 0;
  color: #111827;
  font-size: 28px;
  font-weight: 950;
  line-height: 1.1;
}

.release-stack {
  display: grid;
  gap: 18px;
}

.table-heading {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
  margin-bottom: 14px;
}

.table-heading h2 {
  margin: 0;
  color: #111827;
  font-size: 17px;
  font-weight: 950;
}

.table-heading p {
  margin: 5px 0 0;
  color: #7b8492;
  font-size: 12px;
  font-weight: 700;
}

:deep(.plugin-cell),
:deep(.device-cell) {
  display: grid;
  gap: 3px;
}

:deep(.plugin-cell strong),
:deep(.device-cell strong) {
  color: #111827;
  font-weight: 900;
}

:deep(.plugin-cell span),
:deep(.device-cell code) {
  color: #7b8492;
  font-size: 12px;
  font-weight: 800;
}

:deep(.issue-text) {
  color: #c24141;
  font-weight: 800;
}

.release-form {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 14px;
}

.release-form label {
  display: grid;
  gap: 7px;
  color: #596273;
  font-size: 12px;
  font-weight: 900;
}

.release-form .is-wide {
  grid-column: 1 / -1;
}

.modal-actions {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
}

.drawer-stack {
  display: grid;
  gap: 14px;
}

.drawer-summary,
.plugin-version-card {
  border: 1px solid var(--border);
  border-radius: 16px;
  background: #fff;
  padding: 14px;
}

.drawer-summary {
  display: grid;
  gap: 4px;
}

.drawer-summary code {
  color: #7b8492;
  font-size: 12px;
  font-weight: 800;
}

.plugin-version-card {
  display: grid;
  gap: 10px;
}

.plugin-version-card > div:first-child {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 12px;
}

.plugin-version-card span {
  color: #7b8492;
  font-size: 12px;
  font-weight: 800;
}

.plugin-version-card dl {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 8px;
  margin: 0;
}

.plugin-version-card dt {
  color: #7b8492;
  font-size: 11px;
  font-weight: 800;
}

.plugin-version-card dd {
  margin: 2px 0 0;
  color: #111827;
  font-size: 13px;
  font-weight: 900;
}

.plugin-version-card p {
  margin: 0;
  color: #c24141;
  font-size: 12px;
  font-weight: 800;
  line-height: 1.5;
}

@media (max-width: 1180px) {
  .release-metrics {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 760px) {
  .release-metrics,
  .release-form {
    grid-template-columns: 1fr;
  }

  .release-filters :deep(.ui-input),
  .release-filters :deep(input) {
    width: 100%;
  }
}
</style>
