<template>
  <n-config-provider :theme="lightTheme" :theme-overrides="themeOverrides">
    <router-view />
  </n-config-provider>
</template>

<script setup lang="ts">
import { NConfigProvider, lightTheme } from 'naive-ui';
import type { GlobalThemeOverrides } from 'naive-ui';

const themeOverrides: GlobalThemeOverrides = {
  common: {
    primaryColor: '#0891b2',
    primaryColorHover: '#0e7490',
    primaryColorPressed: '#155e75',
    primaryColorSuppl: '#155e75',
    successColor: '#059669',
    successColorHover: '#047857',
    successColorPressed: '#065f46',
    warningColor: '#d97706',
    warningColorHover: '#b45309',
    warningColorPressed: '#92400e',
    errorColor: '#dc2626',
    errorColorHover: '#b91c1c',
    errorColorPressed: '#991b1b',
    infoColor: '#6366f1',
    infoColorHover: '#4f46e5',
    infoColorPressed: '#4338ca',
    bodyColor: '#fafbfc',
    cardColor: '#ffffff',
    modalColor: '#ffffff',
    popoverColor: '#ffffff',
    textColorBase: '#1a1d29',
    textColor1: '#1a1d29',
    textColor2: '#6b7384',
    textColor3: '#9ba3b4',
    fontFamily:
      "'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', 'Noto Sans SC', 'PingFang SC', 'Microsoft YaHei', sans-serif",
    fontFamilyMono:
      "'JetBrains Mono', Consolas, Menlo, 'Courier New', monospace",
    borderRadius: '10px',
  },
};
</script>
