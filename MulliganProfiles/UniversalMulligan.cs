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

// Original designer of this plugin is Olanga and EE
// Last edit 22/March/2025 by EE

namespace SmartBot.Mulligan
{
    [Serializable]
    public class UniversalMulligan : MulliganProfile
    {
        // Main class implementing the mulligan logic, using HSReplay data for decision making.
        private static bool Kept = true;

        // StringBuilder used to accumulate the runtime log, converted to string at the end.
        private string log;

        // Cards selected to keep during mulligan.
        private readonly List<Card.Cards> CardsToKeep = new List<Card.Cards>();

        // Mulligan percentages loaded for the current context.
        private List<HSReplayHelper.HSReplayMulligan> mulliganCards;

        // Text builders used to present Winrate and candidate cards in the log.
        private readonly StringBuilder WinrateCardBuilder = new StringBuilder("\nCards to Keep:");
        private readonly StringBuilder choiceCardsBuilder = new StringBuilder("\nCards to choose from:");

        // Base directory where mulligan profiles are stored.
        private readonly string mulliganProfilesDirectory = Directory.GetCurrentDirectory() + @"\MulliganProfiles\";

        // Primary entry - decide which cards to keep given mulligan data and choices presented by the game.
        public List<Card.Cards> HandleMulligan(List<Card.Cards> choices, Card.CClass opponentClass, Card.CClass ownClass)
        {
            // On first run after deployment, perform cleanup of old files and read the Kept flag.
            ReadMulliganFlag();

            // Merged per-deck file that includes deck lists and per-card opening-hand winrates.
            string MulliganDeckFile = Path.Combine(mulliganProfilesDirectory, @"{MODE}\MulliganDecks.json");

            // Fallback generic mulligan table (class-based scores) for Arena / other modes or when per-deck data is insufficient.
            string mulliganFile = Path.Combine(mulliganProfilesDirectory, @"{MODE}\mulligan.json");

            string deckId = null;
            string jsonContents = null;
            string matchDeck = null;
            int overlap = 0;
            string mode = CurrentMode();

            // Initialize logging buffer.
            log = string.Format("===== Universal Mulligan V6.0, Card definition V{0} ===EE", CurrentVersion());

            try
            {
                string mulliganDeckPath = MulliganDeckFile.Replace("{MODE}", mode);
                string mulliganPath = mulliganFile.Replace("{MODE}", mode);
                if (!File.Exists(mulliganDeckPath) || !File.Exists(mulliganDeckPath))
                    throw new FileNotFoundException("MulliganDecks file not found", mulliganDeckPath);

                switch (mode)
                {
                    case "Standard":
                    case "Wild":
                        // For Constructed modes, attempt to use the merged per-deck file with deck lists and per-card winrates.
                        jsonContents = File.ReadAllText(mulliganDeckPath);

                        // Get the current deck's card list.
                        var myDeck = Bot.CurrentDeck().Cards.Distinct().Select(c => CardTemplate.LoadFromId(c).Id).ToList();

                        // Find deck_id that best matches current deck composition.
                        deckId = DeckId(jsonContents, myDeck, ownClass);

                        // Load mulligan data for the matched deck_id.
                        mulliganCards = HSReplayHelper.MulliganDecks(jsonContents, deckId, ownClass);

                        // Compute how many of the mulligan cards exist in the current deck (overlap).
                        overlap = mulliganCards.Count(m => myDeck.Contains(m.Key));

                        // Percentage of per-deck mulligan cards present in our deck.
                        double overlapPctOfMulligan = mulliganCards.Count > 0 ? 100.0 * overlap / mulliganCards.Count : 0.0;

                        // If per-deck data has low overlap, fall back to generic class-based mulligan table.
                        if (overlapPctOfMulligan < 20)
                        {
                            matchDeck = "Mulligan profiles from mulligan file, mode: " + mode;
                            var fallbackJson = File.ReadAllText(mulliganPath);
                            mulliganCards = HSReplayHelper.Mulligan(fallbackJson, ownClass);
                        }
                        else
                        {
                            matchDeck = deckId + " gameType = " + mode;
                        }
                        break;

                    case "Arena":
                        // Arena uses class-level mulligan scores.
                        matchDeck = "Mulligan profiles from mulligan file, mode: " + mode;
                        jsonContents = File.ReadAllText(mulliganPath);
                        mulliganCards = HSReplayHelper.Mulligan(jsonContents, ownClass);
                        break;

                    default:
                        // Other modes use the Wild class fallback file.
                        matchDeck = "Mulligan profiles from mulligan file, mode: " + mode;
                        var wildPath = mulliganFile.Replace("{MODE}", "Wild");
                        jsonContents = File.ReadAllText(wildPath);
                        mulliganCards = HSReplayHelper.Mulligan(jsonContents, ownClass);
                        break;
                }

                AddLog("Selected Deck: " + Bot.CurrentDeck().Name);
                AddLog("Archetype: " + GetCurrentHSReplayArchetype());
                AddLog("Matched Deck: " + matchDeck);
                AddLog("Matching Entries: " + overlap + "/" + mulliganCards.Count);
                AddLog("\nFull Mulligan Data:");

                // Decide which cards to keep using the loaded mulligan data.
                CardChoice(choices, mulliganCards);
            }
            catch (Exception ex)
            {
                // If anything fails, attempt to use the fallback class-based file in uppercase mode folder.
                try
                {
                    string fallbackPath = mulliganFile.Replace("{MODE}", mode.ToUpper());
                    if (File.Exists(fallbackPath))
                    {
                        var fallbackJson = File.ReadAllText(fallbackPath);
                        mulliganCards = HSReplayHelper.Mulligan(fallbackJson, ownClass);
                        AddLog("Error reading merged mulligan data: " + ex.Message + ". Using fallback file, " + mulliganCards.Count + " cards");
                        CardChoice(choices, mulliganCards);
                    }
                    else
                    {
                        AddLog("Critical error reading mulligan data: " + ex.Message + ". No fallback mulligan file found.");
                    }
                }
                catch (Exception inner)
                {
                    AddLog("Fallback attempt failed: " + inner.Message);
                }
            }

            // Append choice/Winrate summaries and flush the log.
            AddLog(choiceCardsBuilder + "\r\n" + WinrateCardBuilder);
            AddLog("===== End of mulligan ======");
            Bot.Log(log);

            return CardsToKeep;
        }

