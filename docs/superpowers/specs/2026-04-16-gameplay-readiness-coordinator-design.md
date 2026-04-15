# 统一交互就绪协调器重构设计

**日期**: 2026-04-16  
**范围**: 构筑对局动作链、留牌、发现/抉择、竞技场选牌  
**目标**: 在真正可操作前不发送脚本操作；一旦可操作立刻执行；统一等待逻辑，便于维护和调试

## 背景

上一版传统对战动画等待被整体删除后，脚本虽然获得了零延迟发送动作的能力，但也重新暴露了老问题：

- 动画、抽牌、输入锁、PowerProcessor、Zone 变更尚未完成时，动作可能被吃掉或失败
- 留牌、发现/抉择、竞技场选牌与对局动作使用不同节奏，等待策略分散
- 旧版等待逻辑虽然有真实游戏机制输入，但放行策略过粗，部分仍不可操作的状态会被提前绕过
- `BotService` 中等待、轮询、sleep 与业务逻辑耦合，后续调优和排障成本高

本次不恢复旧方案，而是重新设计一层“统一交互就绪协调器”。

## 设计目标

### 1. 成功率优先

所有交互必须在游戏真实可操作时才放行，避免因为动画、输入锁、布局未稳定导致操作失败。

### 2. 准备好后立刻执行

不再依赖固定大颗粒 sleep。改为短轮询和按场景放行，一旦 ready 立即发送命令。

### 3. 结构统一

把构筑动作、留牌、发现/抉择、竞技场选牌统一收敛到一个协调入口，调用方只声明“我要做哪类交互”，不再自己决定如何等待。

### 4. 可诊断

等待过程必须输出结构化日志，能够明确看到 scope、原因、轮询次数、耗时和超时点。

## 现状判断

### 可复用的底层机制

`HearthstonePayload.ActionExecutor.EvaluateGameReadyState()` 已经具备可靠的游戏内全局 readiness 判定能力。它会检查：

- `GameState.IsResponsePacketBlocked()`
- `InputManager.PermitDecisionMakingInput()`
- `GameState.IsBlockingPowerProcessor()`
- `PowerProcessor.IsRunning()`
- `ZoneMgr.HasActiveServerChange()` / `HasPendingServerChange()`
- 抽牌任务与回合开始抽牌计数
- 手牌布局更新状态
- `GameState.IsBusy()` 尾部宽限

这说明“真实游戏机制输入”并不缺，问题主要在 BotMain 如何编排和解释这些信号。

### 旧方案的问题

旧方案的问题不是读取的信号不真实，而是放行抽象不准确：

- 等待逻辑散落在多个业务分支
- 固定轮询参数偏粗
- `ActionPostReadyBypassReasons` 和 `TurnStartBypassReasons` 把部分仍不可操作的状态提前放行
- 非 `GAMEPLAY` 场景和 `GAMEPLAY` 场景没有清晰分层

因此本次重构只保留底层真实信号，不保留旧的放行白名单策略。

## 总体方案

新增一层统一协调器，暂定命名为 `InteractionReadinessCoordinator`。

### 职责

- 接收交互类型与上下文
- 决定使用哪种 gate
- 按 scope 轮询就绪状态
- 返回统一的 ready / busy / timeout 结果
- 记录诊断日志

### 非职责

- 不负责推荐逻辑
- 不负责动作发送
- 不重写 Payload 侧已有的交互提交实现
- 不替代现有的动作结果确认逻辑

## Gate 分层

### 1. GameplayGate

用于 `GAMEPLAY` 内交互，直接复用 `WAIT_READY_DETAIL` 作为全局输入锁真相源。

适用范围：

- 构筑动作链
- 留牌提交
- 发现/抉择提交

原则：

- 不再使用旧的 BotMain bypass 白名单作为放行依据
- 只要 Payload 判定为未 ready，就继续等待
- Payload 内部已经处理了 `GameState.IsBusy()` 的尾部宽限，BotMain 不再二次猜测

