# OverlayDetector 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 用炉石引擎管理器单例的直接查询替代对象图遍历+关键词匹配，实现精准的覆盖层检测与关闭。

**Architecture:** 新增 `OverlayDetector` 类做分层检测（InputManager → LoadingScreen → DialogManager → UniversalInputManager → PopupDisplayManager），在 `SceneNavigator` 的 `GetBlockingDialog` / `DismissBlockingDialog` 开头插入调用，失败回退现有逻辑。BotProtocol 新增 WAIT/FATAL/DISMISSED 协议，BotService 按类型做不同决策。

**Tech Stack:** C# / .NET 8.0 / Reflection / xUnit

---

### Task 1: OverlayDetector — 反射缓存与 OverlayStatus 数据结构

**Files:**
- Create: `HearthstonePayload/OverlayDetector.cs`

- [ ] **Step 1: 创建 OverlayStatus 枚举和结构体 + 反射缓存初始化**

```csharp
// HearthstonePayload/OverlayDetector.cs
using System;
using System.Collections;
using System.Reflection;

namespace HearthstonePayload
{
    public sealed class OverlayDetector
    {
        public enum OverlayAction
        {
            None,        // 无覆盖层
            CanDismiss,  // 可安全关闭
            Wait,        // 不可关闭，等待
            Fatal        // 致命，应停止脚本
        }

        public sealed class OverlayStatus
        {
            public static readonly OverlayStatus NoDialog = new OverlayStatus
            {
                Action = OverlayAction.None,
                Type = "NO_DIALOG",
                Detail = string.Empty
            };

            public OverlayAction Action { get; set; }
            public string Type { get; set; }
            public string Detail { get; set; }

            public string ToProtocol()
            {
                if (Action == OverlayAction.None)
                    return "NO_DIALOG";
                string actionStr = Action switch
                {
                    OverlayAction.CanDismiss => "CAN_DISMISS",
                    OverlayAction.Wait => "WAIT",
                    OverlayAction.Fatal => "FATAL",
                    _ => "WAIT"
                };
                return "DIALOG:" + Type + ":" + actionStr + ":" + (Detail ?? string.Empty);
            }
        }

        // 可安全关闭的 DialogBase 子类
        private static readonly HashSet<string> DismissableDialogTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AlertPopup", "BasicPopup", "CardListPopup", "MultiPagePopup",
            "FriendlyChallengeDialog", "BattlegroundsInviteDialog", "BattlegroundsSuggestDialog",
            "PrivacyFeaturesPopup", "FreeArenaWinDialog", "LuckyDrawEventDialog",
            "CompletedQuestsUpdatedPopup", "OutstandingDraftTicketDialog"
        };

        // 致命状态的弹窗类型
        private static readonly HashSet<string> FatalDialogTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // ReconnectHelperDialog 的某些状态是 fatal，但类型名本身不能判断
            // 需要后续在检测时检查其状态字段
        };

        // 不可关闭，需要等待的弹窗类型
        private static readonly HashSet<string> WaitDialogTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SeasonEndDialog", "InitialDownloadDialog", "ExistingAccountPopup",
            "PrivacyPolicyPopup", "GenericConfirmationPopup",
            "LeaguePromoteSelfManuallyDialog", "MercenariesSeasonRewardsDialog",
            "MercenariesZoneUnlockDialog"
        };

        private readonly Assembly _asm;
        private readonly Func<Func<object>, object> _runOnMain;
        private readonly Action<string> _log;
        private bool _initialized;

        // DialogManager 缓存
        private Type _dialogManagerType;
        private MethodInfo _dialogManagerGet;
        private FieldInfo _dialogManagerCurrentDialog;
        private FieldInfo _dialogManagerRequests;
        private MethodInfo _dialogManagerGoBack;

        // UniversalInputManager 缓存
        private Type _universalInputManagerType;
        private MethodInfo _universalInputManagerGet;
        private FieldInfo _uimSystemDialogActive;
        private FieldInfo _uimGameDialogActive;
        private FieldInfo _uimNotificationDialogActive;

        // InputManager 缓存
        private Type _inputManagerType;
        private MethodInfo _inputManagerGet;
        private FieldInfo _inputManagerCheckForInput;

        // LoadingScreen 缓存
        private Type _loadingScreenType;
        private MethodInfo _loadingScreenGet;

        // PopupDisplayManager 缓存
        private Type _popupDisplayManagerType;
        private FieldInfo _popupDisplayManagerIsShowing;

        public OverlayDetector(Assembly asm, Func<Func<object>, object> runOnMain, Action<string> log)
        {
            _asm = asm ?? throw new ArgumentNullException(nameof(asm));
            _runOnMain = runOnMain ?? throw new ArgumentNullException(nameof(runOnMain));
            _log = log ?? (_ => { });
        }

        public bool Init()
        {
            if (_initialized) return true;

            var bf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

            // DialogManager
            _dialogManagerType = _asm.GetType("DialogManager");
            if (_dialogManagerType != null)
            {
                _dialogManagerGet = _dialogManagerType.GetMethod("Get", BindingFlags.Public | BindingFlags.Static);
                _dialogManagerCurrentDialog = _dialogManagerType.GetField("m_currentDialog", bf);
                _dialogManagerRequests = _dialogManagerType.GetField("m_dialogRequests", bf);
                _dialogManagerGoBack = _dialogManagerType.GetMethod("GoBack", bf);
            }

            // UniversalInputManager
            _universalInputManagerType = _asm.GetType("UniversalInputManager");
            if (_universalInputManagerType != null)
            {
                _universalInputManagerGet = _universalInputManagerType.GetMethod("Get", BindingFlags.Public | BindingFlags.Static);
                _uimSystemDialogActive = _universalInputManagerType.GetField("m_systemDialogActive", bf);
                _uimGameDialogActive = _universalInputManagerType.GetField("m_gameDialogActive", bf);
                _uimNotificationDialogActive = _universalInputManagerType.GetField("m_notificationDialogActive", bf);
            }

            // InputManager
            _inputManagerType = _asm.GetType("InputManager");
            if (_inputManagerType != null)
            {
                _inputManagerGet = _inputManagerType.GetMethod("Get", BindingFlags.Public | BindingFlags.Static);
                _inputManagerCheckForInput = _inputManagerType.GetField("m_checkForInput", bf);
            }

            // LoadingScreen
            _loadingScreenType = _asm.GetType("LoadingScreen");
            if (_loadingScreenType != null)
            {
                _loadingScreenGet = _loadingScreenType.GetMethod("Get", BindingFlags.Public | BindingFlags.Static);
            }

            // PopupDisplayManager
            _popupDisplayManagerType = _asm.GetType("PopupDisplayManager");
            if (_popupDisplayManagerType != null)
            {
                _popupDisplayManagerIsShowing = _popupDisplayManagerType.GetField("s_isShowing", bf);
            }

            // 至少 DialogManager 可用才算初始化成功
            _initialized = _dialogManagerType != null && _dialogManagerGet != null && _dialogManagerCurrentDialog != null;
            if (!_initialized)
                _log("[OverlayDetector] 初始化失败：DialogManager 反射不可用");
            return _initialized;
        }

        private object CallStatic(MethodInfo method)
        {
            if (method == null) return null;
            try { return method.Invoke(null, null); }
            catch { return null; }
        }

        private object GetField(FieldInfo field, object instance)
        {
            if (field == null || instance == null) return null;
            try { return field.GetValue(instance); }
            catch { return null; }
        }

        private bool GetBoolField(FieldInfo field, object instance)
        {
            var val = GetField(field, instance);
            return val is bool b && b;
        }
    }
}
```

