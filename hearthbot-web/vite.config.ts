import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  server: {
    proxy: {
      '/api': 'http://localhost:5000',
      '/hub': { target: 'http://localhost:5000', ws: true }
    }
  },
  build: {
    outDir: '../HearthBot.Cloud/wwwroot',
    emptyOutDir: true,
    rollupOptions: {
      output: {
        manualChunks(id: string) {
          if (id.includes('node_modules/naive-ui')) return 'naive-ui'
          if (id.includes('node_modules/@microsoft/signalr')) return 'signalr'
          if (id.includes('node_modules/chart.js') || id.includes('node_modules/vue-chartjs')) return 'chart'
        }
      }
    }
  }
})
