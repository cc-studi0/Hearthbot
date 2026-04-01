<script setup lang="ts">
import { ref, onMounted, h } from 'vue'
import {
  NLayout, NLayoutHeader, NLayoutContent, NCard, NDataTable, NSpace,
  NSelect, NTag, NButton, NMenu, NPagination
} from 'naive-ui'
import { useRouter } from 'vue-router'
import { gameRecordApi, deviceApi } from '../api'
import { useAuth } from '../composables/useAuth'
import { useSignalR } from '../composables/useSignalR'

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

const router = useRouter()
const { logout } = useAuth()
const records = ref<GameRecord[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(50)
const filterDevice = ref<string | null>(null)
const filterResult = ref<string | null>(null)
const filterDays = ref(1)
const deviceOptions = ref<{ label: string; value: string }[]>([])

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

onMounted(async () => {
  await Promise.all([loadRecords(), loadDevices()])

  const { connect } = useSignalR()
  const hub = connect()
  hub.on('NewGameRecord', () => loadRecords())
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
        <NSpace style="margin-bottom:16px">
          <NSelect v-model:value="filterDevice" :options="deviceOptions" style="width:140px"
            placeholder="全部设备" clearable @update:value="loadRecords" />
          <NSelect v-model:value="filterResult" :options="resultOptions" style="width:120px"
            placeholder="全部结果" clearable @update:value="loadRecords" />
          <NSelect v-model:value="filterDays" :options="daysOptions" style="width:120px"
            @update:value="loadRecords" />
        </NSpace>

        <NDataTable :columns="columns" :data="records" :row-key="(r: GameRecord) => r.id" />

        <NSpace justify="center" style="margin-top:16px">
          <NPagination v-model:page="page" :page-count="Math.ceil(total / pageSize)"
            @update:page="loadRecords" />
        </NSpace>
      </NCard>
    </NLayoutContent>
  </NLayout>
</template>
