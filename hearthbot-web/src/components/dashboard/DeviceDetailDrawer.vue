<script setup lang="ts">
import { computed, h, onMounted, onUnmounted, ref, watch } from 'vue'
import {
  NAlert,
  NButton,
  NDataTable,
  NDrawer,
  NDrawerContent,
  NInput,
  NPopconfirm,
  NSelect,
  NSpace,
  NTag
} from 'naive-ui'
import { commandApi, deviceApi, gameRecordApi } from '../../api'
import type { Device } from '../../types'
import { getDisplayStatus } from '../../utils/dashboardState'
import { buildTargetRankOptions } from '../../utils/rankOptions'
import { rankToNumber } from '../../utils/rankMapping'
import PassProgress from '../PassProgress.vue'

const props = defineProps<{
  show: boolean
  device: Device | null
}>()

const emit = defineEmits<{
  'update:show': [show: boolean]
  refresh: []
  hide: [device: Device]
}>()

const drawerWidth = ref(typeof window === 'undefined' ? 420 : window.innerWidth)
const editingOrder = ref('')
const selectedDeck = ref<string | null>(null)
const selectedProfile = ref<string | null>(null)
const selectedTarget = ref<number | null>(null)
const records = ref<any[]>([])
const recordsLoading = ref(false)
const opMessage = ref('')

const targetOptions = buildTargetRankOptions(true).map(option => ({
  label: option.label,
  value: option.value
}))

const isMobile = computed(() => drawerWidth.value < 860)
const placement = computed(() => isMobile.value ? 'bottom' : 'right')
const drawerSize = computed(() => isMobile.value ? '90vh' : 440)

const titleText = computed(() => props.device?.orderNumber
  ? `订单 #${props.device.orderNumber}`
  : props.device?.displayName ?? '设备详情')

const displayStatus = computed(() => props.device ? getDisplayStatus(props.device) : 'Unknown')

const winRate = computed(() => {
  if (!props.device) return '-'
  const total = props.device.sessionWins + props.device.sessionLosses
  return total > 0 ? `${((props.device.sessionWins / total) * 100).toFixed(1)}%` : '-'
})

const deckOptions = computed(() => {
  if (!props.device?.availableDecksJson) return []
  try {
    return JSON.parse(props.device.availableDecksJson).map((value: string) => ({ label: value, value }))
  } catch {
    return []
  }
})

const profileOptions = computed(() => {
  if (!props.device?.availableProfilesJson) return []
  try {
    return JSON.parse(props.device.availableProfilesJson).map((value: string) => ({ label: value, value }))
  } catch {
    return []
  }
})

function syncLocalState(device: Device | null) {
  editingOrder.value = device?.orderNumber ?? ''
  selectedDeck.value = device?.currentDeck || null
  selectedProfile.value = device?.currentProfile || null
  selectedTarget.value = device?.targetRank ? rankToNumber(device.targetRank) : null
}

async function loadRecords() {
  if (!props.device) return

  recordsLoading.value = true
  try {
    const response = await gameRecordApi.getAll({
      deviceId: props.device.deviceId,
      accountName: props.device.currentAccount,
      days: 0,
      page: 1,
      pageSize: 5
    })
    records.value = response.data.records
  } finally {
    recordsLoading.value = false
  }
}

function showMessage(message: string) {
  opMessage.value = message
  window.setTimeout(() => {
    if (opMessage.value === message) opMessage.value = ''
  }, 2200)
}

async function saveOrder() {
  if (!props.device) return
  try {
    await deviceApi.setOrderNumber(props.device.deviceId, editingOrder.value)
    showMessage('订单号已保存')
    emit('refresh')
  } catch {
    showMessage('订单号保存失败')
  }
}

async function applyDeck() {
  if (!props.device || !selectedDeck.value) return
  try {
    await commandApi.changeDeck(props.device.deviceId, selectedDeck.value)
    showMessage('卡组切换指令已发送')
    emit('refresh')
  } catch {
    showMessage('卡组指令发送失败')
  }
}

async function applyProfile() {
  if (!props.device || !selectedProfile.value) return
  try {
    await commandApi.changeProfile(props.device.deviceId, selectedProfile.value)
    showMessage('策略切换指令已发送')
    emit('refresh')
  } catch {
    showMessage('策略指令发送失败')
  }
}

