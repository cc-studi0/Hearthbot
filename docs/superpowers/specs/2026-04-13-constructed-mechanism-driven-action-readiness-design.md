# 构筑模式机制驱动动作就绪改造 · 设计

**日期**：2026-04-13  
**作者**：Codex 与用户协作  
**状态**：待评审

## 目标

1. 构筑模式不再依赖固定动画等待，只要游戏内部已经允许下一步操作，就立刻继续执行。
2. 覆盖 `PLAY / ATTACK / HERO_POWER / USE_LOCATION / OPTION / TRADE / END_TURN / Discover / Choose One / 泰坦子技能 / 地标 / 指向性英雄技能` 全链路。
3. 保留现有拟人化鼠标轨迹、悬停、回合开始思考和微停顿，不把构筑模式改成“纯机器点击”。
4. readiness 判定必须基于炉石内部机制与实体状态，而不是猜测动画时长或硬编码固定延迟。
5. 当输入已经开放但实体还在轻微收尾位移动画时，只保留“最小稳定校验”，不等待整段视觉动画播放完。

## 非目标

1. 不重写盒子推荐映射、策略规划或构筑决策逻辑。
2. 不移除 `ConstructedHumanizerPlanner`、`MaybePreviewAlternateTarget`、鼠标曲线等拟人化模块。
3. 不把构筑执行层全面改成纯网络包执行。
4. 不修改战旗 readiness 机制，只复用其“动作级对象 ready gate”的设计思路。

## 背景与现状

当前构筑模式已经具备大量 readiness 与确认逻辑，但它们分散在两层：

1. `BotMain/BotService.cs`
   - 构筑主循环在动作前后主要使用通用 `WaitForGameReady(...)`
   - 其核心来源是 payload 端的 `WAIT_READY / WAIT_READY_DETAIL`
   - 这能识别全局硬阻塞，但无法精确回答“下一条具体动作现在能不能点”

2. `HearthstonePayload/ActionExecutor.cs`
   - `PLAY / OPTION / ATTACK / HERO_POWER / USE_LOCATION / Discover / Choose One` 已混合使用一部分机制判定
   - 但仍保留多处固定 `Thread.Sleep(...)` / `yield return ...` 的尾等待
   - 这些等待有些是必要的“最小稳定校验”，有些则是在补动画时序

这导致构筑模式当前出现典型问题：

- 上一动作的真实游戏结算已经允许下一步输入；
- 但主循环仍在统一 `WAIT_READY` 或动作内部固定尾等待里多停一段；
- 结果就是看起来像“还在等动画”，而不是“游戏真的还不能操作”。

用户要求的目标并不是简单把 sleep 调小，而是：

`像战旗一样，只要下一步在游戏内已经可以操作，就立刻操作；但保留拟人化动作本身。`

## 已确认的机制信号

本次设计只采用项目内已存在或已在游戏程序集可核实的机制信号，不靠猜测：

- `InputManager.PermitDecisionMakingInput`
- `GameState.IsResponsePacketBlocked`
- `GameState.IsBlockingPowerProcessor`
- `PowerProcessor.IsRunning`
- `ZoneMgr.HasActiveServerChange`
- `ZoneMgr.HasPendingServerChange`
- `GameState.GetResponseMode`
- `GameState.IsInChoiceMode`
- `GameState.IsInSubOptionMode`
- `ChoiceCardMgr.HasSubOption`
- `GameState.GetFriendlyEntityChoices`
- `GameState.GetFriendlyCardBeingDrawn`
- 现有 `ChoiceSnapshot / PlayTargetConfirmation / hand zone / actor ready / screen pos` 相关 helper

这些信号已经在当前仓库的 `ActionExecutor` 与 `WAIT_READY` 诊断路径里被使用，且在 `H:\Hearthstone\Hearthstone_Data\Managed\Assembly-CSharp.dll` 中可确认存在相关符号，因此可以作为机制驱动改造的基础。

## 方案比较

### 方案 A：只改 BotMain 的统一等待

做法：

- 只在 `BotMain/BotService.cs` 中把构筑动作前后的 `WaitForGameReady` 调整得更激进。

优点：

- 改动面最小；
- 不需要新增 payload 协议。

缺点：

- 动作内部的固定尾等待依然存在；
- 只能解决“主循环多等”，解决不了“协程内部多等”；
- 不能精确解释某类动作为什么还不能继续。

