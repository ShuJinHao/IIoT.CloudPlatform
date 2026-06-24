<template>
  <div class="perm-selector">
    <div v-for="group in groups" :key="group.groupName" class="perm-group">
      <div class="perm-group__title">{{ group.groupName }}</div>
      <div class="perm-group__items">
        <UiCheckbox
          v-for="permission in group.permissions"
          :key="permission"
          :checked="selected.includes(permission)"
          @update:checked="$emit('toggle', permission, $event)"
        >
          {{ permission }}
        </UiCheckbox>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import type { PermissionGroupDto } from '../../api/identity';
import UiCheckbox from '../../components/ui/UiCheckbox.vue';

defineProps<{
  groups: PermissionGroupDto[];
  selected: string[];
}>();

defineEmits<{
  toggle: [permission: string, checked: boolean];
}>();
</script>
