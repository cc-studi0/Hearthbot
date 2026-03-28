# 炉石出牌后手牌目标运行时重解析修复设计

日期：2026-03-28

## 1. 背景

当前仓库已经具备完整的构筑执行链路：

- `BotMain/HsBoxRecommendationProvider.cs` 负责把盒子结构化 JSON 映射为内部动作命令
- `HearthstonePayload/ActionExecutor.cs` 负责把 `PLAY|source|target|position` 等命令落到游戏客户端
- `HearthstonePayload/GameObjectFinder.cs` 负责把实体映射为屏幕点击坐标

现有系统在普通场上目标、英雄目标、无目标出牌场景下基本可用，但在一类新的炉石交互中会稳定出错：

- 打出卡牌后
- 游戏进入“从你的手牌中选择一张牌”之类的后续选择界面
- 脚本却把上游 `target` 继续当成场上目标
- 最终点到场上随从，而不是手牌

用户补充的盒子结构化数据如下：

```json
{
  "actionName": "play_minion",
  "card": {
    "ZONE_POSITION": 1,
    "cardId": "CATA_490",
    "cardName": "魔眼秘术师"
  },
  "position": 1,
  "target": {
    "cardId": "CATA_499",
    "cardName": "助祭耗材",
    "position": 2
  }
}
```

这个结构里的 `target` 没有说明目标区域是“手牌”还是“场上”，只有通用的 `cardId/cardName/position`。因此修复不能继续依赖上游类型判断，必须下沉到执行层，依据游戏实时状态决定当前到底是：

- 场上目标选择
- 手牌目标选择
- 普通抉择 / 子选项

## 2. 问题定义

### 2.1 当前症状

当前“打出后选手牌”的 bug 具有固定特征：

1. 盒子把目标描述为一个普通 `target`
2. `BotMain/HsBoxRecommendationProvider.cs` 优先尝试把这个 `target` 解释为友方场上实体
3. `HearthstonePayload/ActionExecutor.cs` 在 `PLAY` 流程中也默认按场上目标坐标点击
4. 当真实游戏态已经进入“从手牌选择”界面时，脚本仍可能继续点击场上随从

### 2.2 已确认事实

结合当前仓库代码与 `H:\Hearthstone\Logs\...\Power.log`，已经确认两件关键事实：

1. 盒子上游不会稳定告诉我们“这是手牌目标”  
   `target` 只能当作一个目标提示，不能当作目标区域真相。

2. 游戏内“需要从手牌中选择”的交互不一定表现为 `TARGET`  
   在真实 `Power.log` 中，存在 `ChoiceType=GENERAL CountMin=1 CountMax=1`，且 `Entities[...]` 中直接出现 `zone=HAND` 的记录。  
   这说明：
   - 不能只依赖 `ResponseMode=TARGET`
   - 不能只依赖旧的单次 `DoesSelectedOptionHaveHandTarget()` 检查
   - 必须同时读取运行时合法候选实体列表

### 2.3 当前实现的根因

当前实现虽然已经有一段“手牌目标纠偏”逻辑，但它存在三个结构性问题：

1. 触发时机过窄  
   只在 `PLAY` 流程中很窄的一段时机做一次纠偏，容易读早或读不到。

2. 判定条件过窄  
   主要依赖 `DoesSelectedOptionHaveHandTarget()` 和 `GetSelectedNetworkOption().Main.Targets`，没有把 `ChoiceType=GENERAL + zone=HAND` 纳入一等信号。

3. 失败回退不够保守  
   当真实游戏态已经进入手牌选择，但没有稳定纠偏成功时，仍可能继续沿着场上目标路径点击，导致错误操作。

### 2.4 关键结论

这次修复的核心不是“继续猜目标”，而是：

`把 PLAY 后续目标解析改成“游戏运行时合法目标优先”，上游 target 只作为提示信息。`

## 3. 目标与约束

### 3.1 主目标

在构筑模式普通对局中，只要某张卡打出后真实游戏态进入“从手牌选择”的后续界面，脚本必须：

- 以游戏运行时状态为准识别为“手牌目标选择”
- 从真实合法手牌候选中解析最终点击目标
- 不再错误点击场上随从

### 3.2 运行时原则

本次修复采用严格的“运行时真相优先”原则：

