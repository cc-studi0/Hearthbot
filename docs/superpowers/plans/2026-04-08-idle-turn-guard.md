# Idle Turn Guard 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 连续三个我方空回合时紧急关闭游戏、停止脚本、推送微信通知。

**Architecture:** 在 BotService 主循环中内联计数空回合，达到阈值时杀进程+停止+触发事件；MainViewModel 监听事件发推送。

**Tech Stack:** C# / .NET 8.0 / WPF

---

### Task 1: BotService 新增字段和事件

**Files:**
- Modify: `BotMain/BotService.cs:100-102` (字段区域)
- Modify: `BotMain/BotService.cs:211` (事件区域)

- [ ] **Step 1: 新增空回合追踪字段**

在 `BotService.cs` 约第102行（`_restartPending` 之后）添加：

```csharp
        // 空回合紧急刹车
        private int _consecutiveIdleTurns;
        private bool _turnHadEffectiveAction;
```

- [ ] **Step 2: 新增事件声明**

在 `BotService.cs` 约第212行（`OnGameEnded` 之后）添加：

```csharp
        public event Action OnIdleGuardTriggered;
```

- [ ] **Step 3: 在 Start() 中重置计数器**

在 `BotService.cs` 的 `Start()` 方法中（约第727行 `_currentMatchResultHandled = false;` 之后）添加：

```csharp
            _consecutiveIdleTurns = 0;
            _turnHadEffectiveAction = false;
```

- [ ] **Step 4: 提交**

```bash
git add BotMain/BotService.cs
git commit -m "feat: IdleGuard 新增字段、事件和启动重置"
```

---

### Task 2: 天梯主循环——标记有效动作 + END_TURN 后判断

**Files:**
- Modify: `BotMain/BotService.cs:2469-2470` (SEED 回合开始处)
- Modify: `BotMain/BotService.cs:2834-2838` (动作发送成功处)
- Modify: `BotMain/BotService.cs:3108-3132` (END_TURN 后等待处)

- [ ] **Step 1: 回合开始时重置 _turnHadEffectiveAction**

在天梯主循环的 SEED 分支开头（约第2470行 `notOurTurnStreak = 0;` 之���）添加：

```csharp
                _turnHadEffectiveAction = false;
```

- [ ] **Step 2: 动作执行成功时标记**

在天梯主循环的动作执行段落中，约第2838行 `Log($"[Action] {action} -> {result}");` 之后、`if (IsActionFailure(result))` ��前，添加：

```csharp
                            // IdleGuard: 标记本回合有有效动作
                            if (!isEndTurn && !IsActionFailure(result))
                                _turnHadEffectiveAction = true;
```

- [ ] **Step 3: END_TURN 后检查空回合并触发刹车**

在天梯主循环中，约第3108行 `// END_TURN 后等待回合切换，避免重复发送` **之前**，插入：

```csharp
                // ── IdleGuard: 空回合紧���刹车 ──
                if (lastAction != null && lastAction.Equals("END_TURN", StringComparison.OrdinalIgnoreCase))
                {
                    if (_turnHadEffectiveAction)
                    {
                        _consecutiveIdleTurns = 0;
                    }
                    else
                    {
                        _consecutiveIdleTurns++;
                        Log($"[IdleGuard] 空回合 #{_consecutiveIdleTurns}/3");
                        if (_consecutiveIdleTurns >= 3)
                        {
                            Log("[IdleGuard] 连续3回合无操作，触发紧急停止");
                            _running = false;
                            try { BattleNetWindowManager.KillHearthstone(s => Log(s)); } catch { }
                            try { OnIdleGuardTriggered?.Invoke(); } catch { }
                            break;
                        }
                    }
                }
```

- [ ] **Step 4: 对局结束时重置计数器**

在 `FinalizeMatchAndAutoQueue` 方法中，约第8850行 `wasInGame = false;` 之后添加：

```csharp
                _consecutiveIdleTurns = 0;
```

- [ ] **Step 5: 留牌阶段重置计数器**

在天梯主循环的 MULLIGAN 首次检测处（约第2338行 `mulliganStreak == 1` 块内，`mulliganPhaseStartedUtc = DateTime.UtcNow;` 之后）添加：

```csharp
                            _consecutiveIdleTurns = 0;
```

- [ ] **Step 6: 提交**

```bash
git add BotMain/BotService.cs
git commit -m "feat: 天梯主循环空回合计数与紧急刹车"
```

---

### Task 3: 竞技场主循环——同样的空回合检测

