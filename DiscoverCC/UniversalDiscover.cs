using SmartBot.Database;
using SmartBot.Discover;
using SmartBot.Plugins.API;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

// by Evil_Eyes

namespace UniversalDiscover
{
    public class UniversalDiscover : DiscoverPickHandler
    {
        // 用于对手场上来源校正的卡牌列表
        private static List<Card.Cards> enemyCards = new List<Card.Cards>
        {
            Card.Cards.REV_000, // Suspicious Alchemist
            Card.Cards.REV_002, // Suspicious Usher
            Card.Cards.REV_006, // Suspicious Pirate
            Card.Cards.NX2_044, // Suspicious Peddler
            Card.Cards.MIS_916 // Pro Gamer, Challenge your opponent to a game of Rock-Paper-Scissors! The winner draws 2 cards.
        };

        // 用于友方场上来源校正的卡牌列表
        private static List<Card.Cards> friendCards = new List<Card.Cards>
        {
            Card.Cards.GDB_874, // Astrobiologist
            Card.Cards.TTN_429, // Aman'Thul
            Card.Cards.TOY_801, // Chia Drake Miniaturize
            Card.Cards.TOY_801t, // Chia Drake Mini
            Card.Cards.BG31_BOB, // Bob the Bartender
            Card.Cards.WON_103 // Chamber of Viscidus
        };

        // 图腾列表：失误图腾、立体声图腾、融合乐团、远古图腾、点唱机图腾、锚定图腾、火舌图腾、火舌图腾、深渊融合怪、图腾魔像、巨化图腾、罪石图腾、派对图腾、派对图腾、空气之怒图腾、灼热图腾、治疗图腾、石爪图腾、力量图腾、大图腾艾索尔 -> 暗月马戏团、大图腾艾索尔 -> 未知系列、树人图腾、戏法图腾、图腾巨歌、邪恶图腾、毒蛇守卫、鱼人图腾、活力图腾、法力之潮图腾
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

        // 全局变量声明
        private static readonly Random random = new Random();

        // 版本控制变量
        private static bool versionChecked, discard;

        // Log variable
        private StringBuilder logBuilder = new StringBuilder();

        // Directory paths
        private readonly string smartBotDirectory = Directory.GetCurrentDirectory();
        private readonly string discoverCCDirectory = Directory.GetCurrentDirectory() + @"\DiscoverCC\";

        // Ini 文件处理器和日志描述
        private IniManager iniTierList;

        // 卡牌定义库的版本信息
        private string description;

