# 攻击分类系统（FACE/MINION）实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 引入攻击分类标签（FACE/MINION），让打脸攻击跳过不必要的等待，将间隔从 ~1.2s 压至 ~0.4s，打随从保持完整稳定性。

**Architecture:** BotService 在发送 ATTACK 命令前根据 planningBoard 判断目标是否为敌方英雄，拼接分类标签到命令第4字段。ActionExecutor 解析标签后走不同时序参数路径。两层独立改动，通过协议格式 `ATTACK|id|id|FACE/MINION` 解耦。

**Tech Stack:** C# / .NET 8 / xUnit

**Spec:** `docs/superpowers/specs/2026-04-15-attack-classification-design.md`

---

### File Map

| 文件 | 职责 | 改动类型 |
|------|------|---------|
| `BotMain/BotService.cs` | 分类注入 + pre-ready/choice-probe/post-ready 按分类选参 | 修改 |
| `HearthstonePayload/ActionExecutor.cs` | 解析第4字段 + FACE 快速路径 + MINION 标准路径 | 修改 |
| `BotCore.Tests/AttackClassificationTests.cs` | 分类注入与确认逻辑的单元测试 | 新建 |

---

### Task 1: ActionExecutor — FACE 快速路径

**Files:**
- Modify: `HearthstonePayload/ActionExecutor.cs:1266-1485`（ATTACK case 分支）

- [ ] **Step 1: 修改 ATTACK 分支，解析第4字段为 FACE/MINION**

在 `ActionExecutor.cs` 的 `case "ATTACK":` 分支中，将现有的 CHAIN 解析替换为 FACE/MINION 分类解析，并根据分类走不同路径。

找到这段代码（约1266-1271行）：

```csharp
                case "ATTACK":
                    {
                        int attackerId = int.Parse(parts[1]);
                        int targetId = int.Parse(parts[2]);
                        bool isChainAttack = parts.Length > 3
                            && string.Equals(parts[3], "CHAIN", StringComparison.OrdinalIgnoreCase);
```

替换为：

```csharp
                case "ATTACK":
                    {
                        int attackerId = int.Parse(parts[1]);
                        int targetId = int.Parse(parts[2]);
                        bool isChainAttack = parts.Length > 3
                            && string.Equals(parts[3], "CHAIN", StringComparison.OrdinalIgnoreCase);
                        bool isFaceAttack = parts.Length > 3
                            && string.Equals(parts[3], "FACE", StringComparison.OrdinalIgnoreCase);
```

- [ ] **Step 2: 在 CHAIN 路径之后、标准路径之前插入 FACE 快速路径**

找到 CHAIN 路径结束后的标准路径注释（约1307行）：

```csharp
                        // ── 标准路径（首次攻击 / 非连续攻击） ──
                        bool sourceIsFriendlyHero = false;
                        bool targetIsEnemyHero = false;
                        const int attackConfirmDeadlineMs = 800;
                        const int attackConfirmSleepMs = 25;
```

在这行之前插入 FACE 快速路径：

