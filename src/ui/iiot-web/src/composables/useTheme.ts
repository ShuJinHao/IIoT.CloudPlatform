import { computed } from 'vue'
import { useColorMode } from '@vueuse/core'

/**
 * 主题管理 Composable
 * 提供亮色/暗色模式的切换，自动同步到 localStorage 并支持系统偏好回退。
 */
export function useTheme() {
  // useColorMode 会在 HTML 标签上应用 attribute="data-theme"
  // 并将用户偏好保存在 localStorage 的 'vueuse-color-scheme' 中
  const colorMode = useColorMode({
    attribute: 'data-theme',
    modes: {
      light: 'light',
      dark: 'dark'
    }
  })

  // 暴露当前的模式（抛出 'light' | 'dark' 状态）
  const mode = computed(() => colorMode.value === 'dark' ? 'dark' : 'light')

  // 切换主题的方法
  const toggle = () => {
    colorMode.value = colorMode.value === 'dark' ? 'light' : 'dark'
  }

  return {
    mode,
    toggle
  }
}