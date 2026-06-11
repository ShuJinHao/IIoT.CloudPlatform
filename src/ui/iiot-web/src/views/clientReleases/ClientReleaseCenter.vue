<template>
  <NiondDataPage
    class="release-page"
    page-key="clientReleases"
    title="客户端下载中心"
    subtitle="查看宿主与插件版本，下载并配置客户端安装包"
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
              v-if="canManage"
              class="release-tab"
              :class="{ 'is-active': activeView === 'binding' }"
              type="button"
              @click="activeView = 'binding'"
            >
              <Boxes :size="16" />
              首装下载
            </button>
          </div>
          <div class="release-filters">
            <UiButton size="small" secondary @click="refresh">
              <RefreshCw :size="15" />
              刷新
            </UiButton>
          </div>
        </div>
      </NiondToolbar>
    </template>

    <div v-if="activeView === 'catalog'" class="release-stack">
      <NiondTableCard>
        <div class="table-heading">
          <div>
            <h2>通用宿主</h2>
            <p>宿主各版本与下载</p>
          </div>
        </div>
        <UiDataTable
          :columns="hostColumns"
          :data="catalog?.hostReleases ?? []"
          :loading="loadingCatalog"
          :row-key="(row: ClientHostReleaseDto) => row.id"
        >
          <template #empty>
            <EmptyState title="暂无宿主数据" description="当前条件下未找到任何通用宿主版本，请登记新版本。" />
          </template>
        </UiDataTable>
      </NiondTableCard>

      <NiondTableCard>
        <div class="table-heading">
          <div>
            <h2>工序插件</h2>
            <p>按工序分类，每个工序下是它的各个版本</p>
          </div>
        </div>
        <div v-if="pluginGroups.length" class="plugin-groups">
          <div v-for="group in pluginGroups" :key="group.moduleId" class="plugin-group">
            <div class="plugin-group__head">
              <strong>{{ group.displayName }}</strong>
              <span>{{ group.moduleId }}</span>
            </div>
            <div v-for="ver in group.versions" :key="ver.id" class="plugin-version-row">
              <span class="pv-version">{{ ver.version }}</span>
              <UiTag :type="statusTone(ver.status)" size="small" :bordered="false">
                {{ statusText(ver.status) }}
              </UiTag>
              <span class="pv-meta">{{ formatSize(ver.packageSize) }}</span>
              <span class="pv-meta">{{ formatDate(ver.publishedAtUtc) }}</span>
              <span class="pv-note">{{ ver.description || ver.releaseNotes || '-' }}</span>
              <UiButton size="tiny" secondary type="primary" @click="openUrl(ver.downloadUrl)">
                <ExternalLink :size="13" />
                下载
              </UiButton>
            </div>
          </div>
        </div>
        <EmptyState
          v-else
          title="暂无插件"
          description="还没有登记任何工序插件版本。"
          style="padding: 32px 24px"
        />
      </NiondTableCard>
    </div>

    <NiondTableCard v-else-if="activeView === 'inventory'">
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
      >
        <template #empty>
          <EmptyState title="暂无盘点数据" description="未找到匹配的设备版本上报数据。" />
        </template>
      </UiDataTable>
    </NiondTableCard>

    <NiondTableCard v-else-if="activeView === 'binding'">
      <EdgeBindingDownloadPanel
        :plugin-releases="catalog?.pluginReleases ?? []"
        :channel="channelDisplay"
        :target-runtime="targetRuntime || 'win-x64'"
        :host-version="catalog?.latestHost?.version ?? null"
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
  Boxes,
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
import EdgeBindingDownloadPanel from './EdgeBindingDownloadPanel.vue';
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

type ViewMode = 'catalog' | 'inventory' | 'binding';
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

// 工序插件按工序(moduleId)分类，每个工序下挂它的各个版本
const pluginGroups = computed(() => {
  const groups = new Map<string, { moduleId: string; displayName: string; versions: ClientPluginReleaseDto[] }>();
  for (const plugin of catalog.value?.pluginReleases ?? []) {
    let group = groups.get(plugin.moduleId);
    if (!group) {
      group = { moduleId: plugin.moduleId, displayName: plugin.displayName || plugin.moduleId, versions: [] };
      groups.set(plugin.moduleId, group);
    }
    group.versions.push(plugin);
  }
  return Array.from(groups.values());
});

