# HearthstoneWatchdog 全局看门狗设计

## 背景

当前 HearthBot 的故障恢复逻辑分散在 BotService 各业务分支中（匹配超时、Pipe 断连、Seed null 等），缺少一个独立于业务逻辑的全局监控。参考 SmartBot 的 `ReloggerImproved` 架构，新增一个在 MainViewModel 层运行的独立看门狗，作为所有故障场景的兜底。

## 目标

- 炉石闪退后自动重启
- 进程无响应 / WerFault 崩溃对话框自动处理
- 启动超时、Payload 连接超时自动杀进程重试
- Bot 运行中长时间无有效操作自动重启
- 即使 BotService 主循环异常退出，看门狗仍能兜底

## 设计

### 状态机

```
NotRunning → Launching → WaitingPayload → Connected → Running
     ↑           |              |                        |
     |      LaunchTimeout  PayloadTimeout           GameTimeout
     |           |              |              ProcessCrash / NotResponding
     |           v              v                        v
     +←←←←← Recovering ←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←+
```

| 状态 | 含义 | 进入条件 |
|------|------|----------|
| `NotRunning` | 炉石未运行 | 初始 / 恢复完成后炉石未启动 |
| `Launching` | 正在通过战网启动炉石 | 看门狗触发启动 |
| `WaitingPayload` | 炉石进程已存在，等待 Payload Pipe 连接 | 检测到 Hearthstone 进程 |
| `Connected` | Payload 已连接，Bot 未运行 | BotService 报告 Pipe 已连接 |
| `Running` | Bot 正常运行中 | BotService 报告正在运行 |
| `Recovering` | 正在执行恢复流程（杀进程 → 重启） | 任何超时/崩溃触发 |

### 检测层

按优先级排列，每轮 tick 依次检查：

| # | 检测项 | 方法 | 阈值 | 触发状态 |
|---|--------|------|------|----------|
| 1 | 崩溃对话框 | `Process.GetProcessesByName("WerFault")` | 立即 | Recovering |
| 2 | 进程消失 | `Process.GetProcessesByName("Hearthstone").Length == 0` | 立即 | Recovering |
| 3 | 进程无响应 | `process.Responding == false` 持续时间 | 30s | Recovering |
| 4 | 启动超时 | Launching 状态持续时间 | 120s | Recovering |
| 5 | Payload 连接超时 | WaitingPayload 状态持续时间 | 120s | Recovering |
| 6 | 游戏内超时 | Bot 运行中，BotService 上次有效操作距今 | 可配置，默认 300s | Recovering |

### 恢复流程（Recovering 状态）

```
1. 记录日志：故障类型 + 时间
2. 通知 BotService 停止（如果还活着）
3. 杀 WerFault 进程（如果存在）
4. 杀 Hearthstone 进程，等待退出（最多 15s）
5. 等待 5s 冷却
6. 递增连续失败计数器
7. 如果连续失败 >= 3 次：
   a. 杀 Battle.net 进程
   b. 等待 10s
   c. 重新启动 Battle.net
   d. 等待 15s
8. 通过 BattleNetWindowManager.LaunchHearthstoneViaClick() 启动炉石
9. 状态 → Launching
10. 如果启动成功，重置连续失败计数器
```

### 新建文件：`BotMain/HearthstoneWatchdog.cs`

```csharp
public class HearthstoneWatchdog : IDisposable
{
    public enum WatchdogState
    {
        Disabled,       // 看门狗未激活
        NotRunning,     // 炉石未运行
        Launching,      // 正在启动炉石
        WaitingPayload, // 等待 Payload 连接
        Connected,      // 已连接，Bot 未运行
        Running,        // Bot 运行中
        Recovering      // 正在恢复
    }

    // --- 配置 ---
    public int TickIntervalMs { get; set; } = 3000;
    public int LaunchTimeoutSeconds { get; set; } = 120;
    public int PayloadTimeoutSeconds { get; set; } = 120;
    public int NotRespondingTimeoutSeconds { get; set; } = 30;
    public int GameTimeoutSeconds { get; set; } = 300;
    public int MaxConsecutiveFailures { get; set; } = 5;

    // --- 外部注入的回调 ---
    // 查询 BotService 状态
    public Func<bool> IsBotRunning { get; set; }
    public Func<bool> IsPipeConnected { get; set; }
    public Func<DateTime?> GetLastEffectiveAction { get; set; }

    // 控制 BotService
    public Action RequestBotStop { get; set; }
    public Action RequestBotRestart { get; set; }

    // 启动炉石
    public Func<Task<bool>> LaunchHearthstone { get; set; }

    // 日志
    public Action<string> Log { get; set; }

    // 状态变更通知（供 UI 绑定）
    public event Action<WatchdogState> StateChanged;

    // --- 核心方法 ---
    public void Start();   // 启动监控线程
    public void Stop();    // 停止监控
    public void Dispose();
}
```

