<script setup lang="ts">
import { computed } from 'vue'
import { NTag, NInput, NButton, NSpace, NPopconfirm } from 'naive-ui'
import RankProgress from './RankProgress.vue'
import type { Device } from '../types'

const props = defineProps<{
  device: Device
  column: 'unmarked' | 'active' | 'completed'
}>()

const emit = defineEmits<{
  click: [device: Device]
  saveOrder: [deviceId: string, orderNumber: string]
  markCompleted: [deviceId: string]
}>()

const orderInput = defineModel<string>('orderInput', { default: '' })

const statusMap: Record<string, { type: 'success' | 'warning' | 'error' | 'default'; label: string }> = {
  InGame: { type: 'success', label: '对局中' },
  Running: { type: 'success', label: '运行中' },
  Switching: { type: 'warning', label: '切换中' },
  Idle: { type: 'warning', label: '空闲' },
  Online: { type: 'success', label: '在线' },
  Offline: { type: 'error', label: '离线' },
}

const statusInfo = computed(() => statusMap[props.device.status] || { type: 'default' as const, label: props.device.status })

const winRate = computed(() => {
  const total = props.device.sessionWins + props.device.sessionLosses
  return total > 0 ? ((props.device.sessionWins / total) * 100).toFixed(1) + '%' : '-'
})

const borderColor = computed(() => {
  if (props.column === 'completed') return '#8b5cf6'
  if (props.device.status === 'InGame') return '#22c55e'
  if (props.device.status === 'Running' || props.device.status === 'Switching') return '#3b82f6'
  if (props.device.status === 'Idle' || props.device.status === 'Online') return '#3b82f6'
  return '#e2e8f0'
})
</script>

<template>
  <div
    class="order-card"
    :style="{ borderLeftColor: borderColor, opacity: column === 'completed' ? 0.85 : 1 }"
    @click="column !== 'unmarked' && emit('click', device)"
  >
    <div class="card-header">
      <span class="order-number" v-if="column !== 'unmarked'">#{{ device.orderNumber }}</span>
      <span class="need-order" v-else>需要填写订单号</span>
      <NTag :type="statusInfo.type" size="tiny">{{ statusInfo.label }}</NTag>
    </div>

    <div class="card-info">
      账号: {{ device.currentAccount }} · 设备: <span class="device-name">{{ device.displayName }}</span>
    </div>

    <!-- 未标记列：订单号输入框 -->
    <div v-if="column === 'unmarked'" class="order-input">
      <NSpace>
        <NInput
          v-model:value="orderInput"
          placeholder="输入订单号..."
          size="tiny"
          style="width:140px"
          @click.stop
        />
        <NButton
          type="warning"
          size="tiny"
          @click.stop="emit('saveOrder', device.deviceId, orderInput)"
        >确认</NButton>
      </NSpace>
    </div>

    <!-- 进行中列：段位进度 + 对局信息 -->
    <template v-if="column === 'active'">
      <RankProgress
        v-if="device.startRank && device.targetRank"
        :start-rank="device.startRank"
        :current-rank="device.currentRank"
        :target-rank="device.targetRank"
      />
      <div v-if="device.status === 'InGame' && device.currentOpponent" class="game-status">
        <div class="game-matchup">
          <span>{{ device.currentDeck }} vs {{ device.currentOpponent }}</span>
        </div>
        <div class="game-stats">
          {{ device.sessionWins }}胜{{ device.sessionLosses }}负({{ winRate }})
        </div>
      </div>
      <div v-else class="game-stats">
        {{ device.sessionWins }}胜{{ device.sessionLosses }}负({{ winRate }}) · 等待下一局...
      </div>
      <div class="card-footer">
        <NPopconfirm
          @positive-click="emit('markCompleted', device.deviceId)"
          positive-text="确认完成"
          negative-text="取消"
        >
          <template #trigger>
            <NButton
              size="tiny"
              type="primary"
              ghost
              @click.stop
            >手动完成</NButton>
          </template>
          确认将订单 #{{ device.orderNumber }} 标记为已完成？
        </NPopconfirm>
        <span class="card-expand-hint">点击展开详情 ▸</span>
      </div>
    </template>

    <!-- 已完成列：最终战绩 -->
    <template v-if="column === 'completed'">
      <div class="completed-info">
        {{ device.startRank }} → {{ device.currentRank }} · {{ device.sessionWins }}胜{{ device.sessionLosses }}负({{ winRate }})
      </div>
      <div class="completed-time">
        设备: {{ device.displayName }}
      </div>
    </template>
  </div>
</template>

<style scoped>
.order-card {
  background: #ffffff;
  border-radius: 8px;
  padding: 12px;
  margin-bottom: 8px;
  border: 1px solid #e2e8f0;
  border-left: 3px solid #e2e8f0;
  cursor: pointer;
  transition: all 0.2s ease;
}
.order-card:hover {
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.08);
  transform: translateY(-2px);
}
.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 4px;
}
.order-number {
  font-weight: 600;
  color: #3b82f6;
}
.need-order { color: #f59e0b; font-size: 11px; }
.card-info {
  font-size: 12px;
  color: #64748b;
  margin-bottom: 6px;
}
.device-name {
  color: #3b82f6;
  font-weight: 500;
}
.order-input { margin-top: 8px; }
.game-status {
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  padding: 8px;
  font-size: 11px;
  margin-top: 6px;
  color: #1e293b;
}
.game-matchup {
  margin-bottom: 2px;
  color: #1e293b;
}
.game-stats {
  font-size: 11px;
  color: #64748b;
  margin-top: 4px;
}
.card-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-top: 8px;
  gap: 8px;
}
.card-expand-hint {
  font-size: 10px;
  color: #94a3b8;
}
.completed-info {
  font-size: 11px;
  color: #64748b;
  margin-top: 4px;
}
.completed-time {
  font-size: 11px;
  color: #94a3b8;
}
</style>
