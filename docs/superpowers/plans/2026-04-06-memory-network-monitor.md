# 内存泄漏监控 + 网络异常检测 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 Hearthbot 添加内存增量监控和游戏网络断连检测，集成到现有 Watchdog 恢复流程中。

**Architecture:** 新增 MemoryMonitor 和 NetworkMonitor 两个独立模块，各自带定时器线程。通过回调通知 Watchdog 触发已有的 EnterRecovering 流程。Payload 端新增 GC 和 NETSTATUS 命令处理。BotService 暴露 Pipe 命令代理方法供监控器使用。

**Tech Stack:** C# / .NET, System.Diagnostics.Process, 反射 (Assembly-CSharp Network 类), TCP Pipe 通信

---

### Task 1: MemoryMonitor — 内存增量监控

**Files:**
- Create: `BotMain/MemoryMonitor.cs`

- [ ] **Step 1: 创建 MemoryMonitor.cs**

```csharp
using System;
using System.Collections.Generic;
using System.Threading;

namespace BotMain
{
    public class MemoryMonitor : IDisposable
    {
        // ── 配置 ──
        public int SampleIntervalSeconds { get; set; } = 30;
        public int WindowSize { get; set; } = 20;
        public int GrowthThresholdMB { get; set; } = 500;
        public int GcRecoveryThresholdMB { get; set; } = 100;

        // ── 外部回调 ──
        public Func<long?> GetHearthstoneMemoryBytes { get; set; }
        public Func<bool> RequestGarbageCollection { get; set; }
        public Action<string> OnMemoryAlert { get; set; }
        public Action<string> Log { get; set; }

        // ── 内部状态 ──
        private volatile bool _active;
        private Thread _thread;
        private readonly LinkedList<(DateTime Time, long Bytes)> _samples = new LinkedList<(DateTime, long)>();
        private bool _gcRequested;

        public void Start()
        {
            if (_active) return;
            _active = true;
            _samples.Clear();
            _gcRequested = false;

            _thread = new Thread(TickLoop)
            {
                Name = "MemoryMonitor",
                IsBackground = true
            };
            _thread.Start();
            Log?.Invoke("[MemoryMonitor] 已启动");
        }

        public void Stop()
        {
            _active = false;
            Log?.Invoke("[MemoryMonitor] 已停止");
        }

        public void Dispose() => Stop();

        private void TickLoop()
        {
            while (_active)
            {
                try
                {
                    Tick();
                }
                catch (Exception ex)
                {
                    Log?.Invoke($"[MemoryMonitor] tick 异常: {ex.Message}");
                }

                Thread.Sleep(SampleIntervalSeconds * 1000);
            }
        }

        private void Tick()
        {
            var memBytes = GetHearthstoneMemoryBytes?.Invoke();
            if (memBytes == null || memBytes.Value <= 0)
                return;

            var now = DateTime.UtcNow;
            var currentMB = memBytes.Value / (1024L * 1024L);

            // 添加采样
            _samples.AddLast((now, memBytes.Value));
            while (_samples.Count > WindowSize)
                _samples.RemoveFirst();

            if (_samples.Count < 2)
                return;

            // 如果上次触发了 GC，检查是否回落
            if (_gcRequested)
            {
                _gcRequested = false;
                var prevSample = _samples.Last.Previous?.Value ?? _samples.First.Value;
                var prevMB = prevSample.Bytes / (1024L * 1024L);
                var dropMB = prevMB - currentMB;

                if (dropMB >= GcRecoveryThresholdMB)
                {
                    Log?.Invoke($"[MemoryMonitor] GC 有效，内存回落 {dropMB}MB (当前 {currentMB}MB)");
                    _samples.Clear();
                    return;
                }

                Log?.Invoke($"[MemoryMonitor] GC 无效，内存未回落 (当前 {currentMB}MB)，触发重启恢复");
                OnMemoryAlert?.Invoke("内存泄漏");
                _samples.Clear();
                return;
            }

            // 增量检测：窗口首尾差
            var oldest = _samples.First.Value;
            var newest = _samples.Last.Value;
            var growthMB = (newest.Bytes - oldest.Bytes) / (1024L * 1024L);

            if (growthMB >= GrowthThresholdMB)
            {
                Log?.Invoke($"[MemoryMonitor] 检测到内存增长 {growthMB}MB (阈值 {GrowthThresholdMB}MB)，窗口 {_samples.Count} 个采样，当前 {currentMB}MB，尝试 GC");

                var gcResult = false;
                try { gcResult = RequestGarbageCollection?.Invoke() ?? false; }
                catch (Exception ex) { Log?.Invoke($"[MemoryMonitor] GC 请求失败: {ex.Message}"); }

                if (gcResult)
                {
                    _gcRequested = true;
                    // 下次 Tick 检查 GC 效果
                }
                else
                {
                    Log?.Invoke("[MemoryMonitor] GC 请求未成功，触发重启恢复");
                    OnMemoryAlert?.Invoke("内存泄漏");
                    _samples.Clear();
                }
            }
        }
    }
}
```

