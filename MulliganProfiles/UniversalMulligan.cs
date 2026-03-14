using Newtonsoft.Json;
using SmartBot.Database;
using SmartBot.Plugins.API;
using SmartBotAPI.Plugins.API.HSReplayArchetypes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

// 本插件原始设计者为 Olanga 和 EE
// 最后编辑 2026/02/12 by EE

namespace SmartBot.Mulligan
{
    [Serializable]
    public class UniversalMulligan : MulliganProfile
    {
        // 初始化日志和保留卡牌列表
        private string log;

        // 留牌阶段要保留的卡牌列表
        private List<Card.Cards> CardsToKeep = new List<Card.Cards>();

        // 来自 HSReplay 的留牌数据本地列表
        private List<HSReplayHelper.HSReplayMulligan> mulliganCards;

        // 用于显示保留卡牌的 StringBuilder
        private StringBuilder keptCardBuilder = new StringBuilder("\nCards to Keep:");

        // 用于显示待选卡牌的 StringBuilder
        private StringBuilder choiceCardsBuilder = new StringBuilder("\nCards to choose from:");

        // 目录
        private readonly string _mulliganProfilesDirectory = Directory.GetCurrentDirectory() + @"\MulliganProfiles\";

        // 机器人留牌处理
        public List<Card.Cards> HandleMulligan(List<Card.Cards> choices, Card.CClass opponentClass, Card.CClass ownClass)
        {
            // 定义牌组 ID、所有牌组的留牌数据和通用留牌数据的文件路径
            string deckIdFile = Path.Combine(_mulliganProfilesDirectory, @"{MODE}\DeckId.json");

            // 所有牌组的留牌数据，用于标准和狂野模式，根据匹配的牌组 ID 获取特定的留牌百分比
            string deckMulliganFile = Path.Combine(_mulliganProfilesDirectory, @"{MODE}\MulliganAllDecks.json");

            // 通用留牌数据，作为标准和狂野的兜底，也是竞技场、转变、练习和酒馆战棋的主要数据源
            string mulliganFile = Path.Combine(_mulliganProfilesDirectory, @"{MODE}\mulligan.json");

            string deckId = null;
            string dataJson = null;
            string deckList = null;
            string matchDeck = null;
            string mode = CurrentMode();

            log = string.Format("===== Universal Mulligan V5.2, Card definition V{0} ===EE", CurrentVersion());

            try
            {
                // 根据当前模式准备文件路径
                string deckIdPath = deckIdFile.Replace("{MODE}", mode);
                string deckMulliganPath = deckMulliganFile.Replace("{MODE}", mode);
                string mulliganPath = mulliganFile.Replace("{MODE}", mode);

                switch (mode)
                {
                    case "Standard":
                    case "Wild":
                        {
                            // 读取牌组 ID 文件和所有牌组的留牌数据
                            deckList = File.ReadAllText(deckIdPath);

                            // 预加载牌组卡牌 ID
                            var myDeck = Bot.CurrentDeck().Cards.Select(c => CardTemplate.LoadFromId(c).Id).ToList();

                            // 从 HSReplay 获取最佳匹配的牌组 ID
                            deckId = DeckId(deckList, myDeck, ownClass);

                            // 读取匹配牌组 ID 的留牌数据
                            dataJson = File.ReadAllText(deckMulliganPath);

                            // 获取匹配牌组 ID 的留牌数据
                            mulliganCards = HSReplayHelper.ExternalMulliganData(dataJson, deckId);

                            // 未找到高价值卡牌时回退到通用留牌数据
                            if (!mulliganCards.Any() || mulliganCards.Count(m => choices.Contains(m.Key) && m.Value > 49 && CardTemplate.LoadFromId(m.Key).Cost < 4) == 0)
                            {
                                matchDeck = "Mulligan profiles from mulligan file, mode: " + mode;
                                dataJson = File.ReadAllText(mulliganPath);
                                mulliganCards = HSReplayHelper.InternalMulliganData(dataJson, ownClass);
                            }
                            else
                            {
                                matchDeck = deckId + " gameType = " + mode;
                            }
                            break;
                        }

                    case "Arena":
                        {
                            // 竞技场使用内部留牌数据
                            matchDeck = "Mulligan profiles from mulligan file, mode: " + mode;
                            dataJson = File.ReadAllText(mulliganPath);
                            mulliganCards = HSReplayHelper.InternalMulliganData(dataJson, ownClass);
                            break;
                        }

                    default:
                        {
                            // 其他模式使用内部留牌数据
                            matchDeck = "Mulligan profiles from mulligan file, mode: " + mode;
                            dataJson = File.ReadAllText(mulliganFile.Replace("{MODE}", "Wild"));
                            mulliganCards = HSReplayHelper.InternalMulliganData(dataJson, ownClass);
                            break;
                        }
                }

                // 计算留牌数据与当前牌组的重叠度
                var deckCardIds = Bot.CurrentDeck().Cards.Select(c => CardTemplate.LoadFromId(c).Id).ToList();
                int overlap = mulliganCards.Count(m => deckCardIds.Contains(m.Key));

                AddLog("Selected Deck: " + Bot.CurrentDeck().Name);
                AddLog("Archetype: " + GetCurrentHSReplayArchetype());
                AddLog("Matched Deck: " + matchDeck);
                AddLog("Matching Entries: " + overlap + "/" + mulliganCards.Count);
                AddLog("\nFull Mulligan Data:");

                // 根据留牌数据进行卡牌选择
                CardChoice(choices, mulliganCards);
            }
            catch (Exception)
            {
                string fallbackPath = mulliganFile.Replace("{MODE}", mode.ToUpper());
                dataJson = File.ReadAllText(fallbackPath);

                mulliganCards = HSReplayHelper.InternalMulliganData(dataJson, ownClass);
                AddLog("Error from HSReplayMulligan: using default logic, " + mulliganCards.Count + " cards");

                CardChoice(choices, mulliganCards);
            }

            AddLog(choiceCardsBuilder + "\r\n" + keptCardBuilder);
            AddLog("===== End of mulligan ======");
            Bot.Log(log);

            return CardsToKeep;
        }

