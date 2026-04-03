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