- [ ] **Step 2: 确认编译通过**

Run: `dotnet build BotMain/BotMain.csproj --no-restore 2>&1 | tail -5`
Expected: Build succeeded

- [ ] **Step 3: 提交**

```bash
git add BotMain/MemoryMonitor.cs
git commit -m "feat: 添加 MemoryMonitor 内存增量监控模块"
```

---

### Task 2: NetworkMonitor — 网络状态监控

**Files:**
- Create: `BotMain/NetworkMonitor.cs`

- [ ] **Step 1: 创建 NetworkMonitor.cs**

```csharp
using System;
using System.Threading;

namespace BotMain
{
    public class NetworkMonitor : IDisposable
    {
        // ── 配置 ──
        public int PollIntervalSeconds { get; set; } = 10;
        public int DisconnectTimeoutSeconds { get; set; } = 60;

        // ── 外部回调 ──
        public Func<string> QueryNetStatus { get; set; }
        public Action<string> OnNetworkAlert { get; set; }
        public Action<string> Log { get; set; }

        // ── 内部状态 ──
        private volatile bool _active;
        private Thread _thread;
        private DateTime? _disconnectedSinceUtc;

        public void Start()
        {
            if (_active) return;
            _active = true;
            _disconnectedSinceUtc = null;

            _thread = new Thread(TickLoop)
            {
                Name = "NetworkMonitor",
                IsBackground = true
            };
            _thread.Start();
            Log?.Invoke("[NetworkMonitor] 已启动");
        }

        public void Stop()
        {
            _active = false;
            Log?.Invoke("[NetworkMonitor] 已停止");
        }

        public void Dispose() => Stop();

        private void TickLoop()
        {
            while (_active)
            {
                try
                {
                    Tick();
                }
                catch (Exception ex)
                {
                    Log?.Invoke($"[NetworkMonitor] tick 异常: {ex.Message}");
                }

                Thread.Sleep(PollIntervalSeconds * 1000);
            }
        }

        private void Tick()
        {
            string response;
            try
            {
                response = QueryNetStatus?.Invoke();
            }
            catch
            {
                // Pipe 故障由 Watchdog 处理，这里忽略
                return;
            }

            if (string.IsNullOrEmpty(response))
                return;

            // 解析响应：NETSTATUS:connected 或 NETSTATUS:disconnected;reason=xxx 或 NETSTATUS:unknown
            var payload = response;
            if (payload.StartsWith("NETSTATUS:", StringComparison.OrdinalIgnoreCase))
                payload = payload.Substring(10);

            if (payload.StartsWith("connected", StringComparison.OrdinalIgnoreCase))
            {
                if (_disconnectedSinceUtc != null)
                {
                    Log?.Invoke("[NetworkMonitor] 网络已恢复");
                    _disconnectedSinceUtc = null;
                }
                return;
            }

            if (payload.StartsWith("unknown", StringComparison.OrdinalIgnoreCase))
                return;

            // disconnected
            if (_disconnectedSinceUtc == null)
            {
                _disconnectedSinceUtc = DateTime.UtcNow;
                Log?.Invoke($"[NetworkMonitor] 检测到网络断连: {payload}");
                return;
            }

            var elapsed = (DateTime.UtcNow - _disconnectedSinceUtc.Value).TotalSeconds;
            if (elapsed >= DisconnectTimeoutSeconds)
            {
                Log?.Invoke($"[NetworkMonitor] 网络断连超时 {elapsed:F0}s (阈值 {DisconnectTimeoutSeconds}s)，触发重启恢复");
                OnNetworkAlert?.Invoke("网络断连");
                _disconnectedSinceUtc = null;
            }
        }
    }
}
```

- [ ] **Step 2: 确认编译通过**

Run: `dotnet build BotMain/BotMain.csproj --no-restore 2>&1 | tail -5`
Expected: Build succeeded

- [ ] **Step 3: 提交**

```bash
git add BotMain/NetworkMonitor.cs
git commit -m "feat: 添加 NetworkMonitor 网络状态监控模块"
```

---

### Task 3: Watchdog 集成 — 添加 TriggerRecovery 公开方法

**Files:**
- Modify: `BotMain/HearthstoneWatchdog.cs:226` (EnterRecovering 附近)

- [ ] **Step 1: 在 HearthstoneWatchdog 中添加 TriggerRecovery 方法**

