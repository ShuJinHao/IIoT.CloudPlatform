<template>
  <UiModal v-model:show="show" preset="card" title="员工档案详情" style="width: 560px;" :mask-closable="true">
    <div v-if="detail" class="detail-grid">
      <div class="detail-row">
        <span class="detail-row__label">工号</span>
        <span class="detail-row__value detail-row__value--mono detail-row__value--brand">{{ detail.employeeNo }}</span>
      </div>
      <div class="detail-row">
        <span class="detail-row__label">姓名</span>
        <span class="detail-row__value">{{ detail.realName }}</span>
      </div>
      <div class="detail-row">
        <span class="detail-row__label">状态</span>
        <UiTag size="small" :bordered="false" :type="detail.isActive ? 'success' : 'default'">{{ detail.isActive ? '在职' : '停用' }}</UiTag>
      </div>
      <div class="detail-row">
        <span class="detail-row__label">系统 ID</span>
        <span class="detail-row__value detail-row__value--mono detail-row__value--small">{{ detail.id }}</span>
      </div>
      <div class="detail-row detail-row--full">
        <span class="detail-row__label">机台管辖</span>
        <div v-if="detail.deviceIds.length" class="detail-chips">
          <UiTag v-for="id in detail.deviceIds" :key="id" size="small" :bordered="false" type="info">{{ deviceNameMap[id] || id.substring(0, 8) + '…' }}</UiTag>
        </div>
        <span v-else class="detail-row__value detail-row__value--muted">未分配</span>
      </div>
    </div>
    <template #footer>
      <div class="modal-actions">
        <UiButton @click="show = false">关闭</UiButton>
      </div>
    </template>
  </UiModal>
</template>

<script setup lang="ts">
import UiButton from '../../components/ui/UiButton.vue';
import UiModal from '../../components/ui/UiModal.vue';
import UiTag from '../../components/ui/UiTag.vue';
import type { EmployeeDetailDto } from './api';

const show = defineModel<boolean>('show', { required: true });
defineProps<{
  detail: EmployeeDetailDto | null;
  deviceNameMap: Record<string, string>;
}>();
</script>
