# 云控状态稳定性与统一判定设计

## 概述

本次设计解决的是云控首页里同一台设备状态来回跳动的问题：

- 脚本明明正常运行，卡片却会在 `进行中` 和 `异常` 之间来回切
- 状态标签一会显示 `对局中`，一会又显示 `离线`
- `Switching`（切号中）这种本来属于正常流程的状态，也会被直接打进异常分组

目前问题不是单点 bug，而是**原始状态、展示状态、分组状态由多处重复判定且阈值过紧**导致的：

1. BotMain 上报一套原始状态：`Idle / Running / InGame / Switching / Offline`
2. 云控前端在本地再次根据 `LastHeartbeat` 推断“是否异常”
3. 云控后端 `DeviceWatchdog` 又在相同超时阈值下把设备直接落库成 `Offline`

结果就是同一台设备会同时被多套规则解释，从而出现“标签和分组不一致”“SignalR 推送和手动刷新不一致”“正常设备被误判异常”的现象。

因此，本次设计的核心方向是：

- **BotMain 只负责上报事实**
- **HearthBot.Cloud 负责统一解释事实**
- **hearthbot-web 只负责展示解释结果**

## 设计目标

### 目标

- 消除“状态标签”和“所在分组”不一致的问题
- 消除正常运行设备在 `进行中 / 异常 / 离线` 之间的抖动
- 把 `Switching` 从“默认异常”改为“短时正常，超时才异常”
- 让 SignalR 实时推送与 REST 手动刷新走同一套判定逻辑
- 让首页统计、卡片展示、详情抽屉、异常数量都来自同一份展示态

### 非目标

- 不修改 BotMain 的实际对局、切号、挂单主流程
- 不改动订单完成、完成快照、手动隐藏等现有业务语义
- 不引入复杂状态机持久化或新的后台任务类型
- 不在前端加纯视觉防抖来掩盖后端状态错误

## 根因分析

当前状态抖动由以下三类问题共同造成。

### 1. 状态来源重复

现状里至少存在三处状态解释：

- `BotMain/Cloud/DeviceStatusCollector.cs`
- `hearthbot-web/src/utils/dashboardState.ts`
- `HearthBot.Cloud/Services/DeviceWatchdog.cs`

这三处都在试图回答“设备现在是不是异常”，但它们使用的输入、触发时机和输出语义都不同。

### 2. 超时阈值过紧

当前心跳周期为 30 秒，前后端都按 90 秒判断超时。

这意味着：

- 只要连续丢 2 到 3 次心跳
- 或 SignalR / 网络 / GC / 切号阶段出现短暂抖动

前端就可能先把设备打成“异常”，随后后端又直接打成 `Offline`。

### 3. `Switching` 被直接当作异常

`Switching` 本质是切号流程中的过渡态，不等于脚本故障。

当前做法把它直接视为异常，会把正常切号与真正卡死混在一起，导致首页对“异常”的含义被稀释。

## 现状复用

当前项目已经具备以下可复用基础：

- `Device` 已持久化：
  - `Status`
  - `LastHeartbeat`
  - `CurrentAccount`
  - `CurrentRank`
  - `CurrentDeck`
  - `CurrentProfile`
  - `CurrentOpponent`
  - `OrderNumber`
  - `IsCompleted`
- BotMain 心跳已稳定上报原始状态与运行信息
- `DeviceWatchdog` 已具备定时巡检能力
- `DeviceController.GetAll`、`GetStats` 与 `BotHub.DeviceUpdated` 已覆盖首页主要数据入口
- 前端首页已经拆成：
  - 统计区
  - 分组 Tab
  - 设备卡片
  - 详情抽屉

因此本次改造不需要重做页面结构，而是把“状态解释权”收口到云控后端。

## 统一状态模型

本次设计将状态拆成两层。

### 1. 原始状态 `RawStatus`

由 BotMain 上报，继续沿用当前语义：

- `Idle`
- `Running`
- `InGame`
- `Switching`
- `Offline`

它表示“脚本端观察到的事实”，不直接等价于首页最终分组。

### 2. 展示状态 `DisplayState`

由云控后端统一派生，供前端直接消费。建议包含：