- [ ] **Step 2: 提交**

```bash
git add HearthstonePayload/OverlayDetector.cs
git commit -m "feat: OverlayDetector 反射缓存与 OverlayStatus 数据结构"
```

---

### Task 2: OverlayDetector — DetectBlockingOverlay() 检测逻辑

**Files:**
- Modify: `HearthstonePayload/OverlayDetector.cs`

- [ ] **Step 1: 实现 DetectBlockingOverlay 方法**

在 `OverlayDetector` 类末尾（`GetBoolField` 方法之后，类的 `}` 之前）添加：

```csharp
        public OverlayStatus DetectBlockingOverlay()
        {
            return (OverlayStatus)_runOnMain(() =>
            {
                if (!Init())
                    return null; // 返回 null 表示检测失败，调用方回退到旧逻辑

                // 优先级1: 全局输入开关
                var inputMgr = CallStatic(_inputManagerGet);
                if (inputMgr != null && _inputManagerCheckForInput != null)
                {
                    if (!GetBoolField(_inputManagerCheckForInput, inputMgr))
                    {
                        return new OverlayStatus
                        {
                            Action = OverlayAction.Fatal,
                            Type = "InputDisabled",
                            Detail = "InputManager.m_checkForInput=false"
                        };
                    }
                }

                // 优先级2: 场景过渡
                var loadingScreen = CallStatic(_loadingScreenGet);
                if (loadingScreen != null)
                {
                    // LoadingScreen.Get() 返回非 null 且对象存在即表示正在过渡
                    // 检查它是否真的在活跃状态（gameObject.activeSelf）
                    if (IsUnityObjectActive(loadingScreen))
                    {
                        return new OverlayStatus
                        {
                            Action = OverlayAction.Wait,
                            Type = "LoadingScreen",
                            Detail = "scene_transition"
                        };
                    }
                }

                // 优先级3: DialogManager 当前弹窗
                var dialogMgr = CallStatic(_dialogManagerGet);
                if (dialogMgr != null)
                {
                    var currentDialog = GetField(_dialogManagerCurrentDialog, dialogMgr);
                    if (currentDialog != null)
                    {
                        var typeName = currentDialog.GetType().Name;
                        var action = ClassifyDialogType(typeName);
                        return new OverlayStatus
                        {
                            Action = action,
                            Type = typeName,
                            Detail = action.ToString()
                        };
                    }

                    // 优先级4: 队列中有等待的弹窗
                    var requests = GetField(_dialogManagerRequests, dialogMgr);
                    if (requests is ICollection col && col.Count > 0)
                    {
                        return new OverlayStatus
                        {
                            Action = OverlayAction.Wait,
                            Type = "DialogLoading",
                            Detail = $"queue_count={col.Count}"
                        };
                    }
                }

                // 优先级5: UniversalInputManager 标志位
                var uim = CallStatic(_universalInputManagerGet);
                if (uim != null)
                {
                    bool sys = GetBoolField(_uimSystemDialogActive, uim);
                    bool game = GetBoolField(_uimGameDialogActive, uim);
                    bool notif = GetBoolField(_uimNotificationDialogActive, uim);
                    if (sys || game || notif)
                    {
                        return new OverlayStatus
                        {
                            Action = OverlayAction.Wait,
                            Type = "InputBlocked",
                            Detail = $"sys={sys},game={game},notif={notif}"
                        };
                    }
                }

                // 优先级6: PopupDisplayManager
                if (_popupDisplayManagerIsShowing != null)
                {
                    // s_isShowing 是静态字段
                    var val = _popupDisplayManagerIsShowing.GetValue(null);
                    if (val is bool showing && showing)
                    {
                        return new OverlayStatus
                        {
                            Action = OverlayAction.Wait,
                            Type = "PopupDisplay",
                            Detail = "rewards_or_achievements"
                        };
                    }
                }

                return OverlayStatus.NoDialog;
            });
        }

        private OverlayAction ClassifyDialogType(string typeName)
        {
            if (DismissableDialogTypes.Contains(typeName))
                return OverlayAction.CanDismiss;
            if (WaitDialogTypes.Contains(typeName))
                return OverlayAction.Wait;
            // 未知类型默认尝试关闭（GoBack 是安全操作）
            return OverlayAction.CanDismiss;
        }

        private bool IsUnityObjectActive(object obj)
        {
            if (obj == null) return false;
            try
            {
                // 尝试获取 gameObject.activeSelf
                var goProperty = obj.GetType().GetProperty("gameObject");
                if (goProperty != null)
                {
                    var go = goProperty.GetValue(obj);
                    if (go != null)
                    {
                        var activeSelf = go.GetType().GetProperty("activeSelf");
                        if (activeSelf != null)
                            return (bool)activeSelf.GetValue(go);
                    }
                }
                // 如果对象本身就是 MonoBehaviour，检查 enabled
                var enabledProp = obj.GetType().GetProperty("enabled");
                if (enabledProp != null)
                    return (bool)enabledProp.GetValue(obj);
            }
            catch { }
            return true; // 无法确定时假设活跃
        }
```

