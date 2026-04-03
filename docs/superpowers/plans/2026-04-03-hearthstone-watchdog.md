# HearthstoneWatchdog 全局看门狗实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 新增一个在 MainViewModel 层运行的独立看门狗，作为所有故障场景的全局兜底；同时将炉石启动方式从模拟点击改为 SmartBot 的命令行方式。

**Architecture:** HearthstoneWatchdog 独立线程每 3s 轮询，维护状态机（NotRunning → Launching → WaitingPayload → Connected → Running → Recovering），通过回调委托查询/控制 BotService，完全解耦。BattleNetWindowManager 新增 `LaunchHearthstoneCmd()` 方法使用 `--exec="launch WTCG"` 命令行启动。

**Tech Stack:** C# / .NET / WPF / System.Diagnostics.Process

---

## 文件结构

| 操作 | 文件 | 职责 |
|------|------|------|
| 新建 | `BotMain/HearthstoneWatchdog.cs` | 状态机 + 检测逻辑 + 恢复流程 |
| 修改 | `BotMain/BattleNetWindowManager.cs` | 新增 `LaunchHearthstoneCmd()` 命令行启动方法 |
| 修改 | `BotMain/BotService.cs` | 暴露 `LastEffectiveActionUtc` + `TouchEffectiveAction()` |
| 修改 | `BotMain/MainViewModel.cs` | 创建/启停 Watchdog，接线回调 |

---

### Task 1: BattleNetWindowManager 新增命令行启动方法

**Files:**
- Modify: `BotMain/BattleNetWindowManager.cs`

- [ ] **Step 1: 新增 `LaunchHearthstoneCmd` 方法**

在 `BattleNetWindowManager` 类中，在 `#region SendMessage 后台点击启动炉石` 之前，新增命令行启动方法：

