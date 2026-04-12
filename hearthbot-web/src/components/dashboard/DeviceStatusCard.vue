<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { NButton, NInput, NTag } from 'naive-ui'
import type { DashboardBucket, Device } from '../../types'
import { rankToNumber } from '../../utils/rankMapping'

const props = defineProps<{
  device: Device
  bucket: DashboardBucket
  suspectedCompletion: boolean
  hintText?: string
}>()

const emit = defineEmits<{
  open: [device: Device]
  saveOrder: [deviceId: string, orderNumber: string]
}>()

const orderInput = ref('')

watch(() => props.device.deviceId, () => {
  orderInput.value = ''
}, { immediate: true })

const statusInfo = computed(() => {
  const map: Record<string, { label: string; type: 'success' | 'warning' | 'error' | 'info' | 'default' }> = {
    InGame: { label: '对局中', type: 'success' },
    Running: { label: '运行中', type: 'success' },
    Switching: { label: '切换中', type: 'warning' },
    Idle: { label: '空闲', type: 'info' },
    Offline: { label: '离线', type: 'error' }
  }
  return map[props.device.status] ?? { label: props.device.status || '未知', type: 'default' as const }
})

const winRate = computed(() => {
  const total = props.device.sessionWins + props.device.sessionLosses
  return total > 0 ? `${((props.device.sessionWins / total) * 100).toFixed(1)}%` : '-'
})

const progressPercent = computed(() => {
  const start = rankToNumber(props.device.startRank)
  const current = rankToNumber(props.device.currentRank)
  const target = rankToNumber(props.device.targetRank)

  if (start === null || current === null || target === null || target <= start) return null
  return Math.min(100, Math.max(0, ((current - start) / (target - start)) * 100))
})

const warningText = computed(() => {
  if (props.bucket === 'pending') {
    return props.hintText || `当前账号 ${props.device.currentAccount || '未知'}，开始新单前请先录入订单号`
  }
  if (props.suspectedCompletion) {
    return '已达到目标段位，等待脚本正式确认'
  }
  if (props.bucket === 'abnormal') {
    if (props.device.status === 'Offline') return '设备离线或心跳超时，请优先检查'
    if (props.device.status === 'Switching') return '当前处于账号切换阶段'
  }
  return ''
})

const cardTone = computed(() => {
  if (props.bucket === 'completed') return 'tone-completed'
  if (props.bucket === 'abnormal') return 'tone-abnormal'
  if (props.bucket === 'pending') return 'tone-pending'
  if (props.suspectedCompletion) return 'tone-suspected'
  return 'tone-active'
})

function submitOrder() {
  const value = orderInput.value.trim()
  if (!value) return
  emit('saveOrder', props.device.deviceId, value)
  orderInput.value = ''
}
</script>

<template>
  <article class="device-card" :class="cardTone" @click="emit('open', device)">
    <div class="card-top">
      <div>
        <div class="device-name">{{ device.displayName }}</div>
        <div class="device-account">{{ device.currentAccount || '未识别账号' }}</div>
      </div>
      <NTag :type="statusInfo.type" size="small" round>{{ statusInfo.label }}</NTag>
    </div>

    <div class="order-line">
      <span class="order-label">{{ device.orderNumber ? `订单 #${device.orderNumber}` : '待录订单' }}</span>
      <span class="mode">{{ device.gameMode === 'Wild' ? '狂野' : '标准' }}</span>
    </div>

    <div class="rank-line">
      <span>{{ device.currentRank || '未知段位' }}</span>
      <span v-if="device.targetRank" class="rank-arrow">→ {{ device.targetRank }}</span>
    </div>

    <div v-if="progressPercent !== null" class="progress-track">
      <span class="progress-fill" :style="{ width: `${progressPercent}%` }" />
    </div>

    <div class="meta-grid">
      <div class="meta-item">
        <span>脚本</span>
        <strong>{{ device.currentProfile || '未设置' }}</strong>
      </div>
      <div class="meta-item">
        <span>卡组</span>
        <strong>{{ device.currentDeck || '未设置' }}</strong>
      </div>
      <div class="meta-item">
        <span>战绩</span>
        <strong>{{ device.sessionWins }}W {{ device.sessionLosses }}L</strong>
      </div>
      <div class="meta-item">
        <span>胜率</span>
        <strong>{{ winRate }}</strong>
      </div>
    </div>

    <div v-if="warningText" class="warning-box">
      {{ warningText }}
    </div>

    <div v-if="bucket === 'pending'" class="pending-actions" @click.stop>
      <NInput
        v-model:value="orderInput"
        placeholder="输入订单号"
        size="small"
        clearable
      />
      <NButton type="warning" size="small" @click="submitOrder">录入订单</NButton>
    </div>

    <div v-else class="open-hint">
      查看详情与操作
    </div>
  </article>
