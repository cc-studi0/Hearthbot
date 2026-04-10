# OverlayDetector 设计文档 — 基于管理器单例的覆盖层检测与关闭

> 替换现有的对象图遍历 + 关键词模糊匹配，用炉石引擎自身的管理器 API 做精准检测和关闭。

## 问题

现有 `SceneNavigator.FindBlockingDialogHit()` 通过 `EnumerateObjectGraph` + `GenericBlockingDialogTokens`（dialog/popup/alert/reconnect）做弹窗检测：
- **漏检**：不在对象图中或类名不含关键词的覆盖层检测不到（如 LoadingScreen、InputManager 禁用）
- **误判**：非阻塞性 UI 元素（Toast、Banner）类名含关键词被误判为弹窗

## 方案

**分层检测 + 精准关闭（方案 C）**：
1. 用管理器单例的直接查询替代对象图遍历（检测层）
2. 按弹窗类型分发关闭策略（关闭层）
3. OverlayDetector 作为增强叠加在现有逻辑上，反射失败时回退到现有逻辑

---

## 炉石 UI 架构（反编译分析结果）

### 输入阻塞链（从高到低）

| 层级 | 管理器 | 关键 API | 含义 |
|------|--------|----------|------|
| 1 | `InputManager` | `m_checkForInput` | 全局输入开关（致命错误/场景过渡时关闭） |
| 2 | `UniversalInputManager` | `m_systemDialogActive` / `m_gameDialogActive` / `m_notificationDialogActive` | 三类对话框标志，任一为 true 就阻塞棋盘输入 |
| 3 | `DialogManager` | `m_currentDialog` + `m_dialogRequests` 队列 | 当前显示的弹窗实例和等待队列 |
| 4 | `PopupDisplayManager` | `s_isShowing` | 奖励/成就/任务弹窗显示中 |
| 5 | `LoadingScreen` | Phase 状态机 + blocker 计数 | 场景过渡期间 |

### DialogManager 核心机制

- **单例**：`DialogManager.Get()` → `s_instance`
- **队列**：`m_dialogRequests`（Queue\<DialogRequest\>），FIFO 顺序处理
- **当前弹窗**：`m_currentDialog`（DialogBase 引用）
- **状态查询**：`ShowingDialog()` → 有弹窗显示或排队中
- **通用关闭**：`GoBack()` → 调用当前弹窗的 `Hide()`，等价于用户按 ESC
- **生命周期**：请求入队 → 异步加载 Prefab → ProcessRequest → Show() → 用户交互 → Hide() → OnHideAnimFinished → Destroy → 处理下一个
- **输入阻塞**：`Show()` 时调用 `UniversalInputManager.SetSystemDialogActive(true)`，`OnHideAnimFinished()` 时设为 false

### DialogBase 子类（21 种）

| 类名 | 触发场景 | 按钮 | 可安全关闭 |
|------|----------|------|-----------|
| **AlertPopup** | 233 处调用：网络错误、匹配失败、卡组问题、功能限制等 | OK / 确认+取消 | 是 |
| **ReconnectHelperDialog** | 网络断开（8 种状态面板） | 确认+取消 / 更新 / 退出 | 视状态而定 |
| **FriendlyChallengeDialog** | 好友发起挑战 | 接受+拒绝 | 是（拒绝） |
| **BattlegroundsInviteDialog** | 战棋邀请 | 接受+拒绝 | 是（拒绝） |
| **BattlegroundsSuggestDialog** | 战棋建议邀请 | 接受+拒绝 | 是（拒绝） |
| **SeasonEndDialog** | 赛季结束（7 种 MODE） | 下一步/完成 | 否（等待动画） |
| **CardListPopup** | 卡牌变更通知 | 确定 | 是 |
| **MultiPagePopup** | 多页信息展示 | 下一步/完成 | 是 |
| **ExistingAccountPopup** | 首次登录 | 有账户/没账户 | 否（必须选） |
| **InitialDownloadDialog** | 首次下载资源 | 无 | 否（等待完成） |
| **PrivacyPolicyPopup** | 启动时 | 接受+拒绝 | 否（必须接受） |
| **PrivacyFeaturesPopup** | 隐私设置 | 取消+开启/关闭 | 是（取消） |
| **GenericConfirmationPopup** | 操作确认动画 | 无（自动隐藏） | 否（等待） |
| **FreeArenaWinDialog** | 竞技场免费赢局 | 确定 | 是 |
| **LuckyDrawEventDialog** | 幸运抽奖活动 | 解除 | 是 |
| **CompletedQuestsUpdatedPopup** | 任务定义更新 | 确定+翻页 | 是 |
| **BasicPopup** | 通用弹窗 | 取消+自定义 | 是 |
| **LeaguePromoteSelfManuallyDialog** | 等级手动晋升 | — | 否 |
| **MercenariesSeasonRewardsDialog** | 佣兵赛季奖励 | — | 否（等待动画） |
| **MercenariesZoneUnlockDialog** | 佣兵区域解锁 | — | 否 |
| **OutstandingDraftTicketDialog** | 未用竞技场门票 | — | 是 |

