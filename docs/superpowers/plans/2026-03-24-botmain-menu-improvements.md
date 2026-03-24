# BotMain 菜单改进实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修正 Finish 语义（对局结束后停止）、增加 Inject 按钮状态、提升 Stop 响应速度

**Architecture:** 三项改动都集中在 `BotMain/BotService.cs` 和 `BotMain/MainViewModel.cs`。Finish 改为在对局结算后检查标志；Inject 通过三态按钮文本和 `IsPrepared` 属性实现；Stop 通过 `CancellationToken.WaitHandle` 替代 `Thread.Sleep` 实现即时响应。

**Tech Stack:** C# / WPF / MVVM

**Spec:** `docs/superpowers/specs/2026-03-24-botmain-menu-improvements-design.md`

---

### Task 1: 修正 Finish — 删除动作循环中的错误检查点

**Files:**
- Modify: `BotMain/BotService.cs:2645-2650`

- [ ] **Step 1: 删除动作循环中的 `_finishAfterGame` 检查**

在 `BotService.cs` 第 2645-2650 行，删除以下代码（含前后空行）：

```csharp
                    if (_finishAfterGame)
                    {
                        Log("Current game finished, stopping automatically.");
                        _running = false;
                        break;
                    }
```

- [ ] **Step 2: 验证编译通过**

Run: `dotnet build BotMain/BotMain.csproj`
Expected: Build succeeded

- [ ] **Step 3: 提交**

```bash
git add BotMain/BotService.cs
git commit -m "移除动作循环中错误的 finishAfterGame 检查点"
```

---

### Task 2: 修正 Finish — 在对局结算后添加正确检查点（标准模式）

**Files:**
- Modify: `BotMain/BotService.cs:7486-7500`

- [ ] **Step 1: 在标准模式对局结算后添加检查**

在 `BotService.cs` 的 `wasInGame` 块**内部**，第 7498-7499 行 `if (CheckRankStopLimit(pipe, force: true)) return;` 之后、第 7500 行 `}` (wasInGame 块结束大括号) 之前，插入：

```csharp
                if (_finishAfterGame)
                {
                    Log("Game finished, stopping as requested.");
                    _running = false;
                    return;
                }
```

**重要**: 此检查必须在 `wasInGame` 块内部，确保只在对局实际结束时才触发，不会在非对局循环中误触。

- [ ] **Step 2: 验证编译通过**

Run: `dotnet build BotMain/BotMain.csproj`
Expected: Build succeeded

- [ ] **Step 3: 提交**

```bash
git add BotMain/BotService.cs
git commit -m "标准模式：对局结算后检查 finishAfterGame 并停止"
```

---

### Task 3: 修正 Finish — 在战旗模式添加检查点

**Files:**
- Modify: `BotMain/BotService.cs:1727`

- [ ] **Step 1: 在战旗模式 BG.AutoQueue 循环中添加检查**

在 `BotService.cs` 第 1727 行 `if (!_running) break;` 之后，添加：

```csharp
                    if (_finishAfterGame)
                    {
                        Log("[BG] Game finished, stopping as requested.");
                        _running = false;
                        break;
                    }
```

- [ ] **Step 2: 验证编译通过**

Run: `dotnet build BotMain/BotMain.csproj`
Expected: Build succeeded

- [ ] **Step 3: 提交**

```bash
git add BotMain/BotService.cs
git commit -m "战旗模式：对局结束后检查 finishAfterGame 并停止"
```

---

### Task 4: Inject 按钮 — 暴露 IsPrepared 属性

**Files:**
- Modify: `BotMain/BotService.cs:187`（`public BotState State` 属性附近）

- [ ] **Step 1: 在 BotService 中添加 IsPrepared 公开属性**

在 `BotService.cs` 第 187 行 `public BotState State { get; private set; } = BotState.Idle;` 之后添加：

```csharp
        public bool IsPrepared => _prepared;
```

- [ ] **Step 2: 验证编译通过**

Run: `dotnet build BotMain/BotMain.csproj`
Expected: Build succeeded

- [ ] **Step 3: 提交**

```bash
git add BotMain/BotService.cs
git commit -m "BotService 暴露 IsPrepared 属性"
```

---

### Task 5: Inject 按钮 — 修改 MainButtonText 为三态

