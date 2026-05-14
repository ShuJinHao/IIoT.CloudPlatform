<template>
  <button :class="classes">
    <slot />
  </button>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { cva, type VariantProps } from 'class-variance-authority';
import { cn } from '../../lib/utils';

const buttonVariants = cva(
  'inline-flex items-center justify-center gap-2 whitespace-nowrap rounded-[12px] text-sm font-bold transition-colors disabled:pointer-events-none disabled:opacity-50 focus-visible:ring-2 focus-visible:ring-[var(--focus-ring)]',
  {
    variants: {
      variant: {
        default: 'bg-[#111827] text-white hover:bg-[#262f3f]',
        primary: 'bg-[var(--primary)] text-[var(--primary-foreground)] hover:bg-[#d4f77a]',
        secondary: 'bg-[var(--secondary)] text-[var(--secondary-foreground)] hover:bg-[#e1e8eb]',
        ghost: 'text-[var(--foreground)] hover:bg-[var(--secondary)]',
        outline: 'border border-[var(--border)] bg-[var(--card)] text-[var(--foreground)] hover:bg-[var(--secondary)]',
      },
      size: {
        sm: 'h-9 px-3',
        md: 'h-10 px-4',
        lg: 'h-12 px-5',
        icon: 'size-10',
      },
    },
    defaultVariants: {
      variant: 'default',
      size: 'md',
    },
  },
);

type ButtonVariants = VariantProps<typeof buttonVariants>;

const props = defineProps<{
  variant?: ButtonVariants['variant'];
  size?: ButtonVariants['size'];
  class?: string;
}>();

const classes = computed(() => cn(buttonVariants({ variant: props.variant, size: props.size }), props.class));
</script>
