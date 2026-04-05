using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace HearthstonePayload
{
    /// <summary>
    /// 场景检测和导航（仅读取状态，操作统一走鼠标模拟）
    /// </summary>
    public class SceneNavigator
    {
        private const int DismissClickTimeoutMs = 2000;
        private const string StartupRatingsDialogType = "StartupRatings";
        private const string StartupRatingsButtonLabel = "\u70b9\u51fb\u5f00\u59cb";
        private const string StartupRatingsStartPressedEvent = "USER_START_PRESSED";
        private const float StartupRatingsFallbackClickX = 0.50f;
        private const float StartupRatingsFallbackClickY = 0.78f;
        private const int StartupRatingsDismissPollIntervalMs = 100;
        private const int StartupRatingsDismissPollAttempts = 8;
        private Assembly _asm;
        private Type _sceneMgrType;
        private Type _gameMgrType;
        private Type _collMgrType;
        private bool _initialized;
        private CoroutineExecutor _coroutine;
        private Func<Func<object>, object> _runOnMain;
        private static readonly string[] KnownBlockingDialogTypes =
        {
            "AlertPopup",
            "ReconnectHelperDialog"
        };
        private static readonly string[] GenericBlockingDialogTokens =
        {
            "dialog",
            "popup",
            "alert",
            "reconnect"
        };
        private static readonly string[] BlockingDialogButtonMemberNames =
        {
            "m_okayButton",
            "m_okButton",
            "m_confirmButton",
            "m_doneButton",
            "m_closeButton",
            "m_cancelButton",
            "m_backButton",
            "m_primaryButton",
            "m_secondaryButton",
            "m_positiveButton",
            "m_negativeButton",
            "m_responseButton",
            "m_responseButton0",
            "m_responseButton1",
            "m_button",
            "m_button0",
            "m_button1"
        };
        private static readonly string[] ButtonTextMemberNames =
        {
            "Text",
            "text",
            "Label",
            "label",
            "m_text",
            "m_Text",
            "m_label",
            "m_labelText",
            "m_buttonText",
            "m_ButtonText",
            "m_newPlayButtonText",
            "m_textMeshGameObject",
            "m_messageText",
            "m_titleText",
            "m_headerText",
            "name",
            "Name"
        };
        private static readonly string[] VisibilityMemberNames =
        {
            "Visible",
            "visible",
            "m_visible",
            "m_isVisible",
            "IsVisible",
            "Shown",
            "shown",
            "m_shown",
            "m_isShown",
            "IsShown",
            "Active",
            "active",
            "m_active",
            "m_isActive",
            "enabled",
            "Enabled",
            "activeSelf",
            "activeInHierarchy",
            "isActiveAndEnabled"
        };

        private sealed class BlockingDialogHit
        {
            public string DialogType { get; set; }
            public string ButtonLabel { get; set; }
            public int ButtonX { get; set; }
            public int ButtonY { get; set; }
            public bool CanDismiss { get; set; }
            public bool IsRetryButton { get; set; }
            public string Detail { get; set; }
        }

        private sealed class HubButtonDescriptor
        {
            public string ButtonKey { get; set; }
            public string TargetScene { get; set; }
            public string MemberName { get; set; }
            public string DefaultLabel { get; set; }
        }

        private sealed class HubButtonSnapshot
        {
            public string Scene { get; set; }
            public string ButtonKey { get; set; }
            public string Label { get; set; }
            public bool Enabled { get; set; }
            public int ScreenX { get; set; }
            public int ScreenY { get; set; }
            public string Detail { get; set; }
            public string TargetScene { get; set; }
        }

        private sealed class HubButtonActionResult
        {
            public string ImmediateResult { get; set; }
            public HubButtonSnapshot FallbackButton { get; set; }
        }

        private sealed class OtherModeSnapshot
        {
            public int GameModeRecordId { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }
            public string LinkedScene { get; set; }
            public string ModeKey { get; set; }
            public bool IsDownloadRequired { get; set; }
            public bool IsDownloading { get; set; }
        }

        private sealed class CommandResult
        {
            public string ImmediateResult { get; set; }
            public bool ShouldVerify { get; set; }
            public long ExpectedDeckId { get; set; }
            public int ExpectedFormat { get; set; }
        }

        private static readonly HubButtonDescriptor[] HubButtonDescriptors =
        {
            new HubButtonDescriptor
            {
                ButtonKey = "traditional",
                TargetScene = "TOURNAMENT",
                MemberName = "m_PlayButton",
                DefaultLabel = "\u4f20\u7edf\u5bf9\u6218"
            },
            new HubButtonDescriptor
            {
                ButtonKey = "battlegrounds",
                TargetScene = "BACON",
                MemberName = "m_BattleGroundsButton",
                DefaultLabel = "\u9152\u9986\u6218\u68cb"
            },
            new HubButtonDescriptor
            {
                ButtonKey = "arena",
                TargetScene = "DRAFT",
                MemberName = "m_ArenaButton",
                DefaultLabel = "\u7ade\u6280\u6a21\u5f0f"
            },
            new HubButtonDescriptor
            {
                ButtonKey = "other_modes",
                TargetScene = "GAME_MODE",
                MemberName = "m_GameModesButton",
                DefaultLabel = "\u5176\u4ed6\u6a21\u5f0f"
            }
        };

        public void SetCoroutine(CoroutineExecutor coroutine)
        {
            _coroutine = coroutine;
        }

        public void SetMainThreadRunner(Func<Func<object>, object> runner)
        {
            _runOnMain = runner;
        }

        /// <summary>
        /// 在主线程执行（Unity API必须在主线程调用）
        /// </summary>
        private T OnMain<T>(Func<T> func)
        {
            if (_runOnMain == null) return func();
            return (T)_runOnMain(() => (object)func());
        }

        public bool Init()
        {
            if (_initialized) return true;
            try
            {
                // 从共享反射上下文获取已缓存的类型
                var ctx = ReflectionContext.Instance;
                if (!ctx.Init()) return false;
                _asm = ctx.AsmCSharp;
                _sceneMgrType = ctx.SceneMgrType;
                _gameMgrType = ctx.GameMgrType;
                _collMgrType = ctx.CollMgrType;
                _initialized = _sceneMgrType != null;
                return _initialized;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 内部获取场景（不走OnMain，供OnMain lambda内部使用）
        /// </summary>
        private string GetSceneInternal()
        {
            if (!Init()) return "UNKNOWN";
            try
            {
                var mgr = CallStatic(_sceneMgrType, "Get");
                if (mgr == null) return "UNKNOWN";
                var mode = Call(mgr, "GetMode");
                return mode != null ? mode.ToString() : "UNKNOWN";
            }
            catch
            {
                return "UNKNOWN";
            }
        }

        public string GetScene()
        {
            return OnMain(() => GetSceneInternal());
        }

        public string GetHubButtons()
        {
            return OnMain(() =>
            {
                if (!Init()) return "ERROR:not_initialized";

                var scene = GetSceneInternal();
                if (!string.Equals(scene, "HUB", StringComparison.OrdinalIgnoreCase))
                    return "ERROR:not_hub:" + scene;

                var box = GetBox();
                if (box == null)
                    return "ERROR:no_box";

                var buttons = new List<string>();
                foreach (var descriptor in HubButtonDescriptors)
                {
                    var snapshot = BuildHubButtonSnapshot(box, scene, descriptor);
                    buttons.Add(FormatHubButtonSnapshot(snapshot));
                }

                return "HUB_BUTTONS:" + string.Join(";", buttons);
            });
        }

        public string ClickHubButton(string buttonKey)
        {
            if (string.IsNullOrWhiteSpace(buttonKey))
                return "ERROR:invalid_button";

            var action = OnMain(() =>
            {
                if (!Init())
                    return new HubButtonActionResult { ImmediateResult = "ERROR:not_initialized" };

                var descriptor = FindHubButtonDescriptor(buttonKey);
                if (descriptor == null)
                    return new HubButtonActionResult { ImmediateResult = "ERROR:unknown_button:" + buttonKey };

                var scene = GetSceneInternal();
                if (string.Equals(scene, descriptor.TargetScene, StringComparison.OrdinalIgnoreCase))
                    return new HubButtonActionResult { ImmediateResult = "OK:already:" + descriptor.TargetScene };
                if (!string.Equals(scene, "HUB", StringComparison.OrdinalIgnoreCase))
                    return new HubButtonActionResult { ImmediateResult = "ERROR:not_hub:" + scene };

                var box = GetBox();
                if (box == null)
                    return new HubButtonActionResult { ImmediateResult = "ERROR:no_box" };

                var snapshot = BuildHubButtonSnapshot(box, scene, descriptor);
                if (!snapshot.Enabled)
                    return new HubButtonActionResult { ImmediateResult = "ERROR:button_disabled:" + descriptor.ButtonKey };

                var navResult = NavigateToSceneModeInternal(descriptor.TargetScene);
                if (!string.IsNullOrWhiteSpace(navResult)
                    && navResult.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
                {
                    return new HubButtonActionResult
                    {
                        ImmediateResult = navResult + ":button=" + descriptor.ButtonKey
                    };
                }

                return new HubButtonActionResult
                {
                    ImmediateResult = navResult ?? ("ERROR:navigate_failed:" + descriptor.TargetScene),
                    FallbackButton = snapshot
                };
            });

            if (action == null)
                return "ERROR:null_action";
            if (action.FallbackButton == null)
                return action.ImmediateResult ?? "ERROR:unknown";
            if (action.FallbackButton.ScreenX <= 0 || action.FallbackButton.ScreenY <= 0)
                return action.ImmediateResult ?? "ERROR:button_pos";

            var clickResult = ClickAt(action.FallbackButton.ScreenX, action.FallbackButton.ScreenY, 0.35f);
            if (!string.IsNullOrWhiteSpace(clickResult)
                && clickResult.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
            {
                return "OK:click:" + action.FallbackButton.ButtonKey + ":" + action.FallbackButton.Detail;
            }

            return string.IsNullOrWhiteSpace(action.ImmediateResult)
                ? (clickResult ?? "ERROR:click_failed")
                : action.ImmediateResult;
        }

        public string GetOtherModeButtons()
        {
            return OnMain(() =>
            {
                if (!Init()) return "ERROR:not_initialized";

                var scene = GetSceneInternal();
                if (!string.Equals(scene, "GAME_MODE", StringComparison.OrdinalIgnoreCase))
                    return "ERROR:not_other_modes:" + scene;

                var display = GetGameModeDisplay();
                if (display == null)
                    return "ERROR:no_gamemode_display";

                var dataModel = Call(display, "GetGameModeSceneDataModel")
                    ?? GetProp(display, "GameModeSceneDataModel")
                    ?? GetProp(display, "m_gameModeSceneDataModel");
                if (dataModel == null)
                    return "ERROR:no_gamemode_data_model";

                var records = new List<string>();
                foreach (var buttonModel in AsEnumerable(GetProp(dataModel, "GameModeButtons")))
                {
                    var snapshot = BuildOtherModeSnapshot(buttonModel);
                    if (snapshot == null)
                        continue;
                    records.Add(FormatOtherModeSnapshot(snapshot));
                }

                return "OTHER_MODE_BUTTONS:" + string.Join(";", records);
            });
        }

        public string NavigateTo(string sceneName)
        {
            sceneName = (sceneName ?? string.Empty).Trim();
            if (!string.Equals(sceneName, "TOURNAMENT", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(sceneName, "BACON", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(sceneName, "HUB", StringComparison.OrdinalIgnoreCase))
                return "ERROR:unsupported_scene:" + sceneName;

            return OnMain(() => NavigateToSceneModeInternal(sceneName));
        }

        public string GetDeckId(string deckName)
        {
            return OnMain(() =>
            {
                if (!Init()) return "ERROR:not_initialized";
                try
                {
                    var id = FindDeckId(deckName);
                    return id > 0 ? id.ToString() : "ERROR:not_found";
                }
                catch (Exception ex)
                {
                    return "ERROR:" + ex.Message;
                }
            });
        }

        public bool IsFindingGame()
        {
            return OnMain(() =>
            {
                if (!Init() || _gameMgrType == null) return false;
                try
                {
                    var mgr = CallStatic(_gameMgrType, "Get");
                    if (mgr == null) return false;
                    var method = FindZeroArgMethod(mgr.GetType(), "IsFindingGame",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    return method != null && Convert.ToBoolean(method.Invoke(mgr, null));
                }
                catch
                {
                    return false;
                }
            });
        }

        public string SetFormat(int vft)
        {
            var result = OnMain(() =>
            {
                if (!Init()) return "ERROR:not_initialized";
                var scene = GetSceneInternal();
                if (!string.Equals(scene, "TOURNAMENT", StringComparison.OrdinalIgnoreCase))
                    return "ERROR:scene:" + scene;
                if (IsFormatSelected(vft)) return "OK:already";

                try
                {
                    var optionsType = _asm.GetType("Options");
                    if (optionsType == null) return "ERROR:no_options_type";
                    var options = CallStatic(optionsType, "Get");
                    if (options == null) return "ERROR:no_options";

                    object formatArg = null;
                    var optFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

                    // vft=4 是休闲模式，其他是天梯模式
                    if (vft == 4)
                    {
                        CallMethod(options, "SetInRankedPlayMode", false);
                    }
                    else
                    {
                        CallMethod(options, "SetInRankedPlayMode", true);

                        var setFmt = options.GetType().GetMethods(optFlags)
                            .FirstOrDefault(m => m.Name == "SetFormatType" && m.GetParameters().Length >= 1);
                        if (setFmt == null)
                            setFmt = options.GetType().GetMethods(optFlags)
                                .FirstOrDefault(m => m.Name == "SetFormatType");
                        if (setFmt == null)
                        {
                            var diag = options.GetType().GetMethods(optFlags)
                                .Where(m => m.Name.IndexOf("Format", StringComparison.OrdinalIgnoreCase) >= 0)
                                .Select(m => (m.IsStatic ? "S:" : "") + m.Name + "(" +
                                    string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name)) + ")")
                                .Distinct().ToArray();
                            return "ERROR:no_SetFormatType|" + string.Join(";", diag);
                        }

                        var paramType = setFmt.GetParameters()[0].ParameterType;
                        if (paramType.IsEnum)
                        {
                            string[] candidates;
                            switch (vft)
                            {
                                case 1: candidates = new[] { "FT_WILD", "WILD", "Wild" }; break;
                                case 2: candidates = new[] { "FT_STANDARD", "STANDARD", "Standard" }; break;
                                case 3: candidates = new[] { "FT_CLASSIC", "CLASSIC", "Classic" }; break;
                                case 5: candidates = new[] { "FT_TWIST", "TWIST", "Twist" }; break;
                                default: return "ERROR:unknown_vft:" + vft;
                            }
                            foreach (var name in candidates)
                            {
                                try { formatArg = Enum.Parse(paramType, name); break; } catch { }
                            }
                            if (formatArg == null)
                            {
                                try { formatArg = Enum.ToObject(paramType, vft); } catch { }
                            }
                            if (formatArg == null) return "ERROR:no_enum:" + string.Join("/", candidates);
                        }
                        else
                        {
                            formatArg = Convert.ChangeType(vft, paramType);
                        }

                        CallMethod(options, "SetFormatType", formatArg);
                    }

                    // 通过 DeckPickerTrayDisplay 触发实际 UI 切换
                    var dpt = GetDeckPickerTray();
                    if (dpt == null)
                        return "ERROR:no_dpt";

                    var vftType = _asm.GetType("VisualsFormatType");
                    object vftEnum = null;
                    if (vftType != null && vftType.IsEnum)
                    {
                        try { vftEnum = Enum.ToObject(vftType, vft); } catch { }
                    }

                    if (vftEnum != null)
                    {
                        var r = CallMethod(dpt, "SwitchFormatTypeAndRankedPlayMode", vftEnum);
                        if (r != null) return "PENDING:switch:" + vftEnum;

                        r = CallMethod(dpt, "UpdateFormat_Tournament", vftEnum);
                        if (r != null) return "PENDING:update:" + vftEnum;
                    }

                    if (formatArg != null)
                    {
                        var r2 = CallMethod(dpt, "TransitionToFormatType", formatArg, vft != 4, 0f);
                        if (r2 != null) return "PENDING:transition";
                    }

                    var fallbackResult = CallMethod(dpt, "SwitchFormatButtonPress");
                    if (fallbackResult != null) return "PENDING:switchBtn";
                    if (IsFormatSelected(vft)) return "OK:options_only";

                    return "ERROR:format_not_set:" + vft;
                }
                catch (Exception ex)
                {
                    return "ERROR:" + ex.GetBaseException().Message;
                }
            });

            if (string.IsNullOrWhiteSpace(result)
                || !result.StartsWith("PENDING:", StringComparison.OrdinalIgnoreCase))
            {
                return result ?? "ERROR:unknown";
            }

            for (var i = 0; i < 25; i++)
            {
                Thread.Sleep(150);
                var selected = OnMain(() =>
                {
                    if (!Init()) return false;
                    return IsFormatSelected(vft);
                });
                if (selected)
                    return "OK:" + result.Substring("PENDING:".Length);
            }

            return "ERROR:format_not_set:" + vft;
        }

        public string SelectDeck(long deckId)
        {
            if (deckId <= 0) return "ERROR:invalid_deck_id";

            var result = OnMain(() =>
            {
                if (!Init()) return new CommandResult { ImmediateResult = "ERROR:not_initialized" };
                var scene = GetSceneInternal();
                if (!string.Equals(scene, "TOURNAMENT", StringComparison.OrdinalIgnoreCase))
                    return new CommandResult { ImmediateResult = "ERROR:scene:" + scene };

                try
                {
                    var dpt = GetDeckPickerTray();
                    if (dpt == null) return new CommandResult { ImmediateResult = "ERROR:no_dpt" };

                    if (GetSelectedDeckId(dpt) == deckId)
                    {
                        return new CommandResult { ImmediateResult = "OK:already:" + deckId };
                    }

                    object targetDeckBox;
                    int pageIndex;
                    int deckIndex;
                    var foundDeckBox = TryFindDeckBoxById(dpt, deckId, out targetDeckBox, out pageIndex, out deckIndex);
                    if (foundDeckBox && !CanSelectDeckBox(targetDeckBox))
                        return new CommandResult { ImmediateResult = "ERROR:deck_not_selectable:" + deckId };

                    if (foundDeckBox && pageIndex >= 0)
                        CallMethod(dpt, "ShowPage", pageIndex);

                    foreach (var name in new[] { "SelectDeckById", "SelectDeck", "SetSelectedDeck" })
                    {
                        var direct = CallMethod(dpt, name, deckId);
                        if (direct != null)
                        {
                            return new CommandResult
                            {
                                ImmediateResult = "PENDING:api:" + name,
                                ShouldVerify = true,
                                ExpectedDeckId = deckId
                            };
                        }
                    }

                    if (!foundDeckBox)
                        return new CommandResult { ImmediateResult = "ERROR:deck_not_found:" + deckId };

                    var selectResult = CallMethod(targetDeckBox, "SelectDeck")
                        ?? CallMethod(targetDeckBox, "OnSelected")
                        ?? CallMethod(targetDeckBox, "Select");
                    if (selectResult != null)
                    {
                        return new CommandResult
                        {
                            ImmediateResult = "PENDING:deckbox_select:" + pageIndex + ":" + deckIndex,
                            ShouldVerify = true,
                            ExpectedDeckId = deckId
                        };
                    }

                    var fromTray = CallMethod(dpt, "SelectCustomDeck", targetDeckBox);
                    if (fromTray != null)
                    {
                        return new CommandResult
                        {
                            ImmediateResult = "PENDING:select_custom:" + pageIndex + ":" + deckIndex,
                            ShouldVerify = true,
                            ExpectedDeckId = deckId
                        };
                    }

                    return new CommandResult { ImmediateResult = "ERROR:deck_select_method_missing:" + deckId };
                }
                catch (Exception ex)
                {
                    return new CommandResult { ImmediateResult = "ERROR:" + ex.GetBaseException().Message };
                }
            });

            if (result == null)
                return "ERROR:null_result";
            if (!result.ShouldVerify)
                return result.ImmediateResult ?? "ERROR:unknown";

            for (var i = 0; i < 10; i++)
            {
                Thread.Sleep(100);
                var selectedDeckId = OnMain(() =>
                {
                    if (!Init()) return 0L;
                    var dpt = GetDeckPickerTray();
                    return GetSelectedDeckId(dpt);
                });
                if (selectedDeckId == result.ExpectedDeckId)
                    return "OK:selected:" + result.ExpectedDeckId;
            }

            return "ERROR:deck_not_selected:" + result.ExpectedDeckId;
        }

        public string ClickPlay()
        {
            // 1. 在主线程获取按钮坐标和按钮对象引用
            object capturedBtn = null;
            var result = OnMain(() =>
            {
                if (!Init()) return "ERROR:not_initialized";

                try
                {
                    var scene = GetSceneInternal();
                    object btn;
                    if (string.Equals(scene, "TOURNAMENT", StringComparison.OrdinalIgnoreCase))
                    {
                        var r = GetPlayButtonInfo(GetDeckPickerTray(), out btn);
                        capturedBtn = btn;
                        return r;
                    }
                    if (string.Equals(scene, "BACON", StringComparison.OrdinalIgnoreCase))
                    {
                        var r = GetPlayButtonInfo(GetBaconDisplay(), out btn);
                        capturedBtn = btn;
                        return r;
                    }
                    if (string.Equals(scene, "DRAFT", StringComparison.OrdinalIgnoreCase))
                    {
                        var r = GetPlayButtonInfo(GetDraftDisplay(), out btn);
                        capturedBtn = btn;
                        return r;
                    }
                    return "ERROR:scene:" + scene;
                }
                catch (Exception ex)
                {
                    return "ERROR:" + ex.GetBaseException().Message;
                }
            });

            if (string.IsNullOrWhiteSpace(result))
                return "ERROR:unknown";

            if (result.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
                return result;

            // 2. 尝试鼠标点击
            if (result.StartsWith("MOUSE_CLICK:", StringComparison.Ordinal))
            {
                var coords = result.Substring("MOUSE_CLICK:".Length).Split(',');
                if (coords.Length == 2
                    && int.TryParse(coords[0], out var clickX)
                    && int.TryParse(coords[1], out var clickY))
                {
                    ClickAt(clickX, clickY, 0.3f);

                    for (var i = 0; i < 6; i++)
                    {
                        Thread.Sleep(150);
                        if (IsFindingGame())
                            return "OK:playButton:finding";
                    }
                }
            }

            // 3. 鼠标没触发匹配，回退到 API 直接触发按钮
            if (capturedBtn != null)
            {
                var apiResult = OnMain(() =>
                {
                    try
                    {
                        CallMethod(capturedBtn, "TriggerPress");
                        CallMethod(capturedBtn, "TriggerRelease");
                        return "PENDING:playButton_api";
                    }
                    catch { return null; }
                });

                if (!string.IsNullOrWhiteSpace(apiResult))
                {
                    for (var i = 0; i < 6; i++)
                    {
                        Thread.Sleep(150);
                        if (IsFindingGame())
                            return "OK:playButton_api:finding";
                    }
                    return "OK:playButton_api";
                }
            }

            return "OK:playButton";
        }

        private string GetPlayButtonInfo(object container, out object playBtn)
        {
            playBtn = null;
            if (container == null) return "ERROR:no_container";

            playBtn = GetProp(container, "m_playButton");
            if (playBtn == null) return "ERROR:no_play_button";
            if (!IsButtonEnabled(playBtn))
            {
                var buttonText = TryExtractButtonLabel(playBtn);
                if (string.IsNullOrWhiteSpace(buttonText))
                    buttonText = "UNKNOWN";
                playBtn = null;
                return "ERROR:play_disabled:" + buttonText;
            }

            if (!TryGetScreenPos(playBtn, out var btnX, out var btnY))
                return "ERROR:no_button_pos";

            return "MOUSE_CLICK:" + btnX + "," + btnY;
        }

        /// <summary>
        /// 检查战旗大厅 UI 是否完全加载就绪：
        /// 场景为 BACON 且 BaconDisplay 存在且开始按钮可用。
        /// 返回 "READY" 或 "NOT_READY:原因"。
        /// </summary>
        public string IsBattlegroundsLobbyReady()
        {
            return OnMain(() =>
            {
                if (!Init()) return "NOT_READY:not_initialized";

                var scene = GetSceneInternal();
                if (!string.Equals(scene, "BACON", StringComparison.OrdinalIgnoreCase))
                    return "NOT_READY:scene:" + scene;

                var baconDisplay = GetBaconDisplay();
                if (baconDisplay == null)
                    return "NOT_READY:no_bacon_display";

                var playBtn = GetProp(baconDisplay, "m_playButton");
                if (playBtn == null)
                    return "NOT_READY:no_play_button";

                if (!IsButtonEnabled(playBtn))
                {
                    var buttonText = TryExtractButtonLabel(playBtn);
                    return "NOT_READY:play_disabled:" + (string.IsNullOrWhiteSpace(buttonText) ? "UNKNOWN" : buttonText);
                }

                return "READY";
            });
        }

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

        public string DismissBlockingDialog()
        {
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

        public string Reflect(string typeName)
        {
            return OnMain(() =>
            {
                if (!Init()) return "ERROR:not_initialized";
                try
                {
                    string methodToCall = null;
                    if (typeName.Contains("."))
                    {
                        var parts = typeName.Split(new[] { '.' }, 2);
                        typeName = parts[0];
                        methodToCall = parts[1];
                    }

                    var type = _asm.GetType(typeName);
                    if (type == null) return "ERROR:type_not_found:" + typeName;

                    Type targetType = type;
                    if (methodToCall != null)
                    {
                        var inst = CallStatic(type, methodToCall);
                        if (inst != null) targetType = inst.GetType();
                    }

                    var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                    var methods = targetType.GetMethods(flags)
                        .Where(m => !m.IsSpecialName)
                        .Select(m => (m.IsStatic ? "S:" : "") + m.Name + "(" +
                            string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name)) + ")")
                        .Distinct().ToArray();

                    var props = targetType.GetProperties(flags)
                        .Where(p => p.GetIndexParameters().Length == 0)
                        .Select(p => "P:" + p.Name + ":" + p.PropertyType.Name)
                        .ToArray();

                    return "M:" + string.Join(";", methods) + "|" + string.Join(";", props);
                }
                catch (Exception ex)
                {
                    return "ERROR:" + ex.Message;
                }
            });
        }

        // ────────────────────────────────────────
        // Arena / Draft 相关命令
        // ────────────────────────────────────────

        /// <summary>
        /// 获取DraftManager单例（竞技场选牌管理器）
        /// </summary>
        private object GetDraftManager()
        {
            var type = _asm?.GetType("DraftManager");
            return type != null ? (CallStatic(type, "Get") ?? GetStaticValue(type, "s_instance")) : null;
        }

        /// <summary>
        /// 获取NetCache单例
        /// </summary>
        private object GetNetCache()
        {
            var type = _asm?.GetType("NetCache");
            return type != null ? CallStatic(type, "Get") : null;
        }

        /// <summary>
        /// 通过NetCache.GetNetObject泛型方法获取缓存的网络对象
        /// </summary>
        private object GetNetObject(string typeName)
        {
            var cache = GetNetCache();
            if (cache == null) return null;

            var targetType = _asm?.GetType(typeName);
            if (targetType == null) return null;

            // 查找 GetNetObject<T>() 泛型方法
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var methods = cache.GetType().GetMethods(flags)
                .Where(m => m.Name == "GetNetObject" && m.IsGenericMethod && m.GetParameters().Length == 0)
                .ToArray();
            foreach (var mi in methods)
            {
                try
                {
                    var generic = mi.MakeGenericMethod(targetType);
                    return generic.Invoke(cache, null);
                }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// 调试：枚举 NetCache 中所有与 Arena/Draft/Ticket/Gold 相关的类型和字段值
        /// </summary>
        public string DumpNetCacheArenaTypes()
        {
            return OnMain(() =>
            {
                if (!Init()) return "ERROR:not_initialized";
                try
                {
                    var cache = GetNetCache();
                    if (cache == null) return "ERROR:no_netcache";

                    var sb = new System.Text.StringBuilder();
                    var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

                    // 列出 NetCache 所有字段，找到存储网络对象的 dictionary/map
                    sb.AppendLine("=== NetCache fields ===");
                    foreach (var f in cache.GetType().GetFields(flags))
                    {
                        var val = f.GetValue(cache);
                        if (val is System.Collections.IDictionary dict)
                        {
                            sb.AppendLine($"Dict: {f.Name} ({dict.Count} entries)");
                            foreach (System.Collections.DictionaryEntry entry in dict)
                            {
                                var keyStr = entry.Key?.ToString() ?? "null";
                                // 只打印跟 Arena/Draft/Ticket/Gold/Currency 相关的
                                if (keyStr.IndexOf("Arena", StringComparison.OrdinalIgnoreCase) >= 0
                                    || keyStr.IndexOf("Draft", StringComparison.OrdinalIgnoreCase) >= 0
                                    || keyStr.IndexOf("Ticket", StringComparison.OrdinalIgnoreCase) >= 0
                                    || keyStr.IndexOf("Gold", StringComparison.OrdinalIgnoreCase) >= 0
                                    || keyStr.IndexOf("Currency", StringComparison.OrdinalIgnoreCase) >= 0
                                    || keyStr.IndexOf("Balance", StringComparison.OrdinalIgnoreCase) >= 0
                                    || keyStr.IndexOf("Forge", StringComparison.OrdinalIgnoreCase) >= 0
                                    || keyStr.IndexOf("Store", StringComparison.OrdinalIgnoreCase) >= 0
                                    || keyStr.IndexOf("Shop", StringComparison.OrdinalIgnoreCase) >= 0
                                    || keyStr.IndexOf("Booster", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    sb.AppendLine($"  Key={keyStr}");
                                    if (entry.Value != null)
                                    {
                                        foreach (var vf in entry.Value.GetType().GetFields(flags))
                                        {
                                            try { sb.AppendLine($"    F:{vf.Name}={vf.GetValue(entry.Value)}"); }
                                            catch { sb.AppendLine($"    F:{vf.Name}=<err>"); }
                                        }
                                        foreach (var vp in entry.Value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                                        {
                                            if (vp.GetIndexParameters().Length > 0) continue;
                                            try { sb.AppendLine($"    P:{vp.Name}={vp.GetValue(entry.Value)}"); }
                                            catch { sb.AppendLine($"    P:{vp.Name}=<err>"); }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // 也尝试直接查找已知类型
                    sb.AppendLine("=== Direct type lookup ===");
                    var candidates = new[] {
                        "NetCacheArenaTickets", "NetCache+NetCacheArenaTickets",
                        "NetCacheGoldBalance", "NetCache+NetCacheGoldBalance",
                        "NetCacheBuySellData", "NetCache+NetCacheBuySellData",
                        "NetCacheFeatures", "NetCache+NetCacheFeatures",
                        "NetCacheProfileProgress", "NetCache+NetCacheProfileProgress",
                        "NetCacheCurrencyBalance", "NetCache+NetCacheCurrencyBalance",
                        "NetCacheArenaSession", "NetCache+NetCacheArenaSession"
                    };
                    foreach (var name in candidates)
                    {
                        var t = _asm?.GetType(name);
                        sb.AppendLine($"  {name}: {(t != null ? "FOUND" : "NOT_FOUND")}");
                        if (t != null)
                        {
                            var obj = GetNetObject(name);
                            sb.AppendLine($"    instance: {(obj != null ? "YES" : "NULL")}");
                            if (obj != null)
                            {
                                foreach (var vf in obj.GetType().GetFields(flags))
                                {
                                    try { sb.AppendLine($"    F:{vf.Name}={vf.GetValue(obj)}"); }
                                    catch { sb.AppendLine($"    F:{vf.Name}=<err>"); }
                                }
                            }
                        }
                    }

                    return sb.ToString();
                }
                catch (Exception ex)
                {
                    return "ERROR:" + ex.Message;
                }
            });
        }

        /// <summary>
        /// 从DraftManager读取竞技场当前状态
        /// </summary>
        // ── 获取 DraftDisplay 单例 ──
        private object GetDraftDisplay()
        {
            var type = _asm?.GetType("DraftDisplay");
            if (type == null) return null;
            return CallStatic(type, "Get") ?? GetStaticValue(type, "s_instance");
        }

        // ── 获取 CurrencyManager 余额 ──
        private int GetCurrencyBalance(string currencyEnumName)
        {
            var cmType = _asm?.GetType("CurrencyManager");
            if (cmType == null) return -1;
            // ServiceManager.Get<CurrencyManager>()
            var smType = _asm?.GetType("ServiceManager");
            if (smType == null) return -1;
            var getMethod = smType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "Get" && m.IsGenericMethod && m.GetParameters().Length == 0);
            if (getMethod == null) return -1;
            var cm = getMethod.MakeGenericMethod(cmType).Invoke(null, null);
            if (cm == null) return -1;

            // CurrencyType 枚举
            var ctType = _asm?.GetType("CurrencyType");
            if (ctType == null) return -1;
            object enumVal;
            try { enumVal = Enum.Parse(ctType, currencyEnumName); }
            catch { return -1; }

            // GetBalance(CurrencyType)
            var balMethod = cmType.GetMethod("GetBalance", BindingFlags.Public | BindingFlags.Instance,
                null, new[] { ctType }, null);
            if (balMethod == null) return -1;
            var result = balMethod.Invoke(cm, new[] { enumVal });
            return result != null ? Convert.ToInt32(result) : -1;
        }

        public string GetArenaStatus()
        {
            return OnMain(() =>
            {
                if (!Init()) return "ERROR:not_initialized";
                try
                {
                    var dm = GetDraftManager();
                    if (dm == null) return "NO_DRAFT";

                    // DraftManager.GetArenaState() → ArenaSessionState 枚举
                    var stateObj = CallMethod(dm, "GetArenaState");
                    var stateStr = stateObj?.ToString() ?? "UNKNOWN";

                    // ArenaSessionState: INVALID, NO_RUN, DRAFTING, REDRAFTING, MIDRUN,
                    //   MIDRUN_REDRAFT_PENDING, EDITING_DECK, REWARDS
                    if (stateStr.Contains("NO_RUN") || stateStr.Contains("INVALID"))
                        return "NO_DRAFT";

                    if (stateStr.Contains("REWARDS"))
                        return "REWARDS";

                    if (stateStr.Contains("MIDRUN") || stateStr.Contains("EDITING"))
                        return "DRAFT_COMPLETE";

                    if (stateStr.Contains("DRAFTING") || stateStr.Contains("REDRAFT"))
                    {
                        // 判断是选英雄还是选卡牌
                        // DraftManager.GetSlotType() → DRAFT_SLOT_HERO / DRAFT_SLOT_CARD
                        var slotType = CallMethod(dm, "GetSlotType");
                        var slotTypeStr = slotType?.ToString() ?? "";
                        var slot = CallMethod(dm, "GetSlot");
                        var maxSlot = CallMethod(dm, "GetMaxSlot");
                        int slotNum = slot != null ? Convert.ToInt32(slot) : 0;
                        int maxSlotNum = maxSlot != null ? Convert.ToInt32(maxSlot) : 30;

                        if (slotTypeStr.Contains("HERO"))
                            return "HERO_PICK";

                        return $"CARD_DRAFT:{slotNum}/{maxSlotNum}";
                    }

                    return "UNKNOWN:" + stateStr;
                }
                catch (Exception ex)
                {
                    return "ERROR:" + ex.Message;
                }
            });
        }

        /// <summary>
        /// 查询竞技场门票和金币
        /// 真实调用路径：DraftManager.GetNumTicketsOwned() → CurrencyManager.GetBalance(CurrencyType.TAVERN_TICKET)
        /// 金币：NetCache.NetCacheGoldBalance.GetTotal()
        /// </summary>
        public string GetArenaTicketInfo()
        {
            return OnMain(() =>
            {
                if (!Init()) return "ERROR:not_initialized";
                try
                {
                    int tickets = 0;
                    int gold = 0;

                    // ── 方式1（正确路径）：DraftManager.GetNumTicketsOwned() ──
                    var dm = GetDraftManager();
                    if (dm != null)
                    {
                        var numTickets = CallMethod(dm, "GetNumTicketsOwned");
                        if (numTickets != null)
                        {
                            try { tickets = Convert.ToInt32(numTickets); } catch { }
                        }
                    }

                    // ── 方式2（备选）：CurrencyManager.GetBalance(CurrencyType.TAVERN_TICKET) ──
                    if (tickets <= 0)
                    {
                        var cb = GetCurrencyBalance("TAVERN_TICKET");
                        if (cb >= 0) tickets = cb;
                    }

                    // ── 方式3（备选）：NetCache.NetPlayerArenaTickets.Balance ──
                    if (tickets <= 0)
                    {
                        var ticketObj = GetNetObject("NetPlayerArenaTickets");
                        if (ticketObj != null)
                        {
                            var bal = TryGetFirstProp(ticketObj, "Balance", "m_balance");
                            if (bal != null)
                            {
                                try { tickets = Convert.ToInt32(bal); } catch { }
                            }
                        }
                    }

                    // ── 金币：NetCache.NetCacheGoldBalance ──
                    var goldObj = GetNetObject("NetCacheGoldBalance");
                    if (goldObj != null)
                    {
                        // GetTotal() = CappedBalance + BonusBalance
                        var total = CallMethod(goldObj, "GetTotal");
                        if (total != null)
                        {
                            try { gold = Convert.ToInt32(total); } catch { }
                        }
                        if (gold == 0)
                        {
                            var cap = TryGetFirstProp(goldObj, "CappedBalance", "m_cappedBalance");
                            if (cap != null)
                            {
                                try { gold = Convert.ToInt32(cap); } catch { }
                            }
                        }
                    }

                    return "TICKETS:" + tickets + "|GOLD:" + gold;
                }
                catch (Exception ex)
                {
                    return "ERROR:" + ex.Message;
                }
            });
        }

        // ════════════════════════════════════════════════════════════════
        //  以下所有竞技场操作基于反编译 Assembly-CSharp.dll 的真实调用链
        //  关键类: DraftManager, DraftDisplay, ArenaLandingPageManager, Network
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// 购票/开始新轮次
        /// 反编译路径: ArenaLandingPageManager.SpendTicket()
        ///   → m_widget.TriggerEvent("MOVETO_DRAFTING") + ("DRAFTING")
        ///   → DraftManager.RequestDraftBegin()
        ///   → Network.DraftBegin(isUnderground)
        /// </summary>
        public string ArenaBuyTicket()
        {
            return OnMain(() =>
            {
                if (!Init()) return "ERROR:not_initialized";
                try
                {
                    var dm = GetDraftManager();
                    if (dm == null) return "ERROR:no_draft_manager";

                    // 检查票数
                    var ticketCount = CallMethod(dm, "GetNumTicketsOwned");
                    int tickets = ticketCount != null ? Convert.ToInt32(ticketCount) : 0;
                    if (tickets <= 0) return "ERROR:no_tickets";

                    // 直接调用 RequestDraftBegin (SpendTicket 的核心)
                    CallMethod(dm, "RequestDraftBegin");
                    return "OK:BUY";
                }
                catch (Exception ex)
                {
                    return "ERROR:" + ex.Message;
                }
            });
        }

        /// <summary>
        /// 获取当前选项 (英雄或卡牌)
        /// 从 DraftDisplay.m_choices 读取, 每个 DraftChoice 有 m_cardID
        /// </summary>
        private string GetDraftDisplayChoices()
        {
            var dd = GetDraftDisplay();
            if (dd == null) return null;

            var choices = TryGetFirstProp(dd, "m_choices");
            if (choices == null) return null;

            var ids = new List<string>();
            foreach (var c in AsEnumerable(choices))
            {
                if (c == null) continue;
                var cardId = TryGetFirstProp(c, "m_cardID");
                ids.Add(cardId?.ToString() ?? "unknown");
            }

            return ids.Count > 0 ? string.Join(",", ids) : null;
        }

        public string GetArenaHeroChoices()
        {
            return OnMain(() =>
            {
                if (!Init()) return "ERROR:not_initialized";
                try
                {
                    var choiceStr = GetDraftDisplayChoices();
                    return choiceStr != null ? "HEROES:" + choiceStr : "ERROR:no_hero_choices";
                }
                catch (Exception ex) { return "ERROR:" + ex.Message; }
            });
        }

        public string GetArenaDraftChoices()
        {
            return OnMain(() =>
            {
                if (!Init()) return "ERROR:not_initialized";
                try
                {
                    var choiceStr = GetDraftDisplayChoices();
                    return choiceStr != null ? "CHOICES:" + choiceStr : "ERROR:no_draft_choices";
                }
                catch (Exception ex) { return "ERROR:" + ex.Message; }
            });
        }

        /// <summary>
        /// 选择英雄 (0-based index)
        /// 反编译路径: DraftDisplay.OnHeroClicked(choiceNum) → ZoomHeroCard → OnConfirmButtonClicked
        ///   → DoHeroSelectAnimation → DraftManager.MakeChoice(choiceNum, premium)
        /// 英雄选择需要两步: 先点击英雄放大, 再确认
        /// 但 DraftDisplay.ClickConfirmButton() 可以直接跳过动画确认
        /// </summary>
        public string ArenaPickHero(int index)
        {
            return OnMain(() =>
            {
                if (!Init()) return "ERROR:not_initialized";
                try
                {
                    int choiceNum = index + 1; // 1-based
                    var dd = GetDraftDisplay();
                    if (dd == null) return "ERROR:no_draft_display";

                    // DraftDisplay.OnHeroClicked(choiceNum) — 触发英雄放大+确认按钮显示
                    CallMethod(dd, "OnHeroClicked", choiceNum);

                    // 短暂等待动画开始
                    Thread.Sleep(300);

                    // DraftDisplay.ClickConfirmButton() — 直接确认(公开方法,内部调 OnConfirmButtonClicked)
                    CallMethod(dd, "ClickConfirmButton");

                    return "OK:HERO_PICKED";
                }
                catch (Exception ex) { return "ERROR:" + ex.Message; }
            });
        }

        /// <summary>
        /// 选择卡牌 (0-based index)
        /// 反编译路径: DraftManager.MakeChoice(choiceNum, TAG_PREMIUM.NORMAL)
        ///   → Network.MakeDraftChoice(...)
        ///   → 服务端 DraftChosen → OnChosen() → 下一轮选牌
        /// 卡牌选择不需要确认按钮,直接 MakeChoice
        /// </summary>
        public string ArenaPickCard(int index)
        {
            return OnMain(() =>
            {
                if (!Init()) return "ERROR:not_initialized";
                try
                {
                    var dm = GetDraftManager();
                    if (dm == null) return "ERROR:no_draft_manager";

                    int choiceNum = index + 1; // 1-based

                    // 获取 TAG_PREMIUM.NORMAL
                    var premiumType = _asm?.GetType("TAG_PREMIUM");
                    object normalPremium = null;
                    if (premiumType != null)
                    {
                        try { normalPremium = Enum.Parse(premiumType, "NORMAL"); } catch { }
                        if (normalPremium == null)
                            try { normalPremium = Enum.ToObject(premiumType, 0); } catch { }
                    }

                    // DraftManager.MakeChoice(choiceNum, premium, packagePremiums=null, isConfirming=false)
                    var method = dm.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "MakeChoice" && m.GetParameters().Length >= 2);

                    if (method != null && normalPremium != null)
                    {
                        var parms = method.GetParameters();
                        var args = new object[parms.Length];
                        args[0] = choiceNum;
                        args[1] = normalPremium;
                        for (int i = 2; i < parms.Length; i++)
                            args[i] = parms[i].HasDefaultValue ? parms[i].DefaultValue
                                    : parms[i].ParameterType == typeof(bool) ? (object)false : null;
                        method.Invoke(dm, args);
                        return "OK:CARD_PICKED";
                    }

                    return "ERROR:MakeChoice_not_found";
                }
                catch (Exception ex) { return "ERROR:" + ex.Message; }
            });
        }

        /// <summary>
        /// 开始匹配
        /// 反编译路径: DraftManager.FindGame()
        ///   → GameMgr.FindGame(GameType.GT_ARENA, ...)
        /// 注意: ArenaLandingPageManager.StartMatchmaking() 也是调这个,
        ///   但会额外 SetUIButtonsEnabled(false) 和设置 PresenceStatus
        /// </summary>
        public string ArenaFindGame()
        {
            return OnMain(() =>
            {
                if (!Init()) return "ERROR:not_initialized";
                try
                {
                    var dm = GetDraftManager();
                    if (dm == null) return "ERROR:no_draft_manager";

                    CallMethod(dm, "FindGame");
                    return "OK:FIND_GAME";
                }
                catch (Exception ex) { return "ERROR:" + ex.Message; }
            });
        }

        /// <summary>
        /// 领取奖励
        /// 反编译路径 (ArenaTrayDisplay + DraftDisplay):
        ///   Network.AckDraftRewards(deckId, slot, isUnderground)
        ///   DraftDisplay.OnOpenRewardsComplete()
        ///   → 服务端 DraftRewardsAcked → OnAckRewards() → 状态变 NO_RUN
        /// </summary>
        public string ArenaClaimRewards()
        {
            return OnMain(() =>
            {
                if (!Init()) return "ERROR:not_initialized";
                try
                {
                    var dm = GetDraftManager();
                    if (dm == null) return "ERROR:no_draft_manager";

                    var networkType = _asm?.GetType("Network");
                    if (networkType == null) return "ERROR:no_network_type";
                    var network = CallStatic(networkType, "Get");
                    if (network == null) return "ERROR:no_network";

                    // 获取 deckId
                    var deck = CallMethod(dm, "GetDraftDeck");
                    if (deck == null) return "ERROR:no_draft_deck";
                    var deckId = TryGetFirstProp(deck, "ID");
                    if (deckId == null) return "ERROR:no_deck_id";

                    var slot = CallMethod(dm, "GetSlot");
                    int slotVal = slot != null ? Convert.ToInt32(slot) : 0;
                    var isUg = CallMethod(dm, "IsUnderground");
                    bool isUnderground = isUg != null && Convert.ToBoolean(isUg);

                    // Network.AckDraftRewards(long deckId, int slot, bool isUnderground)
                    var ackMethod = networkType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "AckDraftRewards");
                    if (ackMethod != null)
                    {
                        var p = ackMethod.GetParameters();
                        if (p.Length == 3)
                            ackMethod.Invoke(network, new object[] { Convert.ToInt64(deckId), slotVal, isUnderground });
                        else if (p.Length == 2)
                            ackMethod.Invoke(network, new object[] { Convert.ToInt64(deckId), slotVal });
                    }

                    // DraftDisplay.OnOpenRewardsComplete()
                    var dd = GetDraftDisplay();
                    if (dd != null)
                        CallMethod(dd, "OnOpenRewardsComplete");

                    return "OK:CLAIMED";
                }
                catch (Exception ex) { return "ERROR:" + ex.Message; }
            });
        }

        /// <summary>
        /// 调试：枚举DraftManager的所有字段和属性（运行时验证字段名）
        /// </summary>
        public string DumpDraftManager()
        {
            return OnMain(() =>
            {
                if (!Init()) return "ERROR:not_initialized";
                try
                {
                    var dm = GetDraftManager();
                    if (dm == null) return "ERROR:no_draft_manager";

                    var type = dm.GetType();
                    var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

                    var fields = type.GetFields(flags)
                        .Select(f =>
                        {
                            string val;
                            try { val = (f.GetValue(f.IsStatic ? null : dm) ?? "null").ToString(); }
                            catch { val = "<error>"; }
                            if (val.Length > 60) val = val.Substring(0, 60) + "...";
                            return "F:" + (f.IsStatic ? "S:" : "") + f.Name + "=" + val;
                        })
                        .ToArray();

                    var props = type.GetProperties(flags)
                        .Where(p => p.GetIndexParameters().Length == 0)
                        .Select(p =>
                        {
                            string val;
                            try { val = (p.GetValue(dm, null) ?? "null").ToString(); }
                            catch { val = "<error>"; }
                            if (val.Length > 60) val = val.Substring(0, 60) + "...";
                            return "P:" + p.Name + "=" + val;
                        })
                        .ToArray();

                    var methods = type.GetMethods(flags)
                        .Where(m => !m.IsSpecialName)
                        .Select(m => "M:" + (m.IsStatic ? "S:" : "") + m.Name + "(" +
                            string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name)) + ")")
                        .Distinct()
                        .ToArray();

                    return "DRAFT_DUMP:" + string.Join(";", fields) + "|" + string.Join(";", props) + "|" + string.Join(";", methods);
                }
                catch (Exception ex)
                {
                    return "ERROR:" + ex.Message;
                }
            });
        }

        /// <summary>
        /// 尝试从多个候选名称中获取第一个非空属性/字段值
        /// </summary>
        private static object TryGetFirstProp(object obj, params string[] candidates)
        {
            if (obj == null) return null;
            foreach (var name in candidates)
            {
                var val = GetProp(obj, name);
                if (val != null) return val;
            }
            return null;
        }

        private object GetDeckPickerTray()
        {
            var type = _asm.GetType("DeckPickerTrayDisplay");
            return type != null ? CallStatic(type, "Get") : null;
        }

        private object GetBox()
        {
            var type = _asm?.GetType("Box");
            return type != null ? (CallStatic(type, "Get") ?? GetStaticValue(type, "s_instance")) : null;
        }

        private object GetGameModeDisplay()
        {
            var type = _asm?.GetType("GameModeDisplay");
            return type != null ? (CallStatic(type, "Get") ?? GetStaticValue(type, "m_instance")) : null;
        }

        private object GetBaconDisplay()
        {
            var type = _asm?.GetType("BaconDisplay");
            return type != null ? (CallStatic(type, "Get") ?? GetStaticValue(type, "m_instance")) : null;
        }

        private long FindDeckId(string deckName)
        {
            if (_collMgrType == null || string.IsNullOrWhiteSpace(deckName)) return -1;
            var manager = CallStatic(_collMgrType, "Get");
            if (manager == null) return -1;

            var getDecks = _collMgrType.GetMethod("GetDecks",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, Type.EmptyTypes, null);
            if (getDecks == null) return -1;

            var decks = getDecks.Invoke(manager, null) as IEnumerable;
            if (decks == null) return -1;

            foreach (var entry in decks)
            {
                var deck = UnwrapValue(entry);
                if (deck == null) continue;
                var name = (GetProp(deck, "Name") ?? string.Empty).ToString();
                if (!string.Equals(name, deckName, StringComparison.OrdinalIgnoreCase))
                    continue;
                var id = ToLong(GetProp(deck, "ID") ?? GetProp(deck, "Id"));
                if (id > 0) return id;
            }

            return -1;
        }

        private string NavigateToSceneModeInternal(string sceneName)
        {
            if (!Init()) return "ERROR:not_initialized";

            try
            {
                var mgr = CallStatic(_sceneMgrType, "Get");
                if (mgr == null) return "ERROR:no_scenemgr";

                var currentScene = GetSceneInternal();
                if (string.Equals(currentScene, sceneName, StringComparison.OrdinalIgnoreCase))
                    return "OK:already";

                var modeType = _sceneMgrType.GetNestedType("Mode")
                    ?? _asm.GetType("SceneMgr+Mode")
                    ?? _asm.GetType("SceneMode");
                if (modeType == null) return "ERROR:no_mode_type";

                object sceneMode;
                string resolvedName;
                if (!TryResolveEnumValue(modeType, sceneName, out sceneMode, out resolvedName))
                    return "ERROR:no_scene_enum:" + sceneName;

                if (!TrySetNextMode(mgr, modeType, sceneMode))
                    return "ERROR:no_setNextMode";

                return "OK:api:" + resolvedName;
            }
            catch (Exception ex)
            {
                return "ERROR:" + ex.GetBaseException().Message;
            }
        }

        private HubButtonSnapshot BuildHubButtonSnapshot(object box, string scene, HubButtonDescriptor descriptor)
        {
            var button = box != null ? GetProp(box, descriptor.MemberName) : null;
            var label = button != null ? TryExtractButtonLabel(button) : null;
            if (string.IsNullOrWhiteSpace(label))
                label = descriptor.DefaultLabel;

            var enabled = button != null && IsButtonEnabled(button);
            int x;
            int y;
            if (button == null || !TryGetScreenPos(button, out x, out y))
            {
                x = 0;
                y = 0;
            }

            return new HubButtonSnapshot
            {
                Scene = scene,
                ButtonKey = descriptor.ButtonKey,
                Label = label,
                Enabled = enabled,
                ScreenX = x,
                ScreenY = y,
                Detail = "Box." + descriptor.MemberName + (button == null ? ":missing" : string.Empty),
                TargetScene = descriptor.TargetScene
            };
        }

        private OtherModeSnapshot BuildOtherModeSnapshot(object buttonModel)
        {
            if (buttonModel == null)
                return null;

            var id = (int)ToLong(GetProp(buttonModel, "GameModeRecordId"));
            if (id <= 0)
                return null;

            var record = GetGameModeRecord(id);
            return new OtherModeSnapshot
            {
                GameModeRecordId = id,
                Name = (GetProp(buttonModel, "Name") ?? string.Empty).ToString(),
                Description = (GetProp(buttonModel, "Description") ?? string.Empty).ToString(),
                LinkedScene = (record != null ? GetProp(record, "LinkedScene") : null)?.ToString() ?? string.Empty,
                ModeKey = (record != null ? GetProp(record, "ModeKey") : null)?.ToString() ?? string.Empty,
                IsDownloadRequired = ToBool(GetProp(buttonModel, "IsDownloadRequired")),
                IsDownloading = ToBool(GetProp(buttonModel, "IsDownloading"))
            };
        }

        private object GetGameModeRecord(int recordId)
        {
            if (_asm == null || recordId <= 0)
                return null;

            var gameDbfType = _asm.GetType("GameDbf");
            if (gameDbfType == null)
                return null;

            var table = GetStaticValue(gameDbfType, "GameMode");
            if (table == null)
                return null;

            return CallMethod(table, "GetRecord", recordId);
        }

        private static string FormatHubButtonSnapshot(HubButtonSnapshot snapshot)
        {
            if (snapshot == null)
                return string.Empty;

            return string.Join("|", new[]
            {
                "scene=" + EncodeProtocolValue(snapshot.Scene),
                "buttonKey=" + EncodeProtocolValue(snapshot.ButtonKey),
                "label=" + EncodeProtocolValue(snapshot.Label),
                "enabled=" + (snapshot.Enabled ? "1" : "0"),
                "screenX=" + snapshot.ScreenX,
                "screenY=" + snapshot.ScreenY,
                "detail=" + EncodeProtocolValue(snapshot.Detail)
            });
        }

        private static string FormatOtherModeSnapshot(OtherModeSnapshot snapshot)
        {
            if (snapshot == null)
                return string.Empty;

            return string.Join("|", new[]
            {
                "gameModeRecordId=" + snapshot.GameModeRecordId,
                "name=" + EncodeProtocolValue(snapshot.Name),
                "description=" + EncodeProtocolValue(snapshot.Description),
                "linkedScene=" + EncodeProtocolValue(snapshot.LinkedScene),
                "modeKey=" + EncodeProtocolValue(snapshot.ModeKey),
                "isDownloadRequired=" + (snapshot.IsDownloadRequired ? "1" : "0"),
                "isDownloading=" + (snapshot.IsDownloading ? "1" : "0")
            });
        }

        private static string EncodeProtocolValue(string value)
        {
            return Uri.EscapeDataString(value ?? string.Empty);
        }

        private static bool TryResolveEnumValue(Type enumType, string requestedName, out object value, out string resolvedName)
        {
            value = null;
            resolvedName = null;
            if (enumType == null || !enumType.IsEnum || string.IsNullOrWhiteSpace(requestedName))
                return false;

            var expected = NormalizeToken(requestedName);
            foreach (var name in Enum.GetNames(enumType))
            {
                if (!string.Equals(NormalizeToken(name), expected, StringComparison.Ordinal))
                    continue;

                try
                {
                    value = Enum.Parse(enumType, name);
                    resolvedName = name;
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        private static string NormalizeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return new string(value
                .Trim()
                .ToUpperInvariant()
                .Where(char.IsLetterOrDigit)
                .ToArray());
        }

        private static bool TrySetNextMode(object sceneMgr, Type modeType, object sceneMode)
        {
            if (sceneMgr == null || modeType == null || sceneMode == null)
                return false;

            var methods = sceneMgr.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(m => m.Name == "SetNextMode")
                .OrderBy(m => m.GetParameters().Length)
                .ToArray();

            foreach (var method in methods)
            {
                var pars = method.GetParameters();
                if (pars.Length < 1 || pars[0].ParameterType != modeType)
                    continue;

                var args = new object[pars.Length];
                args[0] = sceneMode;
                for (var i = 1; i < pars.Length; i++)
                    args[i] = pars[i].HasDefaultValue ? pars[i].DefaultValue : GetDefault(pars[i].ParameterType);

                try
                {
                    method.Invoke(sceneMgr, args);
                    return true;
                }
                catch
                {
                }
            }

            return false;
        }

        private static HubButtonDescriptor FindHubButtonDescriptor(string buttonKey)
        {
            var expected = NormalizeToken(buttonKey);
            return HubButtonDescriptors.FirstOrDefault(d =>
                string.Equals(NormalizeToken(d.ButtonKey), expected, StringComparison.Ordinal));
        }

        private static bool IsButtonEnabled(object buttonObj)
        {
            if (buttonObj == null)
                return false;

            bool enabled;
            foreach (var memberName in new[] { "IsEnabled", "Enabled", "enabled", "m_isEnabled", "m_enabled" })
            {
                if (TryGetBoolLike(buttonObj, memberName, out enabled))
                    return enabled;
            }

            return true;
        }

        private static bool ToBool(object value)
        {
            if (value == null)
                return false;
            if (value is bool b)
                return b;

            var text = value.ToString();
            return string.Equals(text, "1", StringComparison.Ordinal)
                || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase);
        }

        private long GetSelectedDeckId(object dpt)
        {
            return ToLong(Call(dpt, "GetSelectedDeckID")
                ?? Call(dpt, "GetSelectedDeckId")
                ?? GetProp(dpt, "SelectedDeckID")
                ?? GetProp(dpt, "SelectedDeckId"));
        }

        private bool TryFindDeckBoxById(object dpt, long deckId, out object deckBox, out int pageIndex, out int deckIndex)
        {
            deckBox = null;
            pageIndex = -1;
            deckIndex = -1;
            if (dpt == null || deckId <= 0)
                return false;

            var currentPageIndex = 0;
            foreach (var page in AsEnumerable(GetProp(dpt, "m_customPages")))
            {
                var currentDeckIndex = 0;
                foreach (var candidate in AsEnumerable(GetProp(page, "m_customDecks")))
                {
                    var id = ToLong(GetProp(candidate, "m_deckID")
                        ?? GetProp(candidate, "m_deckId")
                        ?? GetProp(candidate, "DeckID")
                        ?? GetProp(candidate, "DeckId")
                        ?? GetProp(candidate, "m_preconDeckID"));
                    if (id == deckId)
                    {
                        deckBox = candidate;
                        pageIndex = currentPageIndex;
                        deckIndex = currentDeckIndex;
                        return true;
                    }

                    currentDeckIndex++;
                }

                currentPageIndex++;
            }

            return false;
        }

        private static bool CanSelectDeckBox(object deckBox)
        {
            if (deckBox == null)
                return false;

            var canSelect = Call(deckBox, "CanSelectDeck");
            if (canSelect is bool b)
                return b;

            if (TryGetBoolLike(deckBox, "IsLocked", out var isLocked) && isLocked)
                return false;

            return true;
        }

        private bool TryFindTournamentEntryPos(out int x, out int y, out string detail)
        {
            x = y = 0;
            detail = null;

            var sceneMgr = CallStatic(_sceneMgrType, "Get");
            var scene = sceneMgr != null ? Call(sceneMgr, "GetScene") : null;
            var roots = new[] { sceneMgr, scene }.Where(r => r != null).ToArray();

            foreach (var root in roots)
            {
                if (TryFindByNames(root, new[]
                {
                    "m_tournamentButton",
                    "m_constructedButton",
                    "m_playButton",
                    "m_playButtonWidgetReference"
                }, out x, out y, out detail))
                {
                    return true;
                }

                if (TryFindByToken(root, "tournament", out x, out y, out detail)
                    || TryFindByToken(root, "constructed", out x, out y, out detail)
                    || TryFindByToken(root, "play", out x, out y, out detail))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryFindFormatPos(object dpt, int vft, out int x, out int y, out string detail)
        {
            x = y = 0;
            detail = null;
            if (dpt == null) return false;

            var token = GetFormatToken(vft);
            if (string.IsNullOrEmpty(token)) return false;

            var rankedDisplay = GetProp(dpt, "m_rankedPlayDisplay");
            if (rankedDisplay != null)
            {
                if (TryFindByNames(rankedDisplay, new[]
                {
                    "m_" + token + "Button",
                    token + "Button",
                    "m_" + token + "ModeButton",
                    token + "ModeButton"
                }, out x, out y, out detail))
                {
                    return true;
                }

                if (TryFindByToken(rankedDisplay, token, out x, out y, out detail))
                    return true;
            }

            if (TryFindByToken(dpt, token, out x, out y, out detail))
                return true;

            return false;
        }

        private bool TryFindDeckPosById(object dpt, long deckId, out int x, out int y, out string detail)
        {
            x = y = 0;
            detail = null;
            if (dpt == null) return false;

            var pageIndex = 0;
            foreach (var page in AsEnumerable(GetProp(dpt, "m_customPages")))
            {
                var deckIndex = 0;
                foreach (var deck in AsEnumerable(GetProp(page, "m_customDecks")))
                {
                    var id = ToLong(GetProp(deck, "m_deckID")
                        ?? GetProp(deck, "m_deckId")
                        ?? GetProp(deck, "DeckID")
                        ?? GetProp(deck, "DeckId")
                        ?? GetProp(deck, "m_preconDeckID"));
                    if (id == deckId && TryGetScreenPos(deck, out x, out y))
                    {
                        detail = "m_customPages[" + pageIndex + "].m_customDecks[" + deckIndex + "]";
                        return true;
                    }

                    deckIndex++;
                }
                pageIndex++;
            }

            return false;
        }

        private bool IsFormatSelected(int vft)
        {
            if (_asm == null) return false;
            var optionsType = _asm.GetType("Options");
            if (optionsType == null) return false;
            var options = CallStatic(optionsType, "Get");
            if (options == null) return false;

            var formatObj = Call(options, "GetFormatType")
                ?? GetProp(options, "m_formatType")
                ?? GetProp(options, "FormatType");
            var format = (formatObj?.ToString() ?? string.Empty).ToUpperInvariant();

            var rankedObj = Call(options, "GetInRankedPlayMode")
                ?? GetProp(options, "m_inRankedPlayMode")
                ?? GetProp(options, "InRankedPlayMode");
            var inRanked = true;
            if (rankedObj is bool b) inRanked = b;

            switch (vft)
            {
                case 1: return inRanked && format.Contains("WILD");
                case 2: return inRanked && format.Contains("STANDARD");
                case 3: return inRanked && format.Contains("CLASSIC");
                case 4: return !inRanked;
                case 5: return inRanked && format.Contains("TWIST");
                default: return false;
            }
        }

        private static string GetFormatToken(int vft)
        {
            switch (vft)
            {
                case 1: return "wild";
                case 2: return "standard";
                case 3: return "classic";
                case 4: return "casual";
                case 5: return "twist";
                default: return null;
            }
        }

        private BlockingDialogHit FindBlockingDialogHit()
        {
            var roots = GetBlockingDialogRoots().ToArray();
            if (roots.Length == 0)
                return null;

            var nodes = EnumerateObjectGraph(roots, 4).ToArray();
            BlockingDialogHit fallback = null;

            foreach (var knownType in KnownBlockingDialogTypes)
            {
                foreach (var node in nodes)
                {
                    if (!string.Equals(node.obj.GetType().Name, knownType, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!TryBuildBlockingDialogHit(node.obj, node.path, out var hit))
                        continue;
                    if (hit.CanDismiss)
                        return hit;
                    if (fallback == null)
                        fallback = hit;
                }
            }

            var startupRatingsHit = FindStartupRatingsHit();
            if (startupRatingsHit != null)
                return startupRatingsHit;

            foreach (var node in nodes)
            {
                if (!LooksLikeBlockingDialogObject(node.obj, node.path))
                    continue;
                if (!TryBuildBlockingDialogHit(node.obj, node.path, out var hit))
                    continue;
                if (hit.CanDismiss)
                    return hit;
                if (fallback == null)
                    fallback = hit;
            }

            return fallback;
        }

        private BlockingDialogHit FindStartupRatingsHit()
        {
            var popup = FindStartupRatingsPopup();
            if (popup == null)
                return null;

            return new BlockingDialogHit
            {
                DialogType = StartupRatingsDialogType,
                ButtonLabel = StartupRatingsButtonLabel,
                CanDismiss = true,
                IsRetryButton = false,
                Detail = popup.GetType().FullName ?? popup.GetType().Name
            };
        }

        private object FindStartupRatingsPopup()
        {
            if (_asm == null || !IsStartupOrLoginScene(GetSceneInternal()))
                return null;

            var splashScreenType = _asm.GetType("SplashScreen");
            if (splashScreenType == null)
                return null;

            var splashScreen = CallStatic(splashScreenType, "Get")
                ?? GetStaticValue(splashScreenType, "s_instance");
            if (splashScreen == null || !IsObjectProbablyVisible(splashScreen))
                return null;

            var ratingsPopupType = _asm.GetType("RatingsPopupControl");
            if (ratingsPopupType == null)
                return null;

            UnityEngine.Object[] popups;
            try
            {
                popups = UnityEngine.Object.FindObjectsOfType(ratingsPopupType, true);
            }
            catch
            {
                try
                {
                    popups = UnityEngine.Object.FindObjectsOfType(ratingsPopupType);
                }
                catch
                {
                    return null;
                }
            }

            if (popups == null || popups.Length == 0)
                return null;

            object fallback = null;
            foreach (var popupObj in popups)
            {
                var popup = (object)popupObj;
                if (popup == null || !IsObjectProbablyVisible(popup))
                    continue;
                if (!TryGetBoolLike(popup, "WaitForUserToStart", out var waitForUserToStart) || !waitForUserToStart)
                    continue;

                var widget = GetProp(popup, "m_widget") ?? GetProp(popup, "Widget");
                if (widget != null && IsObjectProbablyVisible(widget))
                    return popup;

                if (fallback == null)
                    fallback = popup;
            }

            return fallback;
        }

        private string DismissStartupRatings()
        {
            var eventTriggered = OnMain(() =>
            {
                if (!Init())
                    return false;

                var popup = FindStartupRatingsPopup();
                if (popup == null)
                    return false;

                var widget = GetProp(popup, "m_widget") ?? GetProp(popup, "Widget");
                if (widget == null || !IsObjectProbablyVisible(widget))
                    return false;

                var eventName = (GetProp(popup, "m_startPressedEvent") as string) ?? StartupRatingsStartPressedEvent;
                var triggerResult = CallMethod(widget, "TriggerEvent", eventName);
                return triggerResult != null;
            });

            if (eventTriggered && WaitForStartupRatingsDismissed())
                return "OK:" + StartupRatingsDialogType + ":" + StartupRatingsButtonLabel;

            var clickResult = ClickAtRatio(StartupRatingsFallbackClickX, StartupRatingsFallbackClickY, 0.25f);
            if (string.IsNullOrWhiteSpace(clickResult)
                || !clickResult.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
            {
                return "FAIL:" + StartupRatingsDialogType + ":" + (string.IsNullOrWhiteSpace(clickResult) ? "click_failed" : clickResult);
            }

            return WaitForStartupRatingsDismissed()
                ? "OK:" + StartupRatingsDialogType + ":" + StartupRatingsButtonLabel
                : "FAIL:" + StartupRatingsDialogType + ":still_present";
        }

        private bool WaitForStartupRatingsDismissed()
        {
            for (var i = 0; i < StartupRatingsDismissPollAttempts; i++)
            {
                Thread.Sleep(StartupRatingsDismissPollIntervalMs);
                var stillPresent = OnMain(() =>
                {
                    if (!Init())
                        return false;
                    return FindStartupRatingsPopup() != null;
                });
                if (!stillPresent)
                    return true;
            }

            return OnMain(() =>
            {
                if (!Init())
                    return true;
                return FindStartupRatingsPopup() == null;
            });
        }

        private static bool IsStartupOrLoginScene(string scene)
        {
            return string.Equals(scene, "STARTUP", StringComparison.OrdinalIgnoreCase)
                || string.Equals(scene, "LOGIN", StringComparison.OrdinalIgnoreCase);
        }

        private IEnumerable<(object obj, string path)> GetBlockingDialogRoots()
        {
            var roots = new List<(object obj, string path)>();
            var seen = new HashSet<object>();

            void TryAdd(object root, string path)
            {
                if (root == null || string.IsNullOrWhiteSpace(path))
                    return;
                if (seen.Add(root))
                    roots.Add((root, path));
            }

            var sceneMgr = CallStatic(_sceneMgrType, "Get");
            TryAdd(sceneMgr, "SceneMgr.Get()");
            TryAdd(sceneMgr != null ? Call(sceneMgr, "GetScene") : null, "SceneMgr.Get().Scene");

            foreach (var typeName in KnownBlockingDialogTypes.Concat(new[] { "DialogManager", "PopupManager" }))
            {
                var type = _asm?.GetType(typeName);
                if (type == null)
                    continue;

                TryAdd(CallStatic(type, "Get"), typeName + ".Get()");
                TryAdd(CallStatic(type, "GetInstance"), typeName + ".GetInstance()");
                TryAdd(GetStaticValue(type, "Instance"), typeName + ".Instance");
                TryAdd(GetStaticValue(type, "s_instance"), typeName + ".s_instance");
            }

            return roots;
        }

        private IEnumerable<(object obj, string path)> EnumerateObjectGraph(IEnumerable<(object obj, string path)> roots, int maxDepth)
        {
            var queue = new Queue<(object obj, string path, int depth)>();
            var visited = new HashSet<object>();
            foreach (var root in roots)
            {
                if (root.obj == null || !visited.Add(root.obj))
                    continue;
                queue.Enqueue((root.obj, root.path, 0));
            }

            var yielded = 0;
            while (queue.Count > 0 && yielded < 600)
            {
                var item = queue.Dequeue();
                yielded++;
                yield return (item.obj, item.path);

                if (item.depth >= maxDepth || item.obj == null)
                    continue;

                var members = item.obj.GetType()
                    .GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(m => m.MemberType == MemberTypes.Field || m.MemberType == MemberTypes.Property);

                foreach (var member in members)
                {
                    object value = null;
                    try
                    {
                        if (member is FieldInfo fi)
                            value = fi.GetValue(item.obj);
                        else if (member is PropertyInfo pi && pi.GetIndexParameters().Length == 0)
                            value = pi.GetValue(item.obj, null);
                    }
                    catch
                    {
                    }

                    if (value == null || value is string)
                        continue;

                    var memberName = member.Name ?? string.Empty;
                    if (memberName.IndexOf("Reference", StringComparison.OrdinalIgnoreCase) >= 0)
                        value = ResolveAsyncReference(value) ?? value;

                    if (value is IEnumerable enumerable)
                    {
                        var idx = 0;
                        foreach (var element in enumerable)
                        {
                            if (idx >= 40)
                                break;
                            if (element != null && visited.Add(element))
                                queue.Enqueue((element, item.path + "." + memberName + "[" + idx + "]", item.depth + 1));
                            idx++;
                        }
                    }
                    else if (!value.GetType().IsValueType && visited.Add(value))
                    {
                        queue.Enqueue((value, item.path + "." + memberName, item.depth + 1));
                    }
                }
            }
        }

        private bool LooksLikeBlockingDialogObject(object obj, string path)
        {
            if (obj == null)
                return false;

            var typeName = obj.GetType().Name ?? string.Empty;
            if (LooksLikeButtonObject(typeName, path))
                return false;
            if (typeName.IndexOf("Manager", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.EndsWith("Mgr", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return ContainsAnyToken(typeName, GenericBlockingDialogTokens)
                || ContainsAnyToken(path, GenericBlockingDialogTokens);
        }

        private bool TryBuildBlockingDialogHit(object dialogObj, string path, out BlockingDialogHit hit)
        {
            hit = null;
            if (dialogObj == null || !IsObjectProbablyVisible(dialogObj))
                return false;

            var candidates = new List<BlockingDialogHit>();

            foreach (var memberName in BlockingDialogButtonMemberNames)
            {
                var button = GetProp(dialogObj, memberName);
                if (button == null)
                    continue;
                if (memberName.IndexOf("Reference", StringComparison.OrdinalIgnoreCase) >= 0)
                    button = ResolveAsyncReference(button) ?? button;
                TryAddBlockingDialogButtonCandidate(candidates, button, path + "." + memberName, dialogObj.GetType().Name);
            }

            foreach (var node in EnumerateObjectGraph(new[] { (dialogObj, path) }, 2))
            {
                if (ReferenceEquals(node.obj, dialogObj))
                    continue;
                if (!LooksLikeButtonObject(node.obj.GetType().Name, node.path))
                    continue;
                TryAddBlockingDialogButtonCandidate(candidates, node.obj, node.path, dialogObj.GetType().Name);
            }

            if (candidates.Count == 0)
                return false;

            hit = candidates.FirstOrDefault(c => c.CanDismiss)
                ?? candidates.FirstOrDefault(c => !c.IsRetryButton)
                ?? candidates[0];
            return hit != null;
        }

        private void TryAddBlockingDialogButtonCandidate(List<BlockingDialogHit> candidates, object buttonObj, string path, string dialogType)
        {
            if (buttonObj == null || !IsObjectProbablyVisible(buttonObj))
                return;
            if (!TryGetScreenPos(buttonObj, out var x, out var y))
                return;

            var label = TryExtractButtonLabel(buttonObj);
            if (string.IsNullOrWhiteSpace(label))
                label = "UNKNOWN";

            candidates.Add(new BlockingDialogHit
            {
                DialogType = string.IsNullOrWhiteSpace(dialogType) ? "UnknownDialog" : dialogType,
                ButtonLabel = label,
                ButtonX = x,
                ButtonY = y,
                CanDismiss = IsSafeBlockingDialogButtonLabel(label),
                IsRetryButton = IsRetryBlockingDialogButtonLabel(label),
                Detail = path
            });
        }

        private static bool LooksLikeButtonObject(string typeName, string path)
        {
            return (!string.IsNullOrWhiteSpace(typeName)
                    && typeName.IndexOf("button", StringComparison.OrdinalIgnoreCase) >= 0)
                || (!string.IsNullOrWhiteSpace(path)
                    && path.IndexOf("button", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool ContainsAnyToken(string value, IEnumerable<string> tokens)
        {
            if (string.IsNullOrWhiteSpace(value) || tokens == null)
                return false;

            foreach (var token in tokens)
            {
                if (!string.IsNullOrWhiteSpace(token)
                    && value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string TryExtractButtonLabel(object obj)
        {
            return TryExtractButtonLabel(obj, 0, new HashSet<object>());
        }

        private static string TryExtractButtonLabel(object obj, int depth, HashSet<object> visited)
        {
            if (obj == null || depth > 3 || !visited.Add(obj))
                return null;
            if (obj is string s)
                return NormalizeButtonLabel(s);

            foreach (var memberName in ButtonTextMemberNames)
            {
                var value = GetProp(obj, memberName);
                if (value == null)
                    continue;

                if (value is string text)
                {
                    var normalized = NormalizeButtonLabel(text);
                    if (!string.IsNullOrWhiteSpace(normalized))
                        return normalized;
                    continue;
                }

                var nested = TryExtractButtonLabel(value, depth + 1, visited);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }

            foreach (var methodName in new[] { "GetText", "GetLabelText", "GetButtonText" })
            {
                var value = Call(obj, methodName);
                if (value is string text)
                {
                    var normalized = NormalizeButtonLabel(text);
                    if (!string.IsNullOrWhiteSpace(normalized))
                        return normalized;
                }
            }

            return null;
        }

        private static string NormalizeButtonLabel(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return string.Join(" ", text
                .Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
                .Trim();
        }

        private static bool IsSafeBlockingDialogButtonLabel(string label)
        {
            var normalized = NormalizeDialogButtonToken(label);
            return normalized == "ok"
                || normalized == "okay"
                || normalized == NormalizeDialogButtonToken(StartupRatingsButtonLabel)
                || normalized == "确认"
                || normalized == "确定"
                || normalized == "关闭"
                || normalized == "返回"
                || normalized == "取消";
        }

        private static bool IsRetryBlockingDialogButtonLabel(string label)
        {
            var normalized = NormalizeDialogButtonToken(label);
            return normalized == "重连"
                || normalized == "重新连接"
                || normalized == "重试"
                || normalized == "reconnect"
                || normalized == "tryagain";
        }

        private static string NormalizeDialogButtonToken(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
                return string.Empty;

            return new string(label
                .Trim()
                .ToLowerInvariant()
                .Where(c => !char.IsWhiteSpace(c) && c != '_' && c != '-' && c != ':')
                .ToArray());
        }

        private static bool IsObjectProbablyVisible(object obj)
        {
            if (obj == null)
                return false;

            foreach (var name in VisibilityMemberNames)
            {
                if (TryGetBoolLike(obj, name, out var value) && !value)
                    return false;
            }

            var gameObject = GetProp(obj, "gameObject")
                ?? GetProp(obj, "GameObject")
                ?? GetProp(obj, "m_gameObject")
                ?? GetProp(obj, "m_root")
                ?? GetProp(obj, "m_RootObject");
            if (gameObject != null)
            {
                foreach (var name in new[] { "activeSelf", "activeInHierarchy" })
                {
                    if (TryGetBoolLike(gameObject, name, out var active) && !active)
                        return false;
                }
            }

            return true;
        }

        private static bool TryGetBoolLike(object obj, string name, out bool value)
        {
            value = false;
            if (obj == null || string.IsNullOrWhiteSpace(name))
                return false;

            var raw = GetProp(obj, name);
            if (raw is bool b)
            {
                value = b;
                return true;
            }

            var called = Call(obj, name);
            if (called is bool cb)
            {
                value = cb;
                return true;
            }

            return false;
        }

        private bool TryFindByNames(object root, IEnumerable<string> names, out int x, out int y, out string detail)
        {
            x = y = 0;
            detail = null;
            if (root == null || names == null) return false;

            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var value = GetProp(root, name);
                if (value == null) continue;
                if (name.IndexOf("Reference", StringComparison.OrdinalIgnoreCase) >= 0)
                    value = ResolveAsyncReference(value) ?? value;

                if (TryGetScreenPos(value, out x, out y))
                {
                    detail = root.GetType().Name + "." + name;
                    return true;
                }
            }

            return false;
        }

        private bool TryFindByToken(object root, string token, out int x, out int y, out string detail)
        {
            x = y = 0;
            detail = null;
            if (root == null || string.IsNullOrWhiteSpace(token)) return false;

            var queue = new Queue<(object obj, string path, int depth)>();
            var visited = new HashSet<object>();
            queue.Enqueue((root, root.GetType().Name, 0));
            visited.Add(root);

            while (queue.Count > 0)
            {
                var item = queue.Dequeue();
                if (item.depth > 2) continue;

                var members = item.obj.GetType()
                    .GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(m => m.MemberType == MemberTypes.Field || m.MemberType == MemberTypes.Property);

                foreach (var member in members)
                {
                    object value = null;
                    try
                    {
                        if (member is FieldInfo fi)
                            value = fi.GetValue(item.obj);
                        else if (member is PropertyInfo pi && pi.GetIndexParameters().Length == 0)
                            value = pi.GetValue(item.obj, null);
                    }
                    catch
                    {
                    }

                    if (value == null || value is string) continue;

                    var memberName = member.Name ?? string.Empty;
                    if (memberName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var candidate = memberName.IndexOf("Reference", StringComparison.OrdinalIgnoreCase) >= 0
                            ? (ResolveAsyncReference(value) ?? value)
                            : value;
                        if (TryGetScreenPos(candidate, out x, out y))
                        {
                            detail = item.path + "." + memberName;
                            return true;
                        }
                    }

                    if (item.depth >= 2) continue;

                    if (value is IEnumerable enumerable)
                    {
                        var idx = 0;
                        foreach (var element in enumerable)
                        {
                            if (idx >= 30) break;
                            if (element != null && visited.Add(element))
                                queue.Enqueue((element, item.path + "." + memberName + "[" + idx + "]", item.depth + 1));
                            idx++;
                        }
                    }
                    else if (!value.GetType().IsValueType && visited.Add(value))
                    {
                        queue.Enqueue((value, item.path + "." + memberName, item.depth + 1));
                    }
                }
            }

            return false;
        }

        private static bool TryGetScreenPos(object obj, out int x, out int y)
        {
            x = y = 0;
            if (obj == null) return false;
            if (!TryGetWorldPos(obj, 0, out var wx, out var wy, out var wz))
                return false;
            if (!MouseSimulator.WorldToScreen(wx, wy, wz, out x, out y))
                return false;

            var w = MouseSimulator.GetScreenWidth();
            var h = MouseSimulator.GetScreenHeight();
            return w > 0 && h > 0 && x > 5 && y > 5 && x < w - 5 && y < h - 5;
        }

        private static bool TryGetWorldPos(object obj, int depth, out float x, out float y, out float z)
        {
            x = y = z = 0;
            if (obj == null || depth > 5) return false;

            if (TryReadVector(obj, out x, out y, out z))
                return true;

            foreach (var name in new[]
            {
                "position", "Position", "center", "Center", "m_Center",
                "Transform", "transform", "m_transform",
                "gameObject", "GameObject", "m_RootObject", "RootObject",
                "Renderer", "renderer", "Bounds", "bounds",
                "m_ButtonText", "m_newPlayButtonText", "m_TextMeshGameObject"
            })
            {
                var child = GetProp(obj, name);
                if (child == null) continue;
                if (TryReadVector(child, out x, out y, out z)) return true;
                if (TryGetWorldPos(child, depth + 1, out x, out y, out z)) return true;
            }

            return false;
        }

        private static bool TryReadVector(object obj, out float x, out float y, out float z)
        {
            x = y = z = 0;
            if (obj == null) return false;

            if (!TryGetFloat(obj, "x", out x) && !TryGetFloat(obj, "X", out x))
                return false;
            if (!TryGetFloat(obj, "y", out y) && !TryGetFloat(obj, "Y", out y))
                return false;
            if (!TryGetFloat(obj, "z", out z) && !TryGetFloat(obj, "Z", out z))
                return false;
            return true;
        }

        private static bool TryGetFloat(object obj, string name, out float value)
        {
            value = 0;
            var raw = GetProp(obj, name);
            if (raw == null) return false;
            try
            {
                value = Convert.ToSingle(raw);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetFallbackTournamentPos(out int x, out int y)
        {
            x = y = 0;
            var w = MouseSimulator.GetScreenWidth();
            var h = MouseSimulator.GetScreenHeight();
            if (w <= 0 || h <= 0) return false;
            x = (int)(w * 0.16f);
            y = (int)(h * 0.53f);
            return true;
        }

        private static bool TryGetFallbackFormatPos(int vft, out int x, out int y)
        {
            x = y = 0;
            var w = MouseSimulator.GetScreenWidth();
            var h = MouseSimulator.GetScreenHeight();
            if (w <= 0 || h <= 0) return false;
            y = (int)(h * 0.88f);
            switch (vft)
            {
                case 4: x = (int)(w * 0.34f); break;
                case 2: x = (int)(w * 0.42f); break;
                case 1: x = (int)(w * 0.50f); break;
                case 3: x = (int)(w * 0.58f); break;
                case 5: x = (int)(w * 0.66f); break;
                default: x = w / 2; break;
            }
            return true;
        }

        private static bool TryGetFallbackDeckPos(out int x, out int y)
        {
            x = y = 0;
            var w = MouseSimulator.GetScreenWidth();
            var h = MouseSimulator.GetScreenHeight();
            if (w <= 0 || h <= 0) return false;
            x = w / 2;
            y = (int)(h * 0.72f);
            return true;
        }

        private static bool TryGetFallbackPlayPos(out int x, out int y)
        {
            x = y = 0;
            var w = MouseSimulator.GetScreenWidth();
            var h = MouseSimulator.GetScreenHeight();
            if (w <= 0 || h <= 0) return false;
            x = (int)(w * 0.82f);
            y = (int)(h * 0.86f);
            return true;
        }

        public string ClickDismiss()
        {
            string screenTypeName = null;
            var isBattlegroundsEndGame = false;

            // 优先触发 EndGameScreen 的 hitbox，这能覆盖段位变化等依赖真正 RELEASE 事件的结算层。
            var apiResult = OnMain(() =>
            {
                try
                {
                    if (_asm == null) return (object)null;
                    var egType = _asm.GetType("EndGameScreen");
                    if (egType == null) return (object)null;

                    var screen = egType.GetMethod("Get",
                            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                        ?.Invoke(null, null);
                    if (screen == null) return (object)null;

                    screenTypeName = screen.GetType().Name ?? string.Empty;
                    isBattlegroundsEndGame = screenTypeName.IndexOf("Bacon", StringComparison.OrdinalIgnoreCase) >= 0;

                    var hitbox = GetProp(screen, "m_hitbox");
                    if (hitbox != null && IsObjectProbablyVisible(hitbox))
                    {
                        CallMethod(hitbox, "TriggerPress");
                        CallMethod(hitbox, "TriggerRelease");
                        return (object)("HITBOX:" + (string.IsNullOrWhiteSpace(screenTypeName) ? "EndGameScreen" : screenTypeName));
                    }

                    // hitbox 不可用时，再退回到已知的继续方法。
                    var names = new[]
                    {
                        "ContinueEvents", "Continue", "ContinueAfterScoreScreen",
                        "OnTwoScoopsShown", "ShowStandardFlowIfReady",
                        "OnBackOutOfGameplay", "Hide"
                    };
                    foreach (var name in names)
                    {
                        try
                        {
                            var method = screen.GetType().GetMethod(name,
                                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (method != null && method.GetParameters().Length == 0)
                            {
                                method.Invoke(screen, null);
                                return (object)("API:" + name);
                            }
                        }
                        catch { }
                    }
                    return (object)null;
                }
                catch
                {
                    return (object)null;
                }
            });

            var apiResultStr = apiResult as string;
            var mouseResult = "SKIPPED";

            // 无论 API 是否成功，始终执行鼠标点击序列作为兜底/补充
            var dims = OnMain(() =>
            {
                var sw = MouseSimulator.GetScreenWidth();
                var sh = MouseSimulator.GetScreenHeight();
                return new int[] { sw, sh };
            });
            var w = dims[0];
            var h = dims[1];
            if (w > 0 && h > 0)
            {
                mouseResult = _coroutine.RunAndWait(MouseDismissClickSequence(w, h, isBattlegroundsEndGame), DismissClickTimeoutMs);
            }

            if (!string.IsNullOrEmpty(apiResultStr))
                return "OK:bg_dismiss:" + apiResultStr + "|mouse=" + (mouseResult ?? "null");
            if (mouseResult != null && mouseResult.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
                return mouseResult;
            return "OK:bg_dismiss:click_sent|mouse=" + (mouseResult ?? "null");
        }

        private string ClickDismissLegacy()
        {
            // Screen.width/height 必须在主线程读取，后台线程返回 0
            // 但 ClickAt → RunAndWait 需要 Update() 驱动协程，
            // 不能放在 OnMain 里（否则死锁），所以只在主线程查屏幕尺寸
            var dims = OnMain(() =>
            {
                var sw = MouseSimulator.GetScreenWidth();
                var sh = MouseSimulator.GetScreenHeight();
                return new int[] { sw, sh };
            });
            var w = dims[0];
            var h = dims[1];
            if (w <= 0 || h <= 0) return "ERROR:no_screen";
            var result = _coroutine.RunAndWait(MouseDismissClickSequence(w, h, false), DismissClickTimeoutMs);
            return result ?? "ERROR:click_failed";
        }

        private string ClickAt(int x, int y, float delayAfter)
        {
            return _coroutine.RunAndWait(MouseClick(x, y, delayAfter));
        }

        private string ClickAtRatio(float ratioX, float ratioY, float delayAfter)
        {
            var dims = OnMain(() =>
            {
                var sw = MouseSimulator.GetScreenWidth();
                var sh = MouseSimulator.GetScreenHeight();
                return new[] { sw, sh };
            });
            var w = dims[0];
            var h = dims[1];
            if (w <= 0 || h <= 0)
                return "ERROR:no_screen";

            var x = Math.Max(6, Math.Min(w - 6, (int)(w * ratioX)));
            var y = Math.Max(6, Math.Min(h - 6, (int)(h * ratioY)));
            return ClickAt(x, y, delayAfter);
        }

        private IEnumerable<float> SmoothMove(int tx, int ty, int steps = 12)
        {
            var sx = MouseSimulator.CurX;
            var sy = MouseSimulator.CurY;
            for (var i = 1; i <= steps; i++)
            {
                MouseSimulator.MoveTo(sx + (tx - sx) * i / steps, sy + (ty - sy) * i / steps);
                yield return 0.02f;
            }
        }

        private IEnumerator<float> MouseClick(int x, int y, float delayAfter)
        {
            InputHook.Simulating = true;
            foreach (var wait in SmoothMove(x, y)) yield return wait;
            MouseSimulator.LeftDown();
            yield return 0.05f;
            MouseSimulator.LeftUp();
            yield return delayAfter;
            _coroutine.SetResult("OK");
        }

        /// <summary>
        /// 对齐标准对战原来的结算页点击：固定点击右下区域。
        /// </summary>
        private IEnumerator<float> MouseDismissClickSequence(int w, int h, bool isBattlegroundsEndGame)
        {
            InputHook.Simulating = true;
            foreach (var point in BuildDismissPoints(w, h, isBattlegroundsEndGame))
            {
                MouseSimulator.MoveTo(point.X, point.Y);
                yield return 0.03f;
                MouseSimulator.LeftDown();
                yield return 0.02f;
                MouseSimulator.LeftUp();
                yield return 0.06f;
            }

            _coroutine.SetResult("OK:bg_dismiss:screen_sequence");
        }

        private static ScreenPoint[] BuildDismissPoints(int width, int height, bool isBattlegroundsEndGame)
        {
            int ClampX(float ratio) => Math.Max(6, Math.Min(width - 6, (int)(width * ratio)));
            int ClampY(float ratio) => Math.Max(6, Math.Min(height - 6, (int)(height * ratio)));

            if (isBattlegroundsEndGame)
            {
                return new[]
                {
                    new ScreenPoint(ClampX(0.50f), ClampY(0.52f)), // 战旗名次牌中心
                    new ScreenPoint(ClampX(0.50f), ClampY(0.66f)), // 名次牌下半区
                    new ScreenPoint(ClampX(0.50f), ClampY(0.80f)), // 下方面板区域
                    new ScreenPoint(ClampX(0.50f), ClampY(0.90f)), // 靠近“点击继续”常见区域
                    new ScreenPoint(ClampX(0.50f), ClampY(0.66f)),
                };
            }

            return new[]
            {
                new ScreenPoint(ClampX(0.82f), ClampY(0.86f)), // 标准模式优先点右下安全区
                new ScreenPoint(ClampX(0.88f), ClampY(0.84f)), // 略偏右，避开中轴/手牌
                new ScreenPoint(ClampX(0.90f), ClampY(0.89f)), // 右下角兜底
                new ScreenPoint(ClampX(0.85f), ClampY(0.91f)), // 更靠下，继续避开中间手牌区
                new ScreenPoint(ClampX(0.82f), ClampY(0.86f)),
            };
        }

        private static string WrapClickResult(string clickResult, string detail)
        {
            if (clickResult != null && clickResult.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
                return "OK:" + detail;
            return clickResult ?? "ERROR:click_failed";
        }

        private struct ScreenPoint
        {
            public ScreenPoint(int x, int y)
            {
                X = x;
                Y = y;
            }

            public int X { get; }
            public int Y { get; }
        }

        private static object ResolveAsyncReference(object asyncRef)
        {
            if (asyncRef == null) return null;
            foreach (var n in new[] { "Asset", "Object", "asset", "m_asset", "m_object", "GameObject", "gameObject", "Target" })
            {
                var value = GetProp(asyncRef, n);
                if (value != null && value.ToString() != "null")
                    return value;
            }

            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (var methodName in new[] { "GetAsset", "GetObject", "Get" })
            {
                var method = asyncRef.GetType().GetMethod(methodName, flags, null, Type.EmptyTypes, null);
                if (method == null) continue;
                try
                {
                    var value = method.Invoke(asyncRef, null);
                    if (value != null && value.ToString() != "null")
                        return value;
                }
                catch
                {
                }
            }

            return null;
        }

        private static IEnumerable AsEnumerable(object obj)
        {
            if (obj == null || obj is string) return Enumerable.Empty<object>();
            return obj as IEnumerable ?? Enumerable.Empty<object>();
        }

        private static object UnwrapValue(object entry)
        {
            if (entry == null) return null;
            var prop = entry.GetType().GetProperty("Value",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return prop != null ? (prop.GetValue(entry, null) ?? entry) : entry;
        }

        private static long ToLong(object value)
        {
            if (value == null) return 0;
            try { return Convert.ToInt64(value); } catch { return 0; }
        }

        private static object GetProp(object obj, string name)
        {
            if (obj == null || string.IsNullOrWhiteSpace(name)) return null;
            var type = obj.GetType();
            var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null)
            {
                try { return prop.GetValue(obj, null); } catch { }
            }

            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                try { return field.GetValue(obj); } catch { }
            }

            return null;
        }

        private static object Call(object obj, string method)
        {
            if (obj == null || string.IsNullOrWhiteSpace(method)) return null;
            var mi = FindZeroArgMethod(obj.GetType(), method,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (mi == null) return null;
            try { return mi.Invoke(obj, null); } catch { return null; }
        }

        private static object GetDefault(Type t)
        {
            return t.IsValueType ? Activator.CreateInstance(t) : null;
        }

        // void方法Invoke返回null，用哨兵值区分"方法存在但返回void"和"方法不存在"
        private static readonly object _voidResult = new object();

        private static object CallMethod(object obj, string method, params object[] args)
        {
            if (obj == null) return null;
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var methods = obj.GetType().GetMethods(flags).Where(m => m.Name == method).ToArray();
            foreach (var mi in methods)
            {
                var pars = mi.GetParameters();
                if (pars.Length == args.Length)
                {
                    try { return mi.Invoke(obj, args) ?? _voidResult; } catch { }
                }
            }
            // 尝试带默认参数的重载
            foreach (var mi in methods)
            {
                var pars = mi.GetParameters();
                if (pars.Length > args.Length && pars.Skip(args.Length).All(p => p.HasDefaultValue))
                {
                    var fullArgs = new object[pars.Length];
                    Array.Copy(args, fullArgs, args.Length);
                    for (int i = args.Length; i < pars.Length; i++)
                        fullArgs[i] = pars[i].DefaultValue;
                    try { return mi.Invoke(obj, fullArgs) ?? _voidResult; } catch { }
                }
            }
            return null;
        }

        private static object CallStatic(Type type, string method)
        {
            if (type == null || string.IsNullOrWhiteSpace(method)) return null;
            var mi = FindZeroArgMethod(type, method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (mi == null) return null;
            try { return mi.Invoke(null, null); } catch { return null; }
        }

        private static MethodInfo FindZeroArgMethod(Type type, string method, BindingFlags flags)
        {
            if (type == null || string.IsNullOrWhiteSpace(method))
                return null;

            return type.GetMethods(flags)
                .FirstOrDefault(m => string.Equals(m.Name, method, StringComparison.Ordinal)
                    && m.GetParameters().Length == 0);
        }

        private static object GetStaticValue(Type type, string name)
        {
            if (type == null || string.IsNullOrWhiteSpace(name))
                return null;

            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            var prop = type.GetProperty(name, flags);
            if (prop != null)
            {
                try { return prop.GetValue(null, null); } catch { }
            }

            var field = type.GetField(name, flags);
            if (field != null)
            {
                try { return field.GetValue(null); } catch { }
            }

            return null;
        }
    }
}
