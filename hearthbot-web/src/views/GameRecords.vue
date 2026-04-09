<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { NSkeleton } from 'naive-ui'
import { gameRecordApi } from '../api'
import { useSignalR } from '../composables/useSignalR'

interface DeviceRecord {
  result: string
  opponentClass: string
  deckName: string
  durationSeconds: number
  rankBefore: string
  rankAfter: string
  playedAt: string
}

interface DeviceColumn {
  deviceId: string
  displayName: string
  currentAccount: string
  currentRank: string
  totalGames: number
  wins: number
  losses: number
  concedes: number
  winRate: number
  records: DeviceRecord[]
}

const deviceColumns = ref<DeviceColumn[]>([])
const loading = ref(true)

function formatDuration(s: number) {
  return `${Math.floor(s / 60)}:${(s % 60).toString().padStart(2, '0')}`
}

function winRateColor(rate: number): string {
  if (rate >= 55) return '#22c55e'
  if (rate >= 45) return '#f59e0b'
  return '#ef4444'
}

function resultLabel(r: string): { text: string; color: string; bg: string } {
  if (r === 'Win') return { text: 'W', color: '#22c55e', bg: '#f0fdf4' }
  if (r === 'Loss') return { text: 'L', color: '#ef4444', bg: '#fef2f2' }
  return { text: 'C', color: '#f59e0b', bg: '#fffbeb' }
}

function rankDisplay(rec: DeviceRecord): string | null {
  if (!rec.rankAfter) return null
  if (rec.rankBefore && rec.rankBefore !== rec.rankAfter)
    return `${rec.rankBefore}→${rec.rankAfter}`
  return rec.rankAfter
}

async function loadData() {
  const { data } = await gameRecordApi.byDevice()
  deviceColumns.value = data
  loading.value = false
}

onMounted(async () => {
  await loadData()

  const { connect } = useSignalR()
  const hub = connect()
  hub.on('NewGameRecord', () => loadData())
})
</script>

<template>
  <div class="records-page">
    <div class="records-card">
      <h2 class="records-title">设备战绩</h2>

      <!-- 骨架屏 -->
      <div v-if="loading" class="skeleton-row">
        <div v-for="i in 4" :key="i" class="skeleton-col">
          <NSkeleton text :repeat="12" />
        </div>
      </div>

      <!-- 设备列视图 -->
      <div v-else-if="deviceColumns.length > 0" class="device-columns">
        <div
          v-for="(dev, idx) in deviceColumns"
          :key="dev.deviceId"
          class="device-column"
          :style="{ borderRight: idx < deviceColumns.length - 1 ? '1px solid #e2e8f0' : 'none' }"
        >
          <!-- 设备头部 -->
          <div class="device-header">
            <div class="device-name">{{ dev.displayName }}</div>
            <div class="device-meta">{{ dev.currentAccount }} · {{ dev.currentRank }}</div>
            <div class="device-winrate" :style="{ color: winRateColor(dev.winRate) }">
              {{ dev.wins }}W {{ dev.losses }}L ({{ dev.winRate }}%)
            </div>
          </div>

          <!-- 对局列表 -->
          <div class="record-list">
            <div
              v-for="(rec, ri) in dev.records"
              :key="ri"
              class="record-row"
            >
              <span
                class="record-result"
                :style="{ color: resultLabel(rec.result).color, background: resultLabel(rec.result).bg }"
              >
                {{ resultLabel(rec.result).text }}
              </span>
              <span class="record-opponent">{{ rec.opponentClass }}</span>
              <span class="record-deck">{{ rec.deckName }}</span>
              <span class="record-duration">{{ formatDuration(rec.durationSeconds) }}</span>
              <span v-if="rankDisplay(rec)" class="record-rank">{{ rankDisplay(rec) }}</span>
            </div>
          </div>
        </div>
      </div>

      <!-- 无数据 -->
      <div v-else class="no-data">暂无设备数据</div>
    </div>
  </div>
</template>

<style scoped>
.records-card {
  background: #ffffff;
  border-radius: 8px;
  padding: 20px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.06);
}

.records-title {
  font-size: 16px;
  font-weight: 600;
  color: #1e293b;
  margin: 0 0 16px 0;
}

.skeleton-row {
  display: flex;
  gap: 16px;
}

.skeleton-col {
  flex: 1;
}

.device-columns {
  display: flex;
  overflow-x: auto;
}

.device-column {
  flex: 1;
  min-width: 180px;
  padding: 0 12px;
}

.device-header {
  margin-bottom: 12px;
}

.device-name {
  font-weight: 600;
  color: #3b82f6;
  font-size: 14px;
}

.device-meta {
  color: #64748b;
  font-size: 12px;
}

.device-winrate {
  font-weight: 600;
  font-size: 13px;
}

.record-list {
  display: flex;
  flex-direction: column;
  gap: 3px;
  font-size: 12px;
}

.record-row {
  display: flex;
  align-items: center;
  gap: 6px;
  line-height: 1.6;
  padding: 2px 4px;
}

.record-row:hover {
  background: #f8fafc;
  border-radius: 4px;
}

.record-result {
  font-weight: 600;
  width: 20px;
  height: 20px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: 4px;
  font-size: 11px;
  flex-shrink: 0;
}

.record-opponent {
  color: #1e293b;
}

.record-deck {
  color: #64748b;
  font-size: 11px;
}

.record-duration {
  color: #94a3b8;
  font-size: 11px;
}

.record-rank {
  color: #3b82f6;
  font-size: 11px;
}

.no-data {
  color: #94a3b8;
  text-align: center;
  padding: 60px;
}
</style>