结论：

不采用。

### 方案 B：双层机制驱动 readiness gate（推荐）

做法：

- `BotMain` 新增构筑动作级 ready wait；
- `Entry` 新增构筑动作级协议；
- `ActionExecutor` 新增构筑动作级 probe / evaluator；
- 将构筑动作内部“用于等动画”的固定等待替换为“机制确认 + 最小稳定校验”。

优点：

- 同时解决主循环等待和动作内部等待；
- 能给出动作级诊断原因；
- 与战旗现有 readiness gate 的体系一致；
- 最贴合“可操作即继续”的目标。

缺点：

- 改动跨两层；
- 需要系统验证特殊动作链路。

结论：

采用。

### 方案 C：深度事件化重构

做法：

- 围绕 `ResponseMode / Choice / PowerProcessor / Zone change` 建立更激进的事件状态机。

优点：

- 理论上最干净；
- 轮询更少。

缺点：

- 侵入性太高；
- 会显著放大版本兼容风险；
- 超出本次“提速但不重写执行层”的范围。

结论：

本次不采用。

## 决策

采用 **方案 B：双层机制驱动 readiness gate**。

保留：

- `ConstructedHumanizerPlanner` 及所有拟人化轨迹、悬停、微停顿；
- 当前 choice / discover / attack / hero power 等已存在的有效机制判定；
- `WAIT_READY` 作为全局兜底；
- 现有失败恢复链路，如 `CANCEL`、choice probe、soft failure、重规划。

新增：

- 构筑动作专用 `WAIT_CONSTRUCTED_ACTION_READY` / `WAIT_CONSTRUCTED_ACTION_READY_DETAIL`；
- `BotService` 中的构筑动作级 ready wait；
- `ActionExecutor` 中的 `ConstructedActionReadyProbe` 与 evaluator；
- 基于“输入开放 + 动作相关对象已 ready + 最小稳定校验”的动作后推进规则。

## 总体架构

### 1. 分层职责

#### BotMain 负责“什么时候发下一条命令”

构筑主循环从“统一等游戏 ready”改为“优先等当前动作对象 ready”：

1. 解析当前动作类型；
2. 若属于构筑动作级 gate 支持范围，则调用 `WAIT_CONSTRUCTED_ACTION_READY_DETAIL:<action>`；
3. 对象 ready 后立即发送动作；
4. 动作发出后，再根据动作类型等待“下一步可继续”的机制条件；
5. 如构筑动作 gate 不适用或 probe 失败，再回退到现有 `WAIT_READY`。

#### ActionExecutor 负责“当前动作在游戏里是否真的已进入可交互状态”

payload 端负责读取游戏内的即时状态：

- 全局硬阻塞信号；
- 当前动作源对象是否存在、可点、可抓取、可拖拽；
- 若存在目标，目标实体是否可解坐标且满足最小稳定；
- 若处于 choice / target mode，当前模式是否已经 ready；
- 动作提交后是否已发生可确认的状态切换。

### 2. readiness 原则

#### 全局硬阻塞优先

以下信号为硬阻塞，只要命中就不允许继续：

- `PermitDecisionMakingInput == false`
- `IsResponsePacketBlocked == true`
- `IsBlockingPowerProcessor == true`
- `PowerProcessor.IsRunning == true`
- `ZoneMgr.HasActiveServerChange == true`

#### 动作级对象 ready 决定“能否立刻做下一步”

只要硬阻塞解除，就不再等待“整局都很空闲”，而是只检查当前动作相关对象：

- 手牌实体
- 场上攻击源/地标/随从
- 英雄技能按钮
- 当前目标实体
- 当前 choice/suboption/discover 选项
- 结束回合按钮

#### 最小稳定校验只在依赖屏幕坐标时启用

用于避免“输入已开但坐标还在漂”的空点，不用于人为拖慢节奏：

- 手牌抓取前
- 拖拽落点确认前
- 目标点击前
- 地标或英雄技能目标点击前

推荐规则：

- 连续两次采样坐标变化小于阈值即可；
- 不再以固定 300ms、500ms 之类动画时长为前提。

## 命令级就绪判定

### PLAY

适用对象：

- 普通随从
- 法术
- 指向性法术
- 战吼选目标随从
- 触发后接 `OPTION` 的牌

#### 动作前 ready

要求：

