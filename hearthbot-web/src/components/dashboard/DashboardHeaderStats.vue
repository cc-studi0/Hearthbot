<script setup lang="ts">
import { computed } from 'vue'
import { NSkeleton } from 'naive-ui'
import type { Stats } from '../../types'

const props = defineProps<{
  stats: Stats
  loading: boolean
}>()

const cards = computed(() => [
  { key: 'online', label: '在线设备', value: `${props.stats.onlineCount}/${props.stats.totalCount}`, tone: 'blue' },
  { key: 'games', label: '今日对局', value: String(props.stats.todayGames), tone: 'green' },
  { key: 'done', label: '7天完成', value: String(props.stats.completedCount), tone: 'amber' },
  { key: 'bad', label: '异常设备', value: String(props.stats.abnormalCount), tone: 'red' }
])
</script>

<template>
  <div class="stats-grid">
    <div
      v-for="card in cards"
      :key="card.key"
      class="stat-card"
      :class="`tone-${card.tone}`"
    >
      <template v-if="loading">
        <NSkeleton text style="width: 54%; height: 14px" />
        <NSkeleton text style="width: 72%; height: 28px; margin-top: 10px" />
      </template>
      <template v-else>
        <div class="stat-label">{{ card.label }}</div>
        <div class="stat-value">{{ card.value }}</div>
      </template>
    </div>
  </div>
</template>

<style scoped>
.stats-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
  margin-bottom: 16px;
}

.stat-card {
  position: relative;
  overflow: hidden;
  min-height: 96px;
  border-radius: 20px;
  padding: 16px;
  border: 1px solid rgba(15, 23, 42, 0.08);
  background:
    radial-gradient(circle at top right, rgba(255, 255, 255, 0.88), transparent 42%),
    linear-gradient(180deg, rgba(255, 255, 255, 0.92), rgba(248, 250, 252, 0.98));
  box-shadow: 0 18px 32px rgba(15, 23, 42, 0.08);
}

.stat-card::after {
  content: '';
  position: absolute;
  inset: auto 0 0 0;
  height: 4px;
  opacity: 0.82;
}

.tone-blue::after { background: linear-gradient(90deg, #2563eb, #38bdf8); }
.tone-green::after { background: linear-gradient(90deg, #15803d, #4ade80); }
.tone-amber::after { background: linear-gradient(90deg, #d97706, #fbbf24); }
.tone-red::after { background: linear-gradient(90deg, #dc2626, #fb7185); }

.stat-label {
  color: #64748b;
  font-size: 12px;
  letter-spacing: 0.06em;
  text-transform: uppercase;
}

.stat-value {
  margin-top: 10px;
  color: #0f172a;
  font-size: 30px;
  font-weight: 700;
  line-height: 1.05;
}

@media (min-width: 860px) {
  .stats-grid {
    grid-template-columns: repeat(4, minmax(0, 1fr));
  }
}
</style>
