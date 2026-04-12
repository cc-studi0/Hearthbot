<script setup lang="ts">
interface CompletionBannerItem {
  key: string
  deviceId: string
  title: string
  detail: string
}

defineProps<{
  items: CompletionBannerItem[]
}>()

const emit = defineEmits<{
  dismiss: [key: string]
  open: [deviceId: string]
}>()
</script>

<template>
  <div v-if="items.length" class="banner-shell">
    <div class="banner-title">新完成订单</div>
    <div class="banner-list">
      <button
        v-for="item in items"
        :key="item.key"
        class="banner-item"
        type="button"
        @click="emit('open', item.deviceId)"
      >
        <div class="banner-copy">
          <div class="item-title">{{ item.title }}</div>
          <div class="item-detail">{{ item.detail }}</div>
        </div>
        <span
          class="dismiss"
          @click.stop="emit('dismiss', item.key)"
        >关闭</span>
      </button>
    </div>
  </div>
</template>

<style scoped>
.banner-shell {
  margin-bottom: 16px;
  border-radius: 22px;
  padding: 14px;
  border: 1px solid rgba(217, 119, 6, 0.18);
  background:
    radial-gradient(circle at top right, rgba(251, 191, 36, 0.22), transparent 34%),
    linear-gradient(180deg, rgba(255, 251, 235, 0.96), rgba(255, 247, 237, 0.98));
  box-shadow: 0 18px 34px rgba(217, 119, 6, 0.14);
}

.banner-title {
  color: #9a3412;
  font-size: 12px;
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
}

.banner-list {
  display: grid;
  gap: 10px;
  margin-top: 10px;
}

.banner-item {
  width: 100%;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  border: 0;
  border-radius: 18px;
  padding: 12px 14px;
  background: rgba(255, 255, 255, 0.84);
  cursor: pointer;
  text-align: left;
}

.banner-copy {
  min-width: 0;
}

.item-title {
  color: #7c2d12;
  font-size: 14px;
  font-weight: 700;
}

.item-detail {
  margin-top: 4px;
  color: #9a3412;
  font-size: 12px;
  opacity: 0.88;
}

.dismiss {
  flex-shrink: 0;
  color: #b45309;
  font-size: 12px;
  font-weight: 600;
}
</style>
