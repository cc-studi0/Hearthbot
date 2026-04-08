<script setup lang="ts">
import { ref, onMounted, h } from 'vue'
import {
  NLayout, NLayoutHeader, NLayoutContent, NCard, NDataTable, NSpace,
  NSelect, NTag, NButton, NMenu, NPagination, NGrid, NGi, NStatistic
} from 'naive-ui'
import { useRouter } from 'vue-router'
import { gameRecordApi, deviceApi } from '../api'
import { useAuth } from '../composables/useAuth'
import { useSignalR } from '../composables/useSignalR'
import { Bar, Line } from 'vue-chartjs'
import {
  Chart as ChartJS, CategoryScale, LinearScale, BarElement,
  PointElement, LineElement, Title, Tooltip, Legend
} from 'chart.js'
import { rankToNumber, numberToRank } from '../utils/rankMapping'

ChartJS.register(CategoryScale, LinearScale, BarElement, PointElement, LineElement, Title, Tooltip, Legend)

interface GameRecord {
  id: number
  deviceId: string
  accountName: string
  result: string
  myClass: string
  opponentClass: string
  deckName: string
  durationSeconds: number
  rankBefore: string
  rankAfter: string
  playedAt: string
}

interface AccountStats {
  accountName: string
  totalGames: number
  wins: number
  losses: number
  concedes: number
  winRate: number
  matchups: { opponentClass: string; games: number; wins: number; winRate: number }[]
  rankHistory: { date: string; rank: string }[]
  dailyTrend: { date: string; games: number; wins: number; winRate: number }[]
}

