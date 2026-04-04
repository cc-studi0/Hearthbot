# HSBox 推荐 Key-Based 去重实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 HSBox 推荐去重从时间戳+新鲜度机制改为 SmartBot 风格的 key-based HashSet 去重，操作确认成功后才标记已消费。

**Architecture:** 新建 `RecommendationDeduplicator` 类封装 `_knownKeys` HashSet；BotService 在获取推荐时用 `IsKnown()` 过滤，操作成功后调用 `MarkConsumed()`；移除 `FreshnessSlackMs` 相关的时间戳新鲜度过滤逻辑。

**Tech Stack:** C# / .NET

---

## 文件结构

| 操作 | 文件 | 职责 |
|------|------|------|
| 新建 | `BotMain/RecommendationDeduplicator.cs` | Key-based 去重核心类 |
| 修改 | `BotMain/BotService.cs` | 集成去重器，替换旧字段 |
| 修改 | `BotMain/HsBoxRecommendationProvider.cs` | 移除 FreshnessSlackMs 相关逻辑 |
| 修改 | `BotMain/GameRecommendationProvider.cs` | 简化 ConsumptionTracker（保留连续重复检测） |

---

### Task 1: 新建 RecommendationDeduplicator 类

**Files:**
- Create: `BotMain/RecommendationDeduplicator.cs`

- [ ] **Step 1: 创建 RecommendationDeduplicator 类**

```csharp
using System;
using System.Collections.Generic;

namespace BotMain
{
    /// <summary>
    /// SmartBot 风格的推荐去重器。用 key-based HashSet 记住整局已成功执行的推荐。
    /// 与 SmartBot 的区别：操作确认成功后才标记已消费，失败可重试。
    /// </summary>
    internal sealed class RecommendationDeduplicator
    {
        private readonly HashSet<string> _knownKeys = new(StringComparer.Ordinal);
        private readonly object _lock = new();

        /// <summary>
        /// 检查推荐是否已被成功执行过。
        /// </summary>
        public bool IsKnown(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            lock (_lock) { return _knownKeys.Contains(key); }
        }

        /// <summary>
        /// 操作确认成功后调用，标记该推荐为已消费。
        /// </summary>
        public void MarkConsumed(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            lock (_lock) { _knownKeys.Add(key); }
        }

        /// <summary>
        /// 对局开始/结束时清空所有已知 key。
        /// </summary>
        public void Clear()
        {
            lock (_lock) { _knownKeys.Clear(); }
        }

        /// <summary>当前已知 key 数量（用于日志）。</summary>
        public int Count
        {
            get { lock (_lock) { return _knownKeys.Count; } }
        }

        /// <summary>
        /// 从推荐的 PayloadSignature 和首个动作命令构建去重 key。
        /// PayloadSignature 是推荐内容的哈希，首个动作命令标识具体执行内容。
        /// </summary>
        public static string BuildKey(string payloadSignature, string firstActionCommand)
        {
            return $"{payloadSignature ?? ""}|{firstActionCommand ?? ""}";
        }

        /// <summary>
        /// 从推荐的 PayloadSignature 和动作列表构建去重 key。
        /// </summary>
        public static string BuildKey(string payloadSignature, IReadOnlyList<string> actions)
        {
            var firstAction = actions != null && actions.Count > 0 ? actions[0] : "";
            return BuildKey(payloadSignature, firstAction);
        }
    }
}
```

- [ ] **Step 2: 提交**

```bash
git add BotMain/RecommendationDeduplicator.cs
git commit -m "feat: 新建 RecommendationDeduplicator key-based 去重类"
```

---

### Task 2: BotService 集成去重器 — 字段与初始化

**Files:**
- Modify: `BotMain/BotService.cs`

- [ ] **Step 1: 添加去重器字段**

在 BotService 字段区域（约 151 行 `_lastConsumedHsBoxActionUpdatedAtMs` 附近），添加：

```csharp
private readonly RecommendationDeduplicator _actionDedup = new();
private readonly RecommendationDeduplicator _choiceDedup = new();
```

- [ ] **Step 2: 在回合切换时清空去重器**

搜索 `ResetHsBoxActionRecommendationTracking()` 的调用处（约 4901 行，turn change 处），在旁边添加：

```csharp
// 已有代码：
ResetHsBoxActionRecommendationTracking();
_lastConsumedHsBoxChoiceUpdatedAtMs = 0;
_lastConsumedHsBoxChoicePayloadSignature = string.Empty;
_choiceRepeatedRecommendationCount = 0;

// 注意：回合切换时不清空 _actionDedup/_choiceDedup
// 与 SmartBot 一致：整局对局记住所有已成功执行的推荐
```

