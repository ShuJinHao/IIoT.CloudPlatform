<template>
  <NiondTableCard>
    <div class="table-heading">
      <div>
        <h2>设备安装状态</h2>
        <p>最近一次客户端版本上报与当前 catalog 最新版本的差异</p>
      </div>
    </div>
    <UiDataTable :columns="columns" :data="inventory" :loading="loading" :row-key="rowKey">
      <template #empty>
        <EmptyState title="暂无安装状态" description="未找到匹配的设备版本上报数据。" />
      </template>
    </UiDataTable>
  </NiondTableCard>
</template>

<script setup lang="ts">
import NiondTableCard from '../../components/layout/NiondTableCard.vue';
import EmptyState from '../../components/states/EmptyState.vue';
import UiDataTable from '../../components/ui/UiDataTable.vue';
import type { UiDataTableColumn } from '../../components/ui/types';
import type { DeviceClientVersionInventoryDto } from './api';

defineProps<{
  columns: UiDataTableColumn<DeviceClientVersionInventoryDto>[];
  inventory: DeviceClientVersionInventoryDto[];
  loading: boolean;
}>();

const rowKey = (row: DeviceClientVersionInventoryDto) => row.deviceId;
</script>
