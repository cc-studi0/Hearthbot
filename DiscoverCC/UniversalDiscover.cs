using SmartBot.Database;
using SmartBot.Discover;
using SmartBot.Plugins.API;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

// by Evil_Eyes

namespace UniversalDiscover
{
    public class UniversalDiscover : DiscoverPickHandler
    {
        // List of card for origin correction on opponent board
        private static readonly List<Card.Cards> enemyCards = new List<Card.Cards>
        {
            Card.Cards.REV_000, // Suspicious Alchemist
            Card.Cards.REV_002, // Suspicious Usher
            Card.Cards.REV_006, // Suspicious Pirate
            Card.Cards.NX2_044, // Suspicious Peddler
            Card.Cards.MIS_916 // Pro Gamer, Challenge your opponent to a game of Rock-Paper-Scissors! The winner draws 2 cards.
        };

        // List of cards for origin correction on friendly board
        private static readonly List<Card.Cards> friendCards = new List<Card.Cards>
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

        // Raptor Herald, Rewind Battlecry: Discover a Beast with a Dark Gift. 
        private static readonly Dictionary<Card.Cards, double> raptorHeraldCards = new Dictionary<Card.Cards, double>
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
            { Card.Cards.EDR_100t8,   10 }  // Sweet Dreams
        };

        // Dark Rider: Battlecry: If you're holding a Dragon, Discover a Dragon with a Dark Gift.
        private static readonly Dictionary<Card.Cards, double> darkRider = new Dictionary<Card.Cards, double>
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

        // Initialize the log and list to store cards to keep
        private string log, description;

        // Version control variables
        private static bool versionChecked, discard;

        // Directory paths
        private readonly string smartBotDirectory = Directory.GetCurrentDirectory();
        private readonly string discoverCCDirectory = Directory.GetCurrentDirectory() + @"\DiscoverCC\";

        // Random instance for any random decisions
        private static Random random = new Random();