```csharp
/// <summary>
/// 通过 Battle.net 命令行启动炉石（SmartBot 方式）。
/// 使用 --exec="launch WTCG" 参数直接启动，无需窗口交互。
/// </summary>
public static async Task<BattleNetLaunchResult> LaunchHearthstoneCmd(
    Action<string> log, CancellationToken ct, int timeoutSeconds = 120)
{
    try
    {
        // 杀掉旧炉石进程
        var existing = Process.GetProcessesByName("Hearthstone");
        if (existing.Length > 0)
        {
            log?.Invoke("[Watchdog] 等待旧炉石进程退出...");
            foreach (var proc in existing)
            {
                try { if (!proc.HasExited) { proc.Kill(); proc.WaitForExit(15000); } }
                catch { }
                finally { proc.Dispose(); }
            }

            var exitDeadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < exitDeadline && !ct.IsCancellationRequested)
            {
                if (Process.GetProcessesByName("Hearthstone").Length == 0) break;
                await Task.Delay(1000, ct);
            }

            if (Process.GetProcessesByName("Hearthstone").Length > 0)
                return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.LaunchTimedOut, "旧炉石进程未能退出");

            log?.Invoke("[Watchdog] 旧炉石进程已退出");
        }

        // 查找 Battle.net 可执行文件路径
        string bnetExePath = null;

        // 优先从运行中的进程获取
        foreach (var name in new[] { "Battle.net", "Battle.net.beta" })
        {
            var procs = Process.GetProcessesByName(name);
            foreach (var proc in procs)
            {
                try
                {
                    var path = proc.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                        bnetExePath = path;
                }
                catch { }
                finally { proc.Dispose(); }
            }
            if (bnetExePath != null) break;
        }

        // 进程中没找到，用 FindBattleNetExePath 查找
        if (bnetExePath == null)
            bnetExePath = FindBattleNetExePath();

        if (string.IsNullOrWhiteSpace(bnetExePath))
            return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.WindowNotFound, "未找到 Battle.net 路径");

        // 确保 Battle.net 正在运行
        bool bnetRunning = Process.GetProcessesByName("Battle.net").Length > 0
                        || Process.GetProcessesByName("Battle.net.beta").Length > 0;
        if (!bnetRunning)
        {
            log?.Invoke($"[Watchdog] 启动 Battle.net: {bnetExePath}");
            Process.Start(new ProcessStartInfo { FileName = bnetExePath, UseShellExecute = true });

            var bnetDeadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < bnetDeadline && !ct.IsCancellationRequested)
            {
                await Task.Delay(2000, ct);
                if (Process.GetProcessesByName("Battle.net").Length > 0
                    || Process.GetProcessesByName("Battle.net.beta").Length > 0)
                    break;
            }

            bool bnetUp = Process.GetProcessesByName("Battle.net").Length > 0
                       || Process.GetProcessesByName("Battle.net.beta").Length > 0;
            if (!bnetUp)
                return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.WindowNotFound, "Battle.net 启动超时");

            // 等战网完全加载
            await Task.Delay(5000, ct);

            // 重新获取路径（新进程可能路径不同）
            foreach (var name in new[] { "Battle.net", "Battle.net.beta" })
            {
                var procs = Process.GetProcessesByName(name);
                foreach (var proc in procs)
                {
                    try
                    {
                        var path = proc.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                            bnetExePath = path;
                    }
                    catch { }
                    finally { proc.Dispose(); }
                }
                if (bnetExePath != null) break;
            }
        }

        // 通过命令行启动炉石
        log?.Invoke($"[Watchdog] 执行: \"{bnetExePath}\" --exec=\"launch {HearthstoneProductCode}\"");
        Process.Start(new ProcessStartInfo
        {
            FileName = bnetExePath,
            Arguments = $"--exec=\"launch {HearthstoneProductCode}\"",
            UseShellExecute = true
        });

        // 等待炉石进程出现
        log?.Invoke("[Watchdog] 等待炉石进程启动...");
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            await Task.Delay(2000, ct);

            // 清理崩溃弹窗
            try
            {
                foreach (var wf in Process.GetProcessesByName("WerFault"))
                {
                    try { wf.Kill(); } catch { }
                    finally { wf.Dispose(); }
                }
            }
            catch { }

            var hsProcs = Process.GetProcessesByName("Hearthstone");
            if (hsProcs.Length > 0)
            {
                var hsPid = hsProcs[0].Id;
                log?.Invoke($"[Watchdog] 炉石进程已启动 PID={hsPid}");
                return BattleNetLaunchResult.Succeeded(0, hsPid, $"炉石已启动 PID={hsPid}");
            }
        }

        if (ct.IsCancellationRequested)
            return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.Cancelled, "启动取消");

        return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.LaunchTimedOut, $"启动炉石超时 ({timeoutSeconds}s)");
    }
    catch (OperationCanceledException)
    {
        return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.Cancelled, "启动取消");
    }
    catch (Exception ex)
    {
        return BattleNetLaunchResult.Failed(BattleNetRestartFailureKind.LaunchTimedOut, $"启动失败: {ex.Message}");
    }
}

/// <summary>
/// 杀掉所有 Battle.net 进程
/// </summary>
public static void KillBattleNet(Action<string> log)
{
    foreach (var name in new[] { "Battle.net", "Battle.net.beta" })
    {
        var procs = Process.GetProcessesByName(name);
        foreach (var proc in procs)
        {
            try
            {
                log?.Invoke($"[Watchdog] 关闭 {name} PID={proc.Id}");
                proc.Kill();
                proc.WaitForExit(10000);
            }
            catch { }
            finally { proc.Dispose(); }
        }
    }
}

/// <summary>
/// 杀掉所有 WerFault 崩溃弹窗进程
/// </summary>
public static void KillWerFault(Action<string> log)
{
    var procs = Process.GetProcessesByName("WerFault");
    foreach (var proc in procs)
    {
        try
        {
            log?.Invoke("[Watchdog] 关闭 WerFault 崩溃弹窗");
            proc.Kill();
        }
        catch { }
        finally { proc.Dispose(); }
    }
}

/// <summary>
/// 检查 Hearthstone 进程是否正在响应
/// </summary>
public static bool IsHearthstoneResponding()
{
    var procs = Process.GetProcessesByName("Hearthstone");
    if (procs.Length == 0) return false;
    try { return procs[0].Responding; }
    catch { return false; }
    finally { foreach (var p in procs) p.Dispose(); }
}
```

- [ ] **Step 2: 将 `LaunchHearthstoneViaProtocol` 改为调用新方法**

将 `LaunchHearthstoneViaProtocol` 方法体改为调用 `LaunchHearthstoneCmd`：

```csharp
public static async Task<BattleNetLaunchResult> LaunchHearthstoneViaProtocol(
    Action<string> log, CancellationToken ct, int timeoutSeconds = 120)
{
    return await LaunchHearthstoneCmd(log, ct, timeoutSeconds);
}
```

这样 BotService 中现有的 `LaunchFromBoundBattleNet()` 自动使用新的命令行方式，无需额外改动。

