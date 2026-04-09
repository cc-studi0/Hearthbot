# 段位达标停止重试轮询 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 对局结束后通过重试轮询确保段位达标停止100%触发，消除服务器延迟导致的漏检。

**Architecture:** 在 `BotService.cs` 新增 `CheckRankStopLimitWithRetry` 方法，对局结束后先等500ms再循环查询最多3次，确认段位稳定后再决定是否AutoQueue。仅当 `_maxRank > 0` 时走重试路径。

**Tech Stack:** C# / .NET Framework / WPF

---

## 文件映射

- 修改: `BotMain/BotService.cs`
  - 新增方法 `CheckRankStopLimitWithRetry`（在 `CheckRankStopLimit` 方法之后）
  - 修改 `HandlePostGame` 中的段位检查调用点（行8831-8835区域）

---

### Task 1: 新增 CheckRankStopLimitWithRetry 方法

**Files:**
- Modify: `BotMain/BotService.cs:8952`（在 `CheckRankStopLimit` 方法结束后插入）

- [ ] **Step 1: 在 `CheckRankStopLimit` 方法之后插入新方法**

在 `BotService.cs` 中找到 `CheckRankStopLimit` 方法结束的 `}` 之后（行8952），插入：

```csharp
        private bool CheckRankStopLimitWithRetry(PipeServer pipe, int retryCount = 3, int intervalMs = 500)
        {
            if (_maxRank <= 0)
            {
                // 未设置目标段位，仅更新UI显示
                TryQueryCurrentRank(pipe, force: true);
                return false;
            }

            var targetText = RankHelper.FormatRank(_maxRank);
            Log($"[RankRetry] 对局结束，开始重试轮询段位 (目标: {targetText}, 最多{retryCount}次)");

            // 首次查询前等待，给服务器更新时间
            if (SleepOrCancelled(intervalMs))
                return false;

            for (var i = 1; i <= retryCount; i++)
            {
                if (!_running)
                    return false;

                if (!TryQueryCurrentRank(pipe, force: true))
                {
                    Log($"[RankRetry] 第{i}次查询失败，跳过");
                    if (i < retryCount)
                        SleepOrCancelled(intervalMs);
                    continue;
                }

                var currentText = RankHelper.FormatRank(_lastQueriedStarLevel, _lastQueriedEarnedStars, _lastQueriedLegendIndex);

                if (_lastQueriedStarLevel >= _maxRank)
                {
                    var modeText = GetRankFormatNameForCurrentMode() == "FT_STANDARD" ? "标准" : "狂野";
                    Log($"[RankRetry] 第{i}次查询: {currentText}，已达标，停止");
                    Log($"[Limit] TargetRank={targetText} reached ({currentText}), stopping.");
                    OnRankTargetReached?.Invoke(currentText, modeText);
                    _running = false;
                    return true;
                }

                if (i < retryCount)
                {
                    Log($"[RankRetry] 第{i}次查询: {currentText}，未达标，{intervalMs}ms后重试");
                    if (SleepOrCancelled(intervalMs))
                        return false;
                }
                else
                {
                    Log($"[RankRetry] {retryCount}次查询均未达标 (当前: {currentText})，继续排队");
                }
            }

            return false;
        }
```

- [ ] **Step 2: 确认编译通过**

Run: `dotnet build BotMain/BotMain.csproj`
Expected: Build succeeded，无错误

- [ ] **Step 3: 提交**

```bash
git add BotMain/BotService.cs
git commit -m "feat: 新增 CheckRankStopLimitWithRetry 段位重试轮询方法"
```

---

### Task 2: 修改 HandlePostGame 调用点

**Files:**
- Modify: `BotMain/BotService.cs:8831-8835`

- [ ] **Step 1: 替换 HandlePostGame 中的段位检查逻辑**

找到 `BotService.cs` 中 `HandlePostGame` 方法内的以下代码（行8831-8835区域）：

```csharp
                CheckRunLimits();
                TryQueryCurrentRank(pipe, force: true);
                TryQueryPlayerName(pipe);
                if (CheckRankStopLimit(pipe, force: true))
                    return;
```

替换为：

```csharp
                CheckRunLimits();
                TryQueryPlayerName(pipe);
                if (CheckRankStopLimitWithRetry(pipe))
                    return;
```

说明：
- `TryQueryCurrentRank` 的调用已移入 `CheckRankStopLimitWithRetry` 内部（无论是否设了目标段位都会调用，确保UI显示更新）
- `TryQueryPlayerName` 移到前面，与段位重试无关，不需要等待
- 原有的 `CheckRankStopLimit(force: true)` 被替换为 `CheckRankStopLimitWithRetry(pipe)`

- [ ] **Step 2: 确认编译通过**

Run: `dotnet build BotMain/BotMain.csproj`
Expected: Build succeeded，无错误

- [ ] **Step 3: 提交**

```bash
git add BotMain/BotService.cs
git commit -m "fix: HandlePostGame 使用重试轮询检查段位，修复服务器延迟导致的漏检"
```
