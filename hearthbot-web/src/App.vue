<script setup lang="ts">
import { computed } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useAuth } from './composables/useAuth'

const router = useRouter()
const route = useRoute()
const { logout } = useAuth()

const isLoginPage = computed(() => route.path === '/login')

const navItems = [
  { key: '/', label: '总览', icon: '📊' },
  { key: '/records', label: '对局记录', icon: '📋' },
]

function navigate(path: string) {
  router.push(path)
}
</script>

<template>
  <!-- Login 页面：无侧边栏 -->
  <router-view v-if="isLoginPage" />

  <!-- 其他页面：侧边栏 + 顶部栏 + 内容区 -->
  <div v-else class="app-layout">
    <aside class="sidebar">
      <div class="sidebar-logo">HearthBot</div>
      <nav class="sidebar-nav">
        <div
          v-for="item in navItems"
          :key="item.key"
          class="nav-item"
          :class="{ active: route.path === item.key }"
          @click="navigate(item.key)"
        >
          <span class="nav-icon">{{ item.icon }}</span>
          <span class="nav-label">{{ item.label }}</span>
        </div>
      </nav>
      <div class="sidebar-footer">
        <div class="nav-item" @click="logout">
          <span class="nav-icon">🚪</span>
          <span class="nav-label">退出登录</span>
        </div>
      </div>
    </aside>

    <div class="main-area">
      <header class="topbar">
        <h1 class="page-title">{{ navItems.find(n => n.key === route.path)?.label || '总览' }}</h1>
        <span class="user-name">管理员</span>
      </header>
      <main class="content">
        <router-view />
      </main>
    </div>
  </div>
</template>

<style scoped>
.app-layout {
  display: flex;
  min-height: 100vh;
}

.sidebar {
  width: 200px;
  background: #1e293b;
  display: flex;
  flex-direction: column;
  flex-shrink: 0;
  position: fixed;
  top: 0;
  left: 0;
  bottom: 0;
  z-index: 100;
}

.sidebar-logo {
  padding: 20px 16px;
  font-size: 18px;
  font-weight: 700;
  color: #fff;
  letter-spacing: 1px;
}

.sidebar-nav {
  flex: 1;
  padding: 8px 0;
}

.sidebar-footer {
  padding: 8px 0;
  border-top: 1px solid #334155;
}

.nav-item {
  display: flex;
  align-items: center;
  padding: 10px 16px;
  cursor: pointer;
  color: #94a3b8;
  font-size: 14px;
  transition: all 0.2s ease;
  border-left: 3px solid transparent;
}

.nav-item:hover {
  background: #334155;
  color: #fff;
}

.nav-item.active {
  background: #334155;
  color: #fff;
  border-left-color: #3b82f6;
}

.nav-icon {
  margin-right: 10px;
  font-size: 16px;
}

.nav-label {
  font-size: 14px;
}

.main-area {
  flex: 1;
  margin-left: 200px;
  display: flex;
  flex-direction: column;
  min-height: 100vh;
}

.topbar {
  height: 56px;
  background: #ffffff;
  border-bottom: 1px solid #e2e8f0;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 24px;
  flex-shrink: 0;
}

.page-title {
  font-size: 16px;
  font-weight: 600;
  color: #1e293b;
  margin: 0;
}

.user-name {
  font-size: 14px;
  color: #64748b;
}

.content {
  flex: 1;
  background: #f5f7fa;
  padding: 24px;
}
</style>
