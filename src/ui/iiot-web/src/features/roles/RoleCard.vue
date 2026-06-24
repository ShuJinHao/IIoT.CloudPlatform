<template>
  <CardSurface
    class="role-card"
    :class="{ 'role-card--admin': role === 'Admin' }"
    hoverable
  >
    <div class="role-card__head">
      <div class="role-card__name-block">
        <h3 class="role-card__name">{{ role }}</h3>
        <UiTag v-if="role === 'Admin'" size="small" :bordered="false" type="warning">
          系统内置
        </UiTag>
      </div>
      <UiButton
        v-if="role !== 'Admin' && canUpdate"
        size="tiny"
        secondary
        @click="$emit('edit', role)"
      >
        编辑权限
      </UiButton>
    </div>

    <div class="role-card__body">
      <div class="role-card__meta">
        <span class="role-card__meta-label">权限点</span>
        <span class="role-card__meta-value">{{ summary }}</span>
      </div>
      <div v-if="status === 'loaded'" class="role-card__chips">
        <UiTag
          v-for="permission in permissions"
          :key="permission"
          size="small"
          :bordered="false"
          type="info"
        >
          {{ permission }}
        </UiTag>
        <span v-if="permissions.length === 0" class="role-card__no-perm">
          暂无权限点
        </span>
      </div>
      <span v-else-if="status === 'failed'" class="role-card__error">
        权限加载失败，请刷新后重试。
      </span>
      <span v-else class="role-card__loading">加载权限中...</span>
    </div>
  </CardSurface>
</template>

<script setup lang="ts">
import CardSurface from '../../components/layout/CardSurface.vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiTag from '../../components/ui/UiTag.vue';
import type { RolePermissionStatus } from './types';

withDefaults(
  defineProps<{
    role: string;
    permissions?: string[];
    status?: RolePermissionStatus;
    summary: string;
    canUpdate: boolean;
  }>(),
  {
    permissions: () => [],
    status: 'loading',
  },
);

defineEmits<{
  edit: [role: string];
}>();
</script>