        // 从当前选择的牌组字符串获取当前牌组模式
        private static string CurrentMode()
        {
            var mode = Bot.CurrentMode();

            if (mode == Bot.Mode.Practice || mode == Bot.Mode.Casual)
            {
                string key = Bot.CurrentDeck().Name.Substring(1, 1);
                return key == "S" ? "Standard" :
                       key == "W" ? "Wild" : "Wild";
            }

            if (mode == Bot.Mode.Arena || mode == Bot.Mode.ArenaAuto) return "Arena";
            if (mode == Bot.Mode.Standard) return "Standard";

            return "Wild";
        }

        // 根据留牌百分比处理卡牌选择的方法
        private void CardChoice(List<Card.Cards> choices, List<HSReplayHelper.HSReplayMulligan> mulliganCards)
        {
            // 初始化当前牌组中的卡牌列表，去除重复
            var myDeck = Bot.CurrentDeck().Cards.Distinct().Select(card => CardTemplate.LoadFromId(card).Id).ToList();

            double mulliganValue = 0;
            Card.Cards bestLowCost = Card.Cards.GAME_005;

            // 遍历留牌卡牌并记录选择
            foreach (var mulliganCard in mulliganCards.OrderByDescending(card => card.Value))
            {
                if (myDeck.Contains(mulliganCard.Key) || choices.Contains(mulliganCard.Key))
                {
                    AddLog(string.Format("{0} - Kept : {1}%", CardTemplate.LoadFromId(mulliganCard.Key).Name, mulliganCard.Value.ToString("0.00")));
                }

                // 如果卡牌在当前牌组或选择中，将其加入保留卡牌
                if (choices.Contains(mulliganCard.Key))
                {
                    if (mulliganCard.Value >= 75)
                    {
                        keptCardBuilder.AppendFormat("\n{0} - Kept : {1}%", CardTemplate.LoadFromId(mulliganCard.Key).Name, mulliganCard.Value.ToString("0.00"));
                        Keep(mulliganCard.Key);
                    }

                    // 如果是低费卡牌且保留百分比高于当前最佳，更新最佳保留卡牌
                    if (mulliganCard.Value > mulliganValue && CardTemplate.LoadFromId(mulliganCard.Key).Cost < 4)
                    {
                        mulliganValue = mulliganCard.Value;
                        bestLowCost = mulliganCard.Key;
                    }

                    // 将卡牌追加到待选卡牌字符串
                    choiceCardsBuilder.AppendFormat("\n{0} - Kept : {1}%", CardTemplate.LoadFromId(mulliganCard.Key).Name, mulliganCard.Value.ToString("0.00"));
                }
            }

            // 如果没有保留任何卡牌且留牌值超过 49%，保留最佳低费卡牌
            if (!CardsToKeep.Any() && mulliganValue > 49)
            {
                var card = CardTemplate.LoadFromId(bestLowCost);
                choiceCardsBuilder.AppendFormat("\n\nSelecting best, low cost card:\n{0} - Kept : {1}%", card.Name, mulliganValue.ToString("0.00"));
                keptCardBuilder.AppendFormat("\n{0} - Kept : {1}%", card.Name, mulliganValue.ToString("0.00"));
                Keep(bestLowCost);
            }
        }

