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
using System.Threading;

// by Evil_Eyes

namespace UniversalDiscover
{
    public class UniversalDiscover : DiscoverPickHandler
    {
        // 发现逻辑版本号（弃牌术/颜射术专项）：仅 Discover 自己的独立版本号；每次改动请递增。
        // tip: 项目是 c#6 不要用新语法
        private const string DiscardWarlockDiscoverVersion = "DW-DISC-093"; // 梦魇之王发现机制并轨到TLC_451

        // 蛋术专项：凯洛斯的蛋阶段（用于发现“破蛋手段”优先级）
        private static readonly HashSet<Card.Cards> _eggStages = new HashSet<Card.Cards>
        {
            Card.Cards.DINO_410,
            Card.Cards.DINO_410t2,
            Card.Cards.DINO_410t3,
            Card.Cards.DINO_410t4,
            Card.Cards.DINO_410t5,
        };

        // 蛋术专项：场上有蛋时，优先发现“触发蛋”的组件（按用户指定顺序）。
        private static readonly Card.Cards[] _eggPopToolsPriority = new[]
        {
            Card.Cards.AV_312,   // 献祭召唤者
            Card.Cards.CS3_002,  // 末日仪式
            Card.Cards.TRL_249,  // 残酷集结
            Card.Cards.BAR_910,  // 牺牲魔典
            Card.Cards.LOOT_017, // 黑暗契约
            Card.Cards.VAC_939,  // 吃掉小鬼！
        };

        private static bool HasEggOnFriendlyBoard(Board board)
        {
            try
            {
                if (board == null || board.MinionFriend == null) return false;
                return board.MinionFriend.Any(m => m != null && m.Template != null && _eggStages.Contains(m.Template.Id));
            }
            catch
            {
                return false;
            }
        }

        private static int GetDefileMaxContiguousHealthChain(Board board)
        {
            try
            {
                if (board == null) return 0;
                var healthSet = new HashSet<int>();

                if (board.MinionFriend != null)
                {
                    foreach (var m in board.MinionFriend)
                    {
                        if (m == null) continue;
                        if (m.CurrentHealth > 0) healthSet.Add(m.CurrentHealth);
                    }
                }

                if (board.MinionEnemy != null)
                {
                    foreach (var m in board.MinionEnemy)
                    {
                        if (m == null) continue;
                        if (m.CurrentHealth > 0) healthSet.Add(m.CurrentHealth);
                    }
                }

                int n = 0;
                while (healthSet.Contains(n + 1)) n++;
                return n;
            }
            catch
            {
                return 0;
            }
        }

        private static bool IsEggPopToolPlayableNow(Board board, Card.Cards id, int virtualRemainingManaNow)
        {
            try
            {
                var t = CardTemplate.LoadFromId(id);
                int cost = t != null ? t.Cost : 99;
                if (cost > virtualRemainingManaNow) return false;

                // 献祭召唤者：需要场上有蛋，且要有随从位可下。
                if (id == Card.Cards.AV_312)
                {
                    if (!HasEggOnFriendlyBoard(board)) return false;
                    int friendCount = board != null && board.MinionFriend != null
                        ? board.MinionFriend.Count(m => m != null && m.Template != null)
                        : 0;
                    if (friendCount >= 7) return false;
                    return true;
                }

                // 其余“触发蛋”法术都要求场上先有蛋。
                if (id == Card.Cards.CS3_002
                    || id == Card.Cards.TRL_249
                    || id == Card.Cards.BAR_910
                    || id == Card.Cards.LOOT_017
                    || id == Card.Cards.VAC_939)
                {
                    return HasEggOnFriendlyBoard(board);
                }

                // 混乱吞噬需要敌方有随从
                if (id == Card.Cards.TTN_932)
                {
                    return board != null && board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null);
                }

                // 亵渎需要递增链（至少到3）
                if (id == Card.Cards.ICC_041)
                {
                    return GetDefileMaxContiguousHealthChain(board) >= 3;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

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
            Card.Cards.TOY_801t // Chia Drake Mini
        };

        // Global variables declaration
        private readonly string smartBotDirectory = Directory.GetCurrentDirectory();
        private readonly string discoverCCDirectory = Directory.GetCurrentDirectory() + @"\DiscoverCC\";
        private static readonly Random random = new Random();
        private IniManager iniTierList;
        private string description, log;
        private bool oneShot;
        // Unified safety threshold for BT_300 (古尔丹之手) when considering cave/discover picks
        // 窟穴发现：手牌<=6时可选择古尔丹之手（窟穴弃1+发现1=手牌不变，选BT_300后抽3张容易爆牌）
        // 过期商贩发现：手牌<=8时可选择古尔丹之手（发现+1，打出后最多9+3=12有安全空间）
        private const int Bt300SafeThresholdForCave = 6;       // 窟穴专用阈值
        private const int Bt300SafeThresholdForMerchant = 8;  // 过期商贩专用阈值

        // 弃牌术专项：地标/窟穴弃牌时的“临时被弃组件”标识（按回合）。
        // 目的：如果某个被弃组件在使用地标前就已经是临时牌，那么它本来就会消失/被弃掉，
        // 因此使用地标时优先弃掉其它组件，避免浪费弃牌收益。
        private static int _dwTempPayoffMarkedTurn = int.MinValue;
        private static readonly HashSet<Card.Cards> _dwTempPayoffsMarkedThisTurn = new HashSet<Card.Cards>();
        // 跨回合记录“曾在手中出现过的临时灵魂弹幕实体ID”，用于修正发现侧牌库弹幕估算。
        private static int _dwTempBarrageTrackTurn = int.MinValue;
        private static readonly HashSet<int> _dwSeenTemporaryBarrageEntityIds = new HashSet<int>();
        // 最近一次可用局面下的“牌库剩余灵魂弹幕估算”，用于 board==null 的发现兜底。
        private static int _dwLastKnownBarrageRemainEstimate = -1;
        // 最近一次可用局面的“当回合可用费用(含硬币)”快照；用于 board==null 时的 TLC_451 费用判断。
        private static int _dwLastKnownRemainingManaNow = 0;
        private static bool _dwLastKnownHasCoinNow = false;
        private static int _dwLastKnownVirtualRemainingManaNow = 0;
        private static bool _dwHasManaSnapshot = false;

        private static bool IsPureLearningModeEnabledCompat()
        {
            return InvokeDecisionRuntimeModeBool("IsPureLearningModeEnabled", true);
        }

        private static bool AllowLiveTeacherFallbackCompat()
        {
            return InvokeDecisionRuntimeModeBool("AllowLiveTeacherFallback", true);
        }

        private static bool AllowLegacyDiscoverFallbackCompat()
        {
            return InvokeDecisionRuntimeModeBool("AllowLegacyDiscoverFallback", false);
        }

        private static bool InvokeDecisionRuntimeModeBool(string methodName, bool fallback)
        {
            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var runtimeType = assembly.GetType("SmartBotProfiles.DecisionRuntimeMode", false);
                    if (runtimeType == null)
                        continue;

                    var method = runtimeType.GetMethod(methodName, Type.EmptyTypes);
                    if (method == null)
                        continue;

                    object result = method.Invoke(null, null);
                    if (result is bool)
                        return (bool)result;
                }
            }
            catch
            {
                // ignore
            }

            return fallback;
        }

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
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var memoryType = assembly.GetType("SmartBotProfiles.DecisionDiscoverMemory", false);
                    if (memoryType == null)
                        continue;