- `RawStatus`
- `DisplayStatus`
- `Bucket`
- `AbnormalReason`
- `HeartbeatAgeSeconds`
- `IsHeartbeatStale`
- `IsSwitchingTooLong`

其中：

- `DisplayStatus` 用于卡片标签和详情页标签
- `Bucket` 用于首页 4 个分组
- `AbnormalReason` 用于异常卡片说明文案
- `HeartbeatAgeSeconds` 用于诊断和后续扩展

## 统一判定规则

### 判定优先级

展示分组按以下优先级统一判定：

```text
completed > abnormal > pending > active
```

即：

1. 已完成优先级最高
2. 其次才判断是否异常
3. 未异常时，再看有没有订单号决定进 `待录单` 还是 `进行中`

### 规则明细

#### 1. 已完成

若 `device.IsCompleted == true`：

- `Bucket = completed`
- `DisplayStatus = Completed`
- 不再参与异常与离线判定

#### 2. 离线

若心跳年龄超过离线阈值：

- `DisplayStatus = Offline`
- `Bucket = abnormal`
- `AbnormalReason = HeartbeatTimeout`

后端 watchdog 也使用同一阈值，不再和前端各算一套。

#### 3. 切号中

若 `RawStatus = Switching` 且切号持续时间未超过阈值：

- 不视为异常
- 若有订单号则 `Bucket = active`
- 若无订单号则 `Bucket = pending`
- `DisplayStatus = Switching`

若 `RawStatus = Switching` 且持续时间超过阈值：

- `Bucket = abnormal`
- `DisplayStatus = Switching`
- `AbnormalReason = SwitchingTooLong`

也就是说，`Switching` 的标签保留“切换中”，但是否归入异常由时长决定。

#### 4. 对局中 / 运行中 / 空闲

若 `RawStatus` 为 `InGame / Running / Idle` 且心跳新鲜：

- 一律不进异常
- `DisplayStatus` 直接沿用映射后的展示值
- 有订单号进 `active`
- 无订单号进 `pending`

这样可以确保正常跑单设备不会仅因为前端本地 tick 计算而突然跳到异常组。

## 阈值设计

### 现状问题

当前心跳间隔为 30 秒，90 秒超时过于紧张，容错不足。

### 新阈值建议

- 心跳刷新周期：维持 30 秒不变
- 离线阈值：`150 秒`
- `Switching` 异常阈值：`180 秒`

### 设计理由

- 允许偶发网络抖动、SignalR 重连、短时切号过程
- 避免因为丢失 2 到 3 次心跳就把正常设备判为离线
- `Switching` 比普通心跳更容易受到流程耗时影响，因此给更宽裕阈值

后续如果需要调优，应只改后端统一评估器中的常量，而不是让前后端各自维护。

## 后端改造设计

### 1. 新增统一评估器

在 `HearthBot.Cloud` 中新增统一状态评估器，例如：

- `Services/DeviceDisplayStateEvaluator.cs`

职责：

- 接收原始 `Device`
- 基于当前时间和统一阈值计算展示态
- 返回前端可直接消费的展示模型

建议评估器接口示意：

```csharp
DeviceDisplayState Evaluate(Device device, DateTime utcNow)
```

### 2. 引入展示 DTO

不要把数据库实体直接原样暴露给前端，而是返回带展示态的 DTO，例如：

- `DeviceViewModel`

建议字段：

- 保留 `Device` 当前已有字段
- 新增：
  - `RawStatus`
  - `DisplayStatus`
  - `Bucket`
  - `AbnormalReason`
  - `HeartbeatAgeSeconds`
  - `IsHeartbeatStale`
  - `IsSwitchingTooLong`

其中 `Status` 可在过渡期继续保留，但应逐步让前端改读 `DisplayStatus`。

### 3. 统一 REST 入口

以下接口改为通过统一评估器返回展示态：

- `GET /api/device`
- `GET /api/device/{id}`
- `GET /api/device/stats`

其中 `GetStats` 的异常数量必须基于统一 `Bucket` 统计，而不是单独用 `LastHeartbeat < cutoff` 再算一遍。

### 4. 统一 SignalR 入口

`BotHub` 在广播 `DeviceUpdated` 时，不再直接广播数据库实体，而是广播带展示态的视图模型。

这样可以确保：

- SignalR 推送看到的状态
- 手动刷新 REST 看到的状态

