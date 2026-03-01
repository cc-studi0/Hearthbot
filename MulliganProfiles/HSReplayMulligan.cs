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
        // Initialize the log and list to store cards to keep
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

                // Determine the deck list and mulligan URLs based on the game mode
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
                    // Handle unsupported modes by switching to Default Mulligan
                    //throw new Exception("Unsupported game mode. Switching to Default Mulligan.");
                }
                */
                // Fetch deck lists data from HSReplay
                var deckListsSrc = HttpGet.Get(deckListsUrl);
                if (string.IsNullOrEmpty(deckListsSrc))
                {
                    // Handle empty or invalid response by switching to Default Mulligan
                    throw new Exception("Failed to get deck lists from HSReplay. Switching to Default Mulligan.");
                }

                // Deserialize deck lists data
                Root deckListsDatasJson = JsonConvert.DeserializeObject<Root>(deckListsSrc);
                if (deckListsDatasJson == null || deckListsDatasJson.series == null || deckListsDatasJson.series.data == null)
                {
                    // Handle invalid JSON data by switching to Default Mulligan
                    throw new Exception("Failed to parse deck lists data from HSReplay. Switching to Default Mulligan.");
                }
                AddLog("Selected Deck : " +Bot.CurrentDeck().Name.ToString());
                AddLog("Archetype : " +GetCurrentHSReplayArchetype().ToString());
                var Data = deckListsDatasJson.series.data;
                var deckId = HSReplayHelper.GetDeckIdForClosestMatch(Bot.CurrentDeck(), Data);
                AddLog("Matched Deck :\nhttps://hsreplay.net/decks/" + deckId + "/#gameType=" + mode);

                // Fetch mulligan data from HSReplay
                var mulliganSrc = HttpGet.Get(mulliganUrl.Replace("DECKIDHERE", deckId));
                if (string.IsNullOrEmpty(mulliganSrc))
                {
                    // Handle empty or invalid response by switching to Default Mulligan
                    throw new Exception("Failed to get mulligan data from HSReplay. Switching to Default Mulligan.");
                }

                // Deserialize mulligan data
                MRoot mulliganDataJson = JsonConvert.DeserializeObject<MRoot>(mulliganSrc);
                if (mulliganDataJson == null || mulliganDataJson.ALL == null)
                {
                    // Handle invalid JSON data by switching to Default Mulligan
                    throw new Exception("Failed to parse mulligan data from HSReplay. Switching to Default Mulligan.");
                }

                // Fetch mulligan data from HSReplay and store it in mulliganData
                var mulliganData = mulliganDataJson;

                // Log the number of mulligan entries for debugging or information purposes
                AddLog("MulliganEntries : " + mulliganData.ALL.cards_by_dbf_id.Count);

                // Sanitize the mulligan data using the HSReplayHelper class
                var sanitizedDatas = HSReplayHelper.SanitizeMulliganDatas(mulliganData.ALL);

                // Sort the sanitized mulligan data in descending order based on the kept percentage
                var sortedDatas = sanitizedDatas.OrderByDescending(d => d.KeptPercentage);

                // Iterate through the sorted data and log the card names and their kept percentages
                AddLog("\nFull Mulligan Data:");
                foreach (var d in sortedDatas)
                {
                    AddLog(CardTemplate.LoadFromId(d.CardId).Name + " - Kept : " + d.KeptPercentage.ToString("0.00") + "%");
                }

                // Log the cards available to choose from, based on the choices list
                AddLog("\nCards to choose from:");
                foreach (var d in sortedDatas.Where(d => choices.Contains(d.CardId)))
                {
                    AddLog(CardTemplate.LoadFromId(d.CardId).Name + " - Kept : " + d.KeptPercentage.ToString("0.00") + "%");
                }

                // Log the cards to keep if their kept percentage is greater than or equal to 75
                AddLog("\nCards to Keep:");
                foreach (var d in sortedDatas.Where(d => choices.Contains(d.CardId) && d.KeptPercentage >= 75))
                {
                    // Flag to check if a high-kept card is found in the choices list
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

                    // If the card is in the choices list and not already marked to keep, add it to CardsToKeep list and log it
                    if (choices.Contains(d.CardId) && !CardsToKeep.Contains(d.CardId))
                    {
                        AddLog(CardTemplate.LoadFromId(d.CardId).Name + " - Kept : " + d.KeptPercentage.ToString("0.00") + "%");
                        Keep(d.CardId);
                    }

                    // If a high-kept card is found, iterate through other cards in the sorted data with kept percentage between 50 and 75, and add them to CardsToKeep list
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
                // Handle the exception or log the error and switch to Default Mulligan
                Bot.Log("Error in HSReplayMulligan.HandleMulligan: " + ex.Message + " \nSwitching to Default Mulligan.");
                Bot.StopBot();
                Bot.ChangeMulligan("Default.cs");
                Thread.Sleep(1000);
                Bot.StartBot();
                
                // Return null to indicate an error occurred and no cards to keep
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
            _log += "\r\n" + log; //Combine all Addlog, includes return and new line
        }
                //Get Current Local Archetype


        //Get Current HSReplay Archetype
        private static string GetCurrentHSReplayArchetype()
        {
            var HSReplayArchetype = HSReplayArchetypesMatcher.DetectArchetype(Bot.GetSelectedDecks()[0].Cards, Bot.GetSelectedDecks()[0].Class, 30);
            if (HSReplayArchetype == null || HSReplayArchetype is HSReplayArchetypeError)
                return string.Empty; //Archetype couldn't be found
            return HSReplayArchetype.Name;
        }
    }

    public class HSReplayHelper
    {
        private static string CardsRegex = "\\[(\\d*?),(.*?)\\]";

        // Method to sanitize the mulligan data received from HSReplay
        public static List<HSReplayMulliganInfo> SanitizeMulliganDatas(MALL datas)
        {
            List<HSReplayMulliganInfo> ret = new List<HSReplayMulliganInfo>();

            // Iterate through each mulligan data entry and create an HSReplayMulliganInfo object
            foreach (var d in datas.cards_by_dbf_id)
            {
                HSReplayMulliganInfo info = new HSReplayMulliganInfo();
                // Find the corresponding card ID based on the dbf_id in the CardTemplate.TemplateList dictionary
                info.CardId = CardTemplate.TemplateList.FirstOrDefault(x => x.Value.DbfId == d.dbf_id).Key;
                info.KeptPercentage = d.keep_percentage;
                info.MulliganWinrate = d.opening_hand_winrate;
                info.DrawnWinrate = d.winrate_when_drawn;
                info.PlayedWinrate = d.winrate_when_played;
                ret.Add(info);
            }

            return ret;
        }

        // Method to calculate the match points for a given decklist and deck
        public static int GetMatchPoint(string decklist, Deck deck)
        {
            int ret = 0;
            // Use regular expression to find the cardDbfId and cardCount in the decklist
            var matches = Regex.Matches(decklist, CardsRegex);
            foreach (Match match in matches)
            {
                var cardDbfId = int.Parse(match.Groups[1].ToString());
                var cardCount = int.Parse(match.Groups[2].ToString());
                // Find the corresponding card ID based on the dbf_id in the CardTemplate.TemplateList dictionary
                var cardId = CardTemplate.TemplateList.FirstOrDefault(x => x.Value.DbfId == cardDbfId).Key;
                // Count the number of occurrences of the card in the deck and update match points accordingly
                var cardCountInDeck = deck.Cards.Count(x => x == cardId.ToString());

                if (cardCountInDeck > 0)
                    ret++;
                if (cardCount == 2 && cardCountInDeck == 2)
                    ret++;
            }
            return ret;
        }

        // Method to get the deck ID for the closest match to the given deck in the data
        // It iterates through each archetype, calculates match points, and stores the best match
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

            // Iterate through each archetype entry in the data
            foreach (var entry in entries)
            {
                // Calculate match points for the current archetype entry and the given deck
                var points = GetMatchPoint(entry.deck_list, deck);

                // Check if the current archetype entry is the best match so far based on match points
                if (points > bestMatchPoints)
                {
                    bestMatchGamesPlayed = entry.total_games;
                    bestMatchPoints = points;
                    ret = entry.deck_id;
                }
                // If there's a tie in match points, choose the one with more games played
                else if (points == bestMatchPoints && bestMatchGamesPlayed < entry.total_games)
                {
                    bestMatchGamesPlayed = entry.total_games;
                    bestMatchPoints = points;
                    ret = entry.deck_id;
                }
            }

            // Finally, return the deck_id of the closest match
            return ret;
        }
    }
    // Class representing the mulligan information for a specific card
    public class HSReplayMulliganInfo
    {
        public Card.Cards CardId;            // The ID of the card
        public double KeptPercentage;       // Percentage of times the card is kept in the mulligan
        public double MulliganWinrate;      // Winrate when the card is kept in the mulligan
        public double DrawnWinrate;         // Winrate when the card is drawn during the game
        public double PlayedWinrate;        // Winrate when the card is played during the game
    }

    // Class representing the data for all card statistics in the game
    public class ALL
    {
        // Properties for various mulligan and gameplay statistics for a card
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

    // Class representing data related to the mulligan phase of a game
    public class MulliganData
    {
        public List<ALL> ALL { get; set; } // List of ALL entries containing mulligan statistics for all cards in the game
    }

    // Class representing metadata for the mulligan data
    public class MulliganMetadata
    {
        public string earliest_date { get; set; }   // Earliest date in the mulligan data
        public string latest_date { get; set; }     // Latest date in the mulligan data
        public int total_games { get; set; }        // Total number of games in the mulligan data
        public double base_winrate { get; set; }    // Base winrate for the mulligan data
    }

    // Class representing the root of the mulligan data
    public class MulliganRoot
    {
        public MulliganMetadata metadata { get; set; }
        public ALL ALL { get; set; }
    }

    // Class representing the mulligan data series
    public class MulliganSeries
    {
        public MulliganMetadata metadata { get; set; }   // Metadata for the mulligan data
        public MulliganData data { get; set; }           // Mulligan data
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





    // Class representing the data for all decks in the game
    public class Data
    {
        // Lists containing decks for each class in the game
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

    // Classes representing data for specific classes (DEATHKNIGHT, DEMONHUNTER, etc.) in the game
    // Each class contains information about the deck, archetype, and gameplay statistics
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

    // Class representing the root of the game data, which includes metadata and the actual data for each class
    public class Root
    {
        public string render_as { get; set; }       // Rendering information for the root
        public Series series { get; set; }          // Game data series
        public DateTime as_of { get; set; }         // Date of the game data
    }

    // Class representing the game data series
    public class Series
    {
        public Metadata metadata { get; set; }   // Metadata for the game data
        public Data data { get; set; }           // Game data
    }
}