import { computed, ref, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import {
  archiveClientReleaseApi,
  deleteClientReleaseFilesApi,
  getClientReleaseCatalogApi,
  getClientReleaseComponentDeletionsApi,
  getClientReleaseHistoryApi,
  getDeviceClientVersionInventoryApi,
  hardDeleteClientReleaseComponentApi,
  retryClientReleaseComponentDeletionApi,
  type ClientReleaseCatalogDto,
  type ClientReleaseComponentDeletionDto,
  type ClientReleaseHistoryComponentDto,
  type DeviceClientVersionInventoryDto,
} from './api';
import { createHistoryColumns, createInventoryColumns, createReleaseCatalogColumns } from './columns';
import { useAuthStore } from '../../stores/auth';
import { Permissions } from '../../types/permissions';
import { notifyError, notifySuccess, notifyWarning, requestConfirmation } from '../../utils/feedback';
import {
  formatDate,
  formatReleaseNotes,
  formatSize,
  isDeletionRetryComplete,
  pickCurrentVersion,
  statusText,
  statusTone,
  type ReleaseCatalogRow,
  type ReleaseDetail,
  type ReleaseVersionEntry,
  type ViewMode,
} from './types';

const HISTORY_PAGE_SIZE = 10;

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
  const showHistoryModal = ref(false);
  const showReleaseDetailModal = ref(false);
  const selectedReleaseRow = ref<ReleaseCatalogRow | null>(null);
  const selectedReleaseDetail = ref<ReleaseDetail | null>(null);

  // ===== 独立历史分页 =====
  const historyItems = ref<ClientReleaseHistoryComponentDto[]>([]);
  const historyTotal = ref(0);
  const historyPage = ref(1);
  const loadingHistory = ref(false);

  // ===== 永久删除确认弹窗 =====
  const showHardDeleteModal = ref(false);
  const hardDeleteTarget = ref<ReleaseCatalogRow | null>(null);
  const hardDeleteConfirmText = ref('');
  const hardDeleteReason = ref('');

  // ===== 删除恢复操作列表 =====
  const deletions = ref<ClientReleaseComponentDeletionDto[]>([]);
  const loadingDeletions = ref(false);
  const retryingDeletionId = ref<string | null>(null);

  const canGenerateInstaller = computed(() =>
    authStore.hasPermission(Permissions.ClientRelease.GenerateInstaller),
  );
  const canManageReleases = computed(() =>
    authStore.hasPermission(Permissions.ClientRelease.Manage),
  );
  // 永久删除必须同时满足管理员身份和 ClientRelease.HardDelete 权限。
  const canHardDelete = computed(
    () => authStore.isAdmin && authStore.hasPermission(Permissions.ClientRelease.HardDelete),
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
        componentId: currentHost.componentId,
        currentVersion: currentHost,
        historyVersions: hostVersions.filter((version) => version.id !== currentHost.id),
      });
    }

    for (const plugin of catalog.value?.plugins ?? []) {
      // 防御性跳过没有可见版本的空组件。
      if (plugin.versions.length === 0) continue;
      const currentPlugin = pickCurrentVersion(plugin.versions);
      if (!currentPlugin) continue;
      rows.push({
        key: `plugin:${plugin.moduleId}`,
        kind: 'plugin',
        kindLabel: '工序插件',
        componentName: plugin.displayName || plugin.moduleId,
        componentCode: plugin.moduleId,
        componentId: currentPlugin.componentId,
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
    canHardDelete: () => canHardDelete.value,
    onHistory: openHistoryModal,
    onDetail: openReleaseDetailModal,
    onOpenUrl: openUrl,
    onArchive: archiveReleaseVersion,
    onDeleteFiles: deleteReleaseFiles,
    onHardDelete: openHardDeleteModal,
  }));
  const historyColumns = computed(() => createHistoryColumns({
    isPublishRoute: () => isPublishRoute.value,
    selectedRow: () => selectedReleaseRow.value,
    onDetail: openReleaseDetailModal,
    onOpenUrl: openUrl,
    onArchive: archiveReleaseVersion,
    onDeleteFiles: deleteReleaseFiles,
  }));
  const inventoryColumns = createInventoryColumns();

  async function fetchCatalog() {
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

  async function fetchHistory() {
    loadingHistory.value = true;
    try {
      const result = await getClientReleaseHistoryApi({
        channel: channel.value,
        targetRuntime: targetRuntime.value,
        pageNumber: historyPage.value,
        pageSize: HISTORY_PAGE_SIZE,
      });
      historyItems.value = result.items;
      historyTotal.value = result.metaData.totalCount;
    } catch {
      historyItems.value = [];
      historyTotal.value = 0;
    } finally {
      loadingHistory.value = false;
    }
  }

  function gotoHistoryPage(page: number) {
    const totalPages = Math.max(1, Math.ceil(historyTotal.value / HISTORY_PAGE_SIZE));
    const clamped = Math.max(1, Math.min(totalPages, page));
    if (clamped !== historyPage.value) {
      historyPage.value = clamped;
      void fetchHistory();
    }
  }

  async function fetchDeletions() {
    if (!canHardDelete.value) {
      deletions.value = [];
      return;
    }
    loadingDeletions.value = true;
    try {
      deletions.value = await getClientReleaseComponentDeletionsApi();
    } catch {
      deletions.value = [];
    } finally {
      loadingDeletions.value = false;
    }
  }

  async function refresh() {
    const tasks = [fetchCatalog(), fetchInventory(), fetchHistory()];
    if (canHardDelete.value) tasks.push(fetchDeletions());
    await Promise.all(tasks);
  }

  function goPublishManager() {
    if (!canManageReleases.value) return;
    void router.push({ name: 'ClientReleasePublish' });
  }

  function goInstallerCenter() {
    activeView.value = canGenerateInstaller.value ? 'binding' : 'catalog';
    void router.push({ name: 'ClientReleases' });
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

  async function archiveReleaseVersion(version: ReleaseVersionEntry, row: ReleaseCatalogRow | null) {
    if (!canManageReleases.value) return;
    const confirmed = await requestConfirmation({
      title: '归档发布版本',
      message: `确认归档 ${row?.componentName ?? '组件'} ${version.version}？归档后不会作为可安装版本展示。`,
      confirmText: '确认归档',
    });
    if (!confirmed) return;

    submitting.value = true;
    try {
      await archiveClientReleaseApi(version.id);
      notifySuccess('发布版本已归档');
      await Promise.all([fetchCatalog(), fetchHistory()]);
    } catch {
      /* feedback handled by http client */
    } finally {
      submitting.value = false;
    }
  }

  async function deleteReleaseFiles(version: ReleaseVersionEntry, row: ReleaseCatalogRow | null) {
    if (!canManageReleases.value) return;
    const confirmed = await requestConfirmation({
      type: 'error',
      title: '删除发布文件',
      message: `确认删除 ${row?.componentName ?? '组件'} ${version.version} 的本机发布文件？如果仍有设备在用，后端会拒绝删除。`,
      confirmText: '确认删除文件',
    });
    if (!confirmed) return;

    submitting.value = true;
    try {
      const result = await deleteClientReleaseFilesApi(version.id);
      notifySuccess(result.warning || '发布文件已删除并归档');
      await Promise.all([fetchCatalog(), fetchHistory()]);
    } catch {
      /* feedback handled by http client */
    } finally {
      submitting.value = false;
    }
  }

  // ===== 永久删除 =====

  function openHardDeleteModal(row: ReleaseCatalogRow) {
    if (!canHardDelete.value) return;
    hardDeleteTarget.value = row;
    hardDeleteConfirmText.value = '';
    hardDeleteReason.value = '';
    showHardDeleteModal.value = true;
  }

  function closeHardDeleteModal() {
    showHardDeleteModal.value = false;
    hardDeleteTarget.value = null;
    hardDeleteConfirmText.value = '';
    hardDeleteReason.value = '';
  }

  async function submitHardDelete() {
    const target = hardDeleteTarget.value;
    if (!target || !canHardDelete.value) return;

    const expected = target.kind === 'plugin' ? target.componentCode : target.componentName;
    if (hardDeleteConfirmText.value.trim() !== expected) {
      notifyWarning(`请输入正确的确认内容：${expected}`);
      return;
    }
    const reason = hardDeleteReason.value.trim();
    if (!reason) {
      notifyWarning('请填写非空删除原因。');
      return;
    }

    submitting.value = true;
    try {
      // 必须使用组件 ID（componentId），禁止用 version.id。
      const result = await hardDeleteClientReleaseComponentApi(target.componentId, reason);
      closeHardDeleteModal();
      notifySuccess(result.warning || `已永久删除组件 ${result.componentName}。`);
      await Promise.all([fetchCatalog(), fetchHistory(), fetchDeletions()]);
    } catch {
      // 400 表示元数据可能已删除但文件清理或审计仍待恢复；不显示成功，刷新删除恢复列表。
      await fetchDeletions();
    } finally {
      submitting.value = false;
    }
  }

  // ===== 删除恢复重试 =====

  async function retryDeletion(deletion: ClientReleaseComponentDeletionDto) {
    if (!canHardDelete.value) return;
    retryingDeletionId.value = deletion.deletionId;
    try {
      const result = await retryClientReleaseComponentDeletionApi(deletion.deletionId);
      if (isDeletionRetryComplete(result)) {
        notifySuccess(`已永久删除组件 ${result.componentKey}。`);
        await Promise.all([fetchCatalog(), fetchHistory(), fetchDeletions()]);
      } else {
        notifyError(
          `组件 ${result.componentKey} 永久删除未完成（${result.failureCode ?? '待恢复'}），可在删除恢复列表中重试。`,
          { title: '删除待恢复' },
        );
        await fetchDeletions();
      }
    } catch {
      await fetchDeletions();
    } finally {
      retryingDeletionId.value = null;
    }
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
    showHistoryModal,
    showReleaseDetailModal,
    selectedReleaseRow,
    selectedReleaseDetail,
    historyItems,
    historyTotal,
    historyPage,
    loadingHistory,
    historyPageSize: HISTORY_PAGE_SIZE,
    showHardDeleteModal,
    hardDeleteTarget,
    hardDeleteConfirmText,
    hardDeleteReason,
    deletions,
    loadingDeletions,
    retryingDeletionId,
    canGenerateInstaller,
    canManageReleases,
    canHardDelete,
    activeView,
    isPublishRoute,
    pageTitle,
    pageSubtitle,
    channelDisplay,
    selectedHostPackageVersion,
    releaseCatalogRows,
    selectedHistoryVersions,
    historyModalTitle,
    releaseCatalogColumns,
    historyColumns,
    inventoryColumns,
    refresh,
    fetchHistory,
    gotoHistoryPage,
    fetchDeletions,
    goPublishManager,
    goInstallerCenter,
    archiveReleaseVersion,
    deleteReleaseFiles,
    openHardDeleteModal,
    closeHardDeleteModal,
    submitHardDelete,
    retryDeletion,
  };
}
