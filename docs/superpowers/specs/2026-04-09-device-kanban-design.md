# 设备总览看板设计

## 概述

将现有的设备表格式总览重构为**订单看板视图**，以接单工作流为核心，实时展示每个订单的段位进度和对局状态。

## 核心概念

- **视角转换**：从"设备为中心"转为"订单为中心"，设备作为执行资源
- **一台设备 = 一个账号 = 一个订单**：设备上线自动出现在看板，手动填写订单号完成标记
- **轻量订单管理**：只需填订单号，其余信息（账号、段位、模式等）从 BotMain 心跳自动获取

## 看板三列结构

### 1. 未标记列

设备已上线、Bot已运行，但尚未填写订单号的条目。

- 卡片显示：账号名、设备名、当前段位、游戏模式、运行状态
- 卡片内嵌**订单号输入框**，填写后自动移入"进行中"列
- 触发条件：设备上线且心跳状态为 Online/InGame/Idle

### 2. 进行中列

已标记订单号、正在代练的订单。宽度略大于其他两列（flex: 1.3）。

**卡片信息：**
- 订单号（粗体）
- 运行状态标签：对局中（绿）、空闲（蓝）、离线（灰）
- 账号名 + 设备名
- **段位进度条**：起始段位 → 当前段位 → 目标段位，百分比渐变色
- **当前对局信息框**（仅对局中显示）：己方卡组 vs 对手职业、对局时长、本次胜负统计、连胜/连败

### 3. 今日完成列

达到目标段位的订单，当天显示，**隔天自动归档**到对局记录页面。

- 卡片显示：订单号、账号名、起止段位、总战绩（胜负/胜率）、总耗时、使用设备
- 整体略透明（opacity: 0.85），与进行中区分

## 顶部统计栏

四个统计卡片横排：

| 指标 | 来源 |
|------|------|
| 设备在线数/总数 | DeviceWatchdog 心跳状态 |
| 今日对局数 | GameRecord 按日统计 |
| 今日胜率 | GameRecord 胜场/总场 |
| 今日完成数 | 已达标订单计数 |

## 卡片展开详情

点击任意进行中/未标记的卡片，展开为详情面板，包含三个区域：

### 左侧 - 基本信息 + 操作

**基本信息（网格布局）：**
- 账号、设备、模式、卡组、策略、订单号（可编辑）

**操作按钮：**
- 停止 Bot（红色）
- 切换卡组（下拉选择，数据来自 availableDecksJson）

### 右侧 - 段位进度

- 大号进度条：起始 → 当前 → 目标
- 四个数字指标：胜场、负场、胜率、当前连胜/连败

### 底部 - 内嵌对局记录

当前订单的对局记录表格，字段：
- 结果（胜/负，带颜色）
- 我方职业
- 对手职业
- 段位变化
- 对局时长
- 时间

默认显示最近5条，"加载更多"按需展开，无需跳转到对局记录页面。

## 数据流

### 现有数据源（无需新增）

所有展示数据已通过现有心跳机制获取：
- `Device` 模型：status, currentAccount, currentRank, currentDeck, currentProfile, gameMode, sessionWins, sessionLosses, orderNumber
- `GameRecord` 模型：result, myClass, opponentClass, rankBefore, rankAfter, durationSeconds, playedAt
- SignalR 事件：DeviceUpdated, DeviceOnline, DeviceOffline, NewGameRecord

### 需要新增/调整的数据

| 数据 | 说明 | 存储位置 |
|------|------|----------|
| targetRank | 目标段位 | Device 模型新增字段，由 BotMain 心跳上报 |
| startRank | 本次起始段位 | Device 模型新增字段，首次心跳时记录 |
| startedAt | 本次开始时间 | Device 模型新增字段，首次心跳时记录 |
| currentOpponentClass | 当前对手职业 | HeartbeatData 新增字段，对局中上报 |
| currentGameDuration | 当前对局时长 | 前端根据对局开始时间计算，或心跳上报 |
| streak | 当前连胜/连败 | 前端根据最近 GameRecord 计算 |

### 订单生命周期

```
设备上线（心跳到达）
  → 自动创建/更新卡片 → 进入"未标记"列
    → 用户填写订单号 → 调用 PUT /api/device/{id}/order-number
      → 移入"进行中"列
        → 达到目标段位（使用 rankMapping 数值比较，currentRank >= targetRank）
          → 移入"今日完成"列
            → 次日 0:00 → 自动归档（从看板移除，对局记录页可查）
```

### 实时更新机制

沿用现有架构：
- SignalR `DeviceUpdated` 事件驱动卡片更新
- SignalR `NewGameRecord` 事件更新对局记录列表和统计
- 每60秒全量同步兜底

## 前端组件结构

```
Dashboard.vue (重构)
├── StatsBar.vue          — 顶部四个统计卡片
├── KanbanBoard.vue       — 三列看板容器
│   ├── KanbanColumn.vue  — 单列（未标记/进行中/已完成）
│   └── OrderCard.vue     — 订单卡片（折叠态）
│       ├── RankProgress.vue   — 段位进度条
│       └── GameStatus.vue     — 当前对局状态
└── OrderDetail.vue       — 卡片展开详情面板
    ├── OrderInfo.vue     — 基本信息 + 操作
    ├── RankDetail.vue    — 段位进度大图
    └── GameHistory.vue   — 内嵌对局记录表
```

## 后端改动

### Device 模型新增字段

```csharp
public string TargetRank { get; set; }       // 目标段位
public string StartRank { get; set; }        // 本次起始段位
public DateTime? StartedAt { get; set; }     // 本次开始时间
public string CurrentOpponent { get; set; }  // 当前对手职业（对局中）
```

### HeartbeatData 新增字段

```csharp
public string TargetRank;         // 目标段位
public string CurrentOpponent;    // 当前对手职业
```

### 归档逻辑

在 `DeviceWatchdog` 或新建定时服务中：
- 每日 0:00 检查已完成订单
- 已完成且非当天的订单，清除设备上的订单号和起始信息
- 对局记录已持久化在 GameRecord 表中，无需额外归档

## 边界情况

- **设备离线又上线**：订单号和进度保留（数据持久化在 Device 模型），卡片恢复到原来的列
- **startRank 记录时机**：设备上线且 orderNumber 为空时，首次心跳的 currentRank 记为 startRank；如果已有 startRank 且 orderNumber 不变，不覆盖
- **已完成但设备仍在线**：卡片移入"今日完成"列，不再显示对局状态；如果 Bot 仍在运行（用户没停），前端仍可展开详情操作

## 不做的事

- 不做拖拽排序或拖拽分配设备
- 不做订单的创建/删除 CRUD（订单只是一个标签）
- 不做价格、客户联系方式等业务字段
- 不做离线设备的展示（离线设备不在看板上）
- 不改变现有的对局记录页面和账号统计页面
