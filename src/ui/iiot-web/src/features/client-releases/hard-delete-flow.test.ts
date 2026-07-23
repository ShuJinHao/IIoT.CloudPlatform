import { flushPromises, mount } from '@vue/test-utils';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, nextTick, reactive } from 'vue';
import type {
  ClientReleaseCatalogDto,
  ClientReleaseComponentDeletionDto,
  ClientReleaseComponentDeletionRetryResultDto,
  ClientReleaseHistoryComponentDto,
  ClientReleaseHistoryPageDto,
  ClientReleaseHardDeletionResultDto,
  ClientHostVersionEntryDto,
} from './api';
import ReleaseHardDeleteModal, { type HardDeleteProblem } from './ReleaseHardDeleteModal.vue';
import { useClientReleases } from './useClientReleases';
import type { ReleaseCatalogRow } from './types';

// ===== API 模块整体打桩：组件/组合式函数走真实流程，只有边界是假的 =====
const apiMocks = vi.hoisted(() => ({
  getClientReleaseCatalogApi: vi.fn(),
  getClientReleaseHistoryApi: vi.fn(),
  getDeviceClientVersionInventoryApi: vi.fn(),
  getClientReleaseComponentDeletionsApi: vi.fn(),
  hardDeleteClientReleaseComponentApi: vi.fn(),
  retryClientReleaseComponentDeletionApi: vi.fn(),
  archiveClientReleaseApi: vi.fn(),
  deleteClientReleaseFilesApi: vi.fn(),
}));

vi.mock('./api', () => ({
  ...apiMocks,
  getClientReleaseRetentionPolicyApi: vi.fn(),
  updateClientReleaseRetentionPolicyApi: vi.fn(),
  updateClientReleaseStatusApi: vi.fn(),
  generateEdgeInstallerPackageApi: vi.fn(),
}));

const feedbackMocks = vi.hoisted(() => ({
  notifySuccess: vi.fn(),
  notifyError: vi.fn(),
  notifyWarning: vi.fn(),
  requestConfirmation: vi.fn(),
}));

vi.mock('../../utils/feedback', () => ({
  ...feedbackMocks,
  notifyInfo: vi.fn(),
}));

const authMock = vi.hoisted(() => ({
  isAdmin: true,
  permissionResult: true,
}));

vi.mock('../../stores/auth', () => ({
  useAuthStore: () => ({
    isAdmin: authMock.isAdmin,
    hasPermission: () => authMock.permissionResult,
  }),
}));

vi.mock('vue-router', () => ({
  useRoute: () => ({ name: 'ClientReleasePublish' }),
  useRouter: () => ({ push: vi.fn() }),
}));

const COMPONENT_ID = '2b8f1c0d-1111-4222-8333-444455556666';
const DELETION_ID = '9d4e5f6a-7777-4888-8999-aaaabbbbcccc';

function makeHostVersion(overrides: Partial<ClientHostVersionEntryDto> = {}): ClientHostVersionEntryDto {
  return {
    id: 'version-id-1',
    componentId: COMPONENT_ID,
    channel: 'stable',
    version: '2.4.0',
    hostApiVersion: '1.0.0',
    targetRuntime: 'win-x64',
    targetFramework: 'net10.0',
    downloadUrl: '/edge-updates/host/IIoT.EdgeClient.exe',
    sha256: 'a'.repeat(64),
    packageSize: 2048,
    releaseNotes: '',
    status: 'Published',
    createdAtUtc: '2026-07-01T00:00:00Z',
    publishedAtUtc: '2026-07-02T00:00:00Z',
    deletedAtUtc: null,
    deletionReason: null,
    deletionFailure: null,
    ...overrides,
  };
}

function makeCatalog(): ClientReleaseCatalogDto {
  return {
    catalogSchemaVersion: 1,
    channel: 'stable',
    targetRuntime: 'win-x64',
    host: {
      componentKind: 'Host',
      displayName: '通用宿主',
      versions: [makeHostVersion()],
    },
    plugins: [],
    generatedAtUtc: '2026-07-20T00:00:00Z',
  };
}

function makeHistoryPage(items: ClientReleaseHistoryComponentDto[], totalCount = items.length): ClientReleaseHistoryPageDto {
  return {
    items,
    metaData: {
      currentPage: 1,
      totalPages: Math.max(1, Math.ceil(totalCount / 10)),
      pageSize: 10,
      totalCount,
    },
  };
}