注意：还需要在文件顶部添加 `using System.Collections;`（如果 Task 1 中没有加的话，已经加了）以及 `using System.Collections.Generic;`。

在文件顶部 using 区域确保包含：
```csharp
using System.Collections.Generic;
```

同时在类中 `FatalDialogTypes` 声明后，添加 `HashSet` 的 using：在已有的 `DismissableDialogTypes` 定义中已经使用了 `HashSet<string>`，需要确保 `using System.Collections.Generic;` 存在。

- [ ] **Step 2: 提交**

```bash
git add HearthstonePayload/OverlayDetector.cs
git commit -m "feat: OverlayDetector.DetectBlockingOverlay() 分层检测逻辑"
```

---

### Task 3: OverlayDetector — DismissOverlay() 关闭逻辑

**Files:**
- Modify: `HearthstonePayload/OverlayDetector.cs`

- [ ] **Step 1: 实现 DismissOverlay 方法**

在 `OverlayDetector` 类中 `IsUnityObjectActive` 方法之后添加：

```csharp
        public string DismissOverlay()
        {
            var status = DetectBlockingOverlay();
            if (status == null)
                return null; // 检测失败，调用方回退到旧逻辑

            if (status.Action == OverlayAction.None)
                return "FAIL:NO_DIALOG";

            if (status.Action == OverlayAction.Wait)
                return "WAIT:" + status.Type;

            if (status.Action == OverlayAction.Fatal)
                return "FATAL:" + status.Type;

            // CAN_DISMISS: 尝试 GoBack
            _log($"[OverlayDetector] 尝试关闭 {status.Type} via GoBack()");

            var goBackResult = _runOnMain(() =>
            {
                var dialogMgr = CallStatic(_dialogManagerGet);
                if (dialogMgr == null || _dialogManagerGoBack == null)
                    return (object)"FAIL:no_dialog_manager";
                try
                {
                    _dialogManagerGoBack.Invoke(dialogMgr, null);
                    return (object)"INVOKED";
                }
                catch (Exception ex)
                {
                    return (object)("FAIL:goback_exception:" + ex.Message);
                }
            });

            var goBackStr = goBackResult as string ?? "FAIL:null_result";
            if (!string.Equals(goBackStr, "INVOKED", StringComparison.Ordinal))
            {
                _log($"[OverlayDetector] GoBack 失败: {goBackStr}");
                return null; // 回退到旧逻辑
            }

            // 等待 150ms 让关闭动画开始
            System.Threading.Thread.Sleep(150);

            // 验证是否关闭成功
            var after = DetectBlockingOverlay();
            if (after == null || after.Action == OverlayAction.None)
            {
                _log($"[OverlayDetector] 成功关闭 {status.Type}");
                return "DISMISSED:" + status.Type;
            }

            // GoBack 后弹窗仍在（可能是动画未完成，或者 GoBack 不适用）
            _log($"[OverlayDetector] GoBack 后仍有覆盖层 {after.Type}，回退到旧逻辑");
            return null; // 回退到旧逻辑
        }
```

