# 云控幽灵设备修复 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复云控仪表板显示重复设备（幽灵设备）和设备状态冻结的 bug。

**Architecture:** 三处独立修复 — 服务端断连时推送设备状态给仪表板、前端改为增量更新消除竞态、客户端心跳定时器加锁防泄漏。加一个 60 秒轮询兜底确保自愈。

**Tech Stack:** C# / ASP.NET Core SignalR (服务端 + 客户端), Vue 3 + TypeScript (前端)

---

## 文件清单

| 文件 | 操作 | 职责 |
|------|------|------|
| `HearthBot.Cloud/Hubs/BotHub.cs` | 修改 | `OnDisconnectedAsync` 添加仪表板推送 |
| `BotMain/Cloud/CloudAgent.cs` | 修改 | 心跳定时器操作加锁 |
| `hearthbot-web/src/views/Dashboard.vue` | 修改 | 增量更新 + 定时轮询自愈 |

---

### Task 1: 服务端断连通知仪表板（BotHub.cs）

**Files:**
- Modify: `HearthBot.Cloud/Hubs/BotHub.cs:66-72`

- [ ] **Step 1: 修改 OnDisconnectedAsync**

将 `BotHub.cs` 的 `OnDisconnectedAsync` 方法从：

```csharp
public override async Task OnDisconnectedAsync(Exception? exception)
{
    // 只清除连接映射，不立即标记 Offline
    // 让 DeviceWatchdog 通过心跳超时来判定真正离线，避免网络波动导致状态闪烁
    _devices.RemoveConnection(Context.ConnectionId);
    await base.OnDisconnectedAsync(exception);
}
```

改为：

```csharp
public override async Task OnDisconnectedAsync(Exception? exception)
{
    var deviceId = _devices.GetDeviceIdByConnection(Context.ConnectionId);
    _devices.RemoveConnection(Context.ConnectionId);

    // 推送当前设备状态给仪表板，防止前端出现幽灵条目
    if (deviceId != null)
    {
        var device = await _devices.GetDevice(deviceId);
        if (device != null)
            await _dashboard.Clients.All.SendAsync("DeviceUpdated", device);
    }

    await base.OnDisconnectedAsync(exception);
}
```

- [ ] **Step 2: 在 DeviceManager 中添加 GetDevice 方法**

在 `HearthBot.Cloud/Services/DeviceManager.cs` 的 `RemoveConnection` 方法之后添加：

```csharp
public async Task<Device?> GetDevice(string deviceId)
{
    using var scope = _scopeFactory.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CloudDbContext>();
    return await db.Devices.FindAsync(deviceId);
}
```

- [ ] **Step 3: 验证编译通过**

Run: `dotnet build HearthBot.Cloud/HearthBot.Cloud.csproj`
Expected: Build succeeded

- [ ] **Step 4: 提交**

```bash
git add HearthBot.Cloud/Hubs/BotHub.cs HearthBot.Cloud/Services/DeviceManager.cs
git commit -m "fix: 服务端断连时推送设备状态给仪表板，消除幽灵设备"
```

---

### Task 2: 客户端心跳定时器防泄漏（CloudAgent.cs）

**Files:**
- Modify: `BotMain/Cloud/CloudAgent.cs`

- [ ] **Step 1: 添加锁字段**

在 `CloudAgent.cs` 的字段声明区域（第 16 行 `private Timer? _heartbeatTimer;` 之后）添加：

```csharp
private readonly object _timerLock = new();
```

- [ ] **Step 2: 给 ConnectWithRetry 中的 Timer 操作加锁**

将 `ConnectWithRetry` 方法中第 100-102 行从：

```csharp
_heartbeatTimer?.Dispose();
_heartbeatTimer = new Timer(_ => _ = SendHeartbeatAsync(),
    null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
```

改为：

```csharp
lock (_timerLock)
{
    _heartbeatTimer?.Dispose();
    _heartbeatTimer = new Timer(_ => _ = SendHeartbeatAsync(),
        null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
}
```

- [ ] **Step 3: 给 Reconnected 回调中的 Timer 操作加锁**

将 `Reconnected` 回调中第 74-76 行从：

```csharp
_heartbeatTimer?.Dispose();
_heartbeatTimer = new Timer(_ => _ = SendHeartbeatAsync(),
    null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
```

改为：