- [ ] **Step 3: 在对局结束时清空去重器**

搜索 `HandleGameEnd` 或对局结束处调用的 `ResetHsBoxActionRecommendationTracking()`（约 7853 行），在旁边添加：

```csharp
ResetHsBoxActionRecommendationTracking();
_actionDedup.Clear();
_choiceDedup.Clear();
```

- [ ] **Step 4: 在 Bot Start 时清空去重器**

搜索 BotService 的 `Start()` 方法（约 648 行），在 `_running = true;` 之后添加：

```csharp
_actionDedup.Clear();
_choiceDedup.Clear();
```

- [ ] **Step 5: 提交**

```bash
git add BotMain/BotService.cs
git commit -m "feat: BotService 添加 RecommendationDeduplicator 字段和生命周期管理"
```

---

### Task 3: BotService 操作推荐消费处集成去重

**Files:**
- Modify: `BotMain/BotService.cs`

- [ ] **Step 1: 在操作推荐获取后添加 key-based 去重检查**

搜索操作推荐消费主逻辑（约 2565 行附近），找到 `recommendation?.ShouldRetryWithoutAction == true` 之前、推荐有有效 Actions 的分支。在推荐被判定有效（非 ShouldRetryWithoutAction）且准备执行之前，添加去重检查：

```csharp
// 在推荐有效、准备执行操作之前：
var dedupKey = RecommendationDeduplicator.BuildKey(
    recommendation.SourcePayloadSignature,
    recommendation.Actions);
if (_actionDedup.IsKnown(dedupKey))
{
    Log($"[Action] 跳过已成功执行的推荐 key={dedupKey}");
    Thread.Sleep(120);
    continue;
}
```

- [ ] **Step 2: 在操作成功后标记已消费**

搜索 `RememberConsumedHsBoxActionRecommendation` 的调用处（操作执行成功后），在旁边添加：

```csharp
RememberConsumedHsBoxActionRecommendation(recommendation, executedAction);
// 新增：操作成功，标记 key 为已消费
var consumedKey = RecommendationDeduplicator.BuildKey(
    recommendation.SourcePayloadSignature,
    recommendation.Actions);
_actionDedup.MarkConsumed(consumedKey);
```

- [ ] **Step 3: 提交**

```bash
git add BotMain/BotService.cs
git commit -m "feat: 操作推荐消费处集成 key-based 去重"
```

---

### Task 4: BotService 选择推荐消费处集成去重

**Files:**
- Modify: `BotMain/BotService.cs`

- [ ] **Step 1: 在选择推荐获取后添加 key-based 去重检查**

搜索选择推荐消费逻辑（约 5370 行附近），找到 `ChoiceRecommendationResult` 被获取且有效的分支，在执行前添加：

```csharp
var choiceDedupKey = RecommendationDeduplicator.BuildKey(
    choiceRecommendation.SourcePayloadSignature,
    choiceRecommendation.SelectedOptionCommand ?? "");
if (_choiceDedup.IsKnown(choiceDedupKey))
{
    Log($"[Choice] 跳过已成功执行的推荐 key={choiceDedupKey}");
    Thread.Sleep(120);
    continue;
}
```

- [ ] **Step 2: 在选择操作成功后标记已消费**

在 `ChoiceRecommendationConsumptionTracker.TryRememberConsumed` 调用附近，添加：

```csharp
// 新增：选择成功，标记 key 为已消费
var consumedChoiceKey = RecommendationDeduplicator.BuildKey(
    choiceRecommendation.SourcePayloadSignature,
    choiceRecommendation.SelectedOptionCommand ?? "");
_choiceDedup.MarkConsumed(consumedChoiceKey);
```

- [ ] **Step 3: 提交**

```bash
git add BotMain/BotService.cs
git commit -m "feat: 选择推荐消费处集成 key-based 去重"
```

---

### Task 5: 移除 FreshnessSlackMs 相关逻辑

**Files:**
- Modify: `BotMain/HsBoxRecommendationProvider.cs`

- [ ] **Step 1: 移除 FreshnessSlackMs 常量**

搜索 `FreshnessSlackMs`（约 32 行），删除：

```csharp
// 删除这行：
private const int FreshnessSlackMs = 3000;
```

- [ ] **Step 2: 简化 TryEvaluateActionPayloadFreshness**

将 `TryEvaluateActionPayloadFreshness` 方法（约 385 行）简化为仅检查是否比上次消费更新（保留 `lastConsumedUpdatedAtMs` 和 `lastConsumedPayloadSignature` 比较，因为这是 ConsumptionTracker 需要的）：

