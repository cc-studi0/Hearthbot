using SmartBot.Database;
using SmartBot.Discover;
using SmartBot.Plugins.API;
using SmartBotAPI.Plugins.API.HSReplayArchetypes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

// by Evil_Eyes
// 兼容修复：SmartBot 的 Discover 脚本有时以“单文件”方式编译，脚本之间不能互相引用。
// 因此这里提供一个完全自包含的 DiscoverPickHandler 实现，只做最保守兜底选择，保证编译通过。

namespace UniversalDiscover
{
    public class UniversalDiscover : DiscoverPickHandler
    {
        // List of card for origin correction on opponent board
        private static List<Card.Cards> enemyCards = new List<Card.Cards>
        {
            Card.Cards.REV_000, // Suspicious Alchemist
            Card.Cards.REV_002, // Suspicious Usher
            Card.Cards.REV_006, // Suspicious Pirate
            Card.Cards.NX2_044, // Suspicious Peddler
            Card.Cards.MIS_916 // Pro Gamer, Challenge your opponent to a game of Rock-Paper-Scissors! The winner draws 2 cards.
        };

        // List of cards for origin correction on friendly board
        private static List<Card.Cards> friendCards = new List<Card.Cards>
        {
            Card.Cards.GDB_874, // Astrobiologist
            Card.Cards.TTN_429, // Aman'Thul
            Card.Cards.TOY_801, // Chia Drake Miniaturize
            Card.Cards.TOY_801t, // Chia Drake Mini
            Card.Cards.BG31_BOB, // Bob the Bartender
            Card.Cards.WON_103 // Chamber of Viscidus
        };

        // List of totems: Mistake, Stereo Totem, The One-Amalgam Band, Ancient Totem, Jukebox Totem, Anchored Totem, Flametongue Totem, Flametongue Totem, Amalgam of the Deep, Totem Golem, Gigantotem, Sinstone Totem, Party Favor Totem, Party Favor Totem, Wrath of Air Totem, Searing Totem, Healing Totem, Stoneclaw Totem, Strength Totem, Grand Totem Eys'or -> Set Madness At The Darkmoon Faire, Grand Totem Eys'or -> Set Unknown, Treant Totem, Trick Totem, Totem Goliath, EVIL Totem, Serpent Ward, Primalfin Totem, Vitality Totem, Mana Tide Totem
        private static readonly HashSet<Card.Cards> totemCards = new HashSet<Card.Cards>
        {
            Card.Cards.NX2_050, Card.Cards.ETC_105, Card.Cards.ETC_409, Card.Cards.TTN_710, Card.Cards.JAM_010,
            Card.Cards.TSC_922, Card.Cards.EX1_565, Card.Cards.CORE_EX1_565, Card.Cards.TSC_069, Card.Cards.AT_052,
            Card.Cards.REV_838, Card.Cards.REV_839, Card.Cards.REV_935, Card.Cards.REV_935t, Card.Cards.CS2_052,
            Card.Cards.CS2_050, Card.Cards.NEW1_009, Card.Cards.CS2_051, Card.Cards.CS2_058, Card.Cards.DMF_709,
            Card.Cards.CORE_DMF_709, Card.Cards.SCH_612t, Card.Cards.SCH_537, Card.Cards.SCH_615, Card.Cards.ULD_276,
            Card.Cards.TRL_057, Card.Cards.UNG_201, Card.Cards.GVG_039, Card.Cards.CORE_EX1_575
        };

        // Rapter Herald, Rewind Battlecry: Discover a Beast with a Dark Gift. 
        private static readonly Dictionary<Card.Cards, int> raptorHeraldCards = new Dictionary<Card.Cards, int>
        {
            { Card.Cards.EDR_100t13, 100 }, // Harpy's Talons
            { Card.Cards.EDR_100t9,   90 }, // Persisting Horror
            { Card.Cards.EDR_100t1,   80 }, // Well Rested
            { Card.Cards.EDR_100t5,   70 }, // Living Nightmare
            { Card.Cards.EDR_100t,    60 }, // Waking Terror
            { Card.Cards.EDR_100t3,   50 }, // Bundled Up
            { Card.Cards.EDR_100t7,   40 }, // Rude Awakening
            { Card.Cards.EDR_100t2,   30 }, // Short Claws
            { Card.Cards.EDR_100t6,   20 }, // Sleepwalker
            { Card.Cards.EDR_100t8,   10 } // Sweet Dreams
        };

        // Dark Rider: Battlecry: If you're holding a Dragon, Discover a Dragon with a Dark Gift.
        private static readonly Dictionary<Card.Cards, int> darkRider = new Dictionary<Card.Cards, int>
        {
            { Card.Cards.EDR_100t,   60 }, // Waking Terror
            { Card.Cards.EDR_100t1,  80 }, // Well Rested
            { Card.Cards.EDR_100t2,  30 }, // Short Claws
            { Card.Cards.EDR_100t3,  50 }, // Bundled Up
            { Card.Cards.EDR_100t5,  70 }, // Living Nightmare
            { Card.Cards.EDR_100t6,  20 }, // Sleepwalker
            { Card.Cards.EDR_100t7,  40 }, // Rude Awakening
            { Card.Cards.EDR_100t8,  10 }, // Sweet Dreams
            { Card.Cards.EDR_100t9,  90 }, // Persisting Horror
            { Card.Cards.EDR_100t13, 100 } // Harpy's Talons
        };