```csharp
                        // ── FACE 快速路径 ──
                        // 目标是敌方英雄：跳过 ReadGameState（~570ms），
                        // 用轻量反射判断英雄身份，确认截止 200ms。
                        if (isFaceAttack)
                        {
                            var faceSourceHero = IsFriendlyHeroEntityId(attackerId);
                            var faceTargetHero = IsEnemyHeroEntityId(targetId);

                            // 反射失败时回退到标准路径
                            if (!faceTargetHero)
                            {
                                AppendActionTrace(
                                    "ATTACK face_fallback attacker=" + attackerId
                                    + " target=" + targetId
                                    + " reason=target_not_enemy_hero");
                                goto standardAttackPath;
                            }

                            var faceMouseSw = Stopwatch.StartNew();
                            var faceResult = _coroutine.RunAndWait(
                                MouseAttack(attackerId, targetId, faceSourceHero, true));
                            faceMouseSw.Stop();
                            var faceMouseMs = faceMouseSw.ElapsedMilliseconds;

                            if (!faceResult.StartsWith("OK:", StringComparison.OrdinalIgnoreCase))
                            {
                                AppendActionTrace(
                                    "ATTACK face_mouse_fail attacker=" + attackerId
                                    + " target=" + targetId
                                    + " mouseMs=" + faceMouseMs
                                    + " result=" + faceResult);
                                return AppendAttackTimingToResult(
                                    faceResult, 1, faceMouseMs, 0, 0, "face_mouse_failed");
                            }

                            // 缩短确认轮询：200ms 截止
                            const int faceConfirmDeadlineMs = 200;
                            const int faceConfirmSleepMs = 25;
                            GameStateData faceBeforeState = null;
                            AttackStateSnapshot faceBeforeSnapshot = default;
                            var hasFaceBeforeSnapshot = false;
                            try
                            {
                                faceBeforeState = reader?.ReadGameState();
                                hasFaceBeforeSnapshot = TryCaptureAttackState(
                                    faceBeforeState, attackerId, targetId, out faceBeforeSnapshot);
                            }
                            catch { }

                            if (!hasFaceBeforeSnapshot)
                            {
                                AppendActionTrace(
                                    "ATTACK face_ok_no_confirm attacker=" + attackerId
                                    + " target=" + targetId
                                    + " mouseMs=" + faceMouseMs);
                                return AppendAttackTimingToResult(
                                    faceResult, 1, faceMouseMs, 0, 0, "face_no_before_snapshot");
                            }

                            var faceConfirmSw = Stopwatch.StartNew();
                            var faceConfirmPolls = 0;
                            var faceConfirmReason = "unchanged";
                            while (faceConfirmSw.ElapsedMilliseconds < faceConfirmDeadlineMs)
                            {
                                Thread.Sleep(faceConfirmSleepMs);
                                faceConfirmPolls++;
                                var faceAfterState = reader?.ReadGameState();
                                if (!TryCaptureAttackState(faceAfterState, attackerId, targetId, out var faceAfterSnapshot))
                                {
                                    faceConfirmReason = "after_snapshot_missing";
                                    continue;
                                }

                                var faceApplyObs = GetAttackApplyObservation(faceBeforeSnapshot, faceAfterSnapshot);
                                faceConfirmReason = faceApplyObs.Reason;
                                if (faceApplyObs.Applied)
                                {
                                    faceConfirmSw.Stop();
                                    AppendActionTrace(
                                        "ATTACK face_confirm_ok attacker=" + attackerId
                                        + " target=" + targetId
                                        + " mouseMs=" + faceMouseMs
                                        + " confirmMs=" + faceConfirmSw.ElapsedMilliseconds
                                        + " confirmPolls=" + faceConfirmPolls
                                        + " apply=" + faceConfirmReason);
                                    return AppendAttackTimingToResult(
                                        faceResult, 1, faceMouseMs,
                                        faceConfirmSw.ElapsedMilliseconds, faceConfirmPolls,
                                        faceConfirmReason);
                                }
                            }

                            faceConfirmSw.Stop();
                            AppendActionTrace(
                                "ATTACK face_confirm_timeout attacker=" + attackerId
                                + " target=" + targetId
                                + " mouseMs=" + faceMouseMs
                                + " confirmMs=" + faceConfirmSw.ElapsedMilliseconds
                                + " confirmPolls=" + faceConfirmPolls
                                + " apply=" + faceConfirmReason);
                            return AppendAttackTimingToResult(
                                "FAIL:ATTACK:not_confirmed:" + attackerId,
                                1, faceMouseMs,
                                faceConfirmSw.ElapsedMilliseconds, faceConfirmPolls,
                                "face_confirm_timeout");
                        }

                        standardAttackPath:
```

注意 `standardAttackPath:` 标签加在现有 `// ── 标准路径` 注释之前。

- [ ] **Step 3: 验证构建**

运行：
```bash
cd "H:\桌面\炉石脚本\Hearthbot" && dotnet build HearthstonePayload/HearthstonePayload.csproj
```

预期：无编译错误。

- [ ] **Step 4: 提交**

```bash
git add HearthstonePayload/ActionExecutor.cs
git commit -m "ActionExecutor: 添加 FACE 攻击快速路径（跳过ReadGameState，确认200ms）"
```

