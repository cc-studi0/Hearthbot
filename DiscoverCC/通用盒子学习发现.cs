using SmartBot.Database;
using SmartBot.Discover;
using SmartBot.Plugins.API;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace UniversalDiscover
{
    public class BoxLearningDiscover : DiscoverPickHandler
    {
        private readonly StringBuilder _log = new StringBuilder();
        private const string DiscoverVersion = "2026-03-31.19";
        private const int DiscoverStateFreshSeconds = 15;
        private const int DiscoverTeacherMaxAttempts = 1;
        private const int DiscoverTeacherRetryDelayMs = 180;
        private const int DiscoverOcrTimeoutMs = 5000;
        private const int DiscoverLateStateGraceMs = 8000;
        private const int DiscoverLateStatePollDelayMs = 180;
        private const int CursorParkDelayMs = 900;
        private const double CursorParkTriggerYRatio = 0.42d;
        private const double CursorParkXRatio = 0.50d;
        private const double CursorParkYRatio = 0.05d;
        private static readonly Random Random = new Random();
        private static int _cursorParkRequestId = 0;
        private const int DiscoverOcrRequestDedupWindowMs = 1500;
        private const int DiscoverLogicVerifyRetryCount = 5;
        private const int DiscoverLogicVerifyRetryDelayMs = 120;
        private static readonly object DiscoverOcrRequestSync = new object();
        private static readonly string[] PreferredLocalDiscoverLogicNames = new[] { "Custom", "SimulationCustom" };
        private static string _lastDiscoverOcrRequestKey = string.Empty;
        private static DateTime _lastDiscoverOcrRequestUtc = DateTime.MinValue;
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        private static IEnumerable<Assembly> GetPreferredAssemblies()
        {
            Assembly currentAssembly = typeof(BoxLearningDiscover).Assembly;
            if (currentAssembly != null)
                yield return currentAssembly;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly == null || assembly == currentAssembly)
                    continue;

                yield return assembly;
            }
        }

        private static string CaptureTeacherSampleCompat(
            Card.Cards originCard,
            List<Card.Cards> choices,
            Card.Cards pickedCard,
            Board board,
            string profileName,
            string discoverProfileName)
        {
            try
            {
                foreach (Assembly assembly in GetPreferredAssemblies())
                {
                    string status = TryInvokeCaptureTeacherSample(
                        assembly,
                        originCard,
                        choices,
                        pickedCard,
                        board,
                        profileName,
                        discoverProfileName);
                    if (!string.IsNullOrWhiteSpace(status))
                        return status;
                }
            }
            catch
            {
                // ignore
            }

            return "跳过=memory_missing";
        }

        private static string TryInvokeCaptureTeacherSample(
            Assembly assembly,
            Card.Cards originCard,
            List<Card.Cards> choices,
            Card.Cards pickedCard,
            Board board,
            string profileName,
            string discoverProfileName)
        {
            if (assembly == null)
                return string.Empty;

            try
            {
                Type memoryType = assembly.GetType("SmartBotProfiles.DecisionDiscoverMemory", false);
                if (memoryType == null)
                    return string.Empty;

                MethodInfo method = memoryType.GetMethod(
                    "CaptureTeacherSample",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(Card.Cards), typeof(List<Card.Cards>), typeof(Card.Cards), typeof(Board), typeof(string), typeof(string) },
                    null);
                if (method == null)
                    return string.Empty;

                object result = method.Invoke(null, new object[] { originCard, choices, pickedCard, board, profileName, discoverProfileName });
                if (method.ReturnType == typeof(string))
                    return result == null ? "跳过=空返回" : result.ToString().Trim();

                return "样本已请求(旧版)";
            }
            catch
            {
                return "跳过=调用失败";
            }
        }

        private static bool TryPickFromMemoryCompat(
            Card.Cards originCard,
            List<Card.Cards> choices,
            Board board,
            string profileName,
            string discoverProfileName,
            out Card.Cards pickedCard)
        {
            pickedCard = default(Card.Cards);

            try
            {
                foreach (Assembly assembly in GetPreferredAssemblies())
                {
                    Type memoryType = assembly.GetType("SmartBotProfiles.DecisionDiscoverMemory", false);
                    if (memoryType == null)
                        continue;

                    MethodInfo method = memoryType.GetMethod(
                        "TryPickFromMemory",
                        new[] { typeof(Card.Cards), typeof(List<Card.Cards>), typeof(Board), typeof(string), typeof(string), typeof(Card.Cards).MakeByRefType() });
                    if (method == null)
                        continue;

                    object[] args = { originCard, choices, board, profileName, discoverProfileName, pickedCard };
                    object result = method.Invoke(null, args);
                    if (args[5] is Card.Cards)
                        pickedCard = (Card.Cards)args[5];

                    return result is bool && (bool)result;
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private static bool TryPickFromMemoryDetailedCompat(
            Card.Cards originCard,
            List<Card.Cards> choices,
            Board board,
            string profileName,
            string discoverProfileName,
            bool captureHit,
            out Card.Cards pickedCard,
            out string detail,
            out double score,
            out double margin)
        {
            pickedCard = default(Card.Cards);
            detail = string.Empty;
            score = 0d;
            margin = 0d;

            try
            {
                foreach (Assembly assembly in GetPreferredAssemblies())
                {
                    Type memoryType = assembly.GetType("SmartBotProfiles.DecisionDiscoverMemory", false);
                    if (memoryType == null)
                        continue;

                    MethodInfo detailedMethod = memoryType.GetMethod(
                        "TryPickFromMemoryDetailed",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new[]
                        {
                            typeof(Card.Cards),
                            typeof(List<Card.Cards>),
                            typeof(Board),
                            typeof(string),
                            typeof(string),
                            typeof(bool),
                            typeof(Card.Cards).MakeByRefType(),
                            typeof(string).MakeByRefType(),
                            typeof(double).MakeByRefType(),
                            typeof(double).MakeByRefType()
                        },
                        null);
                    if (detailedMethod != null)
                    {
                        object[] args = { originCard, choices, board, profileName, discoverProfileName, captureHit, pickedCard, detail, score, margin };
                        object result = detailedMethod.Invoke(null, args);
                        if (args[6] is Card.Cards)
                            pickedCard = (Card.Cards)args[6];
                        detail = args[7] == null ? string.Empty : args[7].ToString().Trim();
                        if (args[8] is double)
                            score = (double)args[8];
                        if (args[9] is double)
                            margin = (double)args[9];
                        return result is bool && (bool)result;
                    }

                    if (captureHit)
                    {
                        MethodInfo legacyMethod = memoryType.GetMethod(
                            "TryPickFromMemory",
                            new[] { typeof(Card.Cards), typeof(List<Card.Cards>), typeof(Board), typeof(string), typeof(string), typeof(Card.Cards).MakeByRefType() });
                        if (legacyMethod == null)
                            continue;

                        object[] args = { originCard, choices, board, profileName, discoverProfileName, pickedCard };
                        object result = legacyMethod.Invoke(null, args);
                        if (args[5] is Card.Cards)
                            pickedCard = (Card.Cards)args[5];
                        detail = "legacy_capture";
                        return result is bool && (bool)result;
                    }
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private static string DescribeRuntimePathsCompat()
        {
            try
            {
                foreach (Assembly assembly in GetPreferredAssemblies())
                {
                    string described = TryInvokeDescribeRuntimePaths(assembly);
                    if (!string.IsNullOrWhiteSpace(described))
                        return described;
                }
            }
            catch
            {
                // ignore
            }

            return string.Empty;
        }

        private static string TryInvokeDescribeRuntimePaths(Assembly assembly)
        {
            if (assembly == null)
                return string.Empty;

            try
            {
                Type memoryType = assembly.GetType("SmartBotProfiles.DecisionDiscoverMemory", false);
                if (memoryType == null)
                    return string.Empty;

                MethodInfo method = memoryType.GetMethod(
                    "DescribeRuntimePaths",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    Type.EmptyTypes,
                    null);
                if (method == null)
                    return string.Empty;

                object result = method.Invoke(null, null);
                return result == null ? string.Empty : result.ToString().Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static object LoadTeacherHintCompat()
        {
            try
            {
                foreach (Assembly assembly in GetPreferredAssemblies())
                {
                    Type extractorType = assembly.GetType("SmartBotProfiles.DecisionStateExtractor", false);
                    if (extractorType == null)
                        continue;

                    MethodInfo method = extractorType.GetMethod("LoadTeacherHint", Type.EmptyTypes);
                    if (method == null)
                        continue;

                    return method.Invoke(null, null);
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        private static object GetMemberValue(object raw, string memberName)
        {
            if (raw == null || string.IsNullOrWhiteSpace(memberName))
                return null;

            try
            {
                Type type = raw.GetType();
                PropertyInfo property = type.GetProperty(memberName);
                if (property != null && property.GetIndexParameters().Length == 0)
                    return property.GetValue(raw, null);

                FieldInfo field = type.GetField(memberName);
                if (field != null)
                    return field.GetValue(raw);
            }
            catch
            {
                // ignore
            }

            return null;
        }

        private static string GetStringMember(object raw, string memberName)
        {
            object value = GetMemberValue(raw, memberName);
            return value == null ? string.Empty : value.ToString().Trim();
        }

        private static bool GetBoolMember(object raw, string memberName)
        {
            object value = GetMemberValue(raw, memberName);
            if (value is bool)
                return (bool)value;

            bool parsed;
            return bool.TryParse(value == null ? string.Empty : value.ToString(), out parsed) && parsed;
        }

        private static bool InvokeTeacherBoolMethod(object teacher, string methodName, object[] args)
        {
            if (teacher == null || string.IsNullOrWhiteSpace(methodName))
                return false;

            try
            {
                MethodInfo method = teacher.GetType().GetMethod(methodName);
                if (method == null)
                    return false;

                object result = method.Invoke(teacher, args);
                return result is bool && (bool)result;
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private static bool TeacherMatchesDiscoverProfile(object teacher, string discoverProfileName)
        {
            if (teacher == null)
                return false;

            if (InvokeTeacherBoolMethod(teacher, "MatchesDiscoverProfile", new object[] { discoverProfileName }))
                return true;

            string actual = NormalizeStrategyName(GetStringMember(teacher, "SBDiscoverProfile"));
            string expected = NormalizeStrategyName(discoverProfileName);
            if (string.IsNullOrWhiteSpace(expected))
                return true;

            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeStrategyName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            string value = raw.Trim().Replace('/', '\\');
            return Path.GetFileName(value).Trim();
        }

        public Card.Cards HandlePickDecision(Card.Cards originCard, List<Card.Cards> choices, Board board)
        {
            _log.Clear();
            string currentProfileName = SafeCurrentProfileName();
            string currentDiscoverProfileName = SafeCurrentDiscoverProfileName();
            TryEnsureLocalDiscoverLogic(currentDiscoverProfileName);
            currentDiscoverProfileName = SafeCurrentDiscoverProfileName();
            AddLog("┌───── 通用学习发现 v" + DiscoverVersion + " ─────");
            AddLog("│ 场景: " + SafeCardName(originCard) + " | 备选=" + FormatChoices(choices));

            if (choices == null || choices.Count == 0)
            {
                AddLog("│ 盒子: 未命中 | 来源=无可选项");
                AddLog("│ 决策: --- 无可选项");
                AddLog("└───────────────────────────────");
                FlushLog();
                return default(Card.Cards);
            }

            Card.Cards pick;
            string memoryDetail;
            double memoryScore;
            double memoryMargin;
            if (DecisionRuntimeModeCompat.PreferDiscoverMemoryFirst()
                && TryPickFromMemoryDetailedCompat(
                    originCard,
                    choices,
                    board,
                    currentProfileName,
                    currentDiscoverProfileName,
                    false,
                    out pick,
                    out memoryDetail,
                    out memoryScore,
                    out memoryMargin))
            {
                if (PassesDiscoverMemoryFirstPolicy(memoryScore, memoryMargin)
                    && TryPickFromMemoryDetailedCompat(
                        originCard,
                        choices,
                        board,
                        currentProfileName,
                        currentDiscoverProfileName,
                        true,
                        out pick,
                        out memoryDetail,
                        out memoryScore,
                        out memoryMargin))
                {
                    AddLog("│ 盒子: 未命中 | 来源=本地记忆(老师前)");
                    AddLog("│ 决策: ★★☆ 本地记忆(老师前)");
                    AddLog("│ 结果: 选择=" + SafeCardName(pick));
                    AddLog("└───────────────────────────────");
                    ScheduleCursorParkAfterPick();
                    FlushLog();
                    return pick;
                }
            }

            bool allowTeacherFallback = !DecisionRuntimeModeCompat.IsPureLearningModeEnabled()
                || DecisionRuntimeModeCompat.AllowLiveTeacherFallback();
            string teacherSource;
            if (allowTeacherFallback && TryPickByTeacher(originCard, choices, board, out pick, out teacherSource))
            {
                AddLog("│ 盒子: 命中 | 来源=盒子老师(OCR)");
                AddLog("│ 决策: ★★★ 盒子老师(OCR)");
                AddLog("│ 结果: 选择=" + SafeCardName(pick) + " | 细分=" + teacherSource);
                AddLog("└───────────────────────────────");
                ScheduleCursorParkAfterPick();
                FlushLog();
                return pick;
            }

            if (TryPickFromMemoryDetailedCompat(
                originCard,
                choices,
                board,
                currentProfileName,
                currentDiscoverProfileName,
                true,
                out pick,
                out memoryDetail,
                out memoryScore,
                out memoryMargin))
            {
                AddLog("│ 盒子: 未命中 | 来源=本地记忆(老师后)");
                AddLog("│ 决策: ★☆☆ 本地记忆(老师后)");
                AddLog("│ 结果: 选择=" + SafeCardName(pick));
                AddLog("└───────────────────────────────");
                ScheduleCursorParkAfterPick();
                FlushLog();
                return pick;
            }

            if (TryPickSafeFallback(originCard, choices, board, out pick))
            {
                AddLog("│ 盒子: 未命中 | 来源=安全兜底");
                AddLog("│ 决策: --- 安全兜底");
                AddLog("│ 结果: 选择=" + SafeCardName(pick));
                AddLog("└───────────────────────────────");
                ScheduleCursorParkAfterPick();
                FlushLog();
                return pick;
            }

            pick = choices[Random.Next(0, choices.Count)];
            AddLog("│ 盒子: 未命中 | 来源=随机兜底");
            AddLog("│ 决策: --- 随机兜底");
            AddLog("│ 结果: 选择=" + SafeCardName(pick));
            AddLog("└───────────────────────────────");
            ScheduleCursorParkAfterPick();
            FlushLog();
            return pick;
        }

        private bool TryPickByTeacher(Card.Cards originCard, List<Card.Cards> choices, Board board, out Card.Cards pick, out string source)
        {
            pick = default(Card.Cards);
            source = "teacher_strict";
            if (!IsRankedWinOcrPluginEnabled())
                return false;

            string currentProfile = Sanitize(SafeCurrentProfileName());
            string currentMulligan = Sanitize(SafeCurrentMulliganName());
            string currentDiscoverProfile = Sanitize(SafeCurrentDiscoverProfileName());
            string currentMode = CurrentMode();
            string expectedStateKey = BuildDiscoverOcrRequestKey(
                originCard,
                choices,
                currentProfile,
                currentMulligan,
                currentDiscoverProfile,
                currentMode);

            BoxAuditGuardState boxAuditGuard = LoadBoxAuditGuardState();
            bool discoverGuardActive = boxAuditGuard != null
                && boxAuditGuard.IsFresh()
                && boxAuditGuard.SuppressesStage("discover");
            if (discoverGuardActive)
            {
                // discover guard active (silent)
            }

            for (int attempt = 0; attempt < DiscoverTeacherMaxAttempts; attempt++)
            {
                if (TryPickByTeacherStateOrHint(originCard, choices, expectedStateKey, board, boxAuditGuard, "preexisting", out pick, out source))
                    return true;

                bool ocrTimedOut = RunDiscoverOcr(
                    originCard,
                    choices,
                    currentProfile,
                    currentMulligan,
                    currentDiscoverProfile,
                    currentMode,
                    expectedStateKey,
                    attempt == 0);

                if (TryPickByTeacherStateOrHint(originCard, choices, expectedStateKey, board, boxAuditGuard, "after_launch", out pick, out source))
                    return true;

                if (ocrTimedOut
                    && TryWaitForLateTeacherResult(originCard, choices, expectedStateKey, board, boxAuditGuard, out pick, out source))
                    return true;

                if (attempt + 1 < DiscoverTeacherMaxAttempts)
                    Thread.Sleep(DiscoverTeacherRetryDelayMs);
            }

            if (TryPickByTeacherStateOrHint(originCard, choices, expectedStateKey, board, boxAuditGuard, "final_local_state", out pick, out source))
                return true;

            return false;
        }

        private bool TryWaitForLateTeacherResult(
            Card.Cards originCard,
            List<Card.Cards> choices,
            string expectedStateKey,
            Board board,
            BoxAuditGuardState boxAuditGuard,
            out Card.Cards pick,
            out string source)
        {
            pick = default(Card.Cards);
            source = string.Empty;

            DateTime deadlineUtc = DateTime.UtcNow.AddMilliseconds(DiscoverLateStateGraceMs);
            int polls = 0;
            while (true)
            {
                if (TryPickByTeacherStateOrHint(originCard, choices, expectedStateKey, board, boxAuditGuard, "after_timeout_grace", out pick, out source))
                {
                    return true;
                }

                if (DateTime.UtcNow >= deadlineUtc)
                    break;

                Thread.Sleep(DiscoverLateStatePollDelayMs);
                polls++;
            }
            return false;
        }

        private bool TryPickByTeacherStateOrHint(
            Card.Cards originCard,
            List<Card.Cards> choices,
            string expectedStateKey,
            Board board,
            BoxAuditGuardState boxAuditGuard,
            string phase,
            out Card.Cards pick,
            out string source)
        {
            pick = default(Card.Cards);
            source = string.Empty;

            string relaxReason;
            if (TryPickByDiscoverState(originCard, choices, expectedStateKey, out pick, out relaxReason))
            {
                if (FinalizeTeacherPick(originCard, choices, board, pick, "discover_state:" + phase + ":" + relaxReason, boxAuditGuard, out pick))
                {
                    source = "discover_ocr_strict:" + phase + ":" + relaxReason;
                    return true;
                }
            }

            object teacher = LoadTeacherHintCompat();
            if (teacher != null
                && InvokeTeacherBoolMethod(teacher, "IsFresh", new object[] { DiscoverStateFreshSeconds })
                && string.Equals(GetStringMember(teacher, "Status"), "ok", StringComparison.OrdinalIgnoreCase)
                && string.Equals(GetStringMember(teacher, "Stage"), "discover", StringComparison.OrdinalIgnoreCase)
                && TeacherMatchesDiscoverProfile(teacher, SafeCurrentDiscoverProfileName())
                && GetBoolMember(teacher, "HasDiscoverPick"))
            {
                object teacherPick = GetMemberValue(teacher, "DiscoverPickId");
                if (teacherPick is Card.Cards && choices.Contains((Card.Cards)teacherPick))
                {
                    if (FinalizeTeacherPick(originCard, choices, board, (Card.Cards)teacherPick, "teacher_pick_id:" + phase, boxAuditGuard, out pick))
                    {
                        source = "discover_hint_strict:" + phase + ":teacher_pick_id";
                        return true;
                    }
                }
            }

            return false;
        }

        private void TryEnsureLocalDiscoverLogic(string discoverProfileName)
        {
            string normalizedProfile = NormalizeStrategyName(discoverProfileName);
            if (string.IsNullOrWhiteSpace(normalizedProfile))
                normalizedProfile = "通用盒子学习发现.cs";

            if (!string.Equals(normalizedProfile, "通用盒子学习发现.cs", StringComparison.OrdinalIgnoreCase))
                return;

            string currentLogic = ReadCurrentDiscoverLogicNameUiSafe();
            string configuredLogic = ReadConfiguredDiscoverLogicNameUiSafe();
            if (IsLocalDiscoverLogicName(configuredLogic) || IsLocalDiscoverLogicName(currentLogic))
                return;

            string appliedLogic = string.Empty;
            string verifiedLogic = string.Empty;
            string error;
            if (TryChangeDiscoverProfileToPreferredLocalLogic(normalizedProfile, out appliedLogic, out verifiedLogic, out error))
            {
                // forced local discover logic (silent)
            }
            else if (!string.IsNullOrWhiteSpace(error))
            {
                // force local discover logic failed (silent)
            }
        }

        private static bool TryChangeDiscoverProfileToPreferredLocalLogic(string discoverProfileName, out string appliedLogic, out string verifiedLogic, out string error)
        {
            appliedLogic = string.Empty;
            verifiedLogic = string.Empty;
            error = string.Empty;
            List<string> attempts = new List<string>();

            for (int i = 0; i < PreferredLocalDiscoverLogicNames.Length; i++)
            {
                string logicName = PreferredLocalDiscoverLogicNames[i];
                if (string.IsNullOrWhiteSpace(logicName))
                    continue;

                string attemptError;
                if (TryChangeDiscoverProfileByLogicName(discoverProfileName, logicName, out attemptError))
                {
                    string observedConfiguredLogic = ReadConfiguredDiscoverLogicNameUiSafeWithRetry();
                    string observedApiLogic = ReadCurrentDiscoverLogicNameUiSafeWithRetry();
                    verifiedLogic = !string.IsNullOrWhiteSpace(observedConfiguredLogic) ? observedConfiguredLogic : observedApiLogic;
                    attempts.Add(logicName
                        + "->config=" + (string.IsNullOrWhiteSpace(observedConfiguredLogic) ? "(empty)" : observedConfiguredLogic)
                        + "/api=" + (string.IsNullOrWhiteSpace(observedApiLogic) ? "(empty)" : observedApiLogic));
                    if (IsLocalDiscoverLogicName(observedConfiguredLogic) || IsLocalDiscoverLogicName(observedApiLogic))
                    {
                        appliedLogic = logicName;
                        return true;
                    }

                    if (string.IsNullOrWhiteSpace(appliedLogic))
                        appliedLogic = logicName;

                    error = "verify_mismatch(config="
                        + (string.IsNullOrWhiteSpace(observedConfiguredLogic) ? "(empty)" : observedConfiguredLogic)
                        + ", api="
                        + (string.IsNullOrWhiteSpace(observedApiLogic) ? "(empty)" : observedApiLogic)
                        + ")";
                    continue;
                }

                attempts.Add(logicName + "->invoke_failed(" + (string.IsNullOrWhiteSpace(attemptError) ? "unknown" : attemptError) + ")");
                error = attemptError;
            }

            if (attempts.Count > 0)
            {
                string attemptSummary = string.Join(", ", attempts.ToArray());
                if (string.IsNullOrWhiteSpace(error))
                    error = "attempts=" + attemptSummary;
                else
                    error += " | attempts=" + attemptSummary;
            }

            if (string.IsNullOrWhiteSpace(error))
                error = "change_method_missing";
            return false;
        }

        private static bool TryChangeDiscoverProfileByLogicName(string discoverProfileName, string logicName, out string error)
        {
            error = string.Empty;

            try
            {
                string normalizedProfile = NormalizeStrategyName(discoverProfileName);
                if (string.IsNullOrWhiteSpace(normalizedProfile))
                {
                    error = "empty_profile";
                    return false;
                }

                string normalizedLogicName = Sanitize(logicName);
                if (string.IsNullOrWhiteSpace(normalizedLogicName))
                {
                    error = "empty_logic";
                    return false;
                }

                Type logicType = ResolveDiscoverLogicEnumType();
                object currentLogic = null;
                if (logicType == null)
                {
                    try { currentLogic = Bot.CurrentDiscoverLogic(); } catch { }
                    if (currentLogic != null)
                        logicType = currentLogic.GetType();
                }

                if (logicType == null || !logicType.IsEnum)
                {
                    error = "logic_type_missing";
                    return false;
                }

                object targetLogic = Enum.Parse(logicType, normalizedLogicName, true);
                MethodInfo[] methods = typeof(Bot).GetMethods(BindingFlags.Public | BindingFlags.Static);
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo method = methods[i];
                    if (!string.Equals(method.Name, "ChangeDiscoverProfile", StringComparison.Ordinal))
                        continue;

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters == null || parameters.Length != 2)
                        continue;

                    if (parameters[1].ParameterType != typeof(string))
                        continue;

                    if (parameters[0].ParameterType != logicType)
                        continue;

                    if (!TryInvokeUiAction(
                        delegate { method.Invoke(null, new object[] { targetLogic, normalizedProfile }); },
                        out error))
                    {
                        return false;
                    }

                    string settingsError;
                    if (!TrySetCurrentDiscoverSelectionByLogicName(normalizedLogicName, normalizedProfile, out settingsError))
                    {
                        error = "set_current_selection_failed(" + settingsError + ")";
                        return false;
                    }

                    return true;
                }

                error = "change_method_missing";
                return false;
            }
            catch (Exception ex)
            {
                Exception root = ex.InnerException ?? ex;
                error = root == null ? "unknown_error" : root.Message;
                return false;
            }
        }

        private static bool IsLocalDiscoverLogicName(string logicName)
        {
            string normalized = Sanitize(logicName);
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            for (int i = 0; i < PreferredLocalDiscoverLogicNames.Length; i++)
            {
                string candidate = PreferredLocalDiscoverLogicNames[i];
                if (!string.IsNullOrWhiteSpace(candidate)
                    && string.Equals(normalized, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryInvokeUiAction(Action action, out string error)
        {
            error = string.Empty;
            if (action == null)
                return true;

            try
            {
                Type applicationType = Type.GetType("System.Windows.Application, PresentationFramework", false);
                if (applicationType == null)
                {
                    action();
                    return true;
                }

                PropertyInfo currentProperty = applicationType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
                object application = currentProperty == null ? null : currentProperty.GetValue(null, null);
                if (application == null)
                {
                    action();
                    return true;
                }

                PropertyInfo dispatcherProperty = application.GetType().GetProperty("Dispatcher", BindingFlags.Public | BindingFlags.Instance);
                object dispatcher = dispatcherProperty == null ? null : dispatcherProperty.GetValue(application, null);
                if (dispatcher == null)
                {
                    action();
                    return true;
                }

                MethodInfo checkAccessMethod = dispatcher.GetType().GetMethod("CheckAccess", Type.EmptyTypes);
                object accessRaw = checkAccessMethod == null ? null : checkAccessMethod.Invoke(dispatcher, null);
                if (accessRaw is bool && (bool)accessRaw)
                {
                    action();
                    return true;
                }

                MethodInfo invokeMethod = null;
                MethodInfo[] methods = dispatcher.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo candidate = methods[i];
                    if (!string.Equals(candidate.Name, "Invoke", StringComparison.Ordinal))
                        continue;

                    ParameterInfo[] parameters = candidate.GetParameters();
                    if (parameters == null || parameters.Length != 1)
                        continue;

                    if (!typeof(Delegate).IsAssignableFrom(parameters[0].ParameterType))
                        continue;

                    invokeMethod = candidate;
                    break;
                }

                if (invokeMethod == null)
                {
                    action();
                    return true;
                }

                invokeMethod.Invoke(dispatcher, new object[] { action });
                return true;
            }
            catch (Exception ex)
            {
                Exception root = ex.InnerException ?? ex;
                error = root == null ? "unknown_error" : root.Message;
                return false;
            }
        }

        private static string ReadCurrentDiscoverLogicNameUiSafe()
        {
            string value = SafeCurrentDiscoverLogicName();
            string error;
            if (!TryInvokeUiAction(
                delegate { value = SafeCurrentDiscoverLogicName(); },
                out error))
            {
                return value;
            }

            return string.IsNullOrWhiteSpace(value) ? SafeCurrentDiscoverLogicName() : value;
        }

        private static string ReadCurrentDiscoverLogicNameUiSafeWithRetry()
        {
            string latest = string.Empty;
            int attempts = Math.Max(1, DiscoverLogicVerifyRetryCount);
            int delayMs = Math.Max(1, DiscoverLogicVerifyRetryDelayMs);
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                latest = ReadCurrentDiscoverLogicNameUiSafe();
                if (IsLocalDiscoverLogicName(latest))
                    return latest;

                if (attempt + 1 < attempts)
                    Thread.Sleep(delayMs);
            }

            return latest;
        }

        private static string ReadConfiguredDiscoverLogicNameUiSafe()
        {
            string logicName = string.Empty;
            string profileName = string.Empty;
            string error;
            if (!TryInvokeUiAction(
                delegate { TryReadCurrentDiscoverSelectionFromSettingsManager(out logicName, out profileName); },
                out error))
            {
                TryReadCurrentDiscoverSelectionFromSettingsManager(out logicName, out profileName);
            }

            return logicName ?? string.Empty;
        }

        private static string ReadConfiguredDiscoverLogicNameUiSafeWithRetry()
        {
            string latest = string.Empty;
            int attempts = Math.Max(1, DiscoverLogicVerifyRetryCount);
            int delayMs = Math.Max(1, DiscoverLogicVerifyRetryDelayMs);
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                latest = ReadConfiguredDiscoverLogicNameUiSafe();
                if (IsLocalDiscoverLogicName(latest))
                    return latest;

                if (attempt + 1 < attempts)
                    Thread.Sleep(delayMs);
            }

            return latest;
        }

        private static Type ResolveDiscoverLogicEnumType()
        {
            try
            {
                return typeof(Bot).GetNestedType("DiscoverLogic", BindingFlags.Public | BindingFlags.NonPublic);
            }
            catch
            {
                return null;
            }
        }

        private static Type ResolveSettingsManagerType()
        {
            try
            {
                Type direct = Type.GetType("SmartBotUI.Settings.SettingsManager", false);
                if (direct != null)
                    return direct;
            }
            catch
            {
                // ignore
            }

            try
            {
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length; i++)
                {
                    Assembly assembly = assemblies[i];
                    if (assembly == null)
                        continue;

                    try
                    {
                        Type candidate = assembly.GetType("SmartBotUI.Settings.SettingsManager", false);
                        if (candidate != null)
                            return candidate;
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        private static bool TryReadCurrentDiscoverSelectionFromSettingsManager(out string logicName, out string profileName)
        {
            logicName = string.Empty;
            profileName = string.Empty;

            try
            {
                Type settingsType = ResolveSettingsManagerType();
                if (settingsType == null)
                    return false;

                FieldInfo logicField = settingsType.GetField("DiscoProfile", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                FieldInfo profileField = settingsType.GetField("CurrentDiscoCC", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                object logicValue = logicField == null ? null : logicField.GetValue(null);
                object profileValue = profileField == null ? null : profileField.GetValue(null);
                logicName = logicValue == null ? string.Empty : logicValue.ToString().Trim();
                profileName = profileValue == null ? string.Empty : NormalizeStrategyName(profileValue.ToString());
                return !string.IsNullOrWhiteSpace(logicName) || !string.IsNullOrWhiteSpace(profileName);
            }
            catch
            {
                return false;
            }
        }

        private static bool TrySetCurrentDiscoverSelectionByLogicName(string logicName, string discoverProfileName, out string error)
        {
            error = string.Empty;

            try
            {
                string normalizedLogicName = Sanitize(logicName);
                string normalizedProfile = NormalizeStrategyName(discoverProfileName);
                if (string.IsNullOrWhiteSpace(normalizedLogicName))
                {
                    error = "empty_logic";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(normalizedProfile))
                {
                    error = "empty_profile";
                    return false;
                }

                Type settingsType = ResolveSettingsManagerType();
                if (settingsType == null)
                {
                    error = "settings_manager_type_missing";
                    return false;
                }

                FieldInfo logicField = settingsType.GetField("DiscoProfile", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (logicField == null || !logicField.FieldType.IsEnum)
                {
                    error = "settings_logic_field_missing";
                    return false;
                }

                FieldInfo profileField = settingsType.GetField("CurrentDiscoCC", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (profileField == null || profileField.FieldType != typeof(string))
                {
                    error = "settings_profile_field_missing";
                    return false;
                }

                MethodInfo writeSettingsMethod = settingsType.GetMethod("WriteSettingsFile", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null);
                object targetLogic = Enum.Parse(logicField.FieldType, normalizedLogicName, true);
                return TryInvokeUiAction(
                    delegate
                    {
                        logicField.SetValue(null, targetLogic);
                        profileField.SetValue(null, normalizedProfile);
                        if (writeSettingsMethod != null)
                            writeSettingsMethod.Invoke(null, null);
                    },
                    out error);
            }
            catch (Exception ex)
            {
                Exception root = ex.InnerException ?? ex;
                error = root == null ? "unknown_error" : root.Message;
                return false;
            }
        }

        private bool FinalizeTeacherPick(
            Card.Cards originCard,
            List<Card.Cards> choices,
            Board board,
            Card.Cards candidatePick,
            string reason,
            BoxAuditGuardState boxAuditGuard,
            out Card.Cards pick)
        {
            pick = default(Card.Cards);
            if (choices == null || choices.Count == 0 || !choices.Contains(candidatePick))
                return false;

            if (boxAuditGuard != null && boxAuditGuard.IsFresh() && IsCardPenalized(boxAuditGuard, candidatePick))
            {
                // discover penalty relaxed (silent)
            }

            pick = candidatePick;
            CaptureTeacherSampleCompat(
                originCard,
                choices,
                pick,
                board,
                SafeCurrentProfileName(),
                SafeCurrentDiscoverProfileName());
            return true;
        }

        private bool TryPickByDiscoverState(Card.Cards originCard, List<Card.Cards> choices, string expectedStateKey, out Card.Cards pick, out string reason)
        {
            pick = default(Card.Cards);
            reason = string.Empty;

            foreach (string stateFile in ResolveDiscoverStatePaths())
            {
                if (TryReadDiscoverPickFromStateFile(stateFile, originCard, choices, expectedStateKey, out pick, out reason))
                    return true;
            }

            return false;
        }

        private IEnumerable<string> ResolveDiscoverStatePaths()
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> paths = new List<string>();

            if (!IsRankedWinOcrPluginEnabled())
                return paths;

            Action<string> addPath = path =>
            {
                if (string.IsNullOrWhiteSpace(path))
                    return;

                string normalized = path.Trim();
                if (seen.Add(normalized))
                    paths.Add(normalized);
            };

            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                addPath(Path.Combine(baseDir, "runtime", "decision_teacher_discover_state.txt"));
                addPath(Path.Combine(baseDir, "runtime", "netease_box_ocr_discover_state.txt"));
                addPath(Path.Combine(baseDir, "runtime", "decision_teacher_state.txt"));
                addPath(Path.Combine(baseDir, "runtime", "netease_box_ocr_state.txt"));
            }
            catch
            {
                // ignore
            }

            return paths;
        }

        private static bool IsRankedWinOcrPluginEnabled()
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins", "RankedWinOcrPlugin.json");
                if (!File.Exists(configPath))
                    return false;

                string raw = File.ReadAllText(configPath);
                if (string.IsNullOrWhiteSpace(raw))
                    return false;

                string compact = raw.Replace(" ", string.Empty)
                    .Replace("\t", string.Empty)
                    .Replace("\r", string.Empty)
                    .Replace("\n", string.Empty);
                return compact.IndexOf("\"Enabled\":true", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private bool TryReadDiscoverPickFromStateFile(
            string stateFile,
            Card.Cards originCard,
            List<Card.Cards> choices,
            string expectedStateKey,
            out Card.Cards pick,
            out string reason)
        {
            pick = default(Card.Cards);
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(stateFile) || !File.Exists(stateFile) || choices == null || choices.Count == 0)
                return false;

            string status = string.Empty;
            string stage = string.Empty;
            DateTime tsUtc = DateTime.MinValue;
            string discoverPickRaw = string.Empty;
            string firstCardRaw = string.Empty;
            string firstMatchRaw = string.Empty;
            string stateOriginCard = string.Empty;
            string stateCandidateKey = string.Empty;
            int stateCandidateCount = -1;
            int preferredChoiceIndex = 0;
            List<string> textHints = new List<string>();

            try
            {
                foreach (string rawLine in ReadAllLinesShared(stateFile))
                {
                    string line = rawLine ?? string.Empty;
                    if (line.StartsWith("status=", StringComparison.OrdinalIgnoreCase))
                        status = line.Substring("status=".Length).Trim();
                    else if (line.StartsWith("stage=", StringComparison.OrdinalIgnoreCase))
                        stage = line.Substring("stage=".Length).Trim();
                    else if (line.StartsWith("ts_utc=", StringComparison.OrdinalIgnoreCase))
                    {
                        DateTime parsedUtc;
                        if (DateTime.TryParse(
                            line.Substring("ts_utc=".Length).Trim(),
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                            out parsedUtc))
                        {
                            tsUtc = parsedUtc;
                        }
                    }
                    else if (line.StartsWith("discover_pick_id=", StringComparison.OrdinalIgnoreCase))
                        discoverPickRaw = line.Substring("discover_pick_id=".Length).Trim();
                    else if (line.StartsWith("origin_card=", StringComparison.OrdinalIgnoreCase))
                        stateOriginCard = line.Substring("origin_card=".Length).Trim();
                    else if (line.StartsWith("candidate_key=", StringComparison.OrdinalIgnoreCase))
                        stateCandidateKey = line.Substring("candidate_key=".Length).Trim();
                    else if (line.StartsWith("candidate_count=", StringComparison.OrdinalIgnoreCase))
                    {
                        int parsedCount;
                        if (int.TryParse(line.Substring("candidate_count=".Length).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedCount))
                            stateCandidateCount = parsedCount;
                    }
                    else if (preferredChoiceIndex <= 0 && line.StartsWith("preferred_choice_index=", StringComparison.OrdinalIgnoreCase))
                        TryParseDiscoverChoiceIndex(line.Substring("preferred_choice_index=".Length).Trim(), out preferredChoiceIndex);
                    else if (preferredChoiceIndex <= 0 && line.StartsWith("action_choice_index=", StringComparison.OrdinalIgnoreCase))
                        TryParseDiscoverChoiceIndex(line.Substring("action_choice_index=".Length).Trim(), out preferredChoiceIndex);
                    else if (string.IsNullOrWhiteSpace(firstCardRaw) && line.StartsWith("card_id=", StringComparison.OrdinalIgnoreCase))
                        firstCardRaw = line.Substring("card_id=".Length).Trim();
                    else if (string.IsNullOrWhiteSpace(firstMatchRaw) && line.StartsWith("match_id=", StringComparison.OrdinalIgnoreCase))
                        firstMatchRaw = line.Substring("match_id=".Length).Trim();
                    else if (line.StartsWith("discover_pick_name=", StringComparison.OrdinalIgnoreCase))
                        AddDiscoverTextHint(textHints, line.Substring("discover_pick_name=".Length).Trim());
                    else if (line.StartsWith("match_name=", StringComparison.OrdinalIgnoreCase))
                        AddDiscoverTextHint(textHints, line.Substring("match_name=".Length).Trim());
                    else if (line.StartsWith("line=", StringComparison.OrdinalIgnoreCase))
                        AddDiscoverTextHint(textHints, line.Substring("line=".Length).Trim());
                }
            }
            catch
            {
                return false;
            }

            if (!string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
                return false;

            bool hasEmbeddedDiscoverEvidence = HasEmbeddedDiscoverEvidence(textHints, preferredChoiceIndex, discoverPickRaw, firstCardRaw, firstMatchRaw);
            if (!string.Equals(stage, "discover", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(stateFile).IndexOf("discover", StringComparison.OrdinalIgnoreCase) < 0)
            {
                if (!hasEmbeddedDiscoverEvidence)
                    return false;
            }

            if (!IsDiscoverStateFresh(stateFile, tsUtc))
                return false;

            string expectedOriginCard = originCard.ToString();
            if (!string.IsNullOrWhiteSpace(stateOriginCard)
                && !string.Equals(stateOriginCard, expectedOriginCard, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // candidate_count may exceed choices.Count when daemon reports
            // multiple display-name rows per card; only reject when fewer.
            if (stateCandidateCount >= 0 && stateCandidateCount < choices.Count)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(expectedStateKey)
                && string.IsNullOrWhiteSpace(stateCandidateKey))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(expectedStateKey)
                && !string.Equals(stateCandidateKey, expectedStateKey, StringComparison.Ordinal))
            {
                return false;
            }

            if (preferredChoiceIndex > 0 && preferredChoiceIndex <= choices.Count)
            {
                pick = choices[preferredChoiceIndex - 1];
                reason = "state_choice_index";
                return true;
            }

            if (TryParseDiscoverChoiceId(discoverPickRaw, choices, out pick))
            {
                reason = "state_structured_pick";
                return true;
            }

            if (TryParseDiscoverChoiceId(firstCardRaw, choices, out pick))
            {
                reason = "state_first_card_id";
                return true;
            }

            if (TryParseDiscoverChoiceId(firstMatchRaw, choices, out pick))
            {
                reason = "state_first_match_id";
                return true;
            }

            if (TryRecoverDiscoverPickFromTextHints(textHints, choices, out pick))
            {
                reason = "state_text_hint_pick";
                return true;
            }

            return false;
        }

        private static List<string> ReadAllLinesShared(string path)
        {
            List<string> lines = new List<string>();
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                    lines.Add(line);
            }

            return lines;
        }

        private static bool IsDiscoverStateFresh(string stateFile, DateTime tsUtc)
        {
            DateTime thresholdUtc = DateTime.UtcNow.AddSeconds(-DiscoverStateFreshSeconds);
            if (tsUtc != DateTime.MinValue)
                return tsUtc >= thresholdUtc;

            try
            {
                return File.GetLastWriteTimeUtc(stateFile) >= thresholdUtc;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryParseDiscoverChoiceIndex(string raw, out int choiceIndex)
        {
            choiceIndex = 0;
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            if (!int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out choiceIndex))
            {
                choiceIndex = 0;
                return false;
            }

            if (choiceIndex <= 0)
            {
                choiceIndex = 0;
                return false;
            }

            return true;
        }

        private static bool HasEmbeddedDiscoverEvidence(
            List<string> textHints,
            int preferredChoiceIndex,
            string discoverPickRaw,
            string firstCardRaw,
            string firstMatchRaw)
        {
            if (preferredChoiceIndex > 0)
                return true;

            if (!string.IsNullOrWhiteSpace(discoverPickRaw))
                return true;

            if (!string.IsNullOrWhiteSpace(firstCardRaw) && !string.IsNullOrWhiteSpace(firstMatchRaw))
                return true;

            if (textHints == null || textHints.Count == 0)
                return false;

            for (int i = 0; i < textHints.Count; i++)
            {
                string hint = textHints[i];
                string normalized = NormalizeDiscoverText(hint);
                if (string.IsNullOrWhiteSpace(normalized))
                    continue;

                if (normalized.Contains("选择卡牌")
                    || normalized.Contains("选择我方")
                    || normalized.Contains("选项")
                    || normalized.Contains("三选一")
                    || normalized.Contains("发现")
                    || normalized.Contains("抉择"))
                {
                    return true;
                }

                int index;
                if (TryExtractChoiceIndex(hint, out index) && index >= 1 && index <= 3)
                    return true;
            }

            return false;
        }

        private static void AddDiscoverTextHint(List<string> textHints, string raw)
        {
            if (textHints == null || string.IsNullOrWhiteSpace(raw) || textHints.Count >= 12)
                return;

            string cleaned = raw.Trim();
            if (cleaned.Length == 0)
                return;

            textHints.Add(cleaned);
        }

        private static bool TryParseDiscoverChoiceId(string raw, List<Card.Cards> choices, out Card.Cards pick)
        {
            pick = default(Card.Cards);
            if (string.IsNullOrWhiteSpace(raw) || choices == null || choices.Count == 0)
                return false;

            Card.Cards parsed;
            if (!TryParseCardId(raw, out parsed))
                return false;

            if (!choices.Contains(parsed))
                return false;

            pick = parsed;
            return true;
        }

        private bool TryRecoverDiscoverPickFromTextHints(List<string> textHints, List<Card.Cards> choices, out Card.Cards pick)
        {
            pick = default(Card.Cards);
            if (textHints == null || textHints.Count == 0 || choices == null || choices.Count == 0)
                return false;

            Card.Cards bestChoice = default(Card.Cards);
            int bestScore = 0;

            foreach (Card.Cards choice in choices)
            {
                List<string> aliases = BuildDiscoverChoiceAliases(choice);
                if (aliases.Count == 0)
                    continue;

                foreach (string hint in textHints)
                {
                    string normalizedHint = NormalizeDiscoverText(hint);
                    if (string.IsNullOrWhiteSpace(normalizedHint))
                        continue;

                    foreach (string alias in aliases)
                    {
                        int score = ScoreDiscoverAliasMatch(normalizedHint, alias);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestChoice = choice;
                        }
                    }
                }
            }

            if (bestScore > 0)
            {
                pick = bestChoice;
                return true;
            }

            return TryRecoverDiscoverPickFromChoiceIndex(textHints, choices, out pick);
        }

        private static int ScoreDiscoverAliasMatch(string normalizedHint, string normalizedAlias)
        {
            if (string.IsNullOrWhiteSpace(normalizedHint) || string.IsNullOrWhiteSpace(normalizedAlias))
                return 0;

            if (string.Equals(normalizedHint, normalizedAlias, StringComparison.Ordinal))
                return 1000 + normalizedAlias.Length;

            if (normalizedAlias.Length >= 2
                && normalizedHint.Length >= 2
                && (normalizedHint.Contains(normalizedAlias) || normalizedAlias.Contains(normalizedHint)))
            {
                return 100 + Math.Min(normalizedHint.Length, normalizedAlias.Length);
            }

            return 0;
        }

        private bool TryRecoverDiscoverPickFromChoiceIndex(List<string> textHints, List<Card.Cards> choices, out Card.Cards pick)
        {
            pick = default(Card.Cards);
            if (textHints == null || textHints.Count == 0 || choices == null || choices.Count == 0)
                return false;

            foreach (string hint in textHints)
            {
                if (string.IsNullOrWhiteSpace(hint))
                    continue;

                int index = 0;
                if (TryExtractChoiceIndex(hint, out index) && index >= 1 && index <= choices.Count)
                {
                    pick = choices[index - 1];
                    return true;
                }
            }

            return false;
        }

        private static bool TryExtractChoiceIndex(string hint, out int index)
        {
            index = 0;
            if (string.IsNullOrWhiteSpace(hint))
                return false;

            try
            {
                string raw = hint.Trim();
                string[] patterns =
                {
                    "(?:选择)?(?:我方|敌方)?\\s*([123])\\s*号位",
                    "(?:第|选项)\\s*([123])\\s*(?:个|张|项)?"
                };

                foreach (string pattern in patterns)
                {
                    System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(raw, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (!match.Success)
                        continue;

                    if (int.TryParse(match.Groups[1].Value, out index))
                        return true;
                }
            }
            catch
            {
                // ignore
            }

            index = 0;
            return false;
        }

        private static List<string> BuildDiscoverChoiceAliases(Card.Cards cardId)
        {
            List<string> aliases = new List<string>();

            Action<string> addAlias = raw =>
            {
                string normalized = NormalizeDiscoverText(raw);
                if (!string.IsNullOrWhiteSpace(normalized) && !aliases.Contains(normalized))
                    aliases.Add(normalized);
            };

            addAlias(cardId.ToString());
            foreach (string displayName in BuildDiscoverChoiceDisplayNames(cardId))
                addAlias(displayName);

            return aliases;
        }

        private static List<string> BuildDiscoverChoiceDisplayNames(Card.Cards cardId)
        {
            List<string> names = new List<string>();

            Action<string> addName = raw =>
            {
                string cleaned = string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim();
                if (!string.IsNullOrWhiteSpace(cleaned) && !names.Contains(cleaned))
                    names.Add(cleaned);
            };

            try
            {
                CardTemplate template = CardTemplate.LoadFromId(cardId);
                if (template != null)
                {
                    addName(template.NameCN);
                    addName(template.Name);
                }
            }
            catch
            {
                // ignore
            }

            return names;
        }

        private static string NormalizeDiscoverText(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            StringBuilder sb = new StringBuilder(raw.Length);
            foreach (char ch in raw)
            {
                if (char.IsLetterOrDigit(ch))
                    sb.Append(char.ToLowerInvariant(ch));
            }

            return sb.ToString();
        }

        private sealed class BoxAuditGuardState
        {
            public DateTime TimestampUtc = DateTime.MinValue;
            public int ExpiresAfterSeconds = 900;
            public int ConsistencyScore = 100;
            public string Summary = string.Empty;
            public readonly HashSet<string> SuppressStages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<Card.Cards> PenalizedCardIds = new HashSet<Card.Cards>();

            public bool IsFresh()
            {
                if (TimestampUtc == DateTime.MinValue)
                    return false;

                return TimestampUtc >= DateTime.UtcNow.AddSeconds(-Math.Max(30, ExpiresAfterSeconds));
            }

            public bool SuppressesStage(string stage)
            {
                if (string.IsNullOrWhiteSpace(stage))
                    return false;

                return SuppressStages.Contains("all") || SuppressStages.Contains(stage.Trim());
            }
        }

        private static string ResolveBoxAuditGuardPath()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] candidates = new[]
                {
                    Path.Combine(baseDir, "runtime", "box_audit_guard.txt"),
                    Path.Combine(baseDir, "Temp", "runtime", "box_audit_guard.txt"),
                    Path.Combine(baseDir, "..", "Temp", "runtime", "box_audit_guard.txt")
                };

                foreach (string candidate in candidates)
                {
                    try
                    {
                        string normalized = Path.GetFullPath(candidate);
                        if (File.Exists(normalized))
                            return normalized;
                    }
                    catch
                    {
                        // ignore
                    }
                }

                return Path.GetFullPath(candidates[0]);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static BoxAuditGuardState LoadBoxAuditGuardState()
        {
            string path = ResolveBoxAuditGuardPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return new BoxAuditGuardState();

            BoxAuditGuardState state = new BoxAuditGuardState();
            try
            {
                string[] lines = File.ReadAllLines(path);
                foreach (string rawLine in lines)
                {
                    if (string.IsNullOrWhiteSpace(rawLine))
                        continue;

                    int idx = rawLine.IndexOf('=');
                    if (idx <= 0)
                        continue;

                    string key = rawLine.Substring(0, idx).Trim();
                    string value = rawLine.Substring(idx + 1).Trim();
                    if (string.IsNullOrWhiteSpace(key))
                        continue;

                    if (string.Equals(key, "generated_at_utc", StringComparison.OrdinalIgnoreCase))
                    {
                        DateTime parsed;
                        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed))
                            state.TimestampUtc = parsed;
                    }
                    else if (string.Equals(key, "expires_after_seconds", StringComparison.OrdinalIgnoreCase))
                    {
                        int parsed;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) && parsed > 0)
                            state.ExpiresAfterSeconds = parsed;
                    }
                    else if (string.Equals(key, "consistency_score", StringComparison.OrdinalIgnoreCase))
                    {
                        int parsed;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                            state.ConsistencyScore = parsed;
                    }
                    else if (string.Equals(key, "summary", StringComparison.OrdinalIgnoreCase))
                    {
                        state.Summary = value;
                    }
                    else if (string.Equals(key, "suppress_stage", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(value))
                            state.SuppressStages.Add(value.Trim());
                    }
                    else if (string.Equals(key, "penalize_card_id", StringComparison.OrdinalIgnoreCase))
                    {
                        Card.Cards parsed;
                        if (TryParseCardId(value, out parsed))
                            state.PenalizedCardIds.Add(parsed);
                    }
                }
            }
            catch
            {
                return new BoxAuditGuardState();
            }

            return state;
        }

        private static bool IsCardPenalized(BoxAuditGuardState state, Card.Cards cardId)
        {
            return state != null
                && state.IsFresh()
                && cardId != default(Card.Cards)
                && state.PenalizedCardIds.Contains(cardId);
        }

        private static bool TryParseCardId(string raw, out Card.Cards value)
        {
            value = default(Card.Cards);
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            try
            {
                value = (Card.Cards)Enum.Parse(typeof(Card.Cards), raw.Trim(), true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryPickSafeFallback(Card.Cards originCard, List<Card.Cards> choices, Board board, out Card.Cards pick)
        {
            pick = default(Card.Cards);
            if (choices == null || choices.Count == 0)
                return false;

            Card.Cards bestCard = default(Card.Cards);
            int bestScore = int.MinValue;
            foreach (Card.Cards cardId in choices)
            {
                int score = ScoreFallbackChoice(cardId, board);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestCard = cardId;
                }
            }

            if (bestCard == default(Card.Cards))
                return false;

            pick = bestCard;
            return true;
        }

        private int ScoreFallbackChoice(Card.Cards cardId, Board board)
        {
            CardTemplate template = null;
            try { template = CardTemplate.LoadFromId(cardId); } catch { template = null; }
            if (template == null)
                return 0;

            int manaAvailable = board != null ? board.ManaAvailable : 0;
            int enemyBoardCount = board != null && board.MinionEnemy != null ? board.MinionEnemy.Count(m => m != null) : 0;
            int friendBoardCount = board != null && board.MinionFriend != null ? board.MinionFriend.Count(m => m != null) : 0;
            int enemyPressure = GetEnemyAttack(board);
            int friendAttack = GetFriendlyAttack(board);
            int friendlyHpArmor = board != null && board.HeroFriend != null ? board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor : 0;
            int enemyHpArmor = board != null && board.HeroEnemy != null ? board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor : 0;
            bool enemyHasTaunt = board != null && board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            bool underPressure = friendlyHpArmor <= Math.Max(12, enemyPressure + 2);

            int score = 0;
            score += Math.Max(0, 8 - Math.Abs(template.Cost - manaAvailable)) * 4;

            if (template.Type == Card.CType.MINION)
            {
                score += Math.Max(0, template.Atk) * 3;
                score += Math.Max(0, template.Health) * 2;
                if (template.Taunt)
                    score += underPressure ? 90 : 24;
                if (template.Charge)
                {
                    score += 18;
                    if (!enemyHasTaunt && enemyHpArmor > 0 && enemyHpArmor <= friendAttack + Math.Max(0, template.Atk))
                        score += 120;
                }
                if (enemyBoardCount > friendBoardCount)
                    score += Math.Max(0, template.Atk) * 2;
            }
            else if (template.Type == Card.CType.SPELL)
            {
                score += enemyBoardCount > 0 ? 34 : 12;
                if (underPressure)
                    score += 16;
                if (template.Cost <= manaAvailable + 1)
                    score += 10;
            }
            else if (template.Type == Card.CType.WEAPON)
            {
                score += enemyBoardCount > 0 ? 28 : 12;
                if (!enemyHasTaunt)
                    score += 12;
            }

            if (manaAvailable <= 3 && template.Cost <= 3)
                score += 16;
            if (manaAvailable >= 7 && template.Cost >= 6)
                score += 10;

            return score;
        }

        private static int GetFriendlyAttack(Board board)
        {
            int total = 0;
            try
            {
                if (board != null && board.MinionFriend != null)
                    total += board.MinionFriend.Where(m => m != null && m.CanAttack).Sum(m => Math.Max(0, m.CurrentAtk));
                if (board != null && board.WeaponFriend != null && board.HeroFriend != null && board.HeroFriend.CanAttack)
                    total += Math.Max(0, board.WeaponFriend.CurrentAtk);
            }
            catch
            {
                // ignore
            }

            return total;
        }

        private static int GetEnemyAttack(Board board)
        {
            int total = 0;
            try
            {
                if (board != null && board.MinionEnemy != null)
                    total += board.MinionEnemy.Where(m => m != null).Sum(m => Math.Max(0, m.CurrentAtk));
                if (board != null && board.WeaponEnemy != null)
                    total += Math.Max(0, board.WeaponEnemy.CurrentAtk);
            }
            catch
            {
                // ignore
            }

            return total;
        }

        private static bool TryResolveOcrRunner(string repoRoot, out string fileName, out string argumentPrefix)
        {
            // exe 优先，保证本地和拉取用户行为一致
            string exe = ResolveBundledOcrExecutable(repoRoot);
            if (!string.IsNullOrWhiteSpace(exe))
            {
                fileName = exe;
                argumentPrefix = string.Empty;
                return true;
            }

            string scriptPath = Path.Combine(repoRoot, "tools", "decision_teacher_ocr.py");
            if (File.Exists(scriptPath))
            {
                fileName = ResolveBundledPython(repoRoot);
                argumentPrefix = Quote(scriptPath);
                return true;
            }

            fileName = string.Empty;
            argumentPrefix = string.Empty;
            return false;
        }

        private static string ResolveBundledOcrExecutable(string repoRoot)
        {
            string directExe = Path.Combine(repoRoot, "tools", "decision_teacher_ocr.exe");
            if (File.Exists(directExe))
                return directExe;

            string nestedExe = Path.Combine(repoRoot, "tools", "decision_teacher_ocr", "decision_teacher_ocr.exe");
            if (File.Exists(nestedExe))
                return nestedExe;

            return string.Empty;
        }

        private static string ResolveBundledPython(string repoRoot)
        {
            string bundledPython = Path.Combine(repoRoot, "tools", "python", "python.exe");
            if (File.Exists(bundledPython))
                return bundledPython;

            return "python";
        }

        private static string ResolveDaemonClientCommand(string repoRoot)
        {
            if (string.IsNullOrWhiteSpace(repoRoot))
                return string.Empty;

            string script = Path.Combine(repoRoot, "tools", "decision_teacher_ocr_client.ps1");
            if (File.Exists(script))
                return script;

            string executable = Path.Combine(repoRoot, "tools", "decision_teacher_ocr_client.exe");
            return File.Exists(executable) ? executable : string.Empty;
        }

        private static bool IsPowerShellScriptPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path)
                && string.Equals(Path.GetExtension(path), ".ps1", StringComparison.OrdinalIgnoreCase);
        }

        private bool RunDiscoverOcr(
            Card.Cards originCard,
            List<Card.Cards> choices,
            string currentProfile,
            string currentMulligan,
            string currentDiscoverProfile,
            string currentMode,
            string requestKey,
            bool allowRecentDedup)
        {
            // OCR 已移除，由 read_box_recommendation_v2.py 守护进程直接写入 discover 状态文件。
            // 先把当前 discover 请求元数据预写到 discover 状态文件，供守护进程回填结果时保留。
            try
            {
                if (!allowRecentDedup || !ShouldSkipRecentDiscoverOcrLaunch(requestKey))
                {
                    foreach (string stateFile in ResolveDiscoverStatePaths())
                    {
                        string fileName = Path.GetFileName(stateFile);
                        if (string.IsNullOrWhiteSpace(fileName)
                            || fileName.IndexOf("discover", StringComparison.OrdinalIgnoreCase) < 0)
                            continue;

                        PrimeDiscoverStateFile(
                            stateFile,
                            originCard,
                            requestKey,
                            choices == null ? 0 : choices.Count,
                            currentProfile,
                            currentMulligan,
                            currentDiscoverProfile,
                            currentMode);
                    }

                    MarkDiscoverOcrLaunch(requestKey);
                }
            }
            catch
            {
                // ignore
            }

            // 短暂等待以给守护进程时间写入 discover_pick。
            Thread.Sleep(500);
            return false;
        }

        private static void PrimeDiscoverStateFile(
            string stateFile,
            Card.Cards originCard,
            string requestKey,
            int candidateCount,
            string currentProfile,
            string currentMulligan,
            string currentDiscoverProfile,
            string currentMode)
        {
            if (string.IsNullOrWhiteSpace(stateFile))
                return;

            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("status=refreshing");
                sb.AppendLine("ts_utc=" + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                sb.AppendLine("stage=discover");
                sb.AppendLine("status_reason=ocr_inflight");
                sb.AppendLine("origin_card=" + originCard.ToString());
                sb.AppendLine("candidate_key=" + Sanitize(requestKey));
                sb.AppendLine("candidate_count=" + Math.Max(0, candidateCount).ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("sb_profile=" + Sanitize(currentProfile));
                sb.AppendLine("sb_mulligan=" + Sanitize(currentMulligan));
                sb.AppendLine("sb_discover_profile=" + Sanitize(currentDiscoverProfile));
                sb.AppendLine("sb_mode=" + Sanitize(currentMode));
                File.WriteAllText(stateFile, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // ignore
            }
        }

        private static List<string> BuildDiscoverCandidateLines(List<Card.Cards> choices)
        {
            List<string> rows = new List<string>();
            if (choices != null)
            {
                for (int i = 0; i < choices.Count; i++)
                {
                    Card.Cards id = choices[i];
                    List<string> displayNames = BuildDiscoverChoiceDisplayNames(id);
                    if (displayNames.Count == 0)
                        displayNames.Add(id.ToString());

                    foreach (string displayName in displayNames)
                        rows.Add("card\t" + id + "\t" + Sanitize(displayName) + "\t" + (i + 1));
                }
            }

            return rows;
        }

        private static string CurrentMode()
        {
            Bot.Mode mode = Bot.CurrentMode();
            if (mode == Bot.Mode.Practice || mode == Bot.Mode.Casual)
            {
                try
                {
                    string deckName = SafeCurrentDeckName();
                    if (!string.IsNullOrWhiteSpace(deckName) && deckName.Length >= 2)
                    {
                        string key = deckName.Substring(1, 1);
                        if (string.Equals(key, "S", StringComparison.OrdinalIgnoreCase))
                            return "Standard";
                        if (string.Equals(key, "W", StringComparison.OrdinalIgnoreCase))
                            return "Wild";
                    }
                }
                catch
                {
                    // ignore
                }
                return "Wild";
            }

            if (mode == Bot.Mode.Arena || mode == Bot.Mode.ArenaAuto)
                return "Arena";
            if (mode == Bot.Mode.Standard)
                return "Standard";
            return "Wild";
        }

        private static string FindRepoRoot(string baseDir)
        {
            try
            {
                DirectoryInfo dir = new DirectoryInfo(baseDir);
                for (int i = 0; i < 4 && dir != null; i++, dir = dir.Parent)
                {
                    // 内存读取器 (替代已移除的 OCR)
                    string memReaderCandidate = Path.Combine(dir.FullName, "tools", "read_box_recommendation_v2.py");
                    if (File.Exists(memReaderCandidate))
                        return dir.FullName;

                    string primaryCandidate = Path.Combine(dir.FullName, "tools", "decision_teacher_ocr.py");
                    if (File.Exists(primaryCandidate))
                        return dir.FullName;
                }
            }
            catch
            {
                // ignore
            }

            return baseDir;
        }

        private static string BuildDiscoverOcrRequestKey(
            Card.Cards originCard,
            List<Card.Cards> choices,
            string profileName,
            string mulliganName,
            string discoverProfileName,
            string mode)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(originCard)
                .Append('|').Append(Sanitize(profileName))
                .Append('|').Append(Sanitize(mulliganName))
                .Append('|').Append(Sanitize(discoverProfileName))
                .Append('|').Append(Sanitize(mode));

            if (choices != null)
            {
                for (int i = 0; i < choices.Count; i++)
                    sb.Append('[').Append(choices[i]).Append(']');
            }

            return sb.ToString();
        }

        private static bool ShouldSkipRecentDiscoverOcrLaunch(string requestKey)
        {
            if (string.IsNullOrWhiteSpace(requestKey))
                return false;

            lock (DiscoverOcrRequestSync)
            {
                DateTime nowUtc = DateTime.UtcNow;
                return string.Equals(_lastDiscoverOcrRequestKey, requestKey, StringComparison.Ordinal)
                    && _lastDiscoverOcrRequestUtc != DateTime.MinValue
                    && nowUtc < _lastDiscoverOcrRequestUtc.AddMilliseconds(DiscoverOcrRequestDedupWindowMs);
            }
        }

        private static void MarkDiscoverOcrLaunch(string requestKey)
        {
            if (string.IsNullOrWhiteSpace(requestKey))
                return;

            lock (DiscoverOcrRequestSync)
            {
                _lastDiscoverOcrRequestKey = requestKey;
                _lastDiscoverOcrRequestUtc = DateTime.UtcNow;
            }
        }

        private void AddLog(string line)
        {
            if (_log.Length > 0)
                _log.Append("\r\n");
            _log.Append(line);
        }

        private void FlushLog()
        {
            try
            {
                if (_log.Length > 0)
                    Bot.Log(_log.ToString());
            }
            catch
            {
                // ignore
            }
        }

        private static void ScheduleCursorParkAfterPick()
        {
            int requestId = Interlocked.Increment(ref _cursorParkRequestId);
            ThreadPool.QueueUserWorkItem(_ => TryParkCursorAfterDiscoverPick(requestId));
        }

        private static void TryParkCursorAfterDiscoverPick(int requestId)
        {
            try
            {
                Thread.Sleep(CursorParkDelayMs);
                if (requestId != Interlocked.CompareExchange(ref _cursorParkRequestId, 0, 0))
                    return;

                POINT cursor;
                if (!GetCursorPos(out cursor))
                    return;

                Process[] processes = Process.GetProcessesByName("Hearthstone");
                if (processes == null || processes.Length == 0)
                    return;

                foreach (Process process in processes)
                {
                    if (process == null)
                        continue;

                    IntPtr hwnd = IntPtr.Zero;
                    try { hwnd = process.MainWindowHandle; } catch { hwnd = IntPtr.Zero; }
                    if (hwnd == IntPtr.Zero)
                        continue;

                    RECT rect;
                    if (!GetWindowRect(hwnd, out rect))
                        continue;

                    int width = Math.Max(0, rect.Right - rect.Left);
                    int height = Math.Max(0, rect.Bottom - rect.Top);
                    if (width < 200 || height < 200)
                        continue;

                    if (!IsPointInsideRect(cursor, rect))
                        continue;

                    int triggerY = rect.Top + (int)Math.Round(height * CursorParkTriggerYRatio);
                    if (cursor.Y < triggerY)
                        return;

                    int parkX = rect.Left + (int)Math.Round(width * CursorParkXRatio);
                    int parkY = rect.Top + (int)Math.Round(height * CursorParkYRatio);
                    SetCursorPos(parkX, parkY);
                    return;
                }
            }
            catch
            {
                // ignore
            }
        }

        private static bool IsPointInsideRect(POINT point, RECT rect)
        {
            return point.X >= rect.Left
                && point.X <= rect.Right
                && point.Y >= rect.Top
                && point.Y <= rect.Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private static string FormatChoices(List<Card.Cards> cards)
        {
            if (cards == null || cards.Count == 0)
                return "(none)";

            List<string> names = new List<string>();
            foreach (Card.Cards cardId in cards)
                names.Add(SafeCardName(cardId));
            return string.Join(", ", names.ToArray());
        }

        private static string SafeCardName(Card.Cards id)
        {
            try
            {
                CardTemplate template = CardTemplate.LoadFromId(id);
                if (template != null)
                {
                    if (!string.IsNullOrWhiteSpace(template.NameCN))
                        return template.NameCN + "(" + id + ")";
                    if (!string.IsNullOrWhiteSpace(template.Name))
                        return template.Name + "(" + id + ")";
                }
            }
            catch
            {
                // ignore
            }

            return id.ToString();
        }

        private static string Sanitize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;
            return raw.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private static string Quote(string raw)
        {
            if (raw == null)
                return "\"\"";
            return "\"" + raw.Replace("\"", "\\\"") + "\"";
        }

        private static string SafeCurrentProfileName()
        {
            try { return Bot.CurrentProfile(); } catch { return string.Empty; }
        }

        private static string SafeCurrentMulliganName()
        {
            try { return Bot.CurrentMulligan(); } catch { return string.Empty; }
        }

        private static string SafeCurrentDiscoverProfileName()
        {
            try { return Bot.CurrentDiscoverProfile(); } catch { return string.Empty; }
        }

        private static string SafeCurrentDiscoverLogicName()
        {
            try
            {
                object logic = Bot.CurrentDiscoverLogic();
                return logic == null ? string.Empty : logic.ToString().Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string FormatVersionedDiscoverName(string raw)
        {
            string name = string.IsNullOrWhiteSpace(raw) ? "通用盒子学习发现.cs" : raw.Trim();
            return name + "(v" + DiscoverVersion + ")";
        }

        private static string SafeCurrentDeckName()
        {
            try
            {
                var deck = Bot.CurrentDeck();
                if (deck == null || string.IsNullOrWhiteSpace(deck.Name))
                    return string.Empty;
                return deck.Name.Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        private sealed class DecisionRuntimeModeConfigCompat
        {
            public bool pure_learning_mode { get; set; }
            public bool allow_live_teacher_fallback { get; set; }
            public bool prefer_discover_memory_first { get; set; }
            public double discover_memory_first_min_score { get; set; }
            public double discover_memory_first_min_margin { get; set; }
        }

        private static class DecisionRuntimeModeCompat
        {
            private static readonly object Sync = new object();
            private static DateTime _lastLoadUtc = DateTime.MinValue;
            private static DecisionRuntimeModeConfigCompat _cached = BuildDefault();

            public static bool IsPureLearningModeEnabled()
            {
                return GetConfig().pure_learning_mode;
            }

            public static bool AllowLiveTeacherFallback()
            {
                DecisionRuntimeModeConfigCompat config = GetConfig();
                if (!config.pure_learning_mode)
                    return true;

                return config.allow_live_teacher_fallback;
            }

            public static bool PreferDiscoverMemoryFirst()
            {
                DecisionRuntimeModeConfigCompat config = GetConfig();
                if (!config.pure_learning_mode)
                    return false;

                return config.prefer_discover_memory_first;
            }

            public static double GetDiscoverMemoryFirstMinScore()
            {
                DecisionRuntimeModeConfigCompat config = GetConfig();
                return Math.Max(0d, config.discover_memory_first_min_score);
            }

            public static double GetDiscoverMemoryFirstMinMargin()
            {
                DecisionRuntimeModeConfigCompat config = GetConfig();
                return Math.Max(0d, config.discover_memory_first_min_margin);
            }

            private static DecisionRuntimeModeConfigCompat GetConfig()
            {
                lock (Sync)
                {
                    if (_cached != null && _lastLoadUtc.AddSeconds(2) > DateTime.UtcNow)
                        return _cached;

                    _cached = LoadConfig();
                    _lastLoadUtc = DateTime.UtcNow;
                    return _cached;
                }
            }

            private static DecisionRuntimeModeConfigCompat LoadConfig()
            {
                DecisionRuntimeModeConfigCompat config = BuildDefault();
                try
                {
                    string path = ResolveConfigPath();

                    if (!File.Exists(path))
                        return config;

                    string raw = File.ReadAllText(path);
                    DecisionRuntimeModeConfigCompat parsed = JsonConvert.DeserializeObject<DecisionRuntimeModeConfigCompat>(raw);
                    if (parsed == null)
                        return config;

                    return parsed;
                }
                catch
                {
                    return config;
                }
            }

            private static DecisionRuntimeModeConfigCompat BuildDefault()
            {
                DecisionRuntimeModeConfigCompat config = new DecisionRuntimeModeConfigCompat();
                config.pure_learning_mode = false;
                config.allow_live_teacher_fallback = true;
                config.prefer_discover_memory_first = false;
                config.discover_memory_first_min_score = 0d;
                config.discover_memory_first_min_margin = 0d;
                return config;
            }

            private static string ResolveConfigPath()
            {
                List<string> candidates = GetConfigCandidatePaths();
                for (int i = 0; i < candidates.Count; i++)
                {
                    string candidate = candidates[i];
                    if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                        return candidate;
                }

                return candidates.Count > 0
                    ? candidates[0]
                    : CombinePath(AppDomain.CurrentDomain.BaseDirectory ?? string.Empty, "runtime", "learning", "decision_runtime_mode.json");
            }

            private static List<string> GetConfigCandidatePaths()
            {
                List<string> paths = new List<string>();
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
                AddConfigCandidate(paths, seen, CombinePath(baseDir, "runtime", "learning", "decision_runtime_mode.json"));
                AddConfigCandidate(paths, seen, CombinePath(baseDir, "Temp", "runtime", "learning", "decision_runtime_mode.json"));

                try
                {
                    string parentBaseDir = Path.GetFullPath(Path.Combine(baseDir, ".."));
                    AddConfigCandidate(paths, seen, CombinePath(parentBaseDir, "runtime", "learning", "decision_runtime_mode.json"));
                    AddConfigCandidate(paths, seen, CombinePath(parentBaseDir, "Temp", "runtime", "learning", "decision_runtime_mode.json"));
                }
                catch
                {
                    // ignore
                }

                return paths;
            }

            private static void AddConfigCandidate(List<string> paths, HashSet<string> seen, string path)
            {
                if (string.IsNullOrWhiteSpace(path))
                    return;

                string normalized = path;
                try
                {
                    normalized = Path.GetFullPath(path);
                }
                catch
                {
                    normalized = path;
                }

                if (!seen.Add(normalized))
                    return;

                paths.Add(normalized);
            }

            private static string CombinePath(string root, params string[] parts)
            {
                string result = root ?? string.Empty;
                if (parts == null || parts.Length == 0)
                    return result;

                for (int i = 0; i < parts.Length; i++)
                {
                    result = Path.Combine(result, parts[i] ?? string.Empty);
                }

                return result;
            }
        }

        private static bool PassesDiscoverMemoryFirstPolicy(double score, double margin)
        {
            return score >= DecisionRuntimeModeCompat.GetDiscoverMemoryFirstMinScore()
                && margin >= DecisionRuntimeModeCompat.GetDiscoverMemoryFirstMinMargin();
        }
    }
}
