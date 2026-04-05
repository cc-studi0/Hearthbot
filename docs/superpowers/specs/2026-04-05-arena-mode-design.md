# 竞技场自动模式设计文档

日期：2026-04-05

## 概述

为 Hearthbot 新增竞技场（Arena）完整自动循环模式。Bot 跟随炉石盒子（HSBox）的 CEF 页面推荐完成选职业、选牌、对局内出牌，并自动处理购票、排队、结算、领奖、重开。

## 需求摘要

- 完整自动循环：买票 → 选职业 → 30轮选牌 → 排队 → 对局 → 结算 → 领奖 → 重开
- 选职业/选牌：跟随盒子推荐（CEF 页面 `/client-jipaiqi/arena` 的 `window.fRecommend` 数据）
- 对局内出牌：跟随盒子推荐（CEF 页面 `/client-jipaiqi/ai-recommend` 的 `window.onUpdateArenaRecommend` 回调）
- 支付优先级：票 → 金币（可配置开关） → 停止
- 停止条件：票用完且金币开关关闭，或金币低于保底阈值
- 竞技场当前规则：2负或12胜结束一轮

## 实现方案

采用方案 A：在现有 BotService 主循环中扩展，与战旗模式（`_modeIndex == 100`）的组织方式一致。

## 状态机设计

竞技场自动循环是一个状态机，在 `BotService.cs` 的 `_modeIndex == 2` 分支中运行：

```
ARENA_START → ARENA_CHECK → ARENA_BUY → ARENA_HERO_PICK → ARENA_CARD_DRAFT
                  ↑                                              ↓
                  │                                        ARENA_QUEUE
                  │                                              ↓
                  │                                        ARENA_MATCH（复用天梯出牌逻辑）
                  │                                              ↓
                  │                                        ARENA_SETTLE
                  │                                         ↓         ↓
                  │                                   未结束轮次   轮次结束
                  │                                   回QUEUE    ARENA_REWARDS
                  │                                                  ↓
                  └──────────────────────────────────────────────────┘
```

### 状态说明

| 状态 | 职责 |
|------|------|
| ARENA_START | 进入竞技场场景（SceneNavigator 导航到 DRAFT） |
| ARENA_CHECK | 查询 `ARENA_GET_STATUS` 判断当前竞技场状态，路由到对应阶段 |
| ARENA_BUY | 购票。票优先，票用完检查金币开关和保底阈值 |
| ARENA_HERO_PICK | 从盒子获取职业推荐，选评分最高的职业 |
| ARENA_CARD_DRAFT | 30轮选牌，每轮从盒子获取3选1推荐，选评分最高的卡 |
| ARENA_QUEUE | 点击开始匹配，等待进入对局 |
| ARENA_MATCH | 对局内出牌，复用天梯 HSBox 跟随逻辑 |
| ARENA_SETTLE | 对局结算，关闭弹窗，检查轮次是否结束 |
| ARENA_REWARDS | 领取奖励，关闭弹窗，回到 ARENA_CHECK |

### 停止条件

在 ARENA_BUY 阶段检查：
1. 有票 → 用票，继续
2. 无票 + `ArenaUseGold == true` + 金币 >= (150 + `ArenaGoldReserve`) → 用金币，继续
3. 否则 → 停止循环，日志输出原因

## 选牌桥接：HsBoxArenaDraftBridge

### 职责

通过 CDP 连接盒子的 `/client-jipaiqi/arena` 页面，捕获选牌推荐数据。

### 数据捕获机制

与现有 `HsBoxRecommendationBridge` 同模式：

1. **GetDebuggerUrl**：从 `http://127.0.0.1:9222/json/list` 找到 URL 包含 `/client-jipaiqi/arena` 的页面
2. **Hook 注入**：通过 `Runtime.evaluate` 包装 `window.fRecommend`，拦截盒子推送的选牌数据
3. **状态读取**：轮询读取捕获的数据

### Hook 脚本

```javascript
const original = window.fRecommend;
window.fRecommend = function(hero, guideList, cardStage) {
    window.__hbArenaDraftHero = hero;
    window.__hbArenaDraftGuideList = guideList;
    window.__hbArenaDraftCardStage = cardStage;
    window.__hbArenaDraftUpdatedAt = Date.now();
    window.__hbArenaDraftCount++;
    return original.apply(this, arguments);
};
```

