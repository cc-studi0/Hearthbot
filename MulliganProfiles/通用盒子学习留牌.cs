using Newtonsoft.Json;
using SmartBot.Database;
using SmartBot.Plugins.API;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SmartBot.Mulligan
{
    [Serializable]
    public class BoxLearningMulligan : MulliganProfile
    {
        private readonly StringBuilder _log = new StringBuilder();

        public List<Card.Cards> HandleMulligan(List<Card.Cards> choices, Card.CClass opponentClass, Card.CClass ownClass)
        {
            List<Card.Cards> keeps = new List<Card.Cards>();
            _log.Clear();

            AddLog("===== 通用学习留牌 v2026-03-14.1 =====");
            AddLog("Profile=" + SafeCurrentProfileName() + " | Mulligan=" + SafeCurrentMulliganName() + " | Deck=" + SafeCurrentDeckName());
            AddLog("Own=" + ownClass + " | Opponent=" + opponentClass + " | Choices=" + FormatChoices(choices));

            bool changed = DecisionMulliganMemoryCompat.ApplyStandaloneLearningHints(keeps, choices, opponentClass, ownClass);
            if (changed)
                AddLog("Source=teacher_or_memory | Keeps=" + FormatChoices(keeps));
            else
                AddLog("Source=none | fallback=replace_all");

            FlushLog();
            return keeps.Distinct().ToList();
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
            return string.Join(", ", names.Distinct().ToArray());
        }

        private static string SafeCardName(Card.Cards id)
        {
            try
            {
                CardTemplate card = CardTemplate.LoadFromId(id);
                if (card != null)
                {
                    if (!string.IsNullOrWhiteSpace(card.NameCN))
                        return card.NameCN + "(" + id + ")";
                    if (!string.IsNullOrWhiteSpace(card.Name))
                        return card.Name + "(" + id + ")";
                }
            }
            catch
            {
                // ignore
            }

            return id.ToString();
        }

        private static string SafeCurrentProfileName()
        {
            try { return Bot.CurrentProfile(); } catch { return string.Empty; }
        }

        private static string SafeCurrentMulliganName()
        {
            try { return Bot.CurrentMulligan(); } catch { return string.Empty; }
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

    internal static class DecisionMulliganMemoryCompat
    {
        public static bool ApplyStandaloneLearningHints(
            List<Card.Cards> keeps,
            List<Card.Cards> choices,
            Card.CClass opponentClass,
            Card.CClass ownClass)
        {
            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = assemblies.Length - 1; i >= 0; i--)
                {
                    var assembly = assemblies[i];
                    var type = assembly.GetType("SmartBot.Mulligan.DecisionMulliganMemory", false);
                    if (type == null)
                        continue;

                    var method = type.GetMethod(
                        "ApplyStandaloneLearningHints",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                        null,
                        new[]
                        {
                            typeof(List<Card.Cards>),
                            typeof(List<Card.Cards>),
                            typeof(Card.CClass),
                            typeof(Card.CClass)
                        },
                        null);

                    if (method == null)
                        continue;

                    try
                    {
                        object result = method.Invoke(null, new object[] { keeps, choices, opponentClass, ownClass });
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
    }
}