---

### Task 2: BotService — 分类注入（命令拼接）

**Files:**
- Modify: `BotMain/BotService.cs:2909-2986`（action 解析与 commandToSend 构建）

- [ ] **Step 1: 在 action 标志位解析区域新增 isFaceAttack 判断**

找到这段代码（约2909-2912行）：

```csharp
                            bool isAttack = action.StartsWith("ATTACK|", StringComparison.OrdinalIgnoreCase);
                            bool isTrade = action.StartsWith("TRADE|", StringComparison.OrdinalIgnoreCase);
                            bool isEndTurn = action.StartsWith("END_TURN", StringComparison.OrdinalIgnoreCase);
                            bool isOption = action.StartsWith("OPTION|", StringComparison.OrdinalIgnoreCase);
```

在 `isOption` 之后添加：

```csharp
                            bool isFaceAttack = false;
                            if (isAttack && planningBoard?.HeroEnemy != null)
                            {
                                var attackParts = action.Split('|');
                                if (attackParts.Length > 2
                                    && int.TryParse(attackParts[2], out var attackTargetId)
                                    && attackTargetId == planningBoard.HeroEnemy.Id)
                                {
                                    isFaceAttack = true;
                                }
                            }
```

- [ ] **Step 2: 在 commandToSend 构建处拼接分类标签**

找到这行（约2986行）：

```csharp
                            var commandToSend = action;
```

替换为：

```csharp
                            var commandToSend = action;
                            if (isAttack)
                            {
                                commandToSend = isFaceAttack
                                    ? action + "|FACE"
                                    : action + "|MINION";
                            }
```

- [ ] **Step 3: 验证构建**

运行：
```bash
cd "H:\桌面\炉石脚本\Hearthbot" && dotnet build BotMain/BotMain.csproj
```

预期：无编译错误。

- [ ] **Step 4: 提交**

```bash
git add BotMain/BotService.cs
git commit -m "BotService: 根据planningBoard注入FACE/MINION攻击分类标签"
```

---

### Task 3: BotService — FACE 攻击缩短 Pre-ready 等待

**Files:**
- Modify: `BotMain/BotService.cs:2934-2981`（pre-ready 等待分支）

- [ ] **Step 1: 在 pre-ready 等待逻辑中为 FACE 攻击使用更短参数**

找到这段代码（约2934-2966行）：

```csharp
                            if (isOption)
                            {
                                preReadyStatus = "skipped_option";
                            }
                            else
                            {
                                var preReadySw = Stopwatch.StartNew();
                                var preReadyOk = false;
                                ConstructedActionReadyState constructedPreReadyState = null;
                                if (ShouldUseConstructedActionReadyWait(action))
                                {
                                    preReadyOk = WaitForConstructedActionReady(pipe, action, 15, 20, readyTimeoutMs, out constructedPreReadyState);
```

替换为：

```csharp
                            if (isOption)
                            {
                                preReadyStatus = "skipped_option";
                            }
                            else
                            {
                                var preReadySw = Stopwatch.StartNew();
                                var preReadyOk = false;
                                ConstructedActionReadyState constructedPreReadyState = null;
                                // FACE 攻击：缩短 pre-ready 等待（maxPolls=5, interval=15ms, 上限75ms）
                                var constructedPreReadyMaxPolls = isFaceAttack ? 5 : 15;
                                var constructedPreReadyIntervalMs = isFaceAttack ? 15 : 20;
                                if (ShouldUseConstructedActionReadyWait(action))
                                {
                                    preReadyOk = WaitForConstructedActionReady(pipe, action, constructedPreReadyMaxPolls, constructedPreReadyIntervalMs, readyTimeoutMs, out constructedPreReadyState);
```

- [ ] **Step 2: 验证构建**

运行：
```bash
cd "H:\桌面\炉石脚本\Hearthbot" && dotnet build BotMain/BotMain.csproj
```

- [ ] **Step 3: 提交**

```bash
git add BotMain/BotService.cs
git commit -m "BotService: FACE攻击缩短pre-ready等待参数（5×15ms）"
```