        // Global variables declaration
        private static readonly Random random = new Random();
        private static readonly Dictionary<string, string> giteeContentCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>> iniCache = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);
        private static bool versionChecked, discard;
        private StringBuilder logBuilder = new StringBuilder();
        private Dictionary<string, Dictionary<string, string>> iniTierList;
        private string description;
        private const string owner = "xnyld";
        private const string repo = "smartbot";


        // Card Handle Pick Decision from SB
        public Card.Cards HandlePickDecision(Card.Cards originCard, List<Card.Cards> choices, Board board) // originCard; ID of card played by SB: choices; names of cards for selection: board; 3 states , Even, Losing, Winning
        {
            // Starting index
            int startIndex = 0;

            // Retrieve latest version of card definition library from cloud
            if (!versionChecked)
            {
                // remote-only project, nothing to update locally
                versionChecked = true;
            }

            // Bot logs heading
            logBuilder.Clear();
            logBuilder.Append("=====Discover V9.31, Card definition V").Append(CurrentVersion()).Append("===EE");
            string Divider = new string('=', 40);

            // Final random choice if no cards found
            Card.Cards bestChoice = choices[random.Next(0, choices.Count)];

            // Get current deck mode
            string mode = CurrentMode();

            // Get current hero class
            string hero = board.FriendClass.ToString();

            // Origin card check and correction
            (originCard, startIndex) = OriginCardCorrection(originCard, choices, board, mode);

            // Origin card name from database template
            string Origin_Card = CardTemplate.LoadFromId(originCard).Name;

            // Create empty card list
            List<CardValue> choicesCardValue = new List<CardValue>();

            // Main loop starts here
            double points = 0;
            double TotalPoints = 0;
            string filePath = null;
            try
            {
                for (int choiceIndex = startIndex; choiceIndex < 3; choiceIndex++)
                {
                    string discoverFile = string.Empty;
                    // Input file selection
                    switch (choiceIndex)
                    {
                        // Check for card specific file, then fallback to discover.ini if exists
                        case 1:
                            filePath = $"smartbot/DiscoverCC/{mode}/{originCard}.ini";
                            iniTierList = LoadIniData(filePath);
                            description = $"From: gitee.com/xnyld/smartbot/{mode}/{Origin_Card}";
                            break;
                        case 2:
                            filePath = $"smartbot/DiscoverCC/{mode}/discover.ini";
                            iniTierList = LoadIniData(filePath);
                            description = $"Origin: {Origin_Card}\nFrom: gitee.com/xnyld/smartbot/{mode}/discover.ini";
                            break;
                    }

                    // Clear previous card values
                    choicesCardValue.Clear();
                    points = 0; // ensure points reset per choice
                    discard = false;

                    // Search for best points
                    foreach (var choice in choices)
                    {
                        var cardTemplate = CardTemplate.LoadFromId(choice); // Using SB database to get details of card
                        points = 0; // ensure points reset per choice
                        switch (choiceIndex)
                        {
                            case 0:
                                // *** Check for any special conditions ***
                                switch (originCard)
                                {
                                    case Card.Cards.WON_103: // Chamber of Viscidus
                                        if (!discard)
                                        {
                                            filePath = $"smartbot/DiscoverCC/{mode}/WON_103.ini";
                                            iniTierList = LoadIniData(filePath);
                                            discard = true;
                                        }
                                        // Searching file "origin.ini" for best points
                                        double.TryParse(GetIniValue(iniTierList, choice.ToString(), "points", "0"), NumberStyles.Any, CultureInfo.InvariantCulture, out double p);
                                        points = 200 - p; // Invert points for Chamber of Viscidus, as we want to discard cards with higher points
                                        description = "From: Chamber of Viscidus";
                                        break;
                                    case Card.Cards.MIS_102: // Return Policy
                                                             // Colifero DH gate to avoid Blob when Tusk/Felhunter offered
                                        var signature = new[] { Card.Cards.VAC_926, Card.Cards.TLC_468, Card.Cards.EDR_891, Card.Cards.TOY_703 }; // Cliff Dive, Blob of Tar, Ravenous Felhunter, Colifero
                                        bool isDemonHunter = board.FriendClass.ToString().Equals("DemonHunter", StringComparison.OrdinalIgnoreCase);
                                        bool hasSignature = board.Hand.Any(c => signature.Contains(c.Template.Id)) || board.Deck.Any(signature.Contains);

                                        if (!isDemonHunter || !hasSignature) break;

                                        bool tuskOffered = choices.Contains(Card.Cards.BAR_330) || choices.Contains(Card.Cards.CORE_BAR_330); // Tuskarr Fisherman
                                        bool felOffered = choices.Contains(Card.Cards.EDR_891); // Ravenous Felhunter
                                                                                                // Evaluate choice cards
                                        if (tuskOffered || felOffered)
                                        {
                                            if (choice == Card.Cards.TLC_468) // Blob of Tar
                                                points = 0;
                                            else if (choice == Card.Cards.BAR_330 || choice == Card.Cards.CORE_BAR_330) // Tuskarr Fisherman
                                                points = 90;
                                            else if (choice == Card.Cards.EDR_891) // Ravenous Felhunter
                                                points = 100;
                                            else
                                                points = 10;

                                            description = "From: Return Policy";
                                        }
                                        break;
                                    case Card.Cards.CORE_EDR_004: // Raptor Herald, evaluate choice cards based on predefined points
                                        points = raptorHeraldCards.TryGetValue(choice, out var val) ? val : points;
                                        description = "From: Raptor Herald";
                                        break;
                                    case Card.Cards.EDR_456: // Dark Rider, evaluate choice cards based on predefined points
                                        points = darkRider.TryGetValue(choice, out var val2) ? val2 : points;
                                        description = "From: Dark Rider";
                                        break;
                                    case Card.Cards.DEEP_027: // Gloomstone Guardian
                                        double discardScore = 2 * Math.Max(board.MinionFriend.Count - 2, 0) + 3 * Math.Max(board.ManaAvailable, 0);
                                        double manaLossScore = 2 * Math.Max(board.MinionFriend.Count, 0) + 3 * Math.Max(board.ManaAvailable, 0);
                                        // Evaluate choice cards
                                        if (choice == Card.Cards.DEEP_027a) // Splintered Form, Discard 2 cards.
                                        {
                                            if (discardScore >= manaLossScore)
                                                points = 100; // Strong reward for discard
                                            else
                                                points = 50;  // Lesser reward for mana loss
                                        }
                                        else if (choice == Card.Cards.DEEP_027b) // Mana Disintegration, Destroy one of your Mana Crystals.
                                        {
                                            points = 75; // Fixed reward for mana loss option
                                        }
                                        description = "From: Gloomstone Guardian";
                                        break;

                                    case Card.Cards.CS3_028: // Thrive in the Shadows
                                        if (choice == Card.Cards.TOY_714 && board.MinionEnemy.Count(x => x.CanAttack) > 2) // Increase points to Fly Off the Shelves if enemy count on board exceeds 3
                                            points += 100; // Increase points to Fly Off the Shelves if conditions is true
                                        description = "From: Thrive in the Shadows";
                                        break;
                                    case Card.Cards.GIFT_06: // Thrall's Gift
                                        points = ThrallsGift(choice, board);
                                        break;
                                    case Card.Cards.TOY_801: // Chia Drake, Whizbang's Workshop: TOY_801 Miniaturize, TOY_801t Mini
                                    case Card.Cards.TOY_801t: // Cultivate TOY_801a, Draw a spell. Seedling Growth TOY_801b, Gain Spell Damage +1.
                                        points = random.Next(0, 101);
                                        description = "From: Chia Drake";
                                        break;
                                    case Card.Cards.TSC_069: // Amalgam of the Deep, Voyage to the Sunken City and Gigantotem
                                        if (choices.Contains(Card.Cards.REV_838)) // Gigantotem
                                        {
                                            points = Gigantotem(choice, board);
                                        }
                                        break;
                                    case Card.Cards.TTN_940: // Freya, Keeper of Nature, Titans
                                        description = "From: Freya, Keeper of Nature: " + cardTemplate.Name;
                                        points = Freya(choice, board);
                                        break;
                                    case Card.Cards.BAR_079: // Kazakus, Golem Shaper, Forged in the Barrens
                                        points = KazakusGolemShaper(cardTemplate.Name, board);
                                        description = "From: Kazakus, Golem Shaper, minion count: " + board.MinionFriend.Count + " mana available: " + board.ManaAvailable;
                                        break;
                                    case Card.Cards.DMF_075: // Guess the Weight, Madness at the Darkmoon Faire
                                        if (choice == Card.Cards.DMF_075a2) // Less!
                                            points = Convert.ToDouble(GuessTheWeight(board).Split(new char[] { '/' })[0].Trim());
                                        else if (choice == Card.Cards.DMF_075a) // More!
                                            points = Convert.ToDouble(GuessTheWeight(board).Split(new char[] { '/' })[1].Trim());
                                        else
                                            description = "From: Guess the Weight: " + cardTemplate.Name + "  Cost: " + cardTemplate.Cost.ToString(); // Display name and cost of weight card
                                        break;
                                    case Card.Cards.AV_295: // Capture Coldtooth Mine, Fractured in Alterac Valley
                                        if (choice == Card.Cards.AV_295b) // More Supplies
                                        {
                                            points = CaptureColdtoothMine(board);
                                            description = points == 100 ? "Capture Coldtooth Mine, selecting highest cost card" : "Capture Coldtooth Mine, selecting lowest cost card";
                                        }
                                        else
                                            points = 10; // More resources
                                        break;
                                    case Card.Cards.AV_258:  // Bru'kan of the Elements, Fractured in Alterac Valley
                                        points = BrukanOfTheElements(choice, board);
                                        description = "Bru'kan of the Elements";
                                        break;
                                    case Card.Cards.REV_022: // Murloc Holmes, Murder at Castle Nathria
                                        points = MurlocHolmes(choice, board);
                                        break;
                                    case Card.Cards.RLK_654: // Beetlemancy, March of the Lich King
                                        points = Beetlemancy(choice, board);
                                        description = "Beetlemancy";
                                        break;
                                    case Card.Cards.RLK_533: // Scourge Supplies, March of the Lich King
                                        points = 200 - Convert.ToDouble(cardTemplate.Cost); // Discard lowest cost card
                                        description = "Scourge Supplies, discard lowest cost card.";
                                        break;
                                    case Card.Cards.ETC_373: // Drum Circle, Festival of Legends
                                        points = DrumCircle(choice, board);
                                        description = "Drum Circle";
                                        break;
                                    case Card.Cards.ETC_375: // Peaceful Piper, Festival of Legends
                                        points = PeacefulPiper(choice, board);
                                        description = "Peaceful Piper";
                                        break;
                                    case Card.Cards.ETC_316: // Fight Over Me Festival of Legends
                                        points = FightOverMe(choice, board);
                                        description = "Fight Over Me";
                                        break;
                                }
                                break;
                            case 1:
                                // Searching for best point from external file
                                if (iniTierList != null)
                                    double.TryParse(GetIniValue(iniTierList, choice.ToString(), "points", "0"), NumberStyles.Any, CultureInfo.InvariantCulture, out points);
                                break;
                            case 2:
                                // Searching file "discover.ini" for best points
                                if (iniTierList != null)
                                    double.TryParse(GetIniValue(iniTierList, choice.ToString(), hero, "0"), NumberStyles.Any, CultureInfo.InvariantCulture, out points);
                                break;
                        }

                        // Suspicious Alchemist: fix condition
                        // If enemy has Suspicious Alchemist on board and that alchemist revealed a card matching our choice (best effort heuristic)
                        if (board.MinionEnemy.Any(m => m.Template.Id == Card.Cards.REV_000) && board.EnemyGraveyard.Contains(choice))
                        {
                            description = $"Suspicious Alchemist possible opponent selected card {cardTemplate.Name}";
                            points += 500;
                        }

                        // Last chance to play affordable card
                        if (cardTemplate.Cost <= board.ManaAvailable)
                        {
                            points = LastChance(choice, points, board);
                        }

                        // Add to card list with points
                        choicesCardValue.Add(new CardValue(choice, points));
                        TotalPoints += points;
                    }
                    if (TotalPoints > 0) break;
                }

                // Card selection with highest points
                double bestPoints = 0;
                for (var i = 0; i < choicesCardValue.Count; i++) // index through each card
                {
                    double pts = choicesCardValue[i].GetPoints(); // calls cardValue subroutine, get points
                    AddLog($"{i + 1}) {CardTemplate.LoadFromId(choicesCardValue[i].GetCard()).Name}: {pts:F2}");  // Output cards choices to log
                    if (!(bestPoints < pts)) continue; // selects highest points
                    bestChoice = choicesCardValue[i].GetCard(); // calls cardValue subroutine, get card assign to bestChoice
                    bestPoints = pts;
                }

                // Out to Bot log
                AddLog(Divider);
                if (bestPoints <= 0)
                {
                    AddLog($"Selecting: {CardTemplate.LoadFromId(bestChoice).Name}");
                    AddLog($"Origin: {Origin_Card}");
                }
                else
                {
                    AddLog($"{(discard ? "Discard" : "Best")}: {CardTemplate.LoadFromId(bestChoice).Name}: {bestPoints:F2}");
                    if (!string.IsNullOrEmpty(description))
                        AddLog(description);
                }
                AddLog(Divider);
                Bot.Log(logBuilder.ToString());
                return bestChoice; // returns cardID
            }
            catch (Exception ex)
            {
                Bot.Log($"Error in UniversalDiscover: {ex.Message}");
                return choices[random.Next(0, choices.Count)]; // Fallback to random choice on error
            }
        }

