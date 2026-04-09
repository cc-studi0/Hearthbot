<script setup lang="ts">
import { computed } from 'vue'
import { rankToNumber } from '../utils/rankMapping'

const props = defineProps<{
  startRank: string
  currentRank: string
  targetRank: string
}>()

const progress = computed(() => {
  const start = rankToNumber(props.startRank)
  const current = rankToNumber(props.currentRank)
  const target = rankToNumber(props.targetRank)
  if (start == null || current == null || target == null || target <= start) return 0
  return Math.min(100, Math.max(0, ((current - start) / (target - start)) * 100))
})
</script>

<template>
  <div class="rank-progress">
    <div class="rank-labels">
      <span class="rank-start">{{ startRank }}</span>
      <span class="rank-current">{{ currentRank }}</span>
      <span class="rank-target">{{ targetRank }}</span>
    </div>
    <div class="rank-bar">
      <div class="rank-bar-fill" :style="{ width: progress + '%' }" />
    </div>
  </div>
</template>

<style scoped>
.rank-progress { margin: 6px 0; }
.rank-labels {
  display: flex;
  justify-content: space-between;
  font-size: 11px;
  margin-bottom: 3px;
}
.rank-start, .rank-target { color: #888; }
.rank-current { color: #fff; font-weight: 600; }
.rank-bar {
  background: #1a1a2e;
  border-radius: 4px;
  height: 6px;
  overflow: hidden;
}
.rank-bar-fill {
  height: 100%;
  background: linear-gradient(90deg, #ffa726, #66bb6a);
  border-radius: 4px;
  transition: width 0.5s ease;
}
</style>
