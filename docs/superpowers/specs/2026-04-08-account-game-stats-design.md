# 账号级对局统计功能设计

## 概述

在云控对局记录页面增加按账号维度的统计汇总功能。选择账号后展示基础统计、职业对阵胜率、段位变化曲线和每日胜率趋势。

## 后端 API

### 1. `GET /api/gamerecord/accounts`

返回去重的账号列表，供前端下拉选择。

**参数**：
- `deviceId`（可选）：按设备筛选

**响应**：
```json
["Player#1234", "Alt#5678"]
```

**实现**：从 `GameRecords` 表 `SELECT DISTINCT AccountName`，过滤空值。

### 2. `GET /api/gamerecord/stats`

返回指定账号的聚合统计数据。

**参数**：
- `accountName`（必填）：账号名
- `days`（默认7）：时间范围天数
- `deviceId`（可选）：按设备筛选

**响应**：
```json
{
  "accountName": "Player#1234",
  "totalGames": 120,
  "wins": 70,
  "losses": 40,
  "concedes": 10,
  "winRate": 58.3,
  "matchups": [
    { "opponentClass": "Warlock", "games": 20, "wins": 12, "winRate": 60.0 }
  ],
  "rankHistory": [
    { "date": "2026-04-07", "rank": "Diamond 5" }
  ],
  "dailyTrend": [
    { "date": "2026-04-07", "games": 15, "wins": 9, "winRate": 60.0 }
  ]
}
```

**聚合逻辑**：
- `totalGames / wins / losses / concedes`：按 Result 字段 COUNT
- `winRate`：`wins / totalGames * 100`，保留一位小数
- `matchups`：按 `OpponentClass` GROUP BY，计算每组胜场和胜率
- `rankHistory`：按日期 GROUP BY，取每日最后一条记录的 `RankAfter`
- `dailyTrend`：按日期 GROUP BY，计算每日场次和胜率

## 前端改造

### 页面布局（GameRecords.vue）

改造现有页面，从上到下：

1. **筛选栏**：在现有筛选器左边加**账号下拉框**（数据来自 `/accounts` 接口）
2. **统计卡片区**（选了账号后显示）：一行4个卡片
   - 总场次（蓝）、胜场（绿）、负场（红）、胜率（根据高低变色）
3. **图表区**（两列并排）：
   - 左：**职业对阵胜率** — 水平条形图，每个对手职业一行
   - 右：**每日胜率趋势** — 折线图，X轴日期，Y轴胜率%
4. **段位变化折线图** — 单独一行，X轴日期，Y轴段位数值
5. **对局记录表** — 现有表格不变，跟随账号筛选联动

### 交互逻辑

- 未选账号：只显示筛选栏 + 对局记录表（兼容现有行为）
- 选了账号：统计区展开，表格自动按该账号过滤
- 切换天数范围：统计和表格同步刷新

### 图表库

使用 Chart.js + vue-chartjs。

### 段位数值映射

前端负责将段位文本映射为数值用于折线图绘制：

| 段位 | 数值范围 |
|------|---------|
| Bronze 10-1 | 1-10 |
| Silver 10-1 | 11-20 |
| Gold 10-1 | 21-30 |
| Platinum 10-1 | 31-40 |
| Diamond 10-1 | 41-50 |
| Legend | 51 |

映射函数：解析段位文本，计算 `tierBase + (11 - starLevel)`。Legend 统一为 51。

## 文件变更清单

| 文件 | 变更 |
|------|------|
| `HearthBot.Cloud/Controllers/GameRecordController.cs` | 新增 `GetAccounts` 和 `GetStats` 两个端点 |
| `hearthbot-web/src/api/index.ts` | 新增 `getAccounts()` 和 `getStats()` 接口 |
| `hearthbot-web/src/views/GameRecords.vue` | 加账号下拉、统计卡片、图表区 |
| `hearthbot-web/package.json` | 新增 chart.js + vue-chartjs 依赖 |

## 不做的事

- 不改 GameRecord 数据模型
- 不改数据库 schema
- 不改设备端上报逻辑
- Legend 排名不做细分
