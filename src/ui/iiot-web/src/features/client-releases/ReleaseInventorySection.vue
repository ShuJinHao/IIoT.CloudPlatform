<template>
  <NiondTableCard>
    <div class="table-heading">
      <div>
        <h2>设备客户端状态</h2>
        <p>运行心跳与版本 catalog 差异分开展示</p>
      </div>
    </div>
    <UiDataTable :columns="columns" :data="inventory" :loading="loading" :row-key="rowKey">
      <template #empty>
        <EmptyState title="暂无客户端状态" description="未找到匹配的设备运行心跳或版本上报数据。" />
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
