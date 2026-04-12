<script setup lang="ts">
import type { DashboardBucket, DashboardCounts } from '../../types'

const props = defineProps<{
  counts: DashboardCounts
  activeTab: DashboardBucket
}>()

const emit = defineEmits<{
  change: [tab: DashboardBucket]
}>()

const tabs: Array<{ key: DashboardBucket; label: string }> = [
  { key: 'active', label: '进行中' },
  { key: 'pending', label: '待录单' },
  { key: 'abnormal', label: '异常' },
  { key: 'completed', label: '已完成' }
]

function getCount(tab: DashboardBucket): number {
  return props.counts[tab]
}
</script>

<template>
  <div class="tabs-shell">
    <button
      v-for="tab in tabs"
      :key="tab.key"
      type="button"
      class="tab-pill"
      :class="{ active: activeTab === tab.key }"
      @click="emit('change', tab.key)"
    >
      <span>{{ tab.label }}</span>
      <strong>{{ getCount(tab.key) }}</strong>
    </button>
  </div>
</template>

<style scoped>
.tabs-shell {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
  margin-bottom: 16px;
}

.tab-pill {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  border: 1px solid rgba(15, 23, 42, 0.08);
  border-radius: 18px;
  padding: 12px 14px;
  background: rgba(255, 255, 255, 0.78);
  color: #475569;
  cursor: pointer;
  transition: all 0.18s ease;
}

.tab-pill strong {
  font-size: 18px;
  color: #0f172a;
}

.tab-pill.active {
  border-color: rgba(37, 99, 235, 0.18);
  background:
    radial-gradient(circle at top right, rgba(56, 189, 248, 0.18), transparent 34%),
    linear-gradient(180deg, rgba(239, 246, 255, 0.96), rgba(255, 255, 255, 0.98));
  box-shadow: 0 12px 24px rgba(37, 99, 235, 0.12);
}

.tab-pill.active strong {
  color: #1d4ed8;
}

@media (min-width: 860px) {
  .tabs-shell {
    grid-template-columns: repeat(4, minmax(0, 1fr));
  }
}
</style>
