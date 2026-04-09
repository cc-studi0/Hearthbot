<script setup lang="ts">
import { ref } from 'vue'
import { useAuth } from '../composables/useAuth'

const username = ref('')
const password = ref('')
const { login, loading, error } = useAuth()

function onSubmit() {
  login(username.value, password.value)
}
</script>

<template>
  <div class="login-page">
    <div class="login-card">
      <h1 class="login-title">HearthBot</h1>
      <p class="login-subtitle">云控管理平台</p>

      <div v-if="error" class="login-error">{{ error }}</div>

      <form @submit.prevent="onSubmit">
        <div class="form-group">
          <label class="form-label">用户名</label>
          <input
            v-model="username"
            class="form-input"
            placeholder="admin"
            autocomplete="username"
          />
        </div>
        <div class="form-group">
          <label class="form-label">密码</label>
          <input
            v-model="password"
            type="password"
            class="form-input"
            placeholder="密码"
            autocomplete="current-password"
            @keyup.enter="onSubmit"
          />
        </div>
        <button
          type="submit"
          class="login-btn"
          :disabled="loading"
        >
          {{ loading ? '登录中...' : '登录' }}
        </button>
      </form>
    </div>
  </div>
</template>

<style scoped>
.login-page {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 100vh;
  background: linear-gradient(135deg, #e0e7ff 0%, #f5f7fa 100%);
}

.login-card {
  width: 400px;
  background: #ffffff;
  border-radius: 12px;
  padding: 40px;
  box-shadow: 0 4px 24px rgba(0, 0, 0, 0.08);
}

.login-title {
  font-size: 24px;
  font-weight: 700;
  color: #3b82f6;
  text-align: center;
  margin: 0 0 4px 0;
}

.login-subtitle {
  font-size: 14px;
  color: #94a3b8;
  text-align: center;
  margin: 0 0 32px 0;
}

.login-error {
  background: #fef2f2;
  color: #ef4444;
  border: 1px solid #fecaca;
  border-radius: 8px;
  padding: 10px 14px;
  font-size: 13px;
  margin-bottom: 16px;
}

.form-group {
  margin-bottom: 20px;
}

.form-label {
  display: block;
  font-size: 13px;
  font-weight: 500;
  color: #1e293b;
  margin-bottom: 6px;
}

.form-input {
  width: 100%;
  padding: 10px 12px;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  font-size: 14px;
  color: #1e293b;
  background: #ffffff;
  outline: none;
  transition: border-color 0.2s ease;
  box-sizing: border-box;
}

.form-input:focus {
  border-color: #3b82f6;
  box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.1);
}

.form-input::placeholder {
  color: #94a3b8;
}

.login-btn {
  width: 100%;
  padding: 10px 0;
  background: #3b82f6;
  color: #ffffff;
  border: none;
  border-radius: 8px;
  font-size: 15px;
  font-weight: 500;
  cursor: pointer;
  transition: background 0.2s ease;
  margin-top: 8px;
}

.login-btn:hover {
  background: #2563eb;
}

.login-btn:disabled {
  background: #93c5fd;
  cursor: not-allowed;
}
</style>