移除方法中所有 `FreshnessSlackMs` 引用。将 `minimumUpdatedAtMs` 相关的新鲜度检查改为直接返回 true（不再基于时间戳拒绝推荐）：

```csharp
// 将原来的：
if (minimumUpdatedAtMs > 0
    && state.UpdatedAtMs + FreshnessSlackMs < minimumUpdatedAtMs)
{
    reason = "minimum_updated_at_ignored_for_unconsumed_action";
    return true;
}

// 改为：
if (minimumUpdatedAtMs > 0)
{
    reason = "no_freshness_filter";
    return true;
}
```

- [ ] **Step 3: 同样简化 TryEvaluateChoicePayloadFreshness**

将 `TryEvaluateChoicePayloadFreshness` 方法（约 448 行）中的 `ChoiceFreshnessSlackMs = 8000` 及相关检查移除，改为直接返回 true：

```csharp
// 删除：
const int ChoiceFreshnessSlackMs = 8000;
if (minimumUpdatedAtMs > 0
    && state.UpdatedAtMs + ChoiceFreshnessSlackMs < minimumUpdatedAtMs)
{
    reason = "minimum_updated_at_ignored_for_unconsumed_choice";
    return true;
}

// 改为：
if (minimumUpdatedAtMs > 0)
{
    reason = "no_freshness_filter";
    return true;
}
```

- [ ] **Step 4: 移除 IsFreshEnough 中的 FreshnessSlackMs**

将 `IsFreshEnough` 方法（约 367 行）简化：

```csharp
private static bool IsFreshEnough(HsBoxRecommendationState state, long minimumUpdatedAtMs)
{
    if (state == null)
        return false;
    return state.UpdatedAtMs > 0;
}
```

- [ ] **Step 5: 提交**

```bash
git add BotMain/HsBoxRecommendationProvider.cs
git commit -m "refactor: 移除 FreshnessSlackMs 时间戳新鲜度过滤，改用 key-based 去重"
```

---

### Task 6: 移除 BotService 中的 _hsBoxActionMinimumUpdatedAtMs 相关逻辑

**Files:**
- Modify: `BotMain/BotService.cs`

- [ ] **Step 1: 删除 _hsBoxActionMinimumUpdatedAtMs 字段和方法**

搜索并删除以下字段和方法：

```csharp
// 删除字段（约 154 行）：
private long _hsBoxActionMinimumUpdatedAtMs;

// 删除方法（约 1277-1284 行）：
private void RefreshHsBoxActionMinimumUpdatedAtNow() { ... }
private long GetHsBoxActionMinimumUpdatedAtMs() => ...;
```

- [ ] **Step 2: 替换所有 RefreshHsBoxActionMinimumUpdatedAtNow() 调用**

搜索 `RefreshHsBoxActionMinimumUpdatedAtNow()` 的所有调用处，全部删除（不需要替换，因为不再需要时间戳最低门槛）。

- [ ] **Step 3: 替换所有 GetHsBoxActionMinimumUpdatedAtMs() 调用**

搜索 `GetHsBoxActionMinimumUpdatedAtMs()` 的所有调用处（约 2531 行，在构建 ActionRecommendationRequest 时），将参数值改为 `0`：

```csharp
// 将：
GetHsBoxActionMinimumUpdatedAtMs(),
// 改为：
0,
```

- [ ] **Step 4: 清理 ResetHsBoxActionRecommendationTracking 中的字段重置**

在 `ResetHsBoxActionRecommendationTracking()` 方法中删除：
```csharp
// 删除：
_hsBoxActionMinimumUpdatedAtMs = 0;
```

- [ ] **Step 5: 提交**

```bash
git add BotMain/BotService.cs
git commit -m "refactor: 移除 _hsBoxActionMinimumUpdatedAtMs 时间戳门槛逻辑"
```

---

### Task 7: 编译验证

- [ ] **Step 1: 编译**

```bash
cd BotMain && dotnet build -o bin/BuildCheck --nologo -v q
```

确保无编译错误。

- [ ] **Step 2: 检查去重流程完整性**

确认以下调用链：
1. 推荐获取 → `BuildKey()` → `IsKnown()` → 跳过/继续
2. 操作执行成功 → `MarkConsumed()` 标记已消费
3. 对局结束 → `Clear()` 清空
4. Bot Start → `Clear()` 清空

- [ ] **Step 3: 提交**

```bash
git add -A
git commit -m "feat: HSBox 推荐 key-based 去重完成"
```
