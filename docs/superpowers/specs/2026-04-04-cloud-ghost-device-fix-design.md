# 云控幽灵设备修复设计

## 问题描述

云控仪表板会出现同一台设备显示两条记录的 bug：一条数据冻结不动（幽灵），另一条正常更新。几乎每次运行都会复现。Bot 本身运行正常，问题仅在云控显示层。

## 根因分析

三个问题叠加导致：

1. **服务端断连不通知仪表板**：`BotHub.OnDisconnectedAsync` 只清除连接映射，不推送任何事件给仪表板。旧设备状态在前端冻结为幽灵条目。
2. **前端 `DeviceOnline`/`DeviceOffline` 触发全量 `loadData()`**：与 `DeviceUpdated` 的增量 push 操作存在竞态，异步请求交错时可能产生重复条目。
3. **客户端心跳定时器可能泄漏**：`Reconnected` 回调和 `ConnectWithRetry` 都创建 Timer，并发执行时可能泄漏旧 Timer，产生双重心跳。

## 修复方案

### 1. 服务端断连通知（BotHub.cs）

`OnDisconnectedAsync` 改为：

- 通过 connectionId 查找 deviceId
- 清除连接映射（保持现有逻辑）
- 从数据库读取该设备最新状态
- 推送 `DeviceUpdated` 给仪表板

不直接标记 Offline，仍由 DeviceWatchdog 90 秒超时判定，保持现有的防抖设计。

### 2. 前端增量更新（Dashboard.vue）

将 `DeviceOnline` 和 `DeviceOffline` 从全量 `loadData()` 改为增量更新：

- **`DeviceOnline(deviceId, displayName)`**：如果列表中没有该 deviceId，插入最小设备对象；如果已有，不做任何事（等下一次心跳 `DeviceUpdated` 带来完整数据）
- **`DeviceOffline(deviceId)`**：找到该 deviceId，将其 `status` 设为 `"Offline"`
- **`DeviceUpdated` handler 不变**（findIndex → 更新或 push）

### 3. 客户端心跳定时器防泄漏（CloudAgent.cs）

用 `object _timerLock` 保护 Timer 的创建和销毁：

```csharp
lock (_timerLock)
{
    _heartbeatTimer?.Dispose();
    _heartbeatTimer = new Timer(...);
}
```

在 `Reconnected`、`ConnectWithRetry`、`Dispose()` 三处都加锁。

### 4. 仪表板定时轮询自愈（Dashboard.vue）

- `onMounted` 中启动 `setInterval`，每 60 秒调用 `loadData()`
- 用 `isLoading` ref 做互斥，防止多个 `loadData` 并发执行
- `onUnmounted` 中清除 interval，防止内存泄漏

确保即使 SignalR 推送全部丢失，最多 60 秒后仪表板也会自动恢复到正确状态。

## 改动文件清单

| 文件 | 改动 |
|------|------|
| `HearthBot.Cloud/Hubs/BotHub.cs` | `OnDisconnectedAsync` 添加仪表板推送 |
| `hearthbot-web/src/views/Dashboard.vue` | 增量更新 + 定时轮询 |
| `BotMain/Cloud/CloudAgent.cs` | Timer 操作加锁 |
