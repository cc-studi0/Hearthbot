# 攻击分类系统设计

**日期**：2026-04-15
**状态**：已确认

## 目标

引入攻击分类系统（FACE/MINION），消除打脸场景下的多层串行等待，将打脸间隔从 ~1.2-2.0s 压至 ~0.4s，同时保持打随从的完整稳定性。

## 问题分析

当前每次攻击操作经过以下串行等待：

| 阶段 | 打脸耗时 | 打随从耗时 |
|------|---------|-----------|
| Pre-ready 等待 | ~100-300ms | ~100-300ms |
| ReadGameState | ~570ms | ~570ms |
| 鼠标操作 | ~300ms | ~400ms |
| 确认轮询 | ~200-800ms | ~200-800ms |
| Choice probe | ~50-200ms | ~50-200ms |
| Post-ready 等待 | 已跳过* | ~500-2000ms |
| **总计** | **~1220-2170ms** | **~1420-4270ms** |

\* `ShouldFastTrackSuccessfulAttack` 在无随从死亡时跳过 post-ready，打脸通常命中此条件。

打脸场景中最大的两个瓶颈：**ReadGameState（~570ms）** 和 **确认轮询（800ms 上限）**。这两项对打脸而言并非必要——英雄位置固定、英雄 ID 可用轻量反射获取、血量变化在游戏状态中几乎即时反映。

## 分类定义

| 分类 | 含义 | 触发条件 |
|------|------|---------|
| `FACE` | 攻击敌方英雄 | 目标 entityId 是敌方英雄 |
| `MINION` | 攻击敌方随从 | 目标 entityId 是敌方随从 |

两个分类足够——分类判断不依赖 ATK/HP 预测，100% 可靠。未来需要细分时只需扩展枚举。

## 协议格式

```
当前:   ATTACK|attackerId|targetId
新增:   ATTACK|attackerId|targetId|FACE
        ATTACK|attackerId|targetId|MINION
缺省:   无第4字段 → 按 MINION（保守）处理
```

复用现有第4字段位置（原 CHAIN 标记位，已计划移除）。

## 时序参数

### ActionExecutor 层（HearthstonePayload/ActionExecutor.cs）

| 参数 | FACE | MINION |
|------|------|--------|
| ReadGameState | 跳过，用轻量反射 `IsFriendlyHeroEntityId`/`IsEnemyHeroEntityId` | 完整调用 |
| 目标定位重试 | 无稳定性校验（英雄位置固定），首次命中即用 | 连续2次偏差 ≤12px |
| 确认轮询截止 | 200ms | 800ms |
| 确认轮询间隔 | 25ms | 25ms |

### BotService 层（BotMain/BotService.cs）

| 参数 | FACE | MINION |
|------|------|--------|
| Pre-ready 等待 | `WaitForConstructedActionReady` maxPolls=5, interval=15ms（上限75ms） | maxPolls=15, interval=20ms |
| Choice probe | 跳过 | 完整探测 |
| Post-ready 等待 | 跳过 | 完整等待 |
| 下一动作 Pre-ready | 若下一动作也是 FACE → FACE 参数；否则正常 | 正常 |

### 预期耗时

| 阶段 | FACE（优化后） | MINION（不变） |
|------|--------------|---------------|
| Pre-ready | ~30-75ms | ~100-300ms |
| ReadGameState | 0ms | ~570ms |
| 鼠标操作 | ~300ms | ~400ms |
| 确认轮询 | ~50-200ms | ~200-800ms |
| Choice probe | 0ms | ~50-200ms |
| Post-ready | 0ms | ~500-2000ms |
| **总计** | **~380-575ms** | ~1420-4270ms |

## 分类注入点与数据流

### 分类产生位置

BotService 在发送 ATTACK 命令前持有 `planningBoard`，可直接判断目标是英雄还是随从：

```
BotService 构建命令时（~2986行）：
  原始: ATTACK|attackerId|targetId
  → 查 planningBoard: targetId == board.HeroEnemy.EntityId ?
  → 是 → ATTACK|attackerId|targetId|FACE
  → 否 → ATTACK|attackerId|targetId|MINION
```

### 数据流

```
BotService                          ActionExecutor (Payload)
    │                                       │
    │  分类判断 (planningBoard)              │
    │  拼接 ATTACK|id|id|FACE               │
    │  ─── Pre-ready (按分类选参) ───→       │
    │  ─── SendActionCommand ──────→  解析第4字段
    │                                  │ FACE → 跳过ReadGameState
    │                                  │        轻量反射判英雄
    │                                  │        确认截止200ms
    │                                  │ MINION → 完整标准路径
    │  ←── 结果返回 ───────────────    │
    │  Post-action确认 (按分类选参)          │
    │  Choice probe (FACE跳过)              │
    │  Post-ready (FACE跳过)                │
```

## 改动范围

| 文件 | 改动 |
|------|------|
| `BotMain/BotService.cs` | 分类注入（~2986行）、pre-ready/choice-probe/post-ready 按分类选参 |
| `HearthstonePayload/ActionExecutor.cs` | 解析第4字段（~1266行），FACE 路径跳过 ReadGameState + 缩短确认轮询 |

## 边界情况与容错

### FACE 路径失败处理

| 场景 | 处理方式 |
|------|---------|
| 轻量反射判断英雄ID失败 | 回退到 MINION 标准路径 |
| 200ms 确认超时 | 返回 `FAIL:ATTACK:not_confirmed`，BotService 按现有逻辑处理（soft failure） |
| `GetHeroScreenPos` 失败 | 与现有逻辑一致：5次重试，全部失败返回 FAIL |
| planningBoard 为 null | 不拼接分类标签 → 按 MINION 处理 |

### 特殊场景

| 场景 | 说明 |
|------|------|
| 奥秘（冰冻/误导） | 确认检查英雄血量变化，奥秘阻止伤害 → not_confirmed → 重新读棋盘，自然修正 |
| FACE 后接 MINION | FACE 的 post-ready 被跳过，下一动作 pre-ready 用 MINION 参数等待布局稳定 |
| 连续多个 FACE | 最佳场景：每次 ~0.4s，3次 ~1.2s |

### 不改的东西

- `ShouldFastTrackSuccessfulAttack` 保留（MINION 路径下仍有价值）
- `WaitForConstructedActionReady` 评估器本身不改（只改调用参数）
- CHAIN 路径代码本次不删除（按原设计文档另行处理）
- 战旗模式不受影响（走独立 Arena 分支）

## 风险

主要风险是 FACE 快速路径在奥秘场景下可能需要一次额外的 soft failure + 重试。这是可接受的，因为：
1. 奥秘触发频率低
2. 重试走标准恢复流程，不丢操作
3. 正常打脸场景获得 ~70% 的速度提升
