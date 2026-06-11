<template>
  <UiModal
    :show="Boolean(dialog)"
    :title="dialog?.title"
    :mask-closable="dialog?.kind !== 'confirm'"
    style="width: min(560px, calc(100vw - 48px));"
    @update:show="handleVisibility"
  >
    <div v-if="dialog" class="feedback-dialog" :class="`is-${dialog.type}`">
      <div class="feedback-dialog__icon" aria-hidden="true">
        <CheckCircle2 v-if="dialog.type === 'success'" :size="24" />
        <AlertTriangle v-else-if="dialog.type === 'warning'" :size="24" />
        <XCircle v-else-if="dialog.type === 'error'" :size="24" />
        <Info v-else :size="24" />
      </div>
      <div class="feedback-dialog__content">
        <p>{{ dialog.message }}</p>
        <ul v-if="dialog.details?.length" class="feedback-dialog__details">
          <li v-for="detail in dialog.details" :key="detail">{{ detail }}</li>
        </ul>
      </div>
    </div>

    <template #footer>
      <div v-if="dialog" class="feedback-dialog__actions">
        <UiButton
          v-if="dialog.kind === 'confirm'"
          secondary
          @click="resolveFeedback(false)"
        >
          {{ dialog.cancelText }}
        </UiButton>
        <UiButton
          :type="dialog.type === 'error' ? 'error' : dialog.type"
          @click="resolveFeedback(true)"
        >
          {{ dialog.confirmText }}
        </UiButton>
      </div>
    </template>
  </UiModal>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import {
  AlertTriangle,
  CheckCircle2,
  Info,
  XCircle,
} from 'lucide-vue-next';
import UiButton from '../ui/UiButton.vue';
import UiModal from '../ui/UiModal.vue';
import { resolveFeedback, useFeedbackState } from '../../utils/feedback';

const feedback = useFeedbackState();
const dialog = computed(() => feedback.dialog);

const handleVisibility = (value: boolean) => {
  if (!value) {
    resolveFeedback(false);
  }
};
</script>

<style scoped>
.feedback-dialog {
  display: grid;
  grid-template-columns: 48px minmax(0, 1fr);
  gap: 16px;
  align-items: flex-start;
}

.feedback-dialog__icon {
  display: grid;
  width: 48px;
  height: 48px;
  place-items: center;
  border-radius: 16px;
  background: var(--info-soft);
  color: var(--info);
}

.feedback-dialog.is-success .feedback-dialog__icon {
  background: var(--success-soft);
  color: var(--success);
}

.feedback-dialog.is-warning .feedback-dialog__icon {
  background: var(--warn-soft);
  color: var(--warn);
}

.feedback-dialog.is-error .feedback-dialog__icon {
  background: var(--error-soft);
  color: var(--error);
}

.feedback-dialog__content {
  min-width: 0;
}

.feedback-dialog__content p {
  margin: 2px 0 0;
  color: var(--text-0);
  font-size: var(--fs-lg);
  font-weight: var(--fw-bold);
  line-height: 1.65;
}

.feedback-dialog__details {
  display: grid;
  gap: 8px;
  margin: 16px 0 0;
  padding: 14px 16px 14px 30px;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--bg-2);
  color: var(--text-1);
  font-size: var(--fs-base);
  font-weight: var(--fw-semibold);
  line-height: 1.55;
}

.feedback-dialog__actions {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
}

@media (max-width: 560px) {
  .feedback-dialog {
    grid-template-columns: 1fr;
  }

  .feedback-dialog__actions {
    flex-direction: column-reverse;
  }

  .feedback-dialog__actions :deep(button) {
    width: 100%;
  }
}
</style>