1. 源手牌实体存在；
2. 若该牌是最近抽到的牌，则不能仍处于 `GetFriendlyCardBeingDrawn` 的未交互状态；
3. 手牌坐标存在；
4. 手牌坐标满足最小稳定校验；
5. 无全局硬阻塞。

#### 提交确认

不再依赖固定尾等待，而是以下任一成立即可视为已提交：

- 源牌已经离开手牌；
- 进入目标确认模式；
- `ResponsePacket / PowerProcessor / Zone change` 出现真实忙碌信号；
- 针对特定出牌链路，现有 `PlayTargetConfirmation` 状态已经发生变化。

#### 动作后继续

- 无目标牌：源牌离手且不存在 pending target confirmation 后即可继续；
- 指向性牌：进入 `target-ready` 分支，而不是固定 sleep；
- 打出后接 `OPTION`：直接等待 option ready，不做统一 post delay。

### OPTION / Discover / Choose One / 泰坦子技能

本链路已有较完整的机制判定，改造重点是“统一推进时机”，不是推倒重写。

#### 动作前 ready

根据 choice 机制区分：

- `EntityChoice`
  - choice packet 存在且包含实体；
  - choice state active / shown；
  - 候选实体坐标可取。

- `SubOptionChoice`
  - `ChoiceCardMgr.HasSubOption` 或 `IsInSubOptionMode` 成立；
  - 子选项实体存在；
  - 子选项可取坐标。

#### 提交确认

只要以下任一成立即可继续：

- `ChoiceSnapshot` 发生变化；
- 当前选择 UI 退出；
- pending source 不再处于 target / suboption 挂起。

不再在 choice 提交后额外追加“等一段动画再继续”。

### ATTACK

#### 动作前 ready

要求：

1. 攻击源存在且具备可攻击状态；
2. 目标实体存在；
3. 目标坐标可取；
4. 目标坐标满足最小稳定校验；
5. 无全局硬阻塞。

#### 动作后继续

以下任一成立即可允许下一步规划：

- 攻击源攻击次数已消耗；
- 棋盘状态已变化；
- 真实 busy 信号出现后解除；
- 已确认不再处于攻击挂起状态。

对攻击保留现有 soft failure 思路：

- 若命令返回 `not_confirmed`，但棋盘刷新后已体现攻击结果，则不把它视为硬失败。

### HERO_POWER

适用：

- 无目标英雄技能
- 指向性英雄技能

#### 动作前 ready

要求：

1. 英雄技能按钮或对应实体存在；
2. 英雄技能屏幕坐标可取；
3. 若技能需要目标，则目标坐标也必须可取并满足最小稳定；
4. 无全局硬阻塞。

#### 动作后继续

只要以下任一成立即可继续：

- 英雄技能已消耗；
- 若需目标，则目标模式退出；
- 真实 busy 后解除。

### USE_LOCATION

适用：

- 无目标地标
- 有目标地标

#### 动作前 ready

要求：

1. 地标实体存在；
2. 地标坐标可取；
3. 地标坐标满足最小稳定；
4. 若有目标，目标坐标也满足最小稳定；
5. 无全局硬阻塞。

#### 动作后继续

确认依据：

- 地标已进入冷却 / 已消耗；
- 目标模式退出；
- 相关 busy 信号已出现并解除。

### TRADE

#### 动作前 ready

要求：

1. 交易牌实体仍在手牌；
2. 可抓取；
3. 手牌坐标存在且短稳；
4. 无全局硬阻塞。

#### 动作后继续

确认依据：

- 源牌离手；
- 手牌集合或牌库状态已变化；
- 相关 busy 信号已出现并解除。

不再为了交易动画额外停固定时长。

### END_TURN

#### 动作前 ready

要求：

1. 结束回合按钮可交互；
2. 当前不存在 pending target / choice / suboption；
3. 无全局硬阻塞。

#### 动作后继续

结束回合不追求极限抢帧，仍允许保留拟人化的极小犹豫，但不再因为无关动画继续等待。

## 需要新增的 payload 能力

建议在 `HearthstonePayload/ActionExecutor.cs` 中新增以下能力：

1. `IsConstructedActionReady(string rawCommand)`
   - 返回当前动作是否 ready。

2. `DescribeConstructedActionReady(string rawCommand)`
   - 返回结构化诊断字符串，供 `BotMain` 记录为什么尚未 ready。