**Files:**
- Modify: `BotMain/MainViewModel.cs:160`

- [ ] **Step 1: 将 MainButtonText 改为三态**

将 `MainViewModel.cs` 第 160 行：

```csharp
        public string MainButtonText => _bot.State == BotState.Idle ? "Start" : "Stop";
```

替换为：

```csharp
        public string MainButtonText => _bot.State == BotState.Idle
            ? (_bot.IsPrepared ? "Start" : "Inject")
            : "Stop";
```

**注意**: 无需新增通知事件。`OnStatusChanged` 回调（MainViewModel.cs:60）已包含 `Notify(nameof(MainButtonText))`，而 `Prepare()` 完成时已调用 `StatusChanged()`，所以 `_prepared` 变化时按钮文本会自动刷新。

- [ ] **Step 2: 验证编译通过**

Run: `dotnet build BotMain/BotMain.csproj`
Expected: Build succeeded

- [ ] **Step 3: 提交**

```bash
git add BotMain/MainViewModel.cs
git commit -m "MainButtonText 三态：Inject / Start / Stop"
```

---

### Task 6: Inject 按钮 — 修改 OnMainButton 增加 Inject 分支

**Files:**
- Modify: `BotMain/MainViewModel.cs:504-538`

- [ ] **Step 1: 在 OnMainButton 中增加 Inject 分支**

在 `MainViewModel.cs` 第 506 行 `if (_bot.State == BotState.Idle)` 块的最前面（第 507 行 `{` 之后），插入：

```csharp
                if (!_bot.IsPrepared)
                {
                    _bot.Prepare();
                    return;
                }
```

原有的 Start 逻辑（第 508 行 `if (SelectedProfileIndex < 0 ...` 开始）保持不变。

- [ ] **Step 2: 验证编译通过**

Run: `dotnet build BotMain/BotMain.csproj`
Expected: Build succeeded

- [ ] **Step 3: 提交**

```bash
git add BotMain/MainViewModel.cs
git commit -m "OnMainButton 增加 Inject 分支：未连接时重新注入"
```

---

### Task 7: Stop 立即中断 — 引入 SleepOrCancelled 辅助方法

**Files:**
- Modify: `BotMain/BotService.cs`

- [ ] **Step 1: 在 BotService 中添加 SleepOrCancelled 方法**

在 `WaitForGameReady` 方法之前（约第 2750 行前），添加：

```csharp
        /// <summary>
        /// 可取消的 Sleep。返回 true 表示已被取消（Stop 被调用），false 表示正常超时。
        /// </summary>
        private bool SleepOrCancelled(int ms)
        {
            try
            {
                return _cts?.Token.WaitHandle.WaitOne(ms) ?? false;
            }
            catch (ObjectDisposedException)
            {
                return true;
            }
        }
```

- [ ] **Step 2: 验证编译通过**

Run: `dotnet build BotMain/BotMain.csproj`
Expected: Build succeeded

- [ ] **Step 3: 提交**

```bash
git add BotMain/BotService.cs
git commit -m "引入 SleepOrCancelled 辅助方法支持可取消等待"
```

---

### Task 8: Stop 立即中断 — 替换 WaitForGameReady 中的 Sleep

**Files:**
- Modify: `BotMain/BotService.cs:2767-2823`

- [ ] **Step 1: 在 WaitForGameReady 循环中加入取消检查并替换 Sleep**

在 `WaitForGameReady` 方法（第 2767 行）的 for 循环中：

1. 在循环开头（第 2782 行 `for` 之后）加入取消检查：
```csharp
            for (int i = 0; i < maxRetries; i++)
            {
                if (_cts?.IsCancellationRequested == true) return false;
```

2. 将第 2818 行的 `Thread.Sleep(intervalMs)` 替换为：
```csharp
                if (i < maxRetries - 1 && intervalMs > 0)
                {
                    if (SleepOrCancelled(intervalMs)) return false;
                }
```

- [ ] **Step 2: 验证编译通过**

Run: `dotnet build BotMain/BotMain.csproj`
Expected: Build succeeded

- [ ] **Step 3: 提交**

```bash
git add BotMain/BotService.cs
git commit -m "WaitForGameReady 支持取消：加入 CancellationToken 检查"
```

---

### Task 9: Stop 立即中断 — 替换主循环路径中的关键 Thread.Sleep

