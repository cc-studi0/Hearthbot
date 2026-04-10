using System;
using System.Collections;
using System.Collections.Generic;
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
                if (loadingScreen != null && IsUnityObjectActive(loadingScreen))
                {
                    return new OverlayStatus
                    {
                        Action = OverlayAction.Wait,
                        Type = "LoadingScreen",
                        Detail = "scene_transition"
                    };
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
                    try
                    {
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
                    catch { }
                }

                return OverlayStatus.NoDialog;
            });
        }

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
                var enabledProp = obj.GetType().GetProperty("enabled");
                if (enabledProp != null)
                    return (bool)enabledProp.GetValue(obj);
            }
            catch { }
            return true; // 无法确定时假设活跃
        }
    }
}