- [ ] **Step 2: 提交**

```bash
git add HearthstonePayload/OverlayDetector.cs
git commit -m "feat: OverlayDetector.DismissOverlay() 关闭逻辑（GoBack + 回退）"
```

---

### Task 4: SceneNavigator 集成 — GetBlockingDialog + DismissBlockingDialog

**Files:**
- Modify: `HearthstonePayload/SceneNavigator.cs:14-30`（新增字段）
- Modify: `HearthstonePayload/SceneNavigator.cs:810-848`（两个方法）

- [ ] **Step 1: SceneNavigator 新增 OverlayDetector 字段和初始化**

在 `SceneNavigator.cs` 约第 29 行（`private Func<Func<object>, object> _runOnMain;` 之后）添加字段：

```csharp
        private OverlayDetector _overlayDetector;
```

在 `Init()` 方法中（现有初始化逻辑的末尾、`_initialized = true;` 之前），添加 OverlayDetector 初始化：

```csharp
                if (_overlayDetector == null)
                    _overlayDetector = new OverlayDetector(_asm, _runOnMain, msg => { /* SceneNavigator 无独立日志，静默 */ });
                _overlayDetector.Init();
```

需要先找到 `Init()` 方法中 `_initialized = true;` 的位置来确定插入点。

- [ ] **Step 2: 改造 GetBlockingDialog()**

