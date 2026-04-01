import { createApp } from 'vue'
import { createRouter, createWebHistory } from 'vue-router'
import App from './App.vue'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/login', component: () => import('./views/Login.vue') },
    { path: '/', component: () => import('./views/Dashboard.vue'), meta: { auth: true } },
    { path: '/records', component: () => import('./views/GameRecords.vue'), meta: { auth: true } },
  ]
})

router.beforeEach((to) => {
  if (to.meta.auth && !localStorage.getItem('token'))
    return '/login'
})

createApp(App).use(router).mount('#app')