- 上游 `target` 只当作提示，不当作最终目标类型
- 目标类型由游戏当前状态决定，而不是盒子 JSON 决定
- 一旦运行时明确处于手牌选择模式，就禁止回退成场上目标点击

### 3.3 范围

本次只处理“构筑模式普通出牌后续目标解析”：

- `PLAY` 指令进入后续目标选择
- 真实合法候选中包含手牌实体
- 上游 `target` 被错误解释为场上实体

包含：

- `HearthstonePayload/ActionExecutor.cs` 的 `PLAY` 流程
- 运行时合法候选读取
- 点击前最终目标重解析
- 日志与测试补齐

### 3.4 非目标

- 不修改盒子 JSON 结构
- 不强依赖盒子新增“target zone=HAND”字段
- 不在本次中大改 `BotMain/HsBoxRecommendationProvider.cs` 的上游协议
- 不顺带重构整个 `OPTION` / `CHOOSE_ONE` 执行框架

## 4. 方案比较

### 4.1 方案 A：最小补丁，继续增强单点纠偏

做法：

- 保留现有 `TryCorrectHandTargetEntityId()`
- 只增加一些判定条件
- 继续在 `PLAY` 中原位置做一次手牌目标修正

优点：

- 改动最小
- 短期可以覆盖部分场景

缺点：

- 仍然依赖单次时机
- 仍可能漏掉 `ChoiceType=GENERAL + zone=HAND`
- 行为不稳定，难以作为长期方案

结论：

不采用。

### 4.2 方案 B：执行层运行时目标重解析

做法：

- `PLAY` 进入后，把上游 `target` 降级为提示
- 在牌离手后短时间轮询游戏实时状态
- 读取当前合法候选和选择上下文
- 一旦发现当前合法候选中存在手牌实体，就切换到“手牌目标选择模式”
- 按真实手牌候选重新解析最终实体并点击

优点：

- 完全符合“看游戏状态，而不是上游”的要求
- 与真实 `Power.log` 事实一致
- 可以同时覆盖 `TARGET` 与 `GENERAL + HAND`

缺点：

- 改动范围比最小补丁大
- 需要补测试和诊断日志

结论：

采用 `方案 B`。

### 4.3 方案 C：修改上游协议，把 target 区域提前标出来

做法：

- 在 `BotMain/HsBoxRecommendationProvider.cs` 里尽量提前把 `target` 判成手牌
- 甚至尝试给 `PLAY` 指令增加新的协议字段

优点：

- 表面上更显式

缺点：

- 盒子原始 JSON 本身没有给出稳定的目标区域信息
- 真正的目标类型仍然必须依赖游戏实时状态
- 会把同一个问题分散到上游映射层和 payload 执行层

结论：

本次不采用。

## 5. 总体设计

### 5.1 设计原则

新的执行逻辑遵循两条硬规则：

1. `PLAY` 的 `targetEntityId` 是提示，不是最终真相
2. 最终点击对象必须从当前游戏合法候选中得出

### 5.2 新增运行时目标解析层

建议在 `HearthstonePayload` 中增加一个小而专注的运行时目标解析层，例如：

- `HearthstonePayload/PlayRuntimeTargetResolver.cs`

它只负责一件事：

`根据当前游戏真实状态，判断此次 PLAY 后续到底是场上目标还是手牌目标，并解析最终应该点击的实体。`

这样可以避免继续把复杂目标判断堆进 `ActionExecutor.cs` 的主流程。

### 5.3 PLAY 流程中的新时序

`MousePlayCardByMouseFlow()` 的目标段改成如下顺序：

1. 打出牌，把卡从手牌拖出
2. 等待确认来源牌已离开手牌
3. 进入一个短轮询窗口，持续读取当前游戏状态
4. 构建“运行时目标上下文”
5. 根据上下文决定：
   - 当前是否为手牌目标模式
   - 若是，最终该点击哪张手牌
   - 若否，继续沿用普通场上目标路径

### 5.4 手牌目标模式的判定来源

“当前是否是手牌目标选择”不能再只看单个 API，而要综合多个来源：