将 `SceneNavigator.cs:810-821` 的 `GetBlockingDialog()` 方法替换为：

```csharp
        public string GetBlockingDialog()
        {
            // 优先使用 OverlayDetector（基于管理器单例）
            if (_overlayDetector != null)
            {
                try
                {
                    var status = _overlayDetector.DetectBlockingOverlay();
                    if (status != null)
                        return status.ToProtocol();
                }
                catch { /* 回退到旧逻辑 */ }
            }

            // 回退：旧的对象图遍历逻辑
            return OnMain(() =>
            {
                if (!Init()) return "NO_DIALOG";

                var hit = FindBlockingDialogHit();
                return hit == null
                    ? "NO_DIALOG"
                    : "DIALOG:" + hit.DialogType + ":" + (hit.ButtonLabel ?? string.Empty);
            });
        }
```

- [ ] **Step 3: 改造 DismissBlockingDialog()**

将 `SceneNavigator.cs:823-848` 的 `DismissBlockingDialog()` 方法替换为：

```csharp
        public string DismissBlockingDialog()
        {
            // 优先使用 OverlayDetector
            if (_overlayDetector != null)
            {
                try
                {
                    var result = _overlayDetector.DismissOverlay();
                    if (result != null)
                        return result;
                    // result == null 表示 OverlayDetector 需要回退
                }
                catch { /* 回退到旧逻辑 */ }
            }

            // 回退：旧的按钮查找+点击逻辑
            var hit = OnMain(() =>
            {
                if (!Init()) return null;
                return FindBlockingDialogHit();
            });

            if (hit == null)
                return "FAIL:NO_DIALOG:no_dialog";
            if (string.Equals(hit.DialogType, StartupRatingsDialogType, StringComparison.OrdinalIgnoreCase))
                return DismissStartupRatings();
            if (!hit.CanDismiss)
                return "FAIL:" + hit.DialogType + ":unsafe_button:" + (string.IsNullOrWhiteSpace(hit.ButtonLabel) ? "UNKNOWN" : hit.ButtonLabel);
            if (hit.ButtonX <= 0 || hit.ButtonY <= 0)
                return "FAIL:" + hit.DialogType + ":button_pos";

            var clickResult = ClickAt(hit.ButtonX, hit.ButtonY, 0.35f);
            if (!string.IsNullOrWhiteSpace(clickResult)
                && clickResult.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
            {
                return "OK:" + hit.DialogType + ":" + (hit.ButtonLabel ?? string.Empty);
            }

            return "FAIL:" + hit.DialogType + ":" + (string.IsNullOrWhiteSpace(clickResult) ? "click_failed" : clickResult);
        }
```

- [ ] **Step 4: 提交**

```bash
git add HearthstonePayload/SceneNavigator.cs
git commit -m "feat: SceneNavigator 集成 OverlayDetector，失败回退旧逻辑"
```

---

### Task 5: BotProtocol 新增 WAIT/FATAL/DISMISSED 协议方法

**Files:**
- Modify: `BotMain/BotProtocol.cs:33-36`（常量区）
- Modify: `BotMain/BotProtocol.cs:376-406`（方法区）

- [ ] **Step 1: 编写测试**

在 `BotCore.Tests/BotProtocolTests.cs` 文件末尾（最后一个 `}` 之前，即类的 `}` 之前）添加：