        // Card Handle Pick Decision from SB
        public Card.Cards HandlePickDecision(Card.Cards originCard, List<Card.Cards> choices, Board board) // originCard; ID of card played by SB: choices; names of cards for selection: board; 3 states , Even, Losing, Winning
        {
            // Starting index
            int startIndex = 0;

            // 从云端获取卡牌定义库的最新版本
            if (!versionChecked)
            {
                FileVersionCheck();
                versionChecked = true;
            }

            // 机器人日志标题
            logBuilder.Clear();
            logBuilder.Append("=====Discover V9.3, Card definition V").Append(CurrentVersion()).Append("===EE");
            string Divider = new string('=', 40);

            // 未找到卡牌时的最终随机选择
            Card.Cards bestChoice = choices[random.Next(0, choices.Count)];

            // 获取当前牌组模式
            string mode = CurrentMode();

            // 获取当前英雄职业
            string hero = board.FriendClass.ToString();

            // 来源卡牌检查和校正
            (originCard, startIndex) = OriginCardCorrection(originCard, choices, board, mode);

            // 来自数据库模板的来源卡牌名称
            string Origin_Card = CardTemplate.LoadFromId(originCard).Name;

            // 创建空卡牌列表
            List<CardValue> choicesCardValue = new List<CardValue>();

            // 主循环从这里开始
            double points = 0;
            double TotalPoints = 0;
            try
            {
                for (int choiceIndex = startIndex; choiceIndex < 3; choiceIndex++)
                {
                    string discoverFile = string.Empty;
                    // 输入文件选择
                    switch (choiceIndex)
                    {
                        case 1:
                            // discoverCCDirectory + mode + originCard.ini
                            discoverFile = Path.Combine(discoverCCDirectory, mode, originCard + ".ini");
                            if (!File.Exists(discoverFile))
                                continue;
                            description = $"From: {discoverCCDirectory}{mode}\\{Origin_Card}";
                            break;
                        case 2:
                            // discoverCCDirectory + mode + discover.ini
                            discoverFile = Path.Combine(discoverCCDirectory, mode, "discover.ini");
                            description = $"Origin: {Origin_Card}\nFrom: {discoverCCDirectory}{mode}\\discover.ini";
                            break;
                    }
                    // 如果存在则加载 ini 文件
                    if (File.Exists(discoverFile))
                        iniTierList = new IniManager(discoverFile);

                    // 清除之前的卡牌值
                    choicesCardValue.Clear();
                    points = 0; // ensure points reset per choice
                    discard = false;

                    // 搜索最佳分数
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
                                            discoverFile = Path.Combine(discoverCCDirectory, mode, originCard + ".ini");
                                            if (File.Exists(discoverFile))
                                                iniTierList = new IniManager(discoverFile);
                                            discard = true;
                                        }
                                        // Searching file "origin.ini" for best points
                                        double.TryParse(iniTierList.GetString(choice.ToString(), "points", "0"), NumberStyles.Any, CultureInfo.InvariantCulture, out double p);
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
                                                                                                // 评估选择卡牌
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
                                        double discardScore = 2 * Math.Max(board.MinionFriend.Count - 2, 0) + 3 * Math.Max(board.ManaAvailable, 0); // Heuristic: Discarding cards is more valuable when you have more minions and mana available, with a threshold to avoid overvaluing discard when you have very few minions.
                                        double manaLossScore = 2 * Math.Max(board.MinionFriend.Count, 0) + 3 * Math.Max(board.ManaAvailable, 0); // Heuristic: Losing mana is more impactful when you have more minions and mana available, but less so than discard, with a threshold to avoid overvaluing mana loss when you have very few minions.
                                        // 评估选择卡牌
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
                                // 从外部文件搜索最佳分数
                                if (iniTierList != null)
                                    double.TryParse(iniTierList.GetString(choice.ToString(), "points", "0"), NumberStyles.Any, CultureInfo.InvariantCulture, out points);
                                break;
                            case 2:
                                // Searching file "discover.ini" for best points
                                if (iniTierList != null)
                                    double.TryParse(iniTierList.GetString(choice.ToString(), hero, "0"), NumberStyles.Any, CultureInfo.InvariantCulture, out points);
                                break;
                        }

                        // Suspicious Alchemist: fix condition
                        // 如果敌方场上有可疑的炼金术师且该炼金术师揭示了与我们选择匹配的卡牌（尽力启发式）
                        if (board.MinionEnemy.Any(m => m.Template.Id == Card.Cards.REV_000) && board.EnemyGraveyard.Contains(choice))
                        {
                            description = $"Suspicious Alchemist possible opponent selected card {cardTemplate.Name}";
                            points += 500;
                        }

                        // 最后一次机会打出负担得起的卡牌
                        if (cardTemplate.Cost <= board.ManaAvailable)
                        {
                            points = LastChance(choice, points, board);
                        }

                        // 将卡牌连同分数加入卡牌列表
                        choicesCardValue.Add(new CardValue(choice, points));
                        TotalPoints += points;
                    }
                    if (TotalPoints > 0) break;
                }