        // Card Handle Pick Decision from SB
        public Card.Cards HandlePickDecision(Card.Cards originCard, List<Card.Cards> choices, Board board)
        {
            if (!versionChecked)
            {
                FileVersionCheck();
                CleanupIniFiles();
                versionChecked = true;
            }

            // Initialize log for this decision
            log = string.Format("===== Universal Discover V10.0, Card definition V{0} ===EE", CurrentVersion());

            // Remove duplicate choices, keeping the first occurrence (LINQ)
            choices = choices.Distinct().ToList();

            // Default to random choice if any unexpected issue occurs
            Card.Cards lastChance = choices[random.Next(0, choices.Count)];

            // Local variables for decision making
            string mode = CurrentMode();
            string hero = board.FriendClass.ToString();
            string Divider = new string('=', 40);
            var choicesCardValue = new List<CardValue>();
            double points = 0;
            double TotalPoints = 0;
            Card.Cards bestChoice = lastChance;

            try
            {
                // Load DiscoverChoices.json for the current mode to access specific scoring for origin card and choices
                var choicesJson = ReadJsonFile(Path.Combine(discoverCCDirectory, mode, "DiscoverChoices.json"));

                // Origin card check and correction
                originCard = OriginCardCorrection(choicesJson, originCard, board);

                // Origin card name from database template
                string Origin_Card = CardTemplate.LoadFromId(originCard).Name;

                // Cache card templates for this decision to avoid repeated lookups
                var choiceTemplates = choices.ToDictionary(c => c, c => CardTemplate.LoadFromId(c));

                // First pass: Evaluate specific conditions for each choice based on the origin card and the current board state, which may override general scoring from DiscoverChoices.json
                for (int choiceIndex = 0; choiceIndex < 3; choiceIndex++)
                {
                    // Reset accumulator per pass
                    TotalPoints = 0;
                    choicesCardValue.Clear();
                    discard = false;

                    // Evaluate each choice for the current pass
                    foreach (var choice in choices)
                    {
                        var cardTemplate = choiceTemplates[choice];
                        points = 0;

                        switch (choiceIndex)
                        {
                            case 0:
                                switch (originCard)
                                {
                                    case Card.Cards.WON_103: // Chamber of Viscidus
                                        discard = true;
                                        // If the offered card is in the opponent's board or graveyard, it's likely the opponent selected it to be discarded, so assign higher points. Otherwise, assign lower points for discarding a card that may not be relevant to the opponent.
                                        points = 200 - (GetDiscoverScore(choicesJson, originCard.ToString(), choice.ToString()) ?? 0);
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

                                    case Card.Cards.CORE_EDR_004: // Raptor Herald
                                        // Evaluate based on the specific card offered and its assigned points in the raptorHeraldCards dictionary
                                        if (raptorHeraldCards.TryGetValue(choice, out points))
                                            description = "From: Raptor Herald";
                                        break;

                                    case Card.Cards.EDR_456: // Dark Rider
                                        if (darkRider.TryGetValue(choice, out points))
                                            description = "From: Dark Rider";
                                        break;

                                    case Card.Cards.DEEP_027: // Gloomstone Guardian
                                        double discardScore =
                                            2 * Math.Max(board.MinionFriend.Count - 2, 0) +
                                            3 * Math.Max(board.ManaAvailable, 0);
                                        double manaLossScore =
                                            2 * Math.Max(board.MinionFriend.Count, 0) +
                                            3 * Math.Max(board.ManaAvailable, 0);

                                        if (choice == Card.Cards.DEEP_027a)
                                        {
                                            points = discardScore >= manaLossScore ? 100 : 50;
                                        }
                                        else if (choice == Card.Cards.DEEP_027b)
                                        {
                                            points = 75;
                                        }
                                        description = "From: Gloomstone Guardian";
                                        break;

                                    case Card.Cards.CS3_028: // Thrive in the Shadows
                                        if (choice == Card.Cards.TOY_714 &&
                                            board.MinionEnemy.Count(x => x.CanAttack) > 2)
                                            points += 100;
                                        description = "From: Thrive in the Shadows";
                                        break;

                                    case Card.Cards.GIFT_06: // Thrall's Gift
                                        points = ThrallsGift(choice, board);
                                        break;

                                    case Card.Cards.TOY_801:
                                    case Card.Cards.TOY_801t:
                                        points = random.Next(0, 101);
                                        description = "From: Chia Drake";
                                        break;

                                    case Card.Cards.TSC_069: // Amalgam of the Deep and Gigantotem
                                        if (choices.Contains(Card.Cards.REV_838))
                                            points = Gigantotem(choice, board);
                                        break;

                                    case Card.Cards.TTN_940: // Freya, Keeper of Nature
                                        description = "From: Freya, Keeper of Nature: " + cardTemplate.Name;
                                        points = Freya(choice, board);
                                        break;

                                    case Card.Cards.BAR_079: // Kazakus, Golem Shaper
                                        points = KazakusGolemShaper(cardTemplate.Name, board);
                                        description = "From: Kazakus, Golem Shaper, minion count: " +
                                                      board.MinionFriend.Count + " mana available: " +
                                                      board.ManaAvailable;
                                        break;

                                    case Card.Cards.DMF_075: // Guess the Weight
                                        string weight = GuessTheWeight(board);
                                        string[] parts = weight.Split('/');
                                        if (choice == Card.Cards.DMF_075a2 && parts.Length >= 1)
                                        {
                                            points = Convert.ToDouble(parts[0].Trim(), CultureInfo.InvariantCulture);
                                        }
                                        else if (choice == Card.Cards.DMF_075a && parts.Length >= 2)
                                        {
                                            points = Convert.ToDouble(parts[1].Trim(), CultureInfo.InvariantCulture);
                                        }
                                        else
                                        {
                                            description = "From: Guess the Weight: " + cardTemplate.Name +
                                                          "  Cost: " + cardTemplate.Cost;
                                        }
                                        break;

                                    case Card.Cards.AV_295: // Capture Coldtooth Mine
                                        if (choice == Card.Cards.AV_295b)
                                        {
                                            points = CaptureColdtoothMine(board);
                                            description = points == 100
                                                ? "Capture Coldtooth Mine, selecting highest cost card"
                                                : "Capture Coldtooth Mine, selecting lowest cost card";
                                        }
                                        else
                                        {
                                            points = 10;
                                        }
                                        break;

                                    case Card.Cards.AV_258: // Bru'kan of the Elements
                                        points = BrukanOfTheElements(choice, board);
                                        description = "From: Bru'kan of the Elements";
                                        break;

                                    case Card.Cards.REV_022: // Murloc Holmes
                                        points = MurlocHolmes(choice, board);
                                        break;

                                    case Card.Cards.RLK_654: // Beetlemancy
                                        points = Beetlemancy(choice, board);
                                        description = "From: Beetlemancy";
                                        break;

                                    case Card.Cards.RLK_533: // Scourge Supplies
                                        discard = true;
                                        points = 200 - cardTemplate.Cost;
                                        description = "From: Scourge Supplies, discard lowest cost card.";
                                        break;

                                    case Card.Cards.ETC_373: // Drum Circle
                                        points = DrumCircle(choice, board);
                                        description = "From: Drum Circle";
                                        break;

                                    case Card.Cards.ETC_375: // Peaceful Piper
                                        points = PeacefulPiper(choice, board);
                                        description = "From: Peaceful Piper";
                                        break;

                                    case Card.Cards.ETC_316: // Fight Over Me
                                        points = FightOverMe(choice, board);
                                        description = "From: Fight Over Me";
                                        break;

                                    default:
                                        if (board.Hand.Count == 0) // Hand empty: favor cards near current mana
                                        {
                                            points = CalculateScore(cardTemplate.Cost, board.ManaAvailable);
                                            description = "Hand empty, selecting best card with cost: " + cardTemplate.Name;
                                        }
                                        break;
                                }
                                break;

                            case 1:
                                // Second pass for general scoring based on DiscoverChoices.json, after specific conditions are evaluated
                                points = GetDiscoverScore(choicesJson, originCard.ToString(), choice.ToString()) ?? 0;
                                description = $"Origin: {Origin_Card}\nPath: {mode}\\DiscoverChoices.json";
                                break;

                            case 2:
                                // Third pass for any additional scoring from discover.json that may not be covered in the specific conditions or general scoring
                                var discoverJson = ReadJsonFile(Path.Combine(discoverCCDirectory, mode, "discover.json"));
                                points = GetDiscoverValue(discoverJson, hero, choice.ToString()) ?? 0;
                                description = $"Origin: {Origin_Card}\nPath: {mode}\\discover.json";
                                break;
                        }

                        // Suspicious Alchemist: if enemy has it and the revealed card is in graveyard
                        if (board.MinionEnemy.Any(m => m.Template.Id == Card.Cards.REV_000) && board.EnemyGraveyard.Contains(choice))
                        {
                            description = "Suspicious Alchemist possible opponent selected card " + cardTemplate.Name;
                            points += 500;
                        }

                        // Last chance to play affordable card
                        if (cardTemplate.Cost <= board.ManaAvailable)
                        {
                            points = LastChance(choice, points, board);
                        }

                        choicesCardValue.Add(new CardValue(choice, points));
                        TotalPoints += points;
                    }

                    if (TotalPoints > 0)
                        break;
                }

                double bestPoints = 0;
                for (int i = 0; i < choicesCardValue.Count; i++)
                {
                    double pts = Math.Round(choicesCardValue[i].GetPoints(), 2);
                    var cardName = choiceTemplates[choicesCardValue[i].GetCard()].Name;
                    AddLog($"{i + 1}) {cardName}: {pts,2:F2}");
                    if (bestPoints < pts)
                    {
                        bestChoice = choicesCardValue[i].GetCard();
                        bestPoints = pts;
                    }
                }

                AddLog(Divider);
                if (bestPoints <= 0)
                {
                    AddLog("Selecting: " + choiceTemplates[bestChoice].Name);
                    AddLog("Origin: " + Origin_Card);
                }
                else
                {
                    AddLog(string.Format("{0}: {1}: {2:F2}", discard ? "Discard" : "Best", choiceTemplates[bestChoice].Name, bestPoints));
                    if (!string.IsNullOrEmpty(description))
                        AddLog(description);
                }
                AddLog(Divider);
                Bot.Log(log);
                return bestChoice;
            }
            catch (Exception ex)
            {
                Bot.Log("===== Universal Discover ===EE");
                Bot.Log("Error in UniversalDiscover: " + ex.Message);
                Bot.Log(new string('=', 40));
                return lastChance;
            }
        }