### 读取脚本返回结构

```json
{
    "ok": true,
    "hero": { ... },
    "guideList": [ ... ],
    "cardStage": 15,
    "updatedAt": 1712345678000,
    "count": 15
}
```

### 选牌决策

从 `guideList` 中取盒子评分最高的卡，通过 Payload 端 `ARENA_PICK_CARD:<index>` 命令选中。

## 对局内出牌

复用现有天梯出牌逻辑，区别：

| 项目 | 天梯 | 竞技场 |
|------|------|--------|
| 目标页面 | `/client-jipaiqi/ladder-opp` | `/client-jipaiqi/ai-recommend` |
| Hook 回调 | `onUpdateLadderActionRecommend` | `onUpdateArenaRecommend` |
| 留牌 | 复用 | 复用（盒子推荐同样通过 ai-recommend 页面下发） |

实现方式：在现有 `HsBoxRecommendationBridge` 中增加模式参数，根据 `_modeIndex` 选择目标页面和回调名。

## 结算流程

```
1. 检测对局结束（GET_SEED 返回 NO_GAME 或 ENDGAME_PENDING）
2. CLICK_DISMISS — 关闭结算弹窗
3. 等待返回竞技场界面
4. ARENA_GET_STATUS 检查：
   - DRAFT_COMPLETE → 还没结束，回到 ARENA_QUEUE
   - REWARDS → 轮次结束，进入领奖
   - NO_DRAFT → 轮次已自动结束，回到 ARENA_CHECK
5. REWARDS 处理：
   - ARENA_CLAIM_REWARDS — 点击领取
   - CLICK_DISMISS — 关闭奖励弹窗
   - 回到 ARENA_CHECK
```

## 错误恢复

复用现有 Watchdog 机制，不需要额外的竞技场专用恢复逻辑：
- 场景卡死超时 → 重启游戏
- 管道断连 → 重连
- 对话框弹出 → `DialogAutoDismissPatch` 自动关闭

## 改动文件清单

| 文件 | 改动内容 |
|------|---------|
| `BotMain/BotService.cs` | 新增 `_modeIndex == 2` 的竞技场主循环状态机 |
| `BotMain/HsBoxRecommendationProvider.cs` | Bridge 支持模式切换（arena 回调名 + 目标页面），新增 `HsBoxArenaDraftBridge` 内部类 |
| `BotMain/MainViewModel.cs` | 新增 `ArenaUseGold`、`ArenaGoldReserve` 配置属性 |
| `BotMain/MainWindow.xaml` | UI 上新增竞技场金币开关和保底阈值设置 |
| `HearthstonePayload/SceneNavigator.cs` | 新增竞技场状态查询命令处理 |
| `HearthstonePayload/ActionExecutor.cs` | 新增竞技场操作命令处理 |

### 不改动的文件

- `ArenaCC/` — 不再使用本地评分逻辑，全部跟随盒子
- `DiscoverCC/Arena/` — 同上
- 现有天梯/战旗逻辑 — 零影响

## 新增管道命令

| 命令 | 方向 | 返回 |
|------|------|------|
| `ARENA_GET_STATUS` | BotMain→Payload | `NO_DRAFT` / `HERO_PICK` / `CARD_DRAFT:N/30` / `DRAFT_COMPLETE` / `REWARDS` |
| `ARENA_GET_TICKET_INFO` | BotMain→Payload | `TICKETS:N\|GOLD:M` |
| `ARENA_BUY_TICKET` | BotMain→Payload | `OK:BUY` / `ERROR:reason` |
| `ARENA_GET_HERO_CHOICES` | BotMain→Payload | `HEROES:classId1,classId2,classId3` |
| `ARENA_PICK_HERO:<index>` | BotMain→Payload | `OK:HERO_PICKED` |
| `ARENA_GET_DRAFT_CHOICES` | BotMain→Payload | `CHOICES:cardId1,cardId2,cardId3` |
| `ARENA_PICK_CARD:<index>` | BotMain→Payload | `OK:CARD_PICKED` |
| `ARENA_CLAIM_REWARDS` | BotMain→Payload | `OK:CLAIMED` |

## BotMain 配置项

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `ArenaUseGold` | bool | false | 是否允许用金币购买竞技场门票 |
| `ArenaGoldReserve` | int | 0 | 金币保底阈值，低于此值停止消耗金币 |
