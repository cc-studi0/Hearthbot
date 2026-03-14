using Newtonsoft.Json;
using SmartBot.Database;
using SmartBot.Plugins.API;
using SmartBotAPI.Plugins.API.HSReplayArchetypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace SmartBot.Mulligan
{
    [Serializable]
    public class HSReplayMulligan : MulliganProfile
    {
        // 初始化日志和保留卡牌列表
        private string _log = "";
        List<Card.Cards> CardsToKeep = new List<Card.Cards>();

        public List<Card.Cards> HandleMulligan(List<Card.Cards> choices, Card.CClass opponentClass, Card.CClass ownClass)
        {
            try
            {
                AddLog("\n----Universal HSReplay Mulligan----\n");
                string deckListsUrl = "";
                string mulliganUrl = "";
                string mode = "";

                // 根据游戏模式确定牌库列表和留牌 URL
                if (Bot.CurrentMode() == Bot.Mode.Standard || Bot.CurrentMode() == Bot.Mode.Casual || Bot.CurrentMode() == Bot.Mode.Practice)
                {
                    deckListsUrl = "https://hsreplay.net/analytics/query/list_decks_by_win_rate_v2/?GameType=RANKED_STANDARD&TimeRange=LAST_30_DAYS&Region=ALL";
                    mulliganUrl = "https://hsreplay.net/api/v1/mulligan/?shortid=DECKIDHERE&game_type_filter=RANKED_STANDARD&league_rank_range=BRONZE_THROUGH_GOLD&player_initiative=ALL&time_range=LAST_30_DAYS";

                    mode = "RANKED_STANDARD";
                    AddLog("Mode : "+Bot.CurrentMode().ToString());
                }
                else/* if (Bot.CurrentMode() == Bot.Mode.Wild || Bot.CurrentMode() == Bot.Mode.Twist)*/
                {
                    deckListsUrl = "https://hsreplay.net/analytics/query/list_decks_by_win_rate_v2/?GameType=RANKED_WILD&TimeRange=LAST_30_DAYS&Region=ALL";
					mulliganUrl = "https://hsreplay.net/api/v1/mulligan/?shortid=DECKIDHERE&game_type_filter=RANKED_WILD&league_rank_range=BRONZE_THROUGH_GOLD&player_initiative=ALL&time_range=LAST_30_DAYS";
                    mode = "RANKED_WILD";
                    AddLog("Mode : "+Bot.CurrentMode().ToString());
                }
               /* else
                {
                     // 通过切换到默认留牌来处理不支持的模式
                    //throw new Exception("Unsupported game mode. Switching to Default Mulligan.");
                }
                */
                // 从 HSReplay 获取牌库列表数据
                var deckListsSrc = HttpGet.Get(deckListsUrl);
                if (string.IsNullOrEmpty(deckListsSrc))
                {
                    // 处理空或无效响应，切换到默认留牌
                    throw new Exception("Failed to get deck lists from HSReplay. Switching to Default Mulligan.");
                }

                // 反序列化牌库列表数据
                Root deckListsDatasJson = JsonConvert.DeserializeObject<Root>(deckListsSrc);
                if (deckListsDatasJson == null || deckListsDatasJson.series == null || deckListsDatasJson.series.data == null)
                {
                    // 处理无效 JSON 数据，切换到默认留牌
                    throw new Exception("Failed to parse deck lists data from HSReplay. Switching to Default Mulligan.");
                }
                AddLog("Selected Deck : " +Bot.CurrentDeck().Name.ToString());
                AddLog("Archetype : " +GetCurrentHSReplayArchetype().ToString());
                var Data = deckListsDatasJson.series.data;
                var deckId = HSReplayHelper.GetDeckIdForClosestMatch(Bot.CurrentDeck(), Data);
                AddLog("Matched Deck :\nhttps://hsreplay.net/decks/" + deckId + "/#gameType=" + mode);

                // 从 HSReplay 获取留牌数据
                var mulliganSrc = HttpGet.Get(mulliganUrl.Replace("DECKIDHERE", deckId));
                if (string.IsNullOrEmpty(mulliganSrc))
                {
                    // 处理空或无效响应，切换到默认留牌
                    throw new Exception("Failed to get mulligan data from HSReplay. Switching to Default Mulligan.");
                }

                // 反序列化留牌数据
                MRoot mulliganDataJson = JsonConvert.DeserializeObject<MRoot>(mulliganSrc);
                if (mulliganDataJson == null || mulliganDataJson.ALL == null)
                {
                    // 处理无效 JSON 数据，切换到默认留牌
                    throw new Exception("Failed to parse mulligan data from HSReplay. Switching to Default Mulligan.");
                }

                // 从 HSReplay 获取留牌数据并存储在 mulliganData 中
                var mulliganData = mulliganDataJson;

                // 记录留牌条目数量用于调试或信息输出
                AddLog("MulliganEntries : " + mulliganData.ALL.cards_by_dbf_id.Count);

                // 使用 HSReplayHelper 类清洗留牌数据
                var sanitizedDatas = HSReplayHelper.SanitizeMulliganDatas(mulliganData.ALL);

                // 按保留百分比降序排序清洗后的留牌数据
                var sortedDatas = sanitizedDatas.OrderByDescending(d => d.KeptPercentage);

                // 遍历排序后的数据并记录卡牌名称和保留百分比
                AddLog("\nFull Mulligan Data:");
                foreach (var d in sortedDatas)
                {
                    AddLog(CardTemplate.LoadFromId(d.CardId).Name + " - Kept : " + d.KeptPercentage.ToString("0.00") + "%");
                }

                // 根据选择列表记录可供选择的卡牌
                AddLog("\nCards to choose from:");
                foreach (var d in sortedDatas.Where(d => choices.Contains(d.CardId)))
                {
                    AddLog(CardTemplate.LoadFromId(d.CardId).Name + " - Kept : " + d.KeptPercentage.ToString("0.00") + "%");
                }

                // 如果保留百分比大于等于 75，记录要保留的卡牌
                AddLog("\nCards to Keep:");
                foreach (var d in sortedDatas.Where(d => choices.Contains(d.CardId) && d.KeptPercentage >= 75))
                {
                    // 检查选择列表中是否找到高保留率卡牌的标志
                    bool highKeptCardCostsOne = false;
                    bool highKeptCardFound = false;
                    bool highKeptCardPlayableOnOne = false;
                    if (choices.Contains(d.CardId))
                    {
                        highKeptCardFound = true;
                    }
                    if (choices.Contains(d.CardId) && CardTemplate.LoadFromId(d.CardId).Cost == 1)
                    {
                        highKeptCardCostsOne = true;
                    }
                    if (choices.Contains(d.CardId) && ((CardTemplate.LoadFromId(d.CardId).Cost == 1) || (CardTemplate.LoadFromId(d.CardId).Cost == 2 && choices.Contains(Card.Cards.GAME_005))))
                    {
                        highKeptCardPlayableOnOne = true;
                    }

                    // 如果卡牌在选择列表中且未被标记为保留，加入保留列表并记录
                    if (choices.Contains(d.CardId) && !CardsToKeep.Contains(d.CardId))
                    {
                        AddLog(CardTemplate.LoadFromId(d.CardId).Name + " - Kept : " + d.KeptPercentage.ToString("0.00") + "%");
                        Keep(d.CardId);
                    }

                    // 如果找到高保留率卡牌，遍历保留百分比在50-75之间的其他卡牌，并加入保留列表
                    if (highKeptCardFound == true && highKeptCardPlayableOnOne == true)
                    {
                        foreach (var e in sortedDatas.Where(e => choices.Contains(e.CardId) && e.KeptPercentage >= 50 && e.KeptPercentage < 75 && !CardsToKeep.Contains(e.CardId)))
                        {
                            AddLog(CardTemplate.LoadFromId(e.CardId).Name + " - Kept : " + e.KeptPercentage.ToString("0.00") + "%");
                            Keep(e.CardId);
                        }
                    }
                }
                AddLog("");
                AddLog("----------Made by Olanga----------");
                AddLog("");
                Bot.Log(_log);
                return CardsToKeep;
            }
            catch (Exception ex)
            {
                // 处理异常或记录错误，切换到默认留牌
                Bot.Log("Error in HSReplayMulligan.HandleMulligan: " + ex.Message + " \nSwitching to Default Mulligan.");
                Bot.StopBot();
                Bot.ChangeMulligan("Default.cs");
                Thread.Sleep(1000);
                Bot.StartBot();
                
                // 返回 null 表示发生错误且无卡牌保留
                return null;
            }
            
        }

        private void Keep(Card.Cards id, string log = "")
        {
            CardsToKeep.Add(id);
            if (log != "")
                AddLog(log);
        }

        private void AddLog(string log)
        {
            _log += "\r\n" + log; //合并所有 AddLog，包含回车和换行
        }
                //获取当前本地原型


        //获取当前 HSReplay 原型
        private static string GetCurrentHSReplayArchetype()
        {
            var HSReplayArchetype = HSReplayArchetypesMatcher.DetectArchetype(Bot.GetSelectedDecks()[0].Cards, Bot.GetSelectedDecks()[0].Class, 30);
            if (HSReplayArchetype == null || HSReplayArchetype is HSReplayArchetypeError)
                return string.Empty; //未找到原型
            return HSReplayArchetype.Name;
        }
    }

    public class HSReplayHelper
    {
        private static string CardsRegex = "\\[(\\d*?),(.*?)\\]";

        // 清洗从 HSReplay 收到的留牌数据的方法
        public static List<HSReplayMulliganInfo> SanitizeMulliganDatas(MALL datas)
        {
            List<HSReplayMulliganInfo> ret = new List<HSReplayMulliganInfo>();

            // 遍历每个留牌数据条目并创建 HSReplayMulliganInfo 对象
            foreach (var d in datas.cards_by_dbf_id)
            {
                HSReplayMulliganInfo info = new HSReplayMulliganInfo();
                // 根据 CardTemplate.TemplateList 字典中的 dbf_id 查找对应的卡牌 ID
                info.CardId = CardTemplate.TemplateList.FirstOrDefault(x => x.Value.DbfId == d.dbf_id).Key;
                info.KeptPercentage = d.keep_percentage;
                info.MulliganWinrate = d.opening_hand_winrate;
                info.DrawnWinrate = d.winrate_when_drawn;
                info.PlayedWinrate = d.winrate_when_played;
                ret.Add(info);
            }

            return ret;
        }

        // 计算给定牌库列表与牌组的匹配分数的方法
        public static int GetMatchPoint(string decklist, Deck deck)
        {
            int ret = 0;
            // 使用正则表达式在牌库列表中查找 cardDbfId 和 cardCount
            var matches = Regex.Matches(decklist, CardsRegex);
            foreach (Match match in matches)
            {
                var cardDbfId = int.Parse(match.Groups[1].ToString());
                var cardCount = int.Parse(match.Groups[2].ToString());
                // 根据 CardTemplate.TemplateList 字典中的 dbf_id 查找对应的卡牌 ID
                var cardId = CardTemplate.TemplateList.FirstOrDefault(x => x.Value.DbfId == cardDbfId).Key;
                // 计算牌组中该卡牌的出现次数并相应更新匹配分数
                var cardCountInDeck = deck.Cards.Count(x => x == cardId.ToString());

                if (cardCountInDeck > 0)
                    ret++;
                if (cardCount == 2 && cardCountInDeck == 2)
                    ret++;
            }
            return ret;
        }

        // 获取数据中与给定牌组最接近匹配的牌组 ID 的方法
        // 遍历每个原型，计算匹配分数，并存储最佳匹配
        public static string GetDeckIdForClosestMatch(Deck deck, Data data)
        {
            string ret = string.Empty;
            List<ENTRY> entries = null;

            if (deck.Class == Card.CClass.DEATHKNIGHT)
                entries = data.DEATHKNIGHT;
            if (deck.Class == Card.CClass.DEMONHUNTER)
                entries = data.DEMONHUNTER;
            if (deck.Class == Card.CClass.DRUID)
                entries = data.DRUID;
            if (deck.Class == Card.CClass.HUNTER)
                entries = data.HUNTER;
            if (deck.Class == Card.CClass.MAGE)
                entries = data.MAGE;
            if (deck.Class == Card.CClass.PALADIN)
                entries = data.PALADIN;
            if (deck.Class == Card.CClass.PRIEST)
                entries = data.PRIEST;
            if (deck.Class == Card.CClass.ROGUE)
                entries = data.ROGUE;
            if (deck.Class == Card.CClass.SHAMAN)
                entries = data.SHAMAN;
            if (deck.Class == Card.CClass.WARLOCK)
                entries = data.WARLOCK;
            if (deck.Class == Card.CClass.WARRIOR)
                entries = data.WARRIOR;

            var bestMatchPoints = 0;
            var bestMatchGamesPlayed = 0;

            // 遍历数据中的每个原型条目
            foreach (var entry in entries)
            {
                // 计算当前原型条目与给定牌组的匹配分数
                var points = GetMatchPoint(entry.deck_list, deck);

                // 检查当前原型条目是否是目前最佳匹配
                if (points > bestMatchPoints)
                {
                    bestMatchGamesPlayed = entry.total_games;
                    bestMatchPoints = points;
                    ret = entry.deck_id;
                }
                // 如果匹配分数相同，选择游戏场次更多的
                else if (points == bestMatchPoints && bestMatchGamesPlayed < entry.total_games)
                {
                    bestMatchGamesPlayed = entry.total_games;
                    bestMatchPoints = points;
                    ret = entry.deck_id;
                }
            }

            // 最终返回最接近匹配的 deck_id
            return ret;
        }
    }
    // 表示特定卡牌留牌信息的类
    public class HSReplayMulliganInfo
    {
        public Card.Cards CardId;            // 卡牌 ID
        public double KeptPercentage;       // 留牌中保留该卡牌的百分比
        public double MulliganWinrate;      // 留牌中保留该卡牌时的胜率
        public double DrawnWinrate;         // 游戏中抽到该卡牌时的胜率
        public double PlayedWinrate;        // 游戏中打出该卡牌时的胜率
    }

    // 表示游戏中所有卡牌统计数据的类
    public class ALL
    {
        // 卡牌的各种留牌和游戏统计属性
        public int dbf_id { get; set; }
        public int times_presented_in_initial_cards { get; set; }
        public int times_kept { get; set; }
        public double keep_percentage { get; set; }
        public int times_in_opening_hand { get; set; }
        public double opening_hand_winrate { get; set; }
        public int times_card_drawn { get; set; }
        public double winrate_when_drawn { get; set; }
        public int times_card_played { get; set; }
        public double avg_turn_played_on { get; set; }
        public double avg_turns_in_hand { get; set; }
        public double winrate_when_played { get; set; }
    }

    // 表示游戏留牌阶段相关数据的类
    public class MulliganData
    {
        public List<ALL> ALL { get; set; } // 包含游戏中所有卡牌留牌统计数据的 ALL 条目列表
    }

    // 表示留牌数据元数据的类
    public class MulliganMetadata
    {
        public string earliest_date { get; set; }   // 留牌数据中的最早日期
        public string latest_date { get; set; }     // 留牌数据中的最晚日期
        public int total_games { get; set; }        // 留牌数据中的总游戏场次
        public double base_winrate { get; set; }    // 留牌数据的基础胜率
    }

    // 表示留牌数据根节点的类
    public class MulliganRoot
    {
        public MulliganMetadata metadata { get; set; }
        public ALL ALL { get; set; }
    }

    // 表示留牌数据系列的类
    public class MulliganSeries
    {
        public MulliganMetadata metadata { get; set; }   // 留牌数据的元数据
        public MulliganData data { get; set; }           // 留牌数据
    }


	  public class MALL
    {
        public DateTime deck { get; set; }
        public DateTime archetype { get; set; }
        public DateTime @class { get; set; }
        public string query_score_scope { get; set; }
        public List<MCardsByDbfId> cards_by_dbf_id { get; set; }
        public double base_winrate { get; set; }
        public int num_games { get; set; }
        public DateTime as_of { get; set; }
        public MMetadata metadata { get; set; }
    }

    public class MAsOf
    {
        public MALL ALL { get; set; }
        public MOPPONENTCLASS OPPONENT_CLASS { get; set; }
    }

    public class MCardsByDbfId
    {
        public int dbf_id { get; set; }
        public double opening_hand_winrate { get; set; }
        public double keep_percentage { get; set; }
        public double winrate_when_drawn { get; set; }
        public double winrate_when_played { get; set; }
        public double avg_turns_in_hand { get; set; }
        public double avg_turn_played_on { get; set; }
    }

    public class MMetadata
    {
        public bool cache_hit { get; set; }
        public MSelectedParams selected_params { get; set; }
        public MAsOf as_of { get; set; }
        public string key { get; set; }
        public int deck_games { get; set; }
        public double deck_winrate { get; set; }
        public int archetype_games { get; set; }
        public double archetype_winrate { get; set; }
        public int class_games { get; set; }
        public double class_winrate { get; set; }
    }

    public class MOPPONENTCLASS
    {
        public DateTime deck { get; set; }
        public DateTime archetype { get; set; }
        public DateTime @class { get; set; }
    }

    public class MRoot
    {
        public MMetadata metadata { get; set; }
        public MALL ALL { get; set; }
    }

    public class MSelectedParams
    {
        public string PlayerInitiative { get; set; }
        public string deck_id { get; set; }
        public string GameType { get; set; }
        public string LeagueRankRange { get; set; }
        public string TimeRange { get; set; }
        public string Region { get; set; }
    }





    // 表示游戏中所有牌组数据的类
    public class Data
    {
        // 包含游戏中每个职业牌组的列表
        public List<ENTRY> DEATHKNIGHT { get; set; }
        public List<ENTRY> DEMONHUNTER { get; set; }
        public List<ENTRY> DRUID { get; set; }
        public List<ENTRY> HUNTER { get; set; }
        public List<ENTRY> MAGE { get; set; }
        public List<ENTRY> PALADIN { get; set; }
        public List<ENTRY> PRIEST { get; set; }
        public List<ENTRY> ROGUE { get; set; }
        public List<ENTRY> SHAMAN { get; set; }
        public List<ENTRY> WARLOCK { get; set; }
        public List<ENTRY> WARRIOR { get; set; }
    }

    // 表示特定职业（死亡骑士、恶魔猎人等）数据的类
    // 每个类包含牌组、原型和游戏统计信息
    public class ENTRY
    {
        public string deck_id { get; set; }
        public string deck_list { get; set; }
        public string deck_sideboard { get; set; }
        public int archetype_id { get; set; }
        public string digest { get; set; }
        public int total_games { get; set; }
        public double win_rate { get; set; }
        public double avg_game_length_seconds { get; set; }
        public double avg_num_player_turns { get; set; }
    }

    public class Metadata
    {
        public ENTRY DEATHKNIGHT { get; set; }
        public ENTRY DEMONHUNTER { get; set; }
        public ENTRY DRUID { get; set; }
        public ENTRY HUNTER { get; set; }
        public ENTRY MAGE { get; set; }
        public ENTRY PALADIN { get; set; }
        public ENTRY PRIEST { get; set; }
        public ENTRY ROGUE { get; set; }
        public ENTRY SHAMAN { get; set; }
        public ENTRY WARLOCK { get; set; }
        public ENTRY WARRIOR { get; set; }
    }

    // 表示游戏数据根节点的类，包含元数据和每个职业的实际数据
    public class Root
    {
        public string render_as { get; set; }       // 根节点的渲染信息
        public Series series { get; set; }          // 游戏数据系列
        public DateTime as_of { get; set; }         // 游戏数据的日期
    }

    // 表示游戏数据系列的类
    public class Series
    {
        public Metadata metadata { get; set; }   // 游戏数据的元数据
        public Data data { get; set; }           // 游戏数据
    }
}