        // Determine the play mode string used to select profile files.
        private static string CurrentMode()
        {
            var mode = Bot.CurrentMode();

            if (mode == Bot.Mode.Practice || mode == Bot.Mode.Casual)
            {
                string key = Bot.CurrentDeck().Name.Substring(1, 1);
                return key == "S" ? "Standard" : "Wild";
            }

            if (mode == Bot.Mode.Arena || mode == Bot.Mode.ArenaAuto) return "Arena";
            if (mode == Bot.Mode.Standard) return "Standard";

            return "Wild";
        }

        // Choose which of the presented choice cards to keep based on mulligan statistics.
        private void CardChoice(List<Card.Cards> choices, List<HSReplayHelper.HSReplayMulligan> mulliganCards)
        {
            string keptWinrate = Kept ? "Kept" : "Winrate";
            int keptThreshold = Kept ? 75 : 63;

            // Use HashSet for fast lookups.
            var choiceSet = new HashSet<Card.Cards>(choices);
            var myDeckSet = new HashSet<Card.Cards>(Bot.CurrentDeck().Cards.Select(c => CardTemplate.LoadFromId(c).Id));

            double mulliganValue = 0;
            Card.Cards bestLowCost = Card.Cards.GAME_005;

            // Cache CardTemplate lookups for performance.
            var templateCache = new Dictionary<Card.Cards, CardTemplate>();

            foreach (var mulliganCard in mulliganCards.OrderByDescending(c => c.Value))
            {
                CardTemplate tmpl;
                if (!templateCache.TryGetValue(mulliganCard.Key, out tmpl))
                {
                    tmpl = CardTemplate.LoadFromId(mulliganCard.Key);
                    templateCache[mulliganCard.Key] = tmpl;
                }

                var cardName = tmpl?.Name ?? mulliganCard.Key.ToString();

                if (myDeckSet.Contains(mulliganCard.Key) || choiceSet.Contains(mulliganCard.Key))
                {
                    AddLog(string.Format("{0} - {1} : {2}%", cardName, keptWinrate, mulliganCard.Value.ToString("0.00")));
                }

                if (choiceSet.Contains(mulliganCard.Key))
                {
                    // Auto-keep very-high mulligan percentages.
                    if (mulliganCard.Value >= keptThreshold)
                    {
                        WinrateCardBuilder.AppendFormat("\n{0} - {1} : {2}%", cardName, keptWinrate, mulliganCard.Value.ToString("0.00"));
                        Keep(mulliganCard.Key);
                    }

                    // Track the best low-cost candidate.
                    if (mulliganCard.Value > mulliganValue && (tmpl?.Cost ?? int.MaxValue) < 4)
                    {
                        mulliganValue = mulliganCard.Value;
                        bestLowCost = mulliganCard.Key;
                    }

                    choiceCardsBuilder.AppendFormat("\n{0} - {1} : {2}%", cardName, keptWinrate, mulliganCard.Value.ToString("0.00"));
                }
            }

            // If nothing was auto-Winrate, choose the best low-cost card above a threshold.
            if (!CardsToKeep.Any() && mulliganValue > 49)
            {
                var card = CardTemplate.LoadFromId(bestLowCost);
                var name = card?.Name ?? bestLowCost.ToString();
                choiceCardsBuilder.AppendFormat("\n\nSelecting best, low cost card:\n{0} - {1} : {2}%", name, keptWinrate, mulliganValue.ToString("0.00"));
                WinrateCardBuilder.AppendFormat("\n{0} - {1} : {2}%", name, keptWinrate, mulliganValue.ToString("0.00"));
                Keep(bestLowCost);
            }
        }

