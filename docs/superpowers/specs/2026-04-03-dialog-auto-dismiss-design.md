# 弹窗自动关闭设计 — Harmony Hook 方案

## 问题

当前脚本在游戏内出现弹窗时会卡住，无法继续操作。原因有三：

1. **GAMEPLAY 场景完全不检测弹窗** — `SceneNavigator.GetBlockingDialog()` 在 GAMEPLAY 场景直接返回 `NO_DIALOG`
2. **非白名单按钮的弹窗只记日志不处理** — BotService 检测到弹窗但按钮文本不在安全白名单内时，仅等待重试
3. **轮询机制有延迟** — 1.5-2.5 秒的轮询间隔，弹窗出现后反应慢

## 方案

使用 Harmony Postfix Hook 拦截弹窗显示方法，弹窗一出现就触发自动关闭，不依赖轮询。

## 技术细节

### 游戏弹窗继承结构

```
DialogBase (基类, MonoBehaviour)
├── AlertPopup          — 最常见的弹窗（确定/取消/确认）
├── ReconnectHelperDialog — 重连弹窗（重连/取消/返回）
└── 其他子类            — 赛季奖励、竞技场、隐私政策等
```

关键：游戏内选择界面（发现、抉择、泰坦技能等）走 `ChoiceCardMgr` 系统，**不是** `DialogBase` 子类，不会被误触。

### Hook 点

| Hook 目标 | 类型 | 作用 |
|-----------|------|------|
| `AlertPopup.Show()` | Postfix | 捕获最常见的弹窗，直接调用 `ButtonPress(Response.OK)` 关闭 |
| `DialogBase.Show()` | Postfix | 兜底捕获其他类型弹窗，通知现有机制处理 |

### AlertPopup 自动关闭策略

`AlertPopup.Show()` 的 Postfix 中：

1. 延迟约 0.5 秒（等待弹窗动画完成，避免操作异常）
2. 通过反射调用 `AlertPopup` 的私有方法 `ButtonPress(Response.OK)` 直接关闭
3. 不走鼠标模拟，更可靠
4. 记录日志：弹窗类型、按钮文本、关闭时间

```csharp
// AlertPopup 反编译关键结构：
public class AlertPopup : DialogBase
{
    public enum Response { OK, CONFIRM, CANCEL }
    private PopupInfo m_popupInfo;           // 弹窗信息
    public UIBButton m_okayButton;           // 确定按钮
    public UIBButton m_confirmButton;        // 确认按钮
    public UIBButton m_cancelButton;         // 取消按钮
    private void ButtonPress(Response response) { ... }  // 私有关闭方法
}
```

关闭优先级：
- 有 OK 按钮 → `ButtonPress(Response.OK)`
- 有 CONFIRM 按钮 → `ButtonPress(Response.CONFIRM)`
- 只有 CANCEL → `ButtonPress(Response.CANCEL)`

判断依据：读取 `m_popupInfo.m_responseDisplay` 枚举值（OK / CONFIRM / CANCEL / CONFIRM_CANCEL / NONE）。

### DialogBase 兜底策略

`DialogBase.Show()` 的 Postfix 中：

1. 检查实际类型是否为 `AlertPopup`（已由上面的 Hook 处理，跳过）
2. 对其他 `DialogBase` 子类，设置一个标志 `HasPendingDialog = true`
3. 现有的 `SceneNavigator.GetBlockingDialog()` 检查该标志，触发现有的按钮搜索和关闭逻辑
4. 同时移除 GAMEPLAY 场景的跳过限制，让兜底机制在所有场景生效

### 安全机制

- **类型白名单**：只有 `AlertPopup` 会被直接自动关闭；其他类型走现有的按钮白名单检测
- **延迟执行**：使用 Unity 协程延迟 0.5 秒，避免弹窗动画未完成时操作
- **开关控制**：通过 Entry.cs 的静态字段控制是否启用自动关闭，可通过 IPC 命令动态开关
- **日志记录**：每次自动关闭都记录弹窗类型和关闭方式，便于调试

## 改动文件

| 文件 | 改动 |
|------|------|
| `HearthstonePayload/DialogAutoDismissPatch.cs` | **新增** — Harmony 补丁类，Hook AlertPopup.Show() 和 DialogBase.Show() |
| `HearthstonePayload/Entry.cs` | **修改** — 注册新补丁，添加 `TOGGLE_AUTO_DISMISS` IPC 命令 |
| `HearthstonePayload/SceneNavigator.cs` | **修改** — 移除 GAMEPLAY 场景跳过限制，添加 `HasPendingDialog` 标志检查 |

## 不改动的部分

- BotMain 端不需要改动 — Payload 端自行处理弹窗
- BotProtocol 白名单保持不变 — 兜底机制仍使用
- 现有的 `GET_BLOCKING_DIALOG` / `DISMISS_BLOCKING_DIALOG` 命令保持不变 — 作为手动触发的备选方案
