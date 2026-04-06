# 弹窗自动关闭 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 通过 Harmony Hook 拦截游戏弹窗显示方法，弹窗一出现就自动关闭，解决脚本在弹窗出现时卡住的问题。

**Architecture:** 在 HearthstonePayload 中新增 Harmony Postfix 补丁，Hook `AlertPopup.Show()` 和 `DialogBase.Show()`。AlertPopup 通过反射直接调用 `ButtonPress(Response.OK)` 关闭；其他 DialogBase 子类通过标志通知现有机制处理。延迟执行通过 Entry.cs 的 `Update()` 帧驱动实现。

**Tech Stack:** C# / Harmony / 反射 / BepInEx

---

## 文件结构

| 文件 | 职责 |
|------|------|
| `HearthstonePayload/DialogAutoDismissPatch.cs` | **新增** — Harmony 补丁类，Hook AlertPopup.Show() 和 DialogBase.Show()，维护待关闭队列 |
| `HearthstonePayload/Entry.cs` | **修改** — 注册新补丁，在 Update() 中驱动延迟关闭逻辑 |
| `HearthstonePayload/SceneNavigator.cs` | **修改** — 移除 GAMEPLAY 场景跳过限制 |

---

### Task 1: 新增 DialogAutoDismissPatch.cs

**Files:**
- Create: `HearthstonePayload/DialogAutoDismissPatch.cs`