### 非 DialogBase 覆盖层

| 类名 | 阻塞输入 | 触发场景 | 可关闭 |
|------|---------|----------|--------|
| **SplashScreen** | 是 | 游戏启动 | 否（等待） |
| **LoadingScreen** | 是 | 场景转换 | 否（等待） |
| **MatchingPopupDisplay** | 是 | 匹配中 | 否（等待） |
| **LoadingPopupDisplay** | 是 | 游戏加载 | 否（等待） |
| **AppRatingsPopup** | 是 | 移动端评分 | 是 |
| **BannerPopup** | 是 | 事件横幅 | 是 |
| **WelcomeQuests** | 是 | 登录后 | 否（等待） |
| **QuestToast** | 否 | 任务完成 | 无需关闭 |
| **GameToast** | 否 | 游戏通知 | 无需关闭 |
| **SocialToast** | 否 | 好友/邀请 | 无需关闭 |
| **AchievementToast** | 否 | 成就解锁 | 无需关闭 |

### Bot 最常遇到的弹窗

开始游戏时的错误弹窗触发链路：
```
GameMgr.FindGame() → Network.FindGame()
  → 服务器返回错误 → GameMgr.OnBnetError()
  → FindGameState.BNET_ERROR
  → Error.AddWarningLoc("GLOBAL_ERROR_GENERIC_HEADER", "GLOBAL_ERROR_GAME_DENIED")
  → Error.ShowWarningDialog() → AlertPopup（标题"错误"，内容"无法开始游戏"，OK 按钮）
```

---

## 新增 OverlayDetector 类

### 位置

`HearthstonePayload/OverlayDetector.cs` — 新增文件

### 职责

1. 启动时一次性缓存管理器单例的 Type/FieldInfo/MethodInfo
2. 提供 `DetectBlockingOverlay()` 检测方法
3. 提供 `DismissOverlay()` 关闭方法
4. 返回结构化的 `OverlayStatus`

### 反射缓存目标

| 管理器 | 获取方式 | 缓存字段/方法 |
|--------|----------|--------------|
| `DialogManager` | `Assembly.GetType("DialogManager")` + `Get()` | `m_currentDialog`, `m_dialogRequests`, `GoBack()` |
| `UniversalInputManager` | 同上 | `m_systemDialogActive`, `m_gameDialogActive`, `m_notificationDialogActive` |
| `InputManager` | 同上 | `m_checkForInput` |
| `LoadingScreen` | 同上 | Phase 相关字段 |
| `PopupDisplayManager` | 同上 | `s_isShowing` |
| `AlertPopup` | 同上 | `m_okButton`, `m_confirmButton`, `m_cancelButton`, `GetInfo()` |

### DetectBlockingOverlay() 检测逻辑

