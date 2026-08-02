import { computed, ref, watch } from 'vue';
import { useTheme } from '../../composables/useTheme';
import { useProductionContext } from '../../shared/production-context';
import { getHourlyByDeviceApi, getSummaryRangeApi } from './api';
import { createCapacityDetailColumns } from './columns';
import { resolveCapacityLoadError, type CapacityLoadError } from './errors';
import { capacityExportFileName, downloadCapacityCsv } from './export';
import {
  createPlcOptions,
  filterRowsByPlc,
  formatInt,
  mapHourlyRows,
  mapMonthRows,
  mapYearRows,
  monthDateRange,
  rateAccent,
  summarizeRows,
  thisMonth,
  todayLocal,
  yearDateRange,
  type CapacityDetailRow,
  type CapacityQueryMode,
} from './types';

export function useCapacityDetail() {
  const { mode: themeMode } = useTheme();
  const productionContext = useProductionContext();
  const {
    processOptions,
    deviceOptions,
    selectedProcessId,
    selectedDeviceId,
    context,
    status: contextStatus,
    error: contextError,
    state: contextState,
    selectionRevision,
    hasAuthorizedDevices,
    loadContext,
    selectProcess,
    selectDevice,
  } = productionContext;
  const queryMode = ref<CapacityQueryMode>('day');
  const queryDate = ref(todayLocal());
  const queryMonth = ref(thisMonth());
  const queryYear = ref(new Date().getFullYear());
  const plcCodeFilter = ref<string | null>(null);
  const loading = ref(false);
  const loadError = ref<CapacityLoadError | null>(null);
  const allRows = ref<CapacityDetailRow[]>([]);
  let requestGeneration = 0;

  const deviceName = computed(() => context.value?.deviceName ?? '产能详情');

  const yearOptions = Array.from({ length: 5 }, (_, index) => {
    const year = new Date().getFullYear() - index;
    return { label: `${year} 年`, value: year };
  });
  const plcOptions = computed(() => createPlcOptions(allRows.value));
  const rows = computed(() => filterRowsByPlc(allRows.value, plcCodeFilter.value));
  const summary = computed(() => summarizeRows(rows.value));
  const selectedPlcName = computed(() =>
    plcOptions.value.find((option) => option.value === plcCodeFilter.value)?.label ?? '全部 PLC');
  const scopeText = computed(() =>
    queryMode.value === 'day'
      ? queryDate.value
      : queryMode.value === 'month'
        ? queryMonth.value
        : String(queryYear.value));
  const subtitleText = computed(() =>
    `完工弹夹明细 · ${scopeText.value} · ${selectedPlcName.value}`);
  const chartSubtitle = computed(() => {
    const grain = queryMode.value === 'day' ? '半小时' : queryMode.value === 'month' ? '每日' : '每月';
    return `${grain}统计 · ${selectedPlcName.value} · ${rows.value.length} 个数据点`;
  });
  const chartOption = computed(() => {
    const labels = rows.value.map((row) => `${row.plcDisplay} · ${row.label}`);
    const palette = themeMode.value === 'dark'
      ? {
          text: '#c4c4ca',
          grid: 'rgba(255, 255, 255, 0.09)',
          tooltipBackground: '#202024',
          tooltipBorder: 'rgba(255, 255, 255, 0.14)',
          total: '#5eead4',
        }
      : {
          text: '#596273',
          grid: 'rgba(17, 24, 39, 0.08)',
          tooltipBackground: '#ffffff',
          tooltipBorder: 'rgba(17, 24, 39, 0.12)',
          total: '#229aa3',
        };
    return {
      grid: { left: 52, right: 16, top: 32, bottom: labels.length > 8 ? 72 : 48 },
      legend: {
        data: ['完工弹夹'],
        top: 0,
        right: 8,
        itemWidth: 12,
        itemHeight: 8,
        textStyle: { color: palette.text, fontSize: 12 },
      },
      tooltip: {
        trigger: 'axis',
        backgroundColor: palette.tooltipBackground,
        borderColor: palette.tooltipBorder,
        borderWidth: 1,
        textStyle: { color: palette.text, fontFamily: "'Inter', sans-serif", fontSize: 12 },
        axisPointer: { type: 'shadow' },
      },
      xAxis: {
        type: 'category',
        data: labels,
        axisLine: { lineStyle: { color: palette.grid } },
        axisLabel: {
          color: palette.text,
          fontFamily: "'JetBrains Mono', monospace",
          fontSize: 11,
          rotate: labels.length > 6 ? 35 : 0,
          interval: labels.length > 24 ? 'auto' : 0,
        },
        axisTick: { show: false },
      },
      yAxis: {
        type: 'value',
        name: '弹夹数（个）',
        splitLine: { lineStyle: { color: palette.grid } },
        axisLine: { show: false },
        axisTick: { show: false },
        axisLabel: { color: palette.text, fontFamily: "'JetBrains Mono', monospace", fontSize: 11 },
      },
      series: [
        {
          name: '完工弹夹',
          type: 'bar',
          data: rows.value.map((row) => row.total),
          itemStyle: { color: palette.total, borderRadius: [4, 4, 0, 0] },
          barMaxWidth: 36,
        },
      ],
    };
  });
  const columns = computed(() => createCapacityDetailColumns(() => queryMode.value));
  const rowKey = (row: CapacityDetailRow) =>
    `${row.bucketKey}-${row.plcKey}-${row.shift || 'all'}`;
  const canExport = computed(() => !loading.value && !loadError.value && rows.value.length > 0);

  async function requestRows(deviceId: string): Promise<CapacityDetailRow[]> {
    if (queryMode.value === 'day') {
      return mapHourlyRows(queryDate.value, await getHourlyByDeviceApi({
        deviceId,
        date: queryDate.value,
      }));
    }
    if (queryMode.value === 'month') {
      const range = monthDateRange(queryMonth.value);
      return mapMonthRows(queryMonth.value, await getSummaryRangeApi({
        deviceId,
        ...range,
        breakdownByPlc: true,
      }));
    }
    const range = yearDateRange(queryYear.value);
    return mapYearRows(queryYear.value, await getSummaryRangeApi({
      deviceId,
      ...range,
      breakdownByPlc: true,
    }));
  }

  async function fetchData() {
    const activeContext = context.value;
    const generation = ++requestGeneration;
    loading.value = true;
    loadError.value = null;
    allRows.value = [];
    plcCodeFilter.value = null;
    if (!activeContext) {
      loading.value = false;
      return;
    }
    try {
      const nextRows = await requestRows(activeContext.deviceId);
      if (generation === requestGeneration) allRows.value = nextRows;
    } catch (error) {
      const resolved = await resolveCapacityLoadError(error);
      if (generation === requestGeneration) loadError.value = resolved;
    } finally {
      if (generation === requestGeneration) loading.value = false;
    }
  }

  function exportRows() {
    if (!canExport.value) return;
    downloadCapacityCsv(
      capacityExportFileName(deviceName.value, queryMode.value, scopeText.value, selectedPlcName.value),
      rows.value,
    );
  }

  function clearData() {
    requestGeneration++;
    loading.value = false;
    loadError.value = null;
    allRows.value = [];
    plcCodeFilter.value = null;
  }

  watch(selectionRevision, () => {
    clearData();
    if (context.value) void fetchData();
  }, { flush: 'sync' });

  return {
    deviceName,
    processOptions,
    deviceOptions,
    selectedProcessId,
    selectedDeviceId,
    context,
    contextStatus,
    contextError,
    contextState,
    hasAuthorizedDevices,
    queryMode,
    queryDate,
    queryMonth,
    queryYear,
    plcCodeFilter,
    yearOptions,
    plcOptions,
    loading,
    loadError,
    rows,
    summary,
    subtitleText,
    chartSubtitle,
    chartOption,
    columns,
    rowKey,
    canExport,
    initialize: loadContext,
    selectProcess,
    selectDevice,
    fetchData,
    exportRows,
    formatInt,
    rateAccent,
  };
}
