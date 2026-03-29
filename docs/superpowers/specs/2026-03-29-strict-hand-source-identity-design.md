# 炉石手牌源牌严格身份校验设计

日期：2026-03-29

## 1. 背景

当前项目已经具备完整的构筑与战旗执行链路：

- `BotMain/HsBoxRecommendationProvider.cs` 负责把盒子结构化数据映射为内部动作命令
- `BotMain/BotService.cs` 负责调度动作执行、失败恢复与重新拉取推荐
- `HearthstonePayload/ActionExecutor.cs` 负责在游戏客户端内执行 `PLAY|...`、`TRADE|...`、`BG_PLAY|...`
- `HearthstonePayload/GameObjectFinder.cs` 负责把实体映射为屏幕坐标

近期反馈的核心问题不是“鼠标坐标偏一点”，而是更上游的“源牌身份解析错误”：

- 盒子结构化数据明确给出了 `cardId` 与“第几张牌”
- 但当前实现仍允许在若干路径上按“纯位置”或“仅 cardId”兜底
- 这样会把错误的实体 ID 传给 payload
- payload 随后会非常稳定地抓起这张“已经选错的实体”
- 最终表现为“API 抓到了右边的一张牌”

因此，这次修复的真正目标不是继续优化点击位置，而是把“手牌源牌身份”从模糊匹配改成硬约束。

## 2. 已确认事实

### 2.1 用户确认的业务规则

本次设计以用户确认的规则为准：

`盒子返回的结构化数据会标明是第几张卡牌，只有同时匹配第几张牌和 cardId，才是对的。`

这条规则适用于所有手牌出牌路径，而不是只适用于 HsBox 构筑链路。

### 2.2 当前代码中的直接根因

当前源码里，手牌源牌解析仍允许以下兜底路径：

- `ordered_card_match`
- `ordered_slot_fallback`
- `hand_snapshot_card_match`
- `hand_snapshot_slot_fallback`

其中最危险的是 `ordered_slot_fallback`：即使 `cardId` 已经对不上，系统仍会按手牌位置直接选择该槽位实体。这就是“右边那张牌被抓起来”的根因之一。

### 2.3 问题本质

问题不在“API 为什么会抓错牌”，而在“API 之前已经把错误实体当成正确源牌”。

也就是说：

- `ActionExecutor` 的 API 抓牌阶段并不是随机出错
- 它通常只是忠实地执行了一个已经错误的 `sourceEntityId`

## 3. 目标与非目标

### 3.1 主目标

统一收紧所有“从手牌发起”的动作，使系统只在以下条件成立时才允许执行：

`同一个运行时实体同时满足 sourceEntityId、cardId、zonePosition 三者一致。`

若无法确认，则宁可等待或重试，也绝不允许打错牌。

### 3.2 范围

本次设计覆盖全部手牌动作路径：

- 构筑：`PLAY`
- 构筑：`TRADE`
- 战旗：`BG_PLAY`
- 打出后进入“从手牌选择目标”的后续目标选择

### 3.3 非目标

- 不修改盒子外部接口定义
- 不依赖新增外部字段
- 不尝试通过“更聪明地猜牌”来维持原有 fallback 行为
- 不顺带重构整个攻击、发现、抉择框架

## 4. 成功标准

满足以下任一场景时，系统都必须表现为“停下并恢复”，而不是“猜一张牌继续打”：

1. `cardId` 匹配，但 `zonePosition` 不匹配
2. `zonePosition` 匹配，但 `cardId` 不匹配
3. 有两张相同 `cardId` 的牌，而当前无法确定是哪一张
4. 推荐生成后，手牌因动画、抽牌、发现、重排而发生变化
5. 打出后进入“从手牌选择目标”界面，但当前合法候选与原始 target 不一致

## 5. 方案比较

### 5.1 方案 A：只删掉 HsBox 的纯位置兜底

做法：

- 仅修改 `HsBoxRecommendationProvider`
- 删除 `ordered_slot_fallback`
- 解析不到就重新拉推荐

优点：

- 改动小
- 能立刻缓解最常见误打

缺点：

- 只修了 HsBox
- 没有覆盖 `TRADE`、`BG_PLAY` 与执行前校验
- 系统行为仍然不统一

结论：

不采用。

### 5.2 方案 B：统一严格源牌解析 + 恢复流程

做法：

- 所有手牌动作统一使用严格解析规则
- 全面移除纯位置兜底
- 解析失败进入“短重试 -> 重新拉推荐”的恢复流程

优点：

- 符合业务规则
- 所有路径一致
- 可以彻底消灭“猜牌执行”

缺点：

- 需要同时修改推荐层、调度层、payload 层

