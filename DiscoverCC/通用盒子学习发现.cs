using SmartBot.Database;
using SmartBot.Discover;
using SmartBot.Plugins.API;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace UniversalDiscover
{
    public class BoxLearningDiscover : DiscoverPickHandler
    {
        private readonly StringBuilder _log = new StringBuilder();
        private static readonly Random Random = new Random();

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
            AddLog("===== 通用学习发现 v2026-03-15.1 =====");
            AddLog("Profile=" + SafeCurrentProfileName() + " | Discover=" + SafeCurrentDiscoverProfileName() + " | Deck=" + SafeCurrentDeckName());
            AddLog("Origin=" + SafeCardName(originCard) + " | Choices=" + FormatChoices(choices));

            if (choices == null || choices.Count == 0)
            {
                FlushLog();
                return default(Card.Cards);
            }

            Card.Cards pick;
            if (TryPickByTeacher(originCard, choices, board, out pick))
            {
                AddLog("Source=teacher | Pick=" + SafeCardName(pick));
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

            for (int attempt = 0; attempt < 3; attempt++)
            {
                RunDiscoverOcr(originCard, choices);

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
                        pick = (Card.Cards)teacherPick;
                        CaptureTeacherSampleCompat(
                            originCard,
                            choices,
                            pick,
                            board,
                            SafeCurrentProfileName(),
                            SafeCurrentDiscoverProfileName());
                        return true;
                    }
                }

                if (attempt < 2)
                    Thread.Sleep(180);
            }

            return false;
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
            fileName = ResolveBundledOcrExecutable(repoRoot);
            argumentPrefix = string.Empty;
            if (!string.IsNullOrWhiteSpace(fileName))
                return true;

            string scriptPath = Path.Combine(repoRoot, "tools", "decision_teacher_ocr.py");
            if (!File.Exists(scriptPath))
                scriptPath = Path.Combine(repoRoot, "tools", "netease_box_ocr.py");
            if (!File.Exists(scriptPath))
                return false;

            fileName = ResolveBundledPython(repoRoot);
            argumentPrefix = Quote(scriptPath);
            return true;
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

        private void RunDiscoverOcr(Card.Cards originCard, List<Card.Cards> choices)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string repoRoot = FindRepoRoot(baseDir);
                string commandPath;
                string argumentPrefix;
                if (!TryResolveOcrRunner(repoRoot, out commandPath, out argumentPrefix))
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

                string stateFile = Path.Combine(baseDir, "runtime", "decision_teacher_state.txt");
                if (!File.Exists(stateFile))
                {
                    string legacyStateFile = Path.Combine(baseDir, "runtime", "netease_box_ocr_state.txt");
                    if (File.Exists(legacyStateFile))
                        stateFile = legacyStateFile;
                }
                string candidateFile = Path.Combine(captureDir, "discover_candidates.txt");
                Directory.CreateDirectory(captureDir);
                WriteDiscoverCandidates(candidateFile, choices);

                string args = string.Join(" ", new[]
                {
                    "--image", Quote(string.Empty),
                    "--state", Quote(stateFile),
                    "--candidate-file", Quote(candidateFile),
                    "--stage", Quote("discover"),
                    "--sb-profile", Quote(Sanitize(SafeCurrentProfileName())),
                    "--sb-mulligan", Quote(Sanitize(SafeCurrentMulliganName())),
                    "--sb-discover-profile", Quote(Sanitize(SafeCurrentDiscoverProfileName())),
                    "--sb-mode", Quote(CurrentMode()),
                    "--strategy-ref", Quote("A"),
                    "--origin-card", Quote(originCard.ToString()),
                    "--capture-window"
                });
                if (!string.IsNullOrWhiteSpace(argumentPrefix))
                    args = argumentPrefix + " " + args;

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
                    rows.Add("card\t" + id + "\t" + Sanitize(SafeCardName(id)) + "\t" + (i + 1));
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
    }
}
