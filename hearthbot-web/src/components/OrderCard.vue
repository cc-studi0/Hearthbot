<script setup lang="ts">
import { computed } from 'vue'
import { NTag, NInput, NButton, NSpace } from 'naive-ui'
import RankProgress from './RankProgress.vue'
import type { Device } from '../types'

const props = defineProps<{
  device: Device
  column: 'unmarked' | 'active' | 'completed'
}>()

const emit = defineEmits<{
  click: [device: Device]
  saveOrder: [deviceId: string, orderNumber: string]
}>()

const orderInput = defineModel<string>('orderInput', { default: '' })

const statusMap: Record<string, { type: 'success' | 'warning' | 'error' | 'default'; label: string }> = {
  InGame: { type: 'success', label: '对局中' },
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
  if (props.column === 'completed') return '#ab47bc'
  if (props.device.status === 'InGame') return '#66bb6a'
  if (props.device.status === 'Idle' || props.device.status === 'Online') return '#42a5f5'
  return '#555'
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
      <div class="card-expand-hint">点击展开详情 ▸</div>
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
  background: #252545;
  border-radius: 8px;
  padding: 12px;
  margin-bottom: 8px;
  border-left: 3px solid #555;
  cursor: pointer;
  transition: background 0.2s;
}
.order-card:hover { background: #2a2a50; }
.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 4px;
}
.order-number { font-weight: 600; }
.need-order { color: #ffa726; font-size: 11px; }
.card-info {
  font-size: 12px;
  color: #aaa;
  margin-bottom: 6px;
}
.device-name { color: #4fc3f7; }
.order-input { margin-top: 8px; }
.game-status {
  background: #1a1a2e;
  border-radius: 6px;
  padding: 8px;
  font-size: 11px;
  margin-top: 6px;
}
.game-matchup { margin-bottom: 2px; }
.game-stats { font-size: 11px; color: #888; margin-top: 4px; }
.card-expand-hint {
  font-size: 10px;
  color: #555;
  text-align: right;
  margin-top: 6px;
}
.completed-info {
  font-size: 11px;
  color: #888;
  margin-top: 4px;
}
.completed-time {
  font-size: 11px;
  color: #666;
}
</style>