在 `HearthstoneWatchdog.cs` 的 `EnterRecovering` 方法前（约第 226 行），插入：

```csharp
/// <summary>
/// 供外部监控器（MemoryMonitor / NetworkMonitor）触发恢复流程。
/// </summary>
public void TriggerRecovery(string reason)
{
    if (_state == WatchdogState.Recovering || _state == WatchdogState.Disabled)
        return;
    EnterRecovering(reason);
}
```

- [ ] **Step 2: 确认编译通过**

Run: `dotnet build BotMain/BotMain.csproj --no-restore 2>&1 | tail -5`
Expected: Build succeeded

- [ ] **Step 3: 提交**

```bash
git add BotMain/HearthstoneWatchdog.cs
git commit -m "feat: Watchdog 添加 TriggerRecovery 供外部监控器调用"
```

---

### Task 4: BotService — 暴露 Pipe 命令代理方法

**Files:**
- Modify: `BotMain/BotService.cs` (IsPipeConnected 属性附近，约第 220 行)

- [ ] **Step 1: 在 BotService 中添加代理方法**

在 `BotService.cs` 的 `IsPipeConnected` 属性后面插入两个代理方法：

```csharp
/// <summary>
/// 通过 Pipe 发送 GC 命令让 Payload 执行内存清理。
/// 返回 true 表示命令已成功发送且收到确认。
/// </summary>
public bool RequestPayloadGC()
{
    lock (_sync)
    {
        if (_pipe == null || !_pipe.IsConnected) return false;
        var resp = _pipe.SendAndReceive("GC", 5000);
        return resp != null && resp.StartsWith("GC:", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// 通过 Pipe 查询 Payload 端的游戏网络状态。
/// 返回原始响应字符串，如 "NETSTATUS:connected"。
/// </summary>
public string QueryPayloadNetStatus()
{
    lock (_sync)
    {
        if (_pipe == null || !_pipe.IsConnected) return null;
        return _pipe.SendAndReceive("NETSTATUS", 3000);
    }
}
```

- [ ] **Step 2: 添加内存采集辅助方法**

在同一区域插入：

```csharp
/// <summary>
/// 获取当前炉石进程的工作集内存（字节）。
/// </summary>
public static long? GetHearthstoneMemoryBytes()
{
    try
    {
        var procs = System.Diagnostics.Process.GetProcessesByName("Hearthstone");
        if (procs.Length == 0) return null;
        try { return procs[0].WorkingSet64; }
        finally { foreach (var p in procs) p.Dispose(); }
    }
    catch { return null; }
}
```

- [ ] **Step 3: 确认编译通过**

Run: `dotnet build BotMain/BotMain.csproj --no-restore 2>&1 | tail -5`
Expected: Build succeeded

- [ ] **Step 4: 提交**

```bash
git add BotMain/BotService.cs
git commit -m "feat: BotService 暴露 GC/NETSTATUS Pipe 代理和内存采集方法"
```

---

### Task 5: Payload 端 — GC 命令处理

**Files:**
- Modify: `HearthstonePayload/Entry.cs` (DispatchCommand 方法，约第 835 行 PING 分支后)

- [ ] **Step 1: 在 DispatchCommand 中添加 GC 命令分支**

在 `Entry.cs` 的 `DispatchCommand` 方法中，`PING` 分支之后（第 838 行后），插入：

```csharp
else if (cmd == "GC")
{
    try
    {
        UnityEngine.Resources.UnloadUnusedAssets();
        System.GC.Collect();
        _logSource?.LogInfo("[GC] UnloadUnusedAssets + GC.Collect triggered by BotMain.");
        _pipe.Write("GC:done");
    }
    catch (Exception ex)
    {
        _logSource?.LogWarning("[GC] failed: " + ex.Message);
        _pipe.Write("GC:error:" + ex.Message);
    }
}
```

- [ ] **Step 2: 确认编译通过**

Run: `dotnet build HearthstonePayload/HearthstonePayload.csproj --no-restore 2>&1 | tail -5`
Expected: Build succeeded

- [ ] **Step 3: 提交**

```bash
git add HearthstonePayload/Entry.cs
git commit -m "feat: Payload 端添加 GC 命令处理"
```

---

### Task 6: Payload 端 — NETSTATUS 命令处理

**Files:**
- Modify: `HearthstonePayload/Entry.cs` (DispatchCommand 方法，GC 分支后)

- [ ] **Step 1: 在 DispatchCommand 中添加 NETSTATUS 命令分支**

在刚添加的 GC 分支之后，插入：