function makeHardDeleteResult(): ClientReleaseHardDeletionResultDto {
  return {
    deletionId: DELETION_ID,
    componentId: COMPONENT_ID,
    componentKind: 'Host',
    componentName: '通用宿主',
    channel: 'stable',
    versions: ['2.4.0'],
    filesDeleted: true,
    deletedPaths: ['edge-updates/host/2.4.0'],
    skippedPaths: [],
    warning: null,
  };
}

function makeDeletionDto(overrides: Partial<ClientReleaseComponentDeletionDto> = {}): ClientReleaseComponentDeletionDto {
  return {
    deletionId: DELETION_ID,
    componentId: COMPONENT_ID,
    componentKind: 'Host',
    componentKey: '通用宿主 (win-x64)',
    channel: 'stable',
    targetRuntime: 'win-x64',
    status: 'Failed',
    failureCode: 'FileDeletionFailed',
    retryCount: 1,
    reason: '退役旧组件',
    requestedByUserName: 'admin',
    createdAtUtc: '2026-07-20T01:00:00Z',
    updatedAtUtc: '2026-07-20T02:00:00Z',
    ...overrides,
  };
}

function makeRetryResult(overrides: Partial<ClientReleaseComponentDeletionRetryResultDto> = {}): ClientReleaseComponentDeletionRetryResultDto {
  return {
    deletionId: DELETION_ID,
    componentId: COMPONENT_ID,
    componentKind: 'Host',
    componentKey: '通用宿主 (win-x64)',
    channel: 'stable',
    succeeded: true,
    auditConfirmed: true,
    deletedPaths: [],
    skippedPaths: [],
    failureCode: null,
    ...overrides,
  };
}

function mockHappyApis() {
  apiMocks.getClientReleaseCatalogApi.mockResolvedValue(makeCatalog());
  apiMocks.getClientReleaseHistoryApi.mockResolvedValue(makeHistoryPage([]));
  apiMocks.getDeviceClientVersionInventoryApi.mockResolvedValue([]);
  apiMocks.getClientReleaseComponentDeletionsApi.mockResolvedValue([]);
}

function makeRow(): ReleaseCatalogRow {
  const version = makeHostVersion();
  return {
    key: 'host:stable:win-x64',
    kind: 'host',
    kindLabel: '宿主',
    componentName: '通用宿主',
    componentCode: 'win-x64',
    componentId: COMPONENT_ID,
    currentVersion: version,
    otherVersions: [],
  };
}

function mountModal(problem: HardDeleteProblem | null = null) {
  const events = { submitCount: 0, cancelCount: 0 };
  // UiModal 走 Teleport 渲染到 body：用全局选择器交互，触发的是真实 DOM 事件流。
  // mount props 的引用值不会随回调更新自动重传，必须用模板驱动的 harness 保持 v-model 同步。
  const Harness = defineComponent({
    components: { ReleaseHardDeleteModal },
    setup() {
      const state = reactive({ show: true, confirmText: '', reason: '' });
      return { state, problem, target: makeRow(), events };
    },
    template: `
      <ReleaseHardDeleteModal
        v-model:show="state.show"
        v-model:confirm-text="state.confirmText"
        v-model:reason="state.reason"
        :target="target"
        :submitting="false"
        :problem="problem"
        @submit="events.submitCount += 1"
        @cancel="events.cancelCount += 1"
      />`,
  });
  const wrapper = mount(Harness, { attachTo: document.body });
  const state = (wrapper.vm as unknown as { state: { show: boolean; confirmText: string; reason: string } }).state;
  return { wrapper, state, events };
}

async function fillModal(confirm: string, reason: string) {
  const input = document.body.querySelector<HTMLInputElement>('.ui-modal input');
  const textarea = document.body.querySelector<HTMLTextAreaElement>('.ui-modal textarea');
  expect(input, '确认输入框必须存在').toBeTruthy();
  expect(textarea, '原因输入框必须存在').toBeTruthy();
  input!.value = confirm;
  input!.dispatchEvent(new Event('input', { bubbles: true }));
  textarea!.value = reason;
  textarea!.dispatchEvent(new Event('input', { bubbles: true }));
  await nextTick();
}

async function clickHardDeleteButton() {
  const button = [...document.body.querySelectorAll<HTMLButtonElement>('.ui-modal button')]
    .find((item) => item.textContent?.trim() === '永久删除');
  expect(button, '永久删除按钮必须存在').toBeTruthy();
  button!.click();
  await nextTick();
}

function modalText(): string {
  return document.body.querySelector('.ui-modal')?.textContent ?? '';
}

async function expectInlineError(expected: string) {
  // 无效输入时模态框必须内联提示，且不发出 submit。
  await vi.waitFor(() => {
    expect(modalText()).toContain(expected);
  });
}