        // Origin card correction
        private Tuple<Card.Cards, int> OriginCardCorrection(Card.Cards originCard, List<Card.Cards> choices, Board board, string mode)
        {
            // List of origin cards for correction
            var originChoices = new List<Card.Cards>();

            if (mode == "Arena")
                return Tuple.Create(originCard, 2);

            if (!GiteeFileExists($"smartbot/DiscoverCC/{mode}/{originCard}.ini"))
            {
                // Add enemy cards matching enemyCards  
                originChoices.AddRange(board.MinionEnemy.Select(card => card.Template.Id).Where(enemyCards.Contains));

                // Add friendly cards matching friendCards  
                originChoices.AddRange(board.MinionFriend.Select(card => card.Template.Id).Where(friendCards.Contains));

                // Add last played card  
                if (board.PlayedCards.Any())
                    originChoices.Add(board.PlayedCards.Last());

                // Select origin card for a match  
                foreach (var card in originChoices)
                {
                    if (originCard == card)
                        break; // No correction needed
                    if (GiteeFileExists($"smartbot/DiscoverCC/{mode}/{card}.ini"))
                    {
                        AddLog($"Origin card correction: {CardTemplate.LoadFromId(card).Name}");
                        return Tuple.Create(card, 0);
                    }
                }
            }

            return Tuple.Create(originCard, 0);
        }