---

### Task 4: BotService — FACE 攻击跳过 Choice Probe 和 Post-ready

**Files:**
- Modify: `BotMain/BotService.cs:3213-3277`（choice probe + post-ready 区域）

- [ ] **Step 1: 在 skipPostActionReadyWait 分支后增加 FACE 跳过逻辑**

找到这段代码（约3213-3228行）：

```csharp
                            if (skipPostActionReadyWait)
                            {
                                postReadyStatus = "skipped_attack_no_minion_death";
                                if (choiceWatchArmed)
                                    ClearChoiceStateWatch("attack_fast_track");
                            }
                            else if (nextIsOption)
                            {
```

替换为：

```csharp
                            if (skipPostActionReadyWait)
                            {
                                postReadyStatus = "skipped_attack_no_minion_death";
                                if (choiceWatchArmed)
                                    ClearChoiceStateWatch("attack_fast_track");
                            }
                            else if (isFaceAttack)
                            {
                                // FACE 攻击跳过 choice probe 和 post-ready 等待
                                // 英雄目标不改变棋盘布局，无需等待
                                postReadyStatus = "skipped_face_attack";
                                if (choiceWatchArmed)
                                    ClearChoiceStateWatch("face_attack_fast_track");
                            }
                            else if (nextIsOption)
                            {
```

- [ ] **Step 2: 验证构建**

运行：
```bash
cd "H:\桌面\炉石脚本\Hearthbot" && dotnet build BotMain/BotMain.csproj
```

- [ ] **Step 3: 提交**

```bash
git add BotMain/BotService.cs
git commit -m "BotService: FACE攻击跳过choice probe和post-ready等待"
```

---

### Task 5: 单元测试 — 分类注入与确认逻辑

**Files:**
- Create: `BotCore.Tests/AttackClassificationTests.cs`

- [ ] **Step 1: 编写测试文件**

创建 `BotCore.Tests/AttackClassificationTests.cs`：