describe('ReleaseHardDeleteModal（真实组件挂载）', () => {
  beforeEach(() => {
    document.body.innerHTML = '';
  });

  it('确认内容不正确时内联提示且不发出 submit', async () => {
    const { events } = mountModal();
    await fillModal('错误的确认内容', '退役旧组件');
    await clickHardDeleteButton();

    await expectInlineError('确认内容不正确，请输入：通用宿主');
    expect(events.submitCount).toBe(0);
    expect(document.body.querySelector('.hard-delete-error')).toBeTruthy();
  });

  it('删除原因为空白时内联提示且不发出 submit', async () => {
    const { events } = mountModal();
    await fillModal('通用宿主', '   ');
    await clickHardDeleteButton();

    await expectInlineError('请填写非空删除原因。');
    expect(events.submitCount).toBe(0);
  });

  it('确认内容匹配且原因非空时才发出 submit', async () => {
    const { events } = mountModal();
    await fillModal('通用宿主', '退役旧组件');
    await clickHardDeleteButton();

    await vi.waitFor(() => {
      expect(events.submitCount).toBe(1);
    });
  });

  it('problem 传入时在弹窗内联展示 title/detail/errors/deletionId', async () => {
    mountModal({
      title: '请求处理失败',
      detail: `组件元数据已删除，但发布文件清理失败，已登记恢复操作 ${DELETION_ID}`,
      errors: ['edge-updates/host/2.4.0 文件被占用'],
      deletionId: DELETION_ID,
    });
    await nextTick();

    const block = document.body.querySelector('.hard-delete-problem');
    expect(block, 'ProblemDetails 区块必须在弹窗内渲染').toBeTruthy();
    expect(block!.textContent).toContain('请求处理失败');
    expect(block!.textContent).toContain('发布文件清理失败');
    expect(block!.textContent).toContain('文件被占用');
    expect(block!.textContent).toContain(DELETION_ID);
    expect(block!.textContent).toContain('删除恢复');
  });
});

