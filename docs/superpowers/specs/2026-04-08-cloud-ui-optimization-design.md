# 云控前端优化：白屏修复 + 对局记录页改版

## 概述

两个改动：
1. Dashboard 和 GameRecords 页面加骨架屏，消除白屏等待
2. 对局记录页从"账号筛选+表格"改为"设备列视图"，直接展示每台设备的最近20场战绩

## 一、白屏优化

### 问题

两个页面在 `onMounted` 中发起多个并行 API 请求，数据返回前页面完全空白。部署在海外服务器，网络延迟放大等待感。

### 方案

使用 naive-ui 的 `NSkeleton` 组件，在数据加载期间显示骨架屏占位。

#### Dashboard 骨架屏

- 添加 `loading` ref，初始 `true`，`loadData()` 完成后设为 `false`
- 4个统计卡片区域：显示 `NSkeleton` 占位
- 设备表格区域：显示若干行 `NSkeleton` 占位

#### GameRecords 骨架屏

- 添加 `loading` ref，初始 `true`，首次数据加载完成后设为 `false`
- 设备列区域：显示若干列 `NSkeleton` 占位

### 影响范围

- `hearthbot-web/src/views/Dashboard.vue` — 添加骨架屏
- `hearthbot-web/src/views/GameRecords.vue` — 添加骨架屏

## 二、对局记录页改版

### 删除的功能

- 账号下拉筛选框
- 设备下拉筛选框
- 结果筛选框（胜/负/投降）
- 时间范围筛选框
- 统计卡片（总场次/胜场/负场/胜率）
- 图表（职业对阵胜率、每日胜率趋势、段位变化）
- 分页对局记录表格

### 新增的功能：设备列视图

#### 布局

- 横向排列，每台设备占一列
- 列之间用竖线（border-right）分隔
- 4-6台设备自动等分宽度，不横向滚动
- 如果设备超出屏幕宽度，允许横向滚动

#### 每列内容

**头部：**
- 设备名（displayName）
- 账号名 · 段位
- 胜负统计：`{wins}W {losses}L ({winRate}%)`，胜率带颜色（≥55%绿、≥45%黄、<45%红）

**对局列表（最近20场）：**
每行显示：
- 胜负标记：W（绿）/ L（红）/ C（黄，投降）
- 对手职业
- 卡组名
- 用时（m:ss 格式）
- 段位变化（如 `钻5→钻4`），仅段位发生变化时显示

### 后端新接口

#### `GET /api/gamerecord/by-device`

一次性返回所有设备各自的最近20场记录和统计。

**响应格式：**
```json
[
  {
    "deviceId": "xxx",
    "displayName": "设备A",
    "currentAccount": "Player1",
    "currentRank": "钻石5",
    "totalGames": 20,
    "wins": 12,
    "losses": 6,
    "concedes": 2,
    "winRate": 60.0,
    "records": [
      {
        "result": "Win",
        "opponentClass": "法师",
        "deckName": "奥秘法",
        "durationSeconds": 201,
        "rankBefore": "钻石5",
        "rankAfter": "钻石4",
        "playedAt": "2026-04-08T10:30:00Z"
      }
    ]
  }
]
```

**实现逻辑：**
1. 从 Device 表获取所有设备（含 displayName、currentAccount、currentRank）
2. 对每台设备查询 GameRecord 最近20条（按 PlayedAt 降序）
3. 对每台设备的20条记录计算胜负统计
4. 组装返回

### 实时更新

- 保留 SignalR `NewGameRecord` 事件监听
- 收到事件后重新调用 `by-device` 接口刷新整个视图

### 影响范围

- `HearthBot.Cloud/Controllers/GameRecordController.cs` — 新增 `by-device` 接口
- `hearthbot-web/src/api/index.ts` — 新增 API 调用
- `hearthbot-web/src/views/GameRecords.vue` — 完全重写模板和逻辑
