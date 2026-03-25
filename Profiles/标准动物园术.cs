using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SmartBot.Database;
using SmartBot.Plugins.API;

namespace SmartBotProfiles
{
    [Serializable]
    public class STDZooWarlock : Profile
    {
        private const string ProfileVersion = "2026-03-13.147";
        private static readonly bool EnableAutoConcede = false; // 用户要求：关闭自动投降
        private string _log = "";
        private const int HearthstoneHandLimit = 10;

        // ===== 英雄技能 / 通用 =====
        private const Card.Cards TheCoin = Card.Cards.GAME_005;
        private const Card.Cards Innervate = Card.Cards.CORE_EX1_169;
        private const Card.Cards LifeTap = Card.Cards.HERO_07bp;
        private const Card.Cards Banana = Card.Cards.EX1_014t;               // 香蕉（+1/+1）

        // ===== 套牌卡牌 =====
        private const Card.Cards TombOfSuffering = Card.Cards.TLC_451;          // 咒怨之墓（Fel）
        private const Card.Cards Wisp = Card.Cards.CORE_CS2_231;                // 小精灵
        private const Card.Cards GlacialShard = Card.Cards.CORE_UNG_205;        // 冰川裂片（Elemental）
        private const Card.Cards ViciousSlitherspear = Card.Cards.CORE_TSC_827; // 凶恶的滑矛纳迦（Naga）
        private const Card.Cards AbductionRay = Card.Cards.GDB_123;             // 挟持射线（Shadow）
        private const Card.Cards Platysaur = Card.Cards.TLC_603;                // 栉龙（Beast）
        private const Card.Cards FlameImp = Card.Cards.CORE_EX1_319;            // 烈焰小鬼（Demon）
        private const Card.Cards EntropicContinuity = Card.Cards.TIME_026;      // 续连熵能
        private const Card.Cards Murmy = Card.Cards.CORE_ULD_723;               // 鱼人木乃伊（Murloc）
        private const Card.Cards TrackingAmulet = Card.Cards.VAC_959t05;        // 追踪护符
        private const Card.Cards CrittersAmulet = Card.Cards.VAC_959t06;        // 生灵护符
        private const Card.Cards EnergyAmulet = Card.Cards.VAC_959t08;          // 能量护符
        private const Card.Cards StridesAmulet = Card.Cards.VAC_959t10;         // 挺进护符
        private const Card.Cards ForebodingFlame = Card.Cards.GDB_121;          // 恶兆邪火（Elemental）
        private const Card.Cards DespicableDreadlord = Card.Cards.CORE_ICC_075; // 卑鄙的恐惧魔王（Demon）
        private const Card.Cards PartyFiend = Card.Cards.VAC_940;               // 派对邪犬（Demon）
        private const Card.Cards TortollanStoryteller = Card.Cards.TLC_254;     // 讲故事的始祖龟
        private const Card.Cards SketchArtist = Card.Cards.TOY_916;             // 速写美术家
        private const Card.Cards ShadowflameStalker = Card.Cards.FIR_924;       // 影焰猎豹（Beast）
        private const Card.Cards FrenziedWrathguard = Card.Cards.GDB_132;       // 躁动的愤怒卫士（击杀目标才发现恶魔）
        private const Card.Cards Rezdir = Card.Cards.TLC_463;                    // 雷兹迪尔（标记态联动）
        private const Card.Cards TimethiefRafaam = Card.Cards.TIME_005;          // 时空大盗拉法姆
        private const Card.Cards MonthlyModelEmployee = Card.Cards.WORK_009;    // 月度魔范员工（贴吸血）
        private const Card.Cards Infernal = Card.Cards.MIS_703;                 // 地狱火！
        private const Card.Cards UnreleasedMasseridon = Card.Cards.TOY_647;     // 玛瑟里顿（未发售版，休眠期回合末对所有敌人造成3伤）
        private const Card.Cards MoargForgefiend = Card.Cards.CORE_SW_068;           // 莫尔葛熔魔（Demon）
        private const Card.Cards Doomguard = Card.Cards.CORE_EX1_310;           // 末日守卫（Demon）
        private const Card.Cards Archimonde = Card.Cards.GDB_128;               // 阿克蒙德（Demon）
        private const Card.Cards Kiljaeden = Card.Cards.GDB_145;                // 基尔加丹（Demon）
        private const Card.Cards Alenashi = Card.Cards.EDR_493;                 // 阿莱纳希
        private const Card.Cards FuriousPriestess = Card.Cards.CORE_BT_493;     // 愤怒的女祭司
        private const int AbductionRayMinManaNonTemp = 4;                       // 挟持射线非临时“起链”最低可用费用门槛
        private const int AbductionRayNonTempPreferredMaxHand = 5;              // 挟持射线非临时“起链”建议手牌上限（<=5）
        private const int AbductionRayOutsideDemonDelayMinHand = 6;             // 射线起链让位套外恶魔：最低手牌数
        private const int AbductionRayOutsideDemonDelayMinPlayable = 2;         // 射线起链让位套外恶魔：本回合最低可下数量
        private const int AbductionRayOutsideDemonDelayMinPlayableUnderPressure = 1; // 高压窗口下，1个可下套外恶魔也可让位
        private const Card.Cards EredarBrute = Card.Cards.GDB_320;              // 艾瑞达蛮兵
        private const Card.Cards RedCard = Card.Cards.TOY_644;                  // 红牌（点控/锁定）
        private const Card.Cards Backstab = Card.Cards.CORE_CS2_072;            // 背刺（0费解场）
        private const Card.Cards BattlefieldNecromancer = Card.Cards.RLK_061;   // 战场通灵师（优先解）
        private const string StarDestroyerCardId = "GDB_118";                   // 星辰毁灭者（手牌满10时禁用，避免爆牌浪费）
        private const string CreationStarCardId = "GDB_118t";                   // 创生之星（当前策略禁用，避免重复无效施放）
        private const string TerminusStarCardId = "GDB_118t2";                  // 终结之星（用于动作判定排除，避免伪动作干扰）
        private const string UnlicensedApothecaryCardId = "CFM_900";            // 无证药剂师（用户硬禁用）
        private const string WindowShopperCardId = "TOY_652";                   // 橱窗看客
        private const string WindowShopperMiniCardId = "TOY_652t";              // 橱窗看客（微缩）
        private const string ArsonEyedDemonCardId = "EDR_486";                  // 纵火眼魔（突袭+吸血）
        private const string SoulDealerCardId = "WORK_015";                     // 精魂商贩（套外恶魔突袭解场）
        private const string KingLlaneCardId = "TIME_875t";                     // 莱恩国王

        // 奇利亚斯（输能模块 + 计数模块）
        private const Card.Cards ZilliaxDeckBuilder = Card.Cards.TOY_330;
        private const Card.Cards ZilliaxTickingPower = Card.Cards.TOY_330t5;

        // 本套“原生恶魔”（不算“套牌外恶魔”）
        private static readonly HashSet<Card.Cards> NativeDeckDemons = new HashSet<Card.Cards>
        {
            FlameImp,
            PartyFiend,
            Archimonde
        };

        // 拉法姆家族（本套可出现）：时空大盗 + 9种形态
        private static readonly HashSet<string> RafaamFamilyCardIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "TIME_005",   // 时空大盗拉法姆
            "TIME_005t1", // 小小拉法姆
            "TIME_005t2", // 绿色拉法姆
            "TIME_005t3", // 探险者拉法姆
            "TIME_005t4", // 大酋长拉法姆
            "TIME_005t5", // 夺心者拉法姆
            "TIME_005t6", // 灾异拉法姆
            "TIME_005t7", // 巨人拉法姆
            "TIME_005t8", // 鱼人拉法姆
            "TIME_005t9"  // 大法师拉法姆
        };

        // 在线来源：https://api.hearthstonejson.com/v1/latest/enUS/cards.json
        // 规则：type=MINION 且 race/races 含 DEMON 且 mechanics 含 TAUNT
        // 分层策略：
        // 1) 构筑相关集合：标准/天梯主线优先（第二层兜底）
        // 2) 模式专用集合：BATTLEGROUNDS/LETTUCE/Story/Prologue 等，默认不启用兜底
        private const bool EnableModeOnlyDemonTauntFallback = false;

        private static readonly HashSet<string> KnownDemonTauntConstructedCardIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "AV_286",
            "BAR_072t",
            "BTA_17",
            "BT_258",
            "BT_304",
            "BT_486",
            "BT_510",
            "CORE_BT_304",
            "CORE_CS2_065",
            "CORE_CS3_021",
            "CORE_LOOT_013",
            "CORE_SW_068",
            "CORE_UNG_833",
            "CS2_065",
            "CS3_021",
            "DAL_185",
            "DINO_132",
            "DMF_114",
            "DMF_247",
            "DMF_247t",
            "DMF_533",
            "DRG_207t",
            "EDR_490t",
            "ETC_t8t",
            "EX1_185",
            "EX1_301",
            "GDB_124t2",
            "GDB_320",
            "GIL_527",
            "JAM_016",
            "KARA_09_08",
            "LOOT_013",
            "LOOT_368",
            "MIS_703",
            "RLK_212",
            "SCH_343",
            "SCH_357t",
            "SCH_600t2",
            "SW_037",
            "SW_068",
            "SW_068_COPY",
            "SW_085t",
            "SW_451",
            "TLC_446t4",
            "TOY_914",
            "TOY_914t",
            "UNG_833",
            "VAC_943t",
            "VAN_CS2_065",
            "VAN_EX1_301",
            "WC_040",
            "WW_442",
        };

        private static readonly HashSet<string> KnownDemonTauntModeOnlyCardIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "BG21_004",
            "BG21_004_G",
            "BG25_807t2",
            "BG25_807t2_G",
            "BG31_870",
            "BG31_870_G",
            "BG32_891",
            "BG32_891_G",
            "BG33_156",
            "BG33_156_G",
            "BG33_157",
            "BG33_157_G",
            "BG34_325",
            "BG34_325_G",
            "BG34_Giant_360",
            "BG34_Giant_360_G",
            "BG34_Giant_584",
            "BG34_Giant_584_G",
            "BGS_014",
            "BG_CS2_065",
            "BG_DMF_533",
            "BG_LOOT_368",
            "KARA_09_08_heroic",
            "LETL_040P5_02t",
            "LETL_040P5_03t",
            "LETL_040P5_04t",
            "LETL_040P7_01m",
            "LETL_040P7_02m",
            "LETL_040P7_03m",
            "LETL_040P7_04m",
            "LETL_040P7_05m",
            "RLK_Prologue_EX1_185",
            "RLK_Prologue_LOOT_013",
            "Story_09_ViciousFelhound",
            "Story_09_VoidDrinker",
            "Story_09_VulgarHomunculus",
            "Story_10_ElderVoidwalker",
            "Story_10_MoargPainsmith",
            "TB_BaconShop_HERO_37_Buddy",
            "TB_BaconShop_HERO_37_Buddy_G",
            "TB_BaconUps_059",
            "TB_BaconUps_059t",
            "TB_BaconUps_113",
            "TB_BaconUps_309",
        };

        // 始祖龟种族统计白名单：与 CountDistinctFriendlyRaceTypes 口径保持一致。
        private static readonly Card.CRace[] StorytellerTrackedRaces =
        {
            Card.CRace.DEMON,
            Card.CRace.MURLOC,
            Card.CRace.NAGA,
            Card.CRace.PET,
            Card.CRace.ELEMENTAL,
            Card.CRace.MECHANICAL,
            Card.CRace.UNDEAD,
            Card.CRace.DRAGON,
            Card.CRace.PIRATE,
            Card.CRace.TOTEM,
            Card.CRace.QUILBOAR
        };

        // 速写/地标等临时牌跟踪：当临时Tag不稳定时，用实体ID做兜底识别。
        private static int _tempTrackTurn = int.MinValue;
        private static readonly HashSet<int> _prevHandEntityIds = new HashSet<int>();
        private static readonly HashSet<int> _temporaryHandEntityIdsThisTurn = new HashSet<int>();
        private static readonly HashSet<int> _temporaryRayEntityIdsThisTurn = new HashSet<int>();
        private static int _tempTrackTombPlayedCount = 0;
        private static int _tempTrackSketchPlayedCount = 0;
        private static int _tempTrackPlatysaurPlayedCount = 0;
        private static int _tempTrackRayPlayedCount = 0;

        // 套外恶魔跟踪：开局记录“牌库+起手”作为初始套牌基线，后续多出来的恶魔视为套外恶魔。
        private static readonly Dictionary<Card.Cards, int> _initialDeckCardCounts = new Dictionary<Card.Cards, int>();
        private static int _initialDeckTotalCards = -1;
        private static int _deckTrackLastTurn = -1;
        private static int _deckTrackLastDeckCount = -1;
        private static int _abductionRayTrackTurn = int.MinValue;
        private static int _abductionRayBasePlayedCount = 0;
        private static int _abductionRayPlannedTurn = int.MinValue;
        private static bool _abductionRayPlannedThisTurn = false;
        private static bool _abductionRayNoTurnInitialized = false;
        private static int _abductionRayNoTurnBasePlayedCount = 0;
        private static int _abductionRayNoTurnLastMaxMana = -1;
        private static int _abductionRayNoTurnLastManaAvailable = -1;
        private static int _abductionRayNoTurnLastDeckCount = -1;
        private int _logTurn = -1;
        private int _logMaxMana = -1;
        private bool _turnMetaLogged = false;

        public ProfileParameters GetParameters(Board board)
        {
            _log = "";
            _turnMetaLogged = false;
            int boardTurn = GetBoardTurn(board);
            _logMaxMana = board != null ? Math.Max(0, board.MaxMana) : -1;
            // 用户口径：日志中的“回合”与“最大法力值上限”一致。
            _logTurn = _logMaxMana >= 0 ? _logMaxMana : boardTurn;
            if (boardTurn != _abductionRayPlannedTurn)
            {
                // Turn 漂移但未到“回满法力”边界时，不清空同回合已计划状态，避免射线链路误断。
                bool shouldResetRayPlanByTurn = _abductionRayPlannedTurn == int.MinValue
                    || boardTurn < 0
                    || _abductionRayPlannedTurn < 0
                    || IsLikelyTurnStartByMana(board);
                if (shouldResetRayPlanByTurn)
                {
                    _abductionRayPlannedTurn = boardTurn;
                    _abductionRayPlannedThisTurn = false;
                }
            }
            var p = new ProfileParameters(BaseProfile.Rush)
            {
                DiscoverSimulationValueThresholdPercent = -10
            };

            if (ProfileCommon.TryRunPureLearningPlayExecutor(board, p))
                return p;

            int friendHp = GetHeroHealth(board.HeroFriend);
            int enemyHp = GetHeroHealth(board.HeroEnemy);
            int friendAttack = GetBoardAttack(board.MinionFriend);
            int friendAttackNow = GetAttackableBoardAttack(board.MinionFriend);
            int enemyAttack = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            int enemyAttackNow = GetAttackableBoardAttack(board.MinionEnemy);
            int virtualManaNow = GetAvailableManaIncludingCoin(board);

            AddLog("================ 标准动物园术 决策日志 v" + ProfileVersion + " ================");
            AddLog("版本特性：可攻场攻判定已启用（斩杀/攻守窗口使用可攻击场攻）");
            AddLog("对手=" + board.EnemyClass + " | 费用=" + board.ManaAvailable + "(含币虚拟=" + virtualManaNow + ") | 手牌=" + board.Hand.Count + " | 牌库=" + board.FriendDeckCount);
            AddLog("我方血甲=" + friendHp + " | 对方血甲=" + enemyHp
                + " | 我方场攻=" + friendAttack + "(可攻=" + friendAttackNow + ")"
                + " | 敌方场攻=" + enemyAttack + "(可攻=" + enemyAttackNow + ")");
            LogHandAndBoardSnapshot(board);
            UpdateTemporaryHandTracking(board);
            UpdateInitialDeckCardSnapshot(board);
            ApplyTombDiscoverNoAbductionRayRule(board, p);
            int outsideDeckDemonsUsed = CountOutsideDeckDemonsInBoardAndGrave(board);
            AddLog("套外恶魔：已使用数量(场+坟)=" + outsideDeckDemonsUsed);
            AddLog("套外恶魔：名单(场+坟)=" + FormatOutsideDeckDemonsInBoardAndGrave(board));

            // 用户规则：判定无解后自动投降（高置信必死且无稳场/翻盘入口时触发）。
            if (EnableAutoConcede
                && ShouldConcedeWhenNoSolution(board, friendHp, enemyHp, friendAttackNow, enemyAttack, enemyAttackNow, virtualManaNow))
            {
                AddLog("投降：判定无解（低血高压且无嘲讽/冻结/射线/分流翻盘线），执行自动投降");
                if (!string.IsNullOrWhiteSpace(_log))
                    Bot.Log(_log);
                try { Bot.Concede(); } catch { }
                ApplyLiveMemoryBiasCompat(board, p);
                return p;
            }

            ApplyGlobalPlan(board, p, friendHp, enemyHp, friendAttackNow, enemyAttack);
            HandleCoin(board, p, enemyHp, friendAttack);

            // 每张牌单独策略（按卡牌效果逐条处理）
            HandleFlameImp(board, p, friendHp);
            HandleViciousSlitherspear(board, p);
            HandleMurmy(board, p);
            HandleWisp(board, p);
            HandleGlacialShard(board, p);
            HandlePlatysaur(board, p);
            HandlePartyFiend(board, p, friendHp);
            HandleForebodingFlame(board, p);
            HandleLateGameOutsideDeckDemonPriority(board, p);
            HandleDespicableDreadlord(board, p);
            HandleBanana(board, p, enemyHp);
            HandleAbductionRay(board, p);
            HandleRedCard(board, p, enemyHp, friendAttack);
            HandleBackstab(board, p, friendHp, enemyHp, friendAttack);
            HandleEntropicContinuity(board, p, friendHp);
            HandleTombOfSuffering(board, p);
            HandleTortollanStoryteller(board, p);
            HandleSketchArtist(board, p);
            HandleShadowflameStalker(board, p);
            HandleMonthlyModelEmployee(board, p, enemyHp);
            HandleArchimonde(board, p);
            HandleKiljaeden(board, p);
            HandleUnreleasedMasseridon(board, p);
            HandleZilliaxTickingPower(board, p, enemyHp, friendAttack);

            HandleHeroPower(board, p, friendHp);
            HandleTempoMinionDump(board, p);
            HandleTemporaryCards(board, p);
            EnforceSketchArtistTopPriority(board, p);
            HandleThreatTargets(board, p, enemyHp, friendAttackNow);
            EnforceAbductionRayContinuationFinalLock(board, p);
            HandleAmuletCardsByEffect(board, p, friendHp);
            HandleEnergyAmuletEmergency(board, p, friendHp);
            HandleHardDisabledCards(board, p);
            EnforceCoinLockBeforeTurnThreeWhenSketchInHand(board, p);
            EnforceFirstTurnSlitherspearOpeningLock(board, p);
            EnforceArchimondeTopPriorityWhenCommitted(board, p);
            ConfigureForcedResimulation(board, p);

            try
            {
                ApplyBoxOcrGeneratedBias(p, board);
            }
            catch (Exception ex)
            {
                AddLog("[BoxOCR] generated bias apply failed -> " + ex.Message);
            }

            try
            {
                ApplyLocalDecisionRules(p, board);
            }
            catch (Exception ex)
            {
                AddLog("[RuleJSON] local rule apply failed -> " + ex.Message);
            }

            try
            {
                ApplyBoxOcrLiveBias(p, board);
            }
            catch (Exception ex)
            {
                AddLog("[BoxOCR] live bias apply failed -> " + ex.Message);
            }

            if (!string.IsNullOrWhiteSpace(_log))
                Bot.Log(_log);
            ApplyLiveMemoryBiasCompat(board, p);
            return p;
        }

        private void ApplyLocalDecisionRules(ProfileParameters p, Board board)
        {
            if (p == null || board == null)
                return;

            GameStateSnapshot snapshot = DecisionStateExtractor.Build(board);
            EnrichLocalDecisionFacts(snapshot, board);
            List<DecisionRuleMatchResult> matches = DecisionRuleEngine.EvaluatePlayRules(snapshot, "标准动物园术.cs");
            bool appliedPrimaryRule = false;
            bool appliedAttackRule = false;
            bool blockLocalPrimaryRules = false;
            DecisionRuleMatchResult appliedMatch = null;
            string appliedSource = string.Empty;

            if (matches != null)
            {
                foreach (DecisionRuleMatchResult match in matches)
                {
                    if (match == null || match.Rule == null || match.Rule.then == null)
                        continue;

                    DecisionRuleAction action = match.Rule.then;
                    string actionType = (action.type ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(actionType))
                        continue;

                    if (string.Equals(actionType, "PlayCard", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!appliedPrimaryRule && !blockLocalPrimaryRules && ApplyLocalRulePlayCard(p, board, match))
                        {
                            appliedPrimaryRule = true;
                            if (appliedMatch == null)
                            {
                                appliedMatch = match;
                                appliedSource = "local_rule_play_card";
                            }
                        }
                        continue;
                    }

                    if (string.Equals(actionType, "UseHeroPower", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!appliedPrimaryRule && !blockLocalPrimaryRules && ApplyLocalRuleHeroPower(p, board, match))
                        {
                            appliedPrimaryRule = true;
                            if (appliedMatch == null)
                            {
                                appliedMatch = match;
                                appliedSource = "local_rule_hero_power";
                            }
                        }
                        continue;
                    }

                    if (string.Equals(actionType, "Attack", StringComparison.OrdinalIgnoreCase))
                    {
                        if (appliedPrimaryRule && action.delay_non_attack_actions.GetValueOrDefault(false))
                            continue;

                        bool shouldBlockLocalPrimaryRules;
                        if (!appliedAttackRule && ApplyLocalRuleAttack(p, board, match, out shouldBlockLocalPrimaryRules))
                        {
                            appliedAttackRule = true;
                            if (appliedMatch == null)
                            {
                                appliedMatch = match;
                                appliedSource = "local_rule_attack";
                            }
                            if (shouldBlockLocalPrimaryRules)
                                blockLocalPrimaryRules = true;
                        }
                    }

                    if ((appliedPrimaryRule || blockLocalPrimaryRules) && appliedAttackRule)
                    {
                        break;
                    }
                }
            }

            DecisionLearningCapture.CapturePlayTeacherSample(
                snapshot,
                "标准动物园术.cs",
                matches,
                appliedMatch,
                appliedSource);
        }

        private void EnrichLocalDecisionFacts(GameStateSnapshot snapshot, Board board)
        {
            if (snapshot == null || board == null)
                return;

            int manaNow = GetAvailableManaIncludingCoin(board);
            snapshot.ManaAvailable = manaNow;
            int friendHp = GetHeroHealth(board.HeroFriend);
            int enemyHp = GetHeroHealth(board.HeroEnemy);
            int enemyAttack = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            bool enemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            bool enemyHasSecret = board.SecretEnemy || board.SecretEnemyCount > 0;
            int attackableFriendAttack = GetAttackableBoardAttack(board.MinionFriend);
            bool hasAttackableLifesteal = HasAttackableLifestealMinion(board);
            bool lowHpSecretRisk = IsLowHpSecretRisk(friendHp, enemyAttack);
            bool hasPlayableHandActionNow = HasPlayableHandActionNow(board, manaNow);
            bool enemyHasBoard = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null);
            bool rayAttackDelayWindow = IsRayAttackDelayWindow(board);
            bool sketchToRayAttackDelayWindow = IsSketchToRayAttackDelayWindow(board, manaNow);
            bool tombAttackDelayWindow = ShouldForceTombFirstThisTurn(board);
            bool tombBuffScoutAttackDelayWindow = IsTombBuffScoutAttackDelayWindow(board, manaNow, enemyHp);
            bool entropyAttackDelayWindow = IsEntropyAttackDelayWindowForRules(
                board,
                manaNow,
                friendHp,
                enemyAttack,
                enemyHasSecret,
                enemyHasBoard,
                hasAttackableLifesteal,
                lowHpSecretRisk,
                rayAttackDelayWindow,
                tombAttackDelayWindow);
            bool monthlyLifestealAttackDelayWindow = IsMonthlyLifestealAttackDelayWindow(board, manaNow);
            bool bananaAttackDelayWindow = IsBananaAttackDelayWindow(board, manaNow);
            bool zeroCostNonRayAttackDelayWindow = IsZeroCostNonRayAttackDelayWindow(board, manaNow);

            if (CanUseLifeTapNow(board))
                snapshot.AddFact("hero_power_usable_now");
            if (HasPlayableWindowShopperFamily(board, manaNow))
                snapshot.AddFact("playable_window_shopper_now");
            if (GetMaxPlayableOutsideDeckDemonsThisTurn(board, manaNow) > 0)
                snapshot.AddFact("playable_outside_deck_demon_now");
            if (GetPlayableTauntMinionCardIds(board, manaNow).Count > 0)
                snapshot.AddFact("playable_taunt_minion_now");
            if (!enemyHasTaunt && attackableFriendAttack >= enemyHp)
                snapshot.AddFact("on_board_lethal_now");
            if (IsDangerousHpTauntTempoWindow(friendHp, enemyAttack))
                snapshot.AddFact("dangerous_hp_taunt_tempo");
            if (enemyHasSecret)
                snapshot.AddFact("enemy_has_secret_now");
            if (hasAttackableLifesteal)
                snapshot.AddFact("attackable_lifesteal_now");
            if (enemyHasSecret
                && board.MinionEnemy != null
                && board.MinionEnemy.Any(m => m != null)
                && hasAttackableLifesteal
                && lowHpSecretRisk)
            {
                snapshot.AddFact("secret_lifesteal_recover_window");
            }
            if (hasPlayableHandActionNow)
                snapshot.AddFact("playable_hand_action_now");
            if (rayAttackDelayWindow)
                snapshot.AddFact("ray_attack_delay_window");
            if (sketchToRayAttackDelayWindow)
                snapshot.AddFact("sketch_to_ray_attack_delay_window");
            if (tombAttackDelayWindow)
                snapshot.AddFact("tomb_attack_delay_window");
            if (tombBuffScoutAttackDelayWindow)
                snapshot.AddFact("tomb_buff_scout_attack_delay_window");
            if (entropyAttackDelayWindow)
                snapshot.AddFact("entropy_attack_delay_window");
            if (monthlyLifestealAttackDelayWindow)
                snapshot.AddFact("monthly_lifesteal_attack_delay_window");
            if (bananaAttackDelayWindow)
                snapshot.AddFact("banana_attack_delay_window");
            if (zeroCostNonRayAttackDelayWindow)
                snapshot.AddFact("zero_cost_non_ray_attack_delay_window");
            if (EvaluateBuffAttackDelayWindow(board, enemyHp, false))
                snapshot.AddFact("buff_attack_delay_window");
            if (ShouldPrioritizeMoargForgefiendNow(board))
                snapshot.AddFact("moarg_priority_window");
            if (board.Hand != null && board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == ForebodingFlame
                && c.CurrentCost <= manaNow)
                && GetFreeBoardSlots(board) > 0)
            {
                snapshot.AddFact("foreboding_flame_playable_now");
            }
        }

        private bool HasPlayableHandActionNow(Board board, int manaNow)
        {
            if (board == null || board.Hand == null)
                return false;

            int freeBoardSlots = GetFreeBoardSlots(board);
            return board.Hand.Any(c =>
                c != null
                && c.Template != null
                && c.CurrentCost <= manaNow
                && (c.Type != Card.CType.MINION || freeBoardSlots > 0));
        }

        private bool HasAttackableLifestealMinion(Board board)
        {
            return board != null
                && board.MinionFriend != null
                && board.MinionFriend.Any(m =>
                    m != null
                    && m.CanAttack
                    && m.CurrentAtk > 0
                    && m.IsLifeSteal);
        }

        private bool HasAttackableNaga(Board board)
        {
            return board != null
                && board.MinionFriend != null
                && board.MinionFriend.Any(m =>
                    m != null
                    && m.CanAttack
                    && m.CurrentAtk > 0
                    && m.Template != null
                    && m.Template.Id == ViciousSlitherspear);
        }

        private bool IsLowHpSecretRisk(int friendHp, int enemyAttack)
        {
            return friendHp <= 8 || enemyAttack >= friendHp || (friendHp <= 12 && enemyAttack >= 6);
        }

        private bool HasTemporaryOrSketchGeneratedAbductionRay(Board board)
        {
            return HasTemporaryPlayableAbductionRay(board) || HasSketchGeneratedAbductionRayWindow(board);
        }

        private bool IsAbductionRayContinuationOrChainWindow(Board board)
        {
            return IsAbductionRayContinuationThisTurn(board) || ShouldChainAbductionRayNow(board);
        }

        private static bool WasAbductionRayLastPlayed(Board board)
        {
            try
            {
                return board != null
                    && board.PlayedCards != null
                    && board.PlayedCards.Count > 0
                    && board.PlayedCards.Last() == AbductionRay;
            }
            catch
            {
                return false;
            }
        }

        private bool HasStartedOrLastPlayedAbductionRayThisTurn(Board board)
        {
            return HasStartedOrPlannedAbductionRayThisTurn(board) || WasAbductionRayLastPlayed(board);
        }

        private sealed class AbductionRayStartupState
        {
            public int ManaNow;
            public bool TempLikeWindow;
            public bool RayStartedThisTurn;
            public int RayStartupHandCount;
            public bool AllowNonTempResourceWindow;
            public bool MidLateMultiRayWindow;
            public bool NonTempStartupManaWindow;
            public bool DelayStartupForOutsideDeckDemonsWindow;
            public bool HandFullWithZeroCostMinionWindow;
            public int PlayableRayCount;
        }

        private AbductionRayStartupState EvaluateAbductionRayStartupState(Board board, int manaNow = -1)
        {
            AbductionRayStartupState state = new AbductionRayStartupState();
            if (board == null)
                return state;

            if (manaNow < 0)
                manaNow = GetAvailableManaIncludingCoin(board);

            state.ManaNow = manaNow;
            state.TempLikeWindow = HasTemporaryOrSketchGeneratedAbductionRay(board);
            state.RayStartedThisTurn = HasStartedOrLastPlayedAbductionRayThisTurn(board);
            state.RayStartupHandCount = GetAbductionRayStartupEffectiveHandCount(board);
            state.AllowNonTempResourceWindow = state.RayStartupHandCount <= AbductionRayNonTempPreferredMaxHand;
            state.MidLateMultiRayWindow = IsAbductionRayMidLateMultiRayPriorityWindow(board, manaNow);
            state.NonTempStartupManaWindow = IsNonTempAbductionRayStartupManaWindow(board, manaNow);
            state.DelayStartupForOutsideDeckDemonsWindow = !state.RayStartedThisTurn
                && ShouldDelayAbductionRayStartupForOutsideDeckDemons(board, manaNow);
            state.HandFullWithZeroCostMinionWindow = board.Hand != null
                && board.Hand.Count >= HearthstoneHandLimit
                && !state.RayStartedThisTurn
                && GetRightmostPlayableZeroCostNonRayMinion(board, manaNow) != null;
            state.PlayableRayCount = board.Hand != null
                ? board.Hand.Count(c => c != null
                    && c.Template != null
                    && c.Template.Id == AbductionRay
                    && c.CurrentCost <= manaNow)
                : 0;
            return state;
        }

        private bool HasAbductionRayPriorityWindow(Board board, AbductionRayStartupState startupState, bool rayHardLockWindow)
        {
            bool tempLikeWindow = startupState != null && startupState.TempLikeWindow;
            bool rayStartedThisTurn = startupState != null && startupState.RayStartedThisTurn;
            bool rayContinuationWindow = rayStartedThisTurn || IsAbductionRayContinuationOrChainWindow(board);
            return rayHardLockWindow || rayContinuationWindow || tempLikeWindow;
        }

        private bool ShouldBlockLifeTapForAbductionRay(Board board, AbductionRayStartupState startupState, bool rayHardLockWindow)
        {
            if (startupState == null)
                return false;

            bool hasAnyPlayableRayNow = startupState.PlayableRayCount > 0;
            bool multiPlayableRayNow = startupState.PlayableRayCount >= 2;
            return hasAnyPlayableRayNow
                && (HasAbductionRayPriorityWindow(board, startupState, rayHardLockWindow) || multiPlayableRayNow);
        }

        private bool IsAbductionRayChainOrTempWindow(Board board, AbductionRayStartupState startupState, bool rayPlayableNow, bool rayHardLockWindow)
        {
            if (startupState == null)
                return false;

            bool hasAnyPlayableRayNow = startupState.PlayableRayCount > 0;
            bool multiPlayableRayNow = startupState.PlayableRayCount >= 2;
            return (rayPlayableNow || hasAnyPlayableRayNow || startupState.RayStartedThisTurn)
                && (HasAbductionRayPriorityWindow(board, startupState, rayHardLockWindow) || multiPlayableRayNow);
        }

        private static bool HasAbductionRayActiveChainWindow(bool rayContinuationThisTurn, bool chainRayNow)
        {
            return rayContinuationThisTurn || chainRayNow;
        }

        private static bool CanDeferAbductionRayForTempo(
            bool tempLikeWindow,
            bool immediateRayFollowupWindow,
            bool fullManaPriorityWindow,
            bool rayContinuationThisTurn,
            bool chainRayNow,
            bool rayMidLateMultiChainWindow)
        {
            return !tempLikeWindow
                && !immediateRayFollowupWindow
                && !fullManaPriorityWindow
                && !rayContinuationThisTurn
                && !chainRayNow
                && !rayMidLateMultiChainWindow;
        }

        private static bool HasAbductionRayOngoingPriorityChainWindow(
            bool rayStartedThisTurn,
            bool rayActiveChainWindow,
            bool immediateRayFollowupWindow,
            bool rayMidLateMultiChainWindow)
        {
            return rayStartedThisTurn
                || rayActiveChainWindow
                || immediateRayFollowupWindow
                || rayMidLateMultiChainWindow;
        }

        private static string GetAbductionRayHardLockComboLogWhenSet(bool chainRayNow, bool rayMidLateMultiChainWindow)
        {
            return chainRayNow
                ? "挟持射线：ComboSet连发锁定"
                : (rayMidLateMultiChainWindow ? "挟持射线：ComboSet中后期多射线锁定" : "挟持射线：ComboSet后期锁定");
        }

        private static string GetAbductionRayHardLockResolveLog(bool rayMidLateMultiChainWindow)
        {
            return rayMidLateMultiChainWindow
                ? "挟持射线：命中中后期多射线窗口，强制先手并禁止始祖龟插队"
                : "挟持射线：命中射线锁定窗口，禁止其他动作插队";
        }

        private static bool ShouldReserveAbductionRayForLowCostTempo(
            bool hasPlayableLowCostMinion,
            bool ongoingPriorityRayChainWindow)
        {
            return hasPlayableLowCostMinion && !ongoingPriorityRayChainWindow;
        }

        private static bool ShouldContinueAbductionRayChainPriority(bool chainRayNow, bool isLateStage, bool handIsVeryLow)
        {
            return chainRayNow && (isLateStage || handIsVeryLow);
        }

        private static bool ShouldSoftReserveAbductionRayChain(bool chainRayNow, bool continueChainRayPriorityWindow)
        {
            return chainRayNow && !continueChainRayPriorityWindow;
        }

        private static string GetAbductionRayChainContinueComboLogWhenSet()
        {
            return "挟持射线：ComboSet连发，继续使用";
        }

        private static string GetAbductionRayChainContinueResolveLog()
        {
            return "挟持射线：连发窗口（后期/低手牌）优先继续使用";
        }

        private static string GetAbductionRayChainSoftReserveResolveLog()
        {
            return "挟持射线：连发已触发但时机偏早，先保留";
        }

        private bool IsRayAttackDelayWindow(Board board)
        {
            return HasAttackableNaga(board)
                && IsAbductionRayPlayableNow(board)
                && (ShouldForceAbductionRayNow(board)
                    || HasTemporaryOrSketchGeneratedAbductionRay(board)
                    || IsAbductionRayContinuationOrChainWindow(board));
        }

        private bool IsSketchToRayAttackDelayWindow(Board board, int manaNow)
        {
            return board != null
                && board.Hand != null
                && board.Hand.Any(c => c != null && c.Template != null
                    && c.Template.Id == SketchArtist
                    && c.CurrentCost <= manaNow)
                && HasAttackableNaga(board)
                && manaNow >= 4
                && board.Hand.Count < 6
                && GetFreeBoardSlots(board) > 0
                && !IsAbductionRayPlayableNow(board);
        }

        private bool IsEntropyAttackDelayWindowForRules(
            Board board,
            int manaNow,
            int friendHp,
            int enemyAttack,
            bool enemyHasSecret,
            bool enemyHasBoard,
            bool hasAttackableLifesteal,
            bool lowHpSecretRisk,
            bool rayAttackDelayWindow,
            bool tombAttackDelayWindow)
        {
            if (board == null || board.Hand == null)
                return false;

            Card entropyPlayableCard = board.Hand
                .Where(c => c != null && c.Template != null
                    && c.Template.Id == EntropicContinuity
                    && c.CurrentCost <= manaNow)
                .OrderBy(c => c.CurrentCost)
                .ThenBy(c => c.Id)
                .FirstOrDefault();
            if (entropyPlayableCard == null)
                return false;

            if (enemyHasSecret && enemyHasBoard && hasAttackableLifesteal && lowHpSecretRisk)
                return false;
            if (friendHp <= 8 && enemyHasBoard)
                return false;
            if (friendHp <= 12 && enemyAttack >= 8)
                return false;
            if (rayAttackDelayWindow || tombAttackDelayWindow)
                return false;

            int manaBeforeEntropy = Math.Max(0, manaNow - entropyPlayableCard.CurrentCost);
            int maxAdditionalBodiesBeforeEntropy = GetMaxAdditionalBodiesBeforeEntropy(board, manaBeforeEntropy);
            int currentFriendlyBodies = board.MinionFriend == null ? 0 : board.MinionFriend.Count(m => m != null);
            return currentFriendlyBodies + maxAdditionalBodiesBeforeEntropy >= 3;
        }

        private bool IsMonthlyLifestealAttackDelayWindow(Board board, int manaNow)
        {
            return board != null
                && board.Hand != null
                && board.Hand.Any(c => c != null && c.Template != null
                    && c.Template.Id == MonthlyModelEmployee
                    && c.CurrentCost <= manaNow)
                && GetFreeBoardSlots(board) > 0
                && board.MinionFriend != null
                && board.MinionFriend.Any(m => m != null && m.CanAttack && m.CurrentAtk > 0);
        }

        private bool IsTombBuffScoutAttackDelayWindow(Board board, int manaNow, int enemyHp)
        {
            return ShouldForceTombBuffScoutBeforeAttack(board, manaNow, enemyHp);
        }

        private bool IsBananaAttackDelayWindow(Board board, int manaNow)
        {
            return board != null
                && board.Hand != null
                && board.Hand.Any(c => c != null && c.Template != null
                    && c.Template.Id == Banana
                    && c.CurrentCost <= manaNow)
                && board.MinionEnemy != null
                && board.MinionEnemy.Any(m => m != null)
                && board.MinionFriend != null
                && board.MinionFriend.Any(m => m != null && m.CanAttack && m.CurrentAtk > 0);
        }

        private bool IsZeroCostNonRayAttackDelayWindow(Board board, int manaNow)
        {
            return GetRightmostPlayableZeroCostNonRayMinion(board, manaNow) != null;
        }

        private bool ShouldCancelBuffAttackDelayOnEmptyBoard(
            Board board,
            int enemyHp,
            int attackableDamage,
            bool enemyHasTaunt,
            bool temporaryRayNagaBuffWindow,
            bool emptyBoardBuffChainWindow,
            bool logDecisions)
        {
            if (board == null || board.MinionFriend == null)
                return false;

            bool enemyBoardEmpty = board.MinionEnemy == null || !board.MinionEnemy.Any(m => m != null);
            if (!enemyBoardEmpty || enemyHasTaunt || attackableDamage <= 0)
                return false;

            bool buffCanConvertToImmediateLethal = CanImmediateBuffConvertToLethal(board, enemyHp, attackableDamage);
            if (!buffCanConvertToImmediateLethal && !emptyBoardBuffChainWindow && !temporaryRayNagaBuffWindow)
            {
                if (logDecisions)
                    AddLog("攻击顺序：敌方空场且buff非当回合斩杀，取消攻前后置");
                return true;
            }

            if (logDecisions)
            {
                if (!buffCanConvertToImmediateLethal && temporaryRayNagaBuffWindow)
                    AddLog("攻击顺序：手握可用临时挟持射线，保留滑矛攻击后置等待射线增伤");
                else if (!buffCanConvertToImmediateLethal && emptyBoardBuffChainWindow)
                    AddLog("攻击顺序：敌方空场但命中攻前buff链，保持后置攻击");
            }

            return false;
        }

        private bool EvaluateBuffAttackDelayWindow(Board board, int enemyHp, bool logDecisions)
        {
            if (board == null || board.Hand == null || board.MinionFriend == null)
                return false;

            var attackers = board.MinionFriend
                .Where(m => m != null && m.CanAttack && m.CurrentAtk > 0)
                .ToList();
            if (attackers.Count == 0)
                return false;

            int manaNow = GetAvailableManaIncludingCoin(board);
            int friendHp = GetHeroHealth(board.HeroFriend);
            int enemyAttack = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            bool enemyHasBoard = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null);
            bool enemyHasSecret = board.SecretEnemy || board.SecretEnemyCount > 0;
            bool hasAttackableLifesteal = HasAttackableLifestealMinion(board);
            bool lowHpSecretRisk = IsLowHpSecretRisk(friendHp, enemyAttack);
            if (enemyHasSecret && enemyHasBoard && hasAttackableLifesteal && lowHpSecretRisk)
            {
                if (logDecisions)
                    AddLog("攻击顺序：低血且敌方有奥秘，先用吸血随从攻击回血，不后置攻击");
                return false;
            }

            if (friendHp <= 8 && enemyHasBoard)
                return false;
            if (friendHp <= 12 && enemyAttack >= 8)
                return false;

            bool enemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            int canAttackDamageNow = GetAttackableBoardAttack(board.MinionFriend);
            if (!enemyHasTaunt && canAttackDamageNow >= enemyHp)
                return false;

            bool rayAttackDelayWindow = IsRayAttackDelayWindow(board);
            bool entropyPlayableNow = IsEntropyAttackDelayWindowForRules(
                board,
                manaNow,
                friendHp,
                enemyAttack,
                enemyHasSecret,
                enemyHasBoard,
                hasAttackableLifesteal,
                lowHpSecretRisk,
                rayAttackDelayWindow,
                ShouldForceTombFirstThisTurn(board));

            bool temporaryRayNagaBuffWindowNow = IsTemporaryRaySlitherspearAttackHoldWindow(board, manaNow);

            bool sketchToRayBuffWindow = IsSketchToRayAttackDelayWindow(board, manaNow);
            bool monthlyLifestealWindow = IsMonthlyLifestealAttackDelayWindow(board, manaNow);
            bool bananaBuffWindow = IsBananaAttackDelayWindow(board, manaNow);
            bool tombBuffScoutWindow = IsTombBuffScoutAttackDelayWindow(board, manaNow, enemyHp);
            bool zeroCostPlayableBeforeAttack = IsZeroCostNonRayAttackDelayWindow(board, manaNow);

            bool shouldDelay = entropyPlayableNow
                               || rayAttackDelayWindow
                               || sketchToRayBuffWindow
                               || monthlyLifestealWindow
                               || bananaBuffWindow
                               || tombBuffScoutWindow
                               || zeroCostPlayableBeforeAttack;
            if (!shouldDelay)
                return false;

            bool emptyBoardBuffChainWindow = ShouldKeepAttackDelayOnEmptyBoard(board, manaNow);
            if (ShouldCancelBuffAttackDelayOnEmptyBoard(
                board,
                enemyHp,
                canAttackDamageNow,
                enemyHasTaunt,
                temporaryRayNagaBuffWindowNow,
                emptyBoardBuffChainWindow,
                logDecisions))
                return false;

            return true;
        }

        private bool ApplyLocalRulePlayCard(ProfileParameters p, Board board, DecisionRuleMatchResult match)
        {
            if (p == null || board == null || match == null || match.Rule == null || match.Rule.then == null)
                return false;

            DecisionRuleAction action = match.Rule.then;
            Card card = ResolveRuleHandCard(board, action.card_id, action.slot);
            if (card == null || card.Template == null)
                return false;

            bool expectsTarget = !string.IsNullOrWhiteSpace(action.target_kind);
            Card target = ResolveRuleTarget(board, action.target_kind, action.target_selector, action.target_card_id, action.target_slot);
            if (expectsTarget && target == null)
                return false;
            if (target == null)
            {
                DecisionTeacherHintMatch hint = new DecisionTeacherHintMatch();
                hint.Kind = "card";
                hint.CardId = card.Template.Id;
                hint.Slot = action.slot.GetValueOrDefault(0);
                ForceBoxOcrPrimaryCard(p, board, card, hint);
            }
            else
            {
                ApplyRuleCardBias(p, card, target);
            }

            AddLog("[RuleJSON] " + SafeRuleId(match) + " -> PlayCard " + card.Template.Id
                + (target != null && target.Template != null ? " target=" + target.Template.Id : string.Empty));
            return true;
        }

        private bool ApplyLocalRuleHeroPower(ProfileParameters p, Board board, DecisionRuleMatchResult match)
        {
            if (p == null || board == null || board.Ability == null || board.Ability.Template == null)
                return false;
            if (board.Ability.Template.Id == LifeTap && !CanUseLifeTapNow(board))
                return false;

            DecisionTeacherHintMatch hint = new DecisionTeacherHintMatch();
            hint.Kind = "hero_power";
            hint.CardId = board.Ability.Template.Id;
            hint.Slot = 0;
            ForceBoxOcrPrimaryHeroPower(p, board, hint);
            AddLog("[RuleJSON] " + SafeRuleId(match) + " -> UseHeroPower " + board.Ability.Template.Id);
            return true;
        }

        private bool ApplyLocalRuleAttack(ProfileParameters p, Board board, DecisionRuleMatchResult match, out bool blockLocalPrimaryRules)
        {
            blockLocalPrimaryRules = false;
            if (p == null || board == null || match == null || match.Rule == null || match.Rule.then == null)
                return false;

            DecisionRuleAction action = match.Rule.then;
            string attackPlan = (action.attack_plan ?? string.Empty).Trim();
            bool delayNonAttackActions = action.delay_non_attack_actions.GetValueOrDefault(false);
            if (string.Equals(attackPlan, "all_face", StringComparison.OrdinalIgnoreCase))
            {
                if (!CanApplyLocalRuleAllFaceAttack(board))
                    return false;

                if (delayNonAttackActions)
                {
                    DelayBoxOcrNonAttackActions(p, board);
                    blockLocalPrimaryRules = true;
                }

                ApplyLocalRuleAllFaceAttackBias(p, board);
                AddLog("[RuleJSON] " + SafeRuleId(match) + " -> AttackPlan all_face"
                    + (delayNonAttackActions ? " delay_non_attack_actions" : string.Empty));
                return true;
            }

            if (string.Equals(attackPlan, "hold_attacks_for_play", StringComparison.OrdinalIgnoreCase))
            {
                if (!CanApplyLocalRuleHoldAttacks(board))
                    return false;

                ApplyLocalRuleHoldAttacksBias(p, board);
                AddLog("[RuleJSON] " + SafeRuleId(match) + " -> AttackPlan hold_attacks_for_play");
                return true;
            }

            if (string.Equals(attackPlan, "focus_target_first", StringComparison.OrdinalIgnoreCase))
            {
                bool expectsTargetForPlan = !string.IsNullOrWhiteSpace(action.target_kind);
                DecisionTeacherHintMatch focusTargetMatch = BuildAttackTargetHint(
                    board,
                    action.target_kind,
                    action.target_selector,
                    action.target_card_id,
                    action.target_slot);
                if (expectsTargetForPlan && focusTargetMatch == null)
                    return false;

                Card focusTarget = ResolveBoxOcrEnemyMinion(board, focusTargetMatch);
                if (!CanApplyLocalRuleFocusTargetAttack(board, focusTarget))
                    return false;

                if (delayNonAttackActions)
                {
                    DelayBoxOcrNonAttackActions(p, board);
                    blockLocalPrimaryRules = true;
                }

                Card preferredSource = ResolveRuleFriendlyAttackSourceForTarget(board, focusTarget, action.source_selector);
                ApplyLocalRuleFocusTargetAttackBias(p, board, preferredSource, focusTarget);
                AddLog("[RuleJSON] " + SafeRuleId(match) + " -> AttackPlan focus_target_first"
                    + " target=" + focusTarget.Template.Id
                    + (preferredSource != null && preferredSource.Template != null ? " source=" + preferredSource.Template.Id : string.Empty)
                    + (delayNonAttackActions ? " delay_non_attack_actions" : string.Empty));
                return true;
            }

            DecisionTeacherHintMatch source = BuildAttackSourceHint(board, action.source_card_id, action.source_selector, action.source_slot);
            if (source == null)
                return false;

            bool expectsTarget = !string.IsNullOrWhiteSpace(action.target_kind);
            DecisionTeacherHintMatch target = BuildAttackTargetHint(board, action.target_kind, action.target_selector, action.target_card_id, action.target_slot);
            if (expectsTarget && target == null)
                return false;

            Card sourceCard = ResolveBoxOcrFriendlyMinion(board, source);
            if (sourceCard == null || sourceCard.Template == null || !sourceCard.CanAttack || sourceCard.CurrentAtk <= 0)
                return false;

            if (delayNonAttackActions)
            {
                DelayBoxOcrNonAttackActions(p, board);
                blockLocalPrimaryRules = true;
            }

            ApplyLocalRuleAttackBias(p, board, sourceCard, target);
            AddLog("[RuleJSON] " + SafeRuleId(match) + " -> Attack " + source.CardId
                + (delayNonAttackActions ? " delay_non_attack_actions" : string.Empty)
                + (target != null ? " target=" + target.Kind + ":" + target.CardId : string.Empty));
            return true;
        }

        private bool CanApplyLocalRuleAllFaceAttack(Board board)
        {
            if (board == null || board.MinionFriend == null)
                return false;

            if (board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt))
                return false;

            return board.MinionFriend.Any(m =>
                m != null
                && m.Template != null
                && m.CanAttack
                && m.CurrentAtk > 0);
        }

        private bool CanApplyLocalRuleHoldAttacks(Board board)
        {
            if (board == null || board.MinionFriend == null)
                return false;

            return board.MinionFriend.Any(m =>
                m != null
                && m.Template != null
                && m.CanAttack
                && m.CurrentAtk > 0);
        }

        private bool CanApplyLocalRuleFocusTargetAttack(Board board, Card focusTarget)
        {
            if (board == null || focusTarget == null || focusTarget.Template == null || board.MinionFriend == null)
                return false;

            if (board.MinionEnemy != null
                && board.MinionEnemy.Any(m => m != null && m.IsTaunt)
                && !focusTarget.IsTaunt)
            {
                return false;
            }

            return board.MinionFriend.Any(m =>
                m != null
                && m.Template != null
                && m.CanAttack
                && m.CurrentAtk > 0);
        }

        private Card ResolveRuleHandCard(Board board, string rawCardId, int? slot)
        {
            if (board == null || board.Hand == null)
                return null;

            Card.Cards cardId;
            if (!TryParseRuleCardId(rawCardId, out cardId))
                return null;

            if (slot.HasValue && slot.Value > 0 && slot.Value <= board.Hand.Count)
            {
                Card bySlot = board.Hand[slot.Value - 1];
                if (bySlot != null && bySlot.Template != null && bySlot.Template.Id == cardId && IsRuleCardPlayableNow(board, bySlot))
                    return bySlot;
            }

            return board.Hand.FirstOrDefault(card =>
                card != null
                && card.Template != null
                && card.Template.Id == cardId
                && IsRuleCardPlayableNow(board, card));
        }

        private bool IsRuleCardPlayableNow(Board board, Card card)
        {
            if (board == null || card == null || card.Template == null)
                return false;

            if (card.CurrentCost > GetAvailableManaIncludingCoin(board))
                return false;

            if (card.Type == Card.CType.MINION && GetFreeBoardSlots(board) <= 0)
                return false;

            return true;
        }

        private Card ResolveRuleTarget(Board board, string targetKind, string targetSelector, string targetCardId, int? targetSlot)
        {
            if (board == null || string.IsNullOrWhiteSpace(targetKind))
                return null;

            if (string.Equals(targetKind, "enemy_hero", StringComparison.OrdinalIgnoreCase))
                return board.HeroEnemy;

            if (!string.Equals(targetKind, "enemy_minion", StringComparison.OrdinalIgnoreCase))
                return null;

            if (board.MinionEnemy == null)
                return null;

            Card selectedBySelector = ResolveRuleEnemyMinionSelector(board, targetSelector);
            if (selectedBySelector != null)
                return selectedBySelector;

            Card.Cards targetId;
            bool hasTargetId = TryParseRuleCardId(targetCardId, out targetId);
            if (targetSlot.HasValue && targetSlot.Value > 0 && targetSlot.Value <= board.MinionEnemy.Count)
            {
                Card bySlot = board.MinionEnemy[targetSlot.Value - 1];
                if (bySlot != null && bySlot.Template != null && (!hasTargetId || bySlot.Template.Id == targetId))
                    return bySlot;
            }

            if (!hasTargetId)
                return null;

            return board.MinionEnemy.FirstOrDefault(card =>
                card != null
                && card.Template != null
                && card.Template.Id == targetId);
        }

        private Card ResolveRuleEnemyMinionSelector(Board board, string targetSelector)
        {
            if (board == null || board.MinionEnemy == null || string.IsNullOrWhiteSpace(targetSelector))
                return null;

            IEnumerable<Card> candidates = board.MinionEnemy.Where(card => card != null && card.Template != null);
            if (string.Equals(targetSelector, "enemy_minion_highest_attack_non_frozen", StringComparison.OrdinalIgnoreCase))
            {
                candidates = candidates.Where(card => !IsCardFrozenByTag(card));
            }
            else if (string.Equals(targetSelector, "enemy_minion_highest_attack_taunt_first", StringComparison.OrdinalIgnoreCase))
            {
                if (candidates.Any(card => card.IsTaunt))
                    candidates = candidates.Where(card => card.IsTaunt);
            }
            else if (string.Equals(targetSelector, "enemy_minion_highest_attack_non_frozen_non_taunt", StringComparison.OrdinalIgnoreCase))
            {
                candidates = candidates.Where(card => !IsCardFrozenByTag(card) && !card.IsTaunt);
            }
            else if (!string.Equals(targetSelector, "enemy_minion_highest_attack", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return candidates
                .Select((card, index) => new { Card = card, Index = index })
                .OrderByDescending(x => Math.Max(0, x.Card.CurrentAtk))
                .ThenByDescending(x => Math.Max(0, x.Card.CurrentHealth))
                .ThenBy(x => x.Index)
                .Select(x => x.Card)
                .FirstOrDefault();
        }

        private void ApplyRuleCardBias(ProfileParameters p, Card card, Card target)
        {
            if (p == null || card == null || card.Template == null || target == null)
                return;

            int targetId = target.Id;
            if (card.Type == Card.CType.MINION)
            {
                p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(-9800, targetId));
                p.CastMinionsModifiers.AddOrUpdate(card.Id, new Modifier(-9800, targetId));
            }
            else if (card.Type == Card.CType.SPELL)
            {
                p.CastSpellsModifiers.AddOrUpdate(card.Template.Id, new Modifier(-9800, targetId));
                p.CastSpellsModifiers.AddOrUpdate(card.Id, new Modifier(-9800, targetId));
            }
            else if (card.Type == Card.CType.WEAPON)
            {
                p.CastWeaponsModifiers.AddOrUpdate(card.Template.Id, new Modifier(-9800, targetId));
                p.CastWeaponsModifiers.AddOrUpdate(card.Id, new Modifier(-9800, targetId));
            }

            p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(9999));
            p.PlayOrderModifiers.AddOrUpdate(card.Id, new Modifier(9999));
        }

        private void ApplyLocalRuleAttackBias(ProfileParameters p, Board board, Card sourceCard, DecisionTeacherHintMatch targetMatch)
        {
            if (p == null || board == null || sourceCard == null || sourceCard.Template == null)
                return;

            if (board.MinionFriend != null)
            {
                foreach (Card friend in board.MinionFriend.Where(m => m != null && m.Template != null && m.CanAttack))
                {
                    if (friend.Id == sourceCard.Id)
                        continue;

                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(friend.Template.Id, new Modifier(4200));
                    p.AttackOrderModifiers.AddOrUpdate(friend.Template.Id, new Modifier(9999));
                }
            }

            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(sourceCard.Template.Id, new Modifier(-2600));
            p.AttackOrderModifiers.AddOrUpdate(sourceCard.Template.Id, new Modifier(-9999));

            Card enemyTarget = ResolveBoxOcrEnemyMinion(board, targetMatch);
            bool faceAttack = targetMatch != null
                && string.Equals(targetMatch.Kind, "enemy_hero", StringComparison.OrdinalIgnoreCase);

            if (enemyTarget != null && enemyTarget.Template != null)
            {
                ApplyExactAttackModifierCompat(p, sourceCard, enemyTarget, 5000);
                p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemyTarget.Template.Id, new Modifier(9800));
                return;
            }

            if (faceAttack && (board.MinionEnemy == null || !board.MinionEnemy.Any(m => m != null && m.IsTaunt)))
            {
                if (board.MinionEnemy != null)
                {
                    foreach (Card enemy in board.MinionEnemy.Where(m => m != null && m.Template != null))
                        p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(-9999));
                }

                p.GlobalAggroModifier = Math.Max(p.GlobalAggroModifier.Value, 300);
            }
        }

        private void ApplyLocalRuleAllFaceAttackBias(ProfileParameters p, Board board)
        {
            if (p == null || board == null)
                return;

            if (board.MinionEnemy != null)
            {
                foreach (Card enemy in board.MinionEnemy.Where(m => m != null && m.Template != null))
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(-9999));
            }

            if (board.MinionFriend != null)
            {
                foreach (Card friend in board.MinionFriend.Where(m => m != null && m.Template != null && m.CanAttack && m.CurrentAtk > 0))
                {
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(friend.Template.Id, new Modifier(9999));
                    p.AttackOrderModifiers.AddOrUpdate(friend.Template.Id, new Modifier(-9999));
                }
            }

            p.GlobalAggroModifier = Math.Max(p.GlobalAggroModifier.Value, 300);
        }

        private void ApplyLocalRuleHoldAttacksBias(ProfileParameters p, Board board, bool applyAggroModifier = true)
        {
            if (p == null || board == null)
                return;

            if (board.MinionEnemy != null)
            {
                foreach (Card enemy in board.MinionEnemy.Where(m => m != null && m.Template != null))
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(-2200));
            }

            if (board.MinionFriend != null)
            {
                foreach (Card friend in board.MinionFriend.Where(m => m != null && m.Template != null && m.CanAttack && m.CurrentAtk > 0))
                {
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(friend.Template.Id, new Modifier(9999));
                    p.AttackOrderModifiers.AddOrUpdate(friend.Template.Id, new Modifier(9999));
                }
            }

            if (applyAggroModifier)
                p.GlobalAggroModifier = Math.Min(p.GlobalAggroModifier.Value, -2200);
        }

        private void ApplyLocalRuleFocusTargetAttackBias(ProfileParameters p, Board board, Card preferredSource, Card focusTarget)
        {
            if (p == null || board == null || focusTarget == null || focusTarget.Template == null)
                return;

            if (board.MinionEnemy != null)
            {
                foreach (Card enemy in board.MinionEnemy.Where(m => m != null && m.Template != null))
                {
                    if (enemy.Id == focusTarget.Id)
                    {
                        p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(9800));
                    }
                    else
                    {
                        p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(-320));
                    }
                }
            }

            if (preferredSource == null || preferredSource.Template == null || board.MinionFriend == null)
                return;

            foreach (Card friend in board.MinionFriend.Where(m => m != null && m.Template != null && m.CanAttack && m.CurrentAtk > 0))
            {
                if (friend.Id == preferredSource.Id)
                {
                    ApplyExactAttackModifierCompat(p, friend, focusTarget, 5000);
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(friend.Template.Id, new Modifier(-1200));
                    p.AttackOrderModifiers.AddOrUpdate(friend.Template.Id, new Modifier(-9999));
                }
                else
                {
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(friend.Template.Id, new Modifier(320));
                    p.AttackOrderModifiers.AddOrUpdate(friend.Template.Id, new Modifier(1800));
                }
            }
        }

        private DecisionTeacherHintMatch BuildAttackSourceHint(Board board, string rawCardId, string sourceSelector, int? slot)
        {
            if (board == null || board.MinionFriend == null)
                return null;

            Card selectedBySelector = ResolveRuleFriendlyAttackSource(board, sourceSelector);
            if (selectedBySelector != null && selectedBySelector.Template != null)
            {
                int selectedSlot = 0;
                for (int i = 0; i < board.MinionFriend.Count; i++)
                {
                    if (board.MinionFriend[i] != null && board.MinionFriend[i].Id == selectedBySelector.Id)
                    {
                        selectedSlot = i + 1;
                        break;
                    }
                }

                return new DecisionTeacherHintMatch
                {
                    Kind = "friendly_minion",
                    CardId = selectedBySelector.Template.Id,
                    Slot = selectedSlot
                };
            }

            Card.Cards sourceId;
            if (!TryParseRuleCardId(rawCardId, out sourceId))
                return null;

            if (slot.HasValue && slot.Value > 0 && slot.Value <= board.MinionFriend.Count)
            {
                Card bySlot = board.MinionFriend[slot.Value - 1];
                if (bySlot != null && bySlot.Template != null && bySlot.Template.Id == sourceId)
                    return new DecisionTeacherHintMatch { Kind = "friendly_minion", CardId = sourceId, Slot = slot.Value };
            }

            for (int i = 0; i < board.MinionFriend.Count; i++)
            {
                Card minion = board.MinionFriend[i];
                if (minion == null || minion.Template == null || minion.Template.Id != sourceId)
                    continue;
                return new DecisionTeacherHintMatch { Kind = "friendly_minion", CardId = sourceId, Slot = i + 1 };
            }

            return null;
        }

        private Card ResolveRuleFriendlyAttackSource(Board board, string sourceSelector)
        {
            if (board == null || board.MinionFriend == null || string.IsNullOrWhiteSpace(sourceSelector))
                return null;

            IEnumerable<Card> candidates = board.MinionFriend.Where(card =>
                card != null
                && card.Template != null
                && card.CanAttack
                && card.CurrentAtk > 0);

            if (string.Equals(sourceSelector, "friendly_lifesteal_highest_attack_can_attack", StringComparison.OrdinalIgnoreCase))
            {
                return candidates
                    .Where(card => card.IsLifeSteal)
                    .Select((card, index) => new { Card = card, Index = index })
                    .OrderByDescending(x => Math.Max(0, x.Card.CurrentAtk))
                    .ThenByDescending(x => Math.Max(0, x.Card.CurrentHealth))
                    .ThenBy(x => x.Index)
                    .Select(x => x.Card)
                    .FirstOrDefault();
            }

            if (string.Equals(sourceSelector, "friendly_minion_highest_attack_can_attack", StringComparison.OrdinalIgnoreCase))
            {
                return candidates
                    .Select((card, index) => new { Card = card, Index = index })
                    .OrderByDescending(x => Math.Max(0, x.Card.CurrentAtk))
                    .ThenByDescending(x => Math.Max(0, x.Card.CurrentHealth))
                    .ThenBy(x => x.Index)
                    .Select(x => x.Card)
                    .FirstOrDefault();
            }

            if (string.Equals(sourceSelector, "friendly_minion_lowest_attack_can_attack", StringComparison.OrdinalIgnoreCase))
            {
                return candidates
                    .Select((card, index) => new { Card = card, Index = index })
                    .OrderBy(x => Math.Max(0, x.Card.CurrentAtk))
                    .ThenBy(x => Math.Max(0, x.Card.CurrentHealth))
                    .ThenBy(x => x.Index)
                    .Select(x => x.Card)
                    .FirstOrDefault();
            }

            return null;
        }

        private Card ResolveRuleFriendlyAttackSourceForTarget(Board board, Card target, string sourceSelector)
        {
            Card selectedBySelector = ResolveRuleFriendlyAttackSource(board, sourceSelector);
            if (selectedBySelector != null)
                return selectedBySelector;

            if (board == null || board.MinionFriend == null || target == null)
                return null;

            List<Card> candidates = board.MinionFriend
                .Where(card =>
                    card != null
                    && card.Template != null
                    && card.CanAttack
                    && card.CurrentAtk > 0)
                .ToList();
            if (candidates.Count == 0)
                return null;

            int targetHealth = Math.Max(1, target.CurrentHealth);
            if (!target.IsDivineShield)
            {
                Card lethalCandidate = candidates
                    .Select((card, index) => new { Card = card, Index = index })
                    .Where(x => x.Card.CurrentAtk >= targetHealth)
                    .OrderBy(x => Math.Max(0, x.Card.CurrentAtk))
                    .ThenBy(x => Math.Max(0, x.Card.CurrentHealth))
                    .ThenBy(x => x.Index)
                    .Select(x => x.Card)
                    .FirstOrDefault();
                if (lethalCandidate != null)
                    return lethalCandidate;
            }

            return candidates
                .Select((card, index) => new { Card = card, Index = index })
                .OrderByDescending(x => Math.Max(0, x.Card.CurrentAtk))
                .ThenBy(x => x.Index)
                .Select(x => x.Card)
                .FirstOrDefault();
        }

        private DecisionTeacherHintMatch BuildAttackTargetHint(Board board, string targetKind, string targetSelector, string rawCardId, int? slot)
        {
            if (string.IsNullOrWhiteSpace(targetKind))
                return null;

            if (string.Equals(targetKind, "enemy_hero", StringComparison.OrdinalIgnoreCase))
            {
                return new DecisionTeacherHintMatch
                {
                    Kind = "enemy_hero",
                    CardId = board != null && board.HeroEnemy != null && board.HeroEnemy.Template != null
                        ? board.HeroEnemy.Template.Id
                        : default(Card.Cards),
                    Slot = 0
                };
            }

            if (!string.Equals(targetKind, "enemy_minion", StringComparison.OrdinalIgnoreCase) || board == null || board.MinionEnemy == null)
                return null;

            Card selectedBySelector = ResolveRuleEnemyMinionSelector(board, targetSelector);
            if (selectedBySelector != null && selectedBySelector.Template != null)
            {
                int selectedSlot = 0;
                for (int i = 0; i < board.MinionEnemy.Count; i++)
                {
                    if (board.MinionEnemy[i] != null && board.MinionEnemy[i].Id == selectedBySelector.Id)
                    {
                        selectedSlot = i + 1;
                        break;
                    }
                }

                return new DecisionTeacherHintMatch
                {
                    Kind = "enemy_minion",
                    CardId = selectedBySelector.Template.Id,
                    Slot = selectedSlot
                };
            }

            Card.Cards targetId;
            bool hasTargetId = TryParseRuleCardId(rawCardId, out targetId);
            if (slot.HasValue && slot.Value > 0 && slot.Value <= board.MinionEnemy.Count)
            {
                Card bySlot = board.MinionEnemy[slot.Value - 1];
                if (bySlot != null && bySlot.Template != null && (!hasTargetId || bySlot.Template.Id == targetId))
                {
                    return new DecisionTeacherHintMatch { Kind = "enemy_minion", CardId = bySlot.Template.Id, Slot = slot.Value };
                }
            }

            if (!hasTargetId)
                return null;

            for (int i = 0; i < board.MinionEnemy.Count; i++)
            {
                Card minion = board.MinionEnemy[i];
                if (minion == null || minion.Template == null || minion.Template.Id != targetId)
                    continue;
                return new DecisionTeacherHintMatch { Kind = "enemy_minion", CardId = targetId, Slot = i + 1 };
            }

            return null;
        }

        private bool TryParseRuleCardId(string raw, out Card.Cards value)
        {
            try
            {
                value = (Card.Cards)Enum.Parse(typeof(Card.Cards), raw ?? string.Empty, true);
                return true;
            }
            catch
            {
                value = default(Card.Cards);
                return false;
            }
        }

        private string SafeRuleId(DecisionRuleMatchResult match)
        {
            if (match == null || match.Rule == null || string.IsNullOrWhiteSpace(match.Rule.id))
                return "unnamed_rule";
            return match.Rule.id;
        }

        private void ApplyBoxOcrLiveBias(ProfileParameters p, Board board)
        {
            DecisionTeacherHintState state = LoadCurrentBoxOcrLivePlayState();
            if (state == null
                || !state.IsFresh(12)
                || !string.Equals(state.Status, "ok", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(state.Stage, "play", StringComparison.OrdinalIgnoreCase)
                || !state.MatchesProfile("标准动物园术.cs"))
            {
                return;
            }

            DecisionTeacherHintMatch primaryHeroPowerMatch = null;
            DecisionTeacherHintMatch primaryCardMatch = null;
            DecisionTeacherHintMatch primaryAttackSourceMatch = null;
            DecisionTeacherHintMatch primaryAttackTargetMatch = null;
            Card primaryCard = null;

            foreach (DecisionTeacherHintMatch match in state.Matches)
            {
                if (string.Equals(match.Kind, "friendly_minion", StringComparison.OrdinalIgnoreCase))
                {
                    if (primaryAttackSourceMatch == null)
                        primaryAttackSourceMatch = match;
                    continue;
                }

                if (string.Equals(match.Kind, "enemy_minion", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(match.Kind, "enemy_hero", StringComparison.OrdinalIgnoreCase))
                {
                    if (primaryAttackTargetMatch == null)
                        primaryAttackTargetMatch = match;
                    continue;
                }

                if (string.Equals(match.Kind, "hero_power", StringComparison.OrdinalIgnoreCase))
                {
                    if (board.Ability != null
                        && board.Ability.Template != null
                        && board.Ability.Template.Id == match.CardId)
                    {
                        if (primaryHeroPowerMatch == null)
                            primaryHeroPowerMatch = match;
                    }
                    continue;
                }

                if (!string.Equals(match.Kind, "card", StringComparison.OrdinalIgnoreCase))
                    continue;

                Card target = ResolveBoxOcrHandCard(board, match);
                if (target == null || target.Template == null)
                    continue;

                if (primaryCard == null)
                {
                    primaryCard = target;
                    primaryCardMatch = match;
                }
            }

            if (primaryCard != null)
            {
                ForceBoxOcrPrimaryCard(p, board, primaryCard, primaryCardMatch);
                return;
            }

            if (primaryHeroPowerMatch != null)
            {
                ForceBoxOcrPrimaryHeroPower(p, board, primaryHeroPowerMatch);
                return;
            }

            if (primaryAttackSourceMatch != null)
            {
                ForceBoxOcrPrimaryAttack(p, board, primaryAttackSourceMatch, primaryAttackTargetMatch);
            }
        }

        private void ApplyBoxOcrGeneratedBias(ProfileParameters p, Board board)
        {
            if (p == null || board == null || board.Hand == null)
                return;

            // BOXOCR_PLAY_GENERATED_START
            foreach (Card card in board.Hand)
            {
                if (card == null || card.Template == null)
                    continue;

                switch (card.Template.Id)
                {
                    case Card.Cards.GDB_123:
                        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GDB_123, new Modifier(-2500));
                        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_123, new Modifier(9999));
                        break;
                    case Card.Cards.GDB_121:
                        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_121, new Modifier(-2500));
                        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_121, new Modifier(9999));
                        break;
                    case Card.Cards.CORE_SW_068:
                        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_SW_068, new Modifier(-2500));
                        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_SW_068, new Modifier(9999));
                        break;
                    case Card.Cards.FIR_924:
                        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.FIR_924, new Modifier(-2500));
                        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.FIR_924, new Modifier(9999));
                        break;
                    case Card.Cards.CORE_TSC_827:
                        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_TSC_827, new Modifier(-2500));
                        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_TSC_827, new Modifier(9800));
                        break;
                    case Card.Cards.CORE_ULD_723:
                        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_ULD_723, new Modifier(-2400));
                        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_ULD_723, new Modifier(9300));
                        break;
                    case Card.Cards.TIME_026:
                        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TIME_026, new Modifier(-2400));
                        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TIME_026, new Modifier(9300));
                        break;
                    case Card.Cards.TLC_603:
                        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_603, new Modifier(-2400));
                        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_603, new Modifier(9300));
                        break;
                    case Card.Cards.TOY_916:
                        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TOY_916, new Modifier(-2150));
                        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_916, new Modifier(9050));
                        break;
                    case Card.Cards.CORE_UNG_205:
                        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_UNG_205, new Modifier(-1900));
                        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_UNG_205, new Modifier(8800));
                        break;
                    case Card.Cards.TLC_254:
                        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.TLC_254, new Modifier(-1900));
                        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_254, new Modifier(8800));
                        break;
                    case Card.Cards.TLC_451:
                        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(-1900));
                        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TLC_451, new Modifier(8800));
                        break;
                    case Card.Cards.VAC_940:
                        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.VAC_940, new Modifier(-1900));
                        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.VAC_940, new Modifier(8800));
                        break;
                    case Card.Cards.CORE_EX1_319:
                        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.CORE_EX1_319, new Modifier(-1650));
                        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.CORE_EX1_319, new Modifier(8550));
                        break;
                    case Card.Cards.GAME_005:
                        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.GAME_005, new Modifier(-1650));
                        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GAME_005, new Modifier(8550));
                        break;
                    case Card.Cards.GDB_128:
                        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_128, new Modifier(-1650));
                        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_128, new Modifier(8550));
                        break;
                    case Card.Cards.GDB_145:
                        p.CastMinionsModifiers.AddOrUpdate(Card.Cards.GDB_145, new Modifier(-1650));
                        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.GDB_145, new Modifier(8550));
                        break;
                    case Card.Cards.TOY_377:
                        p.CastSpellsModifiers.AddOrUpdate(Card.Cards.TOY_377, new Modifier(-1650));
                        p.PlayOrderModifiers.AddOrUpdate(Card.Cards.TOY_377, new Modifier(8550));
                        break;
                }
            }

            if (board.MinionFriend != null)
            {
                foreach (Card friend in board.MinionFriend.Where(m => m != null && m.Template != null && m.CanAttack && m.CurrentAtk > 0))
                {
                    switch (friend.Template.Id)
                    {
                        case Card.Cards.CORE_SW_068:
                            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.CORE_SW_068, new Modifier(-1300));
                            p.AttackOrderModifiers.AddOrUpdate(Card.Cards.CORE_SW_068, new Modifier(-2560));
                            break;
                        case Card.Cards.CORE_ULD_723:
                            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.CORE_ULD_723, new Modifier(-760));
                            p.AttackOrderModifiers.AddOrUpdate(Card.Cards.CORE_ULD_723, new Modifier(-1780));
                            break;
                        case Card.Cards.TOY_916:
                            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.TOY_916, new Modifier(-760));
                            p.AttackOrderModifiers.AddOrUpdate(Card.Cards.TOY_916, new Modifier(-1780));
                            break;
                        case Card.Cards.WORK_009:
                            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.WORK_009, new Modifier(-760));
                            p.AttackOrderModifiers.AddOrUpdate(Card.Cards.WORK_009, new Modifier(-1780));
                            break;
                        case Card.Cards.CORE_CS2_231:
                            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.CORE_CS2_231, new Modifier(-580));
                            p.AttackOrderModifiers.AddOrUpdate(Card.Cards.CORE_CS2_231, new Modifier(-1520));
                            break;
                        case Card.Cards.TOY_914:
                            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.TOY_914, new Modifier(-580));
                            p.AttackOrderModifiers.AddOrUpdate(Card.Cards.TOY_914, new Modifier(-1520));
                            break;
                        case Card.Cards.CORE_TSC_827:
                            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.CORE_TSC_827, new Modifier(-400));
                            p.AttackOrderModifiers.AddOrUpdate(Card.Cards.CORE_TSC_827, new Modifier(-1260));
                            break;
                        case Card.Cards.GDB_121:
                            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.GDB_121, new Modifier(-400));
                            p.AttackOrderModifiers.AddOrUpdate(Card.Cards.GDB_121, new Modifier(-1260));
                            break;
                        case Card.Cards.TLC_603:
                            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.TLC_603, new Modifier(-400));
                            p.AttackOrderModifiers.AddOrUpdate(Card.Cards.TLC_603, new Modifier(-1260));
                            break;
                        case Card.Cards.VAC_927:
                            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.VAC_927, new Modifier(-400));
                            p.AttackOrderModifiers.AddOrUpdate(Card.Cards.VAC_927, new Modifier(-1260));
                            break;
                        case Card.Cards.VAC_940:
                            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.VAC_940, new Modifier(-400));
                            p.AttackOrderModifiers.AddOrUpdate(Card.Cards.VAC_940, new Modifier(-1260));
                            break;
                        case Card.Cards.VAC_940t:
                            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.VAC_940t, new Modifier(-400));
                            p.AttackOrderModifiers.AddOrUpdate(Card.Cards.VAC_940t, new Modifier(-1260));
                            break;
                    }
                }
            }

            if (board.MinionEnemy != null)
            {
                foreach (Card enemy in board.MinionEnemy.Where(m => m != null && m.Template != null))
                {
                    switch (enemy.Template.Id)
                    {
                        case Card.Cards.CORE_EX1_559:
                            p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.CORE_EX1_559, new Modifier(880));
                            break;
                        case Card.Cards.CORE_UNG_928:
                            p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.CORE_UNG_928, new Modifier(880));
                            break;
                        case Card.Cards.EDR_489:
                            p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.EDR_489, new Modifier(880));
                            break;
                        case Card.Cards.CORE_EX1_012:
                            p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.CORE_EX1_012, new Modifier(620));
                            break;
                        case Card.Cards.CORE_EX1_250:
                            p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.CORE_EX1_250, new Modifier(620));
                            break;
                        case Card.Cards.CORE_NEW1_022:
                            p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.CORE_NEW1_022, new Modifier(620));
                            break;
                        case Card.Cards.CORE_TSC_827:
                            p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.CORE_TSC_827, new Modifier(620));
                            break;
                        case Card.Cards.CS2_mirror:
                            p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.CS2_mirror, new Modifier(620));
                            break;
                        case Card.Cards.TLC_819:
                            p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.TLC_819, new Modifier(620));
                            break;
                        case Card.Cards.TOY_006:
                            p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.TOY_006, new Modifier(620));
                            break;
                        case Card.Cards.VAC_332:
                            p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.VAC_332, new Modifier(620));
                            break;
                    }
                }
            }

            p.GlobalAggroModifier = Math.Max(p.GlobalAggroModifier.Value, 320);
// BOXOCR_PLAY_GENERATED_END
        }

        private void ApplyBoxOcrLiveCardBias(ProfileParameters p, Card card, int castValue, int orderValue)
        {
            if (p == null || card == null || card.Template == null)
                return;

            if (card.Type == Card.CType.MINION)
            {
                p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(castValue));
                p.CastMinionsModifiers.AddOrUpdate(card.Id, new Modifier(castValue));
            }
            else if (card.Type == Card.CType.SPELL)
            {
                p.CastSpellsModifiers.AddOrUpdate(card.Template.Id, new Modifier(castValue));
                p.CastSpellsModifiers.AddOrUpdate(card.Id, new Modifier(castValue));
            }
            else if (card.Type == Card.CType.WEAPON)
            {
                p.CastWeaponsModifiers.AddOrUpdate(card.Template.Id, new Modifier(castValue));
                p.CastWeaponsModifiers.AddOrUpdate(card.Id, new Modifier(castValue));
            }

            p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(orderValue));
            p.PlayOrderModifiers.AddOrUpdate(card.Id, new Modifier(orderValue));
        }

        private void ForceBoxOcrPrimaryCard(ProfileParameters p, Board board, Card target, DecisionTeacherHintMatch match)
        {
            if (p == null || board == null || target == null || target.Template == null)
                return;

            bool comboForced = SetSingleCardComboByEntityId(
                board,
                p,
                target.Id,
                allowCoinBridge: true,
                forceOverride: true,
                logWhenSet: "[BoxOCR] ComboSet -> " + target.Template.Id);

            if (!comboForced)
            {
                ApplyBoxOcrLiveCardBias(p, target, -3600, 9800);
                AddLog("[BoxOCR] live card soft fallback -> " + target.Template.Id + " slot=" + (match != null ? match.Slot : 0));
                return;
            }

            ApplyBoxOcrLiveCardBias(p, target, -9800, 9999);
            SuppressBoxOcrCompetingCards(p, board, target);
            SuppressBoxOcrHeroPower(p, board);
            AddLog("[BoxOCR] live card -> " + target.Template.Id + " slot=" + (match != null ? match.Slot : 0));
        }

        private void ForceBoxOcrPrimaryHeroPower(ProfileParameters p, Board board, DecisionTeacherHintMatch match)
        {
            if (p == null || board == null || board.Ability == null || board.Ability.Template == null)
                return;

            p.CastHeroPowerModifier.AddOrUpdate(board.Ability.Template.Id, new Modifier(-9800));
            p.PlayOrderModifiers.AddOrUpdate(board.Ability.Template.Id, new Modifier(9999));

            if (board.Hand != null)
            {
                int manaNow = GetAvailableManaIncludingCoin(board);
                foreach (Card card in board.Hand.Where(c => c != null && c.Template != null && c.CurrentCost <= manaNow))
                    DelayBoxOcrCompetingCard(p, card, false);
            }

            AddLog("[BoxOCR] live hero power -> " + (match != null ? match.CardId.ToString() : board.Ability.Template.Id.ToString()));
        }

        private void SuppressBoxOcrCompetingCards(ProfileParameters p, Board board, Card preferredCard)
        {
            if (p == null || board == null || board.Hand == null || preferredCard == null || preferredCard.Template == null)
                return;

            int realMana = Math.Max(0, board.ManaAvailable);
            int manaNow = GetAvailableManaIncludingCoin(board);
            bool needsCoinBridge = preferredCard.CurrentCost > realMana;

            foreach (Card card in board.Hand.Where(c => c != null && c.Template != null && c.Id != preferredCard.Id && c.CurrentCost <= manaNow))
            {
                if (needsCoinBridge && card.Template.Id == TheCoin)
                    continue;

                bool sameTemplate = card.Template.Id == preferredCard.Template.Id;
                DelayBoxOcrCompetingCard(p, card, sameTemplate);
            }
        }

        private void DelayBoxOcrCompetingCard(ProfileParameters p, Card card, bool sameTemplateAsPreferred)
        {
            if (p == null || card == null || card.Template == null)
                return;

            int castDelay = sameTemplateAsPreferred ? 6200 : 9800;
            int orderDelay = -9999;

            if (card.Type == Card.CType.MINION)
            {
                if (!sameTemplateAsPreferred)
                    p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(castDelay));
                p.CastMinionsModifiers.AddOrUpdate(card.Id, new Modifier(castDelay));
            }
            else if (card.Type == Card.CType.SPELL)
            {
                if (!sameTemplateAsPreferred)
                    p.CastSpellsModifiers.AddOrUpdate(card.Template.Id, new Modifier(castDelay));
                p.CastSpellsModifiers.AddOrUpdate(card.Id, new Modifier(castDelay));
            }
            else if (card.Type == Card.CType.WEAPON)
            {
                if (!sameTemplateAsPreferred)
                    p.CastWeaponsModifiers.AddOrUpdate(card.Template.Id, new Modifier(castDelay));
                p.CastWeaponsModifiers.AddOrUpdate(card.Id, new Modifier(castDelay));
            }

            if (!sameTemplateAsPreferred)
                p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(orderDelay));
            p.PlayOrderModifiers.AddOrUpdate(card.Id, new Modifier(orderDelay));
        }

        private void SuppressBoxOcrHeroPower(ProfileParameters p, Board board)
        {
            if (p == null || board == null || board.Ability == null || board.Ability.Template == null)
                return;

            p.CastHeroPowerModifier.AddOrUpdate(board.Ability.Template.Id, new Modifier(9999));
            p.PlayOrderModifiers.AddOrUpdate(board.Ability.Template.Id, new Modifier(-9999));
        }

        private void ForceBoxOcrPrimaryAttack(ProfileParameters p, Board board, DecisionTeacherHintMatch sourceMatch, DecisionTeacherHintMatch targetMatch)
        {
            Card source = ResolveBoxOcrFriendlyMinion(board, sourceMatch);
            if (p == null || board == null || source == null || source.Template == null || !source.CanAttack || source.CurrentAtk <= 0)
                return;

            DelayBoxOcrNonAttackActions(p, board);

            if (board.MinionFriend != null)
            {
                foreach (Card friend in board.MinionFriend.Where(m => m != null && m.Template != null && m.CanAttack))
                {
                    if (friend.Template.Id == source.Template.Id)
                        continue;

                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(friend.Template.Id, new Modifier(4200));
                    p.AttackOrderModifiers.AddOrUpdate(friend.Template.Id, new Modifier(9999));
                }
            }

            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(source.Template.Id, new Modifier(-2600));
            p.AttackOrderModifiers.AddOrUpdate(source.Template.Id, new Modifier(-9999));

            Card enemyTarget = ResolveBoxOcrEnemyMinion(board, targetMatch);
            bool faceAttack = targetMatch != null
                && string.Equals(targetMatch.Kind, "enemy_hero", StringComparison.OrdinalIgnoreCase);

            if (enemyTarget != null && enemyTarget.Template != null)
            {
                ApplyExactAttackModifierCompat(p, source, enemyTarget, 5000);
                p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemyTarget.Template.Id, new Modifier(9800));
                AddLog("[BoxOCR] live attack -> " + source.Template.Id + " -> " + enemyTarget.Template.Id
                    + " slot=" + (sourceMatch != null ? sourceMatch.Slot : 0)
                    + "/" + (targetMatch != null ? targetMatch.Slot : 0));
                return;
            }

            if (faceAttack && (board.MinionEnemy == null || !board.MinionEnemy.Any(m => m != null && m.IsTaunt)))
            {
                if (board.MinionEnemy != null)
                {
                    foreach (Card enemy in board.MinionEnemy.Where(m => m != null && m.Template != null))
                        p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(-9999));
                }

                p.GlobalAggroModifier = Math.Max(p.GlobalAggroModifier.Value, 300);
                AddLog("[BoxOCR] live attack -> " + source.Template.Id + " -> enemy_hero");
                return;
            }

            AddLog("[BoxOCR] live attack soft fallback -> " + source.Template.Id + " slot=" + (sourceMatch != null ? sourceMatch.Slot : 0));
        }

        private void DelayBoxOcrNonAttackActions(ProfileParameters p, Board board)
        {
            if (p == null || board == null)
                return;

            if (board.Hand != null)
            {
                int manaNow = GetAvailableManaIncludingCoin(board);
                foreach (Card card in board.Hand.Where(c => c != null && c.Template != null && c.CurrentCost <= manaNow))
                    DelayBoxOcrCompetingCard(p, card, false);
            }

            if (board.Ability != null && board.Ability.Template != null)
            {
                p.CastHeroPowerModifier.AddOrUpdate(board.Ability.Template.Id, new Modifier(6200));
                p.PlayOrderModifiers.AddOrUpdate(board.Ability.Template.Id, new Modifier(-9999));
            }
        }

        private Card ResolveBoxOcrHandCard(Board board, DecisionTeacherHintMatch match)
        {
            if (board == null || board.Hand == null || match == null)
                return null;

            if (match.Slot > 0 && match.Slot <= board.Hand.Count)
            {
                Card bySlot = board.Hand[match.Slot - 1];
                if (bySlot != null && bySlot.Template != null && bySlot.Template.Id == match.CardId)
                    return bySlot;
            }

            return board.Hand.FirstOrDefault(card =>
                card != null
                && card.Template != null
                && card.Template.Id == match.CardId);
        }

        private Card ResolveBoxOcrFriendlyMinion(Board board, DecisionTeacherHintMatch match)
        {
            if (board == null || board.MinionFriend == null || match == null)
                return null;

            if (match.Slot > 0 && match.Slot <= board.MinionFriend.Count)
            {
                Card bySlot = board.MinionFriend[match.Slot - 1];
                if (bySlot != null && bySlot.Template != null && bySlot.Template.Id == match.CardId)
                    return bySlot;
            }

            return board.MinionFriend.FirstOrDefault(minion =>
                minion != null
                && minion.Template != null
                && minion.Template.Id == match.CardId
                && minion.CanAttack);
        }

        private Card ResolveBoxOcrEnemyMinion(Board board, DecisionTeacherHintMatch match)
        {
            if (board == null || board.MinionEnemy == null || match == null)
                return null;

            if (!string.Equals(match.Kind, "enemy_minion", StringComparison.OrdinalIgnoreCase))
                return null;

            if (match.Slot > 0 && match.Slot <= board.MinionEnemy.Count)
            {
                Card bySlot = board.MinionEnemy[match.Slot - 1];
                if (bySlot != null && bySlot.Template != null && bySlot.Template.Id == match.CardId)
                    return bySlot;
            }

            return board.MinionEnemy.FirstOrDefault(minion =>
                minion != null
                && minion.Template != null
                && minion.Template.Id == match.CardId);
        }

        private DecisionTeacherHintState LoadCurrentBoxOcrLivePlayState()
        {
            return DecisionStateExtractor.LoadTeacherHint();
        }

        // 最终兜底：对已知会导致“无效重复施放”的牌做硬禁用（按实体ID覆盖）。
        private void HandleHardDisabledCards(Board board, ProfileParameters p)
        {
            if (board == null || board.Hand == null || p == null)
                return;

            bool disabledCreationStar = false;
            bool disabledUnlicensedApothecary = false;
            bool disabledStarDestroyerByFullHand = false;
            bool handFull = board.Hand.Count >= HearthstoneHandLimit;
            foreach (var card in board.Hand.Where(c => c != null && c.Template != null))
            {
                string cardId = card.Template.Id.ToString();
                bool isStarDestroyerAndHandFull = handFull
                    && string.Equals(cardId, StarDestroyerCardId, StringComparison.Ordinal);
                bool disableThisCard = string.Equals(cardId, CreationStarCardId, StringComparison.Ordinal)
                    || string.Equals(cardId, UnlicensedApothecaryCardId, StringComparison.Ordinal)
                    || isStarDestroyerAndHandFull;
                if (!disableThisCard)
                    continue;

                if (card.Type == Card.CType.MINION)
                {
                    p.CastMinionsModifiers.AddOrUpdate(card.Id, new Modifier(9999));
                    p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(9999));
                }
                else if (card.Type == Card.CType.WEAPON)
                {
                    p.CastWeaponsModifiers.AddOrUpdate(card.Id, new Modifier(9999));
                    p.CastWeaponsModifiers.AddOrUpdate(card.Template.Id, new Modifier(9999));
                }
                else
                {
                    p.CastSpellsModifiers.AddOrUpdate(card.Id, new Modifier(9999));
                    p.CastSpellsModifiers.AddOrUpdate(card.Template.Id, new Modifier(9999));
                }

                p.PlayOrderModifiers.AddOrUpdate(card.Id, new Modifier(-9999));
                p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(-9999));
                if (string.Equals(cardId, CreationStarCardId, StringComparison.Ordinal))
                    disabledCreationStar = true;
                if (string.Equals(cardId, UnlicensedApothecaryCardId, StringComparison.Ordinal))
                    disabledUnlicensedApothecary = true;
                if (isStarDestroyerAndHandFull)
                    disabledStarDestroyerByFullHand = true;
            }

            if (disabledCreationStar)
                AddLog("创生之星：硬规则禁用，防止无效重复施放");
            if (disabledUnlicensedApothecary)
                AddLog("无证药剂师：硬规则禁用，不主动使用");
            if (disabledStarDestroyerByFullHand)
                AddLog("星辰毁灭者：手牌已满(10)，硬规则禁用避免爆牌浪费");

            // 用户规则：凶恶的入侵者在场时，法术迸发会对“其他随从”打2，
            // 若己方已铺场，继续施法（如射线/续连/墓）常导致己方场面被反噬清空。
            // 该规则作为最终兜底，在本回合统一禁用主动施法。
            if (ShouldDisableSpellCastingForViciousInvaderBoard(board))
            {
                bool disabledAnySpell = false;
                int manaNow = GetAvailableManaIncludingCoin(board);
                foreach (var card in board.Hand.Where(c => c != null && c.Template != null
                    && c.Type == Card.CType.SPELL
                    && c.CurrentCost <= manaNow))
                {
                    p.CastSpellsModifiers.AddOrUpdate(card.Template.Id, new Modifier(9999));
                    p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(-9999));
                    disabledAnySpell = true;
                }

                if (disabledAnySpell)
                    AddLog("凶恶的入侵者：法术迸发保场窗口，硬规则禁用本回合主动施法");
            }
        }

        private bool ShouldDisableSpellCastingForViciousInvaderBoard(Board board)
        {
            if (board == null || board.MinionFriend == null || board.MinionFriend.Count == 0)
                return false;

            const string viciousInvaderId = "GDB_226";
            bool hasInvaderOnBoard = board.MinionFriend.Any(m => m != null
                && m.Template != null
                && string.Equals(m.Template.Id.ToString(), viciousInvaderId, StringComparison.Ordinal));
            if (!hasInvaderOnBoard)
                return false;

            var otherFriends = board.MinionFriend
                .Where(m => m != null
                    && m.Template != null
                    && !string.Equals(m.Template.Id.ToString(), viciousInvaderId, StringComparison.Ordinal))
                .ToList();
            if (otherFriends.Count < 2)
                return false;

            int fragileOthers = otherFriends.Count(m => m.CurrentHealth <= 2);
            return fragileOthers >= 1 || otherFriends.Count >= 3;
        }

        private void ApplyGlobalPlan(Board board, ProfileParameters p, int friendHp, int enemyHp, int friendAttack, int enemyAttack)
        {
            bool enemyHasTaunt = board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            bool onBoardLethal = !enemyHasTaunt && friendAttack >= enemyHp;
            bool boardAhead = board.MinionFriend.Count >= board.MinionEnemy.Count + 1;
            bool enemyHasBoard = board.MinionEnemy.Any(m => m != null);

            if (onBoardLethal)
            {
                p.GlobalAggroModifier = 300;
                AddLog("全局：场攻斩杀窗口，最大化走脸");
                return;
            }

            // 参考颜射术口径：按“敌方两回合压力 + 职业爆发保留”做攻守切换，避免单一阈值硬走脸。
            int burstReserve = GetEnemyBurstReserveByClass(board.EnemyClass);
            int twoTurnPressure = enemyAttack * 2 + burstReserve;
            if (enemyHasBoard && friendHp <= 22 && friendHp <= twoTurnPressure + 2)
            {
                p.GlobalAggroModifier = 20;
                AddLog("全局：两回合压力偏高（敌攻*2+爆发=" + twoTurnPressure + "），转守控场");
                return;
            }

            // 危险血线（尤其<=8）时，不继续盲目抢血，优先控场保命。
            if (friendHp <= 8 && enemyHasBoard)
            {
                p.GlobalAggroModifier = -40;
                AddLog("全局：危险血线，优先解场保命");
                return;
            }

            if (friendHp <= 12 && enemyAttack >= 8)
            {
                p.GlobalAggroModifier = -20;
                AddLog("全局：低血高压，先解场保命");
                return;
            }

            if (boardAhead)
            {
                p.GlobalAggroModifier = 130;
                AddLog("全局：我方站场领先，偏进攻");
                return;
            }

            int aggro = IsFastOpponent(board.EnemyClass) ? 70 : 95;
            p.GlobalAggroModifier = aggro;
            AddLog("全局：默认节奏压制，激进度=" + aggro);
        }

        // 烈焰小鬼：高节奏 3/2，但要管理血线
        private void HandleFlameImp(Board board, ProfileParameters p, int friendHp)
        {
            int manaNow = GetAvailableManaIncludingCoin(board);
            bool shadowflamePlayableNow = board.Hand != null && board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == ShadowflameStalker
                && c.CurrentCost <= manaNow);

            p.CastMinionsModifiers.AddOrUpdate(FlameImp, new Modifier(-180));
            p.PlayOrderModifiers.AddOrUpdate(FlameImp, new Modifier(420));

            if (board.MaxMana <= 2)
                p.CastMinionsModifiers.AddOrUpdate(FlameImp, new Modifier(-230));

            if (friendHp <= 12)
                p.CastMinionsModifiers.AddOrUpdate(FlameImp, new Modifier(90));
            if (friendHp <= 8)
                p.CastMinionsModifiers.AddOrUpdate(FlameImp, new Modifier(260));

            if (board.MaxMana >= 6)
            {
                p.CastMinionsModifiers.AddOrUpdate(FlameImp, new Modifier(280));
                p.PlayOrderModifiers.AddOrUpdate(FlameImp, new Modifier(4800));
                AddLog("后期前期随从降权：烈焰小鬼后置");
            }

            // 用户规则：后期（6费起）烈焰小鬼不应抢先于影焰猎豹，优先让位猎豹先落地。
            if (board.MaxMana >= 6 && shadowflamePlayableNow)
            {
                p.CastMinionsModifiers.AddOrUpdate(FlameImp, new Modifier(1800));
                p.PlayOrderModifiers.AddOrUpdate(FlameImp, new Modifier(-9200));
                AddLog("烈焰小鬼：后期有影焰猎豹可下，后置到猎豹之后");
            }

            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(FlameImp, new Modifier(65));
        }

        // 滑矛纳迦：法术联动核心，优先“先下后法术”
        private void HandleViciousSlitherspear(Board board, ProfileParameters p)
        {
            int manaNow = GetAvailableManaIncludingCoin(board);
            bool slitherspearPlayableNow = board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == ViciousSlitherspear
                && c.CurrentCost <= manaNow);
            bool flameImpPlayableNow = board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == FlameImp
                && c.CurrentCost <= manaNow);
            bool openingSlitherspearWindow = board.MaxMana <= 2 && board.MinionFriend.Count <= 1;
            bool firstTurnAbsoluteSlitherspearWindow = IsFirstTurnSlitherspearPriorityWindow(board);

            p.CastMinionsModifiers.AddOrUpdate(ViciousSlitherspear, new Modifier(-200));
            p.PlayOrderModifiers.AddOrUpdate(ViciousSlitherspear, new Modifier(450));

            if (HasAnyHand(board, AbductionRay, EntropicContinuity, TombOfSuffering, TheCoin))
                p.CastMinionsModifiers.AddOrUpdate(ViciousSlitherspear, new Modifier(-240));

            // 用户规则：第一回合最优先用凶恶的滑矛纳迦。
            if (firstTurnAbsoluteSlitherspearWindow)
            {
                SetSingleCardCombo(board, p, ViciousSlitherspear, allowCoinBridge: false, forceOverride: true,
                    logWhenSet: "滑矛纳迦：第一回合ComboSet最优先");
                p.CastMinionsModifiers.AddOrUpdate(ViciousSlitherspear, new Modifier(-6800));
                p.PlayOrderModifiers.AddOrUpdate(ViciousSlitherspear, new Modifier(9999));

                // 第一回合命中滑矛绝对优先窗口时，其它前期动作统一后置。
                p.CastMinionsModifiers.AddOrUpdate(Wisp, new Modifier(2600));
                p.PlayOrderModifiers.AddOrUpdate(Wisp, new Modifier(-9200));
                p.CastMinionsModifiers.AddOrUpdate(GlacialShard, new Modifier(2200));
                p.PlayOrderModifiers.AddOrUpdate(GlacialShard, new Modifier(-9100));
                p.CastMinionsModifiers.AddOrUpdate(FlameImp, new Modifier(2000));
                p.PlayOrderModifiers.AddOrUpdate(FlameImp, new Modifier(-9000));
                p.CastMinionsModifiers.AddOrUpdate(Murmy, new Modifier(1800));
                p.PlayOrderModifiers.AddOrUpdate(Murmy, new Modifier(-8900));
                p.CastMinionsModifiers.AddOrUpdate(PartyFiend, new Modifier(3200));
                p.PlayOrderModifiers.AddOrUpdate(PartyFiend, new Modifier(-9500));
                p.CastSpellsModifiers.AddOrUpdate(TheCoin, new Modifier(1500));
                p.PlayOrderModifiers.AddOrUpdate(TheCoin, new Modifier(-8800));

                AddLog("滑矛纳迦：第一回合最优先，强制先手");
                p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(ViciousSlitherspear, new Modifier(105));
                return;
            }

            if (board.MaxMana >= 6)
            {
                p.CastMinionsModifiers.AddOrUpdate(ViciousSlitherspear, new Modifier(220));
                p.PlayOrderModifiers.AddOrUpdate(ViciousSlitherspear, new Modifier(5200));
                AddLog("后期前期随从降权：凶恶的滑矛纳迦后置");
            }

            // 用户规则：起手阶段优先下滑矛纳迦（优先于烈焰小鬼）。
            if (slitherspearPlayableNow && openingSlitherspearWindow)
            {
                SetSingleCardCombo(board, p, ViciousSlitherspear, allowCoinBridge: true, forceOverride: false,
                    logWhenSet: "滑矛纳迦：起手ComboSet优先");
                p.CastMinionsModifiers.AddOrUpdate(ViciousSlitherspear, new Modifier(-3200));
                p.PlayOrderModifiers.AddOrUpdate(ViciousSlitherspear, new Modifier(9999));
                AddLog("滑矛纳迦：起手阶段优先落地");

                if (flameImpPlayableNow)
                {
                    p.CastMinionsModifiers.AddOrUpdate(FlameImp, new Modifier(700));
                    p.PlayOrderModifiers.AddOrUpdate(FlameImp, new Modifier(-7800));
                    AddLog("烈焰小鬼：起手有滑矛可下，后置到滑矛之后");
                }
            }

            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(ViciousSlitherspear, new Modifier(105));
        }

        // 鱼人木乃伊：复生黏场，作为续连熵能/奇利亚斯增伤承载体
        private void HandleMurmy(Board board, ProfileParameters p)
        {
            p.CastMinionsModifiers.AddOrUpdate(Murmy, new Modifier(-175));
            p.PlayOrderModifiers.AddOrUpdate(Murmy, new Modifier(380));

            if (board.MaxMana >= 6)
            {
                p.CastMinionsModifiers.AddOrUpdate(Murmy, new Modifier(360));
                p.PlayOrderModifiers.AddOrUpdate(Murmy, new Modifier(4400));
                AddLog("后期前期随从降权：鱼人木乃伊后置");
            }

            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Murmy, new Modifier(145));
        }

        // 小精灵：0费补场，配合续连熵能与计数模块降费
        private void HandleWisp(Board board, ProfileParameters p)
        {
            int manaNow = GetAvailableManaIncludingCoin(board);
            int freeSlots = GetFreeBoardSlots(board);
            int friendHp = GetHeroHealth(board.HeroFriend);
            var wispCard = board.Hand
                .Where(c => c != null && c.Template != null && c.Template.Id == Wisp)
                .OrderBy(c => c.CurrentCost)
                .FirstOrDefault();
            bool wispPlayableNow = wispCard != null && wispCard.CurrentCost <= manaNow && freeSlots > 0;

            bool entropyPlayableNow = board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == EntropicContinuity
                && c.CurrentCost <= manaNow);
            bool canPlayWispBeforeEntropy = board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == Wisp
                && c.CurrentCost <= manaNow)
                && entropyPlayableNow
                && board.MinionFriend.Count >= 1
                && board.MinionFriend.Count <= 6;
            bool enemyHasMinion = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null);
            bool storytellerOnBoard = board.MinionFriend != null
                && board.MinionFriend.Any(m => m != null && m.Template != null && m.Template.Id == TortollanStoryteller);
            bool storytellerInHand = board.Hand.Any(c => c != null && c.Template != null && c.Template.Id == TortollanStoryteller);
            bool storytellerPlayableNow = freeSlots > 0 && board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == TortollanStoryteller
                && c.CurrentCost <= manaNow);
            bool hasOtherPlayableAction = CountPlayableActionsExcludingCard(board, manaNow, Wisp) > 0;
            bool windowShopperPlayableNow = HasPlayableWindowShopperFamily(board, manaNow);
            bool lateGamePreferWindowShopper = board.MaxMana >= 6 && windowShopperPlayableNow;
            bool holdWispForStorytellerPlan = wispPlayableNow
                && friendHp >= 12
                && enemyHasMinion
                && storytellerInHand
                && !storytellerOnBoard
                && !storytellerPlayableNow
                && !canPlayWispBeforeEntropy;
            bool holdWispForSaferTempo = wispPlayableNow
                && friendHp >= 12
                && enemyHasMinion
                && hasOtherPlayableAction
                && !canPlayWispBeforeEntropy
                && !CanPlayWispThenStorytellerSameTurn(board, manaNow);

            p.CastMinionsModifiers.AddOrUpdate(Wisp, new Modifier(-120));
            p.PlayOrderModifiers.AddOrUpdate(Wisp, new Modifier(320));

            if (board.MaxMana >= 6)
            {
                p.CastMinionsModifiers.AddOrUpdate(Wisp, new Modifier(120));
                p.PlayOrderModifiers.AddOrUpdate(Wisp, new Modifier(6000));
                AddLog("后期前期随从降权：小精灵后置（同组内最高）");
            }

            if (board.MinionFriend.Count >= 6)
                p.CastMinionsModifiers.AddOrUpdate(Wisp, new Modifier(220));

            if (HasAnyHand(board, EntropicContinuity, ZilliaxTickingPower, ZilliaxDeckBuilder))
                p.CastMinionsModifiers.AddOrUpdate(Wisp, new Modifier(-190));

            // 用户规则：小精灵可留着与始祖龟联动；敌方有随从时不必提前裸下给对手解。
            if (holdWispForStorytellerPlan)
            {
                p.CastMinionsModifiers.AddOrUpdate(Wisp, new Modifier(3200));
                p.PlayOrderModifiers.AddOrUpdate(Wisp, new Modifier(-9800));
                AddLog("小精灵：手有始祖龟，后置保留联动避免白给");
            }
            else if (holdWispForSaferTempo)
            {
                p.CastMinionsModifiers.AddOrUpdate(Wisp, new Modifier(1700));
                p.PlayOrderModifiers.AddOrUpdate(Wisp, new Modifier(-9200));
                AddLog("小精灵：敌方有场且有其他动作，轻度后置避免提前送掉");
            }
            else if (wispPlayableNow && lateGamePreferWindowShopper)
            {
                p.CastMinionsModifiers.AddOrUpdate(Wisp, new Modifier(2400));
                p.PlayOrderModifiers.AddOrUpdate(Wisp, new Modifier(-9950));
                AddLog("小精灵：后期有可用橱窗看客，后置避免先占格子");
            }

            // 用户硬规则：若本回合将使用续连熵能，先下小精灵再上续连熵能以提高buff覆盖。
            if (canPlayWispBeforeEntropy)
            {
                SetSingleCardCombo(board, p, Wisp, allowCoinBridge: false, forceOverride: false,
                    logWhenSet: "小精灵：ComboSet先铺后续连");
                p.CastMinionsModifiers.AddOrUpdate(Wisp, new Modifier(-2600));
                p.PlayOrderModifiers.AddOrUpdate(Wisp, new Modifier(9800));
                AddLog("小精灵：命中先铺后续连规则，优先在续连熵能前打出");
            }

            // 用户口径：若攻击后腾出格子，回合末可补下小精灵则应补位再结束回合。
            // 典型场景：先打出高费随从占满场，交换后空出1格，剩余只有0费动作可做。
            if (wispPlayableNow
                && manaNow == 0
                && freeSlots >= 1
                && !lateGamePreferWindowShopper
                && !ShouldHoldForStorytellerBuff(board))
            {
                p.CastMinionsModifiers.AddOrUpdate(Wisp, new Modifier(-3600));
                p.PlayOrderModifiers.AddOrUpdate(Wisp, new Modifier(9999));
                AddLog("小精灵：命中0费收尾补位规则，攻击腾格后优先补下");
            }
        }

        // 冰川裂片：用于冻结嘲讽/高攻/吸血目标抢节奏
        private void HandleGlacialShard(Board board, ProfileParameters p)
        {
            int manaNow = GetAvailableManaIncludingCoin(board);
            int freeSlots = GetFreeBoardSlots(board);
            bool glacialPlayableNow = freeSlots > 0 && board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == GlacialShard
                && c.CurrentCost <= manaNow);

            if (ShouldHoldForStorytellerBuff(board))
            {
                p.CastMinionsModifiers.AddOrUpdate(GlacialShard, new Modifier(700));
                p.PlayOrderModifiers.AddOrUpdate(GlacialShard, new Modifier(-9200));
                AddLog("始祖龟窗口：敌方空场，冰川裂片后置等待回合末buff");
                return;
            }

            p.CastMinionsModifiers.AddOrUpdate(GlacialShard, new Modifier(-80));
            p.PlayOrderModifiers.AddOrUpdate(GlacialShard, new Modifier(260));

            if (board.MaxMana >= 6)
            {
                p.CastMinionsModifiers.AddOrUpdate(GlacialShard, new Modifier(170));
                p.PlayOrderModifiers.AddOrUpdate(GlacialShard, new Modifier(5600));
                AddLog("后期前期随从降权：冰川裂片后置（同组内次高）");
            }

            var enemyMinions = board.MinionEnemy
                .Where(m => m != null && m.Template != null)
                .ToList();
            var nonFrozenEnemyMinions = enemyMinions
                .Where(m => !IsCardFrozenByTag(m))
                .ToList();

            // 默认规则：敌方有随从时，冰片优先冻结“当前攻击最高”的目标。
            // 例外规则：若最高攻击目标是嘲讽，且本回合可低损清光嘲讽，则改冻非嘲讽威胁目标。
            if (enemyMinions.Count > 0)
            {
                if (nonFrozenEnemyMinions.Count == 0)
                {
                    p.CastMinionsModifiers.AddOrUpdate(GlacialShard, new Modifier(2200));
                    p.PlayOrderModifiers.AddOrUpdate(GlacialShard, new Modifier(-9200));
                    AddLog("冰川裂片：敌方目标已冻结，后置避免重复冻结");
                    return;
                }

                var surgeonTarget = nonFrozenEnemyMinions
                    .Where(m => m.Template.Id == Card.Cards.CORE_WON_065)
                    .OrderByDescending(m => m.CurrentHealth)
                    .ThenByDescending(m => m.CurrentAtk)
                    .FirstOrDefault();
                var attackRankedEnemies = nonFrozenEnemyMinions
                    .OrderByDescending(m => m.CurrentAtk)
                    .ThenByDescending(m => GetDangerEngineTradeBonus(m.Template.Id))
                    .ThenByDescending(m => m.IsTaunt ? 1 : 0)
                    .ThenByDescending(m => m.IsLifeSteal ? 1 : 0)
                    .ThenByDescending(m => m.CurrentHealth)
                    .ToList();
                var highestAtkEnemy = attackRankedEnemies.First();
                if (surgeonTarget != null)
                    highestAtkEnemy = surgeonTarget;

                int highestDangerBonus = GetDangerEngineTradeBonus(highestAtkEnemy.Template.Id);
                bool highestIsLowThreatZeroAtk = highestAtkEnemy.CurrentAtk <= 0
                    && !highestAtkEnemy.IsLifeSteal
                    && highestDangerBonus <= 0;
                if (highestIsLowThreatZeroAtk)
                {
                    if (board.WeaponEnemy != null && board.WeaponEnemy.CurrentAtk >= 3)
                    {
                        p.CastMinionsModifiers.AddOrUpdate(GlacialShard, -260, board.HeroEnemy.Id);
                        p.CastMinionsModifiers.AddOrUpdate(GlacialShard, new Modifier(-160));
                        p.CastMinionsModifiers.AddOrUpdate(GlacialShard, 1600, highestAtkEnemy.Id);
                        AddLog("冰川裂片：敌方仅低威胁0攻目标，转为冻结英雄压制高攻武器");
                    }
                    else
                    {
                        p.CastMinionsModifiers.AddOrUpdate(GlacialShard, new Modifier(180));
                        p.CastMinionsModifiers.AddOrUpdate(GlacialShard, 2200, highestAtkEnemy.Id);
                        AddLog("冰川裂片：敌方仅低威胁0攻目标，本回合不强行冻结");
                    }
                    return;
                }

                bool freezeTargetRedirected = false;
                bool canClearAllTauntsNow = CanClearAllEnemyTauntsWithFewAttackers(board, 2);
                if (highestAtkEnemy.IsTaunt && canClearAllTauntsNow)
                {
                    // 仅在存在“真实威胁”的非嘲讽目标时才重定向，避免把冻结浪费到0攻地标/无威胁单位上。
                    var alternativeTarget = nonFrozenEnemyMinions
                        .Where(m => !m.IsTaunt
                            && (m.CurrentAtk > 0
                                || m.IsLifeSteal
                                || GetDangerEngineTradeBonus(m.Template.Id) >= 120))
                        .OrderByDescending(m => m.CurrentAtk)
                        .ThenByDescending(m => GetDangerEngineTradeBonus(m.Template.Id))
                        .ThenByDescending(m => m.IsLifeSteal ? 1 : 0)
                        .ThenByDescending(m => m.CurrentHealth)
                        .FirstOrDefault();

                    if (alternativeTarget != null)
                    {
                        highestAtkEnemy = alternativeTarget;
                        freezeTargetRedirected = true;
                        AddLog("冰川裂片：嘲讽可低损必解，改为冻结非嘲讽目标 atk=" + highestAtkEnemy.CurrentAtk + " id=" + highestAtkEnemy.Template.Id);
                    }
                    else
                    {
                        AddLog("冰川裂片：嘲讽可低损必解，但无高威胁非嘲讽目标，保持冻结嘲讽目标");
                    }
                }

                // 用户规则：若最高攻击目标本回合大概率会被我方攻击解掉，
                // 冰片改冻“次高攻击”目标，提升冻结覆盖面。
                if (surgeonTarget == null
                    && !freezeTargetRedirected
                    && IsEnemyLikelyKilledByFriendlyAttacksThisTurn(board, highestAtkEnemy))
                {
                    var secondHighestEnemy = attackRankedEnemies
                        .FirstOrDefault(m => m != null && m.Id != highestAtkEnemy.Id);
                    if (secondHighestEnemy != null)
                    {
                        highestAtkEnemy = secondHighestEnemy;
                        freezeTargetRedirected = true;
                        AddLog("冰川裂片：最高攻目标预计可解，改冻次高攻 atk="
                            + highestAtkEnemy.CurrentAtk + " id=" + highestAtkEnemy.Template.Id);
                    }
                }

                // 最高攻击目标强制前置；非最高攻击目标显式后置，避免错误冻结。
                foreach (var enemy in enemyMinions)
                {
                    if (enemy.Id == highestAtkEnemy.Id)
                        p.CastMinionsModifiers.AddOrUpdate(GlacialShard, -4200, enemy.Id);
                    else
                        p.CastMinionsModifiers.AddOrUpdate(GlacialShard, 1200, enemy.Id);
                }

                p.CastMinionsModifiers.AddOrUpdate(GlacialShard, new Modifier(-260));
                p.PlayOrderModifiers.AddOrUpdate(GlacialShard, new Modifier(600));
                if (surgeonTarget != null)
                {
                    if (glacialPlayableNow)
                    {
                        SetSingleCardCombo(board, p, GlacialShard, allowCoinBridge: true, forceOverride: true,
                            logWhenSet: "冰川裂片：随船外科医师在场，ComboSet强制先手");
                        p.CastMinionsModifiers.AddOrUpdate(GlacialShard, new Modifier(-5200));
                        p.PlayOrderModifiers.AddOrUpdate(GlacialShard, new Modifier(9999));
                    }
                    AddLog("冰川裂片：检测到随船外科医师，强制优先冻结 id=" + highestAtkEnemy.Template.Id);
                    return;
                }
                AddLog(freezeTargetRedirected
                    ? "冰川裂片：重定向冻结目标 atk=" + highestAtkEnemy.CurrentAtk + " id=" + highestAtkEnemy.Template.Id
                    : "冰川裂片：优先冻结敌方最高攻击随从 atk=" + highestAtkEnemy.CurrentAtk + " id=" + highestAtkEnemy.Template.Id);
                return;
            }

            // 无敌方随从时，若敌方武器攻击较高则倾向冻结敌方英雄。
            if (board.WeaponEnemy != null && board.WeaponEnemy.CurrentAtk >= 3)
            {
                p.CastMinionsModifiers.AddOrUpdate(GlacialShard, -260, board.HeroEnemy.Id);
                p.CastMinionsModifiers.AddOrUpdate(GlacialShard, new Modifier(-160));
                AddLog("冰川裂片：敌方无随从，冻结英雄压制高攻武器");
            }
            else
            {
                bool canTapNow = CanUseLifeTapNow(board);
                bool hasOtherPlayableAction = board.Hand != null && board.Hand.Any(c =>
                    c != null
                    && c.Template != null
                    && c.Template.Id != GlacialShard
                    && c.Template.Id != TheCoin
                    && c.CurrentCost <= manaNow
                    && (c.Type != Card.CType.MINION || GetFreeBoardSlots(board) > 0));
                if (!canTapNow && !hasOtherPlayableAction)
                {
                    p.CastMinionsModifiers.AddOrUpdate(GlacialShard, new Modifier(-220));
                    p.PlayOrderModifiers.AddOrUpdate(GlacialShard, new Modifier(5200));
                    AddLog("冰川裂片：空场且无分流/其他动作，允许先落地避免空过");
                }
                else
                {
                    p.CastMinionsModifiers.AddOrUpdate(GlacialShard, new Modifier(90));
                }
            }
        }

        // 栉龙：抽牌+延迟弃牌，偏前中期节奏补件
        private void HandlePlatysaur(Board board, ProfileParameters p)
        {
            int manaNow = GetAvailableManaIncludingCoin(board);
            int freeSlots = GetFreeBoardSlots(board);
            int friendHp = GetHeroHealth(board.HeroFriend);
            int enemyAttack = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            bool forebodingPlayableNow = freeSlots > 0 && board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == ForebodingFlame
                && c.CurrentCost <= manaNow);
            bool forebodingUntriggered = !HasPlayedCard(board, ForebodingFlame);
            bool hasPlayablePlatysaur = board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == Platysaur
                && c.CurrentCost <= manaNow);
            bool dangerousHpTauntTempoWindow = IsDangerousHpTauntTempoWindow(friendHp, enemyAttack);
            var playableTauntMinionIds = GetPlayableTauntMinionCardIds(board, manaNow);
            bool hasPlayableTauntMinionNow = playableTauntMinionIds.Count > 0;
            bool rayPlayableNow = IsAbductionRayPlayableNow(board);
            bool forceRayNow = ShouldForceAbductionRayNow(board);
            bool enemyHasMinion = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null);
            bool entropyPlayableNow = board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == EntropicContinuity
                && c.CurrentCost <= manaNow);
            int entropyMinCoverage = enemyHasMinion ? 2 : 3;
            bool entropyCoverageReadyNow = board.MinionFriend.Count >= entropyMinCoverage;
            bool entropyPriorityWindowNow = entropyPlayableNow
                && entropyCoverageReadyNow
                && !rayPlayableNow
                && !forceRayNow
                && !ShouldForceTombFirstThisTurn(board);
            bool hasOtherPlayableActionAtOneMana = manaNow == 1 && hasPlayablePlatysaur
                && board.Hand.Any(card =>
                    card != null
                    && card.Template != null
                    && card.Template.Id != Platysaur
                    && card.Template.Id != TheCoin
                    && card.CurrentCost <= manaNow
                    && (card.Type != Card.CType.MINION || freeSlots > 0));
            bool hasPlayableFourCostTempoMinionNow = manaNow == 4
                && freeSlots > 0
                && board.Hand.Any(card =>
                    card != null
                    && card.Template != null
                    && card.Type == Card.CType.MINION
                    && card.Template.Id != Platysaur
                    && card.CurrentCost == 4
                    && card.CurrentCost <= manaNow);

            p.CastMinionsModifiers.AddOrUpdate(Platysaur, new Modifier(-260));
            p.PlayOrderModifiers.AddOrUpdate(Platysaur, new Modifier(760));

            // 用户口径：栉龙应更积极使用，哪怕抽上来后可能被弃也可接受。
            if (hasPlayablePlatysaur)
            {
                // 用户反馈：危险血线且有可下嘲讽时，栉龙必须后置让位嘲讽稳场。
                if (dangerousHpTauntTempoWindow && hasPlayableTauntMinionNow)
                {
                    foreach (var tauntId in playableTauntMinionIds)
                    {
                        p.CastMinionsModifiers.AddOrUpdate(tauntId, new Modifier(-5600));
                        p.PlayOrderModifiers.AddOrUpdate(tauntId, new Modifier(9999));
                    }
                    p.CastMinionsModifiers.AddOrUpdate(Platysaur, new Modifier(4200));
                    p.PlayOrderModifiers.AddOrUpdate(Platysaur, new Modifier(-9999));
                    AddLog("栉龙：危险血线且有可下嘲讽，后置让位嘲讽稳场");
                    return;
                }

                // 用户反馈：当回合可直接打续连熵能且覆盖达标时，先buff再下栉龙。
                if (entropyPriorityWindowNow)
                {
                    p.CastMinionsModifiers.AddOrUpdate(Platysaur, new Modifier(4200));
                    p.PlayOrderModifiers.AddOrUpdate(Platysaur, new Modifier(-9999));
                    AddLog("栉龙：续连熵能可用且覆盖达标，后置让位buff");
                    return;
                }

                // 用户规则：恶兆邪火可用且未触发时，栉龙后置让位邪火先手。
                if (forebodingPlayableNow && forebodingUntriggered)
                {
                    p.CastMinionsModifiers.AddOrUpdate(Platysaur, new Modifier(4600));
                    p.PlayOrderModifiers.AddOrUpdate(Platysaur, new Modifier(-9999));
                    AddLog("栉龙：恶兆邪火可用且未触发，后置让位邪火先手");
                    return;
                }

                if (rayPlayableNow)
                {
                    // 保持“挟持射线绝对优先”硬规则，仅在射线后立刻优先栉龙。
                    p.CastMinionsModifiers.AddOrUpdate(Platysaur, new Modifier(-1400));
                    p.PlayOrderModifiers.AddOrUpdate(Platysaur, new Modifier(8200));
                    AddLog("栉龙：可用且手有挟持射线，射线后优先落栉龙");
                }
                else
                {
                    if (hasOtherPlayableActionAtOneMana)
                    {
                        p.CastMinionsModifiers.AddOrUpdate(Platysaur, new Modifier(1400));
                        p.PlayOrderModifiers.AddOrUpdate(Platysaur, new Modifier(-9000));
                        AddLog("栉龙：1费不急着先手，有其他可打动作时后置");
                        return;
                    }

                    // 用户反馈：4费回合有可下4费随从时，不让栉龙强抢节奏位（避免“送栉龙”）。
                    if (hasPlayableFourCostTempoMinionNow)
                    {
                        p.CastMinionsModifiers.AddOrUpdate(Platysaur, new Modifier(3600));
                        p.PlayOrderModifiers.AddOrUpdate(Platysaur, new Modifier(-9999));
                        AddLog("栉龙：4费曲线有可下随从，后置让位不抢线");
                        return;
                    }

                    ForcePlatysaurFirstThisTurn(board, p, manaNow);
                    bool sketchPlayableNow = board.Hand.Any(c => c != null && c.Template != null
                        && c.Template.Id == SketchArtist
                        && c.CurrentCost <= manaNow)
                        && manaNow >= 4;
                    if (!sketchPlayableNow)
                    {
                        SetSingleCardCombo(board, p, Platysaur, allowCoinBridge: true, forceOverride: false,
                            logWhenSet: "栉龙：ComboSet必出");
                    }
                    p.CastMinionsModifiers.AddOrUpdate(Platysaur, new Modifier(-4200));
                    p.PlayOrderModifiers.AddOrUpdate(Platysaur, new Modifier(9999));
                    AddLog("栉龙：命中优先规则，可用则本回合优先使用");
                }
            }
            else
            {
                if (board.Hand.Count <= 6)
                    p.CastMinionsModifiers.AddOrUpdate(Platysaur, new Modifier(-320));
                if (board.Hand.Count >= 9)
                    p.CastMinionsModifiers.AddOrUpdate(Platysaur, new Modifier(-260));
            }

            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Platysaur, new Modifier(90));
        }

        private void ForcePlatysaurFirstThisTurn(Board board, ProfileParameters p, int manaNow)
        {
            if (board == null || board.Hand == null)
                return;

            foreach (var card in board.Hand.Where(c => c != null && c.Template != null))
            {
                if (card.Template.Id == Platysaur || card.Template.Id == AbductionRay || card.Template.Id == TheCoin)
                    continue;
                if (card.CurrentCost > manaNow)
                    continue;

                if (card.Type == Card.CType.MINION)
                    p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(1700));
                else if (card.Type == Card.CType.SPELL)
                    p.CastSpellsModifiers.AddOrUpdate(card.Template.Id, new Modifier(1700));
                else if (card.Type == Card.CType.WEAPON)
                    p.CastWeaponsModifiers.AddOrUpdate(card.Template.Id, new Modifier(1700));

                p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(-9400));
            }
        }

        // 派对邪犬：2费铺3个身材，强节奏但有自伤
        private void HandlePartyFiend(Board board, ProfileParameters p, int friendHp)
        {
            int manaNow = GetAvailableManaIncludingCoin(board);
            var partyCard = board.Hand
                .Where(c => c != null && c.Template != null && c.Template.Id == PartyFiend)
                .OrderBy(c => c.CurrentCost)
                .FirstOrDefault();
            var entropyCard = board.Hand
                .Where(c => c != null && c.Template != null && c.Template.Id == EntropicContinuity)
                .OrderBy(c => c.CurrentCost)
                .FirstOrDefault();

            // 场面价值独立于“是否在手”，避免牺牲已在场邪犬的保留权重。
            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(PartyFiend, new Modifier(85));

            // 手里没有派对邪犬时，不输出“后置不打”类日志，避免与场上邪犬攻击行为混淆。
            if (partyCard == null)
                return;

            int partyCost = partyCard != null ? partyCard.CurrentCost : 99;
            int entropyCost = entropyCard != null ? entropyCard.CurrentCost : 99;
            bool partyPlayable = partyCost <= manaNow;
            bool entropyPlayableNow = entropyCost <= manaNow;
            bool hasRoomForPartyFiend = board.MinionFriend.Count <= 4; // 邪犬本体+2个衍生共占3格
            bool hardBlockPartyFiendByBoardCount = board.MinionFriend.Count >= 6;
            bool canPlayPartyThenEntropySameTurn = partyPlayable
                && entropyPlayableNow
                && manaNow >= partyCost + entropyCost
                && hasRoomForPartyFiend;
            int enemyAttack = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            bool enemyHasBoard = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null);
            int hpAfterPartySelfDamage = friendHp - 3;
            bool emergencyPartyWindow = ShouldPrioritizePartyFiendNow(board, manaNow);

            p.CastMinionsModifiers.AddOrUpdate(PartyFiend, new Modifier(-165));
            p.PlayOrderModifiers.AddOrUpdate(PartyFiend, new Modifier(400));

            if (board.MaxMana >= 6)
            {
                p.CastMinionsModifiers.AddOrUpdate(PartyFiend, new Modifier(450));
                p.PlayOrderModifiers.AddOrUpdate(PartyFiend, new Modifier(4000));
                AddLog("后期前期随从降权：派对邪犬后置");
            }

            // 用户硬规则：友方随从 >= 6 时，不打派对邪犬（避免挤占格子并打断射线节奏）。
            if (hardBlockPartyFiendByBoardCount)
            {
                p.CastMinionsModifiers.AddOrUpdate(PartyFiend, new Modifier(9000));
                p.PlayOrderModifiers.AddOrUpdate(PartyFiend, new Modifier(-9999));
                AddLog("派对邪犬：友方随从>=6，硬规则禁打");
                return;
            }

            // 场位不足3格时，邪犬无法吃满价值，强后置。
            if (!hasRoomForPartyFiend)
            {
                p.CastMinionsModifiers.AddOrUpdate(PartyFiend, new Modifier(2600));
                p.PlayOrderModifiers.AddOrUpdate(PartyFiend, new Modifier(-9800));
                AddLog("派对邪犬：可用场位<3，价值下降后置");
                return;
            }

            // 血量约束：邪犬会自伤3，低血时必须先做生存判断，避免把自己压进斩杀线。
            if (hpAfterPartySelfDamage <= 2)
            {
                p.CastMinionsModifiers.AddOrUpdate(PartyFiend, new Modifier(9000));
                p.PlayOrderModifiers.AddOrUpdate(PartyFiend, new Modifier(-9999));
                AddLog("派对邪犬：自伤后血量<=2，硬规则禁打");
                return;
            }

            // 用户反馈：低血高压时不应“送邪犬”；若自伤后会落入敌方场攻覆盖，直接禁打（不允许应急窗口覆盖）。
            if (enemyHasBoard
                && friendHp <= 8
                && hpAfterPartySelfDamage <= enemyAttack)
            {
                p.CastMinionsModifiers.AddOrUpdate(PartyFiend, new Modifier(9000));
                p.PlayOrderModifiers.AddOrUpdate(PartyFiend, new Modifier(-9999));
                AddLog("派对邪犬：低血且自伤后落入敌方场攻覆盖，硬规则禁打");
                return;
            }

            // 非应急窗口下，若自伤后血线会落入敌方场攻覆盖，则后置让位保命动作。
            if (!emergencyPartyWindow
                && enemyHasBoard
                && friendHp <= 12
                && hpAfterPartySelfDamage <= enemyAttack)
            {
                p.CastMinionsModifiers.AddOrUpdate(PartyFiend, new Modifier(3800));
                p.PlayOrderModifiers.AddOrUpdate(PartyFiend, new Modifier(-9900));
                AddLog("派对邪犬：自伤后血线(" + hpAfterPartySelfDamage + ")落入敌方场攻(" + enemyAttack + ")覆盖，后置保命");
                return;
            }

            // 危险血线窗口：若可安全落地派对邪犬（不至于被自伤直接带走），优先先下补场止血。
            if (emergencyPartyWindow)
            {
                SetSingleCardCombo(board, p, PartyFiend, allowCoinBridge: true, forceOverride: true,
                    logWhenSet: "派对邪犬：危险血线窗口强制优先");
                p.CastMinionsModifiers.AddOrUpdate(PartyFiend, new Modifier(-5200));
                p.PlayOrderModifiers.AddOrUpdate(PartyFiend, new Modifier(9999));
                AddLog("派对邪犬：危险血线且可安全自伤，强制优先落地");
                return;
            }

            // 用户规则：当始祖龟已命中“高收益窗口（场上类型>=2）”且可落地时，邪犬让位。
            // 仅在“非挟持射线硬锁定”下生效，避免与更高优先硬链冲突。
            // 例外：
            // 1) 场上已有始祖龟时，不继续叠龟，优先邪犬铺场；
            // 2) 前期手握双始祖龟且邪犬可下时，优先邪犬抢节奏。
            bool storytellerHighValuePlayableNow = IsHighValueStorytellerPlayableNow(board, manaNow);
            bool rayHardLock = ShouldForceAbductionRayNow(board);
            bool storytellerOnBoard = board.MinionFriend != null
                && board.MinionFriend.Any(m => m != null && m.Template != null && m.Template.Id == TortollanStoryteller);
            int storytellerCountInHand = board.Hand.Count(c => c != null && c.Template != null && c.Template.Id == TortollanStoryteller);
            bool earlyDoubleStorytellerTempoWindow = partyPlayable
                && board.MaxMana <= 3
                && storytellerCountInHand >= 2;
            bool preferFloodBoardOverStorytellerNow = partyPlayable
                && !rayHardLock
                && GetMaxAdditionalBodiesWithBudgetExcludingCard(board, manaNow, TortollanStoryteller) >= 2;
            bool storytellerShortHandTempoWindow = partyPlayable
                && !rayHardLock
                && board.Hand.Count <= 3
                && board.MinionFriend != null
                && board.MinionFriend.Count >= 2
                && !(board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt));
            if (storytellerShortHandTempoWindow)
            {
                SetSingleCardCombo(board, p, PartyFiend, allowCoinBridge: true, forceOverride: true,
                    logWhenSet: "派对邪犬：短手牌节奏窗口优先");
                p.CastMinionsModifiers.AddOrUpdate(PartyFiend, new Modifier(-3600));
                p.PlayOrderModifiers.AddOrUpdate(PartyFiend, new Modifier(9999));
                p.CastMinionsModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(2600));
                p.PlayOrderModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(-9800));
                AddLog("派对邪犬：中期短手牌节奏窗口，优先于继续叠始祖龟");
                return;
            }
            if (storytellerHighValuePlayableNow && !rayHardLock && !storytellerOnBoard && !earlyDoubleStorytellerTempoWindow && !preferFloodBoardOverStorytellerNow)
            {
                p.CastMinionsModifiers.AddOrUpdate(PartyFiend, new Modifier(2400));
                p.PlayOrderModifiers.AddOrUpdate(PartyFiend, new Modifier(-9400));
                AddLog("派对邪犬：始祖龟高收益可落地，后置让位始祖龟");
                return;
            }
            if (storytellerHighValuePlayableNow && preferFloodBoardOverStorytellerNow)
            {
                int floodBodies = GetMaxAdditionalBodiesWithBudgetExcludingCard(board, manaNow, TortollanStoryteller);
                p.CastMinionsModifiers.AddOrUpdate(PartyFiend, new Modifier(-2600));
                p.PlayOrderModifiers.AddOrUpdate(PartyFiend, new Modifier(9800));
                p.CastMinionsModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(2600));
                p.PlayOrderModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(-9300));
                AddLog("派对邪犬：命中铺场窗口，优先铺场不让位始祖龟（可额外铺场体数=" + floodBodies + "）");
                return;
            }

            // 用户规则：起手命中“硬币跳2费”窗口时，优先跳币下邪犬。
            bool coinPartyOpenerWindow = ShouldCoinIntoPartyFiendNow(board, manaNow)
                && partyPlayable
                && hasRoomForPartyFiend
                && friendHp > 8;
            if (coinPartyOpenerWindow)
            {
                SetSingleCardCombo(board, p, PartyFiend, allowCoinBridge: true, forceOverride: true,
                    logWhenSet: "派对邪犬：起手跳币ComboSet优先");
                p.CastMinionsModifiers.AddOrUpdate(PartyFiend, new Modifier(-5200));
                p.PlayOrderModifiers.AddOrUpdate(PartyFiend, new Modifier(9999));
                p.CastSpellsModifiers.AddOrUpdate(TheCoin, new Modifier(-5200));
                p.PlayOrderModifiers.AddOrUpdate(TheCoin, new Modifier(9999));

                // 命中该窗口时，烈焰小鬼让位，避免“跳币后先下1费随从”打断邪犬节奏。
                p.CastMinionsModifiers.AddOrUpdate(FlameImp, new Modifier(1400));
                p.PlayOrderModifiers.AddOrUpdate(FlameImp, new Modifier(-8200));

                AddLog("派对邪犬：命中起手跳币窗口，强制先跳币再下邪犬");
                return;
            }

            if (friendHp <= 11)
                p.CastMinionsModifiers.AddOrUpdate(PartyFiend, new Modifier(120));
            if (friendHp <= 8)
                p.CastMinionsModifiers.AddOrUpdate(PartyFiend, new Modifier(300));

            if (board.MinionFriend.Count <= 3 && board.HasCardInHand(EntropicContinuity))
                p.CastMinionsModifiers.AddOrUpdate(PartyFiend, new Modifier(-230));

            // 联动优化：能“先邪犬再续连熵能”时，优先先铺邪犬再上buff。
            // 常见窗口：3~5费，已有2~4个随从，手里有可用续连熵能。
            if (canPlayPartyThenEntropySameTurn
                && manaNow >= 3
                && board.MinionFriend.Count >= 2
                && board.MinionFriend.Count <= 4
                && friendHp > 8)
            {
                SetSingleCardCombo(board, p, PartyFiend, allowCoinBridge: true, forceOverride: false,
                    logWhenSet: "派对邪犬：ComboSet先铺后buff");
                p.CastMinionsModifiers.AddOrUpdate(PartyFiend, new Modifier(-4200));
                p.PlayOrderModifiers.AddOrUpdate(PartyFiend, new Modifier(9999));
                AddLog("派对邪犬：可同回合接续连熵能，强制先下邪犬");
            }

            // 用户口径：2费回合优先派对邪犬（滚雪球更强），用 ComboSet 固定优先级。
            if (partyPlayable
                && board.MaxMana == 2
                && board.MinionFriend.Count <= 4
                && friendHp > 8)
            {
                SetSingleCardCombo(board, p, PartyFiend, allowCoinBridge: true, forceOverride: false,
                    logWhenSet: "派对邪犬：2费回合ComboSet优先");
                p.CastMinionsModifiers.AddOrUpdate(PartyFiend, new Modifier(-2800));
                p.PlayOrderModifiers.AddOrUpdate(PartyFiend, new Modifier(9800));
                AddLog("派对邪犬：2费窗口优先落地");
            }
        }

        // 用户新规则：后期优先打套外恶魔，再考虑前期低费随从。
        private void HandleLateGameOutsideDeckDemonPriority(Board board, ProfileParameters p)
        {
            if (board == null || p == null || board.Hand == null)
                return;
            if (board.MaxMana < 6)
                return;

            int manaNow = GetAvailableManaIncludingCoin(board);
            int freeSlots = GetFreeBoardSlots(board);
            int outsidePlayable = 0;
            bool platysaurPlayableNow = freeSlots > 0 && board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == Platysaur
                && c.CurrentCost <= manaNow);

            foreach (var card in board.Hand.Where(c => c != null && c.Template != null))
            {
                if (card.Type != Card.CType.MINION)
                    continue;
                if (card.CurrentCost > manaNow)
                    continue;
                if (!card.IsRace(Card.CRace.DEMON))
                    continue;
                if (!IsOutsideDeckDemonByDeckList(board, card.Template.Id))
                    continue;

                p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(-950));
                p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(9600));
                outsidePlayable++;
            }

            if (outsidePlayable > 0)
            {
                // 用户规则：后期可下套外恶魔时，栉龙不再抢先，后置让位套外恶魔。
                if (platysaurPlayableNow)
                {
                    p.CastMinionsModifiers.AddOrUpdate(Platysaur, new Modifier(5200));
                    p.PlayOrderModifiers.AddOrUpdate(Platysaur, new Modifier(-9999));
                    AddLog("后期优先级：可下套外恶魔时，栉龙后置让位");
                }

                AddLog("后期优先级：套外恶魔前置，优先于小精灵/冰川裂片/滑矛纳迦/烈焰小鬼/鱼人木乃伊/栉龙/派对邪犬，可打数量=" + outsidePlayable);
            }
        }

        // 恶兆邪火：越早下越能放大“套牌外恶魔”体系收益
        private void HandleForebodingFlame(Board board, ProfileParameters p)
        {
            p.CastMinionsModifiers.AddOrUpdate(ForebodingFlame, new Modifier(-180));
            p.PlayOrderModifiers.AddOrUpdate(ForebodingFlame, new Modifier(420));

            bool hasSynergyHand = HasAnyHand(board, AbductionRay, ShadowflameStalker, Archimonde);
            bool playedBefore = HasPlayedCard(board, ForebodingFlame);
            int manaNow = GetAvailableManaIncludingCoin(board);
            int freeSlots = GetFreeBoardSlots(board);
            int friendHp = GetHeroHealth(board.HeroFriend);
            int enemyAttack = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            int outsideDeckDemonsInHand = CountOutsideDeckDemonsInHand(board);
            bool playableNow = freeSlots > 0 && board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == ForebodingFlame
                && c.CurrentCost <= manaNow);
            bool enemyHasMinionNow = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null);
            bool enemyHasTauntNow = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            int enemyHp = GetHeroHealth(board.HeroEnemy);
            int attackableFriendAttackNow = GetAttackableBoardAttack(board.MinionFriend);
            bool onBoardLethalNow = !enemyHasTauntNow && attackableFriendAttackNow >= enemyHp;
            bool hasPlayableTauntMinionNow = GetPlayableTauntMinionCardIds(board, manaNow).Count > 0;
            bool storytellerPlayableNow = freeSlots > 0 && board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == TortollanStoryteller
                && c.CurrentCost <= manaNow);
            int raceTypes = CountDistinctFriendlyRaceTypes(board);
            bool storytellerPriorityWindow = storytellerPlayableNow
                && raceTypes >= 2
                && !ShouldForceAbductionRayNow(board);
            bool preferFloodBoardOverStorytellerNow = playableNow
                && !ShouldForceAbductionRayNow(board)
                && GetMaxAdditionalBodiesWithBudgetExcludingCard(board, manaNow, TortollanStoryteller) >= 2;
            bool emergencyTapBeforeFlameWindow = playableNow
                && CanUseLifeTapNow(board)
                && friendHp >= 3
                && friendHp <= 10
                && enemyHasMinionNow
                && !hasPlayableTauntMinionNow
                && !onBoardLethalNow
                && (enemyHasTauntNow || enemyAttack >= 12 || enemyAttack >= friendHp);

            // 低血高压时，先抽一口找保命牌，避免邪火抢节奏导致错失解场/嘲讽线。
            if (emergencyTapBeforeFlameWindow)
            {
                p.CastMinionsModifiers.AddOrUpdate(ForebodingFlame, new Modifier(3600));
                p.PlayOrderModifiers.AddOrUpdate(ForebodingFlame, new Modifier(-9800));
                AddLog("恶兆邪火：低血高压，后置让位分流找保命牌");
                return;
            }

            // 用户规则：前期若手里有始祖龟，且有“异种族1费随从”可先铺，暂时后置恶兆邪火。
            bool earlyStorytellerOneDropWindow = playableNow
                && ShouldDelayForebodingForEarlyStorytellerOneDrops(board, manaNow, friendHp, enemyAttack);
            if (earlyStorytellerOneDropWindow)
            {
                p.CastMinionsModifiers.AddOrUpdate(ForebodingFlame, new Modifier(5200));
                p.PlayOrderModifiers.AddOrUpdate(ForebodingFlame, new Modifier(-9999));

                var earlyOneDrops = board.Hand
                    .Where(c => c != null && c.Template != null
                        && c.Type == Card.CType.MINION
                        && c.Template.Id != ForebodingFlame
                        && c.Template.Cost == 1
                        && c.CurrentCost <= manaNow)
                    .ToList();
                foreach (var oneDrop in earlyOneDrops)
                {
                    p.CastMinionsModifiers.AddOrUpdate(oneDrop.Template.Id, new Modifier(-2600));
                    p.PlayOrderModifiers.AddOrUpdate(oneDrop.Template.Id, new Modifier(9800));
                }

                AddLog("恶兆邪火：前期手有始祖龟+异种族1费，强后置并让位1费铺场");
                return;
            }

            // 用户规则：恶兆邪火是小核心，优先级高于1费随从。
            // 先做一次前置抑制，后续在节奏阶段还有兜底，避免被其他窗口反向抬高。
            if (playableNow)
            {
                bool delayedAnyOneDrop = false;
                foreach (var oneDrop in board.Hand.Where(c => c != null && c.Template != null
                    && c.Type == Card.CType.MINION
                    && c.Template.Id != ForebodingFlame
                    && c.Template.Cost == 1
                    && c.CurrentCost <= manaNow))
                {
                    p.CastMinionsModifiers.AddOrUpdate(oneDrop.Template.Id, new Modifier(2400));
                    p.PlayOrderModifiers.AddOrUpdate(oneDrop.Template.Id, new Modifier(-9400));
                    delayedAnyOneDrop = true;
                }
                if (delayedAnyOneDrop)
                    AddLog("恶兆邪火：小核心优先于1费随从，先下邪火");
            }

            // 用户规则：始祖龟处于高收益窗口（类型>=2）时，邪火让位，避免抢掉始祖龟落地顺序。
            if (storytellerPriorityWindow && !preferFloodBoardOverStorytellerNow)
            {
                p.CastMinionsModifiers.AddOrUpdate(ForebodingFlame, new Modifier(2600));
                p.PlayOrderModifiers.AddOrUpdate(ForebodingFlame, new Modifier(-9600));
                AddLog("恶兆邪火：始祖龟高收益可落地，后置让位始祖龟");
                return;
            }
            if (storytellerPriorityWindow && preferFloodBoardOverStorytellerNow)
            {
                int floodBodies = GetMaxAdditionalBodiesWithBudgetExcludingCard(board, manaNow, TortollanStoryteller);
                p.CastMinionsModifiers.AddOrUpdate(ForebodingFlame, new Modifier(-2200));
                p.PlayOrderModifiers.AddOrUpdate(ForebodingFlame, new Modifier(8200));
                p.CastMinionsModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(2200));
                p.PlayOrderModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(-9000));
                AddLog("恶兆邪火：命中铺场窗口，优先铺场不让位始祖龟（可额外铺场体数=" + floodBodies + "）");
            }

            if (!playedBefore && hasSynergyHand)
            {
                p.CastMinionsModifiers.AddOrUpdate(ForebodingFlame, new Modifier(-230));
                AddLog("恶兆邪火：未触发过且有恶魔生成组件，优先下");
            }

            // 用户规则：恶兆邪火可重复触发；只要可用就优先（不覆盖“射线连发锁定”等硬优先链）。
            if (playableNow)
            {
                int castBoost = outsideDeckDemonsInHand > 0
                    ? -1800 - Math.Min(700, outsideDeckDemonsInHand * 220)
                    : -1100;
                if (castBoost < -2800) castBoost = -2800;

                SetSingleCardCombo(board, p, ForebodingFlame, allowCoinBridge: true, forceOverride: false,
                    logWhenSet: outsideDeckDemonsInHand > 0
                        ? "恶兆邪火：ComboSet减费窗口优先"
                        : playedBefore
                            ? "恶兆邪火：可重复触发，后续副本继续优先"
                            : "恶兆邪火：ComboSet小幅前置");
                p.CastMinionsModifiers.AddOrUpdate(ForebodingFlame, new Modifier(castBoost));
                p.PlayOrderModifiers.AddOrUpdate(ForebodingFlame, new Modifier(7600));

                if (outsideDeckDemonsInHand > 0)
                    AddLog("恶兆邪火：手中套外恶魔=" + outsideDeckDemonsInHand + "，提高优先级先减费");
                else if (playedBefore)
                    AddLog("恶兆邪火：可重复触发，有就优先使用");
                else
                    AddLog("恶兆邪火：可用且未触发过，小幅优先落地");
                return;
            }
        }

        // 卑鄙的恐惧魔王：敌方1血铺场时，优先落地吃回合末1点群伤。
        private void HandleDespicableDreadlord(Board board, ProfileParameters p)
        {
            if (board == null || board.Hand == null)
                return;
            if (!board.HasCardInHand(DespicableDreadlord))
                return;
            if (GetFreeBoardSlots(board) <= 0)
                return;

            int manaNow = GetAvailableManaIncludingCoin(board);
            bool playableNow = board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == DespicableDreadlord
                && c.CurrentCost <= manaNow);
            if (!playableNow)
                return;

            int enemyCount = board.MinionEnemy != null ? board.MinionEnemy.Count : 0;
            int enemyOneHealthCount = board.MinionEnemy.Count(m => m != null && m.CurrentHealth <= 1);
            bool highValueClearWindow = enemyOneHealthCount >= 3
                                        || (enemyOneHealthCount >= 2 && enemyCount >= 4);

            p.CastMinionsModifiers.AddOrUpdate(DespicableDreadlord, new Modifier(-120));
            p.PlayOrderModifiers.AddOrUpdate(DespicableDreadlord, new Modifier(260));

            // 用户规则：敌方1血随从越多，恐惧魔王越应优先落地。
            if (enemyOneHealthCount > 0)
            {
                int scaledCast = -900 - Math.Min(3600, enemyOneHealthCount * 700);
                int scaledOrder = 4200 + Math.Min(4200, enemyOneHealthCount * 900);

                if (highValueClearWindow)
                {
                    SetSingleCardCombo(board, p, DespicableDreadlord, allowCoinBridge: true, forceOverride: false,
                        logWhenSet: "恐惧魔王：1血清场窗口优先");
                    scaledCast -= 600;
                    scaledOrder += 900;
                }

                if (scaledCast < -5200) scaledCast = -5200;
                if (scaledOrder > 9999) scaledOrder = 9999;

                p.CastMinionsModifiers.AddOrUpdate(DespicableDreadlord, new Modifier(scaledCast));
                p.PlayOrderModifiers.AddOrUpdate(DespicableDreadlord, new Modifier(scaledOrder));
                AddLog("恐惧魔王：敌方1血随从=" + enemyOneHealthCount + "，数量越多优先级越高");
                return;
            }

            if (enemyCount == 0)
            {
                p.CastMinionsModifiers.AddOrUpdate(DespicableDreadlord, new Modifier(180));
                p.PlayOrderModifiers.AddOrUpdate(DespicableDreadlord, new Modifier(-1200));
                AddLog("恐惧魔王：敌方空场，轻微后置");
            }
        }

        // 挟持射线：核心生成器，配合滑矛纳迦与阿克蒙德
        private void HandleAbductionRay(Board board, ProfileParameters p)
        {
            if (!IsAbductionRayTurnUnlocked(board))
            {
                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(9999));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(-9999));
                AddLog("挟持射线：第4回合前硬规则禁用");
                return;
            }

            int manaNow = GetAvailableManaIncludingCoin(board);
            AbductionRayStartupState startupState = EvaluateAbductionRayStartupState(board, manaNow);
            bool hasPlayableRayInHandNow = startupState.PlayableRayCount > 0;
            bool rayPlayableNow = IsAbductionRayPlayableNow(board);
            bool rayContinuationThisTurn = IsAbductionRayContinuationThisTurn(board);
            bool fullManaTurn = IsFullManaTurn(board);
            bool hasTempPlayableRay = HasTemporaryPlayableAbductionRay(board);
            bool sketchGeneratedRayWindow = HasSketchGeneratedAbductionRayWindow(board);
            bool tempLikeWindow = startupState.TempLikeWindow;
            bool chainRayNow = ShouldChainAbductionRayNow(board);
            bool rayMidLateMultiChainWindow = startupState.MidLateMultiRayWindow;
            bool rayStartedThisTurn = startupState.RayStartedThisTurn;
            bool immediateRayFollowupWindow = hasPlayableRayInHandNow && rayStartedThisTurn;
            bool emergencyRaySurvivalWindow = IsAbductionRayEmergencySurvivalWindow(board, manaNow);
            bool hasPlayableLowCostMinion = HasPlayableLowCostMinion(board, 2);
            bool hasPlayableMinionNow = HasPlayableMinionWithBoardSpace(board, manaNow);
            bool hasPlayableBananaNow = board.Hand != null
                && board.Hand.Any(c => c != null && c.Template != null
                    && c.Template.Id == Banana
                    && c.CurrentCost <= manaNow)
                && board.MinionFriend != null
                && board.MinionFriend.Any(m => m != null && m.CanAttack && m.CurrentAtk > 0)
                && board.MinionEnemy != null
                && board.MinionEnemy.Any(m => m != null);
            bool partyEmergencyWindow = ShouldPrioritizePartyFiendNow(board, manaNow);
            int handCount = board.Hand != null ? board.Hand.Count : 0;
            int playableOutsideDeckDemonsThisTurn = GetMaxPlayableOutsideDeckDemonsThisTurn(board, manaNow);
            int outsideDeckDemonsInHand = CountOutsideDeckDemonsInHand(board);
            bool handHeavyOutsideDeckWindow = handCount >= 7
                && outsideDeckDemonsInHand >= 2
                && playableOutsideDeckDemonsThisTurn > 0;
            int outsideDemonDelayThreshold = GetAbductionRayOutsideDemonDelayMinPlayable(board);
            bool delayRayStartupForOutsideDeckDemonsWindow = startupState.DelayStartupForOutsideDeckDemonsWindow;
            int rayStartupHandCount = startupState.RayStartupHandCount;
            int stageMana = board != null ? Math.Max(0, board.MaxMana) : 0;
            bool isLateStage = stageMana >= 6;
            bool isMidLateStage = stageMana >= 5;
            bool handIsVeryLow = rayStartupHandCount <= 3;
            bool handWithinRayStartupWindow = startupState.AllowNonTempResourceWindow;
            // 本回合已起链时，不再受“手牌<=5”补资源阈值约束，避免中途断链。
            bool allowRayAsResourceWindow = handWithinRayStartupWindow || rayStartedThisTurn;
            bool handIsRich = rayStartupHandCount >= 5;
            bool richHandWithTempoActions = rayStartupHandCount >= 6 && (hasPlayableMinionNow || hasPlayableBananaNow);
            bool hasPlayableTempoMinion = HasPlayableMinionWithBoardSpace(board, manaNow);
            bool handRichAndTempoMinionWindow = rayStartupHandCount > AbductionRayNonTempPreferredMaxHand && hasPlayableTempoMinion;
            bool sketchPlayableNow = GetFreeBoardSlots(board) > 0
                && board.Hand.Any(c => c != null && c.Template != null
                    && c.Template.Id == SketchArtist
                    && c.CurrentCost <= manaNow);
            bool rayActiveChainWindow = HasAbductionRayActiveChainWindow(rayContinuationThisTurn, chainRayNow);
            bool preferSketchFirstWindow = sketchPlayableNow
                && manaNow >= 4
                && rayStartupHandCount > AbductionRayNonTempPreferredMaxHand
                && !tempLikeWindow
                && !rayActiveChainWindow
                && !HasPlayedCard(board, AbductionRay);
            bool sketchTempoAtThreeWindow = sketchPlayableNow
                && manaNow == 3
                && GetFreeBoardSlots(board) > 0
                && !tempLikeWindow
                && !emergencyRaySurvivalWindow
                && board.Hand.Count <= 6
                && board.MinionEnemy != null
                && board.MinionEnemy.Any(m => m != null);
            // 用户规则：挟持射线优先用于满费窗口（不再要求“没有可下随从”）。
            bool fullManaPriorityWindow = fullManaTurn && stageMana >= 3;
            // 用户规则：非临时射线仅在可用费用>=4时允许起链。
            bool nonTempStartupManaWindow = startupState.NonTempStartupManaWindow;
            // 用户新规则：非临时补资源线继续收缩，仅手牌<=5才允许进入“满费/连发硬锁”。
            bool rayHardLockWindow = immediateRayFollowupWindow
                || rayContinuationThisTurn
                || rayMidLateMultiChainWindow
                || (allowRayAsResourceWindow && (nonTempStartupManaWindow || chainRayNow));
            bool ongoingPriorityRayChainWindow = HasAbductionRayOngoingPriorityChainWindow(
                rayStartedThisTurn,
                rayActiveChainWindow,
                immediateRayFollowupWindow,
                rayMidLateMultiChainWindow);
            bool canDeferRayForTempoWindow = CanDeferAbductionRayForTempo(
                tempLikeWindow,
                immediateRayFollowupWindow,
                fullManaPriorityWindow,
                rayContinuationThisTurn,
                chainRayNow,
                rayMidLateMultiChainWindow);
            // 用户新规则：临时/连发/满费优先窗口下，挟持射线允许手牌打到10，
            // 防止“满费优先”被爆牌分支误拦截。
            bool tempRayAllowToTen = tempLikeWindow || rayHardLockWindow;
            bool overdrawRiskHigh = IsAbductionRayOverdrawRiskHigh(board, tempRayAllowToTen);
            bool overdrawRiskMedium = IsAbductionRayOverdrawRiskMedium(board, tempRayAllowToTen);
            bool enemyHasTaunt = board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            bool canClearTauntCheap = CanClearAllEnemyTauntsWithFewAttackers(board, 2);
            int friendHp = GetHeroHealth(board.HeroFriend);
            int enemyHp = GetHeroHealth(board.HeroEnemy);
            int friendAttack = GetBoardAttack(board.MinionFriend);
            int enemyAttack = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            bool balancedDefenseWindow = ShouldEnterBalancedDefenseMode(board, friendHp, enemyHp);
            bool pressureClearTauntWindow = enemyHasTaunt && canClearTauntCheap && balancedDefenseWindow;
            var zeroCostMinionForRayRelease = GetRightmostPlayableZeroCostNonRayMinion(board, manaNow);
            // 用户规则：命中射线锁定后，只允许0费随从插队（用于腾手牌/补节奏）。
            bool allowZeroCostReleaseInRayChain = rayHardLockWindow
                && zeroCostMinionForRayRelease != null;
            // 用户规则：危险血线时，允许打断挟持射线链路，优先下可用嘲讽随从稳场。
            bool dangerousHpWindow = IsDangerousHpTauntTempoWindow(friendHp, enemyAttack);
            var playableTauntMinionIds = GetPlayableTauntMinionCardIds(board, manaNow);
            bool hasPlayableTauntMinionNow = playableTauntMinionIds.Count > 0;
            var playableOutsideDeckTauntMinionIds = GetPlayableOutsideDeckTauntMinionCardIds(board, manaNow);
            bool hasPlayableOutsideDeckTauntMinionNow = playableOutsideDeckTauntMinionIds.Count > 0;
            bool rushClearWindow = ShouldPrioritizeRushMinionClear(board, friendHp, enemyHp, friendAttack, enemyAttack)
                && HasPlayableRushMinionForClear(board, manaNow);
            var zeroCostMinionForRayDelay = GetRightmostPlayableZeroCostNonRayMinion(board, manaNow);
            bool handFullWithZeroCostMinionWindow = handCount >= HearthstoneHandLimit
                && zeroCostMinionForRayDelay != null
                && startupState.HandFullWithZeroCostMinionWindow
                && !emergencyRaySurvivalWindow;
            bool handFullWithCheapMinionWindow = handFullWithZeroCostMinionWindow;
            bool tombInHand = board.HasCardInHand(TombOfSuffering);
            // 用户规则：同回合“墓+射线”冲突时，优先直接启动挟持射线，墓后置。
            bool rayStartBeforeTombWindow = tombInHand
                && !tempLikeWindow
                && !rayStartedThisTurn
                && hasPlayableRayInHandNow;

            int doomguardInHand = board.Hand.Count(c => c != null && c.Template != null && c.Template.Id == Doomguard);
            bool doomguardCloggedHandWindow = doomguardInHand > 0
                && rayStartupHandCount >= 1
                && !tempLikeWindow
                && !rayStartedThisTurn
                && nonTempStartupManaWindow
                && (doomguardInHand >= 2 || enemyAttack >= 6);

            // 用户硬规则：非临时挟持射线仅在可用费用>=4时允许起链；
            // 本回合已起链时不受该限制，避免中途断链。
            // 临时挟持射线（含速写临时窗口）不受该限制。
            bool nonTempRayMinManaBlocked = !tempLikeWindow
                && !rayStartedThisTurn
                && manaNow < AbductionRayMinManaNonTemp;

            p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(-110));
            p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(260));

            bool slitherspearOnBoard = board.MinionFriend.Any(m => m != null && m.Template != null && m.Template.Id == ViciousSlitherspear);
            bool slitherspearLethalWindow = IsAbductionRaySlitherspearLethalWindow(board, manaNow);
            bool forebodingActive = HasPlayedCard(board, ForebodingFlame);
            bool forebodingPlayableNow = board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == ForebodingFlame
                && c.CurrentCost <= manaNow);
            bool forebodingPriorityWindow = forebodingPlayableNow;

            if (slitherspearOnBoard)
                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(-230));

            if (forebodingActive || board.HasCardInHand(Archimonde))
                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(-190));

            // T1 只有这张时通常不急着空过节奏（除非有滑矛可联动）
            if (board.MaxMana == 1 && GetAvailableManaIncludingCoin(board) == 1 && !slitherspearOnBoard && !board.HasCardInHand(ViciousSlitherspear))
                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(120));

            if (!rayPlayableNow)
            {
                // 硬规则：不满足可打窗口时，必须显式禁用，避免保留默认负权重导致误打。
                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(9999));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(-9999));
                AddLog("挟持射线：当前不满足可打窗口，硬规则禁用");
                return;
            }

            // 用户修正：当基尔加丹命中直拍窗口时，非临时挟持射线链路让位基尔加丹先手。
            if (!tempLikeWindow
                && !slitherspearLethalWindow
                && ShouldPreferKiljaedenImmediateOverRayChain(board, manaNow))
            {
                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(5600));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(-9999));
                AddLog("挟持射线：基尔加丹命中直拍窗口，后置让位基尔加丹先手");
                return;
            }

            // 用户规则：手里有可下“嘲讽套外随从”时，优先先下嘲讽稳场，挟持射线可暂缓。
            // 该规则也允许打断本回合射线续链，避免继续补资源而错过即时稳场。
            if (hasPlayableOutsideDeckTauntMinionNow && !slitherspearLethalWindow)
            {
                var preferredOutsideTaunt = board.Hand
                    .Where(c => c != null
                        && c.Template != null
                        && c.Type == Card.CType.MINION
                        && c.CurrentCost <= manaNow
                        && c.IsRace(Card.CRace.DEMON)
                        && IsOutsideDeckDemonByDeckList(board, c.Template.Id)
                        && IsTauntMinionForSurvival(c))
                    .OrderBy(c => c.CurrentCost)
                    .ThenBy(c => c.Id)
                    .FirstOrDefault();
                if (preferredOutsideTaunt != null)
                {
                    SetSingleCardComboByEntityId(board, p, preferredOutsideTaunt.Id,
                        allowCoinBridge: true, forceOverride: true,
                        logWhenSet: "挟持射线：命中套外嘲讽优先窗口，ComboSet先下嘲讽");
                }

                foreach (var tauntId in playableOutsideDeckTauntMinionIds)
                {
                    p.CastMinionsModifiers.AddOrUpdate(tauntId, new Modifier(-6200));
                    p.PlayOrderModifiers.AddOrUpdate(tauntId, new Modifier(9999));
                }

                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(5600));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(-9999));
                AddLog("挟持射线：手有可下套外嘲讽恶魔，暂缓射线先下嘲讽");
                return;
            }

            // 用户硬规则：本回合只要已经起过射线，且手里仍有可打射线，就持续续链到底。
            // 仅在爆牌风险过高时允许中断（优先放行0费随从腾手牌；否则暂停续链）。
            if (rayStartedThisTurn && hasPlayableRayInHandNow)
            {
                // 用户补充：本回合已起链时，手牌=9允许再打一张射线（到10不烧牌）；
                // 仅手牌已满(=10)时继续按“高爆牌风险”中断。
                bool allowOneMoreRayToTen = board.Hand != null && board.Hand.Count <= HearthstoneHandLimit - 1;
                if (IsAbductionRayOverdrawRiskHigh(board, allowOneMoreRayToTen))
                {
                    if (zeroCostMinionForRayRelease != null)
                    {
                        SetSingleCardComboByEntityId(board, p, zeroCostMinionForRayRelease.Id,
                            allowCoinBridge: true, forceOverride: true,
                            logWhenSet: "挟持射线：续链中爆牌风险，先放行0费随从腾手牌");
                        p.CastMinionsModifiers.AddOrUpdate(zeroCostMinionForRayRelease.Id, new Modifier(-5000));
                        p.PlayOrderModifiers.AddOrUpdate(zeroCostMinionForRayRelease.Id, new Modifier(9999));
                        p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(900));
                        p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(9200));
                        ForceAbductionRayFirstThisTurn(board, p, manaNow, allowZeroCostMinions: true);
                        AddLog("挟持射线：本回合已起链，命中爆牌风险时仅放行0费，其余动作不插队");
                        return;
                    }

                    AddLog("挟持射线：本回合已起链且会爆牌，仍按硬规则续链");
                }

                SetAbductionRayCombo(board, p, allowCoinBridge: true, chainAll: true, forceOverride: true,
                    logWhenSet: "挟持射线：本回合已起链且手里仍有射线，强制续到底");
                ForceAbductionRayFirstThisTurn(board, p, manaNow, allowZeroCostMinions: true);
                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(-5600));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(9999));
                AddLog("挟持射线：本回合已起射线且手里仍有射线，硬规则续到底（仅爆牌例外）");
                return;
            }

            // 用户规则：滑矛可攻且射线增伤可补足当回合斩杀时，强制先手打射线。
            if (slitherspearLethalWindow)
            {
                SetAbductionRayCombo(board, p, allowCoinBridge: true, chainAll: false, forceOverride: true,
                    logWhenSet: "挟持射线：命中滑矛斩杀增伤窗口，强制先手");
                ForceAbductionRayFirstThisTurn(board, p, manaNow, allowZeroCostMinions: true);
                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(-5600));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(9999));
                AddLog("挟持射线：滑矛可攻斩杀窗口，先打射线吃法术增攻");
                return;
            }

            // 用户新规则：血量危险且可下嘲讽时，允许断链，不让射线（含临时链）抢先。
            if (dangerousHpWindow && hasPlayableTauntMinionNow)
            {
                foreach (var tauntId in playableTauntMinionIds)
                {
                    p.CastMinionsModifiers.AddOrUpdate(tauntId, new Modifier(-5200));
                    p.PlayOrderModifiers.AddOrUpdate(tauntId, new Modifier(9999));
                }

                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(5200));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(-9999));
                AddLog("挟持射线：危险血线且有可下嘲讽，允许断链先下嘲讽稳场");
                return;
            }

            // 用户新规则：高压时若有可下突袭随从可立即解场，允许打断射线链路，先解怪。
            if (rushClearWindow)
            {
                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(4200));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(-9999));
                AddLog("挟持射线：命中突袭解场窗口，后置到突袭随从之后");
                return;
            }

            // 用户硬规则：临时挟持射线链路不受任何条件影响，有就直接用。
            if (tempLikeWindow)
            {
                var tempRayEntity = GetRightmostTemporaryPlayableAbductionRay(board, manaNow);
                bool setTempOnly = tempRayEntity != null
                    && SetSingleCardComboByEntityId(board, p, tempRayEntity.Id, allowCoinBridge: true, forceOverride: true,
                        logWhenSet: hasTempPlayableRay
                            ? "挟持射线：ComboSet临时牌（仅临时）"
                            : "挟持射线：ComboSet速写临时牌（仅临时）");
                if (setTempOnly && tempRayEntity != null)
                {
                    ForceAbductionRayFirstThisTurn(board, p, manaNow);
                    // 实体级硬锁：优先临时射线本体，避免同名非临时副本被误打。
                    p.CastSpellsModifiers.AddOrUpdate(tempRayEntity.Id, new Modifier(-9999));
                    p.PlayOrderModifiers.AddOrUpdate(tempRayEntity.Id, new Modifier(9999));
                    foreach (var ray in board.Hand.Where(c => c != null
                        && c.Template != null
                        && c.Template.Id == AbductionRay
                        && c.CurrentCost <= manaNow
                        && c.Id != tempRayEntity.Id
                        && !IsTemporaryCard(c)))
                    {
                        p.CastSpellsModifiers.AddOrUpdate(ray.Id, new Modifier(6200));
                        p.PlayOrderModifiers.AddOrUpdate(ray.Id, new Modifier(-9999));
                    }

                    AddLog("挟持射线：临时链路硬规则触发，优先临时实体(" + tempRayEntity.Id + ")");
                    return;
                }

                AddLog("挟持射线：检测到临时窗口但未命中临时实体，回退常规决策");
            }

            // 用户新规则：绝境保命窗口（近似必死）且手里无可下嘲讽时，优先射线搏嘲讽。
            if (emergencyRaySurvivalWindow)
            {
                SetAbductionRayCombo(board, p, allowCoinBridge: true, chainAll: true, forceOverride: true,
                    logWhenSet: "挟持射线：绝境保命窗口，强制先手");
                ForceAbductionRayFirstThisTurn(board, p, manaNow, allowZeroCostMinions: false);
                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(-5200));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(9999));
                AddLog("挟持射线：绝境保命窗口，优先使用尝试发现嘲讽随从");
                return;
            }

            // 用户规则：手里已有多张可下套外恶魔且手牌较多时，优先先下手牌，暂缓启动射线。
            if (delayRayStartupForOutsideDeckDemonsWindow)
            {
                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(3200));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(-9900));
                if (handHeavyOutsideDeckWindow)
                {
                    AddLog("挟持射线：手里套外随从较多且手牌偏满，暂缓射线先用手牌（手中套外="
                        + outsideDeckDemonsInHand + "，可打数量=" + playableOutsideDeckDemonsThisTurn + "）");
                }
                else
                {
                    AddLog("挟持射线：手牌>=6且可下套外恶魔>=" + outsideDemonDelayThreshold
                        + (outsideDemonDelayThreshold < AbductionRayOutsideDemonDelayMinPlayable ? "（高压放宽）" : "")
                        + "，暂缓启动让位套外恶魔（可打数量=" + playableOutsideDeckDemonsThisTurn + "）");
                }
                return;
            }

            // 用户规则：墓同回合可用时，不让墓抢先；直接启动射线链。
            if (rayStartBeforeTombWindow)
            {
                SetAbductionRayCombo(board, p, allowCoinBridge: true, chainAll: true, forceOverride: true,
                    logWhenSet: "挟持射线：命中墓冲突窗口，先手启动射线");
                ForceAbductionRayFirstThisTurn(board, p, manaNow, allowZeroCostMinions: false);
                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(-3800));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(9999));
                AddLog("挟持射线：同回合咒怨之墓可用，先启动射线链");
                return;
            }

            if (nonTempRayMinManaBlocked)
            {
                // 非临时起链费用窗口是硬规则，避免低费回合“无动作时仍误放射线”。
                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(9999));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(-9999));
                AddLog("挟持射线：非临时可用费用<4，硬规则禁用");
                return;
            }

            // 用户规则：手里有末日守卫（尤其双末日）且射线可开时，优先射线找替代动作，
            // 不要被“手牌>5先做节奏”分支拦截。
            if (doomguardCloggedHandWindow)
            {
                SetAbductionRayCombo(board, p, allowCoinBridge: true, chainAll: false, forceOverride: true,
                    logWhenSet: "挟持射线：命中末日守卫滞留窗口，先手启动射线");
                ForceAbductionRayFirstThisTurn(board, p, manaNow, allowZeroCostMinions: false);
                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(-3600));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(9999));
                AddLog("挟持射线：手有末日守卫且不宜直接拍出，优先射线找替代动作（启动手牌口径排除币/阿克蒙德/基尔加丹/末日守卫）");
                return;
            }

            // 用户新规则：4-6费回合，手里同时有速写美术家+挟持射线时，优先速写先手，
            // 防止射线抢先导致错过“速写->射线临时链”的当回合转化窗口。
            bool turnFourToSixSketchFirstWindow = stageMana >= 4
                && stageMana <= 6
                && sketchPlayableNow
                && hasPlayableRayInHandNow
                && GetFreeBoardSlots(board) > 0
                && !rayStartedThisTurn
                && !tempLikeWindow
                && !slitherspearLethalWindow
                && !emergencyRaySurvivalWindow;
            if (turnFourToSixSketchFirstWindow)
            {
                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(5200));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(-9999));
                AddLog("挟持射线：4-6费手有速写美术家，射线后置让位速写先手");
                return;
            }

            // 危险血线窗口下，派对邪犬优先补场；射线让位，避免“起链后无法落邪犬”。
            if (partyEmergencyWindow && !tempLikeWindow)
            {
                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(4200));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(-9900));
                AddLog("挟持射线：危险血线命中派对邪犬优先窗口，后置到邪犬之后");
                return;
            }

            // 用户新规则：4费起若速写可下且手牌充足，优先速写；射线留作后续补资源。
            if (preferSketchFirstWindow)
            {
                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(3600));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(-9900));
                AddLog("挟持射线：4费起速写可用且手牌充足，后置保留作补资源");
                return;
            }

            // 用户规则：3费节奏回合且速写可用时，不让“射线续链”抢先，优先当回合落速写。
            if (rayContinuationThisTurn && sketchTempoAtThreeWindow)
            {
                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(4200));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(-9999));
                AddLog("挟持射线：3费速写节奏窗口，后置让位速写先手");
                return;
            }

            // 用户新规则：手牌>5且有可下随从时，降低挟持射线优先值，先做场面节奏动作。
            // 满费窗口/已起链窗口除外，避免打断既有硬规则链。
            if (handRichAndTempoMinionWindow
                && canDeferRayForTempoWindow)
            {
                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(3400));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(-9900));
                AddLog("挟持射线：手牌>5且有可下随从，降低优先值先做节奏");
                return;
            }

            // 手牌充足且有可下随从时，不主动起/续挟持射线链。
            if (richHandWithTempoActions
                && canDeferRayForTempoWindow)
            {
                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(2800));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(-9800));
                AddLog("挟持射线：手牌充足且有节奏动作(含香蕉buff)，后置不启动连发");
                return;
            }

            // 用户规则：阿克蒙德仅在“套外恶魔(场+坟) >= 6”时才算拍出窗口，挟持射线不得抢先。
            bool archimondePlayableNow = board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == Archimonde
                && c.CurrentCost <= manaNow);
            if (archimondePlayableNow && !rayStartedThisTurn)
            {
                int outsideDeckDemonsInBoardAndGrave = CountOutsideDeckDemonsInBoardAndGrave(board);
                bool archimondeReadyNow = outsideDeckDemonsInBoardAndGrave >= 6;
                if (archimondeReadyNow)
                {
                    p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(4200));
                    p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(-9900));
                    AddLog("挟持射线：阿克蒙德拍出窗口已满足，后置到阿克蒙德之后");
                    return;
                }
            }

            // 用户规则：射线连发锁定优先级更高；仅在非连发/非临时窗口时，邪火才可前置。
            bool allowForebodingOverrideRay = !tempLikeWindow && !rayHardLockWindow;
            if (forebodingPriorityWindow && allowForebodingOverrideRay)
            {
                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(3200));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(-9800));
                AddLog("挟持射线：恶兆邪火可优先减费，后置到邪火之后");
                return;
            }

            // 临时挟持射线已在函数前段按硬规则优先处理，这里继续常规非临时分支。

            // 用户规则：手牌=10且有可下“其他0费随从”时，暂缓挟持射线，
            // 先下0费随从腾手牌，避免射线补牌导致爆牌。
            if (handFullWithCheapMinionWindow)
            {
                var cheapMinionForRayDelay = zeroCostMinionForRayDelay;
                if (cheapMinionForRayDelay == null)
                    return;

                SetSingleCardComboByEntityId(board, p, cheapMinionForRayDelay.Id,
                    allowCoinBridge: true, forceOverride: true,
                    logWhenSet: "挟持射线：手牌=10，先下0费随从腾手牌");
                p.CastMinionsModifiers.AddOrUpdate(cheapMinionForRayDelay.Id, new Modifier(-4600));
                p.PlayOrderModifiers.AddOrUpdate(cheapMinionForRayDelay.Id, new Modifier(9999));
                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(1800));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(-9600));
                AddLog("挟持射线：手牌已满，仅允许0费随从先手，射线后置");
                return;
            }

            // 用户新规则：非临时补资源线继续收缩，手牌>5时不主动打挟持射线。
            if (!allowRayAsResourceWindow
                && canDeferRayForTempoWindow)
            {
                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(3600));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(-9900));
                AddLog("挟持射线：手牌>5，非临时补资源线后置保留");
                return;
            }

            // 用户新规则：挟持射线要考虑爆牌风险。手牌接近上限时默认不主动打。
            if (overdrawRiskHigh && !rayHardLockWindow)
            {
                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(3200));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(-9900));
                AddLog(tempRayAllowToTen
                    ? "挟持射线：手牌已满，后置避免爆牌"
                    : "挟持射线：手牌接近上限，后置避免爆牌");
                return;
            }
            if (overdrawRiskMedium && !tempRayAllowToTen && !rayHardLockWindow)
            {
                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(650));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(-1800));
                AddLog("挟持射线：存在爆牌风险，谨慎后置");
            }

            // 用户反馈：在高压且可低损解嘲讽时，优先解场，不让射线链抢走关键交换。
            if (pressureClearTauntWindow)
            {
                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(3600));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(-9900));
                AddLog("挟持射线：高压嘲讽场面，先解场后续射线链");
                return;
            }

            // 用户硬规则：命中连发/后期锁定窗口时，挟持射线必须先打，禁止其他动作插队。
            if (rayHardLockWindow && !overdrawRiskHigh)
            {
                if (board.MinionFriend != null && board.MinionFriend.Count >= 6)
                {
                    p.CastMinionsModifiers.AddOrUpdate(PartyFiend, new Modifier(9000));
                    p.PlayOrderModifiers.AddOrUpdate(PartyFiend, new Modifier(-9999));
                }

                // 用户规则：挟持射线连锁时，0费随从允许放行用于腾手牌（便于继续补牌）。
                if (allowZeroCostReleaseInRayChain)
                {
                    SetSingleCardComboByEntityId(board, p, zeroCostMinionForRayRelease.Id,
                        allowCoinBridge: true, forceOverride: true,
                        logWhenSet: "挟持射线：连锁链路放行0费随从");
                    p.CastMinionsModifiers.AddOrUpdate(zeroCostMinionForRayRelease.Id, new Modifier(-4600));
                    p.PlayOrderModifiers.AddOrUpdate(zeroCostMinionForRayRelease.Id, new Modifier(9999));

                    // 用户反馈：手牌偏满时要“先下0费再续射线”，避免射线先手导致爆牌。
                    p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(900));
                    p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(9200));
                    ForceAbductionRayFirstThisTurn(board, p, manaNow, allowZeroCostMinions: true);
                    AddLog("挟持射线：射线锁定中放行0费随从，其余动作不插队");
                    return;
                }

                SetAbductionRayCombo(board, p, allowCoinBridge: true, chainAll: true, forceOverride: true,
                    logWhenSet: GetAbductionRayHardLockComboLogWhenSet(chainRayNow, rayMidLateMultiChainWindow));
                ForceAbductionRayFirstThisTurn(board, p, manaNow, allowZeroCostMinions: false);
                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(-4200));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(9999));
                AddLog(GetAbductionRayHardLockResolveLog(rayMidLateMultiChainWindow));
                return;
            }

            // 新规则：本回合若可低损清掉嘲讽，先解嘲讽，不让挟持射线抢节奏。
            if (enemyHasTaunt && canClearTauntCheap && !rayHardLockWindow)
            {
                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(2800));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(-9800));
                AddLog("挟持射线：本回合可低损解嘲讽，后置到解场之后");
                return;
            }

            // 用户新规则：三费前不主动打挟持射线，优先下低费随从，射线保留作补资源。
            // 这里按“回合水晶”口径判定前期：MaxMana <= 2（含硬币也视为前期）。
            bool preThreeStage = board.MaxMana <= 2;
            bool reserveRayForLowCostTempoWindow = ShouldReserveAbductionRayForLowCostTempo(
                hasPlayableLowCostMinion,
                ongoingPriorityRayChainWindow);
            bool continueChainRayPriorityWindow = ShouldContinueAbductionRayChainPriority(
                chainRayNow,
                isLateStage,
                handIsVeryLow);
            bool softReserveChainRayWindow = ShouldSoftReserveAbductionRayChain(
                chainRayNow,
                continueChainRayPriorityWindow);

            if (preThreeStage)
            {
                if (reserveRayForLowCostTempoWindow)
                {
                    p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(2600));
                    p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(-9800));
                    AddLog("挟持射线：三费前且有低费随从，后置保留补资源");
                    return;
                }

                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(900));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(-5000));
                AddLog("挟持射线：三费前默认保留，非必要不打");
                return;
            }

            // 用户新口径：只在“低费随从匮乏”时才考虑把挟持射线当补手牌手段。
            if (reserveRayForLowCostTempoWindow)
            {
                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(2400));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(-9600));
                AddLog("挟持射线：手有可用低费随从，后置保留补资源");
                return;
            }

            // 连发窗口：本局已打过射线，且当前仍可打时，优先续接射线。
            if (continueChainRayPriorityWindow)
            {
                SetAbductionRayCombo(board, p, allowCoinBridge: true, chainAll: handIsVeryLow, forceOverride: true,
                    logWhenSet: GetAbductionRayChainContinueComboLogWhenSet());
                ForceAbductionRayFirstThisTurn(board, p, manaNow);
                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(-1800));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(8200));
                AddLog(GetAbductionRayChainContinueResolveLog());
                return;
            }

            if (softReserveChainRayWindow)
            {
                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(600));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(-1400));
                AddLog(GetAbductionRayChainSoftReserveResolveLog());
                return;
            }

            // 用户规则：未命中“连续挟持射线”环节时，不因低手牌主动起射线补资源。
            // 仅在临时牌/连发/硬锁窗口中才允许优先打射线。
            p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(950));
            p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(-2600));
            AddLog("挟持射线：未命中连续挟持射线环节，默认保留");
        }

        // 红牌：优先用于“解除嘲讽阻断斩杀线”，随后直接走脸。
        private void HandleRedCard(Board board, ProfileParameters p, int enemyHp, int friendAttack)
        {
            if (board == null || board.Hand == null || p == null)
                return;

            int manaNow = GetAvailableManaIncludingCoin(board);
            var red = board.Hand
                .Where(c => c != null && c.Template != null && c.Template.Id == RedCard)
                .OrderBy(c => c.CurrentCost)
                .ThenBy(c => c.Id)
                .FirstOrDefault();
            if (red == null || red.CurrentCost > manaNow)
                return;

            p.CastSpellsModifiers.AddOrUpdate(RedCard, new Modifier(-120));
            p.PlayOrderModifiers.AddOrUpdate(RedCard, new Modifier(240));

            if (board.MinionEnemy == null)
                return;

            var taunts = board.MinionEnemy
                .Where(m => m != null && m.Template != null && m.IsTaunt)
                .OrderByDescending(m => m.CurrentHealth)
                .ThenByDescending(m => m.CurrentAtk)
                .ToList();
            if (taunts.Count == 0)
                return;

            bool raceBlockedByTaunt = friendAttack >= enemyHp;
            if (raceBlockedByTaunt)
            {
                var target = taunts[0];
                SetSingleCardCombo(board, p, RedCard, allowCoinBridge: true, forceOverride: true,
                    logWhenSet: "红牌：ComboSet先手解锁走脸");
                p.CastSpellsModifiers.AddOrUpdate(RedCard, -3600, target.Id);
                p.PlayOrderModifiers.AddOrUpdate(RedCard, new Modifier(9999));
                AddLog("红牌：斩杀线被嘲讽阻断，先锁定 " + target.Template.Id + " 再走脸");

                // 该窗口下避免先拿己方随从去撞嘲讽送身材。
                foreach (var taunt in taunts)
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(taunt.Template.Id, new Modifier(-350));
                return;
            }

            // 非斩杀窗口：若嘲讽很厚且本回合不易低损解掉，则优先红牌点控。
            if (!CanClearAnyEnemyTauntWithFewAttackers(board, 2))
            {
                var target = taunts[0];
                p.CastSpellsModifiers.AddOrUpdate(RedCard, -900, target.Id);
                p.PlayOrderModifiers.AddOrUpdate(RedCard, new Modifier(2200));
                AddLog("红牌：高血嘲讽窗口，优先锁定后再组织进攻");
            }
        }

        // 背刺：0费解场法术，命中窗口时优先打掉，避免白白空过。
        private void HandleBackstab(Board board, ProfileParameters p, int friendHp, int enemyHp, int friendAttack)
        {
            if (board == null || board.Hand == null || p == null)
                return;

            int manaNow = GetAvailableManaIncludingCoin(board);
            var backstab = board.Hand
                .Where(c => c != null && c.Template != null && c.Template.Id == Backstab)
                .OrderBy(c => c.CurrentCost)
                .ThenBy(c => c.Id)
                .FirstOrDefault();
            if (backstab == null || backstab.CurrentCost > manaNow)
                return;

            if (board.MinionEnemy == null)
                return;

            var enemies = board.MinionEnemy.Where(m => m != null).ToList();
            if (enemies.Count == 0)
            {
                // 空场时轻微后置，避免无意义提前抢序。
                p.CastSpellsModifiers.AddOrUpdate(Backstab, new Modifier(220));
                p.PlayOrderModifiers.AddOrUpdate(Backstab, new Modifier(-1200));
                return;
            }

            // 用户规则：背刺优先锁定“高血/高威胁”的满状态目标，避免浪费在1血杂兵。
            var preferredBackstabTarget = enemies
                .Where(m => !m.IsDivineShield && m.CurrentHealth >= 2)
                .OrderByDescending(m => m.CurrentHealth)
                .ThenByDescending(m => m.CurrentAtk)
                .ThenByDescending(m => m.IsTaunt)
                .FirstOrDefault()
                ?? enemies
                    .Where(m => !m.IsDivineShield)
                    .OrderByDescending(m => m.CurrentAtk)
                    .ThenByDescending(m => m.CurrentHealth)
                    .FirstOrDefault();

            bool enemyHasTaunt = enemies.Any(m => m.IsTaunt);
            bool onBoardLethal = !enemyHasTaunt && friendAttack >= enemyHp;
            if (onBoardLethal)
            {
                // 已有直伤斩杀线时不强制插入背刺，避免影响走脸结算。
                p.CastSpellsModifiers.AddOrUpdate(Backstab, new Modifier(600));
                p.PlayOrderModifiers.AddOrUpdate(Backstab, new Modifier(-2600));
                AddLog("背刺：已具备场攻斩杀，后置不抢先");
                return;
            }

            int enemyAttack = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            bool underPressure = friendHp <= 12 && enemyAttack >= 8;
            bool hasKillableTwoHp = enemies.Any(m => m.CurrentHealth <= 2);
            bool hasHighAtkThreat = enemies.Any(m => m.CurrentAtk >= 3);

            // 默认在有敌方场面时前置，优先把0费解场资源转化掉。
            p.CastSpellsModifiers.AddOrUpdate(Backstab, new Modifier(-1500));
            p.PlayOrderModifiers.AddOrUpdate(Backstab, new Modifier(7600));
            if (preferredBackstabTarget != null)
                p.CastSpellsModifiers.AddOrUpdate(Backstab, -2200, preferredBackstabTarget.Id);

            if (hasKillableTwoHp || underPressure || hasHighAtkThreat || enemies.Count >= 2)
            {
                if (preferredBackstabTarget != null)
                    p.CastSpellsModifiers.AddOrUpdate(Backstab, -3600, preferredBackstabTarget.Id);
                else
                    p.CastSpellsModifiers.AddOrUpdate(Backstab, new Modifier(-3200));
                p.PlayOrderModifiers.AddOrUpdate(Backstab, new Modifier(9999));
                AddLog(hasKillableTwoHp
                    ? "背刺：命中2血可解目标，优先使用（高血目标优先）"
                    : "背刺：解场压力窗口，优先使用（高血目标优先）");
            }
        }

        private void ForceAbductionRayFirstThisTurn(Board board, ProfileParameters p, int manaNow, bool allowZeroCostMinions = false)
        {
            if (board == null || board.Hand == null)
                return;

            MarkAbductionRayPlannedThisTurn(board);

            foreach (var card in board.Hand.Where(c => c != null && c.Template != null))
            {
                if (card.Template.Id == AbductionRay || card.Template.Id == TheCoin)
                    continue;
                if (card.CurrentCost > manaNow)
                    continue;
                if (allowZeroCostMinions && card.Type == Card.CType.MINION && card.CurrentCost == 0)
                    continue;
                if (card.Template.Id == PartyFiend && board.MinionFriend != null && board.MinionFriend.Count >= 6)
                {
                    p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(9000));
                    p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(-9999));
                    continue;
                }

                if (card.Type == Card.CType.MINION)
                    p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(3600));
                else if (card.Type == Card.CType.SPELL)
                    p.CastSpellsModifiers.AddOrUpdate(card.Template.Id, new Modifier(3600));
                else if (card.Type == Card.CType.WEAPON)
                    p.CastWeaponsModifiers.AddOrUpdate(card.Template.Id, new Modifier(3600));

                p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(-9950));
            }
        }

        // 终局兜底：本回合已起射线且仍可打时，最后阶段再次锁定射线优先，避免被后续模块覆盖。
        private void EnforceAbductionRayContinuationFinalLock(Board board, ProfileParameters p)
        {
            if (board == null || p == null || board.Hand == null)
                return;
            if (!IsAbductionRayTurnUnlocked(board))
                return;

            int manaNow = GetAvailableManaIncludingCoin(board);
            if (manaNow <= 0)
                return;

            var playableRays = board.Hand
                .Where(c => c != null
                    && c.Template != null
                    && c.Template.Id == AbductionRay
                    && c.CurrentCost <= manaNow)
                .ToList();
            if (playableRays.Count == 0)
                return;

            bool rayContinuationNow = HasStartedOrLastPlayedAbductionRayThisTurn(board);
            if (!rayContinuationNow)
                return;

            bool hasTempRayWindow = HasTemporaryOrSketchGeneratedAbductionRay(board);
            if (!hasTempRayWindow && ShouldPreferKiljaedenImmediateOverRayChain(board, manaNow))
            {
                AddLog("挟持射线：终局校验命中基尔加丹直拍窗口，取消续链锁定让位基尔加丹");
                return;
            }

            bool allowOneMoreRayToTen = board.Hand.Count <= HearthstoneHandLimit - 1;
            if (IsAbductionRayOverdrawRiskHigh(board, allowOneMoreRayToTen))
            {
                var zeroCostRelease = GetRightmostPlayableZeroCostNonRayMinion(board, manaNow);
                if (zeroCostRelease != null)
                    return;
                AddLog("挟持射线：终局校验检测到爆牌风险但无0费腾手，保持续链锁定");
            }

            int friendHp = GetHeroHealth(board.HeroFriend);
            int enemyHp = GetHeroHealth(board.HeroEnemy);
            int friendAttack = GetBoardAttack(board.MinionFriend);
            int enemyAttack = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            bool dangerousHpWindow = IsDangerousHpTauntTempoWindow(friendHp, enemyAttack);
            bool hasPlayableTauntMinionNow = GetPlayableTauntMinionCardIds(board, manaNow).Count > 0;
            bool hasPlayableOutsideDeckTauntMinionNow = GetPlayableOutsideDeckTauntMinionCardIds(board, manaNow).Count > 0;
            bool rushClearWindow = ShouldPrioritizeRushMinionClear(board, friendHp, enemyHp, friendAttack, enemyAttack)
                && HasPlayableRushMinionForClear(board, manaNow);
            if ((dangerousHpWindow && hasPlayableTauntMinionNow) || rushClearWindow)
                return;
            if (hasPlayableOutsideDeckTauntMinionNow)
            {
                AddLog("挟持射线：终局校验命中套外嘲讽随从窗口，取消续链锁定让位嘲讽");
                return;
            }

            var preferredRay = playableRays
                .OrderByDescending(c => c.Id)
                .FirstOrDefault();

            if (preferredRay != null)
            {
                SetSingleCardComboByEntityId(board, p, preferredRay.Id, allowCoinBridge: true, forceOverride: true,
                    logWhenSet: "挟持射线：终局校验命中续链锁定");
                p.CastSpellsModifiers.AddOrUpdate(preferredRay.Id, new Modifier(-9999));
                p.PlayOrderModifiers.AddOrUpdate(preferredRay.Id, new Modifier(9999));
            }

            foreach (var ray in playableRays)
            {
                p.CastSpellsModifiers.AddOrUpdate(ray.Id, new Modifier(-9800));
                p.PlayOrderModifiers.AddOrUpdate(ray.Id, new Modifier(9999));
            }
            p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(-6200));
            p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(9999));

            foreach (var card in board.Hand.Where(c => c != null && c.Template != null && c.CurrentCost <= manaNow))
            {
                if (card.Template.Id == AbductionRay)
                    continue;

                if (card.Type == Card.CType.MINION)
                    p.CastMinionsModifiers.AddOrUpdate(card.Id, new Modifier(5600));
                else if (card.Type == Card.CType.SPELL)
                    p.CastSpellsModifiers.AddOrUpdate(card.Id, new Modifier(5600));
                else if (card.Type == Card.CType.WEAPON)
                    p.CastWeaponsModifiers.AddOrUpdate(card.Id, new Modifier(5600));

                p.PlayOrderModifiers.AddOrUpdate(card.Id, new Modifier(-9999));
            }

            AddLog("挟持射线：终局校验命中续链锁定，保持射线优先");
        }

        // 终局兜底：命中“本回合决定拍阿克蒙德”窗口时，再次锁定阿克蒙德第一优先，
        // 防止后续节奏模块把低费随从前置，挤占复活场位。
        private void EnforceArchimondeTopPriorityWhenCommitted(Board board, ProfileParameters p)
        {
            if (board == null || p == null || board.Hand == null)
                return;

            int manaNow = GetAvailableManaIncludingCoin(board);
            if (manaNow <= 0 || GetFreeBoardSlots(board) <= 0)
                return;

            var playableArchimonde = board.Hand
                .Where(c => c != null
                    && c.Template != null
                    && c.Template.Id == Archimonde
                    && c.CurrentCost <= manaNow)
                .OrderByDescending(c => c.Id)
                .FirstOrDefault();
            if (playableArchimonde == null)
                return;

            if (!ShouldCommitArchimondeThisTurn(board, manaNow))
                return;

            SetSingleCardComboByEntityId(board, p, playableArchimonde.Id,
                allowCoinBridge: true, forceOverride: true,
                logWhenSet: "阿克蒙德：终局校验命中前置锁定");
            ForceArchimondeFirstThisTurn(board, p, manaNow);
            p.CastMinionsModifiers.AddOrUpdate(playableArchimonde.Id, new Modifier(-9999));
            p.PlayOrderModifiers.AddOrUpdate(playableArchimonde.Id, new Modifier(9999));
            p.CastMinionsModifiers.AddOrUpdate(Archimonde, new Modifier(-7600));
            p.PlayOrderModifiers.AddOrUpdate(Archimonde, new Modifier(9999));
            p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(4200));
            AddLog("阿克蒙德：终局校验锁定第一优先，压后低费随从避免占用复活场位");
        }

        private bool ShouldCommitArchimondeThisTurn(Board board, int manaNow)
        {
            if (board == null || board.Hand == null)
                return false;
            if (!IsArchimondePlayableNow(board, manaNow))
                return false;
            if (GetFreeBoardSlots(board) <= 0)
                return false;

            int enemyHp = GetHeroHealth(board.HeroEnemy);
            bool enemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            int attackNowForLethal = GetAttackableBoardAttack(board.MinionFriend);
            bool onBoardLethalNow = !enemyHasTaunt && attackNowForLethal >= enemyHp;
            if (onBoardLethalNow)
                return false;

            int outsideDeckDemonsInBoardAndGrave = CountOutsideDeckDemonsInBoardAndGrave(board);
            bool readyByOutsideDemons = outsideDeckDemonsInBoardAndGrave >= 6;
            string boardAdvantageSnapshot;
            bool boardAdvantage = HasFriendlyBoardAdvantageForLegendaryDemons(board, out boardAdvantageSnapshot);

            int friendHp = GetHeroHealth(board.HeroFriend);
            int enemyAttack = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            bool dangerousHpTauntTempoWindow = IsDangerousHpTauntTempoWindow(friendHp, enemyAttack);

            bool enemyHasSurgingShipSurgeon = board.MinionEnemy != null
                && board.MinionEnemy.Any(m => m != null && m.Template != null && m.Template.Id == Card.Cards.CORE_WON_065);
            bool glacialShardPlayableNow = board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == GlacialShard
                && c.CurrentCost <= manaNow);
            if (dangerousHpTauntTempoWindow && enemyHasSurgingShipSurgeon && glacialShardPlayableNow)
                return false;

            if (HasSendableAttackBeforeArchimonde(board))
                return false;

            string outsideTauntDemonSnapshot;
            bool hasOutsideTauntDemonForArchimonde = HasOutsideDeckTauntDemonInBoardAndGrave(board, out outsideTauntDemonSnapshot);

            int masseridonInGrave = CountFriendGraveyard(board, UnreleasedMasseridon);
            int weaponEnemyAtk = board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0;
            int pulseKillCount = board.MinionEnemy == null ? 0 : board.MinionEnemy.Count(m => m != null && m.CurrentHealth <= 3);
            int enemyAttackAfterPulse = (board.MinionEnemy == null ? 0 : board.MinionEnemy
                .Where(m => m != null && m.CurrentHealth > 3)
                .Sum(m => Math.Max(0, m.CurrentAtk))) + weaponEnemyAtk;
            int pulseAttackDrop = Math.Max(0, enemyAttack - enemyAttackAfterPulse);
            bool highValueMasseridonReviveWindow = masseridonInGrave > 0
                && (pulseKillCount >= 2
                    || pulseAttackDrop >= 4
                    || enemyHp <= 6
                    || dangerousHpTauntTempoWindow
                    || enemyAttack >= 8);

            bool dangerousTauntRescueWindow = dangerousHpTauntTempoWindow && hasOutsideTauntDemonForArchimonde;
            bool turnTenImmediateArchimondeWindow = board.MaxMana >= 10 && readyByOutsideDemons;
            bool baselineReadyWindow = readyByOutsideDemons && boardAdvantage;

            return highValueMasseridonReviveWindow
                || dangerousTauntRescueWindow
                || turnTenImmediateArchimondeWindow
                || baselineReadyWindow;
        }

        private bool HasSendableAttackBeforeArchimonde(Board board)
        {
            if (board == null || board.MinionFriend == null || board.MinionEnemy == null)
                return false;
            if (board.MinionEnemy.Count == 0)
                return false;

            var attackableFriends = board.MinionFriend
                .Where(m => m != null && m.Template != null && m.CanAttack && m.CurrentAtk > 0)
                .ToList();
            if (attackableFriends.Count == 0)
                return false;

            var allEnemyTargets = board.MinionEnemy
                .Where(m => m != null && m.Template != null)
                .ToList();
            if (allEnemyTargets.Count == 0)
                return false;

            return attackableFriends.Any(friend => allEnemyTargets.Any(enemy => enemy.CurrentAtk >= friend.CurrentHealth));
        }

        // 终局兜底：第一回合若可下滑矛纳迦，首手必须先下滑矛，防止后续模块改写为其它1费动作。
        private void EnforceFirstTurnSlitherspearOpeningLock(Board board, ProfileParameters p)
        {
            if (board == null || p == null || board.Hand == null)
                return;
            if (!IsFirstTurnSlitherspearPriorityWindow(board))
                return;

            int realManaNow = Math.Max(0, board.ManaAvailable);
            if (realManaNow != Math.Max(0, board.MaxMana))
                return;

            int manaNow = GetAvailableManaIncludingCoin(board);
            if (manaNow <= 0 || GetFreeBoardSlots(board) <= 0)
                return;

            var playableSlitherspears = board.Hand
                .Where(c => c != null
                    && c.Template != null
                    && c.Template.Id == ViciousSlitherspear
                    && c.CurrentCost <= manaNow)
                .OrderByDescending(c => c.Id)
                .ToList();
            if (playableSlitherspears.Count == 0)
                return;

            var preferredSlitherspear = playableSlitherspears[0];
            SetSingleCardComboByEntityId(board, p, preferredSlitherspear.Id,
                allowCoinBridge: false, forceOverride: true,
                logWhenSet: "滑矛纳迦：终局校验命中第一回合首手锁定");
            p.CastMinionsModifiers.AddOrUpdate(preferredSlitherspear.Id, new Modifier(-9999));
            p.PlayOrderModifiers.AddOrUpdate(preferredSlitherspear.Id, new Modifier(9999));
            p.CastMinionsModifiers.AddOrUpdate(ViciousSlitherspear, new Modifier(-8200));
            p.PlayOrderModifiers.AddOrUpdate(ViciousSlitherspear, new Modifier(9999));

            foreach (var card in board.Hand.Where(c => c != null && c.Template != null && c.CurrentCost <= manaNow))
            {
                if (card.Id == preferredSlitherspear.Id || card.Template.Id == ViciousSlitherspear)
                    continue;

                if (card.Template.Id == TheCoin)
                {
                    p.CastSpellsModifiers.AddOrUpdate(card.Id, new Modifier(3800));
                    p.PlayOrderModifiers.AddOrUpdate(card.Id, new Modifier(-9999));
                    continue;
                }

                if (card.Type == Card.CType.MINION)
                    p.CastMinionsModifiers.AddOrUpdate(card.Id, new Modifier(6200));
                else if (card.Type == Card.CType.SPELL)
                    p.CastSpellsModifiers.AddOrUpdate(card.Id, new Modifier(6200));
                else if (card.Type == Card.CType.WEAPON)
                    p.CastWeaponsModifiers.AddOrUpdate(card.Id, new Modifier(6200));

                p.PlayOrderModifiers.AddOrUpdate(card.Id, new Modifier(-9999));
            }

            AddLog("滑矛纳迦：终局校验首手锁定，避免被后续规则改写为其他1费动作");
        }

        private Card GetRightmostPlayableZeroCostNonRayMinion(Board board, int manaNow)
        {
            if (board == null || board.Hand == null)
                return null;
            if (GetFreeBoardSlots(board) <= 0)
                return null;

            Card fallback = null;
            for (int i = board.Hand.Count - 1; i >= 0; i--)
            {
                var c = board.Hand[i];
                if (c == null || c.Template == null)
                    continue;
                if (c.Type != Card.CType.MINION)
                    continue;
                if (c.CurrentCost != 0 || c.CurrentCost > manaNow)
                    continue;
                // 用户规则：强推0费随从时排除凶魔城堡（TOY_526），避免高风险自伤/副作用抢先放行。
                if (string.Equals(c.Template.Id.ToString(), "TOY_526", StringComparison.Ordinal))
                    continue;

                // 用户规则：可下0费时优先狂飙邪魔（VAC_927），再考虑其他0费随从。
                bool isRushDemon = string.Equals(c.Template.Id.ToString(), "VAC_927", StringComparison.Ordinal);
                if (isRushDemon)
                    return c;

                if (fallback == null)
                    fallback = c;
            }

            return fallback;
        }

        private Card GetRightmostPlayableOneCostNonRayMinion(Board board, int manaNow)
        {
            if (board == null || board.Hand == null)
                return null;
            if (GetFreeBoardSlots(board) <= 0)
                return null;

            for (int i = board.Hand.Count - 1; i >= 0; i--)
            {
                var c = board.Hand[i];
                if (c == null || c.Template == null)
                    continue;
                if (c.Type != Card.CType.MINION)
                    continue;
                if (c.Template.Id == AbductionRay)
                    continue;
                if (c.CurrentCost != 1 || c.CurrentCost > manaNow)
                    continue;
                return c;
            }

            return null;
        }

        private List<Card.Cards> GetPlayableTauntMinionCardIds(Board board, int manaNow)
        {
            var result = new List<Card.Cards>();
            if (board == null || board.Hand == null)
                return result;
            if (GetFreeBoardSlots(board) <= 0)
                return result;

            foreach (var card in board.Hand.Where(c => c != null && c.Template != null))
            {
                if (card.Type != Card.CType.MINION)
                    continue;
                if (card.CurrentCost > manaNow)
                    continue;
                if (!IsTauntMinionForSurvival(card))
                    continue;
                if (!result.Contains(card.Template.Id))
                    result.Add(card.Template.Id);
            }

            return result;
        }

        private bool IsTauntMinionForSurvival(Card card)
        {
            if (card == null || card.Template == null || card.Type != Card.CType.MINION)
                return false;

            // 优先读实时TAG，兼容临时牌/减费牌/发现牌等动态来源。
            if (GetTag(card, Card.GAME_TAG.TAUNT) == 1)
                return true;

            // 兜底：部分来源在手牌阶段可能不带TAUNT标签，保留已知保命嘲讽白名单。
            return IsKnownSurvivalTauntMinion(card.Template.Id);
        }

        private static bool IsKnownSurvivalTauntMinion(Card.Cards cardId)
        {
            var cardIdText = cardId.ToString();

            // 第二层优先：仅使用“标准/构筑相关”恶魔嘲讽子集做手牌缺失TAG兜底。
            if (KnownDemonTauntConstructedCardIds.Contains(cardIdText))
                return true;

            // 模式专用（BATTLEGROUNDS/LETTUCE/Story/Prologue）默认不参与兜底，按需再打开。
            if (EnableModeOnlyDemonTauntFallback && KnownDemonTauntModeOnlyCardIds.Contains(cardIdText))
                return true;

            return false;
        }

        // 续连熵能：典型横向铺场增益，空场严禁使用
        private void HandleEntropicContinuity(Board board, ProfileParameters p, int friendHp)
        {
            int count = board.MinionFriend.Count;
            int manaNow = GetAvailableManaIncludingCoin(board);
            bool moargPriorityWindow = ShouldPrioritizeMoargForgefiendNow(board);
            bool rayPlayableNow = IsAbductionRayPlayableNow(board);
            bool forceRayNow = ShouldForceAbductionRayNow(board);
            var wispCard = board.Hand
                .Where(c => c != null && c.Template != null && c.Template.Id == Wisp)
                .OrderBy(c => c.CurrentCost)
                .FirstOrDefault();
            var partyCard = board.Hand
                .Where(c => c != null && c.Template != null && c.Template.Id == PartyFiend)
                .OrderBy(c => c.CurrentCost)
                .FirstOrDefault();
            var entropyCard = board.Hand
                .Where(c => c != null && c.Template != null && c.Template.Id == EntropicContinuity)
                .OrderBy(c => c.CurrentCost)
                .FirstOrDefault();
            var glacialCard = board.Hand
                .Where(c => c != null && c.Template != null && c.Template.Id == GlacialShard)
                .OrderBy(c => c.CurrentCost)
                .FirstOrDefault();
            if (entropyCard == null || entropyCard.CurrentCost > manaNow)
                return;

            // 用户规则：命中“墓先手”窗口时，续连必须后置到墓之后。
            if (ShouldForceTombFirstThisTurn(board))
            {
                p.CastSpellsModifiers.AddOrUpdate(EntropicContinuity, new Modifier(5200));
                p.PlayOrderModifiers.AddOrUpdate(EntropicContinuity, new Modifier(-9900));
                AddLog("续连熵能：咒怨之墓回合，后置到墓之后");
                return;
            }

            // 用户规则：若手里有可下的临时奇利亚斯，且本回合无法“奇利亚斯+续连”同回合完成，
            // 则续连必须后置，避免临时奇利亚斯过回合流失。
            var temporaryZilliax = GetPlayableTemporaryZilliax(board, manaNow);
            if (temporaryZilliax != null && temporaryZilliax.CurrentCost + entropyCard.CurrentCost > manaNow)
            {
                p.CastSpellsModifiers.AddOrUpdate(EntropicContinuity, new Modifier(5600));
                p.PlayOrderModifiers.AddOrUpdate(EntropicContinuity, new Modifier(-9999));
                AddLog("续连熵能：临时奇利亚斯可下且同回合无法兼容，后置保留续连");
                return;
            }

            bool enemyHasMinion = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null);

            // 用户硬规则：
            // 1) 敌方有随从：当前友方随从<2时禁用续连熵能；
            // 2) 敌方空场：低费（<3）时仅在当前友方随从<3才禁用。
            if (enemyHasMinion && count <= 1)
            {
                p.CastSpellsModifiers.AddOrUpdate(EntropicContinuity, new Modifier(9999));
                p.PlayOrderModifiers.AddOrUpdate(EntropicContinuity, new Modifier(-9999));
                AddLog("续连熵能：敌方有随从且友方随从<2，硬规则禁用");
                return;
            }
            if (!enemyHasMinion && manaNow < 3 && count < 3)
            {
                p.CastSpellsModifiers.AddOrUpdate(EntropicContinuity, new Modifier(9999));
                p.PlayOrderModifiers.AddOrUpdate(EntropicContinuity, new Modifier(-9999));
                AddLog("续连熵能：敌方空场且可用费<3且友方随从<3，硬规则禁用");
                return;
            }
            if (count <= 0)
            {
                p.CastSpellsModifiers.AddOrUpdate(EntropicContinuity, new Modifier(9999));
                p.PlayOrderModifiers.AddOrUpdate(EntropicContinuity, new Modifier(-9999));
                AddLog("续连熵能：友方空场，硬规则禁用");
                return;
            }

            // 用户硬规则：挟持射线可用时，续连熵能不应抢在射线之前打出。
            // 例外：若本回合续连熵能可直接形成走脸斩杀，则允许继续执行后续续连逻辑。
            int enemyHp = GetHeroHealth(board.HeroEnemy);
            bool enemyHasTaunt = board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            int canAttackDamageNow = board.MinionFriend
                .Where(m => m != null && m.CanAttack)
                .Sum(m => Math.Max(0, m.CurrentAtk));
            int entropyBuffGain = Math.Max(0, count);
            bool directLethalNow = !enemyHasTaunt && canAttackDamageNow >= enemyHp;
            bool entropyLethalNow = !enemyHasTaunt && (canAttackDamageNow + entropyBuffGain >= enemyHp);
            bool preferEntropyOverStorytellerNow = ShouldPreferEntropicOverStoryteller(board, manaNow);
            bool storytellerPriorityWindow = IsHighValueStorytellerPlayableNow(board, manaNow)
                && !ShouldForceAbductionRayNow(board)
                && !preferEntropyOverStorytellerNow;

            // 用户反馈：高压回合莫尔葛熔魔可下时，续连后置让位熔魔稳场。
            if (moargPriorityWindow && !entropyLethalNow)
            {
                p.CastSpellsModifiers.AddOrUpdate(EntropicContinuity, new Modifier(3600));
                p.PlayOrderModifiers.AddOrUpdate(EntropicContinuity, new Modifier(-9800));
                AddLog("续连熵能：高压且莫尔葛熔魔可用，后置让位熔魔稳场");
                return;
            }

            // 用户规则：已具备场攻斩杀时，续连熵能后置，不抢攻击顺序。
            if (directLethalNow)
            {
                p.CastSpellsModifiers.AddOrUpdate(EntropicContinuity, new Modifier(2600));
                p.PlayOrderModifiers.AddOrUpdate(EntropicContinuity, new Modifier(-9800));
                AddLog("续连熵能：已具备场攻斩杀，后置不抢先");
                return;
            }

            // 用户规则：始祖龟高收益窗口可落地时，续连后置让位始祖龟。
            if (storytellerPriorityWindow && !entropyLethalNow && !preferEntropyOverStorytellerNow)
            {
                p.CastSpellsModifiers.AddOrUpdate(EntropicContinuity, new Modifier(3600));
                p.PlayOrderModifiers.AddOrUpdate(EntropicContinuity, new Modifier(-9800));
                AddLog("续连熵能：始祖龟高收益可落地，后置让位始祖龟");
                return;
            }
            if (storytellerPriorityWindow && preferEntropyOverStorytellerNow)
                AddLog("续连熵能：覆盖收益高于始祖龟，保持续连优先");

            if (rayPlayableNow && !entropyLethalNow)
            {
                p.CastSpellsModifiers.AddOrUpdate(EntropicContinuity, new Modifier(3200));
                p.PlayOrderModifiers.AddOrUpdate(EntropicContinuity, new Modifier(-9800));
                AddLog(forceRayNow
                    ? "续连熵能：命中射线锁定窗口，后置到挟持射线之后"
                    : "续连熵能：挟持射线可用，未先用射线前后置不抢先");
                return;
            }

            int wispCost = wispCard != null ? wispCard.CurrentCost : 99;
            int partyCost = partyCard != null ? partyCard.CurrentCost : 99;
            int entropyCost = entropyCard != null ? entropyCard.CurrentCost : 99;
            int minCoverageBeforeEntropy = enemyHasMinion ? 2 : 3;
            bool canReachCoverageBeforeEntropy = count >= minCoverageBeforeEntropy;
            if (!canReachCoverageBeforeEntropy)
            {
                p.CastSpellsModifiers.AddOrUpdate(EntropicContinuity, new Modifier(4200));
                p.PlayOrderModifiers.AddOrUpdate(EntropicContinuity, new Modifier(-9800));
                AddLog(enemyHasMinion
                    ? "续连熵能：敌方有随从且本回合友方覆盖<2，强制后置"
                    : "续连熵能：敌方空场且本回合友方覆盖<3，强制后置");
                return;
            }
            bool canPlayWispThenEntropySameTurn = wispCost <= manaNow
                && entropyCost <= manaNow
                && manaNow >= wispCost + entropyCost
                && board.MinionFriend.Count >= 1
                && board.MinionFriend.Count <= 6;
            bool canPlayGlacialThenEntropySameTurn = glacialCard != null
                && glacialCard.CurrentCost <= manaNow
                && entropyCost <= manaNow
                && manaNow >= glacialCard.CurrentCost + entropyCost
                && GetFreeBoardSlots(board) > 0
                && board.MinionFriend.Count <= 6;
            bool canPlayPartyThenEntropySameTurn = partyCost <= manaNow
                && entropyCost <= manaNow
                && manaNow >= partyCost + entropyCost
                && board.MinionFriend.Count <= 4
                && friendHp > 8;
            bool canPlayPartyThenZilliaxSameTurn = CanPlayPartyThenZilliaxSameTurn(board, manaNow);
            bool canPlayZilliaxThenEntropySameTurn = CanPlayZilliaxThenEntropySameTurn(board, manaNow);
            // 通用规则：费用允许时，先下其他随从再打续连熵能，扩大buff覆盖。
            bool canPlayOtherMinionThenEntropySameTurn = board.MinionFriend.Count <= 6
                && board.Hand.Any(c => c != null
                    && c.Template != null
                    && c.Type == Card.CType.MINION
                    && c.Template.Id != Wisp
                    && c.Template.Id != PartyFiend
                    && c.Template.Id != ZilliaxTickingPower
                    && c.Template.Id != ZilliaxDeckBuilder
                    && c.Template.Id != EntropicContinuity
                    && c.CurrentCost <= Math.Max(0, manaNow - entropyCost));

            int friendlyCountNow = count;
            bool hasPlayableNonEntropyMinionNow = GetFreeBoardSlots(board) > 0
                && board.Hand.Any(c => c != null
                    && c.Template != null
                    && c.Type == Card.CType.MINION
                    && c.Template.Id != EntropicContinuity
                    && c.CurrentCost <= manaNow);
            bool canExpandThenEntropyThisTurn = canPlayWispThenEntropySameTurn
                || canPlayGlacialThenEntropySameTurn
                || canPlayPartyThenEntropySameTurn
                || canPlayOtherMinionThenEntropySameTurn;
            bool lowCoverageEntropyWindow = friendlyCountNow <= 2;
            if (hasPlayableNonEntropyMinionNow
                && !canExpandThenEntropyThisTurn
                && lowCoverageEntropyWindow
                && !entropyLethalNow)
            {
                p.CastSpellsModifiers.AddOrUpdate(EntropicContinuity, new Modifier(3600));
                p.PlayOrderModifiers.AddOrUpdate(EntropicContinuity, new Modifier(-9800));
                AddLog("续连熵能：可下随从且当前覆盖偏低(友方<=2)，后置让位随从先落地");
                return;
            }

            // 用户规则：若续连对“当回合交换”有明显收益（尤其敌方有嘲讽），
            // 则续连应优先于继续铺场，不再后置让位随从。
            bool enemyHasTauntForEntropy = enemyHasMinion
                && board.MinionEnemy != null
                && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            bool entropyCombatPriorityWindow = enemyHasMinion
                && friendlyCountNow >= 3
                && (enemyHasTauntForEntropy || (board.MinionEnemy != null && board.MinionEnemy.Count(m => m != null) >= 2));
            if (entropyCombatPriorityWindow)
            {
                canPlayWispThenEntropySameTurn = false;
                canPlayGlacialThenEntropySameTurn = false;
                canPlayPartyThenEntropySameTurn = false;
                canPlayOtherMinionThenEntropySameTurn = false;
                AddLog(enemyHasTauntForEntropy
                    ? "续连熵能：敌方有嘲讽且友方覆盖充足，优先先打buff不让位铺场"
                    : "续连熵能：敌方有场且友方覆盖充足，优先先打buff不让位铺场");
            }

            int friendlyCountForEntropyPriority = count;
            if (friendlyCountForEntropyPriority == 0)
            {
                p.CastSpellsModifiers.AddOrUpdate(EntropicContinuity, new Modifier(350));
            }
            else if (friendlyCountForEntropyPriority == 1)
            {
                p.CastSpellsModifiers.AddOrUpdate(EntropicContinuity, new Modifier(60));
            }
            else if (friendlyCountForEntropyPriority == 2)
            {
                if (enemyHasMinion)
                {
                    p.CastSpellsModifiers.AddOrUpdate(EntropicContinuity, new Modifier(520));
                    p.PlayOrderModifiers.AddOrUpdate(EntropicContinuity, new Modifier(-2600));
                    AddLog("续连熵能：敌方有随从且友方随从=2，轻微后置等待更高覆盖");
                }
                else
                {
                    p.CastSpellsModifiers.AddOrUpdate(EntropicContinuity, new Modifier(420));
                    p.PlayOrderModifiers.AddOrUpdate(EntropicContinuity, new Modifier(-2200));
                    AddLog("续连熵能：敌方空场且友方随从=2，轻微后置");
                }
            }
            else
            {
                // 用户规则：至少3个友方随从才进入强优先窗口，且覆盖越多优先级越高。
                int value = -900 - ((friendlyCountForEntropyPriority - 3) * 450);
                if (value < -3200) value = -3200;
                p.CastSpellsModifiers.AddOrUpdate(EntropicContinuity, new Modifier(value));

                int order = 1500 + ((friendlyCountForEntropyPriority - 3) * 600);
                if (order > 4200) order = 4200;
                p.PlayOrderModifiers.AddOrUpdate(EntropicContinuity, new Modifier(order));
                AddLog("续连熵能：友方随从=" + friendlyCountForEntropyPriority + "，强优先使用");
            }

            // 用户规则：放宽优先级阈值按敌方场面动态调整（按友方覆盖）：
            // 敌方有随从/空场统一要求>=3，覆盖不足时先铺随从。
            int minCountForEntropyPriority = 3;
            if (friendlyCountForEntropyPriority >= minCountForEntropyPriority && manaNow >= 2)
            {
                p.CastSpellsModifiers.AddOrUpdate(EntropicContinuity, new Modifier(-3200));
                p.PlayOrderModifiers.AddOrUpdate(EntropicContinuity, new Modifier(4800));
                AddLog("续连熵能：满足动态可用窗口（友方覆盖>=3），放宽优先级");
            }

            // 该牌会洗入时空撕裂（抽到会自伤），低血长局适当保守。
            if (count <= 2 && friendHp <= 10 && board.FriendDeckCount <= 8)
                p.CastSpellsModifiers.AddOrUpdate(EntropicContinuity, new Modifier(130));

            if (friendlyCountForEntropyPriority >= 3 && board.MinionFriend.Any(m => m != null && m.Template != null && m.Template.Id == ViciousSlitherspear))
            {
                p.CastSpellsModifiers.AddOrUpdate(EntropicContinuity, new Modifier(-3000));
                p.PlayOrderModifiers.AddOrUpdate(EntropicContinuity, new Modifier(4200));
            }
            else if (friendlyCountForEntropyPriority <= 1)
            {
                p.PlayOrderModifiers.AddOrUpdate(EntropicContinuity, new Modifier(220));
            }

            // 用户硬规则：能先下小精灵时，续连熵能后置到小精灵之后。
            if (canPlayWispThenEntropySameTurn)
            {
                p.CastSpellsModifiers.AddOrUpdate(EntropicContinuity, new Modifier(520));
                p.PlayOrderModifiers.AddOrUpdate(EntropicContinuity, new Modifier(-4200));
                AddLog("续连熵能：可先下小精灵扩大覆盖，后置到小精灵之后");
            }

            // 用户口径：邪犬可同回合落地时，先邪犬再续连熵能，避免续连抢先打掉费用。
            if (canPlayPartyThenEntropySameTurn)
            {
                p.CastSpellsModifiers.AddOrUpdate(EntropicContinuity, new Modifier(canPlayPartyThenZilliaxSameTurn ? 900 : 420));
                p.PlayOrderModifiers.AddOrUpdate(EntropicContinuity, new Modifier(canPlayPartyThenZilliaxSameTurn ? -6800 : -3200));
                AddLog(canPlayPartyThenZilliaxSameTurn
                    ? "续连熵能：同回合先邪犬后奇利亚斯，续连后置到奇利亚斯之后"
                    : "续连熵能：同回合可先下派对邪犬，后置到邪犬之后");
            }

            // 用户硬规则：同回合可“奇利亚斯 + 续连熵能”时，必须先奇利亚斯再buff。
            if (canPlayZilliaxThenEntropySameTurn && !canPlayPartyThenZilliaxSameTurn)
            {
                // 强制固定顺序：奇利亚斯 -> 续连熵能，防止后续逻辑把续连抢到前面。
                bool setCombo = SetTwoCardCombo(board, p, ZilliaxTickingPower, EntropicContinuity,
                    allowCoinBridge: true, forceOverride: true,
                    logWhenSet: "续连熵能：ComboSet先奇利亚斯后续连");
                if (!setCombo)
                {
                    SetTwoCardCombo(board, p, ZilliaxDeckBuilder, EntropicContinuity,
                        allowCoinBridge: true, forceOverride: true,
                        logWhenSet: "续连熵能：ComboSet先奇利亚斯后续连");
                }

                p.CastSpellsModifiers.AddOrUpdate(EntropicContinuity, new Modifier(780));
                p.PlayOrderModifiers.AddOrUpdate(EntropicContinuity, new Modifier(-5600));
                AddLog("续连熵能：同回合可先奇利亚斯再buff，后置到奇利亚斯之后");
            }

            // 用户规则：同回合有冰川裂片+续连熵能时，先冰片再buff。
            if (canPlayGlacialThenEntropySameTurn)
            {
                SetTwoCardCombo(board, p, GlacialShard, EntropicContinuity,
                    allowCoinBridge: true, forceOverride: false,
                    logWhenSet: "续连熵能：ComboSet先冰片后续连");
                p.CastSpellsModifiers.AddOrUpdate(EntropicContinuity, new Modifier(560));
                p.PlayOrderModifiers.AddOrUpdate(EntropicContinuity, new Modifier(-3600));
                AddLog("续连熵能：可先下冰川裂片再buff，后置到冰片之后");
            }

            if (canPlayOtherMinionThenEntropySameTurn)
            {
                p.CastSpellsModifiers.AddOrUpdate(EntropicContinuity, new Modifier(520));
                p.PlayOrderModifiers.AddOrUpdate(EntropicContinuity, new Modifier(-3000));
                AddLog("续连熵能：同回合可先下其他随从扩大覆盖，后置到随从之后");
            }

            // 硬化顺序：本回合若可“先铺怪再续连”，直接固化为 ComboSet，避免先buff/先攻插队。
            if (canPlayWispThenEntropySameTurn
                || canPlayPartyThenEntropySameTurn
                || canPlayGlacialThenEntropySameTurn
                || canPlayOtherMinionThenEntropySameTurn)
            {
                bool forcedMinionThenEntropy = SetPlayableMinionsThenEntropyCombo(board, p,
                    allowCoinBridge: true, forceOverride: true,
                    logWhenSet: "续连熵能：ComboSet先铺随从后续连（强制）");
                if (forcedMinionThenEntropy)
                {
                    // 用户规则：buff后置到可下随从之后，避免出现“先buff再下邪犬/小精灵”。
                    p.CastSpellsModifiers.AddOrUpdate(EntropicContinuity, new Modifier(2200));
                    p.PlayOrderModifiers.AddOrUpdate(EntropicContinuity, new Modifier(-9800));
                    AddLog("续连熵能：命中先铺随从后buff硬规则，强制后置");
                }
            }

            // 用户口径：只要满足先抽后续连条件（含极限血线>=3），先抽一口再决定是否补下随从，最后再上续连熵能。
            if (ShouldTapBeforeEntropicContinuity(board, friendHp))
            {
                p.CastSpellsModifiers.AddOrUpdate(EntropicContinuity, new Modifier(420));
                p.PlayOrderModifiers.AddOrUpdate(EntropicContinuity, new Modifier(-2600));
                AddLog("续连熵能：命中先抽后buff规则，后置到分流之后");
            }
        }

        // 香蕉：可执行时视作关键buff动作，优先在攻击前使用（尤其用于换掉高价值敌方随从）。
        private void HandleBanana(Board board, ProfileParameters p, int enemyHp)
        {
            if (board == null || board.Hand == null || board.MinionFriend == null)
                return;

            int manaNow = GetAvailableManaIncludingCoin(board);
            var banana = board.Hand
                .Where(c => c != null && c.Template != null
                    && c.Template.Id == Banana
                    && c.CurrentCost <= manaNow)
                .OrderBy(c => c.CurrentCost)
                .ThenBy(c => c.Id)
                .FirstOrDefault();
            if (banana == null)
                return;

            bool hasAttackableFriend = board.MinionFriend.Any(m => m != null && m.CanAttack && m.CurrentAtk > 0);
            bool enemyHasMinion = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null);
            if (!hasAttackableFriend || !enemyHasMinion)
                return;

            bool enemyHasTaunt = board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            int canAttackDamageNow = board.MinionFriend
                .Where(m => m != null && m.CanAttack)
                .Sum(m => Math.Max(0, m.CurrentAtk));
            if (!enemyHasTaunt && canAttackDamageNow >= enemyHp)
                return;

            p.CastSpellsModifiers.AddOrUpdate(Banana, new Modifier(-2800));
            p.PlayOrderModifiers.AddOrUpdate(Banana, new Modifier(9800));
            SetSingleCardCombo(board, p, Banana, allowCoinBridge: true, forceOverride: false,
                logWhenSet: "香蕉：ComboSet攻前buff优先");
            AddLog("香蕉：可执行攻前换场，优先在攻击前使用");
        }

        // 咒怨之墓：发现并临时化，重点是“有费用打掉临时牌”
        private void HandleTombOfSuffering(Board board, ProfileParameters p)
        {
            if (!board.HasCardInHand(TombOfSuffering))
                return;

            int manaNow = GetAvailableManaIncludingCoin(board);
            // 用户硬规则：没费用时不打墓（避免“只发现但无法当回合转化”）。
            if (manaNow <= 0)
            {
                p.CastSpellsModifiers.AddOrUpdate(TombOfSuffering, new Modifier(9999));
                p.PlayOrderModifiers.AddOrUpdate(TombOfSuffering, new Modifier(-9999));
                AddLog("咒怨之墓：可用费=0，硬规则禁用");
                return;
            }
            bool prioritizeSketchArtistWindow = ShouldPrioritizeSketchArtistOverTomb(board, manaNow);
            bool sketchPlayableAtFourPlusWindow = manaNow >= 4
                && GetFreeBoardSlots(board) > 0
                && board.Hand.Any(c => c != null
                    && c.Template != null
                    && c.Template.Id == SketchArtist
                    && c.CurrentCost <= manaNow);
            bool reserveTombForRayWindow = ShouldReserveTombForAbductionRay(board, manaNow);
            bool prioritizeShadowflameStalkerWindow = ShouldPrioritizeShadowflameStalkerOverTomb(board, manaNow);

            // 用户规则：如果本回合要用速写美术家或挟持射线，则本回合不必使用墓。
            if (prioritizeSketchArtistWindow || sketchPlayableAtFourPlusWindow || reserveTombForRayWindow)
            {
                p.CastSpellsModifiers.AddOrUpdate(TombOfSuffering, new Modifier(9999));
                p.PlayOrderModifiers.AddOrUpdate(TombOfSuffering, new Modifier(-9999));
                if ((prioritizeSketchArtistWindow || sketchPlayableAtFourPlusWindow) && reserveTombForRayWindow)
                    AddLog("咒怨之墓：本回合将走速写美术家/挟持射线链路，硬规则禁用");
                else if (prioritizeSketchArtistWindow || sketchPlayableAtFourPlusWindow)
                    AddLog("咒怨之墓：命中速写美术家先手窗口，本回合硬规则禁用");
                else
                    AddLog("咒怨之墓：本回合已决定挟持射线，硬规则禁用");
                return;
            }
            // 用户规则：4费且手里无挟持射线/速写时，若影焰猎豹可下，则墓不抢先。
            if (prioritizeShadowflameStalkerWindow)
            {
                p.CastSpellsModifiers.AddOrUpdate(TombOfSuffering, new Modifier(5200));
                p.PlayOrderModifiers.AddOrUpdate(TombOfSuffering, new Modifier(-9900));
                AddLog("咒怨之墓：4费无挟持射线/速写且影焰猎豹可下，后置让位影焰猎豹");
                return;
            }
            bool enemyHasMinion = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null);
            int friendlyMinionCount = board.MinionFriend != null ? board.MinionFriend.Count(m => m != null) : 0;
            bool hasViciousInvaderOnBoard = board.MinionFriend != null
                && board.MinionFriend.Any(m => m != null
                    && m.Template != null
                    && string.Equals(m.Template.Id.ToString(), "GDB_226", StringComparison.Ordinal));
            bool hasPlayableViciousInvader = board.Hand != null
                && board.Hand.Any(c => c != null
                    && c.Template != null
                    && c.Type == Card.CType.MINION
                    && string.Equals(c.Template.Id.ToString(), "GDB_226", StringComparison.Ordinal)
                    && c.CurrentCost <= manaNow);
            // 用户规则：高压解场回合不先墓，避免“先发现但没解场”导致节奏断档。
            if (ShouldDeferTombForPressure(board, manaNow))
            {
                p.CastSpellsModifiers.AddOrUpdate(TombOfSuffering, new Modifier(5200));
                p.PlayOrderModifiers.AddOrUpdate(TombOfSuffering, new Modifier(-9800));
                AddLog("咒怨之墓：高压解场回合，硬规则禁用先墓");
                return;
            }

            // 用户规则：凶恶的入侵者在场且己方已铺开时，不先墓，避免法术链导致己方场面被清空。
            if (hasViciousInvaderOnBoard && friendlyMinionCount >= 3)
            {
                p.CastSpellsModifiers.AddOrUpdate(TombOfSuffering, new Modifier(9800));
                p.PlayOrderModifiers.AddOrUpdate(TombOfSuffering, new Modifier(-9999));
                AddLog("咒怨之墓：凶恶的入侵者在场且己方已铺场，硬规则禁用先墓（避免法术反噬清场）");
                return;
            }

            // 无可发现低费目标时，不进入“先墓”。
            int affordableDeckTargets = CountDeckCardsAtMostCost(board, manaNow);
            if (affordableDeckTargets <= 0)
            {
                p.CastSpellsModifiers.AddOrUpdate(TombOfSuffering, new Modifier(9999));
                p.PlayOrderModifiers.AddOrUpdate(TombOfSuffering, new Modifier(-9000));
                AddLog("咒怨之墓：未满足硬规则（可用费=" + manaNow + "，可发现低费目标数=" + affordableDeckTargets + "）-> 本回合禁用");
                return;
            }

            // 用户反馈：3费起若墓可用，默认直接先墓，不再让位低费节奏/分流。
            if (manaNow >= 3)
            {
                ForceTombFirstThisTurn(board, p, manaNow);
                SetSingleCardCombo(board, p, TombOfSuffering, allowCoinBridge: false, forceOverride: true,
                    logWhenSet: "咒怨之墓：3费起ComboSet强制先手");
                p.CastSpellsModifiers.AddOrUpdate(TombOfSuffering, new Modifier(-7600));
                p.PlayOrderModifiers.AddOrUpdate(TombOfSuffering, new Modifier(9999));
                AddLog("咒怨之墓：3费起强制先手窗口，优先先墓");
                return;
            }

            bool holdForOneDrop = (manaNow == 1) && HasPlayableMinionAtExactCost(board, manaNow, 1);
            bool holdForTwoDrop = (manaNow == 2) && HasPlayableMinionAtExactCost(board, manaNow, 2);
            bool holdForLowCostTempoMinion = enemyHasMinion && HasPlayableMinionAtMostCost(board, manaNow, 3);
            bool holdForViciousInvader = hasPlayableViciousInvader;

            // 用户新规则：1费有1费随从时，墓不抢先。
            if (holdForOneDrop)
            {
                p.CastSpellsModifiers.AddOrUpdate(TombOfSuffering, new Modifier(450));
                p.PlayOrderModifiers.AddOrUpdate(TombOfSuffering, new Modifier(-9200));
                AddLog("咒怨之墓：1费有1费随从，后置不抢先");
                return;
            }

            // 用户新规则：2费有2费随从时，墓不抢先。
            if (holdForTwoDrop)
            {
                p.CastSpellsModifiers.AddOrUpdate(TombOfSuffering, new Modifier(300));
                p.PlayOrderModifiers.AddOrUpdate(TombOfSuffering, new Modifier(-9000));
                AddLog("咒怨之墓：2费有2费随从，后置不抢先");
                return;
            }

            // 用户规则：若可下凶恶的入侵者，墓可暂缓，避免抢节奏/误触法术链。
            if (holdForViciousInvader)
            {
                p.CastSpellsModifiers.AddOrUpdate(TombOfSuffering, new Modifier(2200));
                p.PlayOrderModifiers.AddOrUpdate(TombOfSuffering, new Modifier(-9600));
                AddLog("咒怨之墓：可下凶恶的入侵者，后置让位节奏随从");
                return;
            }
            if (holdForLowCostTempoMinion)
            {
                p.CastSpellsModifiers.AddOrUpdate(TombOfSuffering, new Modifier(1600));
                p.PlayOrderModifiers.AddOrUpdate(TombOfSuffering, new Modifier(-9400));
                AddLog("咒怨之墓：敌方有场且有可下低费随从，后置让位节奏随从");
                return;
            }

            // 硬规则 3：一旦满足条件，咒怨之墓必须作为本回合最先使用的牌。
            ForceTombFirstThisTurn(board, p, manaNow);
            SetSingleCardCombo(board, p, TombOfSuffering, allowCoinBridge: false, forceOverride: true,
                logWhenSet: "咒怨之墓：ComboSet必出");
            p.CastSpellsModifiers.AddOrUpdate(TombOfSuffering, new Modifier(-7000));
            p.PlayOrderModifiers.AddOrUpdate(TombOfSuffering, new Modifier(9999));
            AddLog("咒怨之墓：满足优先窗口（可用费=" + manaNow + "）-> 强制本回合最先使用");
        }

        private bool ShouldDeferTombForPressure(Board board, int manaNow)
        {
            if (board == null || board.MinionEnemy == null || board.MinionEnemy.Count == 0)
                return false;

            int friendHp = GetHeroHealth(board.HeroFriend);
            int enemyHp = GetHeroHealth(board.HeroEnemy);
            int enemyAttack = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            int attackableFriendAttack = GetAttackableBoardAttack(board.MinionFriend);
            bool enemyHasTaunt = board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            if (!enemyHasTaunt && attackableFriendAttack >= enemyHp)
                return false;

            bool pressureWindow = ShouldEnterBalancedDefenseMode(board, friendHp, enemyHp)
                || (friendHp <= 12 && enemyAttack >= 8);
            if (!pressureWindow)
                return false;

            bool hasPlayableMinionNow = HasPlayableMinionWithBoardSpace(board, manaNow);
            bool canTradeNow = attackableFriendAttack > 0;
            return hasPlayableMinionNow || canTradeNow;
        }

        // 用户规则：当本回合已决定走挟持射线线时，墓应让位，不得抢线。
        private bool ShouldReserveTombForAbductionRay(Board board, int manaNow = -1)
        {
            if (board == null || board.Hand == null)
                return false;

            if (manaNow < 0)
                manaNow = GetAvailableManaIncludingCoin(board);
            AbductionRayStartupState startupState = EvaluateAbductionRayStartupState(board, manaNow);

            // 已登记“本回合射线优先”时，墓直接让位。
            if (startupState.RayStartedThisTurn)
                return true;

            if (!IsAbductionRayPlayableNow(board))
                return false;

            bool rayHardLockWindow = ShouldForceAbductionRayNow(board);
            return HasAbductionRayPriorityWindow(board, startupState, rayHardLockWindow);
        }

        private bool ShouldSkipTombForSketchOrRayThisTurn(Board board, int manaNow = -1)
        {
            if (board == null || board.Hand == null)
                return false;

            if (manaNow < 0)
                manaNow = GetAvailableManaIncludingCoin(board);
            AbductionRayStartupState startupState = EvaluateAbductionRayStartupState(board, manaNow);

            bool reserveTombForRayWindow = startupState.RayStartedThisTurn
                || ShouldReserveTombForAbductionRay(board, manaNow);
            bool prioritizeSketchArtistWindow = ShouldPrioritizeSketchArtistOverTomb(board, manaNow);
            bool prioritizeShadowflameStalkerWindow = ShouldPrioritizeShadowflameStalkerOverTomb(board, manaNow);
            bool sketchPlayableAtFourPlusWindow = manaNow >= 4
                && GetFreeBoardSlots(board) > 0
                && board.Hand.Any(c => c != null
                    && c.Template != null
                    && c.Template.Id == SketchArtist
                    && c.CurrentCost <= manaNow);

            return reserveTombForRayWindow
                || prioritizeSketchArtistWindow
                || prioritizeShadowflameStalkerWindow
                || sketchPlayableAtFourPlusWindow;
        }

        // 用户规则：4费且手里无挟持射线/速写美术家时，影焰猎豹可直接先手，不让墓抢线。
        private bool ShouldPrioritizeShadowflameStalkerOverTomb(Board board, int manaNow = -1)
        {
            if (board == null || board.Hand == null)
                return false;

            if (manaNow < 0)
                manaNow = GetAvailableManaIncludingCoin(board);
            if (manaNow != 4)
                return false;
            if (GetFreeBoardSlots(board) <= 0)
                return false;

            bool tombPlayableNow = board.Hand.Any(c => c != null
                && c.Template != null
                && c.Template.Id == TombOfSuffering
                && c.CurrentCost <= manaNow);
            if (!tombPlayableNow)
                return false;
            if (board.HasCardInHand(AbductionRay))
                return false;
            if (board.HasCardInHand(SketchArtist))
                return false;
            if (IsArchimondePlayableNow(board, manaNow))
                return false;

            bool stalkerPlayableNow = board.Hand.Any(c => c != null
                && c.Template != null
                && c.Template.Id == ShadowflameStalker
                && c.CurrentCost <= manaNow);
            return stalkerPlayableNow;
        }

        // Tomb 强制先手：在 Tomb 仍在手里时，把“本回合当前可打出”的其它动作统一后置。
        // Tomb 打完后会重算参数，此压制不会影响后续行动。
        private void ForceTombFirstThisTurn(Board board, ProfileParameters p, int manaNow)
        {
            if (board == null || board.Hand == null)
                return;
            if (manaNow <= 0)
                return;
            if (ShouldSkipTombForSketchOrRayThisTurn(board, manaNow))
                return;

            bool tombPlayableNow = board.Hand.Any(c => c != null
                && c.Template != null
                && c.Template.Id == TombOfSuffering
                && c.CurrentCost <= manaNow);
            if (!tombPlayableNow)
                return;

            foreach (var card in board.Hand.Where(c => c != null && c.Template != null))
            {
                if (card.Template.Id == TombOfSuffering)
                    continue;
                if (card.CurrentCost > manaNow)
                    continue;

                if (card.Type == Card.CType.MINION)
                    p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(2500));
                else if (card.Type == Card.CType.SPELL)
                    p.CastSpellsModifiers.AddOrUpdate(card.Template.Id, new Modifier(2500));
                else if (card.Type == Card.CType.WEAPON)
                    p.CastWeaponsModifiers.AddOrUpdate(card.Template.Id, new Modifier(2500));

                p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(-9000));
            }

            p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(5000));

            // 决定“先墓”时，统一后置攻击动作，避免出现先攻击再墓的执行偏差。
            if (board.MinionFriend != null)
            {
                foreach (var friend in board.MinionFriend.Where(m => m != null && m.Template != null && m.CanAttack))
                {
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(friend.Template.Id, new Modifier(9999));
                    p.AttackOrderModifiers.AddOrUpdate(friend.Template.Id, new Modifier(9999));
                }
            }

            if (board.MinionEnemy != null)
            {
                foreach (var enemy in board.MinionEnemy.Where(m => m != null && m.Template != null))
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(-9999));
            }

            p.GlobalAggroModifier = Math.Min(p.GlobalAggroModifier.Value, -3600);
        }

        // 用户规则：咒怨之墓发现时，不选择“挟持射线”。
        // 先尝试通过 ChoicesModifiers 精确压制对应选项；若当前环境拿不到选项列表，
        // 则兜底禁用“临时高亮的挟持射线”避免误选后继续执行。
        private void ApplyTombDiscoverNoAbductionRayRule(Board board, ProfileParameters p)
        {
            if (board == null || p == null)
                return;
            if (!WasTombJustPlayed(board))
                return;

            List<Card.Cards> choices;
            if (TryGetCurrentChoiceCards(board, out choices) && choices != null && choices.Count > 0)
            {
                int rayChoiceIndex = -1;
                int firstNonRayChoiceIndex = -1;
                for (int i = 0; i < choices.Count; i++)
                {
                    if (choices[i] == AbductionRay)
                    {
                        rayChoiceIndex = i + 1; // ChoicesModifiers 使用 1-based 索引
                    }
                    else if (firstNonRayChoiceIndex < 0)
                    {
                        firstNonRayChoiceIndex = i + 1;
                    }
                }

                if (rayChoiceIndex > 0 && firstNonRayChoiceIndex > 0)
                {
                    p.ChoicesModifiers.AddOrUpdate(TombOfSuffering, new Modifier(9999, rayChoiceIndex));
                    p.ChoicesModifiers.AddOrUpdate(TombOfSuffering, new Modifier(-4500, firstNonRayChoiceIndex));
                    AddLog("墓发现：禁选挟持射线，固定让位第" + firstNonRayChoiceIndex + "项");
                    return;
                }
            }

            if (board.Hand == null)
                return;

            bool fallbackApplied = false;
            foreach (var card in board.Hand.Where(c => c != null && c.Template != null && c.Template.Id == AbductionRay))
            {
                bool highlighted = false;
                try { highlighted = card.HasTag(Card.GAME_TAG.LUNAHIGHLIGHTHINT); } catch { highlighted = false; }
                if (!highlighted)
                    continue;

                p.CastSpellsModifiers.AddOrUpdate(card.Id, new Modifier(9999));
                p.PlayOrderModifiers.AddOrUpdate(card.Id, new Modifier(-9999));
                fallbackApplied = true;
            }

            if (fallbackApplied)
                AddLog("墓发现：兜底禁用临时高亮的挟持射线，避免误选");
        }

        private bool WasTombJustPlayed(Board board)
        {
            try
            {
                return board != null
                    && board.PlayedCards != null
                    && board.PlayedCards.Count > 0
                    && board.PlayedCards.Last() == TombOfSuffering;
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetCurrentChoiceCards(Board board, out List<Card.Cards> choices)
        {
            choices = new List<Card.Cards>();
            if (board == null)
                return false;

            string[] candidatePropertyNames = new[]
            {
                "Choices",
                "ChoiceCards",
                "DiscoverChoices",
                "DiscoverCards",
                "CurrentChoices"
            };

            for (int i = 0; i < candidatePropertyNames.Length; i++)
            {
                object raw = TryGetPropertyObject(board, candidatePropertyNames[i]);
                if (raw == null)
                    continue;

                if (TryConvertChoiceCollection(raw, choices) && choices.Count > 0)
                    return true;

                choices.Clear();
            }

            return false;
        }

        private bool TryConvertChoiceCollection(object raw, List<Card.Cards> output)
        {
            if (raw == null || output == null)
                return false;

            var enumerable = raw as System.Collections.IEnumerable;
            if (enumerable == null)
                return false;

            bool addedAny = false;
            foreach (var item in enumerable)
            {
                Card.Cards id;
                if (!TryResolveCardIdFromChoiceItem(item, out id))
                    continue;

                output.Add(id);
                addedAny = true;
            }

            return addedAny;
        }

        private bool TryResolveCardIdFromChoiceItem(object item, out Card.Cards id)
        {
            id = default(Card.Cards);
            if (item == null)
                return false;

            if (item is Card.Cards)
            {
                id = (Card.Cards)item;
                return true;
            }

            Card card = item as Card;
            if (card != null && card.Template != null)
            {
                id = card.Template.Id;
                return true;
            }

            object templateObj = TryGetPropertyObject(item, "Template");
            if (templateObj != null)
            {
                object templateIdObj = TryGetPropertyObject(templateObj, "Id");
                if (templateIdObj is Card.Cards)
                {
                    id = (Card.Cards)templateIdObj;
                    return true;
                }
            }

            object itemIdObj = TryGetPropertyObject(item, "Id");
            if (itemIdObj is Card.Cards)
            {
                id = (Card.Cards)itemIdObj;
                return true;
            }

            return false;
        }

        private object TryGetPropertyObject(object obj, string propertyName)
        {
            try
            {
                if (obj == null || string.IsNullOrWhiteSpace(propertyName))
                    return null;

                var prop = obj.GetType().GetProperty(propertyName);
                if (prop == null)
                    return null;
                return prop.GetValue(obj, null);
            }
            catch
            {
                return null;
            }
        }

        private bool ShouldForceTombFirstThisTurn(Board board)
        {
            if (board == null || board.Hand == null)
                return false;

            int manaNow = GetAvailableManaIncludingCoin(board);
            if (manaNow <= 0)
                return false;
            var tomb = board.Hand
                .Where(c => c != null && c.Template != null && c.Template.Id == TombOfSuffering)
                .OrderBy(c => c.CurrentCost)
                .FirstOrDefault();
            if (tomb == null || tomb.CurrentCost > manaNow)
                return false;
            if (ShouldSkipTombForSketchOrRayThisTurn(board, manaNow))
                return false;

            // 没有可发现目标时，不进入“强制先墓”。
            int affordableDeckTargets = CountDeckCardsAtMostCost(board, manaNow);
            if (affordableDeckTargets <= 0)
                return false;

            if (ShouldDeferTombForPressure(board, manaNow))
                return false;

            int friendlyMinionCount = board.MinionFriend != null ? board.MinionFriend.Count(m => m != null) : 0;
            bool hasViciousInvaderOnBoard = board.MinionFriend != null
                && board.MinionFriend.Any(m => m != null
                    && m.Template != null
                    && string.Equals(m.Template.Id.ToString(), "GDB_226", StringComparison.Ordinal));
            if (hasViciousInvaderOnBoard && friendlyMinionCount >= 3)
                return false;

            // 用户反馈：3费起墓应直接先手，不再让位低费节奏。
            if (manaNow >= 3)
                return true;

            // 用户规则：1费有1费随从、2费有2费随从时，墓不抢先。
            bool holdForOneDrop = (manaNow == 1) && HasPlayableMinionAtExactCost(board, manaNow, 1);
            bool holdForTwoDrop = (manaNow == 2) && HasPlayableMinionAtExactCost(board, manaNow, 2);
            if (holdForOneDrop || holdForTwoDrop)
                return false;

            // 用户规则：可下凶恶的入侵者时，墓不强制先手。
            bool hasPlayableViciousInvader = board.Hand.Any(c => c != null
                && c.Template != null
                && c.Type == Card.CType.MINION
                && string.Equals(c.Template.Id.ToString(), "GDB_226", StringComparison.Ordinal)
                && c.CurrentCost <= manaNow);
            if (hasPlayableViciousInvader)
                return false;

            // 用户规则：敌方有场时，若可下低费节奏随从，墓不强制先手。
            bool enemyHasMinion = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null);
            if (enemyHasMinion)
            {
                if (HasPlayableMinionAtMostCost(board, manaNow, 3))
                    return false;
            }

            return true;
        }

        private bool ShouldPrioritizeSketchArtistOverTomb(Board board, int manaNow)
        {
            if (board == null || board.Hand == null)
                return false;
            if (manaNow < 3)
                return false;
            if (GetFreeBoardSlots(board) <= 0)
                return false;

            bool sketchPlayableNow = board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == SketchArtist
                && c.CurrentCost <= manaNow);
            if (!sketchPlayableNow)
                return false;

            if (manaNow == 3)
            {
                if (CanPlayOneDropThenZilliaxSameTurn(board, manaNow))
                    return false;
                if (HasPlayableMinionWithBoardSpaceExcluding(board, manaNow, SketchArtist))
                    return false;
                if (board.Hand.Count > 6)
                    return false;
                return true;
            }

            if (board.Hand.Count >= 7)
                return false;
            if (ShouldPreferTempoBuffOverSketch(board, manaNow))
                return false;

            return true;
        }

        // 讲故事的始祖龟：回合结束按“不同类型”给友方+1/+1
        private void HandleTortollanStoryteller(Board board, ProfileParameters p)
        {
            int raceTypes = CountDistinctFriendlyRaceTypes(board);
            int manaNow = GetAvailableManaIncludingCoin(board);
            int friendHp = GetHeroHealth(board.HeroFriend);
            int enemyHp = GetHeroHealth(board.HeroEnemy);
            int friendAttack = GetBoardAttack(board.MinionFriend);
            int enemyAttack = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            bool storytellerPlayableNow = board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == TortollanStoryteller
                && c.CurrentCost <= manaNow);
            bool rushClearWindow = ShouldPrioritizeRushMinionClear(board, friendHp, enemyHp, friendAttack, enemyAttack);
            var playableOutsideDeckRushMinionIds = GetPlayableOutsideDeckRushMinionCardIdsForClear(board, manaNow);
            bool hasPlayableOutsideDeckRushMinionForClear = rushClearWindow && playableOutsideDeckRushMinionIds.Count > 0;
            bool dangerousHpTauntTempoWindow = IsDangerousHpTauntTempoWindow(friendHp, enemyAttack);
            var playableTauntMinionIds = GetPlayableTauntMinionCardIds(board, manaNow);
            bool hasPlayableTauntMinionNow = playableTauntMinionIds.Count > 0;
            bool moargPriorityWindow = ShouldPrioritizeMoargForgefiendNow(board);
            bool wispStorytellerWindow = CanPlayWispThenStorytellerSameTurn(board, manaNow);
            bool rayPlayableNow = IsAbductionRayPlayableNow(board);
            bool rayHardLock = ShouldForceAbductionRayNow(board);
            int playableRayCountNow = board.Hand.Count(c => c != null && c.Template != null
                && c.Template.Id == AbductionRay
                && c.CurrentCost <= manaNow);
            bool rayMultiChainPriorityWindow = rayPlayableNow
                && !rayHardLock
                && playableRayCountNow >= 2
                && board.MaxMana >= 5
                && !IsAbductionRayOverdrawRiskHigh(board, false)
                && !dangerousHpTauntTempoWindow;
            bool storytellerOnBoard = board.MinionFriend != null
                && board.MinionFriend.Any(m => m != null && m.Template != null && m.Template.Id == TortollanStoryteller);
            bool partyPlayableNow = board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == PartyFiend
                && c.CurrentCost <= manaNow)
                && GetFreeBoardSlots(board) >= 3
                && board.MinionFriend != null
                && board.MinionFriend.Count <= 4;
            bool enemyHasTauntNow = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            int storytellerCountInHand = board.Hand.Count(c => c != null && c.Template != null && c.Template.Id == TortollanStoryteller);
            bool shortHandPartyTempoWindow = storytellerPlayableNow
                && partyPlayableNow
                && board.Hand.Count <= 4
                && board.MinionFriend != null
                && board.MinionFriend.Count >= 1
                && !enemyHasTauntNow
                && !dangerousHpTauntTempoWindow;
            p.CastMinionsModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(80));
            p.PlayOrderModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(310));

            // 用户规则：邪犬优先于“继续下始祖龟”。
            // 触发场景：
            // 1) 场上已有始祖龟，且本回合可下邪犬；
            // 2) 前期(<=3费)手握双始祖龟，且本回合可下邪犬。
            bool preferPartyOverStorytellerWindow = storytellerPlayableNow
                && partyPlayableNow
                && !rayHardLock
                && !dangerousHpTauntTempoWindow
                && (storytellerOnBoard
                    || (board.MaxMana <= 3 && storytellerCountInHand >= 2)
                    || shortHandPartyTempoWindow);
            if (preferPartyOverStorytellerWindow)
            {
                SetSingleCardCombo(board, p, PartyFiend, allowCoinBridge: true, forceOverride: true,
                    logWhenSet: "派对邪犬：始祖龟让位，ComboSet优先落地");
                p.CastMinionsModifiers.AddOrUpdate(PartyFiend, new Modifier(-4600));
                p.PlayOrderModifiers.AddOrUpdate(PartyFiend, new Modifier(9999));
                p.CastMinionsModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(4200));
                p.PlayOrderModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(-9999));
                AddLog("始祖龟：邪犬可下且不该继续叠龟，后置让位邪犬");
                return;
            }

            // 用户硬规则：手里没有小精灵时，场上为0禁止空打始祖龟；优先分流找更高价值动作。
            bool emptyBoardNoWispWindow = storytellerPlayableNow
                && (board.MinionFriend == null || board.MinionFriend.Count == 0)
                && !board.HasCardInHand(Wisp);
            if (emptyBoardNoWispWindow)
            {
                p.CastMinionsModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(5200));
                p.PlayOrderModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(-9800));
                int additionalBodiesWithoutStoryteller = GetMaxAdditionalBodiesWithBudgetExcludingCard(board, manaNow, TortollanStoryteller);
                if (additionalBodiesWithoutStoryteller > 0)
                {
                    AddLog("始祖龟：空场且无小精灵，存在可铺场动作，后置让位铺场");
                }
                else if (CanUseLifeTapNow(board) && GetHeroHealth(board.HeroFriend) >= 3)
                {
                    p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(-2400));
                    AddLog("始祖龟：空场且无小精灵，禁止空打，优先先分流");
                }
                else
                {
                    AddLog("始祖龟：空场且无小精灵，禁止空打");
                }
                return;
            }

            // 用户反馈：危险血线且有可下嘲讽时，始祖龟必须后置让位嘲讽稳场。
            if (storytellerPlayableNow && dangerousHpTauntTempoWindow && hasPlayableTauntMinionNow)
            {
                foreach (var tauntId in playableTauntMinionIds)
                {
                    p.CastMinionsModifiers.AddOrUpdate(tauntId, new Modifier(-5600));
                    p.PlayOrderModifiers.AddOrUpdate(tauntId, new Modifier(9999));
                }
                p.CastMinionsModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(4200));
                p.PlayOrderModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(-9999));
                AddLog("始祖龟：危险血线且有可下嘲讽，后置让位嘲讽稳场");
                return;
            }

            // 用户规则：高压回合若手里有可下“套外恶魔突袭怪”，优先先下解场，不让始祖龟抢节奏。
            if (storytellerPlayableNow && hasPlayableOutsideDeckRushMinionForClear)
            {
                foreach (var rushId in playableOutsideDeckRushMinionIds)
                {
                    p.CastMinionsModifiers.AddOrUpdate(rushId, new Modifier(-6200));
                    p.PlayOrderModifiers.AddOrUpdate(rushId, new Modifier(9999));
                }

                var preferredOutsideRush = board.Hand
                    .Where(c => c != null
                        && c.Template != null
                        && c.Type == Card.CType.MINION
                        && c.CurrentCost <= manaNow
                        && c.IsRace(Card.CRace.DEMON)
                        && IsOutsideDeckDemonByDeckList(board, c.Template.Id)
                        && IsRushMinionForClear(c))
                    .OrderBy(c => c.CurrentCost)
                    .ThenBy(c => c.Template.Id.ToString(), StringComparer.Ordinal)
                    .FirstOrDefault();
                if (preferredOutsideRush != null)
                {
                    SetSingleCardCombo(board, p, preferredOutsideRush.Template.Id,
                        allowCoinBridge: true, forceOverride: true,
                        logWhenSet: "始祖龟：命中套外恶魔突袭解场窗口，ComboSet优先突袭解场");
                }

                p.CastMinionsModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(4600));
                p.PlayOrderModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(-9999));
                AddLog("始祖龟：命中套外恶魔突袭解场窗口，后置让位突袭随从清场");
                return;
            }

            // 用户规则修正：仅在挟持射线“强制窗口”时，始祖龟才后置到射线之后。
            // 若只是“射线可用但不锁定”，始祖龟不应被无条件压住。
            if (rayHardLock)
            {
                p.CastMinionsModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(3200));
                p.PlayOrderModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(-9600));
                AddLog("始祖龟：命中挟持射线锁定窗口，后置到射线之后");
                return;
            }
            if (rayMultiChainPriorityWindow)
            {
                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(-3200));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(9200));
                p.CastMinionsModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(2800));
                p.PlayOrderModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(-9400));
                AddLog("始祖龟：命中挟持射线连发窗口，后置到射线之后");
                return;
            }

            if (rayPlayableNow)
                AddLog("始祖龟：挟持射线可用但未锁定，不强制后置");

            // 用户规则：后期始祖龟不应抢先。
            // 当回合存在其他可打动作（尤其是套外恶魔）时，始祖龟后置让位节奏动作。
            bool hasPlayableOutsideDeckDemonNow = board.Hand.Any(c =>
                c != null
                && c.Template != null
                && c.Type == Card.CType.MINION
                && c.CurrentCost <= manaNow
                && c.IsRace(Card.CRace.DEMON)
                && IsOutsideDeckDemonByDeckList(board, c.Template.Id));
            int otherPlayableActions = CountPlayableActionsExcludingCard(board, manaNow, TortollanStoryteller);
            bool lateGameStorytellerDeprioritizeWindow = storytellerPlayableNow
                && board.MaxMana >= 5
                && !rayHardLock
                && !rayMultiChainPriorityWindow
                && !dangerousHpTauntTempoWindow
                && (hasPlayableOutsideDeckDemonNow || otherPlayableActions >= 1);
            if (lateGameStorytellerDeprioritizeWindow)
            {
                if (hasPlayableOutsideDeckDemonNow)
                {
                    foreach (var card in board.Hand.Where(c =>
                                 c != null
                                 && c.Template != null
                                 && c.Type == Card.CType.MINION
                                 && c.CurrentCost <= manaNow
                                 && c.IsRace(Card.CRace.DEMON)
                                 && IsOutsideDeckDemonByDeckList(board, c.Template.Id)))
                    {
                        p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(-2600));
                        p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(9800));
                    }
                }

                p.CastMinionsModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(4200));
                p.PlayOrderModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(-9800));
                AddLog("始祖龟：后期窗口不抢先，后置让位" + (hasPlayableOutsideDeckDemonNow ? "套外恶魔/其他动作" : "其他节奏动作"));
                return;
            }

            bool preferEntropyOverStorytellerNow = ShouldPreferEntropicOverStoryteller(board, manaNow)
                && !rayHardLock
                && !dangerousHpTauntTempoWindow;
            if (storytellerPlayableNow && preferEntropyOverStorytellerNow)
            {
                int friendlyCount = board.MinionFriend != null ? board.MinionFriend.Count : 0;
                int storytellerCoverage = GetStorytellerBuffCoverageNow(board);
                p.CastSpellsModifiers.AddOrUpdate(EntropicContinuity, new Modifier(-3200));
                p.PlayOrderModifiers.AddOrUpdate(EntropicContinuity, new Modifier(6200));
                p.CastMinionsModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(3800));
                p.PlayOrderModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(-9800));
                AddLog("始祖龟：续连熵能覆盖更高（友方随从=" + friendlyCount + "，类型数=" + storytellerCoverage + "），后置让位续连");
                return;
            }

            bool preferFloodBoardOverStorytellerNow = storytellerPlayableNow
                && !rayHardLock
                && !dangerousHpTauntTempoWindow
                && GetMaxAdditionalBodiesWithBudgetExcludingCard(board, manaNow, TortollanStoryteller) >= 2;
            if (preferFloodBoardOverStorytellerNow)
            {
                int floodBodies = GetMaxAdditionalBodiesWithBudgetExcludingCard(board, manaNow, TortollanStoryteller);
                p.CastMinionsModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(3000));
                p.PlayOrderModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(-9500));
                AddLog("始祖龟：让位铺场，本回合可额外铺场体数=" + floodBodies);
                return;
            }

            // 用户反馈：高压回合且莫尔葛熔魔可落地时，始祖龟后置让位熔魔稳场。
            if (storytellerPlayableNow && moargPriorityWindow)
            {
                p.CastMinionsModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(4200));
                p.PlayOrderModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(-9900));
                AddLog("始祖龟：高压且莫尔葛熔魔可用，后置让位熔魔稳场");
                return;
            }

            bool enemyHasMinion = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null);
            bool storytellerReadyByBoardState = enemyHasMinion
                ? raceTypes >= 2
                : (board.MinionFriend != null && board.MinionFriend.Count >= 1);

            // 用户硬规则：
            // 1) 敌方有随从时，始祖龟仅在己方种族>=2时可用；
            // 2) 敌方空场时，己方有1张随从即可放宽使用。
            if (storytellerReadyByBoardState)
            {
                // 高收益窗口：可满足门槛时强优先落地。
                p.CastMinionsModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(-2600));
                p.PlayOrderModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(9800));
                if (storytellerPlayableNow && !rayHardLock)
                {
                    SetSingleCardCombo(board, p, TortollanStoryteller, allowCoinBridge: true, forceOverride: true,
                        logWhenSet: enemyHasMinion
                            ? "始祖龟：敌方有场且类型>=2时ComboSet优先落地"
                            : "始祖龟：敌方空场且己方有随从时ComboSet优先落地");
                }
                if (enemyHasMinion)
                    AddLog("始祖龟：敌方有随从，场上类型数=" + raceTypes + "，高收益窗口");
                else
                    AddLog("始祖龟：敌方空场，己方已有随从，放宽优先落地");
            }
            else if (!enemyHasMinion && wispStorytellerWindow)
            {
                // 用户规则：若本回合可“小精灵(0费)+始祖龟”联动，放宽<2种族限制。
                p.CastMinionsModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(-1150));
                p.PlayOrderModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(6800));
                if (storytellerPlayableNow && !rayHardLock)
                {
                    SetSingleCardCombo(board, p, TortollanStoryteller, allowCoinBridge: true, forceOverride: false,
                        logWhenSet: "始祖龟：命中小精灵联动窗口，放宽限制优先落地");
                }
                AddLog("始祖龟：场上种族<2，但可同回合小精灵联动，放宽后置限制");
            }
            else
            {
                if (enemyHasMinion)
                {
                    // 敌方有场时严格执行“至少2种族”门槛，不再做1种族兜底放宽。
                    p.CastMinionsModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(5200));
                    p.PlayOrderModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(-9800));
                    AddLog("始祖龟：敌方有随从且场上类型<2，硬规则后置不打");
                }
                else
                {
                    int delay = board.MinionFriend.Count == 0 ? 140 : 90;
                    AddLog("始祖龟：敌方空场但己方无随从，轻微后置");

                    p.CastMinionsModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(delay));
                    p.PlayOrderModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(-260));
                }
            }

            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(190));
        }

        // 速写美术家：抽暗影法术（本套核心是挟持射线）并给临时复制
        private void HandleSketchArtist(Board board, ProfileParameters p)
        {
            if (!board.HasCardInHand(SketchArtist))
                return;

            int manaNow = GetAvailableManaIncludingCoin(board);
            bool sketchPlayableNow = board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == SketchArtist
                && c.CurrentCost <= manaNow);

            // 用户规则修正：2费回合若可下速写美术家，应优先铺场，不让1费动作抢节奏。
            if (manaNow == 2
                && sketchPlayableNow
                && GetFreeBoardSlots(board) > 0
                && board.MaxMana <= 2)
            {
                SetSingleCardCombo(board, p, SketchArtist, allowCoinBridge: true, forceOverride: true,
                    logWhenSet: "速写美术家：2费回合ComboSet强制先手");
                ForceSketchFirstThisTurn(board, p, manaNow);
                p.CastMinionsModifiers.AddOrUpdate(SketchArtist, new Modifier(-5600));
                p.PlayOrderModifiers.AddOrUpdate(SketchArtist, new Modifier(9999));
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(4200));
                AddLog("速写美术家：2费回合优先铺场，避免空过1费/错过节奏");
                return;
            }

            // 用户规则修正：速写美术家默认 4 费起优先；
            // 但 3 费“节奏窗口”允许当回合落地，避免白白空过。
            if (manaNow < 3)
            {
                p.CastMinionsModifiers.AddOrUpdate(SketchArtist, new Modifier(9999));
                p.PlayOrderModifiers.AddOrUpdate(SketchArtist, new Modifier(-9999));
                AddLog("速写美术家：可用费(含币虚拟)=" + manaNow + " < 3，本回合禁用");
                return;
            }

            // 用户规则：命中阿克蒙德直拍窗口时，速写必须后置让位阿克蒙德。
            if (sketchPlayableNow && ShouldCommitArchimondeThisTurn(board, manaNow))
            {
                p.CastMinionsModifiers.AddOrUpdate(SketchArtist, new Modifier(6200));
                p.PlayOrderModifiers.AddOrUpdate(SketchArtist, new Modifier(-9999));
                AddLog("速写美术家：命中阿克蒙德直拍窗口，后置让位阿克蒙德");
                return;
            }

            // 用户规则：高费回合（尤其8费后）若动作稀缺，优先下速写美术家，避免空过大额费用。
            bool enemyHasTauntNow = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            int attackNow = GetAttackableBoardAttack(board.MinionFriend);
            int enemyHp = GetHeroHealth(board.HeroEnemy);
            bool onBoardLethalNow = !enemyHasTauntNow && attackNow >= enemyHp;
            int otherPlayableActionsNow = CountPlayableActionsExcludingCard(board, manaNow, SketchArtist);
            bool highManaNoSkipSketchWindow = sketchPlayableNow
                && board.MaxMana >= 8
                && manaNow >= 6
                && board.Hand.Count <= 3
                && otherPlayableActionsNow <= 1
                && GetFreeBoardSlots(board) > 0
                && !onBoardLethalNow;
            if (highManaNoSkipSketchWindow)
            {
                SetSingleCardCombo(board, p, SketchArtist, allowCoinBridge: true, forceOverride: true,
                    logWhenSet: "速写美术家：高费空过保护窗口，ComboSet强制先手");
                ForceSketchFirstThisTurn(board, p, manaNow);
                p.CastMinionsModifiers.AddOrUpdate(SketchArtist, new Modifier(-6200));
                p.PlayOrderModifiers.AddOrUpdate(SketchArtist, new Modifier(9999));
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(4200));
                AddLog("速写美术家：高费回合动作稀缺，强制优先落地避免空过");
                return;
            }

            if (manaNow == 3)
            {
                bool oneDropThenZilliaxTempoAtThree = CanPlayOneDropThenZilliaxSameTurn(board, manaNow);
                if (oneDropThenZilliaxTempoAtThree)
                {
                    p.CastMinionsModifiers.AddOrUpdate(SketchArtist, new Modifier(4200));
                    p.PlayOrderModifiers.AddOrUpdate(SketchArtist, new Modifier(-9950));
                    AddLog("速写美术家：命中1费随从+奇利亚斯节奏线，3费后置到4费再用");
                    return;
                }

                bool hasPlayableMinionOtherThanSketchAtThree = HasPlayableMinionWithBoardSpaceExcluding(board, manaNow, SketchArtist);
                if (hasPlayableMinionOtherThanSketchAtThree)
                {
                    p.CastMinionsModifiers.AddOrUpdate(SketchArtist, new Modifier(3200));
                    p.PlayOrderModifiers.AddOrUpdate(SketchArtist, new Modifier(-9950));
                    AddLog("速写美术家：3费有其他可下随从，优先铺场，后置到4费再用");
                    return;
                }

                bool allowThreeManaSketch = board.Hand.Count <= 6
                    && GetFreeBoardSlots(board) > 0;

                if (!allowThreeManaSketch)
                {
                    p.CastMinionsModifiers.AddOrUpdate(SketchArtist, new Modifier(9999));
                    p.PlayOrderModifiers.AddOrUpdate(SketchArtist, new Modifier(-9999));
                    if (CanUseLifeTapNow(board) && GetHeroHealth(board.HeroFriend) >= 3)
                        AddLog("速写美术家：3费窗口未满足，优先分流留待下回合");
                    else
                        AddLog("速写美术家：3费窗口未满足，本回合后置");
                    return;
                }

                p.CastMinionsModifiers.AddOrUpdate(SketchArtist, new Modifier(-1600));
                p.PlayOrderModifiers.AddOrUpdate(SketchArtist, new Modifier(9999));
                AddLog("速写美术家：3费节奏窗口开启，允许本回合先手落地");
                return;
            }

            // 用户规则修正：手牌“非常多”才显著后置速写；
            // 6张手牌仍允许按节奏先拍速写（避免错过当回合资源转化）。
            if (board.Hand.Count >= 8)
            {
                p.CastMinionsModifiers.AddOrUpdate(SketchArtist, new Modifier(2600));
                p.PlayOrderModifiers.AddOrUpdate(SketchArtist, new Modifier(-9500));
                AddLog("速写美术家：手牌较多，明显后置避免无效补资源");
                return;
            }
            if (board.Hand.Count >= 7)
            {
                p.CastMinionsModifiers.AddOrUpdate(SketchArtist, new Modifier(900));
                p.PlayOrderModifiers.AddOrUpdate(SketchArtist, new Modifier(-6800));
                AddLog("速写美术家：手牌偏多，降低优先级避免抢节奏");
                return;
            }

            // 用户规则：敌方1血随从较多时，优先恐惧魔王清场，速写让位。
            if (board.HasCardInHand(DespicableDreadlord)
                && board.Hand.Any(c => c != null && c.Template != null
                    && c.Template.Id == DespicableDreadlord
                    && c.CurrentCost <= manaNow)
                && board.MinionEnemy.Count(m => m != null && m.CurrentHealth <= 1) >= 2
                && board.MinionEnemy.Count >= 4)
            {
                p.CastMinionsModifiers.AddOrUpdate(SketchArtist, new Modifier(2600));
                p.PlayOrderModifiers.AddOrUpdate(SketchArtist, new Modifier(-9800));
                AddLog("速写美术家：敌方1血铺场，后置给恐惧魔王先清场");
                return;
            }

            bool hasAttackableFriend = board.MinionFriend != null
                && board.MinionFriend.Any(m => m != null && m.CanAttack && m.CurrentAtk > 0);
            bool enemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            int attackNowForLethal = board.MinionFriend == null
                ? 0
                : board.MinionFriend.Where(m => m != null && m.CanAttack).Sum(m => Math.Max(0, m.CurrentAtk));
            bool onBoardLethal = !enemyHasTaunt && attackNowForLethal >= GetHeroHealth(board.HeroEnemy);

            // 用户规则：即使先攻击，也应同回合继续尝试落速写，避免空过费用。
            // 仅在“可行动作较少”且手中没有阿克蒙德时启用，避免干扰阿克蒙德主线。
            bool hasArchimondeInHand = board.HasCardInHand(Archimonde);
            int otherPlayableActions = CountPlayableActionsExcludingCard(board, manaNow, SketchArtist);
            if (!hasArchimondeInHand && hasAttackableFriend && !onBoardLethal && otherPlayableActions <= 1)
            {
                ForceAttackThenPlayMinionSameTurn(
                    board, p, SketchArtist, manaNow,
                    focusCastMod: 320,
                    focusPlayOrder: -9600,
                    otherDelay: 3400);
                AddLog("速写美术家：先攻击后同回合继续尝试落地，避免空过费用");
                return;
            }

            bool stalkerPlayableNow = HasPlayableSpecificMinion(board, ShadowflameStalker, manaNow);
            bool hasPlayableTempoMinionOtherThanSketch = HasPlayableMinionWithBoardSpaceExcluding(board, manaNow, SketchArtist);
            if (stalkerPlayableNow || hasPlayableTempoMinionOtherThanSketch)
            {
                AddLog("速写美术家：本回合决定使用，强制先手，不再让位其他随从");
            }

            // 默认规则：速写美术家优先于其他手牌动作（幸运币除外）。
            // 但若本回合存在“先铺场(奇利亚斯/邪犬)再续连熵能”的高收益线，则不强推速写先手。
            if (ShouldPreferTempoBuffOverSketch(board, manaNow))
            {
                p.CastMinionsModifiers.AddOrUpdate(SketchArtist, new Modifier(1400));
                p.PlayOrderModifiers.AddOrUpdate(SketchArtist, new Modifier(-9000));
                AddLog("速写美术家：命中铺场后续连收益线，后置不强推");
                return;
            }

            ForceSketchFirstThisTurn(board, p, manaNow);
            SetSingleCardCombo(board, p, SketchArtist, allowCoinBridge: true, forceOverride: true,
                logWhenSet: "速写美术家：ComboSet优先出牌");

            p.PlayOrderModifiers.AddOrUpdate(SketchArtist, new Modifier(9600));
            AddLog("速写美术家：命中先手规则，本回合优先使用（硬币除外）");

            int sketchCastMod = -1600;
            if (board.Hand.Count >= 9)
                sketchCastMod = -900;
            p.CastMinionsModifiers.AddOrUpdate(SketchArtist, new Modifier(sketchCastMod));
        }

        private void ForceSketchFirstThisTurn(Board board, ProfileParameters p, int manaNow)
        {
            if (board == null || board.Hand == null)
                return;

            foreach (var card in board.Hand.Where(c => c != null && c.Template != null))
            {
                if (card.Template.Id == SketchArtist || card.Template.Id == TheCoin)
                    continue;
                if (card.CurrentCost > manaNow)
                    continue;

                if (card.Type == Card.CType.MINION)
                    p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(4200));
                else if (card.Type == Card.CType.SPELL)
                    p.CastSpellsModifiers.AddOrUpdate(card.Template.Id, new Modifier(4200));
                else if (card.Type == Card.CType.WEAPON)
                    p.CastWeaponsModifiers.AddOrUpdate(card.Template.Id, new Modifier(4200));

                p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(-9900));
            }
        }

        // 速写美术家最终兜底入口：4费起默认强制第一手，
        // 以更早拿到资源。
        private void EnforceSketchArtistTopPriority(Board board, ProfileParameters p)
        {
            if (board == null || board.Hand == null || p == null)
                return;

            int manaNow = GetAvailableManaIncludingCoin(board);
            if (manaNow < 4 || GetFreeBoardSlots(board) <= 0)
                return;
            if (!board.HasCardInHand(SketchArtist))
                return;

            // 用户规则：命中阿克蒙德直拍窗口时，关闭速写终局强制先手。
            if (ShouldCommitArchimondeThisTurn(board, manaNow))
            {
                AddLog("速写美术家：阿克蒙德直拍窗口，关闭4费起强制先手");
                return;
            }

            // 用户规则修正：仅在手牌>=8时关闭“4费起速写强制先手”，
            // 7张手牌仍可强制速写先手，避免被挟持射线插队。
            if (board.Hand.Count >= 8)
            {
                AddLog("速写美术家：手牌较多，关闭4费起强制先手");
                return;
            }

            int stageMana = Math.Max(0, board.MaxMana);
            bool hasPlayableRayNow = board.Hand.Any(c => c != null
                && c.Template != null
                && c.Template.Id == AbductionRay
                && c.CurrentCost <= manaNow);
            bool hasTempRayWindow = HasTemporaryOrSketchGeneratedAbductionRay(board);
            bool rayNotStartedThisTurn = !HasStartedOrLastPlayedAbductionRayThisTurn(board);
            bool sketchRayPriorityWindow = stageMana >= 4
                && stageMana <= 6
                && hasPlayableRayNow
                && !hasTempRayWindow
                && rayNotStartedThisTurn;
            if (sketchRayPriorityWindow)
            {
                SetSingleCardCombo(board, p, SketchArtist, allowCoinBridge: true, forceOverride: true,
                    logWhenSet: "速写美术家：4-6费手有挟持射线，强制先手");
                ForceSketchFirstThisTurn(board, p, manaNow);
                p.CastMinionsModifiers.AddOrUpdate(SketchArtist, new Modifier(-6400));
                p.PlayOrderModifiers.AddOrUpdate(SketchArtist, new Modifier(9999));
                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(5200));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(-9999));
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(4200));
                AddLog("速写美术家：4-6费且手有挟持射线，强制第一优先");
                return;
            }

            SetSingleCardCombo(board, p, SketchArtist, allowCoinBridge: true, forceOverride: true,
                logWhenSet: "速写美术家：最优先出牌（强制）");
            ForceSketchFirstThisTurn(board, p, manaNow);
            p.CastMinionsModifiers.AddOrUpdate(SketchArtist, new Modifier(-6200));
            p.PlayOrderModifiers.AddOrUpdate(SketchArtist, new Modifier(9999));
            p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(4200));
            AddLog("速写美术家：4费起强制第一优先");
        }

        // 用户规则：影焰猎豹按“最左/最右6伤”的即时收益动态调优优先级。
        private void HandleShadowflameStalker(Board board, ProfileParameters p)
        {
            int manaNow = GetAvailableManaIncludingCoin(board);
            bool stalkerPlayableNow = board.Hand != null && board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == ShadowflameStalker
                && c.CurrentCost <= manaNow);
            bool archimondePlayableNow = IsArchimondePlayableNow(board, manaNow);
            int playableOutsideDeckDemonsThisTurn = GetMaxPlayableOutsideDeckDemonsThisTurn(board, manaNow);
            bool flameImpPlayableNow = board.Hand != null && board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == FlameImp
                && c.CurrentCost <= manaNow);
            int friendHp = GetHeroHealth(board.HeroFriend);
            int enemyAttack = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            bool enemyHasBoard = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null);
            bool hasPlayableTauntNow = GetPlayableTauntMinionCardIds(board, manaNow).Count > 0;
            bool glacialShardPlayableNow = board.Hand != null && board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == GlacialShard
                && c.CurrentCost <= manaNow);
            bool rayPlayableNow = IsAbductionRayPlayableNow(board);

            p.CastMinionsModifiers.AddOrUpdate(ShadowflameStalker, new Modifier(-350));
            p.PlayOrderModifiers.AddOrUpdate(ShadowflameStalker, new Modifier(5200));

            // 用户规则：影焰猎豹优先级低于阿克蒙德。若阿克蒙德本回合可下，猎豹统一后置让位。
            if (stalkerPlayableNow && archimondePlayableNow)
            {
                p.CastMinionsModifiers.AddOrUpdate(ShadowflameStalker, new Modifier(6200));
                p.PlayOrderModifiers.AddOrUpdate(ShadowflameStalker, new Modifier(-9999));
                AddLog("影焰猎豹：阿克蒙德可下，强后置到阿克蒙德之后");
                return;
            }

            // 用户规则：影焰猎豹优先级低于套外恶魔。若本回合可下套外恶魔，猎豹后置让位。
            if (stalkerPlayableNow && playableOutsideDeckDemonsThisTurn > 0)
            {
                p.CastMinionsModifiers.AddOrUpdate(ShadowflameStalker, new Modifier(6200));
                p.PlayOrderModifiers.AddOrUpdate(ShadowflameStalker, new Modifier(-9999));
                AddLog("影焰猎豹：本回合可下套外恶魔，后置让位（可打数量=" + playableOutsideDeckDemonsThisTurn + "）");
                return;
            }

            // 用户规则：4费且手里无挟持射线/速写时，影焰猎豹直接先手，不让墓抢线。
            if (stalkerPlayableNow && ShouldPrioritizeShadowflameStalkerOverTomb(board, manaNow))
            {
                SetSingleCardCombo(board, p, ShadowflameStalker, allowCoinBridge: true, forceOverride: true,
                    logWhenSet: "影焰猎豹：4费无射线/速写窗口，ComboSet先手");
                p.CastMinionsModifiers.AddOrUpdate(ShadowflameStalker, new Modifier(-5600));
                p.PlayOrderModifiers.AddOrUpdate(ShadowflameStalker, new Modifier(9999));
                p.CastSpellsModifiers.AddOrUpdate(TombOfSuffering, new Modifier(5200));
                p.PlayOrderModifiers.AddOrUpdate(TombOfSuffering, new Modifier(-9900));
                AddLog("影焰猎豹：4费且手里无挟持射线/速写，优先先手，不让位咒怨之墓");
                return;
            }

            if (ShouldDelayShadowflameForAbductionRay(board))
            {
                p.CastMinionsModifiers.AddOrUpdate(ShadowflameStalker, new Modifier(4800));
                p.PlayOrderModifiers.AddOrUpdate(ShadowflameStalker, new Modifier(-9950));
                AddLog("影焰猎豹：命中挟持射线链路窗口，后置到射线之后");
                return;
            }

            // 用户规则：危机且缺保命手段时，优先拍猎豹搏发现（找嘲讽/解场）。
            bool emergencyDiscoverStabilizeWindow = stalkerPlayableNow
                && enemyHasBoard
                && (friendHp <= 10 || enemyAttack >= friendHp)
                && !hasPlayableTauntNow
                && !glacialShardPlayableNow
                && !rayPlayableNow;
            if (emergencyDiscoverStabilizeWindow)
            {
                SetSingleCardCombo(board, p, ShadowflameStalker, allowCoinBridge: true, forceOverride: true,
                    logWhenSet: "影焰猎豹：危险血线博保命发现，强制先手");
                p.CastMinionsModifiers.AddOrUpdate(ShadowflameStalker, new Modifier(-5600));
                p.PlayOrderModifiers.AddOrUpdate(ShadowflameStalker, new Modifier(9999));
                AddLog("影焰猎豹：缺少嘲讽/冰片/射线保命手段，优先落地尝试发现保命牌");
                return;
            }

            bool stalkerEffectAdjusted = TryApplyShadowflameStalkerEdgeDamagePriority(
                board,
                p,
                manaNow,
                allowComboSet: true,
                emitLog: true);

            // 用户规则：后期存在烈焰小鬼可下时，影焰猎豹先于烈焰小鬼使用（调整先后顺序，不改整体框架）。
            if (!stalkerEffectAdjusted && board.MaxMana >= 6 && flameImpPlayableNow)
            {
                p.CastMinionsModifiers.AddOrUpdate(ShadowflameStalker, new Modifier(-350));
                p.PlayOrderModifiers.AddOrUpdate(ShadowflameStalker, new Modifier(7600));
                AddLog("影焰猎豹：后期优先于烈焰小鬼出牌");
            }

            // 用户规则：影焰猎豹在手牌<=9时允许使用；仅手牌满(10)时再后置避免爆牌。
            if (!stalkerEffectAdjusted && board.Hand.Count >= HearthstoneHandLimit)
            {
                p.CastMinionsModifiers.AddOrUpdate(ShadowflameStalker, new Modifier(1200));
                p.PlayOrderModifiers.AddOrUpdate(ShadowflameStalker, new Modifier(-7000));
            }
        }

        private bool TryApplyShadowflameStalkerEdgeDamagePriority(
            Board board,
            ProfileParameters p,
            int manaNow,
            bool allowComboSet,
            bool emitLog)
        {
            if (board == null || board.Hand == null || p == null || board.MinionEnemy == null)
                return false;
            if (board.MinionEnemy.Count == 0)
                return false;

            var stalker = board.Hand
                .Where(c => c != null && c.Template != null
                    && c.Template.Id == ShadowflameStalker
                    && c.CurrentCost <= manaNow)
                .OrderBy(c => c.CurrentCost)
                .ThenBy(c => c.Id)
                .FirstOrDefault();
            if (stalker == null)
                return false;

            bool isPoweredUp = GetTag(stalker, Card.GAME_TAG.POWERED_UP) == 1;
            if (!isPoweredUp)
                return false;

            var enemies = board.MinionEnemy.Where(m => m != null).ToList();
            if (enemies.Count == 0)
                return false;

            Card left = enemies[0];
            Card right = enemies[enemies.Count - 1];
            var edgeTargets = new List<Card> { left };
            if (right != null && right.Id != left.Id)
                edgeTargets.Add(right);

            int killCount = 0;
            int attackRemoved = 0;
            int tauntKillCount = 0;
            int dangerHitCount = 0;
            int dangerScore = 0;
            int lowHealthEdgeCount = 0;
            bool hasDivineShieldEdge = false;

            foreach (var enemy in edgeTargets)
            {
                if (enemy == null)
                    continue;

                int hp = Math.Max(0, enemy.CurrentHealth);
                int atk = Math.Max(0, enemy.CurrentAtk);
                bool hasDivineShield = enemy.IsDivineShield;
                if (hasDivineShield)
                    hasDivineShieldEdge = true;

                if (hp <= 3)
                    lowHealthEdgeCount++;

                int dangerBonus = enemy.Template != null
                    ? Math.Max(0, GetDangerEngineTradeBonus(enemy.Template.Id))
                    : 0;
                if (dangerBonus > 0)
                {
                    dangerHitCount++;
                    dangerScore += dangerBonus;
                }

                bool canKillByEdgeDamage = !hasDivineShield && hp > 0 && hp <= 6;
                if (!canKillByEdgeDamage)
                    continue;

                killCount++;
                attackRemoved += atk;
                if (enemy.IsTaunt)
                    tauntKillCount++;
            }

            int edgeScore = killCount * 1900
                + attackRemoved * 260
                + tauntKillCount * 750
                + lowHealthEdgeCount * 180
                + Math.Min(2800, dangerScore / 2);
            if (hasDivineShieldEdge)
                edgeScore -= 350;

            bool highValueWindow = killCount >= 2
                || edgeScore >= 3400
                || (killCount >= 1 && (tauntKillCount >= 1 || attackRemoved >= 5 || dangerHitCount >= 1));
            bool mediumValueWindow = !highValueWindow && edgeScore >= 1700;
            bool lowValueWindow = !highValueWindow
                && !mediumValueWindow
                && edgeTargets.Count >= 2
                && killCount == 0
                && attackRemoved == 0
                && dangerHitCount == 0
                && lowHealthEdgeCount == 0;

            if (highValueWindow)
            {
                if (allowComboSet)
                {
                    SetSingleCardCombo(board, p, ShadowflameStalker, allowCoinBridge: true, forceOverride: true,
                        logWhenSet: "影焰猎豹：延系双端命中高收益，强制先手");
                }
                p.CastMinionsModifiers.AddOrUpdate(ShadowflameStalker, new Modifier(-5600));
                p.PlayOrderModifiers.AddOrUpdate(ShadowflameStalker, new Modifier(9999));
                if (emitLog)
                    AddLog("影焰猎豹：延系高收益（击杀=" + killCount + "，减攻=" + attackRemoved + "），强制前置");
                return true;
            }

            if (mediumValueWindow)
            {
                p.CastMinionsModifiers.AddOrUpdate(ShadowflameStalker, new Modifier(-1500));
                p.PlayOrderModifiers.AddOrUpdate(ShadowflameStalker, new Modifier(8200));
                if (emitLog)
                    AddLog("影焰猎豹：延系收益可观（击杀=" + killCount + "，减攻=" + attackRemoved + "），前置使用");
                return true;
            }

            if (lowValueWindow)
            {
                p.CastMinionsModifiers.AddOrUpdate(ShadowflameStalker, new Modifier(900));
                p.PlayOrderModifiers.AddOrUpdate(ShadowflameStalker, new Modifier(-5200));
                if (emitLog)
                    AddLog("影焰猎豹：延系双端收益低，适度后置");
                return true;
            }

            return false;
        }

        // 玛瑟里顿（未发售版）：休眠2回合；休眠期间在“你的回合结束时”对所有敌人造成3点伤害（含敌方英雄）。
        // 口径：
        // 1) 高压回合按“本回合结束立刻结算”的清场收益前置；
        // 2) 低压且无有效命中时后置，避免空拍占节奏。
        private void HandleUnreleasedMasseridon(Board board, ProfileParameters p)
        {
            if (board == null || board.Hand == null || p == null)
                return;

            var masseridon = board.Hand
                .Where(c => c != null && c.Template != null && c.Template.Id == UnreleasedMasseridon)
                .OrderBy(c => c.CurrentCost)
                .ThenBy(c => c.Id)
                .FirstOrDefault();
            if (masseridon == null)
                return;

            // 默认中性：避免在无窗口时抢占常规节奏。
            p.CastMinionsModifiers.AddOrUpdate(UnreleasedMasseridon, new Modifier(280));
            p.PlayOrderModifiers.AddOrUpdate(UnreleasedMasseridon, new Modifier(2400));

            int manaNow = GetAvailableManaIncludingCoin(board);
            int freeSlots = GetFreeBoardSlots(board);
            if (masseridon.CurrentCost > manaNow || freeSlots <= 0)
                return;

            var enemies = board.MinionEnemy == null
                ? new List<Card>()
                : board.MinionEnemy.Where(m => m != null).ToList();
            int enemyCount = enemies.Count;
            if (enemyCount <= 0)
            {
                p.CastMinionsModifiers.AddOrUpdate(UnreleasedMasseridon, new Modifier(1200));
                p.PlayOrderModifiers.AddOrUpdate(UnreleasedMasseridon, new Modifier(-5200));
                AddLog("玛瑟里顿（未发售版）：敌方空场，后置避免空拍");
                return;
            }

            int friendHp = GetHeroHealth(board.HeroFriend);
            int enemyHp = GetHeroHealth(board.HeroEnemy);
            int weaponEnemyAtk = board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0;
            int enemyAttackNow = enemies.Sum(m => Math.Max(0, m.CurrentAtk)) + weaponEnemyAtk;
            int enemyAttackAfterPulse = enemies
                .Where(m => m.CurrentHealth > 3)
                .Sum(m => Math.Max(0, m.CurrentAtk)) + weaponEnemyAtk;
            int attackDropByPulse = Math.Max(0, enemyAttackNow - enemyAttackAfterPulse);
            int pulseKillCount = enemies.Count(m => m.CurrentHealth <= 3);
            bool enemyHasTaunt = enemies.Any(m => m.IsTaunt);
            int friendAttackNow = GetAttackableBoardAttack(board.MinionFriend);
            bool onBoardLethal = !enemyHasTaunt && friendAttackNow >= enemyHp;
            bool lowHpHighPressure = friendHp <= 12 && enemyAttackNow >= 8;
            bool severePressure = enemyAttackNow >= friendHp || (friendHp <= 10 && enemyAttackNow >= 6);
            bool balancedDefenseMode = ShouldEnterBalancedDefenseMode(board, friendHp, enemyHp);
            bool wideEnemyBoard = enemyCount >= 4;
            bool highPulseValue = pulseKillCount >= 2 || attackDropByPulse >= 4;
            bool mediumPulseValue = pulseKillCount >= 1 || attackDropByPulse >= 2 || enemyCount >= 5;

            if (onBoardLethal)
            {
                p.CastMinionsModifiers.AddOrUpdate(UnreleasedMasseridon, new Modifier(3800));
                p.PlayOrderModifiers.AddOrUpdate(UnreleasedMasseridon, new Modifier(-9200));
                AddLog("玛瑟里顿（未发售版）：已达场攻斩杀，后置不插队");
                return;
            }

            // 回合末3伤也会打脸：可补足斩杀时直接前置。
            if (enemyHp <= 3)
            {
                SetSingleCardCombo(board, p, UnreleasedMasseridon, allowCoinBridge: true, forceOverride: true,
                    logWhenSet: "玛瑟里顿（未发售版）：回合末3伤斩杀，强制先手");
                p.CastMinionsModifiers.AddOrUpdate(UnreleasedMasseridon, new Modifier(-5600));
                p.PlayOrderModifiers.AddOrUpdate(UnreleasedMasseridon, new Modifier(9999));
                AddLog("玛瑟里顿（未发售版）：命中回合末伤害斩杀窗口，前置落地");
                return;
            }

            // 用户规则：敌方<=3血随从越多，玛瑟里顿优先级越高。
            // 说明：回合末3伤会命中“所有敌方角色”（包括敌方英雄），因此该窗口兼具清场与压血价值。
            if (pulseKillCount > 0)
            {
                int scaledCount = Math.Min(6, pulseKillCount);
                int castMod = -1400 - scaledCount * 700;
                int orderMod = 5600 + Math.Min(5, pulseKillCount) * 900;

                if (enemyHp <= 6)
                {
                    castMod -= 500;
                    orderMod += 400;
                }

                if (castMod < -6200) castMod = -6200;
                if (orderMod > 9999) orderMod = 9999;

                p.CastMinionsModifiers.AddOrUpdate(UnreleasedMasseridon, new Modifier(castMod));
                p.PlayOrderModifiers.AddOrUpdate(UnreleasedMasseridon, new Modifier(orderMod));
                AddLog("玛瑟里顿（未发售版）：敌方<=3血随从=" + pulseKillCount + "，数量越多越优先（回合末同时对敌方英雄造成3伤）");

                // 极高收益窗口：可清理大量低血随从时，强制前置。
                if (pulseKillCount >= 4)
                {
                    SetSingleCardCombo(board, p, UnreleasedMasseridon, allowCoinBridge: true, forceOverride: true,
                        logWhenSet: "玛瑟里顿（未发售版）：多目标<=3血清场窗口，强制先手");
                    p.CastMinionsModifiers.AddOrUpdate(UnreleasedMasseridon, new Modifier(-6200));
                    p.PlayOrderModifiers.AddOrUpdate(UnreleasedMasseridon, new Modifier(9999));
                    AddLog("玛瑟里顿（未发售版）：<=3血目标达到" + pulseKillCount + "个，强制前置清场并打脸");
                    return;
                }
            }

            if (severePressure && highPulseValue)
            {
                SetSingleCardCombo(board, p, UnreleasedMasseridon, allowCoinBridge: true, forceOverride: true,
                    logWhenSet: "玛瑟里顿（未发售版）：高压清场窗口，强制先手");
                p.CastMinionsModifiers.AddOrUpdate(UnreleasedMasseridon, new Modifier(-5200));
                p.PlayOrderModifiers.AddOrUpdate(UnreleasedMasseridon, new Modifier(9999));
                AddLog("玛瑟里顿（未发售版）：高压且回合末可清" + pulseKillCount + "个，敌方场攻预计下降" + attackDropByPulse + "，前置保命");
                return;
            }

            if ((lowHpHighPressure || balancedDefenseMode) && mediumPulseValue)
            {
                p.CastMinionsModifiers.AddOrUpdate(UnreleasedMasseridon, new Modifier(-3000));
                p.PlayOrderModifiers.AddOrUpdate(UnreleasedMasseridon, new Modifier(8600));
                AddLog("玛瑟里顿（未发售版）：防守窗口命中回合末群伤收益，提升优先级");
                return;
            }

            if (wideEnemyBoard && highPulseValue)
            {
                p.CastMinionsModifiers.AddOrUpdate(UnreleasedMasseridon, new Modifier(-2200));
                p.PlayOrderModifiers.AddOrUpdate(UnreleasedMasseridon, new Modifier(6800));
                AddLog("玛瑟里顿（未发售版）：敌方铺场较宽且群伤收益可观，前置落地");
                return;
            }

            if (pulseKillCount <= 0 && attackDropByPulse <= 1 && !lowHpHighPressure)
            {
                p.CastMinionsModifiers.AddOrUpdate(UnreleasedMasseridon, new Modifier(1600));
                p.PlayOrderModifiers.AddOrUpdate(UnreleasedMasseridon, new Modifier(-6000));
                AddLog("玛瑟里顿（未发售版）：群伤收益偏低，后置不抢节奏");
            }
        }

        // 月度魔范员工：有可攻击友方时，优先“先贴吸血再攻击”。
        private void HandleMonthlyModelEmployee(Board board, ProfileParameters p, int enemyHp)
        {
            if (board == null || board.Hand == null || board.MinionFriend == null)
                return;

            int manaNow = GetAvailableManaIncludingCoin(board);
            if (GetFreeBoardSlots(board) <= 0)
                return;
            if (ShouldForceAbductionRayNow(board))
                return;

            var monthly = board.Hand
                .Where(c => c != null && c.Template != null
                    && c.Template.Id == MonthlyModelEmployee
                    && c.CurrentCost <= manaNow)
                .OrderBy(c => c.CurrentCost)
                .ThenBy(c => c.Id)
                .FirstOrDefault();
            if (monthly == null)
                return;

            bool hasFriendlyMinionNow = board.MinionFriend.Any(m => m != null);
            if (!hasFriendlyMinionNow)
            {
                p.CastMinionsModifiers.AddOrUpdate(monthly.Id, new Modifier(9999));
                p.CastMinionsModifiers.AddOrUpdate(MonthlyModelEmployee, new Modifier(9200));
                p.PlayOrderModifiers.AddOrUpdate(monthly.Id, new Modifier(-9999));
                AddLog("月度魔范员工：友方无随从，后置暂缓使用");
                return;
            }

            var attackableFriends = board.MinionFriend
                .Where(m => m != null && m.Template != null && m.CanAttack && m.CurrentAtk > 0)
                .ToList();
            if (attackableFriends.Count == 0)
                return;

            bool enemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            int canAttackDamageNow = attackableFriends.Sum(m => Math.Max(0, m.CurrentAtk));
            if (!enemyHasTaunt && canAttackDamageNow >= enemyHp)
                return;

            var bestTarget = attackableFriends
                .OrderByDescending(m => m.CurrentAtk)
                .ThenByDescending(m => m.CurrentHealth)
                .First();

            SetSingleCardCombo(board, p, MonthlyModelEmployee, allowCoinBridge: true, forceOverride: false,
                logWhenSet: "月度魔范员工：ComboSet先贴吸血");
            p.CastMinionsModifiers.AddOrUpdate(MonthlyModelEmployee, -3200, bestTarget.Id);
            p.PlayOrderModifiers.AddOrUpdate(MonthlyModelEmployee, new Modifier(9999));

            foreach (var friend in attackableFriends)
                p.AttackOrderModifiers.AddOrUpdate(friend.Template.Id, new Modifier(9999));

            AddLog("月度魔范员工：命中先贴吸血窗口，后置攻击（目标=" + bestTarget.Template.Id + " atk=" + bestTarget.CurrentAtk + ")");
        }

        // 阿克蒙德：依赖“已使用过的套牌外恶魔”，非成型局面不要裸拍
        private void HandleArchimonde(Board board, ProfileParameters p)
        {
            int outsideDeckDemonsInBoardAndGrave = CountOutsideDeckDemonsInBoardAndGrave(board);
            bool readyByOutsideDemons = outsideDeckDemonsInBoardAndGrave >= 6;
            string boardAdvantageSnapshot;
            bool boardAdvantage = HasFriendlyBoardAdvantageForLegendaryDemons(board, out boardAdvantageSnapshot);
            int friendHp = GetHeroHealth(board.HeroFriend);
            int enemyAttack = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            bool dangerousHpTauntTempoWindow = IsDangerousHpTauntTempoWindow(friendHp, enemyAttack);
            string outsideTauntDemonSnapshot;
            bool hasOutsideTauntDemonForArchimonde = HasOutsideDeckTauntDemonInBoardAndGrave(board, out outsideTauntDemonSnapshot);
            bool enemyHasSurgingShipSurgeon = board.MinionEnemy != null
                && board.MinionEnemy.Any(m => m != null && m.Template != null && m.Template.Id == Card.Cards.CORE_WON_065);

            int manaNow = GetAvailableManaIncludingCoin(board);
            var archimondeCard = board.Hand
                .Where(c => c != null && c.Template != null && c.Template.Id == Archimonde)
                .OrderBy(c => c.CurrentCost)
                .ThenBy(c => c.Id)
                .FirstOrDefault();
            if (archimondeCard == null)
                return;
            if (archimondeCard.CurrentCost > manaNow)
            {
                p.CastMinionsModifiers.AddOrUpdate(Archimonde, new Modifier(999));
                return;
            }

            // 用户规则：危险血线且随船外科医师在场时，若冰川裂片可用，必须让位冰片先手冻结。
            bool glacialShardPlayableNow = GetFreeBoardSlots(board) > 0
                && board.Hand.Any(c => c != null && c.Template != null
                    && c.Template.Id == GlacialShard
                    && c.CurrentCost <= manaNow);
            if (dangerousHpTauntTempoWindow && enemyHasSurgingShipSurgeon && glacialShardPlayableNow)
            {
                p.CastMinionsModifiers.AddOrUpdate(Archimonde, new Modifier(5200));
                p.PlayOrderModifiers.AddOrUpdate(Archimonde, new Modifier(-9999));
                AddLog("阿克蒙德：随船外科医师在场且冰川裂片可用，后置让位冰片先手");
                return;
            }

            // 用户规则：若坟场里已有玛瑟里顿（未发售版），且当前回合复活收益高，
            // 阿克蒙德应强制前置，不受“套外恶魔>=6/场面优势/默认后置”限制。
            int masseridonInGrave = CountFriendGraveyard(board, UnreleasedMasseridon);
            int enemyHp = GetHeroHealth(board.HeroEnemy);
            int weaponEnemyAtk = board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0;
            int pulseKillCount = board.MinionEnemy == null ? 0 : board.MinionEnemy.Count(m => m != null && m.CurrentHealth <= 3);
            int enemyAttackAfterPulse = (board.MinionEnemy == null ? 0 : board.MinionEnemy
                .Where(m => m != null && m.CurrentHealth > 3)
                .Sum(m => Math.Max(0, m.CurrentAtk))) + weaponEnemyAtk;
            int pulseAttackDrop = Math.Max(0, enemyAttack - enemyAttackAfterPulse);
            bool enemyHasTauntNow = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            int attackNowForArchimonde = GetAttackableBoardAttack(board.MinionFriend);
            bool onBoardLethalNow = !enemyHasTauntNow && attackNowForArchimonde >= enemyHp;
            bool highValueMasseridonReviveWindow = masseridonInGrave > 0
                && GetFreeBoardSlots(board) >= 1
                && (pulseKillCount >= 2
                    || pulseAttackDrop >= 4
                    || enemyHp <= 6
                    || dangerousHpTauntTempoWindow
                    || enemyAttack >= 8);
            if (highValueMasseridonReviveWindow && !onBoardLethalNow)
            {
                if (TryDelayArchimondeForSendableAttacks(board, p))
                    return;

                SetSingleCardCombo(board, p, Archimonde, allowCoinBridge: true, forceOverride: true,
                    logWhenSet: "阿克蒙德：复活玛瑟里顿高收益窗口，强制先手");
                ForceArchimondeFirstThisTurn(board, p, manaNow);
                p.CastMinionsModifiers.AddOrUpdate(Archimonde, new Modifier(-6200));
                p.PlayOrderModifiers.AddOrUpdate(Archimonde, new Modifier(9999));
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(4200));
                AddLog("阿克蒙德：坟场玛瑟里顿=" + masseridonInGrave
                    + "，预估回合末可清<=3血目标=" + pulseKillCount
                    + "，敌方场攻预计下降=" + pulseAttackDrop
                    + "，强制前置复活玛瑟里顿");
                return;
            }

            // 用户规则：危险血线时，若“套外恶魔池(场+坟)”里已有嘲讽恶魔且有场位，
            // 允许阿克蒙德前置保命（不受>=6与场面优势门槛限制）。
            if (dangerousHpTauntTempoWindow
                && GetFreeBoardSlots(board) >= 1
                && hasOutsideTauntDemonForArchimonde)
            {
                if (TryDelayArchimondeForSendableAttacks(board, p))
                    return;

                SetSingleCardCombo(board, p, Archimonde, allowCoinBridge: true, forceOverride: true,
                    logWhenSet: "阿克蒙德：危险血线且套外恶魔池含嘲讽，强制先手保命");
                ForceArchimondeFirstThisTurn(board, p, manaNow);
                p.CastMinionsModifiers.AddOrUpdate(Archimonde, new Modifier(-5200));
                p.PlayOrderModifiers.AddOrUpdate(Archimonde, new Modifier(9999));
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(4200));
                AddLog("阿克蒙德：危险血线且套外恶魔池含嘲讽(" + outsideTauntDemonSnapshot + ")，有场位可用，前置保命");
                return;
            }

            // 用户反馈：10费回合命中拍出条件时，应直接下阿克蒙德，避免小怪先占格子。
            bool turnTenImmediateArchimondeWindow = board.MaxMana >= 10
                && readyByOutsideDemons
                && GetFreeBoardSlots(board) >= 1
                && !onBoardLethalNow;
            if (turnTenImmediateArchimondeWindow)
            {
                if (TryDelayArchimondeForSendableAttacks(board, p))
                    return;

                SetSingleCardCombo(board, p, Archimonde, allowCoinBridge: true, forceOverride: true,
                    logWhenSet: "阿克蒙德：10费直拍窗口，ComboSet强制先手");
                ForceArchimondeFirstThisTurn(board, p, manaNow);
                p.CastMinionsModifiers.AddOrUpdate(Archimonde, new Modifier(-7600));
                p.PlayOrderModifiers.AddOrUpdate(Archimonde, new Modifier(9999));
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(4200));
                AddLog("阿克蒙德：10费命中直拍规则，前置落地避免小怪先占格");
                return;
            }

            // 用户硬规则：套外恶魔至少6才拍阿克蒙德，低于6一律后置。
            if (!readyByOutsideDemons)
            {
                int delayValue = board.MinionFriend.Count >= 2 ? 3600 : 2200;
                p.CastMinionsModifiers.AddOrUpdate(Archimonde, new Modifier(delayValue));
                p.PlayOrderModifiers.AddOrUpdate(Archimonde, new Modifier(-9000));
                AddLog("阿克蒙德：场坟套外恶魔=" + outsideDeckDemonsInBoardAndGrave + " < 6，未达拍出条件，本回合后置不打");
                return;
            }

            // 用户规则：阿克蒙德拍出的前提是“己方场面优势”。
            if (!boardAdvantage)
            {
                int delayValue = GetHeroHealth(board.HeroFriend) <= 12 ? 4200 : 2600;
                p.CastMinionsModifiers.AddOrUpdate(Archimonde, new Modifier(delayValue));
                p.PlayOrderModifiers.AddOrUpdate(Archimonde, new Modifier(-9200));
                AddLog("阿克蒙德：未达场面优势（" + boardAdvantageSnapshot + "），后置不打");
                return;
            }

            // 用户规则更新：一旦决定拍阿克蒙德，必须第一优先，
            // 避免低费随从先占复活随从场位。
            if (GetFreeBoardSlots(board) >= 1)
            {
                if (onBoardLethalNow)
                {
                    p.PlayOrderModifiers.AddOrUpdate(Archimonde, new Modifier(-9800));
                    AddLog("阿克蒙德：已具备场攻斩杀，后置不抢先");
                    return;
                }

                if (TryDelayArchimondeForSendableAttacks(board, p))
                    return;

                SetSingleCardCombo(board, p, Archimonde, allowCoinBridge: true, forceOverride: true,
                    logWhenSet: "阿克蒙德：命中拍出条件，ComboSet强制先手");
                ForceArchimondeFirstThisTurn(board, p, manaNow);
                p.CastMinionsModifiers.AddOrUpdate(Archimonde, new Modifier(-6200));
                p.PlayOrderModifiers.AddOrUpdate(Archimonde, new Modifier(9999));
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(4200));
                AddLog("阿克蒙德：命中拍出条件，强制第一优先避免低费随从占复活位");
                return;
            }

            p.PlayOrderModifiers.AddOrUpdate(Archimonde, new Modifier(-9500));
            AddLog("阿克蒙德：已使用场坟套外恶魔=" + outsideDeckDemonsInBoardAndGrave + " >= 6，后置等待可用场位");
            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Archimonde, new Modifier(150));
        }

        // 基尔加丹：
        // 1) 默认“牌库未空后置(+200)”；
        // 2) 用户规则：血量无忧 或 局势占优 时，直接先手落地；
        // 3) 牌库未空但手里无其他可用手牌动作时，也允许落地。
        private void HandleKiljaeden(Board board, ProfileParameters p)
        {
            if (board == null || board.Hand == null || p == null)
                return;
            if (!board.HasCardInHand(Kiljaeden))
                return;

            // 用户要求：其他时候写个200就行。
            p.CastMinionsModifiers.AddOrUpdate(Kiljaeden, new Modifier(200));

            int manaNow = GetAvailableManaIncludingCoin(board);
            bool playableNow = board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == Kiljaeden
                && c.CurrentCost <= manaNow);
            int freeSlots = GetFreeBoardSlots(board);
            bool deckEmpty = board.FriendDeckCount <= 0;
            string boardAdvantageSnapshot;
            bool boardAdvantage = HasFriendlyBoardAdvantageForLegendaryDemons(board, out boardAdvantageSnapshot);
            int friendHp = GetHeroHealth(board.HeroFriend);
            int enemyHp = GetHeroHealth(board.HeroEnemy);
            int enemyAttackSnapshot = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            bool pressureWindow = ShouldEnterBalancedDefenseMode(board, friendHp, enemyHp);
            bool dangerousHpWindow = IsDangerousHpTauntTempoWindow(friendHp, enemyAttackSnapshot);
            bool emergencyWindow = (friendHp <= 8 && enemyAttackSnapshot > 0) || (friendHp <= 12 && enemyAttackSnapshot >= 8);
            bool safeHpWindow = friendHp >= 15 && enemyAttackSnapshot <= 7;
            bool enemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            int friendAttackNow = GetAttackableBoardAttack(board.MinionFriend);
            bool onBoardLethal = !enemyHasTaunt && friendAttackNow >= enemyHp;

            int otherPlayableHandCards = board.Hand.Count(c =>
                c != null
                && c.Template != null
                && c.Template.Id != Kiljaeden
                && c.Template.Id != TheCoin
                && c.CurrentCost <= manaNow
                && !(c.Type == Card.CType.MINION && freeSlots <= 0)
                && !(c.Template.Id == EntropicContinuity && (board.MinionFriend == null || board.MinionFriend.Count == 0)));
            bool noOtherPlayableHandCards = otherPlayableHandCards <= 0;

            if (!deckEmpty)
            {
                if (playableNow && freeSlots > 0)
                {
                    if (onBoardLethal)
                    {
                        AddLog("基尔加丹：场攻可斩杀，后置不插队");
                        return;
                    }

                    // 用户新规则：血量无忧 或 局势占优 时，基尔加丹直接先手落地，尽早转化为终结压力。
                    if (safeHpWindow || boardAdvantage)
                    {
                        SetSingleCardCombo(board, p, Kiljaeden, allowCoinBridge: true, forceOverride: true,
                            logWhenSet: "基尔加丹：血量无忧/局势占优，ComboSet强制先手");
                        ForceKiljaedenFirstThisTurn(board, p, manaNow);
                        p.CastMinionsModifiers.AddOrUpdate(Kiljaeden, new Modifier(-3200));
                        p.PlayOrderModifiers.AddOrUpdate(Kiljaeden, new Modifier(9999));
                        p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(2600));
                        AddLog("基尔加丹：血量无忧或局势占优，命中直拍规则，强制先手落地");
                        return;
                    }

                    if (!dangerousHpWindow && !pressureWindow && !emergencyWindow)
                    {
                        p.CastMinionsModifiers.AddOrUpdate(Kiljaeden, new Modifier(-1250));
                        p.PlayOrderModifiers.AddOrUpdate(Kiljaeden, new Modifier(7600));
                        p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(2200));
                        AddLog("基尔加丹：局势不危急，前置主动落地");
                        return;
                    }

                    if (noOtherPlayableHandCards)
                    {
                        if (!boardAdvantage)
                        {
                            p.CastMinionsModifiers.AddOrUpdate(Kiljaeden, new Modifier(1800));
                            p.PlayOrderModifiers.AddOrUpdate(Kiljaeden, new Modifier(-9100));
                            AddLog("基尔加丹：牌库未空且未达场面优势（" + boardAdvantageSnapshot + "），后置保留");
                            return;
                        }

                        p.CastMinionsModifiers.AddOrUpdate(Kiljaeden, new Modifier(-900));
                        p.PlayOrderModifiers.AddOrUpdate(Kiljaeden, new Modifier(7000));
                        p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(2600));
                        AddLog("基尔加丹：牌库未空但无其他可用手牌，放宽允许落地");
                        return;
                    }
                }

                AddLog("基尔加丹：牌库未空，按规则后置（+200）");
                return;
            }

            if (!playableNow || freeSlots <= 0)
            {
                AddLog("基尔加丹：牌库已空，等待可用费用/场位");
                return;
            }

            p.CastMinionsModifiers.AddOrUpdate(Kiljaeden, new Modifier(-2600));
            p.PlayOrderModifiers.AddOrUpdate(Kiljaeden, new Modifier(9200));
            AddLog("基尔加丹：牌库为空，优先落地");
        }

        private void ForceKiljaedenFirstThisTurn(Board board, ProfileParameters p, int manaNow)
        {
            if (board == null || board.Hand == null || p == null)
                return;

            foreach (var card in board.Hand.Where(c => c != null && c.Template != null))
            {
                if (card.Template.Id == Kiljaeden || card.Template.Id == TheCoin)
                    continue;
                if (card.CurrentCost > manaNow)
                    continue;

                if (card.Type == Card.CType.MINION)
                    p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(3200));
                else if (card.Type == Card.CType.SPELL)
                    p.CastSpellsModifiers.AddOrUpdate(card.Template.Id, new Modifier(3200));
                else if (card.Type == Card.CType.WEAPON)
                    p.CastWeaponsModifiers.AddOrUpdate(card.Template.Id, new Modifier(3200));

                p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(-9800));
            }
        }

        // 用户修正：当基尔加丹命中“直拍先手”窗口时，非临时挟持射线链路应让位。
        private bool ShouldPreferKiljaedenImmediateOverRayChain(Board board, int manaNow = -1)
        {
            if (board == null || board.Hand == null)
                return false;
            if (!board.HasCardInHand(Kiljaeden))
                return false;

            if (manaNow < 0)
                manaNow = GetAvailableManaIncludingCoin(board);

            bool kiljaedenPlayableNow = board.Hand.Any(c => c != null
                && c.Template != null
                && c.Template.Id == Kiljaeden
                && c.CurrentCost <= manaNow);
            if (!kiljaedenPlayableNow)
                return false;

            if (GetFreeBoardSlots(board) <= 0)
                return false;

            int friendHp = GetHeroHealth(board.HeroFriend);
            int enemyHp = GetHeroHealth(board.HeroEnemy);
            int enemyAttackSnapshot = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            bool enemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            int friendAttackNow = GetAttackableBoardAttack(board.MinionFriend);
            bool onBoardLethal = !enemyHasTaunt && friendAttackNow >= enemyHp;
            if (onBoardLethal)
                return false;

            // 牌库已空时，基尔加丹默认应优先落地，不应再被射线续链抢先。
            if (board.FriendDeckCount <= 0)
                return true;

            bool safeHpWindow = friendHp >= 15 && enemyAttackSnapshot <= 7;
            bool boardAdvantage = HasFriendlyBoardAdvantageForLegendaryDemons(board, out _);
            return safeHpWindow || boardAdvantage;
        }

        // 传奇高费恶魔（基尔加丹/阿克蒙德）门槛：
        // 仅在“己方场面优势”时，才允许进入发现/落地强优先窗口。
        private bool HasFriendlyBoardAdvantageForLegendaryDemons(Board board, out string snapshot)
        {
            snapshot = "board为空";
            if (board == null)
                return false;

            int friendCount = board.MinionFriend == null ? 0 : board.MinionFriend.Count(m => m != null);
            int enemyCount = board.MinionEnemy == null ? 0 : board.MinionEnemy.Count(m => m != null);
            int friendAttackNow = GetAttackableBoardAttack(board.MinionFriend);
            int enemyAttackNow = GetAttackableBoardAttack(board.MinionEnemy);
            int friendTotalAttack = GetBoardAttack(board.MinionFriend);
            int enemyTotalAttack = GetBoardAttack(board.MinionEnemy);

            int friendStats = board.MinionFriend == null
                ? 0
                : board.MinionFriend.Where(m => m != null).Sum(m => Math.Max(0, m.CurrentAtk) + Math.Max(0, m.CurrentHealth));
            int enemyStats = board.MinionEnemy == null
                ? 0
                : board.MinionEnemy.Where(m => m != null).Sum(m => Math.Max(0, m.CurrentAtk) + Math.Max(0, m.CurrentHealth));

            bool countLead = friendCount >= enemyCount + 1;
            bool attackLead = friendAttackNow >= enemyAttackNow + 1;
            bool totalAttackLead = friendTotalAttack >= enemyTotalAttack + 2;
            bool statsLead = friendStats >= enemyStats + 2;
            bool hasBoard = friendCount > 0;

            snapshot = "随从" + friendCount + ":" + enemyCount
                + " | 可攻" + friendAttackNow + ":" + enemyAttackNow
                + " | 总攻" + friendTotalAttack + ":" + enemyTotalAttack
                + " | 身材" + friendStats + ":" + enemyStats;

            return hasBoard && (countLead || ((attackLead || totalAttackLead) && statsLead));
        }

        private void ForceAttackThenPlayMinionSameTurn(Board board, ProfileParameters p, Card.Cards focusCard, int manaNow, int focusCastMod, int focusPlayOrder, int otherDelay)
        {
            if (board == null || board.Hand == null || p == null)
                return;

            if (board.MinionFriend != null)
            {
                foreach (var friend in board.MinionFriend.Where(m => m != null && m.CanAttack && m.CurrentAtk > 0))
                {
                    p.AttackOrderModifiers.AddOrUpdate(friend.Template.Id, new Modifier(-9999));
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(friend.Template.Id, new Modifier(-260));
                }
            }

            foreach (var card in board.Hand.Where(c => c != null && c.Template != null))
            {
                if (card.Template.Id == focusCard || card.Template.Id == TheCoin)
                    continue;
                if (card.CurrentCost > manaNow)
                    continue;

                if (card.Type == Card.CType.MINION)
                    p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(otherDelay));
                else if (card.Type == Card.CType.SPELL)
                    p.CastSpellsModifiers.AddOrUpdate(card.Template.Id, new Modifier(otherDelay));
                else if (card.Type == Card.CType.WEAPON)
                    p.CastWeaponsModifiers.AddOrUpdate(card.Template.Id, new Modifier(otherDelay));

                p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(-9800));
            }

            p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(4200));
            p.CastMinionsModifiers.AddOrUpdate(focusCard, new Modifier(focusCastMod));
            p.PlayOrderModifiers.AddOrUpdate(focusCard, new Modifier(focusPlayOrder));
        }

        private int CountPlayableActionsExcludingCard(Board board, int manaNow, Card.Cards excludeCard)
        {
            if (board == null || board.Hand == null)
                return 0;

            int count = 0;
            int freeSlots = GetFreeBoardSlots(board);

            foreach (var c in board.Hand.Where(x => x != null && x.Template != null))
            {
                if (c.Template.Id == excludeCard || c.Template.Id == TheCoin)
                    continue;
                if (c.CurrentCost > manaNow)
                    continue;
                if (c.Template.Id == EntropicContinuity && (board.MinionFriend == null || board.MinionFriend.Count == 0))
                    continue;
                if (c.Type == Card.CType.MINION && freeSlots <= 0)
                    continue;
                count++;
            }

            if (CanUseLifeTapNow(board))
                count++;

            return count;
        }

        private bool HasPlayableWindowShopperFamily(Board board, int manaNow)
        {
            if (board == null || board.Hand == null)
                return false;
            if (GetFreeBoardSlots(board) <= 0)
                return false;

            return board.Hand.Any(c =>
                c != null
                && c.Template != null
                && c.Type == Card.CType.MINION
                && c.CurrentCost <= manaNow
                && (string.Equals(c.Template.Id.ToString(), WindowShopperCardId, StringComparison.Ordinal)
                    || string.Equals(c.Template.Id.ToString(), WindowShopperMiniCardId, StringComparison.Ordinal)));
        }

        private int CountPlayableActionsExcludingZilliax(Board board, int manaNow)
        {
            if (board == null || board.Hand == null)
                return 0;

            int count = 0;
            int freeSlots = GetFreeBoardSlots(board);

            foreach (var c in board.Hand.Where(x => x != null && x.Template != null))
            {
                if (!ShouldCountAsOtherActionForZilliax(board, c, manaNow, freeSlots))
                    continue;
                count++;
            }

            if (CanUseLifeTapNow(board))
                count++;

            return count;
        }

        private bool ShouldCountAsOtherActionForZilliax(Board board, Card card, int manaNow, int freeSlots)
        {
            if (board == null || card == null || card.Template == null)
                return false;

            if (card.Template.Id == TheCoin
                || card.Template.Id == ZilliaxTickingPower
                || card.Template.Id == ZilliaxDeckBuilder)
                return false;

            if (card.CurrentCost > manaNow)
                return false;

            string cardId = card.Template.Id.ToString();
            if (string.Equals(cardId, CreationStarCardId, StringComparison.Ordinal)
                || string.Equals(cardId, TerminusStarCardId, StringComparison.Ordinal)
                || string.Equals(cardId, UnlicensedApothecaryCardId, StringComparison.Ordinal))
                return false;

            if (card.Template.Id == EntropicContinuity && (board.MinionFriend == null || board.MinionFriend.Count == 0))
                return false;

            // 射线当前不可打窗口时，不应被当作“可先做的动作”去挤压奇利亚斯。
            if (card.Template.Id == AbductionRay && !IsAbductionRayPlayableNow(board))
                return false;

            if (card.Type == Card.CType.MINION && freeSlots <= 0)
                return false;

            return true;
        }

        private void ForceArchimondeFirstThisTurn(Board board, ProfileParameters p, int manaNow)
        {
            if (board == null || board.Hand == null)
                return;

            foreach (var card in board.Hand.Where(c => c != null && c.Template != null))
            {
                if (card.Template.Id == Archimonde || card.Template.Id == TheCoin)
                    continue;
                if (card.CurrentCost > manaNow)
                    continue;

                if (card.Type == Card.CType.MINION)
                    p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(2200));
                else if (card.Type == Card.CType.SPELL)
                    p.CastSpellsModifiers.AddOrUpdate(card.Template.Id, new Modifier(2200));
                else if (card.Type == Card.CType.WEAPON)
                    p.CastWeaponsModifiers.AddOrUpdate(card.Template.Id, new Modifier(2200));

                p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(-9800));
            }

            p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(2600));
        }

        // 用户规则：若本回合计划拍阿克蒙德且场上存在“可送”随从，
        // 先完成送怪交换腾格，再落阿克蒙德，避免提前占位降低复活收益。
        private bool TryDelayArchimondeForSendableAttacks(Board board, ProfileParameters p)
        {
            if (board == null || p == null || board.MinionFriend == null || board.MinionEnemy == null)
                return false;
            if (board.MinionEnemy.Count == 0)
                return false;

            var attackableFriends = board.MinionFriend
                .Where(m => m != null && m.Template != null && m.CanAttack && m.CurrentAtk > 0)
                .ToList();
            if (attackableFriends.Count == 0)
                return false;

            var allEnemyTargets = board.MinionEnemy
                .Where(m => m != null && m.Template != null)
                .ToList();
            if (allEnemyTargets.Count == 0)
                return false;

            var sendableFriends = attackableFriends
                .Where(friend => allEnemyTargets.Any(enemy => enemy.CurrentAtk >= friend.CurrentHealth))
                .ToList();
            if (sendableFriends.Count == 0)
                return false;

            var sendableTemplateIds = new HashSet<Card.Cards>(sendableFriends.Select(m => m.Template.Id));

            foreach (var enemy in allEnemyTargets)
            {
                bool canKillSendable = sendableFriends.Any(friend => enemy.CurrentAtk >= friend.CurrentHealth);
                if (!canKillSendable && !enemy.IsTaunt)
                    continue;

                int pri = 900 + Math.Max(0, enemy.CurrentAtk) * 60;
                if (enemy.IsTaunt) pri += 1200;
                p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(Math.Min(pri, 9999)));
            }

            foreach (var friend in sendableFriends)
            {
                p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(friend.Template.Id, new Modifier(-2600));
                p.AttackOrderModifiers.AddOrUpdate(friend.Template.Id, new Modifier(-9999));
            }

            foreach (var friend in attackableFriends.Where(m => m != null && !sendableTemplateIds.Contains(m.Template.Id)))
            {
                p.AttackOrderModifiers.AddOrUpdate(friend.Template.Id, new Modifier(3600));
            }

            int manaNow = GetAvailableManaIncludingCoin(board);
            if (board.Hand != null)
            {
                foreach (var card in board.Hand.Where(c => c != null && c.Template != null))
                {
                    if (card.Template.Id == Archimonde || card.Template.Id == TheCoin)
                        continue;
                    if (card.CurrentCost > manaNow)
                        continue;

                    if (card.Type == Card.CType.MINION)
                        p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(2200));
                    else if (card.Type == Card.CType.SPELL)
                        p.CastSpellsModifiers.AddOrUpdate(card.Template.Id, new Modifier(2200));
                    else if (card.Type == Card.CType.WEAPON)
                        p.CastWeaponsModifiers.AddOrUpdate(card.Template.Id, new Modifier(2200));

                    p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(-9800));
                }
            }

            p.CastMinionsModifiers.AddOrUpdate(Archimonde, new Modifier(-4200));
            p.PlayOrderModifiers.AddOrUpdate(Archimonde, new Modifier(-9600));
            p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(4200));
            AddLog("阿克蒙德：命中可送窗口，先送" + sendableFriends.Count + "体再下阿克蒙德");
            return true;
        }

        // 奇利亚斯（输能+计数）：本套是“进攻增伤模块”，不是防守模块
        private void HandleZilliaxTickingPower(Board board, ProfileParameters p, int enemyHp, int friendAttack)
        {
            bool hasZilliax = board.HasCardInHand(ZilliaxTickingPower) || board.HasCardInHand(ZilliaxDeckBuilder);
            if (!hasZilliax)
                return;

            int manaNow = GetAvailableManaIncludingCoin(board);
            int freeBoardSlots = GetFreeBoardSlots(board);
            int friendCount = board.MinionFriend.Count;
            bool enemyHasTaunt = board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            int boardDamageAfterAura = friendAttack + friendCount;
            int zilliaxPlayableCost = GetPlayableZilliaxCost(board, manaNow);
            bool zilliaxPlayableNow = zilliaxPlayableCost <= manaNow;
            bool highCostZilliaxNow = zilliaxPlayableNow && zilliaxPlayableCost >= 5;
            bool rayHardLockWindow = ShouldForceAbductionRayNow(board);
            var zilliaxCard = board.Hand
                .Where(c => c != null && c.Template != null
                    && (c.Template.Id == ZilliaxTickingPower || c.Template.Id == ZilliaxDeckBuilder)
                    && c.CurrentCost <= manaNow)
                .OrderBy(c => c.CurrentCost)
                .ThenBy(c => c.Id)
                .FirstOrDefault();
            var oneCostMinion = board.Hand
                .Where(c => c != null && c.Template != null
                    && c.Type == Card.CType.MINION
                    && c.Template.Id != ZilliaxTickingPower
                    && c.Template.Id != ZilliaxDeckBuilder
                    && c.CurrentCost <= 1
                    && c.CurrentCost <= manaNow)
                .OrderBy(c => c.CurrentCost)
                .ThenBy(c => c.Id)
                .FirstOrDefault();
            int projectedZilliaxCostAfterOneDrop = Math.Max(0, zilliaxPlayableCost - 1);
            int oneDropCost = oneCostMinion != null ? Math.Max(0, oneCostMinion.CurrentCost) : 99;
            bool canPlayOneDropThenZilliax = zilliaxPlayableNow
                && oneCostMinion != null
                && freeBoardSlots >= 2
                && oneDropCost + Math.Min(zilliaxPlayableCost, projectedZilliaxCostAfterOneDrop) <= manaNow;
            Card.Cards zilliaxComboTarget = zilliaxCard != null
                ? zilliaxCard.Template.Id
                : (board.HasCardInHand(ZilliaxTickingPower) ? ZilliaxTickingPower : ZilliaxDeckBuilder);
            bool canPlayZilliaxThenEntropySameTurn = CanPlayZilliaxThenEntropySameTurn(board, manaNow);
            bool canPlayPartyThenZilliax = CanPlayPartyThenZilliaxSameTurn(board, manaNow);
            int minOtherPlayableCost = 99;
            foreach (var card in board.Hand.Where(c => c != null && c.Template != null))
            {
                if (!ShouldCountAsOtherActionForZilliax(board, card, manaNow, freeBoardSlots))
                    continue;
                if (card.CurrentCost < minOtherPlayableCost)
                    minOtherPlayableCost = card.CurrentCost;
            }
            bool canPlayOtherThenZilliax = zilliaxCard != null
                && zilliaxPlayableNow
                && minOtherPlayableCost < 99
                && zilliaxCard.CurrentCost + minOtherPlayableCost <= manaNow;
            bool hasAnyOtherPlayableActionNow = CountPlayableActionsExcludingZilliax(board, manaNow) > 0;
            bool hasLowCurvePlayable = board.Hand.Any(c => c != null && c.Template != null
                && ShouldCountAsOtherActionForZilliax(board, c, manaNow, freeBoardSlots)
                && c.CurrentCost <= 3);
            bool preferForebodingNow = board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == ForebodingFlame
                && c.CurrentCost <= manaNow);

            p.CastMinionsModifiers.AddOrUpdate(ZilliaxTickingPower, new Modifier(-110));
            p.CastMinionsModifiers.AddOrUpdate(ZilliaxDeckBuilder, new Modifier(-110));

            if (friendCount == 0)
            {
                p.CastMinionsModifiers.AddOrUpdate(ZilliaxTickingPower, new Modifier(260));
                p.CastMinionsModifiers.AddOrUpdate(ZilliaxDeckBuilder, new Modifier(260));
            }
            else if (friendCount >= 2)
            {
                p.CastMinionsModifiers.AddOrUpdate(ZilliaxTickingPower, new Modifier(-220));
                p.CastMinionsModifiers.AddOrUpdate(ZilliaxDeckBuilder, new Modifier(-220));
            }

            // 用户规则：射线连发锁定优先级更高，奇利亚斯不允许插队。
            if (rayHardLockWindow && zilliaxPlayableNow)
            {
                p.CastMinionsModifiers.AddOrUpdate(ZilliaxTickingPower, new Modifier(2600));
                p.CastMinionsModifiers.AddOrUpdate(ZilliaxDeckBuilder, new Modifier(2600));
                p.PlayOrderModifiers.AddOrUpdate(ZilliaxTickingPower, new Modifier(-9800));
                p.PlayOrderModifiers.AddOrUpdate(ZilliaxDeckBuilder, new Modifier(-9800));
                AddLog("奇利亚斯：命中射线连发锁定，后置不插队");
                return;
            }

            // 用户硬规则：存在“其他1费随从 + 奇利亚斯”同回合连打时，强制先1费再奇利亚斯。
            if (canPlayOneDropThenZilliax)
            {
                SetTwoCardCombo(board, p, oneCostMinion.Template.Id, zilliaxComboTarget,
                    allowCoinBridge: true, forceOverride: true,
                    logWhenSet: "奇利亚斯：硬规则先1费随从后奇利亚斯");

                p.CastMinionsModifiers.AddOrUpdate(oneCostMinion.Template.Id, new Modifier(-3200));
                p.PlayOrderModifiers.AddOrUpdate(oneCostMinion.Template.Id, new Modifier(9999));
                p.CastMinionsModifiers.AddOrUpdate(ZilliaxTickingPower, new Modifier(1500));
                p.CastMinionsModifiers.AddOrUpdate(ZilliaxDeckBuilder, new Modifier(1500));
                p.PlayOrderModifiers.AddOrUpdate(ZilliaxTickingPower, new Modifier(-9800));
                p.PlayOrderModifiers.AddOrUpdate(ZilliaxDeckBuilder, new Modifier(-9800));
                AddLog("奇利亚斯：检测到可先下其他1费随从，强制后置到1费之后");
                return;
            }

            // 用户硬规则：若本回合可“先做其他动作再下奇利亚斯”，奇利亚斯必须后置到末段。
            // 例外：保留“先奇利亚斯后续连熵能”的既定连招窗口。
            if (zilliaxPlayableNow
                && canPlayOtherThenZilliax
                && !canPlayZilliaxThenEntropySameTurn
                && !canPlayOneDropThenZilliax)
            {
                p.CastMinionsModifiers.AddOrUpdate(ZilliaxTickingPower, new Modifier(4200));
                p.CastMinionsModifiers.AddOrUpdate(ZilliaxDeckBuilder, new Modifier(4200));
                p.PlayOrderModifiers.AddOrUpdate(ZilliaxTickingPower, new Modifier(-9950));
                p.PlayOrderModifiers.AddOrUpdate(ZilliaxDeckBuilder, new Modifier(-9950));
                AddLog("奇利亚斯：可先做其他动作，强制后置到本回合末段");
                return;
            }

            // 用户口径：同回合可“派对邪犬 + 奇利亚斯”时，必须先下邪犬再下奇利亚斯。
            // 目的：优先吃到邪犬铺场带来的降费与光环增伤，不浪费当回合剩余费用。
            if (canPlayPartyThenZilliax)
            {
                SetTwoCardCombo(board, p, PartyFiend, zilliaxCard.Template.Id,
                    allowCoinBridge: true, forceOverride: true,
                    logWhenSet: "奇利亚斯：ComboSet先派对邪犬后奇利亚斯");

                p.CastMinionsModifiers.AddOrUpdate(PartyFiend, new Modifier(-3200));
                p.PlayOrderModifiers.AddOrUpdate(PartyFiend, new Modifier(9999));
                p.CastMinionsModifiers.AddOrUpdate(ZilliaxTickingPower, new Modifier(900));
                p.CastMinionsModifiers.AddOrUpdate(ZilliaxDeckBuilder, new Modifier(900));
                p.PlayOrderModifiers.AddOrUpdate(ZilliaxTickingPower, new Modifier(-9000));
                p.PlayOrderModifiers.AddOrUpdate(ZilliaxDeckBuilder, new Modifier(-9000));
                AddLog("奇利亚斯：命中先邪犬后奇利亚斯规则");
                return;
            }

            if (!enemyHasTaunt && boardDamageAfterAura >= enemyHp && !hasAnyOtherPlayableActionNow)
            {
                p.CastMinionsModifiers.AddOrUpdate(ZilliaxTickingPower, new Modifier(-350));
                p.CastMinionsModifiers.AddOrUpdate(ZilliaxDeckBuilder, new Modifier(-350));
                AddLog("奇利亚斯：光环补伤可斩杀且无其他动作，强制优先");
                return;
            }

            // 用户硬规则：奇利亚斯不是低费形态（>=5）时，只要有其他动作就不先拍奇利亚斯。
            if (highCostZilliaxNow && hasAnyOtherPlayableActionNow)
            {
                p.CastMinionsModifiers.AddOrUpdate(ZilliaxTickingPower, new Modifier(3800));
                p.CastMinionsModifiers.AddOrUpdate(ZilliaxDeckBuilder, new Modifier(3800));
                p.PlayOrderModifiers.AddOrUpdate(ZilliaxTickingPower, new Modifier(-9800));
                p.PlayOrderModifiers.AddOrUpdate(ZilliaxDeckBuilder, new Modifier(-9800));
                AddLog("奇利亚斯：非低费形态且有其他动作，硬后置不先拍");
                return;
            }

            // 用户规则：只要费用足够在本回合“先做其他动作再下奇利亚斯”，则奇利亚斯后置到最后。
            // 例外：同回合“先奇利亚斯后续连熵能”规则仍保留，不在这里后置。
            if (canPlayOtherThenZilliax && !canPlayZilliaxThenEntropySameTurn)
            {
                p.CastMinionsModifiers.AddOrUpdate(ZilliaxTickingPower, new Modifier(950));
                p.CastMinionsModifiers.AddOrUpdate(ZilliaxDeckBuilder, new Modifier(950));
                p.PlayOrderModifiers.AddOrUpdate(ZilliaxTickingPower, new Modifier(-9000));
                p.PlayOrderModifiers.AddOrUpdate(ZilliaxDeckBuilder, new Modifier(-9000));
                AddLog("奇利亚斯：费用充足可先做其他动作，后置到最后下");
                return;
            }

            // 用户硬规则：高费奇利亚斯（>=5）不应直接裸拍，先打低费节奏动作（尤其恶兆邪火）。
            if (highCostZilliaxNow && (hasLowCurvePlayable || preferForebodingNow))
            {
                p.CastMinionsModifiers.AddOrUpdate(ZilliaxTickingPower, new Modifier(1200));
                p.CastMinionsModifiers.AddOrUpdate(ZilliaxDeckBuilder, new Modifier(1200));
                p.PlayOrderModifiers.AddOrUpdate(ZilliaxTickingPower, new Modifier(-9200));
                p.PlayOrderModifiers.AddOrUpdate(ZilliaxDeckBuilder, new Modifier(-9200));
                AddLog("奇利亚斯：当前高费形态且有低费动作，后置避免直接拍出");
                return;
            }

            // 为了让光环先给到场面，出牌顺序前置
            p.PlayOrderModifiers.AddOrUpdate(ZilliaxTickingPower, new Modifier(420));
            p.PlayOrderModifiers.AddOrUpdate(ZilliaxDeckBuilder, new Modifier(420));
            p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(ZilliaxTickingPower, new Modifier(160));
        }

        private int GetPlayableZilliaxCost(Board board, int manaNow)
        {
            if (board == null || board.Hand == null)
                return 99;

            int best = 99;
            foreach (var c in board.Hand.Where(x => x != null && x.Template != null))
            {
                if (c.Template.Id != ZilliaxTickingPower && c.Template.Id != ZilliaxDeckBuilder)
                    continue;
                if (c.CurrentCost > manaNow)
                    continue;
                if (c.CurrentCost < best)
                    best = c.CurrentCost;
            }
            return best;
        }

        // 幸运币：前期可用于跳费抢节奏，后期仍避免无收益硬币。
        private void HandleCoin(Board board, ProfileParameters p, int enemyHp, int friendAttack)
        {
            if (!board.HasCardInHand(TheCoin))
                return;
            if (ShouldLockCoinBeforeTurnThreeForSketch(board))
            {
                p.CastSpellsModifiers.AddOrUpdate(TheCoin, new Modifier(9999));
                p.PlayOrderModifiers.AddOrUpdate(TheCoin, new Modifier(-9999));
                AddLog("幸运币：手有速写美术家且未到3费回合，硬规则禁用");
                return;
            }

            bool enemyHasTaunt = board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            if (!enemyHasTaunt && friendAttack >= enemyHp)
            {
                p.CastSpellsModifiers.AddOrUpdate(TheCoin, new Modifier(9999));
                p.PlayOrderModifiers.AddOrUpdate(TheCoin, new Modifier(-9999));
                AddLog("幸运币：已存在场攻斩杀，不使用幸运币");
                return;
            }

            int manaNow = GetAvailableManaIncludingCoin(board);
            if (ShouldCoinIntoPartyFiendNow(board, manaNow))
            {
                p.CastSpellsModifiers.AddOrUpdate(TheCoin, new Modifier(-5200));
                p.PlayOrderModifiers.AddOrUpdate(TheCoin, new Modifier(9999));
                AddLog("幸运币：命中跳币邪犬窗口，前置硬币");
                return;
            }

            int friendHp = GetHeroHealth(board.HeroFriend);
            int enemyAttack = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            bool storytellerOneDropDelayForebodingWindow = ShouldDelayForebodingForEarlyStorytellerOneDrops(board, manaNow, friendHp, enemyAttack);
            if (storytellerOneDropDelayForebodingWindow)
            {
                p.CastSpellsModifiers.AddOrUpdate(TheCoin, new Modifier(1200));
                p.PlayOrderModifiers.AddOrUpdate(TheCoin, new Modifier(-9900));
                AddLog("幸运币：命中始祖龟+异种族1费窗口，后置避免跳币抢邪火");
                return;
            }

            p.CastSpellsModifiers.AddOrUpdate(TheCoin, new Modifier(360));
            p.PlayOrderModifiers.AddOrUpdate(TheCoin, new Modifier(-9200));
            AddLog("幸运币：默认后置，非关键回合不主动使用");

            int realMana = Math.Max(0, board.ManaAvailable);
            var unlockedByCoin = board.Hand
                .Where(c => c != null && c.Template != null
                            && c.Template.Id != TheCoin
                            && c.CurrentCost > realMana
                            && c.CurrentCost <= manaNow)
                .ToList();

            bool earlyTurn = board.MaxMana <= 4;
            bool unlocksMinion = unlockedByCoin.Any(c => c.Type == Card.CType.MINION);
            bool enemyHasBoard = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null);
            bool onlyUnlocksOneCost = unlockedByCoin.All(c => c != null && c.CurrentCost <= 1);
            bool preserveCoinForLowPressure = realMana == 0 && !enemyHasBoard && onlyUnlocksOneCost;

            if (earlyTurn && unlockedByCoin.Count > 0)
            {
                if (preserveCoinForLowPressure)
                {
                    p.CastSpellsModifiers.AddOrUpdate(TheCoin, new Modifier(900));
                    p.PlayOrderModifiers.AddOrUpdate(TheCoin, new Modifier(-9800));
                    AddLog("幸运币：空场仅解锁1费动作，保留硬币避免低价值跳币");
                }
                else
                {
                    p.CastSpellsModifiers.AddOrUpdate(TheCoin, new Modifier(unlocksMinion ? -1400 : -900));
                    p.PlayOrderModifiers.AddOrUpdate(TheCoin, new Modifier(4200));
                    AddLog("幸运币：前期跳费用于展开，提升使用优先级");
                }
            }

            // 若临时挟持射线可转化但当前真费不足，允许硬币前置衔接射线。
            if (HasTemporaryPlayableAbductionRay(board) && realMana < 1)
            {
                p.CastSpellsModifiers.AddOrUpdate(TheCoin, new Modifier(-2600));
                p.PlayOrderModifiers.AddOrUpdate(TheCoin, new Modifier(9800));
                AddLog("幸运币：临时挟持射线可转化，前置硬币");
            }
        }

        private void HandleHeroPower(Board board, ProfileParameters p, int friendHp)
        {
            int manaNow = GetAvailableManaIncludingCoin(board);
            AbductionRayStartupState rayStartupState = EvaluateAbductionRayStartupState(board, manaNow);
            int enemyAttack = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            bool hasPlayableMinion = HasPlayableMinionWithBoardSpace(board, manaNow);
            bool windowShopperPlayableNow = HasPlayableWindowShopperFamily(board, manaNow);
            int playableOutsideDeckDemonsNow = GetMaxPlayableOutsideDeckDemonsThisTurn(board, manaNow);
            bool hasPlayableMinionExceptStoryteller = HasPlayableMinionWithBoardSpaceExcluding(board, manaNow, TortollanStoryteller);
            bool moargPriorityWindow = ShouldPrioritizeMoargForgefiendNow(board);
            bool pressureDigTapWindow = ShouldPrioritizeLifeTapForPressureDig(board, friendHp, manaNow, hasPlayableMinion);
            bool sketchPlayableNow = board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == SketchArtist
                && c.CurrentCost <= manaNow)
                && manaNow >= 4
                && GetFreeBoardSlots(board) > 0;
            bool shadowflameStalkerPlayableNow = board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == ShadowflameStalker
                && c.CurrentCost <= manaNow)
                && GetFreeBoardSlots(board) > 0;
            bool furiousPriestessPlayableNow = board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == FuriousPriestess
                && c.CurrentCost <= manaNow)
                && GetFreeBoardSlots(board) > 0;
            bool forebodingPlayableNow = board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == ForebodingFlame
                && c.CurrentCost <= manaNow)
                && GetFreeBoardSlots(board) > 0;
            bool storytellerHighValuePlayableNow = IsHighValueStorytellerPlayableNow(board, manaNow);
            bool wispStorytellerWindow = CanPlayWispThenStorytellerSameTurn(board, manaNow);
            bool storytellerDualOneDropWindow = HasStorytellerDualOneDropRaceWindow(board, manaNow);
            bool rayPlayableNow = IsAbductionRayPlayableNow(board);
            bool forceRayNow = ShouldForceAbductionRayNow(board);
            bool shouldBlockLifeTapForRayChain = ShouldBlockLifeTapForAbductionRay(board, rayStartupState, forceRayNow);
            bool rayChainOrTempWindow = IsAbductionRayChainOrTempWindow(board, rayStartupState, rayPlayableNow, forceRayNow);
            bool rayOverdrawHigh = IsAbductionRayOverdrawRiskHigh(board, rayChainOrTempWindow);
            bool enemyHasMinionNow = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null);
            bool friendHasMinionNow = board.MinionFriend != null && board.MinionFriend.Any(m => m != null);
            bool emptyBoardNoWispStorytellerWindow = (board.MinionFriend == null || board.MinionFriend.Count == 0)
                && !board.HasCardInHand(Wisp)
                && board.Hand.Any(c => c != null && c.Template != null
                    && c.Template.Id == TortollanStoryteller
                    && c.CurrentCost <= manaNow);
            var playableMinionsExceptStoryteller = board.Hand
                .Where(c => c != null
                    && c.Template != null
                    && c.Type == Card.CType.MINION
                    && c.Template.Id != TortollanStoryteller
                    && c.CurrentCost <= manaNow)
                .ToList();
            bool onlyArchimondeAsAlternativeTempo = playableMinionsExceptStoryteller.Count > 0
                && playableMinionsExceptStoryteller.All(c => c.Template.Id == Archimonde);
            int outsideDeckDemonsInBoardAndGrave = CountOutsideDeckDemonsInBoardAndGrave(board);
            bool onlyUnreadyArchimondeAsAlternativeTempo = onlyArchimondeAsAlternativeTempo
                && outsideDeckDemonsInBoardAndGrave < 6;
            int additionalBodiesWithoutStoryteller = GetMaxAdditionalBodiesWithBudgetExcludingCard(board, manaNow, TortollanStoryteller);
            bool hasAlternativeTempoWithoutStoryteller = additionalBodiesWithoutStoryteller > 0 || hasPlayableMinionExceptStoryteller;
            bool dangerousHpTauntTempoWindow = IsDangerousHpTauntTempoWindow(friendHp, enemyAttack);
            var playableTauntMinionIds = GetPlayableTauntMinionCardIds(board, manaNow);
            bool hasPlayableTauntMinionNow = playableTauntMinionIds.Count > 0;
            bool hasPlayableEnergyAmuletNow = board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == EnergyAmulet
                && c.CurrentCost <= manaNow);
            bool desperateHailMaryTapWindow = ShouldForceDesperateLifeTap(
                board,
                friendHp,
                enemyAttack,
                manaNow,
                hasPlayableTauntMinionNow,
                rayChainOrTempWindow);

            // 用户硬规则：临时射线/速写射线/可续链射线窗口，一律禁用分流，避免断链抽一口。
            if (shouldBlockLifeTapForRayChain)
            {
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(9999));
                AddLog("分流：检测到可续挟持射线链（含临时/速写/连发），硬规则禁用分流");
                return;
            }

            int enemyHpForEnergyAmuletTap = GetHeroHealth(board.HeroEnemy);
            int attackableFriendAttackForEnergyAmuletTap = GetAttackableBoardAttack(board.MinionFriend);
            bool enemyHasTauntForEnergyAmuletTap = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            bool onBoardLethalForEnergyAmuletTap = !enemyHasTauntForEnergyAmuletTap
                && attackableFriendAttackForEnergyAmuletTap >= enemyHpForEnergyAmuletTap;
            bool shouldBlockLifeTapForEnergyAmulet = hasPlayableEnergyAmuletNow
                && !onBoardLethalForEnergyAmuletTap
                && (friendHp <= 8 || enemyAttack >= friendHp || dangerousHpTauntTempoWindow);
            if (shouldBlockLifeTapForEnergyAmulet)
            {
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(9999));
                AddLog("分流：低血高压且手握能量护符，先回血，禁用先分流");
                return;
            }

            bool forceInfernalEmergencyWindow = ShouldForceInfernalEmergency(board, friendHp, manaNow);
            if (forceInfernalEmergencyWindow)
            {
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(9999));
                AddLog("分流：3血地狱火保命窗口，硬规则禁用先分流");
                return;
            }

            // 用户规则：命中墓先手窗口时，分流必须后置到墓之后。
            if (ShouldForceTombFirstThisTurn(board))
            {
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(5200));
                AddLog("分流：咒怨之墓回合，后置到墓之后");
                return;
            }

            // 用户反馈：危险血线且手里有可下嘲讽时，禁止分流抢费，先下嘲讽稳场。
            if (dangerousHpTauntTempoWindow && hasPlayableTauntMinionNow)
            {
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(5600));
                AddLog("分流：危险血线且有可下嘲讽，后置分流优先先下嘲讽");
                return;
            }

            // 用户反馈：绝境且无稳场线时，允许先抽一口搏命找解/嘲讽。
            if (desperateHailMaryTapWindow)
            {
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(-5800));
                AddLog("分流：绝境搏命线，先抽一口找解/嘲讽；若无解则准备认输");
                return;
            }

            // 用户规则：低血且被敌方嘲讽/高压阻断斩杀时，优先先抽一口找解，
            // 不要继续无脑铺场把费用打光。
            bool enemyHasTauntNow = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            int enemyHp = GetHeroHealth(board.HeroEnemy);
            int attackableFriendAttackNow = GetAttackableBoardAttack(board.MinionFriend);
            bool onBoardLethalNow = !enemyHasTauntNow && attackableFriendAttackNow >= enemyHp;
            bool hasPlayableOutsideDeckDemonNow = playableOutsideDeckDemonsNow > 0;
            bool lowHpForceTapOutsideDeckDemonExempt = CanUseLifeTapNow(board)
                && friendHp >= 3
                && friendHp <= 9
                && enemyHasMinionNow
                && hasPlayableMinion
                && hasPlayableOutsideDeckDemonNow
                && !onBoardLethalNow;
            if (lowHpForceTapOutsideDeckDemonExempt)
            {
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(6200));
                AddLog("分流：低血强制分流豁免，手里有可下套外恶魔，后置分流先走场面");
                return;
            }

            bool lowHpDigBeforeFloodWindow = CanUseLifeTapNow(board)
                && friendHp >= 3
                && friendHp <= 9
                && enemyHasMinionNow
                && hasPlayableMinion
                && !windowShopperPlayableNow
                && !hasPlayableOutsideDeckDemonNow
                && !hasPlayableTauntMinionNow
                && !onBoardLethalNow
                && (enemyHasTauntNow || enemyAttack >= 8 || enemyAttack >= friendHp - 1);
            if (lowHpDigBeforeFloodWindow)
            {
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(-7600));
                AddLog("分流：低血且敌方嘲讽/高压阻断斩杀，强制先抽一口找解再决策");
                return;
            }
            if (CanUseLifeTapNow(board)
                && friendHp >= 3
                && friendHp <= 9
                && enemyHasMinionNow
                && hasPlayableMinion
                && (windowShopperPlayableNow || hasPlayableOutsideDeckDemonNow)
                && !onBoardLethalNow)
            {
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(5200));
                AddLog("分流：低血窗口但有可下橱窗看客/套外恶魔，后置分流优先先铺场稳场");
                return;
            }

            // 用户反馈：空场低压时，优先抽一口找关键资源，不急于硬币跳1费动作。
            bool calmEmptyBoardDigWindow = !enemyHasMinionNow
                && !rayChainOrTempWindow
                && CanUseLifeTapNow(board)
                && friendHp >= 3
                && board.Hand.Count <= 6
                && !hasPlayableMinion
                && (board.MaxMana <= 3 || !friendHasMinionNow)
                && !forebodingPlayableNow
                && !ShouldForceTombFirstThisTurn(board);
            if (calmEmptyBoardDigWindow)
            {
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(-2100));
                AddLog("分流：空场低压资源窗口，优先抽一口");
                return;
            }

            // Avoid a pass turn when the only "tempo" option is an unready Archimonde.
            if (emptyBoardNoWispStorytellerWindow
                && onlyUnreadyArchimondeAsAlternativeTempo
                && CanUseLifeTapNow(board)
                && friendHp >= 3
                && board.Hand.Count < HearthstoneHandLimit)
            {
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(-2400));
                AddLog("分流：空场始祖龟窗口仅有未达条件阿克蒙德，优先抽一口防空过");
                return;
            }

            // 用户反馈：空场始祖龟窗口若存在其他可铺场动作，不应先抽，避免“疯狂分流”。
            if (emptyBoardNoWispStorytellerWindow && hasAlternativeTempoWithoutStoryteller)
            {
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(2600));
                AddLog("分流：空场始祖龟窗口但有可铺场动作，后置分流优先先铺场");
                return;
            }

            // 用户规则：空场且无小精灵时，不空打始祖龟；仅在没有其他可铺场动作时才允许先抽一口。
            if (emptyBoardNoWispStorytellerWindow && !hasAlternativeTempoWithoutStoryteller
                && CanUseLifeTapNow(board) && friendHp >= 3)
            {
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(-2600));
                AddLog("分流：空场且无小精灵，血线>=3，优先抽一口");
                return;
            }

            // 用户反馈：高压且莫尔葛熔魔可下时，分流后置，优先稳场。
            if (moargPriorityWindow)
            {
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(3200));
                AddLog("分流：高压且莫尔葛熔魔可用，后置分流优先先下熔魔");
                return;
            }

            // 用户规则：愤怒的女祭司可下时，不先分流，优先落地女祭司抢场面。
            if (furiousPriestessPlayableNow)
            {
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(3200));
                AddLog("分流：愤怒的女祭司可用，后置分流优先先下女祭司");
                return;
            }

            // 用户规则：恶兆邪火可下时，不先分流，优先落地邪火减费与站场。
            if (forebodingPlayableNow)
            {
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(3000));
                AddLog("分流：恶兆邪火可用，后置分流优先先下邪火");
                return;
            }

            // 用户规则：命中先抽后buff时，分流优先于续连，且允许极限血线（>=3）。
            // 仅在“非挟持射线连发窗口”下生效，避免打断射线链。
            if (!rayChainOrTempWindow && ShouldTapBeforeEntropicContinuity(board, friendHp))
            {
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(-5200));
                AddLog("分流：命中先抽后buff规则，强制先抽再续连");
                return;
            }

            if (friendHp <= 9)
            {
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(350));
                AddLog("分流：血线危险，降低优先级");
                return;
            }

            if (friendHp <= 12 && enemyAttack >= 8)
            {
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(1800));
                AddLog("分流：低血高压，后置分流优先解场");
                return;
            }

            // 速写优先：可用时先打速写，再考虑分流，避免分流插队掉速。
            if (sketchPlayableNow)
            {
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(3000));
                AddLog("分流：速写美术家可用，后置分流优先先下速写");
                return;
            }

            // 用户规则：影焰猎豹优先级下调，仅轻度后置分流，不再强让位。
            if (shadowflameStalkerPlayableNow)
            {
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(600));
                AddLog("分流：影焰猎豹可用，轻度后置分流");
                return;
            }

            if (storytellerHighValuePlayableNow && friendHp >= 10)
            {
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(1800));
                AddLog("分流：始祖龟高收益可落地，后置分流优先先下始祖龟");
                return;
            }

            if (wispStorytellerWindow && friendHp >= 10)
            {
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(1700));
                AddLog("分流：命中小精灵+始祖龟联动窗口，后置分流优先先铺场");
                return;
            }

            // 用户规则：始祖龟在场且可同回合连下“两张1费不同种族随从”时，先铺再抽。
            if (storytellerDualOneDropWindow && friendHp >= 10)
            {
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(2400));
                AddLog("分流：始祖龟在场且可连下两张异种族1费，后置分流优先铺场");
                return;
            }

            if (ShouldHoldForStorytellerBuff(board) && friendHp >= 14)
            {
                // 用户反馈：若本回合会空过2费以上，应先抽一口再结束，不浪费费用。
                if (!hasPlayableMinion && CanUseLifeTapNow(board) && board.Hand.Count <= 4)
                {
                    p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(-120));
                    AddLog("始祖龟窗口：无可下牌且有剩余费用，优先分流再结束");
                }
                else
                {
                    p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(520));
                    AddLog("始祖龟窗口：敌方空场，后置分流等待回合末buff");
                }
                return;
            }

            // 用户新规则：命中挟持射线连发/临时窗口时，分流让位，不先抽。
            if (rayChainOrTempWindow && !rayOverdrawHigh)
            {
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(2600));
                AddLog("分流：命中挟持射线连发窗口，后置分流优先继续打射线");
                return;
            }

            if (ShouldTapBeforeEntropicContinuity(board, friendHp))
            {
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(-5200));
                AddLog("分流：命中先抽后buff规则，强制先抽再续连");
                return;
            }

            // 用户规则：地狱火可用且费用允许同回合“分流+地狱火”时，先抽一口再下地狱火。
            if (!rayChainOrTempWindow && ShouldTapBeforeInfernal(board, friendHp, manaNow))
            {
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(-4200));
                AddLog("分流：命中先抽后地狱火规则，优先先抽一口");
                return;
            }

            // 用户反馈：攻守平衡且低费动作不足时，优先抽一口找解场/续航，而不是直接拍中费随从。
            if (pressureDigTapWindow)
            {
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(-1800));
                AddLog("分流：攻守平衡且低费动作不足，优先抽一口找解场/续航");
                return;
            }

            // 用户新规则：手上有可下随从时，分流应后置。
            if (hasPlayableMinion)
            {
                bool friendBoardEmpty = board.MinionFriend == null || board.MinionFriend.Count == 0;
                if (friendBoardEmpty)
                {
                    p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(1800));
                    AddLog("分流：空场且有可下随从，后置分流优先先铺场");
                    return;
                }

                // 放宽：不再一刀切禁分流。手牌偏少时允许穿插抽一口。
                if (friendHp >= 12 && board.Hand.Count <= 5 && manaNow >= 2)
                {
                    p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(-90));
                    AddLog("分流：手上有可下随从但手牌偏少，放宽分流");
                }
                else
                {
                    p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(220));
                    AddLog("分流：手上有可下随从，适度后置分流");
                }
                return;
            }

            if (board.Hand.Count <= 4 && friendHp >= 14)
            {
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(-45));
                AddLog("分流：手牌偏少且无其他动作，适度前置");
                return;
            }

            if (board.Hand.Count >= 8)
                p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(160));
        }

        private bool ShouldPrioritizeLifeTapForPressureDig(Board board, int friendHp, int manaNow, bool hasPlayableMinion)
        {
            if (board == null || board.Hand == null)
                return false;
            if (!hasPlayableMinion)
                return false;
            // 用户规则：有可下橱窗看客/套外恶魔时，优先先铺场，不先抽一口。
            if (HasPlayableWindowShopperFamily(board, manaNow))
                return false;
            if (GetMaxPlayableOutsideDeckDemonsThisTurn(board, manaNow) > 0)
                return false;
            if (!CanUseLifeTapNow(board))
                return false;
            if (manaNow < 2)
                return false;
            if (friendHp <= 9)
                return false;
            if (board.Hand.Count > 4)
                return false;
            if (HasPlayableLowCostMinion(board, 2))
                return false;

            int enemyHp = GetHeroHealth(board.HeroEnemy);
            int friendAttack = GetBoardAttack(board.MinionFriend);
            bool enemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            bool onBoardLethal = !enemyHasTaunt && friendAttack >= enemyHp;
            if (onBoardLethal)
                return false;

            int enemyAttack = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            int enemyCount = board.MinionEnemy != null ? board.MinionEnemy.Count(m => m != null) : 0;
            bool balancedPressure = ShouldEnterBalancedDefenseMode(board, friendHp, enemyHp);
            bool boardPressureWindow = friendHp <= 18 && enemyAttack >= 7 && enemyCount >= 3;

            return balancedPressure || boardPressureWindow;
        }

        private bool ShouldForceDesperateLifeTap(
            Board board,
            int friendHp,
            int enemyAttack,
            int manaNow,
            bool hasPlayableTauntMinionNow,
            bool rayChainOrTempWindow)
        {
            if (board == null || board.Hand == null)
                return false;
            if (!CanUseLifeTapNow(board))
                return false;
            if (manaNow < 2)
                return false;
            if (friendHp <= 2)
                return false;
            if (friendHp > 10)
                return false;
            if (enemyAttack < friendHp)
                return false;
            if (hasPlayableTauntMinionNow)
                return false;
            if (rayChainOrTempWindow)
                return false;
            if (board.Hand.Count > 2)
                return false;

            int enemyHp = GetHeroHealth(board.HeroEnemy);
            int friendAttack = GetBoardAttack(board.MinionFriend);
            bool enemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            if (!enemyHasTaunt && friendAttack >= enemyHp)
                return false;

            int attackableFriendAttack = board.MinionFriend != null
                ? board.MinionFriend.Where(m => m != null && m.CanAttack).Sum(m => Math.Max(0, m.CurrentAtk))
                : 0;
            if (attackableFriendAttack > 0 && Math.Max(0, enemyAttack - attackableFriendAttack) < friendHp)
                return false;

            return true;
        }

        private bool ShouldPrioritizeMoargForgefiendNow(Board board)
        {
            if (board == null || board.Hand == null)
                return false;
            if (GetFreeBoardSlots(board) <= 0)
                return false;

            int manaNow = GetAvailableManaIncludingCoin(board);
            var moargCard = board.Hand
                .Where(c => c != null && c.Template != null
                    && c.Template.Id == MoargForgefiend
                    && c.CurrentCost <= manaNow)
                .OrderBy(c => c.CurrentCost)
                .ThenBy(c => c.Id)
                .FirstOrDefault();
            if (moargCard == null)
                return false;

            int friendHp = GetHeroHealth(board.HeroFriend);
            int enemyHp = GetHeroHealth(board.HeroEnemy);
            int friendAttack = GetBoardAttack(board.MinionFriend);
            bool enemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            if (!enemyHasTaunt && friendAttack >= enemyHp)
                return false;

            int enemyAttack = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            bool hasHighAttackEnemy = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.CurrentAtk >= 6);
            bool hasBigTauntEnemy = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt && m.CurrentAtk >= 3);
            bool pressureWindow = ShouldEnterBalancedDefenseMode(board, friendHp, enemyHp)
                || (friendHp <= 20 && enemyAttack >= 8)
                || hasHighAttackEnemy
                || hasBigTauntEnemy;

            return pressureWindow;
        }

        private bool ShouldConcedeWhenNoSolution(
            Board board,
            int friendHp,
            int enemyHp,
            int friendAttackNow,
            int enemyAttackTotal,
            int enemyAttackNow,
            int manaNow)
        {
            if (board == null)
                return false;

            // 仅在危险血线+高压场面触发，避免误投。
            if (friendHp > 10)
                return false;
            if (enemyAttackTotal < friendHp)
                return false;

            bool enemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            if (!enemyHasTaunt && friendAttackNow >= enemyHp)
                return false; // 本回合可斩杀，不投降

            int enemyCount = board.MinionEnemy != null ? board.MinionEnemy.Count(m => m != null) : 0;
            if (enemyCount < 2)
                return false;

            bool friendlyTauntOnBoard = board.MinionFriend != null
                && board.MinionFriend.Any(m => m != null && m.IsTaunt && m.CurrentHealth > 0);
            if (friendlyTauntOnBoard)
                return false;

            if (GetPlayableTauntMinionCardIds(board, manaNow).Count > 0)
                return false;
            if (IsAbductionRayPlayableNow(board))
                return false;

            bool canPlayGlacialShardNow = board.Hand != null && board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == GlacialShard
                && c.CurrentCost <= manaNow)
                && GetFreeBoardSlots(board) > 0;
            if (canPlayGlacialShardNow)
                return false;

            bool canLifeTapLastChance = CanUseLifeTapNow(board)
                && friendHp >= 3
                && board.Hand != null
                && board.Hand.Count <= 2;
            if (canLifeTapLastChance)
                return false;

            int attackableFriendAttack = GetAttackableBoardAttack(board.MinionFriend);
            int enemyAttackAfterOptimisticTrades = Math.Max(0, enemyAttackTotal - attackableFriendAttack);
            if (enemyAttackAfterOptimisticTrades >= friendHp + 1)
                return true;

            bool hasPlayableMinion = HasPlayableMinionWithBoardSpace(board, manaNow);
            bool canUseHeroPower = CanUseLifeTapNow(board);
            if (!hasPlayableMinion && !canUseHeroPower && attackableFriendAttack <= 0 && enemyAttackNow >= friendHp)
                return true;

            return false;
        }

        private bool IsHighValueStorytellerPlayableNow(Board board, int manaNow)
        {
            if (board == null || board.Hand == null)
                return false;
            if (GetFreeBoardSlots(board) <= 0)
                return false;

            bool storytellerPlayable = board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == TortollanStoryteller
                && c.CurrentCost <= manaNow);
            if (!storytellerPlayable)
                return false;

            // 用户规则：若“续连熵能覆盖收益”已明确高于始祖龟，始祖龟不应再视为高收益优先。
            if (ShouldPreferEntropicOverStoryteller(board, manaNow))
                return false;

            // 用户规则：当本回合可明显铺出更多身材时，始祖龟不应判定为高收益优先。
            // 这里用“可额外铺场体数>=2”作为门槛，避免始祖龟在中前期反复抢节奏位。
            int floodBodies = GetMaxAdditionalBodiesWithBudgetExcludingCard(board, manaNow, TortollanStoryteller);
            if (floodBodies >= 2 && !ShouldForceAbductionRayNow(board))
                return false;

            bool enemyHasMinion = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null);
            if (!enemyHasMinion)
                return board.MinionFriend != null && board.MinionFriend.Count >= 1;

            int raceTypes = CountDistinctFriendlyRaceTypes(board);
            return raceTypes >= 2;
        }

        private int GetAttackableFriendlyMinionCount(Board board)
        {
            if (board == null || board.MinionFriend == null)
                return 0;

            return board.MinionFriend.Count(m => m != null && m.CanAttack && m.CurrentAtk > 0);
        }

        private int GetStorytellerBuffCoverageNow(Board board)
        {
            if (board == null)
                return 0;

            return Math.Max(0, CountDistinctFriendlyRaceTypes(board));
        }

        private int GetEntropicBuffCoveragePotential(Board board, int manaNow)
        {
            if (board == null || board.Hand == null || board.MinionFriend == null)
                return 0;

            var entropy = board.Hand
                .Where(c => c != null && c.Template != null
                    && c.Template.Id == EntropicContinuity
                    && c.CurrentCost <= manaNow)
                .OrderBy(c => c.CurrentCost)
                .ThenBy(c => c.Id)
                .FirstOrDefault();
            if (entropy == null)
                return 0;

            // 用户规则：续连熵能覆盖统一按“友方场上随从总数”评估。
            return board.MinionFriend.Count(m => m != null);
        }

        // 用户规则：始祖龟让位于“集体buff/续连熵能”时，按可覆盖收益动态比较，不再固定偏向始祖龟。
        // 典型case：友方随从=5、类型数=4 => 续连优先。
        private bool ShouldPreferEntropicOverStoryteller(Board board, int manaNow)
        {
            if (board == null || board.Hand == null)
                return false;
            if (GetFreeBoardSlots(board) <= 0)
                return false;

            bool storytellerPlayable = board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == TortollanStoryteller
                && c.CurrentCost <= manaNow);
            if (!storytellerPlayable)
                return false;

            bool entropyPlayable = board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == EntropicContinuity
                && c.CurrentCost <= manaNow);
            if (!entropyPlayable)
                return false;

            int storytellerCoverage = GetStorytellerBuffCoverageNow(board);
            int entropicCoverage = board.MinionFriend != null ? board.MinionFriend.Count(m => m != null) : 0;
            int entropicPotentialCoverage = GetEntropicBuffCoveragePotential(board, manaNow);

            if (entropicCoverage > storytellerCoverage)
                return true;
            if (entropicPotentialCoverage >= storytellerCoverage + 2)
                return true;

            return false;
        }

        private bool CanPlayWispThenStorytellerSameTurn(Board board, int manaNow)
        {
            if (board == null || board.Hand == null)
                return false;
            if (GetFreeBoardSlots(board) < 2)
                return false;

            var storyteller = board.Hand
                .Where(c => c != null && c.Template != null && c.Template.Id == TortollanStoryteller)
                .OrderBy(c => c.CurrentCost)
                .FirstOrDefault();
            if (storyteller == null || storyteller.CurrentCost > manaNow)
                return false;

            var wisp = board.Hand
                .Where(c => c != null && c.Template != null && c.Template.Id == Wisp)
                .OrderBy(c => c.CurrentCost)
                .FirstOrDefault();
            if (wisp == null || wisp.CurrentCost > manaNow)
                return false;

            return storyteller.CurrentCost + wisp.CurrentCost <= manaNow;
        }

        private bool HasStorytellerDualOneDropRaceWindow(Board board, int manaNow)
        {
            return FindBestStorytellerDualOneDropRacePair(board, manaNow) != null;
        }

        private Tuple<Card, Card> FindBestStorytellerDualOneDropRacePair(Board board, int manaNow)
        {
            if (board == null || board.Hand == null || board.MinionFriend == null)
                return null;
            if (manaNow < 2 || GetFreeBoardSlots(board) < 2)
                return null;

            bool storytellerOnBoard = board.MinionFriend.Any(m => m != null && m.Template != null && m.Template.Id == TortollanStoryteller);
            if (!storytellerOnBoard)
                return null;

            var oneDrops = board.Hand
                .Where(c => c != null && c.Template != null
                    && c.Type == Card.CType.MINION
                    && c.Template.Cost == 1
                    && c.CurrentCost <= manaNow)
                .ToList();
            if (oneDrops.Count < 2)
                return null;

            var boardRaces = GetTrackedFriendlyRaceSet(board.MinionFriend);
            Tuple<Card, Card> bestPair = null;
            int bestScore = int.MinValue;
            int bestMana = int.MaxValue;

            for (int i = 0; i < oneDrops.Count; i++)
            {
                for (int j = i + 1; j < oneDrops.Count; j++)
                {
                    var first = oneDrops[i];
                    var second = oneDrops[j];
                    int totalCost = first.CurrentCost + second.CurrentCost;
                    if (totalCost > manaNow)
                        continue;

                    var firstRaces = GetTrackedRaces(first.Template);
                    var secondRaces = GetTrackedRaces(second.Template);
                    if (firstRaces.Count == 0 || secondRaces.Count == 0)
                        continue;
                    if (firstRaces.SetEquals(secondRaces))
                        continue;

                    var after = new HashSet<Card.CRace>(boardRaces);
                    after.UnionWith(firstRaces);
                    after.UnionWith(secondRaces);
                    int raceGain = after.Count - boardRaces.Count;

                    // 优先“新增种族数”更高，其次选总费用更低的组合。
                    int score = raceGain * 100 - totalCost;
                    if (score > bestScore || (score == bestScore && totalCost < bestMana))
                    {
                        bestScore = score;
                        bestMana = totalCost;
                        bestPair = Tuple.Create(first, second);
                    }
                }
            }

            return bestPair;
        }

        private static HashSet<Card.CRace> GetTrackedFriendlyRaceSet(IEnumerable<Card> minions)
        {
            var races = new HashSet<Card.CRace>();
            if (minions == null)
                return races;

            foreach (var m in minions.Where(m => m != null && m.Template != null))
                races.UnionWith(GetTrackedRaces(m.Template));
            return races;
        }

        private static HashSet<Card.CRace> GetTrackedRaces(CardTemplate template)
        {
            var races = new HashSet<Card.CRace>();
            if (template == null)
                return races;

            foreach (var race in StorytellerTrackedRaces)
            {
                if (template.IsRace(race))
                    races.Add(race);
            }
            return races;
        }

        private void HandleTempoMinionDump(Board board, ProfileParameters p)
        {
            if (board == null || board.Hand == null)
                return;
            if (GetFreeBoardSlots(board) <= 0)
                return;

            int realManaNow = Math.Max(0, board.ManaAvailable);
            int manaNow = GetAvailableManaIncludingCoin(board);
            int freeBoardSlots = GetFreeBoardSlots(board);
            int friendHp = GetHeroHealth(board.HeroFriend);
            int enemyHp = GetHeroHealth(board.HeroEnemy);
            int friendAttack = GetBoardAttack(board.MinionFriend);
            int enemyAttack = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            int friendMinionCount = board.MinionFriend != null ? board.MinionFriend.Count(m => m != null) : 0;
            int enemyMinionCount = board.MinionEnemy != null ? board.MinionEnemy.Count(m => m != null) : 0;
            bool lowHpHighPressure = friendHp <= 12 && enemyAttack >= 8;
            bool dangerousHpTauntTempoWindow = IsDangerousHpTauntTempoWindow(friendHp, enemyAttack);
            bool rushClearWindow = ShouldPrioritizeRushMinionClear(board, friendHp, enemyHp, friendAttack, enemyAttack)
                && HasPlayableRushMinionForClear(board, manaNow);
            bool boardBehind = enemyMinionCount > friendMinionCount || enemyAttack >= friendAttack + 3;
            bool hpBehind = friendHp + 6 <= enemyHp;
            bool isDisadvantaged = lowHpHighPressure || boardBehind || hpBehind;

            // 既有硬优先链：临时/满费挟持射线 > 咒怨之墓 > 先抽后buff > 速写美术家。
            // 用户规则：即使是0费收尾，也必须先遵守这些规则，不能提前插队。
            bool forceRayNow = ShouldForceAbductionRayNow(board);
            if (forceRayNow && !rushClearWindow)
            {
                if (realManaNow <= 0)
                    AddLog("节奏：0费收尾窗口命中挟持射线锁定，后置不插队");
                return;
            }
            if (forceRayNow && rushClearWindow)
                AddLog("节奏：命中突袭解场窗口，允许打断射线锁定");

            // 用户规则：命中阿克蒙德直拍窗口时，节奏模块不得插队（尤其0费随从）。
            // 否则会先占场位，降低阿克蒙德复活收益。
            bool archimondeCommitWindow = ShouldCommitArchimondeThisTurn(board, manaNow);
            if (archimondeCommitWindow)
            {
                AddLog("节奏：命中阿克蒙德直拍窗口，暂停节奏随从前置避免占位");
                return;
            }

            if (ShouldForceTombFirstThisTurn(board))
            {
                if (realManaNow <= 0)
                    AddLog("节奏：0费收尾窗口命中咒怨之墓先手规则，后置不插队");
                else
                    AddLog("节奏：命中咒怨之墓先手规则，后置其余节奏随从");
                return;
            }
            if (ShouldTapBeforeEntropicContinuity(board, friendHp))
            {
                if (realManaNow <= 0)
                    AddLog("节奏：0费收尾窗口命中先抽后buff规则，后置不插队");
                else
                    AddLog("节奏：命中先抽后buff规则，后置随从优先先分流");
                return;
            }
            if (board.HasCardInHand(SketchArtist) && manaNow >= 4
                && !HasPlayableMinionWithBoardSpaceExcluding(board, manaNow, SketchArtist))
            {
                if (realManaNow <= 0)
                    AddLog("节奏：0费收尾窗口命中速写优先窗口，后置不插队");
                return;
            }

            // 用户新增规则：只要存在可下0费随从，优先先手落地。
            // 但在危险血线且场位仅剩1格、且存在可下嘲讽时，暂缓0费非嘲讽，避免卡住保命位。
            var zeroCostMinion = GetRightmostPlayableZeroCostNonRayMinion(board, manaNow);
            if (zeroCostMinion != null)
            {
                var playableTauntMinionIdsForZeroFirst = GetPlayableTauntMinionCardIds(board, manaNow);
                bool reserveSlotForTaunt = dangerousHpTauntTempoWindow
                    && freeBoardSlots <= 1
                    && playableTauntMinionIdsForZeroFirst.Count > 0
                    && !IsTauntMinionForSurvival(zeroCostMinion);

                if (!reserveSlotForTaunt)
                {
                    SetSingleCardComboByEntityId(board, p, zeroCostMinion.Id,
                        allowCoinBridge: true, forceOverride: true,
                        logWhenSet: realManaNow <= 0
                            ? "节奏：0费收尾窗口，优先落地"
                            : "节奏：检测到可下0费随从，强制先手");
                    p.CastMinionsModifiers.AddOrUpdate(zeroCostMinion.Id, new Modifier(-5400));
                    p.PlayOrderModifiers.AddOrUpdate(zeroCostMinion.Id, new Modifier(9999));
                    AddLog(realManaNow <= 0
                        ? "节奏：费用已用尽且通过既有规则校验，优先落地0费随从避免空过"
                        : "节奏：存在可下0费随从，优先先手落地后再考虑其他动作");
                    return;
                }

                AddLog("节奏：危险血线且场位紧张，暂缓0费非嘲讽，优先保留场位给嘲讽");
            }

            if (realManaNow <= 0)
                return;

            bool forcedStorytellerDualOneDrop = false;
            var storytellerDualOneDropPair = FindBestStorytellerDualOneDropRacePair(board, manaNow);
            if (storytellerDualOneDropPair != null)
            {
                forcedStorytellerDualOneDrop = SetTwoCardCombo(board, p,
                    storytellerDualOneDropPair.Item1.Template.Id,
                    storytellerDualOneDropPair.Item2.Template.Id,
                    allowCoinBridge: true,
                    forceOverride: true,
                    logWhenSet: "始祖龟：命中两张1费异种族窗口，ComboSet最优先铺场");
                if (forcedStorytellerDualOneDrop)
                {
                    p.CastMinionsModifiers.AddOrUpdate(storytellerDualOneDropPair.Item1.Template.Id, new Modifier(-4200));
                    p.CastMinionsModifiers.AddOrUpdate(storytellerDualOneDropPair.Item2.Template.Id, new Modifier(-4200));
                    p.PlayOrderModifiers.AddOrUpdate(storytellerDualOneDropPair.Item1.Template.Id, new Modifier(9800));
                    p.PlayOrderModifiers.AddOrUpdate(storytellerDualOneDropPair.Item2.Template.Id, new Modifier(9700));
                    AddLog("始祖龟：优先先下两张1费不同种族随从，再进行后续动作");
                }
            }

            if (ShouldHoldForStorytellerBuff(board) && !forcedStorytellerDualOneDrop)
            {
                // 用户反馈：即使命中“始祖龟空场吃buff”窗口，若本回合仍剩1费且可下1费随从，
                // 应先打满费用，避免白白浪费1费后直接结束回合。
                var playableOneManaMinion = board.Hand
                    .Where(c => c != null
                        && c.Template != null
                        && c.Type == Card.CType.MINION
                        && c.CurrentCost <= realManaNow
                        && c.CurrentCost == 1)
                    .OrderByDescending(c => c.Id)
                    .FirstOrDefault();
                if (realManaNow == 1 && freeBoardSlots > 0 && playableOneManaMinion != null)
                {
                    SetSingleCardComboByEntityId(board, p, playableOneManaMinion.Id,
                        allowCoinBridge: true, forceOverride: true,
                        logWhenSet: "始祖龟窗口：剩余1费优先打满");
                    p.CastMinionsModifiers.AddOrUpdate(playableOneManaMinion.Id, new Modifier(-3600));
                    p.PlayOrderModifiers.AddOrUpdate(playableOneManaMinion.Id, new Modifier(9999));
                    AddLog("始祖龟窗口：剩余1费且可下1费随从，先打满费用再结束回合");
                    return;
                }

                AddLog("始祖龟窗口：敌方空场，暂停无收益补下，优先结束回合吃buff");
                return;
            }

            var playableRealManaMinions = board.Hand
                .Where(c => c != null && c.Template != null
                            && c.Type == Card.CType.MINION
                            && c.CurrentCost <= realManaNow)
                .ToList();
            if (playableRealManaMinions.Count == 0)
                return;

            // 用户规则：血量危险时，优先使用手上的嘲讽随从稳场。
            // 覆盖普通节奏优先级，避免被高节奏非嘲讽（如影焰猎豹/低费随从）抢走费用。
            var playableTauntMinionIds = GetPlayableTauntMinionCardIds(board, manaNow);
            if (dangerousHpTauntTempoWindow && playableTauntMinionIds.Count > 0)
            {
                foreach (var tauntId in playableTauntMinionIds)
                {
                    p.CastMinionsModifiers.AddOrUpdate(tauntId, new Modifier(-5600));
                    p.PlayOrderModifiers.AddOrUpdate(tauntId, new Modifier(9999));
                }

                foreach (var card in playableRealManaMinions)
                {
                    if (card == null || card.Template == null)
                        continue;
                    if (playableTauntMinionIds.Contains(card.Template.Id))
                        continue;

                    p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(3600));
                    p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(-9900));
                }

                AddLog("节奏：危险血线且有可下嘲讽，优先嘲讽稳场");
                return;
            }

            // 用户反馈：攻守平衡且低费动作不足时，先分流找解，不让中费随从抢先占费。
            // 例外：若影焰猎豹可下，仅放宽分流后置，不做强让位。
            bool stalkerPlayableRealMana = playableRealManaMinions.Any(c => c != null && c.Template != null && c.Template.Id == ShadowflameStalker);
            bool pressureDigTapWindow = ShouldPrioritizeLifeTapForPressureDig(board, friendHp, manaNow, hasPlayableMinion: true);
            if (pressureDigTapWindow && !stalkerPlayableRealMana)
            {
                foreach (var card in playableRealManaMinions)
                {
                    if (card.Template.Id == Archimonde || card.Template.Id == Kiljaeden || card.Template.Id == TheCoin)
                        continue;

                    p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(3200));
                    p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(-9300));
                }
                AddLog("节奏：攻守平衡且低费动作不足，后置随从优先先分流");
                return;
            }
            if (pressureDigTapWindow && stalkerPlayableRealMana)
                AddLog("节奏：攻守平衡窗口但影焰猎豹可用，放宽分流后置");

            // 用户反馈：高压局面下，不能在中后期“剩较多费用直接空过”。
            // 兜底策略：当仍有较高可用费用且存在可下节奏随从时，强制前置一张可落地随从，
            // 防止被多条“后置规则”叠加后直接结束回合。
            var antiFloatTempoCandidates = playableRealManaMinions
                .Where(c => c != null
                    && c.Template != null
                    && c.Template.Id != FrenziedWrathguard
                    && !string.Equals(c.Template.Id.ToString(), KingLlaneCardId, StringComparison.Ordinal)
                    && c.Template.Id != SketchArtist
                    && c.Template.Id != TortollanStoryteller
                    && c.Template.Id != Archimonde
                    && c.Template.Id != Kiljaeden
                    && c.Template.Id != ZilliaxTickingPower
                    && c.Template.Id != ZilliaxDeckBuilder)
                .OrderByDescending(c => c.CurrentCost)
                .ThenBy(c => c.Id)
                .ToList();
            bool startedNonTempRayThisTurn = HasStartedOrLastPlayedAbductionRayThisTurn(board) && !HasTemporaryPlayableAbductionRay(board);
            bool severeManaFloatRiskWindow = realManaNow >= 4
                && antiFloatTempoCandidates.Count > 0
                && !ShouldHoldForStorytellerBuff(board)
                && (isDisadvantaged
                    || enemyMinionCount >= 2
                    || enemyAttack >= friendAttack + 4
                    || startedNonTempRayThisTurn);
            if (severeManaFloatRiskWindow)
            {
                var topTempo = antiFloatTempoCandidates[0];
                SetSingleCardComboByEntityId(board, p, topTempo.Id, allowCoinBridge: true, forceOverride: false,
                    logWhenSet: "节奏：高费留费兜底，优先打出可下随从");

                foreach (var tempo in antiFloatTempoCandidates)
                {
                    int castBoost = tempo.CurrentCost >= 3 ? -4600 : -3400;
                    int orderBoost = tempo.CurrentCost >= 3 ? 9800 : 9000;
                    p.CastMinionsModifiers.AddOrUpdate(tempo.Template.Id, new Modifier(castBoost));
                    p.PlayOrderModifiers.AddOrUpdate(tempo.Template.Id, new Modifier(orderBoost));
                }

                AddLog("节奏：命中高压留费兜底（剩余费用=" + realManaNow + "），前置可下节奏随从避免空过");
                return;
            }

            int minionMod = board.MaxMana <= 4 ? -1100 : -420;
            bool infernalDelayed = false;
            bool infernalDelayedForTap = false;
            int infernalDelayHpSnapshot = 0;
            bool infernalLowHpPrioritized = false;
            int infernalPriorityHpSnapshot = 0;
            bool infernalEmergencyForced = false;
            bool doomguardDelayed = false;
            bool doomguardStrongDelayed = false;
            bool doomguardHardDelayedByOtherCards = false;
            bool alenashiDelayedByCoreDemonPlan = false;
            bool alenashiDelayedByOtherHandCards = false;
            bool wrathguardPrioritized = false;
            bool wrathguardHeldForSetup = false;
            bool wrathguardHardDelayed = false;
            bool eredarBruteZeroCostPrioritized = false;
            bool moargForgefiendPrioritized = false;
            bool shadowflameStalkerPrioritized = false;
            bool shadowflameStalkerDelayedForRayChain = false;
            bool shadowflameStalkerDelayedForArchimonde = false;
            bool shadowflameStalkerDelayedForOutsideDeckDemons = false;
            bool kingLlaneDelayed = false;
            bool monthlyDelayedForNoFriendlyMinion = false;
            bool rushMinionPrioritizedForClear = false;
            bool arsonEyedDemonLifestealPrioritized = false;
            bool arsonEyedDemonDelayedForNoHealTarget = false;
            int arsonEyedDemonMissingHpSnapshot = 0;
            bool delayedOneDropsForForebodingCore = false;
            bool wispDelayedForStorytellerPlan = false;
            bool wispDelayedAgainstEnemyMinions = false;
            bool midCurvePlayOrderRaised = false;
            bool masseridonOrderKept = false;
            bool hasArchimondeOrKiljaedenInHand = board.HasCardInHand(Archimonde) || board.HasCardInHand(Kiljaeden);
            bool archimondePlayableNowForTempo = IsArchimondePlayableNow(board, manaNow);
            int playableOutsideDeckDemonsNowForTempo = GetMaxPlayableOutsideDeckDemonsThisTurn(board, manaNow);
            bool hasOtherCardBesidesAlenashiInHand = board.Hand.Any(c => c != null && c.Template != null && c.Template.Id != Alenashi);
            int playableNonDoomguardCount = playableRealManaMinions.Count(c => c != null && c.Template != null && c.Template.Id != Doomguard);
            bool tapBeforeInfernalWindow = ShouldTapBeforeInfernal(board, friendHp, manaNow);
            bool forceInfernalEmergencyWindow = ShouldForceInfernalEmergency(board, friendHp, manaNow);
            bool forebodingCorePlayableNow = freeBoardSlots > 0
                && board.Hand.Any(c => c != null && c.Template != null
                    && c.Template.Id == ForebodingFlame
                    && c.CurrentCost <= manaNow);
            bool forebodingHoldForEarlyStorytellerOneDropWindow = forebodingCorePlayableNow
                && ShouldDelayForebodingForEarlyStorytellerOneDrops(board, manaNow, friendHp, enemyAttack);
            bool skippedOneDropsDelayForForebodingStorytellerWindow = false;
            foreach (var card in playableRealManaMinions)
            {
                if (card.Template.Id == ForebodingFlame)
                    continue;
                if (card.Template.Id == Archimonde)
                    continue;
                if (card.Template.Id == Kiljaeden)
                    continue;
                if (card.Template.Id == ZilliaxTickingPower || card.Template.Id == ZilliaxDeckBuilder)
                    continue;
                if (card.Template.Id == Alenashi && hasArchimondeOrKiljaedenInHand)
                {
                    // 用户规则：手里有阿克蒙德或基尔加丹时，不走阿莱纳希。
                    p.CastMinionsModifiers.AddOrUpdate(Alenashi, new Modifier(9500));
                    p.PlayOrderModifiers.AddOrUpdate(Alenashi, new Modifier(-9999));
                    alenashiDelayedByCoreDemonPlan = true;
                    continue;
                }
                if (card.Template.Id == Alenashi && hasOtherCardBesidesAlenashiInHand)
                {
                    // 用户规则：手里有其他牌时，降低阿莱纳希使用优先级。
                    p.CastMinionsModifiers.AddOrUpdate(Alenashi, new Modifier(2600));
                    p.PlayOrderModifiers.AddOrUpdate(Alenashi, new Modifier(-9000));
                    alenashiDelayedByOtherHandCards = true;
                    continue;
                }
                if (card.Template.Id == SketchArtist)
                    continue;
                if (card.Template.Id == TortollanStoryteller)
                    continue;
                if (card.Template.Id == PartyFiend && board.MinionFriend.Count > 4)
                    continue;

                if (string.Equals(card.Template.Id.ToString(), KingLlaneCardId, StringComparison.Ordinal))
                {
                    p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(2400));
                    p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(-7600));
                    kingLlaneDelayed = true;
                    continue;
                }

                if (card.Template.Id == MonthlyModelEmployee
                    && (board.MinionFriend == null || !board.MinionFriend.Any(m => m != null)))
                {
                    p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(9999));
                    p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(-9999));
                    monthlyDelayedForNoFriendlyMinion = true;
                    continue;
                }

                if (rushClearWindow && IsRushMinionForClear(card))
                {
                    p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(-5600));
                    p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(9999));
                    rushMinionPrioritizedForClear = true;
                    continue;
                }

                // 用户规则：低血时优先打“纵火眼魔（突袭+吸血）”立即解场回血保命。
                if (string.Equals(card.Template.Id.ToString(), ArsonEyedDemonCardId, StringComparison.Ordinal))
                {
                    int enemyMinionsNow = board.MinionEnemy == null ? 0 : board.MinionEnemy.Count(m => m != null);
                    int missingHp = Math.Max(0, 30 - friendHp);
                    bool canHealThisTurn = enemyMinionsNow > 0;
                    bool survivalWindow = friendHp <= 16
                        || lowHpHighPressure
                        || missingHp >= 10
                        || (enemyAttack >= friendHp - 1);

                    if (canHealThisTurn && survivalWindow)
                    {
                        int castBoost = -2200 - Math.Min(3400, missingHp * 180 + enemyMinionsNow * 160);
                        if (castBoost < -6200) castBoost = -6200;
                        int orderBoost = 8400 + Math.Min(1200, missingHp * 60) + (lowHpHighPressure ? 400 : 0);
                        if (orderBoost > 9999) orderBoost = 9999;

                        p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(castBoost));
                        p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(orderBoost));
                        arsonEyedDemonLifestealPrioritized = true;
                        arsonEyedDemonMissingHpSnapshot = Math.Max(arsonEyedDemonMissingHpSnapshot, missingHp);
                        continue;
                    }

                    if (!canHealThisTurn && friendHp <= 12)
                    {
                        p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(1200));
                        p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(-3600));
                        arsonEyedDemonDelayedForNoHealTarget = true;
                        continue;
                    }
                }

                if (card.Template.Id == Wisp)
                {
                    bool enemyHasMinionForWisp = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null);
                    bool lateGamePreferWindowShopperForWisp = board.MaxMana >= 6 && HasPlayableWindowShopperFamily(board, manaNow);
                    bool storytellerOnBoardNow = board.MinionFriend != null
                        && board.MinionFriend.Any(m => m != null && m.Template != null && m.Template.Id == TortollanStoryteller);
                    bool storytellerInHandNow = board.Hand.Any(c => c != null && c.Template != null && c.Template.Id == TortollanStoryteller);
                    bool storytellerPlayableNowForWisp = freeBoardSlots > 0 && board.Hand.Any(c => c != null && c.Template != null
                        && c.Template.Id == TortollanStoryteller
                        && c.CurrentCost <= manaNow);
                    bool canWispThenEntropyNow = board.Hand.Any(c => c != null && c.Template != null
                        && c.Template.Id == EntropicContinuity
                        && c.CurrentCost <= manaNow)
                        && board.MinionFriend.Count >= 1
                        && board.MinionFriend.Count <= 6;
                    bool hasOtherPlayableActionExWisp = CountPlayableActionsExcludingCard(board, manaNow, Wisp) > 0;
                    bool canWispStorySameTurn = CanPlayWispThenStorytellerSameTurn(board, manaNow);

                    if (enemyHasMinionForWisp
                        && friendHp >= 12
                        && storytellerInHandNow
                        && !storytellerOnBoardNow
                        && !storytellerPlayableNowForWisp
                        && !canWispThenEntropyNow)
                    {
                        p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(3200));
                        p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(-9800));
                        wispDelayedForStorytellerPlan = true;
                        continue;
                    }

                    if (enemyHasMinionForWisp
                        && friendHp >= 12
                        && hasOtherPlayableActionExWisp
                        && !canWispThenEntropyNow
                        && !canWispStorySameTurn)
                    {
                        p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(1700));
                        p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(-9200));
                        wispDelayedAgainstEnemyMinions = true;
                        continue;
                    }

                    if (lateGamePreferWindowShopperForWisp)
                    {
                        p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(2600));
                        p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(-9950));
                        wispDelayedAgainstEnemyMinions = true;
                        continue;
                    }
                }

                // 用户规则兜底：恶兆邪火（未触发）可下时，1费随从统一后置到邪火之后。
                // 危险血线且手里有可下嘲讽时允许例外，优先保命。
                bool oneDropByTemplate = card.Template.Cost == 1;
                bool dangerTauntException = lowHpHighPressure && IsTauntMinionForSurvival(card);
                if (forebodingCorePlayableNow
                    && oneDropByTemplate
                    && !dangerTauntException
                    && !forebodingHoldForEarlyStorytellerOneDropWindow)
                {
                    p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(5200));
                    p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(-9900));
                    delayedOneDropsForForebodingCore = true;
                    continue;
                }
                if (forebodingCorePlayableNow
                    && oneDropByTemplate
                    && !dangerTauntException
                    && forebodingHoldForEarlyStorytellerOneDropWindow)
                {
                    skippedOneDropsDelayForForebodingStorytellerWindow = true;
                }

                // 用户规则：艾瑞达蛮兵在0费时应优先落地，先把“免费节奏”转化掉。
                if (card.Template.Id == EredarBrute && card.CurrentCost == 0)
                {
                    p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(-4200));
                    p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(9999));
                    eredarBruteZeroCostPrioritized = true;
                    continue;
                }

                // 用户新规则：提高莫尔葛熔魔使用优先级，劣势时进一步前置用于稳场。
                if (card.Template.Id == MoargForgefiend)
                {
                    int castBoost = isDisadvantaged ? -3600 : -1800;
                    int orderBoost = isDisadvantaged ? 9800 : 7600;
                    p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(castBoost));
                    p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(orderBoost));
                    moargForgefiendPrioritized = true;
                    continue;
                }

                // 用户新规则：影焰猎豹保持原优先级，单独提高使用次序（先后顺序前移）。
                if (card.Template.Id == ShadowflameStalker)
                {
                    if (archimondePlayableNowForTempo)
                    {
                        p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(6200));
                        p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(-9999));
                        shadowflameStalkerDelayedForArchimonde = true;
                    }
                    else if (playableOutsideDeckDemonsNowForTempo > 0)
                    {
                        p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(6200));
                        p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(-9999));
                        shadowflameStalkerDelayedForOutsideDeckDemons = true;
                    }
                    else if (ShouldDelayShadowflameForAbductionRay(board, manaNow))
                    {
                        p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(4800));
                        p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(-9950));
                        shadowflameStalkerDelayedForRayChain = true;
                    }
                    else if (TryApplyShadowflameStalkerEdgeDamagePriority(
                        board,
                        p,
                        manaNow,
                        allowComboSet: false,
                        emitLog: false))
                    {
                        shadowflameStalkerPrioritized = true;
                    }
                    else
                    {
                        p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(-150));
                        p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(5200));
                        shadowflameStalkerPrioritized = true;
                    }
                    continue;
                }

                // TOY_647 专属逻辑已在 HandleUnreleasedMasseridon 计算，
                // 这里避免被通用节奏分支覆盖。
                if (card.Template.Id == UnreleasedMasseridon)
                {
                    masseridonOrderKept = true;
                    continue;
                }

                // 用户规则：3血且地狱火可用时，地狱火必须保命优先，不再让位分流。
                if (card.Template.Id == Infernal && forceInfernalEmergencyWindow)
                {
                    SetSingleCardCombo(board, p, Infernal, allowCoinBridge: true, forceOverride: false,
                        logWhenSet: "地狱火！：3血保命窗口，ComboSet强制优先");
                    p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(-9800));
                    p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(9999));
                    infernalEmergencyForced = true;
                    infernalLowHpPrioritized = true;
                    infernalPriorityHpSnapshot = infernalPriorityHpSnapshot <= 0
                        ? friendHp
                        : Math.Min(infernalPriorityHpSnapshot, friendHp);
                    continue;
                }

                // 用户规则：可“分流+地狱火”时，地狱火后置到分流之后。
                if (card.Template.Id == Infernal && tapBeforeInfernalWindow)
                {
                    p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(5200));
                    p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(-9950));
                    infernalDelayedForTap = true;
                    continue;
                }

                // 用户优化：地狱火血量越低越应优先，低血时前置争夺节奏。
                if (card.Template.Id == Infernal && friendHp <= 12)
                {
                    int hpMissingTo13 = Math.Max(0, 13 - friendHp);
                    int castBoost = -1400 - hpMissingTo13 * 320;
                    if (friendHp <= 8) castBoost -= 900;
                    if (friendHp <= 5) castBoost -= 1200;
                    if (castBoost < -5600) castBoost = -5600;
                    int orderBoost = 7200 + hpMissingTo13 * 260 + (friendHp <= 8 ? 900 : 0);
                    if (orderBoost > 9999) orderBoost = 9999;

                    p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(castBoost));
                    p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(orderBoost));
                    infernalLowHpPrioritized = true;
                    infernalPriorityHpSnapshot = infernalPriorityHpSnapshot <= 0
                        ? friendHp
                        : Math.Min(infernalPriorityHpSnapshot, friendHp);
                    continue;
                }

                // 用户规则：己方血量 > 15 时，地狱火硬禁用，避免被强制压血到15。
                if (card.Template.Id == Infernal && friendHp > 15)
                {
                    p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(9999));
                    p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(-9999));
                    infernalDelayed = true;
                    infernalDelayHpSnapshot = Math.Max(infernalDelayHpSnapshot, friendHp);
                    continue;
                }

                // 用户规则：降低末日守卫使用次序，避免在有替代动作时过早弃牌。
                if (card.Template.Id == Doomguard)
                {
                    bool hasOtherNonDoomguardCardInHand = board.Hand.Any(c => c != null && c.Template != null && c.Template.Id != Doomguard);
                    bool highDiscardRisk = board.Hand.Count >= 5;
                    bool hasOtherTempoMinion = playableNonDoomguardCount >= 1;
                    bool underHeavyPressure = friendHp <= 10 && enemyAttack >= 10;

                    // 新规则：手里有其他牌时，优先打其他牌，末日守卫强后置。
                    if (hasOtherNonDoomguardCardInHand)
                    {
                        p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(9200));
                        p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(-9950));
                        doomguardDelayed = true;
                        doomguardStrongDelayed = true;
                        doomguardHardDelayedByOtherCards = true;
                        continue;
                    }

                    // 非高压回合默认后置；手牌多或有替代动作时强后置。
                    if (!underHeavyPressure || highDiscardRisk || hasOtherTempoMinion)
                    {
                        int castDelay = (highDiscardRisk || hasOtherTempoMinion) ? 2200 : 850;
                        int orderDelay = (highDiscardRisk || hasOtherTempoMinion) ? -9600 : -3500;
                        p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(castDelay));
                        p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(orderDelay));
                        doomguardDelayed = true;
                        if (castDelay >= 2000)
                            doomguardStrongDelayed = true;
                        continue;
                    }
                }

                // 用户规则：雷兹迪尔仅标记态（POWERED_UP）可用；非标记态硬禁用。
                if (card.Template.Id == Rezdir)
                {
                    bool isPoweredUp = GetTag(card, Card.GAME_TAG.POWERED_UP) == 1;
                    if (!isPoweredUp)
                    {
                        p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(9999));
                        p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(-9999));
                        AddLog("雷兹迪尔：非标记态，硬规则禁用");
                        continue;
                    }

                    p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(-3600));
                    p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(9999));
                    AddLog("雷兹迪尔：标记态已激活，强制先手压制对手手牌");

                    continue;
                }

                if (TryApplyRafaamFamilyTempoRules(board, p, card, manaNow, isDisadvantaged))
                    continue;

                // 用户规则：躁动的愤怒卫士优先用于处理“敌方<=2血随从”；
                // 若当前无窗口但可先通过交换压到<=2血，则先后置卫士等待发现窗口。
                bool isWrathguardCard = card.Template.Id == FrenziedWrathguard;
                if (isWrathguardCard)
                {
                    bool enemyHasTwoOrLessHealthMinion = board.MinionEnemy != null
                        && board.MinionEnemy.Any(m => m != null && m.CurrentHealth <= 2);
                    bool canSetupTwoOrLessHealthWindowByAttack = false;
                    if (!enemyHasTwoOrLessHealthMinion
                        && board.MinionEnemy != null
                        && board.MinionFriend != null)
                    {
                        var enemyCandidates = board.MinionEnemy
                            .Where(m => m != null)
                            .ToList();
                        if (enemyCandidates.Count > 0)
                        {
                            bool enemyHasTaunt = enemyCandidates.Any(m => m.IsTaunt);
                            if (enemyHasTaunt)
                                enemyCandidates = enemyCandidates.Where(m => m.IsTaunt).ToList();

                            var attackableFriends = board.MinionFriend
                                .Where(m => m != null && m.CanAttack && m.CurrentAtk > 0)
                                .ToList();
                            if (attackableFriends.Count > 0)
                            {
                                canSetupTwoOrLessHealthWindowByAttack = enemyCandidates.Any(enemy =>
                                {
                                    if (enemy.CurrentHealth <= 2)
                                        return false;
                                    int minChipDamage = Math.Max(1, enemy.CurrentHealth - 2);
                                    return attackableFriends.Any(friend =>
                                        friend.CurrentAtk >= minChipDamage && friend.CurrentAtk < enemy.CurrentHealth);
                                });
                            }
                        }
                    }

                    if (enemyHasTwoOrLessHealthMinion)
                    {
                        p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(-3600));
                        p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(9400));
                        wrathguardPrioritized = true;
                    }
                    else if (canSetupTwoOrLessHealthWindowByAttack)
                    {
                        p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(8600));
                        p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(-9950));
                        wrathguardHeldForSetup = true;
                    }
                    else
                    {
                        p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(5200));
                        p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(-9950));
                        wrathguardHardDelayed = true;
                    }
                    continue;
                }

                int castMod = minionMod;
                if (card.Template.Id == Platysaur)
                    castMod = Math.Min(castMod, -2600);

                // 用户规则：在“通用节奏分支”里提高中费随从的使用次序（PlayOrder），
                // 不改其是否被选中的优先级（CastModifier）。
                int generalPlayOrder = 1500;
                if (card.CurrentCost >= 2 && card.CurrentCost <= 4)
                {
                    generalPlayOrder = 3200;
                    midCurvePlayOrderRaised = true;
                }

                p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(castMod));
                p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(generalPlayOrder));
            }

            if (infernalEmergencyForced)
                AddLog("地狱火！：3血保命窗口，强制前置且不再让位分流");
            else if (infernalDelayedForTap)
                AddLog("地狱火！：命中先抽后地狱火规则，后置到分流之后");
            else if (infernalDelayed)
                AddLog("地狱火！：己方血量>15，硬规则禁用（当前血量=" + infernalDelayHpSnapshot + "）");
            else if (infernalLowHpPrioritized)
                AddLog("地狱火！：血量越低越优先，低血窗口前置（当前血量=" + infernalPriorityHpSnapshot + "）");
            if (doomguardHardDelayedByOtherCards)
                AddLog("末日守卫：手里有其他牌，优先使用其他牌并强后置末日守卫");
            else if (doomguardStrongDelayed)
                AddLog("末日守卫：手牌较多或有替代动作，强后置避免过早弃牌");
            else if (doomguardDelayed)
                AddLog("末日守卫：默认后置，优先保留手牌节奏");
            if (alenashiDelayedByCoreDemonPlan)
                AddLog("阿莱纳希：手有阿克蒙德/基尔加丹，后置不打");
            else if (alenashiDelayedByOtherHandCards)
                AddLog("阿莱纳希：手里有其他牌，降低优先级并后置");
            if (wrathguardPrioritized)
                AddLog("躁动的愤怒卫士：敌方存在<=2血随从，前置优先使用");
            else if (wrathguardHeldForSetup)
                AddLog("躁动的愤怒卫士：可先将目标压到<=2血，后置等待发现窗口");
            else if (wrathguardHardDelayed)
                AddLog("躁动的愤怒卫士：无<=2血目标，强后置等待击杀窗口");
            if (eredarBruteZeroCostPrioritized)
                AddLog("艾瑞达蛮兵：0费可用，前置优先落地");
            if (moargForgefiendPrioritized)
                AddLog("莫尔葛熔魔：提高优先级，劣势回合进一步前置稳场");
            if (rushMinionPrioritizedForClear)
                AddLog("突袭随从：命中高压解场窗口，前置用于解场");
            if (arsonEyedDemonLifestealPrioritized)
                AddLog("纵火眼魔：低血保命窗口，前置突袭解场并吸血回血（缺失血量=" + arsonEyedDemonMissingHpSnapshot + "）");
            else if (arsonEyedDemonDelayedForNoHealTarget)
                AddLog("纵火眼魔：敌方空场无法立即吸血，低血回合后置避免空拍");
            if (shadowflameStalkerDelayedForArchimonde)
                AddLog("影焰猎豹：阿克蒙德可下，强后置到阿克蒙德之后");
            else if (shadowflameStalkerDelayedForOutsideDeckDemons)
                AddLog("影焰猎豹：可下套外恶魔，强后置让位套外恶魔");
            else if (shadowflameStalkerDelayedForRayChain)
                AddLog("影焰猎豹：命中挟持射线链路窗口，后置到射线之后");
            else if (shadowflameStalkerPrioritized)
                AddLog("影焰猎豹：保持优先级不变，使用次序前移（PlayOrder=5200）");
            if (kingLlaneDelayed)
                AddLog("莱恩国王：降低使用优先级，后置不抢节奏");
            if (monthlyDelayedForNoFriendlyMinion)
                AddLog("月度魔范员工：友方无随从，后置暂缓使用");
            if (wispDelayedForStorytellerPlan)
                AddLog("小精灵：手有始祖龟，后置保留联动避免白给");
            else if (wispDelayedAgainstEnemyMinions)
                AddLog("小精灵：敌方有场且有其他动作，轻度后置避免提前送掉");
            if (delayedOneDropsForForebodingCore)
                AddLog("恶兆邪火：小核心优先于1费随从（节奏兜底）");
            else if (skippedOneDropsDelayForForebodingStorytellerWindow)
                AddLog("恶兆邪火：前期始祖龟+异种族1费窗口，取消1费后置");
            if (midCurvePlayOrderRaised)
                AddLog("节奏：通用逻辑提高中费随从使用次序（仅PlayOrder）");
            if (masseridonOrderKept)
                AddLog("节奏：玛瑟里顿（未发售版）使用专属逻辑，跳过通用节奏覆盖");
            AddLog("节奏：存在可用真实费用随从，优先落地避免空过");
        }

        private bool ShouldDelayForebodingForEarlyStorytellerOneDrops(Board board, int manaNow, int friendHp, int enemyAttack)
        {
            if (board == null || board.Hand == null)
                return false;
            if (board.MaxMana > 3)
                return false;
            if (friendHp <= 10)
                return false;
            if (enemyAttack >= Math.Max(8, friendHp - 5))
                return false;
            if (GetFreeBoardSlots(board) <= 0)
                return false;
            if (ShouldForceAbductionRayNow(board))
                return false;

            bool hasStorytellerInHand = board.Hand.Any(c => c != null && c.Template != null && c.Template.Id == TortollanStoryteller);
            if (!hasStorytellerInHand)
                return false;

            var oneDrops = board.Hand
                .Where(c => c != null && c.Template != null
                    && c.Type == Card.CType.MINION
                    && c.Template.Id != ForebodingFlame
                    && c.Template.Cost == 1
                    && c.CurrentCost <= 1)
                .ToList();
            if (oneDrops.Count < 2)
                return false;

            bool hasPlayableOneDropNow = oneDrops.Any(c => c.CurrentCost <= manaNow);
            if (!hasPlayableOneDropNow)
                return false;

            for (int i = 0; i < oneDrops.Count; i++)
            {
                for (int j = i + 1; j < oneDrops.Count; j++)
                {
                    var firstRaces = GetTrackedRaces(oneDrops[i].Template);
                    var secondRaces = GetTrackedRaces(oneDrops[j].Template);
                    if (firstRaces.Count == 0 || secondRaces.Count == 0)
                        continue;
                    if (!firstRaces.SetEquals(secondRaces))
                        return true;
                }
            }

            return false;
        }

        private bool ShouldTapBeforeEntropicContinuity(Board board, int friendHp)
        {
            if (board == null || board.Hand == null)
                return false;
            // 用户规则：分流安全血线下放到3，>=3 可先抽后续连。
            if (friendHp <= 2)
                return false;
            if (board.Hand.Count >= 9)
                return false;
            if (board.MinionFriend == null || board.MinionFriend.Count == 0)
                return false;
            if (board.MinionFriend.Count >= 7)
                return false;
            int manaNow = GetAvailableManaIncludingCoin(board);
            bool forebodingPlayableNow = board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == ForebodingFlame
                && c.CurrentCost <= manaNow)
                && GetFreeBoardSlots(board) > 0;
            if (forebodingPlayableNow)
                return false;

            int enemyAttack = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            bool dangerousHpTauntTempoWindow = IsDangerousHpTauntTempoWindow(friendHp, enemyAttack);
            if (dangerousHpTauntTempoWindow && GetPlayableTauntMinionCardIds(board, manaNow).Count > 0)
                return false;

            var entropy = board.Hand
                .Where(c => c != null && c.Template != null && c.Template.Id == EntropicContinuity)
                .OrderBy(c => c.CurrentCost)
                .FirstOrDefault();
            if (entropy == null)
                return false;

            int tapCost = 2;
            try
            {
                if (board.Ability != null)
                    tapCost = Math.Max(0, board.Ability.CurrentCost);
            }
            catch
            {
                tapCost = 2;
            }

            // 需要留出至少1费余量：抽牌后仍有机会补下0/1费随从再上续连。
            if (manaNow < tapCost + entropy.CurrentCost + 1)
                return false;
            int friendlyCoverage = board.MinionFriend.Count(m => m != null);
            if (friendlyCoverage < 3)
                return false;

            bool enemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            int enemyHp = GetHeroHealth(board.HeroEnemy);
            int buffAttack = GetBoardAttack(board.MinionFriend) + board.MinionFriend.Count;
            if (!enemyHasTaunt && buffAttack >= enemyHp)
                return false;

            return true;
        }

        private bool ShouldTapBeforeInfernal(Board board, int friendHp, int manaNow)
        {
            if (board == null || board.Hand == null)
                return false;
            // 用户规则：允许“先分流后地狱火”；仅保留分流不自杀的硬底线。
            if (friendHp > 15)
                return false;
            if (friendHp <= 3)
                return false;
            if (!CanUseLifeTapNow(board))
                return false;
            if (board.Hand.Count >= 9)
                return false;

            var infernal = board.Hand
                .Where(c => c != null && c.Template != null
                    && c.Template.Id == Infernal
                    && c.CurrentCost <= manaNow)
                .OrderBy(c => c.CurrentCost)
                .ThenBy(c => c.Id)
                .FirstOrDefault();
            if (infernal == null)
                return false;

            int tapCost = 2;
            try
            {
                if (board.Ability != null)
                    tapCost = Math.Max(0, board.Ability.CurrentCost);
            }
            catch
            {
                tapCost = 2;
            }

            if (manaNow < tapCost + infernal.CurrentCost)
                return false;

            bool enemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            int friendAttack = GetBoardAttack(board.MinionFriend);
            int enemyHp = GetHeroHealth(board.HeroEnemy);
            if (!enemyHasTaunt && friendAttack >= enemyHp)
                return false;

            return true;
        }

        private bool ShouldForceInfernalEmergency(Board board, int friendHp, int manaNow)
        {
            if (board == null || board.Hand == null)
                return false;
            if (friendHp > 3)
                return false;

            bool infernalPlayableNow = board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == Infernal
                && c.CurrentCost <= manaNow);
            if (!infernalPlayableNow)
                return false;

            bool enemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            int attackableFriendAttack = GetAttackableBoardAttack(board.MinionFriend);
            int enemyHp = GetHeroHealth(board.HeroEnemy);
            bool onBoardLethal = !enemyHasTaunt && attackableFriendAttack >= enemyHp;
            if (onBoardLethal)
                return false;

            return true;
        }

        private int GetTag(Card c, Card.GAME_TAG tag)
        {
            if (c != null && c.tags != null && c.tags.ContainsKey(tag))
                return c.tags[tag];
            return -1;
        }

        private bool IsCardFrozenByTag(Card c)
        {
            if (c == null)
                return false;

            Card.GAME_TAG frozenTag;
            if (!Enum.TryParse("FROZEN", out frozenTag))
                return false;

            return GetTag(c, frozenTag) == 1;
        }

        private bool TryApplyRafaamFamilyTempoRules(Board board, ProfileParameters p, Card card, int manaNow, bool isDisadvantaged)
        {
            if (board == null || board.Hand == null || p == null || card == null || card.Template == null)
                return false;

            string cardId = card.Template.Id.ToString();
            if (!RafaamFamilyCardIds.Contains(cardId))
                return false;

            bool hasOtherPlayableAction = board.Hand.Any(c => c != null && c.Template != null
                && !ReferenceEquals(c, card)
                && c.Template.Id != TheCoin
                && c.CurrentCost <= manaNow
                && !(c.Template.Id == EntropicContinuity && (board.MinionFriend == null || board.MinionFriend.Count == 0))
                && !(c.Type == Card.CType.MINION && GetFreeBoardSlots(board) <= 0));

            // 夺心者拉法姆：仅标记态（POWERED_UP）允许正常使用。
            if (string.Equals(cardId, "TIME_005t5", StringComparison.Ordinal))
            {
                bool isPoweredUp = GetTag(card, Card.GAME_TAG.POWERED_UP) == 1;
                if (!isPoweredUp)
                {
                    p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(1700));
                    p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(-7800));
                    AddLog("夺心者拉法姆：非标记态，降低使用优先级");
                    return true;
                }

                p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(-600));
                p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(3000));
                AddLog("夺心者拉法姆：标记态，前置使用");
                return true;
            }

            // 大法师拉法姆：仅劣势局面优先，非劣势强后置。
            if (string.Equals(cardId, "TIME_005t9", StringComparison.Ordinal))
            {
                if (!isDisadvantaged)
                {
                    p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(1900));
                    p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(-8200));
                    AddLog("大法师拉法姆：我方非劣势，降低使用优先级");
                    return true;
                }

                p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(-680));
                p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(3200));
                AddLog("大法师拉法姆：我方劣势，允许优先使用");
                return true;
            }

            // 用户规则：时空大盗拉法姆必须是标记态（POWERED_UP）才允许使用。
            // 非标记态：本回合禁止使用；标记态：保持最优先ComboSet（用于斩杀窗口）。
            if (card.Template.Id == TimethiefRafaam)
            {
                bool isPoweredUp = GetTag(card, Card.GAME_TAG.POWERED_UP) == 1;
                if (!isPoweredUp)
                {
                    p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(9800));
                    p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(-9999));
                    AddLog("时空大盗拉法姆：非标记态，硬规则禁用");
                    return true;
                }

                bool forcedTopCombo = SetSingleCardCombo(board, p, TimethiefRafaam, allowCoinBridge: true, forceOverride: true,
                    logWhenSet: "时空大盗拉法姆：ComboSet最优先使用");
                if (forcedTopCombo)
                {
                    p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(-9600));
                    p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(9999));
                    AddLog("时空大盗拉法姆：命中硬规则，强制本回合最先使用");
                    return true;
                }

                // 兜底：若ComboSet异常未生效，仍保持最高优先级直接使用。
                p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(-9200));
                p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(9999));
                AddLog("时空大盗拉法姆：ComboSet未命中，按最高优先级兜底");
                return true;
            }

            // 鱼人拉法姆：当我方场上随从<=5时优先使用；否则后置。
            if (string.Equals(cardId, "TIME_005t8", StringComparison.Ordinal))
            {
                int friendMinionCount = board.MinionFriend != null ? board.MinionFriend.Count : 0;
                if (friendMinionCount <= 5)
                {
                    p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(-2800));
                    p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(8200));
                    AddLog("鱼人拉法姆：我方场上随从<=5，优先使用");
                    return true;
                }

                p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(950));
                p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(-4200));
                AddLog("鱼人拉法姆：我方场上随从>5，后置使用");
                return true;
            }

            // 灾异/巨人拉法姆：高费体，劣势可前置，优势有替代动作时后置。
            if (string.Equals(cardId, "TIME_005t6", StringComparison.Ordinal)
                || string.Equals(cardId, "TIME_005t7", StringComparison.Ordinal))
            {
                if (!isDisadvantaged && hasOtherPlayableAction)
                {
                    p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(1450));
                    p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(-6200));
                    AddLog("高费拉法姆：我方非劣势且有替代动作，后置使用");
                    return true;
                }

                if (isDisadvantaged)
                {
                    p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(-420));
                    p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(2200));
                    AddLog("高费拉法姆：我方劣势，适度前置");
                    return true;
                }

                p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(-120));
                p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(1200));
                return true;
            }

            // 低中费拉法姆（小小/绿色/探险者/大酋长/鱼人）：默认按节奏落地，劣势时小幅前置。
            if (isDisadvantaged)
            {
                p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(-360));
                p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(1700));
                AddLog("拉法姆变体：我方劣势，节奏前置");
                return true;
            }

            p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(-180));
            p.PlayOrderModifiers.AddOrUpdate(card.Template.Id, new Modifier(900));
            return true;
        }

        private void HandleAmuletCardsByEffect(Board board, ProfileParameters p, int friendHp)
        {
            if (board == null || board.Hand == null || p == null)
                return;

            int manaNow = GetAvailableManaIncludingCoin(board);
            if (manaNow <= 0)
                return;

            int enemyAttack = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            int enemyHp = GetHeroHealth(board.HeroEnemy);
            bool enemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            int attackableFriendAttack = GetAttackableBoardAttack(board.MinionFriend);
            bool onBoardLethal = !enemyHasTaunt && attackableFriendAttack >= enemyHp;
            bool dangerousHpTauntTempoWindow = IsDangerousHpTauntTempoWindow(friendHp, enemyAttack);
            int handSpace = HearthstoneHandLimit - board.Hand.Count;
            int freeSlots = GetFreeBoardSlots(board);

            var trackingAmulet = board.Hand
                .Where(c => c != null && c.Template != null
                    && c.Template.Id == TrackingAmulet
                    && c.CurrentCost <= manaNow)
                .OrderBy(c => c.CurrentCost)
                .ThenBy(c => c.Id)
                .FirstOrDefault();
            if (trackingAmulet != null)
            {
                if (onBoardLethal)
                {
                    p.CastSpellsModifiers.AddOrUpdate(TrackingAmulet, new Modifier(2600));
                    p.PlayOrderModifiers.AddOrUpdate(TrackingAmulet, new Modifier(-9800));
                    AddLog("追踪护符：场攻可斩杀，后置不抢先");
                }
                else if (handSpace <= 1)
                {
                    p.CastSpellsModifiers.AddOrUpdate(TrackingAmulet, new Modifier(6800));
                    p.PlayOrderModifiers.AddOrUpdate(TrackingAmulet, new Modifier(-9999));
                    AddLog("追踪护符：手牌空间过小，硬后置防爆牌");
                }
                else if (dangerousHpTauntTempoWindow)
                {
                    p.CastSpellsModifiers.AddOrUpdate(TrackingAmulet, new Modifier(2400));
                    p.PlayOrderModifiers.AddOrUpdate(TrackingAmulet, new Modifier(-9400));
                    AddLog("追踪护符：危险血线窗口，后置让位保命动作");
                }
                else if (handSpace >= 3)
                {
                    p.CastSpellsModifiers.AddOrUpdate(TrackingAmulet, new Modifier(-2200));
                    p.PlayOrderModifiers.AddOrUpdate(TrackingAmulet, new Modifier(7600));
                    AddLog("追踪护符：手牌空间充足，前置补资源");
                }
                else
                {
                    p.CastSpellsModifiers.AddOrUpdate(TrackingAmulet, new Modifier(-500));
                    p.PlayOrderModifiers.AddOrUpdate(TrackingAmulet, new Modifier(1800));
                }
            }

            var crittersAmulet = board.Hand
                .Where(c => c != null && c.Template != null
                    && c.Template.Id == CrittersAmulet
                    && c.CurrentCost <= manaNow)
                .OrderBy(c => c.CurrentCost)
                .ThenBy(c => c.Id)
                .FirstOrDefault();
            if (crittersAmulet != null)
            {
                bool friendBoardEmpty = board.MinionFriend == null || board.MinionFriend.Count == 0;
                if (onBoardLethal)
                {
                    p.CastSpellsModifiers.AddOrUpdate(CrittersAmulet, new Modifier(2600));
                    p.PlayOrderModifiers.AddOrUpdate(CrittersAmulet, new Modifier(-9800));
                    AddLog("生灵护符：场攻可斩杀，后置不抢先");
                }
                else if (freeSlots <= 0)
                {
                    p.CastSpellsModifiers.AddOrUpdate(CrittersAmulet, new Modifier(9999));
                    p.PlayOrderModifiers.AddOrUpdate(CrittersAmulet, new Modifier(-9999));
                }
                else if (dangerousHpTauntTempoWindow || enemyAttack >= friendHp)
                {
                    p.CastSpellsModifiers.AddOrUpdate(CrittersAmulet, new Modifier(-3400));
                    p.PlayOrderModifiers.AddOrUpdate(CrittersAmulet, new Modifier(9400));
                    AddLog("生灵护符：高压窗口前置，优先补嘲讽稳场");
                }
                else if (friendBoardEmpty && enemyAttack >= 4)
                {
                    p.CastSpellsModifiers.AddOrUpdate(CrittersAmulet, new Modifier(-1800));
                    p.PlayOrderModifiers.AddOrUpdate(CrittersAmulet, new Modifier(6400));
                    AddLog("生灵护符：空场受压，前置补场");
                }
                else
                {
                    p.CastSpellsModifiers.AddOrUpdate(CrittersAmulet, new Modifier(-260));
                    p.PlayOrderModifiers.AddOrUpdate(CrittersAmulet, new Modifier(900));
                }
            }

            var stridesAmulet = board.Hand
                .Where(c => c != null && c.Template != null
                    && c.Template.Id == StridesAmulet
                    && c.CurrentCost <= manaNow)
                .OrderBy(c => c.CurrentCost)
                .ThenBy(c => c.Id)
                .FirstOrDefault();
            if (stridesAmulet != null)
            {
                int reducibleNonSpellCount = board.Hand.Count(c => c != null
                    && c.Template != null
                    && c.Template.Id != StridesAmulet
                    && c.Template.Id != TheCoin
                    && c.Type != Card.CType.SPELL
                    && c.CurrentCost >= 1);
                bool archimondePlayableNow = IsArchimondePlayableNow(board, manaNow);
                bool shouldYieldToArchimonde = archimondePlayableNow && board.MaxMana >= 10;

                if (onBoardLethal)
                {
                    p.CastSpellsModifiers.AddOrUpdate(StridesAmulet, new Modifier(2000));
                    p.PlayOrderModifiers.AddOrUpdate(StridesAmulet, new Modifier(-9800));
                    AddLog("挺进护符：场攻可斩杀，后置不抢先");
                }
                else if (shouldYieldToArchimonde)
                {
                    p.CastSpellsModifiers.AddOrUpdate(StridesAmulet, new Modifier(5200));
                    p.PlayOrderModifiers.AddOrUpdate(StridesAmulet, new Modifier(-9999));
                    AddLog("挺进护符：阿克蒙德可下，后置让位阿克蒙德");
                }
                else if (dangerousHpTauntTempoWindow && enemyAttack >= friendHp - 1)
                {
                    p.CastSpellsModifiers.AddOrUpdate(StridesAmulet, new Modifier(2600));
                    p.PlayOrderModifiers.AddOrUpdate(StridesAmulet, new Modifier(-9600));
                    AddLog("挺进护符：危险血线高压，后置让位保命动作");
                }
                else if (reducibleNonSpellCount >= 3)
                {
                    p.CastSpellsModifiers.AddOrUpdate(StridesAmulet, new Modifier(-2600));
                    p.PlayOrderModifiers.AddOrUpdate(StridesAmulet, new Modifier(7600));
                    AddLog("挺进护符：可减费目标>=3，前置赚节奏");
                }
                else if (reducibleNonSpellCount >= 2)
                {
                    p.CastSpellsModifiers.AddOrUpdate(StridesAmulet, new Modifier(-1200));
                    p.PlayOrderModifiers.AddOrUpdate(StridesAmulet, new Modifier(3400));
                }
                else
                {
                    p.CastSpellsModifiers.AddOrUpdate(StridesAmulet, new Modifier(1800));
                    p.PlayOrderModifiers.AddOrUpdate(StridesAmulet, new Modifier(-9200));
                    AddLog("挺进护符：可减费收益偏低，后置");
                }
            }

            var energyAmulet = board.Hand
                .Where(c => c != null && c.Template != null
                    && c.Template.Id == EnergyAmulet
                    && c.CurrentCost <= manaNow)
                .OrderBy(c => c.CurrentCost)
                .ThenBy(c => c.Id)
                .FirstOrDefault();
            if (energyAmulet != null)
            {
                int missingHealth = 0;
                try
                {
                    missingHealth = board.HeroFriend != null
                        ? Math.Max(0, board.HeroFriend.MaxHealth - board.HeroFriend.CurrentHealth)
                        : Math.Max(0, 30 - friendHp);
                }
                catch
                {
                    missingHealth = Math.Max(0, 30 - friendHp);
                }

                if (onBoardLethal)
                {
                    p.CastSpellsModifiers.AddOrUpdate(EnergyAmulet, new Modifier(2800));
                    p.PlayOrderModifiers.AddOrUpdate(EnergyAmulet, new Modifier(-9800));
                    AddLog("能量护符：场攻可斩杀，后置不抢先");
                }
                else if (dangerousHpTauntTempoWindow || enemyAttack >= friendHp)
                {
                    p.CastSpellsModifiers.AddOrUpdate(EnergyAmulet, new Modifier(-3200));
                    p.PlayOrderModifiers.AddOrUpdate(EnergyAmulet, new Modifier(8600));
                    AddLog("能量护符：危险血线窗口，前置回血");
                }
                else if (missingHealth >= 7)
                {
                    p.CastSpellsModifiers.AddOrUpdate(EnergyAmulet, new Modifier(-1600));
                    p.PlayOrderModifiers.AddOrUpdate(EnergyAmulet, new Modifier(4600));
                    AddLog("能量护符：缺失生命较高，前置回补血线");
                }
                else if (missingHealth <= 3)
                {
                    p.CastSpellsModifiers.AddOrUpdate(EnergyAmulet, new Modifier(1600));
                    p.PlayOrderModifiers.AddOrUpdate(EnergyAmulet, new Modifier(-9000));
                    AddLog("能量护符：缺失生命较少，后置避免低收益自伤");
                }
            }
        }

        private void HandleEnergyAmuletEmergency(Board board, ProfileParameters p, int friendHp)
        {
            if (board == null || board.Hand == null || p == null)
                return;

            int manaNow = GetAvailableManaIncludingCoin(board);
            var energyAmuletCard = board.Hand
                .Where(c => c != null && c.Template != null
                    && c.Template.Id == EnergyAmulet
                    && c.CurrentCost <= manaNow)
                .OrderBy(c => c.CurrentCost)
                .ThenByDescending(c => c.Id)
                .FirstOrDefault();
            if (energyAmuletCard == null)
                return;

            int enemyAttack = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            bool enemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            int enemyHp = GetHeroHealth(board.HeroEnemy);
            int attackableFriendAttack = GetAttackableBoardAttack(board.MinionFriend);
            bool onBoardLethal = !enemyHasTaunt && attackableFriendAttack >= enemyHp;
            bool emergencyWindow = !onBoardLethal
                && (friendHp <= 8
                    || enemyAttack >= friendHp
                    || IsDangerousHpTauntTempoWindow(friendHp, enemyAttack));
            if (!emergencyWindow)
                return;

            SetSingleCardComboByEntityId(board, p, energyAmuletCard.Id, allowCoinBridge: true, forceOverride: true,
                logWhenSet: "能量护符：低血高压，ComboSet前置回血");

            if (energyAmuletCard.Type == Card.CType.MINION)
            {
                p.CastMinionsModifiers.AddOrUpdate(energyAmuletCard.Id, new Modifier(-9800));
                p.CastMinionsModifiers.AddOrUpdate(EnergyAmulet, new Modifier(-4200));
            }
            else if (energyAmuletCard.Type == Card.CType.WEAPON)
            {
                p.CastWeaponsModifiers.AddOrUpdate(energyAmuletCard.Id, new Modifier(-9800));
                p.CastWeaponsModifiers.AddOrUpdate(EnergyAmulet, new Modifier(-4200));
            }
            else
            {
                p.CastSpellsModifiers.AddOrUpdate(energyAmuletCard.Id, new Modifier(-9800));
                p.CastSpellsModifiers.AddOrUpdate(EnergyAmulet, new Modifier(-4200));
            }

            p.PlayOrderModifiers.AddOrUpdate(energyAmuletCard.Id, new Modifier(9999));
            p.PlayOrderModifiers.AddOrUpdate(EnergyAmulet, new Modifier(9800));
            p.CastHeroPowerModifier.AddOrUpdate(LifeTap, new Modifier(9999));
            AddLog("能量护符：低血高压窗口，强制前置回血并禁用先分流");
        }

        private void HandleTemporaryCards(Board board, ProfileParameters p)
        {
            if (board == null || board.Hand == null || p == null)
                return;

            int manaNow = GetAvailableManaIncludingCoin(board);
            int freeSlots = GetFreeBoardSlots(board);
            int friendHpNow = GetHeroHealth(board.HeroFriend);
            int enemyAttackNow = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            bool enemyHasMinionForEntropyRule = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null);
            int friendMinionCountForEntropyRule = board.MinionFriend != null ? board.MinionFriend.Count : 0;
            bool entropyHardDisabledForCurrentBoard = friendMinionCountForEntropyRule <= 0
                || (enemyHasMinionForEntropyRule && friendMinionCountForEntropyRule <= 1)
                || (!enemyHasMinionForEntropyRule && manaNow < 3 && friendMinionCountForEntropyRule < 3);
            string entropyHardDisabledReason = friendMinionCountForEntropyRule <= 0
                ? "友方空场"
                : (enemyHasMinionForEntropyRule && friendMinionCountForEntropyRule <= 1
                    ? "敌方有随从且友方随从<2"
                    : (!enemyHasMinionForEntropyRule && manaNow < 3 && friendMinionCountForEntropyRule < 3
                        ? "敌方空场且可用费<3且友方随从<3"
                        : string.Empty));
            bool sketchPlayableNow = board.HasCardInHand(SketchArtist)
                && manaNow >= 4
                && freeSlots > 0
                && board.Hand.Any(c => c != null && c.Template != null
                    && c.Template.Id == SketchArtist
                    && c.CurrentCost <= manaNow);
            bool sketchShouldPreemptTemporary = sketchPlayableNow && board.Hand.Count <= 6;
            bool holdTemporaryEntropyForZilliax = CanPlayZilliaxThenEntropySameTurn(board, manaNow);
            var playableTauntMinionIds = GetPlayableTauntMinionCardIds(board, manaNow);
            var playableOutsideDeckTauntMinionIds = GetPlayableOutsideDeckTauntMinionCardIds(board, manaNow);
            bool dangerousHpTauntBreakWindow = IsDangerousHpTauntTempoWindow(friendHpNow, enemyAttackNow)
                                               && playableTauntMinionIds.Count > 0;

            // 用户规则：局势危急时优先手牌嘲讽，允许打断临时射线链路先保命。
            if (dangerousHpTauntBreakWindow)
            {
                foreach (var tauntId in playableTauntMinionIds)
                {
                    p.CastMinionsModifiers.AddOrUpdate(tauntId, new Modifier(-5600));
                    p.PlayOrderModifiers.AddOrUpdate(tauntId, new Modifier(9999));
                }

                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(5200));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(-9999));
                AddLog("临时牌：危险血线且有可下嘲讽，允许断链先下嘲讽稳场");
                return;
            }

            // 用户规则：只要手里有可下套外嘲讽恶魔，就先下嘲讽，临时射线可暂缓。
            if (playableOutsideDeckTauntMinionIds.Count > 0)
            {
                var preferredOutsideTaunt = board.Hand
                    .Where(c => c != null
                        && c.Template != null
                        && c.Type == Card.CType.MINION
                        && c.CurrentCost <= manaNow
                        && c.IsRace(Card.CRace.DEMON)
                        && IsOutsideDeckDemonByDeckList(board, c.Template.Id)
                        && IsTauntMinionForSurvival(c))
                    .OrderBy(c => c.CurrentCost)
                    .ThenBy(c => c.Id)
                    .FirstOrDefault();
                if (preferredOutsideTaunt != null)
                {
                    SetSingleCardComboByEntityId(board, p, preferredOutsideTaunt.Id,
                        allowCoinBridge: true, forceOverride: true,
                        logWhenSet: "临时牌：命中套外嘲讽优先窗口，ComboSet先下嘲讽");
                }

                foreach (var tauntId in playableOutsideDeckTauntMinionIds)
                {
                    p.CastMinionsModifiers.AddOrUpdate(tauntId, new Modifier(-6200));
                    p.PlayOrderModifiers.AddOrUpdate(tauntId, new Modifier(9999));
                }

                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(5600));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(-9999));
                AddLog("临时牌：手有可下套外嘲讽恶魔，暂缓临时射线先下嘲讽");
                return;
            }

            // 用户规则：套外随从较多且手牌偏满时，临时射线也可暂缓，优先先用手牌。
            int outsideDeckDemonsInHand = CountOutsideDeckDemonsInHand(board);
            int playableOutsideDeckDemonsNow = GetMaxPlayableOutsideDeckDemonsThisTurn(board, manaNow);
            bool delayTemporaryRayForHeavyOutsideHandWindow = !HasStartedOrLastPlayedAbductionRayThisTurn(board)
                && board.Hand.Count >= 7
                && outsideDeckDemonsInHand >= 2
                && playableOutsideDeckDemonsNow > 0;
            if (delayTemporaryRayForHeavyOutsideHandWindow)
            {
                p.CastSpellsModifiers.AddOrUpdate(AbductionRay, new Modifier(5600));
                p.PlayOrderModifiers.AddOrUpdate(AbductionRay, new Modifier(-9999));
                AddLog("临时牌：手里套外随从较多且手牌偏满，暂缓挟持射线先用手牌");
                return;
            }

            // 用户硬规则：临时挟持射线不受任何条件影响，有就直接用。
            Card immediateTempRay = null;
            for (int i = board.Hand.Count - 1; i >= 0; i--)
            {
                var c = board.Hand[i];
                if (c == null || c.Template == null)
                    continue;
                if (c.Template.Id != AbductionRay)
                    continue;
                if (c.CurrentCost > manaNow)
                    continue;
                if (!IsTemporaryCard(c))
                    continue;
                immediateTempRay = c;
                break;
            }
            if (immediateTempRay != null)
            {
                SetSingleCardComboByEntityId(board, p, immediateTempRay.Id, allowCoinBridge: true, forceOverride: true,
                    logWhenSet: "临时牌：挟持射线临时链硬规则，有就直接用");
                p.CastSpellsModifiers.AddOrUpdate(immediateTempRay.Id, new Modifier(-9800));
                p.PlayOrderModifiers.AddOrUpdate(immediateTempRay.Id, new Modifier(9999));
                // 同名保护：临时链路命中时，同回合可打的非临时射线后置，避免错打到普通副本。
                foreach (var ray in board.Hand.Where(c => c != null
                    && c.Template != null
                    && c.Template.Id == AbductionRay
                    && c.CurrentCost <= manaNow
                    && c.Id != immediateTempRay.Id
                    && !IsTemporaryCard(c)))
                {
                    p.CastSpellsModifiers.AddOrUpdate(ray.Id, new Modifier(5600));
                    p.PlayOrderModifiers.AddOrUpdate(ray.Id, new Modifier(-9800));
                }
                return;
            }

            if (sketchShouldPreemptTemporary)
            {
                AddLog("临时牌：速写美术家可用，临时牌让位速写先手");
                return;
            }
            if (sketchPlayableNow && board.Hand.Count >= 7)
                AddLog("临时牌：手牌较多，不再让位速写先手");

            bool hasPlayableTemporaryTomb = board.Hand.Any(c => c != null
                && c.Template != null
                && c.Template.Id == TombOfSuffering
                && c.CurrentCost <= manaNow
                && IsTemporaryCard(c));
            if (hasPlayableTemporaryTomb)
                AddLog("临时牌：临时咒怨之墓遵守墓规则，交由墓逻辑判定");

            // 咒怨之墓/速写美术家产生的临时牌，尽量当回合转化。
            foreach (var card in board.Hand.Where(c => c != null && c.Template != null))
            {
                if (!IsTemporaryCard(card))
                    continue;
                if (card.Template.Id == TombOfSuffering)
                    continue;

                // 与主规则保持一致：
                // 敌方有随从时友方>=2可用；
                // 敌方空场时，低费(<3)仅在友方随从<3时禁用。
                if (card.Template.Id == EntropicContinuity && entropyHardDisabledForCurrentBoard)
                {
                    p.CastSpellsModifiers.AddOrUpdate(card.Id, new Modifier(9999));
                    p.PlayOrderModifiers.AddOrUpdate(card.Id, new Modifier(-9999));
                    AddLog("临时牌：续连熵能" + entropyHardDisabledReason + "，硬规则禁用");
                    continue;
                }

                if (card.Type == Card.CType.MINION)
                    p.CastMinionsModifiers.AddOrUpdate(card.Id, new Modifier(-220));
                else if (card.Type == Card.CType.SPELL)
                    p.CastSpellsModifiers.AddOrUpdate(card.Id, new Modifier(-220));
                else if (card.Type == Card.CType.WEAPON)
                    p.CastWeaponsModifiers.AddOrUpdate(card.Id, new Modifier(-220));
            }

            // 用户规则：当手里存在同名“临时+非临时”副本时，若要打该牌则优先临时实体。
            ApplyTemporaryDuplicateEntityPriority(board, p, manaNow);

            // 用户硬规则：同回合可“奇利亚斯 + 续连熵能”时，临时牌阶段不再强行抢先。
            // 避免临时续连或其他临时牌覆盖既定的“先奇利亚斯再buff”顺序。
            if (holdTemporaryEntropyForZilliax)
            {
                AddLog("临时牌：命中先奇利亚斯后续连规则，临时牌不抢先");
                return;
            }

            // 用户硬规则：命中临时牌时，优先“最右且可用”的临时牌本体（按实体ID）。
            Card rightmostPlayableTemporary = null;
            for (int i = board.Hand.Count - 1; i >= 0; i--)
            {
                var c = board.Hand[i];
                if (c == null || c.Template == null)
                    continue;
                if (!IsTemporaryCard(c))
                    continue;
                if (c.CurrentCost > manaNow)
                    continue;
                if (c.Type == Card.CType.MINION && freeSlots <= 0)
                    continue;
                if (c.Template.Id == TombOfSuffering)
                    continue;
                if (c.Template.Id == EntropicContinuity && entropyHardDisabledForCurrentBoard)
                    continue;

                rightmostPlayableTemporary = c;
                break;
            }

            if (rightmostPlayableTemporary == null)
                return;

            // 用户规则：临时挟持射线链中，允许先放行0费随从腾手牌，避免射线连发把手牌顶满。
            if (rightmostPlayableTemporary.Template.Id == AbductionRay)
            {
                var zeroCostRelease = GetRightmostPlayableZeroCostNonRayMinion(board, manaNow);
                if (zeroCostRelease != null)
                {
                    SetSingleCardComboByEntityId(board, p, zeroCostRelease.Id, allowCoinBridge: true, forceOverride: true,
                        logWhenSet: "临时牌：挟持射线链放行0费随从");
                    p.CastMinionsModifiers.AddOrUpdate(zeroCostRelease.Id, new Modifier(-5000));
                    p.PlayOrderModifiers.AddOrUpdate(zeroCostRelease.Id, new Modifier(9999));

                    // 先放行0费，再续临时射线，避免手牌顶满。
                    p.CastSpellsModifiers.AddOrUpdate(rightmostPlayableTemporary.Id, new Modifier(900));
                    p.PlayOrderModifiers.AddOrUpdate(rightmostPlayableTemporary.Id, new Modifier(9200));
                    AddLog("临时牌：命中临时挟持射线，先放行0费随从腾手牌");
                    return;
                }
            }

            // 用户规则：若命中的临时牌是小精灵，且本回合可分流，优先先抽一口再补位。
            if (rightmostPlayableTemporary.Template.Id == Wisp
                && CanUseLifeTapNow(board)
                && board.Hand.Count <= 5
                && GetHeroHealth(board.HeroFriend) >= 14
                && !CanPlayWispThenStorytellerSameTurn(board, manaNow)
                && !ShouldHoldForStorytellerBuff(board))
            {
                bool wispEnemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
                int wispFriendAttack = GetBoardAttack(board.MinionFriend);
                int wispEnemyHp = GetHeroHealth(board.HeroEnemy);
                if (wispEnemyHasTaunt || wispFriendAttack < wispEnemyHp)
                {
                    p.CastMinionsModifiers.AddOrUpdate(rightmostPlayableTemporary.Id, new Modifier(900));
                    p.PlayOrderModifiers.AddOrUpdate(rightmostPlayableTemporary.Id, new Modifier(-9200));
                    AddLog("临时牌：最右临时小精灵后置，优先先抽一口再补位");
                    return;
                }
            }

            // 用户新规则：临时牌不再一刀切强推，需看局势（临时挟持射线链路除外）。
            bool isTempRay = rightmostPlayableTemporary.Template.Id == AbductionRay;
            int friendHp = GetHeroHealth(board.HeroFriend);
            int enemyHp = GetHeroHealth(board.HeroEnemy);
            int friendAttack = GetBoardAttack(board.MinionFriend);
            int enemyAttack = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            bool enemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            bool onBoardLethal = !enemyHasTaunt && friendAttack >= enemyHp;
            bool highPressureWindow = ShouldEnterBalancedDefenseMode(board, friendHp, enemyHp)
                || (friendHp <= 10 && enemyAttack >= 8);
            bool shouldForceTemporary = isTempRay || onBoardLethal || !highPressureWindow;

            // 用户规则：始祖龟buff窗口下，临时自伤随从不强推，优先留场吃回合末增益。
            bool storytellerBuffHoldWindow = ShouldHoldForStorytellerBuff(board);
            bool isTempSelfDamageMinion = rightmostPlayableTemporary.Template.Id == PartyFiend
                || rightmostPlayableTemporary.Template.Id == FlameImp
                || rightmostPlayableTemporary.Template.Id == Infernal;
            if (storytellerBuffHoldWindow && isTempSelfDamageMinion && !onBoardLethal)
                shouldForceTemporary = false;

            if (!shouldForceTemporary)
            {
                bool isSelfDamageTempoMinion = rightmostPlayableTemporary.Template.Id == PartyFiend
                    || rightmostPlayableTemporary.Template.Id == FlameImp
                    || rightmostPlayableTemporary.Template.Id == Infernal;

                int castDelay = isSelfDamageTempoMinion ? 3200 : 900;
                int orderDelay = isSelfDamageTempoMinion ? -9800 : -7800;
                if (rightmostPlayableTemporary.Type == Card.CType.MINION)
                    p.CastMinionsModifiers.AddOrUpdate(rightmostPlayableTemporary.Id, new Modifier(castDelay));
                else if (rightmostPlayableTemporary.Type == Card.CType.SPELL)
                    p.CastSpellsModifiers.AddOrUpdate(rightmostPlayableTemporary.Id, new Modifier(castDelay));
                else if (rightmostPlayableTemporary.Type == Card.CType.WEAPON)
                    p.CastWeaponsModifiers.AddOrUpdate(rightmostPlayableTemporary.Id, new Modifier(castDelay));

                p.PlayOrderModifiers.AddOrUpdate(rightmostPlayableTemporary.Id, new Modifier(orderDelay));
                if (storytellerBuffHoldWindow && isTempSelfDamageMinion && !onBoardLethal)
                    AddLog("始祖龟窗口：临时自伤随从后置，优先留场吃buff");
                AddLog("临时牌：局势高压，取消强制先手（" + rightmostPlayableTemporary.Template.Id + "）");
                return;
            }

            SetSingleCardComboByEntityId(board, p, rightmostPlayableTemporary.Id, allowCoinBridge: true, forceOverride: true,
                logWhenSet: "临时牌：ComboSet命中最右可用临时牌");

            if (rightmostPlayableTemporary.Type == Card.CType.MINION)
                p.CastMinionsModifiers.AddOrUpdate(rightmostPlayableTemporary.Id, new Modifier(-4200));
            else if (rightmostPlayableTemporary.Type == Card.CType.SPELL)
                p.CastSpellsModifiers.AddOrUpdate(rightmostPlayableTemporary.Id, new Modifier(-4200));
            else if (rightmostPlayableTemporary.Type == Card.CType.WEAPON)
                p.CastWeaponsModifiers.AddOrUpdate(rightmostPlayableTemporary.Id, new Modifier(-4200));

            p.PlayOrderModifiers.AddOrUpdate(rightmostPlayableTemporary.Id, new Modifier(9999));
            AddLog("临时牌：最右可用临时牌=" + rightmostPlayableTemporary.Template.Id + "，按实体ID强制优先");

            // 同名不同实体时，把非目标副本后置，防止误打到非临时那张。
            foreach (var c in board.Hand.Where(x => x != null && x.Template != null
                                                    && x.Template.Id == rightmostPlayableTemporary.Template.Id
                                                    && x.Id != rightmostPlayableTemporary.Id))
            {
                if (c.Type == Card.CType.MINION)
                    p.CastMinionsModifiers.AddOrUpdate(c.Id, new Modifier(1800));
                else if (c.Type == Card.CType.SPELL)
                    p.CastSpellsModifiers.AddOrUpdate(c.Id, new Modifier(1800));
                else if (c.Type == Card.CType.WEAPON)
                    p.CastWeaponsModifiers.AddOrUpdate(c.Id, new Modifier(1800));

                p.PlayOrderModifiers.AddOrUpdate(c.Id, new Modifier(-9000));
            }
        }

        private bool CanPlayZilliaxThenEntropySameTurn(Board board, int manaNow)
        {
            if (board == null || board.Hand == null)
                return false;
            if (GetFreeBoardSlots(board) <= 0)
                return false;

            var zilliaxCard = board.Hand
                .Where(c => c != null && c.Template != null
                    && (c.Template.Id == ZilliaxTickingPower || c.Template.Id == ZilliaxDeckBuilder)
                    && c.CurrentCost <= manaNow)
                .OrderBy(c => c.CurrentCost)
                .ThenBy(c => c.Id)
                .FirstOrDefault();
            var entropyCard = board.Hand
                .Where(c => c != null && c.Template != null
                    && c.Template.Id == EntropicContinuity
                    && c.CurrentCost <= manaNow)
                .OrderBy(c => c.CurrentCost)
                .ThenBy(c => c.Id)
                .FirstOrDefault();

            if (zilliaxCard == null || entropyCard == null)
                return false;

            return zilliaxCard.CurrentCost + entropyCard.CurrentCost <= manaNow;
        }

        private Card GetPlayableTemporaryZilliax(Board board, int manaNow)
        {
            if (board == null || board.Hand == null)
                return null;
            if (GetFreeBoardSlots(board) <= 0)
                return null;

            return board.Hand
                .Where(c => c != null && c.Template != null
                    && (c.Template.Id == ZilliaxTickingPower || c.Template.Id == ZilliaxDeckBuilder)
                    && c.CurrentCost <= manaNow
                    && IsTemporaryCard(c))
                .OrderBy(c => c.CurrentCost)
                .ThenBy(c => c.Id)
                .FirstOrDefault();
        }

        private bool CanPlayPartyThenZilliaxSameTurn(Board board, int manaNow)
        {
            if (board == null || board.Hand == null)
                return false;
            if (GetFreeBoardSlots(board) < 4)
                return false;
            if (board.MinionFriend != null && board.MinionFriend.Count > 3)
                return false;

            var partyFiendCard = board.Hand
                .Where(c => c != null && c.Template != null
                    && c.Template.Id == PartyFiend
                    && c.CurrentCost <= manaNow)
                .OrderBy(c => c.CurrentCost)
                .ThenBy(c => c.Id)
                .FirstOrDefault();
            var zilliaxCard = board.Hand
                .Where(c => c != null && c.Template != null
                    && (c.Template.Id == ZilliaxTickingPower || c.Template.Id == ZilliaxDeckBuilder)
                    && c.CurrentCost <= manaNow)
                .OrderBy(c => c.CurrentCost)
                .ThenBy(c => c.Id)
                .FirstOrDefault();

            if (partyFiendCard == null || zilliaxCard == null)
                return false;

            // 邪犬落地会额外铺两个邪能兽，按历史口径预估可使奇利亚斯当回合降费约3。
            int zilliaxCostAfterPartyEstimate = Math.Max(0, zilliaxCard.CurrentCost - 3);
            return partyFiendCard.CurrentCost + zilliaxCostAfterPartyEstimate <= manaNow;
        }

        private bool ShouldCoinIntoPartyFiendNow(Board board, int manaNow)
        {
            if (board == null || board.Hand == null)
                return false;
            if (IsFirstTurnSlitherspearPriorityWindow(board))
                return false;
            if (!board.HasCardInHand(TheCoin))
                return false;
            if (board.MaxMana != 1)
                return false;
            if (GetFreeBoardSlots(board) < 3)
                return false;
            if (board.MinionFriend != null && board.MinionFriend.Count > 1)
                return false;

            return board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == PartyFiend
                && c.CurrentCost <= manaNow);
        }

        private bool IsFirstTurnSlitherspearPriorityWindow(Board board)
        {
            if (board == null || board.Hand == null)
                return false;
            if (board.MaxMana != 1)
                return false;
            if (GetFreeBoardSlots(board) <= 0)
                return false;

            int manaNow = GetAvailableManaIncludingCoin(board);
            return board.Hand.Any(c => c != null
                && c.Template != null
                && c.Template.Id == ViciousSlitherspear
                && c.CurrentCost <= manaNow);
        }

        private bool CanPlayOneDropThenZilliaxSameTurn(Board board, int manaNow)
        {
            if (board == null || board.Hand == null)
                return false;
            if (GetFreeBoardSlots(board) < 2)
                return false;

            var oneDropMinion = board.Hand
                .Where(c => c != null && c.Template != null
                    && c.Type == Card.CType.MINION
                    && c.Template.Id != ZilliaxTickingPower
                    && c.Template.Id != ZilliaxDeckBuilder
                    && c.CurrentCost <= 1
                    && c.CurrentCost <= manaNow)
                .OrderBy(c => c.CurrentCost)
                .ThenBy(c => c.Id)
                .FirstOrDefault();
            var zilliaxCard = board.Hand
                .Where(c => c != null && c.Template != null
                    && (c.Template.Id == ZilliaxTickingPower || c.Template.Id == ZilliaxDeckBuilder)
                    && c.CurrentCost <= manaNow)
                .OrderBy(c => c.CurrentCost)
                .ThenBy(c => c.Id)
                .FirstOrDefault();
            if (oneDropMinion == null || zilliaxCard == null)
                return false;

            int projectedZilliaxCostAfterOneDrop = Math.Max(0, zilliaxCard.CurrentCost - 1);
            int oneDropCost = Math.Max(0, oneDropMinion.CurrentCost);
            return oneDropCost + Math.Min(zilliaxCard.CurrentCost, projectedZilliaxCostAfterOneDrop) <= manaNow;
        }

        // 用户规则：有“先铺场再续连熵能”高收益线时，不强推速写美术家第一手。
        private bool ShouldPreferTempoBuffOverSketch(Board board, int manaNow)
        {
            if (board == null || board.Hand == null || board.MinionFriend == null)
                return false;
            if (GetFreeBoardSlots(board) <= 0)
                return false;
            if (ShouldForceAbductionRayNow(board))
                return false;

            var entropyCard = board.Hand
                .Where(c => c != null && c.Template != null
                    && c.Template.Id == EntropicContinuity
                    && c.CurrentCost <= manaNow)
                .OrderBy(c => c.CurrentCost)
                .ThenBy(c => c.Id)
                .FirstOrDefault();
            if (entropyCard == null)
                return false;

            int entropyCost = entropyCard.CurrentCost;
            int friendlyCoverage = board.MinionFriend.Count(m => m != null);
            // 用户规则：续连熵能至少要能覆盖到3个友方随从，否则不应作为“优于速写”的主线。
            if (friendlyCoverage < 3)
                return false;

            bool canPlayZilliaxThenEntropySameTurn = CanPlayZilliaxThenEntropySameTurn(board, manaNow);

            var partyCard = board.Hand
                .Where(c => c != null && c.Template != null
                    && c.Template.Id == PartyFiend
                    && c.CurrentCost <= manaNow)
                .OrderBy(c => c.CurrentCost)
                .ThenBy(c => c.Id)
                .FirstOrDefault();
            bool canPlayPartyThenEntropySameTurn = partyCard != null
                && manaNow >= partyCard.CurrentCost + entropyCost
                && board.MinionFriend.Count <= 4
                && GetFreeBoardSlots(board) >= 3
                && GetHeroHealth(board.HeroFriend) > 8;

            return canPlayZilliaxThenEntropySameTurn || canPlayPartyThenEntropySameTurn;
        }

        private static bool HasExistingCombo(ProfileParameters p)
        {
            if (p == null || p.ComboModifier == null)
                return false;

            try { return !p.ComboModifier.IsEmpty(); }
            catch { return true; }
        }

        private bool SetSingleCardCombo(Board board, ProfileParameters p, Card.Cards targetDbId, bool allowCoinBridge, bool forceOverride, string logWhenSet)
        {
            if (board == null || board.Hand == null || p == null)
                return false;
            if (targetDbId == AbductionRay && !IsAbductionRayTurnUnlocked(board))
                return false;
            if (targetDbId != ViciousSlitherspear && IsFirstTurnSlitherspearPriorityWindow(board))
                return false;
            if (!forceOverride && HasExistingCombo(p))
                return false;

            int manaNow = GetAvailableManaIncludingCoin(board);
            var target = GetRightmostHandCardByDbId(board, targetDbId, manaNow);
            if (target == null)
                return false;
            if (target.Type == Card.CType.MINION && GetFreeBoardSlots(board) <= 0)
                return false;

            int realMana = Math.Max(0, board.ManaAvailable);
            if (target.CurrentCost > manaNow)
                return false;

            var combo = new List<int>();
            if (target.CurrentCost > realMana)
            {
                if (!allowCoinBridge)
                    return false;

                var coin = board.Hand.FirstOrDefault(c => c != null && c.Template != null && c.Template.Id == TheCoin);
                if (coin == null || target.CurrentCost > realMana + 1)
                    return false;

                combo.Add(coin.Id);
            }

            combo.Add(target.Id);
            p.ComboModifier = new ComboSet(combo.ToArray());

            if (!string.IsNullOrWhiteSpace(logWhenSet))
                AddLog(logWhenSet);

            return true;
        }

        private bool SetSingleCardComboByEntityId(Board board, ProfileParameters p, int targetEntityId, bool allowCoinBridge, bool forceOverride, string logWhenSet)
        {
            if (board == null || board.Hand == null || p == null)
                return false;
            if (!forceOverride && HasExistingCombo(p))
                return false;

            var target = board.Hand.FirstOrDefault(c => c != null && c.Template != null && c.Id == targetEntityId);
            if (target == null)
                return false;
            if (target.Template.Id == AbductionRay && !IsAbductionRayTurnUnlocked(board))
                return false;
            if (target.Type == Card.CType.MINION && GetFreeBoardSlots(board) <= 0)
                return false;

            int realMana = Math.Max(0, board.ManaAvailable);
            int manaNow = GetAvailableManaIncludingCoin(board);
            if (target.CurrentCost > manaNow)
                return false;

            var combo = new List<int>();
            if (target.CurrentCost > realMana)
            {
                if (!allowCoinBridge)
                    return false;

                var coin = board.Hand.FirstOrDefault(c => c != null && c.Template != null && c.Template.Id == TheCoin);
                if (coin == null || target.CurrentCost > realMana + 1)
                    return false;

                combo.Add(coin.Id);
            }

            combo.Add(target.Id);
            p.ComboModifier = new ComboSet(combo.ToArray());

            if (!string.IsNullOrWhiteSpace(logWhenSet))
                AddLog(logWhenSet);

            return true;
        }

        private bool SetTwoCardCombo(Board board, ProfileParameters p, Card.Cards firstDbId, Card.Cards secondDbId, bool allowCoinBridge, bool forceOverride, string logWhenSet)
        {
            if (board == null || board.Hand == null || p == null)
                return false;
            if (!forceOverride && HasExistingCombo(p))
                return false;

            int manaNow = GetAvailableManaIncludingCoin(board);
            Card first = null;
            Card second = null;
            if (firstDbId == secondDbId)
            {
                var sameCards = GetRightmostHandCardsByDbId(board, firstDbId, manaNow).Take(2).ToList();
                if (sameCards.Count >= 2)
                {
                    first = sameCards[0];
                    second = sameCards[1];
                }
            }
            else
            {
                first = GetRightmostHandCardByDbId(board, firstDbId, manaNow);
                second = GetRightmostHandCardByDbId(board, secondDbId, manaNow);
            }
            if (first == null || second == null)
                return false;
            if (first.Id == second.Id)
                return false;

            int neededBoardSlots = 0;
            if (first.Type == Card.CType.MINION) neededBoardSlots++;
            if (second.Type == Card.CType.MINION) neededBoardSlots++;
            if (GetFreeBoardSlots(board) < neededBoardSlots)
                return false;

            int realMana = Math.Max(0, board.ManaAvailable);
            int totalCost = first.CurrentCost + second.CurrentCost;
            if (totalCost > manaNow)
                return false;

            var combo = new List<int>();
            if (totalCost > realMana)
            {
                if (!allowCoinBridge)
                    return false;

                var coin = board.Hand.FirstOrDefault(c => c != null && c.Template != null && c.Template.Id == TheCoin);
                if (coin == null || totalCost > realMana + 1)
                    return false;

                combo.Add(coin.Id);
            }

            combo.Add(first.Id);
            combo.Add(second.Id);
            p.ComboModifier = new ComboSet(combo.ToArray());

            if (!string.IsNullOrWhiteSpace(logWhenSet))
                AddLog(logWhenSet);

            return true;
        }

        // 用户规则：在续连回合，尽量先铺可下随从，再打续连熵能，最大化buff覆盖。
        private bool SetPlayableMinionsThenEntropyCombo(Board board, ProfileParameters p, bool allowCoinBridge, bool forceOverride, string logWhenSet)
        {
            if (board == null || board.Hand == null || p == null)
                return false;
            if (!forceOverride && HasExistingCombo(p))
                return false;

            int manaNow = GetAvailableManaIncludingCoin(board);
            var entropy = board.Hand
                .Where(c => c != null && c.Template != null
                    && c.Template.Id == EntropicContinuity
                    && c.CurrentCost <= manaNow)
                .OrderBy(c => c.CurrentCost)
                .ThenByDescending(c => c.Id)
                .FirstOrDefault();
            if (entropy == null)
                return false;

            int freeSlots = GetFreeBoardSlots(board);
            if (freeSlots <= 0)
                return false;

            int manaBeforeEntropy = manaNow - entropy.CurrentCost;
            if (manaBeforeEntropy < 0)
                return false;

            var normalMinions = board.Hand
                .Where(c => c != null
                    && c.Template != null
                    && c.Type == Card.CType.MINION
                    && c.Template.Id != PartyFiend
                    && c.CurrentCost <= manaBeforeEntropy)
                .OrderBy(c => c.CurrentCost)
                .ThenByDescending(c => c.Id)
                .ToList();
            var partyMinions = board.Hand
                .Where(c => c != null
                    && c.Template != null
                    && c.Type == Card.CType.MINION
                    && c.Template.Id == PartyFiend
                    && c.CurrentCost <= manaBeforeEntropy)
                .OrderBy(c => c.CurrentCost)
                .ThenByDescending(c => c.Id)
                .ToList();

            var selectedMinions = new List<Card>();
            int remainMana = manaBeforeEntropy;
            int remainSlots = freeSlots;

            // 先按费用铺普通随从，保证“能下的先下”。
            foreach (var m in normalMinions)
            {
                if (remainSlots <= 0)
                    break;
                if (m.CurrentCost > remainMana)
                    continue;
                selectedMinions.Add(m);
                remainMana -= m.CurrentCost;
                remainSlots -= 1;
            }

            // 邪犬放在普通随从之后，避免提前占格导致后续随从无法落地。
            foreach (var m in partyMinions)
            {
                if (remainSlots <= 0)
                    break;
                if (m.CurrentCost > remainMana)
                    continue;

                selectedMinions.Add(m);
                remainMana -= m.CurrentCost;
                remainSlots -= Math.Min(3, remainSlots); // 邪犬+两只邪能兽占位上限3格
            }

            if (selectedMinions.Count == 0)
                return false;

            int totalCost = selectedMinions.Sum(x => x.CurrentCost) + entropy.CurrentCost;
            int realMana = Math.Max(0, board.ManaAvailable);

            var combo = new List<int>();
            if (totalCost > realMana)
            {
                if (!allowCoinBridge)
                    return false;

                var coin = board.Hand.FirstOrDefault(c => c != null && c.Template != null && c.Template.Id == TheCoin);
                if (coin == null || totalCost > realMana + 1)
                    return false;

                combo.Add(coin.Id);
            }

            foreach (var m in selectedMinions)
                combo.Add(m.Id);
            combo.Add(entropy.Id);

            p.ComboModifier = new ComboSet(combo.ToArray());
            if (!string.IsNullOrWhiteSpace(logWhenSet))
                AddLog(logWhenSet);

            return true;
        }

        private bool SetAbductionRayCombo(Board board, ProfileParameters p, bool allowCoinBridge, bool chainAll, bool forceOverride, string logWhenSet)
        {
            if (board == null || board.Hand == null || p == null)
                return false;
            if (!forceOverride && HasExistingCombo(p))
                return false;
            if (!IsAbductionRayTurnUnlocked(board))
                return false;

            int realMana = Math.Max(0, board.ManaAvailable);
            int manaNow = GetAvailableManaIncludingCoin(board);

            var rays = GetRightmostHandCardsByDbId(board, AbductionRay, manaNow).ToList();
            if (rays.Count == 0)
                return false;

            int rightmostCost = rays[0].CurrentCost;
            if (rightmostCost > manaNow)
                return false;

            var combo = new List<int>();
            int spendable = realMana;

            if (spendable < rightmostCost)
            {
                if (!allowCoinBridge)
                    return false;

                var coin = board.Hand.FirstOrDefault(c => c != null && c.Template != null && c.Template.Id == TheCoin);
                if (coin == null || spendable + 1 < rightmostCost)
                    return false;

                combo.Add(coin.Id);
                spendable += 1;
            }

            int addedRay = 0;
            foreach (var ray in rays)
            {
                if (ray.CurrentCost > spendable)
                    continue;

                combo.Add(ray.Id);
                spendable -= ray.CurrentCost;
                addedRay++;

                if (!chainAll)
                    break;
            }

            if (addedRay <= 0)
                return false;

            p.ComboModifier = new ComboSet(combo.ToArray());
            if (!string.IsNullOrWhiteSpace(logWhenSet))
                AddLog(logWhenSet);

            return true;
        }

        private bool IsTemporaryCard(Card card)
        {
            if (card == null)
                return false;

            // 用户硬规则：挟持射线仅按“追踪到的临时ID”识别，避免把非最右射线误判为临时。
            if (card.Template != null && card.Template.Id == AbductionRay)
            {
                try { return _temporaryHandEntityIdsThisTurn.Contains(card.Id); }
                catch { return false; }
            }

            bool byTag = false;
            try { byTag = card.HasTag(Card.GAME_TAG.LUNAHIGHLIGHTHINT); }
            catch { byTag = false; }
            if (byTag)
                return true;

            try { return _temporaryHandEntityIdsThisTurn.Contains(card.Id); }
            catch { return false; }
        }

        private void ApplyTemporaryDuplicateEntityPriority(Board board, ProfileParameters p, int manaNow)
        {
            if (board == null || board.Hand == null || p == null)
                return;

            var playableCards = board.Hand
                .Where(c => c != null && c.Template != null && c.CurrentCost <= manaNow)
                .ToList();
            if (playableCards.Count == 0)
                return;

            var groups = playableCards.GroupBy(c => c.Template.Id);
            foreach (var group in groups)
            {
                var sameCards = group.ToList();
                if (sameCards.Count < 2)
                    continue;

                var tempCopies = sameCards.Where(IsTemporaryCard).ToList();
                var normalCopies = sameCards.Where(c => !IsTemporaryCard(c)).ToList();
                if (tempCopies.Count == 0 || normalCopies.Count == 0)
                    continue;

                foreach (var tempCard in tempCopies)
                {
                    if (tempCard.Type == Card.CType.MINION)
                        p.CastMinionsModifiers.AddOrUpdate(tempCard.Id, new Modifier(-380));
                    else if (tempCard.Type == Card.CType.SPELL)
                        p.CastSpellsModifiers.AddOrUpdate(tempCard.Id, new Modifier(-380));
                    else if (tempCard.Type == Card.CType.WEAPON)
                        p.CastWeaponsModifiers.AddOrUpdate(tempCard.Id, new Modifier(-380));

                    p.PlayOrderModifiers.AddOrUpdate(tempCard.Id, new Modifier(2400));
                }

                foreach (var normalCard in normalCopies)
                {
                    if (normalCard.Type == Card.CType.MINION)
                        p.CastMinionsModifiers.AddOrUpdate(normalCard.Id, new Modifier(380));
                    else if (normalCard.Type == Card.CType.SPELL)
                        p.CastSpellsModifiers.AddOrUpdate(normalCard.Id, new Modifier(380));
                    else if (normalCard.Type == Card.CType.WEAPON)
                        p.CastWeaponsModifiers.AddOrUpdate(normalCard.Id, new Modifier(380));

                    p.PlayOrderModifiers.AddOrUpdate(normalCard.Id, new Modifier(-2400));
                }

                AddLog("临时牌：同名牌命中，优先临时副本 " + group.Key);
            }
        }

        private bool HasTemporaryPlayableAbductionRay(Board board)
        {
            if (board == null || board.Hand == null)
                return false;

            int manaNow = GetAvailableManaIncludingCoin(board);
            return board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == AbductionRay
                && c.CurrentCost <= manaNow
                && IsTemporaryCard(c));
        }

        private Card GetRightmostTemporaryPlayableAbductionRay(Board board, int manaCapInclusive)
        {
            if (board == null || board.Hand == null)
                return null;

            for (int i = board.Hand.Count - 1; i >= 0; i--)
            {
                var c = board.Hand[i];
                if (c == null || c.Template == null)
                    continue;
                if (c.Template.Id != AbductionRay)
                    continue;
                if (c.CurrentCost > manaCapInclusive)
                    continue;
                if (!IsTemporaryCard(c))
                    continue;
                return c;
            }

            return null;
        }

        private bool HasSketchGeneratedAbductionRayWindow(Board board)
        {
            if (board == null || board.Hand == null)
                return false;

            int manaNow = GetAvailableManaIncludingCoin(board);
            return board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == AbductionRay
                && c.CurrentCost <= manaNow
                && IsTrackedTemporaryCard(c));
        }

        private bool IsFullManaTurn(Board board)
        {
            if (board == null)
                return false;
            return Math.Max(0, board.ManaAvailable) >= Math.Max(0, board.MaxMana);
        }

        // 用户硬规则：挟持射线至少第4回合（4费回合）才能使用。
        private bool IsAbductionRayTurnUnlocked(Board board)
        {
            if (board == null)
                return false;
            return Math.Max(0, board.MaxMana) >= 4;
        }

        // 非临时挟持射线“起链”费用窗口：可用费用>=4。
        private bool IsNonTempAbductionRayStartupManaWindow(Board board, int manaNow = -1)
        {
            if (board == null)
                return false;

            if (manaNow < 0)
                manaNow = GetAvailableManaIncludingCoin(board);

            return manaNow >= AbductionRayMinManaNonTemp;
        }

        private bool IsTrackedTemporaryCard(Card card)
        {
            if (card == null)
                return false;
            try { return _temporaryHandEntityIdsThisTurn.Contains(card.Id); }
            catch { return false; }
        }

        private static int? TryGetIntProperty(object obj, params string[] propertyNames)
        {
            try
            {
                if (obj == null || propertyNames == null || propertyNames.Length == 0)
                    return null;
                var t = obj.GetType();
                foreach (var name in propertyNames)
                {
                    try
                    {
                        var p = t.GetProperty(name);
                        if (p == null)
                            continue;
                        var v = p.GetValue(obj, null);
                        if (v == null)
                            continue;
                        if (v is int)
                            return (int)v;
                        if (v is short)
                            return (short)v;
                        if (v is byte)
                            return (byte)v;
                        int parsed;
                        if (int.TryParse(v.ToString(), out parsed))
                            return parsed;
                    }
                    catch { }
                }
            }
            catch { }

            return null;
        }

        private static int GetBoardTurn(Board board)
        {
            try
            {
                var t = TryGetIntProperty(board, "Turn", "TurnNumber", "CurrentTurn", "TurnCount", "GameTurn");
                return t.HasValue ? t.Value : -1;
            }
            catch
            {
                return -1;
            }
        }

        // 某些环境下 Turn 字段会在同回合重算中漂移；用“可用法力是否回满”辅助判断是否进入新回合。
        private static bool IsLikelyTurnStartByMana(Board board)
        {
            if (board == null)
                return false;
            int manaAvailable = 0;
            int maxMana = 0;
            try { manaAvailable = Math.Max(0, board.ManaAvailable); } catch { manaAvailable = 0; }
            try { maxMana = Math.Max(0, board.MaxMana); } catch { maxMana = 0; }
            return maxMana > 0 && manaAvailable >= maxMana;
        }

        private void UpdateInitialDeckCardSnapshot(Board board)
        {
            if (board == null)
                return;

            int turn = GetBoardTurn(board);
            int deckCount = board.FriendDeckCount;

            bool turnReset = (turn >= 0 && _deckTrackLastTurn >= 0 && turn < _deckTrackLastTurn);
            bool deckJumpReset = (_deckTrackLastDeckCount >= 0 && deckCount >= _deckTrackLastDeckCount + 8);
            if (turnReset || deckJumpReset)
            {
                _initialDeckCardCounts.Clear();
                _initialDeckTotalCards = -1;
            }

            bool shouldSnapshot = _initialDeckCardCounts.Count == 0
                                  || (turn >= 0 && turn <= 1 && _initialDeckTotalCards <= 0);
            if (shouldSnapshot)
            {
                var snapshot = new Dictionary<Card.Cards, int>();
                // 某些环境下 board.Deck 是“整副套牌清单”(30张)而非“剩余牌堆”，
                // 此时再叠加手牌会重复计数；仅在 Deck 不像整副清单时才并入手牌。
                bool deckLooksFullList = board.Deck != null
                    && board.Deck.Count > 0
                    && board.FriendDeckCount >= 0
                    && board.Deck.Count > board.FriendDeckCount + 1;

                if (board.Deck != null)
                {
                    foreach (var id in board.Deck)
                    {
                        int old;
                        snapshot.TryGetValue(id, out old);
                        snapshot[id] = old + 1;
                    }
                }

                if (board.Hand != null && (!deckLooksFullList || board.Deck == null || board.Deck.Count == 0))
                {
                    foreach (var c in board.Hand)
                    {
                        if (c == null || c.Template == null)
                            continue;
                        if (c.Template.Id == TheCoin)
                            continue;

                        int old;
                        snapshot.TryGetValue(c.Template.Id, out old);
                        snapshot[c.Template.Id] = old + 1;
                    }
                }

                if (snapshot.Count > 0)
                {
                    _initialDeckCardCounts.Clear();
                    foreach (var kv in snapshot)
                        _initialDeckCardCounts[kv.Key] = kv.Value;

                    _initialDeckTotalCards = snapshot.Values.Sum();
                    AddLog("套外恶魔跟踪：已记录开局牌库快照，基线卡数=" + _initialDeckTotalCards);
                }
            }

            if (turn >= 0)
                _deckTrackLastTurn = turn;
            _deckTrackLastDeckCount = deckCount;
        }

        private void UpdateTemporaryHandTracking(Board board)
        {
            if (board == null)
                return;

            int turn = GetBoardTurn(board);
            int currentTombPlayedCount = CountPlayedCardInLog(board, TombOfSuffering);
            int currentSketchPlayedCount = CountPlayedCardInLog(board, SketchArtist);
            int currentPlatysaurPlayedCount = CountPlayedCardInLog(board, Platysaur);
            int currentRayPlayedCount = CountPlayedCardInLog(board, AbductionRay);
            if (turn != _tempTrackTurn)
            {
                _tempTrackTurn = turn;
                _prevHandEntityIds.Clear();
                _temporaryHandEntityIdsThisTurn.Clear();
                _temporaryRayEntityIdsThisTurn.Clear();
                // 以“当前观测到的来源牌已出次数”作为本回合基线，
                // 仅在本回合新增时才触发临时标记，避免误把常规抽到的同名牌标记为临时。
                _tempTrackTombPlayedCount = currentTombPlayedCount;
                _tempTrackSketchPlayedCount = currentSketchPlayedCount;
                _tempTrackPlatysaurPlayedCount = currentPlatysaurPlayedCount;
                _tempTrackRayPlayedCount = currentRayPlayedCount;
            }

            if (board.Hand == null)
                return;

            var currentHandIds = new HashSet<int>();
            for (int i = 0; i < board.Hand.Count; i++)
            {
                var c = board.Hand[i];
                if (c == null || c.Template == null)
                    continue;
                currentHandIds.Add(c.Id);
            }

            Card.Cards lastPlayed = default(Card.Cards);
            bool hasLastPlayed = false;
            try
            {
                if (board.PlayedCards != null && board.PlayedCards.Count > 0)
                {
                    lastPlayed = board.PlayedCards.Last();
                    hasLastPlayed = true;
                }
            }
            catch { hasLastPlayed = false; }

            bool tombJustPlayed = currentTombPlayedCount > _tempTrackTombPlayedCount;
            bool sketchJustPlayed = currentSketchPlayedCount > _tempTrackSketchPlayedCount;
            bool platysaurJustPlayed = currentPlatysaurPlayedCount > _tempTrackPlatysaurPlayedCount;
            bool rayJustPlayed = currentRayPlayedCount > _tempTrackRayPlayedCount;
            bool trackedTempRayConsumedTransition = _temporaryRayEntityIdsThisTurn.Any(id =>
                _prevHandEntityIds.Contains(id) && !currentHandIds.Contains(id));

            int requiredNewCardsForTempMark = int.MaxValue;
            Card.Cards tempSource = default(Card.Cards);
            bool hasTempSource = false;

            if (hasLastPlayed)
            {
                if (lastPlayed == TombOfSuffering && tombJustPlayed)
                {
                    requiredNewCardsForTempMark = 1;
                    tempSource = TombOfSuffering;
                    hasTempSource = true;
                }
                else if (lastPlayed == SketchArtist && sketchJustPlayed)
                {
                    requiredNewCardsForTempMark = 2;
                    tempSource = SketchArtist;
                    hasTempSource = true;
                }
                else if (lastPlayed == Platysaur && platysaurJustPlayed)
                {
                    requiredNewCardsForTempMark = 1;
                    tempSource = Platysaur;
                    hasTempSource = true;
                }
                else if (lastPlayed == AbductionRay && rayJustPlayed && trackedTempRayConsumedTransition)
                {
                    requiredNewCardsForTempMark = 1;
                    tempSource = AbductionRay;
                    hasTempSource = true;
                }
            }

            if (!hasTempSource)
            {
                if (tombJustPlayed)
                {
                    requiredNewCardsForTempMark = 1;
                    tempSource = TombOfSuffering;
                    hasTempSource = true;
                }
                else if (sketchJustPlayed)
                {
                    requiredNewCardsForTempMark = 2;
                    tempSource = SketchArtist;
                    hasTempSource = true;
                }
                else if (platysaurJustPlayed)
                {
                    requiredNewCardsForTempMark = 1;
                    tempSource = Platysaur;
                    hasTempSource = true;
                }
                else if (rayJustPlayed && trackedTempRayConsumedTransition)
                {
                    requiredNewCardsForTempMark = 1;
                    tempSource = AbductionRay;
                    hasTempSource = true;
                }
            }

            if (hasTempSource && requiredNewCardsForTempMark != int.MaxValue && _prevHandEntityIds.Count > 0)
            {
                var newIds = currentHandIds.Where(id => !_prevHandEntityIds.Contains(id)).ToList();
                if (newIds.Count >= requiredNewCardsForTempMark)
                {
                    Card rightmostNew = null;
                    for (int i = board.Hand.Count - 1; i >= 0; i--)
                    {
                        var c = board.Hand[i];
                        if (c == null || c.Template == null)
                            continue;
                        if (!newIds.Contains(c.Id))
                            continue;
                        rightmostNew = c;
                        break;
                    }

                    if (rightmostNew != null)
                    {
                        bool markAsTemporary = true;

                        // 墓牌专项（用户硬规则）：咒怨之墓选中的牌必定视为临时牌。
                        if (tempSource == TombOfSuffering)
                        {
                            markAsTemporary = true;
                            AddLog("墓选中牌跟踪：右侧新增 " + rightmostNew.Template.Id + " 强制标记为临时");
                        }
                        // 速写专项：最右新增为挟持射线时，按临时牌兜底处理（Tag仅用于日志校验）。
                        else if (tempSource == SketchArtist)
                        {
                            bool isRay = rightmostNew.Template.Id == AbductionRay;
                            bool hasTempTag = false;
                            try { hasTempTag = rightmostNew.HasTag(Card.GAME_TAG.LUNAHIGHLIGHTHINT); }
                            catch { hasTempTag = false; }

                            // 用户硬规则：速写新增到手的挟持射线按临时牌兜底处理（即使Tag缺失）。
                            markAsTemporary = isRay;
                            if (isRay && hasTempTag)
                                AddLog("速写临时牌跟踪：右侧新增 " + rightmostNew.Template.Id + " 命中临时Tag，标记为临时");
                            else if (isRay)
                                AddLog("速写临时牌跟踪：右侧新增挟持射线无临时Tag，按速写兜底标记为临时");
                            else
                                AddLog("速写临时牌跟踪：最右新增非挟持射线，不判定为临时");
                        }
                        // 临时射线链继承：当本回合打出的射线是临时副本时，
                        // 其后新增的右侧射线继续按临时处理，确保可继续连发。
                        else if (tempSource == AbductionRay)
                        {
                            bool isRay = rightmostNew.Template.Id == AbductionRay;
                            markAsTemporary = isRay;
                            if (isRay)
                                AddLog("挟持射线临时链跟踪：右侧新增挟持射线，继承临时标记");
                            else
                                AddLog("挟持射线临时链跟踪：右侧新增非挟持射线，不继承临时标记");
                        }

                        if (markAsTemporary)
                        {
                            _temporaryHandEntityIdsThisTurn.Add(rightmostNew.Id);
                            if (rightmostNew.Template != null && rightmostNew.Template.Id == AbductionRay)
                                _temporaryRayEntityIdsThisTurn.Add(rightmostNew.Id);
                        }
                    }
                }
            }

            _prevHandEntityIds.Clear();
            foreach (var id in currentHandIds)
                _prevHandEntityIds.Add(id);
            _temporaryHandEntityIdsThisTurn.RemoveWhere(id => !currentHandIds.Contains(id));
            _temporaryRayEntityIdsThisTurn.RemoveWhere(id => !currentHandIds.Contains(id));

            _tempTrackTombPlayedCount = currentTombPlayedCount;
            _tempTrackSketchPlayedCount = currentSketchPlayedCount;
            _tempTrackPlatysaurPlayedCount = currentPlatysaurPlayedCount;
            _tempTrackRayPlayedCount = currentRayPlayedCount;
        }

        private bool IsAbductionRayContinuationThisTurn(Board board)
        {
            if (board == null || board.Hand == null)
                return false;

            int manaNow = GetAvailableManaIncludingCoin(board);
            if (!HasPlayableAbductionRayInHand(board, manaNow))
                return false;

            // 用户修正：本回合已起射线后，临时射线窗口强续链；
            // 非临时射线在不高爆牌风险时也允许续打，避免中途断链。
            if (!HasStartedOrLastPlayedAbductionRayThisTurn(board))
                return false;

            bool tempContinuationWindow = HasTemporaryOrSketchGeneratedAbductionRay(board);
            if (tempContinuationWindow)
                return true;

            int playableRays = board.Hand.Count(c => c != null
                && c.Template != null
                && c.Template.Id == AbductionRay
                && c.CurrentCost <= manaNow);
            if (playableRays <= 0)
                return false;

            return !IsAbductionRayOverdrawRiskHigh(board, false);
        }

        private int GetMaxPlayableOutsideDeckDemonsThisTurn(Board board, int manaNow = -1)
        {
            if (board == null || board.Hand == null)
                return 0;

            if (manaNow < 0)
                manaNow = GetAvailableManaIncludingCoin(board);
            if (manaNow <= 0)
                return 0;

            int freeSlots = GetFreeBoardSlots(board);
            if (freeSlots <= 0)
                return 0;

            var costs = board.Hand
                .Where(c => c != null
                    && c.Template != null
                    && c.Type == Card.CType.MINION
                    && c.IsRace(Card.CRace.DEMON)
                    && c.CurrentCost <= manaNow
                    && IsOutsideDeckDemonByDeckList(board, c.Template.Id))
                .Select(c => Math.Max(0, c.CurrentCost))
                .OrderBy(cost => cost)
                .ToList();
            if (costs.Count == 0)
                return 0;

            int manaLeft = manaNow;
            int playable = 0;
            foreach (var cost in costs)
            {
                if (playable >= freeSlots)
                    break;
                if (cost > manaLeft)
                    continue;

                manaLeft -= cost;
                playable++;
            }

            return playable;
        }

        private bool ShouldDelayAbductionRayStartupForOutsideDeckDemons(Board board, int manaNow = -1)
        {
            if (board == null || board.Hand == null)
                return false;

            if (manaNow < 0)
                manaNow = GetAvailableManaIncludingCoin(board);
            if (manaNow <= 0)
                return false;
            if (board.Hand.Count < AbductionRayOutsideDemonDelayMinHand)
                return false;
            if (!HasPlayableAbductionRayInHand(board, manaNow))
                return false;
            if (HasStartedOrLastPlayedAbductionRayThisTurn(board))
                return false;
            if (HasTemporaryOrSketchGeneratedAbductionRay(board))
                return false;

            int playableOutsideDeckDemons = GetMaxPlayableOutsideDeckDemonsThisTurn(board, manaNow);
            int outsideDeckDemonsInHand = CountOutsideDeckDemonsInHand(board);
            int minPlayable = GetAbductionRayOutsideDemonDelayMinPlayable(board);
            bool handHeavyOutsideDeckWindow = board.Hand.Count >= 7
                && outsideDeckDemonsInHand >= 2
                && playableOutsideDeckDemons > 0;
            if (handHeavyOutsideDeckWindow)
                return true;

            return playableOutsideDeckDemons >= minPlayable;
        }

        private int GetAbductionRayOutsideDemonDelayMinPlayable(Board board)
        {
            if (board == null)
                return AbductionRayOutsideDemonDelayMinPlayable;

            int friendHp = GetHeroHealth(board.HeroFriend);
            int enemyHp = GetHeroHealth(board.HeroEnemy);
            int friendAttack = GetBoardAttack(board.MinionFriend);
            int enemyAttack = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            bool highPressureWindow = IsDangerousHpTauntTempoWindow(friendHp, enemyAttack)
                || ShouldEnterBalancedDefenseMode(board, friendHp, enemyHp)
                || ShouldPrioritizeRushMinionClear(board, friendHp, enemyHp, friendAttack, enemyAttack);
            return highPressureWindow
                ? AbductionRayOutsideDemonDelayMinPlayableUnderPressure
                : AbductionRayOutsideDemonDelayMinPlayable;
        }

        private bool ShouldDelayAbductionRayForPlayableOutsideDeckTaunt(Board board, int manaNow = -1)
        {
            if (board == null || board.Hand == null)
                return false;

            if (manaNow < 0)
                manaNow = GetAvailableManaIncludingCoin(board);
            if (manaNow <= 0)
                return false;
            if (!HasPlayableAbductionRayInHand(board, manaNow))
                return false;

            return GetPlayableOutsideDeckTauntMinionCardIds(board, manaNow).Count > 0;
        }

        private bool ShouldForceAbductionRayNow(Board board)
        {
            if (!IsAbductionRayPlayableNow(board))
                return false;
            AbductionRayStartupState startupState = EvaluateAbductionRayStartupState(board);
            int manaNow = startupState.ManaNow;
            bool tempLikeWindow = startupState.TempLikeWindow;
            if (!tempLikeWindow && ShouldPreferKiljaedenImmediateOverRayChain(board, manaNow))
                return false;
            if (ShouldDelayAbductionRayForPlayableOutsideDeckTaunt(board, manaNow))
                return false;
            bool rayStartedThisTurn = startupState.RayStartedThisTurn;
            if (rayStartedThisTurn)
            {
                // 用户修正：本回合起过射线且仍有可打射线时，续链锁定优先于爆牌顾虑；
                // 若需腾手牌，交给HandleAbductionRay内部的“0费放行”分支处理。
                bool hasPlayableRayInHandNow = startupState.PlayableRayCount > 0;
                if (!hasPlayableRayInHandNow)
                    return false;
                return true;
            }

            bool slitherspearLethalWindow = IsAbductionRaySlitherspearLethalWindow(board, manaNow);
            if (slitherspearLethalWindow)
                return true;
            if (startupState.DelayStartupForOutsideDeckDemonsWindow)
                return false;

            int rayStartupHandCount = startupState.RayStartupHandCount;
            bool allowNonTempResourceWindow = startupState.AllowNonTempResourceWindow;
            bool continuationWindow = IsAbductionRayContinuationOrChainWindow(board);
            bool emergencySurvivalWindow = IsAbductionRayEmergencySurvivalWindow(board, manaNow);
            bool handFullWithZeroCostMinionWindow = startupState.HandFullWithZeroCostMinionWindow;
            bool midLateMultiRayWindow = startupState.MidLateMultiRayWindow;
            int stageMana = board != null ? Math.Max(0, board.MaxMana) : 0;
            bool fullManaPriorityWindow = IsFullManaTurn(board) && stageMana >= 3;
            bool nonTempStartupManaWindow = startupState.NonTempStartupManaWindow;
            bool partyEmergencyWindow = ShouldPrioritizePartyFiendNow(board, manaNow);
            bool forebodingPlayableNow = board != null && board.Hand != null && board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == ForebodingFlame
                && c.CurrentCost <= manaNow);
            int doomguardInHand = board != null && board.Hand != null
                ? board.Hand.Count(c => c != null && c.Template != null && c.Template.Id == Doomguard)
                : 0;
            int enemyAttack = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            bool doomguardCloggedHandWindow = doomguardInHand > 0
                && board != null
                && board.Hand != null
                && rayStartupHandCount >= 1
                && !tempLikeWindow
                && nonTempStartupManaWindow
                && (doomguardInHand >= 2 || enemyAttack >= 6);
            // 连发窗口命中后维持高优先；但是否可打仍受“非临时可用费>=4”硬门槛约束。
            if (continuationWindow)
                return true;
            if (emergencySurvivalWindow)
                return true;
            if (midLateMultiRayWindow)
                return true;
            if (doomguardCloggedHandWindow)
                return true;

            // 用户规则：非临时射线统一受费用窗口（可用费>=4）约束。
            if (!tempLikeWindow && !nonTempStartupManaWindow)
                return false;
            bool hasPlayableTempoMinion = HasPlayableMinionWithBoardSpace(board, manaNow);
            bool handRichAndTempoMinionWindow = board != null && board.Hand != null
                && rayStartupHandCount > AbductionRayNonTempPreferredMaxHand
                && hasPlayableTempoMinion;
            bool sketchPlayableNow = board != null && board.Hand != null
                && manaNow >= 4
                && GetFreeBoardSlots(board) > 0
                && board.Hand.Any(c => c != null && c.Template != null
                    && c.Template.Id == SketchArtist
                    && c.CurrentCost <= manaNow);
            bool preferSketchFirstWindow = sketchPlayableNow
                && board != null
                && board.Hand != null
                && rayStartupHandCount > AbductionRayNonTempPreferredMaxHand
                && !tempLikeWindow
                && !continuationWindow
                && !HasPlayedCard(board, AbductionRay);
            bool sketchFirstAtFourToSixWithRayWindow = sketchPlayableNow
                && stageMana >= 4
                && stageMana <= 6
                && !tempLikeWindow
                && !continuationWindow
                && !HasPlayedCard(board, AbductionRay);

            if (handFullWithZeroCostMinionWindow)
                return false;

            if (partyEmergencyWindow && !tempLikeWindow)
                return false;
            if (forebodingPlayableNow && !tempLikeWindow)
                return false;
            if (!tempLikeWindow && !allowNonTempResourceWindow)
                return false;
            if (IsAbductionRayOverdrawRiskHigh(board, tempLikeWindow || fullManaPriorityWindow))
                return false;
            if (handRichAndTempoMinionWindow && !fullManaPriorityWindow && !tempLikeWindow)
                return false;
            if (tempLikeWindow)
                return true;
            if (sketchFirstAtFourToSixWithRayWindow)
                return false;
            if (preferSketchFirstWindow)
                return false;

            // 用户新规则：非临时牌时，仅在手牌<=5且满足起链费用窗口（可用费>=4）时强推射线。
            return nonTempStartupManaWindow && allowNonTempResourceWindow;
        }

        private int GetAbductionRayStartupEffectiveHandCount(Board board)
        {
            if (board == null || board.Hand == null)
                return 0;

            return board.Hand.Count(c => c != null
                && c.Template != null
                && c.Template.Id != TheCoin
                && c.Template.Id != Archimonde
                && c.Template.Id != Kiljaeden
                && c.Template.Id != Doomguard);
        }

        private bool ShouldHoldForStorytellerBuff(Board board)
        {
            if (board == null || board.MinionFriend == null || board.MinionEnemy == null)
                return false;

            bool storytellerOnBoard = board.MinionFriend.Any(m => m != null && m.Template != null && m.Template.Id == TortollanStoryteller);
            if (!storytellerOnBoard)
                return false;

            if (board.MinionEnemy.Count > 0)
                return false;

            // 需要有一定场面规模才值得“停手吃buff”。
            if (board.MinionFriend.Count < 3)
                return false;

            return true;
        }

        private bool ShouldChainAbductionRayNow(Board board)
        {
            if (board == null || board.Hand == null)
                return false;
            if (!IsAbductionRayPlayableNow(board))
                return false;

            AbductionRayStartupState startupState = EvaluateAbductionRayStartupState(board);
            int manaNow = startupState.ManaNow;
            bool tempLikeWindow = startupState.TempLikeWindow;
            if (!tempLikeWindow && ShouldPreferKiljaedenImmediateOverRayChain(board, manaNow))
                return false;
            if (ShouldDelayAbductionRayForPlayableOutsideDeckTaunt(board, manaNow))
                return false;
            bool allowNonTempResourceWindow = startupState.AllowNonTempResourceWindow;
            bool rayStartedThisTurn = startupState.RayStartedThisTurn;
            bool midLateMultiRayWindow = startupState.MidLateMultiRayWindow;
            if (startupState.DelayStartupForOutsideDeckDemonsWindow)
                return false;
            if (startupState.HandFullWithZeroCostMinionWindow)
                return false;
            bool chainStartupWindow = !rayStartedThisTurn;
            bool nonTempStartupManaWindow = startupState.NonTempStartupManaWindow;
            if (chainStartupWindow && !nonTempStartupManaWindow && !midLateMultiRayWindow)
                return false;
            int playableRays = startupState.PlayableRayCount;
            if (rayStartedThisTurn)
            {
                // 本回合已起射线：优先续到底，避免被阿克蒙德/分流/低费随从插队打断。
                return playableRays >= 1;
            }

            if (playableRays >= 2)
                return allowNonTempResourceWindow || midLateMultiRayWindow;

            return false;
        }

        // 用户规则：中后期手里有2张及以上可打挟持射线时，允许直接进入射线连发窗口。
        private bool IsAbductionRayMidLateMultiRayPriorityWindow(Board board, int manaNow = -1)
        {
            if (board == null || board.Hand == null)
                return false;

            if (manaNow < 0)
                manaNow = GetAvailableManaIncludingCoin(board);
            if (manaNow <= 0)
                return false;

            int stageMana = Math.Max(0, board.MaxMana);
            if (stageMana < 5)
                return false;

            int playableRays = board.Hand.Count(c => c != null
                && c.Template != null
                && c.Template.Id == AbductionRay
                && c.CurrentCost <= manaNow);
            if (playableRays < 2)
                return false;

            // 手满且有0费可下时，仍然遵守“先放行0费再续射线”。
            if (board.Hand.Count >= HearthstoneHandLimit
                && GetRightmostPlayableZeroCostNonRayMinion(board, manaNow) != null)
                return false;

            int friendHp = GetHeroHealth(board.HeroFriend);
            int enemyAttack = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            bool dangerousHpWindow = IsDangerousHpTauntTempoWindow(friendHp, enemyAttack);
            bool hasPlayableTauntMinionNow = GetPlayableTauntMinionCardIds(board, manaNow).Count > 0;
            if (dangerousHpWindow && hasPlayableTauntMinionNow)
                return false;
            if (GetPlayableOutsideDeckTauntMinionCardIds(board, manaNow).Count > 0)
                return false;

            if (IsAbductionRayOverdrawRiskHigh(board, false))
                return false;

            return true;
        }

        private bool HasPlayableLowCostMinion(Board board, int maxCost)
        {
            if (board == null || board.Hand == null)
                return false;
            if (GetFreeBoardSlots(board) <= 0)
                return false;

            int manaNow = GetAvailableManaIncludingCoin(board);
            return board.Hand.Any(c => c != null && c.Template != null
                && c.Type == Card.CType.MINION
                && c.CurrentCost <= manaNow
                && c.CurrentCost <= maxCost);
        }

        private bool HasPlayableRushMinionForClear(Board board, int manaNow)
        {
            if (board == null || board.Hand == null)
                return false;
            if (board.MinionEnemy == null || board.MinionEnemy.Count == 0)
                return false;
            if (GetFreeBoardSlots(board) <= 0)
                return false;

            return board.Hand.Any(c => c != null
                && c.Template != null
                && c.Type == Card.CType.MINION
                && c.CurrentCost <= manaNow
                && IsRushMinionForClear(c));
        }

        private List<Card.Cards> GetPlayableOutsideDeckRushMinionCardIdsForClear(Board board, int manaNow)
        {
            if (board == null || board.Hand == null)
                return new List<Card.Cards>();
            if (board.MinionEnemy == null || board.MinionEnemy.Count == 0)
                return new List<Card.Cards>();
            if (GetFreeBoardSlots(board) <= 0)
                return new List<Card.Cards>();
            if (manaNow <= 0)
                return new List<Card.Cards>();

            return board.Hand
                .Where(c => c != null
                    && c.Template != null
                    && c.Type == Card.CType.MINION
                    && c.CurrentCost <= manaNow
                    && c.IsRace(Card.CRace.DEMON)
                    && IsOutsideDeckDemonByDeckList(board, c.Template.Id)
                    && IsRushMinionForClear(c))
                .Select(c => c.Template.Id)
                .Distinct()
                .ToList();
        }

        private List<Card.Cards> GetPlayableOutsideDeckTauntMinionCardIds(Board board, int manaNow)
        {
            if (board == null || board.Hand == null)
                return new List<Card.Cards>();
            if (GetFreeBoardSlots(board) <= 0)
                return new List<Card.Cards>();
            if (manaNow <= 0)
                return new List<Card.Cards>();

            return board.Hand
                .Where(c => c != null
                    && c.Template != null
                    && c.Type == Card.CType.MINION
                    && c.CurrentCost <= manaNow
                    && c.IsRace(Card.CRace.DEMON)
                    && IsOutsideDeckDemonByDeckList(board, c.Template.Id)
                    && IsTauntMinionForSurvival(c))
                .Select(c => c.Template.Id)
                .Distinct()
                .ToList();
        }

        private bool IsRushMinionForClear(Card card)
        {
            if (card == null || card.Template == null || card.Type != Card.CType.MINION)
                return false;

            string cardId = card.Template.Id.ToString();
            // 已确认突袭：狂飙邪魔（VAC_927）、伊利达雷审判官（CS3_020）、
            // 纵火眼魔（EDR_486）、精魂商贩（WORK_015）。
            return string.Equals(cardId, "VAC_927", StringComparison.Ordinal)
                || string.Equals(cardId, "CS3_020", StringComparison.Ordinal)
                || string.Equals(cardId, ArsonEyedDemonCardId, StringComparison.Ordinal)
                || string.Equals(cardId, SoulDealerCardId, StringComparison.Ordinal);
        }

        private bool ShouldPrioritizeRushMinionClear(Board board, int friendHp, int enemyHp, int friendAttack, int enemyAttack)
        {
            if (board == null || board.MinionEnemy == null || board.MinionEnemy.Count == 0)
                return false;

            bool pressureWindow = ShouldEnterBalancedDefenseMode(board, friendHp, enemyHp)
                || (friendHp <= 12 && enemyAttack >= 8);
            bool enemyAttackAdvantage = enemyAttack >= friendAttack + 2;
            bool enemyThreatPresent = board.MinionEnemy.Any(m => m != null
                && (m.IsTaunt || m.CurrentAtk >= 4 || m.CurrentHealth >= 6));

            return enemyThreatPresent && (pressureWindow || enemyAttackAdvantage);
        }

        // 用户规则：挟持射线链路/保命窗口优先级高于影焰猎豹，避免猎豹抢先占费断链。
        private bool ShouldDelayShadowflameForAbductionRay(Board board, int manaNow = -1)
        {
            if (board == null || board.Hand == null)
                return false;
            if (manaNow < 0)
                manaNow = GetAvailableManaIncludingCoin(board);
            AbductionRayStartupState startupState = EvaluateAbductionRayStartupState(board, manaNow);
            if (startupState.PlayableRayCount <= 0)
                return false;

            bool rayHardLockWindow = ShouldForceAbductionRayNow(board);
            bool emergencyRayWindow = IsAbductionRayEmergencySurvivalWindow(board, manaNow);

            int friendHp = GetHeroHealth(board.HeroFriend);
            int enemyAttack = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            bool enemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            bool pressureTauntWindow = enemyHasTaunt && (friendHp <= 12 || enemyAttack >= 8);

            return HasAbductionRayPriorityWindow(board, startupState, rayHardLockWindow)
                || emergencyRayWindow
                || pressureTauntWindow;
        }

        private bool ShouldPrioritizePartyFiendNow(Board board, int manaNow)
        {
            if (board == null || board.Hand == null)
                return false;
            if (GetFreeBoardSlots(board) < 3)
                return false;

            bool partyPlayable = board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == PartyFiend
                && c.CurrentCost <= manaNow);
            if (!partyPlayable)
                return false;

            int friendHp = GetHeroHealth(board.HeroFriend);
            // 派对邪犬会自伤3：危险血线窗口允许用来补场，但至少要保留到>2血。
            if (friendHp <= 5)
                return false;

            int enemyAttack = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            bool enemyHasBoard = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null);
            if (!enemyHasBoard)
                return false;

            bool dangerousWindow = friendHp <= 8 || (friendHp <= 12 && enemyAttack >= 8);
            return dangerousWindow;
        }

        private bool IsAbductionRayEmergencySurvivalWindow(Board board, int manaNow = -1)
        {
            if (board == null || board.Hand == null || board.MinionEnemy == null)
                return false;
            if (board.MinionEnemy.Count == 0)
                return false;
            if (manaNow < 0)
                manaNow = GetAvailableManaIncludingCoin(board);
            AbductionRayStartupState startupState = EvaluateAbductionRayStartupState(board, manaNow);
            if (startupState.PlayableRayCount <= 0)
                return false;
            if (startupState.RayStartedThisTurn)
                return false;
            if (GetFreeBoardSlots(board) <= 0)
                return false;
            if (GetPlayableTauntMinionCardIds(board, manaNow).Count > 0)
                return false;

            int friendHp = GetHeroHealth(board.HeroFriend);
            int enemyAttack = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            int burstReserve = GetEnemyBurstReserveByClass(board.EnemyClass);
            int twoTurnPressure = enemyAttack * 2 + burstReserve;

            bool lethalSoon = enemyAttack >= Math.Max(1, friendHp - 1);
            bool lowHpHighPressure = friendHp <= 12 && enemyAttack >= 8;
            bool twoTurnCollapse = friendHp <= 14 && friendHp <= twoTurnPressure;

            return lethalSoon || lowHpHighPressure || twoTurnCollapse;
        }

        private bool HasPlayableMinionWithBoardSpace(Board board, int manaNow)
        {
            if (board == null || board.Hand == null)
                return false;
            if (GetFreeBoardSlots(board) <= 0)
                return false;

            return board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id != TheCoin
                && c.Type == Card.CType.MINION
                && c.CurrentCost <= manaNow);
        }

        private bool HasPlayableMinionWithBoardSpaceExcluding(Board board, int manaNow, Card.Cards excluded)
        {
            if (board == null || board.Hand == null)
                return false;
            if (GetFreeBoardSlots(board) <= 0)
                return false;

            return board.Hand.Any(c => c != null && c.Template != null
                && c.Type == Card.CType.MINION
                && c.Template.Id != excluded
                && c.CurrentCost <= manaNow);
        }

        private bool IsArchimondePlayableNow(Board board, int manaNow = -1)
        {
            if (board == null || board.Hand == null)
                return false;
            if (GetFreeBoardSlots(board) <= 0)
                return false;

            if (manaNow < 0)
                manaNow = GetAvailableManaIncludingCoin(board);

            return board.Hand.Any(c => c != null && c.Template != null
                && c.Type == Card.CType.MINION
                && c.Template.Id == Archimonde
                && c.CurrentCost <= manaNow);
        }

        private bool HasPlayableSpecificMinion(Board board, Card.Cards target, int manaNow)
        {
            if (board == null || board.Hand == null)
                return false;
            if (GetFreeBoardSlots(board) <= 0)
                return false;

            return board.Hand.Any(c => c != null && c.Template != null
                && c.Type == Card.CType.MINION
                && c.Template.Id == target
                && c.CurrentCost <= manaNow);
        }

        private int GetFreeBoardSlots(Board board)
        {
            int friendCount = 0;
            try { friendCount = board != null && board.MinionFriend != null ? board.MinionFriend.Count : 0; }
            catch { friendCount = 0; }

            int slots = 7 - friendCount;
            return slots < 0 ? 0 : slots;
        }

        // 用户规则：危险血线+场压窗口统一口径（用于“嘲讽稳场优先”判断）。
        // 覆盖典型 case：13血/敌方11攻，也要进入嘲讽优先分支。
        private bool IsDangerousHpTauntTempoWindow(int friendHp, int enemyAttack)
        {
            if (friendHp <= 8)
                return true;
            if (friendHp <= 12 && enemyAttack >= 8)
                return true;
            if (friendHp <= 14 && enemyAttack >= 10)
                return true;
            if (friendHp <= 16 && enemyAttack >= 12)
                return true;
            return false;
        }

        private int GetMaxAdditionalBodiesBeforeEntropy(Board board, int manaBudget)
        {
            if (board == null || board.Hand == null)
                return 0;
            if (manaBudget < 0)
                return 0;

            int freeSlots = GetFreeBoardSlots(board);
            if (freeSlots <= 0)
                return 0;

            var candidates = board.Hand
                .Where(c => c != null
                    && c.Template != null
                    && c.Type == Card.CType.MINION
                    && c.Template.Id != EntropicContinuity
                    && c.CurrentCost <= manaBudget)
                .ToList();
            if (candidates.Count == 0)
                return 0;

            // 0/1 背包：在法力预算内最大化“可新增随从体数”（邪犬按3体估算）。
            var dp = new int[manaBudget + 1];
            foreach (var card in candidates)
            {
                int cost = Math.Max(0, card.CurrentCost);
                int bodies = card.Template.Id == PartyFiend ? 3 : 1;
                for (int m = manaBudget; m >= cost; m--)
                {
                    int candidateBodies = dp[m - cost] + bodies;
                    if (candidateBodies > freeSlots)
                        candidateBodies = freeSlots;
                    if (candidateBodies > dp[m])
                        dp[m] = candidateBodies;
                }
            }

            int best = 0;
            for (int m = 0; m <= manaBudget; m++)
            {
                if (dp[m] > best)
                    best = dp[m];
            }

            if (best > freeSlots)
                best = freeSlots;
            return best;
        }

        private int GetMaxAdditionalBodiesWithBudgetExcludingCard(Board board, int manaBudget, Card.Cards excludedCard)
        {
            if (board == null || board.Hand == null)
                return 0;
            if (manaBudget < 0)
                return 0;

            int freeSlots = GetFreeBoardSlots(board);
            if (freeSlots <= 0)
                return 0;

            var candidates = board.Hand
                .Where(c => c != null
                    && c.Template != null
                    && c.Type == Card.CType.MINION
                    && c.Template.Id != excludedCard
                    && c.CurrentCost <= manaBudget)
                .ToList();
            if (candidates.Count == 0)
                return 0;

            var dp = new int[manaBudget + 1];
            foreach (var card in candidates)
            {
                int cost = Math.Max(0, card.CurrentCost);
                int bodies = card.Template.Id == PartyFiend ? 3 : 1;
                for (int m = manaBudget; m >= cost; m--)
                {
                    int candidateBodies = dp[m - cost] + bodies;
                    if (candidateBodies > freeSlots)
                        candidateBodies = freeSlots;
                    if (candidateBodies > dp[m])
                        dp[m] = candidateBodies;
                }
            }

            int best = 0;
            for (int m = 0; m <= manaBudget; m++)
            {
                if (dp[m] > best)
                    best = dp[m];
            }

            if (best > freeSlots)
                best = freeSlots;
            return best;
        }

        private bool IsAbductionRayOverdrawRiskHigh(Board board, bool allowToTenForTempOrChain = false)
        {
            if (board == null || board.Hand == null)
                return false;

            // 用户规则：临时/连发窗口下，允许手牌打到10（挟持射线本体会先离手再补牌）。
            if (allowToTenForTempOrChain)
                return false;

            // 保守估计：挟持射线本次动作可能净增 2 张资源（发现 + 额外生成）。
            int freeSlots = HearthstoneHandLimit - board.Hand.Count;
            return freeSlots < 2;
        }

        private bool IsAbductionRayOverdrawRiskMedium(Board board, bool allowToTenForTempOrChain = false)
        {
            if (board == null || board.Hand == null)
                return false;

            int freeSlots = HearthstoneHandLimit - board.Hand.Count;
            return allowToTenForTempOrChain ? freeSlots == 1 : freeSlots == 2;
        }

        private bool ShouldPreserveBoardForStorytellerCombat(Board board)
        {
            if (board == null || board.MinionFriend == null || board.MinionEnemy == null)
                return false;

            bool storytellerOnBoard = board.MinionFriend.Any(m => m != null && m.Template != null && m.Template.Id == TortollanStoryteller);
            if (!storytellerOnBoard)
                return false;

            bool enemyHasTaunt = board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            int attackableFriendCount = board.MinionFriend.Count(m => m != null && m.CanAttack && m.CurrentAtk > 0);
            bool canClearAllTauntsNow = enemyHasTaunt
                && CanClearAllEnemyTauntsWithFewAttackers(board, Math.Max(1, attackableFriendCount));
            bool preserveByNoTauntFace = !enemyHasTaunt && board.MinionFriend.Count >= 2;
            bool preserveByTauntHold = enemyHasTaunt && board.MinionFriend.Count >= 3 && !canClearAllTauntsNow;
            if (!preserveByNoTauntFace && !preserveByTauntHold)
                return false;

            int friendHp = GetHeroHealth(board.HeroFriend);
            int enemyAttack = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            bool dangerousHpWindow = IsDangerousHpTauntTempoWindow(friendHp, enemyAttack);
            if (dangerousHpWindow)
                return false;

            // 血线安全时优先“停手保场”吃始祖龟buff；若有嘲讽，要求更高安全边际。
            int preserveMargin = enemyHasTaunt ? 7 : 6;
            return friendHp >= enemyAttack + preserveMargin;
        }

        private bool CanClearAnyEnemyTauntWithFewAttackers(Board board, int maxAttackers)
        {
            if (board == null || board.MinionFriend == null || board.MinionEnemy == null)
                return false;
            if (maxAttackers <= 0)
                return false;

            var attackers = board.MinionFriend
                .Where(m => m != null && m.Template != null && m.CanAttack && m.CurrentAtk > 0)
                .OrderByDescending(m => m.CurrentAtk)
                .ToList();
            if (attackers.Count == 0)
                return false;

            foreach (var taunt in board.MinionEnemy.Where(m => m != null && m.IsTaunt))
            {
                int hpLeft = Math.Max(1, taunt.CurrentHealth);
                int used = 0;
                foreach (var a in attackers)
                {
                    if (used >= maxAttackers)
                        break;
                    hpLeft -= Math.Max(0, a.CurrentAtk);
                    used++;
                    if (hpLeft <= 0)
                        return true;
                }
            }

            return false;
        }

        private bool IsEnemyLikelyKilledByFriendlyAttacksThisTurn(Board board, Card enemy)
        {
            if (board == null || enemy == null || board.MinionFriend == null || board.MinionEnemy == null)
                return false;
            if (enemy.CurrentHealth <= 0 || enemy.IsDivineShield)
                return false;

            var attacks = board.MinionFriend
                .Where(m => m != null && m.Template != null && m.CanAttack && m.CurrentAtk > 0)
                .Select(m => Math.Max(0, m.CurrentAtk))
                .OrderByDescending(v => v)
                .ToList();
            if (attacks.Count == 0)
                return false;

            int totalAttack = attacks.Sum();
            if (totalAttack < enemy.CurrentHealth)
                return false;

            bool enemyHasTaunt = board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            if (enemy.IsTaunt)
                return true;

            if (!enemyHasTaunt)
                return true;

            int attackableFriendCount = attacks.Count;
            if (!CanClearAllEnemyTauntsWithFewAttackers(board, Math.Max(1, attackableFriendCount)))
                return false;

            int tauntHpSum = board.MinionEnemy
                .Where(m => m != null && m.IsTaunt)
                .Sum(m => Math.Max(1, m.CurrentHealth));
            return (totalAttack - tauntHpSum) >= enemy.CurrentHealth;
        }

        // 用户规则：仅当“本回合可低损清掉全部嘲讽”时，才允许判定为可先解后续动作。
        // 防止出现“能清一个薄嘲讽”就误判为可解嘲讽，导致关键链路（如挟持射线）被错误后置。
        private bool CanClearAllEnemyTauntsWithFewAttackers(Board board, int maxAttackers)
        {
            if (board == null || board.MinionFriend == null || board.MinionEnemy == null)
                return false;
            if (maxAttackers <= 0)
                return false;

            var taunts = board.MinionEnemy
                .Where(m => m != null && m.IsTaunt)
                .Select(m => Math.Max(1, m.CurrentHealth))
                .ToList();
            if (taunts.Count == 0)
                return false;
            if (taunts.Count > maxAttackers)
                return false;

            var attacks = board.MinionFriend
                .Where(m => m != null && m.Template != null && m.CanAttack && m.CurrentAtk > 0)
                .Select(m => Math.Max(0, m.CurrentAtk))
                .OrderByDescending(v => v)
                .ToList();
            if (attacks.Count == 0)
                return false;

            if (taunts.Count == 1)
            {
                int hp = taunts[0];
                if (attacks.Any(a => a >= hp))
                    return true;

                int total = 0;
                int used = 0;
                foreach (var atk in attacks)
                {
                    if (used >= maxAttackers)
                        break;
                    total += atk;
                    used++;
                    if (total >= hp)
                        return true;
                }
                return false;
            }

            // taunts.Count == 2 且 maxAttackers >= 2：尝试两两分配到两个嘲讽。
            int hp1 = taunts[0];
            int hp2 = taunts[1];
            for (int i = 0; i < attacks.Count; i++)
            {
                for (int j = i + 1; j < attacks.Count; j++)
                {
                    int a = attacks[i];
                    int b = attacks[j];
                    if ((a >= hp1 && b >= hp2) || (a >= hp2 && b >= hp1))
                        return true;
                }
            }

            return false;
        }

        private bool TryForceBuffComboBeforeAttacks(Board board, ProfileParameters p, int enemyHp)
        {
            if (board == null || board.Hand == null || p == null)
                return false;

            int manaNow = GetAvailableManaIncludingCoin(board);
            if (manaNow <= 0)
                return false;

            bool hasAttackableFriend = board.MinionFriend != null
                && board.MinionFriend.Any(m => m != null && m.CanAttack && m.CurrentAtk > 0);
            if (!hasAttackableFriend)
                return false;

            bool sketchPlayableNow = board.HasCardInHand(SketchArtist)
                && manaNow >= 4
                && GetFreeBoardSlots(board) > 0
                && board.Hand.Any(c => c != null && c.Template != null
                    && c.Template.Id == SketchArtist
                    && c.CurrentCost <= manaNow);
            if (sketchPlayableNow)
            {
                AddLog("攻击顺序：速写美术家可用，攻前buff链让位速写先手");
                return false;
            }

            bool enemyHasTaunt = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            int canAttackDamageNow = board.MinionFriend
                .Where(m => m != null && m.CanAttack)
                .Sum(m => Math.Max(0, m.CurrentAtk));
            if (!enemyHasTaunt && canAttackDamageNow >= enemyHp)
                return false;

            // 用户新增规则：命中“攻前墓探buff”窗口时，先墓再攻击（优先级高于0费随从先手）。
            if (ShouldForceTombBuffScoutBeforeAttack(board, manaNow, enemyHp))
            {
                ForceTombFirstThisTurn(board, p, manaNow);
                SetSingleCardCombo(board, p, TombOfSuffering, allowCoinBridge: false, forceOverride: true,
                    logWhenSet: "攻击顺序：ComboSet先咒怨之墓探buff后攻击");
                AddLog("攻击顺序：命中攻前墓探buff窗口，先墓看buff再攻击");
                return true;
            }

            // 用户规则：有可下0费随从时，先下随从（优先狂飙邪魔）再攻击。
            var zeroCostReleaseBeforeAttack = GetRightmostPlayableZeroCostNonRayMinion(board, manaNow);
            if (zeroCostReleaseBeforeAttack != null)
            {
                bool isRushDemon = string.Equals(zeroCostReleaseBeforeAttack.Template.Id.ToString(), "VAC_927", StringComparison.Ordinal);
                bool setZeroFirst = SetSingleCardComboByEntityId(board, p, zeroCostReleaseBeforeAttack.Id,
                    allowCoinBridge: true, forceOverride: true,
                    logWhenSet: isRushDemon
                        ? "攻击顺序：先下狂飙邪魔后攻击"
                        : "攻击顺序：先下0费随从后攻击");
                if (setZeroFirst)
                {
                    AddLog(isRushDemon
                        ? "攻击顺序：有可下0费随从，优先先下狂飙邪魔再攻击"
                        : "攻击顺序：有可下0费随从，先下0费随从再攻击");
                    return true;
                }
            }

            // 用户规则：墓回合先墓，攻击后置。
            if (ShouldForceTombFirstThisTurn(board))
            {
                ForceTombFirstThisTurn(board, p, manaNow);
                SetSingleCardCombo(board, p, TombOfSuffering, allowCoinBridge: false, forceOverride: true,
                    logWhenSet: "攻击顺序：ComboSet先咒怨之墓后攻击");
                AddLog("攻击顺序：咒怨之墓回合，先墓再攻击");
                return true;
            }

            // 1) 月度魔范员工：先贴吸血再攻击。
            bool monthlyLifestealWindow = board.MinionFriend != null
                && board.MinionFriend.Any(m => m != null)
                && board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == MonthlyModelEmployee
                && c.CurrentCost <= manaNow)
                && GetFreeBoardSlots(board) > 0;
            if (monthlyLifestealWindow)
            {
                bool forced = SetSingleCardCombo(board, p, MonthlyModelEmployee, allowCoinBridge: true, forceOverride: true,
                    logWhenSet: "攻击顺序：ComboSet先贴吸血后攻击");
                if (forced)
                {
                    AddLog("攻击顺序：已强制先贴吸血，再执行攻击");
                    return true;
                }
            }

            // 1.5) 香蕉：有敌方随从时，优先先贴buff再攻击，避免“先攻后香蕉”亏交换。
            bool bananaBuffWindow = board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == Banana
                && c.CurrentCost <= manaNow)
                && board.MinionEnemy != null
                && board.MinionEnemy.Any(m => m != null);
            if (bananaBuffWindow)
            {
                bool forced = SetSingleCardCombo(board, p, Banana, allowCoinBridge: true, forceOverride: true,
                    logWhenSet: "攻击顺序：ComboSet先香蕉buff后攻击");
                if (forced)
                {
                    AddLog("攻击顺序：已强制先香蕉buff，再执行攻击");
                    return true;
                }
            }

            // 2) 续连熵能：优先“先铺可下随从再续连”，确保攻击在buff之后。
            var entropyCard = board.Hand
                .Where(c => c != null && c.Template != null
                    && c.Template.Id == EntropicContinuity
                    && c.CurrentCost <= manaNow)
                .OrderBy(c => c.CurrentCost)
                .ThenBy(c => c.Id)
                .FirstOrDefault();
            var temporaryZilliax = GetPlayableTemporaryZilliax(board, manaNow);
            int entropyCost = entropyCard != null ? entropyCard.CurrentCost : 99;
            int friendlyBodiesNowForEntropy = board.MinionFriend.Count(m => m != null);
            bool enemyHasMinionForEntropy = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null);
            int minEntropyBodies = enemyHasMinionForEntropy ? 2 : 3;
            bool canReachMinEntropyBeforeCast = entropyCard != null
                && (friendlyBodiesNowForEntropy >= minEntropyBodies);
            // 用户规则：若本回合有可下临时奇利亚斯，且无法同回合兼容续连，则取消“攻前续连强推”。
            bool cannotPairTempZilliaxWithEntropyThisTurn = entropyCard != null
                && temporaryZilliax != null
                && temporaryZilliax.CurrentCost + entropyCard.CurrentCost > manaNow;
            if (cannotPairTempZilliaxWithEntropyThisTurn)
            {
                p.CastSpellsModifiers.AddOrUpdate(EntropicContinuity, new Modifier(4200));
                p.PlayOrderModifiers.AddOrUpdate(EntropicContinuity, new Modifier(-9800));
                AddLog("攻击顺序：临时奇利亚斯可下且本回合无法兼容续连，取消攻前续连强推");
                return false;
            }
            AbductionRayStartupState startupState = EvaluateAbductionRayStartupState(board, manaNow);
            bool rayPlayableNow = IsAbductionRayPlayableNow(board);
            bool rayForceWindow = ShouldForceAbductionRayNow(board);
            bool rayTemporaryOrChainWindow = startupState.TempLikeWindow
                || IsAbductionRayContinuationOrChainWindow(board);
            if (canReachMinEntropyBeforeCast && rayPlayableNow
                && (rayForceWindow || rayTemporaryOrChainWindow))
            {
                bool setRayFirst = SetAbductionRayCombo(board, p,
                    allowCoinBridge: true,
                    chainAll: rayForceWindow || IsAbductionRayContinuationThisTurn(board),
                    forceOverride: true,
                    logWhenSet: "攻击顺序：ComboSet先射线探资源后续连");
                if (setRayFirst)
                {
                    ForceAbductionRayFirstThisTurn(board, p, manaNow, allowZeroCostMinions: true);
                    p.CastSpellsModifiers.AddOrUpdate(EntropicContinuity, new Modifier(3400));
                    p.PlayOrderModifiers.AddOrUpdate(EntropicContinuity, new Modifier(-9800));
                    AddLog("攻击顺序：先打射线探0费资源，续连后置到射线之后");
                    return true;
                }

                p.CastSpellsModifiers.AddOrUpdate(EntropicContinuity, new Modifier(3000));
                p.PlayOrderModifiers.AddOrUpdate(EntropicContinuity, new Modifier(-9800));
                AddLog("攻击顺序：射线优先窗口，续连后置等待射线结果");
                return false;
            }
            bool storytellerPriorityWindow = IsHighValueStorytellerPlayableNow(board, manaNow)
                && !ShouldForceAbductionRayNow(board);
            if (canReachMinEntropyBeforeCast && !storytellerPriorityWindow)
            {
                bool setCombo = SetPlayableMinionsThenEntropyCombo(board, p,
                    allowCoinBridge: true, forceOverride: true,
                    logWhenSet: "攻击顺序：ComboSet先铺随从后续连");
                if (!setCombo)
                {
                    bool hasPlayableOneCostMinion = board.Hand.Any(c => c != null
                        && c.Template != null
                        && c.Type == Card.CType.MINION
                        && c.CurrentCost <= manaNow
                        && c.CurrentCost <= 1);
                    int manaBeforeEntropy = Math.Max(0, manaNow - entropyCard.CurrentCost);
                    bool canPlayMinionThenEntropySameTurn = GetFreeBoardSlots(board) > 0
                        && board.Hand.Any(c => c != null
                            && c.Template != null
                            && c.Type == Card.CType.MINION
                            && c.CurrentCost <= manaBeforeEntropy);
                    bool boardIsFullForEntropy = GetFreeBoardSlots(board) <= 0;
                    bool enemyHasTauntNowForEntropy = board.MinionEnemy != null
                        && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
                    int friendlyBodiesNow = board.MinionFriend.Count(m => m != null);
                    int enemyMinionCountNow = board.MinionEnemy != null ? board.MinionEnemy.Count(m => m != null) : 0;
                    bool entropyCombatPriorityWindow = enemyHasMinionForEntropy
                        && friendlyBodiesNow >= 2
                        && (enemyHasTauntNowForEntropy || enemyMinionCountNow >= 2);

                    if (hasPlayableOneCostMinion && !canPlayMinionThenEntropySameTurn && !boardIsFullForEntropy && !entropyCombatPriorityWindow)
                    {
                        int friendlyBodiesForEntropyPriority = board.MinionFriend.Count(m => m != null);
                        bool entropyAttackPriorityWindow = friendlyBodiesForEntropyPriority >= minEntropyBodies;
                        if (entropyAttackPriorityWindow)
                        {
                            bool forceEntropyBeforeAttack = SetSingleCardCombo(board, p, EntropicContinuity, allowCoinBridge: true, forceOverride: true,
                                logWhenSet: "攻击顺序：ComboSet先续连后攻击");
                            if (forceEntropyBeforeAttack)
                            {
                                AddLog("攻击顺序：虽有1费随从不可同回合衔接，但续连友方覆盖达标，优先先打buff再攻击");
                                return true;
                            }

                            AddLog("攻击顺序：续连覆盖达标但未成功建立ComboSet，回退常规后置判定");
                        }

                        p.CastSpellsModifiers.AddOrUpdate(EntropicContinuity, new Modifier(3200));
                        p.PlayOrderModifiers.AddOrUpdate(EntropicContinuity, new Modifier(-9800));
                        AddLog("攻击顺序：有可下1费随从但无法同回合接续连，续连后置不强推");
                        return false;
                    }
                    if (hasPlayableOneCostMinion && entropyCombatPriorityWindow)
                        AddLog("攻击顺序：敌方有场且续连具备即时交换收益，优先续连不让位1费随从");
                    if (hasPlayableOneCostMinion && !canPlayMinionThenEntropySameTurn && boardIsFullForEntropy)
                        AddLog("攻击顺序：场位已满，忽略1费随从前置，续连先打后攻击");

                    setCombo = SetSingleCardCombo(board, p, EntropicContinuity, allowCoinBridge: true, forceOverride: true,
                        logWhenSet: "攻击顺序：ComboSet先续连后攻击");
                }

                if (setCombo)
                {
                    AddLog("攻击顺序：已强制先完成buff动作，再执行攻击");
                    return true;
                }
            }
            else if (canReachMinEntropyBeforeCast && storytellerPriorityWindow)
            {
                p.CastSpellsModifiers.AddOrUpdate(EntropicContinuity, new Modifier(3200));
                p.PlayOrderModifiers.AddOrUpdate(EntropicContinuity, new Modifier(-9800));
                AddLog("攻击顺序：始祖龟高收益窗口，续连让位始祖龟");
            }
            else if (entropyCard != null && friendlyBodiesNowForEntropy < minEntropyBodies && !canReachMinEntropyBeforeCast)
            {
                p.CastSpellsModifiers.AddOrUpdate(EntropicContinuity, new Modifier(3400));
                p.PlayOrderModifiers.AddOrUpdate(EntropicContinuity, new Modifier(-9800));
                AddLog("攻击顺序：续连需至少" + minEntropyBodies + "友方随从，当前达不到阈值，取消攻前强推");
            }

            // 3) 射线+滑矛buff窗口：先射线，再攻击（包含临时链路，不仅限强制射线）。
            bool nagaRayBuffWindow = IsRayAttackDelayWindow(board);
            if (nagaRayBuffWindow)
            {
                bool setRayCombo = SetAbductionRayCombo(board, p,
                    allowCoinBridge: true,
                    chainAll: IsAbductionRayContinuationOrChainWindow(board),
                    forceOverride: true,
                    logWhenSet: "攻击顺序：ComboSet先射线后攻击");
                if (setRayCombo)
                {
                    ForceAbductionRayFirstThisTurn(board, p, manaNow, allowZeroCostMinions: true);
                    AddLog("攻击顺序：滑矛在场且命中射线链路，强制先打射线buff再执行攻击");
                    return true;
                }
            }

            return false;
        }

        private bool CanImmediateBuffConvertToLethal(Board board, int enemyHp, int canAttackDamageNow)
        {
            if (board == null || board.Hand == null || board.MinionFriend == null)
                return false;
            if (canAttackDamageNow >= enemyHp)
                return true;

            int manaNow = GetAvailableManaIncludingCoin(board);
            int attackableBodies = board.MinionFriend.Count(m => m != null && m.CanAttack && m.CurrentAtk > 0);
            if (attackableBodies <= 0)
                return false;

            bool entropyPlayableNow = board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == EntropicContinuity
                && c.CurrentCost <= manaNow);
            if (entropyPlayableNow && canAttackDamageNow + attackableBodies >= enemyHp)
                return true;

            bool bananaPlayableNow = board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == Banana
                && c.CurrentCost <= manaNow);
            if (bananaPlayableNow && canAttackDamageNow + 1 >= enemyHp)
                return true;

            int attackableSlitherspearCount = board.MinionFriend.Count(m => m != null
                && m.CanAttack
                && m.CurrentAtk > 0
                && m.Template != null
                && m.Template.Id == ViciousSlitherspear);
            bool rayNagaBuffNow = IsAbductionRayPlayableNow(board) && attackableSlitherspearCount > 0;
            if (rayNagaBuffNow && canAttackDamageNow + attackableSlitherspearCount >= enemyHp)
                return true;

            return false;
        }

        // 用户规则：本回合存在可执行的关键buff时，优先“先buff后攻击”。
        private bool ShouldDelayAttacksUntilBuff(Board board, int enemyHp)
        {
            return EvaluateBuffAttackDelayWindow(board, enemyHp, true);
        }

        private bool ShouldForceTombBuffScoutBeforeAttack(Board board, int manaNow, int enemyHp)
        {
            if (board == null || board.Hand == null || board.MinionFriend == null)
                return false;
            if (manaNow <= 0)
                return false;

            var tomb = board.Hand
                .Where(c => c != null && c.Template != null && c.Template.Id == TombOfSuffering)
                .OrderBy(c => c.CurrentCost)
                .FirstOrDefault();
            if (tomb == null || tomb.CurrentCost > manaNow)
                return false;

            if (ShouldSkipTombForSketchOrRayThisTurn(board, manaNow))
                return false;

            bool enemyHasMinion = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null);
            if (!enemyHasMinion)
                return false;

            bool hasAttackableFriend = board.MinionFriend.Any(m => m != null && m.CanAttack && m.CurrentAtk > 0);
            if (!hasAttackableFriend)
                return false;

            int attackableDamage = GetAttackableBoardAttack(board.MinionFriend);
            bool enemyHasTaunt = board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            if (!enemyHasTaunt && attackableDamage >= enemyHp)
                return false;

            // 无可发现目标时，不强推“先墓探buff”。
            int affordableDeckTargets = CountDeckCardsAtMostCost(board, manaNow);
            if (affordableDeckTargets <= 0)
                return false;

            return true;
        }

        // 空场口径：只要存在明确“先出牌可提升本回合后续攻击收益”的链路，也要保留攻前后置。
        private bool ShouldKeepAttackDelayOnEmptyBoard(Board board, int manaNow)
        {
            if (board == null || board.Hand == null || board.MinionFriend == null)
                return false;
            if (manaNow <= 0)
                return false;
            if (!board.MinionFriend.Any(m => m != null && m.CanAttack && m.CurrentAtk > 0))
                return false;

            bool monthlyPlayableNow = board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == MonthlyModelEmployee
                && c.CurrentCost <= manaNow)
                && GetFreeBoardSlots(board) > 0;

            bool rayBuffPlayableNow = IsRayAttackDelayWindow(board);

            var entropy = board.Hand
                .Where(c => c != null && c.Template != null
                    && c.Template.Id == EntropicContinuity
                    && c.CurrentCost <= manaNow)
                .OrderBy(c => c.CurrentCost)
                .ThenBy(c => c.Id)
                .FirstOrDefault();
            bool entropyBuffWindow = false;
            if (entropy != null)
            {
                int friendlyBodiesNow = board.MinionFriend.Count(m => m != null);
                int manaBeforeEntropy = Math.Max(0, manaNow - entropy.CurrentCost);
                int additionalBodiesBeforeEntropy = GetMaxAdditionalBodiesBeforeEntropy(board, manaBeforeEntropy);
                entropyBuffWindow = friendlyBodiesNow + additionalBodiesBeforeEntropy >= 3;
            }

            return monthlyPlayableNow || rayBuffPlayableNow || entropyBuffWindow;
        }

        private bool ShouldEnterBalancedDefenseMode(Board board, int friendHp, int enemyHp)
        {
            if (board == null || board.MinionEnemy == null || board.MinionEnemy.Count == 0)
                return false;

            bool enemyHasTaunt = board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            int friendAttackNow = GetAttackableBoardAttack(board.MinionFriend);
            bool onBoardLethal = !enemyHasTaunt && friendAttackNow >= enemyHp;
            if (onBoardLethal)
                return false;

            int enemyAttack = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            int enemyCount = board.MinionEnemy.Count(m => m != null);
            int myCount = board.MinionFriend != null ? board.MinionFriend.Count(m => m != null) : 0;
            int highAtkCount = board.MinionEnemy.Count(m => m != null && m.CurrentAtk >= 3);
            int veryHighAtkCount = board.MinionEnemy.Count(m => m != null && m.CurrentAtk >= 5);
            bool noTauntOnOurSide = board.MinionFriend == null || !board.MinionFriend.Any(m => m != null && m.IsTaunt);

            int burstReserve = GetEnemyBurstReserveByClass(board.EnemyClass);
            int twoTurnPressure = enemyAttack * 2 + burstReserve;
            bool twoTurnRisk = friendHp <= twoTurnPressure + 2;
            bool enemyTempoLead = enemyCount >= Math.Max(1, myCount);
            bool mediumPressure = friendHp <= 22 && enemyAttack >= 5 && highAtkCount >= 1 && noTauntOnOurSide;
            bool severeBoardPressure = friendHp <= 18 && (highAtkCount >= 2 || veryHighAtkCount >= 1);

            return (twoTurnRisk && enemyTempoLead) || mediumPressure || severeBoardPressure;
        }

        // 低血且敌方有奥秘时，优先让可攻击吸血随从先出手回血，再考虑出牌/其余攻击。
        private bool TryForceLifestealAttackBeforePlaysAgainstSecret(Board board, ProfileParameters p, int enemyAttackSnapshot)
        {
            if (board == null || p == null || board.HeroFriend == null || board.MinionFriend == null || board.MinionEnemy == null || board.Hand == null)
                return false;

            bool enemyHasSecret = board.SecretEnemy || board.SecretEnemyCount > 0;
            if (!enemyHasSecret || !board.MinionEnemy.Any(m => m != null))
                return false;

            int friendHp = GetHeroHealth(board.HeroFriend);
            bool lowHpRisk = friendHp <= 8 || enemyAttackSnapshot >= friendHp || (friendHp <= 12 && enemyAttackSnapshot >= 6);
            if (!lowHpRisk)
                return false;

            var lifestealAttackers = board.MinionFriend
                .Where(m => m != null && m.CanAttack && m.CurrentAtk > 0 && m.IsLifeSteal && m.Template != null)
                .ToList();
            if (lifestealAttackers.Count == 0)
                return false;

            int manaNow = GetAvailableManaIncludingCoin(board);
            foreach (var card in board.Hand.Where(c => c != null && c.Template != null && c.CurrentCost <= manaNow))
            {
                if (card.Type == Card.CType.MINION)
                {
                    p.CastMinionsModifiers.AddOrUpdate(card.Id, new Modifier(5200));
                    p.CastMinionsModifiers.AddOrUpdate(card.Template.Id, new Modifier(5200));
                }
                else if (card.Type == Card.CType.WEAPON)
                {
                    p.CastWeaponsModifiers.AddOrUpdate(card.Id, new Modifier(5200));
                    p.CastWeaponsModifiers.AddOrUpdate(card.Template.Id, new Modifier(5200));
                }
                else
                {
                    p.CastSpellsModifiers.AddOrUpdate(card.Id, new Modifier(5200));
                    p.CastSpellsModifiers.AddOrUpdate(card.Template.Id, new Modifier(5200));
                }
            }

            int lifestealAtkCap = lifestealAttackers.Max(m => Math.Max(1, m.CurrentAtk));
            foreach (var enemy in board.MinionEnemy.Where(m => m != null && m.Template != null))
            {
                int healTradePriority = 1200
                    + Math.Max(0, enemy.CurrentAtk) * 55
                    + Math.Min(700, Math.Max(0, enemy.CurrentHealth) * 90);
                if (enemy.IsTaunt)
                    healTradePriority += 320;
                if (enemy.CurrentHealth >= lifestealAtkCap)
                    healTradePriority += 220;

                p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(Math.Min(9800, healTradePriority)));
            }

            foreach (var lifesteal in lifestealAttackers)
                p.AttackOrderModifiers.AddOrUpdate(lifesteal.Template.Id, new Modifier(-9999));

            p.GlobalAggroModifier = -180;
            AddLog("奥秘保命：低血且敌方有奥秘，强制先用吸血随从攻击回血，再考虑出牌");
            return true;
        }

        // 用户规则：低血高压且非斩杀时，禁止英雄主动攻击（避免“古尔丹送脸/送身材”）。
        private void ApplyLowHpHeroAttackSafety(Board board, ProfileParameters p, int enemyHp, int enemyAttackSnapshot)
        {
            if (board == null || p == null || board.HeroFriend == null)
                return;

            bool enemyHasBoard = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null);
            if (!enemyHasBoard)
                return;

            int friendHp = GetHeroHealth(board.HeroFriend);
            bool enemyHasTaunt = board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            int minionOnlyAttack = GetAttackableBoardAttack(board.MinionFriend);
            bool lethalWithoutHero = !enemyHasTaunt && minionOnlyAttack >= enemyHp;
            bool lowHpRisk = friendHp <= 8 || enemyAttackSnapshot >= friendHp || (friendHp <= 12 && enemyAttackSnapshot >= 8);
            if (!lowHpRisk || lethalWithoutHero)
                return;

            const int heroAttackBlockValue = 9999;
            p.GlobalWeaponsAttackModifier = heroAttackBlockValue;

            // 兼容“无武器但英雄有攻击力”的场景：直接按英雄实体屏蔽英雄攻击。
            p.WeaponsAttackModifiers.AddOrUpdate(board.HeroFriend.Id, new Modifier(heroAttackBlockValue));

            // 常规武器攻击路径也同步强禁，防止被交换/嘲讽权重覆盖。
            if (board.WeaponFriend != null && board.WeaponFriend.Template != null)
                p.WeaponsAttackModifiers.AddOrUpdate(board.WeaponFriend.Template.Id, new Modifier(heroAttackBlockValue));

            AddLog("英雄攻击：低血高压且非斩杀，禁止英雄主动攻击（含无武器攻击）");
        }

        private void HandleThreatTargets(Board board, ProfileParameters p, int enemyHp, int friendAttack)
        {
            int friendAttackNowSnapshot = GetAttackableBoardAttack(board.MinionFriend);
            int enemyAttackSnapshot = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            AddLog("威胁快照：我方可攻场攻=" + friendAttackNowSnapshot + " | 敌方场攻=" + enemyAttackSnapshot);

            if (TryForceLifestealAttackBeforePlaysAgainstSecret(board, p, enemyAttackSnapshot))
                return;

            bool enemyHasTauntForLethal = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            bool onBoardLethalNow = !enemyHasTauntForLethal && friendAttackNowSnapshot >= enemyHp;
            if (onBoardLethalNow)
            {
                foreach (var enemy in board.MinionEnemy.Where(m => m != null && m.Template != null))
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(-9999));

                foreach (var friend in board.MinionFriend.Where(m => m != null && m.Template != null && m.CanAttack))
                {
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(friend.Template.Id, new Modifier(9999));
                    p.AttackOrderModifiers.AddOrUpdate(friend.Template.Id, new Modifier(-9999));
                }

                p.GlobalAggroModifier = 300;
                AddLog("威胁：可攻场攻已达斩杀，强制全走脸不交换");
                return;
            }

            ApplyLowHpHeroAttackSafety(board, p, enemyHp, enemyAttackSnapshot);

            // 用户规则：随船外科医师（CORE_WON_065）必须优先解。
            // 仅当“当回合已斩杀”时才允许绕过；其余情况强制进入解场分支，
            // 覆盖始祖龟留场/常规抢血窗口，避免其持续给对手随从滚雪球。
            bool enemyHasSurgingShipSurgeonHard = board.MinionEnemy.Any(m => m != null && m.Template != null
                && m.Template.Id == Card.Cards.CORE_WON_065);
            if (enemyHasSurgingShipSurgeonHard)
            {
                foreach (var enemy in board.MinionEnemy.Where(m => m != null && m.Template != null))
                {
                    int pri = enemy.Template.Id == Card.Cards.CORE_WON_065
                        ? 9800
                        : (enemy.IsTaunt ? 780 : 160 + Math.Max(0, enemy.CurrentAtk) * 35);
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(pri));
                }

                p.GlobalAggroModifier = -80;
                AddLog("威胁：随船外科医师在场，强制优先解场");
                return;
            }

            if (ShouldPreserveBoardForStorytellerCombat(board))
            {
                bool preserveEnemyHasTaunt = board.MinionEnemy.Any(m => m != null && m.IsTaunt);
                int attackableFriendCountForTauntHold = board.MinionFriend == null
                    ? 0
                    : board.MinionFriend.Count(m => m != null && m.CanAttack && m.CurrentAtk > 0);
                bool canClearAllTauntsNow = preserveEnemyHasTaunt
                    && CanClearAllEnemyTauntsWithFewAttackers(board, Math.Max(1, attackableFriendCountForTauntHold));
                var storytellerRaceKeeperTemplates = new HashSet<Card.Cards>();
                foreach (var race in StorytellerTrackedRaces)
                {
                    var raceKeeper = board.MinionFriend
                        .Where(m => m != null && m.Template != null && m.IsRace(race))
                        .OrderByDescending(m => Math.Max(0, m.CurrentHealth) * 10 + Math.Max(0, m.CurrentAtk) * 6)
                        .FirstOrDefault();
                    if (raceKeeper != null && raceKeeper.Template != null)
                        storytellerRaceKeeperTemplates.Add(raceKeeper.Template.Id);
                }
                int enemyTradePenalty = preserveEnemyHasTaunt
                    ? (canClearAllTauntsNow ? -450 : -1200)
                    : -920;
                foreach (var enemy in board.MinionEnemy.Where(m => m != null && m.Template != null))
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(enemyTradePenalty));

                foreach (var friend in board.MinionFriend.Where(m => m != null && m.Template != null))
                {
                    if (friend.Template.Id == TortollanStoryteller)
                    {
                        p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(friend.Template.Id, new Modifier(1300));
                        continue;
                    }

                    if (storytellerRaceKeeperTemplates.Contains(friend.Template.Id))
                    {
                        p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(friend.Template.Id, new Modifier(560));
                    }
                    else
                    {
                        // 非同族核心：降低保留权重，允许作为临时解场资源。
                        p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(friend.Template.Id, new Modifier(-220));
                        p.AttackOrderModifiers.AddOrUpdate(friend.Template.Id, new Modifier(-420));
                    }
                }

                // 始祖龟是本窗口核心，额外抬高保留权重，避免被用于无收益交换。
                p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(1300));
                AddLog("始祖龟窗口：同种族优先保留1种模板，其余可参与交换");

                if (!preserveEnemyHasTaunt)
                {
                    p.GlobalAggroModifier = 240;
                    AddLog("始祖龟窗口：敌方无嘲讽，优先走脸并保留始祖龟吃回合末buff");
                }
                else if (!canClearAllTauntsNow)
                {
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(2600));
                    p.AttackOrderModifiers.AddOrUpdate(TortollanStoryteller, new Modifier(9999));
                    AddLog("始祖龟窗口：嘲讽本回合解不净，强保始祖龟不送，优先结束回合吃buff");
                }
                else
                {
                    AddLog("始祖龟窗口：压制送死交换，优先结束回合吃buff");
                }
                return;
            }

            Card.Cards reserveFor;
            if (ShouldReserveBoardSlotsForDemonRevive(board, out reserveFor))
            {
                foreach (var enemy in board.MinionEnemy.Where(m => m != null && m.Template != null))
                {
                    int pri = 420 + Math.Max(0, enemy.CurrentAtk) * 25;
                    if (enemy.IsTaunt) pri += 180;
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(pri));
                }

                foreach (var friend in board.MinionFriend.Where(m => m != null && m.Template != null && m.CanAttack))
                {
                    bool expendable = friend.CurrentAtk <= 3
                                      || friend.CurrentHealth <= 3
                                      || (!friend.IsTaunt && board.MinionFriend.Count >= 5);
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(friend.Template.Id,
                        expendable ? new Modifier(-260) : new Modifier(120));
                }

                AddLog("阿克蒙德：优先交换腾格，给复活随从预留场位");
                return;
            }

            int friendHp = GetHeroHealth(board.HeroFriend);
            int enemyAttack = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
            bool enemyHasBoard = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null);
            bool lowHpClearMode = (friendHp <= 8 && enemyHasBoard) || (friendHp <= 12 && enemyAttack >= 8);
            if (lowHpClearMode)
            {
                foreach (var enemy in board.MinionEnemy.Where(m => m != null && m.Template != null))
                {
                    int urgent = friendHp <= 8
                        ? 520 + Math.Max(0, enemy.CurrentAtk) * 45
                        : 260 + Math.Max(0, enemy.CurrentAtk) * 35;
                    if (enemy.IsTaunt) urgent += 220;
                    if (enemy.IsLifeSteal) urgent += 160;
                    urgent += GetDangerEngineTradeBonus(enemy.Template.Id);
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(urgent));
                }

                int dreadlordAutoClearCount = ApplyDreadlordOneHealthAutoClearThreatOverride(board, p, -9999);
                if (dreadlordAutoClearCount > 0)
                    AddLog("恐惧魔王：敌方1血非嘲讽将被回合末清理，降低主动交换优先级");

                AddLog(friendHp <= 8
                    ? "威胁：危险血线，强制提升解场优先级"
                    : "威胁：低血高压，提升解场优先级");
                return;
            }

            // 绝息剑龙（DINO_132）有“回合结束随机打5”时，避免无收益解掉可被回合末自动清理的小怪。
            // 典型场景：无嘲讽、血线安全，场上可攻击绝息剑龙，优先把攻击留给走脸或更高价值目标。
            if (ApplyDinoEndTurnRandomShotThreatOverrides(board, p, friendHp, enemyAttackSnapshot))
                return;

            // 用户规则：手里有临时牌时，避免主动送掉栉龙（亡语会弃掉其抽到的牌）；
            // 若同时处于始祖龟留场窗口，进一步压制无嘲讽交换，优先留场吃buff。
            bool hasEnemyTauntForPlatysaurHold = board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            bool hasTemporaryCardInHandForPlatysaurHold = board.Hand != null && board.Hand.Any(c => c != null && IsTemporaryCard(c));
            bool hasAttackablePlatysaurToProtect = board.MinionFriend != null
                && board.MinionFriend.Any(m => m != null && m.CanAttack && m.Template != null && m.Template.Id == Platysaur);
            bool storytellerOnBoardForPlatysaurHold = board.MinionFriend != null
                && board.MinionFriend.Any(m => m != null && m.Template != null && m.Template.Id == TortollanStoryteller);
            bool safeToHoldForBuff = friendHp >= enemyAttack + 4;
            bool onBoardLethalNowForPlatysaurHold = !hasEnemyTauntForPlatysaurHold && friendAttack >= enemyHp;
            // 用户规则：若命中“滑矛+射线”攻前buff窗口，优先法术后攻击，不让栉龙保守逻辑抢先return。
            bool rayNagaBuffFirstWindowForPlatysaurHold = IsRayAttackDelayWindow(board);
            bool shouldProtectPlatysaurFromDiscard = hasAttackablePlatysaurToProtect
                && hasTemporaryCardInHandForPlatysaurHold
                && !hasEnemyTauntForPlatysaurHold
                && !onBoardLethalNowForPlatysaurHold
                && safeToHoldForBuff
                && !rayNagaBuffFirstWindowForPlatysaurHold;
            if (shouldProtectPlatysaurFromDiscard)
            {
                foreach (var enemy in board.MinionEnemy.Where(m => m != null && m.Template != null))
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(-420));

                p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Platysaur, new Modifier(760));

                if (storytellerOnBoardForPlatysaurHold && board.MinionFriend.Count >= 3)
                {
                    foreach (var friend in board.MinionFriend.Where(m => m != null && m.Template != null))
                        p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(friend.Template.Id, new Modifier(320));

                    AddLog("始祖龟窗口：留场吃buff，压制无嘲讽送死交换");
                }

                AddLog("栉龙：手有临时牌，避免送死触发弃牌");
                return;
            }

            // 参考颜射术攻守策略：在“非极限低血”但存在两回合斩杀风险/场压领先时，
            // 通过威胁表 + 动态加权综合转入解场，不再一味走脸。
            if (ShouldEnterBalancedDefenseMode(board, friendHp, enemyHp))
            {
                ApplyBalancedThreatOverrides(board, p);

                foreach (var enemy in board.MinionEnemy.Where(m => m != null && m.Template != null))
                {
                    int pri = 140 + Math.Max(0, enemy.CurrentAtk) * 45;
                    if (enemy.IsTaunt) pri += 220;
                    if (enemy.IsLifeSteal) pri += 220;
                    if (enemy.CurrentAtk >= 5) pri += 180;
                    if (enemy.CurrentHealth <= 2) pri += 40;
                    pri += GetDangerEngineTradeBonus(enemy.Template.Id);
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(pri));
                }

                // 用户规则：随船外科医师必须强制优先解，避免对手后续滚雪球。
                if (board.MinionEnemy.Any(m => m != null && m.Template != null && m.Template.Id == Card.Cards.CORE_WON_065))
                {
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(Card.Cards.CORE_WON_065, new Modifier(8200));
                    AddLog("威胁：攻守平衡窗口检测到随船外科医师，强制优先解");
                }

                int dreadlordAutoClearCount = ApplyDreadlordOneHealthAutoClearThreatOverride(board, p, -9999);
                if (dreadlordAutoClearCount > 0)
                    AddLog("恐惧魔王：敌方1血非嘲讽将被回合末清理，降低主动交换优先级");

                p.GlobalAggroModifier = 30;
                AddLog("威胁：攻守平衡窗口（血线/场压/职业爆发），综合提升解场优先级");
                return;
            }

            if (ShouldDelayAttacksUntilBuff(board, enemyHp))
            {
                // 用户规则：命中buff窗口时，攻击必须尽量后置到buff之后执行。
                // 这里不仅提高“保留可攻击随从”的价值并压低交换倾向，
                // 还用 AttackOrder 硬后置可攻击随从，避免先攻后buff。
                bool forcedBuffCombo = TryForceBuffComboBeforeAttacks(board, p, enemyHp);
                int manaNowForDelay = GetAvailableManaIncludingCoin(board);
                bool hasPlayableHandActionNow = HasPlayableHandActionNow(board, manaNowForDelay);
                bool temporaryRayNagaAttackHoldWindow = IsTemporaryRaySlitherspearAttackHoldWindow(board, manaNowForDelay);
                bool rayChainAttackHoldWindow = IsRayAttackDelayWindow(board);
                bool emptyBoardBuffChainWindow = ShouldKeepAttackDelayOnEmptyBoard(board, manaNowForDelay);
                bool shouldHardDelayAttacks = forcedBuffCombo || rayChainAttackHoldWindow || emptyBoardBuffChainWindow;
                bool enemyHasTauntForAttackDelay = board.MinionEnemy != null && board.MinionEnemy.Any(m => m != null && m.IsTaunt);
                int canAttackDamageNowForDelay = GetAttackableBoardAttack(board.MinionFriend);

                // 用户规则：敌方空场且buff不能转化为当回合斩杀时，不强制后置攻击，避免空过。
                if (shouldHardDelayAttacks
                    && ShouldCancelBuffAttackDelayOnEmptyBoard(
                        board,
                        enemyHp,
                        canAttackDamageNowForDelay,
                        enemyHasTauntForAttackDelay,
                        temporaryRayNagaAttackHoldWindow,
                        emptyBoardBuffChainWindow,
                        true))
                {
                    shouldHardDelayAttacks = false;
                }

                // 若攻前buff链未建立成功，也不在射线强锁窗口，则取消攻击后置，
                // 避免出现“既没buff也不攻击”的空过回合。
                if (!shouldHardDelayAttacks)
                {
                    AddLog("攻击顺序：未建立可执行buff链，取消攻击后置避免空过");
                }
                else
                {
                    ApplyLocalRuleHoldAttacksBias(p, board, applyAggroModifier: hasPlayableHandActionNow);

                    // 命中攻前buff链且本回合确有可出牌动作时，统一强压走脸倾向，避免“先攻击后buff”。
                    if (hasPlayableHandActionNow)
                    {
                        if (rayChainAttackHoldWindow)
                            p.GlobalAggroModifier = Math.Min(p.GlobalAggroModifier.Value, -3200);

                        AddLog(temporaryRayNagaAttackHoldWindow
                            ? "攻击顺序：手握可用临时挟持射线，强制后置滑矛与走脸到射线之后"
                            : (rayChainAttackHoldWindow
                                ? "攻击顺序：命中挟持射线链路，强制后置滑矛与走脸到射线之后"
                                : (forcedBuffCombo
                                    ? "攻击顺序：命中攻前buff链，强制后置攻击到buff落地后"
                                    : "攻击顺序：检测到可执行buff窗口，临时强压进攻避免先攻")));
                    }

                    AddLog("攻击顺序：存在可执行buff，强制后置攻击到buff之后");
                    return;
                }
            }

            if (ShouldForceTombFirstThisTurn(board))
            {
                int manaNow = GetAvailableManaIncludingCoin(board);
                ForceTombFirstThisTurn(board, p, manaNow);
                SetSingleCardCombo(board, p, TombOfSuffering, allowCoinBridge: false, forceOverride: true,
                    logWhenSet: "攻击顺序：ComboSet先咒怨之墓后攻击");
                ApplyLocalRuleHoldAttacksBias(p, board, applyAggroModifier: false);

                AddLog("攻击顺序：咒怨之墓回合，强制先墓后攻击");
                return;
            }

            foreach (var enemy in board.MinionEnemy.Where(m => m != null && m.Template != null))
            {
                if (enemy.IsTaunt)
                {
                    int tauntPriority = CanClearAnyEnemyTauntWithFewAttackers(board, 2) ? 520 : 180;
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(tauntPriority));
                }
                if (enemy.IsLifeSteal)
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(240));
                if (enemy.CurrentAtk >= 6)
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(160));
                else if (enemy.CurrentAtk >= 4)
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(110));

                int engineBonus = GetDangerEngineTradeBonus(enemy.Template.Id);
                if (engineBonus > 0)
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(engineBonus));
                if (enemy.Template.Id == Card.Cards.CORE_WON_065)
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(6200));
                if (enemy.Template.Id == BattlefieldNecromancer)
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(7600));
                if (enemy.Template.Id == DespicableDreadlord)
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(7000));
                if (enemy.Template.Id == Card.Cards.VAC_435)
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(7800));
                if (IsLabPartnerCardId(enemy.Template.Id))
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(6900));
            }
            bool enemyHasBattlefieldNecromancer = board.MinionEnemy.Any(m => m != null && m.Template != null && m.Template.Id == BattlefieldNecromancer);
            bool enemyHasDespicableDreadlord = board.MinionEnemy.Any(m => m != null && m.Template != null && m.Template.Id == DespicableDreadlord);
            bool enemyHasMaroonedArchmage = board.MinionEnemy.Any(m => m != null && m.Template != null && m.Template.Id == Card.Cards.VAC_435);
            bool enemyHasLabPartner = board.MinionEnemy.Any(m => m != null && m.Template != null && IsLabPartnerCardId(m.Template.Id));
            bool enemyHasShadowAscendant = board.MinionEnemy.Any(m => m != null && m.Template != null && IsShadowAscendantCardId(m.Template.Id));
            bool enemyHasSurgingShipSurgeon = board.MinionEnemy.Any(m => m != null && m.Template != null && m.Template.Id == Card.Cards.CORE_WON_065);
            bool enemyHasSingerMachine = board.MinionEnemy.Any(m => m != null && m.Template != null && m.Template.Id == Card.Cards.TOY_528);
            if (enemyHasBattlefieldNecromancer)
                AddLog("威胁：检测到战场通灵师，强制优先解");
            else if (enemyHasDespicableDreadlord)
                AddLog("威胁：检测到卑鄙的恐惧魔王，强制优先解");
            else if (enemyHasMaroonedArchmage)
                AddLog("威胁：检测到落难的大法师，强制优先解");
            else if (enemyHasLabPartner)
                AddLog("威胁：检测到研究伙伴，强制优先解");
            else if (enemyHasShadowAscendant)
                AddLog("威胁：检测到暗影升腾者，强制优先解");
            else if (enemyHasSurgingShipSurgeon)
                AddLog("威胁：检测到随船外科医师，强制优先解");
            else if (enemyHasSingerMachine)
                AddLog("威胁：检测到伴唱机，强制优先解");
            else if (board.MinionEnemy.Any(m => m != null && m.Template != null && GetDangerEngineTradeBonus(m.Template.Id) > 0))
                AddLog("威胁：检测到成长型威胁随从，提升解场优先级");

            bool enemyHasTaunt = board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            int canAttackDamage = board.MinionFriend
                .Where(m => m != null && m.CanAttack)
                .Sum(m => Math.Max(0, m.CurrentAtk));
            bool nextTurnLethalSetupWindow = !enemyHasTaunt
                && canAttackDamage < enemyHp
                && friendAttack >= enemyHp - 4
                && enemyAttack >= 10;

            // 用户规则：我方优势且接近下回合斩杀时，允许“适度解场”降低被反扑风险。
            // 该窗口下优先处理1-2个高攻威胁，而非盲目全走脸。
            if (nextTurnLethalSetupWindow)
            {
                var dangerTargets = board.MinionEnemy
                    .Where(m => m != null && m.Template != null && !m.IsTaunt)
                    .OrderByDescending(m => GetDangerEngineTradeBonus(m.Template.Id))
                    .ThenByDescending(m => m.CurrentAtk)
                    .ThenByDescending(m => m.CurrentHealth)
                    .ToList();
                if (dangerTargets.Count > 0)
                {
                    int topBonus = GetDangerEngineTradeBonus(dangerTargets[0].Template.Id);
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(dangerTargets[0].Template.Id,
                        new Modifier(Math.Max(360, topBonus + 120)));

                    if (dangerTargets.Count > 1)
                    {
                        int secondBonus = GetDangerEngineTradeBonus(dangerTargets[1].Template.Id);
                        if (dangerTargets[1].CurrentAtk >= 3 || secondBonus > 0)
                            p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(dangerTargets[1].Template.Id,
                                new Modifier(Math.Max(220, secondBonus + 80)));
                    }
                }

                var surgeonTarget = dangerTargets.FirstOrDefault(m => m != null && m.Template != null && m.Template.Id == Card.Cards.CORE_WON_065);
                if (surgeonTarget != null)
                {
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(surgeonTarget.Template.Id, new Modifier(8200));
                    AddLog("威胁：斩杀筹备窗口检测到随船外科医师，强制优先解");
                }
                var necromancerTarget = dangerTargets.FirstOrDefault(m => m != null && m.Template != null && m.Template.Id == BattlefieldNecromancer);
                if (necromancerTarget != null)
                {
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(necromancerTarget.Template.Id, new Modifier(9000));
                    AddLog("威胁：斩杀筹备窗口检测到战场通灵师，强制优先解");
                }

                AddLog("威胁：优势斩杀筹备窗口，适度解高攻保下回合斩杀");
                return;
            }

            bool raceLethalWindow = !enemyHasTaunt && friendAttack >= enemyHp - 4;
            if (raceLethalWindow)
            {
                bool hasDangerEngine = false;
                foreach (var enemy in board.MinionEnemy.Where(m => m != null && !m.IsTaunt))
                {
                    int engineBonus = (enemy.Template != null) ? GetDangerEngineTradeBonus(enemy.Template.Id) : 0;
                    if (engineBonus > 0)
                    {
                        hasDangerEngine = true;
                        int keepClearPri = Math.Max(260, engineBonus + 120);
                        if (enemy.CurrentAtk >= 3) keepClearPri += 60;
                        if (enemy.Template != null && enemy.Template.Id == Card.Cards.CORE_WON_065)
                            keepClearPri = Math.Max(keepClearPri, 8200);
                        p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(keepClearPri));
                    }
                    else
                    {
                        p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(-120));
                    }
                }
                AddLog(hasDangerEngine
                    ? "威胁：抢血窗口存在成长型威胁，保留关键解场优先级"
                    : "威胁：进入抢血窗口，降低非嘲讽交换");
            }

            int lateDreadlordAutoClearCount = ApplyDreadlordOneHealthAutoClearThreatOverride(board, p, -9999);
            if (lateDreadlordAutoClearCount > 0)
                AddLog("恐惧魔王：敌方1血非嘲讽将被回合末清理，降低主动交换优先级");
        }

        // 绝息剑龙：利用“回合结束随机对敌方随从造成5点伤害”优化交换。
        // 规则：
        // 1) 我方场上存在绝息剑龙（可攻击时进一步加强保留）；
        // 2) 我方血线安全（非低血高压）；
        // 3) 对“<=5血且低威胁”的目标降低主动交换倾向，优先保留攻击价值；
        // 4) 即使敌方有嘲讽，也会降低可被回合末清理的小怪交换优先级，避免无收益补刀。
        private bool ApplyDinoEndTurnRandomShotThreatOverrides(Board board, ProfileParameters p, int friendHp, int enemyAttackSnapshot)
        {
            if (board == null || p == null || board.MinionFriend == null || board.MinionEnemy == null)
                return false;
            if (board.MinionEnemy.Count == 0)
                return false;

            bool hasDinoOnBoard = board.MinionFriend.Any(m => m != null
                && m.Template != null
                && m.Template.Id == Card.Cards.DINO_132);
            if (!hasDinoOnBoard)
                return false;

            bool hasAttackableDino = board.MinionFriend.Any(m => m != null
                && m.CanAttack
                && m.CurrentAtk > 0
                && m.Template != null
                && m.Template.Id == Card.Cards.DINO_132);

            // 仅在“非低血高压”下启用，避免保命局面贪回合末随机伤害。
            bool safeHpWindow = friendHp >= 10 && enemyAttackSnapshot <= friendHp - 3;
            if (!safeHpWindow)
                return false;

            var enemyMinions = board.MinionEnemy
                .Where(m => m != null && m.Template != null)
                .ToList();
            if (enemyMinions.Count == 0)
                return false;

            var snipeCandidates = enemyMinions
                .Where(IsDinoEndTurnSnipeCandidate)
                .ToList();
            if (snipeCandidates.Count == 0)
                return false;

            // 只有一个可清目标时，回合末5点必定命中，强制降低主动交换倾向。
            if (enemyMinions.Count == 1 && snipeCandidates.Count == 1)
            {
                var target = snipeCandidates[0];
                p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(target.Template.Id, new Modifier(-9000));
                if (hasAttackableDino)
                {
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.DINO_132, new Modifier(1200));
                    p.AttackOrderModifiers.AddOrUpdate(Card.Cards.DINO_132, new Modifier(9800));
                }
                p.GlobalAggroModifier = Math.Max(p.GlobalAggroModifier.Value, 220);
                AddLog("绝息剑龙：敌方仅剩可被回合末5点清理目标，降低交换优先级并保留攻击价值");
                return true;
            }

            // 多目标时：降低可被回合末清理的小怪优先级，鼓励把交换资源留给高价值目标。
            var highValueTargets = enemyMinions
                .Where(m => !IsDinoEndTurnSnipeCandidate(m))
                .Where(m => m.IsTaunt
                    || m.IsLifeSteal
                    || m.CurrentAtk >= 4
                    || m.CurrentHealth > 5
                    || GetDangerEngineTradeBonus(m.Template.Id) >= 120)
                .ToList();

            if (highValueTargets.Count == 0)
            {
                foreach (var candidate in snipeCandidates)
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(candidate.Template.Id, new Modifier(-3400));

                if (hasAttackableDino)
                {
                    p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.DINO_132, new Modifier(700));
                    p.AttackOrderModifiers.AddOrUpdate(Card.Cards.DINO_132, new Modifier(9400));
                }

                p.GlobalAggroModifier = Math.Max(p.GlobalAggroModifier.Value, 170);
                AddLog("绝息剑龙：敌方均为低血目标，压低交换优先级并保留回合末5点收益");
                return true;
            }

            foreach (var candidate in snipeCandidates)
                p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(candidate.Template.Id, new Modifier(-4200));

            foreach (var hv in highValueTargets)
                p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(hv.Template.Id, new Modifier(560));

            if (hasAttackableDino)
            {
                p.OnBoardFriendlyMinionsValuesModifiers.AddOrUpdate(Card.Cards.DINO_132, new Modifier(900));
                p.AttackOrderModifiers.AddOrUpdate(Card.Cards.DINO_132, new Modifier(9600));
            }

            p.GlobalAggroModifier = Math.Max(p.GlobalAggroModifier.Value, 120);
            AddLog("绝息剑龙：命中回合末5点窗口，显著后置低血小怪交换，优先处理高价值目标");
            return true;
        }

        private bool IsDinoEndTurnSnipeCandidate(Card minion)
        {
            if (minion == null || minion.Template == null)
                return false;
            if (minion.IsTaunt || minion.IsDivineShield)
                return false;
            if (minion.CurrentHealth > 5)
                return false;

            int dangerBonus = GetDangerEngineTradeBonus(minion.Template.Id);
            bool veryHighThreat = minion.IsLifeSteal || minion.CurrentAtk >= 6 || dangerBonus >= 220;
            return !veryHighThreat;
        }

        // 用户规则：我方场上有恐惧魔王时，敌方非嘲讽1血随从通常可由回合末1点群伤自动清理，
        // 优先避免主动送攻击去交换（嘲讽和圣盾目标除外）。
        private int ApplyDreadlordOneHealthAutoClearThreatOverride(Board board, ProfileParameters p, int oneHealthTargetPenalty)
        {
            if (board == null || p == null || board.MinionFriend == null || board.MinionEnemy == null)
                return 0;

            // 防止调用方误传导致 Modifier 越界崩溃。
            if (oneHealthTargetPenalty < -9999) oneHealthTargetPenalty = -9999;
            if (oneHealthTargetPenalty > 9999) oneHealthTargetPenalty = 9999;

            bool dreadlordOnBoard = board.MinionFriend.Any(m => m != null && m.Template != null && m.Template.Id == DespicableDreadlord);
            if (!dreadlordOnBoard)
                return 0;

            int affected = 0;
            foreach (var enemy in board.MinionEnemy.Where(m =>
                         m != null
                         && m.Template != null
                         && !m.IsTaunt
                         && !m.IsDivineShield
                         && m.CurrentHealth <= 1))
            {
                p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(oneHealthTargetPenalty));
                affected++;
            }

            return affected;
        }

        // 用户规则：每次手牌动作后都要重算；幸运币/激活仍排除。
        // 挟持射线仅在“临时牌”时排除，常规射线打完后继续重算。
        private void ConfigureForcedResimulation(Board board, ProfileParameters p)
        {
            if (board == null || board.Hand == null || p == null || p.ForcedResimulationCardList == null)
                return;

            var excluded = new HashSet<Card.Cards> { TheCoin, Innervate };
            int added = 0;
            foreach (var c in board.Hand.Where(x => x != null && x.Template != null))
            {
                if (excluded.Contains(c.Template.Id))
                    continue;

                // 用户规则修正：临时挟持射线不加入重算名单，避免连锁抖动；
                // 非临时挟持射线仍纳入重算，确保“用完后继续思考”。
                if (c.Template.Id == AbductionRay && IsTemporaryCard(c))
                    continue;

                if (!p.ForcedResimulationCardList.Contains(c.Template.Id))
                {
                    p.ForcedResimulationCardList.Add(c.Template.Id);
                    added++;
                }
            }

            if (added > 0)
                AddLog("重算：已登记手牌重算列表(排除幸运币/激活/临时挟持射线) 数量=" + added);
        }

        private bool HasOtherPlayableCardThanTomb(Board board, int manaNow)
        {
            if (board == null || board.Hand == null)
                return false;

            int freeSlots = GetFreeBoardSlots(board);

            foreach (var c in board.Hand.Where(x => x != null && x.Template != null))
            {
                if (c.Template.Id == TombOfSuffering || c.Template.Id == TheCoin)
                    continue;
                if (c.CurrentCost > manaNow)
                    continue;
                // 空场续连熵能基本不可转化，不作为“本回合可打动作”来阻止咒怨兜底。
                if (c.Template.Id == EntropicContinuity && (board.MinionFriend == null || board.MinionFriend.Count == 0))
                    continue;
                if (c.Type == Card.CType.MINION && freeSlots <= 0)
                    continue;
                return true;
            }

            return false;
        }

        private bool HasOtherPlayableCardThanArchimonde(Board board, int manaNow)
        {
            if (board == null || board.Hand == null)
                return false;

            int freeSlots = GetFreeBoardSlots(board);

            foreach (var c in board.Hand.Where(x => x != null && x.Template != null))
            {
                if (c.Template.Id == Archimonde || c.Template.Id == TheCoin)
                    continue;
                if (c.CurrentCost > manaNow)
                    continue;
                // 空场续连熵能基本不可转化，不作为“本回合可打动作”来阻止阿克蒙德兜底。
                if (c.Template.Id == EntropicContinuity && (board.MinionFriend == null || board.MinionFriend.Count == 0))
                    continue;
                if (c.Type == Card.CType.MINION && freeSlots <= 0)
                    continue;
                return true;
            }

            return false;
        }

        private bool HasOtherPlayableCardThanStoryteller(Board board, int manaNow)
        {
            if (board == null || board.Hand == null)
                return false;

            int freeSlots = GetFreeBoardSlots(board);

            foreach (var c in board.Hand.Where(x => x != null && x.Template != null))
            {
                if (c.Template.Id == TortollanStoryteller || c.Template.Id == TheCoin)
                    continue;
                if (c.CurrentCost > manaNow)
                    continue;
                if (c.Template.Id == EntropicContinuity && (board.MinionFriend == null || board.MinionFriend.Count == 0))
                    continue;
                if (c.Type == Card.CType.MINION && freeSlots <= 0)
                    continue;
                return true;
            }

            return false;
        }

        private bool HasPlayableMinionAtExactCost(Board board, int manaNow, int exactCost)
        {
            if (board == null || board.Hand == null)
                return false;
            if (GetFreeBoardSlots(board) <= 0)
                return false;

            return board.Hand.Any(c => c != null
                && c.Template != null
                && c.Template.Id != TombOfSuffering
                && c.Template.Id != TheCoin
                && c.Type == Card.CType.MINION
                && c.CurrentCost == exactCost
                && c.CurrentCost <= manaNow);
        }

        private bool HasPlayableMinionAtMostCost(Board board, int manaNow, int maxCost)
        {
            if (board == null || board.Hand == null)
                return false;
            if (GetFreeBoardSlots(board) <= 0)
                return false;

            return board.Hand.Any(c => c != null
                && c.Template != null
                && c.Template.Id != TombOfSuffering
                && c.Template.Id != TheCoin
                && c.Type == Card.CType.MINION
                && c.CurrentCost <= manaNow
                && c.CurrentCost <= maxCost);
        }

        private bool ShouldReserveBoardSlotsForDemonRevive(Board board, out Card.Cards reserveFor)
        {
            reserveFor = default(Card.Cards);
            if (board == null || board.Hand == null)
                return false;
            if (board.MinionFriend == null || board.MinionEnemy == null)
                return false;
            if (board.MinionEnemy.Count == 0)
                return false;
            if (!board.MinionFriend.Any(m => m != null && m.CanAttack && m.CurrentAtk > 0))
                return false;

            int freeSlots = GetFreeBoardSlots(board);
            if (freeSlots > 2)
                return false;

            bool enemyHasTaunt = board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            int friendAttack = GetBoardAttack(board.MinionFriend);
            int enemyHp = GetHeroHealth(board.HeroEnemy);
            if (!enemyHasTaunt && friendAttack >= enemyHp)
                return false;

            int manaNow = GetAvailableManaIncludingCoin(board);
            int outsideDeckDemonsInBoardAndGrave = CountOutsideDeckDemonsInBoardAndGrave(board);
            if (outsideDeckDemonsInBoardAndGrave <= 0)
                return false;

            bool archimondePlayable = board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == Archimonde
                && c.CurrentCost <= manaNow);
            bool archimondeReady = outsideDeckDemonsInBoardAndGrave >= 6;
            int archimondeReviveSlotsNeed = Math.Max(0, outsideDeckDemonsInBoardAndGrave - Math.Max(0, freeSlots - 1));
            if (archimondePlayable && archimondeReady && archimondeReviveSlotsNeed > 0)
            {
                reserveFor = Archimonde;
                return true;
            }

            return false;
        }

        private bool IsAbductionRayPlayableNow(Board board)
        {
            if (board == null || board.Hand == null)
                return false;
            if (!IsAbductionRayTurnUnlocked(board))
                return false;

            AbductionRayStartupState startupState = EvaluateAbductionRayStartupState(board);
            int manaNow = startupState.ManaNow;
            bool hasPlayableRay = startupState.PlayableRayCount > 0;
            if (!hasPlayableRay)
                return false;

            bool tempRayWindow = startupState.TempLikeWindow;
            bool rayStartedThisTurn = startupState.RayStartedThisTurn;

            // 用户硬规则：非临时挟持射线仅在可用费用>=4时允许起链；临时牌/已起链续链不受影响。
            if (manaNow < AbductionRayMinManaNonTemp && !tempRayWindow && !rayStartedThisTurn)
                return false;

            // 用户规则：滑矛可攻且射线法术增伤可直接补足斩杀时，放宽起链门槛直接允许使用。
            if (IsAbductionRaySlitherspearLethalWindow(board, manaNow))
                return true;

            // 用户规则：手里有墓且射线可打时，允许直接启动射线，不受常规起链费用门槛限制。
            if (board.HasCardInHand(TombOfSuffering) && !rayStartedThisTurn)
                return true;

            // 用户规则：绝境保命窗口允许放宽起链门槛，优先射线找嘲讽。
            if (IsAbductionRayEmergencySurvivalWindow(board, manaNow))
                return true;

            // 用户硬规则：挟持射线非临时需达到起链费用窗口；临时牌不受影响。
            if (startupState.NonTempStartupManaWindow)
                return true;

            // 用户规则：中后期若可连续打出多张射线（>=2），允许直接启动连发，不再被始祖龟插队。
            if (startupState.MidLateMultiRayWindow)
                return true;

            if (tempRayWindow)
                return true;

            // 本回合已起链时，即使可用费降到<4也允许继续射线链。
            return rayStartedThisTurn;
        }

        // 攻击后置窗口：手握可用“临时挟持射线”且场上有可攻击滑矛时，优先先射线再攻击。
        private bool IsTemporaryRaySlitherspearAttackHoldWindow(Board board, int manaNow = -1)
        {
            if (board == null || board.Hand == null || board.MinionFriend == null)
                return false;

            if (manaNow < 0)
                manaNow = GetAvailableManaIncludingCoin(board);
            if (manaNow <= 0)
                return false;

            if (!HasAttackableNaga(board))
                return false;

            if (!IsAbductionRayPlayableNow(board))
                return false;

            return HasTemporaryOrSketchGeneratedAbductionRay(board);
        }

        private bool IsAbductionRaySlitherspearLethalWindow(Board board, int manaNow = -1)
        {
            if (board == null || board.MinionFriend == null || board.MinionEnemy == null)
                return false;

            if (manaNow < 0)
                manaNow = GetAvailableManaIncludingCoin(board);
            if (!HasPlayableAbductionRayInHand(board, manaNow))
                return false;

            bool enemyHasTaunt = board.MinionEnemy.Any(m => m != null && m.IsTaunt);
            if (enemyHasTaunt)
                return false;

            int attackableSlitherspearCount = board.MinionFriend.Count(m => m != null
                && m.CanAttack
                && m.CurrentAtk > 0
                && m.Template != null
                && m.Template.Id == ViciousSlitherspear);
            if (attackableSlitherspearCount <= 0)
                return false;

            int attackableDamageNow = GetAttackableBoardAttack(board.MinionFriend);
            int enemyHp = GetHeroHealth(board.HeroEnemy);
            if (enemyHp <= 0 || attackableDamageNow >= enemyHp)
                return false;

            return attackableDamageNow + attackableSlitherspearCount >= enemyHp;
        }

        private bool HasPlayableAbductionRayInHand(Board board, int manaNow)
        {
            if (board == null || board.Hand == null)
                return false;
            if (!IsAbductionRayTurnUnlocked(board))
                return false;

            return board.Hand.Any(c => c != null && c.Template != null
                && c.Template.Id == AbductionRay
                && c.CurrentCost <= manaNow);
        }

        private static int CountPlayedCardInLog(Board board, Card.Cards target)
        {
            if (board == null || board.PlayedCards == null || board.PlayedCards.Count == 0)
                return 0;

            int count = 0;
            try
            {
                count = board.PlayedCards.Count(c => c == target);
            }
            catch
            {
                count = 0;
                foreach (var c in board.PlayedCards)
                {
                    if (c == target)
                        count++;
                }
            }

            return count;
        }

        private static bool HasStartedAbductionRayThisTurn(Board board)
        {
            if (board == null)
                return false;

            int currentRayCount = CountPlayedCardInLog(board, AbductionRay);
            int manaAvailable = 0;
            int maxMana = 0;
            try { manaAvailable = Math.Max(0, board.ManaAvailable); } catch { manaAvailable = 0; }
            try { maxMana = Math.Max(0, board.MaxMana); } catch { maxMana = 0; }
            bool likelyTurnStartByMana = maxMana > 0 && manaAvailable >= maxMana;
            int turn = GetBoardTurn(board);
            if (turn >= 0)
            {
                // Turn 字段可用时，停用“无 Turn 兜底追踪”，避免两套状态互相污染。
                _abductionRayNoTurnInitialized = false;
                _abductionRayNoTurnBasePlayedCount = 0;
                _abductionRayNoTurnLastMaxMana = -1;
                _abductionRayNoTurnLastManaAvailable = -1;
                _abductionRayNoTurnLastDeckCount = -1;

                // 以“本回合起始时射线总计”为基线，只有本回合新增才算“已起链”。
                if (turn != _abductionRayTrackTurn)
                {
                    // 某些环境 Turn 会在同回合重算时变化；只有到“回满法力”边界时才重置基线。
                    bool unstableTurnDrift = _abductionRayTrackTurn != int.MinValue && !likelyTurnStartByMana;
                    if (!unstableTurnDrift)
                    {
                        _abductionRayTrackTurn = turn;

                        int baseCount = currentRayCount;
                        bool likelyAfterAction = manaAvailable < maxMana;
                        bool lastPlayedRayAtTurnStart = WasAbductionRayLastPlayed(board);

                        // 若本回合首次观测就发生在“首发射线之后”，把基线回退1，避免续链被误断。
                        if (currentRayCount > 0 && likelyAfterAction && lastPlayedRayAtTurnStart)
                            baseCount = Math.Max(0, currentRayCount - 1);

                        _abductionRayBasePlayedCount = baseCount;
                    }
                }

                return currentRayCount > _abductionRayBasePlayedCount;
            }

            // 回合号不可用时兜底：
            // 用“法力回满 / 最大法力变化 / 牌库抽1”重置本回合射线基线，避免跨回合误判“已起链”。
            bool lastPlayedRay = WasAbductionRayLastPlayed(board);

            int deckCount = -1;
            try { deckCount = board.FriendDeckCount; } catch { deckCount = -1; }

            if (!_abductionRayNoTurnInitialized)
            {
                int baseCount = currentRayCount;
                // 若首次观测就发生在“打出射线之后”，回退1，避免同回合续链被误断。
                if (currentRayCount > 0 && manaAvailable < maxMana && lastPlayedRay)
                    baseCount = Math.Max(0, currentRayCount - 1);

                _abductionRayNoTurnBasePlayedCount = baseCount;
                _abductionRayNoTurnInitialized = true;
            }
            else
            {
                bool maxManaIncreased = _abductionRayNoTurnLastMaxMana >= 0
                    && maxMana > _abductionRayNoTurnLastMaxMana;
                bool manaRefilled = _abductionRayNoTurnLastMaxMana == maxMana
                    && maxMana > 0
                    && _abductionRayNoTurnLastManaAvailable >= 0
                    && _abductionRayNoTurnLastManaAvailable < maxMana
                    && manaAvailable >= maxMana;
                bool deckDrew = _abductionRayNoTurnLastDeckCount >= 0
                    && deckCount >= 0
                    && deckCount < _abductionRayNoTurnLastDeckCount;

                if (maxManaIncreased || manaRefilled || deckDrew)
                    _abductionRayNoTurnBasePlayedCount = currentRayCount;
            }

            bool startedInNoTurnMode = currentRayCount > _abductionRayNoTurnBasePlayedCount;

            _abductionRayNoTurnLastMaxMana = maxMana;
            _abductionRayNoTurnLastManaAvailable = manaAvailable;
            _abductionRayNoTurnLastDeckCount = deckCount;

            return startedInNoTurnMode;
        }

        private static bool HasPlannedAbductionRayThisTurn(Board board)
        {
            if (!_abductionRayPlannedThisTurn)
                return false;

            int turn = GetBoardTurn(board);
            if (turn < 0 || _abductionRayPlannedTurn < 0)
                return true;
            if (turn == _abductionRayPlannedTurn)
                return true;

            // Turn 漂移但未到回合起点时，仍视为同回合计划有效，避免中途掉锁。
            return !IsLikelyTurnStartByMana(board);
        }

        private static bool HasStartedOrPlannedAbductionRayThisTurn(Board board)
        {
            return HasStartedAbductionRayThisTurn(board) || HasPlannedAbductionRayThisTurn(board);
        }

        private static void MarkAbductionRayPlannedThisTurn(Board board)
        {
            int turn = GetBoardTurn(board);
            if (turn < 0)
                return;
            _abductionRayPlannedTurn = turn;
            _abductionRayPlannedThisTurn = true;
        }

        private bool CanUseLifeTapNow(Board board)
        {
            if (board == null)
                return false;

            int manaNow = GetAvailableManaIncludingCoin(board);
            int tapCost = 2;
            try
            {
                if (board.Ability != null)
                    tapCost = Math.Max(0, board.Ability.CurrentCost);
            }
            catch
            {
                tapCost = 2;
            }

            return manaNow >= tapCost;
        }

        // 用户规则：手里有速写美术家时，第三回合前（1-2费回合）禁用幸运币。
        private bool ShouldLockCoinBeforeTurnThreeForSketch(Board board)
        {
            if (board == null || board.Hand == null)
                return false;
            if (!board.HasCardInHand(TheCoin) || !board.HasCardInHand(SketchArtist))
                return false;
            return Math.Max(0, board.MaxMana) < 3;
        }

        private void EnforceCoinLockBeforeTurnThreeWhenSketchInHand(Board board, ProfileParameters p)
        {
            if (board == null || p == null)
                return;
            if (!ShouldLockCoinBeforeTurnThreeForSketch(board))
                return;

            p.CastSpellsModifiers.AddOrUpdate(TheCoin, new Modifier(9999));
            p.PlayOrderModifiers.AddOrUpdate(TheCoin, new Modifier(-9999));
            AddLog("幸运币：终局校验命中速写前置锁币，保持禁用");
        }

        // 统一“可用费用”口径：当前费用 + 幸运币虚拟1费。
        private int GetAvailableManaIncludingCoin(Board board)
        {
            int mana = 0;
            try { mana = board != null ? Math.Max(0, board.ManaAvailable) : 0; } catch { mana = 0; }

            bool hasCoin = false;
            try { hasCoin = board != null && board.HasCardInHand(TheCoin); } catch { hasCoin = false; }
            if (hasCoin && !ShouldLockCoinBeforeTurnThreeForSketch(board)) mana++;

            if (mana > 10) mana = 10;
            return mana;
        }

        private static bool HasAnyHand(Board board, params Card.Cards[] cards)
        {
            if (board == null || board.Hand == null || cards == null)
                return false;
            return cards.Any(c => board.HasCardInHand(c));
        }

        private static bool HasPlayedCard(Board board, Card.Cards card)
        {
            if (board == null)
                return false;

            bool inGraveyard = board.FriendGraveyard != null && board.FriendGraveyard.Contains(card);
            bool inPlayedCards = board.PlayedCards != null && board.PlayedCards.Contains(card);
            bool onBoard = board.MinionFriend != null && board.MinionFriend.Any(m => m != null && m.Template != null && m.Template.Id == card);
            return inGraveyard || inPlayedCards || onBoard;
        }

        private static int CountFriendGraveyard(Board board, Card.Cards card)
        {
            if (board == null || board.FriendGraveyard == null)
                return 0;
            return board.FriendGraveyard.Count(id => id == card);
        }

        private static int CountFriendHand(Board board, Card.Cards card)
        {
            if (board == null || board.Hand == null)
                return 0;

            int count = 0;
            foreach (var c in board.Hand)
            {
                if (c == null || c.Template == null)
                    continue;
                if (c.Template.Id == card)
                    count++;
            }
            return count;
        }

        private int GetEstimatedRemainingInDeck(Board board, Card.Cards card)
        {
            // 优先使用“剩余牌堆”口径：当 board.Deck 看起来不是整副清单时，直接按 Deck 统计剩余数量。
            if (board != null && board.Deck != null && board.Deck.Count > 0 && board.FriendDeckCount >= 0)
            {
                bool deckLooksFullList = board.Deck.Count > board.FriendDeckCount + 1;
                if (!deckLooksFullList)
                    return Math.Max(0, board.Deck.Count(id => id == card));
            }

            // 其次使用开局快照估算：初始数量 - 手牌 - 坟场。
            if (_initialDeckCardCounts != null && _initialDeckCardCounts.Count > 0)
            {
                int initialCount;
                if (!_initialDeckCardCounts.TryGetValue(card, out initialCount))
                    return 0;

                int remaining = initialCount
                                - CountFriendHand(board, card)
                                - CountFriendGraveyard(board, card);
                return Math.Max(0, remaining);
            }

            // 兜底：无法可靠估算时返回未知。
            return -1;
        }

        private bool HasNoAbductionRayRemainingInDeck(Board board)
        {
            int remain = GetEstimatedRemainingInDeck(board, AbductionRay);
            return remain == 0;
        }

        private static int CountDeckCardsAtMostCost(Board board, int maxCost)
        {
            if (board == null || board.Deck == null)
                return 0;

            int count = 0;
            foreach (var id in board.Deck)
            {
                try
                {
                    var t = CardTemplate.LoadFromId(id);
                    if (t != null && t.Cost <= maxCost)
                        count++;
                }
                catch
                {
                    // ignore
                }
            }
            return count;
        }

        private static int CountDistinctFriendlyRaceTypes(Board board)
        {
            if (board == null || board.MinionFriend == null || board.MinionFriend.Count == 0)
                return 0;

            int count = 0;
            if (board.MinionFriend.Any(m => m != null && m.IsRace(Card.CRace.DEMON))) count++;
            if (board.MinionFriend.Any(m => m != null && m.IsRace(Card.CRace.MURLOC))) count++;
            if (board.MinionFriend.Any(m => m != null && m.IsRace(Card.CRace.NAGA))) count++;
            if (board.MinionFriend.Any(m => m != null && m.IsRace(Card.CRace.PET))) count++;
            if (board.MinionFriend.Any(m => m != null && m.IsRace(Card.CRace.ELEMENTAL))) count++;
            if (board.MinionFriend.Any(m => m != null && m.IsRace(Card.CRace.MECHANICAL))) count++;
            if (board.MinionFriend.Any(m => m != null && m.IsRace(Card.CRace.UNDEAD))) count++;
            if (board.MinionFriend.Any(m => m != null && m.IsRace(Card.CRace.DRAGON))) count++;
            if (board.MinionFriend.Any(m => m != null && m.IsRace(Card.CRace.PIRATE))) count++;
            if (board.MinionFriend.Any(m => m != null && m.IsRace(Card.CRace.TOTEM))) count++;
            if (board.MinionFriend.Any(m => m != null && m.IsRace(Card.CRace.QUILBOAR))) count++;
            return count;
        }

        private static int CountOutsideDeckDemonsInGraveyard(Board board)
        {
            if (board == null || board.FriendGraveyard == null)
                return 0;

            var graveDemonCounts = new Dictionary<Card.Cards, int>();
            foreach (var id in board.FriendGraveyard)
            {
                try
                {
                    var template = CardTemplate.LoadFromId(id);
                    if (template == null || template.Type != Card.CType.MINION || !template.IsRace(Card.CRace.DEMON))
                        continue;
                }
                catch
                {
                    continue;
                }

                int old;
                graveDemonCounts.TryGetValue(id, out old);
                graveDemonCounts[id] = old + 1;
            }

            if (graveDemonCounts.Count == 0)
                return 0;

            int outsideCount = 0;
            foreach (var kv in graveDemonCounts)
            {
                if (IsOutsideDeckDemonByDeckList(board, kv.Key))
                    outsideCount += kv.Value;
            }

            return outsideCount;
        }

        private static int CountOutsideDeckDemonsInHand(Board board)
        {
            if (board == null || board.Hand == null)
                return 0;

            var handDemonCounts = new Dictionary<Card.Cards, int>();
            foreach (var c in board.Hand)
            {
                if (c == null || c.Template == null)
                    continue;
                if (c.Type != Card.CType.MINION || !c.IsRace(Card.CRace.DEMON))
                    continue;

                int old;
                handDemonCounts.TryGetValue(c.Template.Id, out old);
                handDemonCounts[c.Template.Id] = old + 1;
            }

            if (handDemonCounts.Count == 0)
                return 0;

            int outsideCount = 0;

            foreach (var kv in handDemonCounts)
            {
                if (IsOutsideDeckDemonByDeckList(board, kv.Key))
                    outsideCount += kv.Value;
            }

            return outsideCount;
        }

        private static int CountOutsideDeckDemonsInBoardAndGrave(Board board)
        {
            if (board == null)
                return 0;

            var zoneDemonCounts = new Dictionary<Card.Cards, int>();

            if (board.FriendGraveyard != null)
            {
                foreach (var id in board.FriendGraveyard)
                {
                    try
                    {
                        var template = CardTemplate.LoadFromId(id);
                        if (template == null || template.Type != Card.CType.MINION || !template.IsRace(Card.CRace.DEMON))
                            continue;
                    }
                    catch
                    {
                        continue;
                    }

                    int old;
                    zoneDemonCounts.TryGetValue(id, out old);
                    zoneDemonCounts[id] = old + 1;
                }
            }

            if (board.MinionFriend != null)
            {
                foreach (var m in board.MinionFriend)
                {
                    if (m == null || m.Template == null)
                        continue;
                    if (m.Type != Card.CType.MINION || !m.IsRace(Card.CRace.DEMON))
                        continue;

                    int old;
                    zoneDemonCounts.TryGetValue(m.Template.Id, out old);
                    zoneDemonCounts[m.Template.Id] = old + 1;
                }
            }

            if (zoneDemonCounts.Count == 0)
                return 0;

            int outsideCount = 0;
            foreach (var kv in zoneDemonCounts)
            {
                if (IsOutsideDeckDemonByDeckList(board, kv.Key))
                    outsideCount += kv.Value;
            }

            return outsideCount;
        }

        private bool HasOutsideDeckTauntDemonInBoardAndGrave(Board board, out string tauntSnapshot)
        {
            tauntSnapshot = "(空)";
            if (board == null)
                return false;

            var tauntIds = new HashSet<Card.Cards>();

            if (board.FriendGraveyard != null)
            {
                foreach (var id in board.FriendGraveyard)
                {
                    if (!IsOutsideDeckDemonByDeckList(board, id))
                        continue;
                    if (!IsKnownSurvivalTauntMinion(id))
                        continue;
                    tauntIds.Add(id);
                }
            }

            if (board.MinionFriend != null)
            {
                foreach (var m in board.MinionFriend)
                {
                    if (m == null || m.Template == null)
                        continue;
                    if (m.Type != Card.CType.MINION || !m.IsRace(Card.CRace.DEMON))
                        continue;
                    if (!IsOutsideDeckDemonByDeckList(board, m.Template.Id))
                        continue;
                    if (!m.IsTaunt && !IsKnownSurvivalTauntMinion(m.Template.Id))
                        continue;
                    tauntIds.Add(m.Template.Id);
                }
            }

            if (tauntIds.Count == 0)
                return false;

            tauntSnapshot = string.Join(" || ",
                tauntIds
                    .OrderBy(id => GetCardDisplayName(id), StringComparer.Ordinal)
                    .ThenBy(id => id.ToString(), StringComparer.Ordinal)
                    .Take(3)
                    .Select(id => GetCardDisplayName(id) + "|Id=" + id));
            return true;
        }

        private static string FormatOutsideDeckDemonsInBoardAndGrave(Board board)
        {
            if (board == null)
                return "(空)";

            var zoneDemonCounts = new Dictionary<Card.Cards, int>();

            if (board.FriendGraveyard != null)
            {
                foreach (var id in board.FriendGraveyard)
                {
                    try
                    {
                        var template = CardTemplate.LoadFromId(id);
                        if (template == null || template.Type != Card.CType.MINION || !template.IsRace(Card.CRace.DEMON))
                            continue;
                    }
                    catch
                    {
                        continue;
                    }

                    int old;
                    zoneDemonCounts.TryGetValue(id, out old);
                    zoneDemonCounts[id] = old + 1;
                }
            }

            if (board.MinionFriend != null)
            {
                foreach (var m in board.MinionFriend)
                {
                    if (m == null || m.Template == null)
                        continue;
                    if (m.Type != Card.CType.MINION || !m.IsRace(Card.CRace.DEMON))
                        continue;

                    int old;
                    zoneDemonCounts.TryGetValue(m.Template.Id, out old);
                    zoneDemonCounts[m.Template.Id] = old + 1;
                }
            }

            if (zoneDemonCounts.Count == 0)
                return "(空)";

            var outsideParts = zoneDemonCounts
                .Where(kv => IsOutsideDeckDemonByDeckList(board, kv.Key))
                .OrderBy(kv => GetCardDisplayName(kv.Key), StringComparer.Ordinal)
                .ThenBy(kv => kv.Key.ToString(), StringComparer.Ordinal)
                .Select(kv => GetCardDisplayName(kv.Key) + "|Id=" + kv.Key + "|x" + kv.Value)
                .ToList();

            return outsideParts.Count > 0 ? string.Join(" || ", outsideParts) : "(空)";
        }

        // 用户规则：套外恶魔只按“是否在套牌清单中”判定，
        // 不再使用同名数量溢出扣减（例如本家恶魔额外复制也不计套外）。
        private static bool IsOutsideDeckDemonByDeckList(Board board, Card.Cards cardId)
        {
            bool hasSnapshot = _initialDeckCardCounts != null && _initialDeckCardCounts.Count > 0;
            if (hasSnapshot)
                return !_initialDeckCardCounts.ContainsKey(cardId);

            if (board != null && board.Deck != null && board.Deck.Count > 0)
            {
                try
                {
                    return !board.Deck.Contains(cardId);
                }
                catch
                {
                    // ignore and fallback
                }
            }

            // 快照不可用时的兜底：沿用本构筑原生恶魔白名单。
            return !NativeDeckDemons.Contains(cardId);
        }

        // 用户硬规则：同名牌动作优先临时副本，其次再按最右，避免打到非目标副本。
        private IEnumerable<Card> GetRightmostHandCardsByDbId(Board board, Card.Cards targetDbId, int manaCapInclusive)
        {
            if (board == null || board.Hand == null)
                yield break;

            // 第一轮：先枚举最右侧到最左侧的临时副本。
            for (int i = board.Hand.Count - 1; i >= 0; i--)
            {
                var c = board.Hand[i];
                if (c == null || c.Template == null)
                    continue;
                if (c.Template.Id != targetDbId)
                    continue;
                if (c.CurrentCost > manaCapInclusive)
                    continue;
                if (!IsTemporaryCard(c))
                    continue;
                yield return c;
            }

            // 第二轮：再枚举最右侧到最左侧的非临时副本。
            for (int i = board.Hand.Count - 1; i >= 0; i--)
            {
                var c = board.Hand[i];
                if (c == null || c.Template == null)
                    continue;
                if (c.Template.Id != targetDbId)
                    continue;
                if (c.CurrentCost > manaCapInclusive)
                    continue;
                if (IsTemporaryCard(c))
                    continue;
                yield return c;
            }
        }

        private Card GetRightmostHandCardByDbId(Board board, Card.Cards targetDbId, int manaCapInclusive)
        {
            foreach (var c in GetRightmostHandCardsByDbId(board, targetDbId, manaCapInclusive))
                return c;
            return null;
        }

        private static int GetHeroHealth(Card hero)
        {
            if (hero == null)
                return 0;
            return Math.Max(0, hero.CurrentHealth + hero.CurrentArmor);
        }

        private static int GetBoardAttack(List<Card> minions)
        {
            if (minions == null || minions.Count == 0)
                return 0;
            return minions.Where(m => m != null).Sum(m => Math.Max(0, m.CurrentAtk));
        }

        private static int GetAttackableBoardAttack(List<Card> minions)
        {
            if (minions == null || minions.Count == 0)
                return 0;
            return minions.Where(m => m != null && m.CanAttack).Sum(m => Math.Max(0, m.CurrentAtk));
        }

        private static bool IsFastOpponent(Card.CClass enemyClass)
        {
            switch (enemyClass)
            {
                case Card.CClass.DEMONHUNTER:
                case Card.CClass.HUNTER:
                case Card.CClass.ROGUE:
                case Card.CClass.PALADIN:
                case Card.CClass.SHAMAN:
                case Card.CClass.WARLOCK:
                    return true;
                default:
                    return false;
            }
        }

        private static int GetEnemyBurstReserveByClass(Card.CClass enemyClass)
        {
            switch (enemyClass)
            {
                case Card.CClass.MAGE:
                    return 7;
                case Card.CClass.HUNTER:
                    return 6;
                case Card.CClass.ROGUE:
                case Card.CClass.DEMONHUNTER:
                    return 5;
                case Card.CClass.SHAMAN:
                case Card.CClass.WARLOCK:
                case Card.CClass.DRUID:
                    return 4;
                case Card.CClass.PALADIN:
                    return 3;
                case Card.CClass.PRIEST:
                case Card.CClass.WARRIOR:
                    return 2;
                default:
                    return 3;
            }
        }

        // 低攻但高滚雪球风险的随从：即使当前攻击不高，也应提高交换优先级。
        private static bool IsShadowAscendantCardId(Card.Cards id)
        {
            return id == Card.Cards.CORE_ICC_210
                || id == Card.Cards.CORE_DAL_729;
        }

        private static bool IsLabPartnerCardId(Card.Cards id)
        {
            return string.Equals(id.ToString(), "SCH_310", StringComparison.Ordinal);
        }

        private static int GetDangerEngineTradeBonus(Card.Cards id)
        {
            if (IsShadowAscendantCardId(id)) // 暗影升腾者
                return 4200;
            if (IsLabPartnerCardId(id)) // 研究伙伴
                return 6200;

            switch (id)
            {
                // 伴唱机：用户指定必须优先处理，给高额威胁加权确保抢先解。
                case Card.Cards.TOY_528:      // 伴唱机
                    return 5200;

                // 颜射术高威胁核心：通用滚雪球/引擎随从，提升交换优先级。
                case Card.Cards.TOY_505:      // 玩具船
                    return 1800;
                case Card.Cards.TOY_381:      // 纸艺天使
                    return 1400;
                case Card.Cards.DEEP_008:     // 针岩图腾
                    return 1200;
                case Card.Cards.TTN_924:      // 锋鳞
                    return 1000;
                case Card.Cards.DMF_120:      // 纳兹曼尼织血者
                    return 2200;
                case Card.Cards.CORE_WON_065: // 随船外科医师
                    return 6800;
                case BattlefieldNecromancer:  // 战场通灵师
                    return 7200;
                case DespicableDreadlord:     // 卑鄙的恐惧魔王
                    return 6400;
                case Card.Cards.CORE_LOOT_231:// 奥术工匠
                    return 1200;
                case Card.Cards.CORE_NEW1_020:// 狂野炎术师
                    return 1200;
                case Card.Cards.RLK_572:      // 药剂大师普崔塞德
                    return 2600;
                case Card.Cards.BRM_002:      // 火妖
                    return 2200;
                case Card.Cards.VAC_435:      // 落难的大法师
                    return 7800;

                case Card.Cards.VAC_402:      // 霜噬海盗
                    return 520;
                case Card.Cards.EDR_105:      // 疯狂生物
                    return 460;
                case Card.Cards.CORE_DMF_067: // 奖品商贩
                    return 320;
                default:
                    return 0;
            }
        }

        // 单文件编译兼容：不依赖 ProfileCommon/ProfileThreatTables，
        // 仅保留少量跨卡组通用高威胁目标，用于攻守平衡窗口的“关键随从回拉”。
        private void ApplyBalancedThreatOverrides(Board board, ProfileParameters p)
        {
            if (board == null || p == null || board.MinionEnemy == null)
                return;

            foreach (var enemy in board.MinionEnemy.Where(m => m != null && m.Template != null))
            {
                int extra = 0;
                switch (enemy.Template.Id)
                {
                    case Card.Cards.CORE_LOOT_231: // 奥术工匠
                    case Card.Cards.CORE_DMF_067:  // 奖品商贩
                    case Card.Cards.CORE_CS2_122:  // 团队领袖
                    case Card.Cards.CORE_NEW1_020: // 狂野炎术师
                        extra = 220;
                        break;
                    case Card.Cards.TOY_811:       // 绒绒虎
                    case Card.Cards.CORE_CFM_790:  // 卑劣的脏鼠
                        extra = 260;
                        break;
                    case Card.Cards.VAC_402:       // 霜噬海盗
                    case Card.Cards.EDR_105:       // 疯狂生物
                        extra = 360;
                        break;
                    default:
                        break;
                }

                if (extra > 0)
                    p.OnBoardBoardEnemyMinionsModifiers.AddOrUpdate(enemy.Template.Id, new Modifier(extra));
            }
        }

        private void LogHandAndBoardSnapshot(Board board)
        {
            try
            {
                var hand = new List<string>();
                foreach (var c in board.Hand.Where(x => x != null && x.Template != null))
                {
                    bool isTemp = false;
                    try { isTemp = c.HasTag(Card.GAME_TAG.LUNAHIGHLIGHTHINT); }
                    catch { isTemp = false; }

                    string coinTag = c.Template.Id == TheCoin ? "|Coin=Y" : "";
                    hand.Add(GetCardDisplayName(c)
                        + "|Id=" + c.Template.Id
                        + "|Cost=" + c.CurrentCost
                        + "|Temp=" + (isTemp ? "Y" : "N")
                        + coinTag);
                }
                AddLog("[手牌明细] " + (hand.Count > 0 ? string.Join(" || ", hand) : "(空)"));
            }
            catch
            {
                AddLog("[手牌明细] 读取失败");
            }

            try
            {
                var myBoard = new List<string>();
                foreach (var m in board.MinionFriend.Where(x => x != null && x.Template != null))
                {
                    myBoard.Add(GetCardDisplayName(m)
                        + "|Id=" + m.Template.Id
                        + "|Stat=" + m.CurrentAtk + "/" + m.CurrentHealth
                        + "|CanAtk=" + (m.CanAttack ? "Y" : "N")
                        + "|Taunt=" + (m.IsTaunt ? "Y" : "N"));
                }
                AddLog("[我方场面] " + (myBoard.Count > 0 ? string.Join(" || ", myBoard) : "(空)"));
            }
            catch
            {
                AddLog("[我方场面] 读取失败");
            }

            try
            {
                var enemyBoard = new List<string>();
                foreach (var m in board.MinionEnemy.Where(x => x != null && x.Template != null))
                {
                    enemyBoard.Add(GetCardDisplayName(m)
                        + "|Id=" + m.Template.Id
                        + "|Stat=" + m.CurrentAtk + "/" + m.CurrentHealth
                        + "|Taunt=" + (m.IsTaunt ? "Y" : "N"));
                }
                AddLog("[敌方场面] " + (enemyBoard.Count > 0 ? string.Join(" || ", enemyBoard) : "(空)"));
            }
            catch
            {
                AddLog("[敌方场面] 读取失败");
            }

            try
            {
                int myAtkTotal = GetBoardAttack(board.MinionFriend);
                int myAtkNow = GetAttackableBoardAttack(board.MinionFriend);
                int enemyAtkTotal = GetBoardAttack(board.MinionEnemy) + (board.WeaponEnemy != null ? board.WeaponEnemy.CurrentAtk : 0);
                int enemyAtkNow = GetAttackableBoardAttack(board.MinionEnemy);
                AddLog("[场攻快照] 我方总场攻=" + myAtkTotal + " | 我方可攻场攻=" + myAtkNow
                    + " | 敌方总场攻=" + enemyAtkTotal + " | 敌方可攻场攻=" + enemyAtkNow);
            }
            catch
            {
                AddLog("[场攻快照] 读取失败");
            }
        }

        private static string GetCardDisplayName(Card card)
        {
            try
            {
                if (card != null && card.Template != null)
                {
                    if (!string.IsNullOrWhiteSpace(card.Template.NameCN))
                        return card.Template.NameCN;
                    if (!string.IsNullOrWhiteSpace(card.Template.Name))
                        return card.Template.Name;
                    return card.Template.Id.ToString();
                }
            }
            catch
            {
                // ignore
            }
            return "Unknown";
        }

        private static string GetCardDisplayName(Card.Cards cardId)
        {
            try
            {
                var template = CardTemplate.LoadFromId(cardId);
                if (template != null)
                {
                    if (!string.IsNullOrWhiteSpace(template.NameCN))
                        return template.NameCN;
                    if (!string.IsNullOrWhiteSpace(template.Name))
                        return template.Name;
                }
            }
            catch
            {
                // ignore
            }
            return cardId.ToString();
        }

        // 芬利/卡扎库斯接口：本套通常不触发，但 SmartBot 需要实现。
        public Card.Cards SirFinleyChoice(List<Card.Cards> choices)
        {
            return choices != null && choices.Count > 0 ? choices[0] : default(Card.Cards);
        }

        public Card.Cards KazakusChoice(List<Card.Cards> choices)
        {
            return choices != null && choices.Count > 0 ? choices[0] : default(Card.Cards);
        }

        private void AddLog(string line)
        {
            if (!ShouldKeepDecisionLogLine(line))
                return;

            if (!_turnMetaLogged && (_logTurn >= 0 || _logMaxMana >= 0))
            {
                if (_log.Length > 0)
                    _log += "\r\n";

                _log += "[回合=" + (_logTurn >= 0 ? _logTurn.ToString() : "?")
                    + "|最大法力=" + (_logMaxMana >= 0 ? _logMaxMana.ToString() : "?") + "]";
                _turnMetaLogged = true;
            }

            if (_log.Length > 0)
                _log += "\r\n";
            _log += line;
        }

        private bool ShouldKeepDecisionLogLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            if (line.StartsWith("[BoxOCR]", StringComparison.OrdinalIgnoreCase))
                return true;

            if (line.StartsWith("\u6295\u964d\uff1a", StringComparison.OrdinalIgnoreCase))
                return true;

            return line.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("exception", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("\u5f02\u5e38", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("\u5931\u8d25", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        #region SmartBot single-file compile compatibility
        private static bool ApplyLiveMemoryBiasCompat(Board board, ProfileParameters p)
        {
            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var profileCommonType = assembly.GetType("SmartBotProfiles.ProfileCommon", false);
                    if (profileCommonType == null)
                        continue;

                    var method = profileCommonType.GetMethod(
                        "ApplyLiveMemoryBias",
                        new[] { typeof(Board), typeof(ProfileParameters) });
                    if (method == null)
                        continue;

                    object result = method.Invoke(null, new object[] { board, p });
                    if (result is bool applied)
                        return applied;
                    return false;
                }
            }
            catch
            {
                // Ignore and fall back to local behavior.
            }

            return false;
        }

        private static bool ApplyExactAttackModifierCompat(ProfileParameters p, Card source, Card target, int modifier)
        {
            if (p == null || source == null || source.Template == null || target == null || target.Template == null)
                return false;

            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var profileCommonType = assembly.GetType("SmartBotProfiles.ProfileCommon", false);
                    if (profileCommonType == null)
                        continue;

                    var method = profileCommonType.GetMethod(
                        "ApplyExactAttackModifier",
                        new[] { typeof(ProfileParameters), typeof(Card), typeof(Card), typeof(int) });
                    if (method == null)
                        continue;

                    object result = method.Invoke(null, new object[] { p, source, target, modifier });
                    if (result is bool applied)
                        return applied;
                    return false;
                }
            }
            catch
            {
                // Ignore and fall back to direct modifier application.
            }

            if (modifier == 0)
                return false;

            if (source.Id > 0 && target.Id > 0)
            {
                p.MinionsAttackModifiers.AddOrUpdate(source.Id, modifier, target.Id);
                return true;
            }

            if (source.Id > 0)
            {
                p.MinionsAttackModifiers.AddOrUpdate(source.Id, modifier, target.Template.Id);
                return true;
            }

            p.MinionsAttackModifiers.AddOrUpdate(source.Template.Id, modifier, target.Template.Id);
            return true;
        }

        private sealed class DecisionTeacherHintMatch
        {
            public string Kind = string.Empty;
            public Card.Cards CardId = default(Card.Cards);
            public int Slot = 0;
            public double Score = 0d;
        }

        private sealed class DecisionTeacherHintState
        {
            public DateTime TimestampUtc = DateTime.MinValue;
            public string Status = string.Empty;
            public string Stage = string.Empty;
            public string SBProfile = string.Empty;
            public readonly List<DecisionTeacherHintMatch> Matches = new List<DecisionTeacherHintMatch>();

            public bool IsFresh(int maxAgeSeconds)
            {
                if (TimestampUtc == DateTime.MinValue)
                    return false;

                return TimestampUtc >= DateTime.UtcNow.AddSeconds(-Math.Max(1, maxAgeSeconds));
            }

            public bool MatchesProfile(string expectedProfileName)
            {
                string expected = NormalizeStrategyName(expectedProfileName);
                if (string.IsNullOrWhiteSpace(expected))
                    return true;

                string actual = NormalizeStrategyName(SBProfile);
                if (string.IsNullOrWhiteSpace(actual))
                    return false;

                return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
            }

            private static string NormalizeStrategyName(string raw)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    return string.Empty;

                string value = raw.Trim().Replace('/', '\\');
                return Path.GetFileName(value).Trim();
            }
        }

        private sealed class GameStateSnapshot
        {
            public int ManaAvailable = 0;
            public DecisionTeacherHintState TeacherHint = null;
            public readonly HashSet<string> Facts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public void AddFact(string fact)
            {
                if (!string.IsNullOrWhiteSpace(fact))
                    Facts.Add(fact);
            }
        }

        private sealed class DecisionRuleAction
        {
            public string type = string.Empty;
            public string card_id = string.Empty;
            public int? slot = null;
            public string attack_plan = string.Empty;
            public bool? delay_non_attack_actions = null;
            public string source_selector = string.Empty;
            public string target_kind = string.Empty;
            public string target_selector = string.Empty;
            public string target_card_id = string.Empty;
            public string source_card_id = string.Empty;
            public int? source_slot = null;
            public int? target_slot = null;
        }

        private sealed class DecisionRuleDefinition
        {
            public string id = string.Empty;
            public DecisionRuleAction then = null;
        }

        private sealed class DecisionRuleMatchResult
        {
            public DecisionRuleDefinition Rule = null;
            public string Reason = string.Empty;
        }

        private static class DecisionStateExtractor
        {
            public static GameStateSnapshot Build(Board board)
            {
                GameStateSnapshot snapshot = new GameStateSnapshot();
                snapshot.TeacherHint = LoadTeacherHint();
                return snapshot;
            }

            public static DecisionTeacherHintState LoadTeacherHint()
            {
                return new DecisionTeacherHintState();
            }
        }

        private static class DecisionRuleEngine
        {
            public static List<DecisionRuleMatchResult> EvaluatePlayRules(GameStateSnapshot state, string profileName)
            {
                return new List<DecisionRuleMatchResult>();
            }
        }

        private static class DecisionLearningCapture
        {
            public static void CapturePlayTeacherSample(
                GameStateSnapshot snapshot,
                string profileName,
                List<DecisionRuleMatchResult> matches,
                DecisionRuleMatchResult appliedMatch,
                string appliedSource)
            {
                // Shared capture is unavailable during SmartBot single-file compilation.
            }
        }
        #endregion
    internal static class ProfileCommon
    {
        public static bool TryRunPureLearningPlayExecutor(Board board, ProfileParameters p)
        {
            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var executorType = assembly.GetType("SmartBotProfiles.DecisionPlayExecutor", false);
                    if (executorType == null)
                        continue;

                    var method = executorType.GetMethod(
                        "TryRunPureLearningPlayExecutor",
                        new[] { typeof(Board), typeof(ProfileParameters) });
                    if (method == null)
                        continue;

                    object result = method.Invoke(null, new object[] { board, p });
                    return result is bool && (bool)result;
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
}
