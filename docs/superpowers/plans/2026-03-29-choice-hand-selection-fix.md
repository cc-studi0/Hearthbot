# 修复：选择手牌失败导致无限卡住 — 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复打牌后"从手牌中选择一张"操作因位置不精确导致点击失败、推荐被永久消费、脚本无限卡住的问题。

**Architecture:** 三层防御——(1) Choice 消费追踪器增加释放阈值打破死循环；(2) MouseClickChoice 增加 ChosenEntityIds 二次验证减少 false positive；(3) 两者结合确保即使验证误判也能自动恢复。

**Tech Stack:** C# (.NET)，无自动化测试框架（游戏脚本项目，手动验证）

---

### Task 1: ChoiceRecommendationConsumptionTracker 增加释放阈值

**Files:**
- Modify: `BotMain/GameRecommendationProvider.cs:381-438`

- [ ] **Step 1: 添加 IsSamePayload 和 ShouldTreatAsConsumed 方法**

在 `ChoiceRecommendationConsumptionTracker` 类中（`GameRecommendationProvider.cs:381`），在现有 `Reset` 方法之后、类的闭合大括号之前，添加以下代码：

```csharp
        internal const int ReleaseThreshold = 2;

        public static bool IsSamePayload(
            long sourceUpdatedAtMs,
            string sourcePayloadSignature,
            long lastConsumedUpdatedAtMs,
            string lastConsumedPayloadSignature)
        {
            if (sourceUpdatedAtMs <= 0 || lastConsumedUpdatedAtMs <= 0)
                return false;

            if (sourceUpdatedAtMs != lastConsumedUpdatedAtMs)
                return false;

            if (string.IsNullOrWhiteSpace(lastConsumedPayloadSignature))
                return true;

            return string.Equals(
                sourcePayloadSignature ?? string.Empty,
                lastConsumedPayloadSignature,
                StringComparison.Ordinal);
        }

        public static bool ShouldTreatAsConsumed(
            long sourceUpdatedAtMs,
            string sourcePayloadSignature,
            long lastConsumedUpdatedAtMs,
            string lastConsumedPayloadSignature,
            ref int repeatedRecommendationCount,
            out bool releasedDueToRepetition)
        {
            releasedDueToRepetition = false;

            if (!IsSamePayload(sourceUpdatedAtMs, sourcePayloadSignature, lastConsumedUpdatedAtMs, lastConsumedPayloadSignature))
            {
                repeatedRecommendationCount = 0;
                return false;
            }

            repeatedRecommendationCount++;
            if (repeatedRecommendationCount < ReleaseThreshold)
                return true;

            releasedDueToRepetition = true;
            repeatedRecommendationCount = 0;
            return false;
        }
```

- [ ] **Step 2: 验证编译**

Run: `dotnet build BotMain/BotMain.csproj --no-restore`（或项目实际的构建命令）
Expected: 编译成功，无错误

- [ ] **Step 3: Commit**

```bash
git add BotMain/GameRecommendationProvider.cs
git commit -m "功能：ChoiceRecommendationConsumptionTracker 增加释放阈值机制"
```

---

### Task 2: BotService 集成 Choice 消费释放逻辑

**Files:**
- Modify: `BotMain/BotService.cs:153-154`（字段区域）
- Modify: `BotMain/BotService.cs:5242-5246`（TryHandleChoice 中 ShouldRetryWithoutAction 处理）
- Modify: `BotMain/BotService.cs:3204-3205`（重置逻辑）

- [ ] **Step 1: 添加重复计数字段**

在 `BotService.cs:154` 行（`_lastConsumedHsBoxChoicePayloadSignature` 字段之后）添加：

```csharp
        private int _choiceRepeatedRecommendationCount;
```

- [ ] **Step 2: 在 TryHandleChoice 中增加释放逻辑**

在 `BotService.cs:5242-5245`，将：

```csharp
                if (recommendation?.ShouldRetryWithoutAction == true)
                {
                    Log($"[Choice] waiting snapshotId={currentState.SnapshotId} mechanism={currentState.MechanismKind} mode={currentState.Mode} detail={recommendation.Detail}");
                    return false;
                }
```