        // Get from list
        public class CardValue
        {
            private readonly double _points;
            private readonly Card.Cards _card;

            public CardValue(Card.Cards card, double points)
            {
                _card = card;
                _points = points;
            }

            public Card.Cards GetCard()
            {
                return _card;
            }

            public double GetPoints()
            {
                return _points;
            }
        }

        /// <summary>
        /// Checks remote Gitee repository for file existence using content fetch and cache.
        /// </summary>
        public bool GiteeFileExists(string filePath)
        {
            string content = GetFileContent(filePath);
            return !string.IsNullOrWhiteSpace(content);
        }

        /// <summary>
        /// Fetches text content from Gitee and caches it. Normalizes line endings and BOM.
        /// </summary>
        private string GetFileContent(string relativePath)
        {
            string url = $"https://gitee.com/{owner}/{repo}/raw/main/{relativePath}";
            if (giteeContentCache.TryGetValue(url, out var cached))
                return cached;

            string content = string.Empty;
            try
            {
                content = HttpGet.Get(url) ?? string.Empty;
            }
            catch
            {
                content = string.Empty;
            }
            // Normalize line endings and trim BOM if present
            content = NormalizeText(content);
            giteeContentCache[url] = content;
            return content;
        }

        /// <summary>
        /// Normalizes text: strips UTF-8 BOM, converts CRLF/CR to LF.
        /// </summary>
        private static string NormalizeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            // Remove UTF-8 BOM if exists
            if (text.Length > 0 && text[0] == '\uFEFF')
                text = text.Substring(1);
            // Normalize CRLF to LF and trim trailing whitespace
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            return text;
        }

        /// <summary>
        /// Loads and parses INI content with cache; returns nested dictionary [section][key]=value.
        /// </summary>
        private Dictionary<string, Dictionary<string, string>> LoadIniData(string filePath)
        {
            if (iniCache.TryGetValue(filePath, out var ini))
                return ini;

            string iniContent = GetFileContent(filePath);
            if (!string.IsNullOrWhiteSpace(iniContent))
            {
                ini = ParseIni(iniContent);
                iniCache[filePath] = ini;
                return ini;
            }
            return null;
        }

        /// <summary>
        /// Parses INI text into dictionary of sections with key-value pairs.
        /// Supports inline comments ';' and '#'.
        /// </summary>
        private static Dictionary<string, Dictionary<string, string>> ParseIni(string content)
        {
            var data = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            string currentSection = "";

            foreach (var rawLine in content.Split('\n'))
            {
                string line = rawLine;
                if (line == null) continue;

                line = line.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Strip inline comments starting with ';' or '#'
                int commentIdx = IndexOfComment(line);
                if (commentIdx >= 0)
                    line = line.Substring(0, commentIdx).Trim();

                if (line.Length == 0) continue;

                // Section header
                if (line.StartsWith("[") && line.EndsWith("]") && line.Length > 2)
                {
                    currentSection = line.Substring(1, line.Length - 2).Trim();
                    if (!data.ContainsKey(currentSection))
                        data[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    // Key=value pair
                    int eqIdx = line.IndexOf('=');
                    if (eqIdx <= 0) continue; // skip malformed lines and empty keys
                    string key = line.Substring(0, eqIdx).Trim();
                    string value = line.Substring(eqIdx + 1).Trim();

                    if (!data.ContainsKey(currentSection))
                        data[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    data[currentSection][key] = value;
                }
            }

            return data;
        }

        /// <summary>
        /// Finds index of first comment delimiter ';' or '#'.
        /// </summary>
        private static int IndexOfComment(string line)
        {
            int semi = line.IndexOf(';');
            int hash = line.IndexOf('#');
            if (semi < 0) return hash;
            if (hash < 0) return semi;
            return Math.Min(semi, hash);
        }

        /// <summary>
        /// Gets INI value from parsed dictionary; returns default if not found.
        /// </summary>
        private static string GetIniValue(Dictionary<string, Dictionary<string, string>> ini, string section, string key, string defaultValue = null)
        {
            if (ini == null || section == null || key == null) return defaultValue;
            if (ini.TryGetValue(section, out var sec) && sec != null && sec.TryGetValue(key, out var value) && value != null)
                return value;
            return defaultValue;
        }

        // Get current deck mode from selected deck string
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

        // Adds text to log variable
        private void AddLog(string entry)
        {
            logBuilder.Append("\r\n").Append(entry);
        }

        //  *********** Special conditions ***********

        // Thrall's Gift
        private double ThrallsGift(Card.Cards choice, Board board)
        {
            double points = 0;
            int friendCount = board.MinionFriend.Count;
            int enemyCount = board.MinionEnemy.Count;
            int availableMana = board.ManaAvailable;

            switch (choice)
            {
                case Card.Cards.CS2_046: // Bloodlust
                    description = String.Format("From: Thrall's Gift, mana: {0}, F->count: {1}, E->count: {2}", availableMana, friendCount, enemyCount);
                    if (friendCount > 0 && CardTemplate.LoadFromId(Card.Cards.CS2_046).Cost <= availableMana)
                    {
                        points = CurrentFriendAttack(board) + (friendCount * 3) >= CurrentEnemyBoardDefense(board) ? 100 : CurrentFriendAttack(board) + (friendCount * 3);
                    }
                    break;

                case Card.Cards.CORE_EX1_259: //  Lightning Storm
                    points = enemyCount * 3;
                    break;

                case Card.Cards.CORE_EX1_246: //  Hex
                    if (friendCount < 4 && availableMana >= 3 && board.MinionEnemy.Any(x => x.IsTaunt && x.CurrentAtk > 4 && x.CurrentHealth > 4))
                    {
                        points = 100;
                    }
                    break;
            }
            return points;
        }

        // Gigantotem, Murder at Castle Nathria
        private double Gigantotem(Card.Cards choice, Board board)
        {
            if (choice != Card.Cards.REV_838) return 0;

            int totemCount =
                board.MinionFriend.Count(m => totemCards.Contains(m.Template.Id))
                + board.FriendGraveyard.Count(id => totemCards.Contains(id));

            int effectiveCost = Math.Max(10 - totemCount, 0);
            bool playableSoon = effectiveCost - 3 <= board.MaxMana;

            if (playableSoon)
            {
                description = $"Best: Gigantotem calculated cost: {effectiveCost}";
                return 100;
            }
            return 0;
        }

        // Kazakus, Golem Shaper choice cards 
        private static readonly List<Kazakus> kazakusCards = new List<Kazakus>() //  Create a list of Kazakus choiceCards
        {           // Kazakus, Golem Shaper choice cards popularity from HSReplay
               //  First choice
                new Kazakus(){ Name = "Lesser Golem", Lesser = 200, Greater = 1, Superior = 1 }, // BAR_079_m1
                new Kazakus(){ Name = "Greater Golem", Lesser = 1, Greater = 200, Superior = 1 }, // BAR_079_m2
                new Kazakus(){ Name = "Superior Golem", Lesser = 1, Greater = 1, Superior = 200 }, // BAR_079_m3
                //  Second choice
                new Kazakus(){ Name = "Grave Moss", Lesser = 196, Greater = 196, Superior = 196 }, // Poisonous, BAR_079t9
                new Kazakus(){ Name = "Sungrass", Lesser = 198, Greater = 198, Superior = 198 }, // Divine Shield, BAR_079t6
                new Kazakus(){ Name = "Fadeleaf", Lesser = 195, Greater = 195, Superior = 195 }, // Stealth, BAR_079t8
                new Kazakus(){ Name = "Earthroot", Lesser = 197, Greater = 197, Superior = 197 }, // Taunt, BAR_079t5
                new Kazakus(){ Name = "Liferoot", Lesser = 199, Greater = 199, Superior = 199 }, // Lifesteal, BAR_079t7
                new Kazakus(){ Name = "Swifthistle", Lesser = 200, Greater = 200, Superior = 200 }, // Rush, BAR_079t4
                //  Third choice
                new Kazakus(){ Name = "Wildvine", Lesser = 101, Greater = 50.524, Superior = 41.67 }, // Give your other minions +(1, 2, 4), BAR_079t10, BAR_079t10b, BAR_079t10c
                new Kazakus(){ Name = "Firebloom", Lesser = 75.333, Greater = 67.907, Superior = 44.94 }, // Deal 3 damage to (1, 2, 4) random enemy minion, BAR_079t13, BAR_079t13b, BAR_079t13c
                new Kazakus(){ Name = "Gromsblood", Lesser = 63.581, Greater = 1, Superior = 1 }, // Summon a copy of this, BAR_079t11
                new Kazakus(){ Name = "Kingsblood", Lesser = 26, Greater = 24.1, Superior = 9.8 }, // Draw a card (1, 2, 4), BAR_079t15, BAR_079t15b, BAR_079t15c
                new Kazakus(){ Name = "Icecap", Lesser = 1, Greater = 6.97, Superior = 31.65 }, // Freeze (1, 2, 4) random enemy minions, BAR_079t12b, BAR_079t12, BAR_079t12c
                new Kazakus(){ Name = "Mageroyal", Lesser = 1, Greater = 2.12, Superior = 1 }, // Spell Damage +(1, 2, 4), BAR_079t14, BAR_079t14b, BAR_079t14c
    };

        // Kazakus, Golem Shaper, Forged in the Barrens
        private static double KazakusGolemShaper(string kazakusCard, Board board)
        {
            // Select Superior Golem if equal or more than 8 mana
            if (board.MaxMana >= 8)
            {
                // Deal damage to enemy minions vs give your minions health
                if (board.MinionFriend.Count >= board.MinionEnemy.Count)
                    kazakusCards.Find(x => x.Name == "Wildvine").Superior += 100;
                return kazakusCards.Find(x => x.Name == kazakusCard).Superior;
            }

            // Select Greater Golem  if equal or more than 4 mana
            if (board.MaxMana >= 4)
            {
                // Deal damage to enemy minions vs give your minions health
                if (board.MinionFriend.Count >= board.MinionEnemy.Count)
                    kazakusCards.Find(x => x.Name == "Wildvine").Greater += 100;
                return kazakusCards.Find(x => x.Name == kazakusCard).Greater;
            }

            // Default Lesser Golem
            // Deal damage to enemy minions vs give your minions health
            if (board.MinionFriend.Count >= board.MinionEnemy.Count)
                kazakusCards.Find(x => x.Name == "Wildvine").Lesser += 100;
            return kazakusCards.Find(x => x.Name == kazakusCard).Lesser; // Greater Golem
        }

        private class Kazakus
        {
            public string Name { get; set; }
            public double Lesser { get; set; }
            public double Greater { get; set; }
            public double Superior { get; set; }
        }

        // Guess the weight, Madness at the Darkmoon Faire
        private static string GuessTheWeight(Board board)
        {
            var currentDeck = CurrentDeck(board);
            var last = board.Hand.LastOrDefault();
            int lastCardCost = last != null ? last.CurrentCost : 0;

            int less = 0, more = 0;
            for (int i = 0; i < currentDeck.Count; i++)
            {
                var tmpl = CardTemplate.LoadFromId(currentDeck[i]);
                if (tmpl.Cost < lastCardCost) less++;
                else if (tmpl.Cost > lastCardCost) more++;
            }
            return less + "/" + more;
        }

        // Capture Coldtooth Mine, Fractured in Alterac Valley
        private static double CaptureColdtoothMine(Board board) // Select highest cost card if equal or 1 higher current mana available
        {
            // Get list of current cards in my deck
            List<Card.Cards> currentDeck = new List<Card.Cards>();
            currentDeck = CurrentDeck(board);
            if (currentDeck.Select(CardTemplate.LoadFromId).Max(x => x.Cost) >= board.ManaAvailable - 1)
                return 100;
            return 1;
        }

        // Bru'kan of the Elements, Fractured in Alterac Valley
        private static double BrukanOfTheElements(Card.Cards choice, Board board)
        {
            double[] points = { 40, 30, 20, 10 }; // Default; Earth Invocation[0], Water Invocation[1], Fire Invocation[2], Lightning Invocation[3]
            // Overrides
            if (CurrentEnemyBoardDefense(board) - CurrentFriendAttack(board) <= 6) // Can opponent hero can be destroyed this turn
                points[2] = 100; // Fire Invocation
            else if (board.MinionEnemy.Count > 1 && CurrentEnemyBoardHealth(board) / board.MinionEnemy.Count < 4) // If opponent has more than 2 minions on board average health 3 or less. Deal 2 damage to all enemy minions
                points[3] = 90; // for Lightning Invocation
            switch (choice)
            {
                case Card.Cards.AV_258t:  // Earth Invocation, Summon two 2/3 Elementals with Taunt
                    return points[0];
                case Card.Cards.AV_258t2: // Water Invocation(67816) Restore 6 Health to all friendly characters
                    return points[1];
                case Card.Cards.AV_258t3: // Fire Invocation(67817) Deal 6 damage to the enemy hero
                    return points[2];
                case Card.Cards.AV_258t4: // Lightning Invocation(67818) Deal 2 damage to all enemy minions
                    return points[3];
            }
            return 0;
        }

        // Murloc Holmes, Murder at Castle Nathria.
        private double MurlocHolmes(Card.Cards choice, Board board)
        {
            description = "Murloc Holmes, possible choices: ";
            // Create new empty list, add cards from opponent graveyard and board
            var _opponentCards = (from _card in board.EnemyGraveyard select _card).ToList(); // First options
            _opponentCards.AddRange(from _card in board.MinionEnemy select _card.Template.Id);
            // Out to bot log
            foreach (var _card in _opponentCards)
            {
                // Bot.Log("Card: " + CardTemplate.LoadFromId(card).Name);
                description += CardTemplate.LoadFromId(_card).Name + ", ";
            }
            // First possible choice, the coin
            if (CardTemplate.LoadFromId(choice).Name == "The Coin")
                return 500;
            // Second possible choice apply points to matched cards
            foreach (var card in _opponentCards)
            {
                if (card == choice)
                    return 200 - _opponentCards.IndexOf(card); // Subtract index of _opponentCards list in order of opponent cards in Graveyard --> board
            }
            // If no cards found, try external file
            return 0;
        }

        // Beetlemancy, March of the Lich King
        private double Beetlemancy(Card.Cards choice, Board board)
        {
            switch (choice)
            {
                case Card.Cards.RLK_654t: // Summon two 3/3 Beetles with Taunt
                    if (board.MinionFriend.Count < 6)
                        return 60;
                    else
                        return 40;
                default: // Default, gain 12 Armor if no room on board for 2 beetles 
                    return 50;
            }
        }

        // Drum Circle, Festival of Legends
        private double DrumCircle(Card.Cards choice, Board board) // dbfId: 94201
        {
            int points = board.MinionFriend.Count;
            switch (choice) // choice cards selected from origin card, Drum Circle
            {
                case Card.Cards.ETC_373b: // Good vibrations
                    if (EnemyHasLethal(board) && points > 0)
                        return 106 + points;
                    else
                        return 100 + points; // 1st choice, points increase / more minions on board, give your minions +2/+4 and Taunt
                case Card.Cards.ETC_373a: // Flower power
                    return 107 - points; // 2nd choice, points decrease / more minions on board, summon five 2/2 Treants
                default:
                    return points;
            }
        }

        // Peaceful Piper, Festival of Legends
        private double PeacefulPiper(Card.Cards choice, Board board) // Choose One - Draw a Beast; or Discover one.
        {
            switch (choice)
            {
                case Card.Cards.ETC_375a: // Friendly face
                    if (board.Deck.Count(card => CardTemplate.LoadFromId(card).Races.Contains(Card.CRace.PET)) > 0) // If deck has a beast card then, Draw a Beast.
                        return 100;
                    else
                        return 10;
                case Card.Cards.ETC_375b: // Happy Hippie, Discover a Beast.
                    return 50;
                default: return 10;
            }
        }

        // Fight Over Me, Festival of Legends, Choose two enemy minions. They fight! Add copies of any that die to your hand.
        private double FightOverMe(Card.Cards choice, Board board)
        {
            var _opponentCards = board.MinionEnemy.FindAll(x => x.Type == Card.CType.MINION).OrderByDescending(x => x.CurrentAtk + x.CurrentHealth).ToList();
            foreach (var card in _opponentCards)
                if (card.Template.Id == choice)
                    return 200 - _opponentCards.IndexOf(card); // Subtract index of _opponentCards list in order of descending
            return 10;
        }

        // Freya, Keeper of Nature, Titans
        private double Freya(Card.Cards choice, Board board)
        {
            double totalCost = 0;
            double totalCount = 0;
            switch (choice)
            {
                case Card.Cards.TTN_940a: // Summon copies all other friendly minions.
                    if (!board.MinionFriend.Any()) return 0;
                    totalCost = board.MinionFriend.Sum(x => x.CurrentCost);
                    totalCount = board.MinionFriend.Count();
                    break;
                case Card.Cards.TTN_940b: // Duplicate your hand.
                    if (!board.Hand.Any()) return 0;
                    totalCost = board.Hand.Sum(x => x.CurrentCost);
                    totalCount = board.Hand.Count();
                    break;
            }
            return totalCost / totalCount; // Calculate average cost of card, return highest average
        }

        //  *********** End of special card conditions ***********

        // Get current version from EE_Information.txt
        private string CurrentVersion()
        {
            string url = $"https://gitee.com/{owner}/{repo}/raw/main/smartbot/DiscoverCC/EE_Information.txt";

            if (!giteeContentCache.TryGetValue(url, out var content))
            {
                try
                {
                    content = HttpGet.Get(url) ?? string.Empty;
                }
                catch
                {
                    content = string.Empty;
                }
                giteeContentCache[url] = NormalizeText(content);
            }

            if (string.IsNullOrWhiteSpace(content))
                return null;

            var lines = NormalizeText(content).Split('\n');
            string firstLine = lines.Length > 0 ? lines[0].Trim() : string.Empty;

            if (string.IsNullOrEmpty(firstLine))
                return null;

            Match match = Regex.Match(firstLine, @"\((.+?)\)");
            return match.Success ? match.Groups[1].Value : null;
        }

        // Return list of current cards remaining in my deck
        private static List<Card.Cards> CurrentDeck(Board board)
        {
            var played = new HashSet<Card.Cards>(
            board.Hand.Select(h => h.Template.Id)
            .Concat(board.MinionFriend.Select(m => m.Template.Id))
            .Concat(board.FriendGraveyard));

            return board.Deck.Where(card => !played.Contains(card)).ToList();
        }

        // Board calculations
        // Calculate friendly attack value
        private static int CurrentFriendAttack(Board board)
        {
            return (board.MinionFriend.FindAll(x => x.CanAttack && (x.IsCharge || x.NumTurnsInPlay != 0) && x.CountAttack == 0 && !x.IsTired).Sum(x => x.CurrentAtk) + (board.HasWeapon(true) && board.HeroFriend.CountAttack == 0 ? board.WeaponFriend.CurrentAtk : 0));
        }

        // Calculate friendly defense value (armor, health and taunt)
        private static int CurrentFriendDefense(Board board)
        {
            return board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor + (board.MinionFriend.FindAll(x => x.IsTaunt == true).Sum(x => x.CurrentHealth));
        }

        // Calculate opponent board defense value (armor, health and taunt values)
        private static int CurrentEnemyBoardDefense(Board board)
        {
            return board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor + (board.MinionEnemy.FindAll(x => x.IsTaunt == true).Sum(x => x.CurrentHealth));
        }

        // Calculate opponent hero defense value (armor and health)
        private static int CurrentEnemyHeroDefense(Board board)
        {
            return board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor;
        }

        // Calculate opponent attack value
        private static int CurrentEnemyAttack(Board board)
        {
            return board.MinionEnemy.FindAll(x => x.CanAttack && (x.IsCharge || x.NumTurnsInPlay != 0) && x.CountAttack == 0 && !x.IsTired).Sum(x => x.CurrentAtk) + (board.HasWeapon(false) && board.HeroEnemy.CountAttack == 0 ? board.WeaponEnemy.CurrentAtk : 0);
        }

        // Calculate opponent board health value
        private static int CurrentEnemyBoardHealth(Board board)
        {
            return board.MinionEnemy.FindAll(x => x.CurrentHealth > 0).Sum(x => x.CurrentHealth);
        }

        // Check if enemy has lethal
        private static bool EnemyHasLethal(Board board)
        {
            if (board.MinionFriend.Any(x => x.IsTaunt)) return false;
            return board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor <=
                   board.MinionEnemy.FindAll(
                       x => x.CanAttack && (x.IsCharge || x.NumTurnsInPlay != 0) && x.CountAttack == 0 && !x.IsTired)
                       .Sum(x => x.CurrentAtk) +
                   (board.HasWeapon(false) && board.HeroEnemy.CountAttack == 0 ? board.WeaponEnemy.CurrentAtk : 0);
        }

        // Last chance card for a win
        private double LastChance(Card.Cards card, double points, Board board)
        {
            // Declare variables
            var cardTemplate = CardTemplate.LoadFromId(card);

            // Has card charge and able to kill opponent hero
            if (cardTemplate.Charge && CurrentEnemyBoardDefense(board) <= (CurrentFriendAttack(board) + cardTemplate.Atk))
            {
                description = "Possible enemy defeat, selecting charge card";
                points = 1000 + cardTemplate.Atk;
            }

            // If card has taunt and enemy has lethal
            if (cardTemplate.Taunt && EnemyHasLethal(board))
            {
                description = "Enemy has lethal, selecting taunt card";
                points = 1000 + cardTemplate.Health;
            }
            return points;
        }
    }
}
