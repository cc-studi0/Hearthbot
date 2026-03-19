using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using SmartBot.Database;
using SmartBot.Plugins.API;

namespace SmartBotProfiles
{
    [Serializable]
    public class UniversalPlayProfile : Profile
    {
        private string _log = string.Empty;
        private const string ProfileVersion = "2026-03-14.4";
        private const Card.Cards TheCoin = Card.Cards.GAME_005;

        public ProfileParameters GetParameters(Board board)
        {
            _log = string.Empty;

            ProfileParameters p = new ProfileParameters(BaseProfile.Rush)
            {
                DiscoverSimulationValueThresholdPercent = -10
            };

            AddLog("============== 通用学习出牌 v" + ProfileVersion + " ==============");
            AddLog("Profile=" + SafeCurrentProfileName() + " | Deck=" + SafeCurrentDeckName());

            if (board == null)
            {
                AddLog("[PlayExecutor][MISS] board unavailable");
                FlushLog();
                return p;
            }

            AddLog(
                "Enemy=" + board.EnemyClass
                + " | Mana=" + board.ManaAvailable + "/" + board.MaxMana
                + " | Hand=" + (board.Hand != null ? board.Hand.Count : 0)
                + " | FriendDeck=" + board.FriendDeckCount);

            ConfigureForcedResimulation(board, p);

            bool applied = TryApplyPlayExecutorCompat(board, p, AddLog);
            if (!applied)
                AddLog("[PlayExecutor][MISS] no teacher step hit; fallback to default base profile");

            FlushLog();
            return p;
        }

        private void AddLog(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            if (!string.IsNullOrWhiteSpace(_log))
                _log += "\r\n";
            _log += line;
        }

        private void FlushLog()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_log))
                    Bot.Log(_log);
            }
            catch
            {
                // ignore
            }
        }

        private static string SafeCurrentProfileName()
        {
            try
            {
                string profile = Bot.CurrentProfile();
                return string.IsNullOrWhiteSpace(profile) ? string.Empty : profile.Trim();
            }
            catch
            {
                return string.Empty;
            }
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

        private static bool TryApplyPlayExecutorCompat(Board board, ProfileParameters p, Action<string> addLog)
        {
            try
            {
                EnsureDecisionSupportAssemblyLoaded();
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = assemblies.Length - 1; i >= 0; i--)
                {
                    var assembly = assemblies[i];
                    var executorType = assembly.GetType("SmartBotProfiles.DecisionPlayExecutor", false);
                    if (executorType == null)
                        continue;

                    var method = executorType.GetMethod(
                        "ApplyStandalone",
                        new[] { typeof(Board), typeof(ProfileParameters), typeof(bool), typeof(Action<string>) });
                    if (method == null)
                        continue;

                    try
                    {
                        object result = method.Invoke(null, new object[] { board, p, true, addLog });
                        if (result is bool)
                            return (bool)result;
                    }
                    catch
                    {
                        // ignore and try older loaded versions
                    }
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private static void EnsureDecisionSupportAssemblyLoaded()
        {
            string[] candidates = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime", "compilecheck_decisionplayexecutor", "bin", "Debug", "net8.0", "compilecheck_decisionplayexecutor.dll"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime", "compilecheck_decisionplayexecutor", "bin", "Release", "net8.0", "compilecheck_decisionplayexecutor.dll")
            };

            foreach (string candidate in candidates)
            {
                try
                {
                    if (!File.Exists(candidate))
                        continue;

                    Assembly.LoadFrom(candidate);
                    return;
                }
                catch
                {
                    // ignore
                }
            }
        }

        private void ConfigureForcedResimulation(Board board, ProfileParameters p)
        {
            if (board == null || board.Hand == null || board.Hand.Count == 0 || p == null)
                return;

            if (p.ForcedResimulationCardList == null)
                p.ForcedResimulationCardList = new List<Card.Cards>();

            HashSet<Card.Cards> unique = new HashSet<Card.Cards>();
            foreach (Card card in board.Hand)
            {
                if (card == null || card.Template == null)
                    continue;

                Card.Cards id = card.Template.Id;

                if (!unique.Add(id))
                    continue;

                if (!p.ForcedResimulationCardList.Contains(id))
                    p.ForcedResimulationCardList.Add(id);
            }

            if (unique.Count > 0)
                AddLog("[PlayExecutor][INFO] forced resim cards +" + unique.Count + " (except coin)");
        }

        public Card.Cards SirFinleyChoice(System.Collections.Generic.List<Card.Cards> choices)
        {
            return choices != null && choices.Count > 0 ? choices[0] : default(Card.Cards);
        }

        public Card.Cards KazakusChoice(System.Collections.Generic.List<Card.Cards> choices)
        {
            return choices != null && choices.Count > 0 ? choices[0] : default(Card.Cards);
        }
    }
}
