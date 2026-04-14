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
            None,             // 无覆盖层
            CanDismiss,       // 可安全关闭
            Wait,             // 不可关闭，等待
            Fatal,            // 致命，应停止脚本
            RestartRequired   // 弹窗文本指示必须重启炉石才能恢复
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
                string actionStr;
                switch (Action)
                {
                    case OverlayAction.CanDismiss:      actionStr = "CAN_DISMISS"; break;
                    case OverlayAction.Wait:            actionStr = "WAIT"; break;
                    case OverlayAction.Fatal:           actionStr = "FATAL"; break;
                    case OverlayAction.RestartRequired: actionStr = "RESTART_REQUIRED"; break;
                    default:                            actionStr = "WAIT"; break;
                }
                return "DIALOG:" + Type + ":" + actionStr + ":" + (Detail ?? string.Empty);
            }
        }

        // 弹窗正文文本可能出现在这些成员名上
        private static readonly string[] DialogBodyMemberNames =
        {
            "m_bodyText", "m_messageText", "m_headerText", "m_titleText",
            "m_body", "m_message", "m_text", "m_content", "m_description"
        };

        // 出现以下关键字即认为弹窗要求重启炉石客户端
        private static readonly string[] RestartRequiredKeywords =
        {
            "请重新启动",
            "重新启动后",
            "progress has been saved",
            "please restart",
            "需要重新启动"
        };

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
        private MethodInfo _loadingScreenIsTransitioning;

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
                _loadingScreenIsTransitioning = _loadingScreenType.GetMethod("IsTransitioning", BindingFlags.Public | BindingFlags.Instance);
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

                // 优先级2: 场景过渡（调用游戏自身的 IsTransitioning()，即 m_phase != INVALID）
                var loadingScreen = CallStatic(_loadingScreenGet);
                if (loadingScreen != null && _loadingScreenIsTransitioning != null)
                {
                    try
                    {
                        var transitioning = _loadingScreenIsTransitioning.Invoke(loadingScreen, null);
                        if (transitioning is bool t && t)
                        {
                            return new OverlayStatus
                            {
                                Action = OverlayAction.Wait,
                                Type = "LoadingScreen",
                                Detail = "scene_transition"
                            };
                        }
                    }
                    catch { /* 反射失败则忽略此层 */ }
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

                        // 内容识别：如果弹窗正文包含"请重新启动"等关键字，
                        // 说明炉石自身认为必须重启客户端才能恢复，升级为 RestartRequired
                        var bodyText = TryExtractDialogBodyText(currentDialog);
                        if (MatchesRestartRequired(bodyText))
                        {
                            var bodySnippet = bodyText.Length > 80 ? bodyText.Substring(0, 80) : bodyText;
                            _log($"[OverlayDetector] 检测到重启提示弹窗 {typeName}: {bodySnippet}");
                            return new OverlayStatus
                            {
                                Action = OverlayAction.RestartRequired,
                                Type = typeName,
                                Detail = "restart_required"
                            };
                        }

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
                            var typeName = "PopupDisplay";
                            var action = OverlayPopupPolicy.ShouldTreatAsDismissablePopupDisplay(typeName)
                                ? OverlayAction.CanDismiss
                                : OverlayAction.Wait;
                            return new OverlayStatus
                            {
                                Action = action,
                                Type = typeName,
                                Detail = "popup_display_manager"
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

            // 需要重启客户端：不尝试关闭，直接上报让 BotService 触发 RestartHearthstone。
            // 之所以不尝试 GoBack，是因为炉石的 "请重新启动" 这类弹窗经过实测会使
            // DialogManager 进入半死状态，GoBack/点击 OK 都可能把主线程卡住多秒，
            // 导致整个 pipe worker 停摆，后续 GET_SEED 连续超时。
            if (status.Action == OverlayAction.RestartRequired)
                return "RESTART_REQUIRED:" + status.Type;

            // PopupDisplayManager 管理的大厅弹窗经常带明确的确认按钮（如活动结束提示），
            // 这里优先回退到旧逻辑做按钮定位点击，避免误用 GoBack()。
            if (OverlayPopupPolicy.ShouldTreatAsDismissablePopupDisplay(status.Type))
                return null;

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

        /// <summary>
        /// 从弹窗对象中提取正文文本。走有限深度的字段/属性反射，兼容 Hearthstone 的
        /// UberText（通过 Text 属性暴露字符串）以及其他常见 body text 封装。
        /// </summary>
        private string TryExtractDialogBodyText(object dialog)
        {
            if (dialog == null) return null;
            try
            {
                var visited = new HashSet<object>(new ReferenceEqualityComparer());
                return TryExtractDialogBodyTextRecursive(dialog, 0, visited);
            }
            catch
            {
                return null;
            }
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

        private string TryExtractDialogBodyTextRecursive(object obj, int depth, HashSet<object> visited)
        {
            if (obj == null || depth > 3) return null;
            if (obj is string s) return s;
            if (!visited.Add(obj)) return null;

            var type = obj.GetType();
            var bf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            foreach (var name in DialogBodyMemberNames)
            {
                object value = null;
                try
                {
                    var field = type.GetField(name, bf);
                    if (field != null)
                        value = field.GetValue(obj);
                }
                catch { }
                if (value == null)
                {
                    try
                    {
                        var prop = type.GetProperty(name, bf);
                        if (prop != null && prop.GetIndexParameters().Length == 0)
                            value = prop.GetValue(obj, null);
                    }
                    catch { }
                }
                if (value == null) continue;

                if (value is string text)
                {
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                    continue;
                }

                var nested = TryExtractDialogBodyTextRecursive(value, depth + 1, visited);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }

            // UberText 之类控件通常通过 Text 属性或 GetText() 方法暴露字符串
            try
            {
                var textProp = type.GetProperty("Text", bf);
                if (textProp != null && textProp.GetIndexParameters().Length == 0)
                {
                    var v = textProp.GetValue(obj, null) as string;
                    if (!string.IsNullOrWhiteSpace(v))
                        return v;
                }
            }
            catch { }
            try
            {
                var getText = type.GetMethod("GetText", bf, null, Type.EmptyTypes, null);
                if (getText != null && getText.ReturnType == typeof(string))
                {
                    var v = getText.Invoke(obj, null) as string;
                    if (!string.IsNullOrWhiteSpace(v))
                        return v;
                }
            }
            catch { }

            return null;
        }

        private static bool MatchesRestartRequired(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            foreach (var kw in RestartRequiredKeywords)
            {
                if (text.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

    }
}