        // Origin card correction
        private Card.Cards OriginCardCorrection(string json, Card.Cards originCard, Board board)
        {
            var originChoices = new List<Card.Cards>();

            // Add cards from opponent board and graveyard that are in the enemyCards list
            originChoices.AddRange(board.MinionEnemy.Select(card => card.Template.Id).Where(enemyCards.Contains));

            // Add cards from friendly board and graveyard that are in the friendCards list
            originChoices.AddRange(board.MinionFriend.Select(card => card.Template.Id).Where(friendCards.Contains));

            // Add last played card if it exists, as it can be the origin of the discover
            if (board.PlayedCards.Any())
                originChoices.Add(board.PlayedCards.Last());

            // Select origin card for a match, checking in reverse order (most recent entries first)
            for (int i = originChoices.Count - 1; i >= 0; i--)
            {
                var card = originChoices[i];

                if (originCard == card)
                    break; // No correction needed

                if (OriginCardExists(json, card.ToString()))
                {
                    AddLog($"Origin card correction: {CardTemplate.LoadFromId(card).Name}");
                    return card;
                }
            }

            return originCard;
        }

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

        // Combine all Addlog, includes return and new line
        private void AddLog(string message)
        {
            log += "\r\n" + message;
        }

        // Get current mode: Standard, Wild, Arena, Practice/Casual
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

