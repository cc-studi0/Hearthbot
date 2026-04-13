<script setup lang="ts">
import { computed } from 'vue'
import { NButton, NTag } from 'naive-ui'
import type { CompletedOrderSnapshot } from '../../types'
import { getRemainingRetentionDays } from '../../utils/completedHistory'

const props = defineProps<{
  snapshot: CompletedOrderSnapshot
}>()

const emit = defineEmits<{
  hide: [id: number]
}>()

const winRate = computed(() => {
  const total = props.snapshot.wins + props.snapshot.losses
  return total > 0 ? `${((props.snapshot.wins / total) * 100).toFixed(1)}%` : '-'
})

const remainingDays = computed(() => getRemainingRetentionDays(props.snapshot.expiresAt))

const completedTime = computed(() => {
  const date = new Date(props.snapshot.completedAt)
  if (Number.isNaN(date.getTime())) return props.snapshot.completedAt
  return `${date.getMonth() + 1}-${date.getDate()} ${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`
})
</script>

<template>
  <article class="snapshot-card">
    <div class="card-top">
      <div>
        <div class="order-line">订单 #{{ snapshot.orderNumber || '未记录' }}</div>
        <div class="account-line">{{ snapshot.accountName || '未知账号' }} · {{ snapshot.displayName }}</div>
      </div>
      <NTag type="success" round>保留 {{ remainingDays }} 天</NTag>
    </div>

    <div class="rank-line">
      {{ snapshot.startRank || '未知' }} → {{ snapshot.completedRank || snapshot.targetRank || '未知' }}
    </div>

    <div class="meta-grid">
      <div>
        <span>卡组</span>
        <strong>{{ snapshot.deckName || '未记录' }}</strong>
      </div>
      <div>
        <span>脚本</span>
        <strong>{{ snapshot.profileName || '未记录' }}</strong>
      </div>
      <div>
        <span>战绩</span>
        <strong>{{ snapshot.wins }}W {{ snapshot.losses }}L</strong>
      </div>
      <div>
        <span>胜率</span>
        <strong>{{ winRate }}</strong>
      </div>
    </div>

    <div class="bottom-line">
      <span>完成于 {{ completedTime }}</span>
      <NButton text type="error" @click="emit('hide', snapshot.id)">移出已完成</NButton>
    </div>
  </article>
</template>

<style scoped>
.snapshot-card {
  border-radius: 22px;
  padding: 16px;
  background:
    radial-gradient(circle at top right, rgba(74, 222, 128, 0.18), transparent 34%),
    linear-gradient(180deg, rgba(240, 253, 244, 0.96), rgba(255, 255, 255, 0.98));
  border: 1px solid rgba(22, 163, 74, 0.16);
  box-shadow: 0 18px 34px rgba(15, 23, 42, 0.08);
}

.card-top,
.bottom-line {
  display: flex;
  justify-content: space-between;
  gap: 10px;
  align-items: center;
}

.order-line {
  color: #14532d;
  font-size: 16px;
  font-weight: 700;
}

.account-line {
  margin-top: 4px;
  color: #4b5563;
  font-size: 13px;
}

.rank-line {
  margin-top: 14px;
  color: #0f172a;
  font-size: 15px;
  font-weight: 700;
}

.meta-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
  margin-top: 14px;
}

.meta-grid span {
  display: block;
  color: #64748b;
  font-size: 12px;
}

.meta-grid strong {
  display: block;
  margin-top: 4px;
  color: #14532d;
  font-size: 14px;
}

.bottom-line {
  margin-top: 14px;
  color: #64748b;
  font-size: 12px;
}
</style>
