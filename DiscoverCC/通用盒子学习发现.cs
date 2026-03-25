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
using System.Text;
using System.Threading;

namespace UniversalDiscover
{
    public class BoxLearningDiscover : DiscoverPickHandler
    {
        private readonly StringBuilder _log = new StringBuilder();
        private const string DiscoverVersion = "2026-03-23.1";
        private static readonly Random Random = new Random();
        private const int DiscoverOcrRequestDedupWindowMs = 1500;
        private static readonly object DiscoverOcrRequestSync = new object();
        private static string _lastDiscoverOcrRequestKey = string.Empty;
        private static DateTime _lastDiscoverOcrRequestUtc = DateTime.MinValue;
        private static readonly object DiscoverJsonCacheSync = new object();
        private static readonly Dictionary<string, CachedTextFile> DiscoverJsonCache = new Dictionary<string, CachedTextFile>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<Card.Cards> LocalDataEnemyOriginCards = new List<Card.Cards>
        {
            Card.Cards.REV_000,
            Card.Cards.REV_002,
            Card.Cards.REV_006,
            Card.Cards.NX2_044,
            Card.Cards.MIS_916
        };
        private static readonly List<Card.Cards> LocalDataFriendOriginCards = new List<Card.Cards>
        {
            Card.Cards.GDB_874,
            Card.Cards.TTN_429,
            Card.Cards.TOY_801,
            Card.Cards.TOY_801t,
            Card.Cards.BG31_BOB,
            Card.Cards.WON_103
        };