                // 选择分数最高的卡牌
                double bestPoints = 0;
                for (var i = 0; i < choicesCardValue.Count; i++) // index through each card
                {
                    double pts = Math.Round(choicesCardValue[i].GetPoints(), 2); // round to 2 decimal places
                    AddLog($"{i + 1}) {CardTemplate.LoadFromId(choicesCardValue[i].GetCard()).Name}: {pts:F2}");  // log with 2 decimals
                    if (!(bestPoints < pts)) continue; // selects highest points
                    bestChoice = choicesCardValue[i].GetCard();
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

        // 来源卡牌校正
        private Tuple<Card.Cards, int> OriginCardCorrection(Card.Cards originCard, List<Card.Cards> choices, Board board, string mode)
        {
            // 用于校正的来源卡牌列表
            var originChoices = new List<Card.Cards>();

            if (mode == "Arena")
                return Tuple.Create(originCard, 2);

            if (!File.Exists($"{discoverCCDirectory}{mode}\\{originCard}.ini"))
            {
                // 添加匹配 enemyCards 的敌方卡牌  
                originChoices.AddRange(board.MinionEnemy.Select(card => card.Template.Id).Where(enemyCards.Contains));

                // 添加匹配 friendCards 的友方卡牌  
                originChoices.AddRange(board.MinionFriend.Select(card => card.Template.Id).Where(friendCards.Contains));

                // 添加上一张打出的卡牌  
                if (board.PlayedCards.Any())
                    originChoices.Add(board.PlayedCards.Last());

                // 为匹配选择来源卡牌  
                foreach (var card in originChoices)
                {
                    if (originCard == card)
                        break; // No correction needed
                    if (File.Exists(discoverCCDirectory + mode + "\\" + card + ".ini"))
                    {
                        AddLog($"Origin card correction: {CardTemplate.LoadFromId(card).Name}");
                        return Tuple.Create(card, 0);
                    }
                }
            }

            return Tuple.Create(originCard, 0);
        }

        // 从列表获取
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

        // Memory management, input/output operations
        public class IniManager
        {
            private const int CSize = 1024;

            public IniManager(string path)
            {
                Path = path;
            }

            public string Path { get; set; }

            public string GetString(string section, string key, string Default = null)
            {
                StringBuilder buffer = new StringBuilder(CSize);
                GetString(section, key, Default, buffer, CSize, Path);
                return buffer.ToString();
            }

            public void WriteString(string section, string key, string sValue)
            {
                WriteString(section, key, sValue, Path);
            }

            [DllImport("kernel32.dll", EntryPoint = "GetPrivateProfileString")]
            private static extern int GetString(string section, string key, string def, StringBuilder bufer, int size, string path);

            [DllImport("kernel32.dll", EntryPoint = "WritePrivateProfileString")]
            private static extern int WriteString(string section, string key, string str, string path);
        }

        // 将文本添加到日志变量
        private void AddLog(string entry)
        {
            logBuilder.Append("\r\n").Append(entry);
        }

        // 获取当前牌组模式 from selected deck string
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
                // 对敌方随从造成伤害 vs 给己方随从增加生命值
                if (board.MinionFriend.Count >= board.MinionEnemy.Count)
                    kazakusCards.Find(x => x.Name == "Wildvine").Superior += 100;
                return kazakusCards.Find(x => x.Name == kazakusCard).Superior;
            }

            // Select Greater Golem  if equal or more than 4 mana
            if (board.MaxMana >= 4)
            {
                // 对敌方随从造成伤害 vs 给己方随从增加生命值
                if (board.MinionFriend.Count >= board.MinionEnemy.Count)
                    kazakusCards.Find(x => x.Name == "Wildvine").Greater += 100;
                return kazakusCards.Find(x => x.Name == kazakusCard).Greater;
            }

            // Default Lesser Golem
            // 对敌方随从造成伤害 vs 给己方随从增加生命值
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

        // 猜测重量，暗月马戏团的疯狂
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
            // 获取当前牌组中的卡牌列表
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
            // 创建新空列表，添加对手墓地和场上的卡牌
            var _opponentCards = (from _card in board.EnemyGraveyard select _card).ToList(); // First options
            _opponentCards.AddRange(from _card in board.MinionEnemy select _card.Template.Id);
            // 输出到机器人日志
            foreach (var _card in _opponentCards)
            {
                // Bot.Log("Card: " + CardTemplate.LoadFromId(card).Name);
                description += CardTemplate.LoadFromId(_card).Name + ", ";
            }
            // 第一个可能的选择，幸运币
            if (CardTemplate.LoadFromId(choice).Name == "The Coin")
                return 500;
            // 第二个可能的选择，为匹配的卡牌应用分数
            foreach (var card in _opponentCards)
            {
                if (card == choice)
                    return 200 - _opponentCards.IndexOf(card); // Subtract index of _opponentCards list in order of opponent cards in Graveyard --> board
            }
            // 如果未找到卡牌，尝试外部文件
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
        // 卡牌定义版本检查。如有需要则更新文件
        private void FileVersionCheck()
        {
            // 构建文件路径
            string updaterPath = Path.Combine(smartBotDirectory, "DiscoverMulliganUpdater.exe");

            // 检查更新器是否存在
            if (File.Exists(updaterPath))
            {
                string newVersion = NewVersion();
                string currentVersion = CurrentVersion();

                // 如果版本已更改则启动更新器
                if (currentVersion != newVersion)
                {
                    Process.Start(updaterPath);
                    Bot.Log("[PLUGIN] -> EvilEyesDiscovery: Updating Files ...");
                }
            }
        }