3. `ConstructedActionReadyProbe`
   - 汇总当前动作的：
     - `HardBlockFlags`
     - `SourceReadySnapshot`
     - `TargetReadySnapshot`
     - `ChoiceReadySnapshot`
     - `PendingResolutionSnapshot`

4. `ConstructedActionReadyEvaluator`
   - 按动作类型给出：
     - `IsReady`
     - `PrimaryReason`
     - `Flags`
     - `CommandKind`

建议复用现有 helper，避免重复造轮子：

- `TryReadCardActorReady`
- `TryReadCardStandInIsInteractive`
- `TryGetFriendlyChoiceCardObject`
- `TryBuildChoiceSnapshot`
- `IsPlayTargetConfirmationPending`
- `TryResolveRuntimeCardObject`
- `GameObjectFinder.GetEntityScreenPos / GetHeroPowerScreenPos / GetHeroScreenPos`

## Entry 协议扩展

在 `HearthstonePayload/Entry.cs` 中新增：

- `WAIT_CONSTRUCTED_ACTION_READY:<action>`
- `WAIT_CONSTRUCTED_ACTION_READY_DETAIL:<action>`

原则：

1. 不修改现有 `WAIT_READY` 语义；
2. 不修改现有 `WAIT_BG_ACTION_READY` 语义；
3. 构筑 readiness 与战旗 readiness 并行存在，便于回退与日志对比。

## BotService 接入设计

### 1. 新增构筑动作 ready wait

在 `BotMain/BotService.cs` 中新增：

- `ShouldUseConstructedActionReadyWait(string action)`
- `WaitForConstructedActionReady(PipeServer pipe, string action, ...)`
- `TryGetConstructedActionReadyDiagnostic(...)`

覆盖动作：

- `PLAY|`
- `ATTACK|`
- `HERO_POWER|`
- `USE_LOCATION|`
- `OPTION|`
- `TRADE|`
- `END_TURN`

### 2. 主循环行为调整

新的优先级：

1. 若当前动作属于构筑动作 gate 范围，优先等待 `WAIT_CONSTRUCTED_ACTION_READY_DETAIL`；
2. 仅在构筑 gate 不适用、诊断不可用或超时回退时，才走现有 `WaitForGameReady`；
3. 动作发送后，post-ready 也优先使用构筑动作级 ready，而不是统一 `WAIT_READY`。

### 3. 与拟人化的关系

保留：

- `ConstructedHumanizerPlanner.ComputeInterActionDelayMs(...)`
- 回合开始思考、扫视、微动
- `MaybePreviewAlternateTarget(...)`

调整：

- 拟人化延迟只代表“像人怎么动”；
- 不再承担“替游戏等动画”的职责；
- 当 readiness 已满足时，拟人化不应再叠加一段无意义固定动画等待。

## 动作内部固定等待的替换策略

### 替换目标

重点替换以下“用于等动画”的等待：

- `PLAY` 提交后的统一尾等待；
- 指向性出牌在目标前的固定停顿；
- 地标提交后的固定等待；
- 部分攻击、英雄技能、交易提交后的统一等待；
- Discover / SubOption / Titan 链路中非必要的固定等待。

### 保留目标

保留以下“动作表现或最小安全校验”：

- 鼠标移动、按下、松开对应的必要极短时间；
- 拟人化悬停与扫视；
- 目标短稳采样的小窗口；
- choice 提交时为了确认 snapshot 变化而进行的短轮询。

### 替换原则

从：

- “动作发出后固定等一段”

改为：

- “动作发出后轮询机制状态，满足即继续”

## 失败恢复与回退

### 1. 构筑动作 ready 超时

处理策略：

- 记录动作级 busy 原因；
- 允许一次回退到现有 `WAIT_READY`，避免因单个漏判直接中断整回合；
- 若仍失败，则走当前短退避与重规划路径。

### 2. 动作已发出但确认不足

保留现有恢复逻辑：

- `CANCEL`
- choice probe
- soft failure
- 重新规划

### 3. 目标短稳失败

只在该动作内做短重试：

- 不把整局拖成固定等待；
- 不因为一次目标轻微漂移直接进入长时间 timeout。

### 4. 版本兼容策略

每个 readiness 子判断都采用“能读就读，读不到就降级”：

- 读不到 actor ready 时，降级到“实体存在 + 坐标存在 + 无硬阻塞”；
- 读不到目标附加状态时，至少保留坐标短稳；
- 不让单一反射字段缺失导致整套 gate 不可用。