完全一致。

### 5. Watchdog 与展示规则对齐

`DeviceWatchdog` 继续负责真正的离线落库和告警，但规则与统一评估器保持一致：

- 离线阈值使用同一常量
- 告警与首页异常含义一致

同时，watchdog 不再承担“前端展示逻辑”的解释职责，只承担巡检和告警职责。

## 前端改造设计

### 1. 类型扩展

在 `hearthbot-web/src/types.ts` 中为设备类型新增展示态字段：

- `rawStatus`
- `displayStatus`
- `bucket`
- `abnormalReason`
- `heartbeatAgeSeconds`
- `isHeartbeatStale`
- `isSwitchingTooLong`

### 2. dashboardState 收口

`hearthbot-web/src/utils/dashboardState.ts` 不再自己根据 `lastHeartbeat` 重新推导异常。

它的职责改为：

- 读取后端返回的 `bucket`
- 做极薄的兼容兜底

兼容策略：

- 如果新字段存在，完全信任后端
- 如果新字段不存在，再退回旧逻辑

这样可以避免前后端发布顺序不同导致页面异常。

### 3. 卡片与详情页展示

`DeviceStatusCard.vue`、详情抽屉等组件统一使用：

- `displayStatus` 渲染状态标签
- `abnormalReason` 渲染异常文案

不再本地判断“`Switching` 就是异常”。

### 4. 分组和统计一致

首页 Tab 数量、卡片过滤、详情页标签、顶部异常统计都只基于后端统一展示态。

这样可以保证：

- 卡片标签是什么
- 它落在哪个分组
- 统计里算不算异常

三者始终一致。

## BotMain 侧约束

BotMain 保持“上报原始事实”的职责，不承接云控展示逻辑。

`DeviceStatusCollector` 继续按当前规则上报：

- `InGame`：对局中
- `Running`：脚本运行中但当前不在对局
- `Idle`：空闲
- `Switching`：切号中
- `Offline`：游戏进程不在且非切号流程

本次不要求 BotMain 增加新的展示态字段，也不要求它知道首页分组。

## 接口兼容策略

为了避免前后端不同步导致发布期抖动，采用渐进兼容方案：

1. 后端先新增展示字段，保留旧字段
2. 前端优先读取新字段，读取不到时回退旧逻辑
3. 确认全部上线稳定后，再决定是否逐步淡化旧字段的语义

这样可以降低一次性切换风险。

## 验证方案

### 后端单元测试

至少覆盖以下场景：

1. `InGame` 且心跳新鲜
   预期：`active`，状态为 `InGame`
2. `Running` 且心跳新鲜
   预期：有单时为 `active`，无单时为 `pending`
3. `Switching` 且未超阈值
   预期：不进异常
4. `Switching` 超阈值
   预期：进入 `abnormal`，理由为 `SwitchingTooLong`
5. 心跳超过离线阈值
   预期：进入 `abnormal`，状态为 `Offline`
6. `IsCompleted = true`
   预期：始终进 `completed`

### 前端验证

至少验证：

- 首页卡片标签与分组一致
- 刷新前后设备不会在 `active / abnormal` 之间无理由切换
- `Switching` 短时停留仍留在正常分组
- 顶部异常数量与异常 Tab 数量一致

## 风险与回滚

### 主要风险

- 前后端发布顺序不同，导致部分设备缺少新字段
- 异常统计数字会因为规则调整而发生变化
- 旧前端若继续使用旧逻辑，短期内仍会看到不一致

### 缓解方式

- 保留旧字段并做前端兼容读取
- 阈值常量集中管理，避免多处散落
- 用单元测试锁住关键分桶规则

### 回滚策略

若新规则出现误判，可先只回滚前端到旧展示逻辑；若问题在统一评估器规则本身，则回滚后端状态评估改动即可。

## 成功标准

改造完成后应满足以下标准：

1. 正常运行设备不再在 `进行中 / 异常 / 离线` 之间来回跳
2. 同一台设备的卡片标签、所在分组、异常统计保持一致
3. `Switching` 短时视为正常流程，仅在持续过久时进入异常
4. SignalR 实时推送与手动刷新看到的状态一致
5. 后续若需要调整阈值或异常标准，只需修改后端统一评估器