        // 将卡牌加入保留列表
        private void Keep(Card.Cards id)
        {
            CardsToKeep.Add(id);
        }

        // 合并所有 AddLog，包含回车和换行
        private void AddLog(string message)
        {
            log += "\r\n" + message;
        }

        // 获取当前 HSReplay 原型
        private static string GetCurrentHSReplayArchetype()
        {
            var HSReplayArchetype = HSReplayArchetypesMatcher.DetectArchetype(Bot.GetSelectedDecks()[0].Cards, Bot.GetSelectedDecks()[0].Class, 30);
            if (HSReplayArchetype == null || HSReplayArchetype is HSReplayArchetypeError)
                return string.Empty; // Archetype couldn't be found
            return HSReplayArchetype.Name;
        }

        // 新类
        // 提取并排序从 HSReplay 收到的所需留牌数据，转换为新格式列表
        public class HSReplayHelper
        {
            private static List<HSReplayMulligan> cards = new List<HSReplayMulligan>();

            // 标准和狂野模式的留牌数据
            public static List<HSReplayMulligan> ExternalMulliganData(string contents, string deckId)
            {
                cards.Clear();

                var root = JsonConvert.DeserializeObject<Dictionary<string, List<MulliganRoot>>>(contents);

                if (root == null || !root.ContainsKey(deckId))
                    return cards;

                // 仅获取请求的职业/牌组 ID 对应的条目
                var cardEntries = root[deckId];

                foreach (var card in cardEntries)
                {
                    var template = CardTemplate.TemplateList.FirstOrDefault(x => x.Value.Id.ToString() == card.card_id);

                    if (template.Value != null) // ensure match found
                    {
                        var info = new HSReplayMulligan
                        {
                            Key = template.Key,
                            Value = card.opening_hand_winrate
                        };
                        cards.Add(info);
                    }
                }

                return cards;
            }

            // 从 HSReplay 的"留牌保留表"获取最佳保留留牌百分比，用于竞技场、转变、练习和酒馆战棋
            public static List<HSReplayMulligan> InternalMulliganData(string jsonData, Card.CClass ownClass)
            {
                cards.Clear();

                CardRoot deckDataHSReplay = JsonConvert.DeserializeObject<CardRoot>(jsonData);

                var className = ownClass.ToString().ToUpper();
                var deckData = typeof(CardRoot).GetProperty(className)?.GetValue(deckDataHSReplay, null) as List<CardScore>;

                if (deckData == null) return cards; // safety check

                foreach (var card_by_dbf_Id in deckData)
                {
                    var info = new HSReplayMulligan
                    {
                        Key = CardTemplate.TemplateList.FirstOrDefault(x => x.Value.Id.ToString() == card_by_dbf_Id.card_id).Key,
                        Value = card_by_dbf_Id.mulligan_score
                    };
                    cards.Add(info);
                }

                return cards;
            }

            // 表示特定卡牌留牌信息的类
            public class HSReplayMulligan
            {
                public Card.Cards Key; // 卡牌 ID
                public double Value; // 留牌中保留该卡牌的百分比
            }
        }
        // HSReplayHelper 类结束

        // 计算给定牌库列表与牌组的匹配分数的方法
        public static int GetMatchPoint(string deckList, List<Card.Cards> deck)
        {
            const string pattern = @"\[(?<Id>\d+),(?<count>\d+)]";
            int count = 0;

            foreach (Match match in Regex.Matches(deckList, pattern))
            {
                int cardDbfId = int.Parse(match.Groups["Id"].Value);
                int cardCount = int.Parse(match.Groups["count"].Value);

                // 根据 DbfId 在 CardTemplate 列表中查找卡牌 ID
                var cardId = CardTemplate.TemplateList.FirstOrDefault(x => x.Value.DbfId == cardDbfId).Key;

                // 计算该卡牌在给定牌组中出现的次数
                int cardCountInDeck = deck.Count(c => c == cardId);

                // 如果卡牌存在则奖励分数，如果双方都有2张则额外加分
                count += (cardCountInDeck > 0 ? 1 : 0);
                count += (cardCountInDeck == 2 && cardCount == 2 ? 1 : 0);
            }
            return count;
        }