```csharp
lock (_timerLock)
{
    _heartbeatTimer?.Dispose();
    _heartbeatTimer = new Timer(_ => _ = SendHeartbeatAsync(),
        null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
}
```

- [ ] **Step 4: 给 Dispose 方法中的 Timer 操作加锁**

将 `Dispose` 方法中第 187 行从：

```csharp
_heartbeatTimer?.Dispose();
```

改为：

```csharp
lock (_timerLock)
{
    _heartbeatTimer?.Dispose();
    _heartbeatTimer = null;
}
```

- [ ] **Step 5: 验证编译通过**

Run: `dotnet build BotMain/BotMain.csproj`
Expected: Build succeeded

- [ ] **Step 6: 提交**

```bash
git add BotMain/Cloud/CloudAgent.cs
git commit -m "fix: 心跳定时器操作加锁，防止重连时泄漏"
```

---

### Task 3: 前端增量更新 + 定时轮询自愈（Dashboard.vue）

**Files:**
- Modify: `hearthbot-web/src/views/Dashboard.vue`

- [ ] **Step 1: 添加 onUnmounted 导入和 isLoading / pollTimer 变量**

将第 2 行的 import 从：

```typescript
import { ref, onMounted, computed, h } from 'vue'
```

改为：

```typescript
import { ref, onMounted, onUnmounted, computed, h } from 'vue'
```

在第 45 行 `const editingOrderNumber = ref('')` 之后添加：

```typescript
const isLoading = ref(false)
let pollTimer: ReturnType<typeof setInterval> | null = null
```

- [ ] **Step 2: 给 loadData 加互斥锁**

将 `loadData` 函数（第 115-119 行）从：

```typescript
async function loadData() {
  const [devRes, statRes] = await Promise.all([deviceApi.getAll(), deviceApi.getStats()])
  devices.value = devRes.data
  stats.value = statRes.data
}
```

改为：

```typescript
async function loadData() {
  if (isLoading.value) return
  isLoading.value = true
  try {
    const [devRes, statRes] = await Promise.all([deviceApi.getAll(), deviceApi.getStats()])
    devices.value = devRes.data
    stats.value = statRes.data
  } finally {
    isLoading.value = false
  }
}
```

- [ ] **Step 3: 将 DeviceOnline / DeviceOffline 改为增量更新，添加轮询**

将 `onMounted` 中的 SignalR 事件处理（第 127-137 行）从：

```typescript
  hub.on('DeviceUpdated', (device: Device) => {
    const idx = devices.value.findIndex(d => d.deviceId === device.deviceId)
    if (idx >= 0) devices.value[idx] = device
    else devices.value.push(device)
    if (selectedDevice.value?.deviceId === device.deviceId)
      selectedDevice.value = device
  })

  hub.on('DeviceOnline', () => loadData())
  hub.on('DeviceOffline', () => loadData())
})
```

改为：

```typescript
  hub.on('DeviceUpdated', (device: Device) => {
    const idx = devices.value.findIndex(d => d.deviceId === device.deviceId)
    if (idx >= 0) devices.value[idx] = device
    else devices.value.push(device)
    if (selectedDevice.value?.deviceId === device.deviceId)
      selectedDevice.value = device
  })

  hub.on('DeviceOnline', (deviceId: string, displayName: string) => {
    const idx = devices.value.findIndex(d => d.deviceId === deviceId)
    if (idx < 0) {
      devices.value.push({ deviceId, displayName, status: 'Idle' } as Device)
    }
  })

  hub.on('DeviceOffline', (deviceId: string) => {
    const idx = devices.value.findIndex(d => d.deviceId === deviceId)
    if (idx >= 0) devices.value[idx] = { ...devices.value[idx], status: 'Offline' }
  })

  // 每 60 秒全量同步兜底，确保漏消息时也能自愈
  pollTimer = setInterval(() => loadData(), 60000)
})

onUnmounted(() => {
  if (pollTimer) {
    clearInterval(pollTimer)
    pollTimer = null
  }
})
```

- [ ] **Step 4: 验证前端编译通过**

Run: `cd hearthbot-web && npm run build`
Expected: Build succeeded，无 TypeScript 错误

- [ ] **Step 5: 提交**

```bash
git add hearthbot-web/src/views/Dashboard.vue
git commit -m "fix: 仪表板增量更新消除竞态 + 60秒轮询自愈"
```