替换为：

```csharp
                if (recommendation?.ShouldRetryWithoutAction == true)
                {
                    var consumed = ChoiceRecommendationConsumptionTracker.ShouldTreatAsConsumed(
                        recommendation.SourceUpdatedAtMs,
                        recommendation.SourcePayloadSignature,
                        _lastConsumedHsBoxChoiceUpdatedAtMs,
                        _lastConsumedHsBoxChoicePayloadSignature,
                        ref _choiceRepeatedRecommendationCount,
                        out var releasedDueToRepetition);

                    if (releasedDueToRepetition)
                    {
                        Log($"[Choice] consumption_released_due_to_repetition snapshotId={currentState.SnapshotId} mode={currentState.Mode}");
                        ChoiceRecommendationConsumptionTracker.Reset(
                            ref _lastConsumedHsBoxChoiceUpdatedAtMs,
                            ref _lastConsumedHsBoxChoicePayloadSignature);
                        continue;
                    }

                    Log($"[Choice] waiting snapshotId={currentState.SnapshotId} mechanism={currentState.MechanismKind} mode={currentState.Mode} detail={recommendation.Detail}");
                    return false;
                }
```

注意：释放后用 `continue` 回到 `for` 循环顶部，重新获取推荐（此时消费状态已清空，推荐应该能正常返回）。

- [ ] **Step 3: 在现有重置点同步重置计数**

在 `BotService.cs:3204-3205`（已有的 `_lastConsumedHsBoxChoice*` 重置位置）之后添加：

```csharp
            _choiceRepeatedRecommendationCount = 0;
```

同样在 `BotService.cs:4759-4760`（另一个重置位置）之后添加：

```csharp
            _choiceRepeatedRecommendationCount = 0;
```

- [ ] **Step 4: 验证编译**

Run: `dotnet build BotMain/BotMain.csproj --no-restore`
Expected: 编译成功

- [ ] **Step 5: Commit**

```bash
git add BotMain/BotService.cs
git commit -m "功能：TryHandleChoice 集成 Choice 消费释放逻辑，打破无限等待"
```

---

### Task 3: MouseClickChoice 验证增强 — 添加 IsEntityInChosenSnapshot 辅助方法

**Files:**
- Modify: `HearthstonePayload/ActionExecutor.cs:7610-7626`（CaptureChoiceSnapshot 附近）

- [ ] **Step 1: 添加辅助方法**

在 `ActionExecutor.cs` 的 `CaptureChoiceSnapshot` 方法（第7610行）之后，添加：

```csharp
        private static bool CaptureChoiceSnapshotChosen(int entityId, out bool entityChosen)
        {
            entityChosen = false;

            var gs = GetGameState();
            if (gs == null) return false;

            if (!TryBuildChoiceSnapshot(gs, out var snapshot) || snapshot == null)
                return false;

            entityChosen = snapshot.ChosenEntityIds != null && snapshot.ChosenEntityIds.Contains(entityId);
            return true;
        }
```

此方法返回 `true` 表示成功获取到快照（选择界面仍然存在），`entityChosen` 表示目标 entityId 是否在已选列表中。返回 `false` 表示选择界面已关闭。

- [ ] **Step 2: 验证编译**

Run: `dotnet build HearthstonePayload/HearthstonePayload.csproj --no-restore`
Expected: 编译成功

- [ ] **Step 3: Commit**

```bash
git add HearthstonePayload/ActionExecutor.cs
git commit -m "功能：添加 CaptureChoiceSnapshotChosen 辅助方法"
```

---

### Task 4: MouseClickChoice 鼠标验证中使用 ChosenEntityIds 二次确认

**Files:**
- Modify: `HearthstonePayload/ActionExecutor.cs:7536-7555`（MouseClickChoice 验证循环）

- [ ] **Step 1: 修改验证逻辑**

在 `ActionExecutor.cs:7536-7555`，将鼠标点击后的验证循环：