结论：

采用。

### 5.3 方案 C：执行层协议增加源牌元数据

做法：

- 在 `PLAY`、`TRADE`、`BG_PLAY` 命令中追加 `sourceCardId` 与 `sourceZonePosition`
- payload 执行前再次校验

优点：

- 执行层也知道“本来要打的是哪一张”
- 即使上游出错，也能在最后一层拦下

缺点：

- 要保证对现有命令解析兼容

结论：

作为方案 B 的一部分一起采用，但以兼容扩展方式实现。

## 6. 总体设计

### 6.1 核心原则

新的系统遵循三条硬规则：

1. 只要动作来源是手牌，就必须同时校验 `cardId + zonePosition`
2. 禁止所有纯位置兜底
3. 不确定时停止执行并恢复，不允许猜测执行

### 6.2 统一身份模型

为所有手牌动作引入统一的“源牌身份”概念：

- `SourceEntityId`
- `SourceCardId`
- `SourceZonePosition`

其中：

- `SourceEntityId` 是候选运行时实体
- `SourceCardId` 与 `SourceZonePosition` 是该实体必须满足的身份约束

只有三者在执行前再次核对仍然一致，动作才允许继续。

### 6.3 数据流

新的数据流如下：

1. 推荐层解析出候选手牌实体
2. 同时记录该实体在推荐生成时的 `cardId` 与 `zonePosition`
3. `BotService` 将这些元数据附加到动作命令
4. `ActionExecutor` 在真正 grab 之前再次核对运行时实体身份
5. 若任一约束不成立，则返回“源牌身份不一致”失败
6. `BotService` 触发短重试与重新拉推荐

## 7. 模块设计

### 7.1 推荐层：严格手牌源牌解析

目标文件：

- `BotMain/HsBoxRecommendationProvider.cs`

改动要点：

1. 对“手牌源牌”单独使用严格解析逻辑
2. 禁止以下结果作为成功映射：
   - `ordered_card_match`
   - `ordered_slot_fallback`
   - `hand_snapshot_card_match`
   - `hand_snapshot_slot_fallback`
3. 仅允许以下成功类型：
   - `hand_snapshot_exact_slot_card`
   - `ordered_exact_slot_card`
4. 若解析失败，返回结构化失败原因，而不是继续生成危险动作

新增失败原因建议：

- `hand_source_exact_match_missing`
- `hand_source_card_matched_but_slot_changed`
- `hand_source_slot_matched_but_card_changed`
- `hand_source_duplicate_card_ambiguous`

### 7.2 调度层：统一恢复策略

目标文件：

- `BotMain/BotService.cs`

改动要点：

1. 所有手牌动作统一补充源牌元数据
2. 当收到以下失败时，不按普通 action failure 处理：
   - `FAIL:PLAY:source_identity_mismatch`
   - `FAIL:TRADE:source_identity_mismatch`
   - `FAIL:BG_PLAY:source_identity_mismatch`
3. 执行统一恢复流程：
   - 短等待后重读一次到两次
   - 仍失败则重新拉当前推荐
   - 限制最大重试轮次
   - 超过阈值后放弃当前动作，但不允许执行 fallback

### 7.3 命令协议：兼容扩展

目标文件：

- `BotMain/BotService.cs`
- `HearthstonePayload/ActionExecutor.cs`

建议命令格式扩展为：

- `PLAY|sourceId|targetId|position|sourceCardId|sourceZonePosition`
- `TRADE|sourceId|sourceCardId|sourceZonePosition`
- `BG_PLAY|sourceId|targetId|position|sourceCardId|sourceZonePosition`

设计要求：

- 旧命令格式仍然兼容
- payload 只要发现附带了源牌元数据，就必须启用严格身份校验
- 未附带时保留旧路径，但后续实现应尽量确保所有手牌动作都带上元数据

### 7.4 执行层：grab 前最终校验

目标文件：

- `HearthstonePayload/ActionExecutor.cs`

新增逻辑：

1. 在 `TryGrabCardViaAPI(...)` 前执行 `ValidateHandSourceIdentity(...)`
2. 连续短轮询几次，确认当前 `entityId` 仍满足：
   - 在我方手牌中
   - `cardId == expectedCardId`
   - `zonePosition == expectedZonePosition`
3. 若任何一项不匹配，立即返回身份失败
4. 禁止降级成“按视觉位置硬抓牌”

建议失败码：

- `FAIL:PLAY:source_identity_mismatch:<entityId>:<detail>`
- `FAIL:TRADE:source_identity_mismatch:<entityId>:<detail>`
- `FAIL:BG_PLAY:source_identity_mismatch:<entityId>:<detail>`

