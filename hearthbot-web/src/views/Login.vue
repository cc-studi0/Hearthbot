<script setup lang="ts">
import { ref } from 'vue'
import { NCard, NForm, NFormItem, NInput, NButton, NAlert } from 'naive-ui'
import { useAuth } from '../composables/useAuth'

const username = ref('')
const password = ref('')
const { login, loading, error } = useAuth()

function onSubmit() {
  login(username.value, password.value)
}
</script>

<template>
  <div style="display:flex;align-items:center;justify-content:center;min-height:100vh;background:#1a1a2e">
    <NCard title="HearthBot 云控" style="width:360px">
      <NAlert v-if="error" type="error" style="margin-bottom:16px">{{ error }}</NAlert>
      <NForm @submit.prevent="onSubmit">
        <NFormItem label="用户名">
          <NInput v-model:value="username" placeholder="admin" />
        </NFormItem>
        <NFormItem label="密码">
          <NInput v-model:value="password" type="password" placeholder="密码"
            @keyup.enter="onSubmit" />
        </NFormItem>
        <NButton type="primary" block :loading="loading" @click="onSubmit">登录</NButton>
      </NForm>
    </NCard>
  </div>
</template>
