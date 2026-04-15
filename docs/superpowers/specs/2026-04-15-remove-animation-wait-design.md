# 移除传统对战动画等待逻辑

**日期**: 2026-04-15
**范围**: 构筑/竞技场/休闲（传统对战），酒馆战棋不动

## 目标

删除传统对战中所有动画等待逻辑，让脚本在获取到盒子推荐后零延迟连续执行所有操作命令。空推荐时直接结束回合。后续由用户重写这块逻辑。

## 设计决策

- **方案选择**: 方案A——直接删除等待代码，不留开关、不设参数归零
- **动画等待**: 前置 `WaitForGameReady` 和后置 `WaitForGameReady` 全部移除
- **推荐轮询**: 保留轮询机制，间隔改为200ms，超时改为10秒
- **空推荐**: 直接发送 END_TURN 结束回合
- **动作间隔**: 零延迟

## 改动清单

### 1. HsBoxRecommendationProvider.cs

- 轮询间隔从180ms改为200ms
- 超时上限从2600ms改为10000ms
- 删除旧的 `_actionWaitTimeoutMs`（2600ms）和 `_actionPollIntervalMs`（180ms）字段，替换为新值

### 2. BotService.cs

- 删除传统对战路径中所有 `WaitForGameReady` 调用点（约11处）：
  - 构筑 TurnStart（行2685）
  - 构筑 ActionPreReady + Fallback（行2978-2990）
  - 构筑 ActionPostReady + Fallback（行3311-3323）
  - 构筑 重新模拟后（行3378）
  - 竞技场 TurnStart（行4045）
  - 竞技场 ActionPreReady + Fallback（行4161-4172）
  - 竞技场 ActionPostReady + Fallback（行4338-4343）
  - 选择处理后（行7529）
  - ConcedeWhenLethal（行8787）
- 空推荐时直接发送 END_TURN

### 3. ConstructedActionReadyEvaluator.cs

- 整个文件删除

### 4. ActionExecutor.cs

- 删除对 `ConstructedActionReadyEvaluator` 的调用（约2处：行8277、行8392）

### 5. BotCore.Tests/ConstructedActionReadyEvaluatorTests.cs

- 整个测试文件删除

## 保留不动的部分

- `WaitForGameReady` 方法本身（战棋使用：行5313、5379、5578）
- `IsGameReady` / `EvaluateGameReadyState`（战棋通过 `_isGameReady` 委托间接使用）
- `ShouldBypassReadyWait` / `ResolveReadyWaitReason`（在 `WaitForGameReady` 内部）
- 酒馆战棋所有逻辑（`BgExecutionGate`、`BgActionReadyEvaluator` 等）

## 执行流程对比

### 改造前
```
获取推荐(180ms轮询/2600ms超时)
  → 前置WaitForGameReady(300ms×30)
    → 发送命令
      → 后置WaitForGameReady(300ms×30)
        → 下一个动作...
```

### 改造后
```
获取推荐(200ms轮询/10s超时)
  → 直接发送命令
    → 直接发送下一个命令
      → ...
        → 回合结束
空推荐 → 直接END_TURN
```
