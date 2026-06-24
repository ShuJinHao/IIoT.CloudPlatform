import { computed, ref } from 'vue';
import { useRoute } from 'vue-router';
import {
  getDailySummaryApi,
  getHourlyByDeviceApi,
  getSummaryRangeApi,
} from './api';
import { createCapacityDetailColumns } from './columns';
import {
  createDailyFallbackRows,
  formatInt,
  mapHourlyDetailRow,
  mapMonthRows,
  mapYearRows,
  monthDateRange,
  rateAccent,
  thisMonth,
  todayLocal,
  yearDateRange,
  type CapacityDetailRow,
  type CapacityQueryMode,
} from './types';

export function useCapacityDetail() {
  const route = useRoute();
  const deviceId = ref((route.query.deviceId as string | undefined) ?? '');
  const deviceName = ref(
    (route.query.deviceName as string | undefined) ?? '设备详情',
  );
  const queryMode = ref<CapacityQueryMode>('day');
  const queryDate = ref(todayLocal());
  const queryMonth = ref(thisMonth());
  const queryYear = ref(new Date().getFullYear());
  const plcNameFilter = ref('');
  const loading = ref(false);
  const rows = ref<CapacityDetailRow[]>([]);

  const yearOptions = Array.from({ length: 5 }, (_, index) => {
    const year = new Date().getFullYear() - index;
    return { label: `${year} 年`, value: year };
  });
  const summary = computed(() => {
    const total = rows.value.reduce((sum, row) => sum + row.total, 0);
    const ok = rows.value.reduce((sum, row) => sum + row.ok, 0);
    const ng = rows.value.reduce((sum, row) => sum + row.ng, 0);
    const ratePercent = total > 0 ? (ok * 100) / total : 0;
    const divisor = queryMode.value === 'year'
      ? 12
      : Math.max(1, rows.value.length);
    const avg = Math.round(total / divisor);
    return { total, ok, ng, ratePercent, avg };
  });
  const avgLabel = computed(() => {
    if (queryMode.value === 'year') return '月均产出';
    if (queryMode.value === 'month') return '日均产出';
    return '半小时均产';
  });
  const subtitleText = computed(() => {
    let text = '产能详细报表 · 年 / 月 / 日 三级查询';
    if (plcNameFilter.value) text += ` · PLC: ${plcNameFilter.value}`;
    return text;
  });
  const chartSubtitle = computed(() => {
    if (queryMode.value === 'day') return `按时间段统计 · ${rows.value.length} 个数据点`;
    if (queryMode.value === 'month') return `按日统计 · ${queryMonth.value}`;
    return `按月统计 · ${queryYear.value} 年`;
  });
  const chartOption = computed(() => {
    const xAxis = rows.value.map((row) => row.label);
    return {
      grid: { left: 48, right: 16, top: 32, bottom: 36 },
      legend: {
        data: ['良品', '不良品'],
        top: 0,
        right: 8,
        itemWidth: 12,
        itemHeight: 8,
        textStyle: { color: 'var(--text-2)', fontSize: 12 },
      },
      tooltip: {
        trigger: 'axis',
        backgroundColor: 'rgba(255, 255, 255, 0.98)',
        borderColor: 'rgba(15, 23, 42, 0.08)',
        borderWidth: 1,
        extraCssText: 'box-shadow: 0 4px 16px rgba(15, 23, 42, 0.08);',
        textStyle: {
          color: 'var(--text-0)',
          fontFamily: "'Inter', sans-serif",
          fontSize: 12,
        },
        axisPointer: { type: 'shadow' },
      },
      xAxis: {
        type: 'category',
        data: xAxis,
        axisLine: { lineStyle: { color: 'rgba(15, 23, 42, 0.08)' } },
        axisLabel: {
          color: 'var(--text-2)',
          fontFamily: "'JetBrains Mono', monospace",
          fontSize: 11,
          rotate: queryMode.value === 'day' && xAxis.length > 12 ? 35 : 0,
          interval: queryMode.value === 'day' && xAxis.length > 24 ? 'auto' : 0,
        },
        axisTick: { show: false },
      },
      yAxis: {
        type: 'value',
        splitLine: { lineStyle: { color: 'rgba(15, 23, 42, 0.05)' } },
        axisLine: { show: false },
        axisTick: { show: false },
        axisLabel: {
          color: 'var(--text-2)',
          fontFamily: "'JetBrains Mono', monospace",
          fontSize: 11,
        },
      },
      series: [
        {
          name: '良品',
          type: 'bar',
          stack: 'total',
          data: rows.value.map((row) => row.ok),
          itemStyle: { color: 'var(--brand)', borderRadius: [0, 0, 0, 0] },
          barMaxWidth: 36,
        },
        {
          name: '不良品',
          type: 'bar',
          stack: 'total',
          data: rows.value.map((row) => row.ng),
          itemStyle: { color: 'var(--error)', borderRadius: [4, 4, 0, 0] },
          barMaxWidth: 36,
        },
      ],
    };
  });
  const columns = computed(() =>
    createCapacityDetailColumns(() => queryMode.value),
  );
  const rowKey = (row: CapacityDetailRow) => `${row.label}-${row.shift}`;

  async function fetchDay(date: string) {
    try {
      const hourly = await getHourlyByDeviceApi({
        deviceId: deviceId.value,
        date,
        plcName: plcNameFilter.value || undefined,
      });
      if (Array.isArray(hourly) && hourly.length > 0) {
        rows.value = hourly.map(mapHourlyDetailRow);
        return;
      }
    } catch {
      /* fallback to daily summary */
    }

    try {
      const daily = await getDailySummaryApi({
        deviceId: deviceId.value,
        date,
        plcName: plcNameFilter.value || undefined,
      });
      rows.value = createDailyFallbackRows(daily, date);
    } catch {
      rows.value = [];
    }
  }

  async function fetchMonth(month: string) {
    const range = monthDateRange(month);
    const list = await getSummaryRangeApi({
      deviceId: deviceId.value,
      ...range,
      plcName: plcNameFilter.value || undefined,
    });
    rows.value = mapMonthRows(month, list);
  }

  async function fetchYear(year: number) {
    const range = yearDateRange(year);
    const list = await getSummaryRangeApi({
      deviceId: deviceId.value,
      ...range,
      plcName: plcNameFilter.value || undefined,
    });
    rows.value = mapYearRows(year, list);
  }

  async function fetchData() {
    if (!deviceId.value) {
      rows.value = [];
      return;
    }
    loading.value = true;
    rows.value = [];
    try {
      if (queryMode.value === 'day') {
        await fetchDay(queryDate.value);
      } else if (queryMode.value === 'month') {
        await fetchMonth(queryMonth.value);
      } else {
        await fetchYear(queryYear.value);
      }
    } finally {
      loading.value = false;
    }
  }

  function onModeChange() {
    void fetchData();
  }

  return {
    deviceName,
    queryMode,
    queryDate,
    queryMonth,
    queryYear,
    plcNameFilter,
    yearOptions,
    loading,
    rows,
    summary,
    avgLabel,
    subtitleText,
    chartSubtitle,
    chartOption,
    columns,
    rowKey,
    fetchData,
    formatInt,
    rateAccent,
    onModeChange,
  };
}