### tick 伪代码

```
每 3 秒执行一次：

if (状态 == Disabled) return;

// 1. 崩溃对话框检测
if (DetectWerFault())
    → KillWerFault()
    → 进入 Recovering

// 2. 进程存活检测
hearthstoneAlive = Process.GetProcessesByName("Hearthstone").Length > 0

if (状态 in [WaitingPayload, Connected, Running] && !hearthstoneAlive)
    → 进入 Recovering

// 3. 进程响应检测
if (hearthstoneAlive && !process.Responding)
    if (持续 > NotRespondingTimeoutSeconds)
        → 进入 Recovering
else
    重置无响应计时器

// 4. 状态超时检测
switch (状态):
    case NotRunning:
        if (IsBotRunning())
            → LaunchHearthstone()
            → 状态 = Launching
    
    case Launching:
        if (hearthstoneAlive)
            → 状态 = WaitingPayload
        elif (进入此状态 > LaunchTimeoutSeconds)
            → 进入 Recovering
    
    case WaitingPayload:
        if (IsPipeConnected())
            → 状态 = Connected
        elif (进入此状态 > PayloadTimeoutSeconds)
            → 进入 Recovering
    
    case Connected:
        if (IsBotRunning())
            → 状态 = Running
    
    case Running:
        if (!IsBotRunning())
            → 状态 = Connected
        elif (GetLastEffectiveAction() 距今 > GameTimeoutSeconds)
            → 进入 Recovering
    
    case Recovering:
        执行恢复流程（见上方）
        → 状态 = Launching 或 NotRunning
```

### 修改 `BotMain/BotService.cs`

新增一个时间戳字段，每次执行有效操作时更新：

```csharp
// 新增字段
private DateTime _lastEffectiveActionUtc = DateTime.UtcNow;

// 暴露给 Watchdog 查询
public DateTime LastEffectiveActionUtc => _lastEffectiveActionUtc;

// 在以下位置更新：
// - 成功出牌/攻击后
// - 成功获取 Seed 后
// - 成功进入匹配后
// - 回合开始/结束时
// - 任何场景切换成功后
private void TouchEffectiveAction()
{
    _lastEffectiveActionUtc = DateTime.UtcNow;
}
```

### 修改 `BotMain/MainViewModel.cs`

```csharp
// 新增字段
private HearthstoneWatchdog _watchdog;

// 在 Bot 启动时创建并启动 Watchdog
private void StartBot()
{
    // ... 现有启动逻辑 ...

    _watchdog = new HearthstoneWatchdog
    {
        IsBotRunning = () => _botService?.IsRunning ?? false,
        IsPipeConnected = () => _botService?.IsPipeConnected ?? false,
        GetLastEffectiveAction = () => _botService?.LastEffectiveActionUtc,
        RequestBotStop = () => _botService?.Stop(),
        RequestBotRestart = () => _botService?.RequestRestart(),
        LaunchHearthstone = () => BattleNetWindowManager.LaunchHearthstoneViaClick(...),
        Log = msg => AddLog(msg)
    };
    _watchdog.StateChanged += state => StatusText = $"Watchdog: {state}";
    _watchdog.Start();
}

// 在 Bot 停止时停止 Watchdog
private void StopBot()
{
    _watchdog?.Stop();
    // ... 现有停止逻辑 ...
}
```

### 与现有恢复机制的关系

| 机制 | 层级 | 保留 | 说明 |
|------|------|------|------|
| BotService.匹配超时 60s | 内层 | 保留 | 快速恢复，不杀进程 |
| BotService.TryReconnectLoop | 内层 | 保留 | Pipe 断连后快速重连 |
| BotService.RestartHearthstone | 内层 | 保留 | BotService 主动重启 |
| BotService.RunPostGameDismissLoop | 内层 | 保留 | 结算界面点击恢复 |
| **HearthstoneWatchdog** | **外层** | **新增** | **兜底所有内层无法处理的情况** |

原则：内层先尝试恢复，Watchdog 只在内层失败或 BotService 自身异常时介入。Watchdog 通过 `LastEffectiveActionUtc` 判断内层是否已经卡死。

### 超时配置默认值

| 参数 | 默认值 | 说明 |
|------|--------|------|
| TickIntervalMs | 3000 | 轮询间隔 3 秒 |
| LaunchTimeoutSeconds | 120 | 启动炉石最多等 2 分钟 |
| PayloadTimeoutSeconds | 120 | 等待 Payload 连接最多 2 分钟 |
| NotRespondingTimeoutSeconds | 30 | 进程无响应持续 30 秒判定卡死 |
| GameTimeoutSeconds | 300 | 游戏内 5 分钟无有效操作判定卡死 |
| MaxConsecutiveFailures | 5 | 连续失败 5 次后停止看门狗，需人工干预 |
