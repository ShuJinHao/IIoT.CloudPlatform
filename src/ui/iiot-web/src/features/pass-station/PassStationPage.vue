<template>
  <NiondDataPage class="passstation-page" :title="currentSchema?.title ?? '过站追溯'" :subtitle="currentSchema?.subtitle ?? '请选择已接入追溯能力的工序，查询过站记录。'">
    <CardSurface v-if="selectError">
      <EmptyState title="过站查询上下文加载失败" :description="selectError">
        <template #action>
          <UiButton secondary size="small" :loading="selectLoading" @click="fetchSelectData">重试</UiButton>
        </template>
      </EmptyState>
    </CardSurface>
    <LoadingState v-else-if="selectLoading" variant="card" />
    <PassStationContextToolbar v-else v-model:current-process-id="currentProcessId" :process-options="processOptions" :current-process="currentProcess" />
    <PassStationFilterCard v-if="!selectError && !selectLoading && currentSchema" :current-mode="currentMode" :active-query-modes="activeQueryModes" :filters="filters" :device-options="deviceOptions" :exporting="exporting" @switch-mode="switchMode" @search="doSearch" @export="doExport" />
    <CardSurface v-if="!selectError && !selectLoading && !currentSchema">
      <EmptyState title="请先选择支持追溯的工序" description="只有已接入追溯能力的工序才会出现在选择器里。" />
    </CardSurface>
    <PassStationTableSection v-if="!selectError && !selectLoading && currentSchema" :searched="searched" :loading="loading" :error="queryError" :columns="columns" :records="records" :row-key="rowKey" :row-props="rowProps" :meta-data="metaData" :current-page="currentPage" :page-size="PAGE_SIZE" @page-change="onPageChange" @retry="doSearch" />
    <PassStationDetailDrawer v-model:show="showDetail" :loading="detailLoading" :detail="detailData" :schema="currentSchema" />
  </NiondDataPage>
</template>

<script setup lang="ts">
import { onMounted } from 'vue';
import CardSurface from '../../components/layout/CardSurface.vue';
import NiondDataPage from '../../components/layout/NiondDataPage.vue';
import EmptyState from '../../components/states/EmptyState.vue';
import LoadingState from '../../components/states/LoadingState.vue';
import UiButton from '../../components/ui/UiButton.vue';
import PassStationContextToolbar from './PassStationContextToolbar.vue';
import PassStationDetailDrawer from './PassStationDetailDrawer.vue';
import PassStationFilterCard from './PassStationFilterCard.vue';
import PassStationTableSection from './PassStationTableSection.vue';
import { usePassStation } from './usePassStation';
import './pass-station-page.css';

const state = usePassStation();
const {
  PAGE_SIZE, loading, selectLoading, selectError, queryError, exporting, searched, currentPage, currentMode, currentProcessId, records, metaData, filters,
  currentProcess, currentSchema, processOptions, deviceOptions, activeQueryModes, columns, rowKey, rowProps,
  showDetail, detailLoading, detailData, fetchSelectData, doSearch, doExport, onPageChange, switchMode,
} = state;

onMounted(fetchSelectData);
</script>
