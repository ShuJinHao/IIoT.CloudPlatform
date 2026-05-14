import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'

// Aspire 注入的环境变量可能是这些格式之一
const apiUrl = process.env.VITE_API_URL
  || process.env['services__iiot-gateway__http__0']
  || process.env['services__iiot-gateway__https__0']
  || process.env['services__iiot-httpapi__http__0']
  || process.env['services__iiot-httpapi__https__0']
  || 'http://localhost:5191'

console.log('=== ALL ENV WITH iiot or VITE ===')
console.log(Object.entries(process.env).filter(([k]) => /iiot|vite|PORT/i.test(k)))
console.log('=== Using API target:', apiUrl, '===')

export default defineConfig({
  plugins: [vue(), tailwindcss()],
  server: {
    port: process.env.PORT ? parseInt(process.env.PORT) : 5173,
    proxy: {
      '/api': {
        target: apiUrl,
        changeOrigin: true,
        secure: false
      }
    }
  },
  build: {
    // 大体积第三方库拆出共享 chunk，减小路由懒加载块
    chunkSizeWarningLimit: 600,
    rollupOptions: {
      output: {
        manualChunks(id) {
          const normalizedId = id.replace(/\\/g, '/')
          if (!normalizedId.includes('/node_modules/')) {
            return undefined
          }

          if (
            normalizedId.includes('/node_modules/vue/') ||
            normalizedId.includes('/node_modules/@vue/') ||
            normalizedId.includes('/node_modules/vue-router/') ||
            normalizedId.includes('/node_modules/pinia/')
          ) {
            return 'vendor-vue'
          }

          if (normalizedId.includes('/node_modules/axios/')) {
            return 'vendor-http'
          }

          if (normalizedId.includes('/node_modules/vue-echarts/')) {
            return 'vendor-vue-echarts'
          }

          if (normalizedId.includes('/node_modules/zrender/')) {
            return 'vendor-zrender'
          }

          if (normalizedId.includes('/node_modules/echarts/')) {
            return 'vendor-echarts'
          }

          return undefined
        }
      }
    }
  }
})