describe('useClientReleases 永久删除/重试/历史流（API 打桩、逻辑真实）', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    authMock.isAdmin = true;
    authMock.permissionResult = true;
    mockHappyApis();
  });

  async function setupReadyState() {
    const state = useClientReleases();
    await state.refresh();
    expect(state.releaseCatalogRows.value).toHaveLength(1);
    return state;
  }

  it('canHardDelete 要求管理员身份和 HardDelete 权限同时成立', async () => {
    authMock.isAdmin = true;
    authMock.permissionResult = true;
    let state = useClientReleases();
    expect(state.canHardDelete.value).toBe(true);

    authMock.isAdmin = false;
    state = useClientReleases();
    expect(state.canHardDelete.value).toBe(false);

    authMock.isAdmin = true;
    authMock.permissionResult = false;
    state = useClientReleases();
    expect(state.canHardDelete.value).toBe(false);
  });

  it('提交永久删除：用 componentId 和 trim 后的原因发请求，成功后关闭弹窗并提示', async () => {
    apiMocks.hardDeleteClientReleaseComponentApi.mockResolvedValue(makeHardDeleteResult());
    const state = await setupReadyState();

    state.openHardDeleteModal(state.releaseCatalogRows.value[0]!);
    state.hardDeleteReason.value = '  退役旧组件  ';
    await state.submitHardDelete();

    expect(apiMocks.hardDeleteClientReleaseComponentApi).toHaveBeenCalledWith(COMPONENT_ID, '退役旧组件');
    expect(state.showHardDeleteModal.value).toBe(false);
    expect(feedbackMocks.notifySuccess).toHaveBeenCalledTimes(1);
    expect(state.hardDeleteProblem.value).toBeNull();
  });

  it('原因为空白时直接拦截，不发 DELETE', async () => {
    const state = await setupReadyState();
    state.openHardDeleteModal(state.releaseCatalogRows.value[0]!);
    state.hardDeleteReason.value = '   ';
    await state.submitHardDelete();

    expect(apiMocks.hardDeleteClientReleaseComponentApi).not.toHaveBeenCalled();
  });

  it('HTTP 400 时弹窗保留并内联展示真实 ProblemDetails 和 deletionId，不伪造成功', async () => {
    const axiosError = Object.assign(new Error('Request failed with status code 400'), {
      isAxiosError: true,
      response: {
        status: 400,
        data: {
          title: 'Bad Request',
          status: 400,
          detail: `组件元数据已删除，但发布文件清理失败，已登记恢复操作 ${DELETION_ID}`,
          errors: ['edge-updates/host/2.4.0 文件被占用'],
        },
      },
    });
    apiMocks.hardDeleteClientReleaseComponentApi.mockRejectedValue(axiosError);
    apiMocks.getClientReleaseComponentDeletionsApi.mockResolvedValue([makeDeletionDto()]);
    const state = await setupReadyState();

    state.openHardDeleteModal(state.releaseCatalogRows.value[0]!);
    state.hardDeleteReason.value = '退役旧组件';
    await state.submitHardDelete();

    expect(feedbackMocks.notifySuccess).not.toHaveBeenCalled();
    expect(state.showHardDeleteModal.value).toBe(true);
    const problem = state.hardDeleteProblem.value;
    expect(problem).not.toBeNull();
    expect(problem!.detail).toContain('发布文件清理失败');
    expect(problem!.errors).toContain('edge-updates/host/2.4.0 文件被占用');
    expect(problem!.deletionId).toBe(DELETION_ID);
    // 400 之后刷新删除恢复列表，让用户看到待恢复操作
    expect(state.deletions.value).toHaveLength(1);
    expect(state.deletions.value[0]!.deletionId).toBe(DELETION_ID);
  });

  it('重试只有 succeeded 且 auditConfirmed 都为 true 才提示成功', async () => {
    apiMocks.getClientReleaseComponentDeletionsApi.mockResolvedValue([makeDeletionDto()]);
    const state = await setupReadyState();
    const deletion = state.deletions.value[0]!;

    // auditConfirmed = false：不允许提示成功
    apiMocks.retryClientReleaseComponentDeletionApi.mockResolvedValue(
      makeRetryResult({ succeeded: true, auditConfirmed: false, failureCode: 'FileFactsMismatch' }),
    );
    await state.retryDeletion(deletion);
    expect(feedbackMocks.notifySuccess).not.toHaveBeenCalled();
    expect(feedbackMocks.notifyError).toHaveBeenCalledTimes(1);
    expect(feedbackMocks.notifyError.mock.calls[0]![0]).toContain('FileFactsMismatch');

    // 双条件都满足：提示成功
    feedbackMocks.notifyError.mockClear();
    apiMocks.retryClientReleaseComponentDeletionApi.mockResolvedValue(makeRetryResult());
    await state.retryDeletion(deletion);
    expect(feedbackMocks.notifySuccess).toHaveBeenCalledTimes(1);
    expect(feedbackMocks.notifyError).not.toHaveBeenCalled();
  });

  it('历史区区分 loading/empty/error，翻页携带真实页码', async () => {
    const historyItem: ClientReleaseHistoryComponentDto = {
      componentId: COMPONENT_ID,
      componentKind: 'Host',
      moduleId: '',
      displayName: '通用宿主',
      channel: 'stable',
      targetRuntime: 'win-x64',
      versions: [{
        id: 'history-version-1',
        version: '1.9.0',
        status: 'Deleted',
        createdAtUtc: '2026-06-01T00:00:00Z',
        publishedAtUtc: '2026-06-02T00:00:00Z',
        deletedAtUtc: '2026-07-01T00:00:00Z',
        deletionReason: '退役',
        deletionFailure: null,
      }],
    };
    const deferred = <T,>() => {
      let resolve!: (value: T) => void;
      let reject!: (error: unknown) => void;
      const promise = new Promise<T>((res, rej) => { resolve = res; reject = rej; });
      return { promise, resolve, reject };
    };

    // 首轮：历史请求挂起 → loading 可见
    const first = deferred<ClientReleaseHistoryPageDto>();
    apiMocks.getClientReleaseHistoryApi.mockReturnValue(first.promise);
    const state = useClientReleases();
    const refreshing = state.refresh();
    await nextTick();
    expect(state.loadingHistory.value).toBe(true);
    first.resolve(makeHistoryPage([historyItem], 25));
    await refreshing;
    expect(state.loadingHistory.value).toBe(false);
    expect(state.historyError.value).toBeNull();
    expect(state.historyItems.value).toHaveLength(1);
    expect(state.historyTotal.value).toBe(25);

    // 翻页：携带真实 pageNumber
    apiMocks.getClientReleaseHistoryApi.mockResolvedValue(makeHistoryPage([], 25));
    state.gotoHistoryPage(2);
    await flushPromises();
    expect(apiMocks.getClientReleaseHistoryApi).toHaveBeenLastCalledWith(
      expect.objectContaining({ pageNumber: 2, pageSize: 10 }),
    );

    // 失败：error 与空态分离
    apiMocks.getClientReleaseHistoryApi.mockRejectedValue(new Error('network down'));
    await state.fetchHistory();
    expect(state.historyError.value).toBeTruthy();
    expect(state.loadingHistory.value).toBe(false);
  });
});