```csharp
else if (cmd == "NETSTATUS")
{
    try
    {
        var ctx = ReflectionContext.Instance;
        if (ctx.NetworkType == null)
        {
            _pipe.Write("NETSTATUS:unknown");
            return;
        }

        // Network.Get() → 获取 Network 单例
        var getMethod = ctx.NetworkType.GetMethod("Get",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        if (getMethod == null)
        {
            _pipe.Write("NETSTATUS:unknown");
            return;
        }

        var networkInstance = getMethod.Invoke(null, null);
        if (networkInstance == null)
        {
            _pipe.Write("NETSTATUS:disconnected;reason=no_instance");
            return;
        }

        // 尝试 IsConnectedToAurora() 方法
        bool connected = false;
        string reason = "unknown";

        var auroraMethod = ctx.NetworkType.GetMethod("IsConnectedToAurora",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (auroraMethod != null)
        {
            connected = (bool)auroraMethod.Invoke(networkInstance, null);
            reason = connected ? "aurora" : "server_lost";
        }
        else
        {
            // 回退：尝试其他连接检查方法
            var connMethod = ctx.NetworkType.GetMethod("IsConnected",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (connMethod != null)
            {
                connected = (bool)connMethod.Invoke(null, null);
                reason = connected ? "generic" : "server_lost";
            }
            else
            {
                _pipe.Write("NETSTATUS:unknown");
                return;
            }
        }

        _pipe.Write(connected
            ? "NETSTATUS:connected"
            : "NETSTATUS:disconnected;reason=" + reason);
    }
    catch (Exception ex)
    {
        _logSource?.LogWarning("[NETSTATUS] check failed: " + ex.Message);
        _pipe.Write("NETSTATUS:unknown");
    }
}
```

- [ ] **Step 2: 确认编译通过**

Run: `dotnet build HearthstonePayload/HearthstonePayload.csproj --no-restore 2>&1 | tail -5`
Expected: Build succeeded

- [ ] **Step 3: 提交**

```bash
git add HearthstonePayload/Entry.cs
git commit -m "feat: Payload 端添加 NETSTATUS 网络状态查询命令"
```

---

### Task 7: MainViewModel 接线 — 创建并挂载监控器

**Files:**
- Modify: `BotMain/MainViewModel.cs` (Watchdog 创建区域，约第 864-885 行)

- [ ] **Step 1: 添加字段声明**

在 `MainViewModel.cs` 中 `_watchdog` 字段声明附近（约第 39 行），添加：

```csharp
private MemoryMonitor? _memoryMonitor;
private NetworkMonitor? _networkMonitor;
```

- [ ] **Step 2: 在 Start 分支中创建并启动监控器**

在 `_watchdog.Start();` 之后（约第 885 行），插入：

```csharp
// 启动内存监控
_memoryMonitor?.Stop();
_memoryMonitor = new MemoryMonitor
{
    GetHearthstoneMemoryBytes = () => BotService.GetHearthstoneMemoryBytes(),
    RequestGarbageCollection = () => _bot.RequestPayloadGC(),
    OnMemoryAlert = reason => _watchdog?.TriggerRecovery(reason),
    Log = EnqueueLog
};
_memoryMonitor.Start();

// 启动网络监控
_networkMonitor?.Stop();
_networkMonitor = new NetworkMonitor
{
    QueryNetStatus = () => _bot.QueryPayloadNetStatus(),
    OnNetworkAlert = reason => _watchdog?.TriggerRecovery(reason),
    Log = EnqueueLog
};
_networkMonitor.Start();
```

- [ ] **Step 3: 在 Stop 分支中停止监控器**

在 `_watchdog?.Stop();`（约第 889 行）之后，插入：

```csharp
_memoryMonitor?.Stop();
_networkMonitor?.Stop();
```

- [ ] **Step 4: 在 Dispose 中清理监控器**

在 `_watchdog?.Dispose();`（约第 273 行）附近，插入：

```csharp
_memoryMonitor?.Dispose();
_networkMonitor?.Dispose();
```

- [ ] **Step 5: 确认编译通过**

Run: `dotnet build BotMain/BotMain.csproj --no-restore 2>&1 | tail -5`
Expected: Build succeeded

- [ ] **Step 6: 提交**

```bash
git add BotMain/MainViewModel.cs
git commit -m "feat: MainViewModel 接线 MemoryMonitor 和 NetworkMonitor"
```

---

### Task 8: 全量编译验证

- [ ] **Step 1: 编译整个解决方案**

Run: `dotnet build Hearthbot.sln --no-restore 2>&1 | tail -10`
Expected: Build succeeded, 0 errors

- [ ] **Step 2: 如有编译错误，修复后重新编译**

- [ ] **Step 3: 最终提交（如有修复）**

```bash
git add -A
git commit -m "fix: 修复编译问题"
```