## 日志设计

新增构筑动作级诊断原因，至少覆盖：

- `input_denied`
- `response_packet_blocked`
- `blocking_power_processor`
- `power_processor_running`
- `zone_active_server_change`
- `zone_pending_server_change`
- `source_missing`
- `source_not_interactive`
- `source_not_stable`
- `target_missing`
- `target_pos_not_found`
- `target_not_stable`
- `choice_not_ready`
- `pending_target_confirmation`
- `end_turn_button_disabled`
- `not_ready_timeout`

日志目标是让一次“为什么没有立刻继续”的问题可以快速区分：

- 是全局状态机还没开放；
- 还是当前动作对象没准备好；
- 还是目标坐标仍在轻微位移；
- 还是动作提交后仍处于 pending target / choice。

## 测试策略

### 单元测试

建议新增或扩展以下测试：

1. 构筑动作类型到 readiness evaluator 的映射；
2. `PLAY / ATTACK / HERO_POWER / USE_LOCATION / OPTION / TRADE / END_TURN` 各自的 ready 条件解析；
3. 硬阻塞优先级正确；
4. 目标短稳失败时保持 busy；
5. 构筑 gate 超时后允许回退到 `WAIT_READY`；
6. `OPTION` 与 choice snapshot 提交后不再依赖固定 sleep；
7. Discover / Choose One / 泰坦子技能路径的 ready 诊断。

### 手动验证

重点验证以下真实场景：

1. 普通随从无目标出牌后，源牌一离手就继续；
2. 指向性法术一进入目标模式就继续选目标；
3. 战吼选目标随从落地后，不再额外等一段动画再点目标；
4. Discover 弹出后，选项刚可点就点；
5. Choose One / 泰坦子技能 UI 刚 ready 就点；
6. 地标无目标 / 有目标链路不再等整段视觉动画；
7. 指向性英雄技能进入目标模式后立即继续；
8. 交易后牌一离手即继续；
9. 连续攻击场景明显提速，但不增加空挥；
10. 结束回合前存在 lingering animation 时，只要按钮已可交互且无 pending 选择，就允许结束回合。

## 验收标准

本次设计完成后的验收标准为：

1. 保留现有拟人化执行表现，不把构筑模式改成纯机器点击。
2. 只要游戏内部已经开放输入且动作相关对象满足最小稳定条件，就不再额外等待固定动画时间。
3. 构筑模式的 `PLAY / ATTACK / HERO_POWER / USE_LOCATION / OPTION / TRADE / END_TURN / Discover / Choose One / 泰坦子技能 / 地标` 全链路均接入机制驱动 readiness。
4. 特殊选择链路不比当前实现更脆弱，失败时可安全回退。
5. 日志能够明确说明 readiness 被什么机制阻塞。

## 风险

1. 构筑不同对象类型的反射接口可能不完全统一，需要多路径兼容。
2. 若动作级 gate 过于激进，可能在个别实体轻微漂移时引入空点；因此必须落实最小稳定校验。
3. 若动作级 gate 过于保守，则会重新退化为“虽然机制化了，但还是在等”。
4. 部分特殊卡牌链路依赖历史兼容逻辑，必须保留回退到 `WAIT_READY` 的能力。

## 回滚策略

回滚应尽量简单：

1. `BotService` 停止调用构筑动作级 ready wait；
2. 恢复使用现有 `WaitForGameReady` 与当前动作内部逻辑；
3. `Entry` 中新增协议可以保留但不再使用；
4. 拟人化与现有决策层保持不变。

这意味着本次改造是一层可拆卸优化，而不是对构筑执行层的不可逆重写。

## 结论

本次改造的关键，不是继续微调固定 sleep，而是把构筑模式“能不能做下一步”的判断，从：

- “全局是不是完全空闲”

切换为：

- “游戏输入是否已开放”
- “这条动作对应的对象是否已经真正可交互”
- “若依赖坐标，是否已经满足最小稳定条件”

在这个前提下：

- 可以保留拟人化动作表现；
- 可以显著减少构筑模式里为了等动画而产生的空等；
- 可以让 Discover、Choose One、泰坦子技能、地标、攻击与指向性操作都更早衔接到下一步；
- 仍然保留现有失败恢复和全局兜底，避免因为局部漏判把执行层打坏。