                    var method = memoryType.GetMethod(
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
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var memoryType = assembly.GetType("SmartBotProfiles.DecisionDiscoverMemory", false);
                    if (memoryType == null)
                        continue;

                    var method = memoryType.GetMethod(
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

        private static int GetVirtualRemainingManaForDiscover(Board board, out int remainingManaNow, out bool hasCoinNow, out bool fromSnapshot)
        {
            remainingManaNow = 0;
            hasCoinNow = false;
            fromSnapshot = false;

            try
            {
                Board manaBoard = board;
                if (manaBoard == null)
                {
                    try { manaBoard = Bot.CurrentBoard; } catch { manaBoard = null; }
                }

                if (manaBoard != null)
                {
                    remainingManaNow = Math.Max(0, manaBoard.ManaAvailable);
                    hasCoinNow = HasCardInHand(manaBoard, Card.Cards.GAME_005);

                    int virtualRemainingManaNow = remainingManaNow + (hasCoinNow ? 1 : 0);
                    if (virtualRemainingManaNow > 10) virtualRemainingManaNow = 10;

                    _dwLastKnownRemainingManaNow = remainingManaNow;
                    _dwLastKnownHasCoinNow = hasCoinNow;
                    _dwLastKnownVirtualRemainingManaNow = virtualRemainingManaNow;
                    _dwHasManaSnapshot = true;
                    // 当输入 board 为空但读取到了 Bot.CurrentBoard，也视作“快照可用”。
                    fromSnapshot = (board == null);
                    return virtualRemainingManaNow;
                }
            }
            catch
            {
                // ignore
            }

            if (!_dwHasManaSnapshot)
            {
                remainingManaNow = 0;
                hasCoinNow = false;
                fromSnapshot = false;
                return 0;
            }

            fromSnapshot = true;
            remainingManaNow = _dwLastKnownRemainingManaNow;
            hasCoinNow = _dwLastKnownHasCoinNow;
            return _dwLastKnownVirtualRemainingManaNow;
        }

        private static bool IsTlc451LikeDiscoverOrigin(Card.Cards originCard)
        {
            return originCard == Card.Cards.TLC_451 || originCard == Card.Cards.EDR_856;
        }

        private static int? TryGetIntProperty(object obj, params string[] propertyNames)
        {
            try
            {
                if (obj == null || propertyNames == null || propertyNames.Length == 0) return null;
                var t = obj.GetType();
                foreach (var name in propertyNames)
                {
                    try
                    {
                        var p = t.GetProperty(name);
                        if (p == null) continue;
                        var v = p.GetValue(obj, null);
                        if (v == null) continue;
                        if (v is int) return (int)v;
                        if (v is short) return (short)v;
                        if (v is byte) return (byte)v;
                        if (v is bool) return (bool)v ? 1 : 0;
                        int parsed;
                        if (int.TryParse(v.ToString(), out parsed)) return parsed;
                        bool parsedBool;
                        if (bool.TryParse(v.ToString(), out parsedBool)) return parsedBool ? 1 : 0;
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
            catch
            {
                // ignore
            }
            return null;
        }

        private static int TryReadGameTag(object obj, Card.GAME_TAG tag, int fallbackValue)
        {
            try
            {
                if (obj == null) return fallbackValue;

                var t = obj.GetType();
                var getTag = t.GetMethod("GetTag", new[] { typeof(Card.GAME_TAG) });
                if (getTag != null)
                {
                    var v = getTag.Invoke(obj, new object[] { tag });
                    if (v != null)
                    {
                        if (v is int) return (int)v;
                        if (v is short) return (short)v;
                        if (v is byte) return (byte)v;
                        if (v is bool) return (bool)v ? 1 : 0;
                        int parsed;
                        if (int.TryParse(v.ToString(), out parsed)) return parsed;
                        bool parsedBool;
                        if (bool.TryParse(v.ToString(), out parsedBool)) return parsedBool ? 1 : 0;
                    }
                }

                // 兼容某些实现把标签暴露成属性（例如 Exhausted）
                var exhausted = TryGetIntProperty(obj, "Exhausted", "EXHAUSTED", "IsExhausted");
                if (exhausted.HasValue) return exhausted.Value;
            }
            catch
            {
                // ignore
            }
            return fallbackValue;
        }

        private static int GetBoardTurn(Board board)
        {
            try
            {
                var turn = TryGetIntProperty(board, "Turn", "TurnNumber", "CurrentTurn", "TurnCount", "GameTurn");
                return turn.HasValue ? turn.Value : -1;
            }
            catch
            {
                return -1;
            }
        }

        private static bool IsDiscardPayoffId(Card.Cards id)
        {
            return id == Card.Cards.RLK_532
                   || id == Card.Cards.WON_098
                   || id == Card.Cards.KAR_205
                   || id == Card.Cards.WW_441
                   || id == Card.Cards.WW_044t
                   || id == Card.Cards.AT_022
                   || id == Card.Cards.RLK_534
                   || id == Card.Cards.BT_300;
        }

        // “诚信商家格里伏塔”(VAC_959) 给双方的护符牌：忽略其对“全手被弃组件”口径的干扰。
        private static bool IsHonestMerchantCharm(Card.Cards id)
        {
            try
            {
                return id.ToString().StartsWith("VAC_959t");
            }
            catch
            {
                return false;
            }
        }

        // 从主策略复制：动态判定“被弃组件”集合（用于发现选牌口径一致化）
        // 说明：这些组件主要通过弃掉获得收益，不需要立即打出；但会受场面格位/手牌爆牌/武器攻击窗口影响。
        private static HashSet<Card.Cards> GetDiscardComponentsConsideringHand(Board board, int furnaceFuelMaxHand)
        {
            var set = new HashSet<Card.Cards>
            {
                Card.Cards.RLK_534, // 灵魂弹幕
                Card.Cards.AT_022,  // 加拉克苏斯之拳
                Card.Cards.WW_044t, // 淤泥桶
            };

            try
            {
                int handCount = 0;
                try { handCount = board != null && board.Hand != null ? board.Hand.Count : 0; } catch { handCount = 0; }

                int friendMinions = 0;
                try { friendMinions = board != null && board.MinionFriend != null ? board.MinionFriend.Count : 0; } catch { friendMinions = 0; }

                // 行尸/镀银魔像：只有随从位<=6时才算“被弃组件”（否则弃掉也召不出来，等价于浪费）
                if (friendMinions <= 6)
                {
                    set.Add(Card.Cards.RLK_532); // 行尸
                    set.Add(Card.Cards.WON_098); // 镀银魔像
                    set.Add(Card.Cards.KAR_205); // 镀银魔像（旧版）
                }

                // 古尔丹之手：只有手牌<=8时才算“被弃组件”（避免弃到后抽3爆牌/节奏崩）
                // 最新口径：只要已装备武器且英雄可攻击，就禁用古尔丹之手(BT_300)作为被弃组件/决策触发点。
                bool weaponEquippedAndHeroCanAttack = false;
                try
                {
                    weaponEquippedAndHeroCanAttack = board != null
                        && board.WeaponFriend != null
                        && board.HeroFriend != null
                        && board.HeroFriend.CanAttack;
                }
                catch { weaponEquippedAndHeroCanAttack = false; }

                if (!weaponEquippedAndHeroCanAttack && handCount > 0 && handCount <= Bt300SafeThresholdForMerchant)
                {
                    set.Add(Card.Cards.BT_300);
                }

                // 锅炉燃料：只有手牌数量<=设定值时才算被弃组件
                if (handCount > 0 && handCount <= furnaceFuelMaxHand)
                {
                    set.Add(Card.Cards.WW_441);
                }
            }
            catch { }

            return set;
        }

        private static bool IsDiscardPayoffRatioAbove(Board board, HashSet<Card.Cards> payoffSet, int thresholdPercent, out int payoffCount, out int handCount)
        {
            payoffCount = 0;
            handCount = 0;
            try
            {
                if (board == null || board.Hand == null || payoffSet == null || payoffSet.Count == 0) return false;

                var handForRatio = board.Hand
                    .Where(h => h != null && h.Template != null
                        && h.Template.Id != Card.Cards.GAME_005
                        && !IsHonestMerchantCharm(h.Template.Id))
                    .ToList();

                handCount = handForRatio.Count;
                if (handCount <= 0) return false;

                payoffCount = handForRatio.Count(h => payoffSet.Contains(h.Template.Id));
                return payoffCount * 100 > handCount * thresholdPercent;
            }
            catch
            {
                payoffCount = 0;
                handCount = 0;
                return false;
            }
        }

        private static bool IsTemporaryCardInstance(SmartBot.Plugins.API.Card c)
        {
            try
            {
                if (c == null || c.Template == null) return false;
                if (c.HasTag(SmartBot.Plugins.API.Card.GAME_TAG.LUNAHIGHLIGHTHINT)) return true;

                // 兼容：保留此前对“灵魂弹幕临时牌”的启发式判定
                if (c.Template.Id == Card.Cards.RLK_534)
                {
                    int baseCost = c.Template != null ? c.Template.Cost : 99;
                    if (c.CurrentCost < baseCost) return true;
                }
            }
            catch
            {
                // ignore
            }
            return false;
        }

        private static bool IsTemporaryPayoffInHand(Board board, Card.Cards id)
        {
            try
            {
                if (board == null || board.Hand == null) return false;
                foreach (var c in board.Hand)
                {
                    if (c == null || c.Template == null) continue;
                    if (c.Template.Id != id) continue;
                    if (IsTemporaryCardInstance(c)) return true;
                }
            }
            catch
            {
                // ignore
            }
            return false;
        }

        private static void RefreshDiscardWarlockTempPayoffsMarker(Board board)
        {
            try
            {
                if (board == null || board.Hand == null) return;
                int turn = GetBoardTurn(board);
                if (turn != _dwTempPayoffMarkedTurn)
                {
                    _dwTempPayoffMarkedTurn = turn;
                    _dwTempPayoffsMarkedThisTurn.Clear();
                }

                foreach (var c in board.Hand)
                {
                    if (c == null || c.Template == null) continue;
                    var id = c.Template.Id;
                    if (!IsDiscardPayoffId(id)) continue;
                    if (!IsTemporaryCardInstance(c)) continue;
                    _dwTempPayoffsMarkedThisTurn.Add(id);
                }
            }
            catch
            {
                // ignore
            }
        }

        private static bool IsDiscoverChoicePlayableNow(Card.Cards id, int virtualRemainingManaNow)
        {
            try
            {
                var t = CardTemplate.LoadFromId(id);
                int cost = t != null ? t.Cost : 99;
                return cost <= virtualRemainingManaNow;
            }
            catch
            {
                return false;
            }
        }

        private static bool CardTemplateHasRush(CardTemplate t)
        {
            if (t == null) return false;

            // 兼容不同 API 字段名：优先读 HasRush，次选 Rush。
            try
            {
                var p = t.GetType().GetProperty("HasRush");
                if (p != null && p.PropertyType == typeof(bool))
                {
                    object v = p.GetValue(t, null);
                    if (v is bool) return (bool)v;
                }
            }
            catch
            {
                // ignore
            }

            try
            {
                var p = t.GetType().GetProperty("Rush");
                if (p != null && p.PropertyType == typeof(bool))
                {
                    object v = p.GetValue(t, null);
                    if (v is bool) return (bool)v;
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        // TLC_451 专用费用口径：
        // 奇利亚斯豪华版3000型按“7-友方随从数”动态计费（最低0）。
        // 这样发现阶段与实战扣费口径一致，避免误判“当回合可落地”。
        private static int GetTlc451EffectiveDiscoverCost(Board board, Card.Cards id)
        {
            try
            {
                if (id == Card.Cards.TOY_330t5 || id == Card.Cards.TOY_330)
                {
                    int friendMinions = CountFriendlyMinions(board);
                    int dynamicCost = 7 - friendMinions;
                    if (dynamicCost < 0) dynamicCost = 0;
                    return dynamicCost;
                }

                var t = CardTemplate.LoadFromId(id);
                int cost = t != null ? t.Cost : 99;
                return cost;
            }
            catch
            {
                return 99;
            }
        }

        private static bool IsTlc451DiscoverChoicePlayableNow(Board board, Card.Cards id, int virtualRemainingManaNow)
        {
            return GetTlc451EffectiveDiscoverCost(board, id) <= virtualRemainingManaNow;
        }

        // TLC_451 低费发现口径：当回合可用费用较低时，优先可立即落地的过牌随从。
        // 默认优先级：狗头人图书管理员 > 栉龙 > 微型机器人(含核心版)。
        private static Card.Cards PickLowCostDrawMinionForTlc451(Board board, List<Card.Cards> choices, int virtualRemainingManaNow, bool ignoreManaGate = false)
        {
            try
            {
                if (choices == null || choices.Count == 0) return default(Card.Cards);

                var priority = new[]
                {
                    Card.Cards.LOOT_014,
                    Card.Cards.TLC_603,
                    Card.Cards.BOT_568,
                    Card.Cards.CORE_BOT_568,
                };

                foreach (var id in priority)
                {
                    if (!choices.Contains(id)) continue;
                    if (ignoreManaGate || IsTlc451DiscoverChoicePlayableNow(board, id, virtualRemainingManaNow))
                        return id;
                }
            }
            catch
            {
                // ignore
            }

            return default(Card.Cards);
        }

        private static int GetFriendlyMinionTribeTypeCount(Board board)
        {
            try
            {
                if (board == null || board.MinionFriend == null) return 0;

                var races = new HashSet<Card.CRace>();
                foreach (var m in board.MinionFriend)
                {
                    if (m == null || m.Template == null || m.Template.Races == null) continue;
                    foreach (var race in m.Template.Races)
                    {
                        string raceName = race.ToString();
                        if (string.IsNullOrEmpty(raceName)) continue;
                        if (raceName.Equals("INVALID", StringComparison.OrdinalIgnoreCase)) continue;
                        if (raceName.Equals("NONE", StringComparison.OrdinalIgnoreCase)) continue;
                        if (raceName.Equals("ALL", StringComparison.OrdinalIgnoreCase)) continue;
                        races.Add(race);
                    }
                }

                return races.Count;
            }
            catch
            {
                return 0;
            }
        }

        // TLC_451 末段兜底：基于当前局面做细粒度择优，避免直接取 safeChoices[0]。
        // 约束：
        // 1) 保留前置硬规则（禁选/费用门槛/核心连段）；
        // 2) 这里只处理“剩余候选”的相对优先级；
        // 3) 特别考虑场面格子、手牌紧张与是否需要即时解压。
        private static Card.Cards PickContextualTlc451Fallback(
            Board board,
            List<Card.Cards> choices,
            int virtualRemainingManaNow,
            bool manaUnknownForNullBoard,
            int handCount)
        {
            try
            {
                if (choices == null || choices.Count == 0) return default(Card.Cards);

                int friendMinions = CountFriendlyMinions(board);
                int enemyMinions = CountEnemyMinions(board);
                int enemyLowHp = CountEnemyLowHealthMinions(board, 3);
                int friendTribes = GetFriendlyMinionTribeTypeCount(board);
                int freeSlots = Math.Max(0, 7 - friendMinions);
                int turn = -1;
                try { turn = GetBoardTurn(board); } catch { turn = -1; }
                bool lateGameDiscoverWindow = virtualRemainingManaNow >= 7 || turn >= 8;
                int attackableFriendMinions = 0;
                try
                {
                    attackableFriendMinions = board != null && board.MinionFriend != null
                        ? board.MinionFriend.Count(m => m != null && m.CanAttack && m.CurrentAtk > 0)
                        : 0;
                }
                catch { attackableFriendMinions = 0; }

                // 用户口径：墓发现优先拿恶兆邪火；仅在续连熵能已是明显高收益窗口时让位续连。
                bool entropyHighValueWindow = choices.Contains(Card.Cards.TIME_026)
                    && enemyMinions == 0
                    && friendMinions >= 5
                    && attackableFriendMinions >= 5
                    && friendTribes >= 4;
                if (choices.Contains(Card.Cards.GDB_121))
                {
                    var forebodingTpl = CardTemplate.LoadFromId(Card.Cards.GDB_121);
                    int forebodingCost = forebodingTpl != null ? forebodingTpl.Cost : 99;
                    if (forebodingCost <= virtualRemainingManaNow && !entropyHighValueWindow)
                    {
                        Bot.Log("[Discover] 咒怨发现(TLC_451)：硬规则命中（恶兆邪火优先，续连非高收益窗口）-> 选择恶兆邪火(GDB_121)");
                        return Card.Cards.GDB_121;
                    }
                }

                // 用户硬规则：咒怨之墓发现时，我方随从>=3 且可当回合落地时，优先选择 buff（续连熵能）。
                if (friendMinions >= 3 && choices.Contains(Card.Cards.TIME_026))
                {
                    var entropyTpl = CardTemplate.LoadFromId(Card.Cards.TIME_026);
                    int entropyCost = entropyTpl != null ? entropyTpl.Cost : 99;
                    if (entropyCost <= virtualRemainingManaNow)
                    {
                        Bot.Log("[Discover] 咒怨发现(TLC_451)：硬规则命中（友方随从>=3）-> 优先选择续连熵能(TIME_026)");
                        return Card.Cards.TIME_026;
                    }
                }

                int friendAtk = 0;
                int enemyAtk = 0;
                int lethalDeficit = 99;
                bool underPressure = false;
                try { if (board != null) friendAtk = CurrentFriendAttack(board); } catch { friendAtk = 0; }
                try { if (board != null) enemyAtk = CurrentEnemyAttack(board); } catch { enemyAtk = 0; }
                try { lethalDeficit = EstimateLethalDeficit(board); } catch { lethalDeficit = 99; }
                try { underPressure = IsBoardUnderPressure(board); } catch { underPressure = false; }

                int bestScore = int.MinValue;
                Card.Cards bestId = default(Card.Cards);

                foreach (var id in choices.Distinct())
                {
                    int score = 0;
                    var reasons = new List<string>();

                    var t = CardTemplate.LoadFromId(id);
                    int cost = GetTlc451EffectiveDiscoverCost(board, id);
                    bool playableNow = cost <= virtualRemainingManaNow;
                    if (id == Card.Cards.TOY_330t5 || id == Card.Cards.TOY_330)
                        reasons.Add("奇利亚斯动态费=7-友方随从(" + friendMinions + ")");

                    if (playableNow)
                    {
                        score += Math.Max(10, 120 - (cost * 11));
                        reasons.Add("当回合可落地");
                    }
                    else
                    {
                        score -= (80 + cost * 5);
                        reasons.Add("当回合不可落地");
                    }

                    if (t != null)
                    {
                        if (t.Type == Card.CType.MINION)
                        {
                            score += 60;
                            reasons.Add("随从基础");

                            if (freeSlots <= 0)
                            {
                                score -= 600;
                                reasons.Add("场满7格");
                            }
                            else if (friendMinions == 0)
                            {
                                score += 55;
                                reasons.Add("空场补站场");
                            }
                        }
                        else if (t.Type == Card.CType.SPELL)
                        {
                            score += friendMinions >= 3 ? 10 : -10;
                            reasons.Add(friendMinions >= 3 ? "法术有场面承接" : "法术承接偏弱");
                        }
                    }

                    // 用户口径：后期（7费窗口及以后）咒怨发现应更偏向高费价值牌，
                    // 避免默认拿1费小随从拖慢上限；高压且可立即补嘲讽时例外放宽。
                    if (lateGameDiscoverWindow)
                    {
                        if (cost >= 4)
                        {
                            score += 105;
                            reasons.Add("后期窗口优先高费价值");
                        }
                        else if (cost <= 1 && t != null && t.Type == Card.CType.MINION)
                        {
                            bool tauntEmergencyException = underPressure && t.Taunt;
                            if (!tauntEmergencyException)
                            {
                                score -= 260;
                                reasons.Add("后期避免1费随从");
                            }
                            else
                            {
                                reasons.Add("后期低费随从例外：高压嘲讽补位");
                            }
                        }
                        else if (cost <= 2)
                        {
                            score -= 70;
                            reasons.Add("后期降低低费发现");
                        }
                    }

                    // 奇利亚斯专项：不仅看“友方随从折扣”，还要看“当前可用费用是否能顺滑落地并衔接后续动作”。
                    if (id == Card.Cards.TOY_330t5 || id == Card.Cards.TOY_330)
                    {
                        int dynamicCost = cost;
                        int manaMargin = virtualRemainingManaNow - dynamicCost;

                        if (playableNow)
                        {
                            // 友方随从越多，动态减费越稳定，且输能模块光环收益越高。
                            int discountBonus = Math.Min(90, friendMinions * 22);
                            score += discountBonus;
                            reasons.Add("奇利亚斯折扣收益+" + discountBonus);

                            if (friendMinions >= 4)
                            {
                                score += 36;
                                reasons.Add("友方随从>=4，奇利亚斯光环收益高");
                            }
                            else if (friendMinions >= 3)
                            {
                                score += 24;
                                reasons.Add("友方随从>=3，奇利亚斯收益可观");
                            }
                            else if (friendMinions <= 1)
                            {
                                score -= 35;
                                reasons.Add("友方随从过少，奇利亚斯收益受限");
                            }

                            // 当前可用费用越宽裕，越容易同回合衔接动作，实战价值更高。
                            if (manaMargin >= 2)
                            {
                                score += 30;
                                reasons.Add("当前可用费用富余(" + manaMargin + ")，可衔接后续动作");
                            }
                            else if (manaMargin == 1)
                            {
                                score += 18;
                                reasons.Add("当前可用费用富余1点，节奏顺滑");
                            }
                            else
                            {
                                score += 6;
                                reasons.Add("当前可用费用刚好贴费可落地");
                            }
                        }
                        else
                        {
                            score -= 120;
                            reasons.Add("奇利亚斯当回合不可落地");
                        }
                    }

                    // 关键牌定向修正
                    if (id == Card.Cards.TIME_026) // 续连熵能
                    {
                        if (friendMinions >= 5) { score += 120; reasons.Add("续连友方>=5"); }
                        else if (friendMinions >= 3) { score += 80; reasons.Add("续连友方>=3"); }
                        else if (friendMinions == 2) { score += 35; reasons.Add("续连友方=2"); }
                        else if (friendMinions <= 1) { score -= 120; reasons.Add("续连场面过少"); }
                    }

                    if (id == Card.Cards.TLC_254) // 始祖龟
                    {
                        if (friendTribes >= 2) { score += 70; reasons.Add("始祖龟种族>=2"); }
                        else { score -= 45; reasons.Add("始祖龟种族<2"); }
                    }

                    if (id == Card.Cards.VAC_940) // 派对邪犬
                    {
                        if (friendMinions > 4)
                        {
                            score -= 220;
                            reasons.Add("派对邪犬场上>4后置");
                        }
                        else if (freeSlots < 3)
                        {
                            score -= 120;
                            reasons.Add("派对邪犬格子不足3");
                        }
                        else
                        {
                            score += 65;
                            reasons.Add("派对邪犬滚雪球窗口");
                        }
                    }

                    if (id == Card.Cards.GDB_123) // 挟持射线
                    {
                        bool earlyTurn = virtualRemainingManaNow <= 3;
                        bool shouldResource = handCount <= 2 && !earlyTurn;
                        bool haveCheapBoardPlays = choices.Any(c =>
                        {
                            if (c == Card.Cards.GDB_123) return false;
                            var tc = CardTemplate.LoadFromId(c);
                            if (tc == null) return false;
                            return tc.Cost <= 2;
                        });

                        if (earlyTurn && haveCheapBoardPlays)
                        {
                            score -= 120;
                            reasons.Add("射线前期且有低费展开");
                        }
                        else if (shouldResource)
                        {
                            score += 55;
                            reasons.Add("射线后期补资源");
                        }
                        else
                        {
                            score -= 30;
                            reasons.Add("射线暂缓");
                        }
                    }

                    if (id == Card.Cards.TOY_916) // 速写美术家
                    {
                        if (virtualRemainingManaNow < 4)
                        {
                            score -= 140;
                            reasons.Add("速写<4费禁前置");
                        }
                        else
                        {
                            score += 45;
                            reasons.Add("速写可用费窗口");
                        }
                    }

                    if (id == Card.Cards.CORE_EX1_319
                        || id == Card.Cards.CORE_ULD_723
                        || id == Card.Cards.CORE_UNG_205
                        || id == Card.Cards.CORE_TSC_827
                        || id == Card.Cards.TLC_603
                        || id == Card.Cards.CORE_CS2_231)
                    {
                        score += 38;
                        reasons.Add("低费曲线友好");
                    }

                    // 恶兆邪火是套外恶魔引擎：优先建立减费线，不仅限于“空场优势”。
                    if (id == Card.Cards.GDB_121)
                    {
                        if (enemyMinions == 0 && friendMinions >= 3)
                        {
                            score += 95;
                            reasons.Add("恶兆引擎优先");
                        }
                        else if (enemyMinions >= 1 && friendMinions >= 2)
                        {
                            score += 75;
                            reasons.Add("有敌方随从时提前铺邪火引擎");
                        }
                        else if (friendMinions >= 1)
                        {
                            score += 45;
                            reasons.Add("提前建立邪火减费线");
                        }
                    }

                    // 优势空场时，不强推烈焰小鬼自伤；让位给更稳健的引擎牌/不自伤节奏牌。
                    if (id == Card.Cards.CORE_EX1_319 && enemyMinions == 0 && friendMinions >= 3)
                    {
                        score -= 65;
                        reasons.Add("优势空场避免自伤");
                    }

                    // 压力局：更偏向即时解压/抗压；优势局：偏向扩大场攻。
                    if (underPressure)
                    {
                        if (id == Card.Cards.CORE_CS2_062 || id == Card.Cards.TIME_027 || id == Card.Cards.EX1_308)
                        {
                            score += 85;
                            reasons.Add("压力局解压法术");
                        }
                        if (id == Card.Cards.GDB_121)
                        {
                            score += 40;
                            reasons.Add("压力局补站场");
                        }
                    }
                    else
                    {
                        if (lethalDeficit > 0 && lethalDeficit <= 8
                            && (id == Card.Cards.EX1_308 || id == Card.Cards.TIME_027 || id == Card.Cards.RLK_534))
                        {
                            score += 95;
                            reasons.Add("补斩杀伤害");
                        }
                        if (friendAtk >= enemyAtk && friendMinions >= enemyMinions)
                        {
                            if (id == Card.Cards.TIME_026 || id == Card.Cards.GDB_121 || id == Card.Cards.CORE_EX1_319)
                            {
                                score += 35;
                                reasons.Add("优势局扩场");
                            }
                        }
                    }

                    if (enemyMinions == 0 && friendMinions >= 2 && id == Card.Cards.TIME_026)
                    {
                        score += 25;
                        reasons.Add("空场滚雪球");
                    }

                    if (enemyMinions >= 2 && enemyLowHp >= 2 && (id == Card.Cards.CORE_CS2_062 || id == Card.Cards.TIME_027))
                    {
                        score += 50;
                        reasons.Add("敌方低血群体");
                    }

                    Bot.Log("[Discover] 咒怨发现(TLC_451)：上下文评分 "
                        + SafeCardName(id) + "(" + id + ")="
                        + score
                        + " | hand=" + handCount
                        + ",friend=" + friendMinions
                        + ",enemy=" + enemyMinions
                        + ",freeSlots=" + freeSlots
                        + ",tribes=" + friendTribes
                        + ",underPressure=" + (underPressure ? "Y" : "N")
                        + " | " + string.Join(";", reasons));

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestId = id;
                    }
                }

                if (!bestId.Equals(default(Card.Cards)))
                {
                    Bot.Log("[Discover] 咒怨发现(TLC_451)：上下文评分最终选择 -> "
                        + SafeCardName(bestId) + "(" + bestId + ") score=" + bestScore);
                    return bestId;
                }
            }
            catch
            {
                // ignore
            }

            return default(Card.Cards);
        }

        private static void UpdateTemporaryBarrageTracking(Board board)
        {
            try
            {
                if (board == null || board.Hand == null) return;

                int turn = GetBoardTurn(board);
                bool shouldReset =
                    (turn >= 0 && turn <= 1 && _dwTempBarrageTrackTurn > 1)
                    || (turn >= 0 && _dwTempBarrageTrackTurn >= 0 && turn < _dwTempBarrageTrackTurn);
                if (shouldReset)
                    _dwSeenTemporaryBarrageEntityIds.Clear();
                if (turn >= 0)
                    _dwTempBarrageTrackTurn = turn;

                foreach (var c in board.Hand)
                {
                    if (c == null || c.Template == null) continue;
                    if (c.Template.Id != Card.Cards.RLK_534) continue;
                    if (!IsTemporaryCardInstance(c)) continue;
                    _dwSeenTemporaryBarrageEntityIds.Add(c.Id);
                }
            }
            catch
            {
                // ignore
            }
        }

        private static bool ShouldAvoidCaveDiscardBecauseAlreadyTemporary(Board board, Card.Cards id)
        {
            try
            {
                if (!IsDiscardPayoffId(id)) return false;
                if (_dwTempPayoffsMarkedThisTurn.Contains(id)) return true;
                return IsTemporaryPayoffInHand(board, id);
            }
            catch
            {
                return false;
            }
        }

        private static Card.Cards PickDiscardWarlockCaveDiscard(Board board, List<Card.Cards> choices, string logPrefix)
        {
            bool _keepBarrageForClawLocal = false;
            bool hasClawEquipped = false;
            bool bt300Safe = false;
            bool furnaceFuelSafeInLandmarkDiscard = false;
            bool preferDiscardBarrageWhenHandHasTwo = false;
            int barrageInHandCount = 0;
            int guldanInHandCount = 0;
            Func<Card.Cards, bool> isSafeDiscardChoice = c =>
                (c != Card.Cards.BT_300 || bt300Safe)
                && (c != Card.Cards.WW_441 || furnaceFuelSafeInLandmarkDiscard);
            try
            {
                if (choices == null || choices.Count == 0) return default(Card.Cards);
                RefreshDiscardWarlockTempPayoffsMarker(board);

                string choiceList = string.Empty;
                try
                {
                    choiceList = string.Join(", ", choices.Select(c => SafeCardName(c) + "(" + c + ")"));
                }
                catch
                {
                    choiceList = string.Join(", ", choices.Select(c => c.ToString()));
                }

                int handCount = board != null && board.Hand != null ? board.Hand.Count : 0;
                bt300Safe = handCount > 0 && handCount <= Bt300SafeThresholdForCave;
                Bot.Log(logPrefix + "：BT_300阈值检查 handCount=" + handCount
                    + " threshold=" + Bt300SafeThresholdForCave
                    + " => " + (bt300Safe ? "allow" : "deny"));
                
               
                
                // 锅炉燃料(WW_441)弃掉会抽2：为避免爆牌，地标/窟穴弃牌场景下手牌<=6
                // 说明：6+2=8不爆牌（手牌容量10）
                furnaceFuelSafeInLandmarkDiscard = handCount > 0 && handCount <= 6;

                int virtualManaNowForClaw = board != null ? board.ManaAvailable : 0;
                try
                {
                    bool hasCoinNow = board != null && board.Hand != null
                        && board.Hand.Any(c => c != null && c.Template != null && c.Template.Id == Card.Cards.GAME_005 && c.CurrentCost <= board.ManaAvailable);
                    if (hasCoinNow) virtualManaNowForClaw = Math.Min(10, virtualManaNowForClaw + 1);
                }
                catch { }
                try
                {
                    hasClawEquipped = board != null
                        && board.WeaponFriend != null
                        && board.WeaponFriend.Template != null
                        && board.WeaponFriend.Template.Id == Card.Cards.END_016;
                }
                catch { hasClawEquipped = false; }
                _keepBarrageForClawLocal = CanTriggerClawBarrageThisTurn(board, virtualManaNowForClaw);
                try
                {
                    barrageInHandCount = board != null && board.Hand != null
                        ? board.Hand.Count(c => c != null && c.Template != null && c.Template.Id == Card.Cards.RLK_534)
                        : 0;
                }
                catch { barrageInHandCount = 0; }
                try
                {
                    guldanInHandCount = board != null && board.Hand != null
                        ? board.Hand.Count(c => c != null && c.Template != null && c.Template.Id == Card.Cards.BT_300)
                        : 0;
                }
                catch { guldanInHandCount = 0; }

                bool preferDiscardBarrageByBarrageCount = barrageInHandCount >= 2 && choices.Contains(Card.Cards.RLK_534);
                bool preferDiscardBarrageByDoubleGuldan = guldanInHandCount >= 2 && choices.Contains(Card.Cards.RLK_534);
                bool preferDiscardBarrageByMerchantCombo = false;
                bool preferDiscardBarrageByMerchantEmergency = false;
                try
                {
                    int myHpArmor = board != null && board.HeroFriend != null
                        ? board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor
                        : 30;
                    bool canMerchantBarrageSoon = CanStablyMerchantBarrageAndLikelyDraw(board, virtualManaNowForClaw, false);
                    preferDiscardBarrageByMerchantCombo = choices.Contains(Card.Cards.RLK_534)
                        && canMerchantBarrageSoon;
                    preferDiscardBarrageByMerchantEmergency = choices.Contains(Card.Cards.RLK_534)
                        && myHpArmor <= 12
                        && canMerchantBarrageSoon
                        && !_keepBarrageForClawLocal;
                }
                catch
                {
                    preferDiscardBarrageByMerchantCombo = false;
                    preferDiscardBarrageByMerchantEmergency = false;
                }

                preferDiscardBarrageWhenHandHasTwo = preferDiscardBarrageByBarrageCount
                    || preferDiscardBarrageByDoubleGuldan
                    || preferDiscardBarrageByMerchantCombo
                    || preferDiscardBarrageByMerchantEmergency;
                if (preferDiscardBarrageByBarrageCount)
                {
                    Bot.Log(logPrefix + "：手里已有灵魂弹幕x" + barrageInHandCount + "，放开“留给刀”限制，优先考虑弃灵魂弹幕");
                }
                else if (preferDiscardBarrageByDoubleGuldan)
                {
                    Bot.Log(logPrefix + "：手里古尔丹之手x" + guldanInHandCount + "，放开“留给刀”限制，优先考虑弃灵魂弹幕");
                }
                else if (preferDiscardBarrageByMerchantCombo)
                {
                    Bot.Log(logPrefix + "：可稳定商贩弃弹幕并较快过牌，放开“留给刀”限制，优先考虑弃灵魂弹幕");
                }
                else if (preferDiscardBarrageByMerchantEmergency)
                {
                    Bot.Log(logPrefix + "：血量偏低且商贩弃弹幕可较快成型，放开“留给刀”限制，优先考虑弃灵魂弹幕");
                }

                // 复杂评分分支：当三选里至少有2个被弃组件时，按局势动态评分。
                // 默认：多组件情况下优先弃“非灵魂弹幕”。
                // 例外：敌方低血随从较多，或补足斩杀缺口时，允许提高灵魂弹幕优先级。
                try
                {
                    var payoffChoicesForScore = choices
                        .Where(c => IsDiscardPayoffId(c) && isSafeDiscardChoice(c))
                        .Distinct()
                        .ToList();

                    if (payoffChoicesForScore.Count >= 2)
                    {
                        int friendMinionsNow = CountFriendlyMinions(board);
                        int enemyMinionsNow = CountEnemyMinions(board);
                        int enemyLowHpNow = CountEnemyLowHealthMinions(board, 3);
                        int lethalDeficitNow = EstimateLethalDeficit(board);

                        bool allowBarrageByLowHpBoard = enemyMinionsNow >= 2 && enemyLowHpNow >= 2;
                        bool allowBarrageByLethalGap = lethalDeficitNow > 0 && lethalDeficitNow <= 6;

                        bool reserveBarrageForMerchant = false;
                        bool reserveBarrageForClaw = false;
                        try
                        {
                            reserveBarrageForMerchant = CanStablyMerchantBarrageAndLikelyDraw(board, virtualManaNowForClaw, false);
                        }
                        catch { reserveBarrageForMerchant = false; }
                        reserveBarrageForClaw = !reserveBarrageForMerchant && _keepBarrageForClawLocal;

                        int bestScore = int.MinValue;
                        Card.Cards bestChoice = default(Card.Cards);

                        foreach (var id in payoffChoicesForScore)
                        {
                            if (id == Card.Cards.BT_300 && !bt300Safe) continue;
                            if (id == Card.Cards.WW_441 && !furnaceFuelSafeInLandmarkDiscard) continue;

                            int score = 0;
                            var reason = new List<string>();

                            if (id == Card.Cards.RLK_532)
                            {
                                score += 860;
                                reason.Add("行尸基础+860");
                                if (friendMinionsNow <= 6) { score += 280; reason.Add("友方<=6可召回+280"); }
                                else { score -= 650; reason.Add("友方>6位满风险-650"); }
                            }
                            else if (id == Card.Cards.WON_098 || id == Card.Cards.KAR_205)
                            {
                                score += 840;
                                reason.Add("镀银魔像基础+840");
                                if (friendMinionsNow <= 6) { score += 260; reason.Add("友方<=6可召回+260"); }
                                else { score -= 650; reason.Add("友方>6位满风险-650"); }
                            }
                            else if (id == Card.Cards.AT_022)
                            {
                                score += 760;
                                reason.Add("拳头基础+760");
                            }
                            else if (id == Card.Cards.WW_044t)
                            {
                                score += 740;
                                reason.Add("淤泥桶基础+740");
                            }
                            else if (id == Card.Cards.BT_300)
                            {
                                score += 700;
                                reason.Add("古手基础+700");
                            }
                            else if (id == Card.Cards.WW_441)
                            {
                                score += 660;
                                reason.Add("锅炉燃料基础+660");
                            }
                            else if (id == Card.Cards.RLK_534)
                            {
                                score += 620;
                                reason.Add("灵魂弹幕基础+620");
                                if (payoffChoicesForScore.Count >= 2) { score -= 220; reason.Add("多组件默认避弹幕-220"); }
                                if (allowBarrageByLowHpBoard) { score += 560; reason.Add("敌方低血随从多+560"); }
                                if (allowBarrageByLethalGap) { score += 780; reason.Add("补斩杀缺口+780"); }
                                if (preferDiscardBarrageWhenHandHasTwo) { score += 260; reason.Add("手里重复组件放行+260"); }

                                // 灵魂弹幕保留优先级：商贩 > 时空之爪。
                                if (!allowBarrageByLethalGap)
                                {
                                    if (reserveBarrageForMerchant)
                                    {
                                        score -= 1150;
                                        reason.Add("保留给商贩触发-1150");
                                    }
                                    else if (reserveBarrageForClaw)
                                    {
                                        score -= 800;
                                        reason.Add("保留给时空之爪触发-800");
                                    }
                                }
                            }
                            else
                            {
                                score += 200;
                                reason.Add("通用组件+200");
                            }

                            if (id != Card.Cards.RLK_534 && payoffChoicesForScore.Contains(Card.Cards.RLK_534))
                            {
                                score += 100;
                                reason.Add("同池有弹幕，非弹幕偏好+100");
                            }

                            if (ShouldAvoidCaveDiscardBecauseAlreadyTemporary(board, id))
                            {
                                bool hasOtherNonTempPayoff = payoffChoicesForScore.Any(c => c != id && !ShouldAvoidCaveDiscardBecauseAlreadyTemporary(board, c));
                                if (hasOtherNonTempPayoff)
                                {
                                    score -= 1500;
                                    reason.Add("已是临时且有其它非临时组件-1500");
                                }
                            }

                            Bot.Log(logPrefix + "：复杂评分 " + SafeCardName(id) + "(" + id + ") = " + score
                                + " | enemyMinions=" + enemyMinionsNow
                                + ",enemyLowHp=" + enemyLowHpNow
                                + ",lethalDeficit=" + lethalDeficitNow
                                + ",friendMinions=" + friendMinionsNow
                                + " | " + string.Join(";", reason));

                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestChoice = id;
                            }
                        }

                        if (!bestChoice.Equals(default(Card.Cards)))
                        {
                            Bot.Log(logPrefix + "：复杂评分最终选择弃牌 -> " + SafeCardName(bestChoice) + "(" + bestChoice + ") score=" + bestScore);
                            return bestChoice;
                        }
                    }
                }
                catch
                {
                    // ignore and fall back to fixed priority below
                }

                // 核心优先级顺序（根据是否装备时空之爪动态调整）
                // 1) 装备爪子：优先弃中低费收益(拳/桶等)，留高费（弹幕）给爪子砍脸触发。
                // 2) 未装爪子：优先弃高投返（弹幕）。
                Card.Cards[] priority;
                if (hasClawEquipped)
                {
                    // 顺序：拳 > 桶 > 行尸 > 魔像 > 弹幕 > 抽牌
                    priority = new[]
                    {
                        Card.Cards.AT_022,  // 贾拉克瑟斯之拳 (4 Dmg)
                        Card.Cards.WW_044t, // 淤泥桶 (4 Dmg)
                        Card.Cards.RLK_532, // 行尸 (Body)
                        Card.Cards.WON_098, // 镀银魔像
                        Card.Cards.KAR_205, // 镀银魔像
                        Card.Cards.RLK_534, // 灵魂弹幕 (6 Dmg - 后置留给爪子)
                        Card.Cards.BT_300,  // 古尔丹之手 (Draw 3)
                        Card.Cards.WW_441,  // 锅炉燃料 (Draw 2)
                    };
                }
                else
                {
                    // 顺序：弹幕 > 拳 > 桶 > 行尸 > 魔像 > 抽牌
                    priority = new[]
                    {
                        Card.Cards.RLK_534, // 灵魂弹幕 (6 Dmg)
                        Card.Cards.AT_022,  // 贾拉克瑟斯之拳 (4 Dmg)
                        Card.Cards.WW_044t, // 淤泥桶 (4 Dmg)
                        Card.Cards.RLK_532, // 行尸 (Body)
                        Card.Cards.WON_098, // 镀银魔像
                        Card.Cards.KAR_205, // 镀银魔像
                        Card.Cards.BT_300,  // 古尔丹之手 (Draw 3)
                        Card.Cards.WW_441,  // 锅炉燃料 (Draw 2)
                    };
                }

                bool hasAvailablePayoffInChoices = false;
                foreach (var id in priority)
                {
                    if (id == Card.Cards.BT_300 && !bt300Safe) continue;
                    if (id == Card.Cards.WW_441 && !furnaceFuelSafeInLandmarkDiscard) continue;
                    if (id == Card.Cards.RLK_534 && _keepBarrageForClawLocal && !preferDiscardBarrageWhenHandHasTwo
                        && choices.Any(c => c != Card.Cards.RLK_534))
                    {
                        Bot.Log(logPrefix + "：本回合可稳定触发时空之爪弃牌，跳过灵魂弹幕(RLK_534)，留给时空之爪触发");
                        continue;
                    }
                    if (choices.Contains(id))
                    {
                        hasAvailablePayoffInChoices = true;
                        break;
                    }
                }
                if (!hasAvailablePayoffInChoices)
                {
                    Bot.Log(logPrefix + "：当前三选不含可用被弃组件，候选=[" + choiceList + "]，进入低损失兜底");
                }

                // 1) 优先避开“使用地标前已是临时牌”的被弃组件
                // 特殊口径：三选弃牌中，如果某项是临时牌（默认它是从发现抽上来的，回合结束会自动弃掉），
                // 且三选中存在其它非临时的弃牌收益组件，则绝对避开该临时牌，去弃其它的组件以最大化收益。
                foreach (var id in priority)
                {
                    if (id == Card.Cards.BT_300 && !bt300Safe) continue;
                    if (id == Card.Cards.WW_441 && !furnaceFuelSafeInLandmarkDiscard) continue;
                    if (id == Card.Cards.RLK_534 && _keepBarrageForClawLocal && !preferDiscardBarrageWhenHandHasTwo
                        && choices.Any(c => c != Card.Cards.RLK_534))
                    {
                        continue;
                    }
                    if (!choices.Contains(id)) continue;
                    if (ShouldAvoidCaveDiscardBecauseAlreadyTemporary(board, id))
                    {
                        // 检查是否有其它非临时的收益组件可选
                        bool hasOtherNonTempPayoff = choices.Any(c => c != id && IsDiscardPayoffId(c) && !ShouldAvoidCaveDiscardBecauseAlreadyTemporary(board, c));
                        if (hasOtherNonTempPayoff)
                        {
                            Bot.Log(logPrefix + "：检测到 " + SafeCardName(id) + " 已是临时牌且有其它非临时收益，跳过之");
                            continue;
                        }
                    }

                    Bot.Log(logPrefix + "：按优先级选择弃牌 -> " + SafeCardName(id) + "(" + id + ")");
                    return id;
                }

                // 2) 如果三选全是临时组件（或仅剩临时），则仍按顺序回退（避免乱弃）
                foreach (var id in priority)
                {
                    if (id == Card.Cards.BT_300 && !bt300Safe) continue;
                    if (id == Card.Cards.WW_441 && !furnaceFuelSafeInLandmarkDiscard) continue;
                    if (id == Card.Cards.RLK_534 && _keepBarrageForClawLocal && !preferDiscardBarrageWhenHandHasTwo
                        && choices.Any(c => c != Card.Cards.RLK_534))
                    {
                        continue;
                    }
                    if (!choices.Contains(id)) continue;
                    Bot.Log(logPrefix + "：三选可能均为临时组件/或无更优选择，回退按优先级弃牌 -> " + SafeCardName(id) + "(" + id + ")");
                    return id;
                }
            }
            catch
            {
                // ignore
            }

            // 3) 再兜底：避开核心启动牌和武器
            try
            {
                if (!_keepBarrageForClawLocal && board != null)
                {
                    bool hasClawEquippedForFallback = board.WeaponFriend != null && board.WeaponFriend.Template != null
                        && board.WeaponFriend.Template.Id == Card.Cards.END_016;
                    bool hasClawInHand = board.Hand != null && board.Hand.Any(c => c != null && c.Template != null
                        && c.Template.Id == Card.Cards.END_016);
                    _keepBarrageForClawLocal = hasClawEquippedForFallback || hasClawInHand;
                }

                // 低损失兜底：优先弃硬币；若已持有/在场窟穴，允许弃窟穴；尽量保留郊狼/邪翼蝠/超光子等连段关键牌。
                if (choices.Contains(Card.Cards.GAME_005))
                {
                    Bot.Log(logPrefix + "：低损失兜底 -> 选择硬币(GAME_005)");
                    return Card.Cards.GAME_005;
                }

                bool hasCaveOnBoard = board != null
                    && board.MinionFriend != null
                    && board.MinionFriend.Any(m => m != null && m.Template != null && m.Template.Id == Card.Cards.WON_103);
                int caveInHandCount = board != null && board.Hand != null
                    ? board.Hand.Count(h => h != null && h.Template != null && h.Template.Id == Card.Cards.WON_103)
                    : 0;
                if (choices.Contains(Card.Cards.WON_103) && (hasCaveOnBoard || caveInHandCount >= 2))
                {
                    Bot.Log(logPrefix + "：低损失兜底 -> 已有窟穴，选择弃窟穴(WON_103)");
                    return Card.Cards.WON_103;
                }

                var neverDiscardInCaveLocal = new HashSet<Card.Cards>
                {
                    Card.Cards.END_016,
                    Card.Cards.ULD_163,
                    Card.Cards.TOY_916,
                    Card.Cards.TLC_451,
                    Card.Cards.TIME_047,
                    Card.Cards.YOD_032,
                    Card.Cards.TIME_027
                };
                if (_keepBarrageForClawLocal && !preferDiscardBarrageWhenHandHasTwo && choices.Any(c => c != Card.Cards.RLK_534))
                {
                    // 已有刀/手握刀时，尽量把弹幕留给爪子触发，兜底也避免选回弹幕。
                    neverDiscardInCaveLocal.Add(Card.Cards.RLK_534);
                }
                var alt = choices.FirstOrDefault(c => !neverDiscardInCaveLocal.Contains(c) && isSafeDiscardChoice(c));
                if (!alt.Equals(default(Card.Cards)))
                {
                    Bot.Log(logPrefix + "：低损失兜底 -> 选择 " + SafeCardName(alt) + "(" + alt + ")");
                    return alt;
                }

                // 最后一道兜底：若“留弹幕给刀”生效且还有非弹幕选项，强制不选弹幕。
                if (_keepBarrageForClawLocal && !preferDiscardBarrageWhenHandHasTwo)
                {
                    var nonBarrage = choices.FirstOrDefault(c => c != Card.Cards.RLK_534 && isSafeDiscardChoice(c));
                    if (!nonBarrage.Equals(default(Card.Cards)))
                    {
                        Bot.Log(logPrefix + "：低损失兜底(留弹幕给刀) -> 选择 " + SafeCardName(nonBarrage) + "(" + nonBarrage + ")");
                        return nonBarrage;
                    }
                    Bot.Log(logPrefix + "：skip_barrage_for_claw_failed_no_alternative（仅剩灵魂弹幕可选）");
                }
            }
            catch
            {
                // ignore
            }
						 // 早期节奏优先：如果三选有船载火炮(GVG_075)，手上有海盗，且费用足够打出火炮，优先选择火炮
                try
                {
                    if (choices.Contains(Card.Cards.GVG_075) && board != null)
                    {
                        int manaAvailable = board.ManaAvailable;
                        var cannonTemplate = CardTemplate.LoadFromId(Card.Cards.GVG_075);
                        int cannonCost = cannonTemplate != null ? cannonTemplate.Cost : 2;
                        
                        // 检查手上是否有海盗（使用Races属性）
                        bool hasPirateInHand = board.Hand != null && board.Hand.Any(c => 
                            c != null && c.Template != null && 
                            c.Template.Races != null && c.Template.Races.Contains(Card.CRace.PIRATE));
                        
                        // 费用足够且手上有海盗：优先选择船载火炮
                        if (hasPirateInHand && manaAvailable >= cannonCost)
                        {
                            Bot.Log(logPrefix + "：早期节奏优先 -> 手上有海盗且费用足够(" + manaAvailable + "费)，优先选择船载火炮(GVG_075)配合打节奏");
                            return Card.Cards.GVG_075;
                        }
                    }
                }
                catch
                {
                    // ignore
                }

            if (choices != null && choices.Count > 0)
            {
                var safeFirst = choices.FirstOrDefault(c => isSafeDiscardChoice(c));
                if (!safeFirst.Equals(default(Card.Cards)))
                {
                    Bot.Log(logPrefix + "：最终兜底(安全筛选) -> " + SafeCardName(safeFirst) + "(" + safeFirst + ")");
                    return safeFirst;
                }

                Bot.Log(logPrefix + "：最终兜底无安全项，回退到首项 -> " + SafeCardName(choices[0]) + "(" + choices[0] + ")");
                return choices[0];
            }
            return default(Card.Cards);
        }

        // 弃牌术发现口径：仅当“本回合可打商贩 + 最高费组(去商贩/硬币后)仅灵魂弹幕”时，视为稳定商贩弃弹幕线。
        private static bool CanStablyMerchantDiscardBarrage(Board board, int virtualRemainingManaNow, bool includeDiscoveredMerchant)
        {
            try
            {
                if (board == null || board.Hand == null) return false;
                if (virtualRemainingManaNow < 2) return false;

                int friendCount = 0;
                try
                {
                    friendCount = board.MinionFriend != null
                        ? board.MinionFriend.Count(m => m != null && m.Template != null)
                        : 0;
                }
                catch { friendCount = 0; }
                if (friendCount >= 7) return false;

                var hand = board.Hand.Where(h => h != null && h.Template != null).ToList();
                if (hand.Count == 0) return false;

                bool hasMerchant = hand.Any(h => h.Template.Id == Card.Cards.ULD_163);
                if (!hasMerchant && !includeDiscoveredMerchant) return false;

                var handWithoutMerchantAndCoin = hand.Where(h =>
                    h.Template.Id != Card.Cards.ULD_163
                    && h.Template.Id != Card.Cards.GAME_005
                    && !IsHonestMerchantCharm(h.Template.Id)).ToList();

                if (handWithoutMerchantAndCoin.Count == 0) return false;

                int maxCost = handWithoutMerchantAndCoin.Max(h => h.CurrentCost);
                var highestNow = handWithoutMerchantAndCoin.Where(h => h.CurrentCost == maxCost).ToList();
                return highestNow.Count > 0
                    && highestNow.All(h => h.Template != null && h.Template.Id == Card.Cards.RLK_534);
            }
            catch
            {
                return false;
            }
        }

        // 时空之爪触发口径：仅当“本回合确实有机会挥刀”时，才把灵魂弹幕视为“留给刀”。
        // 避免仅因“手里有刀”就过度跳过灵魂弹幕（尤其在刀已攻击过/本回合无法挥刀时）。
        private static bool CanTriggerClawBarrageThisTurn(Board board, int virtualRemainingManaNow)
        {
            try
            {
                if (board == null || board.HeroFriend == null) return false;

                bool hasEquippedReadyClaw = false;
                try
                {
                    hasEquippedReadyClaw = board.WeaponFriend != null
                        && board.WeaponFriend.Template != null
                        && board.WeaponFriend.Template.Id == Card.Cards.END_016
                        && board.WeaponFriend.CurrentDurability > 0
                        && (board.HeroFriend.CanAttack || (!board.HeroFriend.IsFrozen && board.HeroFriend.CountAttack == 0));
                }
                catch
                {
                    hasEquippedReadyClaw = board.WeaponFriend != null
                        && board.WeaponFriend.Template != null
                        && board.WeaponFriend.Template.Id == Card.Cards.END_016
                        && board.WeaponFriend.CurrentDurability > 0
                        && board.HeroFriend != null
                        && board.HeroFriend.CanAttack;
                }
                if (hasEquippedReadyClaw) return true;

                bool hasAnyWeaponEquipped = board.WeaponFriend != null
                    && board.WeaponFriend.Template != null
                    && board.WeaponFriend.CurrentDurability > 0;
                if (hasAnyWeaponEquipped) return false;

                bool heroCanSwingAfterEquip = false;
                try
                {
                    heroCanSwingAfterEquip = !board.HeroFriend.IsFrozen && board.HeroFriend.CountAttack == 0;
                }
                catch
                {
                    heroCanSwingAfterEquip = !board.HeroFriend.IsFrozen;
                }
                if (!heroCanSwingAfterEquip) return false;

                if (board.Hand == null) return false;
                return board.Hand.Any(h => h != null
                    && h.Template != null
                    && h.Template.Id == Card.Cards.END_016
                    && h.CurrentCost <= virtualRemainingManaNow);
            }
            catch
            {
                return false;
            }
        }

        // 商贩“稳定过到弹幕”补充门槛：除了可稳定弃到弹幕，还需有较高概率尽快触发亡语。
        // 这里采用保守且可解释的条件：
        // 1) 敌方当前有随从（通常可在对手回合被交易掉）；
        // 2) 或我方当回合可用地狱烈焰(CORE_CS2_062)主动触发亡语。
        private static bool CanLikelyTriggerMerchantDeathrattleSoon(Board board, int virtualRemainingManaNow)
        {
            try
            {
                if (board == null) return false;

                bool enemyHasMinion = board.MinionEnemy != null
                    && board.MinionEnemy.Any(m => m != null && m.Template != null && m.CurrentHealth > 0);
                if (enemyHasMinion) return true;

                bool canHellfireNow = board.Hand != null
                    && board.Hand.Any(h => h != null
                        && h.Template != null
                        && h.Template.Id == Card.Cards.CORE_CS2_062
                        && h.CurrentCost <= virtualRemainingManaNow);
                if (canHellfireNow) return true;

                return false;
            }
            catch
            {
                return false;
            }
        }

        // 仅当“较快摸到灵魂弹幕”的条件成立时，才把商贩线视为稳定。
        // 规则：
        // 1) 手里已有灵魂弹幕：直接满足；
        // 2) 否则要求牌库仍有灵魂弹幕，且至少存在稳定过牌动作。
        // 3) 若当前在“发现里考虑拿商贩”且手里还没有商贩，则额外收紧：
        //    需满足 drawActions>=2，或 drawActions>=1 且牌库弹幕>=2（避免空拿商贩卡手）。
        private static bool CanLikelyDrawBarrageSoon(Board board, int virtualRemainingManaNow, bool includeDiscoveredMerchant)
        {
            try
            {
                if (board == null || board.Hand == null) return false;

                bool hasBarrageInHand = board.Hand.Any(h => h != null && h.Template != null && h.Template.Id == Card.Cards.RLK_534);
                if (hasBarrageInHand) return true;

                int barrageRemain = 0;
                try { barrageRemain = GetTrueBarrageCountInDeck(board); } catch { barrageRemain = 0; }
                if (barrageRemain <= 0) return false;

                bool canTapNow = false;
                try
                {
                    bool abilityKnown = board.Ability != null;
                    int exhausted = abilityKnown ? TryReadGameTag(board.Ability, Card.GAME_TAG.EXHAUSTED, -1) : -1;
                    canTapNow = board.ManaAvailable >= 2 && (!abilityKnown || exhausted == 0);
                }
                catch { canTapNow = board.ManaAvailable >= 2; }

                bool canPlaySketchNow = board.Hand.Any(h => h != null
                    && h.Template != null
                    && h.Template.Id == Card.Cards.TOY_916
                    && h.CurrentCost <= virtualRemainingManaNow);
                bool canPlayKoboldNow = board.Hand.Any(h => h != null
                    && h.Template != null
                    && h.Template.Id == Card.Cards.LOOT_014
                    && h.CurrentCost <= virtualRemainingManaNow);

                int drawActions = 0;
                if (canTapNow) drawActions++;
                if (canPlaySketchNow) drawActions++;
                if (canPlayKoboldNow) drawActions++;

                if (drawActions <= 0) return false;

                bool hasMerchantInHand = board.Hand.Any(h => h != null
                    && h.Template != null
                    && h.Template.Id == Card.Cards.ULD_163);

                if (includeDiscoveredMerchant && !hasMerchantInHand && !hasBarrageInHand)
                {
                    bool strictStable = drawActions >= 2 || (drawActions >= 1 && barrageRemain >= 2);
                    if (!strictStable)
                    {
                        Bot.Log("[Discover] 咒怨发现(TLC_451)：商贩抽弹幕稳定性不足(drawActions="
                            + drawActions + ",deckBarrage=" + barrageRemain + ") -> 不按商贩优先");
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool CanStablyMerchantBarrageAndLikelyDraw(Board board, int virtualRemainingManaNow, bool includeDiscoveredMerchant)
        {
            try
            {
                return CanStablyMerchantDiscardBarrage(board, virtualRemainingManaNow, includeDiscoveredMerchant)
                    && CanLikelyTriggerMerchantDeathrattleSoon(board, virtualRemainingManaNow)
                    && CanLikelyDrawBarrageSoon(board, virtualRemainingManaNow, includeDiscoveredMerchant);
            }
            catch
            {
                return false;
            }
        }

        // TLC_451 发现口径（商贩）：优先沿用严格口径；
        // 若严格口径不满足，但已满足“可稳定弃弹幕 + 可较快过到弹幕 + 当前手里已有弹幕”，
        // 也允许优先拿商贩（避免空场时被“亡语触发时机”过度限制）。
        private static bool CanPreferMerchantBarrageForTlc451(Board board, int virtualRemainingManaNow, bool includeDiscoveredMerchant)
        {
            try
            {
                if (CanStablyMerchantBarrageAndLikelyDraw(board, virtualRemainingManaNow, includeDiscoveredMerchant))
                    return true;

                bool stableDiscardOnly = CanStablyMerchantDiscardBarrage(board, virtualRemainingManaNow, includeDiscoveredMerchant);
                if (!stableDiscardOnly) return false;

                bool canDrawBarrageSoon = CanLikelyDrawBarrageSoon(board, virtualRemainingManaNow, includeDiscoveredMerchant);
                if (!canDrawBarrageSoon) return false;

                int barrageInHand = 0;
                try
                {
                    barrageInHand = board != null && board.Hand != null
                        ? board.Hand.Count(h => h != null && h.Template != null && h.Template.Id == Card.Cards.RLK_534)
                        : 0;
                }
                catch { barrageInHand = 0; }

                return barrageInHand > 0;
            }
            catch
            {
                return false;
            }
        }

        private static Card.Cards PickDiscardWarlockTlc451Discover(Board board, List<Card.Cards> choices)
        {
            if (choices == null || choices.Count == 0) return default(Card.Cards);

            // 注意：本函数是“弃牌术专项”大分支，但其中包含一条蛋术也需要的硬规则。
            // 若后续调用方改为只在 IsDiscardWarlock(board) 下调用，这条规则会失效。
            // 因此：蛋术对 TLC_451 的硬规则已在调用方提前处理（TryPickEggWarlockTlc451Discover）。

            // 硬规则：咒怨之墓(TLC_451)发现也永远不选帕奇斯。
            // 说明：通用分支里虽然有过滤，但 TLC_451 是专用早返回分支，不会走到通用过滤。
            try
            {
                if (choices.Contains(Card.Cards.CFM_637))
                {
                    var filtered = choices.Where(c => c != Card.Cards.CFM_637).ToList();
                    if (filtered.Count > 0)
                    {
                        choices = filtered;
                        Bot.Log("[Discover] 咒怨发现(TLC_451)：候选含帕奇斯(CFM_637) -> 已过滤，不会选择");
                    }
                }
            }
            catch
            {
                // ignore
            }

            // 硬规则：咒怨之墓(TLC_451)发现不要选时空撕裂(TIME_025t)。
            // 说明：该牌对本套逻辑收益极低，且容易在兜底分支被误选。
            try
            {
                if (choices.Contains(Card.Cards.TIME_025t))
                {
                    var filtered = choices.Where(c => c != Card.Cards.TIME_025t).ToList();
                    if (filtered.Count > 0)
                    {
                        choices = filtered;
                        Bot.Log("[Discover] 咒怨发现(TLC_451)：候选含时空撕裂(TIME_025t) -> 已过滤，不会选择");
                    }
                }
            }
            catch
            {
                // ignore
            }

            int handCount = 0;
            try { handCount = board != null && board.Hand != null ? board.Hand.Count : 0; } catch { handCount = 0; }

            int remainingManaNow = 0;
            bool hasCoinNow = false;
            bool manaFromSnapshot = false;
            int virtualRemainingManaNow = 0;
            try
            {
                virtualRemainingManaNow = GetVirtualRemainingManaForDiscover(board, out remainingManaNow, out hasCoinNow, out manaFromSnapshot);
            }
            catch
            {
                remainingManaNow = 0;
                hasCoinNow = false;
                manaFromSnapshot = false;
                virtualRemainingManaNow = 0;
            }
            try
            {
                if (hasCoinNow)
                    Bot.Log("[Discover] 咒怨发现(TLC_451)：检测到手里有硬币，发现可用费用按剩余" + remainingManaNow + "+1=" + virtualRemainingManaNow + " 计算");
                else if (manaFromSnapshot)
                    Bot.Log("[Discover] 咒怨发现(TLC_451)：当前 board 为空，发现可用费用使用快照=" + virtualRemainingManaNow);
            }
            catch { }
            bool manaUnknownForNullBoard = (board == null && !manaFromSnapshot);
            try
            {
                if (manaUnknownForNullBoard)
                    Bot.Log("[Discover] 咒怨发现(TLC_451)：board为空且无可用费用快照 -> 按0费执行保守硬过滤");
            }
            catch { }

            // 用户硬规则：按“当前可用费用 + 硬币虚拟1费”约束发现可选范围。
            int strictRemainingManaNow = 0;
            try
            {
                strictRemainingManaNow = Math.Max(0, virtualRemainingManaNow);
                if (strictRemainingManaNow > 10) strictRemainingManaNow = 10;
            }
            catch { strictRemainingManaNow = 0; }
            try
            {
                Bot.Log("[Discover] 咒怨发现(TLC_451)：硬规则启用，仅允许选择费用<=当前可用费用(含硬币虚拟1费=" + strictRemainingManaNow + ")的牌");
            }
            catch { }

            int enemyHpArmor = 99;
            try { if (board != null && board.HeroEnemy != null) enemyHpArmor = Math.Max(0, (board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor)); } catch { enemyHpArmor = 99; }

            // 用户需求：动态判定被弃组件集合（与主策略口径保持一致）
            var discardPayoffComponents = GetDiscardComponentsConsideringHand(board, 9);
            int handCountForCaveRatio = 0;
            int payoffCountForCaveRatio = 0;
            bool highDiscardRatioForCave = false;
            try
            {
                highDiscardRatioForCave = IsDiscardPayoffRatioAbove(board, discardPayoffComponents, 20, out payoffCountForCaveRatio, out handCountForCaveRatio);
            }
            catch
            {
                highDiscardRatioForCave = false;
                handCountForCaveRatio = 0;
                payoffCountForCaveRatio = 0;
            }

            // 魂火选择口径：仅在“全手被弃组件”或“本回合需要补伤害达成斩杀”时才允许选。
            bool allowPickSoulfire = false;
            try
            {
                // A) 全手被弃组件（排除硬币）：此时拿魂火相当于补一个输出/出牌点
                bool handAllDiscardPayoffs = false;
                try
                {
                    if (board != null && board.Hand != null)
                    {
                        var nonCoin = board.Hand.Where(c => c != null && c.Template != null
                            && c.Template.Id != Card.Cards.GAME_005
                            && !IsHonestMerchantCharm(c.Template.Id)).ToList();
                        if (nonCoin.Count > 0)
                            handAllDiscardPayoffs = nonCoin.All(c => discardPayoffComponents.Contains(c.Template.Id));
                    }
                }
                catch { handAllDiscardPayoffs = false; }

                // B) 本回合斩杀补伤害：当前已有伤害不足，但若额外+魂火(4+法强)即可达成
                bool lethalHuntBySoulfire = false;
                try
                {
                    int boardAttack = 0;
                    try { if (board != null && board.MinionFriend != null) boardAttack = board.MinionFriend.Where(m => m != null && m.CanAttack).Sum(m => m.CurrentAtk); } catch { boardAttack = 0; }

                    int weaponAttack = 0;
                    try { if (board != null && board.WeaponFriend != null && board.HeroFriend != null && board.HeroFriend.CanAttack) weaponAttack = board.WeaponFriend.CurrentAtk; } catch { weaponAttack = 0; }

                    int spellPower = 0;
                    try { if (board != null && board.MinionFriend != null) spellPower = board.MinionFriend.Sum(m => m != null ? m.SpellPower : 0); } catch { spellPower = 0; }

                    bool noTaunt = true;
                    try { if (board != null && board.MinionEnemy != null) noTaunt = !board.MinionEnemy.Any(m => m != null && m.IsTaunt); } catch { noTaunt = true; }

                    if (noTaunt && board != null)
                    {
                        int currentHandSpellPotential = 0;
                        try { currentHandSpellPotential = MaxFaceSpellDamageWithinMana(board); } catch { currentHandSpellPotential = 0; }

                        int currentTotal = boardAttack + weaponAttack + currentHandSpellPotential;
                        int withSoulfireTotal = currentTotal + (4 + spellPower);
                        lethalHuntBySoulfire = (currentTotal < enemyHpArmor) && (withSoulfireTotal >= enemyHpArmor);
                    }
                }
                catch { lethalHuntBySoulfire = false; }

                allowPickSoulfire = handAllDiscardPayoffs || lethalHuntBySoulfire;
            }
            catch { allowPickSoulfire = false; }

            // 用户硬规则：发现候选必须满足 费用 <= 当前可用费用 + 硬币虚拟1费。
            Func<Card.Cards, bool> isAffordableOrPayoff = (cardId) =>
            {
                try
                {
                    return GetTlc451EffectiveDiscoverCost(board, cardId) <= strictRemainingManaNow;
                }
                catch
                {
                    return false;
                }
            };

            // TLC_451 硬过滤：先剔除所有“费用 > 当前可用费用(含硬币虚拟1费)”的候选。
            try
            {
                var affordableOrPayoffChoices = new List<Card.Cards>();
                var removedChoices = new List<string>();

                foreach (var c in choices)
                {
                    if (isAffordableOrPayoff(c))
                    {
                        affordableOrPayoffChoices.Add(c);
                        continue;
                    }

                    int cost = 99;
                    try
                    {
                        cost = GetTlc451EffectiveDiscoverCost(board, c);
                    }
                    catch { cost = 99; }

                    removedChoices.Add(SafeCardName(c) + "(" + c + ",费" + cost + ")");
                }

                if (affordableOrPayoffChoices.Count > 0)
                {
                    if (removedChoices.Count > 0)
                    {
                        Bot.Log("[Discover] 咒怨发现(TLC_451)：当前可用费用(含硬币虚拟1费)=" + strictRemainingManaNow
                            + "，已剔除超费候选 -> " + string.Join("；", removedChoices));
                    }
                    choices = affordableOrPayoffChoices;
                }
                else if (removedChoices.Count > 0)
                {
                    int minCost = 99;
                    try
                    {
                        minCost = choices
                            .Select(c => GetTlc451EffectiveDiscoverCost(board, c))
                            .DefaultIfEmpty(99)
                            .Min();
                    }
                    catch { minCost = 99; }

                    var minCostChoices = new List<Card.Cards>();
                    try
                    {
                        minCostChoices = choices.Where(c =>
                        {
                            try
                            {
                                int cst = GetTlc451EffectiveDiscoverCost(board, c);
                                return cst == minCost;
                            }
                            catch { return false; }
                        }).ToList();
                    }
                    catch { minCostChoices = new List<Card.Cards>(); }

                    if (minCostChoices.Count > 0)
                    {
                        choices = minCostChoices;
                        Bot.Log("[Discover] 咒怨发现(TLC_451)：当前可用费用(含硬币虚拟1费)=" + strictRemainingManaNow
                            + "，三选均超费 -> 改为最低费用兜底(费" + minCost + ")");
                    }
                    else
                    {
                        Bot.Log("[Discover] 咒怨发现(TLC_451)：当前可用费用(含硬币虚拟1费)=" + strictRemainingManaNow
                            + "，三选均超费且最低费用兜底失败 -> 保留原候选");
                    }
                }
            }
            catch
            {
                // ignore
            }

            // 1) 斩杀优先：能本回合直接斩杀就先拿斩杀牌
            try
            {
                int boardAttack = 0;
                try { if (board != null && board.MinionFriend != null) boardAttack = board.MinionFriend.Where(m => m != null && m.CanAttack).Sum(m => m.CurrentAtk); } catch { boardAttack = 0; }

                int weaponAttack = 0;
                try { if (board != null && board.WeaponFriend != null && board.HeroFriend != null && board.HeroFriend.CanAttack) weaponAttack = board.WeaponFriend.CurrentAtk; } catch { weaponAttack = 0; }

                int spellPower = 0;
                try { if (board != null && board.MinionFriend != null) spellPower = board.MinionFriend.Sum(m => m != null ? m.SpellPower : 0); } catch { spellPower = 0; }

                bool noTaunt = true;
                try { if (board != null && board.MinionEnemy != null) noTaunt = !board.MinionEnemy.Any(m => m != null && m.IsTaunt); } catch { noTaunt = true; }

                if (noTaunt)
                {
                    if (choices.Contains(Card.Cards.CORE_CS2_062) && board != null && virtualRemainingManaNow >= 4)
                    {
                        int hellfireDamage = 3 + spellPower;
                        int totalDamage = boardAttack + weaponAttack + hellfireDamage;
                        if (totalDamage >= enemyHpArmor)
                        {
                            Bot.Log("[Discover] 咒怨发现(TLC_451)：斩杀优先 - 地狱烈焰可斩杀(场攻" + boardAttack + "+武器" + weaponAttack + "+地狱烈焰" + hellfireDamage + "=" + totalDamage + " >= 敌方" + enemyHpArmor + ")");
                            return Card.Cards.CORE_CS2_062;
                        }
                    }
                    else if (choices.Contains(Card.Cards.EX1_308) && board != null && virtualRemainingManaNow >= 1 && allowPickSoulfire)
                    {
                        int soulfireDamage = 4 + spellPower;
                        int totalDamage = boardAttack + weaponAttack + soulfireDamage;
                        if (totalDamage >= enemyHpArmor)
                        {
                            Bot.Log("[Discover] 咒怨发现(TLC_451)：斩杀优先 - 魂火可斩杀(场攻" + boardAttack + "+武器" + weaponAttack + "+魂火" + soulfireDamage + "=" + totalDamage + " >= 敌方" + enemyHpArmor + ")");
                            return Card.Cards.EX1_308;
                        }
                    }
                    else if (choices.Contains(Card.Cards.TIME_027) && board != null && virtualRemainingManaNow >= 2)
                    {
                        bool enemyBoardEmpty = true;
                        try { enemyBoardEmpty = board.MinionEnemy == null || !board.MinionEnemy.Any(m => m != null); } catch { enemyBoardEmpty = true; }

                        if (enemyBoardEmpty)
                        {
                            int photonDamage = 6 + spellPower;
                            int totalDamage = boardAttack + weaponAttack + photonDamage;
                            if (totalDamage >= enemyHpArmor)
                            {
                                Bot.Log("[Discover] 咒怨发现(TLC_451)：斩杀优先 - 超光子弹幕可斩杀(场攻" + boardAttack + "+武器" + weaponAttack + "+超光子" + photonDamage + "=" + totalDamage + " >= 敌方" + enemyHpArmor + ")");
                                return Card.Cards.TIME_027;
                            }
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }

            // 蛋术专项：坟场已有蛋阶段时，优先拿尸身保护令(MAW_002)来复活触发亡语连锁。
            // 说明：这通常比“再拿一张破蛋工具”更能提升胜率（尤其是中后期资源战）。
            try
            {
                int eggInGrave = 0;
                try
                {
                    if (board != null && board.FriendGraveyard != null)
                        eggInGrave = board.FriendGraveyard.Count(x => _eggStages.Contains(x));
                }
                catch { eggInGrave = 0; }

                if (eggInGrave > 0 && choices.Contains(Card.Cards.MAW_002))
                {
                    Bot.Log("[Discover] 咒怨发现(TLC_451)：坟场有蛋x" + eggInGrave + " -> 优先选择尸身保护令(MAW_002)");
                    return Card.Cards.MAW_002;
                }
            }
            catch
            {
                // ignore
            }

            bool hasSoulfireInHand = false;
            try { hasSoulfireInHand = HasCardInHand(board, Card.Cards.EX1_308); } catch { hasSoulfireInHand = false; }

            bool hasBarrageInHand = false;
            try { hasBarrageInHand = HasCardInHand(board, Card.Cards.RLK_534); } catch { hasBarrageInHand = false; }

            bool hasMerchantInHand = false;
            try { hasMerchantInHand = HasCardInHand(board, Card.Cards.ULD_163); } catch { hasMerchantInHand = false; }

            bool hasBt300InHand = false;
            try { hasBt300InHand = HasCardInHand(board, Card.Cards.BT_300); } catch { hasBt300InHand = false; }
            int bt300InHandCount = 0;
            try
            {
                bt300InHandCount = board != null && board.Hand != null
                    ? board.Hand.Count(c => c != null && c.Template != null && c.Template.Id == Card.Cards.BT_300)
                    : 0;
            }
            catch { bt300InHandCount = hasBt300InHand ? 1 : 0; }

            bool hasSketchInHand = false;
            try { hasSketchInHand = HasCardInHand(board, Card.Cards.TOY_916); } catch { hasSketchInHand = false; }

            // TLC_451 商贩口径：
            // 1) 严格：稳定弃弹幕 + 较快触发亡语 + 过牌稳定；
            // 2) 宽松：稳定弃弹幕 + 过牌稳定 + 手里已有弹幕。
            bool stableMerchantBarrageNow = false;
            try { stableMerchantBarrageNow = CanPreferMerchantBarrageForTlc451(board, virtualRemainingManaNow, choices.Contains(Card.Cards.ULD_163)); } catch { stableMerchantBarrageNow = false; }

            // 用户口径：手里有双古尔丹之手时，地标弃牌发现优先拿灵魂弹幕，不把窗口让给恐怖海盗/刀线。
            bool preferBarrageByDoubleGuldan = bt300InHandCount >= 2 && choices.Contains(Card.Cards.RLK_534);
            if (preferBarrageByDoubleGuldan)
            {
                Bot.Log("[Discover] 咒怨发现(TLC_451)：手里古尔丹之手x" + bt300InHandCount + " -> 优先选择灵魂弹幕(RLK_534)");
                return Card.Cards.RLK_534;
            }

            // 2a) 速写前置护栏：
            // 若牌库仍有灵魂弹幕，且三选有可立即落地的速写，则优先速写，避免被商贩前置优先覆盖。
            bool preferSketchBecauseDeckHasBarrage = false;
            int barrageRemainEstimateForSketch = 0;
            try
            {
                barrageRemainEstimateForSketch = GetTrueBarrageCountInDeck(board);
                bool sketchCandidateNow = choices.Contains(Card.Cards.TOY_916)
                    && handCount <= 8
                    && !hasSketchInHand;
                bool sketchAffordableNowForGuard = false;
                try { sketchAffordableNowForGuard = isAffordableOrPayoff(Card.Cards.TOY_916); } catch { sketchAffordableNowForGuard = false; }

                preferSketchBecauseDeckHasBarrage = sketchCandidateNow
                    && sketchAffordableNowForGuard
                    && barrageRemainEstimateForSketch > 0;
            }
            catch
            {
                preferSketchBecauseDeckHasBarrage = false;
                barrageRemainEstimateForSketch = 0;
            }

            // 2) 过期商贩（前置硬优先）：
            // 统一口径：仅当“满足商贩弃弹幕放行口径”时，才允许优先拿商贩。
            // 放在速写之前，避免“速写高优先级”覆盖掉可用商贩线。
            bool highestHasDiscardForMerchant = false;
            int maxCostForMerchant = -1;
            try
            {
                if (choices.Contains(Card.Cards.ULD_163) && isAffordableOrPayoff(Card.Cards.ULD_163))
                {
                    var hand = board != null ? board.Hand : null;
                    if (hand != null && hand.Any())
                    {
                        maxCostForMerchant = hand.Max(x => x.CurrentCost);
                        highestHasDiscardForMerchant = hand.Any(x => x.CurrentCost == maxCostForMerchant
                            && x.Template != null
                            && discardPayoffComponents.Contains(x.Template.Id));
                    }
                }
            }
            catch
            {
                highestHasDiscardForMerchant = false;
                maxCostForMerchant = -1;
            }

            if (highestHasDiscardForMerchant && stableMerchantBarrageNow)
            {
                bool handCountHigh = handCount >= 7;
                bool guardBlock = handCountHigh && hasBt300InHand;
                if (!guardBlock)
                {
                    if (preferSketchBecauseDeckHasBarrage)
                    {
                        Bot.Log("[Discover] 咒怨发现(TLC_451)：牌库仍有灵魂弹幕(est=" + barrageRemainEstimateForSketch + ")且速写可用 -> 优先选择速写美术家，跳过商贩前置优先");
                        return Card.Cards.TOY_916;
                    }
                    Bot.Log("[Discover] 咒怨发现(TLC_451)：前置优先 - 满足商贩弃弹幕放行口径，优先选择过期货物专卖商(ULD_163)");
                    return Card.Cards.ULD_163;
                }
            }
            else if (choices.Contains(Card.Cards.ULD_163) && isAffordableOrPayoff(Card.Cards.ULD_163) && highestHasDiscardForMerchant && !stableMerchantBarrageNow)
            {
                Bot.Log("[Discover] 咒怨发现(TLC_451)：最高费命中被弃组件但不满足商贩放行口径 -> 跳过过期货物专卖商");
            }

            // 2.4) 恶兆邪火前置：
            // 用户口径：墓发现应优先拿恶兆邪火建立减费引擎；
            // 仅当续连熵能已命中“高收益窗口”时，让位续连。
            try
            {
                if (choices.Contains(Card.Cards.GDB_121) && isAffordableOrPayoff(Card.Cards.GDB_121))
                {
                    int friendMinionsNow = CountFriendlyMinions(board);
                    int enemyMinionsNow = CountEnemyMinions(board);
                    int friendTribeTypesNow = GetFriendlyMinionTribeTypeCount(board);
                    int attackableFriendMinionsNow = 0;
                    try
                    {
                        attackableFriendMinionsNow = board != null && board.MinionFriend != null
                            ? board.MinionFriend.Count(m => m != null && m.CanAttack && m.CurrentAtk > 0)
                            : 0;
                    }
                    catch { attackableFriendMinionsNow = 0; }

                    bool entropyHighValueWindow = choices.Contains(Card.Cards.TIME_026)
                        && isAffordableOrPayoff(Card.Cards.TIME_026)
                        && enemyMinionsNow == 0
                        && friendMinionsNow >= 5
                        && attackableFriendMinionsNow >= 5
                        && friendTribeTypesNow >= 4;

                    if (!entropyHighValueWindow)
                    {
                        Bot.Log("[Discover] 咒怨发现(TLC_451)：恶兆邪火前置规则命中 -> 优先选择恶兆邪火(GDB_121)");
                        return Card.Cards.GDB_121;
                    }

                    Bot.Log("[Discover] 咒怨发现(TLC_451)：恶兆邪火可选，但已命中续连高收益窗口 -> 让位续连");
                }
            }
            catch
            {
                // ignore
            }

            // 2.5) 低费发现保护：可用费用<=2时，优先过牌随从，避免误拿速写(3费)造成空转。
            try
            {
                if (virtualRemainingManaNow <= 2)
                {
                    var lowManaDrawPick = PickLowCostDrawMinionForTlc451(board, choices, virtualRemainingManaNow, false);
                    if (!lowManaDrawPick.Equals(default(Card.Cards)))
                    {
                        Bot.Log("[Discover] 咒怨发现(TLC_451)：当前可用费用(含硬币)=" + virtualRemainingManaNow
                            + " <= 2 -> 优先过牌随从 " + SafeCardName(lowManaDrawPick) + "(" + lowManaDrawPick + ")");
                        return lowManaDrawPick;
                    }
                }
            }
            catch
            {
                // ignore
            }

            // 3) 速写美术家：优先级很高（不仅限于“牌库有灵魂弹幕”）
            // 诉求：咒怨之墓发现经常给到“速写/超光子/狗头人”等，通常速写的长期收益更高，且这些牌多为临时牌，尽量别让评分分支覆盖掉。
            // 规则：三选有速写，且手牌不满(<=8)，且手里还没有速写 -> 优先选速写。
            // 进一步加权：只要牌库/手里仍存在关键暗影法术（弹幕/古尔丹/超光子），更应优先速写。
            bool sketchAffordableNow = false;
            try { sketchAffordableNow = isAffordableOrPayoff(Card.Cards.TOY_916); } catch { sketchAffordableNow = false; }

            bool sketchCandidate = choices.Contains(Card.Cards.TOY_916)
                && handCount <= 8
                && !hasSketchInHand;

            if (sketchCandidate && sketchAffordableNow)
            {
                // 新口径：TLC_451 发现要优先“本回合可立即落地”的组件，避免 2 费无币拿 3 费速写空转。
                bool hasKeySpellInDeckOrHand = false;
                try
                {
                    hasKeySpellInDeckOrHand = HasCardInDeck(board, Card.Cards.RLK_534)
                        || HasCardInDeck(board, Card.Cards.BT_300)
                        || hasBarrageInHand
                        || hasBt300InHand;
                }
                catch { hasKeySpellInDeckOrHand = true; }

                if (hasKeySpellInDeckOrHand)
                {
                    Bot.Log("[Discover] 咒怨发现(TLC_451)：三选包含速写美术家且存在关键法术收益来源 -> 优先选择速写美术家");
                    return Card.Cards.TOY_916;
                }

                // 即使未检测到关键法术，速写仍是强组件（避免落入评分分支导致拿错）
                Bot.Log("[Discover] 咒怨发现(TLC_451)：三选包含速写美术家且手牌安全(<=8)且手里无速写 -> 优先选择速写美术家");
                return Card.Cards.TOY_916;
            }
            if (sketchCandidate && !sketchAffordableNow)
            {
                Bot.Log("[Discover] 咒怨发现(TLC_451)：三选含速写但当前可用费用(含硬币)=" + virtualRemainingManaNow + " < 3 -> 跳过速写，优先可立即落地组件");
            }
          
            // 4) 过期商贩：兜底（若未命中前置优先，仍要求“可稳定商贩弃灵魂弹幕”）
            if (choices.Contains(Card.Cards.ULD_163) && isAffordableOrPayoff(Card.Cards.ULD_163))
            {
                bool hasDiscardAtMaxCost = false;
                int maxCost = -1;
                try
                {
                    var hand = board != null ? board.Hand : null;
                    if (hand != null && hand.Any())
                    {
                        maxCost = hand.Max(x => x.CurrentCost);
                        hasDiscardAtMaxCost = hand.Any(x => x.CurrentCost == maxCost
                            && x.Template != null
                            && discardPayoffComponents.Contains(x.Template.Id));
                    }
                }
                catch { hasDiscardAtMaxCost = false; }

                if (hasDiscardAtMaxCost && stableMerchantBarrageNow)
                {
                    bool handCountHigh = handCount >= 7;
                    bool guardBlock = handCountHigh && hasBt300InHand;
                    if (!guardBlock)
                    {
                        if (preferSketchBecauseDeckHasBarrage)
                        {
                            Bot.Log("[Discover] 咒怨发现(TLC_451)：牌库仍有灵魂弹幕(est=" + barrageRemainEstimateForSketch + ")且速写可用 -> 优先选择速写美术家，跳过商贩兜底");
                            return Card.Cards.TOY_916;
                        }
                        Bot.Log("[Discover] 咒怨发现(TLC_451)：满足商贩弃弹幕放行口径，选择过期货物专卖商");
                        return Card.Cards.ULD_163;
                    }
                }
                else
                {
                    if (!hasDiscardAtMaxCost)
                    {
                        Bot.Log("[Discover] 咒怨发现(TLC_451)：最高费(" + maxCost + ")中无被弃组件，跳过过期货物专卖商");
                    }
                    else
                    {
                        Bot.Log("[Discover] 咒怨发现(TLC_451)：最高费有被弃组件但不满足商贩放行口径，跳过过期货物专卖商");
                    }
                }
            }

    				// 4) 古尔丹之手：仅当套牌确实包含时，手牌安全且手里没有才优先补齐
                else if (choices.Contains(Card.Cards.BT_300)
                    && handCount > 0 && handCount <= Bt300SafeThresholdForMerchant
                    && !hasBt300InHand
                    && HasCardInDeck(board, Card.Cards.BT_300))
            {
                Bot.Log("[Discover] 咒怨发现(TLC_451)：三选包含古尔丹之手且手牌安全(<=8)且手里无BT_300 -> 优先选择古尔丹之手");
                return Card.Cards.BT_300;
            }
            // 5) 武器(END_016)：统一处理（避免同卡多处 if）
            if (choices.Contains(Card.Cards.END_016) && isAffordableOrPayoff(Card.Cards.END_016))
            {
                bool hasClawInHand = false;
                bool hasClawEquipped = false;
                try { hasClawInHand = HasCardInHand(board, Card.Cards.END_016); } catch { hasClawInHand = false; }
                try { hasClawEquipped = board != null && board.WeaponFriend != null && board.WeaponFriend.Template != null && board.WeaponFriend.Template.Id == Card.Cards.END_016; } catch { hasClawEquipped = false; }

                if (hasClawInHand || hasClawEquipped)
                {
                    Bot.Log("[Discover] 咒怨发现(TLC_451)：手里已有时空之爪或已装备，跳过选择刀。");
                }
                else
                {
                    bool barrageInMaxCost = false;
                    try
                    {
                        var hand = board != null ? board.Hand : null;
                        if (hand != null && hand.Any())
                        {
                            int maxCost = hand.Max(x => x.CurrentCost);
                            barrageInMaxCost = hand.Any(x => x.CurrentCost == maxCost && x.Template != null && x.Template.Id == Card.Cards.RLK_534);
                        }
                    }
                    catch { barrageInMaxCost = false; }

                    bool shouldPickClaw = false;
                    if (board != null && virtualRemainingManaNow >= 4 && barrageInMaxCost)
                    {
                        Bot.Log("[Discover] 咒怨发现(TLC_451)：最高费组包含灵魂弹幕，强推提速选择时空之爪作为触发器。");
                        shouldPickClaw = true;
                    }
                    else if (board != null && virtualRemainingManaNow >= 4 && hasBarrageInHand)
                    {
                        // 兜底：手里有弹幕且费>=4时，允许拿刀做触发器（与原兜底一致）
                        Bot.Log("[Discover] 咒怨发现(TLC_451)兜底：手里有弹幕且费>=4 -> 选择时空之爪");
                        shouldPickClaw = true;
                    }

                    if (shouldPickClaw) return Card.Cards.END_016;
                }
            }

            // 5.5) 邪翼蝠/郊狼：基于卡牌减费文本的中段加权（不覆盖前面的核心连段优先级）
            try
            {
                bool enemyHasTauntNow = false;
                try { enemyHasTauntNow = board != null && board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt); } catch { enemyHasTauntNow = false; }

                int attackableMinionsCount = 0;
                int faceDamageFromAttackableBoard = 0;
                try
                {
                    if (board != null && board.MinionFriend != null)
                    {
                        var attackers = board.MinionFriend.Where(m => m != null && m.CanAttack && m.CurrentAtk > 0).ToList();
                        attackableMinionsCount = attackers.Count;
                        if (!enemyHasTauntNow)
                        {
                            faceDamageFromAttackableBoard = attackers.Sum(m => Math.Max(0, m.CurrentAtk));
                        }
                    }
                }
                catch
                {
                    attackableMinionsCount = 0;
                    faceDamageFromAttackableBoard = 0;
                }

                // A) 场面可稳定打到脸4攻：可优先拿邪翼蝠（通常可被压到0费）
                if (!enemyHasTauntNow && faceDamageFromAttackableBoard >= 4 && choices.Contains(Card.Cards.YOD_032))
                {
                    Bot.Log("[Discover] 咒怨发现(TLC_451)：场面可走脸伤害=" + faceDamageFromAttackableBoard + ">=4 且无嘲讽 -> 选择狂暴邪翼蝠(YOD_032)");
                    return Card.Cards.YOD_032;
                }

                // B) 郊狼触发条件：
                //    1) 己方有5个可攻击随从且对面无嘲讽；
                //    2) 或手里有超光子弹幕；
                //    3) 或存在“稳定触发灵魂弹幕”线（已装备可攻爪子且最高费仅弹幕 / 可打商贩且其最高费仅弹幕）。
                bool hasPhotonInHandNow = false;
                try { hasPhotonInHandNow = HasCardInHand(board, Card.Cards.TIME_027); } catch { hasPhotonInHandNow = false; }

                bool stableBarrageTriggerNow = false;
                try
                {
                    var handNow = board != null ? board.Hand : null;
                    bool hasBarrageNow = handNow != null && handNow.Any(h => h != null && h.Template != null && h.Template.Id == Card.Cards.RLK_534);
                    if (hasBarrageNow && handNow != null && handNow.Count > 0)
                    {
                        bool stableByClaw = false;
                        try
                        {
                            bool clawCanAttack = board != null
                                && board.WeaponFriend != null
                                && board.WeaponFriend.Template != null
                                && board.WeaponFriend.Template.Id == Card.Cards.END_016
                                && board.WeaponFriend.CurrentDurability > 0
                                && board.HeroFriend != null
                                && board.HeroFriend.CanAttack;

                            if (clawCanAttack)
                            {
                                var handWithoutCoin = handNow.Where(h => h != null && h.Template != null && h.Template.Id != Card.Cards.GAME_005).ToList();
                                if (handWithoutCoin.Count > 0)
                                {
                                    int maxCostNow = handWithoutCoin.Max(h => h.CurrentCost);
                                    var highestNow = handWithoutCoin.Where(h => h.CurrentCost == maxCostNow).ToList();
                                    stableByClaw = highestNow.Count > 0 && highestNow.All(h => h.Template != null && h.Template.Id == Card.Cards.RLK_534);
                                }
                            }
                        }
                        catch { stableByClaw = false; }

                        bool stableByMerchant = false;
                        try
                        {
                            bool hasSpace = board != null && board.MinionFriend != null ? board.MinionFriend.Count < 7 : true;
                            var merchantPlayable = handNow.FirstOrDefault(h => h != null && h.Template != null
                                && h.Template.Id == Card.Cards.ULD_163
                                && h.CurrentCost <= virtualRemainingManaNow);

                            if (hasSpace && merchantPlayable != null)
                            {
                                var handWithoutMerchantAndCoin = handNow.Where(h => h != null && h.Template != null
                                    && h.Template.Id != Card.Cards.ULD_163
                                    && h.Template.Id != Card.Cards.GAME_005).ToList();
                                if (handWithoutMerchantAndCoin.Count > 0)
                                {
                                    int maxCostNow = handWithoutMerchantAndCoin.Max(h => h.CurrentCost);
                                    var highestNow = handWithoutMerchantAndCoin.Where(h => h.CurrentCost == maxCostNow).ToList();
                                    stableByMerchant = highestNow.Count > 0 && highestNow.All(h => h.Template != null && h.Template.Id == Card.Cards.RLK_534);
                                }
                            }
                        }
                        catch { stableByMerchant = false; }

                        stableBarrageTriggerNow = stableByClaw || stableByMerchant;
                    }
                }
                catch { stableBarrageTriggerNow = false; }

                bool coyoteByBoard = !enemyHasTauntNow && attackableMinionsCount >= 5;
                bool coyoteByPhoton = hasPhotonInHandNow;
                bool coyoteByStableBarrage = stableBarrageTriggerNow;

                // C) 对称口径：若手里已有郊狼/邪翼蝠，且敌方空场或敌方随从全员<=3血，三选出现超光子时优先超光子。
                // 目的：稳定形成“超光子 -> 压费 -> 郊狼/邪翼蝠”连段，而不是反向再拿一张减费随从。
                bool enemyBoardEmptyNow = false;
                bool enemyAllLowHp3Now = false;
                int enemyMinionCountNow = 0;
                bool hasCoyoteInHandNow = false;
                bool hasFelwingInHandNow = false;
                try
                {
                    enemyMinionCountNow = board != null && board.MinionEnemy != null
                        ? board.MinionEnemy.Count(m => m != null && m.CurrentHealth > 0)
                        : 0;
                    enemyBoardEmptyNow = enemyMinionCountNow == 0;
                    enemyAllLowHp3Now = enemyMinionCountNow > 0
                        && board != null
                        && board.MinionEnemy != null
                        && board.MinionEnemy
                            .Where(m => m != null && m.CurrentHealth > 0)
                            .All(m => m.CurrentHealth <= 3);
                    hasCoyoteInHandNow = HasCardInHand(board, Card.Cards.TIME_047);
                    hasFelwingInHandNow = HasCardInHand(board, Card.Cards.YOD_032);
                }
                catch
                {
                    enemyBoardEmptyNow = false;
                    enemyAllLowHp3Now = false;
                    enemyMinionCountNow = 0;
                    hasCoyoteInHandNow = false;
                    hasFelwingInHandNow = false;
                }

                if (choices.Contains(Card.Cards.TIME_027)
                    && (enemyBoardEmptyNow || enemyAllLowHp3Now)
                    && (hasCoyoteInHandNow || hasFelwingInHandNow)
                    && isAffordableOrPayoff(Card.Cards.TIME_027))
                {
                    string enemyBoardTag = enemyBoardEmptyNow
                        ? "敌方空场"
                        : ("敌方随从全员<=3血(count=" + enemyMinionCountNow + ")");
                    Bot.Log("[Discover] 咒怨发现(TLC_451)：手里已有"
                        + (hasCoyoteInHandNow ? "郊狼" : "邪翼蝠")
                        + "且" + enemyBoardTag + " -> 优先选择超光子弹幕(TIME_027)用于压费连段");
                    return Card.Cards.TIME_027;
                }

                if (choices.Contains(Card.Cards.TIME_047) && (coyoteByBoard || coyoteByPhoton || coyoteByStableBarrage))
                {
                    Bot.Log("[Discover] 咒怨发现(TLC_451)：满足郊狼条件(board5atk=" + (coyoteByBoard ? "Y" : "N")
                        + ",photon=" + (coyoteByPhoton ? "Y" : "N")
                        + ",stableBarrage=" + (coyoteByStableBarrage ? "Y" : "N")
                        + ") -> 选择狡诈的郊狼(TIME_047)");
                    return Card.Cards.TIME_047;
                }
            }
            catch
            {
                // ignore
            }

            // 5.8) 窟穴地标前置：手牌被弃组件占比 > 20% 时，优先拿地标（允许忽略当回合法力门槛）。
            try
            {
                if (choices.Contains(Card.Cards.WON_103) && highDiscardRatioForCave)
                {
                    bool hasCaveInHandAlready = HasCardInHand(board, Card.Cards.WON_103);
                    bool hasCaveOnBoardAlready = board != null
                        && board.MinionFriend != null
                        && board.MinionFriend.Any(m => m != null && m.Template != null && m.Template.Id == Card.Cards.WON_103);

                    if (!hasCaveInHandAlready && !hasCaveOnBoardAlready)
                    {
                        if (virtualRemainingManaNow >= 3)
                        {
                            Bot.Log("[Discover] 咒怨发现(TLC_451)：手牌被弃组件占比=" + payoffCountForCaveRatio + "/" + handCountForCaveRatio
                                + " > 20%，且窟穴本回合可落地 -> 优先选择窟穴(WON_103)");
                        }
                        else
                        {
                            Bot.Log("[Discover] 咒怨发现(TLC_451)：手牌被弃组件占比=" + payoffCountForCaveRatio + "/" + handCountForCaveRatio
                                + " > 20%，当前可用费用(含硬币)=" + virtualRemainingManaNow + " < 3 -> 仍优先选择窟穴(WON_103)");
                        }
                        return Card.Cards.WON_103;
                    }

                    Bot.Log("[Discover] 咒怨发现(TLC_451)：手牌被弃组件占比>20%但手/场已有窟穴 -> 跳过重复选择窟穴");
                }
            }
            catch
            {
                // ignore
            }

            // 6) 弃牌收益组件评分器（地标/墓统一口径）：
            // - 多组件默认优先非灵魂弹幕；
            // - 行尸/镀银魔像在友方随从位<=6时显著提权；
            // - 例外：敌方低血随从较多，或缺伤害斩杀时，允许提权灵魂弹幕；
            // - 灵魂弹幕保留优先级：商贩触发 > 时空之爪触发。
            bool bt300SafeForDiscover = handCount > 0 && handCount <= Bt300SafeThresholdForMerchant;
            var payoffPriorityBeforeSoulfire = new[] { Card.Cards.BT_300, Card.Cards.AT_022, Card.Cards.WW_044t, Card.Cards.WW_441, Card.Cards.RLK_532, Card.Cards.WON_098, Card.Cards.KAR_205 };

            try
            {
                var payoffChoicesForDiscover = choices
                    .Where(c => IsDiscardPayoffId(c))
                    .Where(c => c != Card.Cards.BT_300 || bt300SafeForDiscover)
                    .Distinct()
                    .ToList();

                if (payoffChoicesForDiscover.Count > 0)
                {
                    int friendMinionsNow = CountFriendlyMinions(board);
                    int enemyMinionsNow = CountEnemyMinions(board);
                    int enemyLowHpNow = CountEnemyLowHealthMinions(board, 3);
                    int lethalDeficitNow = EstimateLethalDeficit(board);
                    bool preferBarrageForBoardClear = enemyMinionsNow >= 2 && enemyLowHpNow >= 2;
                    bool preferBarrageForLethal = lethalDeficitNow > 0 && lethalDeficitNow <= 6;

                    bool reserveBarrageForMerchant = false;
                    bool reserveBarrageForClaw = false;
                    try { reserveBarrageForMerchant = CanStablyMerchantBarrageAndLikelyDraw(board, virtualRemainingManaNow, choices.Contains(Card.Cards.ULD_163)); } catch { reserveBarrageForMerchant = false; }
                    try { reserveBarrageForClaw = !reserveBarrageForMerchant && CanTriggerClawBarrageThisTurn(board, virtualRemainingManaNow); } catch { reserveBarrageForClaw = false; }

                    int bestScore = int.MinValue;
                    Card.Cards bestChoice = default(Card.Cards);

                    foreach (var id in payoffChoicesForDiscover)
                    {
                        int score = 0;
                        var reason = new List<string>();

                        if (id == Card.Cards.RLK_532)
                        {
                            score += 900; reason.Add("行尸基础+900");
                            if (friendMinionsNow <= 6) { score += 280; reason.Add("友方<=6可召回+280"); }
                            else { score -= 650; reason.Add("友方>6位满风险-650"); }
                        }
                        else if (id == Card.Cards.WON_098 || id == Card.Cards.KAR_205)
                        {
                            score += 860; reason.Add("镀银魔像基础+860");
                            if (friendMinionsNow <= 6) { score += 260; reason.Add("友方<=6可召回+260"); }
                            else { score -= 650; reason.Add("友方>6位满风险-650"); }
                        }
                        else if (id == Card.Cards.AT_022)
                        {
                            score += 780; reason.Add("拳头基础+780");
                        }
                        else if (id == Card.Cards.WW_044t)
                        {
                            score += 760; reason.Add("淤泥桶基础+760");
                        }
                        else if (id == Card.Cards.WW_441)
                        {
                            score += 700; reason.Add("锅炉燃料基础+700");
                        }
                        else if (id == Card.Cards.BT_300)
                        {
                            score += 730; reason.Add("古手基础+730");
                        }
                        else if (id == Card.Cards.RLK_534)
                        {
                            score += 620; reason.Add("灵魂弹幕基础+620");
                            if (payoffChoicesForDiscover.Count >= 2) { score -= 240; reason.Add("多组件默认避弹幕-240"); }
                            if (preferBarrageForBoardClear) { score += 540; reason.Add("敌方低血随从多+540"); }
                            if (preferBarrageForLethal) { score += 700; reason.Add("补斩杀缺口+700"); }

                            if (!preferBarrageForLethal)
                            {
                                if (reserveBarrageForMerchant) { score -= 1050; reason.Add("保留给商贩触发-1050"); }
                                else if (reserveBarrageForClaw) { score -= 700; reason.Add("保留给时空之爪触发-700"); }
                            }
                        }
                        else
                        {
                            score += 300; reason.Add("通用收益+300");
                        }

                        if (id != Card.Cards.RLK_534 && payoffChoicesForDiscover.Contains(Card.Cards.RLK_534))
                        {
                            score += 100;
                            reason.Add("同池含弹幕，非弹幕偏好+100");
                        }

                        Bot.Log("[Discover] 咒怨发现(TLC_451)：组件评分 " + SafeCardName(id) + "(" + id + ")=" + score
                            + " | enemyMinions=" + enemyMinionsNow
                            + ",enemyLowHp=" + enemyLowHpNow
                            + ",lethalDeficit=" + lethalDeficitNow
                            + ",friendMinions=" + friendMinionsNow
                            + " | " + string.Join(";", reason));

                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestChoice = id;
                        }
                    }

                    if (!bestChoice.Equals(default(Card.Cards)))
                    {
                        Bot.Log("[Discover] 咒怨发现(TLC_451)：组件评分最终选择 -> " + SafeCardName(bestChoice) + "(" + bestChoice + ") score=" + bestScore);
                        return bestChoice;
                    }
                }
            }
            catch
            {
                // ignore and fall through
            }

            // 7) 优先拿弃牌收益组件（评分失败时回退顺序）
            foreach (var id in payoffPriorityBeforeSoulfire)
            {
                if (id == Card.Cards.BT_300 && !bt300SafeForDiscover) continue;
                if (choices.Contains(id)) return id;
            }

            // 8) 窟穴地标 (WON_103)：手牌收益占比条件（已有窟穴时跳过重复拿）
            try
            {
                if (handCount > 0
                    && choices.Contains(Card.Cards.WON_103)
                    && highDiscardRatioForCave)
                {
                    bool hasCaveInHandAlready = HasCardInHand(board, Card.Cards.WON_103);
                    bool hasCaveOnBoardAlready = board != null
                        && board.MinionFriend != null
                        && board.MinionFriend.Any(m => m != null && m.Template != null && m.Template.Id == Card.Cards.WON_103);

                    if (hasCaveInHandAlready || hasCaveOnBoardAlready)
                    {
                        Bot.Log("[Discover] 咒怨发现(TLC_451)：已持有/在场有窟穴 -> 跳过重复选择窟穴");
                    }
                    else
                    {
                        Bot.Log("[Discover] 咒怨发现(TLC_451)：手牌被弃组件占比>20% -> 选择窟穴(WON_103)");
                        return Card.Cards.WON_103;
                    }
                }
                else if (handCount > 0
                    && choices.Contains(Card.Cards.WON_103)
                    && !highDiscardRatioForCave
                    && !isAffordableOrPayoff(Card.Cards.WON_103))
                {
                    Bot.Log("[Discover] 咒怨发现(TLC_451)：三选包含窟穴地标，但手牌被弃组件占比<=20%且当前可用费用(含硬币)不足 -> 跳过地标");
                }
            }
            catch
            {
                // ignore
            }

            // 9) 核心过牌组件
            if ((choices.Contains(Card.Cards.BOT_568) && isAffordableOrPayoff(Card.Cards.BOT_568)) || (choices.Contains(Card.Cards.CORE_BOT_568) && isAffordableOrPayoff(Card.Cards.CORE_BOT_568)))
                return choices.Contains(Card.Cards.BOT_568) && isAffordableOrPayoff(Card.Cards.BOT_568) ? Card.Cards.BOT_568 : Card.Cards.CORE_BOT_568;

            // 9.5) 狗头人图书管理员：咒怨发现场景下，优先于功能牌（例如海星）。
            // 但若牌库为空，则跳过，避免疲劳局“过牌过输”。
            try
            {
                if (choices.Contains(Card.Cards.LOOT_014) && isAffordableOrPayoff(Card.Cards.LOOT_014))
                {
                    bool deckEmptyNow = false;
                    try { deckEmptyNow = board == null || CurrentDeck(board).Count <= 0; } catch { deckEmptyNow = false; }

                    if (!deckEmptyNow)
                    {
                        Bot.Log("[Discover] 咒怨发现(TLC_451)：三选含狗头人图书管理员 -> 优先于功能牌(如海星)");
                        return Card.Cards.LOOT_014;
                    }

                    Bot.Log("[Discover] 咒怨发现(TLC_451)：牌库为空 -> 跳过狗头人图书管理员");
                }
            }
            catch
            {
                // ignore
            }

            // 10) 功能组件
            if (choices.Contains(Card.Cards.TSC_032) && isAffordableOrPayoff(Card.Cards.TSC_032)) return Card.Cards.TSC_032;

            // 11) 恐怖海盗：有刀时可 0 费
            if (choices.Contains(Card.Cards.CORE_NEW1_022) && isAffordableOrPayoff(Card.Cards.CORE_NEW1_022))
            {
                bool hasClawInHandForPirate = false;
                bool hasClawEquippedForPirate = false;
                bool clawCanTriggerNowForPirate = false;
                try { hasClawInHandForPirate = HasCardInHand(board, Card.Cards.END_016); } catch { hasClawInHandForPirate = false; }
                try { hasClawEquippedForPirate = board != null && board.WeaponFriend != null && board.WeaponFriend.Template != null && board.WeaponFriend.Template.Id == Card.Cards.END_016; } catch { hasClawEquippedForPirate = false; }
                try { clawCanTriggerNowForPirate = CanTriggerClawBarrageThisTurn(board, virtualRemainingManaNow); } catch { clawCanTriggerNowForPirate = false; }

                if ((hasClawInHandForPirate || hasClawEquippedForPirate) && clawCanTriggerNowForPirate)
                {
                    Bot.Log("[Discover] 咒怨发现(TLC_451)：刀线可在本回合稳定触发，发现恐怖海盗 -> " + SafeCardName(Card.Cards.CORE_NEW1_022));
                    return Card.Cards.CORE_NEW1_022;
                }
                if (hasClawInHandForPirate || hasClawEquippedForPirate)
                {
                    Bot.Log("[Discover] 咒怨发现(TLC_451)：虽有刀但本回合难以稳定触发，跳过恐怖海盗优先");
                }
            }
            // 若仍未命中：兜底仅返回“被弃组件”或“当前法力可打出”的牌，避免落入通用评分分支选到打不出的非组件
            // 新口径：若手上/场上已存在窟穴，TLC_451 兜底默认避开 WON_103（除非三选只剩地标可选）。
            try
            {
                bool avoidCaveInFallback = false;
                try
                {
                    bool hasCaveInHandAlready = HasCardInHand(board, Card.Cards.WON_103);
                    bool hasCaveOnBoardAlready = board != null
                        && board.MinionFriend != null
                        && board.MinionFriend.Any(m => m != null && m.Template != null && m.Template.Id == Card.Cards.WON_103);
                    avoidCaveInFallback = hasCaveInHandAlready || hasCaveOnBoardAlready;
                }
                catch { avoidCaveInFallback = false; }

                var safeChoices = choices.Where(c => isAffordableOrPayoff(c)).ToList();
                if (!allowPickSoulfire)
                {
                    safeChoices = safeChoices.Where(c => c != Card.Cards.EX1_308).ToList();
                }

                if (avoidCaveInFallback)
                {
                    var safeChoicesWithoutCave = safeChoices.Where(c => c != Card.Cards.WON_103).ToList();
                    if (safeChoicesWithoutCave.Count > 0)
                    {
                        Bot.Log("[Discover] 咒怨发现(TLC_451)：兜底避开窟穴(手/场已有) -> " + SafeCardName(safeChoicesWithoutCave[0]));
                        return safeChoicesWithoutCave[0];
                    }

                    var anyChoicesWithoutCave = choices.Where(c => c != Card.Cards.WON_103).ToList();
                    if (anyChoicesWithoutCave.Count > 0)
                    {
                        Bot.Log("[Discover] 咒怨发现(TLC_451)：仅窟穴可立即落地，兜底改选非窟穴 -> " + SafeCardName(anyChoicesWithoutCave[0]));
                        return anyChoicesWithoutCave[0];
                    }
                }

                if (safeChoices.Count > 0)
                {
                    var contextualPick = PickContextualTlc451Fallback(
                        board,
                        safeChoices,
                        strictRemainingManaNow,
                        manaUnknownForNullBoard,
                        handCount
                    );
                    if (!contextualPick.Equals(default(Card.Cards)))
                    {
                        return contextualPick;
                    }

                    Bot.Log("[Discover] 咒怨发现(TLC_451)：兜底返回可用牌 -> " + SafeCardName(safeChoices[0]));
                    return safeChoices[0];
                }
            }
            catch
            {
                // ignore
            }
            return default(Card.Cards);
        }

        private static Card.Cards TryPickEggWarlockTlc451Discover(Board board, List<Card.Cards> choices)
        {
            try
            {
                if (choices == null || choices.Count == 0) return default(Card.Cards);
                if (board == null) return default(Card.Cards);

                bool hasEggOnBoard = false;
                bool hasEggInHand = false;
                bool hasEggInDeck = false;
                int eggInGrave = 0;

                try { hasEggOnBoard = board.MinionFriend != null && board.MinionFriend.Any(m => m != null && m.Template != null && _eggStages.Contains(m.Template.Id)); } catch { hasEggOnBoard = false; }
                try { hasEggInHand = board.Hand != null && board.Hand.Any(c => c != null && c.Template != null && _eggStages.Contains(c.Template.Id)); } catch { hasEggInHand = false; }
                try { hasEggInDeck = HasCardInDeck(board, Card.Cards.DINO_410); } catch { hasEggInDeck = false; }
                try { eggInGrave = board.FriendGraveyard != null ? board.FriendGraveyard.Count(x => _eggStages.Contains(x)) : 0; } catch { eggInGrave = 0; }

                // 保护：仅在“蛋术上下文”触发，避免误伤其他构筑。
                bool hasEggWarlockSignatureInChoices = choices.Any(c =>
                    c == Card.Cards.DINO_410 || c == Card.Cards.DINO_411
                    || c == Card.Cards.AV_312 || c == Card.Cards.CS3_002 || c == Card.Cards.TRL_249
                    || c == Card.Cards.BAR_910 || c == Card.Cards.LOOT_017 || c == Card.Cards.VAC_939
                    || c == Card.Cards.SCH_514 || c == Card.Cards.MAW_002);

                if (!hasEggOnBoard && !hasEggInHand && !hasEggInDeck && eggInGrave <= 0 && !hasEggWarlockSignatureInChoices)
                    return default(Card.Cards);

                bool hasCoinNow = false;
                try { hasCoinNow = HasCardInHand(board, Card.Cards.GAME_005); } catch { hasCoinNow = false; }
                int remainingManaNow = 0;
                try { remainingManaNow = board.ManaAvailable; } catch { remainingManaNow = 0; }
                int virtualRemainingManaNow = remainingManaNow + (hasCoinNow ? 1 : 0);
                if (virtualRemainingManaNow > 10) virtualRemainingManaNow = 10;

                // 1) 场上有蛋：优先触发蛋组件（按用户指定顺序，且必须本回合可落地）
                if (hasEggOnBoard)
                {
                    foreach (var id in _eggPopToolsPriority)
                    {
                        if (!choices.Contains(id)) continue;
                        if (!IsEggPopToolPlayableNow(board, id, virtualRemainingManaNow)) continue;

                        Bot.Log("[Discover] 蛋术TLC_451：场上有蛋且可用费=" + virtualRemainingManaNow + " -> 优先选择触发蛋组件 " + SafeCardName(id) + "(" + id + ")");
                        return id;
                    }
                }

                // 2) 牌库有蛋，且手/场无蛋：优先找蛋组件（神圣布蛋者 -> 凯洛斯的蛋），并校验费用
                if (hasEggInDeck && !hasEggInHand && !hasEggOnBoard)
                {
                    if (choices.Contains(Card.Cards.DINO_411) && IsDiscoverChoicePlayableNow(Card.Cards.DINO_411, virtualRemainingManaNow))
                    {
                        Bot.Log("[Discover] 蛋术TLC_451：牌库有蛋且手/场无蛋，且可用费=" + virtualRemainingManaNow + " -> 优先神圣布蛋者(DINO_411)");
                        return Card.Cards.DINO_411;
                    }

                    if (choices.Contains(Card.Cards.DINO_410) && IsDiscoverChoicePlayableNow(Card.Cards.DINO_410, virtualRemainingManaNow))
                    {
                        Bot.Log("[Discover] 蛋术TLC_451：牌库有蛋且手/场无蛋，且可用费=" + virtualRemainingManaNow + " -> 次选凯洛斯的蛋(DINO_410)");
                        return Card.Cards.DINO_410;
                    }
                }

                // 3) 牌库没蛋 + 手/场无蛋 + 坟场有蛋：优先复活蛋组件（亡者复生 -> 尸身保护令），并校验费用
                if (!hasEggInDeck && !hasEggInHand && !hasEggOnBoard && eggInGrave > 0)
                {
                    if (choices.Contains(Card.Cards.SCH_514) && IsDiscoverChoicePlayableNow(Card.Cards.SCH_514, virtualRemainingManaNow))
                    {
                        Bot.Log("[Discover] 蛋术TLC_451：牌库/手/场无蛋且坟场有蛋x" + eggInGrave + "，可用费=" + virtualRemainingManaNow + " -> 优先亡者复生(SCH_514)");
                        return Card.Cards.SCH_514;
                    }

                    if (choices.Contains(Card.Cards.MAW_002) && IsDiscoverChoicePlayableNow(Card.Cards.MAW_002, virtualRemainingManaNow))
                    {
                        Bot.Log("[Discover] 蛋术TLC_451：牌库/手/场无蛋且坟场有蛋x" + eggInGrave + "，可用费=" + virtualRemainingManaNow + " -> 次选尸身保护令(MAW_002)");
                        return Card.Cards.MAW_002;
                    }
                }
            }
            catch
            {
                // ignore
            }

            return default(Card.Cards);
        }

        private static bool HasTemporaryBarrageInHand(Board board)
        {
            try
            {
                if (board == null || board.Hand == null) return false;
                return board.Hand.Any(c => c != null && c.Template != null && c.Template.Id == Card.Cards.RLK_534
                                           && (c.HasTag(SmartBot.Plugins.API.Card.GAME_TAG.LUNAHIGHLIGHTHINT)
                                               || (c.CurrentCost < (c.Template != null ? c.Template.Cost : 99))));
            }
            catch
            {
                return false;
            }
        }

        private static string SafeCardName(Card.Cards id)
        {
            try
            {
                var template = CardTemplate.LoadFromId(id);
                return template != null && !string.IsNullOrEmpty(template.Name) ? template.Name : id.ToString();
            }
            catch
            {
                return id.ToString();
            }
        }

        private static bool HasCardInHand(Board board, Card.Cards id)
        {
            return board != null && board.Hand != null && board.Hand.Any(c => c.Template != null && c.Template.Id == id);
        }

        private static bool HasCardInDeck(Board board, Card.Cards id)
        {
            // 如果是灵魂弹幕，使用精准估算公式
            if (id == Card.Cards.RLK_534)
            {
                return GetTrueBarrageCountInDeck(board) > 0;
            }

            // 说明：board.Deck 更接近“初始牌表/已知套牌列表”，并不一定等同于“当前剩余牌库”。
            // 为了让 Discover 与实战状态一致，这里改为基于 CurrentDeck(board) 推断“当前牌库仍剩余”。
            try
            {
                return board != null && board.Deck != null && CurrentDeck(board).Any(c => c == id);
            }
            catch
            {
                return board != null && board.Deck != null && board.Deck.Any(c => c == id);
            }
        }

        private static int GetConfiguredBarrageBaseCount(Board board)
        {
            try
            {
                if (board != null && board.Deck != null)
                {
                    int deckListedCopies = board.Deck.Count(id => id == Card.Cards.RLK_534);
                    if (deckListedCopies > 0) return deckListedCopies;
                }
            }
            catch { }

            // Fallback when deck list is unavailable.
            return 2;
        }

        /// <summary>
        /// 保守估算牌库剩余灵魂弹幕数量：
        /// 按初始卡表中的弹幕基数 - 当前已见（手牌非临时 + 坟场）计算。
        /// 目的：避免“牌库已空但仍被判定有弹幕”的误判。
        /// </summary>
        private static int GetTrueBarrageCountInDeck(Board board)
        {
            if (board == null)
            {
                return _dwLastKnownBarrageRemainEstimate >= 0 ? _dwLastKnownBarrageRemainEstimate : 0;
            }
            try
            {
                try { UpdateTemporaryBarrageTracking(board); } catch { }
                int baseCount = GetConfiguredBarrageBaseCount(board);

                // 手牌灵魂弹幕（区分临时/非临时）
                int barrageHandNonTemp = board.Hand != null
                    ? board.Hand.Count(c => c != null && c.Template != null && c.Template.Id == Card.Cards.RLK_534 && !IsTemporaryCardInstance(c))
                    : 0;

                // 坟场灵魂弹幕数量
                int barrageGrave = board.FriendGraveyard != null ? board.FriendGraveyard.Count(c => c == Card.Cards.RLK_534) : 0;

                int estimate = baseCount - barrageHandNonTemp - barrageGrave;

                // 负数时归零（牌库不可能有负数张牌）
                if (estimate < 0) estimate = 0;

                _dwLastKnownBarrageRemainEstimate = estimate;
                return estimate;
            }
            catch
            {
                return _dwLastKnownBarrageRemainEstimate >= 0 ? _dwLastKnownBarrageRemainEstimate : 0;
            }
        }

        private static bool HasAnyDiscardPayoffInHand(Board board)
        {
            if (board == null || board.Hand == null) return false;
            int handCount = board.Hand.Count;
            bool bt300GoodNow = handCount <= Bt300SafeThresholdForMerchant;

            var payoffs = new[]
            {
                Card.Cards.RLK_534, // 灵魂弹幕
                Card.Cards.AT_022,  // 加拉克苏斯之拳
                Card.Cards.WW_044t, // 淤泥桶
                Card.Cards.WW_441,  // 锅炉燃料
                Card.Cards.RLK_532, // 行尸
                Card.Cards.WON_098, // 镀银魔像
                Card.Cards.KAR_205, // 镀银魔像 KAR_205
            };

            foreach (var c in board.Hand)
            {
                if (c == null || c.Template == null) continue;
                var id = c.Template.Id;
                if (payoffs.Contains(id)) return true;
                if (bt300GoodNow && id == Card.Cards.BT_300) return true; // 古尔丹之手（偏向手牌<=8窗口）
            }
            return false;
        }

        private static bool IsDiscardWarlock(Board board)
        {
            if (board == null) return false;
            if (!board.FriendClass.ToString().Equals("Warlock", StringComparison.OrdinalIgnoreCase)) return false;

            var signature = new[]
            {
                Card.Cards.RLK_534, // Soul Barrage
                Card.Cards.ULD_163, // Expired Merchant
                Card.Cards.TOY_916, // Sketch Artist
                Card.Cards.EX1_308, // Soulfire
                Card.Cards.WON_103, // Chamber of Viscidus
                Card.Cards.TLC_451, // Cursed Catacombs
                Card.Cards.AT_022,  // Fist of Jaraxxus
                Card.Cards.RLK_532, // Walking Dead
                Card.Cards.WW_044t, // Slime Bucket
                Card.Cards.WW_441,  // Furnace Fuel
                Card.Cards.WON_098, // Silverware Golem
                Card.Cards.KAR_205, // Silverware Golem KAR_205
                Card.Cards.BT_300,  // Hand of Gul'dan
            };

            int found = 0;
            foreach (var id in signature)
            {
                if (HasCardInHand(board, id) || HasCardInDeck(board, id)) found++;
            }

            return found >= 3;
        }

        private static bool IsBoardUnderPressure(Board board)
        {
            if (board == null) return false;
            try
            {
                int enemyAtk = (board.MinionEnemy != null ? board.MinionEnemy.Sum(x => x.CurrentAtk) : 0)
                             + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
                int friendHpArmor = (board.HeroFriend != null) ? (board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor) : 30;

                // 1) 敌方场攻威胁到生命（例如2-3回合内可能死）
                if (friendHpArmor <= 15 && enemyAtk >= 10) return true;
                if (friendHpArmor <= 10 && enemyAtk >= 6) return true;

                // 2) 敌方随从数量过多
                if (board.MinionEnemy != null && board.MinionEnemy.Count >= 4) return true;

                // 3) 对方场上有大怪 (>= 6攻)
                if (board.MinionEnemy != null && board.MinionEnemy.Any(m => m.CurrentAtk >= 6)) return true;

                // 4) 敌方低血量随从较多（>= 3个），需要清场
                if (board.MinionEnemy != null && board.MinionEnemy.Count(m => m.CurrentHealth <= 3) >= 2) return true;
            }
            catch { }
            return false;
        }

        private static int CountEnemyLowHealthMinions(Board board, int hpThreshold)
        {
            try
            {
                if (board == null || board.MinionEnemy == null) return 0;
                return board.MinionEnemy.Count(m => m != null && m.CurrentHealth > 0 && m.CurrentHealth <= hpThreshold);
            }
            catch
            {
                return 0;
            }
        }

        private static int CountEnemyMinions(Board board)
        {
            try
            {
                if (board == null || board.MinionEnemy == null) return 0;
                return board.MinionEnemy.Count(m => m != null && m.CurrentHealth > 0);
            }
            catch
            {
                return 0;
            }
        }

        private static int CountFriendlyMinions(Board board)
        {
            try
            {
                if (board == null || board.MinionFriend == null) return 0;
                return board.MinionFriend.Count(m => m != null && m.Template != null);
            }
            catch
            {
                return 0;
            }
        }

        private static int EstimateLethalDeficit(Board board)
        {
            try
            {
                if (board == null || board.HeroEnemy == null) return 99;
                int hpArmor = Math.Max(0, board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor);
                int stableDamage = EstimateStableFaceDamageThisTurn(board);
                return hpArmor - stableDamage;
            }
            catch
            {
                return 99;
            }
        }

        // 颜射术 Discover 侧：稳定打脸/斩杀估算（口径尽量与主策略一致）
        // - 稳定直伤：永远计入
        // - 随机直伤：仅在对面无随从时计入（避免高估“稳定斩杀/稳定打脸”）
        private static readonly Dictionary<Card.Cards, int> DiscardWarlockStableFaceSpellDamage = new Dictionary<Card.Cards, int>
        {
            { Card.Cards.CORE_CS2_062, 3 }, // Hellfire：对所有角色造成3点
            { Card.Cards.EX1_308, 4 },      // Soulfire：灵魂火是定向直伤，计入稳定直伤
        };

        private static readonly Dictionary<Card.Cards, int> DiscardWarlockRandomFaceSpellDamage = new Dictionary<Card.Cards, int>
        {
            { Card.Cards.TIME_027, 6 }, // 超光子弹幕：随机直伤
            { Card.Cards.RLK_534, 6 },  // 灵魂弹幕：随机直伤
            { Card.Cards.WW_044t, 4 },  // 淤泥桶：对生命值最低的敌人造成4；对面无随从时等价打脸
            { Card.Cards.AT_022, 4 },   // 加拉克苏斯之拳：随机直伤
        };

        private static int MaxFaceSpellDamageWithinMana(Board board)
        {
            if (board == null || board.Hand == null) return 0;

            int mana = Math.Max(0, board.ManaAvailable);
            bool enemyHasNoMinions = board.MinionEnemy == null || board.MinionEnemy.Count == 0;

            // 0/1 背包：mana<=10，手牌<=10，DP足够轻量。
            var dp = new int[mana + 1];

            foreach (var c in board.Hand)
            {
                if (c == null || c.Template == null) continue;
                if (c.Type != Card.CType.SPELL) continue;

                var id = c.Template.Id;
                int cost = c.CurrentCost;
                if (cost < 0 || cost > mana) continue;

                int dmg;
                bool ok = DiscardWarlockStableFaceSpellDamage.TryGetValue(id, out dmg)
                          || (enemyHasNoMinions && DiscardWarlockRandomFaceSpellDamage.TryGetValue(id, out dmg));
                if (!ok || dmg <= 0) continue;

                for (int m = mana; m >= cost; m--)
                    dp[m] = Math.Max(dp[m], dp[m - cost] + dmg);
            }

            int best = 0;
            for (int m = 0; m <= mana; m++)
                if (dp[m] > best) best = dp[m];
            return best;
        }

        private static int EstimateStableFaceDamageThisTurn(Board board)
        {
            if (board == null) return 0;

            int total = 0;
            total += MaxFaceSpellDamageWithinMana(board);

            bool enemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(x => x != null && x.IsTaunt);
            if (!enemyHasTaunt)
                total += CurrentFriendAttack(board);

            return total;
        }

        // Card Handle Pick Decision from SB
        public Card.Cards HandlePickDecision(Card.Cards originCard, List<Card.Cards> choices, Board board) // originCard; ID of card played by SB: choices; names of cards for selection: board; 3 states , Even, Losing, Winning
        {
            if (choices == null || choices.Count == 0)
            {
                Bot.Log("[Discover] UniversalDiscover：未收到三选列表，回退返回 originCard");
                return originCard;
            }

            Bot.Log("[Discover] 触发发现：来源卡牌 = " + SafeCardName(originCard) + " (" + originCard + ")");

            // 用户硬规则：永远不选「无限巨龙姆诺兹多」(TIME_024)。
            // 入口先过滤一遍，避免后续任意分支（含硬规则分支）误选。
            try
            {
                if (choices.Contains(Card.Cards.TIME_024))
                {
                    var withoutMurozondUnbounded = choices.Where(c => c != Card.Cards.TIME_024).ToList();
                    if (withoutMurozondUnbounded.Count > 0)
                    {
                        choices = withoutMurozondUnbounded;
                        Bot.Log("[Discover] 硬规则：候选含无限巨龙姆诺兹多(TIME_024) -> 已过滤，永不选择");
                    }
                    else
                    {
                        Bot.Log("[Discover] 硬规则：候选仅剩 TIME_024，无法过滤，保留原候选防止异常");
                    }
                }
            }
            catch
            {
                // ignore
            }

            try
            {
                if (AllowLegacyDiscoverFallbackCompat())
                {
                    Card.Cards generatedBoxChoice = TryPickGeneratedBoxOcrChoice(originCard, choices);
                    if (!generatedBoxChoice.Equals(default(Card.Cards)) && choices.Contains(generatedBoxChoice))
                    {
                        Bot.Log("[Discover][BoxOCR] generated ref=A -> " + SafeCardName(generatedBoxChoice) + "(" + generatedBoxChoice + ")");
                        return generatedBoxChoice;
                    }
                }
            }
            catch (Exception ex)
            {
                Bot.Log("[Discover][BoxOCR] 生成区接管失败 -> " + ex.Message);
            }

            try
            {
                Card.Cards boxOcrChoice = TryPickDiscoverByBoxOcr(originCard, choices, board);
                if (!boxOcrChoice.Equals(default(Card.Cards)) && choices.Contains(boxOcrChoice))
                    return boxOcrChoice;

                if (IsPureLearningModeEnabledCompat() && !AllowLegacyDiscoverFallbackCompat())
                {
                    Bot.Log("[Discover][NewLogic] miss -> random fallback");
                    return choices[random.Next(0, choices.Count)];
                }
            }
            catch (Exception ex)
            {
                Bot.Log("[Discover][BoxOCR] 顶层接管失败 -> " + ex.Message);
            }

            // 影焰猎豹(FIR_924)/橱窗看客(TOY_652, TOY_652t)/躁动的愤怒卫士(GDB_132)/
            // 狡诈拷问者(EDR_102)/秘迹观测者(TOY_520)硬规则：
            // 1) 三选含基尔加丹(GDB_145)时，仅在己方场面优势且手里没有基尔加丹时优先选择；
            // 2) 否则若有阿克蒙德(GDB_128)，仅在己方场面优势时优先选择；
            // 3) 否则若有雷兹迪尔(TLC_463)，仅在己方场面优势时优先选择；
            // 4) 否则选择费用最高的候选（同费用按最右优先）。
            bool isWindowShopperToken = string.Equals(originCard.ToString(), "TOY_652t", StringComparison.OrdinalIgnoreCase);
            bool isTreacherousTormentorVariant = originCard.ToString().StartsWith("EDR_102", StringComparison.OrdinalIgnoreCase);
            if (originCard == Card.Cards.FIR_924
                || originCard == Card.Cards.TOY_652
                || isWindowShopperToken
                || originCard == Card.Cards.GDB_132
                || originCard == Card.Cards.EDR_102
                || isTreacherousTormentorVariant
                || originCard == Card.Cards.TOY_520)
            {
                try
                {
                    string originKey = originCard == Card.Cards.FIR_924
                        ? "FIR_924"
                        : (originCard == Card.Cards.TOY_652
                            ? "TOY_652"
                            : (isWindowShopperToken
                                ? "TOY_652t"
                                : (originCard == Card.Cards.GDB_132
                                    ? "GDB_132"
                                    : ((originCard == Card.Cards.EDR_102 || isTreacherousTormentorVariant) ? "EDR_102" : "TOY_520"))));
                    if (isTreacherousTormentorVariant && originCard != Card.Cards.EDR_102)
                        Bot.Log("[Discover] EDR_102 变体来源(" + originCard + ")：复用影焰猎豹同口径发现硬规则");
                    string boardAdvantageSnapshot;
                    bool boardAdvantage = HasFriendlyBoardAdvantageForLegendaryDiscover(board, out boardAdvantageSnapshot);
                    bool hasKiljaedenInHand = HasCardInHand(board, Card.Cards.GDB_145);
                    bool hasArchimondeInHand = HasCardInHand(board, Card.Cards.GDB_128);
                    string archimondeStableSnapshot;
                    bool archimondeStableWindow = ShouldPreferArchimondeInStableMidgameFromStalker(board, originCard, out archimondeStableSnapshot);

                    if (choices.Contains(Card.Cards.GDB_145))
                    {
                        if (boardAdvantage)
                        {
                            if (!hasKiljaedenInHand)
                            {
                                Bot.Log("[Discover] " + originKey + " 硬规则：候选含基尔加丹，且己方场面优势(" + boardAdvantageSnapshot + ")，手里无基尔加丹 -> 优先选择 Kil'jaeden(GDB_145)");
                                return Card.Cards.GDB_145;
                            }

                            Bot.Log("[Discover] " + originKey + " 硬规则：手里已有基尔加丹(GDB_145)，跳过重复选择");
                        }
                        else
                        {
                            Bot.Log("[Discover] " + originKey + " 硬规则：候选含基尔加丹，但未达场面优势(" + boardAdvantageSnapshot + ") -> 跳过");
                        }
                    }

                    if (choices.Contains(Card.Cards.GDB_128))
                    {
                        if (boardAdvantage)
                        {
                            Bot.Log("[Discover] " + originKey + " 硬规则：候选含阿克蒙德，且己方场面优势(" + boardAdvantageSnapshot + ") -> 优先选择 Archimonde(GDB_128)");
                            return Card.Cards.GDB_128;
                        }

                        if (!hasArchimondeInHand && archimondeStableWindow)
                        {
                            Bot.Log("[Discover] " + originKey + " 硬规则：未达场面优势，但命中中盘稳定窗口(" + archimondeStableSnapshot + ") -> 放行并优先选择 Archimonde(GDB_128)");
                            return Card.Cards.GDB_128;
                        }

                        Bot.Log("[Discover] " + originKey + " 硬规则：候选含阿克蒙德，但未达场面优势(" + boardAdvantageSnapshot + ") -> 跳过");
                    }

                    if (boardAdvantage && choices.Contains(Card.Cards.TLC_463))
                    {
                        Bot.Log("[Discover] " + originKey + " 硬规则：局势顺风(" + boardAdvantageSnapshot + ")，基尔加丹/阿克蒙德后优先雷兹迪尔(TLC_463)");
                        return Card.Cards.TLC_463;
                    }

                    var maxCostCandidates = choices;
                    if (!boardAdvantage)
                    {
                        var withoutLateLegendaries = choices
                            .Where(id => id != Card.Cards.GDB_145 && (archimondeStableWindow || id != Card.Cards.GDB_128))
                            .ToList();
                        if (withoutLateLegendaries.Count > 0)
                        {
                            maxCostCandidates = withoutLateLegendaries;
                            Bot.Log("[Discover] " + originKey + " 硬规则：未达场面优势，已剔除基尔加丹"
                                + (archimondeStableWindow ? "（阿克蒙德因稳定窗口保留）" : "/阿克蒙德")
                                + "候选");
                        }
                    }

                    if (hasKiljaedenInHand && maxCostCandidates.Any(id => id == Card.Cards.GDB_145))
                    {
                        var withoutDuplicateKiljaeden = maxCostCandidates.Where(id => id != Card.Cards.GDB_145).ToList();
                        if (withoutDuplicateKiljaeden.Count > 0)
                        {
                            maxCostCandidates = withoutDuplicateKiljaeden;
                            Bot.Log("[Discover] " + originKey + " 硬规则：手里已有基尔加丹，最高费兜底阶段剔除重复基尔加丹");
                        }
                    }

                    Card.Cards maxCostPick = maxCostCandidates[0];
                    int maxCost = -1;

                    foreach (var id in maxCostCandidates)
                    {
                        int cost = -1;
                        try
                        {
                            var t = CardTemplate.LoadFromId(id);
                            cost = t != null ? t.Cost : -1;
                        }
                        catch
                        {
                            cost = -1;
                        }

                        if (cost >= maxCost)
                        {
                            maxCost = cost;
                            maxCostPick = id;
                        }
                    }

                    Bot.Log("[Discover] " + originKey + " 硬规则：最高费用优先 -> 选择 "
                        + SafeCardName(maxCostPick) + "(" + maxCostPick + ",费" + maxCost + ")");
                    return maxCostPick;
                }
                catch
                {
                    // ignore and continue default logic
                }
            }

            // 挟持射线(GDB_123)发现硬规则：
            // 1) 低血高压时：嘲讽 > 突袭（都优先可当回合落地）；
            // 2) 非危急时：基尔加丹 > 阿克蒙德 > 雷兹迪尔（基尔加丹手里有一张就不重复）；
            // 3) 末日守卫尽量不选，仅在“当回合差伤害可补斩杀”时放行。
            if (originCard == Card.Cards.GDB_123 && board != null)
            {
                try
                {
                    int friendHpArmor = 0;
                    try { friendHpArmor = board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor; }
                    catch { friendHpArmor = 0; }

                    int enemyAttack = 0;
                    try
                    {
                        if (board.MinionEnemy != null)
                            enemyAttack += board.MinionEnemy.Where(m => m != null).Sum(m => Math.Max(0, m.CurrentAtk));
                    }
                    catch { }
                    try
                    {
                        if (board.WeaponEnemy != null)
                            enemyAttack += Math.Max(0, board.WeaponEnemy.CurrentAtk);
                    }
                    catch { }

                    bool enemyHasBoard = false;
                    try { enemyHasBoard = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null); }
                    catch { enemyHasBoard = false; }

                    int remainingManaNow = 0;
                    bool hasCoinNow = false;
                    bool fromSnapshot = false;
                    int virtualRemainingManaNow = GetVirtualRemainingManaForDiscover(board, out remainingManaNow, out hasCoinNow, out fromSnapshot);

                    bool desperateWindow = enemyHasBoard && (
                        (friendHpArmor <= 10 && enemyAttack >= Math.Max(1, friendHpArmor - 1))
                        || (friendHpArmor <= 14 && enemyAttack >= 8)
                        || EnemyHasLethal(board));

                    if (desperateWindow)
                    {
                        var tauntCandidates = new List<Card.Cards>();
                        foreach (var id in choices)
                        {
                            var t = CardTemplate.LoadFromId(id);
                            if (t == null) continue;
                            if (t.Type != Card.CType.MINION) continue;
                            if (!t.Taunt) continue;
                            tauntCandidates.Add(id);
                        }

                        if (tauntCandidates.Count > 0)
                        {
                            Card.Cards bestTaunt = tauntCandidates[0];
                            int bestPlayable = -1;
                            int bestBody = int.MinValue;
                            int bestCost = int.MaxValue;

                            foreach (var id in tauntCandidates)
                            {
                                var t = CardTemplate.LoadFromId(id);
                                if (t == null) continue;

                                int cost = Math.Max(0, t.Cost);
                                int playable = IsDiscoverChoicePlayableNow(id, virtualRemainingManaNow) ? 1 : 0;
                                int body = Math.Max(0, t.Health) + Math.Max(0, t.Atk);

                                bool better = false;
                                if (playable > bestPlayable) better = true;
                                else if (playable == bestPlayable && body > bestBody) better = true;
                                else if (playable == bestPlayable && body == bestBody && cost < bestCost) better = true;

                                if (better)
                                {
                                    bestPlayable = playable;
                                    bestBody = body;
                                    bestCost = cost;
                                    bestTaunt = id;
                                }
                            }

                            Bot.Log("[Discover] GDB_123 保命硬规则：敌攻=" + enemyAttack + "，我方血甲=" + friendHpArmor
                                + " -> 优先选择嘲讽 " + SafeCardName(bestTaunt) + "(" + bestTaunt + ")");
                            return bestTaunt;
                        }

                        var rushCandidates = new List<Card.Cards>();
                        foreach (var id in choices)
                        {
                            var t = CardTemplate.LoadFromId(id);
                            if (t == null) continue;
                            if (t.Type != Card.CType.MINION) continue;
                            if (!CardTemplateHasRush(t)) continue;
                            rushCandidates.Add(id);
                        }

                        if (rushCandidates.Count > 0)
                        {
                            Card.Cards bestRush = rushCandidates[0];
                            int bestPlayable = -1;
                            int bestBody = int.MinValue;
                            int bestCost = int.MaxValue;

                            foreach (var id in rushCandidates)
                            {
                                var t = CardTemplate.LoadFromId(id);
                                if (t == null) continue;

                                int cost = Math.Max(0, t.Cost);
                                int playable = IsDiscoverChoicePlayableNow(id, virtualRemainingManaNow) ? 1 : 0;
                                int body = Math.Max(0, t.Health) + Math.Max(0, t.Atk);

                                bool better = false;
                                if (playable > bestPlayable) better = true;
                                else if (playable == bestPlayable && body > bestBody) better = true;
                                else if (playable == bestPlayable && body == bestBody && cost < bestCost) better = true;

                                if (better)
                                {
                                    bestPlayable = playable;
                                    bestBody = body;
                                    bestCost = cost;
                                    bestRush = id;
                                }
                            }

                            Bot.Log("[Discover] GDB_123 保命硬规则：敌攻=" + enemyAttack + "，我方血甲=" + friendHpArmor
                                + " -> 三选无嘲讽，回退优先突袭 " + SafeCardName(bestRush) + "(" + bestRush + ")");
                            return bestRush;
                        }

                        Bot.Log("[Discover] GDB_123 保命硬规则：绝境窗口但三选无嘲讽/突袭，回退常规逻辑");
                    }
                    else
                    {
                        bool hasKiljaedenInHand = false;
                        try { hasKiljaedenInHand = HasCardInHand(board, Card.Cards.GDB_145); } catch { hasKiljaedenInHand = false; }

                        if (choices.Contains(Card.Cards.GDB_145))
                        {
                            if (!hasKiljaedenInHand)
                            {
                                Bot.Log("[Discover] GDB_123 发现规则：血线不危急 -> 基尔加丹优先");
                                return Card.Cards.GDB_145;
                            }
                            Bot.Log("[Discover] GDB_123 发现规则：手里已有基尔加丹，跳过重复选择");
                        }

                        if (choices.Contains(Card.Cards.GDB_128))
                        {
                            Bot.Log("[Discover] GDB_123 发现规则：血线不危急 -> 阿克蒙德次优先");
                            return Card.Cards.GDB_128;
                        }

                        if (choices.Contains(Card.Cards.TLC_463))
                        {
                            Bot.Log("[Discover] GDB_123 发现规则：血线不危急 -> 雷兹迪尔再次优先");
                            return Card.Cards.TLC_463;
                        }

                        int enemyHpArmor = 0;
                        try { enemyHpArmor = board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor; } catch { enemyHpArmor = 0; }
                        int stableFaceDamage = 0;
                        try { stableFaceDamage = EstimateStableFaceDamageThisTurn(board); } catch { stableFaceDamage = 0; }
                        bool enemyHasTauntNow = false;
                        try { enemyHasTauntNow = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt); } catch { enemyHasTauntNow = false; }

                        bool hasCoreDoomguard = choices.Contains(Card.Cards.CORE_EX1_310);
                        bool hasClassicDoomguard = choices.Contains(Card.Cards.EX1_310);
                        if (hasCoreDoomguard || hasClassicDoomguard)
                        {
                            bool canPlayCoreDoomguardNow = hasCoreDoomguard && IsDiscoverChoicePlayableNow(Card.Cards.CORE_EX1_310, virtualRemainingManaNow);
                            bool canPlayClassicDoomguardNow = hasClassicDoomguard && IsDiscoverChoicePlayableNow(Card.Cards.EX1_310, virtualRemainingManaNow);
                            bool canPlayAnyDoomguardNow = canPlayCoreDoomguardNow || canPlayClassicDoomguardNow;

                            int lethalGap = enemyHpArmor - stableFaceDamage;
                            bool allowDoomguardForLethal = canPlayAnyDoomguardNow && !enemyHasTauntNow && lethalGap > 0 && lethalGap <= 5;

                            if (!allowDoomguardForLethal && choices.Count > 1)
                            {
                                var filtered = choices.Where(c => c != Card.Cards.CORE_EX1_310 && c != Card.Cards.EX1_310).ToList();
                                if (filtered.Count > 0)
                                {
                                    choices = filtered;
                                    Bot.Log("[Discover] GDB_123 发现规则：末日守卫默认后置(非当回合补斩杀)，已从候选中剔除");
                                }
                            }
                            else if (allowDoomguardForLethal)
                            {
                                Bot.Log("[Discover] GDB_123 发现规则：末日守卫命中当回合补斩杀窗口(缺口=" + lethalGap + ")，允许选择");
                            }
                        }
                    }
                }
                catch
                {
                    // ignore and continue default logic
                }
            }

            // TLC_451 全局硬规则：永不选择“时空撕裂”(TIME_025t)。
            // 说明：该规则必须在入口生效，覆盖弃牌术分支、蛋术分支、board==null 兜底与通用评分兜底。
            if (IsTlc451LikeDiscoverOrigin(originCard))
            {
                try
                {
                    if (choices.Contains(Card.Cards.TIME_025t))
                    {
                        var filtered = choices.Where(c => c != Card.Cards.TIME_025t).ToList();
                        if (filtered.Count > 0)
                        {
                            choices = filtered;
                            Bot.Log("[Discover] TLC_451 全局硬规则：候选含时空撕裂(TIME_025t) -> 已剔除，不允许发现");
                        }
                    }
                }
                catch
                {
                    // ignore
                }
            }

            // 刷新“可用费用(含硬币)”快照，供 board==null 的 TLC_451 发现口径复用。
            try
            {
                int snapManaNow;
                bool snapHasCoinNow;
                bool snapFromSnapshot;
                GetVirtualRemainingManaForDiscover(board, out snapManaNow, out snapHasCoinNow, out snapFromSnapshot);
            }
            catch
            {
                // ignore
            }

            // TLC_451 全局硬规则：发现候选优先约束为“费用 <= 当前可用费用 + 硬币虚拟1费”。
            // 说明：若三选全部超费（理论上可能出现），改为最低费用兜底，避免偏离“当前可用费用”口径。
            if (IsTlc451LikeDiscoverOrigin(originCard))
            {
                try
                {
                    int remainingManaNow = 0;
                    bool hasCoinNow = false;
                    bool manaFromSnapshot = false;
                    int virtualManaNow = GetVirtualRemainingManaForDiscover(board, out remainingManaNow, out hasCoinNow, out manaFromSnapshot);
                    int strictManaNow = Math.Max(0, Math.Min(10, virtualManaNow));
                    bool manaUnknownForNullBoard = (board == null && !manaFromSnapshot);
                    if (manaUnknownForNullBoard)
                    {
                        Bot.Log("[Discover] TLC_451 顶层硬规则：board为空且无可用费用快照 -> 按0费执行保守硬过滤");
                    }

                    var affordableChoices = new List<Card.Cards>();
                    var removed = new List<string>();

                    foreach (var id in choices)
                    {
                        int cost = 99;
                        try
                        {
                            cost = GetTlc451EffectiveDiscoverCost(board, id);
                        }
                        catch { cost = 99; }

                        // 用户硬规则：速写美术家(TOY_916)仅在可用费用>=4时允许选择。
                        if (id == Card.Cards.TOY_916 && strictManaNow < 4)
                        {
                            removed.Add(SafeCardName(id) + "(" + id + ",费" + cost + ",需>=4)");
                            continue;
                        }

                        if (cost <= strictManaNow)
                            affordableChoices.Add(id);
                        else
                            removed.Add(SafeCardName(id) + "(" + id + ",费" + cost + ")");
                    }

                    if (affordableChoices.Count > 0)
                    {
                        choices = affordableChoices;
                        if (removed.Count > 0)
                            Bot.Log("[Discover] TLC_451 顶层硬规则：当前可用费用(含硬币虚拟1费)=" + strictManaNow + "，已剔除超费候选 -> " + string.Join("；", removed));
                    }
                    else if (removed.Count > 0)
                    {
                        int minCost = 99;
                        try
                        {
                            minCost = choices
                                .Select(c => GetTlc451EffectiveDiscoverCost(board, c))
                                .DefaultIfEmpty(99)
                                .Min();
                        }
                        catch { minCost = 99; }

                        var minCostChoices = new List<Card.Cards>();
                        try
                        {
                            minCostChoices = choices.Where(c =>
                            {
                                try
                                {
                                    int cst = GetTlc451EffectiveDiscoverCost(board, c);
                                    return cst == minCost;
                                }
                                catch { return false; }
                            }).ToList();
                        }
                        catch { minCostChoices = new List<Card.Cards>(); }

                        if (minCostChoices.Count > 0)
                        {
                            choices = minCostChoices;
                            Bot.Log("[Discover] TLC_451 顶层硬规则：当前可用费用(含硬币虚拟1费)=" + strictManaNow + "，三选均超费 -> 改为最低费用兜底(费" + minCost + ")");
                        }
                        else
                        {
                            Bot.Log("[Discover] TLC_451 顶层硬规则：当前可用费用(含硬币虚拟1费)=" + strictManaNow + "，三选均超费且最低费用兜底失败 -> 保留原候选");
                        }
                    }
                }
                catch
                {
                    // ignore
                }
            }

            // ======================================================================
            // 蛋术专项：若己方场上有“凯洛斯的蛋”阶段，并且三选中有【本回合费用足够打出】的破蛋手段，则优先选择。
            // 目的：让发现更稳定地服务“场上有蛋 -> 立刻破蛋”的关键线。
            // 注意：如果这次发现其实是“从手牌三选弃牌”，则不能触发该逻辑（避免把破蛋牌当作弃牌对象）。
            // ======================================================================
            try
            {
                bool isHandDiscardChoice = false;
                try
                {
                    isHandDiscardChoice = board != null
                        && board.Hand != null
                        && choices.All(c => HasCardInHand(board, c));
                }
                catch { isHandDiscardChoice = false; }

                if (!isHandDiscardChoice && HasEggOnFriendlyBoard(board))
                {
                    bool hasCoinNow = false;
                    try { hasCoinNow = HasCardInHand(board, Card.Cards.GAME_005); } catch { hasCoinNow = false; }

                    int remainingManaNow = 0;
                    try { remainingManaNow = board != null ? board.ManaAvailable : 0; } catch { remainingManaNow = 0; }

                    int virtualRemainingManaNow = remainingManaNow + (hasCoinNow ? 1 : 0);
                    if (virtualRemainingManaNow > 10) virtualRemainingManaNow = 10;

                    foreach (var id in _eggPopToolsPriority)
                    {
                        if (!choices.Contains(id)) continue;
                        if (!IsEggPopToolPlayableNow(board, id, virtualRemainingManaNow)) continue;

                        Bot.Log("[Discover] 蛋术发现：场上有蛋且费用足够(" + virtualRemainingManaNow + "费) -> 优先选择破蛋手段 " + SafeCardName(id) + "(" + id + ")");
                        return id;
                    }
                }
            }
            catch
            {
                // ignore
            }

            // 弃牌术专项：刷新本回合“临时被弃组件”标识（用于地标/窟穴弃牌时避开已临时的组件）。
            try
            {
                if (IsDiscardWarlock(board))
                    RefreshDiscardWarlockTempPayoffsMarker(board);
            }
            catch
            {
                // ignore
            }

            // 弃牌术额外专项：通用斩杀判定（只要发现列表中包含能导致本回合斩杀的牌，优先选择）
            try
            {
                if (IsDiscardWarlock(board))
                {
                    // 重要：如果这是“从手牌三选弃牌”（如窟穴/地标），那么这里的选择代表【弃掉】而不是【获得】。
                    // 因此必须跳过“斩杀发现优先”逻辑，避免误选魂火等关键伤害牌去弃掉。
                    bool isHandDiscardChoiceGlobal = false;
                    bool hasCaveOnBoardForSkip = false;
                    try
                    {
                        hasCaveOnBoardForSkip = board != null
                            && board.MinionFriend != null
                            && board.MinionFriend.Any(m => m != null && m.Template != null && m.Template.Id == Card.Cards.WON_103);

                        // 兼容：手牌不足3张时，窟穴/地标可能给出少于3个选项；核心判断应是“选项是否都来自手牌”。
                        isHandDiscardChoiceGlobal = board != null
                            && choices != null
                            && choices.Count > 0
                            && choices.All(c => HasCardInHand(board, c))
                            && (originCard == Card.Cards.WON_103 || hasCaveOnBoardForSkip);
                    }
                    catch { isHandDiscardChoiceGlobal = false; }

                    if (isHandDiscardChoiceGlobal)
                    {
                        Bot.Log("[Discover] 斩杀发现跳过：识别为【手牌弃牌三选】(地标/窟穴)，不做斩杀优先选择");
                    }
                    else
                    {
                    int poolHpNow = CurrentEnemyPoolHealth(board);
                    int burstBaseNow = GetTotalBlastDamagesInHand(board) + CurrentFriendAttack(board);

                    foreach (var opt in choices)
                    {
                        int dmgAdd;
                        bool isDamageSpell = DiscardWarlockStableFaceSpellDamage.TryGetValue(opt, out dmgAdd) || DiscardWarlockRandomFaceSpellDamage.TryGetValue(opt, out dmgAdd);
                        if (isDamageSpell)
                        {
                            int totalSum;
                            int triggerCost = 0;
                            // 发现场景：这张牌不在手牌里，需要加上它的伤害，并检查费用。
                            totalSum = burstBaseNow + dmgAdd;
                            try { var t = CardTemplate.LoadFromId(opt); if (t != null) triggerCost = t.Cost; } catch { }

                            if (board.ManaAvailable >= triggerCost && (totalSum >= poolHpNow))
                            {
                                Bot.Log("[Discover] 颜射术斩杀发现：识别到斩杀组件 -> " + SafeCardName(opt) + " (总伤害: " + totalSum + " VS 血池: " + poolHpNow + ")");
                                return opt;
                            }
                        }
                    }
                    }
                }
            }
            catch { }

            // ======================================================================
            // 弃牌术专项：维希度斯的窟穴 (WON_103) - 选一张【从手牌弃掉】
            // ======================================================================
            // ======================================================================
            // 弃牌术专项：维希度斯的窟穴 (WON_103) - 选一张【从手牌弃掉】
            // ======================================================================
            if (originCard == Card.Cards.WON_103 && IsDiscardWarlock(board))
            {
                // 核心逻辑：这里选择意味着直接弃掉。
                
                // 1) 灵魂弹幕放行：仅在“敌方低血随从较多”或“补足斩杀缺口”时强推。
                //    且保留优先级为：商贩触发 > 时空之爪触发。
                // 特殊处理：如果弹幕本身已经是临时牌（比如速写生成的），且还有非临时收益组件可弃，则避开弹幕。
                if (choices.Contains(Card.Cards.RLK_534))
                {
                    int enemyMinionsNow = CountEnemyMinions(board);
                    int enemyLowHpNow = CountEnemyLowHealthMinions(board, 3);
                    int lethalDeficitNow = EstimateLethalDeficit(board);
                    bool allowByLowHpBoard = enemyMinionsNow >= 2 && enemyLowHpNow >= 2;
                    bool allowByLethalGap = lethalDeficitNow > 0 && lethalDeficitNow <= 6;

                    if (!(allowByLowHpBoard || allowByLethalGap))
                    {
                        Bot.Log("[Discover] 窟穴弃牌：灵魂弹幕不放行（enemyMinions=" + enemyMinionsNow
                            + ",enemyLowHp=" + enemyLowHpNow
                            + ",lethalDeficit=" + lethalDeficitNow + "）");
                    }
                    else
                    {
                    bool isBarrageAlreadyTemp = ShouldAvoidCaveDiscardBecauseAlreadyTemporary(board, Card.Cards.RLK_534);
                    bool hasOtherPayoffsSafe = choices.Any(c => c != Card.Cards.RLK_534 && IsDiscardPayoffId(c) && !ShouldAvoidCaveDiscardBecauseAlreadyTemporary(board, c));

                    if (!isBarrageAlreadyTemp || !hasOtherPayoffsSafe)
                    {
                        bool hasClawEquippedForCave = board != null && board.WeaponFriend != null && board.WeaponFriend.Template != null && board.WeaponFriend.Template.Id == Card.Cards.END_016;
                        int barrageInHandCountForCave = board != null && board.Hand != null
                            ? board.Hand.Count(h => h != null && h.Template != null && h.Template.Id == Card.Cards.RLK_534)
                            : 0;
                        int guldanInHandCountForCave = board != null && board.Hand != null
                            ? board.Hand.Count(h => h != null && h.Template != null && h.Template.Id == Card.Cards.BT_300)
                            : 0;
                        bool hasBarrageInHandForCave = barrageInHandCountForCave > 0;
                        bool hasMultipleBarrageForCave = barrageInHandCountForCave >= 2 && choices.Contains(Card.Cards.RLK_534);
                        bool hasDoubleGuldanForCave = guldanInHandCountForCave >= 2 && choices.Contains(Card.Cards.RLK_534);
                        int virtualManaForCave = board != null ? board.ManaAvailable : 0;
                        try
                        {
                            bool hasCoinForCave = board != null && board.Hand != null
                                && board.Hand.Any(h => h != null && h.Template != null && h.Template.Id == Card.Cards.GAME_005 && h.CurrentCost <= board.ManaAvailable);
                            if (hasCoinForCave) virtualManaForCave = Math.Min(10, virtualManaForCave + 1);
                        }
                        catch { }
                        bool canTriggerByClawNowForCave = CanTriggerClawBarrageThisTurn(board, virtualManaForCave);
                        bool canMerchantBarrageSoonForCave = false;
                        try { canMerchantBarrageSoonForCave = CanStablyMerchantBarrageAndLikelyDraw(board, virtualManaForCave, false); } catch { canMerchantBarrageSoonForCave = false; }

                        bool skipBarrageBecauseReserve = hasBarrageInHandForCave
                            && (canMerchantBarrageSoonForCave || canTriggerByClawNowForCave)
                            && !hasMultipleBarrageForCave
                            && !hasDoubleGuldanForCave
                            && !allowByLethalGap; // 补斩杀时允许覆盖保留策略

                        if (!skipBarrageBecauseReserve)
                        {
                            if (hasMultipleBarrageForCave && (hasClawEquippedForCave || canTriggerByClawNowForCave || canMerchantBarrageSoonForCave))
                            {
                                Bot.Log("[Discover] 窟穴弃牌：手里已有灵魂弹幕x" + barrageInHandCountForCave + "，放开“留给刀”限制");
                            }
                            else if (hasDoubleGuldanForCave && (hasClawEquippedForCave || canTriggerByClawNowForCave))
                            {
                                Bot.Log("[Discover] 窟穴弃牌：手里古尔丹之手x" + guldanInHandCountForCave + "，放开“留给刀”限制");
                            }
                            Bot.Log("[Discover] 窟穴弃牌：灵魂弹幕放行(低血随从=" + enemyLowHpNow
                                + ",补斩杀=" + (allowByLethalGap ? "Y" : "N") + ") -> 优先选择弃掉 " + SafeCardName(Card.Cards.RLK_534));
                            return Card.Cards.RLK_534;
                        }
                        else
                        {
                            if (canMerchantBarrageSoonForCave)
                                Bot.Log("[Discover] 窟穴弃牌：可稳定商贩触发，跳过弃灵魂弹幕，优先保留给过期货物专卖商");
                            else
                                Bot.Log("[Discover] 窟穴弃牌：本回合可稳定挥刀，跳过弃灵魂弹幕，保留给时空之爪触发");
                        }
                    }
                }
                }

                // 2) 其余情况（无压力或无弹幕），采用通用优先级：行尸 > 镀银魔像 > 淤泥桶 > 拳 > 弹幕
                var pick = PickDiscardWarlockCaveDiscard(board, choices, "[Discover] 窟穴弃牌");
                if (!pick.Equals(default(Card.Cards)))
                    return pick;
            }

            // ======================================================================
            // 弃牌术专项：咒怨之墓 (TLC_451) - 从牌库发现【临时牌入物】
            // ======================================================================
            if (IsTlc451LikeDiscoverOrigin(originCard))
            {
                // 先跑蛋术硬规则：避免因为套牌识别(是否弃牌术)导致规则不生效。
                var eggPick = TryPickEggWarlockTlc451Discover(board, choices);
                if (!eggPick.Equals(default(Card.Cards))) return eggPick;

                // 弃牌术专用逻辑仍保留：仅在弃牌术对局中使用。
                if (IsDiscardWarlock(board))
                {
                    var pick = PickDiscardWarlockTlc451Discover(board, choices);
                    if (!pick.Equals(default(Card.Cards))) return pick;
                }
            }
            // ======================================================================
            // 蛋术专项：尸身保护令 (MAW_002) 发现优先安布拉（手/场有蛋时）
            // ======================================================================
            if (originCard == Card.Cards.MAW_002 && choices != null && choices.Count > 0)
            {
                try
                {
                    bool hasEggInHand = board != null && board.Hand != null
                        && board.Hand.Any(c => c != null && c.Template != null && _eggStages.Contains(c.Template.Id));
                    bool hasEggOnBoard = board != null && board.MinionFriend != null
                        && board.MinionFriend.Any(m => m != null && m.Template != null && _eggStages.Contains(m.Template.Id));

                    if ((hasEggInHand || hasEggOnBoard) && choices.Contains(Card.Cards.UNG_900))
                    {
                        Bot.Log("[Discover] MAW_002 发现：手/场有蛋 -> 优先选择灵魂歌者安布拉(UNG_900)");
                        return Card.Cards.UNG_900;
                    }
                }
                catch
                {
                    // ignore
                }
            }
            // 兜底修正：若当前对局被识别为弃牌术并且三选中包含灵魂弹幕(RLK_534)或其他被弃收益组件，
            // 则在多数情形下优先选择被弃收益组件，避免因为 originCard 错位 or 后续分支覆盖而错失弃牌收益。
            try
            {
                if (IsDiscardWarlock(board) && choices != null && choices.Count > 0)
                {
                    // 1) 强效判定：基于手牌临时状态与包含关系识别发现类型（地标弃牌 vs 咒怨之墓发现）
                    bool isChooseToDiscardFromHand = false;

                    bool hasCaveOnBoard = false;
                    try
                    {
                        hasCaveOnBoard = board != null
                                     && board.MinionFriend != null
                                     && board.MinionFriend.Any(m => m != null
                                                                && m.Template != null
                                                                && m.Template.Id == Card.Cards.WON_103);
                    }
                    catch { hasCaveOnBoard = false; }

                    try { 
                        if (board != null && board.Hand != null)
                        {
                            // 检查三选是否都在手牌中
                            bool allChoicesInHand = choices.All(c => board.Hand.Any(h => h != null && h.Template != null && h.Template.Id == c));
                            // 统计临时牌数量
                            int tempCount = board.Hand.Count(h => IsTemporaryCardInstance(h));

                            // 地标/窟穴弃牌识别：三选都在手牌中，且场上存在窟穴 => 这是“弃手牌”选择
                            // 说明：有时 originCard 会错位(例如显示成 END_016)，这里用局面特征兜底识别。
                            if (!isChooseToDiscardFromHand && choices.Count > 0 && allChoicesInHand && (hasCaveOnBoard || originCard == Card.Cards.WON_103))
                            {
                                isChooseToDiscardFromHand = true;
                                Bot.Log("[Discover] 动态识别：判定为【地标弃牌】(场上有窟穴，三选全在手，临时牌=" + tempCount + "张)");
                            }
                            // 咒怨之墓/牌库发现：至少有一张牌不在手牌中
                            else if (choices.Any(c => !board.Hand.Any(h => h != null && h.Template != null && h.Template.Id == c))) {
                                Bot.Log("[Discover] 动态识别：判定为【咒怨之墓/牌库发现】(发现列表中有非手牌卡)");
                            }
                        }
                    } catch { }

                    // 如果判定为【地标弃牌】
                    if (isChooseToDiscardFromHand)
                    {
                        var discardPick = PickDiscardWarlockCaveDiscard(board, choices, "[Discover] 动态识别-地标弃牌");
                        if (!discardPick.Equals(default(Card.Cards))) return discardPick;
                    }
                    // 2) 常规兜底优先级（适用于非弃牌、即“发现入手”场景）
                    // 口径：
                    // - 多组件默认优先非灵魂弹幕；
                    // - 敌方低血随从多/补斩杀时，灵魂弹幕前移；
                    // - 灵魂弹幕保留优先级：商贩 > 时空之爪。
                    int enemyMinionsForFallback = CountEnemyMinions(board);
                    int enemyLowHpForFallback = CountEnemyLowHealthMinions(board, 3);
                    int lethalDeficitForFallback = EstimateLethalDeficit(board);
                    bool preferBarrageForFallback = (enemyMinionsForFallback >= 2 && enemyLowHpForFallback >= 2)
                        || (lethalDeficitForFallback > 0 && lethalDeficitForFallback <= 6);
                    int friendMinionsForFallback = CountFriendlyMinions(board);

                    var discoverPriority = preferBarrageForFallback
                        ? new[]
                        {
                            Card.Cards.BT_300,  // 古尔丹之手
                            Card.Cards.RLK_534, // 灵魂弹幕（低血清场/补斩杀放行）
                            Card.Cards.RLK_532, // 行尸
                            Card.Cards.WON_098, // 镀银魔像
                            Card.Cards.KAR_205, // 镀银魔像（旧版）
                            Card.Cards.AT_022,  // 加拉克苏斯之拳
                            Card.Cards.WW_044t, // 淤泥桶
                            Card.Cards.WW_441,  // 锅炉燃料
                            Card.Cards.ULD_163, // 过期货物专卖商
                        }
                        : new[]
                        {
                            Card.Cards.BT_300,  // 古尔丹之手
                            Card.Cards.RLK_532, // 行尸
                            Card.Cards.WON_098, // 镀银魔像
                            Card.Cards.KAR_205, // 镀银魔像（旧版）
                            Card.Cards.AT_022,  // 加拉克苏斯之拳
                            Card.Cards.WW_044t, // 淤泥桶
                            Card.Cards.WW_441,  // 锅炉燃料
                            Card.Cards.RLK_534, // 灵魂弹幕（默认后置）
                            Card.Cards.ULD_163, // 过期货物专卖商
                        };

                    // 复盘：窟穴/地标链路的 discover 在部分时序里会被错误归因到其它出牌（例如 TIME_027），
                    // 从而落到“发现入手”的兜底优先级，导致误选 ULD_163。
                    // 口径：只要场上有窟穴且这次发现明显更像窟穴/地标相关（三选里出现被弃收益组件，或 originCard 被错位成 TIME_027），就跳过 ULD_163。
                    bool skipMerchantBecauseLikelyLandmarkDiscover = false;
                    try
                    {
                        skipMerchantBecauseLikelyLandmarkDiscover = hasCaveOnBoard
                            && !isChooseToDiscardFromHand
                            && choices.Contains(Card.Cards.ULD_163)
                            && (originCard == Card.Cards.TIME_027 || choices.Any(c => IsDiscardPayoffId(c)));
                    }
                    catch { skipMerchantBecauseLikelyLandmarkDiscover = false; }
                    
                    int handCountLocalForDiscover = board != null && board.Hand != null ? board.Hand.Count : 0;
                    int virtualRemainingManaForDiscover = 0;
                    bool stableMerchantBarrageForDiscover = false;
                    try
                    {
                        bool hasCoinForDiscover = HasCardInHand(board, Card.Cards.GAME_005);
                        int manaNowForDiscover = board != null ? board.ManaAvailable : 0;
                        virtualRemainingManaForDiscover = manaNowForDiscover + (hasCoinForDiscover ? 1 : 0);
                        if (virtualRemainingManaForDiscover > 10) virtualRemainingManaForDiscover = 10;
                    }
                    catch
                    {
                        virtualRemainingManaForDiscover = board != null ? board.ManaAvailable : 0;
                    }
                    try { stableMerchantBarrageForDiscover = CanStablyMerchantBarrageAndLikelyDraw(board, virtualRemainingManaForDiscover, choices.Contains(Card.Cards.ULD_163)); } catch { stableMerchantBarrageForDiscover = false; }
                    bool stableClawBarrageForDiscover = false;
                    try { stableClawBarrageForDiscover = !stableMerchantBarrageForDiscover && CanTriggerClawBarrageThisTurn(board, virtualRemainingManaForDiscover); } catch { stableClawBarrageForDiscover = false; }
                    
                    foreach (var id in discoverPriority)
                    {
                        // 发现口径：古尔丹之手偏向手牌<=8窗口
                        if (id == Card.Cards.BT_300 && !(handCountLocalForDiscover > 0 && handCountLocalForDiscover <= Bt300SafeThresholdForMerchant)) continue;

                        // 避免在可用法力不足4费时拿拳头(AT_022)，除非是可能弃牌的操作
                        if (id == Card.Cards.AT_022 && board != null && board.ManaAvailable < 4 && !isChooseToDiscardFromHand) continue;

                        // 行尸/镀银魔像：友方随从位>6时收益显著下降，默认跳过（除非没有其它被弃组件）
                        if ((id == Card.Cards.RLK_532 || id == Card.Cards.WON_098 || id == Card.Cards.KAR_205)
                            && friendMinionsForFallback > 6
                            && choices.Any(c => c != id && IsDiscardPayoffId(c)))
                        {
                            continue;
                        }

                        if (id == Card.Cards.RLK_534)
                        {
                            bool hasOtherPayoffChoices = choices.Any(c => c != Card.Cards.RLK_534 && IsDiscardPayoffId(c));
                            if (!preferBarrageForFallback && hasOtherPayoffChoices)
                            {
                                if (stableMerchantBarrageForDiscover)
                                {
                                    Bot.Log("[Discover] 兜底跳过：灵魂弹幕(RLK_534) - 优先保留给过期货物专卖商触发");
                                    continue;
                                }
                                if (stableClawBarrageForDiscover)
                                {
                                    Bot.Log("[Discover] 兜底跳过：灵魂弹幕(RLK_534) - 次级保留给时空之爪触发");
                                    continue;
                                }
                            }
                        }
                        
                        // 过期货物专卖商(ULD_163)仅在“非弃牌”场景下作为高优先级
                        if (id == Card.Cards.ULD_163 && isChooseToDiscardFromHand) continue;

                        if (id == Card.Cards.ULD_163 && skipMerchantBecauseLikelyLandmarkDiscover)
                        {
                            Bot.Log("[Discover] 兜底跳过：疑似窟穴/地标发现链路 -> 不选过期货物专卖商(ULD_163)");
                            continue;
                        }
                        
                        // 用户诉求：仅在可稳定商贩弃灵魂弹幕时，才考虑选择商贩。
                        if (id == Card.Cards.ULD_163 && !stableMerchantBarrageForDiscover)
                        {
                            Bot.Log("[Discover] 兜底跳过：过期货物专卖商(ULD_163) - 不满足“稳定弃弹幕+较快触发亡语”条件，不选商贩");
                            continue;
                        }


                        if (choices.Contains(id))
                        {
                            Bot.Log("[Discover] 兜底：弃牌术对局(弃牌动作=" + isChooseToDiscardFromHand + ") -> 优先选择 -> " + SafeCardName(id));
                            return id;
                        }
                    }

                    // 兜底补充：锅炉燃料(WW_441)（避免手牌太满导致发现后爆牌）
                    try
                    {
                        int handCountLocal = board != null && board.Hand != null ? board.Hand.Count : 0;
                        if (choices.Contains(Card.Cards.WW_441) && handCountLocal <= 6)
                        {
                            Bot.Log("[Discover] 兜底：弃牌术对局，手牌容量安全 -> 选择 锅炉燃料(WW_441)");
                            return Card.Cards.WW_441;
                        }
                    }
                    catch { }
                }
            }
            catch
            {
                // ignore
            }

            // 额外保证：地标/窟穴的发现（origin = WON_103）应优先弃掉被弃收益组件，尤其是灵魂弹幕。
            try
            {
                if (originCard == Card.Cards.WON_103 && choices != null && choices.Count > 0)
                {
                    // 永远不选时空之爪(END_016)
                    if (choices.Contains(Card.Cards.END_016))
                    {
                        int handCountLocalForEnd016Filter = board != null && board.Hand != null ? board.Hand.Count : 0;
                        bool bt300SafeWhenSkippingWeapon = handCountLocalForEnd016Filter > 0 && handCountLocalForEnd016Filter <= Bt300SafeThresholdForCave;
                        var alternativeChoice = choices.FirstOrDefault(c =>
                            c != Card.Cards.END_016
                            && (c != Card.Cards.BT_300 || bt300SafeWhenSkippingWeapon));
                        if (!alternativeChoice.Equals(default(Card.Cards)))
                        {
                            Bot.Log("[Discover] 地标发现：永远不选时空之爪(END_016) -> 选择 " + SafeCardName(alternativeChoice) + "(" + alternativeChoice + ")");
                            return alternativeChoice;
                        }

                        if (choices.Contains(Card.Cards.BT_300) && !bt300SafeWhenSkippingWeapon)
                        {
                            Bot.Log("[Discover] 地标发现：END_016过滤后，BT_300因手牌阈值(" + handCountLocalForEnd016Filter + ">" + Bt300SafeThresholdForCave + ")被禁用，继续走窟穴优先级");
                        }
                    }

                    // 地标/窟穴弃牌：按新顺序 + 避开“使用前已临时”的被弃组件
                    var pick = PickDiscardWarlockCaveDiscard(board, choices, "[Discover] 地标弃牌");
                    if (!pick.Equals(default(Card.Cards)))
                        return pick;
                }
            }
            catch
            {
                // ignore
            }

            // 新需求：一般情况下 WORK_027 只选 WORK_027t3（拿两张直伤）。
            // 兼容性：不直接引用 Card.Cards.WORK_027/WORK_027t3，避免旧卡库枚举缺失导致无法编译。
            try
            {
                if (originCard.ToString() == "WORK_027")
                {
                    var t3 = choices.FirstOrDefault(c => c.ToString() == "WORK_027t3");
                    if (!t3.Equals(default(Card.Cards)))
                    {
                        Bot.Log("[Discover] UniversalDiscover：WORK_027 三选强制选择 -> WORK_027t3（两张直伤）");
                        return t3;
                    }
                }
            }
            catch
            {
                // ignore
            }

            // 复盘问题：点维希度斯的窟穴(WON_103)后如果先打出其它牌（例如异教低阶牧师），
            // SmartBot 可能把“尚未处理的窟穴弃牌 choice”错误地按“后续出牌的 originCard”来交给 Discover 处理。
            // 结果会把本该弃的收益牌弃错（例如弃掉异教）。
            // 兜底口径：只要场上有窟穴，且三选里出现“可弃收益牌”，则无论 originCard 是什么都按窟穴优先级处理。
            try
            {
                if (board != null
                    && board.MinionFriend != null
                    && board.MinionFriend.Any(m => m != null && m.Template != null && m.Template.Id == Card.Cards.WON_103)
                    && (choices.Contains(Card.Cards.BT_300)
                        || choices.Contains(Card.Cards.RLK_534)
                        || choices.Contains(Card.Cards.AT_022)
                        || choices.Contains(Card.Cards.WW_044t)
                        || choices.Contains(Card.Cards.WW_441)
                        || choices.Contains(Card.Cards.RLK_532)
                        || choices.Contains(Card.Cards.WON_098)
                        || choices.Contains(Card.Cards.KAR_205)))
                {
                    string choiceList = string.Join(", ", choices.Select(c => SafeCardName(c) + "(" + c + ")"));

                    var pick = PickDiscardWarlockCaveDiscard(board, choices, "[Discover] 窟穴兜底");
                    if (!pick.Equals(default(Card.Cards)))
                    {
                        Bot.Log("[Discover] UniversalDiscover：检测到窟穴(WON_103)弃牌choice(可能origin错位)，三选=[" + choiceList + "] -> 统一走窟穴选牌逻辑选择 " + SafeCardName(pick) + "(" + pick + ")");
                        return pick;
                    }
                }
            }
            catch
            {
                // ignore
            }

            // 更通用的回退：如果这次三选看起来是“从手牌中三选弃一张”（choices 都在手牌里），
            // 则回退到窟穴原生优先级，不再相信 originCard。
            // 目的：即使三选里没有 WW_044t，也能避免“误按其它卡的 discover.ini”去选错。
            try
            {
                bool looksLikeHandDiscardChoice = board != null
                                                && choices.Count == 3
                                                && HasCardInHand(board, choices[0])
                                                && HasCardInHand(board, choices[1])
                                                && HasCardInHand(board, choices[2]);

                if (looksLikeHandDiscardChoice)
                {
                    string choiceList = string.Join(", ", choices.Select(c => SafeCardName(c) + "(" + c + ")"));
                    Bot.Log("[Discover] UniversalDiscover：检测到从手牌三选弃牌(疑似窟穴/或origin错位)，origin=" + originCard + "，三选=[" + choiceList + "]");

                    // 硬规则：地标/窟穴的“从手牌三选弃牌”场景里，永远不选时空之爪(END_016)。
                    // 说明：该场景下 originCard 可能错位（例如显示为 TIME_026），因此不依赖 originCard 判断。
                    // 直接在此分支统一排除 END_016，避免像日志里那样回退“弃最高费”误选 END_016。
                    try
                    {
                        if (choices.Contains(Card.Cards.END_016))
                        {
                            int handCountLocalForEnd016Filter = board != null && board.Hand != null ? board.Hand.Count : 0;
                            bool bt300SafeWhenSkippingWeapon = handCountLocalForEnd016Filter > 0 && handCountLocalForEnd016Filter <= Bt300SafeThresholdForCave;
                            var alternative = choices.FirstOrDefault(c =>
                                c != Card.Cards.END_016
                                && (c != Card.Cards.BT_300 || bt300SafeWhenSkippingWeapon));
                            if (!alternative.Equals(default(Card.Cards)))
                            {
                                Bot.Log("[Discover] 手牌三选弃牌：永远不选时空之爪(END_016) -> 选择 " + SafeCardName(alternative) + "(" + alternative + ")");
                                return alternative;
                            }

                            if (choices.Contains(Card.Cards.BT_300) && !bt300SafeWhenSkippingWeapon)
                            {
                                Bot.Log("[Discover] 手牌三选弃牌：END_016过滤后，BT_300因手牌阈值(" + handCountLocalForEnd016Filter + ">" + Bt300SafeThresholdForCave + ")被禁用，继续走窟穴优先级");
                            }
                        }
                    }
                    catch
                    {
                        // ignore
                    }

                    int handCountLocal = board.Hand != null ? board.Hand.Count : 0;
                    bool bt300SafeInCave = handCountLocal <= Bt300SafeThresholdForCave; // 选BT_300会额外抽3，窟穴弃1发现1不变手牌；手牌>6时禁用避免爆牌

                    var priority = new[]
                    {
                        Card.Cards.BT_300,  // 古尔丹之手
                        Card.Cards.RLK_534, // 灵魂弹幕
                        Card.Cards.RLK_532, // 行尸
                        Card.Cards.WON_098, // 镀银魔像
                        Card.Cards.KAR_205, // 镀银魔像 (KAR)
                        Card.Cards.AT_022,  // 加拉克苏斯之拳
                        Card.Cards.WW_044t, // 淤泥桶
                        Card.Cards.WW_441,  // 锅炉燃料
                    };

                    // 关键修正：某些版本/时序下，点窟穴后在选择出现时，board 里可能暂时读不到“窟穴在场”，
                    // 且 originCard 也可能被错位成其它牌。
                    // 兜底口径：只要三选都来自手牌，且命中“可弃收益牌”集合，就按窟穴优先级处理。
                    bool hitsDiscardBenefit = priority.Any(id => choices.Contains(id));

                    if (!hitsDiscardBenefit)
                    {
                        // 没命中收益牌时，不强行把所有“从手牌三选”都当窟穴，避免误伤其它交互。
                        // 但弃牌术(颜射术)里，这几乎就是窟穴弃牌 choice；此时不应该弃掉关键启动牌 ULD_163。
                        if (IsDiscardWarlock(board)
                            && choices.Contains(Card.Cards.ULD_163)
                            && choices.Any(x => x != Card.Cards.ULD_163))
                        {
                            Card.Cards fallbackAvoidMerchant = choices.First(x => x != Card.Cards.ULD_163);
                            int bestCostAvoidMerchant = -1;

                            foreach (var opt in choices)
                            {
                                try
                                {
                                    if (opt == Card.Cards.ULD_163) continue;
                                    if (!bt300SafeInCave && opt == Card.Cards.BT_300) continue;
                                    var cardInHand = board.Hand.FirstOrDefault(h => h != null && h.Template != null && h.Template.Id == opt);
                                    int cost = cardInHand != null ? cardInHand.CurrentCost : 0;
                                    if (cost > bestCostAvoidMerchant)
                                    {
                                        bestCostAvoidMerchant = cost;
                                        fallbackAvoidMerchant = opt;
                                    }
                                }
                                catch
                                {
                                    // ignore
                                }
                            }

                            Bot.Log("[Discover] UniversalDiscover：弃牌术手牌三选弃牌：未命中收益牌，避免弃 ULD_163 => 选择 " + SafeCardName(fallbackAvoidMerchant) + "(" + fallbackAvoidMerchant + ")");
                            return fallbackAvoidMerchant;
                        }
                    }
                    else
                    {
                        Bot.Log("[Discover] UniversalDiscover：检测到疑似窟穴(WON_103)弃牌三选(可能窟穴不在board/或origin错位)，三选=[" + choiceList + "]，忽略origin=" + originCard + "，按优先级处理");
                    }

                    if (hitsDiscardBenefit)
                    {
                        Bot.Log("[Discover] UniversalDiscover：检测到疑似窟穴(WON_103)弃牌三选(可能窟穴不在board/或origin错位)，三选=[" + choiceList + "]，忽略origin=" + originCard + "，按优先级处理");
                        var pick = PickDiscardWarlockCaveDiscard(board, choices, "[Discover] 手牌三选弃牌");
                        if (!pick.Equals(default(Card.Cards)))
                        {
                            Bot.Log("[Discover] UniversalDiscover：窟穴(WON_103)弃牌choice回退 -> 统一走窟穴选牌逻辑选择 " + SafeCardName(pick) + "(" + pick + ")");
                            return pick;
                        }
                    }

                    // 如果三选都不在优先级列表里（全是杂牌），则统一走窟穴选牌逻辑（PickDiscardWarlockCaveDiscard 内部已包含弃最高费/避开关键牌的兜底）。
                    var finalPick = PickDiscardWarlockCaveDiscard(board, choices, "[Discover] 手牌三选弃牌兜底");
                    Bot.Log("[Discover] UniversalDiscover：手牌三选弃牌场景无明确收益，统一走窟穴逻辑兜底选择 -> " + SafeCardName(finalPick) + "(" + finalPick + ")");
                    return finalPick;
                }
            }
            catch
            {
                // ignore
            }

            // 修正：维希度斯的窟穴(WON_103) 的 choice/发现 在部分版本里会出现 board==null，
            // 导致 UniversalDiscover 直接随机选，从而出现“窟穴没按优先级弃牌（例如不选淤泥桶）”的问题。
            // 口径：不依赖 board，直接按优先级选：弹幕 > 拳头 > 行尸 > 淤泥桶。
            try
            {
                if (originCard == Card.Cards.WON_103)
                {
                    var pick = PickDiscardWarlockCaveDiscard(board, choices, "[Discover] 窟穴(WON_103)三选");
                    if (!pick.Equals(default(Card.Cards)))
                        return pick;
                }
            }
            catch
            {
                // ignore
            }

            // 新需求：如果手上和场上都没有维希度斯的窟穴，且三选中有窟穴，则可以选择它
            try
            {
                if (choices.Contains(Card.Cards.WON_103))
                {
                    bool hasCaveInHand = HasCardInHand(board, Card.Cards.WON_103);
                    bool hasCaveOnBoard = board != null && board.MinionFriend != null && board.MinionFriend.Any(m => m != null && m.Template != null && m.Template.Id == Card.Cards.WON_103);

                    if (!hasCaveInHand && !hasCaveOnBoard)
                    {
                        int remainingManaNow = 0;
                        bool hasCoinNow = false;
                        bool manaFromSnapshot = false;
                        bool manaUnknownForNullBoard = false;
                        int virtualRemainingManaNow = 0;
                        int caveCost = 3;
                        bool cavePlayableNow = false;
                        try
                        {
                            virtualRemainingManaNow = GetVirtualRemainingManaForDiscover(board, out remainingManaNow, out hasCoinNow, out manaFromSnapshot);
                            manaUnknownForNullBoard = (board == null && !manaFromSnapshot);

                            var caveTemplate = CardTemplate.LoadFromId(Card.Cards.WON_103);
                            caveCost = caveTemplate != null ? caveTemplate.Cost : 3;
                            cavePlayableNow = virtualRemainingManaNow >= caveCost;
                        }
                        catch
                        {
                            cavePlayableNow = false;
                            manaUnknownForNullBoard = (board == null);
                        }

                        int ratioPayoffCount = 0;
                        int ratioHandCount = 0;
                        bool highDiscardRatioForCave = false;
                        try
                        {
                            var payoffForRatio = GetDiscardComponentsConsideringHand(board, 9);
                            highDiscardRatioForCave = IsDiscardPayoffRatioAbove(board, payoffForRatio, 20, out ratioPayoffCount, out ratioHandCount);
                        }
                        catch
                        {
                            highDiscardRatioForCave = false;
                            ratioPayoffCount = 0;
                            ratioHandCount = 0;
                        }

                        if (IsTlc451LikeDiscoverOrigin(originCard) && highDiscardRatioForCave)
                        {
                            Bot.Log("[Discover] UniversalDiscover：来源TLC_451且手牌被弃组件占比="
                                + ratioPayoffCount + "/" + ratioHandCount + " > 20% -> 允许并优先选择窟穴(WON_103)");
                            return Card.Cards.WON_103;
                        }
                        // TLC_451 发现的牌是临时牌：若本回合法力不足且未命中“高占比放行”，禁止选择窟穴，避免拿到“当回合打不出且回合末消失”的死牌。
                        else if (IsTlc451LikeDiscoverOrigin(originCard) && !cavePlayableNow)
                        {
                            if (choices.Count > 1)
                            {
                                choices = choices.Where(c => c != Card.Cards.WON_103).ToList();
                                Bot.Log("[Discover] UniversalDiscover：手上/场上无窟穴，但当前可用费用(含硬币)="
                                    + virtualRemainingManaNow + (manaFromSnapshot ? "(快照)" : "")
                                    + " < 窟穴费用" + caveCost + "，且来源为咒怨之墓(TLC_451)临时发现 -> 跳过并剔除窟穴");
                            }
                            else
                            {
                                Bot.Log("[Discover] UniversalDiscover：手上/场上无窟穴，但当前可用费用(含硬币)="
                                    + virtualRemainingManaNow + (manaFromSnapshot ? "(快照)" : "")
                                    + " < 窟穴费用" + caveCost + "，且来源为咒怨之墓(TLC_451)临时发现 -> 跳过窟穴");
                            }
                        }
                        else if (board != null && !cavePlayableNow)
                        {
                            if (choices.Count > 1)
                            {
                                choices = choices.Where(c => c != Card.Cards.WON_103).ToList();
                                Bot.Log("[Discover] UniversalDiscover：手上/场上无窟穴，但当前可用费用(含硬币)="
                                    + virtualRemainingManaNow + " < 窟穴费用" + caveCost + " -> 跳过并剔除窟穴");
                            }
                            else
                            {
                                Bot.Log("[Discover] UniversalDiscover：手上/场上无窟穴，但当前可用费用(含硬币)="
                                    + virtualRemainingManaNow + " < 窟穴费用" + caveCost + " -> 跳过窟穴");
                            }
                        }
                        else
                        {
                            Bot.Log("[Discover] UniversalDiscover：手上和场上都没有窟穴，选择获得维希度斯的窟穴(WON_103)");
                            return Card.Cards.WON_103;
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }

            // 永不崩溃兜底：即使遇到新卡/卡库缺失导致 CardTemplate 为 null，也不要抛异常回退默认逻辑。
            Random rndEarly = new Random();
            Card.Cards fallbackChoice = choices[rndEarly.Next(0, choices.Count)];

            try
            {

            // Starting index
            int startIndex = 0;

            // Retrieve latest version of card definition library from cloud
            try
            {
                FileVersionCheck();
            }
            catch (Exception e)
            {
                Bot.Log("[Discover] UniversalDiscover: FileVersionCheck failed: " + e.Message);
            }

            // Bot logs heading
            string currentVersion;
            try
            {
                currentVersion = CurrentVersion();
            }
            catch
            {
                currentVersion = null;
            }
            log = "=====Discover V9.1, Card definition V" + currentVersion + "===EE";

            // 仅在弃牌术（颜射术）对局中追加专项版本号，避免影响其它卡组的日志观感。
            if (IsDiscardWarlock(board))
                log += " | DiscardWarlockDiscover " + DiscardWarlockDiscoverVersion;
            string Divider = new string('=', 40);

            // Final random choice if no cards found
            Random rnd = new Random();

            // 硬规则：发现黑名单（永不选择）
            // 说明：仅靠打低分不足以避免“全负分时回退随机选择”选到黑名单卡；
            // 因此在入口处直接从候选列表中剔除。
            List<Card.Cards> filteredChoices = choices;
            try
            {
                if (choices != null && choices.Count > 0)
                {
                    var discoverBlackList = new HashSet<Card.Cards>
                    {
                        Card.Cards.CFM_637, // 帕奇斯
                        Card.Cards.TIME_024 // 无限巨龙姆诺兹多
                    };
                    filteredChoices = choices.Where(c => !discoverBlackList.Contains(c)).ToList();
                    if (filteredChoices.Count > 0)
                    {
                        if (choices.Contains(Card.Cards.CFM_637))
                            AddLog("[Discover] 硬规则：候选含帕奇斯(CFM_637) -> 已过滤，不会选择");
                        if (choices.Contains(Card.Cards.TIME_024))
                            AddLog("[Discover] 硬规则：候选含无限巨龙姆诺兹多(TIME_024) -> 已过滤，不会选择");
                    }
                    else
                        filteredChoices = choices; // 极端兜底：若过滤后为空，仍保留原列表避免异常
                }
            }
            catch
            {
                filteredChoices = choices;
            }

            Card.Cards bestChoice = filteredChoices[rnd.Next(0, filteredChoices.Count)];

            if (board == null)
            {
                // 蛋术专项：TLC_451 且 board 为空时，避免无脑选布蛋者。
                // 说明：board 为空无法可靠判断坟场蛋状态，优先拿更通用的蛋线组件；布蛋者仅作最后兜底。
                try
                {
                    if (IsTlc451LikeDiscoverOrigin(originCard) && choices != null && choices.Count > 0)
                    {
                        // 弃牌术专项：board 为空时，若仍判断牌库有灵魂弹幕且三选含速写，
                        // 则强制优先速写，避免被后续兜底误选郊狼/功能牌。
                        try
                        {
                            if (choices.Contains(Card.Cards.TOY_916))
                            {
                                int remainingManaNowForNullBoard = 0;
                                bool hasCoinNowForNullBoard = false;
                                bool fromSnapshotForNullBoard = false;
                                int virtualRemainingManaNowForNullBoard =
                                    GetVirtualRemainingManaForDiscover(board, out remainingManaNowForNullBoard, out hasCoinNowForNullBoard, out fromSnapshotForNullBoard);
                                bool lowManaWindowForNullBoard = virtualRemainingManaNowForNullBoard <= 2;
                                var lowManaDrawPickForNullBoard = PickLowCostDrawMinionForTlc451(
                                    board,
                                    choices,
                                    virtualRemainingManaNowForNullBoard,
                                    false
                                );
                                if (lowManaWindowForNullBoard && !lowManaDrawPickForNullBoard.Equals(default(Card.Cards)))
                                {
                                    Bot.Log("[Discover] UniversalDiscover：board 为空且来源TLC_451 -> "
                                        + ("当前可用费用(含硬币)=" + virtualRemainingManaNowForNullBoard + " <= 2，优先过牌随从")
                                        + " -> 选择 " + SafeCardName(lowManaDrawPickForNullBoard) + "(" + lowManaDrawPickForNullBoard + ")");
                                    return lowManaDrawPickForNullBoard;
                                }

                                int barrageRemainEstimateForceSketch = 0;
                                try { barrageRemainEstimateForceSketch = GetTrueBarrageCountInDeck(board); } catch { barrageRemainEstimateForceSketch = 0; }

                                if (!lowManaWindowForNullBoard
                                    && virtualRemainingManaNowForNullBoard >= 4
                                    && barrageRemainEstimateForceSketch > 0)
                                {
                                    Bot.Log("[Discover] UniversalDiscover：board 为空且来源TLC_451 -> 牌库仍有灵魂弹幕(est="
                                        + barrageRemainEstimateForceSketch
                                        + ")且可用费="
                                        + virtualRemainingManaNowForNullBoard
                                        + ">=4，强制优先选择速写美术家(TOY_916)");
                                    return Card.Cards.TOY_916;
                                }
                                if (lowManaWindowForNullBoard)
                                {
                                    Bot.Log("[Discover] UniversalDiscover：board 为空且来源TLC_451 -> 低费窗口，暂不强推速写美术家(TOY_916)");
                                }
                            }
                        }
                        catch
                        {
                            // ignore
                        }

                        // TLC_451 统一口径：非被弃组件必须考虑“当回合可用费用(含硬币+1)”。
                        // board 为空时使用上一帧快照；若无快照则按 0 处理（更保守）。
                        try
                        {
                            int remainingManaNowForNullBoard = 0;
                            bool hasCoinNowForNullBoard = false;
                            bool fromSnapshotForNullBoard = false;
                            int virtualRemainingManaNowForNullBoard =
                                GetVirtualRemainingManaForDiscover(board, out remainingManaNowForNullBoard, out hasCoinNowForNullBoard, out fromSnapshotForNullBoard);
                            bool manaUnknownForNullBoard = (board == null && !fromSnapshotForNullBoard);
                            if (manaUnknownForNullBoard)
                            {
                                Bot.Log("[Discover] UniversalDiscover：board 为空且来源TLC_451，缺少费用快照 -> 按0费执行保守硬过滤");
                            }

                            Func<Card.Cards, bool> affordableForNullBoard = (cardId) =>
                            {
                                try
                                {
                                    if (cardId == Card.Cards.TOY_916 && virtualRemainingManaNowForNullBoard < 4) return false;
                                    return GetTlc451EffectiveDiscoverCost(board, cardId) <= virtualRemainingManaNowForNullBoard;
                                }
                                catch
                                {
                                    return false;
                                }
                            };

                            var filteredForNullBoard = choices.Where(c => affordableForNullBoard(c)).ToList();
                            if (filteredForNullBoard.Count > 0 && filteredForNullBoard.Count < choices.Count)
                            {
                                choices = filteredForNullBoard;
                                Bot.Log("[Discover] UniversalDiscover：board 为空且来源TLC_451 -> 当前可用费用(含硬币)="
                                    + virtualRemainingManaNowForNullBoard + (fromSnapshotForNullBoard ? "(快照)" : "")
                                    + "，已剔除不可立即落地的非被弃组件");
                            }
                            else if (filteredForNullBoard.Count == 0 && choices.Count > 0)
                            {
                                int minCost = 99;
                                try
                                {
                                    minCost = choices
                                        .Select(c => GetTlc451EffectiveDiscoverCost(board, c))
                                        .DefaultIfEmpty(99)
                                        .Min();
                                }
                                catch { minCost = 99; }

                                var minCostChoices = new List<Card.Cards>();
                                try
                                {
                                    minCostChoices = choices.Where(c =>
                                    {
                                        try
                                        {
                                            int cst = GetTlc451EffectiveDiscoverCost(board, c);
                                            return cst == minCost;
                                        }
                                        catch { return false; }
                                    }).ToList();
                                }
                                catch { minCostChoices = new List<Card.Cards>(); }

                                if (minCostChoices.Count > 0)
                                {
                                    choices = minCostChoices;
                                    Bot.Log("[Discover] UniversalDiscover：board 为空且来源TLC_451 -> 当前可用费用(含硬币)="
                                        + virtualRemainingManaNowForNullBoard + (fromSnapshotForNullBoard ? "(快照)" : "")
                                        + "，三选均超费 -> 改为最低费用兜底(费" + minCost + ")");
                                }
                            }

                            bool lowManaWindowForNullBoard = virtualRemainingManaNowForNullBoard <= 2;
                            var lowManaDrawPickForNullBoard = PickLowCostDrawMinionForTlc451(
                                board,
                                choices,
                                virtualRemainingManaNowForNullBoard,
                                false
                            );
                            if (lowManaWindowForNullBoard && !lowManaDrawPickForNullBoard.Equals(default(Card.Cards)))
                            {
                                Bot.Log("[Discover] UniversalDiscover：board 为空且来源TLC_451 -> "
                                    + ("当前可用费用(含硬币)=" + virtualRemainingManaNowForNullBoard + " <= 2，优先过牌随从")
                                    + " -> 选择 " + SafeCardName(lowManaDrawPickForNullBoard) + "(" + lowManaDrawPickForNullBoard + ")");
                                return lowManaDrawPickForNullBoard;
                            }
                        }
                        catch
                        {
                            // ignore
                        }

                        // 弃牌术专项：即使 board 为空，若仍判断“牌库有灵魂弹幕”，且三选含速写，则优先速写。
                        try
                        {
                            if (choices.Contains(Card.Cards.TOY_916))
                            {
                                int remainingManaNowForNullBoard = 0;
                                bool hasCoinNowForNullBoard = false;
                                bool fromSnapshotForNullBoard = false;
                                int virtualRemainingManaNowForNullBoard =
                                    GetVirtualRemainingManaForDiscover(board, out remainingManaNowForNullBoard, out hasCoinNowForNullBoard, out fromSnapshotForNullBoard);
                                bool lowManaWindowForNullBoard = virtualRemainingManaNowForNullBoard <= 2;
                                var lowManaDrawPickForNullBoard = PickLowCostDrawMinionForTlc451(
                                    board,
                                    choices,
                                    virtualRemainingManaNowForNullBoard,
                                    false
                                );
                                if (lowManaWindowForNullBoard && !lowManaDrawPickForNullBoard.Equals(default(Card.Cards)))
                                {
                                    Bot.Log("[Discover] UniversalDiscover：board 为空且来源TLC_451 -> "
                                        + ("当前可用费用(含硬币)=" + virtualRemainingManaNowForNullBoard + " <= 2，优先过牌随从")
                                        + " -> 选择 " + SafeCardName(lowManaDrawPickForNullBoard) + "(" + lowManaDrawPickForNullBoard + ")");
                                    return lowManaDrawPickForNullBoard;
                                }

                                int barrageRemainEstimateForNullBoard = 0;
                                try { barrageRemainEstimateForNullBoard = GetTrueBarrageCountInDeck(board); } catch { barrageRemainEstimateForNullBoard = 0; }

                                bool inferDiscardWarlockByChoices = choices.Any(c =>
                                    c == Card.Cards.ULD_163
                                    || IsDiscardPayoffId(c)
                                );

                                if (!lowManaWindowForNullBoard
                                    && virtualRemainingManaNowForNullBoard >= 4
                                    && (barrageRemainEstimateForNullBoard > 0 || inferDiscardWarlockByChoices))
                                {
                                    Bot.Log("[Discover] UniversalDiscover：board 为空且来源TLC_451 -> 牌库仍有灵魂弹幕(est="
                                        + barrageRemainEstimateForNullBoard
                                        + ")且可用费="
                                        + virtualRemainingManaNowForNullBoard
                                        + ">=4，三选含速写 -> 优先选择速写美术家(TOY_916)");
                                    return Card.Cards.TOY_916;
                                }
                                if (lowManaWindowForNullBoard)
                                {
                                    Bot.Log("[Discover] UniversalDiscover：board 为空且来源TLC_451 -> 低费窗口，暂不优先速写美术家(TOY_916)");
                                }
                            }
                        }
                        catch
                        {
                            // ignore
                        }

                        if (choices.Contains(Card.Cards.UNG_900))
                        {
                            Bot.Log("[Discover] UniversalDiscover：board 为空且来源TLC_451 -> 优先选择灵魂歌者安布拉(UNG_900)");
                            return Card.Cards.UNG_900;
                        }

                        if (choices.Contains(Card.Cards.MAW_002))
                        {
                            Bot.Log("[Discover] UniversalDiscover：board 为空且来源TLC_451 -> 优先选择尸身保护令(MAW_002)");
                            return Card.Cards.MAW_002;
                        }

                        if (choices.Contains(Card.Cards.AV_312))
                        {
                            Bot.Log("[Discover] UniversalDiscover：board 为空且来源TLC_451 -> 优先选择献祭召唤者(AV_312)");
                            return Card.Cards.AV_312;
                        }

                        if (choices.Contains(Card.Cards.DINO_410))
                        {
                            Bot.Log("[Discover] UniversalDiscover：board 为空且来源TLC_451 -> 优先选择凯洛斯的蛋(DINO_410)");
                            return Card.Cards.DINO_410;
                        }

                        if (choices.Contains(Card.Cards.SCH_514))
                        {
                            var pickNonRaiseDead = choices.FirstOrDefault(c => c != Card.Cards.SCH_514 && c != Card.Cards.DINO_411);
                            if (!pickNonRaiseDead.Equals(default(Card.Cards)))
                            {
                                Bot.Log("[Discover] UniversalDiscover：board 为空且来源TLC_451 -> 跳过亡者复生(SCH_514)，改选 " + SafeCardName(pickNonRaiseDead) + "(" + pickNonRaiseDead + ")");
                                return pickNonRaiseDead;
                            }
                        }

                        // board 为空时拿不到可靠法力上下文；
                        // TLC_451 发现牌是临时牌，避免兜底误选高费且当回合常打不出的刀/地标。
                        var pickNonEggbearer = choices.FirstOrDefault(c =>
                            c != Card.Cards.DINO_411
                            && c != Card.Cards.END_016
                            && c != Card.Cards.WON_103);
                        if (pickNonEggbearer.Equals(default(Card.Cards)))
                        {
                            pickNonEggbearer = choices.FirstOrDefault(c =>
                                c != Card.Cards.DINO_411
                                && c != Card.Cards.END_016);
                        }
                        if (pickNonEggbearer.Equals(default(Card.Cards)))
                        {
                            pickNonEggbearer = choices.FirstOrDefault(c => c != Card.Cards.DINO_411);
                        }
                        if (!pickNonEggbearer.Equals(default(Card.Cards)))
                        {
                            Bot.Log("[Discover] UniversalDiscover：board 为空且来源TLC_451 -> 兜底避开神圣布蛋者/时空之爪/窟穴，改选 " + SafeCardName(pickNonEggbearer) + "(" + pickNonEggbearer + ")");
                            return pickNonEggbearer;
                        }

                        if (choices.Contains(Card.Cards.DINO_411))
                        {
                            Bot.Log("[Discover] UniversalDiscover：board 为空且来源TLC_451 -> 仅剩神圣布蛋者(DINO_411)，兜底选择");
                            return Card.Cards.DINO_411;
                        }
                    }

                    // 蛋术专项：MAW_002 发现（board 为空）优先选择安布拉，避免被动拿到杂随从。
                    if (originCard == Card.Cards.MAW_002 && choices != null && choices.Count > 0)
                    {
                        if (choices.Contains(Card.Cards.UNG_900))
                        {
                            Bot.Log("[Discover] UniversalDiscover：board 为空且来源MAW_002 -> 优先选择灵魂歌者安布拉(UNG_900)");
                            return Card.Cards.UNG_900;
                        }
                    }
                }
                catch
                {
                    // ignore
                }

                // 修复：部分版本/时序下，窟穴(WON_103)的弃牌choice会在 board 尚未初始化时触发。
                // 此时统一走专用的弃牌收益逻辑（即便 board==null 也能根据优先级确定弃牌）。
                try
                {
                    var pick = PickDiscardWarlockCaveDiscard(null, choices, "[Discover] board为空弃牌");
                    if (!pick.Equals(default(Card.Cards)))
                    {
                        Bot.Log("[Discover] UniversalDiscover：board 为空，通过窟穴逻辑命中 -> 选择 " + SafeCardName(pick) + "(" + pick + ")");
                        return pick;
                    }
                }
                catch
                {
                    // ignore
                }

                Bot.Log("[Discover] UniversalDiscover：board 为空，回退随机选择");
                return bestChoice;
            }

            // Get current play mode,  Skip steps for Arena
            string mode = CurrentMode(Bot.CurrentMode());

            // Get current hero class
            string hero = board != null ? board.FriendClass.ToString() : string.Empty;

            // Origin card check and correction
            try
            {
                (originCard, startIndex) = OriginCardCorrection(originCard, choices, board, mode);
            }
            catch (Exception e)
            {
                Bot.Log("[Discover] UniversalDiscover: OriginCardCorrection failed: " + e.Message);
                startIndex = 0;
            }

            // Origin card name from database template
            string Origin_Card = SafeCardName(originCard);

            // Create empty card list
            List<CardValue> choicesCardValue = new List<CardValue>();

            // Main loop starts here
            double points = 0;
            double TotalPoints = 0;
            iniTierList = null;
            for (int choiceIndex = startIndex; choiceIndex < 3; choiceIndex++)
            {
                // Input file selection
                switch (choiceIndex)
                {
                    case 0:
                        // Try custom file
                        // string customFile = Path.Combine(discoverCCDirectory, mode, "Custom" + originCard + ".ini");
                        // if (File.Exists(customFile))
                        // {
                        //     iniTierList = new IniManager(customFile);
                        //     description = "From custom: " + customFile;
                        //     continue; // Skip to card evaluation
                        // }
                        break;
                    case 1:
                        // Try origin card file
                        // if (iniTierList == null)
                        // {
                        string originFile = Path.Combine(discoverCCDirectory, mode, originCard + ".ini");
                        if (File.Exists(originFile))
                        {
                            iniTierList = new IniManager(originFile);
                            description = $"From: {Origin_Card}, Mode - {mode}";
                        }
                        //}
                        break;
                    case 2:
                        // Fallback to discover.ini
                        string discoverFile = Path.Combine(discoverCCDirectory, mode, "discover.ini");
                        if (File.Exists(discoverFile))
                        {
                            iniTierList = new IniManager(discoverFile);
                            description = $"From: discover.ini, Origin: {Origin_Card}, Mode - {mode}";
                        }
                        break;
                }
                choicesCardValue.Clear();

                // Search for best points
                foreach (var choice in filteredChoices) // loops for each card
                {
                    points = 0;
                    var cardTemplate = CardTemplate.LoadFromId(choice); // Using SB database to get details of card

                    // 防护：卡库缺失/新卡导致模板为空时，直接按 0 分处理，避免后续访问 Name/Cost 引发 NRE。
                    if (cardTemplate == null)
                    {
                        choicesCardValue.Add(new CardValue(choice, 0));
                        continue;
                    }
                    switch (choiceIndex)
                    {
                        case 0:
                            // *** Check for any special conditions ***
                            switch (originCard)
                            {
                                case Card.Cards.TLC_451: // 咒怨之墓：专项逻辑已在 HandlePickDecision 前置分支统一处理
                                    // 按“同一卡牌统一处理 if/else if”的规范，这里不再做重复评分规则。
                                    break;

                                case Card.Cards.WON_103: // 维希度斯的窟穴（弃牌术专用弃牌优先级）
                                    if (!IsDiscardWarlock(board)) break;

                                    // 弃牌优先级：BT_300(窟穴额外抽牌，需更保守避免爆牌) > 弹幕 > 拳 > 桶 > 锅炉燃料 > 行尸 > 镀银魔像，其余交回 ini
                                    {
                                        int handCountLocal = board != null && board.Hand != null ? board.Hand.Count : 0;
                                        bool bt300SafeInCave = handCountLocal > 0 && handCountLocal <= Bt300SafeThresholdForCave; // 窟穴：弃1发现1手牌不变，选BT_300抽3张；手牌>6时禁用避免爆牌

                                        bool hasAnyPayoffOffered = choices.Any(c => c == Card.Cards.RLK_534
                                                                               || c == Card.Cards.AT_022
                                                                               || c == Card.Cards.WW_044t
                                                                               || c == Card.Cards.WW_441
                                                                               || c == Card.Cards.RLK_532
                                                                               || c == Card.Cards.WON_098
                                                                               || c == Card.Cards.KAR_205
                                                                               || (bt300SafeInCave && c == Card.Cards.BT_300));
                                        if (!hasAnyPayoffOffered) break;

                                        if (bt300SafeInCave && choice == Card.Cards.BT_300) points = 1000;
                                        else if (choice == Card.Cards.RLK_534) points = 900;
                                        else if (choice == Card.Cards.AT_022) points = 850;
                                        else if (choice == Card.Cards.WW_044t) points = 800;
                                        else if (choice == Card.Cards.WW_441) points = 780;
                                        else if (choice == Card.Cards.RLK_532) points = 750;
                                        else if (choice == Card.Cards.WON_098 || choice == Card.Cards.KAR_205) points = 700;
                                        else points = 0;
                                    }

                                    description = "来源：维希度斯的窟穴（弃牌术）：按弃牌优先级选择";
                                    break;

                                case Card.Cards.MIS_102: // Return Policy
                                    // Colifero DH gate to avoid Blob when Tusk/Felhunter offered
                                    var signature = new[] { Card.Cards.VAC_926, Card.Cards.TLC_468, Card.Cards.EDR_891, Card.Cards.TOY_703 }; // Cliff Dive, Blob of Tar, Ravenous Felhunter, Colifero
                                    bool isDemonHunter = board.FriendClass.ToString().Equals("DemonHunter", StringComparison.OrdinalIgnoreCase);
                                    bool hasSignature = board.Hand.Any(c => signature.Contains(c.Template.Id)) || board.Deck.Any(signature.Contains);

                                    // If not Demon Hunter, skip
                                    if (!isDemonHunter) break;

                                    // If we don't have signature cards but our graveyard contains EDR_892 or EDR_891,
                                    // allow the return-policy special handling to run (graveyard-aware override).
                                    bool graveHasBrutal = board.FriendGraveyard != null && board.FriendGraveyard.Any(id => id == Card.Cards.EDR_892);
                                    bool graveHasGreedy = board.FriendGraveyard != null && board.FriendGraveyard.Any(id => id == Card.Cards.EDR_891);

                                    if (!hasSignature && !(graveHasBrutal || graveHasGreedy)) break;

                                    bool tuskOffered = choices.Contains(Card.Cards.BAR_330) || choices.Contains(Card.Cards.CORE_BAR_330); // Tuskarr Fisherman
                                    bool felOffered = choices.Contains(Card.Cards.EDR_891); // Ravenous Felhunter
                                    // Evaluate choice cards
                                    if (tuskOffered || felOffered)
                                    {
                                        // 退货政策补充：若己方坟场有残暴的魔蝠(EDR_892)或贪婪的地狱猎犬(EDR_891)，
                                        // 则提高发现这两张卡的优先级，优先 EDR_892。
                                        bool hasBrutalBatInGrave = board.FriendGraveyard != null && board.FriendGraveyard.Any(id => id == Card.Cards.EDR_892);
                                        bool hasGreedyHoundInGrave = board.FriendGraveyard != null && board.FriendGraveyard.Any(id => id == Card.Cards.EDR_891);

                                        if (hasBrutalBatInGrave || hasGreedyHoundInGrave)
                                        {
                                            if (choice == Card.Cards.EDR_892)
                                            {
                                                points = 1000; // 强烈优先残暴的魔蝠
                                                AddLog("Return Policy: Friend graveyard contains EDR_892 -> strongly prefer EDR_892");
                                            }
                                            else if (choice == Card.Cards.EDR_891)
                                            {
                                                points = 900; // 次优先贪婪的地狱猎犬
                                                AddLog("Return Policy: Friend graveyard contains EDR_891 -> prefer EDR_891");
                                            }
                                            else
                                            {
                                                points = 10; // 降低其它选项分值
                                            }
                                            description = "From: Return Policy (graveyard aware)";
                                            break;
                                        }

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
                                case Card.Cards.DEEP_027: // Gloomstone Guardian
                                    // 颜射术/弃牌术口径：
                                    // - 手牌里【没有】弃牌收益组件（BT_300/弹幕/行尸/魔像/拳/桶/锅炉燃料）=> 更倾向选 b（避免无收益硬弃两张）
                                    // - 手牌里【有】弃牌收益组件 => 更倾向选 a（弃两张启动）
                                    bool hasDiscardPayoffComponentNow = false;
                                    try
                                    {
                                        var discardPayoffNow = GetDiscardComponentsConsideringHand(board, 9);
                                        hasDiscardPayoffComponentNow = board != null
                                            && board.Hand != null
                                            && board.Hand.Any(h => h != null && h.Template != null && discardPayoffNow.Contains(h.Template.Id));
                                    }
                                    catch
                                    {
                                        hasDiscardPayoffComponentNow = false;
                                    }



                                    if (choice == Card.Cards.DEEP_027a) // Splintered Form, Discard 2 cards.
                                        points = hasDiscardPayoffComponentNow ? 100 : 0;
                                    else if (choice == Card.Cards.DEEP_027b) // Mana Disintegration, Destroy one of your Mana Crystals.
                                        points = hasDiscardPayoffComponentNow ? 0 : 100;

                                    description = "From: Gloomstone Guardian (DiscardWarlock) payoff=" + (hasDiscardPayoffComponentNow ? "yes" : "no");
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
                                    points = rnd.Next(0, 101);
                                    description = "From: Chia Drake";
                                    break;
                                case Card.Cards.TSC_069: // Amalgam of the Deep, Voyage to the Sunken City and Gigantotem
                                    if (choices.Contains(Card.Cards.REV_838)) // Gigantotem
                                    {
                                        iniTierList = new IniManager(discoverCCDirectory + mode + "\\TSC_069.ini");
                                        if (iniTierList != null)
                                            double.TryParse(iniTierList.GetString(choice.ToString(), "points", "0"), NumberStyles.Any, CultureInfo.InvariantCulture, out points);
                                        points = Math.Round(Gigantotem(choice, points, board), 2);
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
                                double.TryParse(iniTierList.GetString(choice.ToString(), "points", "0"), NumberStyles.Any, CultureInfo.InvariantCulture, out points);
                            break;
                        case 2:
                            // Searching file "discover.ini" for best points
                            if (iniTierList != null)
                                double.TryParse(iniTierList.GetString(choice.ToString(), hero, "0"), NumberStyles.Any, CultureInfo.InvariantCulture, out points);
                            break;
                    }

                    // *** Special conditions ***
                    // If Suspicious Alchemist, is on enemy board and one other card played after matches our choices 
                    if (board.MinionEnemy.Any(minion => minion.Template != null && minion.Template.Id == Card.Cards.REV_000 && minion.Template.Id == choice))
                    {
                        description = String.Format("Suspicious Alchemist possible opponent selected card {0}", cardTemplate.Name);
                        points += 500; // Increase points to card if conditions is true
                    }

                    // If opponent has lethal or can be defeated, search for potential last card
                    if (cardTemplate.Cost <= board.ManaAvailable)
                    {
                        points = LastChance(choice, points, board);
                    }

                    // 弃牌术专项硬规则：咒怨之墓发现时，仅在“满足商贩弃弹幕放行口径”时允许选择过期货物专卖商。
                    // 这里作为统一后处理，确保任何来源的评分都遵守该口径（避免 ini 分数覆盖专用逻辑）。
                    try
                    {
                        if (IsTlc451LikeDiscoverOrigin(originCard)
                            && IsDiscardWarlock(board)
                            && choice == Card.Cards.ULD_163)
                        {
                            bool stableMerchantBarrage = false;
                            try
                            {
                                int manaNow = board != null ? board.ManaAvailable : 0;
                                bool hasCoinNow = HasCardInHand(board, Card.Cards.GAME_005);
                                int virtualManaNow = manaNow + (hasCoinNow ? 1 : 0);
                                if (virtualManaNow > 10) virtualManaNow = 10;
                                stableMerchantBarrage = CanPreferMerchantBarrageForTlc451(board, virtualManaNow, true);
                            }
                            catch
                            {
                                stableMerchantBarrage = false;
                            }

                            if (!stableMerchantBarrage)
                            {
                                points = -9999;
                                description = "来源：咒怨之墓（弃牌术）：不满足商贩弃弹幕放行口径 -> 禁止发现过期货物专卖商";
                            }
                        }
                    }
                    catch
                    {
                        // ignore
                    }

                    // 弃牌术专项硬规则：咒怨之墓发现时，若当前可用费用<4，则不要选择速写美术家。
                    // 目的：避免低费回合误拿速写导致空转；与主策略“速写>=4费再用”保持一致。
                    // 说明：作为统一后处理，覆盖 ini 分数与其它通用打分逻辑。
                    try
                    {
                        if (IsTlc451LikeDiscoverOrigin(originCard)
                            && IsDiscardWarlock(board)
                            && choice == Card.Cards.TOY_916)
                        {
                            int remainingManaNow = 0;
                            bool hasCoinNow = false;
                            bool manaFromSnapshot = false;
                            int virtualManaNow = 0;
                            try
                            {
                                virtualManaNow = GetVirtualRemainingManaForDiscover(board, out remainingManaNow, out hasCoinNow, out manaFromSnapshot);
                            }
                            catch
                            {
                                virtualManaNow = 0;
                                manaFromSnapshot = false;
                            }

                            if (virtualManaNow < 4)
                            {
                                points = -9999;
                                description = "来源：咒怨之墓（弃牌术）：当前可用费用<4 -> 禁止发现速写美术家";
                            }
                        }
                    }
                    catch
                    {
                        // ignore
                    }

                    // 统一硬规则：咒怨之墓发现时，非被弃组件必须“当回合可用费用(含硬币+1)”可落地，否则禁止。
                    try
                    {
                        if (IsTlc451LikeDiscoverOrigin(originCard)
                            && choice != default(Card.Cards)
                            && !IsDiscardPayoffId(choice))
                        {
                            int remainingManaNow = 0;
                            bool hasCoinNow = false;
                            bool manaFromSnapshot = false;
                            int virtualManaNow = 0;
                            bool allowByHighDiscardRatioForCave = false;
                            int ratioPayoffCount = 0;
                            int ratioHandCount = 0;

                            try
                            {
                                virtualManaNow = GetVirtualRemainingManaForDiscover(board, out remainingManaNow, out hasCoinNow, out manaFromSnapshot);
                            }
                            catch
                            {
                                virtualManaNow = 0;
                                manaFromSnapshot = false;
                            }
                            try
                            {
                                if (choice == Card.Cards.WON_103)
                                {
                                    var payoffForRatio = GetDiscardComponentsConsideringHand(board, 9);
                                    allowByHighDiscardRatioForCave = IsDiscardPayoffRatioAbove(board, payoffForRatio, 20, out ratioPayoffCount, out ratioHandCount);
                                }
                            }
                            catch
                            {
                                allowByHighDiscardRatioForCave = false;
                            }

                            if (allowByHighDiscardRatioForCave)
                            {
                                points = Math.Max(points, 9200);
                                description = "来源：咒怨之墓（高占比放行）：手牌被弃组件占比="
                                    + ratioPayoffCount + "/" + ratioHandCount + " > 20% -> 允许发现窟穴";
                            }
                            else if (!IsTlc451DiscoverChoicePlayableNow(board, choice, virtualManaNow))
                            {
                                points = -9999;
                                description = "来源：咒怨之墓（统一规则）：非被弃组件且当前可用费用(含硬币)=" + virtualManaNow + "不足 -> 禁止发现";
                            }
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                    // *** End special conditions ***

                    // Add points and choice of card
                    choicesCardValue.Add(new CardValue(choice, points));
                    TotalPoints += points; // Adding points
                }
                if (TotalPoints > 0) break; // Break from choiceIndex loop if card value found
            }

            // Card selection with highest points
            double bestPoints = 0;
            for (var i = 0; i < choicesCardValue.Count; i++) // index through each card
            {
                double pts = choicesCardValue[i].GetPoints(); // calls cardValue subroutine, get points
                AddLog(String.Format("{0}) {1}: {2}", i + 1, SafeCardName(choicesCardValue[i].GetCard()), pts));  // Output cards choices to log
                if (!(bestPoints < pts)) continue; // selects highest points
                bestChoice = choicesCardValue[i].GetCard(); // calls cardValue subroutine, get card assign to bestChoice
                bestPoints = pts;
            }
            // Out to Bot log
            AddLog(Divider);
            if (bestPoints == 0)
                AddLog(String.Format("Selecting: {0} from: {1}", SafeCardName(bestChoice), Origin_Card));
            else
            {
                AddLog(String.Format("Best: {0}: {1}", SafeCardName(bestChoice), bestPoints));
                AddLog(description);
            }
            AddLog(Divider);
            Bot.Log(log);
            return bestChoice; // returns cardID
            }
            catch (Exception e)
            {
                Bot.Log("[Discover] UniversalDiscover：HandlePickDecision 异常，回退随机选择。原因：" + e);
                return fallbackChoice;
            }
        }

        // Origin card correction
        private Tuple<Card.Cards, int> OriginCardCorrection(Card.Cards originCard, List<Card.Cards> choices, Board board, string mode)
        {
            // List of origin cards for correction
            var originChoices = new List<Card.Cards>();

            // Bob's list of cards
            List<Card.Cards> bobList = new List<Card.Cards> { Card.Cards.BG31_BOBt3, Card.Cards.BG31_BOBt, Card.Cards.BG31_BOBt4, Card.Cards.BG31_BOBt2 };

            if (mode == "Arena")
                return Tuple.Create(originCard, 2);

            // Check if any of the choices are in Bob's list and if the board contains Bob's card
            if (choices.Any(minion => bobList.Contains(minion)) && board != null && board.MinionFriend != null && board.MinionFriend.Any(minion => minion.Template.Id == Card.Cards.BG31_BOB))
            {
                AddLog($"Origin card: {SafeCardName(Card.Cards.BG31_BOB)}");
                return Tuple.Create(Card.Cards.BG31_BOB, 1);
            }

            if (!File.Exists(discoverCCDirectory + mode + "\\" + originCard + ".ini"))
            {
                // Add enemy cards matching enemyCards  
                if (board != null && board.MinionEnemy != null)
                    originChoices.AddRange(board.MinionEnemy.Select(card => card.Template.Id).Where(enemyCards.Contains));

                // Add friendly cards matching friendCards  
                if (board != null && board.MinionFriend != null)
                    originChoices.AddRange(board.MinionFriend.Select(card => card.Template.Id).Where(friendCards.Contains));

                // Add last played card  
                if (board != null && board.PlayedCards != null && board.PlayedCards.Any())
                    originChoices.Add(board.PlayedCards.Last());

                // Select origin card for a match  
                foreach (var card in originChoices)
                {
                    if (File.Exists(discoverCCDirectory + mode + "\\" + card + ".ini"))
                    {
                        AddLog($"Origin card correction: {SafeCardName(card)}");
                        return Tuple.Create(card, 1);
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

        // Memory management, input/output operations
        public class IniManager
        {
            private const int CSize = 1024;

            public IniManager(string path)
            {
                Path = path;
            }

            public IniManager()
                : this("")
            {
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

        // Return current mode
        private static string CurrentMode(Bot.Mode mode)
        {
            switch (mode)
            {
                case Bot.Mode.Arena:
                case Bot.Mode.ArenaAuto:
                    return "Arena";
                case Bot.Mode.Standard:
                    return "Standard";
                // case Bot.Mode.Twist:
                // return "Twist";
                default:
                    return "Wild";
            }
        }

        private Card.Cards TryPickDiscoverByBoxOcr(Card.Cards originCard, List<Card.Cards> choices, Board board)
        {
            try
            {
                Card.Cards pick;
                if (AllowLiveTeacherFallbackCompat())
                {
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        if (TryRunDiscoverOcr(originCard, choices, out pick))
                        {
                            RememberDiscoverPick(originCard, choices, pick);
                            CaptureTeacherSampleCompat(
                                originCard,
                                choices,
                                pick,
                                board,
                                Sanitize(Bot.CurrentProfile()),
                                Sanitize(Bot.CurrentDiscoverProfile()));
                            Bot.Log("[Discover][BoxOCR] live ref=A -> " + SafeCardName(pick) + "(" + pick + ")");
                            return pick;
                        }

                        if (attempt < 2)
                            Thread.Sleep(180);
                    }
                }

                if (TryPickFromMemoryCompat(
                    originCard,
                    choices,
                    board,
                    Sanitize(Bot.CurrentProfile()),
                    Sanitize(Bot.CurrentDiscoverProfile()),
                    out pick))
                {
                    Bot.Log("[Discover][Memory] global -> " + SafeCardName(pick) + "(" + pick + ")");
                    return pick;
                }

                if (AllowLegacyDiscoverFallbackCompat()
                    && TryLoadLearnedDiscoverPick(originCard, choices, out pick))
                {
                    Bot.Log("[Discover][BoxOCR] learned ref=A -> " + SafeCardName(pick) + "(" + pick + ")");
                    return pick;
                }

                Bot.Log(IsPureLearningModeEnabledCompat() && !AllowLegacyDiscoverFallbackCompat()
                    ? "[Discover][BoxOCR] miss -> fallback random"
                    : "[Discover][BoxOCR] miss -> fallback hard rule");
            }
            catch (Exception ex)
            {
                Bot.Log("[Discover][BoxOCR] failed -> " + ex.Message);
            }

            return default(Card.Cards);
        }

        private Card.Cards TryPickGeneratedBoxOcrChoice(Card.Cards originCard, List<Card.Cards> choices)
        {
            if (choices == null || choices.Count == 0)
                return default(Card.Cards);

            string key = originCard + "|" + BuildChoicesKey(choices);
            switch (key)
            {
                // BOXOCR_DISCOVER_GENERATED_START
                // generated by 智能决策老师 OCR 桥接
                // BOXOCR_DISCOVER_GENERATED_END
                default:
                    break;
            }

            return default(Card.Cards);
        }

        private bool TryRunDiscoverOcr(Card.Cards originCard, List<Card.Cards> choices, out Card.Cards pick)
        {
            pick = default(Card.Cards);
            if (choices == null || choices.Count == 0)
                return false;

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
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
            string repoRoot = baseDir;

            try
            {
                DirectoryInfo parent = Directory.GetParent(baseDir);
                if (parent != null)
                    repoRoot = parent.FullName;
            }
            catch
            {
                repoRoot = baseDir;
            }

            string commandPath;
            string argumentPrefix;
            if (!TryResolveOcrRunner(repoRoot, out commandPath, out argumentPrefix))
                return false;

            Directory.CreateDirectory(captureDir);
            WriteDiscoverCandidates(candidateFile, choices);

            string args = string.Join(" ", new[]
            {
                "--image", Quote(string.Empty),
                "--state", Quote(stateFile),
                "--candidate-file", Quote(candidateFile),
                "--stage", Quote("discover"),
                "--sb-profile", Quote(Sanitize(Bot.CurrentProfile())),
                "--sb-mulligan", Quote(Sanitize(Bot.CurrentMulligan())),
                "--sb-discover-profile", Quote(Sanitize(Bot.CurrentDiscoverProfile())),
                "--sb-mode", Quote(CurrentMode(Bot.CurrentMode())),
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
                    return false;
                }
            }

            return TryReadDiscoverPickFromState(stateFile, choices, out pick);
        }

        private bool TryResolveOcrRunner(string repoRoot, out string fileName, out string argumentPrefix)
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

        private string CaptureDiscoverScreenshot(string repoRoot, string captureDir, string requestedImagePath)
        {
            string standardPngPath = Path.ChangeExtension(requestedImagePath, ".png");
            if (TryCaptureHearthstoneWindow(repoRoot, standardPngPath))
                return standardPngPath;

            GUI.TakeScreenshotToPath(captureDir, Path.GetFileName(requestedImagePath));
            for (int i = 0; i < 30; i++)
            {
                string resolvedImagePath = ResolveDiscoverScreenshotPath(requestedImagePath);
                if (!string.IsNullOrWhiteSpace(resolvedImagePath))
                    return resolvedImagePath;
                System.Threading.Thread.Sleep(50);
            }

            return string.Empty;
        }

        private bool TryCaptureHearthstoneWindow(string repoRoot, string imagePath)
        {
            try
            {
                string captureScript = Path.Combine(repoRoot, "tools", "capture_hearthstone_window.ps1");
                if (!File.Exists(captureScript))
                    return false;

                using (Process process = new Process())
                {
                    process.StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell",
                        Arguments = "-NoProfile -ExecutionPolicy Bypass -File " + Quote(captureScript) + " -OutputPath " + Quote(imagePath),
                        WorkingDirectory = repoRoot,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true
                    };
                    process.Start();
                    if (!process.WaitForExit(10000))
                    {
                        try { process.Kill(); } catch { }
                        return false;
                    }
                }

                return File.Exists(imagePath);
            }
            catch
            {
                return false;
            }
        }

        private string ResolveDiscoverScreenshotPath(string requestedImagePath)
        {
            if (File.Exists(requestedImagePath))
                return requestedImagePath;

            string appendedPng = requestedImagePath + ".png";
            if (File.Exists(appendedPng))
                return appendedPng;

            string pngPath = Path.Combine(
                Path.GetDirectoryName(requestedImagePath) ?? string.Empty,
                Path.GetFileNameWithoutExtension(requestedImagePath) + ".png");
            if (File.Exists(pngPath))
                return pngPath;

            return string.Empty;
        }

        private void WriteDiscoverCandidates(string candidateFile, List<Card.Cards> choices)
        {
            List<string> rows = new List<string>();
            for (int i = 0; i < choices.Count; i++)
            {
                Card.Cards id = choices[i];
                rows.Add("card\t" + id + "\t" + Sanitize(SafeCardName(id)) + "\t" + (i + 1).ToString());
            }

            File.WriteAllLines(candidateFile, rows, Encoding.UTF8);
        }

        private bool TryReadDiscoverPickFromState(string stateFile, List<Card.Cards> choices, out Card.Cards pick)
        {
            pick = default(Card.Cards);
            if (!File.Exists(stateFile))
                return false;

            string status = string.Empty;
            string stage = string.Empty;
            string pickRaw = string.Empty;

            foreach (string rawLine in File.ReadAllLines(stateFile))
            {
                if (rawLine.StartsWith("status=", StringComparison.OrdinalIgnoreCase))
                    status = rawLine.Substring("status=".Length).Trim();
                else if (rawLine.StartsWith("stage=", StringComparison.OrdinalIgnoreCase))
                    stage = rawLine.Substring("stage=".Length).Trim();
                else if (rawLine.StartsWith("discover_pick_id=", StringComparison.OrdinalIgnoreCase))
                    pickRaw = rawLine.Substring("discover_pick_id=".Length).Trim();
            }

            if (!string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.Equals(stage, "discover", StringComparison.OrdinalIgnoreCase))
                return false;

            if (string.IsNullOrWhiteSpace(pickRaw))
                return false;

            Card.Cards parsed;
            if (!Enum.TryParse(pickRaw, true, out parsed))
                return false;

            if (!choices.Contains(parsed))
                return false;

            pick = parsed;
            return true;
        }

        private bool TryLoadLearnedDiscoverPick(Card.Cards originCard, List<Card.Cards> choices, out Card.Cards pick)
        {
            pick = default(Card.Cards);
            string path = GetDiscoverMemoryFilePath();
            if (!File.Exists(path) || choices == null || choices.Count == 0)
                return false;

            string discoverProfile = NormalizeStrategyName(Sanitize(Bot.CurrentDiscoverProfile()));
            string choicesKey = BuildChoicesKey(choices);
            Dictionary<Card.Cards, int> counts = new Dictionary<Card.Cards, int>();

            foreach (string rawLine in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                    continue;

                string[] parts = rawLine.Split('\t');
                if (parts.Length < 5)
                    continue;

                if (!string.Equals(parts[0], "discover", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.Equals(NormalizeStrategyName(parts[1]), discoverProfile, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.Equals(parts[2], originCard.ToString(), StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.Equals(parts[3], choicesKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                Card.Cards parsed;
                if (!Enum.TryParse(parts[4], true, out parsed))
                    continue;

                if (!choices.Contains(parsed))
                    continue;

                if (!counts.ContainsKey(parsed))
                    counts[parsed] = 0;
                counts[parsed]++;
            }

            if (counts.Count == 0)
                return false;

            pick = counts.OrderByDescending(x => x.Value).First().Key;
            return true;
        }

        private void RememberDiscoverPick(Card.Cards originCard, List<Card.Cards> choices, Card.Cards pick)
        {
            try
            {
                string path = GetDiscoverMemoryFilePath();
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string row = string.Join("\t", new[]
                {
                    "discover",
                    NormalizeStrategyName(Sanitize(Bot.CurrentDiscoverProfile())),
                    originCard.ToString(),
                    BuildChoicesKey(choices),
                    pick.ToString()
                });

                File.AppendAllLines(path, new[] { row }, Encoding.UTF8);
            }
            catch
            {
                // ignore
            }
        }

        private string GetDiscoverMemoryFilePath()
        {
            string runtimeDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime");
            string primaryPath = Path.Combine(runtimeDir, "decision_teacher_discover_memory.tsv");
            string legacyPath = Path.Combine(runtimeDir, "netease_box_ocr_discover_memory.tsv");
            if (File.Exists(primaryPath))
                return primaryPath;
            if (File.Exists(legacyPath))
                return legacyPath;
            return primaryPath;
        }

        private string BuildChoicesKey(List<Card.Cards> choices)
        {
            return string.Join(",", choices.Select(x => x.ToString()).OrderBy(x => x));
        }

        private string NormalizeStrategyName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;
            return Path.GetFileName(raw.Trim().Replace('/', '\\'));
        }

        private string Sanitize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;
            return raw.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private string Quote(string raw)
        {
            if (raw == null)
                return "\"\"";
            return "\"" + raw.Replace("\"", "\\\"") + "\"";
        }

        // Adds text to log variable
        private void AddLog(string log)
        {
            this.log += "\r\n" + log;
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
        private double Gigantotem(Card.Cards choice, double points, Board board)
        {
            // List of totems: Mistake, Stereo Totem, The One-Amalgam Band, Ancient Totem, Jukebox Totem, Anchored Totem, Flametongue Totem, Flametongue Totem, Amalgam of the Deep, Totem Golem, Gigantotem, Sinstone Totem, Party Favor Totem, Party Favor Totem, Wrath of Air Totem, Searing Totem, Healing Totem, Stoneclaw Totem, Strength Totem, Grand Totem Eys'or -> Set Madness At The Darkmoon Faire, Grand Totem Eys'or -> Set Unknown, Treant Totem, Trick Totem, Totem Goliath, EVIL Totem, Serpent Ward, Primalfin Totem, Vitality Totem, Mana Tide Totem
            {
                var totemTable = new List<Card.Cards>
                    {
                        Card.Cards.NX2_050, Card.Cards.ETC_105, Card.Cards.ETC_409, Card.Cards.TTN_710, Card.Cards.JAM_010,
                        Card.Cards.TSC_922, Card.Cards.EX1_565, Card.Cards.CORE_EX1_565, Card.Cards.TSC_069, Card.Cards.AT_052,
                        Card.Cards.REV_838, Card.Cards.REV_839, Card.Cards.REV_935, Card.Cards.REV_935t, Card.Cards.CS2_052,
                        Card.Cards.CS2_050, Card.Cards.NEW1_009, Card.Cards.CS2_051, Card.Cards.CS2_058, Card.Cards.DMF_709,
                        Card.Cards.CORE_DMF_709, Card.Cards.SCH_612t, Card.Cards.SCH_537, Card.Cards.SCH_615, Card.Cards.ULD_276,
                        Card.Cards.TRL_057, Card.Cards.UNG_201, Card.Cards.GVG_039, Card.Cards.CORE_EX1_575
                    };

                // Calculate total totems you've summoned this game.
                int totemCount = board.MinionFriend.Count(totem => totemTable.Contains(totem.Template.Id)) + board.FriendGraveyard.Count(totem => totemTable.Contains(totem));

                // True if calculated Gigantotem card cost is equal or greater than current max mana include error of 3
                if (choice == Card.Cards.REV_838 && 10 - totemCount - 3 <= board.MaxMana)
                {
                    description = String.Format("Best: Gigantotem calculated cost: {0}", 10 - Math.Min(totemCount, 10));
                    points += 100;
                }
                else if (10 - totemCount - 3 > board.MaxMana)
                {
                    points += 100;
                }

                return points;
            }
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
            // Get list of current cards in my deck
            List<Card.Cards> currentDeck = new List<Card.Cards>();
            currentDeck = CurrentDeck(board);
            // Cost of last card in hand
            int lastCardCost = board.Hand.LastOrDefault().CurrentCost;
            // Return Less/More counting cost
            return currentDeck.Select(CardTemplate.LoadFromId).Count(x => x.Cost < lastCardCost) + "/" + currentDeck.Select(CardTemplate.LoadFromId).Count(x => x.Cost > lastCardCost);
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
        // Card definition version check. Update files if required
        private void FileVersionCheck()
        {
            // Construct the file paths
            string updaterPath = Path.Combine(smartBotDirectory, "DiscoverMulliganUpdater.exe");

            // Check if updater exists
            if (File.Exists(updaterPath))
            {
                string newVersion = NewVersion();
                string currentVersion = CurrentVersion();

                // Launch updater if version has changed
                if (currentVersion != newVersion && !oneShot)
                {
                    oneShot = true; // Ensure update runs only once per session 
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
            string newVersion = result.ToString("F2", CultureInfo.InvariantCulture);
            return newVersion;
        }

        // Get current version from EE_Information.txt
        private string CurrentVersion()
        {
            string infoPath = Path.Combine(discoverCCDirectory, "EE_Information.txt");

            // Create file if it doesn't exist
            if (!File.Exists(infoPath))
            {
                File.WriteAllText(infoPath, $"EvilEyes Discovery Card Choices Version ({NewVersion()})");
                return null;
            }

            // Read first line and extract version number using regex
            string firstLine = File.ReadLines(infoPath).First();
            Match match = Regex.Match(firstLine, @"\((.+?)\)");
            if (match.Success)
                return match.Groups[1].Value;
            return null;
        }

        // Return list of current cards remaining in my deck
        private static List<Card.Cards> CurrentDeck(Board board)
        {
            // Starting cards in my deck -> _myDeck
            List<Card.Cards> _myDeck = new List<Card.Cards>();
            if (board == null || board.Deck == null) return _myDeck;
            foreach (var card in board.Deck)
                _myDeck.Add(card);
            // Add cards in my: hand, friend-board, friend-graveyard ->  _myPlayedCards list
            List<Card.Cards> _myPlayedCards = new List<Card.Cards>();
            if (board.Hand != null)
            {
                foreach (var card in board.Hand)
                {
                    if (card == null || card.Template == null) continue;
                    _myPlayedCards.Add(card.Template.Id);
                }
            }
            if (board.MinionFriend != null)
            {
                foreach (var card in board.MinionFriend)
                {
                    if (card == null || card.Template == null) continue;
                    _myPlayedCards.Add(card.Template.Id);
                }
            }
            if (board.FriendGraveyard != null)
            {
                foreach (var card in board.FriendGraveyard)
                    _myPlayedCards.Add(card);
            }
            // Remove _playedCards from _mydeck list
            foreach (var card in _myPlayedCards)
                _myDeck.Remove(card);
            return _myDeck;
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

        // 斩杀血池计算：敌方英雄血甲 + 所有随从血量
        private static int CurrentEnemyPoolHealth(Board board)
        {
            int hero = board.HeroEnemy.CurrentHealth + board.HeroEnemy.CurrentArmor;
            int minions = (board.MinionEnemy != null) ? board.MinionEnemy.Sum(x => x.CurrentHealth) : 0;
            return hero + minions;
        }

        // 计算手牌中已有的斩杀爆发（计入随机伤害）
        private static int GetTotalBlastDamagesInHand(Board board)
        {
            if (board == null || board.Hand == null) return 0;
            int total = 0;
            foreach (var c in board.Hand)
            {
                if (c == null || c.Template == null) continue;
                var id = c.Template.Id;
                int dmg = 0;
                if (DiscardWarlockStableFaceSpellDamage.TryGetValue(id, out dmg) || DiscardWarlockRandomFaceSpellDamage.TryGetValue(id, out dmg))
                {
                    // 只有费能够用的才计入（简单模型）
                    if (c.CurrentCost <= board.ManaAvailable)
                    {
                        total += dmg;
                    }
                }
            }
            return total;
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

        // 基尔加丹/阿克蒙德发现门槛：只有己方场面优势时才允许强拿这类慢速高费牌。
        private static bool HasFriendlyBoardAdvantageForLegendaryDiscover(Board board, out string snapshot)
        {
            snapshot = "board为空";
            if (board == null)
                return false;

            int friendMinions = board.MinionFriend != null ? board.MinionFriend.Count(x => x != null) : 0;
            int enemyMinions = board.MinionEnemy != null ? board.MinionEnemy.Count(x => x != null) : 0;
            int friendAtkNow = board.MinionFriend != null
                ? board.MinionFriend.Where(x => x != null && x.CanAttack && (x.IsCharge || x.NumTurnsInPlay != 0) && x.CountAttack == 0 && !x.IsTired).Sum(x => x.CurrentAtk)
                : 0;
            int enemyAtkNow = board.MinionEnemy != null
                ? board.MinionEnemy.Where(x => x != null && x.CanAttack && (x.IsCharge || x.NumTurnsInPlay != 0) && x.CountAttack == 0 && !x.IsTired).Sum(x => x.CurrentAtk)
                : 0;
            if (board.HeroFriend != null && board.WeaponFriend != null && board.HeroFriend.CountAttack == 0)
                friendAtkNow += board.WeaponFriend.CurrentAtk;
            if (board.HeroEnemy != null && board.WeaponEnemy != null && board.HeroEnemy.CountAttack == 0)
                enemyAtkNow += board.WeaponEnemy.CurrentAtk;
            int friendTotalAtk = board.MinionFriend != null ? board.MinionFriend.Where(x => x != null).Sum(x => x.CurrentAtk) : 0;
            int enemyTotalAtk = board.MinionEnemy != null ? board.MinionEnemy.Where(x => x != null).Sum(x => x.CurrentAtk) : 0;
            int friendStats = board.MinionFriend != null ? board.MinionFriend.Where(x => x != null).Sum(x => Math.Max(0, x.CurrentAtk) + Math.Max(0, x.CurrentHealth)) : 0;
            int enemyStats = board.MinionEnemy != null ? board.MinionEnemy.Where(x => x != null).Sum(x => Math.Max(0, x.CurrentAtk) + Math.Max(0, x.CurrentHealth)) : 0;

            bool countLead = friendMinions >= enemyMinions + 1;
            bool attackLead = friendAtkNow >= enemyAtkNow + 1;
            bool totalLead = friendTotalAtk >= enemyTotalAtk + 2;
            bool statsLead = friendStats >= enemyStats + 2;
            bool hasBoard = friendMinions > 0;

            snapshot = "随从" + friendMinions + ":" + enemyMinions
                + " | 可攻" + friendAtkNow + ":" + enemyAtkNow
                + " | 总攻" + friendTotalAtk + ":" + enemyTotalAtk
                + " | 身材" + friendStats + ":" + enemyStats;

            return hasBoard && (countLead || ((attackLead || totalLead) && statsLead));
        }

        // FIR_924补充口径：
        // 未达“场面优势”时，如果仍处于中盘稳定窗口（并非绝对劣势/血线压力不高），允许阿克蒙德放行。
        // 目的：避免在可运营局面里把阿克蒙德过度过滤掉。
        private static bool ShouldPreferArchimondeInStableMidgameFromStalker(Board board, Card.Cards originCard, out string snapshot)
        {
            snapshot = "未命中窗口";
            try
            {
                if (board == null) return false;
                if (originCard != Card.Cards.FIR_924) return false;

                int friendHpArmor = 0;
                try { friendHpArmor = board.HeroFriend.CurrentHealth + board.HeroFriend.CurrentArmor; }
                catch { friendHpArmor = 0; }

                int friendMinions = board.MinionFriend != null ? board.MinionFriend.Count(x => x != null) : 0;
                int enemyMinions = board.MinionEnemy != null ? board.MinionEnemy.Count(x => x != null) : 0;
                int friendAtkNow = board.MinionFriend != null
                    ? board.MinionFriend.Where(x => x != null && x.CanAttack && (x.IsCharge || x.NumTurnsInPlay != 0) && x.CountAttack == 0 && !x.IsTired).Sum(x => Math.Max(0, x.CurrentAtk))
                    : 0;
                int enemyAtkNow = board.MinionEnemy != null
                    ? board.MinionEnemy.Where(x => x != null && x.CanAttack && (x.IsCharge || x.NumTurnsInPlay != 0) && x.CountAttack == 0 && !x.IsTired).Sum(x => Math.Max(0, x.CurrentAtk))
                    : 0;
                int friendTotalAtk = board.MinionFriend != null ? board.MinionFriend.Where(x => x != null).Sum(x => Math.Max(0, x.CurrentAtk)) : 0;
                int enemyTotalAtk = board.MinionEnemy != null ? board.MinionEnemy.Where(x => x != null).Sum(x => Math.Max(0, x.CurrentAtk)) : 0;

                if (board.HeroFriend != null && board.WeaponFriend != null && board.HeroFriend.CountAttack == 0)
                    friendAtkNow += Math.Max(0, board.WeaponFriend.CurrentAtk);
                if (board.HeroEnemy != null && board.WeaponEnemy != null && board.HeroEnemy.CountAttack == 0)
                    enemyAtkNow += Math.Max(0, board.WeaponEnemy.CurrentAtk);

                bool enemyHasBoard = enemyMinions > 0;
                bool notDangerous = friendHpArmor >= 14 && enemyAtkNow <= Math.Max(6, friendHpArmor / 3);
                bool boardNotCollapsed = friendMinions >= Math.Max(1, enemyMinions - 1);
                bool tempoNotBehind = friendAtkNow + 2 >= enemyAtkNow && friendTotalAtk + 2 >= enemyTotalAtk;

                snapshot = "血甲" + friendHpArmor
                    + " | 随从" + friendMinions + ":" + enemyMinions
                    + " | 可攻" + friendAtkNow + ":" + enemyAtkNow
                    + " | 总攻" + friendTotalAtk + ":" + enemyTotalAtk;

                return enemyHasBoard && notDangerous && boardNotCollapsed && tempoNotBehind;
            }
            catch
            {
                return false;
            }
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