        // Add a card to the keep list.
        private void Keep(Card.Cards id)
        {
            CardsToKeep.Add(id);
        }

        // Append a message to the runtime log buffer.
        private void AddLog(string message)
        {
            log += "\r\n" + message;
        }

        // Try to detect the currently selected archetype name via the HSReplay matcher.
        private static string GetCurrentHSReplayArchetype()
        {
            var HSReplayArchetype = HSReplayArchetypesMatcher.DetectArchetype(Bot.GetSelectedDecks()[0].Cards, Bot.GetSelectedDecks()[0].Class, 30);
            if (HSReplayArchetype == null || HSReplayArchetype is HSReplayArchetypeError)
                return string.Empty;
            return HSReplayArchetype.Name;
        }

        // Helper types and deserialization helpers for HSReplay data.
        public class HSReplayHelper
        {
            private static readonly List<HSReplayMulligan> cards = new List<HSReplayMulligan>();

            // Extract per-deck opening-hand winrates from the merged DeckRoot JSON by deck_id.
            public static List<HSReplayMulligan> MulliganDecks(string contents, string deckId, Card.CClass ownClass)
            {
                cards.Clear();

                if (string.IsNullOrEmpty(deckId) || string.IsNullOrEmpty(contents))
                    return cards;

                var deckRoot = JsonConvert.DeserializeObject<DeckRoot>(contents);
                if (deckRoot == null) return cards;

                // Flatten all class lists into a single sequence and create a lookup by deck_id.
                var allDecks = new List<Deck>();
                allDecks.AddRange(deckRoot.ALL ?? Enumerable.Empty<Deck>());
                allDecks.AddRange(deckRoot.DEATHKNIGHT ?? Enumerable.Empty<Deck>());
                allDecks.AddRange(deckRoot.DEMONHUNTER ?? Enumerable.Empty<Deck>());
                allDecks.AddRange(deckRoot.DRUID ?? Enumerable.Empty<Deck>());
                allDecks.AddRange(deckRoot.HUNTER ?? Enumerable.Empty<Deck>());
                allDecks.AddRange(deckRoot.MAGE ?? Enumerable.Empty<Deck>());
                allDecks.AddRange(deckRoot.PALADIN ?? Enumerable.Empty<Deck>());
                allDecks.AddRange(deckRoot.PRIEST ?? Enumerable.Empty<Deck>());
                allDecks.AddRange(deckRoot.ROGUE ?? Enumerable.Empty<Deck>());
                allDecks.AddRange(deckRoot.SHAMAN ?? Enumerable.Empty<Deck>());
                allDecks.AddRange(deckRoot.WARLOCK ?? Enumerable.Empty<Deck>());
                allDecks.AddRange(deckRoot.WARRIOR ?? Enumerable.Empty<Deck>());

                var propertyInfo = typeof(DeckRoot).GetProperty(ownClass.ToString().Trim().ToUpper());
                var classDecks = propertyInfo?.GetValue(deckRoot) as List<Deck>;
                var foundDeck = classDecks.FirstOrDefault(d => string.Equals(d.deck_id, deckId, StringComparison.Ordinal));

                if (foundDeck == null || foundDeck.cards == null) return cards;

                foreach (var card in foundDeck.cards)
                {
                    var template = CardTemplate.TemplateList.FirstOrDefault(x => x.Value.Id.ToString() == card.card_id);
                    if (template.Value != null)
                    {
                        cards.Add(new HSReplayMulligan
                        {
                            Key = template.Key,
                            Value = Kept ? card.keep_percentage : card.opening_hand_winrate
                        });
                    }
                }

                return cards;
            }

            // Build a list of class-level mulligan scores from the class-based mulligan.json format.
            public static List<HSReplayMulligan> Mulligan(string jsonData, Card.CClass ownClass)
            {
                cards.Clear();

                var deckDataHSReplay = JsonConvert.DeserializeObject<CardRoot>(jsonData);
                if (deckDataHSReplay == null) return cards;

                var className = ownClass.ToString().ToUpper();
                var deckData = typeof(CardRoot).GetProperty(className)?.GetValue(deckDataHSReplay, null) as List<CardScore>;
                if (deckData == null) return cards;

                foreach (var entry in deckData)
                {
                    var templateKey = CardTemplate.TemplateList.FirstOrDefault(x => x.Value.Id.ToString() == entry.card_id).Key;
                    cards.Add(new HSReplayMulligan
                    {
                        Key = templateKey,
                        Value = entry.mulligan_score
                    });
                }

                return cards;
            }