        // 获取数据中与给定牌组最接近匹配的牌组 ID 的方法
        public static string DeckId(string jsonData, List<Card.Cards> deck, Card.CClass ownClass)
        {
            string bestId = null;
            int bestPoints = 0, bestGames = 0;

            var deckRoot = JsonConvert.DeserializeObject<DeckRoot>(jsonData);
            if (deckRoot == null) return null;

            var propertyInfo = typeof(DeckRoot).GetProperty(ownClass.ToString().Trim().ToUpper());
            if (propertyInfo == null) return null;
            var deck_IDs = propertyInfo.GetValue(deckRoot) as List<Deck>;
            if (deck_IDs == null) return null;

            foreach (var id in deck_IDs)
            {
                int points = GetMatchPoint(id.deck_list, deck);

                if (points > bestPoints || (points == bestPoints && id.total_games > bestGames))
                {
                    bestPoints = points;
                    bestGames = id.total_games;
                    bestId = id.deck_id;
                }
            }
            return bestId;
        }

        // 从 EE_Information.txt 获取当前版本
        private string CurrentVersion()
        {
            string discoverCCDirectory = Path.Combine(Directory.GetCurrentDirectory(), "DiscoverCC");
            string infoPath = Path.Combine(discoverCCDirectory, "EE_Information.txt");

            // 如果文件不存在则创建
            if (!File.Exists(infoPath))
                return null;

            // 读取第一行并用正则表达式提取版本号
            string firstLine = File.ReadLines(infoPath).First();
            Match match = Regex.Match(firstLine, @"\((.+?)\)");
            if (match.Success)
                return match.Groups[1].Value;
            return null;
        }

        // *** Newtonsoft.Json ***
        // ============================================================
        // HSReplay 留牌评分模型
        // 用于反序列化职业特定的留牌评分数据
        // ============================================================

        public class CardScore
        {
            public string card_id { get; set; }
            public double mulligan_score { get; set; }
        }

        public class CardRoot
        {
            public List<CardScore> ALL { get; set; }
            public List<CardScore> DEATHKNIGHT { get; set; }
            public List<CardScore> DEMONHUNTER { get; set; }
            public List<CardScore> DRUID { get; set; }
            public List<CardScore> HUNTER { get; set; }
            public List<CardScore> MAGE { get; set; }
            public List<CardScore> PALADIN { get; set; }
            public List<CardScore> PRIEST { get; set; }
            public List<CardScore> ROGUE { get; set; }
            public List<CardScore> SHAMAN { get; set; }
            public List<CardScore> WARLOCK { get; set; }
            public List<CardScore> WARRIOR { get; set; }
        }

        // ============================================================
        // HSReplay 起手胜率模型
        // 用于反序列化单张卡牌起手胜率数据
        // ============================================================

        public class MulliganRoot
        {
            public string card_id { get; set; }
            public double opening_hand_winrate { get; set; }
        }

        // ============================================================
        // HSReplay 留牌数据模型
        // 用于反序列化 mulligan.json 和职业特定数据
        // ============================================================

        public class Deck
        {
            public string deck_id { get; set; }
            public string deck_list { get; set; }
            public int total_games { get; set; }
            public double win_rate { get; set; }
        }

        public class DeckRoot
        {
            public List<Deck> DEATHKNIGHT { get; set; }
            public List<Deck> DEMONHUNTER { get; set; }
            public List<Deck> DRUID { get; set; }
            public List<Deck> HUNTER { get; set; }
            public List<Deck> MAGE { get; set; }
            public List<Deck> PALADIN { get; set; }
            public List<Deck> PRIEST { get; set; }
            public List<Deck> ROGUE { get; set; }
            public List<Deck> SHAMAN { get; set; }
            public List<Deck> WARLOCK { get; set; }
            public List<Deck> WARRIOR { get; set; }
        }
    }
}
