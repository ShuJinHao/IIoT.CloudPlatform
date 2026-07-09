<template>
  <NiondDataPage
    class="edge-host-page"
    page-key="edge-hosts"
    title="上位机 PLC 状态"
    subtitle="展示 Edge 客户端上报的本地 PLC 配置快照和运行状态"
  >
    <template #toolbar>
      <NiondToolbar>
        <div class="filter-row">
          <UiInput
            v-model:value="keyword"
            placeholder="搜索上位机、ClientCode、PLC 编码或名称..."
            clearable
            size="small"
            style="max-width: 420px;"
            @input="onSearchInput"
            @keyup.enter="fetchList"
            @clear="onClearKeyword"
          >
            <template #prefix><Search :size="14" /></template>
          </UiInput>
          <UiTag round :bordered="false" size="small">共 {{ metaData.totalCount }} 台上位机</UiTag>
        </div>
      </NiondToolbar>
    </template>

    <NiondTableCard>
      <UiDataTable
        class="edge-host-page__table"
        :columns="hostColumns"
        :data="hosts"
        :loading="loading"
        :row-key="rowKey"
      >
        <template #empty>
          <EmptyState title="暂无上位机状态" description="当前没有可访问设备或客户端尚未上报 PLC 清单。" />
        </template>
      </UiDataTable>
      <div v-if="metaData.totalPages > 1" class="pagination-wrap">
        <UiPagination
          :page="currentPage"
          :page-count="metaData.totalPages"
          :item-count="metaData.totalCount"
          :page-size="10"
          show-quick-jumper
          @update:page="onPageChange"
        />
      </div>
    </NiondTableCard>

    <EdgeHostPlcDrawer
      v-model:show="showPlcDrawer"
      :host="selectedHost"
      :runtime-columns="runtimeColumns"
      :runtime-states="runtimeStates"
      :loading="detailLoading"
      :runtime-loading="runtimeLoading"
      @close="closePlcDrawer"
    />
  </NiondDataPage>
</template>

<script setup lang="ts">
import { onMounted } from 'vue';
import { Search } from 'lucide-vue-next';
import NiondDataPage from '../../components/layout/NiondDataPage.vue';
import NiondTableCard from '../../components/layout/NiondTableCard.vue';
import NiondToolbar from '../../components/layout/NiondToolbar.vue';
import EmptyState from '../../components/states/EmptyState.vue';
import UiDataTable from '../../components/ui/UiDataTable.vue';
import UiInput from '../../components/ui/UiInput.vue';
import UiPagination from '../../components/ui/UiPagination.vue';
import UiTag from '../../components/ui/UiTag.vue';
import EdgeHostPlcDrawer from './EdgeHostPlcDrawer.vue';
import { useEdgeHosts } from './useEdgeHosts';
import type { EdgeHostListItemDto } from './api';
import './edge-host-page.css';

const {
  hosts, loading, keyword, currentPage, metaData, detailLoading, runtimeLoading,
  selectedHost, runtimeStates, showPlcDrawer, hostColumns, runtimeColumns, initialize, fetchList, onSearchInput,
  onClearKeyword, onPageChange, closePlcDrawer,
} = useEdgeHosts();

const rowKey = (row: EdgeHostListItemDto) => row.id;

onMounted(initialize);
</script>
