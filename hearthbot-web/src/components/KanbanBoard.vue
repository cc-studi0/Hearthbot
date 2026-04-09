<script setup lang="ts">
import { computed, ref } from 'vue'
import { rankToNumber } from '../utils/rankMapping'
import OrderCard from './OrderCard.vue'
import OrderDetail from './OrderDetail.vue'
import { deviceApi } from '../api'
import type { Device } from '../types'

const props = defineProps<{ devices: Device[] }>()
const emit = defineEmits<{ refresh: [] }>()

const expandedDeviceId = ref<string | null>(null)
const orderInputs = ref<Record<string, string>>({})

function isCompletedToday(d: Device): boolean {
  if (!d.startedAt || !d.orderNumber) return false
  const started = new Date(d.startedAt)
  const today = new Date()
  return started.toDateString() === today.toDateString()
}

function isCompleted(d: Device): boolean {
  if (!d.orderNumber || !d.targetRank || !d.currentRank) return false
  const current = rankToNumber(d.currentRank)
  const target = rankToNumber(d.targetRank)
  if (current == null || target == null) return false
  return current >= target
}

const onlineDevices = computed(() =>
  props.devices.filter(d => d.status !== 'Offline' || isCompletedToday(d))
)

const unmarked = computed(() =>
  onlineDevices.value.filter(d => !d.orderNumber && d.status !== 'Offline')
)

const active = computed(() =>
  onlineDevices.value.filter(d => d.orderNumber && !isCompleted(d) && d.status !== 'Offline')
)

const completed = computed(() =>
  onlineDevices.value.filter(d => d.orderNumber && (isCompleted(d) || (d.status === 'Offline' && isCompletedToday(d))))
)

function toggleDetail(device: Device) {
  expandedDeviceId.value = expandedDeviceId.value === device.deviceId ? null : device.deviceId
}

async function saveOrder(deviceId: string, orderNumber: string) {
  if (!orderNumber.trim()) return
  await deviceApi.setOrderNumber(deviceId, orderNumber.trim())
  orderInputs.value[deviceId] = ''
  emit('refresh')
}
</script>

<template>
  <div class="kanban-board">
    <!-- 未标记列 -->
    <div class="kanban-column">
      <div class="column-header" style="color:#ffa726">
        未标记 <span class="column-count">{{ unmarked.length }}</span>
      </div>
      <OrderCard
        v-for="d in unmarked"
        :key="d.deviceId"
        :device="d"
        column="unmarked"
        v-model:order-input="orderInputs[d.deviceId]"
        @save-order="saveOrder"
      />
      <div v-if="!unmarked.length" class="column-empty">暂无未标记设备</div>
    </div>

    <!-- 进行中列 -->
    <div class="kanban-column kanban-column-active">
      <div class="column-header" style="color:#4fc3f7">
        进行中 <span class="column-count">{{ active.length }}</span>
      </div>
      <template v-for="d in active" :key="d.deviceId">
        <OrderDetail
          v-if="expandedDeviceId === d.deviceId"
          :device="d"
          @close="expandedDeviceId = null"
        />
        <OrderCard
          v-else
          :device="d"
          column="active"
          @click="toggleDetail"
        />
      </template>
      <div v-if="!active.length" class="column-empty">暂无进行中订单</div>
    </div>

    <!-- 今日完成列 -->
    <div class="kanban-column">
      <div class="column-header" style="color:#ab47bc">
        今日完成 <span class="column-count">{{ completed.length }}</span>
      </div>
      <OrderCard
        v-for="d in completed"
        :key="d.deviceId"
        :device="d"
        column="completed"
      />
      <div v-if="!completed.length" class="column-empty">暂无完成订单</div>
      <div v-if="completed.length" class="archive-hint">隔天自动归档</div>
    </div>
  </div>
</template>

<style scoped>
.kanban-board {
  display: flex;
  gap: 12px;
  min-height: 400px;
}
.kanban-column {
  flex: 1;
  background: #1e1e38;
  border-radius: 10px;
  padding: 12px;
}
.kanban-column-active { flex: 1.3; }
.column-header {
  font-weight: 600;
  margin-bottom: 12px;
}
.column-count {
  background: #3a3a5a;
  border-radius: 10px;
  padding: 1px 8px;
  font-size: 11px;
}
.column-empty {
  color: #555;
  font-size: 12px;
  text-align: center;
  padding: 24px 0;
}
.archive-hint {
  font-size: 10px;
  color: #555;
  text-align: center;
  margin-top: 8px;
}
</style>
