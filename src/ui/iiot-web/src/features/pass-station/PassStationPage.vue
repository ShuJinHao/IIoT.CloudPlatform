<template>
  <NiondDataPage class="passstation-page" :title="currentSchema?.title ?? '过站追溯'" :subtitle="currentSchema?.subtitle ?? '请选择已接入追溯能力的工序，查询过站记录。'">
    <ProductionContextToolbar
      :process-id="selectedProcessId"
      :device-id="selectedDeviceId"
      :process-options="processOptions"
      :device-options="deviceOptions"
      :context="context"
      :status="contextStatus"
      :has-authorized-devices="hasAuthorizedDevices"
      test-id-prefix="pass-station"
      @update:process-id="selectProcess"
      @update:device-id="selectDevice"
    />
    <ProductionContextState v-if="contextState !== 'ready'" :state="contextState" :error="contextError" test-id-prefix="pass-station" @retry="initialize" />
    <CardSurface v-else-if="schemaLoading"><LoadingState variant="card" /></CardSurface>
    <CardSurface v-else-if="schemaError">
      <EmptyState title="过站查询契约加载失败" :description="schemaError">
        <template #action>
          <UiButton secondary size="small" @click="loadSchemas">重试</UiButton>
        </template>
      </EmptyState>
    </CardSurface>
    <CardSurface v-else-if="!currentSchema">
      <EmptyState title="当前工序尚未接入过站追溯" description="请选择已配置过站 schema 的其它授权工序。" />
    </CardSurface>
    <CardSurface v-else-if="!hasDeviceQueryMode">
      <EmptyState title="当前工序不支持设备级查询" description="普通运行 UI 仅开放设备级过站查询模式。" />
    </CardSurface>
    <template v-else>
      <PassStationFilterCard :current-mode="currentMode" :active-query-modes="activeQueryModes" :filters="filters" :exporting="exporting" @switch-mode="switchMode" @search="doSearch" @export="doExport" />
      <PassStationTableSection :searched="searched" :loading="loading" :error="queryError" :columns="columns" :records="records" :row-key="rowKey" :row-props="rowProps" :meta-data="metaData" :current-page="currentPage" :page-size="PAGE_SIZE" @page-change="onPageChange" @retry="doSearch" />
    </template>
    <PassStationDetailDrawer v-model:show="showDetail" :loading="detailLoading" :error="detailError" :detail="detailData" :schema="currentSchema" @retry="retryDetail" />
  </NiondDataPage>
</template>

<script setup lang="ts">
import { onMounted } from 'vue';
import CardSurface from '../../components/layout/CardSurface.vue';
import NiondDataPage from '../../components/layout/NiondDataPage.vue';
import EmptyState from '../../components/states/EmptyState.vue';
import LoadingState from '../../components/states/LoadingState.vue';
import UiButton from '../../components/ui/UiButton.vue';
import { ProductionContextState, ProductionContextToolbar } from '../../shared/production-context';
import PassStationDetailDrawer from './PassStationDetailDrawer.vue';
import PassStationFilterCard from './PassStationFilterCard.vue';
import PassStationTableSection from './PassStationTableSection.vue';
import { usePassStation } from './usePassStation';
import './pass-station-page.css';

const state = usePassStation();
const {
  PAGE_SIZE, loading, schemaLoading, schemaError, queryError, exporting, searched, currentPage, currentMode, records, metaData, filters,
  currentSchema, processOptions, deviceOptions, activeQueryModes, hasDeviceQueryMode, columns, rowKey, rowProps,
  showDetail, detailLoading, detailError, detailData, context, contextStatus, contextError, contextState,
  selectedProcessId, selectedDeviceId, hasAuthorizedDevices, initialize, loadSchemas, selectProcess, selectDevice,
  doSearch, doExport, onPageChange, switchMode, retryDetail,
} = state;

onMounted(initialize);
</script>
