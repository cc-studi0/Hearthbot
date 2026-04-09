<script setup lang="ts">
import { NCard, NStatistic, NGrid, NGi, NSkeleton } from 'naive-ui'
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
</script>

<template>
  <NGrid :cols="4" :x-gap="12" style="margin-bottom:24px" v-if="loading">
    <NGi v-for="i in 4" :key="i">
      <NCard><NSkeleton text :repeat="2" /></NCard>
    </NGi>
  </NGrid>
  <NGrid :cols="4" :x-gap="12" style="margin-bottom:24px" v-else>
    <NGi>
      <NCard>
        <NStatistic label="在线设备" :value="stats.onlineCount">
          <template #suffix>/ {{ stats.totalCount }}</template>
        </NStatistic>
      </NCard>
    </NGi>
    <NGi>
      <NCard><NStatistic label="今日对局" :value="stats.todayGames" /></NCard>
    </NGi>
    <NGi>
      <NCard><NStatistic label="今日胜率" :value="todayWinRate" /></NCard>
    </NGi>
    <NGi>
      <NCard><NStatistic label="今日完成" :value="stats.completedCount" /></NCard>
    </NGi>
  </NGrid>
</template>