### 2. SceneGate

用于非 `GAMEPLAY` 场景交互，主要检查场景与目标 UI 是否稳定。

适用范围：

- 竞技场英雄选
- 竞技场卡牌选

原则：

- 不依赖 `WAIT_READY`
- 以场景稳定、弹窗阻塞状态、Draft 状态与选项数据是否就绪为准

## 四类交互规则

### A. ConstructedAction

作用对象：

- `PLAY`
- `ATTACK`
- `HERO_POWER`
- `USE_LOCATION`
- `TRADE`
- `OPTION`
- `END_TURN`

规则：

1. 发送动作前必须通过 `GameplayGate`
2. 动作发送后，如果后续还有连续动作，则进入短轮询 post-wait
3. post-wait 只负责等待“下一步可继续点击”，不再使用固定 300ms 等待
4. 如果动作后进入选择/发现链路，则由选择处理逻辑接管

效果：

- 动作不会在输入锁或动画未落定时抢跑
- 连招不会因为固定 sleep 变慢

### B. MulliganCommit

当前 `TryApplyMulligan()` 的问题是：拿到 `WAIT_READY` 的 `BUSY` 也继续读留牌状态，导致 ready 判断不够严格。

新规则：

1. 先等待 `GameplayGate == ready`
2. 再获取 `GET_MULLIGAN_STATE`
3. 仅在 `snapshot.Choices.Count > 0` 时允许提交
4. 若未 ready 或 choices 仍为空，则按留牌专用等待原因继续重试

效果：

- 不会在开场动画、发牌动画、输入尚未解锁时提前提交留牌

### C. ChoiceCommit

当前发现/抉择链路已经有 `ChoiceStateSnapshot.IsReady` 与提交确认逻辑，这部分保留。

新规则：

1. 等待 `ChoiceStateSnapshot.IsReady == true`
2. 同时等待 `GameplayGate == ready`
3. 两者同时满足后才允许 `APPLY_CHOICE`
4. 提交后继续复用现有 snapshot 变化确认机制

效果：

- 不会出现“选择界面已经显示，但输入仍被动画锁住”时过早提交
- 不重做现有可靠的选择确认流程

### D. ArenaDraftPick

竞技场选牌不在 `GAMEPLAY` 场景，因此必须走 `SceneGate`。

新规则：

1. 当前场景必须稳定为 `DRAFT`
2. 没有阻塞弹窗或加载过渡
3. `ARENA_GET_STATUS` 必须处于 `HERO_PICK` 或 `CARD_PICK`
4. 选项数据必须可读且数量合法
5. 满足后立即发送 `ARENA_PICK_HERO` 或 `ARENA_PICK_CARD`

效果：

- 不会在 DRAFT UI 尚未完整加载或状态错位时提前调用 `DraftManager.MakeChoice()`

## 协调器接口设计

建议定义统一请求与结果对象。

### ReadinessScope

- `ConstructedActionPre`
- `ConstructedActionPost`
- `MulliganCommit`
- `ChoiceCommit`
- `ArenaDraftPick`

### ReadinessRequest

- `Scope`
- `Action`
- `Scene`
- `SnapshotId`
- `TimeoutMs`
- `PollIntervalMs`
- `ExpectedArenaStatus`

### ReadinessResult

- `IsReady`
- `Scope`
- `Reason`
- `Detail`
- `Polls`
- `ElapsedMs`
- `TimedOut`

## 超时与轮询策略

不再使用统一的 `30 * 300ms`。

建议初始参数如下：

### ConstructedActionPre

- 轮询间隔: `40~60ms`
- 预算: `1200~1800ms`

### ConstructedActionPost

- 轮询间隔: `40~60ms`
- 预算: `600~1200ms`

### MulliganCommit

- 轮询间隔: `80~120ms`
- 预算: `3000~5000ms`

### ChoiceCommit

- 轮询间隔: `60~100ms`
- 预算: `2000~3000ms`

