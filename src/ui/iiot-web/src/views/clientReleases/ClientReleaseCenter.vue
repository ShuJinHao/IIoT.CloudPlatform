<template>
  <NiondDataPage
    class="release-page"
    :title="pageTitle"
    :subtitle="pageSubtitle"
  >
    <template #actions>
      <template v-if="isPublishRoute">
        <UiButton size="small" secondary @click="goInstallerCenter">
          <Boxes :size="15" />
          首装生成
        </UiButton>
        <UiButton type="primary" size="small" @click="openHostModal">
          <Plus :size="15" />
          登记宿主
        </UiButton>
        <UiButton type="info" size="small" secondary @click="openPluginModal">
          <PackagePlus :size="15" />
          登记插件
        </UiButton>
      </template>
      <UiButton v-else-if="canManage" type="info" size="small" secondary @click="goPublishManager">
        <Settings2 :size="15" />
        发布管理
      </UiButton>
    </template>

    <template #toolbar>
      <NiondToolbar>
        <div class="release-toolbar">
          <div v-if="!isPublishRoute" class="release-tabs">
            <button
              v-if="canManage"
              class="release-tab"
              :class="{ 'is-active': activeView === 'binding' }"
              type="button"
              @click="activeView = 'binding'"
            >
              <Boxes :size="16" />
              首装生成
            </button>
            <button
              class="release-tab"
              :class="{ 'is-active': activeView === 'catalog' }"
              type="button"
              @click="activeView = 'catalog'"
            >
              <CloudDownload :size="16" />
              版本 catalog
            </button>
            <button
              v-if="canManage"
              class="release-tab"
              :class="{ 'is-active': activeView === 'inventory' }"
              type="button"
              @click="activeView = 'inventory'"
            >
              <MonitorCheck :size="16" />
              设备安装状态
            </button>
          </div>
          <div v-else class="release-mode-label">
            <Settings2 :size="16" />
            客户端发布管理
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

    <div v-if="isPublishRoute || activeView === 'catalog'" class="release-stack">
      <NiondTableCard>
        <div class="table-heading">
          <div>
            <h2>版本 catalog</h2>
            <p>宿主和工序插件统一展示，历史版本从行内查看</p>
          </div>
        </div>
        <UiDataTable
          :columns="releaseCatalogColumns"
          :data="releaseCatalogRows"
          :loading="loadingCatalog"
          :row-key="(row: ReleaseCatalogRow) => row.key"
        >
          <template #empty>
            <EmptyState title="暂无版本数据" description="当前条件下未找到宿主或工序插件版本。" />
          </template>
        </UiDataTable>
      </NiondTableCard>
    </div>

    <NiondTableCard v-else-if="!isPublishRoute && activeView === 'inventory'">
      <div class="table-heading">
        <div>
          <h2>设备安装状态</h2>
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
          <EmptyState title="暂无安装状态" description="未找到匹配的设备版本上报数据。" />
        </template>
      </UiDataTable>
    </NiondTableCard>

    <NiondTableCard v-else-if="!isPublishRoute && activeView === 'binding'">
      <EdgeBindingDownloadPanel
        :plugin-components="catalog?.plugins ?? []"
        :channel="channelDisplay"
        :target-runtime="targetRuntime || 'win-x64'"
        :host-version="selectedHostPackageVersion"
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
              <div><dt>启用</dt><dd>{{ plugin.enabled ? '是' : '否' }}</dd></div>
            </dl>
            <p v-if="plugin.compatibilityIssue">{{ plugin.compatibilityIssue }}</p>
          </div>
          <EmptyState v-if="selectedInventory.plugins.length === 0" title="暂无插件上报" description="该设备最近一次版本上报中没有插件明细。" />
        </div>
      </UiDrawerContent>
    </UiDrawer>

    <UiModal v-model:show="showHistoryModal" preset="card" :title="historyModalTitle" style="width: 860px;">
      <div v-if="selectedReleaseRow" class="history-modal">
        <div class="history-summary">
          <div>
            <strong>{{ selectedReleaseRow.componentName }}</strong>
            <code>{{ selectedReleaseRow.componentCode }}</code>
          </div>
          <UiTag :type="selectedReleaseRow.kind === 'host' ? 'default' : 'info'" size="small" :bordered="false">
            {{ selectedReleaseRow.kindLabel }}
          </UiTag>
        </div>
        <UiDataTable
          :columns="historyColumns"
          :data="selectedHistoryVersions"
          :row-key="(row: ReleaseVersionEntry) => row.id"
        >
          <template #empty>
            <EmptyState title="无历史版本" description="当前组件只有一个可见版本。" />
          </template>
        </UiDataTable>
      </div>
      <template #footer>
        <div class="modal-actions">
          <UiButton @click="showHistoryModal = false">关闭</UiButton>
        </div>
      </template>
    </UiModal>

    <UiModal v-model:show="showReleaseDetailModal" preset="card" title="更新内容详情" style="width: 720px;">
      <div v-if="selectedReleaseDetail" class="release-detail-modal">
        <div class="release-detail-summary">
          <div class="release-detail-heading">
            <strong>{{ selectedReleaseDetail.componentName }}</strong>
            <code>{{ selectedReleaseDetail.componentCode }}</code>
          </div>
          <UiTag :type="selectedReleaseDetail.kind === 'host' ? 'default' : 'info'" size="small" :bordered="false">
            {{ selectedReleaseDetail.kindLabel }}
          </UiTag>
        </div>
        <div class="release-detail-meta">
          <div>
            <span>版本</span>
            <strong>{{ selectedReleaseDetail.version }}</strong>
          </div>
          <div>
            <span>状态</span>
            <UiTag :type="selectedReleaseDetail.statusTone" size="small" :bordered="false">
              {{ selectedReleaseDetail.statusText }}
            </UiTag>
          </div>
          <div>
            <span>发布时间</span>
            <strong>{{ selectedReleaseDetail.publishedAt }}</strong>
          </div>
          <div>
            <span>大小</span>
            <strong>{{ selectedReleaseDetail.packageSize }}</strong>
          </div>
        </div>
        <section class="release-detail-notes">
          <h3>完整更新内容</h3>
          <p>{{ selectedReleaseDetail.releaseNotes }}</p>
        </section>
      </div>
      <template #footer>
        <div class="modal-actions">
          <UiButton @click="showReleaseDetailModal = false">关闭</UiButton>
        </div>
      </template>
    </UiModal>

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
import { useRoute, useRouter } from 'vue-router';
import {
  Boxes,
  CloudDownload,
  ExternalLink,
  History,
  Info,
  MonitorCheck,
  PackagePlus,
  Plus,
  RefreshCw,
  Settings2,
} from 'lucide-vue-next';
import {
  getClientReleaseCatalogApi,
  getDeviceClientVersionInventoryApi,
  upsertClientHostReleaseApi,
  upsertClientPluginReleaseApi,
  type ClientHostVersionEntryDto,
  type ClientPluginVersionEntryDto,
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
import UiDataTable from '../../components/ui/UiDataTable.vue';
import UiDrawer from '../../components/ui/UiDrawer.vue';
import UiDrawerContent from '../../components/ui/UiDrawerContent.vue';
import UiInput from '../../components/ui/UiInput.vue';
import UiModal from '../../components/ui/UiModal.vue';
import UiSelect from '../../components/ui/UiSelect.vue';
import UiTag from '../../components/ui/UiTag.vue';
import type { UiDataTableColumn } from '../../components/ui/types';
import { notifyWarning } from '../../utils/feedback';

type ViewMode = 'catalog' | 'inventory' | 'binding';
type TagTone = 'default' | 'primary' | 'info' | 'success' | 'warning' | 'error';
type HostReleaseForm = Omit<UpsertClientHostReleasePayload, 'packageSize'> & { packageSize: string };
type PluginReleaseForm = Omit<UpsertClientPluginReleasePayload, 'packageSize'> & { packageSize: string };
type ReleaseKind = 'host' | 'plugin';
type ReleaseVersionEntry = ClientHostVersionEntryDto | ClientPluginVersionEntryDto;

interface ReleaseCatalogRow {
  key: string;
  kind: ReleaseKind;
  kindLabel: string;
  componentName: string;
  componentCode: string;
  currentVersion: ReleaseVersionEntry;
  historyVersions: ReleaseVersionEntry[];
}

interface ReleaseDetail {
  kind: ReleaseKind;
  kindLabel: string;
  componentName: string;
  componentCode: string;
  version: string;
  statusText: string;
  statusTone: TagTone;
  publishedAt: string;
  packageSize: string;
  releaseNotes: string;
}

const authStore = useAuthStore();
const route = useRoute();
const router = useRouter();
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
const showHistoryModal = ref(false);
const showReleaseDetailModal = ref(false);
const selectedInventory = ref<DeviceClientVersionInventoryDto | null>(null);
const selectedReleaseRow = ref<ReleaseCatalogRow | null>(null);
const selectedReleaseDetail = ref<ReleaseDetail | null>(null);

const canManage = computed(() => authStore.isAdmin || authStore.hasPermission(Permissions.Device.Update));
const activeView = ref<ViewMode>(canManage.value ? 'binding' : 'catalog');
const isPublishRoute = computed(() => route.name === 'ClientReleasePublish');
const pageTitle = computed(() => (isPublishRoute.value ? '客户端发布管理' : '客户端首装生成'));
const pageSubtitle = computed(() =>
  isPublishRoute.value
    ? '管理宿主与工序插件版本，发布素材只供首装打包链路读取。'
    : '选择工序插件并绑定设备，生成已写入设备身份的客户端安装包。',
);
const channelDisplay = computed(() => channel.value.trim() || 'stable');
const selectedHostPackage = computed(() => {
  const versions = catalog.value?.host.versions ?? [];
  return versions.find((version) => version.status.toLowerCase() === 'published') ?? null;
});
const selectedHostPackageVersion = computed(() => selectedHostPackage.value?.version ?? null);

const statusOptions = [
  { label: 'Draft', value: 'Draft' },
  { label: 'Published', value: 'Published' },
  { label: 'Deprecated', value: 'Deprecated' },
  { label: 'Archived', value: 'Archived' },
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
      includeArchived: true,
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

watch(canManage, (value) => {
  if (!value && activeView.value !== 'catalog') {
    activeView.value = 'catalog';
  }
}, { immediate: true });

onMounted(refresh);

const goPublishManager = () => {
  router.push({ name: 'ClientReleasePublish' });
};

const goInstallerCenter = () => {
  activeView.value = canManage.value ? 'binding' : 'catalog';
  router.push({ name: 'ClientReleases' });
};

const openHostModal = () => {
  Object.assign(hostForm, {
    channel: channel.value || 'stable',
    version: '',
    hostApiVersion: selectedHostPackage.value?.hostApiVersion || '1.0.0',
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
    hostApiVersion: selectedHostPackage.value?.hostApiVersion || '1.0.0',
    minHostVersion: selectedHostPackage.value?.version || '1.0.0',
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
    notifyWarning('请填写宿主版本和下载地址。');
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
    notifyWarning('请填写插件模块、名称和版本。');
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

const openHistoryModal = (row: ReleaseCatalogRow) => {
  if (row.historyVersions.length === 0) return;
  selectedReleaseRow.value = row;
  showHistoryModal.value = true;
};

const openReleaseDetailModal = (version: ReleaseVersionEntry, row: ReleaseCatalogRow | null) => {
  selectedReleaseDetail.value = {
    kind: row?.kind ?? 'plugin',
    kindLabel: row?.kindLabel ?? '组件',
    componentName: row?.componentName ?? '-',
    componentCode: row?.componentCode ?? '-',
    version: version.version,
    statusText: statusText(version.status),
    statusTone: statusTone(version.status),
    publishedAt: formatDate(version.publishedAtUtc),
    packageSize: formatSize(version.packageSize),
    releaseNotes: formatReleaseNotes(version.releaseNotes, '暂无更新内容'),
  };
  showReleaseDetailModal.value = true;
};

const openUrl = (url: string) => {
  if (!url) return;
  window.open(url, '_blank', 'noopener,noreferrer');
};

const statusTone = (status: string): TagTone => {
  const normalized = status.toLowerCase();
  if (normalized === 'published' || normalized === 'latest') return 'success';
  if (normalized === 'updateavailable') return 'warning';
  if (normalized === 'incompatible' || normalized === 'archived') return 'error';
  if (normalized === 'deprecated') return 'warning';
  if (normalized === 'missingreport' || normalized === 'norelease') return 'default';
  return 'info';
};

const statusText = (status: string) => ({
  Draft: '草稿',
  Published: '已发布',
  Deprecated: '已弃用',
  Archived: '已归档',
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

const formatReleaseNotes = (value?: string | null, fallback = '-') => {
  const text = value?.trim();
  return text && text.length > 0 ? text : fallback;
};

const pickCurrentVersion = <T extends ReleaseVersionEntry>(versions: T[]) => {
  return versions.find((version) => version.status.toLowerCase() === 'published')
    ?? versions.find((version) => version.status.toLowerCase() === 'deprecated')
    ?? versions.find((version) => version.status.toLowerCase() === 'draft')
    ?? versions[0]
    ?? null;
};

const releaseCatalogRows = computed<ReleaseCatalogRow[]>(() => {
  const rows: ReleaseCatalogRow[] = [];
  const hostVersions = catalog.value?.host.versions ?? [];
  const currentHost = pickCurrentVersion(hostVersions);

  if (currentHost) {
    rows.push({
      key: `host:${currentHost.channel}:${currentHost.targetRuntime}`,
      kind: 'host',
      kindLabel: '宿主',
      componentName: catalog.value?.host.displayName || '通用宿主',
      componentCode: currentHost.targetRuntime,
      currentVersion: currentHost,
      historyVersions: hostVersions.filter((version) => version.id !== currentHost.id),
    });
  }

  for (const plugin of catalog.value?.plugins ?? []) {
    const currentPlugin = pickCurrentVersion(plugin.versions);
    if (!currentPlugin) continue;
    rows.push({
      key: `plugin:${plugin.moduleId}`,
      kind: 'plugin',
      kindLabel: '工序插件',
      componentName: plugin.displayName || plugin.moduleId,
      componentCode: plugin.moduleId,
      currentVersion: currentPlugin,
      historyVersions: plugin.versions.filter((version) => version.id !== currentPlugin.id),
    });
  }

  return rows;
});

const selectedHistoryVersions = computed(() => selectedReleaseRow.value?.historyVersions ?? []);
const historyModalTitle = computed(() => (
  selectedReleaseRow.value ? `${selectedReleaseRow.value.componentName} - 历史版本` : '历史版本'
));

const releaseCatalogColumns = computed<UiDataTableColumn<ReleaseCatalogRow>[]>(() => {
  const columns: UiDataTableColumn<ReleaseCatalogRow>[] = [
    {
      title: '组件 / 工序',
      key: 'componentName',
      minWidth: 220,
      render: (row) => h('div', { class: 'release-cell' }, [
        h('strong', row.componentName),
        h('code', row.componentCode),
      ]),
    },
    {
      title: '类型',
      key: 'kind',
      width: 110,
      render: (row) => h(UiTag, {
        type: row.kind === 'host' ? 'default' : 'info',
        size: 'small',
        bordered: false,
      }, () => row.kindLabel),
    },
    { title: '当前版本', key: 'version', width: 120, render: (row) => h('strong', row.currentVersion.version) },
    {
      title: '状态',
      key: 'status',
      width: 100,
      render: (row) => h(UiTag, {
        type: statusTone(row.currentVersion.status),
        size: 'small',
        bordered: false,
      }, () => statusText(row.currentVersion.status)),
    },
    {
      title: '发布时间',
      key: 'publishedAtUtc',
      minWidth: 170,
      render: (row) => h('span', formatDate(row.currentVersion.publishedAtUtc)),
    },
    { title: '大小', key: 'packageSize', width: 110, render: (row) => h('span', formatSize(row.currentVersion.packageSize)) },
    {
      title: '更新内容',
      key: 'releaseNotes',
      minWidth: 280,
      render: (row) => h('div', { class: 'release-note-cell' }, [
        h('span', { class: 'release-note' }, formatReleaseNotes(row.currentVersion.releaseNotes)),
        h(UiButton, {
          size: 'tiny',
          secondary: true,
          type: 'info',
          onClick: () => openReleaseDetailModal(row.currentVersion, row),
        }, () => [h(Info, { size: 13 }), '详情']),
      ]),
    },
    {
      title: '历史版本',
      key: 'history',
      width: 130,
      render: (row) => row.historyVersions.length > 0
        ? h(UiButton, {
            size: 'tiny',
            secondary: true,
            type: 'info',
            onClick: () => openHistoryModal(row),
          }, () => [h(History, { size: 13 }), `查看 ${row.historyVersions.length}`])
        : h('span', { class: 'history-empty' }, '无历史版本'),
    },
  ];

  if (isPublishRoute.value) {
    columns.push({
      title: '操作',
      key: 'actions',
      width: 110,
      align: 'right',
      render: (row) => h('div', { class: 'row-actions' }, [
        h(UiButton, {
          size: 'tiny',
          secondary: true,
          type: 'primary',
          onClick: () => openUrl(row.currentVersion.downloadUrl),
        }, () => [h(ExternalLink, { size: 13 }), '打开']),
      ]),
    });
  }

  return columns;
});

const historyColumns = computed<UiDataTableColumn<ReleaseVersionEntry>[]>(() => {
  const columns: UiDataTableColumn<ReleaseVersionEntry>[] = [
    { title: '版本', key: 'version', width: 120, render: (row) => h('strong', row.version) },
    {
      title: '状态',
      key: 'status',
      width: 110,
      render: (row) => h(UiTag, { type: statusTone(row.status), size: 'small', bordered: false }, () => statusText(row.status)),
    },
    { title: '发布时间', key: 'publishedAtUtc', minWidth: 170, render: (row) => h('span', formatDate(row.publishedAtUtc)) },
    {
      title: '更新内容',
      key: 'releaseNotes',
      minWidth: 280,
      render: (row) => h('div', { class: 'release-note-cell' }, [
        h('span', { class: 'release-note' }, formatReleaseNotes(row.releaseNotes)),
        h(UiButton, {
          size: 'tiny',
          secondary: true,
          type: 'info',
          onClick: () => openReleaseDetailModal(row, selectedReleaseRow.value),
        }, () => [h(Info, { size: 13 }), '详情']),
      ]),
    },
  ];

  if (isPublishRoute.value) {
    columns.push({
      title: '操作',
      key: 'actions',
      width: 110,
      align: 'right',
      render: (row) => h('div', { class: 'row-actions' }, [
        h(UiButton, {
          size: 'tiny',
          secondary: true,
          type: 'primary',
          onClick: () => openUrl(row.downloadUrl),
        }, () => [h(ExternalLink, { size: 13 }), '打开']),
      ]),
    });
  }

  return columns;
});

const inventoryColumns: UiDataTableColumn<DeviceClientVersionInventoryDto>[] = [
  { title: '设备', key: 'deviceName', minWidth: 190, render: (row) => h('div', { class: 'device-cell' }, [h('strong', row.deviceName), h('code', row.clientCode)]) },
  { title: 'Channel', key: 'channel', width: 120, render: (row) => h('span', row.channel || '-') },
  { title: '宿主当前', key: 'hostVersion', width: 120, render: (row) => h('span', row.hostVersion || '-') },
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

.release-mode-label {
  display: inline-flex;
  align-items: center;
  gap: var(--space-2);
  height: 36px;
  border-radius: var(--radius-md);
  background: var(--bg-3);
  color: var(--text-0);
  padding: 0 var(--space-3);
  font-size: var(--fs-base);
  font-weight: var(--fw-semibold);
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

:deep(.release-cell),
:deep(.device-cell) {
  display: grid;
  gap: 3px;
  min-width: 0;
}

:deep(.release-cell strong),
:deep(.device-cell strong) {
  color: var(--text-0);
  font-weight: var(--fw-semibold);
}

:deep(.release-cell code),
:deep(.device-cell code) {
  color: var(--text-2);
  font-size: var(--fs-sm);
  font-weight: var(--fw-medium);
}

:deep(.release-note-cell) {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  align-items: center;
  gap: var(--space-2);
  min-width: 0;
}

:deep(.release-note) {
  display: -webkit-box;
  overflow: hidden;
  color: var(--text-1);
  line-height: 1.5;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
  white-space: normal;
}

:deep(.history-empty) {
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

.history-modal {
  display: grid;
  gap: var(--space-4);
}

.history-summary {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-3);
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  background: var(--bg-2);
  padding: var(--space-3) var(--space-4);
}

.history-summary > div {
  display: grid;
  gap: 3px;
}

.history-summary strong {
  color: var(--text-0);
  font-size: var(--fs-base);
  font-weight: var(--fw-semibold);
}

.history-summary code {
  color: var(--text-2);
  font-size: var(--fs-sm);
  font-weight: var(--fw-medium);
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

.release-detail-modal {
  display: grid;
  gap: var(--space-4);
}

.release-detail-summary {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-3);
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  background: var(--bg-2);
  padding: var(--space-3) var(--space-4);
}

.release-detail-heading {
  display: grid;
  gap: 3px;
}

.release-detail-heading strong {
  color: var(--text-0);
  font-size: var(--fs-base);
  font-weight: var(--fw-semibold);
}

.release-detail-heading code {
  color: var(--text-2);
  font-size: var(--fs-sm);
  font-weight: var(--fw-medium);
}

.release-detail-meta {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: var(--space-3);
}

.release-detail-meta > div {
  display: grid;
  gap: 6px;
  min-width: 0;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--card);
  padding: var(--space-3);
}

.release-detail-meta span {
  color: var(--text-2);
  font-size: var(--fs-sm);
  font-weight: var(--fw-medium);
}

.release-detail-meta strong {
  min-width: 0;
  color: var(--text-0);
  font-size: var(--fs-base);
  font-weight: var(--fw-semibold);
  overflow-wrap: anywhere;
}

.release-detail-notes {
  display: grid;
  gap: var(--space-3);
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  background: var(--bg-2);
  padding: var(--space-4);
}

.release-detail-notes h3 {
  margin: 0;
  color: var(--text-0);
  font-size: var(--fs-base);
  font-weight: var(--fw-semibold);
}

.release-detail-notes p {
  max-height: 360px;
  margin: 0;
  overflow: auto;
  color: var(--text-1);
  font-size: var(--fs-base);
  font-weight: var(--fw-medium);
  line-height: 1.7;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}

@media (max-width: 1180px) {
  .release-metrics {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 760px) {
  .release-metrics,
  .release-form,
  .release-detail-meta {
    grid-template-columns: 1fr;
  }

  .release-filters :deep(.ui-input),
  .release-filters :deep(input) {
    width: 100%;
  }
}
</style>