1. `GameState.DoesSelectedOptionHaveHandTarget()`
2. `GameState.GetSelectedNetworkOption().Main.Targets`
3. `GetFriendlyEntityChoices()` 返回的 choice packet
4. 当前 `ChoiceSnapshot`
5. `ChoiceType=GENERAL` 且合法候选实体中出现 `zone=HAND`

设计上，这些来源应统一抽象为一个“运行时合法候选集合”。

只要集合中存在手牌实体，就认为当前处于手牌目标选择模式。

### 5.5 最终目标的解析顺序

在“手牌目标选择模式”下，最终目标解析顺序改为：

1. 如果上游传入的 `targetEntityId` 本身就在合法候选里，直接使用
2. 读取这个提示实体的 `cardId`
3. 读取这个提示实体的 `zonePosition`
4. 在真实合法手牌候选中按以下顺序匹配：
   - `cardId + zonePosition`
   - `cardId`
   - `zonePosition`

这是因为上游虽然可能把目标区域解析错，但通常还保留了有价值的提示：

- 牌名 / `cardId`
- “第几张”的位置信息

### 5.6 保守失败策略

新的行为必须“宁可失败，不可乱点”：

- 如果运行时已经明确是手牌目标模式，但无法从合法手牌候选中唯一解析出目标
- 则立即失败并输出清晰日志
- 不允许回退去点场上随从

这样可以避免当前最致命的回归：把手牌选择误操作成场上目标点击。

## 6. 数据流与模块边界

### 6.1 `BotMain/HsBoxRecommendationProvider.cs`

本次不做大改，只保留现有职责：

- 把盒子 JSON 映射为 `PLAY|source|target|position`
- `target` 继续作为普通提示实体传下去

已确认当前这里的行为是：

- 优先按友方场上实体匹配 `step.Target`
- 场上未命中时再尝试手牌

因此这里天然会把“未显式声明区域的 target”偏向解释成场上实体。  
这不是本次主修点，真正的纠正动作必须在 payload 执行层完成。

### 6.2 `HearthstonePayload/ActionExecutor.cs`

主改动点：

- `MousePlayCardByMouseFlow()`
- `TryCorrectHandTargetEntityId()` 相关逻辑
- 目标点击前的最终实体解析与日志

建议把原来的“单点纠偏”改为“调用运行时目标解析器”的统一流程。

### 6.3 `HearthstonePayload/GameObjectFinder.cs`

现有实现已经优先通过 `HandZone` 查找手牌位置，这一点是正确基础：

- `GetEntityScreenPos()` 会先尝试 `TryGetHandCardWorldPos()`
- 说明一旦我们把最终 `entityId` 纠正成真实手牌实体，点击链路本身可以复用现有坐标能力

因此本次不需要改动手牌坐标主逻辑，只需保证传进去的是正确的手牌实体。

### 6.4 新增解析器的职责

若新增 `PlayRuntimeTargetResolver.cs`，其职责应保持单一：

- 采集运行时候选
- 判断目标模式
- 解析最终目标
- 返回解析结果与命中原因

不负责：

- 鼠标点击
- 出牌确认
- 子选项提交流程

## 7. 具体行为设计

### 7.1 运行时候选抽象

建议统一构建一个内部结构，例如：

- `Mode`: `BoardTarget` / `HandTarget` / `Unknown`
- `Candidates`: 当前合法候选实体列表
- 每个候选至少包含：
  - `EntityId`
  - `Zone`
  - `ZonePosition`
  - `CardId`

构建规则：

- 优先读取 choice packet / selected option targets
- 从实体对象中抽取 `zone / zonePosition / cardId`
- 如果任一候选为 `zone=HAND`，则 `Mode=HandTarget`

### 7.2 提示目标信息的抽取

上游传来的 `targetEntityId` 虽然可能区域错误，但仍可提取提示信息：

- `cardId`
- `zonePosition`

抽取来源按优先级：

1. 当前 `GameState` 中该实体
2. 运行前已存在的场上实体
3. 无法抽取时仅保留原始 `targetEntityId`

### 7.3 匹配规则

对于手牌候选，使用固定优先级：

1. 实体直接命中
2. `cardId + zonePosition`
3. `cardId`
4. `zonePosition`

当同名牌有多张时：

- `cardId + zonePosition` 是一等命中规则
- 不允许直接在多张同名牌里随意取第一张

### 7.4 无法唯一命中的处理

若出现：

