<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{
  level: number
  xp: number
  xpNeeded: number
}>()

const percent = computed(() => {
  if (!props.xpNeeded || props.xpNeeded <= 0) return 0
  const ratio = props.xp / props.xpNeeded
  return Math.min(100, Math.max(0, ratio * 100))
})
</script>

<template>
  <div class="pass-progress">
    <div class="pass-labels">
      <span class="pass-level">Lv.{{ level }}</span>
      <span class="pass-xp">{{ xp }} / {{ xpNeeded }} XP</span>
    </div>
    <div class="pass-bar">
      <div class="pass-bar-fill" :style="{ width: percent + '%' }" />
    </div>
  </div>
</template>

<style scoped>
.pass-progress { margin: 6px 0; }
.pass-labels {
  display: flex;
  justify-content: space-between;
  font-size: 11px;
  margin-bottom: 3px;
}
.pass-level { color: #1e293b; font-weight: 600; }
.pass-xp { color: #64748b; }
.pass-bar {
  background: #e2e8f0;
  border-radius: 4px;
  height: 6px;
  overflow: hidden;
}
.pass-bar-fill {
  height: 100%;
  background: linear-gradient(90deg, #a855f7, #7c3aed);
  border-radius: 4px;
  transition: width 0.5s ease;
}
</style>
