import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute, useRouter, type LocationQueryRaw } from 'vue-router';
import type { UiSelectOption } from '../../components/ui/types';
import { resolveRequestErrorMessage } from '../../core/http/resolveRequestError';
import {
  getScopedDeviceSelectApi,
  type ScopedDeviceSelectDto,
} from '../../features/devices/api';
import type {
  ProductionContext,
  ProductionContextState,
  ProductionContextStatus,
  ProductionDeviceContext,
  ProductionProcessContext,
} from './types';

const contextLoadFallback = '授权设备加载失败，请检查服务状态后重试。';

function queryValue(value: unknown): string | null {
  if (typeof value === 'string' && value.trim()) return value;
  if (Array.isArray(value) && typeof value[0] === 'string' && value[0].trim()) {
    return value[0];
  }
  return null;
}

function toDeviceContext(device: ScopedDeviceSelectDto): ProductionDeviceContext {
  return {
    deviceId: device.id,
    deviceCode: device.code,
    deviceName: device.deviceName,
    processId: device.processId,
    processCode: device.processCode,
    processName: device.processName,
  };
}

export function resolveProductionContextState(input: {
  status: ProductionContextStatus;
  authorizedDeviceCount: number;
  hasSelectedProcess: boolean;
  processDeviceCount: number;
  hasSelectedDevice: boolean;
}): ProductionContextState {
  if (input.status === 'idle' || input.status === 'loading') return 'loading';
  if (input.status === 'error') return 'error';
  if (input.authorizedDeviceCount === 0) return 'no-authorized-devices';
  if (!input.hasSelectedProcess) return 'select-process';
  if (input.processDeviceCount === 0) return 'no-process-devices';
  if (!input.hasSelectedDevice) return 'select-device';
  return 'ready';
}

export function useProductionContext() {
  const { locale } = useI18n();
  const route = useRoute();
  const router = useRouter();
  const authorizedDevices = ref<ProductionDeviceContext[]>([]);
  const selectedProcessId = ref<string | null>(null);
  const selectedDeviceId = ref<string | null>(null);
  const status = ref<ProductionContextStatus>('idle');
  const error = ref('');
  const selectionRevision = ref(0);
  let loadGeneration = 0;

  const processes = computed<ProductionProcessContext[]>(() => {
    const unique = new Map<string, ProductionProcessContext>();
    authorizedDevices.value.forEach((device) => {
      if (unique.has(device.processId)) return;
      unique.set(device.processId, {
        processId: device.processId,
        processCode: device.processCode,
        processName: device.processName,
      });
    });
    return [...unique.values()].sort((left, right) =>
      left.processCode.localeCompare(right.processCode, locale.value));
  });
  const processOptions = computed<UiSelectOption[]>(() =>
    processes.value.map((process) => ({
      label: `${process.processName} · ${process.processCode}`,
      value: process.processId,
    })));
  const selectedProcess = computed(() =>
    processes.value.find((process) => process.processId === selectedProcessId.value) ?? null);
  const devicesForSelectedProcess = computed(() => {
    if (!selectedProcessId.value) return [];
    return authorizedDevices.value
      .filter((device) => device.processId === selectedProcessId.value)
      .sort((left, right) =>
        left.deviceCode.localeCompare(right.deviceCode, locale.value)
        || left.deviceName.localeCompare(right.deviceName, locale.value));
  });
  const deviceOptions = computed<UiSelectOption[]>(() =>
    devicesForSelectedProcess.value.map((device) => ({
      label: `${device.deviceName} · ${device.deviceCode}`,
      value: device.deviceId,
    })));
  const selectedDevice = computed(() =>
    devicesForSelectedProcess.value.find((device) =>
      device.deviceId === selectedDeviceId.value) ?? null);
  const context = computed<ProductionContext | null>(() => {
    const process = selectedProcess.value;
    const device = selectedDevice.value;
    if (!process || !device) return null;
    return {
      processId: process.processId,
      processCode: process.processCode,
      processName: process.processName,
      deviceId: device.deviceId,
      deviceCode: device.deviceCode,
      deviceName: device.deviceName,
    };
  });
  const hasAuthorizedDevices = computed(() => authorizedDevices.value.length > 0);
  const state = computed<ProductionContextState>(() => resolveProductionContextState({
    status: status.value,
    authorizedDeviceCount: authorizedDevices.value.length,
    hasSelectedProcess: Boolean(selectedProcess.value),
    processDeviceCount: devicesForSelectedProcess.value.length,
    hasSelectedDevice: Boolean(selectedDevice.value),
  }));

  function setSelection(processId: string | null, deviceId: string | null) {
    if (
      selectedProcessId.value === processId
      && selectedDeviceId.value === deviceId
    ) return;
    selectedProcessId.value = processId;
    selectedDeviceId.value = deviceId;
    selectionRevision.value++;
  }

  function replaceContextQuery(processId: string | null, deviceId: string | null) {
    const query: LocationQueryRaw = { ...route.query };
    if (processId) query.processId = processId;
    else delete query.processId;
    if (deviceId) query.deviceId = deviceId;
    else delete query.deviceId;
    return router.replace({ query });
  }

  function applyRouteContext() {
    if (status.value !== 'ready') return;

    const processId = queryValue(route.query.processId);
    const deviceId = queryValue(route.query.deviceId);
    const process = processId
      ? processes.value.find((item) => item.processId === processId)
      : null;
    const device = deviceId
      ? authorizedDevices.value.find((item) => item.deviceId === deviceId)
      : null;

    if (process && !deviceId) {
      setSelection(process.processId, null);
      return;
    }

    if (process && device && device.processId === process.processId) {
      setSelection(process.processId, device.deviceId);
      return;
    }

    setSelection(null, null);
    if (processId || deviceId) void replaceContextQuery(null, null);
  }

  async function selectProcess(processId: string | null) {
    const normalized = processId
      && processes.value.some((process) => process.processId === processId)
      ? processId
      : null;
    setSelection(normalized, null);
    await replaceContextQuery(normalized, null);
  }

  async function selectDevice(deviceId: string | null) {
    const normalized = deviceId
      && devicesForSelectedProcess.value.some((device) => device.deviceId === deviceId)
      ? deviceId
      : null;
    setSelection(selectedProcessId.value, normalized);
    await replaceContextQuery(selectedProcessId.value, normalized);
  }

  async function loadContext() {
    const generation = ++loadGeneration;
    status.value = 'loading';
    error.value = '';
    authorizedDevices.value = [];
    setSelection(null, null);

    try {
      const devices = await getScopedDeviceSelectApi({ inlineFeedback: true });
      if (generation !== loadGeneration) return;
      authorizedDevices.value = devices.map(toDeviceContext);
      status.value = 'ready';
      applyRouteContext();
    } catch (loadError) {
      const message = await resolveRequestErrorMessage(loadError, contextLoadFallback);
      if (generation !== loadGeneration) return;
      authorizedDevices.value = [];
      status.value = 'error';
      error.value = message;
    }
  }

  watch(
    () => [route.query.processId, route.query.deviceId],
    applyRouteContext,
  );

  return {
    authorizedDevices,
    processes,
    processOptions,
    devicesForSelectedProcess,
    deviceOptions,
    selectedProcessId,
    selectedDeviceId,
    selectedProcess,
    selectedDevice,
    context,
    status,
    error,
    state,
    selectionRevision,
    hasAuthorizedDevices,
    loadContext,
    selectProcess,
    selectDevice,
  };
}