**Files:**
- Modify: `BotMain/BotService.cs`

替换 MainLoop 标准模式路径（约第 1784-7525 行）中的 `Thread.Sleep` 调用。

**替换规则：**
- 在 `while (_running)` 循环体内的 `Thread.Sleep` → `if (SleepOrCancelled(ms)) break;`
- 在子方法或嵌套块中 → `if (SleepOrCancelled(ms)) return;`
- 动作间短延迟（actionDelayMs）→ `SleepOrCancelled(actionDelayMs);`（不检查返回值，因为下一步操作本身很快，且后续循环头部会检查 `_running`）
- ≤120ms 的短延迟保留为 `Thread.Sleep`（时间太短，中断收益不大）

- [ ] **Step 1: 替换主循环中的 Thread.Sleep（第 1784-2750 行区间）**

| 行号 | 原值(ms) | 替换为 |
|------|----------|--------|
| 1809 | 500 | `if (SleepOrCancelled(500)) break;` |
| 1916 | 300 | `if (SleepOrCancelled(300)) break;` |
| 1930 | 300 | `if (SleepOrCancelled(300)) break;` |
| 2002 | 150/250 | `if (SleepOrCancelled(...)) break;` |
| 2063 | 1000 | `if (SleepOrCancelled(1000)) break;` |
| 2074 | 300 | `if (SleepOrCancelled(300)) break;` |
| 2087 | 300 | `if (SleepOrCancelled(300)) break;` |
| 2137 | 300 | `if (SleepOrCancelled(300)) continue;` |
| 2148 | 1000 | `if (SleepOrCancelled(1000)) break;` |
| 2313 | 300 | `if (SleepOrCancelled(300)) continue;` |
| 2569 | actionDelayMs | `SleepOrCancelled(actionDelayMs);` |
| 2579 | actionDelayMs | `SleepOrCancelled(actionDelayMs);` |
| 2658 | 800 | `if (SleepOrCancelled(800)) break;` |
| 2689 | 2000 | `if (SleepOrCancelled(2000)) break;` |
| 2693 | 1000 | `if (SleepOrCancelled(1000)) break;` |
| 2740 | 800 | `if (SleepOrCancelled(800)) break;` |

保留不替换（≤120ms）：1958(120), 2166(120), 2197(120), 2206(120), 2244(120), 2339(120), 2709(120) 行。

- [ ] **Step 2: 替换对局结果等待和 AutoQueue 前的 Sleep（第 7440-7525 行区间）**

| 行号 | 原值(ms) | 替换为 |
|------|----------|--------|
| 7440 | 150 | `SleepOrCancelled(150);` |
| 7456 | 150 | `SleepOrCancelled(150);` |
| 7522 | PostGameResultResyncAutoQueueDelayMs | `SleepOrCancelled(PostGameResultResyncAutoQueueDelayMs);` |

- [ ] **Step 3: 验证编译通过**

Run: `dotnet build BotMain/BotMain.csproj`
Expected: Build succeeded

- [ ] **Step 4: 提交**

```bash
git add BotMain/BotService.cs
git commit -m "主循环路径 Thread.Sleep 替换为可取消等待"
```

---

### Task 10: Stop 立即中断 — 替换战旗模式循环中的 Thread.Sleep

**Files:**
- Modify: `BotMain/BotService.cs`

- [ ] **Step 1: 替换战旗模式 BG.AutoQueue 循环中的 Sleep（第 1718-1782 行）**

| 行号 | 原值(ms) | 替换为 |
|------|----------|--------|
| 1735 | 1000 | `if (SleepOrCancelled(1000)) break;` |
| 1776 | 500 | `if (SleepOrCancelled(500)) break;` |
| 1778 | 2000 | `if (SleepOrCancelled(2000)) break;` |

- [ ] **Step 2: 替换 BattlegroundsLoop 中的关键 Sleep（第 3058-3583 行）**

