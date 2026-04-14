# Choice系统API与协议映射

## 目标

本次重构将炉石脚本原有的 `Discover` 专线统一为通用 `Choice` 子系统，用于覆盖以下游戏内选择界面：

- 发现 `DISCOVER`
- 抉择 `CHOOSE_ONE`
- 泰坦技能 `TITAN_ABILITY`
- 时间线 `TIMELINE`
- Dredge `DREDGE`
- Adapt `ADAPT`
- 酒馆战棋饰品发现 `TRINKET_DISCOVER`
- 酒馆战棋商店类选择 `SHOP_CHOICE`
- 星舰发射/中止 `STARSHIP_LAUNCH`
- 其他仍由 `ChoiceCardMgr` / `GameState` 驱动的通用选择

## 游戏内API

### 主要原生入口

- `GameState.GetFriendlyEntityChoices()`
  - 读取当前友方 Choice 包
  - 提供 `ID/Id`、`ChoiceType`、`CountMin`、`CountMax`、`Entities`

- `GameState.SendChoices()`
  - 原生提交已选实体
  - 对应网络层 `Network.SendChoices(choiceId, entityIds)`

- `ChoiceCardMgr.GetFriendlyChoiceState()`
  - 读取当前友方 ChoiceState
  - 关键字段：
    - `m_waitingToStart`
    - `m_hasBeenRevealed`
    - `m_hasBeenConcealed`
    - `m_sourceEntityId`
    - `m_chosenEntities`
    - `m_isSubOptionChoice`
    - `m_isTitanAbility`
    - `m_isMagicItemDiscover`
    - `m_isShopChoice`
    - `m_isLaunchpadAbility`
    - `m_isRewindChoice`

- `ChoiceCardMgr.m_lastShownChoiceState`
  - 某些 UI 场景下可作为 `GetFriendlyChoiceState()` 的兜底

- `ChoiceCardMgr.ShowSubOptions(...)`
  - 展开抉择/泰坦/星舰等子选项

- `InputManager.HandleClickOnSubOption(...)`
  - 子选项点击执行入口

## 模式判定优先级

payload 内部统一按以下顺序判定 `mode`：

1. `TIMELINE`
   - 同时包含 `TIME_000ta` 与 `TIME_000tb`
2. `TITAN_ABILITY`
   - `isSubOption=true` 且 `isTitanAbility=true`
3. `STARSHIP_LAUNCH`
   - `isLaunchpadAbility=true`
4. `TRINKET_DISCOVER`
   - `isMagicItemDiscover=true`
5. `SHOP_CHOICE`
   - `isShopChoice=true`
6. `DISCOVER` / `DREDGE` / `ADAPT`
   - 来自 `ChoiceType` 或来源实体 tag/mechanic
7. `CHOOSE_ONE`
   - 来自 `ChoiceType` 或来源实体 `CHOOSE_ONE`
8. `GENERAL`

## 管道协议

### 读取状态

- 命令：`GET_CHOICE_STATE`
- 返回：
  - `NO_CHOICE`
  - `CHOICE:{json}`

### 提交选择

- 命令：`APPLY_CHOICE:{snapshotId}:{entityIdsCsv}`
- 示例：
  - `APPLY_CHOICE:8d1d...:123`
  - `APPLY_CHOICE:8d1d...:123,456`

### `CHOICE:{json}` 字段

- `snapshotId`
  - 当前 choice 快照签名的 SHA1
  - 用于提交流程中的防陈旧校验
- `choiceId`
  - 原生 `EntityChoices.ID`
- `mode`
- `rawChoiceType`
- `sourceEntityId`
- `sourceCardId`
- `countMin`
- `countMax`
- `isReady`
- `readyReason`
- `isSubOption`
- `isTitanAbility`
- `isRewindChoice`
- `isMagicItemDiscover`
- `isShopChoice`
- `isLaunchpadAbility`
- `uiShown`
- `selectedEntityIds`
- `options`
  - 数组元素字段：
    - `entityId`
    - `cardId`
    - `selected`

## Ready 判定

统一 ready 逻辑同时检查：

- ChoiceState 存在
- `m_waitingToStart=false`
- `m_hasBeenRevealed=true`
- `m_hasBeenConcealed=false`
- 选项实体已成功解析
- 对应 UI 卡面 actor 已 ready
- 不存在活动 tween
- 能取得屏幕坐标

未就绪时会通过 `readyReason` 返回具体原因，例如：

- `waiting_to_start`
- `not_revealed`
- `concealed`
- `card_missing:{entityId}`
- `actor_not_ready:{entityId}`
- `card_tween_active:{entityId}`
- `pos_not_found:{entityId}`

## 提交策略

### 鼠标优先

以下模式默认优先鼠标点卡并确认界面关闭或切换：

- `DISCOVER`
- `DREDGE`
- `ADAPT`
- `TIMELINE`
- `GENERAL` 单选卡牌式界面

### 网络提交优先

以下模式优先走 `SendChoices/Network.SendChoices`：

- `TRINKET_DISCOVER`
- `SHOP_CHOICE`
- 任意 `countMax > 1` 的多选界面

### 子选项类界面

以下模式统一通过 `APPLY_CHOICE` 暴露给上层，但 payload 内部会结合当前快照和已有执行能力处理：

- `CHOOSE_ONE`
- `TITAN_ABILITY`
- `STARSHIP_LAUNCH`
- 其他 `isSubOption=true` 的界面

## 上层推荐层

`BotMain` 已统一到 `ChoiceRecommendationRequest/Result`：

- 请求包含：
  - `SnapshotId`
  - `ChoiceId`
  - `Mode`
  - `SourceCardId`
  - `SourceEntityId`
  - `CountMin`
  - `CountMax`
  - `Options`
  - `SelectedEntityIds`
  - `Seed`
  - `MinimumUpdatedAtMs`
  - `LastConsumedUpdatedAtMs`

- 返回包含：
  - `SelectedEntityIds`
  - `Detail`
  - `SourceUpdatedAtMs`

`DiscoverCC` 仍保留，只在发现类模式下复用：

- `DISCOVER`
- `DREDGE`
- `ADAPT`
- `TIMELINE`

## 已知说明

- 目前仓库里仍保留少量历史 discover 私有实现作为内部兼容壳，但主协议和主链路已经切到通用 `Choice`
- `OPTION|...` 动作通道保留，用于正常回合动作规划，不与新的 `APPLY_CHOICE` 冲突