```csharp
                // 校验：选择界面应关闭，或切换到新的选择状态（连发现）。
                for (int i = 0; i < 14; i++)
                {
                    yield return 0.12f;
                    if (!CaptureChoiceSnapshot(out var afterChoiceId, out var afterSignature))
                    {
                        confirmed = true;
                        confirmDetail = "closed@mouse" + attempt;
                        break;
                    }

                    if (afterChoiceId != beforeChoiceId || !string.Equals(afterSignature, beforeSignature, StringComparison.Ordinal))
                    {
                        confirmed = true;
                        confirmDetail = "changed@mouse" + attempt;
                        break;
                    }

                    confirmDetail = "unchanged";
                }
```

替换为：

```csharp
                // 校验：选择界面应关闭，或切换到新的选择状态（连发现）。
                for (int i = 0; i < 14; i++)
                {
                    yield return 0.12f;
                    if (!CaptureChoiceSnapshot(out var afterChoiceId, out var afterSignature))
                    {
                        confirmed = true;
                        confirmDetail = "closed@mouse" + attempt;
                        break;
                    }

                    if (afterChoiceId != beforeChoiceId || !string.Equals(afterSignature, beforeSignature, StringComparison.Ordinal))
                    {
                        // 签名变化时二次确认：检查目标 entityId 是否真的被选中。
                        // 如果选择界面仍在且目标不在已选列表中，可能是手牌重排等无关变化导致的 false positive。
                        if (CaptureChoiceSnapshotChosen(entityId, out var entityChosen) && !entityChosen)
                        {
                            // 选择界面仍在且目标未被选中 → 不认为选择成功，更新基准继续等待
                            AppendActionTrace("choice_click_false_positive entityId=" + entityId + " attempt=" + attempt + " afterChoiceId=" + afterChoiceId);
                            beforeChoiceId = afterChoiceId;
                            beforeSignature = afterSignature;
                            confirmDetail = "false_positive@mouse" + attempt;
                            continue;
                        }

                        confirmed = true;
                        confirmDetail = "changed@mouse" + attempt;
                        break;
                    }

                    confirmDetail = "unchanged";
                }
```

关键逻辑说明：
- 当签名变化时，调用 `CaptureChoiceSnapshotChosen` 检查目标是否在 `ChosenEntityIds` 中
- `CaptureChoiceSnapshotChosen` 返回 `true`（界面仍在）且 `entityChosen=false`（目标未被选中）→ false positive，更新基准签名继续等待
- `CaptureChoiceSnapshotChosen` 返回 `false`（界面关闭）→ 正常确认（走下面的 confirmed=true）
- `entityChosen=true` → 目标确实被选中 → 正常确认

- [ ] **Step 2: 验证编译**

Run: `dotnet build HearthstonePayload/HearthstonePayload.csproj --no-restore`
Expected: 编译成功

- [ ] **Step 3: Commit**

```bash
git add HearthstonePayload/ActionExecutor.cs
git commit -m "功能：MouseClickChoice 增加 ChosenEntityIds 二次确认防止 false positive"
```

---

### Task 5: 最终验证与合并提交

- [ ] **Step 1: 全项目编译验证**

Run: `dotnet build`（整个解决方案）
Expected: 编译成功，无错误无警告

- [ ] **Step 2: 代码审查检查点**

验证以下要点：
- `ChoiceRecommendationConsumptionTracker.ShouldTreatAsConsumed` 参数类型匹配
- `_choiceRepeatedRecommendationCount` 在所有重置点都有重置
- `CaptureChoiceSnapshotChosen` 在获取失败时不会阻止正常的确认流程
- `continue` 在 `TryHandleChoice` 的 for 循环中行为正确（回到 chainedCount 循环顶部重新获取推荐）

- [ ] **Step 3: 合并提交**

```bash
git add BotMain/GameRecommendationProvider.cs BotMain/BotService.cs HearthstonePayload/ActionExecutor.cs
git commit -m "修复：选择手牌失败后释放消费状态并增强验证，防止无限卡住"
```