| 行号 | 原值(ms) | 替换为 |
|------|----------|--------|
| 3082 | 3000 | `if (SleepOrCancelled(3000)) return;` |
| 3111 | 1000 | `if (SleepOrCancelled(1000)) return;` |
| 3139 | 500 | `while (_suspended && _running) SleepOrCancelled(500);` |
| 3148 | 1000 | `if (SleepOrCancelled(1000)) return;` |
| 3175 | 150/300 | `SleepOrCancelled(...);` |
| 3185 | 1000 | `if (SleepOrCancelled(1000)) return;` |
| 3192 | 500 | `if (SleepOrCancelled(500)) return;` |
| 3269 | 200 | `SleepOrCancelled(200);` |
| 3287 | 180 | `SleepOrCancelled(180);` |
| 3312 | 350 | `SleepOrCancelled(350);` |
| 3319 | 250 | `SleepOrCancelled(250);` |
| 3325 | 250 | `SleepOrCancelled(250);` |
| 3335 | 200 | `SleepOrCancelled(200);` |
| 3342 | 1000 | `if (SleepOrCancelled(1000)) return;` |
| 3346 | 500 | `if (SleepOrCancelled(500)) return;` |
| 3353 | 120 | 保留 |
| 3397 | 500 | `if (SleepOrCancelled(500)) return;` |
| 3418 | 1000 | `if (SleepOrCancelled(1000)) return;` |
| 3437 | 500 | `if (SleepOrCancelled(500)) return;` |
| 3467 | 200 | `SleepOrCancelled(200);` |
| 3487 | 180 | `SleepOrCancelled(180);` |
| 3522 | 500 | `if (SleepOrCancelled(500)) return;` |
| 3546 | 180 | `SleepOrCancelled(180);` |
| 3562 | 220 | `SleepOrCancelled(220);` |
| 3571 | 150 | `SleepOrCancelled(150);` |
| 3576 | 150 | `SleepOrCancelled(150);` |
| 3579 | 220 | `SleepOrCancelled(220);` |
| 3583 | 500 | `if (SleepOrCancelled(500)) return;` |

- [ ] **Step 3: 验证编译通过**

Run: `dotnet build BotMain/BotMain.csproj`
Expected: Build succeeded

- [ ] **Step 4: 提交**

```bash
git add BotMain/BotService.cs
git commit -m "战旗模式 BattlegroundsLoop 的 Thread.Sleep 替换为可取消等待"
```

---

### Task 11: Stop 立即中断 — 替换 ResolvePostGameResult 和 AutoQueue 中的 Thread.Sleep

**Files:**
- Modify: `BotMain/BotService.cs`

- [ ] **Step 1: 替换 ResolvePostGameResultWithWindow 中的 Sleep（第 6653-7294 行）**

| 行号 | 原值(ms) | 替换为 |
|------|----------|--------|
| 6765 | 1000 | `if (SleepOrCancelled(1000)) break;` |
| 6772 | 1000 | `if (SleepOrCancelled(1000)) break;` |
| 6784 | 2000 | `if (SleepOrCancelled(2000)) break;` |
| 6801 | 2000 | `if (SleepOrCancelled(2000)) break;` |
| 6813 | 1000 | `if (SleepOrCancelled(1000)) break;` |
| 6847 | 800 | `SleepOrCancelled(800);` |
| 6852 | 1000 | `if (SleepOrCancelled(1000)) break;` |
| 6864 | 1000 | `if (SleepOrCancelled(1000)) break;` |
| 6892 | 1000 | `SleepOrCancelled(1000);` |
| 6921 | 800 | `SleepOrCancelled(800);` |
| 6926 | 1000 | `if (SleepOrCancelled(1000)) break;` |
| 6938 | 1000 | `if (SleepOrCancelled(1000)) break;` |
| 6966 | 1000 | `SleepOrCancelled(1000);` |
| 6994 | 800 | `SleepOrCancelled(800);` |
| 6999 | 1000 | `if (SleepOrCancelled(1000)) break;` |
| 7007 | 1000 | `if (SleepOrCancelled(1000)) break;` |
| 7015 | 1000 | `if (SleepOrCancelled(1000)) break;` |
| 7036 | 1000 | `SleepOrCancelled(1000);` |
| 7065 | 800 | `SleepOrCancelled(800);` |
| 7070 | 1000 | `if (SleepOrCancelled(1000)) break;` |
| 7078 | 1000 | `if (SleepOrCancelled(1000)) break;` |
| 7086 | 1000 | `if (SleepOrCancelled(1000)) break;` |
| 7107 | 1000 | `SleepOrCancelled(1000);` |
| 7136 | 600 | `SleepOrCancelled(600);` |
| 7141 | 800 | `SleepOrCancelled(800);` |
| 7160 | 500 | `SleepOrCancelled(500);` |
| 7168 | 800 | `SleepOrCancelled(800);` |
| 7177 | 500 | `SleepOrCancelled(500);` |
| 7198 | 700 | `SleepOrCancelled(700);` |
| 7202 | 800 | `SleepOrCancelled(800);` |
| 7267 | 250 | `SleepOrCancelled(250);` |
| 7294 | 250 | `SleepOrCancelled(250);` |

