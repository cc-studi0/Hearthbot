<script setup lang="ts">
import { ref, onMounted } from 'vue'
import {
  NLayout, NLayoutHeader, NLayoutContent, NCard, NSpace,
  NButton, NMenu, NSkeleton
} from 'naive-ui'
import { useRouter } from 'vue-router'
import { gameRecordApi } from '../api'
import { useAuth } from '../composables/useAuth'
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

const router = useRouter()
const { logout } = useAuth()
const deviceColumns = ref<DeviceColumn[]>([])
const loading = ref(true)

function formatDuration(s: number) {
  return `${Math.floor(s / 60)}:${(s % 60).toString().padStart(2, '0')}`
}

function winRateColor(rate: number): string {
  if (rate >= 55) return '#63e2b7'
  if (rate >= 45) return '#f2c97d'
  return '#e06262'
}

function resultLabel(r: string): { text: string; color: string } {
  if (r === 'Win') return { text: 'W', color: '#63e2b7' }
  if (r === 'Loss') return { text: 'L', color: '#e06262' }
  return { text: 'C', color: '#f2c97d' }
}

function rankDisplay(rec: DeviceRecord): string | null {
  if (!rec.rankBefore && !rec.rankAfter) return null
  if (rec.rankBefore && rec.rankAfter && rec.rankBefore !== rec.rankAfter)
    return `${rec.rankBefore}→${rec.rankAfter}`
  return rec.rankAfter || rec.rankBefore || null
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

const menuOptions = [
  { label: '总览', key: '/' },
  { label: '对局记录', key: '/records' }
]
</script>

<template>
  <NLayout style="min-height:100vh">
    <NLayoutHeader bordered style="padding:0 24px;display:flex;align-items:center;justify-content:space-between;height:56px">
      <NSpace align="center">
        <strong style="font-size:16px;color:#63e2b7">HearthBot 云控</strong>
        <NMenu mode="horizontal" :options="menuOptions" :value="router.currentRoute.value.path"
          @update:value="(k: string) => router.push(k)" />
      </NSpace>
      <NButton text @click="logout">退出</NButton>
    </NLayoutHeader>

    <NLayoutContent style="padding:24px">
      <NCard title="设备战绩">
        <!-- 骨架屏 -->
        <div v-if="loading" style="display:flex;gap:16px;">
          <div v-for="i in 4" :key="i" style="flex:1;">
            <NSkeleton text :repeat="12" />
          </div>
        </div>

        <!-- 设备列视图 -->
        <div v-else-if="deviceColumns.length > 0" style="display:flex;overflow-x:auto;">
          <div
            v-for="(dev, idx) in deviceColumns"
            :key="dev.deviceId"
            :style="{
              flex: '1',
              minWidth: '180px',
              padding: '0 12px',
              borderRight: idx < deviceColumns.length - 1 ? '1px solid #333' : 'none'
            }"
          >
            <!-- 设备头部 -->
            <div style="margin-bottom:12px;">
              <div style="font-weight:bold;color:#63e2b7;font-size:14px;">{{ dev.displayName }}</div>
              <div style="color:#999;font-size:12px;">{{ dev.currentAccount }} · {{ dev.currentRank }}</div>
              <div :style="{ fontWeight: 'bold', fontSize: '13px', color: winRateColor(dev.winRate) }">
                {{ dev.wins }}W {{ dev.losses }}L ({{ dev.winRate }}%)
              </div>
            </div>

            <!-- 对局列表 -->
            <div style="display:flex;flex-direction:column;gap:3px;font-size:12px;">
              <div
                v-for="(rec, ri) in dev.records"
                :key="ri"
                style="display:flex;align-items:center;gap:4px;line-height:1.6;"
              >
                <span :style="{ color: resultLabel(rec.result).color, fontWeight: 'bold', width: '16px' }">
                  {{ resultLabel(rec.result).text }}
                </span>
                <span style="color:#ccc;">{{ rec.opponentClass }}</span>
                <span style="color:#888;font-size:11px;">{{ rec.deckName }}</span>
                <span style="color:#666;font-size:11px;">{{ formatDuration(rec.durationSeconds) }}</span>
                <span v-if="rankDisplay(rec)" style="color:#7ec8e3;font-size:11px;">{{ rankDisplay(rec) }}</span>
              </div>
            </div>
          </div>
        </div>

        <!-- 无数据 -->
        <div v-else style="color:#999;text-align:center;padding:60px;">
          暂无设备数据
        </div>
      </NCard>
    </NLayoutContent>
  </NLayout>
</template>