**Files:**
- Modify: `BotMain/BotService.cs:3659-3662` (竞技场 SEED 回合开始处)
- Modify: `BotMain/BotService.cs:3800-3801` (竞技场动作执行成功处)
- Modify: `BotMain/BotService.cs:3885-3898` (竞技场 END_TURN 处理处)
- Modify: `BotMain/BotService.cs:3611-3618` (竞技场 MULLIGAN 处)

- [ ] **Step 1: 竞技场回合开始时重置**

在竞技场主循环的 `// ── 我方回合：SEED: 数据 ──` 段落（约第3660行 `mulliganHandled = false;` 之后）���加：

```csharp
                _turnHadEffectiveAction = false;
```

- [ ] **Step 2: 竞技场动作执行成功时标记**

在竞技场动作循环中，约第3801行 `Log($"[Arena.Action] {action} -> {result}");` 之后、`if (IsActionFailure(result))` 之前，添加：

```csharp
                    // IdleGuard: 标记本回合有有效动作
                    if (!isEndTurn && !IsActionFailure(result))
                        _turnHadEffectiveAction = true;
```

- [ ] **Step 3: 竞技场 END_TURN 后检查空回合**

在竞技场主循环中，约第3885行 `// END_TURN 后���待回合切换` **之前**，插入：

```csharp
                // ── IdleGuard: 空回合��急刹车 ──
                if (lastAction != null && lastAction.Equals("END_TURN", StringComparison.OrdinalIgnoreCase))
                {
                    if (_turnHadEffectiveAction)
                    {
                        _consecutiveIdleTurns = 0;
                    }
                    else
                    {
                        _consecutiveIdleTurns++;
                        Log($"[IdleGuard] 空回合 #{_consecutiveIdleTurns}/3 (Arena)");
                        if (_consecutiveIdleTurns >= 3)
                        {
                            Log("[IdleGuard] 连续3回合无操作，���发紧急停止 (Arena)");
                            _running = false;
                            try { BattleNetWindowManager.KillHearthstone(s => Log(s)); } catch { }
                            try { OnIdleGuardTriggered?.Invoke(); } catch { }
                            break;
                        }
                    }
                }
```

- [ ] **Step 4: 竞技场留牌阶段重置**

在竞技场 MULLIGAN 首次检测处（约第3618行 `mulliganStreak == 1` 块内，`mulliganPhaseStartedUtc = DateTime.UtcNow;` 之后）添加：

```csharp
                            _consecutiveIdleTurns = 0;
```

- [ ] **Step 5: 竞技场对局结束处重置**

在竞技场的 `NO_GAME` 处理段（约第3604行 `Log("[Arena] 对局结束 (NO_GAME)。");` 之后）添加：

```csharp
                            _consecutiveIdleTurns = 0;
```

以及 `ENDGAME_PENDING` 处理段（约第3594行 `Log("[Arena] ENDGAME_PENDING，等待结算...");` 之后）添加：

```csharp
                            _consecutiveIdleTurns = 0;
```

- [ ] **Step 6: 提交**

```bash
git add BotMain/BotService.cs
git commit -m "feat: 竞技场主循环空��合计数与紧急刹车"
```

---

### Task 4: MainViewModel 监听事件并推送通知

**Files:**
- Modify: `BotMain/MainViewModel.cs:83` (事件订阅区域)
- Modify: `BotMain/MainViewModel.cs:1157` (通知方法区域)

- [ ] **Step 1: 订阅 OnIdleGuardTriggered 事件**

在 `MainViewModel` 构造函数或初始化区域中，约第83行 `_bot.OnRankTargetReached += OnRankTargetReached;` 之后添加：

```csharp
            _bot.OnIdleGuardTriggered += OnIdleGuardTriggered;
```

- [ ] **Step 2: 实现通知方法**

在 `MainViewModel.cs` 中，约第1157行 `OnRankTargetReached` 方法之后添加：

```csharp
        private void OnIdleGuardTriggered()
        {
            if (string.IsNullOrWhiteSpace(NotifyToken))
                return;

            var device = string.IsNullOrWhiteSpace(DeviceName) ? "默认设备" : DeviceName;
            var title = $"[{device}] 脚本异常停止";
            var content = $"设备: {device}\n原因: 连续3回合无任何操作\n时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

            _notify.SendNotification(SelectedNotifyChannelId, title, content);
        }
```

- [ ] **Step 3: 提交**

```bash
git add BotMain/MainViewModel.cs
git commit -m "feat: IdleGuard 触发时推送微信通知"
```