```
优先级1: InputManager.m_checkForInput == false → FATAL("InputDisabled")
优先级2: LoadingScreen phase != INVALID → WAIT("LoadingScreen")
优先级3: DialogManager.m_currentDialog != null → DIALOG(typeName, canDismiss)
优先级4: DialogManager.m_dialogRequests.Count > 0 → WAIT("DialogLoading")
优先级5: UniversalInputManager 任一标志为 true → WAIT("InputBlocked")
优先级6: PopupDisplayManager.s_isShowing → WAIT("PopupDisplay")
默认: NO_DIALOG
```

### DismissOverlay() 关闭逻辑

```
1. DetectBlockingOverlay() → status
2. 如果 NO_DIALOG → 返回 "FAIL:NO_DIALOG"
3. 如果 WAIT/FATAL → 返回 "{action}:{type}"
4. 如果 CAN_DISMISS:
   a. 优先尝试 DialogManager.GoBack()
   b. 等待 100ms 后验证
   c. 如果仍存在 → 回退到按类型找按钮模拟点击
5. 返回 "DISMISSED:{type}" 或 "FAIL:{reason}"
```

### 关闭策略分类

**可安全关闭（GoBack 或点按钮）**：
- AlertPopup, BasicPopup, CardListPopup, MultiPagePopup
- FriendlyChallengeDialog（拒绝）, BattlegroundsInviteDialog（拒绝）
- PrivacyFeaturesPopup（取消）, FreeArenaWinDialog, CompletedQuestsUpdatedPopup

**不可关闭（等待）**：
- LoadingScreen, InitialDownloadDialog, SeasonEndDialog
- GenericConfirmationPopup, WelcomeQuests, PopupDisplayManager 奖励
- MatchingPopupDisplay, LoadingPopupDisplay

**致命（应停止脚本）**：
- InputManager 禁用（致命错误状态）
- ReconnectHelperDialog 的 BAD_VERSION / RESTART_REQUIRED 状态

---

## 协议改造

### 检测响应

```
现有: "NO_DIALOG" | "DIALOG:{type}:{buttonLabel}"
新增: "DIALOG:{type}:{CAN_DISMISS|WAIT|FATAL}:{detail}"
```

### 关闭响应

```
现有: "OK" | "FAIL:NO_DIALOG:no_dialog"
新增: "DISMISSED:{type}" | "WAIT:{type}" | "FATAL:{type}"
```

### BotService 决策

```
CAN_DISMISS → 调用关闭 → 继续操作
WAIT        → 不操作，1-2 秒后重试检测
FATAL       → 停止当前对局，触发重启流程
```

---

## 与现有代码的集成

### SceneNavigator 改造

不删除现有代码，在 `CheckBlockingDialog()` 和 `DismissBlockingDialog()` 开头插入 OverlayDetector 调用：

```
CheckBlockingDialog():
  1. 先调用 OverlayDetector.DetectBlockingOverlay()
  2. 返回非 NO_DIALOG → 直接返回新格式
  3. OverlayDetector 初始化失败 → 回退到现有对象图遍历逻辑

DismissBlockingDialog():
  1. 先调用 OverlayDetector.DismissOverlay()
  2. 成功 → 返回
  3. 失败 → 回退到现有按钮查找逻辑
```

### BotService 改造

`BotProtocol` 新增协议常量：
- `IsWaitOverlay(response)` — 检测 WAIT 前缀
- `IsFatalOverlay(response)` — 检测 FATAL 前缀
- `IsDismissedOverlay(response)` — 检测 DISMISSED 前缀

BotService 主循环中：
- WAIT → sleep 后重试，不计入 IdleGuard 空回合
- FATAL → 触发紧急停止
- DISMISSED → 继续正常操作

### ActionExecutor 改造

无需改动。现有的操作前 `GetBlockingDialog()` 检查继续工作，只是返回值更丰富。

---

## 不涉及的范围

- 非阻塞覆盖层（Toast、Banner 等）：不影响操作，无需检测
- NarrativeManager 叙事对话：不阻塞输入，无需处理
- 商城/购买弹窗：bot 不会触发购买流程