const hostColumns: UiDataTableColumn<ClientHostReleaseDto>[] = [
  { title: '版本', key: 'version', minWidth: 110, render: (row) => h('strong', row.version) },
  {
    title: '状态',
    key: 'status',
    width: 100,
    render: (row) => h(UiTag, { type: statusTone(row.status), size: 'small', bordered: false }, () => statusText(row.status)),
  },
  { title: '大小', key: 'packageSize', width: 110, render: (row) => h('span', formatSize(row.packageSize)) },
  { title: '发布时间', key: 'publishedAtUtc', minWidth: 170, render: (row) => h('span', formatDate(row.publishedAtUtc)) },
  { title: '备注', key: 'releaseNotes', minWidth: 160, render: (row) => h('span', row.releaseNotes || '-') },
  {
    title: '操作',
    key: 'actions',
    width: 110,
    align: 'right',
    render: (row) => h('div', { class: 'row-actions' }, [
      h(UiButton, { size: 'tiny', secondary: true, type: 'primary', onClick: () => openUrl(row.downloadUrl) }, () => [h(ExternalLink, { size: 13 }), '下载']),
    ]),
  },
];

const pluginColumns: UiDataTableColumn<ClientPluginReleaseDto>[] = [
  { title: '插件', key: 'moduleId', minWidth: 190, render: (row) => h('div', { class: 'plugin-cell' }, [h('strong', row.displayName), h('span', row.moduleId)]) },
  { title: '版本', key: 'version', width: 100 },
  {
    title: '状态',
    key: 'status',
    width: 100,
    render: (row) => h(UiTag, { type: statusTone(row.status), size: 'small', bordered: false }, () => statusText(row.status)),
  },
  { title: '大小', key: 'packageSize', width: 110, render: (row) => h('span', formatSize(row.packageSize)) },
  { title: '发布时间', key: 'publishedAtUtc', minWidth: 170, render: (row) => h('span', formatDate(row.publishedAtUtc)) },
  { title: '备注', key: 'description', minWidth: 160, render: (row) => h('span', row.description || row.releaseNotes || '-') },
  {
    title: '操作',
    key: 'actions',
    width: 110,
    align: 'right',
    render: (row) => h('div', { class: 'row-actions' }, [
      h(UiButton, { size: 'tiny', secondary: true, type: 'primary', onClick: () => openUrl(row.downloadUrl) }, () => [h(ExternalLink, { size: 13 }), '下载']),
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
.release-page {
  font-family: var(--font-sans);
  color: var(--text-0);
}

.release-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-4);
  flex-wrap: wrap;
}

.release-tabs,
.release-filters {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  flex-wrap: wrap;
}

.release-tab {
  display: inline-flex;
  align-items: center;
  gap: var(--space-2);
  height: 36px;
  border: 0;
  border-radius: var(--radius-md);
  padding: 0 var(--space-3);
  background: transparent;
  color: var(--text-1);
  font-size: var(--fs-base);
  font-weight: var(--fw-semibold);
  cursor: pointer;
}

.release-tab.is-active {
  background: var(--bg-3);
  color: var(--text-0);
}

.release-filters :deep(.ui-input),
.release-filters :deep(input) {
  width: 190px;
}

.release-metrics {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: var(--space-3);
}

.metric-card {
  min-width: 0;
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  background: var(--card);
  padding: var(--space-4);
  box-shadow: var(--shadow-sm);
}

.metric-card__label,
.metric-card small {
  display: block;
  color: var(--text-2);
  font-size: var(--fs-sm);
  font-weight: var(--fw-medium);
}

.metric-card strong {
  display: block;
  margin: var(--space-1) 0;
  color: var(--text-0);
  font-size: var(--fs-3xl);
  font-weight: var(--fw-display);
  line-height: 1.1;
}


.metric-card strong.is-empty {
  color: var(--text-2);
}

.metric-card small.empty-hint {
  color: var(--brand);
  cursor: pointer;
  text-decoration: underline;
  text-decoration-style: dotted;
  text-underline-offset: 2px;
}
.metric-card small.empty-hint:hover {
  color: var(--brand-hover);
}

.release-stack {
  display: grid;
  gap: var(--space-4);
}

.table-heading {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--space-4);
  padding: 22px 24px 0;
  margin-bottom: var(--space-3);
}

.table-heading h2 {
  margin: 0;
  color: var(--text-0);
  font-size: var(--fs-xl);
  font-weight: var(--fw-strong);
}

.table-heading p {
  margin: var(--space-1) 0 0;
  color: var(--text-1);
  font-size: var(--fs-sm);
  font-weight: var(--fw-medium);
}

.plugin-groups {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  padding: 0 24px 22px;
}

.plugin-group {
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  overflow: hidden;
}

.plugin-group__head {
  display: flex;
  align-items: baseline;
  gap: var(--space-2);
  padding: 10px 16px;
  background: var(--bg-2);
}

.plugin-group__head strong {
  color: var(--text-0);
  font-size: var(--fs-base);
  font-weight: var(--fw-semibold);
}

.plugin-group__head span {
  color: var(--text-2);
  font-size: var(--fs-xs);
}

.plugin-version-row {
  display: grid;
  grid-template-columns: 80px 80px 90px 170px 1fr 92px;
  align-items: center;
  gap: var(--space-3);
  padding: 10px 16px;
  border-top: 1px solid var(--border);
  font-size: var(--fs-sm);
  color: var(--text-1);
}

.pv-version {
  font-weight: var(--fw-semibold);
  color: var(--text-0);
}

.pv-note {
  overflow: hidden;
  color: var(--text-2);
  text-overflow: ellipsis;
  white-space: nowrap;
}

:deep(.plugin-cell),
:deep(.device-cell) {
  display: grid;
  gap: 3px;
}

:deep(.plugin-cell strong),
:deep(.device-cell strong) {
  color: var(--text-0);
  font-weight: var(--fw-semibold);
}

:deep(.plugin-cell span),
:deep(.device-cell code) {
  color: var(--text-2);
  font-size: var(--fs-sm);
  font-weight: var(--fw-medium);
}

:deep(.issue-text) {
  color: var(--error);
  font-weight: var(--fw-medium);
}

.release-form {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: var(--space-3);
}

.release-form label {
  display: grid;
  gap: var(--space-2);
  color: var(--text-1);
  font-size: var(--fs-sm);
  font-weight: var(--fw-semibold);
}

.release-form .is-wide {
  grid-column: 1 / -1;
}

.modal-actions {
  display: flex;
  justify-content: flex-end;
  gap: var(--space-2);
}

.drawer-stack {
  display: grid;
  gap: var(--space-3);
}

.drawer-summary,
.plugin-version-card {
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--card);
  padding: var(--space-3);
}

.drawer-summary {
  display: grid;
  gap: var(--space-1);
}

.drawer-summary code {
  color: var(--text-2);
  font-size: var(--fs-sm);
  font-weight: var(--fw-medium);
}

.plugin-version-card {
  display: grid;
  gap: var(--space-2);
}

.plugin-version-card > div:first-child {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: var(--space-3);
}

.plugin-version-card span {
  color: var(--text-2);
  font-size: var(--fs-sm);
  font-weight: var(--fw-medium);
}

.plugin-version-card dl {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: var(--space-2);
  margin: 0;
}

.plugin-version-card dt {
  color: var(--text-2);
  font-size: var(--fs-xs);
  font-weight: var(--fw-medium);
}

.plugin-version-card dd {
  margin: 2px 0 0;
  color: var(--text-0);
  font-size: var(--fs-base);
  font-weight: var(--fw-semibold);
}

.plugin-version-card p {
  margin: 0;
  color: var(--error);
  font-size: var(--fs-sm);
  font-weight: var(--fw-medium);
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