</template>

<style scoped>
.device-card {
  border-radius: 22px;
  padding: 16px;
  background:
    radial-gradient(circle at top right, rgba(255, 255, 255, 0.86), transparent 34%),
    linear-gradient(180deg, rgba(255, 255, 255, 0.95), rgba(248, 250, 252, 0.98));
  border: 1px solid rgba(15, 23, 42, 0.08);
  box-shadow: 0 18px 34px rgba(15, 23, 42, 0.08);
  cursor: pointer;
  transition: transform 0.18s ease, box-shadow 0.18s ease;
}

.device-card:hover {
  transform: translateY(-2px);
  box-shadow: 0 22px 40px rgba(15, 23, 42, 0.12);
}

.tone-active {
  border-color: rgba(37, 99, 235, 0.12);
}

.tone-pending {
  border-color: rgba(217, 119, 6, 0.18);
  background:
    radial-gradient(circle at top right, rgba(251, 191, 36, 0.18), transparent 36%),
    linear-gradient(180deg, rgba(255, 251, 235, 0.96), rgba(255, 255, 255, 0.98));
}

.tone-abnormal {
  border-color: rgba(220, 38, 38, 0.16);
  background:
    radial-gradient(circle at top right, rgba(251, 113, 133, 0.18), transparent 36%),
    linear-gradient(180deg, rgba(254, 242, 242, 0.96), rgba(255, 255, 255, 0.98));
}

.tone-completed {
  border-color: rgba(22, 163, 74, 0.16);
  background:
    radial-gradient(circle at top right, rgba(74, 222, 128, 0.18), transparent 36%),
    linear-gradient(180deg, rgba(240, 253, 244, 0.96), rgba(255, 255, 255, 0.98));
}

.tone-suspected {
  border-color: rgba(217, 119, 6, 0.2);
  background:
    radial-gradient(circle at top right, rgba(251, 191, 36, 0.2), transparent 38%),
    linear-gradient(180deg, rgba(255, 247, 237, 0.96), rgba(255, 255, 255, 0.98));
}

.card-top {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
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

.order-line,
.rank-line {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  margin-top: 14px;
}

.order-label {
  color: #1e293b;
  font-size: 14px;
  font-weight: 700;
}

.mode,
.rank-arrow {
  color: #64748b;
  font-size: 12px;
}

.rank-line {
  color: #0f172a;
  font-size: 14px;
  font-weight: 600;
}

.progress-track {
  height: 8px;
  overflow: hidden;
  border-radius: 999px;
  background: rgba(148, 163, 184, 0.18);
  margin-top: 12px;
}

.progress-fill {
  display: block;
  height: 100%;
  border-radius: inherit;
  background: linear-gradient(90deg, #2563eb, #38bdf8);
}

.meta-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
  margin-top: 14px;
}

.meta-item span {
  display: block;
  color: #64748b;
  font-size: 12px;
}

.meta-item strong {
  display: block;
  margin-top: 4px;
  color: #0f172a;
  font-size: 14px;
}

.warning-box {
  margin-top: 14px;
  border-radius: 16px;
  padding: 12px;
  color: #9a3412;
  font-size: 13px;
  background: rgba(255, 247, 237, 0.92);
  border: 1px solid rgba(217, 119, 6, 0.16);
}

.pending-actions {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: 10px;
  margin-top: 14px;
}

.open-hint {
  margin-top: 14px;
  color: #2563eb;
  font-size: 12px;
  font-weight: 600;
}
</style>
