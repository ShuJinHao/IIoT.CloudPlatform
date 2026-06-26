import { computed, reactive, ref, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import {
  getClientReleaseCatalogApi,
  getDeviceClientVersionInventoryApi,
  upsertClientHostReleaseApi,
  upsertClientPluginReleaseApi,
  type ClientReleaseCatalogDto,
  type DeviceClientVersionInventoryDto,
} from './api';
import { createHistoryColumns, createInventoryColumns, createReleaseCatalogColumns } from './columns';
import { useAuthStore } from '../../stores/auth';
import { Permissions } from '../../types/permissions';
import { notifyWarning } from '../../utils/feedback';
import {
  formatDate,
  formatReleaseNotes,
  formatSize,
  getReleaseMetadataValidationMessage,
  normalizeOptional,
  pickCurrentVersion,
  statusText,
  statusTone,
  validateReleaseMetadata,
  type HostReleaseForm,
  type PluginReleaseForm,
  type ReleaseCatalogRow,
  type ReleaseDetail,
  type ReleaseVersionEntry,
  type ViewMode,
} from './types';

export function useClientReleases() {
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
  const showHistoryModal = ref(false);
  const showReleaseDetailModal = ref(false);
  const selectedReleaseRow = ref<ReleaseCatalogRow | null>(null);
  const selectedReleaseDetail = ref<ReleaseDetail | null>(null);

  const canGenerateInstaller = computed(() =>
    authStore.hasPermission(Permissions.ClientRelease.GenerateInstaller),
  );
  const canManageReleases = computed(() =>
    authStore.hasPermission(Permissions.ClientRelease.Manage),
  );
  const activeView = ref<ViewMode>(canGenerateInstaller.value ? 'binding' : 'catalog');
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

  const hostForm = reactive<HostReleaseForm>({
    channel: 'stable',
    version: '',
    hostApiVersion: '1.0.0',
    targetRuntime: 'win-x64',
    targetFramework: 'net10.0',
    downloadUrl: '',
    sha256: '',
    packageSize: '',
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
    sha256: '',
    packageSize: '',
    releaseNotes: '',
    dependenciesJson: '[]',
    status: 'Draft',
    signature: '',
    publisher: '',
  });

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

  const releaseCatalogColumns = computed(() => createReleaseCatalogColumns({
    isPublishRoute: () => isPublishRoute.value,
    onHistory: openHistoryModal,
    onDetail: openReleaseDetailModal,
    onOpenUrl: openUrl,
  }));
  const historyColumns = computed(() => createHistoryColumns({
    isPublishRoute: () => isPublishRoute.value,
    selectedRow: () => selectedReleaseRow.value,
    onDetail: openReleaseDetailModal,
    onOpenUrl: openUrl,
  }));
  const inventoryColumns = createInventoryColumns();

  async function fetchCatalog() {
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
  }

  async function fetchInventory() {
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
  }

  async function refresh() {
    await Promise.all([fetchCatalog(), fetchInventory()]);
  }

  function goPublishManager() {
    if (!canManageReleases.value) return;
    void router.push({ name: 'ClientReleasePublish' });
  }

  function goInstallerCenter() {
    activeView.value = canGenerateInstaller.value ? 'binding' : 'catalog';
    void router.push({ name: 'ClientReleases' });
  }

  function openHostModal() {
    if (!canManageReleases.value) return;
    Object.assign(hostForm, {
      channel: channel.value || 'stable',
      version: '',
      hostApiVersion: selectedHostPackage.value?.hostApiVersion || '1.0.0',
      targetRuntime: targetRuntime.value || 'win-x64',
      targetFramework: 'net10.0',
      downloadUrl: '',
      sha256: '',
      packageSize: '',
      releaseNotes: '',
      status: 'Draft',
      signature: '',
      publisher: '',
    });
    showHostModal.value = true;
  }

  function openPluginModal() {
    if (!canManageReleases.value) return;
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
      sha256: '',
      packageSize: '',
      releaseNotes: '',
      dependenciesJson: '[]',
      status: 'Draft',
      signature: '',
      publisher: '',
    });
    showPluginModal.value = true;
  }

  async function submitHostRelease() {
    if (!hostForm.version.trim() || !hostForm.downloadUrl.trim()) {
      notifyWarning('请填写宿主版本和下载地址。');
      return;
    }

    const metadataMessage = getReleaseMetadataValidationMessage(hostForm);
    if (metadataMessage) {
      notifyWarning(metadataMessage);
      return;
    }
    const packageSize = validateReleaseMetadata(hostForm);
    if (packageSize === null) return;

    submitting.value = true;
    try {
      await upsertClientHostReleaseApi({
        ...hostForm,
        channel: hostForm.channel.trim(),
        version: hostForm.version.trim(),
        hostApiVersion: hostForm.hostApiVersion.trim(),
        targetRuntime: hostForm.targetRuntime.trim(),
        targetFramework: normalizeOptional(hostForm.targetFramework),
        downloadUrl: hostForm.downloadUrl.trim(),
        sha256: hostForm.sha256.trim(),
        packageSize,
        releaseNotes: normalizeOptional(hostForm.releaseNotes),
        signature: normalizeOptional(hostForm.signature),
        publisher: normalizeOptional(hostForm.publisher),
      });
      showHostModal.value = false;
      await fetchCatalog();
    } finally {
      submitting.value = false;
    }
  }

  async function submitPluginRelease() {
    if (!pluginForm.moduleId.trim() || !pluginForm.displayName.trim() || !pluginForm.version.trim()) {
      notifyWarning('请填写插件模块、名称和版本。');
      return;
    }

    const metadataMessage = getReleaseMetadataValidationMessage(pluginForm);
    if (metadataMessage) {
      notifyWarning(metadataMessage);
      return;
    }
    const packageSize = validateReleaseMetadata(pluginForm);
    if (packageSize === null) return;

    submitting.value = true;
    try {
      await upsertClientPluginReleaseApi({
        ...pluginForm,
        moduleId: pluginForm.moduleId.trim(),
        displayName: pluginForm.displayName.trim(),
        description: normalizeOptional(pluginForm.description),
        iconKind: normalizeOptional(pluginForm.iconKind),
        accentColor: normalizeOptional(pluginForm.accentColor),
        channel: pluginForm.channel.trim(),
        version: pluginForm.version.trim(),
        hostApiVersion: pluginForm.hostApiVersion.trim(),
        minHostVersion: pluginForm.minHostVersion.trim(),
        maxHostVersion: pluginForm.maxHostVersion.trim(),
        targetRuntime: pluginForm.targetRuntime.trim(),
        targetFramework: normalizeOptional(pluginForm.targetFramework),
        downloadUrl: pluginForm.downloadUrl.trim(),
        sha256: pluginForm.sha256.trim(),
        packageSize,
        releaseNotes: normalizeOptional(pluginForm.releaseNotes),
        dependenciesJson: normalizeOptional(pluginForm.dependenciesJson),
        signature: normalizeOptional(pluginForm.signature),
        publisher: normalizeOptional(pluginForm.publisher),
      });
      showPluginModal.value = false;
      await fetchCatalog();
    } finally {
      submitting.value = false;
    }
  }

  function openHistoryModal(row: ReleaseCatalogRow) {
    if (row.historyVersions.length === 0) return;
    selectedReleaseRow.value = row;
    showHistoryModal.value = true;
  }

  function openReleaseDetailModal(version: ReleaseVersionEntry, row: ReleaseCatalogRow | null) {
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
  }

  function openUrl(url: string) {
    if (!url) return;
    window.open(url, '_blank', 'noopener,noreferrer');
  }

  watch(activeView, () => {
    if (activeView.value === 'inventory' && inventory.value.length === 0) {
      void fetchInventory();
    }
  });

  watch(canGenerateInstaller, (value) => {
    if (!value && activeView.value === 'binding') {
      activeView.value = 'catalog';
    }
  }, { immediate: true });

  return {
    channel,
    targetRuntime,
    keyword,
    catalog,
    inventory,
    loadingCatalog,
    loadingInventory,
    submitting,
    showHostModal,
    showPluginModal,
    showHistoryModal,
    showReleaseDetailModal,
    selectedReleaseRow,
    selectedReleaseDetail,
    canGenerateInstaller,
    canManageReleases,
    activeView,
    isPublishRoute,
    pageTitle,
    pageSubtitle,
    channelDisplay,
    selectedHostPackageVersion,
    hostForm,
    pluginForm,
    releaseCatalogRows,
    selectedHistoryVersions,
    historyModalTitle,
    releaseCatalogColumns,
    historyColumns,
    inventoryColumns,
    refresh,
    goPublishManager,
    goInstallerCenter,
    openHostModal,
    openPluginModal,
    submitHostRelease,
    submitPluginRelease,
  };
}
