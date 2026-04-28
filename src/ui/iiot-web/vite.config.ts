import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

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
  plugins: [vue()],
  server: {
    port: process.env.PORT ? parseInt(process.env.PORT) : 5173,
    proxy: {
      '/api': {
        target: apiUrl,
        changeOrigin: true,
        secure: false
      }
    }
  }
})