- [ ] **Step 3: 提交**

```bash
git add BotMain/BattleNetWindowManager.cs
git commit -m "feat: BattleNetWindowManager 新增命令行启动方式 (--exec=\"launch WTCG\")"
```

---

### Task 2: BotService 暴露 LastEffectiveActionUtc

**Files:**
- Modify: `BotMain/BotService.cs`

- [ ] **Step 1: 新增字段和属性**

在 `BotService` 类中（`_lastActionCommandUtc` 字段附近，约 148 行），新增：

```csharp
private DateTime _lastEffectiveActionUtc = DateTime.UtcNow;

/// <summary>
/// 上次有效操作的时间戳，供 Watchdog 查询判断游戏是否卡死。
/// </summary>
public DateTime LastEffectiveActionUtc => _lastEffectiveActionUtc;

/// <summary>
/// Pipe 是否已连接，供 Watchdog 查询。
/// </summary>
public bool IsPipeConnected
{
    get
    {
        lock (_sync) { return _pipe != null && _pipe.IsConnected; }
    }
}
```

- [ ] **Step 2: 新增 `TouchEffectiveAction()` 方法**

在 `Stop()` 方法附近新增：

```csharp
/// <summary>
/// 更新有效操作时间戳。在关键操作成功后调用，让 Watchdog 知道 Bot 仍在正常工作。
/// </summary>
private void TouchEffectiveAction()
{
    _lastEffectiveActionUtc = DateTime.UtcNow;
}
```

- [ ] **Step 3: 在关键操作点调用 `TouchEffectiveAction()`**

在 BotService.cs 中搜索以下位置，在成功执行后插入 `TouchEffectiveAction()` 调用：

1. **`Start()` 方法**（约 648 行）— 在 `_running = true;` 之后：
```csharp
_running = true;
TouchEffectiveAction();
```

2. **搜索 `_lastActionCommandUtc = DateTime.UtcNow`**（约 148 行附近，以及所有赋值点）— 在每个赋值旁边加上 `TouchEffectiveAction()`。`_lastActionCommandUtc` 已经在记录操作时间，只需在同样的地方更新看门狗时间戳。

3. **搜索 `StatusChanged("Running")`** — 在状态变为 Running 时更新。

4. **搜索 `TryReconnectLoop` 成功返回处** — 重连成功后重置计时器。

用 Grep 搜索 `_lastActionCommandUtc` 的所有赋值点来定位。

- [ ] **Step 4: 提交**

```bash
git add BotMain/BotService.cs
git commit -m "feat: BotService 暴露 LastEffectiveActionUtc 供 Watchdog 查询"
```

---

### Task 3: 新建 HearthstoneWatchdog 核心类

**Files:**
- Create: `BotMain/HearthstoneWatchdog.cs`

- [ ] **Step 1: 创建完整的 HearthstoneWatchdog 类**

