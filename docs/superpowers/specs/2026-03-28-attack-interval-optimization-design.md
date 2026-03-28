# 连续攻击间隔优化

## 问题

当 hsbox 推荐逐条返回攻击动作（每批 count=1）时，连续攻击之间存在约 900ms 的冗余等待：
- `postDelayMs` ~210ms（拟人化延迟，因单动作批次 `nextIsAttack=false` 走慢速路径）
- `Thread.Sleep(120)` 批次间固定等待
- 主循环准备开销 ~575ms（`ResolveDeckContext`、`RefreshFriendlyEntityContext` 等冗余查询）

根因：chain-attack 快速路径仅在**同一批次内** `nextIsAttack=true` 时触发，单动作批次永远无法命中。

## 方案

利用已有的 `lastRecommendationWasAttackOnly` 标识实现**跨批次连续攻击感知**，在三个阶段消除冗余等待。

### A) Post-Action 阶段（BotService.cs ~行2644-2696）

**当前逻辑：**
- `isAttack && nextIsAttack` → chain-attack 快速轮询 `WaitForGameReady(40, 50)`
- 否则 → `SleepOrCancelled(actionDelayMs)` + `TryProbePendingChoiceAfterAction` + `WaitForGameReady(30, 300)`

**改为：**
- `isAttack && (nextIsAttack || lastRecommendationWasAttackOnly)` → chain-attack 快速轮询
- 即单动作攻击批次在上一批也是纯攻击时，走快速路径
- 跳过 `actionDelayMs` 睡眠和 `TryProbePendingChoiceAfterAction`（纯攻击不触发抉择）

### B) 批次间等待（BotService.cs ~行2790-2796）

**当前逻辑：**
```csharp
Thread.Sleep(120);
continue;
```

**改为：**
```csharp
if (!lastRecommendationWasAttackOnly)
    Thread.Sleep(120);
continue;
```

当上一批是纯攻击时，跳过 120ms 等待直接进入下一循环。

### C) 主循环准备阶段（BotService.cs ~行2370-2437）

**当前逻辑：** 每次循环都执行 `ResolveDeckContext`、`RefreshFriendlyEntityContext`、`TryRunHumanizedTurnPrelude`。

**改为：** 当 `lastRecommendationWasAttackOnly && planningBoard != null` 时（与已有的 board recovery 跳过条件一致），跳过：
- `ResolveDeckContext` — 攻击不改变牌组状态
- `RefreshFriendlyEntityContext` — 攻击阶段友方实体信息无需刷新
- `TryRunHumanizedTurnPrelude` — 已有 `_lastHumanizedTurnNumber` 去重，但跳过可省去函数调用开销

直接复用上次的 `_currentDeckContext` 和 `friendlyEntities` 构建 action request。

## 优化效果

| 阶段 | 当前 | 优化后 |
|------|------|--------|
| postDelay | ~210ms | 0ms（走快速路径） |
| choiceProbe | ~0ms | 跳过 |
| postReady | ~0ms | ~50ms（快速轮询） |
| Thread.Sleep(120) | 120ms | 0ms |
| 主循环准备 | ~575ms | ~10ms |
| **合计间隔开销** | **~900ms** | **~60ms** |

## 安全边界

- 仅在 `lastRecommendationWasAttackOnly` 为 true 时生效，PLAY/HERO_POWER/END_TURN 等动作不受影响
- 快速轮询保留 `WaitForGameReady` 检查，确保游戏状态就绪
- 快速轮询超时上限 40×50ms=2s，不会卡死
- `lastRecommendationWasAttackOnly` 在每批动作执行后重新计算，非攻击批次自动退出快速通道

## 涉及文件

- `BotMain/BotService.cs` — 主循环 post-action 逻辑、批次间等待、准备阶段跳过
