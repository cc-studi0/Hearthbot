using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace HearthstonePayload
{
    /// <summary>
    /// 场景检测和导航（仅读取状态，操作统一走鼠标模拟）
    /// </summary>
    public class SceneNavigator
    {
        private Assembly _asm;
        private Type _sceneMgrType;
        private Type _gameMgrType;
        private Type _collMgrType;
        private bool _initialized;
        private CoroutineExecutor _coroutine;
        private Func<Func<object>, object> _runOnMain;

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

        public string NavigateTo(string sceneName)
        {
            sceneName = (sceneName ?? string.Empty).Trim();
            if (!string.Equals(sceneName, "TOURNAMENT", StringComparison.OrdinalIgnoreCase))
                return "ERROR:unsupported_scene:" + sceneName;

            return OnMain(() =>
            {
                if (!Init()) return "ERROR:not_initialized";
                if (string.Equals(GetSceneInternal(), "TOURNAMENT", StringComparison.OrdinalIgnoreCase))
                    return "OK:already";

                try
                {
                    var mgr = CallStatic(_sceneMgrType, "Get");
                    if (mgr == null) return "ERROR:no_scenemgr";

                    // 解析 SceneMgr.Mode.TOURNAMENT 枚举值
                    var modeType = _sceneMgrType.GetNestedType("Mode")
                        ?? _asm.GetType("SceneMgr+Mode")
                        ?? _asm.GetType("SceneMode");
                    if (modeType == null) return "ERROR:no_mode_type";

                    object tournamentMode = null;
                    foreach (var name in new[] { "TOURNAMENT", "Tournament" })
                    {
                        try { tournamentMode = Enum.Parse(modeType, name); break; } catch { }
                    }
                    if (tournamentMode == null) return "ERROR:no_tournament_enum";

                    // 调用 SetNextMode(mode, ...)
                    var methods = _sceneMgrType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Where(m => m.Name == "SetNextMode")
                        .OrderBy(m => m.GetParameters().Length)
                        .ToArray();

                    foreach (var method in methods)
                    {
                        var pars = method.GetParameters();
                        if (pars.Length >= 1 && pars[0].ParameterType == modeType)
                        {
                            var args = new object[pars.Length];
                            args[0] = tournamentMode;
                            for (int i = 1; i < pars.Length; i++)
                                args[i] = pars[i].HasDefaultValue ? pars[i].DefaultValue : GetDefault(pars[i].ParameterType);
                            method.Invoke(mgr, args);
                            return "OK:api";
                        }
                    }

                    return "ERROR:no_setNextMode";
                }
                catch (Exception ex)
                {
                    return "ERROR:" + ex.GetBaseException().Message;
                }
            });
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
                    var method = mgr.GetType().GetMethod("IsFindingGame",
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
            return OnMain(() =>
            {
                if (!Init()) return "ERROR:not_initialized";
                if (IsFormatSelected(vft)) return "OK:already";

                try
                {
                    var optionsType = _asm.GetType("Options");
                    if (optionsType == null) return "ERROR:no_options_type";
                    var options = CallStatic(optionsType, "Get");
                    if (options == null) return "ERROR:no_options";

                    // vft=4 是休闲模式，其他是天梯模式
                    if (vft == 4)
                    {
                        CallMethod(options, "SetInRankedPlayMode", false);
                        return "OK:casual";
                    }

                    // 确保在天梯模式
                    CallMethod(options, "SetInRankedPlayMode", true);

                    // 找到 SetFormatType 方法，判断参数类型
                    var optFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                    var setFmt = options.GetType().GetMethods(optFlags)
                        .FirstOrDefault(m => m.Name == "SetFormatType" && m.GetParameters().Length >= 1);
                    // 回退：无参版本（可能参数通过其他方式传递）
                    if (setFmt == null)
                        setFmt = options.GetType().GetMethods(optFlags)
                            .FirstOrDefault(m => m.Name == "SetFormatType");
                    if (setFmt == null)
                    {
                        // 诊断：列出所有含 Format 的方法签名
                        var diag = options.GetType().GetMethods(optFlags)
                            .Where(m => m.Name.IndexOf("Format", StringComparison.OrdinalIgnoreCase) >= 0)
                            .Select(m => (m.IsStatic ? "S:" : "") + m.Name + "(" +
                                string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name)) + ")")
                            .Distinct().ToArray();
                        return "ERROR:no_SetFormatType|" + string.Join(";", diag);
                    }

                    var paramType = setFmt.GetParameters()[0].ParameterType;
                    object formatArg;

                    if (paramType.IsEnum)
                    {
                        // 枚举类型：尝试多种名称，最后按整数值兜底
                        formatArg = null;
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
                        // int 或其他数值类型：直接传 vft
                        formatArg = Convert.ChangeType(vft, paramType);
                    }

                    CallMethod(options, "SetFormatType", formatArg);

                    // 通过 DeckPickerTrayDisplay 触发实际 UI 切换
                    var dpt = GetDeckPickerTray();
                    if (dpt != null)
                    {
                        // 解析 VisualsFormatType 枚举（vft 整数值直接对应）
                        var vftType = _asm.GetType("VisualsFormatType");
                        object vftEnum = null;
                        if (vftType != null && vftType.IsEnum)
                        {
                            try { vftEnum = Enum.ToObject(vftType, vft); } catch { }
                        }

                        if (vftEnum != null)
                        {
                            // 最佳方法：切换模式并更新天梯显示
                            var r = CallMethod(dpt, "SwitchFormatTypeAndRankedPlayMode", vftEnum);
                            if (r != null) return "OK:switch:" + vftEnum;

                            r = CallMethod(dpt, "UpdateFormat_Tournament", vftEnum);
                            if (r != null) return "OK:update:" + vftEnum;
                        }

                        // 回退：TransitionToFormatType(FormatType, bool, float)
                        var r2 = CallMethod(dpt, "TransitionToFormatType", formatArg, true, 0f);
                        if (r2 != null) return "OK:transition";

                        // 回退：无参 SwitchFormatButtonPress
                        r2 = CallMethod(dpt, "SwitchFormatButtonPress");
                        if (r2 != null) return "OK:switchBtn";

                        return "OK:options_only|vftType=" + (vftType?.Name ?? "null")
                            + "|vftEnum=" + (vftEnum?.ToString() ?? "null");
                    }

                    return "OK:options_only|no_dpt";
                }
                catch (Exception ex)
                {
                    return "ERROR:" + ex.GetBaseException().Message;
                }
            });
        }

        public string SelectDeck(long deckId)
        {
            if (deckId <= 0) return "ERROR:invalid_deck_id";

            return OnMain(() =>
            {
                if (!Init()) return "ERROR:not_initialized";
                try
                {
                    var dpt = GetDeckPickerTray();
                    if (dpt == null) return "ERROR:no_dpt";

                    // 尝试直接调用 SelectDeck 系列方法
                    foreach (var name in new[] { "SelectDeckById", "SelectDeck", "SetSelectedDeck" })
                    {
                        var result = CallMethod(dpt, name, deckId);
                        if (result != null) return "OK:api:" + name;
                    }

                    // 遍历 m_customPages 找到目标卡组并触发选择
                    foreach (var page in AsEnumerable(GetProp(dpt, "m_customPages")))
                    {
                        foreach (var deckBox in AsEnumerable(GetProp(page, "m_customDecks")))
                        {
                            var id = ToLong(GetProp(deckBox, "m_deckID")
                                ?? GetProp(deckBox, "m_deckId")
                                ?? GetProp(deckBox, "DeckID")
                                ?? GetProp(deckBox, "DeckId")
                                ?? GetProp(deckBox, "m_preconDeckID"));
                            if (id != deckId) continue;

                            // 找到了目标卡组，尝试触发选择事件
                            var selectResult = CallMethod(deckBox, "SelectDeck")
                                ?? CallMethod(deckBox, "OnSelected")
                                ?? CallMethod(deckBox, "Select");
                            if (selectResult != null) return "OK:deckbox_select";

                            // 尝试通过 DeckPickerTrayDisplay 选择这个 deckBox
                            var r2 = CallMethod(dpt, "SelectCustomDeck", deckBox);
                            if (r2 != null) return "OK:select_custom";

                            return "OK:found_no_method";
                        }
                    }

                    return "ERROR:deck_not_found:" + deckId;
                }
                catch (Exception ex)
                {
                    return "ERROR:" + ex.GetBaseException().Message;
                }
            });
        }

        public string ClickPlay()
        {
            return OnMain(() =>
            {
                if (!Init()) return "ERROR:not_initialized";
                try
                {
                    var dpt = GetDeckPickerTray();
                    if (dpt == null) return "ERROR:no_dpt";

                    // 尝试通过 play button 触发（必须 Press + Release 配对）
                    var playBtn = GetProp(dpt, "m_playButton");
                    if (playBtn != null)
                    {
                        var rPress = CallMethod(playBtn, "TriggerPress");
                        var rRelease = CallMethod(playBtn, "TriggerRelease");
                        if (rPress != null || rRelease != null) return "OK:playButton";
                    }

                    // 尝试直接调用 DeckPickerTrayDisplay 的 play 方法
                    foreach (var name in new[] { "OnPlayButtonPressed", "PlayButtonPress", "Play" })
                    {
                        var r = CallMethod(dpt, name);
                        if (r != null) return "OK:api:" + name;
                    }

                    // 尝试通过 GameMgr.FindGame
                    if (_gameMgrType != null)
                    {
                        var gameMgr = CallStatic(_gameMgrType, "Get");
                        if (gameMgr != null)
                        {
                            var r = CallMethod(gameMgr, "FindGame",
                                (int)1 /*GameType.GT_RANKED*/, (int)2 /*FormatType*/, (long)0, (long)0);
                            if (r != null) return "OK:findgame";
                        }
                    }

                    return "ERROR:no_play_method";
                }
                catch (Exception ex)
                {
                    return "ERROR:" + ex.GetBaseException().Message;
                }
            });
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

        private object GetDeckPickerTray()
        {
            var type = _asm.GetType("DeckPickerTrayDisplay");
            return type != null ? CallStatic(type, "Get") : null;
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
            var result = _coroutine.RunAndWait(MouseDismissClickSequence(w, h));
            return result ?? "ERROR:click_failed";
        }

        private string ClickAt(int x, int y, float delayAfter)
        {
            return _coroutine.RunAndWait(MouseClick(x, y, delayAfter));
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
        /// 对局结束页通常“任意位置可点击继续”，
        /// 这里固定以屏幕中心为主，辅以轻微偏移点做容错。
        /// </summary>
        private IEnumerator<float> MouseDismissClickSequence(int w, int h)
        {
            InputHook.Simulating = true;
            var cx = w / 2;
            var cy = h / 2;
            var points = new[]
            {
                (x: cx, y: cy),
                (x: cx + Math.Max(10, w / 40), y: cy),
                (x: cx - Math.Max(10, w / 40), y: cy),
            };

            foreach (var p in points)
            {
                foreach (var wait in SmoothMove(p.x, p.y)) yield return wait;
                MouseSimulator.LeftDown();
                yield return 0.05f;
                MouseSimulator.LeftUp();
                yield return 0.18f;
            }

            _coroutine.SetResult("OK:center_multi");
        }

        private static string WrapClickResult(string clickResult, string detail)
        {
            if (clickResult != null && clickResult.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
                return "OK:" + detail;
            return clickResult ?? "ERROR:click_failed";
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
            var mi = obj.GetType().GetMethod(method,
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
            var mi = type.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (mi == null) return null;
            try { return mi.Invoke(null, null); } catch { return null; }
        }
    }
}