```csharp
        [Fact]
        public void OverlayProtocol_ParsesNewFormatResponses()
        {
            // 新格式检测响应
            Assert.True(BotProtocol.IsBlockingDialogResponse("DIALOG:AlertPopup:CAN_DISMISS:CanDismiss"));
            Assert.True(BotProtocol.IsBlockingDialogResponse("DIALOG:LoadingScreen:WAIT:scene_transition"));
            Assert.True(BotProtocol.IsBlockingDialogResponse("DIALOG:InputDisabled:FATAL:InputManager.m_checkForInput=false"));

            // 新格式关闭响应
            Assert.True(BotProtocol.IsWaitOverlayResponse("WAIT:LoadingScreen"));
            Assert.True(BotProtocol.IsWaitOverlayResponse("WAIT:DialogLoading"));
            Assert.False(BotProtocol.IsWaitOverlayResponse("DISMISSED:AlertPopup"));
            Assert.False(BotProtocol.IsWaitOverlayResponse("OK:AlertPopup:OK"));

            Assert.True(BotProtocol.IsFatalOverlayResponse("FATAL:InputDisabled"));
            Assert.False(BotProtocol.IsFatalOverlayResponse("WAIT:LoadingScreen"));

            Assert.True(BotProtocol.IsDismissedOverlayResponse("DISMISSED:AlertPopup"));
            Assert.False(BotProtocol.IsDismissedOverlayResponse("OK:AlertPopup:OK"));

            // 新格式 TryParse 兼容
            Assert.True(BotProtocol.TryParseBlockingDialog(
                "DIALOG:AlertPopup:CAN_DISMISS:CanDismiss",
                out var dtype, out var dlabel));
            Assert.Equal("AlertPopup", dtype);
            // label 部分包含 CAN_DISMISS:CanDismiss
        }

        [Fact]
        public void OverlayProtocol_IsOverlayActionResponse_ClassifiesCorrectly()
        {
            Assert.True(BotProtocol.IsOverlayActionResponse("WAIT:LoadingScreen"));
            Assert.True(BotProtocol.IsOverlayActionResponse("FATAL:InputDisabled"));
            Assert.True(BotProtocol.IsOverlayActionResponse("DISMISSED:AlertPopup"));
            Assert.False(BotProtocol.IsOverlayActionResponse("OK:AlertPopup:OK"));
            Assert.False(BotProtocol.IsOverlayActionResponse("FAIL:NO_DIALOG:no_dialog"));
        }
```

- [ ] **Step 2: 运行测试，确认失败**

```bash
cd BotCore.Tests && dotnet test --filter "OverlayProtocol" -v n
```

预期：编译错误，`IsWaitOverlayResponse` / `IsFatalOverlayResponse` / `IsDismissedOverlayResponse` / `IsOverlayActionResponse` 不存在。

- [ ] **Step 3: 在 BotProtocol 中实现新方法**

在 `BotMain/BotProtocol.cs` 的 `IsSafeBlockingDialogButtonLabel` 方法之前（约第 413 行前），添加：

```csharp
        public static bool IsWaitOverlayResponse(string resp)
        {
            return !string.IsNullOrWhiteSpace(resp)
                && resp.StartsWith("WAIT:", StringComparison.Ordinal);
        }

        public static bool IsFatalOverlayResponse(string resp)
        {
            return !string.IsNullOrWhiteSpace(resp)
                && resp.StartsWith("FATAL:", StringComparison.Ordinal);
        }

        public static bool IsDismissedOverlayResponse(string resp)
        {
            return !string.IsNullOrWhiteSpace(resp)
                && resp.StartsWith("DISMISSED:", StringComparison.Ordinal);
        }

        public static bool IsOverlayActionResponse(string resp)
        {
            return IsWaitOverlayResponse(resp)
                || IsFatalOverlayResponse(resp)
                || IsDismissedOverlayResponse(resp);
        }
```

还需要让 `IsBlockingDialogResponse` 也能识别新格式。当前实现已经兼容（只检查 `DIALOG:` 前缀），无需修改。

同时让 `IsCrossCommandResponse` 识别新格式。在 `BotProtocol.cs` 的 `IsCrossCommandResponse` 方法中，找到判断链，在末尾追加：

```csharp
            if (IsOverlayActionResponse(resp))
                return true;
```

- [ ] **Step 4: 运行测试，确认通过**

```bash
cd BotCore.Tests && dotnet test --filter "OverlayProtocol" -v n
```

预期：2 个测试全部 PASS。

- [ ] **Step 5: 运行全部现有测试，确认无回归**

```bash
cd BotCore.Tests && dotnet test -v n
```

预期：全部 PASS（现有 `BlockingDialogResponses_ParseKnownPayload` 等测试不受影响）。

- [ ] **Step 6: 提交**

```bash
git add BotMain/BotProtocol.cs BotCore.Tests/BotProtocolTests.cs
git commit -m "feat: BotProtocol 新增 WAIT/FATAL/DISMISSED 覆盖层协议方法"
```

---

### Task 6: BotService 改造 — 弹窗检测/关闭支持新协议

