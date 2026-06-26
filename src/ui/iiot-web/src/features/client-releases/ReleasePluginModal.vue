<template>
  <UiModal v-model:show="show" preset="card" title="登记插件版本" style="width: 760px;" :mask-closable="false">
    <div class="release-form">
      <label>Module ID<UiInput v-model:value="form.moduleId" /></label>
      <label>Display Name<UiInput v-model:value="form.displayName" /></label>
      <label>Channel<UiInput v-model:value="form.channel" /></label>
      <label>Version<UiInput v-model:value="form.version" /></label>
      <label>Host API<UiInput v-model:value="form.hostApiVersion" /></label>
      <label>Min Host<UiInput v-model:value="form.minHostVersion" /></label>
      <label>Max Host<UiInput v-model:value="form.maxHostVersion" /></label>
      <label>Runtime<UiInput v-model:value="form.targetRuntime" /></label>
      <label>Framework<UiInput v-model:value="form.targetFramework" /></label>
      <label>Status<UiSelect v-model:value="form.status" :options="statusOptions" /></label>
      <label>Package Size<UiInput v-model:value="form.packageSize" /></label>
      <label>Publisher<UiInput v-model:value="form.publisher" /></label>
      <label>Description<UiInput v-model:value="form.description" /></label>
      <label>Icon<UiInput v-model:value="form.iconKind" /></label>
      <label>Accent<UiInput v-model:value="form.accentColor" /></label>
      <label class="is-wide">Download URL<UiInput v-model:value="form.downloadUrl" /></label>
      <label class="is-wide">SHA256<UiInput v-model:value="form.sha256" /></label>
      <label class="is-wide">Dependencies JSON<UiInput v-model:value="form.dependenciesJson" /></label>
      <label class="is-wide">Release Notes<UiInput v-model:value="form.releaseNotes" /></label>
      <label class="is-wide">Signature<UiInput v-model:value="form.signature" /></label>
    </div>
    <template #footer>
      <div class="modal-actions">
        <UiButton @click="show = false">取消</UiButton>
        <UiButton type="primary" :loading="submitting" @click="$emit('submit')">保存</UiButton>
      </div>
    </template>
  </UiModal>
</template>

<script setup lang="ts">
import UiButton from '../../components/ui/UiButton.vue';
import UiInput from '../../components/ui/UiInput.vue';
import UiModal from '../../components/ui/UiModal.vue';
import UiSelect from '../../components/ui/UiSelect.vue';
import { statusOptions, type PluginReleaseForm } from './types';

defineEmits<{ submit: [] }>();
const show = defineModel<boolean>('show', { required: true });
defineProps<{
  form: PluginReleaseForm;
  submitting: boolean;
}>();
</script>