        // *********** Special conditions ***********

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
                    description = string.Format(
                        "From: Thrall's Gift, mana: {0}, F->count: {1}, E->count: {2}",
                        availableMana, friendCount, enemyCount);

                    if (friendCount > 0 &&
                        CardTemplate.LoadFromId(Card.Cards.CS2_046).Cost <= availableMana)
                    {
                        int buffedAttack = CurrentFriendAttack(board) + friendCount * 3;
                        points = buffedAttack >= CurrentEnemyBoardDefense(board) ? 100 : buffedAttack;
                    }
                    break;

                case Card.Cards.CORE_EX1_259: // Lightning Storm
                    points = enemyCount * 3;
                    break;

                case Card.Cards.CORE_EX1_246: // Hex
                    if (friendCount < 4 &&
                        availableMana >= 3 &&
                        board.MinionEnemy.Any(x => x.IsTaunt && x.CurrentAtk > 4 && x.CurrentHealth > 4))
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
                board.MinionFriend.Count(m => totemCards.Contains(m.Template.Id)) +
                board.FriendGraveyard.Count(id => totemCards.Contains(id));

            int effectiveCost = Math.Max(10 - totemCount, 0);
            bool playableSoon = effectiveCost - 3 <= board.MaxMana;

            if (playableSoon)
            {
                description = $"Best: Gigantotem calculated cost: {effectiveCost}, MaxMana  {board.MaxMana}";
                return 100;
            }
            return 0;
        }

        // Kazakus, Golem Shaper choice cards 
        private static readonly List<Kazakus> kazakusCards = new List<Kazakus>
        {
            new Kazakus { Name = "Lesser Golem",   Lesser = 200, Greater = 1,     Superior = 1   },
            new Kazakus { Name = "Greater Golem",  Lesser = 1,   Greater = 200,   Superior = 1   },
            new Kazakus { Name = "Superior Golem", Lesser = 1,   Greater = 1,     Superior = 200 },

            new Kazakus { Name = "Grave Moss", Lesser = 196, Greater = 196, Superior = 196 },
            new Kazakus { Name = "Sungrass",   Lesser = 198, Greater = 198, Superior = 198 },
            new Kazakus { Name = "Fadeleaf",   Lesser = 195, Greater = 195, Superior = 195 },
            new Kazakus { Name = "Earthroot",  Lesser = 197, Greater = 197, Superior = 197 },
            new Kazakus { Name = "Liferoot",   Lesser = 199, Greater = 199, Superior = 199 },
            new Kazakus { Name = "Swifthistle",Lesser = 200, Greater = 200, Superior = 200 },

            new Kazakus { Name = "Wildvine",  Lesser = 101,   Greater = 50.524, Superior = 41.67 },
            new Kazakus { Name = "Firebloom", Lesser = 75.333,Greater = 67.907, Superior = 44.94 },
            new Kazakus { Name = "Gromsblood",Lesser = 63.581,Greater = 1,      Superior = 1    },
            new Kazakus { Name = "Kingsblood",Lesser = 26,    Greater = 24.1,   Superior = 9.8  },
            new Kazakus { Name = "Icecap",    Lesser = 1,     Greater = 6.97,   Superior = 31.65},
            new Kazakus { Name = "Mageroyal", Lesser = 1,     Greater = 2.12,   Superior = 1    },
        };

        // Kazakus, Golem Shaper, Forged in the Barrens
        private static double KazakusGolemShaper(string kazakusCard, Board board)
        {
            Kazakus wildvine = kazakusCards.Find(x => x.Name == "Wildvine");
            Kazakus selected = kazakusCards.Find(x => x.Name == kazakusCard);

            if (selected == null || wildvine == null)
                return 0;

            if (board.MaxMana >= 8)
            {
                if (board.MinionFriend.Count >= board.MinionEnemy.Count)
                    wildvine.Superior += 100;
                return selected.Superior;
            }

            if (board.MaxMana >= 4)
            {
                if (board.MinionFriend.Count >= board.MinionEnemy.Count)
                    wildvine.Greater += 100;
                return selected.Greater;
            }

            if (board.MinionFriend.Count >= board.MinionEnemy.Count)
                wildvine.Lesser += 100;
            return selected.Lesser;
        }

        private class Kazakus
        {
            public string Name { get; set; }
            public double Lesser { get; set; }
            public double Greater { get; set; }
            public double Superior { get; set; }
        }

        // Guess the Weight, Madness at the Darkmoon Faire
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

        // Capture Coldtooth Mine, Ashes of Outland
        private static double CaptureColdtoothMine(Board board)
        {
            List<Card.Cards> currentDeck = CurrentDeck(board);
            if (currentDeck.Count == 0)
                return 1;

            int maxCost = currentDeck.Select(CardTemplate.LoadFromId).Max(x => x.Cost);
            return maxCost >= board.ManaAvailable - 1 ? 100 : 1;
        }

        // Bru'kan of the Elements, Fractured in Alterac Valley
        private static double BrukanOfTheElements(Card.Cards choice, Board board)
        {
            double[] points = { 40, 30, 20, 10 };
            if (CurrentEnemyBoardDefense(board) - CurrentFriendAttack(board) <= 6)
                points[2] = 100;
            else if (board.MinionEnemy.Count > 1 &&
                     board.MinionEnemy.Count > 0 &&
                     CurrentEnemyBoardHealth(board) / board.MinionEnemy.Count < 4)
                points[3] = 90;

            switch (choice)
            {
                case Card.Cards.AV_258t:
                    return points[0];
                case Card.Cards.AV_258t2:
                    return points[1];
                case Card.Cards.AV_258t3:
                    return points[2];
                case Card.Cards.AV_258t4:
                    return points[3];
            }
            return 0;
        }

        // Murloc Holmes, Murder at Castle Nathria.
        private double MurlocHolmes(Card.Cards choice, Board board)
        {
            description = "Murloc Holmes, possible choices: ";

            var opponentCards = board.EnemyGraveyard.ToList();
            opponentCards.AddRange(board.MinionEnemy.Select(c => c.Template.Id));

            foreach (var c in opponentCards)
                description += CardTemplate.LoadFromId(c).Name + ", ";

            if (CardTemplate.LoadFromId(choice).Name == "The Coin")
                return 500;

            for (int i = 0; i < opponentCards.Count; i++)
            {
                if (opponentCards[i] == choice)
                    return 200 - i;
            }
            return 0;
        }

        // Beetlemancy, March of the Lich King
        private double Beetlemancy(Card.Cards choice, Board board)
        {
            if (choice == Card.Cards.RLK_654t)
                return board.MinionFriend.Count < 6 ? 60 : 40;

            return 50;
        }

        // Drum Circle, Festival of Legends
        private double DrumCircle(Card.Cards choice, Board board)
        {
            int points = board.MinionFriend.Count;
            switch (choice)
            {
                case Card.Cards.ETC_373b:
                    if (EnemyHasLethal(board) && points > 0)
                        return 106 + points;
                    return 100 + points;
                case Card.Cards.ETC_373a:
                    return 107 - points;
                default:
                    return points;
            }
        }

        // Peaceful Piper, Festival of Legends
        private double PeacefulPiper(Card.Cards choice, Board board)
        {
            switch (choice)
            {
                case Card.Cards.ETC_375a:
                    return board.Deck.Any(card =>
                        CardTemplate.LoadFromId(card).Races.Contains(Card.CRace.PET))
                        ? 100
                        : 10;
                case Card.Cards.ETC_375b:
                    return 50;
                default:
                    return 10;
            }
        }

        // Fight Over Me, Festival of Legends, Choose two enemy minions. They fight! Add copies of any that die to your hand.
        private double FightOverMe(Card.Cards choice, Board board)
        {
            var opponentCards = board.MinionEnemy
                .FindAll(x => x.Type == Card.CType.MINION)
                .OrderByDescending(x => x.CurrentAtk + x.CurrentHealth)
                .ToList();

            for (int i = 0; i < opponentCards.Count; i++)
            {
                if (opponentCards[i].Template.Id == choice)
                    return 200 - i;
            }

            return 10;
        }

        // Freya, Keeper of Nature, Titans
        private double Freya(Card.Cards choice, Board board)
        {
            double totalCost = 0;
            double totalCount = 0;

            switch (choice)
            {
                case Card.Cards.TTN_940a:
                    if (!board.MinionFriend.Any()) return 0;
                    totalCost = board.MinionFriend.Sum(x => x.CurrentCost);
                    totalCount = board.MinionFriend.Count;
                    break;
                case Card.Cards.TTN_940b:
                    if (!board.Hand.Any()) return 0;
                    totalCost = board.Hand.Sum(x => x.CurrentCost);
                    totalCount = board.Hand.Count;
                    break;
            }

            return totalCount > 0 ? totalCost / totalCount : 0;
        }

        // *********** Version and deck helpers ***********

        private void FileVersionCheck()
        {
            string updaterPath = Path.Combine(smartBotDirectory, "DiscoverMulliganUpdater.exe");

            if (File.Exists(updaterPath))
            {
                string newVersion = NewVersion();
                string currentVersion = CurrentVersion();

                if (!string.Equals(currentVersion, newVersion, StringComparison.Ordinal))
                {
                    Process.Start(updaterPath);
                    Bot.Log("[PLUGIN] -> EvilEyesDiscovery: Updating Files ...");
                }
            }
        }

        // Generate version based on number of Mondays since Jan 1, 2025
        private static string NewVersion()
        {
            DateTime start = new DateTime(2025, 1, 1);
            DateTime end = DateTime.Today;

            int daysUntilMonday = ((int)DayOfWeek.Monday - (int)start.DayOfWeek + 7) % 7;
            DateTime firstMonday = start.AddDays(daysUntilMonday);

            int mondays = firstMonday > end ? 0 : ((end - firstMonday).Days / 7) + 1;
            double result = mondays / 100.0 + 300;
            return result.ToString("F2", CultureInfo.InvariantCulture);
        }

        // Get current version from EE_Information.txt
        private string CurrentVersion()
        {
            string infoPath = Path.Combine(discoverCCDirectory, "EE_Information.txt");
            if (!File.Exists(infoPath))
                return null;

            string firstLine = File.ReadLines(infoPath).FirstOrDefault();
            if (string.IsNullOrEmpty(firstLine))
                return null;

            int open = firstLine.IndexOf('(');
            int close = firstLine.IndexOf(')', open + 1);
            if (open >= 0 && close > open)
                return firstLine.Substring(open + 1, close - open - 1);

            // fallback to regex if format differs
            Match match = Regex.Match(firstLine, @"\((.+?)\)");
            return match.Success ? match.Groups[1].Value : null;
        }

        private void CleanupIniFiles()
        {
            string flagFile = Path.Combine(discoverCCDirectory, "DiscoverFlag.json");

            // Already ran once?
            if (File.Exists(flagFile))
                return;

            foreach (var folder in new[] { "Standard", "Wild", "Arena" })
            {
                var path = Path.Combine(discoverCCDirectory, folder);
                if (!Directory.Exists(path)) continue;

                try
                {
                    foreach (var file in Directory.GetFiles(path, "*.ini"))
                        File.Delete(file);
                }
                catch
                {
                    break; // stop on first failure
                }
            }

            // Create flag file
            File.WriteAllText(flagFile, "1");
        }

        // Calculate the current deck composition by removing cards in hand, on board, and in graveyard from the original deck list
        private static List<Card.Cards> CurrentDeck(Board board)
        {
            var playedList = board.Hand.Select(h => h.Template.Id)
                .Concat(board.MinionFriend.Select(m => m.Template.Id))
                .Concat(board.FriendGraveyard)
                .ToList();

            var remaining = board.Deck.ToList();

            foreach (var id in playedList)
                remaining.Remove(id);   // removes only one copy

            return remaining;
        }

        // *********** Board calculations ***********
        // Calculate total attack of all friendly minions and weapon, which is crucial for assessing lethal damage and board threats
        private static int CurrentFriendAttack(Board board)
        {
            int minionAttack = board.MinionFriend
                .FindAll(x => x.CanAttack &&
                              (x.IsCharge || x.NumTurnsInPlay != 0) &&
                              x.CountAttack == 0 &&
                              !x.IsTired)
                .Sum(x => x.CurrentAtk);

            int weaponAttack = board.HasWeapon(true) && board.HeroFriend.CountAttack == 0
                ? board.WeaponFriend.CurrentAtk
                : 0;

            return minionAttack + weaponAttack;
        }

        // Calculate total defense of friendly board including hero and taunt minions, which is important for assessing survivability and prioritizing targets
        private static int CurrentFriendDefense(Board board)
        {
            return board.HeroFriend.CurrentHealth +
                   board.HeroFriend.CurrentArmor +
                   board.MinionFriend.FindAll(x => x.IsTaunt).Sum(x => x.CurrentHealth);
        }

        // Calculate total defense of enemy board including hero and taunt minions, which is important for assessing lethal damage and prioritizing targets
        private static int CurrentEnemyBoardDefense(Board board)
        {
            return board.HeroEnemy.CurrentHealth +
                   board.HeroEnemy.CurrentArmor +
                   board.MinionEnemy.FindAll(x => x.IsTaunt).Sum(x => x.CurrentHealth);
        }

        // Calculate total defense of enemy hero, which is important for assessing lethal damage and prioritizing targets
        private static int CurrentEnemyHeroDefense(Board board)
        {
            return board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor;
        }

        // Calculate total attack of all enemy minions and weapon, which is crucial for assessing lethal damage and board threats
        private static int CurrentEnemyAttack(Board board)
        {
            int minionAttack = board.MinionEnemy
                .FindAll(x => x.CanAttack &&
                              (x.IsCharge || x.NumTurnsInPlay != 0) &&
                              x.CountAttack == 0 &&
                              !x.IsTired)
                .Sum(x => x.CurrentAtk);

            int weaponAttack = board.HasWeapon(false) && board.HeroEnemy.CountAttack == 0
                ? board.WeaponEnemy.CurrentAtk
                : 0;

            return minionAttack + weaponAttack;
        }

        // Calculate total health of all enemy minions, which can be relevant for certain card effects and to assess board state
        private static int CurrentEnemyBoardHealth(Board board)
        {
            return board.MinionEnemy.FindAll(x => x.CurrentHealth > 0).Sum(x => x.CurrentHealth);
        }

        // Check if enemy has lethal damage available, considering taunt minions and hero defense
        private static bool EnemyHasLethal(Board board)
        {
            if (board.MinionFriend.Any(x => x.IsTaunt))
                return false;

            int heroDefense = board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor;
            return heroDefense <= CurrentEnemyAttack(board);
        }

        // Calculate a score based on how closely the card's cost matches the available mana, which can help prioritize cards that are more likely to be played immediately
        private static int CalculateScore(int cost, int mana)
        {
            if (mana <= 0)
                return 0; // avoid division by zero

            int difference = Math.Abs(cost - mana);

            // Score = 100 * (1 - (difference / total))
            double score = 100.0 * (1.0 - ((double)difference / (double)mana));

            // Clamp to 0–100
            if (score < 0)
                score = 0;
            if (score > 100)
                score = 100;

            return (int)Math.Round(score);
        }

        // Last chance to play an affordable card, especially if it can lead to lethal damage or prevent defeat, which is a critical decision point in the game
        private double LastChance(Card.Cards card, double points, Board board)
        {
            var cardTemplate = CardTemplate.LoadFromId(card);

            if (cardTemplate.Charge &&
                CurrentEnemyBoardDefense(board) <= CurrentFriendAttack(board) + cardTemplate.Atk)
            {
                description = "Possible enemy defeat, selecting charge card";
                points = 1000 + cardTemplate.Atk;
            }

            if (cardTemplate.Taunt && EnemyHasLethal(board))
            {
                description = "Enemy has lethal, selecting taunt card";
                points = 1000 + cardTemplate.Health;
            }

            return points;
        }

        // Get popularity of a specific value card under a parent card
        private static double? GetDiscoverScore(string json, string parentCardId, string valueCardId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }
                var root = JsonConvert.DeserializeObject<List<DiscoverChoice>>(json);
                return root?.FirstOrDefault(x => x.CardId != null && x.CardId.Equals(parentCardId, StringComparison.OrdinalIgnoreCase))?.Values?
                    .FirstOrDefault(v => v.CardId != null && v.CardId.Equals(valueCardId, StringComparison.OrdinalIgnoreCase))?.DiscoverScore;
            }
            catch
            {
                return null;
            }
        }

        // Get class-specific numeric value for a card from discover.JSON
        private static double? GetDiscoverValue(string json, string @class, string cardId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }
                var entries = JsonConvert.DeserializeObject<List<DiscoverClassEntry>>(json);
                var card = entries?.FirstOrDefault(e => string.Equals(e.CardId, cardId, StringComparison.OrdinalIgnoreCase));
                if (card.Classes != null && card.Classes.TryGetValue(@class, out var value))
                    return value;
                else
                    return null;
            }
            catch
            {
                return null;
            }
        }

        // Check if there is any discover choice with the given origin card id
        private static bool OriginCardExists(string json, string originCard)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json))
                {
                    return false;
                }
                var root = JsonConvert.DeserializeObject<List<DiscoverChoice>>(json);
                return root != null && root.Any(choice => string.Equals(choice.CardId, originCard, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        // Helper to read a JSON file and return its contents as a string
        private static string ReadJsonFile(String path)
        {
            return File.ReadAllText(path);
        }

        // *** Newtonsoft.Json models***
        // ============================================================
        // These classes are used to deserialize the JSON files containing discover choice popularity and class-specific values.

        public sealed class DiscoverChoice
        {
            // Origin card id of the discover choice
            [JsonProperty("card_id")]
            public string CardId { get; set; }

            // List of possible discovered cards and their popularity
            [JsonProperty("values")]
            public List<DiscoverValue> Values { get; set; } = new List<DiscoverValue>();
        }

        public sealed class DiscoverValue
        {
            // Card id of a discovered option
            [JsonProperty("card_id")]
            public string CardId { get; set; }

            // Popularity value associated with this option
            [JsonProperty("discover_score")]
            public double DiscoverScore { get; set; }
        }

        // Class to represent entries in discover.json which map card ids to class-specific numeric values
        public sealed class DiscoverClassEntry
        {
            // Card id of the entry
            [JsonProperty("card_id")]
            public string CardId { get; set; }

            // Map from class name (e.g., "DEATHKNIGHT") to numeric value
            [JsonProperty("classes")]
            public Dictionary<string, double> Classes { get; set; }
        }
    }
}