**Files:**
- Modify: `BotMain/BotService.cs`（`TryGetBlockingDialog` 和 `TryDismissBlockingDialog` 方法，以及天梯/竞技场主循环中的调用点）

- [ ] **Step 1: 改造 TryDismissBlockingDialog 支持新响应类型**

当前 `TryDismissBlockingDialog`（约第 8449 行）只是发送命令并返回原始响应。不需要改这个方法本身，但需要改**调用方**的判断逻辑。

在天梯主循环的 IdleGuard 第二层（约第 2847-2866 行），将整个弹窗检测+关闭块替换为：

找到这段代码（天梯循环）：
```csharp
                            // ── IdleGuard 第二层：操作前弹窗检测与关闭 ──
                            if (!isEndTurn)
                            {
                                try
                                {
                                    if (TryGetBlockingDialog(pipe, 1500, out var preDialogType, out var preDialogButton, "IdleGuard.PreAction")
                                        && !string.IsNullOrWhiteSpace(preDialogType))
                                    {
                                        if (BotProtocol.IsSafeBlockingDialogButtonLabel(preDialogButton))
                                        {
                                            if (TryDismissBlockingDialog(pipe, 2000, out var dismissResp, "IdleGuard.PreAction")
                                                && !string.IsNullOrWhiteSpace(dismissResp)
                                                && dismissResp.StartsWith("OK:", StringComparison.OrdinalIgnoreCase))
                                            {
                                                Log($"[IdleGuard] 操作前检测到弹窗 {preDialogType}({preDialogButton})，已关闭 -> {dismissResp}");
                                                SleepOrCancelled(500);
                                            }
                                        }
                                        else
                                        {
                                            Log($"[IdleGuard] 操作前检测到弹窗 {preDialogType}({preDialogButton})，按钮不安全，跳过操作");
```

替换弹窗处理的判断逻辑，使其额外识别 DISMISSED/WAIT 响应。关键改动在 dismiss 结果判断处，将：

```csharp
                                                && dismissResp.StartsWith("OK:", StringComparison.OrdinalIgnoreCase))
```

替换为：

```csharp
                                                && (dismissResp.StartsWith("OK:", StringComparison.OrdinalIgnoreCase)
                                                    || BotProtocol.IsDismissedOverlayResponse(dismissResp)))
```

同样的替换需要在以下 4 处进行（天梯 PreAction、天梯 DialogBlock、竞技场 ArenaPreAction、竞技场 ArenaDialogBlock），所有 `dismissResp.StartsWith("OK:"` 判断处都追加 `|| BotProtocol.IsDismissedOverlayResponse(dismissResp)` 条件。

- [ ] **Step 2: DIALOG_BLOCKING 处理也识别新格式**

在天梯主循环的 DIALOG_BLOCKING 处理段（约第 2914-2931 行），将：

```csharp
                                        if (TryGetBlockingDialog(pipe, 1500, out var blockDialogType, out var blockDialogButton, "IdleGuard.DialogBlock")
                                            && !string.IsNullOrWhiteSpace(blockDialogType)
                                            && BotProtocol.IsSafeBlockingDialogButtonLabel(blockDialogButton))
                                        {
                                            TryDismissBlockingDialog(pipe, 2000, out _, "IdleGuard.DialogBlock");
                                            SleepOrCancelled(500);
                                        }
```

替换为（增加 WAIT/FATAL 处理）：

```csharp
                                        if (TryGetBlockingDialog(pipe, 1500, out var blockDialogType, out var blockDialogButton, "IdleGuard.DialogBlock")
                                            && !string.IsNullOrWhiteSpace(blockDialogType))
                                        {
                                            if (BotProtocol.IsSafeBlockingDialogButtonLabel(blockDialogButton))
                                            {
                                                TryDismissBlockingDialog(pipe, 2000, out _, "IdleGuard.DialogBlock");
                                                SleepOrCancelled(500);
                                            }
                                            else
                                            {
                                                Log($"[IdleGuard] 弹窗 {blockDialogType} 不可安全关闭，等待");
                                                SleepOrCancelled(1500);
                                            }
                                        }
```

同样修改竞技场循环的 ArenaDialogBlock 段（约第 3987-3992 行）。