### ArenaDraftPick

- 轮询间隔: `100~150ms`
- 预算: `3000~5000ms`

这些参数不写死在业务分支里，而集中由协调器配置。

## 超时后的恢复策略

### ConstructedActionPre 超时

- 不发送动作
- 回到主循环刷新 seed / board
- 允许重新推荐

### ConstructedActionPost 超时

- 不立即判定动作失败
- 回主循环重新读取棋盘

### MulliganCommit 超时

- 保留现有留牌重试节奏
- 明确日志区分：
  - `gameplay_not_ready`
  - `mulligan_state_timeout`
  - `mulligan_choices_empty`

### ChoiceCommit 超时

- 保留 pending choice
- 不清空选择状态
- 下一轮继续等待

### ArenaDraftPick 超时

- 重新获取 `ARENA_GET_STATUS`
- 若状态错位，重走 `ArenaEnsureDraftScene`

## 日志设计

统一输出一类 readiness 日志，例如：

- `scope`
- `ready/busy/timeout`
- `reason`
- `detail`
- `polls`
- `elapsedMs`

价值：

- 能区分是全局输入锁、选择未 ready、留牌数据为空，还是竞技场 DRAFT UI 未稳定
- 后续若仍有“点早了”或“等久了”，日志能直接定位是哪一层 gate 问题

## 接入点

### `BotMain/BotService.cs`

主要接入点：

- 构筑主循环动作发送前后
- `TryApplyMulligan()`
- `TryHandlePendingInteractiveSelectionBeforePlanning()` / `TryHandleChoice()`
- `ArenaPickHero()` / `ArenaPickCard()`

### `HearthstonePayload`

本次尽量少改 Payload。

保留：

- `WAIT_READY`
- `WAIT_READY_DETAIL`
- 已有的 choice / mulligan / arena 提交实现

如需补充，只增加少量 Draft 场景就绪辅助查询，不引入新的复杂状态机。

## 测试策略

### 单元测试

新增或调整测试覆盖：

- `GameplayGate` 对 busy / ready / timeout 的判定
- `MulliganCommit` 不能把 `BUSY` 视为成功
- `ChoiceCommit` 必须同时满足 choice ready 与 gameplay ready
- `ArenaDraftPick` 在 `NO_DRAFT` / `DRAFT_COMPLETE` / `REWARDS` 时不得放行

### 集成回归

重点验证：

1. 构筑连招在动画结束后立即继续，不再靠固定 sleep
2. 留牌不会在发牌动画未结束时提前提交
3. Discover / Choose One 不会在 UI 已出现但输入仍锁住时提交失败
4. 竞技场英雄选和卡牌选不会打在空白或错误阶段

### 日志回归

确认所有 readiness 日志至少包含：

- scope
- reason
- polls
- elapsedMs

## 风险与取舍

### 风险

- 构筑 post-wait 参数过紧可能导致偶发 timeout
- 竞技场 DRAFT 场景的 UI 稳定判定若过宽，仍可能提早调用 `MakeChoice`
- 保守等待会轻微影响极端快节奏场景

### 取舍

本次优先级明确为：

1. 成功率
2. 速度
3. 结构统一

因此初版宁可略保守，也不接受再次出现“明明不可操作却提前点击”的设计。

## 不做的事

- 不恢复旧版 `ConstructedActionReady` 子系统
- 不回到散落的 `WaitForGameReady + bypass reason` 方案
- 不把所有交互改成事件驱动
- 不在本次同时重写推荐、动作确认或 Scene 导航系统

## 结论

本次重构采用“统一协调器 + GameplayGate / SceneGate 双层模型”：

- 对局内交互信任 Payload 的真实游戏就绪信号
- 非对局场景交互采用场景稳定和业务状态联合判定
- 所有等待统一入口、统一日志、统一超时策略
- 所有交互都满足“不能早点、能点立刻点”的目标

后续实现阶段应先补测试，再按接入点逐步替换现有散装等待逻辑。