```csharp
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace BotMain
{
    public class HearthstoneWatchdog : IDisposable
    {
        public enum WatchdogState
        {
            Disabled,
            NotRunning,
            Launching,
            WaitingPayload,
            Connected,
            Running,
            Recovering
        }

        // ── 配置 ──
        public int TickIntervalMs { get; set; } = 3000;
        public int LaunchTimeoutSeconds { get; set; } = 120;
        public int PayloadTimeoutSeconds { get; set; } = 120;
        public int NotRespondingTimeoutSeconds { get; set; } = 30;
        public int GameTimeoutSeconds { get; set; } = 300;
        public int MaxConsecutiveFailures { get; set; } = 5;
        public int RecoveryCooldownSeconds { get; set; } = 5;
        public int BattleNetRestartThreshold { get; set; } = 3;

        // ── 外部回调 ──
        public Func<bool> IsBotRunning { get; set; }
        public Func<bool> IsPipeConnected { get; set; }
        public Func<DateTime?> GetLastEffectiveAction { get; set; }
        public Action RequestBotStop { get; set; }
        public Action<string> Log { get; set; }

        // ── 事件 ──
        public event Action<WatchdogState> StateChanged;

        // ── 内部状态 ──
        private volatile bool _active;
        private Thread _thread;
        private WatchdogState _state = WatchdogState.Disabled;
        private DateTime _stateEnteredUtc;
        private DateTime? _notRespondingSinceUtc;
        private int _consecutiveFailures;

        public WatchdogState CurrentState => _state;

        public void Start()
        {
            if (_active) return;
            _active = true;
            _consecutiveFailures = 0;
            TransitionTo(WatchdogState.NotRunning);

            _thread = new Thread(TickLoop)
            {
                Name = "HearthstoneWatchdog",
                IsBackground = true
            };
            _thread.Start();
            Log?.Invoke("[Watchdog] 看门狗已启动");
        }

        public void Stop()
        {
            _active = false;
            TransitionTo(WatchdogState.Disabled);
            Log?.Invoke("[Watchdog] 看门狗已停止");
        }

        public void Dispose()
        {
            Stop();
        }

        /// <summary>
        /// 外部通知：炉石已由其他逻辑启动（如 BotService.TryReconnectLoop），
        /// Watchdog 应同步状态而非重复启动。
        /// </summary>
        public void NotifyHearthstoneLaunched()
        {
            if (_state == WatchdogState.NotRunning || _state == WatchdogState.Launching)
                TransitionTo(WatchdogState.WaitingPayload);
        }

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
                    Log?.Invoke($"[Watchdog] tick 异常: {ex.Message}");
                }

                Thread.Sleep(TickIntervalMs);
            }
        }

        private void Tick()
        {
            if (_state == WatchdogState.Disabled) return;

            bool hearthstoneAlive = IsHearthstoneAlive();

            // ── 1. WerFault 崩溃对话框检测 ──
            if (DetectWerFault())
            {
                Log?.Invoke("[Watchdog] 检测到 WerFault 崩溃弹窗");
                BattleNetWindowManager.KillWerFault(Log);
                if (_state != WatchdogState.NotRunning && _state != WatchdogState.Recovering)
                {
                    EnterRecovering("WerFault 崩溃弹窗");
                    return;
                }
            }

            // ── 2. 进程消失检测 ──
            if (!hearthstoneAlive && _state is WatchdogState.WaitingPayload
                                          or WatchdogState.Connected
                                          or WatchdogState.Running)
            {
                Log?.Invoke("[Watchdog] 炉石进程已消失");
                EnterRecovering("进程消失");
                return;
            }

            // ── 3. 进程无响应检测 ──
            if (hearthstoneAlive && !BattleNetWindowManager.IsHearthstoneResponding())
            {
                if (_notRespondingSinceUtc == null)
                {
                    _notRespondingSinceUtc = DateTime.UtcNow;
                }
                else if ((DateTime.UtcNow - _notRespondingSinceUtc.Value).TotalSeconds >= NotRespondingTimeoutSeconds)
                {
                    Log?.Invoke($"[Watchdog] 炉石持续无响应 {NotRespondingTimeoutSeconds}s");
                    EnterRecovering("进程无响应");
                    return;
                }
            }
            else
            {
                _notRespondingSinceUtc = null;
            }

            // ── 4. 状态机流转 ──
            double secondsInState = (DateTime.UtcNow - _stateEnteredUtc).TotalSeconds;

            switch (_state)
            {
                case WatchdogState.NotRunning:
                    if (hearthstoneAlive)
                    {
                        TransitionTo(WatchdogState.WaitingPayload);
                    }
                    break;

                case WatchdogState.Launching:
                    if (hearthstoneAlive)
                    {
                        TransitionTo(WatchdogState.WaitingPayload);
                    }
                    else if (secondsInState >= LaunchTimeoutSeconds)
                    {
                        Log?.Invoke($"[Watchdog] 启动超时 ({LaunchTimeoutSeconds}s)");
                        EnterRecovering("启动超时");
                    }
                    break;

                case WatchdogState.WaitingPayload:
                    if (IsPipeConnected?.Invoke() == true)
                    {
                        TransitionTo(WatchdogState.Connected);
                    }
                    else if (secondsInState >= PayloadTimeoutSeconds)
                    {
                        Log?.Invoke($"[Watchdog] Payload 连接超时 ({PayloadTimeoutSeconds}s)");
                        EnterRecovering("Payload 连接超时");
                    }
                    break;

                case WatchdogState.Connected:
                    if (IsBotRunning?.Invoke() == true)
                    {
                        _consecutiveFailures = 0;
                        TransitionTo(WatchdogState.Running);
                    }
                    break;

                case WatchdogState.Running:
                    if (IsBotRunning?.Invoke() != true)
                    {
                        TransitionTo(WatchdogState.Connected);
                    }
                    else
                    {
                        var lastAction = GetLastEffectiveAction?.Invoke();
                        if (lastAction.HasValue &&
                            (DateTime.UtcNow - lastAction.Value).TotalSeconds >= GameTimeoutSeconds)
                        {
                            Log?.Invoke($"[Watchdog] 游戏内超时: {GameTimeoutSeconds}s 无有效操作");
                            EnterRecovering("游戏内超时");
                        }
                    }
                    break;

                case WatchdogState.Recovering:
                    // Recovering 在 EnterRecovering 中同步执行，不会停留在此状态
                    break;
            }
        }

        private void EnterRecovering(string reason)
        {
            TransitionTo(WatchdogState.Recovering);
            _consecutiveFailures++;
            Log?.Invoke($"[Watchdog] 开始恢复流程 (原因: {reason}, 连续失败: {_consecutiveFailures}/{MaxConsecutiveFailures})");

            if (_consecutiveFailures > MaxConsecutiveFailures)
            {
                Log?.Invoke($"[Watchdog] 连续失败 {_consecutiveFailures} 次，超过上限 {MaxConsecutiveFailures}，停止看门狗");
                Stop();
                return;
            }

            // 1. 通知 BotService 停止
            try { RequestBotStop?.Invoke(); } catch { }

            // 2. 杀 WerFault
            BattleNetWindowManager.KillWerFault(Log);

            // 3. 杀 Hearthstone
            BattleNetWindowManager.KillHearthstone(Log);
            WaitForHearthstoneExit(15);

            // 4. 冷却
            Thread.Sleep(RecoveryCooldownSeconds * 1000);

            // 5. 连续失败多次则重启战网
            if (_consecutiveFailures >= BattleNetRestartThreshold)
            {
                Log?.Invoke("[Watchdog] 连续失败过多，重启 Battle.net");
                BattleNetWindowManager.KillBattleNet(Log);
                Thread.Sleep(10000);
            }

            // 6. 启动炉石
            Log?.Invoke("[Watchdog] 通过命令行启动炉石...");
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(LaunchTimeoutSeconds));
            try
            {
                var result = BattleNetWindowManager.LaunchHearthstoneCmd(Log, cts.Token, LaunchTimeoutSeconds)
                    .GetAwaiter().GetResult();

                if (result.Success)
                {
                    Log?.Invoke("[Watchdog] 炉石启动成功");
                    TransitionTo(WatchdogState.WaitingPayload);
                }
                else
                {
                    Log?.Invoke($"[Watchdog] 炉石启动失败: {result.Message}");
                    TransitionTo(WatchdogState.NotRunning);
                }
            }
            catch (Exception ex)
            {
                Log?.Invoke($"[Watchdog] 启动异常: {ex.Message}");
                TransitionTo(WatchdogState.NotRunning);
            }
            finally
            {
                cts.Dispose();
            }
        }

        private void TransitionTo(WatchdogState newState)
        {
            if (_state == newState) return;
            _state = newState;
            _stateEnteredUtc = DateTime.UtcNow;
            try { StateChanged?.Invoke(newState); } catch { }
        }

        private static bool IsHearthstoneAlive()
        {
            return Process.GetProcessesByName("Hearthstone").Length > 0;
        }

        private static bool DetectWerFault()
        {
            return Process.GetProcessesByName("WerFault").Length > 0;
        }

        private static void WaitForHearthstoneExit(int maxSeconds)
        {
            var deadline = DateTime.UtcNow.AddSeconds(maxSeconds);
            while (DateTime.UtcNow < deadline)
            {
                if (Process.GetProcessesByName("Hearthstone").Length == 0) return;
                Thread.Sleep(1000);
            }
        }
    }
}
```