            // Mulligan entry (card id + keep percentage).
            public class HSReplayMulligan
            {
                public Card.Cards Key;
                public double Value;
            }
        }

        // Convert a deck JSON string into match points against the current deck.
        public static int GetMatchPoint(string deckList, List<Card.Cards> deck)
        {
            const string pattern = @"\[(?<Id>\d+),(?<count>\d+)]";
            int count = 0;

            foreach (Match match in Regex.Matches(deckList ?? string.Empty, pattern))
            {
                int cardDbfId = int.Parse(match.Groups["Id"].Value);
                int cardCount = int.Parse(match.Groups["count"].Value);

                var cardId = CardTemplate.TemplateList.FirstOrDefault(x => x.Value.DbfId == cardDbfId).Key;
                int cardCountInDeck = deck.Count(c => c == cardId);

                count += (cardCountInDeck > 0 ? 1 : 0);
                count += (cardCountInDeck == 2 && cardCount == 2 ? 1 : 0);
            }
            return count;
        }

        // Find the best matching deck_id for the provided deck using DeckRoot JSON.
        public static string DeckId(string jsonData, List<Card.Cards> deck, Card.CClass ownClass)
        {
            if (string.IsNullOrEmpty(jsonData)) return null;

            var deckRoot = JsonConvert.DeserializeObject<DeckRoot>(jsonData);
            if (deckRoot == null) return null;

            var propertyInfo = typeof(DeckRoot).GetProperty(ownClass.ToString().Trim().ToUpper());
            if (propertyInfo == null) return null;

            var deck_IDs = propertyInfo.GetValue(deckRoot) as List<Deck>;
            if (deck_IDs == null) return null;

            string bestId = null;
            int bestPoints = 0, bestGames = 0;

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

        // Read plugin version information from a sidecar file; null if not present.
        private string CurrentVersion()
        {
            string discoverCCDirectory = Path.Combine(Directory.GetCurrentDirectory(), "DiscoverCC");
            string infoPath = Path.Combine(discoverCCDirectory, "EE_Information.txt");

            if (!File.Exists(infoPath))
                return null;

            string firstLine = File.ReadLines(infoPath).First();
            var match = Regex.Match(firstLine, @"\((.+?)\)");
            return match.Success ? match.Groups[1].Value : null;
        }

        // Delete specific files once, using a flag file to ensure it only happens on the first run after deployment.
        private void ReadMulliganFlag()
        {
            string flagFile = Path.Combine(mulliganProfilesDirectory, "MulliganFlag.json");
            bool cleanupDone = false;

            // Ensure file exists with default values
            if (File.Exists(flagFile))
            {
                try
                {
                    var lines = File.ReadAllLines(flagFile);

                    if (lines.Length > 0)
                        bool.TryParse(lines[0], out Kept);

                    if (lines.Length > 1)
                        bool.TryParse(lines[1], out cleanupDone);
                }
                catch
                {
                    // If file is unreadable, treat as "cleanup not done"
                }
            }
            else
            {
                // Create flag file with default values
                File.WriteAllLines(flagFile, new[] { "true", "true" });
            }

            // If cleanup already done, nothing else to do
            if (cleanupDone)
                return;

            // --- Perform one-time cleanup ---
            string[] filesToDelete =
            {
                "DeckId.json",
                "MulliganAllDecks.json"
            };

            foreach (var folder in new[] { "Standard", "Wild" })
            {
                string path = Path.Combine(mulliganProfilesDirectory, folder);
                if (!Directory.Exists(path)) continue;

                foreach (var file in filesToDelete)
                {
                    try { File.Delete(Path.Combine(path, file)); }
                    catch { /* ignore delete errors */ }
                }
            }
        }

        // *** Newtonsoft.Json model***
        // ============================================================
        // HSReplay Mulligan Score Models
        // Used for deserializing class‑specific mulligan score data
        // 
        // Data models used for deserializing class-level mulligan tables.
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
        // HSReplay Opening Hand Winrate Model
        // Used for deserializing single‑card opening hand winrate data
        // ============================================================
        // Model for per-card opening hand winrate found in merged per-deck files.
        public class MulliganRoot
        {
            public string card_id { get; set; }
            public double opening_hand_winrate { get; set; }
            public double keep_percentage { get; set; }
        }

        // Per-deck metadata including optional per-card mulligan stats.
        public class Deck
        {
            public string deck_id { get; set; }
            public string deck_list { get; set; }
            public int total_games { get; set; }
            public double win_rate { get; set; }
            public List<MulliganRoot> cards { get; set; }
        }

        // Top-level model for merged per-deck file with arrays per class.
        public class DeckRoot
        {
            public List<Deck> ALL { get; set; }
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