const router = useRouter()
const { logout } = useAuth()
const records = ref<GameRecord[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(50)
const filterDevice = ref<string | null>(null)
const filterResult = ref<string | null>(null)
const filterDays = ref(7)
const filterAccount = ref<string | null>(null)
const deviceOptions = ref<{ label: string; value: string }[]>([])
const accountOptions = ref<{ label: string; value: string }[]>([])
const stats = ref<AccountStats | null>(null)

const resultOptions = [
  { label: '全部结果', value: '' },
  { label: '胜利', value: 'Win' },
  { label: '失败', value: 'Loss' },
  { label: '投降', value: 'Concede' }
]

const daysOptions = [
  { label: '今天', value: 1 },
  { label: '最近3天', value: 3 },
  { label: '最近7天', value: 7 },
  { label: '全部', value: 0 }
]

function formatDuration(s: number) {
  return `${Math.floor(s / 60)}:${(s % 60).toString().padStart(2, '0')}`
}

function formatTime(iso: string) {
  const d = new Date(iso)
  return `${d.getHours().toString().padStart(2, '0')}:${d.getMinutes().toString().padStart(2, '0')}`
}

const columns = [
  { title: '时间', key: 'playedAt', width: 60, render: (r: GameRecord) => formatTime(r.playedAt) },
  { title: '设备', key: 'deviceId', width: 80 },
  { title: '账号', key: 'accountName', width: 100 },
  {
    title: '结果', key: 'result', width: 60,
    render: (r: GameRecord) => {
      const map: Record<string, 'success' | 'warning' | 'error' | 'default'> = { Win: 'success', Loss: 'error', Concede: 'warning' }
      const label: Record<string, string> = { Win: '胜利', Loss: '失败', Concede: '投降' }
      return h(NTag, { type: map[r.result] || 'default', size: 'small' }, () => label[r.result] || r.result)
    }
  },
  { title: '我方', key: 'myClass', width: 80 },
  { title: '对手', key: 'opponentClass', width: 80 },
  { title: '卡组', key: 'deckName', width: 100 },
  { title: '用时', key: 'duration', width: 60, render: (r: GameRecord) => formatDuration(r.durationSeconds) },
  { title: '段位变化', key: 'rankChange', width: 140, render: (r: GameRecord) => `${r.rankBefore} → ${r.rankAfter}` }
]

async function loadRecords() {
  const params: Record<string, any> = { page: page.value, pageSize: pageSize.value, days: filterDays.value }
  if (filterDevice.value) params.deviceId = filterDevice.value
  if (filterResult.value) params.result = filterResult.value
  if (filterAccount.value) params.accountName = filterAccount.value

  const { data } = await gameRecordApi.getAll(params)
  records.value = data.records
  total.value = data.total
}

async function loadDevices() {
  const { data } = await deviceApi.getAll()
  deviceOptions.value = [
    { label: '全部设备', value: '' },
    ...data.map((d: any) => ({ label: d.displayName, value: d.deviceId }))
  ]
}

async function loadAccounts() {
  const params: Record<string, any> = {}
  if (filterDevice.value) params.deviceId = filterDevice.value
  const { data } = await gameRecordApi.getAccounts(params)
  accountOptions.value = [
    { label: '全部账号', value: '' },
    ...data.map((a: string) => ({ label: a, value: a }))
  ]
}

async function loadStats() {
  if (!filterAccount.value) {
    stats.value = null
    return
  }
  const params: Record<string, any> = { accountName: filterAccount.value, days: filterDays.value }
  if (filterDevice.value) params.deviceId = filterDevice.value
  const { data } = await gameRecordApi.getStats(params)
  stats.value = data
}

async function onFilterChange() {
  page.value = 1
  await Promise.all([loadRecords(), loadStats()])
}

async function onAccountChange() {
  page.value = 1
  await Promise.all([loadRecords(), loadStats()])
}

async function onDeviceChange() {
  page.value = 1
  await Promise.all([loadRecords(), loadAccounts(), loadStats()])
}

function matchupChartData() {
  if (!stats.value || stats.value.matchups.length === 0) return null
  const m = stats.value.matchups
  return {
    labels: m.map(x => x.opponentClass),
    datasets: [{
      label: '胜率%',
      data: m.map(x => x.winRate),
      backgroundColor: m.map(x => x.winRate >= 50 ? 'rgba(99,226,183,0.7)' : 'rgba(224,98,98,0.7)')
    }]
  }
}

function dailyTrendChartData() {
  if (!stats.value || stats.value.dailyTrend.length === 0) return null
  const t = stats.value.dailyTrend
  return {
    labels: t.map(x => x.date.slice(5)),
    datasets: [{
      label: '胜率%',
      data: t.map(x => x.winRate),
      borderColor: '#63e2b7',
      backgroundColor: 'rgba(99,226,183,0.2)',
      fill: true,
      tension: 0.3
    }]
  }
}

function rankChartData() {
  if (!stats.value || stats.value.rankHistory.length === 0) return null
  const r = stats.value.rankHistory
  const values = r.map(x => rankToNumber(x.rank)).filter((v): v is number => v !== null)
  const labels = r.map(x => x.date.slice(5))
  if (values.length === 0) return null
  return {
    labels,
    datasets: [{
      label: '段位',
      data: values,
      borderColor: '#7ec8e3',
      backgroundColor: 'rgba(126,200,227,0.2)',
      fill: true,
      tension: 0.3
    }]
  }
}

const barOptions = {
  indexAxis: 'y' as const,
  responsive: true,
  maintainAspectRatio: false,
  plugins: { legend: { display: false } },
  scales: { x: { min: 0, max: 100 } }
}

const lineOptions = {
  responsive: true,
  maintainAspectRatio: false,
  plugins: { legend: { display: false } },
  scales: { y: { min: 0, max: 100 } }
}

function rankChartOptions() {
  return {
    responsive: true,
    maintainAspectRatio: false,
    plugins: { legend: { display: false } },
    scales: {
      y: {
        reverse: false,
        ticks: {
          callback: (v: number) => numberToRank(v)
        }
      }
    }
  }
}

function winRateColor(rate: number): string {
  if (rate >= 55) return '#63e2b7'
  if (rate >= 45) return '#f2c97d'
  return '#e06262'
}

onMounted(async () => {
  await Promise.all([loadRecords(), loadDevices(), loadAccounts()])

  const { connect } = useSignalR()
  const hub = connect()
  hub.on('NewGameRecord', () => {
    loadRecords()
    loadStats()
    loadAccounts()
  })
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
      <NCard title="对局记录">
        <!-- 筛选栏 -->
        <NSpace style="margin-bottom:16px">
          <NSelect v-model:value="filterAccount" :options="accountOptions" style="width:160px"
            placeholder="全部账号" clearable @update:value="onAccountChange" />
          <NSelect v-model:value="filterDevice" :options="deviceOptions" style="width:140px"
            placeholder="全部设备" clearable @update:value="onDeviceChange" />
          <NSelect v-model:value="filterResult" :options="resultOptions" style="width:120px"
            placeholder="全部结果" clearable @update:value="onFilterChange" />
          <NSelect v-model:value="filterDays" :options="daysOptions" style="width:120px"
            @update:value="onFilterChange" />
        </NSpace>

        <!-- 统计卡片 -->
        <div v-if="stats" style="margin-bottom:20px">
          <NGrid :cols="4" :x-gap="12">
            <NGi>
              <NCard size="small">
                <NStatistic label="总场次" :value="stats.totalGames" />
              </NCard>
            </NGi>
            <NGi>
              <NCard size="small">
                <NStatistic label="胜场" :value="stats.wins">
                  <template #suffix><span style="color:#63e2b7"> W</span></template>
                </NStatistic>
              </NCard>
            </NGi>
            <NGi>
              <NCard size="small">
                <NStatistic label="负场" :value="stats.losses + stats.concedes">
                  <template #suffix><span style="color:#e06262"> L</span></template>
                </NStatistic>
              </NCard>
            </NGi>
            <NGi>
              <NCard size="small">
                <NStatistic label="胜率">
                  <template #default>
                    <span :style="{ color: winRateColor(stats.winRate), fontWeight: 'bold', fontSize: '24px' }">
                      {{ stats.winRate }}%
                    </span>
                  </template>
                </NStatistic>
              </NCard>
            </NGi>
          </NGrid>
        </div>

        <!-- 图表区 -->
        <div v-if="stats" style="margin-bottom:20px">
          <NGrid :cols="2" :x-gap="12">
            <NGi>
              <NCard title="职业对阵胜率" size="small">
                <div style="height:250px" v-if="matchupChartData()">
                  <Bar :data="matchupChartData()!" :options="barOptions" />
                </div>
                <div v-else style="color:#999;text-align:center;padding:40px">暂无数据</div>
              </NCard>
            </NGi>
            <NGi>
              <NCard title="每日胜率趋势" size="small">
                <div style="height:250px" v-if="dailyTrendChartData()">
                  <Line :data="dailyTrendChartData()!" :options="lineOptions" />
                </div>
                <div v-else style="color:#999;text-align:center;padding:40px">暂无数据</div>
              </NCard>
            </NGi>
          </NGrid>
        </div>

        <!-- 段位变化 -->
        <div v-if="stats && rankChartData()" style="margin-bottom:20px">
          <NCard title="段位变化" size="small">
            <div style="height:200px">
              <Line :data="rankChartData()!" :options="rankChartOptions()" />
            </div>
          </NCard>
        </div>

        <!-- 对局记录表 -->
        <NDataTable :columns="columns" :data="records" :row-key="(r: GameRecord) => r.id" />

        <NSpace justify="center" style="margin-top:16px">
          <NPagination v-model:page="page" :page-count="Math.ceil(total / pageSize)"
            @update:page="loadRecords" />
        </NSpace>
      </NCard>
    </NLayoutContent>
  </NLayout>
</template>