async function applyTarget() {
  if (!props.device || selectedTarget.value === null) return
  try {
    await commandApi.changeTarget(props.device.deviceId, selectedTarget.value)
    showMessage('目标段位指令已发送')
    emit('refresh')
  } catch {
    showMessage('目标段位指令发送失败')
  }
}

async function startDevice() {
  if (!props.device) return
  try {
    await commandApi.start(props.device.deviceId)
    showMessage('继续运行指令已发送')
  } catch {
    showMessage('继续运行指令发送失败')
  }
}

async function stopDevice() {
  if (!props.device) return
  try {
    await commandApi.stop(props.device.deviceId)
    showMessage('停止指令已发送')
  } catch {
    showMessage('停止指令发送失败')
  }
}

async function markCompleted() {
  if (!props.device) return
  try {
    await deviceApi.markCompleted(props.device.deviceId)
    showMessage('已标记为完成')
    emit('refresh')
  } catch {
    showMessage('手动完成失败')
  }
}

function hideDevice() {
  if (!props.device) return
  emit('hide', props.device)
  emit('update:show', false)
}

function handleResize() {
  drawerWidth.value = window.innerWidth
}

watch(() => props.device, async device => {
  syncLocalState(device)
  if (props.show && device) {
    await loadRecords()
  } else {
    records.value = []
  }
}, { immediate: true })

watch(() => props.show, async visible => {
  if (visible && props.device) {
    syncLocalState(props.device)
    await loadRecords()
  }
})

onMounted(() => {
  if (typeof window !== 'undefined') {
    window.addEventListener('resize', handleResize)
  }
})

onUnmounted(() => {
  if (typeof window !== 'undefined') {
    window.removeEventListener('resize', handleResize)
  }
})

const recordColumns = [
  {
    title: '结果',
    key: 'result',
    width: 60,
    render: (row: any) => {
      const isWin = row.result === 'Win'
      return h('span', { style: { color: isWin ? '#15803d' : '#dc2626', fontWeight: '700' } }, isWin ? '胜' : '负')
    }
  },
  { title: '对手', key: 'opponentClass', width: 72 },
  { title: '卡组', key: 'deckName', ellipsis: true },
  {
    title: '段位',
    key: 'rankAfter',
    width: 120,
    render: (row: any) => `${row.rankBefore || '-'} → ${row.rankAfter || '-'}`
  }
]
</script>