建议 detail：

- `entity_left_hand`
- `card_changed`
- `slot_changed`
- `card_and_slot_changed`
- `duplicate_ambiguous`

### 7.5 打出后手牌目标解析：同步收紧

目标文件：

- `HearthstonePayload/PlayRuntimeTargetResolver.cs`

改动要点：

1. 进入手牌目标模式后，若 hint 同时带有 `cardId` 与 `zonePosition`，则必须双命中
2. 禁止在手牌目标解析中继续使用纯 `slot` fallback
3. 禁止“同 cardId 多张牌时任选一张”
4. 一旦当前为手牌目标模式但无法唯一确认目标，直接失败并交由上层恢复

## 8. 错误处理与恢复策略

### 8.1 短重试窗口

考虑到以下运行时抖动：

- 抽牌动画
- 发现牌入手
- 手牌重排
- 费用变化导致布局刷新

允许在很短时间窗口内做少量重读，例如：

- 每次间隔 120ms 到 200ms
- 单轮重读 2 到 3 次

这一步只用于等待稳定，不用于猜牌。

### 8.2 重新拉推荐

若短重试后仍不一致：

- 丢弃当前动作
- 重新拉取结构化推荐
- 用新推荐重新解析源牌身份

### 8.3 最终原则

如果重新拉推荐后仍无法确认：

- 本轮动作失败
- 回合主循环继续
- 禁止执行任何基于纯位置的兜底动作

换句话说：

`允许少打一手，不允许打错一手。`

## 9. 日志设计

为了后续定位，需要把“源牌身份”日志补成一等诊断信息。

### 9.1 推荐层日志

建议输出：

- 推荐原始 `cardId`
- 推荐原始 `zonePosition`
- 解析命中的实体 ID
- 失败原因
- 当前手牌快照摘要

### 9.2 payload 日志

建议在执行前输出：

- `expectedEntityId`
- `expectedCardId`
- `expectedZonePosition`
- `runtimeCardId`
- `runtimeZonePosition`
- `runtimeInHand`
- 校验结果

示例：

```text
PLAY(source-identity) expectedEntity=71 expectedCardId=GAME_005 expectedZonePos=5 runtimeCardId=CORE_CS2_231 runtimeZonePos=5 result=card_changed
```

### 9.3 恢复层日志

建议记录：

- 第几次短重试
- 是否重新拉推荐
- 新旧推荐是否变化
- 最终是恢复成功还是放弃动作

## 10. 测试设计

### 10.1 推荐层单元测试

需要新增或修改以下测试：

1. 当仅能走 `ordered_slot_fallback` 时，不再返回 `PLAY`，而是返回可恢复失败
2. 当 `cardId` 对上但 `zonePosition` 不对时，必须失败
3. 当 `zonePosition` 对上但 `cardId` 不对时，必须失败
4. 当两张同名牌存在，但无法唯一确认时，必须失败

现有允许 fallback 成功的测试需要改写，尤其是当前明确接受 `ordered_slot_fallback` 的测试。

### 10.2 payload 测试

需要覆盖：

1. `PLAY` 执行前身份一致，允许继续
2. `PLAY` 执行前 `cardId` 变化，返回 `source_identity_mismatch`
3. `PLAY` 执行前 `zonePosition` 变化，返回 `source_identity_mismatch`
4. `TRADE` 与 `BG_PLAY` 复用同一校验逻辑
5. 手牌目标解析禁止 `slot` fallback

### 10.3 集成回归

需要重点回归：

- 幸运币
- 两张相同手牌同时存在
- 抽到新牌后立即执行
- 发现牌或生成牌进入手牌后立即执行
- 战旗从手牌打出
- 打出后选择手牌目标

## 11. 风险与控制

### 11.1 预期风险

严格模式上线后，短期内可能出现：

- “不打牌”的次数上升
- 推荐重拉次数增加
- 某些原本靠 fallback 勉强能动的场景变成显式失败

### 11.2 风险接受原则

这些都属于可接受代价，因为本次优先级明确为：

`绝不误打牌 > 偶发跳过动作 > 多做一次刷新`

## 12. 最终结论

本次问题的根因不是 API 随机抓错，而是系统在 API 之前仍允许“按位置或半匹配猜源牌”。

因此，最终方案是：

1. 全系统建立统一的手牌源牌身份模型
2. 所有手牌动作必须同时匹配 `cardId + zonePosition`
3. 删除纯位置兜底
4. 在 payload 执行前增加最后一层身份校验
5. 失败时走“短重试 -> 重新拉推荐”，绝不误打

这套方案可以把“抓到右边一张牌”的问题从根上切断，而不是继续在点击阶段打补丁。
