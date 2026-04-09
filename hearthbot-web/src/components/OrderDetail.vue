<script setup lang="ts">
import { ref, watch, h } from 'vue'
import { NButton, NSelect, NInput, NSpace, NDataTable, NTag } from 'naive-ui'
import RankProgress from './RankProgress.vue'
import { gameRecordApi, deviceApi, commandApi } from '../api'
import type { Device } from '../types'

const props = defineProps<{ device: Device }>()
const emit = defineEmits<{ close: [] }>()

const editingOrder = ref(props.device.orderNumber)
const selectedDeck = ref(props.device.currentDeck)
const records = ref<any[]>([])
const recordsTotal = ref(0)
const recordsPage = ref(1)
const loadingRecords = ref(false)

function getAvailableDecks(): string[] {
  try { return JSON.parse(props.device.availableDecksJson || '[]') } catch { return [] }
}

const winRate = (() => {
  const total = props.device.sessionWins + props.device.sessionLosses
  return total > 0 ? ((props.device.sessionWins / total) * 100).toFixed(1) + '%' : '-'
})()

async function loadRecords(page = 1) {
  loadingRecords.value = true
  try {
    const res = await gameRecordApi.getAll({
      deviceId: props.device.deviceId,
      accountName: props.device.currentAccount,
      days: 0,
      page,
      pageSize: 5
    })
    records.value = page === 1 ? res.data.records : [...records.value, ...res.data.records]
    recordsTotal.value = res.data.total
    recordsPage.value = page
  } finally {
    loadingRecords.value = false
  }
}

async function saveOrderNumber() {
  await deviceApi.setOrderNumber(props.device.deviceId, editingOrder.value)
}

async function changeDeck() {
  await commandApi.send(props.device.deviceId, 'ChangeDeck', { DeckName: selectedDeck.value })
}

async function stopBot() {
  await commandApi.send(props.device.deviceId, 'Stop', {})
}

watch(() => props.device.deviceId, () => {
  records.value = []
  loadRecords()
}, { immediate: true })

const recordColumns = [
  {
    title: '结果', key: 'result', width: 50,
    render: (row: any) => {
      const isWin = row.result === 'Win'
      return h('span', { style: { color: isWin ? '#66bb6a' : '#ef5350' } }, isWin ? '胜' : '负')
    }
  },
  { title: '我方', key: 'myClass', width: 60 },
  { title: '对手', key: 'opponentClass', width: 60 },
  {
    title: '段位变化', key: 'rankChange', width: 100,
    render: (row: any) => `${row.rankBefore || '-'} → ${row.rankAfter || '-'}`
  },
  {
    title: '时长', key: 'duration', width: 60,
    render: (row: any) => {
      const m = Math.floor(row.durationSeconds / 60)
      const s = row.durationSeconds % 60
      return `${m}:${String(s).padStart(2, '0')}`
    }
  },
  {
    title: '时间', key: 'playedAt', width: 60,
    render: (row: any) => {
      const d = new Date(row.playedAt)
      return `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
    }
  }
]
</script>

<template>
  <div class="order-detail">
    <div class="detail-header">
      <div>
        <span class="detail-order">#{{ device.orderNumber || '未标记' }}</span>
        <NTag :type="device.status === 'InGame' ? 'success' : device.status === 'Idle' ? 'warning' : 'error'" size="small" style="margin-left:8px">
          {{ device.status === 'InGame' ? '对局中' : device.status === 'Idle' ? '空闲' : device.status }}
        </NTag>
      </div>
      <NButton text size="small" @click="emit('close')">关闭</NButton>
    </div>

    <div class="detail-body">
      <div class="detail-left">
        <div class="detail-section">
          <div class="section-title">基本信息</div>
          <div class="info-grid">
            <span class="info-label">账号</span><span>{{ device.currentAccount }}</span>
            <span class="info-label">设备</span><span class="device-name">{{ device.displayName }}</span>
            <span class="info-label">模式</span><span>{{ device.gameMode === 'Wild' ? '狂野' : '标准' }}</span>
            <span class="info-label">卡组</span><span>{{ device.currentDeck }}</span>
            <span class="info-label">策略</span><span>{{ device.currentProfile }}</span>
            <span class="info-label">订单号</span>
            <NSpace size="small">
              <NInput v-model:value="editingOrder" size="tiny" style="width:100px" />
              <NButton type="primary" size="tiny" @click="saveOrderNumber">保存</NButton>
            </NSpace>
          </div>
        </div>

        <div class="detail-section">
          <div class="section-title">操作</div>
          <NSpace>
            <NButton type="error" size="small" @click="stopBot">停止 Bot</NButton>
            <NSelect
              v-model:value="selectedDeck"
              :options="getAvailableDecks().map(d => ({ label: d, value: d }))"
              size="small"
              style="width:140px"
            />
            <NButton type="primary" size="small" @click="changeDeck">切换卡组</NButton>
          </NSpace>
        </div>
      </div>

      <div class="detail-right">
        <div class="detail-section">
          <div class="section-title">段位进度</div>
          <RankProgress
            v-if="device.startRank && device.targetRank"
            :start-rank="device.startRank"
            :current-rank="device.currentRank"
            :target-rank="device.targetRank"
          />
          <div class="stats-grid">
            <div class="stat-item">
              <div class="stat-label">胜</div>
              <div class="stat-value">{{ device.sessionWins }}</div>
            </div>
            <div class="stat-item">
              <div class="stat-label">负</div>
              <div class="stat-value">{{ device.sessionLosses }}</div>
            </div>
            <div class="stat-item">
              <div class="stat-label">胜率</div>
              <div class="stat-value" style="color:#66bb6a">{{ winRate }}</div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <div class="detail-section">
      <div class="section-title">最近对局</div>
      <NDataTable
        :columns="recordColumns"
        :data="records"
        :loading="loadingRecords"
        size="small"
        :bordered="false"
        :pagination="false"
      />
      <div v-if="recordsTotal > records.length" class="load-more" @click="loadRecords(recordsPage + 1)">
        加载更多 ▾
      </div>
    </div>
  </div>
</template>

<style scoped>
.order-detail {
  background: #1e1e38;
  border-radius: 10px;
  padding: 16px;
  margin-bottom: 12px;
}
.detail-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
}
.detail-order { font-size: 18px; font-weight: 700; }
.detail-body { display: flex; gap: 16px; margin-bottom: 16px; }
.detail-left { flex: 1; }
.detail-right { flex: 1; }
.detail-section {
  background: #252545;
  border-radius: 8px;
  padding: 12px;
  margin-bottom: 10px;
}
.section-title { font-size: 11px; color: #888; margin-bottom: 8px; }
.info-grid {
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 4px 12px;
  font-size: 12px;
}
.info-label { color: #888; }
.device-name { color: #4fc3f7; }
.stats-grid {
  display: flex;
  justify-content: space-around;
  margin-top: 12px;
  text-align: center;
}
.stat-item .stat-label { font-size: 11px; color: #888; }
.stat-item .stat-value { font-size: 20px; font-weight: 700; }
.load-more {
  text-align: center;
  color: #555;
  font-size: 10px;
  margin-top: 8px;
  cursor: pointer;
}
.load-more:hover { color: #888; }
</style>