**注意**: 此区域的 Sleep 多在循环内。若在 for/while 循环中，用 `break`；若在方法级别，直接 `SleepOrCancelled(ms);`（让调用方根据 `_running` 判断）。实施者需读上下文确定 `break` 跳出的目标循环。

- [ ] **Step 2: 替换 AutoQueue 中的 Sleep（第 7797-8148 行）**

| 行号 | 原值(ms) | 替换为 |
|------|----------|--------|
| 7802 | 1000 | `SleepOrCancelled(1000);` |
| 7815 | 500 | `SleepOrCancelled(500);` |
| 7834 | 500 | `SleepOrCancelled(500);` |
| 7845 | 800 | `SleepOrCancelled(800);` |
| 7855 | 1000 | `if (SleepOrCancelled(1000)) return;` |
| 7864 | 2000 | `if (SleepOrCancelled(2000)) return;` |
| 7882 | 1000 | `if (SleepOrCancelled(1000)) return;` |
| 7893 | 2000 | `if (SleepOrCancelled(2000)) return;` |
| 7920 | 2000 | `if (SleepOrCancelled(2000)) return;` |
| 7936 | 1000 | `if (SleepOrCancelled(1000)) return;` |
| 7961 | 1000 | `if (SleepOrCancelled(1000)) return;` |
| 7987 | 1000 | `if (SleepOrCancelled(1000)) return;` |
| 8038 | 3000 | `if (SleepOrCancelled(3000)) return;` |
| 8059 | 1000 | `if (SleepOrCancelled(1000)) return;` |
| 8066 | 1000 | `if (SleepOrCancelled(1000)) return;` |
| 8078 | 1000 | `if (SleepOrCancelled(1000)) return;` |
| 8088 | 3000 | `if (SleepOrCancelled(3000)) return;` |
| 8096 | 5000 | `if (SleepOrCancelled(5000)) return;` |
| 8110 | 5000 | `if (SleepOrCancelled(5000)) return;` |
| 8118 | 1000 | `SleepOrCancelled(1000);` |
| 8125 | 5000 | `if (SleepOrCancelled(5000)) return;` |
| 8132 | 1000 | `SleepOrCancelled(1000);` |
| 8139 | 2000 | `if (SleepOrCancelled(2000)) return;` |
| 8148 | 5000 | `if (SleepOrCancelled(5000)) return;` |

- [ ] **Step 3: 验证编译通过**

Run: `dotnet build BotMain/BotMain.csproj`
Expected: Build succeeded

- [ ] **Step 4: 提交**

```bash
git add BotMain/BotService.cs
git commit -m "ResolvePostGameResult 和 AutoQueue 的 Thread.Sleep 替换为可取消等待"
```

---

### Task 12: 最终验证

- [ ] **Step 1: 全量编译验证**

Run: `dotnet build BotMain/BotMain.csproj`
Expected: Build succeeded, 0 errors

- [ ] **Step 2: 搜索遗漏的关键 Thread.Sleep**

在主循环路径方法中搜索剩余的 `Thread.Sleep`，确认只有以下类型被保留：
- ≤120ms 的短延迟
- `Prepare()` 阶段的 Sleep
- 工具方法中带 `retryDelayMs` 参数的 Sleep（4481, 4655, 4670, 4690, 4721 行）
- 不在 `_running` 循环中的一次性 Sleep（5279, 5450, 5747, 5805 行）
- 独立辅助方法中的 Sleep（8241, 8877 行）

- [ ] **Step 3: 手动集成验证**

按照设计文档中的验证计划测试：
- Finish：对局中途点击 Finish → 对局打完才停止（验证标准模式和战旗模式）
- Inject：未连接游戏 → Inject；连接后 → Start；游戏重启 → Inject
- Stop：对局中点击 Stop → 1-2秒内停止
