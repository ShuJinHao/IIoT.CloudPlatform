import { computed, ref } from 'vue';
import { useRoute } from 'vue-router';
import { useTheme } from '../../composables/useTheme';
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
  const route = useRoute();
  const { mode: themeMode } = useTheme();
  const deviceId = ref((route.query.deviceId as string | undefined) ?? '');
  const deviceName = ref((route.query.deviceName as string | undefined) ?? '设备详情');
  const queryMode = ref<CapacityQueryMode>('day');
  const queryDate = ref(todayLocal());
  const queryMonth = ref(thisMonth());
  const queryYear = ref(new Date().getFullYear());
  const plcNameFilter = ref<string | null>(null);
  const loading = ref(false);
  const loadError = ref<CapacityLoadError | null>(null);
  const allRows = ref<CapacityDetailRow[]>([]);
  let requestGeneration = 0;

  const yearOptions = Array.from({ length: 5 }, (_, index) => {
    const year = new Date().getFullYear() - index;
    return { label: `${year} 年`, value: year };
  });
  const plcOptions = computed(() => createPlcOptions(allRows.value));
  const rows = computed(() => filterRowsByPlc(allRows.value, plcNameFilter.value));
  const summary = computed(() => summarizeRows(rows.value));
  const selectedPlcName = computed(() =>
    plcOptions.value.find((option) => option.value === plcNameFilter.value)?.label ?? '全部 PLC');
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
    const labels = rows.value.map((row) => `${row.plcName} · ${row.label}`);
    const palette = themeMode.value === 'dark'
      ? {
          text: '#c4c4ca',
          grid: 'rgba(255, 255, 255, 0.09)',
          tooltipBackground: '#202024',
          tooltipBorder: 'rgba(255, 255, 255, 0.14)',
          ok: '#5eead4',
          ng: '#f87171',
        }
      : {
          text: '#596273',
          grid: 'rgba(17, 24, 39, 0.08)',
          tooltipBackground: '#ffffff',
          tooltipBorder: 'rgba(17, 24, 39, 0.12)',
          ok: '#229aa3',
          ng: '#ef4444',
        };
    return {
      grid: { left: 52, right: 16, top: 32, bottom: labels.length > 8 ? 72 : 48 },
      legend: {
        data: ['合格弹夹', '不合格弹夹'],
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
          name: '合格弹夹',
          type: 'bar',
          stack: 'total',
          data: rows.value.map((row) => row.ok),
          itemStyle: { color: palette.ok },
          barMaxWidth: 36,
        },
        {
          name: '不合格弹夹',
          type: 'bar',
          stack: 'total',
          data: rows.value.map((row) => row.ng),
          itemStyle: { color: palette.ng, borderRadius: [4, 4, 0, 0] },
          barMaxWidth: 36,
        },
      ],
    };
  });
  const columns = computed(() => createCapacityDetailColumns(() => queryMode.value));
  const rowKey = (row: CapacityDetailRow) =>
    `${row.bucketKey}-${row.plcKey}-${row.shift || 'all'}`;
  const canExport = computed(() => !loading.value && !loadError.value && rows.value.length > 0);

  async function requestRows(): Promise<CapacityDetailRow[]> {
    if (queryMode.value === 'day') {
      return mapHourlyRows(queryDate.value, await getHourlyByDeviceApi({
        deviceId: deviceId.value,
        date: queryDate.value,
      }));
    }
    if (queryMode.value === 'month') {
      const range = monthDateRange(queryMonth.value);
      return mapMonthRows(queryMonth.value, await getSummaryRangeApi({
        deviceId: deviceId.value,
        ...range,
        breakdownByPlc: true,
      }));
    }
    const range = yearDateRange(queryYear.value);
    return mapYearRows(queryYear.value, await getSummaryRangeApi({
      deviceId: deviceId.value,
      ...range,
      breakdownByPlc: true,
    }));
  }

  async function fetchData() {
    const generation = ++requestGeneration;
    loading.value = true;
    loadError.value = null;
    allRows.value = [];
    plcNameFilter.value = null;
    if (!deviceId.value) {
      loadError.value = {
        kind: 'api',
        title: '缺少设备信息',
        message: '未指定要查询的设备，请返回产能看板后重新进入。',
      };
      loading.value = false;
      return;
    }
    try {
      const nextRows = await requestRows();
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

  return {
    deviceName,
    queryMode,
    queryDate,
    queryMonth,
    queryYear,
    plcNameFilter,
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
    fetchData,
    exportRows,
    formatInt,
    rateAccent,
  };
}