- [ ] **Step 3: TryGetBlockingDialog 识别新格式的 dialogType**

当前 `TryGetBlockingDialog` 调用 `BotProtocol.TryParseBlockingDialog` 来解析。新格式 `DIALOG:AlertPopup:CAN_DISMISS:CanDismiss` 会被 `TryParseBlockingDialog` 解析为 `dialogType=AlertPopup`, `buttonLabel=CAN_DISMISS:CanDismiss`。

`IsSafeBlockingDialogButtonLabel("CAN_DISMISS:CanDismiss")` 会返回 false，但这不影响功能——因为 `DismissBlockingDialog` 现在由 OverlayDetector 处理，不再依赖按钮标签。

但为了安全，在 `BotProtocol.IsSafeBlockingDialogButtonLabel` 中增加对新格式 action 标签的识别。在该方法（约第 413 行）中追加：

```csharp
                || normalized == "can_dismiss";
```

添加在最后一个 `||` 条件后面。

- [ ] **Step 4: 运行全部测试**

```bash
cd BotCore.Tests && dotnet test -v n
```

预期：全部 PASS。

- [ ] **Step 5: 提交**

```bash
git add BotMain/BotService.cs BotMain/BotProtocol.cs
git commit -m "feat: BotService 支持 WAIT/FATAL/DISMISSED 覆盖层协议"
```

---

### Task 7: 集成测试 — 验证协议兼容性

**Files:**
- Modify: `BotCore.Tests/BotProtocolTests.cs`

- [ ] **Step 1: 添加协议兼容性测试**

在 `BotProtocolTests.cs` 末尾（类的 `}` 之前）添加：

```csharp
        [Fact]
        public void OverlayProtocol_BackwardCompatible_OldFormatStillWorks()
        {
            // 旧格式仍然正常工作
            Assert.True(BotProtocol.IsBlockingDialogResponse("DIALOG:AlertPopup:OK"));
            Assert.True(BotProtocol.TryParseBlockingDialog("DIALOG:AlertPopup:OK", out var dt, out var bl));
            Assert.Equal("AlertPopup", dt);
            Assert.Equal("OK", bl);
            Assert.True(BotProtocol.IsSafeBlockingDialogButtonLabel("OK"));

            // 旧格式 dismiss 响应
            Assert.False(BotProtocol.IsOverlayActionResponse("OK:AlertPopup:OK"));
            Assert.False(BotProtocol.IsOverlayActionResponse("FAIL:NO_DIALOG:no_dialog"));
        }

        [Fact]
        public void OverlayProtocol_NewDismissLabels_RecognizedAsSafe()
        {
            // CAN_DISMISS 作为按钮标签应被视为安全
            Assert.True(BotProtocol.IsSafeBlockingDialogButtonLabel("CAN_DISMISS"));
        }

        [Fact]
        public void OverlayProtocol_CrossCommand_RecognizesNewFormats()
        {
            Assert.True(BotProtocol.IsCrossCommandResponse("WAIT:LoadingScreen"));
            Assert.True(BotProtocol.IsCrossCommandResponse("FATAL:InputDisabled"));
            Assert.True(BotProtocol.IsCrossCommandResponse("DISMISSED:AlertPopup"));
        }
```

- [ ] **Step 2: 运行全部测试**

```bash
cd BotCore.Tests && dotnet test -v n
```

预期：全部 PASS。

- [ ] **Step 3: 提交**

```bash
git add BotCore.Tests/BotProtocolTests.cs
git commit -m "test: OverlayDetector 协议兼容性和集成测试"
```

---

### Task 8: 最终验证与推送

**Files:** 无新改动

- [ ] **Step 1: 运行全部测试确认无回归**

```bash
cd BotCore.Tests && dotnet test -v n
```

预期：全部 PASS。

- [ ] **Step 2: 检查编译（HearthstonePayload 项目）**

```bash
cd HearthstonePayload && dotnet build
```

注意：HearthstonePayload 可能依赖 Unity 程序集无法独立编译。如果编译失败，改为检查语法：

```bash
cd HearthstonePayload && dotnet build 2>&1 | head -20
```

如果因为缺少 Unity 引用而失败，这是预期行为——Payload 项目需要注入到游戏才能运行。

- [ ] **Step 3: 推送到远端**

```bash
git push
```
