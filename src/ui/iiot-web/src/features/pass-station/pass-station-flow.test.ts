import { beforeEach, describe, expect, it, vi } from 'vitest';
import { getScopedDeviceSelectApi } from '../devices/api';
import {
  exportPassStationsApi,
  getPassStationListApi,
  getPassStationTypesApi,
} from './api';
import { usePassStation } from './usePassStation';

vi.mock('../devices/api', () => ({
  getScopedDeviceSelectApi: vi.fn(),
}));
vi.mock('./api', () => ({
  exportPassStationsApi: vi.fn(),
  getPassStationDetailApi: vi.fn(),
  getPassStationListApi: vi.fn(),
  getPassStationTypesApi: vi.fn(),
}));
vi.mock('../../utils/feedback', () => ({
  notifySuccess: vi.fn(),
  notifyWarning: vi.fn(),
}));

const getScopedDevices = vi.mocked(getScopedDeviceSelectApi);
const getTypes = vi.mocked(getPassStationTypesApi);
const getList = vi.mocked(getPassStationListApi);
const exportCsv = vi.mocked(exportPassStationsApi);

const cpProcessId = '11111111-1111-1111-1111-111111111111';
const apProcessId = '22222222-2222-2222-2222-222222222222';

describe('pass station authorized context flow', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getScopedDevices.mockResolvedValue([
      {
        id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
        deviceName: '正极模切客户端',
        code: 'CP-01',
        processId: cpProcessId,
        processCode: 'CP',
        processName: '正极模切',
      },
      {
        id: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
        deviceName: '负极模切客户端',
        code: 'AP-01',
        processId: apProcessId,
        processCode: 'AP',
        processName: '负极模切',
      },
    ]);
    getTypes.mockResolvedValue([{
      typeKey: 'cp',
      displayName: '正极模切',
      description: '正极模切追溯',
      supportedModes: ['barcode-process', 'device-latest'],
      fields: [
        { key: 'plcCode', label: 'PLC 编码', type: 'string', required: true },
        { key: 'plcName', label: 'PLC 名称', type: 'string', required: true },
        { key: 'clipSlot', label: '弹夹位', type: 'enum', required: true, options: ['MG1', 'MG2'] },
      ],
      listColumns: ['plcCode', 'plcName', 'clipSlot', 'barcode'],
      detailSections: [{ title: '基础信息', fields: ['plcCode', 'plcName', 'clipSlot', 'barcode'] }],
    }]);
    getList.mockResolvedValue({
      items: [],
      metaData: { totalCount: 0, pageSize: 10, currentPage: 1, totalPages: 1 },
    });
  });

  it('derives selectable processes and devices only from the scoped device endpoint', async () => {
    const state = usePassStation();

    await state.fetchSelectData();

    expect(getScopedDevices).toHaveBeenCalledTimes(1);
    expect(state.processOptions.value).toEqual([
      { label: 'CP - 正极模切', value: cpProcessId },
    ]);
    expect(state.currentProcessId.value).toBe(cpProcessId);
    expect(state.deviceOptions.value).toEqual([
      { label: '正极模切客户端', value: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa' },
    ]);
  });

  it('keeps an explicit context error instead of converting a failed load to an empty list', async () => {
    getScopedDevices.mockRejectedValueOnce(new Error('network secret'));
    const state = usePassStation();

    await state.fetchSelectData();

    expect(state.selectError.value).toBe('授权设备与过站契约加载失败，请重试。');
    expect(state.processOptions.value).toEqual([]);
    expect(state.currentProcessId.value).toBeNull();
  });

  it('exports through the server with the same authorized filter contract', async () => {
    const createObjectUrl = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:pass-stations');
    const revokeObjectUrl = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
    exportCsv.mockResolvedValue({
      blob: new Blob(['csv']),
      fileName: 'pass-stations-cp.csv',
    });
    const state = usePassStation();
    await state.fetchSelectData();
    state.filters.barcode = 'CP-CLIP-001';

    await state.doExport();

    expect(exportCsv).toHaveBeenCalledWith(expect.objectContaining({
      typeKey: 'cp',
      mode: 'barcode-process',
      processId: cpProcessId,
      barcode: 'CP-CLIP-001',
    }));
    expect(createObjectUrl).toHaveBeenCalledTimes(1);
    expect(revokeObjectUrl).toHaveBeenCalledWith('blob:pass-stations');
    createObjectUrl.mockRestore();
    revokeObjectUrl.mockRestore();
  });
});