        // 根据自2025年1月1日以来的周一数量生成版本
        private static string NewVersion()
        {
            DateTime start = new DateTime(2025, 1, 1);
            DateTime end = DateTime.Today;

            int daysUntilMonday = ((int)DayOfWeek.Monday - (int)start.DayOfWeek + 7) % 7;
            DateTime firstMonday = start.AddDays(daysUntilMonday);

            int mondays = firstMonday > end ? 0 : ((end - firstMonday).Days / 7) + 1;
            double result = mondays / 100.0 + 300;
            string newVersion = result.ToString("F2", CultureInfo.InvariantCulture);
            return newVersion;
        }

        // 从 EE_Information.txt 获取当前版本
        private string CurrentVersion()
        {
            string infoPath = Path.Combine(discoverCCDirectory, "EE_Information.txt");

            // 读取第一行 and extract version number using regex
            string firstLine = File.ReadLines(infoPath).First();
            Match match = Regex.Match(firstLine, @"\((.+?)\)");
            if (match.Success)
                return match.Groups[1].Value;
            return null;
        }

        // 返回当前牌组中剩余卡牌列表
        private static List<Card.Cards> CurrentDeck(Board board)
        {
            var played = new HashSet<Card.Cards>(
            board.Hand.Select(h => h.Template.Id)
            .Concat(board.MinionFriend.Select(m => m.Template.Id))
            .Concat(board.FriendGraveyard));

            return board.Deck.Where(card => !played.Contains(card)).ToList();
        }

        // Board calculations
        // 计算友方攻击值
        private static int CurrentFriendAttack(Board board)
        {
            return (board.MinionFriend.FindAll(x => x.CanAttack && (x.IsCharge || x.NumTurnsInPlay != 0) && x.CountAttack == 0 && !x.IsTired).Sum(x => x.CurrentAtk) + (board.HasWeapon(true) && board.HeroFriend.CountAttack == 0 ? board.WeaponFriend.CurrentAtk : 0));
        }

        // 计算友方防御值（护甲、生命值和嘲讽）
        private static int CurrentFriendDefense(Board board)
        {
            return board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor + (board.MinionFriend.FindAll(x => x.IsTaunt == true).Sum(x => x.CurrentHealth));
        }

        // 计算对手场上防御值（护甲、生命值和嘲讽值）
        private static int CurrentEnemyBoardDefense(Board board)
        {
            return board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor + (board.MinionEnemy.FindAll(x => x.IsTaunt == true).Sum(x => x.CurrentHealth));
        }

        // 计算对手英雄防御值（护甲和生命值）
        private static int CurrentEnemyHeroDefense(Board board)
        {
            return board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor;
        }

        // 计算对手攻击值
        private static int CurrentEnemyAttack(Board board)
        {
            return board.MinionEnemy.FindAll(x => x.CanAttack && (x.IsCharge || x.NumTurnsInPlay != 0) && x.CountAttack == 0 && !x.IsTired).Sum(x => x.CurrentAtk) + (board.HasWeapon(false) && board.HeroEnemy.CountAttack == 0 ? board.WeaponEnemy.CurrentAtk : 0);
        }

        // 计算对手场上生命值
        private static int CurrentEnemyBoardHealth(Board board)
        {
            return board.MinionEnemy.FindAll(x => x.CurrentHealth > 0).Sum(x => x.CurrentHealth);
        }

        // 检查敌方是否有斩杀
        private static bool EnemyHasLethal(Board board)
        {
            if (board.MinionFriend.Any(x => x.IsTaunt)) return false;
            return board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor <=
                   board.MinionEnemy.FindAll(
                       x => x.CanAttack && (x.IsCharge || x.NumTurnsInPlay != 0) && x.CountAttack == 0 && !x.IsTired)
                       .Sum(x => x.CurrentAtk) +
                   (board.HasWeapon(false) && board.HeroEnemy.CountAttack == 0 ? board.WeaponEnemy.CurrentAtk : 0);
        }

        // 最后一张制胜卡牌
        private double LastChance(Card.Cards card, double points, Board board)
        {
            // Declare variables
            var cardTemplate = CardTemplate.LoadFromId(card);

            // 卡牌有冲锋且能击杀对手英雄
            if (cardTemplate.Charge && CurrentEnemyBoardDefense(board) <= (CurrentFriendAttack(board) + cardTemplate.Atk))
            {
                description = "Possible enemy defeat, selecting charge card";
                points = 1000 + cardTemplate.Atk;
            }

            // 如果卡牌有嘲讽且敌方有斩杀
            if (cardTemplate.Taunt && EnemyHasLethal(board))
            {
                description = "Enemy has lethal, selecting taunt card";
                points = 1000 + cardTemplate.Health;
            }
            return points;
        }
    }
}