        private static void CaptureTeacherSampleCompat(
            Card.Cards originCard,
            List<Card.Cards> choices,
            Card.Cards pickedCard,
            Board board,
            string profileName,
            string discoverProfileName)
        {
            try
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type memoryType = assembly.GetType("SmartBotProfiles.DecisionDiscoverMemory", false);
                    if (memoryType == null)
                        continue;

                    MethodInfo method = memoryType.GetMethod(
                        "CaptureTeacherSample",
                        new[] { typeof(Card.Cards), typeof(List<Card.Cards>), typeof(Card.Cards), typeof(Board), typeof(string), typeof(string) });
                    if (method == null)
                        continue;

                    method.Invoke(null, new object[] { originCard, choices, pickedCard, board, profileName, discoverProfileName });
                    return;
                }
            }
            catch
            {
                // ignore
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
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
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

        private static object LoadTeacherHintCompat()
        {
            try
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
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
            AddLog("===== 通用学习发现 v" + DiscoverVersion + " =====");
            AddLog("Profile=" + SafeCurrentProfileName() + " | Discover=" + FormatVersionedDiscoverName(SafeCurrentDiscoverProfileName()) + " | Deck=" + SafeCurrentDeckName());
            AddLog("Origin=" + SafeCardName(originCard) + " | Choices=" + FormatChoices(choices));

            if (choices == null || choices.Count == 0)
            {
                FlushLog();
                return default(Card.Cards);
            }

            Card.Cards pick;
            if (TryPickByTeacher(originCard, choices, board, out pick))
            {
                AddLog("Source=teacher_strict | Pick=" + SafeCardName(pick));
                FlushLog();
                return pick;
            }

            if (TryPickFromMemoryCompat(
                originCard,
                choices,
                board,
                SafeCurrentProfileName(),
                SafeCurrentDiscoverProfileName(),
                out pick))
            {
                AddLog("Source=memory | Pick=" + SafeCardName(pick));
                FlushLog();
                return pick;
            }

            string fallbackReason;
            if (TryPickByLocalExtension(
                originCard,
                choices,
                board,
                SafeCurrentProfileName(),
                SafeCurrentDiscoverProfileName(),
                out pick,
                out fallbackReason))
            {
                AddLog("Source=local_extension_optional:" + fallbackReason + " | Pick=" + SafeCardName(pick));
                FlushLog();
                return pick;
            }

            if (TryPickByLocalJsonFallback(originCard, choices, board, out pick, out fallbackReason))
            {
                AddLog("Source=local_json:" + fallbackReason + " | Pick=" + SafeCardName(pick));
                FlushLog();
                return pick;
            }

            if (TryPickSafeFallback(originCard, choices, board, out pick))
            {
                AddLog("Source=safe_fallback | Pick=" + SafeCardName(pick));
                FlushLog();
                return pick;
            }

            pick = choices[Random.Next(0, choices.Count)];
            AddLog("Source=random_fallback | Pick=" + SafeCardName(pick));
            FlushLog();
            return pick;
        }

        private bool TryPickByTeacher(Card.Cards originCard, List<Card.Cards> choices, Board board, out Card.Cards pick)
        {
            pick = default(Card.Cards);

            BoxAuditGuardState boxAuditGuard = LoadBoxAuditGuardState();
            bool discoverGuardActive = boxAuditGuard != null
                && boxAuditGuard.IsFresh()
                && boxAuditGuard.SuppressesStage("discover");
            if (discoverGuardActive)
                AddLog("[BOXAUDIT][GUARD][RELAX] discover has OCR priority | score=" + boxAuditGuard.ConsistencyScore + " | summary=" + boxAuditGuard.Summary);

            for (int attempt = 0; attempt < 3; attempt++)
            {
                RunDiscoverOcr(originCard, choices, attempt == 0);

                string relaxReason;
                if (TryPickByDiscoverState(choices, out pick, out relaxReason))
                {
                    if (FinalizeTeacherPick(originCard, choices, board, pick, "discover_state:" + relaxReason, boxAuditGuard, out pick))
                        return true;
                }

                object teacher = LoadTeacherHintCompat();
                if (teacher != null
                    && InvokeTeacherBoolMethod(teacher, "IsFresh", new object[] { 15 })
                    && string.Equals(GetStringMember(teacher, "Status"), "ok", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(GetStringMember(teacher, "Stage"), "discover", StringComparison.OrdinalIgnoreCase)
                    && TeacherMatchesDiscoverProfile(teacher, SafeCurrentDiscoverProfileName())
                    && GetBoolMember(teacher, "HasDiscoverPick"))
                {
                    object teacherPick = GetMemberValue(teacher, "DiscoverPickId");
                    if (teacherPick is Card.Cards && choices.Contains((Card.Cards)teacherPick))
                    {
                        if (FinalizeTeacherPick(originCard, choices, board, (Card.Cards)teacherPick, "teacher_pick_id", boxAuditGuard, out pick))
                            return true;
                    }
                }

                if (attempt < 2)
                    Thread.Sleep(220);
            }

            return false;
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
                AddLog("[BOXAUDIT][PENALTY][RELAX] discover still follows OCR -> " + SafeCardName(candidatePick));

            pick = candidatePick;
            AddLog("[Teacher][STRICT] " + reason + " -> " + SafeCardName(pick));
            CaptureTeacherSampleCompat(
                originCard,
                choices,
                pick,
                board,
                SafeCurrentProfileName(),
                SafeCurrentDiscoverProfileName());
            return true;
        }

        private bool TryPickByDiscoverState(List<Card.Cards> choices, out Card.Cards pick, out string reason)
        {
            pick = default(Card.Cards);
            reason = string.Empty;

            foreach (string stateFile in ResolveDiscoverStatePaths())
            {
                if (TryReadDiscoverPickFromStateFile(stateFile, choices, out pick, out reason))
                    return true;
            }

            return false;
        }

        private IEnumerable<string> ResolveDiscoverStatePaths()
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> paths = new List<string>();

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

        private bool TryReadDiscoverPickFromStateFile(string stateFile, List<Card.Cards> choices, out Card.Cards pick, out string reason)
        {
            pick = default(Card.Cards);
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(stateFile) || !File.Exists(stateFile) || choices == null || choices.Count == 0)
                return false;

            string status = string.Empty;
            string stage = string.Empty;
            string discoverPickRaw = string.Empty;
            string firstCardRaw = string.Empty;
            string firstMatchRaw = string.Empty;
            List<string> textHints = new List<string>();

            try
            {
                foreach (string rawLine in File.ReadAllLines(stateFile))
                {
                    string line = rawLine ?? string.Empty;
                    if (line.StartsWith("status=", StringComparison.OrdinalIgnoreCase))
                        status = line.Substring("status=".Length).Trim();
                    else if (line.StartsWith("stage=", StringComparison.OrdinalIgnoreCase))
                        stage = line.Substring("stage=".Length).Trim();
                    else if (line.StartsWith("discover_pick_id=", StringComparison.OrdinalIgnoreCase))
                        discoverPickRaw = line.Substring("discover_pick_id=".Length).Trim();
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

            if (!string.Equals(stage, "discover", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(stateFile).IndexOf("discover", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
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

        private static bool TryPickByLocalExtension(
            Card.Cards originCard,
            List<Card.Cards> choices,
            Board board,
            string profileName,
            string discoverProfileName,
            out Card.Cards pick,
            out string reason)
        {
            pick = default(Card.Cards);
            reason = string.Empty;
            if (choices == null || choices.Count == 0)
                return false;

            TryEnsureDecisionSupportAssembliesLoadedCompat();

            string[] typeNames =
            {
                "SmartBotProfiles.DecisionDiscoverLocalBridge",
                "SmartBotProfiles.DecisionDiscoverSupportBridge"
            };

            string[] methodNames =
            {
                "TryPickDiscover",
                "TryPickLocalDiscover",
                "TryPick"
            };

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly == null)
                    continue;

                foreach (string typeName in typeNames)
                {
                    Type bridgeType = assembly.GetType(typeName, false);
                    if (bridgeType == null)
                        continue;

                    foreach (string methodName in methodNames)
                    {
                        if (TryInvokeLocalDiscoverBridgeWithReason(
                            bridgeType,
                            methodName,
                            originCard,
                            choices,
                            board,
                            profileName,
                            discoverProfileName,
                            out pick,
                            out reason))
                        {
                            return true;
                        }

                        if (TryInvokeLocalDiscoverBridgeWithoutReason(
                            bridgeType,
                            methodName,
                            originCard,
                            choices,
                            board,
                            profileName,
                            discoverProfileName,
                            out pick))
                        {
                            reason = string.IsNullOrWhiteSpace(reason) ? methodName : reason;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool TryInvokeLocalDiscoverBridgeWithReason(
            Type bridgeType,
            string methodName,
            Card.Cards originCard,
            List<Card.Cards> choices,
            Board board,
            string profileName,
            string discoverProfileName,
            out Card.Cards pick,
            out string reason)
        {
            pick = default(Card.Cards);
            reason = string.Empty;
            if (bridgeType == null || string.IsNullOrWhiteSpace(methodName))
                return false;

            try
            {
                MethodInfo method = bridgeType.GetMethod(
                    methodName,
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[]
                    {
                        typeof(Card.Cards),
                        typeof(List<Card.Cards>),
                        typeof(Board),
                        typeof(string),
                        typeof(string),
                        typeof(Card.Cards).MakeByRefType(),
                        typeof(string).MakeByRefType()
                    },
                    null);
                if (method == null)
                    return false;

                object[] args =
                {
                    originCard,
                    choices,
                    board,
                    profileName ?? string.Empty,
                    discoverProfileName ?? string.Empty,
                    pick,
                    reason
                };

                object result = method.Invoke(null, args);
                if (!(result is bool) || !(bool)result)
                    return false;

                if (args[5] is Card.Cards)
                    pick = (Card.Cards)args[5];
                if (args[6] is string)
                    reason = args[6] as string;
                return choices.Contains(pick);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryInvokeLocalDiscoverBridgeWithoutReason(
            Type bridgeType,
            string methodName,
            Card.Cards originCard,
            List<Card.Cards> choices,
            Board board,
            string profileName,
            string discoverProfileName,
            out Card.Cards pick)
        {
            pick = default(Card.Cards);
            if (bridgeType == null || string.IsNullOrWhiteSpace(methodName))
                return false;

            try
            {
                MethodInfo method = bridgeType.GetMethod(
                    methodName,
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[]
                    {
                        typeof(Card.Cards),
                        typeof(List<Card.Cards>),
                        typeof(Board),
                        typeof(string),
                        typeof(string),
                        typeof(Card.Cards).MakeByRefType()
                    },
                    null);
                if (method == null)
                    return false;

                object[] args =
                {
                    originCard,
                    choices,
                    board,
                    profileName ?? string.Empty,
                    discoverProfileName ?? string.Empty,
                    pick
                };

                object result = method.Invoke(null, args);
                if (!(result is bool) || !(bool)result)
                    return false;

                if (args[5] is Card.Cards)
                    pick = (Card.Cards)args[5];
                return choices.Contains(pick);
            }
            catch
            {
                return false;
            }
        }

        private static void TryEnsureDecisionSupportAssembliesLoadedCompat()
        {
            try
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly == null)
                        continue;

                    Type pluginType = assembly.GetType("SmartBot.Plugins.DecisionSupportAssemblyBootstrapPlugin", false);
                    if (pluginType == null)
                        continue;

                    MethodInfo ensureDiscover = pluginType.GetMethod("EnsureDiscoverSupportLoaded", BindingFlags.Public | BindingFlags.Static);
                    if (ensureDiscover != null)
                        ensureDiscover.Invoke(null, null);

                    MethodInfo ensureCommon = pluginType.GetMethod("EnsureLoaded", BindingFlags.Public | BindingFlags.Static);
                    if (ensureCommon != null)
                        ensureCommon.Invoke(null, null);
                    return;
                }
            }
            catch
            {
                // ignore
            }
        }

        private bool TryPickByLocalJsonFallback(Card.Cards originCard, List<Card.Cards> choices, Board board, out Card.Cards pick, out string reason)
        {
            pick = default(Card.Cards);
            reason = string.Empty;
            if (choices == null || choices.Count == 0)
                return false;

            string mode = CurrentMode();
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string discoverDir = Path.Combine(baseDir, "DiscoverCC", mode);
            string specificPath = Path.Combine(discoverDir, "DiscoverChoices.json");
            string classPath = Path.Combine(discoverDir, "discover.JSON");

            Dictionary<string, Dictionary<string, double>> specificScores = LoadDiscoverChoiceScoreIndex(specificPath);
            Dictionary<string, Dictionary<string, double>> classScores = LoadDiscoverClassScoreIndex(classPath);
            if ((specificScores == null || specificScores.Count == 0)
                && (classScores == null || classScores.Count == 0))
            {
                return false;
            }

            Card.Cards resolvedOrigin = ResolveOriginCardForLocalJson(specificScores, originCard, board);
            string heroClass = board != null ? (board.FriendClass.ToString() ?? string.Empty).Trim().ToUpperInvariant() : string.Empty;

            double bestScore = double.MinValue;
            Card.Cards bestCard = default(Card.Cards);
            bool usedSpecific = false;
            bool usedClass = false;

            foreach (Card.Cards choice in choices)
            {
                double totalScore = 0.0;
                bool hasAnyScore = false;

                double specificScore;
                if (TryGetDiscoverChoiceScore(specificScores, resolvedOrigin.ToString(), choice.ToString(), out specificScore))
                {
                    totalScore += 1000.0 + (specificScore * 10.0);
                    hasAnyScore = true;
                }

                double classScore;
                if (TryGetDiscoverClassScore(classScores, heroClass, choice.ToString(), out classScore))
                {
                    totalScore += 100.0 + classScore;
                    hasAnyScore = true;
                }

                if (!hasAnyScore)
                    continue;

                if (totalScore > bestScore)
                {
                    bestScore = totalScore;
                    bestCard = choice;
                    usedSpecific = TryGetDiscoverChoiceScore(specificScores, resolvedOrigin.ToString(), choice.ToString(), out specificScore);
                    usedClass = TryGetDiscoverClassScore(classScores, heroClass, choice.ToString(), out classScore);
                }
            }

            if (bestCard == default(Card.Cards))
                return false;

            pick = bestCard;
            reason = Sanitize((usedSpecific ? "origin=" + resolvedOrigin : "class_only")
                + (usedClass ? "|hero=" + heroClass : string.Empty));
            return true;
        }

        private static Card.Cards ResolveOriginCardForLocalJson(
            Dictionary<string, Dictionary<string, double>> specificScores,
            Card.Cards originCard,
            Board board)
        {
            if (specificScores == null || specificScores.Count == 0 || board == null)
                return originCard;

            if (specificScores.ContainsKey(originCard.ToString()))
                return originCard;

            List<Card.Cards> originChoices = new List<Card.Cards>();

            try
            {
                if (board.MinionEnemy != null)
                    originChoices.AddRange(board.MinionEnemy.Where(card => card != null && card.Template != null).Select(card => card.Template.Id).Where(LocalDataEnemyOriginCards.Contains));
            }
            catch
            {
                // ignore
            }

            try
            {
                if (board.MinionFriend != null)
                    originChoices.AddRange(board.MinionFriend.Where(card => card != null && card.Template != null).Select(card => card.Template.Id).Where(LocalDataFriendOriginCards.Contains));
            }
            catch
            {
                // ignore
            }

            try
            {
                if (board.PlayedCards != null && board.PlayedCards.Any())
                    originChoices.Add(board.PlayedCards.Last());
            }
            catch
            {
                // ignore
            }

            for (int i = originChoices.Count - 1; i >= 0; i--)
            {
                Card.Cards candidate = originChoices[i];
                if (candidate == originCard)
                    break;

                if (specificScores.ContainsKey(candidate.ToString()))
                    return candidate;
            }

            return originCard;
        }

        private static bool TryGetDiscoverChoiceScore(
            Dictionary<string, Dictionary<string, double>> specificScores,
            string originCardId,
            string choiceCardId,
            out double score)
        {
            score = 0.0;
            if (specificScores == null
                || string.IsNullOrWhiteSpace(originCardId)
                || string.IsNullOrWhiteSpace(choiceCardId))
            {
                return false;
            }

            Dictionary<string, double> byChoice;
            if (!specificScores.TryGetValue(originCardId, out byChoice) || byChoice == null)
                return false;

            return byChoice.TryGetValue(choiceCardId, out score);
        }

        private static bool TryGetDiscoverClassScore(
            Dictionary<string, Dictionary<string, double>> classScores,
            string heroClass,
            string choiceCardId,
            out double score)
        {
            score = 0.0;
            if (classScores == null
                || string.IsNullOrWhiteSpace(heroClass)
                || string.IsNullOrWhiteSpace(choiceCardId))
            {
                return false;
            }

            Dictionary<string, double> byClass;
            if (!classScores.TryGetValue(choiceCardId, out byClass) || byClass == null)
                return false;

            return byClass.TryGetValue(heroClass, out score);
        }

        private static Dictionary<string, Dictionary<string, double>> LoadDiscoverChoiceScoreIndex(string path)
        {
            string json = ReadCachedTextFile(path);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                List<DiscoverChoiceScoreEntry> entries = JsonConvert.DeserializeObject<List<DiscoverChoiceScoreEntry>>(json);
                if (entries == null || entries.Count == 0)
                    return null;

                Dictionary<string, Dictionary<string, double>> index = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
                foreach (DiscoverChoiceScoreEntry entry in entries)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.card_id) || entry.values == null || entry.values.Count == 0)
                        continue;

                    Dictionary<string, double> byChoice = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    foreach (DiscoverChoiceScoreValue value in entry.values)
                    {
                        if (value == null || string.IsNullOrWhiteSpace(value.card_id))
                            continue;

                        byChoice[value.card_id] = value.discover_score;
                    }

                    if (byChoice.Count > 0)
                        index[entry.card_id] = byChoice;
                }

                return index;
            }
            catch
            {
                return null;
            }
        }

        private static Dictionary<string, Dictionary<string, double>> LoadDiscoverClassScoreIndex(string path)
        {
            string json = ReadCachedTextFile(path);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                List<DiscoverClassScoreEntry> entries = JsonConvert.DeserializeObject<List<DiscoverClassScoreEntry>>(json);
                if (entries == null || entries.Count == 0)
                    return null;

                Dictionary<string, Dictionary<string, double>> index = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
                foreach (DiscoverClassScoreEntry entry in entries)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.card_id) || entry.classes == null || entry.classes.Count == 0)
                        continue;

                    index[entry.card_id] = new Dictionary<string, double>(entry.classes, StringComparer.OrdinalIgnoreCase);
                }

                return index;
            }
            catch
            {
                return null;
            }
        }

        private static string ReadCachedTextFile(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return string.Empty;

                DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
                lock (DiscoverJsonCacheSync)
                {
                    CachedTextFile cached;
                    if (DiscoverJsonCache.TryGetValue(path, out cached)
                        && cached != null
                        && cached.LastWriteTimeUtc == lastWriteTimeUtc)
                    {
                        return cached.Content ?? string.Empty;
                    }

                    string content = File.ReadAllText(path);
                    DiscoverJsonCache[path] = new CachedTextFile
                    {
                        LastWriteTimeUtc = lastWriteTimeUtc,
                        Content = content
                    };
                    return content;
                }
            }
            catch
            {
                return string.Empty;
            }
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
            string scriptPath = Path.Combine(repoRoot, "tools", "decision_teacher_ocr.py");
            if (!File.Exists(scriptPath))
                scriptPath = Path.Combine(repoRoot, "tools", "netease_box_ocr.py");
            if (File.Exists(scriptPath))
            {
                fileName = ResolveBundledPython(repoRoot);
                argumentPrefix = Quote(scriptPath);
                return true;
            }

            fileName = ResolveBundledOcrExecutable(repoRoot);
            argumentPrefix = string.Empty;
            return !string.IsNullOrWhiteSpace(fileName);
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

        private void RunDiscoverOcr(Card.Cards originCard, List<Card.Cards> choices, bool allowRecentDedup)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string repoRoot = FindRepoRoot(baseDir);
                string currentProfile = Sanitize(SafeCurrentProfileName());
                string currentMulligan = Sanitize(SafeCurrentMulliganName());
                string currentDiscoverProfile = Sanitize(SafeCurrentDiscoverProfileName());
                string currentMode = CurrentMode();
                string ocrCommandPath;
                string ocrArgumentPrefix;
                if (!TryResolveOcrRunner(repoRoot, out ocrCommandPath, out ocrArgumentPrefix))
                {
                    AddLog("[Teacher] runtime_missing");
                    return;
                }

                string captureDir = Path.Combine(baseDir, "runtime", "decision_teacher_ocr");
                if (!Directory.Exists(captureDir))
                {
                    string legacyCaptureDir = Path.Combine(baseDir, "runtime", "box_ocr");
                    if (Directory.Exists(legacyCaptureDir))
                        captureDir = legacyCaptureDir;
                }

                string stateFile = Path.Combine(baseDir, "runtime", "decision_teacher_discover_state.txt");
                if (!File.Exists(stateFile))
                {
                    string legacyStateFile = Path.Combine(baseDir, "runtime", "netease_box_ocr_discover_state.txt");
                    if (File.Exists(legacyStateFile))
                        stateFile = legacyStateFile;
                }
                string candidateFile = Path.Combine(captureDir, "discover_candidates.txt");
                Directory.CreateDirectory(captureDir);
                WriteDiscoverCandidates(candidateFile, choices);

                string requestKey = BuildDiscoverOcrRequestKey(
                    originCard,
                    choices,
                    currentProfile,
                    currentMulligan,
                    currentDiscoverProfile,
                    currentMode);
                if (allowRecentDedup && ShouldSkipRecentDiscoverOcrLaunch(requestKey))
                    return;

                MarkDiscoverOcrLaunch(requestKey);

                string commandPath = ocrCommandPath;
                string args;
                string daemonClientCommand = ResolveDaemonClientCommand(repoRoot);
                if (!string.IsNullOrWhiteSpace(daemonClientCommand))
                {
                    bool usePowerShellClient = IsPowerShellScriptPath(daemonClientCommand);
                    StringBuilder daemonArgs = new StringBuilder();
                    if (usePowerShellClient)
                    {
                        commandPath = "powershell";
                        daemonArgs.Append("-ExecutionPolicy Bypass -File ")
                            .Append(Quote(daemonClientCommand));
                    }
                    else
                    {
                        commandPath = daemonClientCommand;
                    }

                    daemonArgs.Append(usePowerShellClient ? " -OcrCommand " : "-OcrCommand ")
                        .Append(Quote(ocrCommandPath))
                        .Append(" -OcrArgumentPrefix ")
                        .Append(Quote(ocrArgumentPrefix ?? string.Empty))
                        .Append(" -Port 17873")
                        .Append(" -Image ")
                        .Append(Quote(string.Empty))
                        .Append(" -State ")
                        .Append(Quote(stateFile))
                        .Append(" -CandidateFile ")
                        .Append(Quote(candidateFile))
                        .Append(" -Stage ")
                        .Append(Quote("discover"))
                        .Append(" -SbProfile ")
                        .Append(Quote(currentProfile))
                        .Append(" -SbMulligan ")
                        .Append(Quote(currentMulligan))
                        .Append(" -SbDiscoverProfile ")
                        .Append(Quote(currentDiscoverProfile))
                        .Append(" -SbMode ")
                        .Append(Quote(currentMode))
                        .Append(" -StrategyRef ")
                        .Append(Quote("A"))
                        .Append(" -CaptureWindow");
                    args = daemonArgs.ToString();
                }
                else
                {
                    args = string.Join(" ", new[]
                    {
                        "--image", Quote(string.Empty),
                        "--state", Quote(stateFile),
                        "--candidate-file", Quote(candidateFile),
                        "--stage", Quote("discover"),
                        "--sb-profile", Quote(currentProfile),
                        "--sb-mulligan", Quote(currentMulligan),
                        "--sb-discover-profile", Quote(currentDiscoverProfile),
                        "--sb-mode", Quote(currentMode),
                        "--strategy-ref", Quote("A"),
                        "--origin-card", Quote(originCard.ToString()),
                        "--capture-window"
                    });
                    if (!string.IsNullOrWhiteSpace(ocrArgumentPrefix))
                        args = ocrArgumentPrefix + " " + args;
                }

                using (Process process = new Process())
                {
                    process.StartInfo = new ProcessStartInfo
                    {
                        FileName = commandPath,
                        Arguments = args,
                        WorkingDirectory = repoRoot,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true
                    };
                    process.Start();
                    if (!process.WaitForExit(15000))
                    {
                        try { process.Kill(); } catch { }
                        AddLog("[Teacher] ocr_timeout");
                        return;
                    }

                    string stderr = string.Empty;
                    try { stderr = process.StandardError.ReadToEnd().Trim(); } catch { }
                    if (process.ExitCode != 0)
                    {
                        AddLog("[Teacher] ocr_failed=" + (string.IsNullOrWhiteSpace(stderr) ? ("exit=" + process.ExitCode) : stderr));
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog("[Teacher] ocr_failed=" + ex.Message);
            }
        }

        private void WriteDiscoverCandidates(string candidateFile, List<Card.Cards> choices)
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

            File.WriteAllLines(candidateFile, rows, Encoding.UTF8);
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
                    string exeCandidate = Path.Combine(dir.FullName, "tools", "decision_teacher_ocr.exe");
                    if (File.Exists(exeCandidate))
                        return dir.FullName;

                    string nestedExeCandidate = Path.Combine(dir.FullName, "tools", "decision_teacher_ocr", "decision_teacher_ocr.exe");
                    if (File.Exists(nestedExeCandidate))
                        return dir.FullName;

                    string primaryCandidate = Path.Combine(dir.FullName, "tools", "decision_teacher_ocr.py");
                    if (File.Exists(primaryCandidate))
                        return dir.FullName;

                    string legacyCandidate = Path.Combine(dir.FullName, "tools", "netease_box_ocr.py");
                    if (File.Exists(legacyCandidate))
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
                .Append('|').Append(profileName ?? string.Empty)
                .Append('|').Append(mulliganName ?? string.Empty)
                .Append('|').Append(discoverProfileName ?? string.Empty)
                .Append('|').Append(mode ?? string.Empty);

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

        private sealed class CachedTextFile
        {
            public DateTime LastWriteTimeUtc;
            public string Content;
        }

        private sealed class DiscoverChoiceScoreEntry
        {
            public string card_id { get; set; }
            public List<DiscoverChoiceScoreValue> values { get; set; }
        }

        private sealed class DiscoverChoiceScoreValue
        {
            public string card_id { get; set; }
            public double discover_score { get; set; }
        }

        private sealed class DiscoverClassScoreEntry
        {
            public string card_id { get; set; }
            public Dictionary<string, double> classes { get; set; }
        }
    }
}