```csharp
using BotMain;
using System;
using System.Collections.Generic;
using Xunit;

namespace BotCore.Tests
{
    public class AttackClassificationTests
    {
        [Fact]
        public void ShouldFastTrackSuccessfulAttack_ReturnsTrue_ForFaceAttackNoMinionDeath()
        {
            var before = new BotService.ActionStateSnapshot
            {
                HandCount = 5,
                ManaAvailable = 0,
                FriendMinionCount = 3,
                EnemyMinionCount = 2,
                FriendMinionEntityIds = new[] { 10, 11, 12 },
                EnemyMinionEntityIds = new[] { 20, 21 }
            };
            var after = new BotService.ActionStateSnapshot
            {
                HandCount = 5,
                ManaAvailable = 0,
                FriendMinionCount = 3,
                EnemyMinionCount = 2,
                FriendMinionEntityIds = new[] { 10, 11, 12 },
                EnemyMinionEntityIds = new[] { 20, 21 }
            };

            // FACE 标签的攻击命令也能通过 ShouldFastTrackSuccessfulAttack
            var result = BotService.ShouldFastTrackSuccessfulAttack(
                "ATTACK|10|1|FACE", true, before, after);

            Assert.True(result);
        }

        [Fact]
        public void ShouldFastTrackSuccessfulAttack_ReturnsFalse_ForMinionAttackWithDeath()
        {
            var before = new BotService.ActionStateSnapshot
            {
                HandCount = 5,
                ManaAvailable = 0,
                FriendMinionCount = 3,
                EnemyMinionCount = 2,
                FriendMinionEntityIds = new[] { 10, 11, 12 },
                EnemyMinionEntityIds = new[] { 20, 21 }
            };
            var after = new BotService.ActionStateSnapshot
            {
                HandCount = 5,
                ManaAvailable = 0,
                FriendMinionCount = 3,
                EnemyMinionCount = 1,
                FriendMinionEntityIds = new[] { 10, 11, 12 },
                EnemyMinionEntityIds = new[] { 20 }
            };

            var result = BotService.ShouldFastTrackSuccessfulAttack(
                "ATTACK|10|21|MINION", true, before, after);

            Assert.False(result);
        }

        [Fact]
        public void ResolveActionEffectConfirmation_FaceAttackSetsSkipPostReady_WhenNoMinionDeath()
        {
            var before = new BotService.ActionStateSnapshot
            {
                HandCount = 5,
                ManaAvailable = 0,
                FriendMinionCount = 2,
                EnemyMinionCount = 1,
                FriendMinionEntityIds = new[] { 10, 11 },
                EnemyMinionEntityIds = new[] { 20 }
            };
            var after = new BotService.ActionStateSnapshot
            {
                HandCount = 5,
                ManaAvailable = 0,
                FriendMinionCount = 2,
                EnemyMinionCount = 1,
                FriendMinionEntityIds = new[] { 10, 11 },
                EnemyMinionEntityIds = new[] { 20 }
            };

            var result = BotService.ResolveActionEffectConfirmation(
                useHsBoxPayloadConfirmation: true,
                hsBoxAdvanceConfirmed: false,
                actionReportedSuccess: true,
                action: "ATTACK|10|1|FACE",
                before: before,
                after: after);

            Assert.True(result.MarkTurnHadEffectiveAction);
            Assert.True(result.SkipPostActionReadyWait);
            Assert.Equal("attack_no_minion_death", result.Reason);
        }

        [Fact]
        public void ResolveActionEffectConfirmation_DefaultsToMinion_WhenNoClassification()
        {
            var before = new BotService.ActionStateSnapshot
            {
                HandCount = 5,
                ManaAvailable = 0,
                FriendMinionCount = 2,
                EnemyMinionCount = 1,
                FriendMinionEntityIds = new[] { 10, 11 },
                EnemyMinionEntityIds = new[] { 20 }
            };
            var after = new BotService.ActionStateSnapshot
            {
                HandCount = 5,
                ManaAvailable = 0,
                FriendMinionCount = 2,
                EnemyMinionCount = 1,
                FriendMinionEntityIds = new[] { 10, 11 },
                EnemyMinionEntityIds = new[] { 20 }
            };

            // 无第4字段 → 仍应正常工作（按 MINION 处理）
            var result = BotService.ResolveActionEffectConfirmation(
                useHsBoxPayloadConfirmation: true,
                hsBoxAdvanceConfirmed: false,
                actionReportedSuccess: true,
                action: "ATTACK|10|1",
                before: before,
                after: after);

            Assert.True(result.MarkTurnHadEffectiveAction);
            // 无分类时也应 fast-track（因为 ShouldFastTrackSuccessfulAttack 只看 ATTACK| 前缀和随从变化）
            Assert.True(result.SkipPostActionReadyWait);
        }
    }
}
```

- [ ] **Step 2: 运行测试验证通过**

运行：
```bash
cd "H:\桌面\炉石脚本\Hearthbot" && dotnet test BotCore.Tests --filter "AttackClassification" -v n
```

预期：4 tests passed。

- [ ] **Step 3: 提交**

```bash
git add BotCore.Tests/AttackClassificationTests.cs
git commit -m "测试: 添加攻击分类系统单元测试"
```

---

### Task 6: 全量构建验证与最终提交

- [ ] **Step 1: 全量构建**

运行：
```bash
cd "H:\桌面\炉石脚本\Hearthbot" && dotnet build
```

预期：无编译错误。

- [ ] **Step 2: 全量测试**

运行：
```bash
cd "H:\桌面\炉石脚本\Hearthbot" && dotnet test BotCore.Tests -v n
```

预期：所有测试通过（包括新增的 AttackClassification 测试和已有测试）。

- [ ] **Step 3: 检查日志输出**

确认 FACE/MINION 分类在 ActionTrace 日志中正确输出。搜索代码确认所有新增的 `AppendActionTrace` 调用包含分类标识：

```bash
grep -n "face_" HearthstonePayload/ActionExecutor.cs
```

预期：看到 `face_fallback`、`face_mouse_fail`、`face_ok_no_confirm`、`face_confirm_ok`、`face_confirm_timeout` 等 trace 标签。

- [ ] **Step 4: 推送**

```bash
git push
```
