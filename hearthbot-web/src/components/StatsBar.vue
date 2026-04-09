<script setup lang="ts">
import { computed } from 'vue'
import type { Stats } from '../types'

const props = defineProps<{
  stats: Stats
  loading: boolean
}>()

const todayWinRate = computed(() => {
  const total = props.stats.todayWins + props.stats.todayLosses
  return total > 0 ? ((props.stats.todayWins / total) * 100).toFixed(1) + '%' : '-'
})

const cards = computed(() => [
  { label: '在线设备', value: `${props.stats.onlineCount} / ${props.stats.totalCount}`, color: '#3b82f6' },
  { label: '今日对局', value: props.stats.todayGames, color: '#22c55e' },
  { label: '今日胜率', value: todayWinRate.value, color: '#f59e0b' },
  { label: '今日完成', value: props.stats.completedCount, color: '#8b5cf6' },
])
</script>

<template>
  <div class="stats-bar">
    <template v-if="loading">
      <div v-for="i in 4" :key="i" class="stat-card loading-card">
        <div class="loading-line" />
        <div class="loading-line short" />
      </div>
    </template>
    <template v-else>
      <div
        v-for="card in cards"
        :key="card.label"
        class="stat-card"
        :style="{ borderLeftColor: card.color }"
      >
        <div class="stat-value">{{ card.value }}</div>
        <div class="stat-label">{{ card.label }}</div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.stats-bar {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 16px;
  margin-bottom: 24px;
}

.stat-card {
  background: #ffffff;
  border-radius: 8px;
  padding: 16px 20px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.06);
  border-left: 4px solid #e2e8f0;
}

.stat-value {
  font-size: 24px;
  font-weight: 700;
  color: #1e293b;
  line-height: 1.2;
}

.stat-label {
  font-size: 13px;
  color: #64748b;
  margin-top: 4px;
}

.loading-card {
  padding: 20px;
}

.loading-line {
  height: 24px;
  background: #e2e8f0;
  border-radius: 4px;
  margin-bottom: 8px;
  animation: pulse 1.5s infinite;
}

.loading-line.short {
  width: 60%;
  height: 14px;
  margin-bottom: 0;
}

@keyframes pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.5; }
}
</style>