- [ ] **Step 1: 创建 DialogAutoDismissPatch.cs**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace HearthstonePayload
{
    /// <summary>
    /// Harmony 补丁：Hook DialogBase.Show() 和 AlertPopup.Show()，
    /// 弹窗出现时自动排队关闭，由 Entry.Update() 驱动执行。
    /// </summary>
    public static class DialogAutoDismissPatch
    {
        /// <summary>待关闭的弹窗队列（主线程访问，无需加锁）</summary>
        private static readonly Queue<PendingDismiss> _pending = new Queue<PendingDismiss>();

        /// <summary>是否启用自动关闭</summary>
        public static bool Enabled = true;

        private static Type _alertPopupType;
        private static Type _dialogBaseType;
        private static MethodInfo _buttonPressMethod;
        private static FieldInfo _popupInfoField;
        private static FieldInfo _responseDisplayField;
        private static Action<string> _log;

        private class PendingDismiss
        {
            public object Dialog;
            public float DismissAfterTime;
            public bool IsAlertPopup;
        }

        public static void Apply(Harmony harmony, Action<string> logCallback)
        {
            _log = logCallback;
            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                if (asm == null) return;

                _dialogBaseType = asm.GetType("DialogBase");
                _alertPopupType = asm.GetType("AlertPopup");
                if (_dialogBaseType == null) return;

                // 缓存 AlertPopup 反射成员
                if (_alertPopupType != null)
                {
                    _buttonPressMethod = _alertPopupType.GetMethod("ButtonPress",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    _popupInfoField = _alertPopupType.GetField("m_popupInfo",
                        BindingFlags.NonPublic | BindingFlags.Instance);

                    var popupInfoType = _alertPopupType.GetNestedType("PopupInfo");
                    if (popupInfoType != null)
                    {
                        _responseDisplayField = popupInfoType.GetField("m_responseDisplay",
                            BindingFlags.Public | BindingFlags.Instance);
                    }

                    // Hook AlertPopup.Show()
                    var alertShow = _alertPopupType.GetMethod("Show",
                        BindingFlags.Public | BindingFlags.Instance,
                        null, Type.EmptyTypes, null);
                    if (alertShow != null)
                    {
                        harmony.Patch(alertShow,
                            postfix: new HarmonyMethod(typeof(DialogAutoDismissPatch), nameof(AlertPopupShowPostfix)));
                        _log?.Invoke("[DialogAutoDismiss] Hooked AlertPopup.Show()");
                    }
                }

                // Hook DialogBase.Show() 作为兜底
                var baseShow = _dialogBaseType.GetMethod("Show",
                    BindingFlags.Public | BindingFlags.Instance,
                    null, Type.EmptyTypes, null);
                if (baseShow != null)
                {
                    harmony.Patch(baseShow,
                        postfix: new HarmonyMethod(typeof(DialogAutoDismissPatch), nameof(DialogBaseShowPostfix)));
                    _log?.Invoke("[DialogAutoDismiss] Hooked DialogBase.Show()");
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke("[DialogAutoDismiss] Apply failed: " + ex.Message);
            }
        }

        /// <summary>AlertPopup.Show() Postfix — 排队自动关闭</summary>
        private static void AlertPopupShowPostfix(object __instance)
        {
            if (!Enabled) return;
            try
            {
                _pending.Enqueue(new PendingDismiss
                {
                    Dialog = __instance,
                    DismissAfterTime = UnityEngine.Time.time + 0.5f,
                    IsAlertPopup = true
                });
                _log?.Invoke("[DialogAutoDismiss] AlertPopup queued for dismiss");
            }
            catch (Exception ex)
            {
                _log?.Invoke("[DialogAutoDismiss] AlertPopupShowPostfix error: " + ex.Message);
            }
        }

        /// <summary>DialogBase.Show() Postfix — 兜底捕获非 AlertPopup 弹窗</summary>
        private static void DialogBaseShowPostfix(object __instance)
        {
            if (!Enabled) return;
            try
            {
                // AlertPopup 已由专用 Hook 处理，跳过
                if (_alertPopupType != null && _alertPopupType.IsInstanceOfType(__instance))
                    return;

                _pending.Enqueue(new PendingDismiss
                {
                    Dialog = __instance,
                    DismissAfterTime = UnityEngine.Time.time + 0.8f,
                    IsAlertPopup = false
                });
                _log?.Invoke("[DialogAutoDismiss] DialogBase subclass queued: " + __instance.GetType().Name);
            }
            catch (Exception ex)
            {
                _log?.Invoke("[DialogAutoDismiss] DialogBaseShowPostfix error: " + ex.Message);
            }
        }

        /// <summary>
        /// 每帧由 Entry.Update() 调用，处理到期的待关闭弹窗。
        /// </summary>
        public static void Tick()
        {
            if (_pending.Count == 0) return;

            var now = UnityEngine.Time.time;
            while (_pending.Count > 0 && _pending.Peek().DismissAfterTime <= now)
            {
                var item = _pending.Dequeue();
                try
                {
                    if (item.Dialog == null || (item.Dialog is UnityEngine.Object uObj && uObj == null))
                        continue;

                    if (item.IsAlertPopup)
                        DismissAlertPopup(item.Dialog);
                    else
                        DismissGenericDialog(item.Dialog);
                }
                catch (Exception ex)
                {
                    _log?.Invoke("[DialogAutoDismiss] Tick dismiss error: " + ex.Message);
                }
            }
        }

        private static void DismissAlertPopup(object alertPopup)
        {
            if (_buttonPressMethod == null)
            {
                _log?.Invoke("[DialogAutoDismiss] ButtonPress method not found, cannot dismiss");
                return;
            }

            // 读取 m_popupInfo.m_responseDisplay 确定应该点哪个按钮
            var response = GetBestResponse(alertPopup);
            _buttonPressMethod.Invoke(alertPopup, new object[] { response });
            _log?.Invoke("[DialogAutoDismiss] AlertPopup dismissed with response: " + response);
        }

        /// <summary>
        /// 根据 AlertPopup.PopupInfo.m_responseDisplay 确定最佳响应。
        /// ResponseDisplay 枚举: NONE=0, OK=1, CONFIRM=2, CANCEL=3, CONFIRM_CANCEL=4
        /// Response 枚举: OK=0, CONFIRM=1, CANCEL=2
        /// </summary>
        private static object GetBestResponse(object alertPopup)
        {
            var responseType = _alertPopupType?.GetNestedType("Response");
            if (responseType == null)
                return 0; // fallback to OK

            try
            {
                if (_popupInfoField != null && _responseDisplayField != null)
                {
                    var popupInfo = _popupInfoField.GetValue(alertPopup);
                    if (popupInfo != null)
                    {
                        var display = (int)_responseDisplayField.GetValue(popupInfo);
                        // CONFIRM=2 → Response.CONFIRM(1), CANCEL=3 → Response.CANCEL(2)
                        // CONFIRM_CANCEL=4 → Response.CONFIRM(1), OK=1 or NONE=0 → Response.OK(0)
                        switch (display)
                        {
                            case 2: return Enum.ToObject(responseType, 1); // CONFIRM
                            case 3: return Enum.ToObject(responseType, 2); // CANCEL
                            case 4: return Enum.ToObject(responseType, 1); // CONFIRM_CANCEL → CONFIRM
                            default: return Enum.ToObject(responseType, 0); // OK
                        }
                    }
                }
            }
            catch { }

            return Enum.ToObject(responseType, 0); // fallback to OK
        }

        private static void DismissGenericDialog(object dialog)
        {
            // 尝试调用 GoBack() 方法（DialogBase 的公开虚方法）
            var goBackMethod = dialog.GetType().GetMethod("GoBack",
                BindingFlags.Public | BindingFlags.Instance,
                null, Type.EmptyTypes, null);
            if (goBackMethod != null)
            {
                goBackMethod.Invoke(dialog, null);
                _log?.Invoke("[DialogAutoDismiss] Generic dialog dismissed via GoBack(): " + dialog.GetType().Name);
                return;
            }

            _log?.Invoke("[DialogAutoDismiss] Cannot dismiss generic dialog: " + dialog.GetType().Name);
        }
    }
}
```

- [ ] **Step 2: 确认文件编译就绪**

确认文件位于 `HearthstonePayload/DialogAutoDismissPatch.cs`，命名空间为 `HearthstonePayload`，与现有补丁文件（AntiCheatPatches.cs, InactivityPatch.cs）一致。

---

### Task 2: 修改 Entry.cs — 注册补丁并驱动 Tick

**Files:**
- Modify: `HearthstonePayload/Entry.cs:64-69` (Awake 方法中注册补丁)
- Modify: `HearthstonePayload/Entry.cs:92-98` (Update 方法中驱动 Tick)

- [ ] **Step 1: 在 Awake() 中注册 DialogAutoDismissPatch**

在 `Entry.cs` 第 67 行 `InputHook.Apply(harmony);` 之后，添加注册：

```csharp
                InputHook.Apply(harmony);
                DialogAutoDismissPatch.Apply(harmony, msg => _logSource?.LogInfo(msg));
```

- [ ] **Step 2: 在 Update() 中调用 DialogAutoDismissPatch.Tick()**

在 `Entry.cs` 的 `Update()` 方法中，在 `_coroutine?.Tick(deltaTime);`（第 98 行）之后添加：

```csharp
            _coroutine?.Tick(deltaTime);
            DialogAutoDismissPatch.Tick();
```

- [ ] **Step 3: 添加 TOGGLE_AUTO_DISMISS IPC 命令**

在 `Entry.cs` 的命令分发区域（第 668-671 行 `DISMISS_BLOCKING_DIALOG` 之后），添加开关命令：

```csharp
            else if (cmd == "DISMISS_BLOCKING_DIALOG")
            {
                _pipe.Write(nav.DismissBlockingDialog());
            }
            else if (cmd == "TOGGLE_AUTO_DISMISS")
            {
                DialogAutoDismissPatch.Enabled = !DialogAutoDismissPatch.Enabled;
                _pipe.Write("AUTO_DISMISS:" + (DialogAutoDismissPatch.Enabled ? "ON" : "OFF"));
            }
```

- [ ] **Step 4: Commit**

```bash
git add HearthstonePayload/DialogAutoDismissPatch.cs HearthstonePayload/Entry.cs
git commit -m "feat: 添加弹窗自动关闭 Harmony Hook，支持 AlertPopup 直接关闭和 DialogBase 兜底"
```

---

### Task 3: 修改 SceneNavigator.cs — 移除 GAMEPLAY 场景限制

**Files:**
- Modify: `HearthstonePayload/SceneNavigator.cs:804-817` (GetBlockingDialog)
- Modify: `HearthstonePayload/SceneNavigator.cs:819-827` (DismissBlockingDialog)

- [ ] **Step 1: 移除 GetBlockingDialog() 中的 GAMEPLAY 跳过**

将 `SceneNavigator.cs` 第 804-817 行的 `GetBlockingDialog()` 方法修改为：

```csharp
        public string GetBlockingDialog()
        {
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

即删除以下两行：
```csharp
                if (string.Equals(GetSceneInternal(), "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
                    return "NO_DIALOG";
```

- [ ] **Step 2: 移除 DismissBlockingDialog() 中的 GAMEPLAY 跳过**

将 `SceneNavigator.cs` 第 819-827 行的 `DismissBlockingDialog()` 方法开头修改为：

```csharp
        public string DismissBlockingDialog()
        {
            var hit = OnMain(() =>
            {
                if (!Init()) return null;
                return FindBlockingDialogHit();
            });
```

即删除以下两行：
```csharp
                if (string.Equals(GetSceneInternal(), "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
                    return null;
```

- [ ] **Step 3: Commit**

```bash
git add HearthstonePayload/SceneNavigator.cs
git commit -m "fix: 移除弹窗检测的 GAMEPLAY 场景限制，所有场景均可检测和关闭弹窗"
```

---

### Task 4: 验证与集成测试

- [ ] **Step 1: 编译验证**

```bash
cd H:/桌面/炉石脚本/Hearthbot
dotnet build HearthstonePayload/HearthstonePayload.csproj -c Release
```

预期：编译成功，无错误。

- [ ] **Step 2: 检查 Harmony 补丁注册顺序**

确认 `Entry.cs` Awake() 中的补丁注册顺序为：
1. `AntiCheatPatches.Apply(harmony)` — 必须最先，防止反作弊
2. `InactivityPatch.Apply(harmony)` — 防止 AFK 断线
3. `InputHook.Apply(harmony)` — 输入模拟
4. `DialogAutoDismissPatch.Apply(harmony, ...)` — 弹窗自动关闭（最后注册即可）

- [ ] **Step 3: Commit 最终版本**

如果编译有问题，修复后提交：

```bash
git add -A
git commit -m "fix: 修复弹窗自动关闭编译问题"
```