<template>
  <NDrawer
    :show="show"
    :placement="placement"
    :width="!isMobile ? drawerSize : undefined"
    :height="isMobile ? drawerSize : undefined"
    @update:show="emit('update:show', $event)"
  >
    <NDrawerContent :title="titleText" closable body-content-style="padding: 0;">
      <div v-if="device" class="drawer-body">
        <section class="detail-panel panel-primary">
          <div class="device-head">
            <div>
              <div class="device-name">{{ device.displayName }}</div>
              <div class="device-account">{{ device.currentAccount || '未识别账号' }}</div>
            </div>
            <NTag :type="device.isCompleted || displayStatus === 'Completed' ? 'success' : displayStatus === 'Offline' ? 'error' : displayStatus === 'Switching' ? 'warning' : 'info'" round>
              {{ device.isCompleted || displayStatus === 'Completed' ? '已完成' : displayStatus }}
            </NTag>
          </div>

          <div class="summary-grid">
            <div>
              <span>当前段位</span>
              <strong>{{ device.currentRank || '未知' }}</strong>
            </div>
            <div>
              <span>目标段位</span>
              <strong>{{ device.targetRank || '未设置' }}</strong>
            </div>
            <div>
              <span>脚本</span>
              <strong>{{ device.currentProfile || '未设置' }}</strong>
            </div>
            <div>
              <span>战绩</span>
              <strong>{{ device.sessionWins }}W {{ device.sessionLosses }}L · {{ winRate }}</strong>
            </div>
          </div>

          <div class="pass-section">
            <span class="pass-section-label">通行证</span>
            <PassProgress
              v-if="device.passLevel > 0"
              :level="device.passLevel"
              :xp="device.passXp"
              :xp-needed="device.passXpNeeded" />
            <p v-else class="pass-section-muted">暂无通行证数据</p>
          </div>
        </section>

        <section class="detail-panel">
          <h3>订单与目标</h3>
          <NSpace vertical :size="12">
            <div class="field-row">
              <NInput v-model:value="editingOrder" placeholder="输入订单号" />
              <NButton type="warning" @click="saveOrder">保存订单</NButton>
            </div>
            <div class="field-row">
              <NSelect v-model:value="selectedTarget" :options="targetOptions" placeholder="选择目标段位" />
              <NButton type="primary" @click="applyTarget">更新目标</NButton>
            </div>
          </NSpace>
        </section>

        <section class="detail-panel">
          <h3>脚本与卡组</h3>
          <NSpace vertical :size="12">
            <div class="field-row">
              <NSelect v-model:value="selectedDeck" :options="deckOptions" placeholder="选择卡组" />
              <NButton type="primary" secondary @click="applyDeck">应用卡组</NButton>
            </div>
            <div class="field-row">
              <NSelect v-model:value="selectedProfile" :options="profileOptions" placeholder="选择策略" />
              <NButton type="primary" secondary @click="applyProfile">应用策略</NButton>
            </div>
          </NSpace>
        </section>

        <section class="detail-panel">
          <h3>运行控制</h3>
          <div class="action-grid">
            <NButton type="error" @click="stopDevice">停止</NButton>
            <NButton type="primary" ghost @click="startDevice">继续运行</NButton>
            <NButton type="error" secondary @click="hideDevice">隐藏当前卡片</NButton>
            <NPopconfirm
              positive-text="确认完成"
              negative-text="取消"
              @positive-click="markCompleted"
            >
              <template #trigger>
                <NButton type="success" secondary>手动完成</NButton>
              </template>
              确认将当前订单标记为已完成？
            </NPopconfirm>
          </div>

          <NAlert v-if="opMessage" type="info" class="op-alert">
            {{ opMessage }}
          </NAlert>
        </section>

        <section class="detail-panel">
          <h3>最近对局</h3>
          <NDataTable
            :columns="recordColumns"
            :data="records"
            :loading="recordsLoading"
            size="small"
            :bordered="false"
            :pagination="false"
          />
        </section>
      </div>
    </NDrawerContent>
  </NDrawer>
</template>

<style scoped>
.drawer-body {
  padding: 16px;
  display: grid;
  gap: 14px;
  background:
    radial-gradient(circle at top right, rgba(56, 189, 248, 0.08), transparent 28%),
    linear-gradient(180deg, rgba(248, 250, 252, 0.98), rgba(255, 255, 255, 0.98));
}

.detail-panel {
  border-radius: 20px;
  padding: 16px;
  background: rgba(255, 255, 255, 0.88);
  border: 1px solid rgba(15, 23, 42, 0.08);
}

.panel-primary {
  background:
    radial-gradient(circle at top right, rgba(59, 130, 246, 0.14), transparent 30%),
    linear-gradient(180deg, rgba(239, 246, 255, 0.94), rgba(255, 255, 255, 0.98));
}

.detail-panel h3 {
  margin: 0 0 12px;
  color: #0f172a;
  font-size: 14px;
}

.device-head {
  display: flex;
  justify-content: space-between;
  gap: 12px;
}

.device-name {
  color: #0f172a;
  font-size: 18px;
  font-weight: 700;
}

.device-account {
  margin-top: 4px;
  color: #64748b;
  font-size: 13px;
}

.summary-grid {
  margin-top: 14px;
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.summary-grid span {
  display: block;
  color: #64748b;
  font-size: 12px;
}

.summary-grid strong {
  display: block;
  margin-top: 4px;
  color: #0f172a;
  font-size: 14px;
}

.field-row {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: 10px;
}

.action-grid {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
}

.op-alert {
  margin-top: 12px;
}

.pass-section {
  margin-top: 14px;
  padding-top: 12px;
  border-top: 1px dashed #e2e8f0;
}
.pass-section-label {
  display: block;
  font-size: 12px;
  color: #64748b;
  margin-bottom: 6px;
}
.pass-section-muted {
  font-size: 12px;
  color: #94a3b8;
  margin: 0;
}

@media (max-width: 640px) {
  .summary-grid,
  .field-row {
    grid-template-columns: 1fr;
  }
}
</style>