- [ ] **Step 2: 提交**

```bash
git add BotMain/HearthstoneWatchdog.cs
git commit -m "feat: 新增 HearthstoneWatchdog 全局看门狗状态机"
```

---

### Task 4: BotService 添加 TouchEffectiveAction 调用点

**Files:**
- Modify: `BotMain/BotService.cs`

- [ ] **Step 1: 找到所有 `_lastActionCommandUtc` 赋值点**

运行搜索：
```
Grep: _lastActionCommandUtc = DateTime  in BotMain/BotService.cs
```

在每个 `_lastActionCommandUtc = DateTime.UtcNow;` 旁边加上 `TouchEffectiveAction();`。

- [ ] **Step 2: 在 TryReconnectLoop 成功处调用**

搜索 `[Restart] 重连成功`，在该 Log 之后加入：
```csharp
TouchEffectiveAction();
```

- [ ] **Step 3: 在 StatusChanged("Running") 调用处加入**

搜索 `StatusChanged("Running")`，在其之后加入：
```csharp
TouchEffectiveAction();
```

- [ ] **Step 4: 提交**

```bash
git add BotMain/BotService.cs
git commit -m "feat: BotService 在关键操作点调用 TouchEffectiveAction"
```

---

### Task 5: MainViewModel 集成 Watchdog