- 合法手牌候选为空
- 候选存在但无法匹配
- 候选中多个同分命中，无法唯一决定

则返回明确失败，不继续执行错误点击。

## 8. 日志与诊断

### 8.1 必须新增的日志

建议在 `PLAY` 后续目标阶段补充统一日志，至少包含：

- `sourceEntityId`
- 原始 `targetEntityId`
- 运行时判定模式：`board_target` / `hand_target`
- 当前合法候选摘要：`entityId + zone + zonePosition + cardId`
- 最终命中的实体
- 命中原因：`entity_direct` / `card_slot` / `card` / `slot`

### 8.2 失败日志

失败时至少区分：

- `hand_target_detected_but_no_match`
- `hand_target_detected_but_multiple_matches`
- `target_context_not_ready_timeout`

这样后续再遇到边界问题时，能明确知道是：

- 没读到状态
- 读到了但匹配不到
- 匹配规则还不够

## 9. 测试策略

### 9.1 单元测试

建议新增或补充 `BotCore.Tests` 中与目标解析相关的测试，重点覆盖：

1. 当前合法候选包含 `zone=HAND` 时，应优先判定为手牌目标
2. `ChoiceType=GENERAL` 且候选中有 `HAND` 时，仍应判定为手牌目标
3. 同名牌多张时，优先使用 `cardId + zonePosition`
4. 运行时已判定手牌目标但匹配失败时，不允许回退为场上目标

若新增独立解析器文件，则建议同时新增专门测试文件，例如：

- `BotCore.Tests/PlayRuntimeTargetResolverTests.cs`

### 9.2 手工验收

至少验证以下场景：

1. 打出后选择手牌的战吼
   - 必须点击手牌
   - 不得点击场上随从

2. 普通场上指向战吼
   - 仍应正常点击场上目标

3. 无目标普通出牌
   - 仍应正常完成出牌

4. 抉择 / 子选项后再选目标
   - 现有流程不应回归

## 10. 模块改动点

### 10.1 `HearthstonePayload/ActionExecutor.cs`

需要改动：

- `MousePlayCardByMouseFlow()`
- 目标点击前的最终实体解析时序
- 旧 `TryCorrectHandTargetEntityId()` 的职责收敛或替换
- 新日志输出

### 10.2 `HearthstonePayload/GameObjectFinder.cs`

原则上不做主逻辑改动，仅依赖现有：

- `GetEntityScreenPos()`
- `TryGetHandCardWorldPos()`

### 10.3 新增文件

建议新增：

- `HearthstonePayload/PlayRuntimeTargetResolver.cs`

如果实现中需要独立的数据模型，也可以新增：

- `HearthstonePayload/PlayRuntimeTargetContext.cs`

### 10.4 测试文件

建议新增：

- `BotCore.Tests/PlayRuntimeTargetResolverTests.cs`

## 11. 验收标准

本次设计完成后的验收标准为：

- 盒子继续传通用 `target` 结构时，脚本仍能以游戏实时状态判断真实目标类型
- 当真实运行时候选中存在手牌实体时，脚本必须点击手牌，不得点击场上随从
- `ChoiceType=GENERAL + zone=HAND` 的真实场景必须被识别为手牌目标模式
- 普通场上目标、英雄目标、无目标出牌路径不回归
- 当运行时已经明确是手牌目标但无法唯一匹配时，脚本应保守失败，而不是乱点

## 12. 风险与后续

### 12.1 已知风险

- 如果上游提示目标的 `cardId` 与 `zonePosition` 同时都失真，运行时只能依赖有限提示，可能需要更强的兜底策略
- `ActionExecutor.cs` 当前文件体量较大，若直接堆改逻辑，后续可维护性会继续下降

### 12.2 本次应对

- 用“新增运行时解析器”的方式隔离复杂度
- 以保守失败替代错误点击
- 用单元测试固定 `GENERAL + HAND` 这类关键事实

### 12.3 后续工作

在本次修复落地后，可再评估是否需要进一步优化上游提示链路，例如：

- 在 `BotMain/HsBoxRecommendationProvider.cs` 增加诊断信息，记录 `target` 是如何被解析成场上实体的
- 针对更多“目标区域不显式”的盒子 JSON 场景补做统计

但这些不属于本次主修范围。
