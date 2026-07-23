import { computed, ref, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import axios from 'axios';
import {
  archiveClientReleaseApi,
  deleteClientReleaseFilesApi,
  getClientReleaseCatalogApi,
  getClientReleaseComponentDeletionsApi,
  getClientReleaseHistoryApi,
  hardDeleteClientReleaseComponentApi,
  retryClientReleaseComponentDeletionApi,
  type ClientReleaseCatalogDto,
  type ClientReleaseComponentDeletionDto,
  type ClientReleaseHistoryComponentDto,
} from './api';
import { createHistoryColumns, createReleaseCatalogColumns } from './columns';
import { useAuthStore } from '../../stores/auth';
import { Permissions } from '../../types/permissions';
import { useListPage } from '../../core/list-page';
import { isApiResult, type ApiResult } from '../../core/types/api';
import { readProblemDetails, resolveProblemNotification } from '../../core/http/problemDetails';
import { notifyError, notifySuccess, requestConfirmation } from '../../utils/feedback';
import {
  formatDate,
  formatReleaseNotes,
  formatSize,
  isDeletionRetryComplete,
  pickCurrentVersion,
  statusText,
  statusTone,
  type HardDeleteProblem,
  type ReleaseCatalogRow,
  type ReleaseDetail,
  type ReleaseVersionEntry,
  type ViewMode,
} from './types';

const HISTORY_PAGE_SIZE = 10;

function isAxiosErrorWithResponse(error: unknown): error is { response: { status: number; data: unknown } } {
  return axios.isAxiosError(error) && error.response !== undefined;
}

function isApiResultError(error: unknown): error is ApiResult<unknown> & { status: number } {
  return isApiResult(error);
}

export function useClientReleases() {
  const authStore = useAuthStore();
  const route = useRoute();
  const router = useRouter();
  const channel = ref('stable');
  const targetRuntime = ref('win-x64');
  const catalog = ref<ClientReleaseCatalogDto | null>(null);
  const loadingCatalog = ref(false);
  const submitting = ref(false);
  const showHistoryModal = ref(false);
  const showReleaseDetailModal = ref(false);
  const selectedReleaseRow = ref<ReleaseCatalogRow | null>(null);
  const selectedReleaseDetail = ref<ReleaseDetail | null>(null);

  // ===== 独立历史分页（统一 useListPage，loading/empty/error 分离，后端真实分页） =====
  const history = useListPage<ClientReleaseHistoryComponentDto, Record<string, unknown>>({
    initialPageSize: HISTORY_PAGE_SIZE,
    immediate: false,
    fetcher: async ({ page, pageSize }) => {
      const result = await getClientReleaseHistoryApi({
        channel: channel.value,
        targetRuntime: targetRuntime.value,
        pageNumber: page,
        pageSize,
      });
      return { items: result.items, total: result.metaData.totalCount };
    },
  });

  // ===== 永久删除确认弹窗 =====
  const showHardDeleteModal = ref(false);
  const hardDeleteTarget = ref<ReleaseCatalogRow | null>(null);
  const hardDeleteConfirmText = ref('');
  const hardDeleteReason = ref('');
  const hardDeleteProblem = ref<HardDeleteProblem | null>(null);

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
        otherVersions: hostVersions.filter((version) => version.id !== currentHost.id),
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
        otherVersions: plugin.versions.filter((version) => version.id !== currentPlugin.id),
      });
    }

    return rows;
  });
  const selectedOtherVersions = computed(() => selectedReleaseRow.value?.otherVersions ?? []);
  const historyModalTitle = computed(() => (
    selectedReleaseRow.value ? `${selectedReleaseRow.value.componentName} - 其他活动版本` : '其他活动版本'
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

  async function fetchCatalog() {
    loadingCatalog.value = true;
    try {
      catalog.value = await getClientReleaseCatalogApi({
        channel: channel.value,
        targetRuntime: targetRuntime.value,
      });
    } catch {
      catalog.value = null;
    } finally {
      loadingCatalog.value = false;
    }
  }

  async function fetchHistory() {
    await history.refresh();
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
    const tasks = [fetchCatalog(), history.refresh()];
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
    if (row.otherVersions.length === 0) return;
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
    hardDeleteProblem.value = null;
    showHardDeleteModal.value = true;
  }

  function closeHardDeleteModal() {
    showHardDeleteModal.value = false;
    hardDeleteTarget.value = null;
    hardDeleteConfirmText.value = '';
    hardDeleteReason.value = '';
    hardDeleteProblem.value = null;
  }

  function extractDeletionId(text?: string): string | undefined {
    if (!text) return undefined;
    const match = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i.exec(text);
    return match?.[0];
  }

  async function buildHardDeleteProblem(error: unknown): Promise<HardDeleteProblem> {
    let payload: unknown;
    let status = 0;
    if (isAxiosErrorWithResponse(error)) {
      status = error.response.status;
      payload = error.response.data;
    } else if (isApiResultError(error)) {
      status = error.status;
      payload = { errors: error.errors };
    }

    const problem = await readProblemDetails(payload);
    const notification = resolveProblemNotification(status, problem);
    const detail = problem?.detail?.trim() || notification.message;
    const errors = notification.details;
    const deletionId =
      extractDeletionId(detail) ?? errors.map(extractDeletionId).find((id) => id);
    return {
      title: notification.title,
      detail,
      errors,
      deletionId,
    };
  }

  async function submitHardDelete() {
    const target = hardDeleteTarget.value;
    if (!target || !canHardDelete.value) return;

    // 确认内容与原因为空的拦截已在 modal 内联完成；此处仅做最终 trim。
    const reason = hardDeleteReason.value.trim();
    if (!reason) return;

    hardDeleteProblem.value = null;
    submitting.value = true;
    try {
      // 必须使用组件 ID（componentId），禁止用 version.id。
      const result = await hardDeleteClientReleaseComponentApi(target.componentId, reason);
      closeHardDeleteModal();
      notifySuccess(result.warning || `已永久删除组件 ${result.componentName}。`);
      await Promise.all([fetchCatalog(), fetchHistory(), fetchDeletions()]);
    } catch (error) {
      // 400 表示元数据可能已删除但文件清理或审计仍待恢复：不显示成功，
      // 在弹窗内联展示真实 ProblemDetails 与 deletionId，并刷新删除恢复列表。
      hardDeleteProblem.value = await buildHardDeleteProblem(error);
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

  watch(canGenerateInstaller, (value) => {
    if (!value && activeView.value === 'binding') {
      activeView.value = 'catalog';
    }
  }, { immediate: true });

  return {
    channel,
    targetRuntime,
    catalog,
    loadingCatalog,
    submitting,
    showHistoryModal,
    showReleaseDetailModal,
    selectedReleaseRow,
    selectedReleaseDetail,
    historyItems: history.items,
    historyTotal: history.total,
    historyPage: history.page,
    historyTotalPages: history.totalPages,
    loadingHistory: history.loading,
    historyError: history.error,
    historyIsEmpty: history.isEmpty,
    historyPageSize: HISTORY_PAGE_SIZE,
    showHardDeleteModal,
    hardDeleteTarget,
    hardDeleteConfirmText,
    hardDeleteReason,
    hardDeleteProblem,
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
    selectedOtherVersions,
    historyModalTitle,
    releaseCatalogColumns,
    historyColumns,
    refresh,
    fetchHistory,
    gotoHistoryPage: history.gotoPage,
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