**Files:**
- Modify: `BotMain/MainViewModel.cs`

- [ ] **Step 1: 添加 Watchdog 字段**

在 `MainViewModel` 类字段区域（`_cloudAgent` 附近，约 34 行），新增：

```csharp
private HearthstoneWatchdog _watchdog;
```

- [ ] **Step 2: 在 Bot 启动时创建并启动 Watchdog**

在 `OnMainButton()` 方法的启动分支中（`_bot.Start();` 之后，约 814 行），添加：

```csharp
_bot.Start();

// 启动看门狗
_watchdog?.Stop();
_watchdog = new HearthstoneWatchdog
{
    IsBotRunning = () => _bot.State == BotState.Running || _bot.State == BotState.Finishing,
    IsPipeConnected = () => _bot.IsPipeConnected,
    GetLastEffectiveAction = () => _bot.LastEffectiveActionUtc,
    RequestBotStop = () => _bot.Stop(),
    Log = EnqueueLog,
    GameTimeoutSeconds = _matchmakingTimeoutSeconds * 5 // 默认 300s
};
_watchdog.StateChanged += state =>
    _dispatcher.BeginInvoke(() =>
    {
        AppendLocalLog($"[Watchdog] 状态: {state}");
    });
_watchdog.Start();
```

- [ ] **Step 3: 在 Bot 停止时停止 Watchdog**

在 `OnMainButton()` 的停止分支中（`_bot.Stop();` 之前，约 819 行），添加：

```csharp
_watchdog?.Stop();
_timer.Stop();
_bot.Stop();
```

- [ ] **Step 4: 在 Dispose 中清理 Watchdog**

在 `Dispose()` 方法中添加：

```csharp
public void Dispose()
{
    _watchdog?.Dispose();
    _autoUpdater?.Dispose();
    _cloudAgent?.Dispose();
}
```

- [ ] **Step 5: 提交**

```bash
git add BotMain/MainViewModel.cs
git commit -m "feat: MainViewModel 集成 HearthstoneWatchdog"
```

---

### Task 6: 移除 BattleNetWindowManager 中旧的点击启动代码

**Files:**
- Modify: `BotMain/BattleNetWindowManager.cs`

- [ ] **Step 1: 删除旧的 SendMessage 点击相关代码**

删除 `#region SendMessage 后台点击启动炉石` 到 `#endregion` 之间的整个区域（约 153-343 行），包括：
- `SendMessage`, `GetWindowRect` 的 P/Invoke 声明
- `RECT` 结构体
- `WM_LBUTTONDOWN`, `WM_LBUTTONUP` 常量
- `BnetRefWidth/Height`, `HsTabX/Y`, `PlayBtnX/Y` 坐标常量
- `MakeLParam`, `SendClick`, `ScaleCoord`, `FindBattleNetMainWindow` 方法
- `LaunchHearthstoneViaClick` 方法

保留 `LaunchHearthstoneViaProtocol`（已改为调用 `LaunchHearthstoneCmd`）。

- [ ] **Step 2: 提交**

```bash
git add BotMain/BattleNetWindowManager.cs
git commit -m "refactor: 移除旧的模拟点击启动代码，统一使用命令行方式"
```

---

### Task 7: 验证与调试

- [ ] **Step 1: 编译验证**

```bash
cd BotMain && dotnet build
```

确保无编译错误。

- [ ] **Step 2: 检查所有调用链**

确认以下调用链完整：
1. `MainViewModel.OnMainButton()` → `_watchdog.Start()`
2. `HearthstoneWatchdog.Tick()` → 检测异常 → `EnterRecovering()`
3. `EnterRecovering()` → `BattleNetWindowManager.KillHearthstone()` → `LaunchHearthstoneCmd()`
4. `BotService.TouchEffectiveAction()` → 更新 `_lastEffectiveActionUtc`
5. Watchdog 读取 `_bot.LastEffectiveActionUtc` 判断超时

- [ ] **Step 3: 提交最终状态**

```bash
git add -A
git commit -m "feat: HearthstoneWatchdog 全局看门狗完成"
```
